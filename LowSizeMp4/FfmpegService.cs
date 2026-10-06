using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace LowSizeMp4;

public sealed class FfmpegService
{
    public event EventHandler<FfmpegProgressEventArgs>? ProgressChanged;

    public string FfmpegPath { get; set; } = "ffmpeg.exe";
    public string FfprobePath { get; set; } = "ffprobe.exe";

    public async Task<CompressionJobResult> CompressAsync(
        string inputPath,
        string outputPath,
        string arguments,
        CancellationToken cancellationToken,
        string? seekStart = null,
        string? seekEnd = null,
        string? seekDuration = null,
        double? overrideDuration = null)
    {
        if (!File.Exists(inputPath))
        {
            return new CompressionJobResult(false, null, "Исходный файл не найден.");
        }

        var resolvedFfmpeg = ResolveExecutablePath(FfmpegPath);
        if (resolvedFfmpeg is null)
        {
            return new CompressionJobResult(
                false,
                null,
                "ffmpeg.exe не найден. Скачай ffmpeg во вкладке «Настройки» или укажи путь к нему.");
        }

        var duration = overrideDuration ?? await TryGetDurationSecondsAsync(inputPath, cancellationToken);
        if (overrideDuration == null && !string.IsNullOrEmpty(seekDuration) && double.TryParse(seekDuration, CultureInfo.InvariantCulture, out var sd))
        {
            duration = sd;
        }

        var seekArgs = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(seekStart))
        {
            seekArgs.Append($"-ss {seekStart.Trim()} ");
        }
        if (!string.IsNullOrWhiteSpace(seekEnd))
        {
            seekArgs.Append($"-to {seekEnd.Trim()} ");
        }
        if (!string.IsNullOrWhiteSpace(seekDuration))
        {
            seekArgs.Append($"-t {seekDuration.Trim()} ");
        }

        var ffmpegDir = Path.GetDirectoryName(resolvedFfmpeg);
        var startInfo = new ProcessStartInfo
        {
            FileName = resolvedFfmpeg,
            Arguments = $"-y -hide_banner {seekArgs}-i \"{inputPath}\" {arguments} -progress pipe:1 \"{outputPath}\"",
            WorkingDirectory = string.IsNullOrEmpty(ffmpegDir) ? AppContext.BaseDirectory : ffmpegDir,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var stderr = new StringBuilder();

        try
        {
            if (!process.Start())
            {
                return new CompressionJobResult(false, null, "Не удалось запустить процесс ffmpeg.");
            }
        }
        catch (Exception ex)
        {
            return new CompressionJobResult(false, null, $"Ошибка запуска ffmpeg: {ex.Message}");
        }

        // Вычитываем stderr для логов и ошибок
        var readErrorTask = Task.Run(async () =>
        {
            try
            {
                while (!process.StandardError.EndOfStream && !cancellationToken.IsCancellationRequested)
                {
                    var line = await process.StandardError.ReadLineAsync();
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    if (stderr.Length < 32000)
                    {
                        stderr.AppendLine(line);
                    }

                    ProgressChanged?.Invoke(this, new FfmpegProgressEventArgs
                    {
                        Percent = null,
                        CurrentTime = null,
                        Speed = null,
                        Fps = null,
                        RawLine = line
                    });
                }
            }
            catch
            {
            }
        }, cancellationToken);

        // Вычитываем stdout (-progress pipe:1) для точного парсинга прогресса
        var readOutputTask = Task.Run(async () =>
        {
            try
            {
                string? currentTime = null;
                string? speed = null;
                string? fps = null;

                while (!process.StandardOutput.EndOfStream && !cancellationToken.IsCancellationRequested)
                {
                    var line = await process.StandardOutput.ReadLineAsync();
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    var eqIndex = line.IndexOf('=');
                    if (eqIndex <= 0)
                    {
                        continue;
                    }

                    var key = line[..eqIndex].Trim();
                    var value = line[(eqIndex + 1)..].Trim();

                    switch (key)
                    {
                        case "fps":
                            fps = value;
                            break;
                        case "speed":
                            speed = value;
                            break;
                        case "out_time":
                            currentTime = value;
                            break;
                        case "out_time_us":
                            if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var us))
                            {
                                var currentSec = us / 1_000_000.0;
                                double? percent = null;
                                if (duration is > 0)
                                {
                                    percent = Math.Clamp(currentSec / duration.Value * 100.0, 0, 100);
                                }

                                ProgressChanged?.Invoke(this, new FfmpegProgressEventArgs
                                {
                                    Percent = percent,
                                    CurrentTime = currentTime ?? TimeSpan.FromSeconds(currentSec).ToString(@"hh\:mm\:ss"),
                                    Speed = speed,
                                    Fps = fps,
                                    RawLine = null
                                });
                            }
                            break;
                    }
                }
            }
            catch
            {
            }
        }, cancellationToken);

        try
        {
            await Task.WhenAll(process.WaitForExitAsync(cancellationToken), readErrorTask, readOutputTask);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            if (File.Exists(outputPath))
            {
                try { File.Delete(outputPath); } catch { }
            }
            return new CompressionJobResult(false, null, "Сжатие отменено.");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            if (File.Exists(outputPath))
            {
                try { File.Delete(outputPath); } catch { }
            }
            return new CompressionJobResult(false, null, "Сжатие отменено.");
        }

        if (process.ExitCode != 0)
        {
            return new CompressionJobResult(
                false,
                null,
                $"ffmpeg завершился с ошибкой (код {process.ExitCode}): {stderr.ToString().Trim()}");
        }

        return new CompressionJobResult(true, outputPath, "Сжатие успешно завершено.");
    }

    public void SetCustomPaths(string ffmpegPath, string ffprobePath)
    {
        FfmpegPath = ffmpegPath;
        FfprobePath = ffprobePath;
    }

    public sealed record GpuEncoderInfo(
        bool HasNvidia,
        bool HasIntel,
        bool HasAmd,
        string PreferredH264Encoder,
        string PreferredHevcEncoder,
        string? PreferredAv1Encoder);

    private GpuEncoderInfo? _cachedGpuInfo;

    public async Task<GpuEncoderInfo> DetectGpuSupportAsync(CancellationToken ct = default)
    {
        if (_cachedGpuInfo != null) return _cachedGpuInfo;

        var resolvedFfmpeg = ResolveExecutablePath(FfmpegPath);
        if (resolvedFfmpeg is null)
        {
            return _cachedGpuInfo = new GpuEncoderInfo(false, false, false, "libx264", "libx265", null);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = resolvedFfmpeg,
            Arguments = "-hide_banner -encoders",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        try
        {
            using var proc = Process.Start(startInfo);
            if (proc is null) return _cachedGpuInfo = new GpuEncoderInfo(false, false, false, "libx264", "libx265", null);

            var outputTask = proc.StandardOutput.ReadToEndAsync(ct);
            var errTask = proc.StandardError.ReadToEndAsync(ct);
            await Task.WhenAll(proc.WaitForExitAsync(ct), outputTask, errTask);

            var text = await outputTask;
            bool hasNvenc = text.Contains("h264_nvenc");
            bool hasQsv = text.Contains("h264_qsv");
            bool hasAmf = text.Contains("h264_amf");

            string h264 = hasNvenc ? "h264_nvenc" : hasQsv ? "h264_qsv" : hasAmf ? "h264_amf" : "libx264";
            string hevc = hasNvenc ? "hevc_nvenc" : hasQsv ? "hevc_qsv" : hasAmf ? "hevc_amf" : "libx265";

            bool hasAv1Nvenc = text.Contains("av1_nvenc");
            bool hasAv1Qsv = text.Contains("av1_qsv");
            bool hasAv1Amf = text.Contains("av1_amf");
            string? av1 = hasAv1Nvenc ? "av1_nvenc" : hasAv1Qsv ? "av1_qsv" : hasAv1Amf ? "av1_amf" : null;

            return _cachedGpuInfo = new GpuEncoderInfo(hasNvenc, hasQsv, hasAmf, h264, hevc, av1);
        }
        catch
        {
            return _cachedGpuInfo = new GpuEncoderInfo(false, false, false, "libx264", "libx265", null);
        }
    }

    public string BuildCompressionArguments(
        CompressionProfile profile,
        bool isTargetSizeMode,
        int targetSizeMb,
        double? durationSeconds,
        string resolution,
        string fps,
        bool useInterpolation,
        bool isMuteAudio,
        bool useGpu,
        GpuEncoderInfo gpuInfo,
        string videoCodec = "H.264 (AVC)",
        string exportFormat = "MP4 Видео",
        string audioBitrate = "192 kbps")
    {
        // 1. Экспорт в GIF
        if (string.Equals(exportFormat, "GIF Анимация", StringComparison.OrdinalIgnoreCase))
        {
            var filters = new List<string>();
            if (resolution == "1080p") filters.Add("scale=-2:min(1080\\,ih):flags=lanczos");
            else if (resolution == "720p") filters.Add("scale=-2:min(720\\,ih):flags=lanczos");
            else if (resolution == "480p") filters.Add("scale=-2:min(480\\,ih):flags=lanczos");
            else filters.Add("scale=-2:min(720\\,ih):flags=lanczos");

            int gifFps = 15;
            if (!string.IsNullOrWhiteSpace(fps) && !fps.Equals("Оригинал", StringComparison.OrdinalIgnoreCase))
            {
                var match = System.Text.RegularExpressions.Regex.Match(fps, @"\d+");
                if (match.Success && int.TryParse(match.Value, out var parsedFps) && parsedFps > 0)
                {
                    gifFps = Math.Clamp(parsedFps, 1, 60);
                }
            }

            if (useInterpolation)
            {
                filters.Add($"minterpolate=fps={gifFps}:mi_mode=mci");
            }
            else
            {
                filters.Add($"fps={gifFps}");
            }

            var filterStr = string.Join(",", filters);
            return $"-vf \"{filterStr},split[s0][s1];[s0]palettegen=max_colors=128[p];[s1][p]paletteuse=dither=bayer\" -loop 0";
        }

        // 2. Экспорт только звука в MP3
        if (string.Equals(exportFormat, "MP3 Аудио", StringComparison.OrdinalIgnoreCase))
        {
            int kbps = 192;
            var match = System.Text.RegularExpressions.Regex.Match(audioBitrate, @"\d+");
            if (match.Success && int.TryParse(match.Value, out var parsed))
            {
                kbps = parsed;
            }
            return $"-vn -c:a libmp3lame -b:a {kbps}k";
        }

        var sb = new StringBuilder();

        // 3. Видеофильтры (масштабирование и FPS / интерполяция)
        var videoFilters = new List<string>();

        string? scaleFilter = resolution switch
        {
            "1080p" => "scale=-2:min(1080\\,ih)",
            "720p" => "scale=-2:min(720\\,ih)",
            "480p" => "scale=-2:min(480\\,ih)",
            _ => null
        };
        if (scaleFilter != null)
        {
            videoFilters.Add(scaleFilter);
        }

        int targetFps = 0;
        if (!string.IsNullOrWhiteSpace(fps) && !fps.Equals("Оригинал", StringComparison.OrdinalIgnoreCase))
        {
            var match = System.Text.RegularExpressions.Regex.Match(fps, @"\d+");
            if (match.Success && int.TryParse(match.Value, out var parsedFps) && parsedFps > 0)
            {
                targetFps = Math.Clamp(parsedFps, 1, 360);
            }
        }
        else if (useInterpolation)
        {
            targetFps = 60;
        }

        if (targetFps > 0)
        {
            if (useInterpolation)
            {
                videoFilters.Add($"minterpolate=fps={targetFps}:mi_mode=mci");
            }
            else
            {
                videoFilters.Add($"fps={targetFps}");
            }
        }

        if (videoFilters.Count > 0)
        {
            sb.Append($"-vf \"{string.Join(",", videoFilters)}\" ");
        }

        bool isAv1 = videoCodec.Contains("AV1", StringComparison.OrdinalIgnoreCase);
        bool isHevc = videoCodec.Contains("H.265", StringComparison.OrdinalIgnoreCase) || videoCodec.Contains("HEVC", StringComparison.OrdinalIgnoreCase);

        // 4. Видеокодек и битрейт
        if (isTargetSizeMode && durationSeconds.HasValue && durationSeconds.Value > 0)
        {
            long targetBits = (long)(targetSizeMb * 1024.0 * 1024.0 * 8.0 * 0.94);
            int audioBitrateKbps = isMuteAudio ? 0 : 96;
            int totalBitrateKbps = (int)(targetBits / (durationSeconds.Value * 1000.0));
            int videoBitrateKbps = Math.Max(40, totalBitrateKbps - audioBitrateKbps);

            string vcodec;
            if (isAv1)
            {
                vcodec = useGpu && !string.IsNullOrEmpty(gpuInfo.PreferredAv1Encoder) ? gpuInfo.PreferredAv1Encoder : "libsvtav1";
            }
            else if (isHevc)
            {
                vcodec = useGpu && (gpuInfo.HasNvidia || gpuInfo.HasIntel || gpuInfo.HasAmd) ? gpuInfo.PreferredHevcEncoder : "libx265";
            }
            else
            {
                vcodec = useGpu && (gpuInfo.HasNvidia || gpuInfo.HasIntel || gpuInfo.HasAmd) ? gpuInfo.PreferredH264Encoder : "libx264";
            }

            string tagStr = isHevc ? "-tag:v hvc1 " : "";
            sb.Append($"-c:v {vcodec} -b:v {videoBitrateKbps}k -maxrate {videoBitrateKbps * 1.4:0}k -bufsize {videoBitrateKbps * 2:0}k {tagStr}");
        }
        else
        {
            if (isAv1)
            {
                if (useGpu && !string.IsNullOrEmpty(gpuInfo.PreferredAv1Encoder))
                {
                    if (gpuInfo.HasNvidia)
                        sb.Append($"-c:v av1_nvenc -rc:v vbr -cq:v {profile.GpuQualityLevel} -preset p5 ");
                    else if (gpuInfo.HasIntel)
                        sb.Append($"-c:v av1_qsv -global_quality {profile.GpuQualityLevel} -preset medium ");
                    else if (gpuInfo.HasAmd)
                        sb.Append($"-c:v av1_amf -rc cqp -qp_p {profile.GpuQualityLevel} -quality quality ");
                    else
                        sb.Append($"-c:v {gpuInfo.PreferredAv1Encoder} -cq {profile.GpuQualityLevel} ");
                }
                else
                {
                    sb.Append($"-c:v libsvtav1 -crf {profile.Av1Crf} -preset {profile.Av1CpuPreset} ");
                }
            }
            else if (isHevc)
            {
                if (useGpu && (gpuInfo.HasNvidia || gpuInfo.HasIntel || gpuInfo.HasAmd))
                {
                    if (gpuInfo.HasNvidia)
                        sb.Append($"-c:v hevc_nvenc -rc:v vbr -cq:v {profile.GpuQualityLevel} -preset p5 -tag:v hvc1 ");
                    else if (gpuInfo.HasIntel)
                        sb.Append($"-c:v hevc_qsv -global_quality {profile.GpuQualityLevel} -preset medium -tag:v hvc1 ");
                    else if (gpuInfo.HasAmd)
                        sb.Append($"-c:v hevc_amf -rc cqp -qp_p {profile.GpuQualityLevel} -quality quality -tag:v hvc1 ");
                    else
                        sb.Append($"-c:v {gpuInfo.PreferredHevcEncoder} -cq {profile.GpuQualityLevel} -tag:v hvc1 ");
                }
                else
                {
                    sb.Append($"-c:v libx265 -crf {profile.HevcCrf} -preset {profile.CpuPreset} -tag:v hvc1 ");
                }
            }
            else
            {
                if (useGpu && (gpuInfo.HasNvidia || gpuInfo.HasIntel || gpuInfo.HasAmd))
                {
                    if (gpuInfo.HasNvidia)
                        sb.Append($"-c:v h264_nvenc -rc:v vbr -cq:v {profile.GpuQualityLevel} -preset p5 ");
                    else if (gpuInfo.HasIntel)
                        sb.Append($"-c:v h264_qsv -global_quality {profile.GpuQualityLevel} -preset medium ");
                    else if (gpuInfo.HasAmd)
                        sb.Append($"-c:v h264_amf -rc cqp -qp_p {profile.GpuQualityLevel} -quality quality ");
                    else
                        sb.Append($"-c:v {gpuInfo.PreferredH264Encoder} -cq {profile.GpuQualityLevel} ");
                }
                else
                {
                    sb.Append($"-c:v libx264 -crf {profile.H264Crf} -preset {profile.CpuPreset} ");
                }
            }
        }

        // 5. Обработка звука
        if (isMuteAudio)
        {
            sb.Append("-an ");
        }
        else
        {
            sb.Append("-c:a aac -b:a 128k ");
        }

        // 6. Максимальная совместимость
        sb.Append("-map 0:v:0 -map 0:a? -pix_fmt yuv420p -movflags +faststart -sn");

        return sb.ToString().Trim();
    }

    public async Task<string?> GenerateThumbnailAsync(string inputPath, CancellationToken cancellationToken = default)
    {
        var resolvedFfmpeg = ResolveExecutablePath(FfmpegPath);
        if (resolvedFfmpeg is null || !File.Exists(inputPath))
        {
            return null;
        }

        var tempThumb = Path.Combine(Path.GetTempPath(), $"lowsize_thumb_{Guid.NewGuid():N}.jpg");

        var startInfo = new ProcessStartInfo
        {
            FileName = resolvedFfmpeg,
            Arguments = $"-y -hide_banner -ss 00:00:01 -i \"{inputPath}\" -frames:v 1 -q:v 2 \"{tempThumb}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var outTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await Task.WhenAll(process.WaitForExitAsync(cancellationToken), outTask, errTask);

            if (File.Exists(tempThumb) && new FileInfo(tempThumb).Length > 0)
            {
                return tempThumb;
            }
        }
        catch
        {
        }

        return null;
    }

    public async Task<double?> TryGetDurationSecondsAsync(string inputPath, CancellationToken cancellationToken)
    {
        var resolvedFfprobe = ResolveExecutablePath(FfprobePath);
        if (resolvedFfprobe is null)
        {
            return null;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = resolvedFfprobe,
            Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{inputPath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var outTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await Task.WhenAll(process.WaitForExitAsync(cancellationToken), outTask, errTask);

            var text = await outTask;
            return double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                ? seconds
                : null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<string?> TryGetVideoResolutionAsync(string inputPath, CancellationToken cancellationToken)
    {
        var resolvedFfprobe = ResolveExecutablePath(FfprobePath);
        if (resolvedFfprobe is null)
        {
            return null;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = resolvedFfprobe,
            Arguments = $"-v error -select_streams v:0 -show_entries stream=width,height -of csv=s=x:p=0 \"{inputPath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var outTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await Task.WhenAll(process.WaitForExitAsync(cancellationToken), outTask, errTask);

            var text = (await outTask).Trim();
            if (!string.IsNullOrWhiteSpace(text) && text.Contains('x'))
            {
                return text;
            }
        }
        catch
        {
        }

        return null;
    }

    public string? ResolveExecutablePath(string executableName)
    {
        // 1. Прямой путь (если передан абсолютный или кастомный)
        if (!string.IsNullOrWhiteSpace(executableName) && File.Exists(executableName))
        {
            return Path.GetFullPath(executableName);
        }

        var exeFileName = Path.GetFileName(executableName);

        // 2. Рядом с приложением
        var localCandidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, exeFileName),
            Path.Combine(AppContext.BaseDirectory, "ffmpeg", exeFileName),
            Path.Combine(AppContext.BaseDirectory, "bin", exeFileName),
            Path.Combine(Environment.CurrentDirectory, exeFileName),
        };

        foreach (var candidate in localCandidates)
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        // 3. Каталог загрузки в LocalAppData (%LOCALAPPDATA%\LowSizeMp4\ffmpeg)
        var appDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LowSizeMp4",
            "ffmpeg");

        if (Directory.Exists(appDataRoot))
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(appDataRoot, exeFileName, SearchOption.AllDirectories))
                {
                    if (File.Exists(file))
                    {
                        return Path.GetFullPath(file);
                    }
                }
            }
            catch
            {
            }
        }

        // 4. Поиск в PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathEnv))
        {
            foreach (var folder in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                try
                {
                    var candidate = Path.Combine(folder, exeFileName);
                    if (File.Exists(candidate))
                    {
                        return Path.GetFullPath(candidate);
                    }
                }
                catch
                {
                }
            }
        }

        return null;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }
        }
        catch
        {
        }
    }
}
