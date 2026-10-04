using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace LowSizeMp4;

public partial class MainViewModel : ObservableObject
{
    private readonly FfmpegService _ffmpeg = new();
    private readonly FfmpegDownloadService _downloader = new();
    private readonly StringBuilder _logBuffer = new();
    private readonly DispatcherTimer _logFlushTimer;
    private readonly Stopwatch _itemStopwatch = new();
    private string _currentEtaText = string.Empty;
    private CancellationTokenSource? _compressionCts;
    private bool _hasNewLogs;

    public ObservableCollection<QueueItem> Queue { get; } = new();

    public ObservableCollection<CompressionProfile> Profiles { get; } = new()
    {
        new(
            "Баланс H.264 (Рекомендуется)",
            "Универсальный",
            "Оптимальный баланс размера и качества. Воспроизводится абсолютно на любых устройствах.",
            "-c:v libx264 -preset medium -crf 23 -c:a aac -b:a 128k -pix_fmt yuv420p -movflags +faststart -map 0:v:0 -map 0:a? -sn"),
        new(
            "Сильное сжатие H.265 (HEVC)",
            "Макс. сжатие",
            "Уменьшает вес до 60-80% сильнее H.264. Совместим с современными ПК и смартфонами.",
            "-c:v libx265 -preset medium -crf 28 -tag:v hvc1 -c:a aac -b:a 96k -pix_fmt yuv420p -movflags +faststart -map 0:v:0 -map 0:a? -sn"),
        new(
            "Высокое качество H.264",
            "Без потерь",
            "Минимум визуальных потерь для важных записей и архива.",
            "-c:v libx264 -preset slow -crf 19 -c:a aac -b:a 192k -pix_fmt yuv420p -movflags +faststart -map 0:v:0 -map 0:a? -sn"),
        new(
            "Для Discord / Telegram",
            "Для чатов",
            "Быстрое сжатие с уменьшенным битрейтом под лимиты отправки файлов в мессенджеры.",
            "-c:v libx264 -preset faster -crf 26 -c:a aac -b:a 96k -pix_fmt yuv420p -movflags +faststart -map 0:v:0 -map 0:a? -sn")
    };

    [ObservableProperty]
    private int _selectedNavIndex = 0;

    [ObservableProperty]
    private CompressionProfile _selectedProfile;

    [ObservableProperty]
    private QueueItem? _selectedItem;

    [ObservableProperty]
    private string? _customOutputFolder;

    [ObservableProperty]
    private string _statusMessage = "Перетащите видео в окно программы для начала";

    [ObservableProperty]
    private double _currentProgress;

    [ObservableProperty]
    private string _progressDetails = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    private bool _isCompressing;

    [ObservableProperty]
    private string _logText = string.Empty;

    // Settings
    [ObservableProperty]
    private string _ffmpegStatusText = "Проверка наличия FFmpeg...";

    [ObservableProperty]
    private bool _isFfmpegInstalled;

    [ObservableProperty]
    private string _customFfmpegPath = string.Empty;

    partial void OnCustomFfmpegPathChanged(string value)
    {
        RefreshFfmpegStatus();
    }

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    private string _downloadStatusText = string.Empty;

    [ObservableProperty]
    private bool _isInPath;

    public ObservableCollection<AccentColorItem> AccentColors { get; } = new()
    {
        new("Синий (Windows)", "#0078D4", new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 120, 212)), true),
        new("Электрик Неон", "#0099FF", new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 153, 255))),
        new("Морская волна", "#00B7C3", new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 183, 195))),
        new("Неоновый Лайм", "#00CC6A", new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 204, 106))),
        new("Изумрудный", "#107C41", new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 124, 65))),
        new("Золотой / Янтарный", "#FFB900", new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 185, 0))),
        new("Закатный оранжевый", "#F7630C", new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(247, 99, 12))),
        new("Алый Кармин", "#E81123", new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(232, 17, 35))),
        new("Неон Маджента", "#E3008C", new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(227, 0, 140))),
        new("Фиолетовый (Cyberpunk)", "#8764B8", new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(135, 100, 184))),
        new("Глубокий Индиго", "#4F46E5", new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(79, 70, 229))),
        new("Графитовый", "#71717A", new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(113, 113, 122))),
    };

    [ObservableProperty]
    private string _currentTheme = "Dark";

    [ObservableProperty]
    private string _currentAccentHex = "#0078D4";

    [ObservableProperty]
    private string _customAccentHexInput = "#0078D4";

    [ObservableProperty]
    private string _currentBackdrop = "Mica";

    [ObservableProperty]
    private bool _playSoundOnComplete = true;

    [ObservableProperty]
    private bool _autoOpenFolderOnComplete;

    [ObservableProperty]
    private string _outputFileSuffix = "_compressed";

    [ObservableProperty]
    private bool _autoClearCompleted;

    [ObservableProperty]
    private bool _isTargetSizeMode;

    [ObservableProperty]
    private int _targetSizeMb = 25;

    [ObservableProperty]
    private string _selectedResolution = "Оригинал";

    [ObservableProperty]
    private bool _isMuteAudio;

    [ObservableProperty]
    private bool _useGpuAcceleration;

    [ObservableProperty]
    private string _gpuStatusText = "Проверка поддержки видеокарты...";

    [ObservableProperty]
    private string _selectedFps = "Оригинал";

    [ObservableProperty]
    private int _customFps = 120;

    [ObservableProperty]
    private bool _useInterpolation;

    public bool IsCustomFps => string.Equals(SelectedFps, "Свой FPS", StringComparison.OrdinalIgnoreCase);

    partial void OnPlaySoundOnCompleteChanged(bool value) => SaveSettings();
    partial void OnAutoOpenFolderOnCompleteChanged(bool value) => SaveSettings();
    partial void OnOutputFileSuffixChanged(string value)
    {
        UpdateAllOutputPaths();
        SaveSettings();
    }
    partial void OnAutoClearCompletedChanged(bool value) => SaveSettings();
    partial void OnIsTargetSizeModeChanged(bool value) => SaveSettings();
    partial void OnTargetSizeMbChanged(int value) => SaveSettings();
    partial void OnSelectedResolutionChanged(string value) => SaveSettings();
    partial void OnSelectedFpsChanged(string value)
    {
        OnPropertyChanged(nameof(IsCustomFps));
        SaveSettings();
    }
    partial void OnCustomFpsChanged(int value) => SaveSettings();
    partial void OnUseInterpolationChanged(bool value)
    {
        if (value && SelectedFps == "Оригинал")
        {
            SelectedFps = "60 FPS";
        }
        SaveSettings();
    }
    partial void OnIsMuteAudioChanged(bool value) => SaveSettings();
    partial void OnUseGpuAccelerationChanged(bool value) => SaveSettings();
    partial void OnSelectedProfileChanged(CompressionProfile value) => SaveSettings();

    [ObservableProperty]
    private TaskbarItemProgressState _taskbarProgressState = TaskbarItemProgressState.None;

    [ObservableProperty]
    private double _taskbarProgressValue;

    [ObservableProperty]
    private string _selectedVideoCodec = "H.264 (AVC)";

    [ObservableProperty]
    private string _selectedExportFormat = "MP4 Видео";

    [ObservableProperty]
    private string _completionAction = "Ничего не делать";

    public bool IsVideoExport => SelectedExportFormat == "MP4 Видео";

    partial void OnSelectedVideoCodecChanged(string value) => SaveSettings();
    partial void OnSelectedExportFormatChanged(string value)
    {
        OnPropertyChanged(nameof(IsVideoExport));
        UpdateAllOutputPaths();
        SaveSettings();
    }
    partial void OnCompletionActionChanged(string value) => SaveSettings();

    public ObservableCollection<string> VideoCodecs { get; } = new()
    {
        "H.264 (AVC)",
        "H.265 (HEVC)",
        "AV1"
    };

    public ObservableCollection<string> ExportFormats { get; } = new()
    {
        "MP4 Видео",
        "GIF Анимация",
        "MP3 Аудио"
    };

    public ObservableCollection<string> CompletionActions { get; } = new()
    {
        "Ничего не делать",
        "Спящий режим",
        "Выключить ПК"
    };

    public ObservableCollection<string> Resolutions { get; } = new()
    {
        "Оригинал",
        "1080p",
        "720p",
        "480p"
    };

    public ObservableCollection<string> FpsOptions { get; } = new()
    {
        "Оригинал",
        "120 FPS",
        "60 FPS",
        "50 FPS",
        "30 FPS",
        "24 FPS",
        "15 FPS",
        "Свой FPS"
    };

    private FfmpegService.GpuEncoderInfo? _gpuInfo;

    public event Action<string>? BackdropChanged;

    public bool CanStart => !IsCompressing && Queue.Count > 0;

    public string StartButtonText => Queue.Count > 0 && Queue.All(x => x.IsCompleted)
        ? "Сжать заново"
        : "Сжать видео";

    public string StartButtonIcon => Queue.Count > 0 && Queue.All(x => x.IsCompleted)
        ? "ArrowCounterclockwise24"
        : "Play24";

    public MainViewModel()
    {
        var settings = SettingsService.Load();
        _currentTheme = settings.Theme;
        _currentAccentHex = settings.AccentHex;
        _customAccentHexInput = settings.AccentHex;
        _currentBackdrop = settings.Backdrop;
        _playSoundOnComplete = settings.PlaySoundOnComplete;
        _autoOpenFolderOnComplete = settings.AutoOpenFolderOnComplete;
        _outputFileSuffix = settings.OutputFileSuffix ?? "_compressed";
        _autoClearCompleted = settings.AutoClearCompleted;
        _isTargetSizeMode = settings.IsTargetSizeMode;
        _targetSizeMb = settings.TargetSizeMb;
        _selectedResolution = settings.Resolution;
        _selectedFps = settings.Fps ?? "Оригинал";
        _customFps = settings.CustomFps > 0 ? settings.CustomFps : 120;
        _useInterpolation = settings.UseInterpolation;
        _isMuteAudio = settings.IsMuteAudio;
        _useGpuAcceleration = settings.UseGpuAcceleration;
        _customOutputFolder = settings.CustomOutputFolder;
        _customFfmpegPath = settings.CustomFfmpegPath ?? string.Empty;
        _selectedVideoCodec = settings.VideoCodec ?? "H.264 (AVC)";
        _selectedExportFormat = settings.ExportFormat ?? "MP4 Видео";
        _completionAction = settings.CompletionAction ?? "Ничего не делать";

        var matchingProfile = Profiles.FirstOrDefault(p => p.Name == settings.LastPresetName);
        _selectedProfile = matchingProfile ?? Profiles[0];

        foreach (var item in AccentColors)
        {
            item.IsSelected = string.Equals(item.Hex, _currentAccentHex, StringComparison.OrdinalIgnoreCase);
        }

        _ffmpeg.ProgressChanged += OnFfmpegProgressChanged;

        _logFlushTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _logFlushTimer.Tick += (s, e) => FlushLogsToUi();
        _logFlushTimer.Start();

        SetTheme(_currentTheme);
        SetAccentColor(_currentAccentHex);
        RefreshFfmpegStatus();

        _ = Task.Run(async () =>
        {
            _gpuInfo = await _ffmpeg.DetectGpuSupportAsync();
            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (_gpuInfo.HasNvidia)
                    GpuStatusText = "NVIDIA NVENC (Аппаратное ускорение)";
                else if (_gpuInfo.HasIntel)
                    GpuStatusText = "Intel QuickSync (Аппаратное ускорение)";
                else if (_gpuInfo.HasAmd)
                    GpuStatusText = "AMD AMF (Аппаратное ускорение)";
                else
                    GpuStatusText = "GPU энкодер не найден (кодирование процессором)";
            });
        });
    }

    public void SaveSettings()
    {
        SettingsService.Save(new UserSettings
        {
            Theme = CurrentTheme,
            AccentHex = CurrentAccentHex,
            Backdrop = CurrentBackdrop,
            LastPresetName = SelectedProfile?.Name ?? Profiles[0].Name,
            IsTargetSizeMode = IsTargetSizeMode,
            TargetSizeMb = TargetSizeMb,
            Resolution = SelectedResolution,
            Fps = SelectedFps,
            CustomFps = CustomFps,
            UseInterpolation = UseInterpolation,
            IsMuteAudio = IsMuteAudio,
            UseGpuAcceleration = UseGpuAcceleration,
            PlaySoundOnComplete = PlaySoundOnComplete,
            AutoOpenFolderOnComplete = AutoOpenFolderOnComplete,
            OutputFileSuffix = OutputFileSuffix,
            AutoClearCompleted = AutoClearCompleted,
            VideoCodec = SelectedVideoCodec,
            ExportFormat = SelectedExportFormat,
            CompletionAction = CompletionAction,
            CustomOutputFolder = CustomOutputFolder,
            CustomFfmpegPath = CustomFfmpegPath
        });
    }

    [RelayCommand]
    public void SetTargetSize(object? param)
    {
        if (param is int i)
        {
            TargetSizeMb = i;
        }
        else if (param != null && int.TryParse(param.ToString(), out var parsed))
        {
            TargetSizeMb = parsed;
        }
        IsTargetSizeMode = true;
        SaveSettings();
    }

    public void RefreshFfmpegStatus()
    {
        var foundFfmpeg = _ffmpeg.ResolveExecutablePath(
            string.IsNullOrWhiteSpace(CustomFfmpegPath) ? "ffmpeg.exe" : CustomFfmpegPath);

        IsFfmpegInstalled = foundFfmpeg != null;
        if (foundFfmpeg != null)
        {
            _ffmpeg.FfmpegPath = foundFfmpeg;
            var binDir = Path.GetDirectoryName(foundFfmpeg);
            var probePath = binDir != null ? Path.Combine(binDir, "ffprobe.exe") : "ffprobe.exe";
            if (File.Exists(probePath))
            {
                _ffmpeg.FfprobePath = probePath;
            }

            IsInPath = _downloader.IsInstallRootInUserPath();
            FfmpegStatusText = $"FFmpeg найден и готов к работе:\n{foundFfmpeg}";
        }
        else
        {
            FfmpegStatusText = "FFmpeg не обнаружен. Нажмите «Скачать FFmpeg» или укажите путь вручную.";
        }
    }

    public void AddFiles(IEnumerable<string> paths)
    {
        var validExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mkv", ".mov", ".avi", ".webm", ".flv", ".wmv", ".m4v", ".ts"
        };

        foreach (var path in paths)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            var ext = Path.GetExtension(path);
            if (!validExtensions.Contains(ext))
            {
                continue;
            }

            if (Queue.Any(x => string.Equals(x.FilePath, path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var fileInfo = new FileInfo(path);
            var item = new QueueItem
            {
                FilePath = path,
                FileName = Path.GetFileName(path),
                FileSizeFormatted = FormatBytes(fileInfo.Length),
                DurationFormatted = "Анализ...",
                ResolutionFormatted = ext.ToUpperInvariant().TrimStart('.'),
                OutputPath = GetDefaultOutputPath(path),
                Status = "В очереди"
            };

            Queue.Add(item);
            SelectedItem ??= item;

            // Асинхронно извлекаем превью и длительность
            _ = Task.Run(async () =>
            {
                try
                {
                    var dur = await _ffmpeg.TryGetDurationSecondsAsync(path, CancellationToken.None);
                    var thumb = await _ffmpeg.GenerateThumbnailAsync(path, CancellationToken.None);

                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        if (dur.HasValue)
                        {
                            item.DurationSeconds = dur.Value;
                            item.DurationFormatted = TimeSpan.FromSeconds(dur.Value).ToString(@"hh\:mm\:ss");
                            item.TrimStart = "00:00:00";
                            item.TrimEnd = TimeSpan.FromSeconds(dur.Value).ToString(@"hh\:mm\:ss");
                        }
                        else
                        {
                            item.DurationFormatted = "—";
                        }

                        if (!string.IsNullOrEmpty(thumb))
                        {
                            item.ThumbnailPath = thumb;
                        }
                    });
                }
                catch
                {
                }
            });
        }

        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(StartButtonText));
        OnPropertyChanged(nameof(StartButtonIcon));
        StatusMessage = $"В очереди: {Queue.Count} видео";
    }

    [RelayCommand]
    public void RemoveQueueItem(QueueItem? item)
    {
        if (item == null) return;
        Queue.Remove(item);
        if (SelectedItem == item)
        {
            SelectedItem = Queue.FirstOrDefault();
        }
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(StartButtonText));
        OnPropertyChanged(nameof(StartButtonIcon));
    }

    [RelayCommand]
    public void ClearQueue()
    {
        if (IsCompressing) return;
        Queue.Clear();
        SelectedItem = null;
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(StartButtonText));
        OnPropertyChanged(nameof(StartButtonIcon));
        StatusMessage = "Очередь очищена.";
    }

    [RelayCommand]
    public async Task RecompressItemAsync(QueueItem? item)
    {
        if (item == null || IsCompressing) return;
        item.IsCompleted = false;
        item.IsError = false;
        item.Progress = 0;
        item.Status = "В очереди";
        item.ResultDetails = null;
        SelectedItem = item;
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(StartButtonText));
        OnPropertyChanged(nameof(StartButtonIcon));
        await StartCompressionAsync();
    }

    [RelayCommand]
    public async Task RecompressAllAsync()
    {
        if (IsCompressing || Queue.Count == 0) return;
        foreach (var it in Queue)
        {
            it.IsCompleted = false;
            it.IsError = false;
            it.Progress = 0;
            it.Status = "В очереди";
            it.ResultDetails = null;
        }
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(StartButtonText));
        OnPropertyChanged(nameof(StartButtonIcon));
        await StartCompressionAsync();
    }

    [RelayCommand]
    public async Task StartCompressionAsync()
    {
        if (IsCompressing) return;

        RefreshFfmpegStatus();
        if (!IsFfmpegInstalled)
        {
            StatusMessage = "Сначала установите FFmpeg в разделе «Настройки»!";
            MessageBox.Show(
                "FFmpeg не найден. Перейдите во вкладку «Настройки» и нажмите «Скачать FFmpeg».",
                "FFmpeg не найден",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var itemsToProcess = Queue.Where(x => !x.IsCompleted).ToList();
        if (itemsToProcess.Count == 0 && Queue.Count > 0)
        {
            // Пользователь нажал «Сжать повторно» — сбрасываем статус завершенных файлов
            foreach (var it in Queue)
            {
                it.IsCompleted = false;
                it.IsError = false;
                it.Progress = 0;
                it.Status = "В очереди";
                it.ResultDetails = null;
            }
            itemsToProcess = Queue.ToList();
            OnPropertyChanged(nameof(StartButtonText));
            OnPropertyChanged(nameof(StartButtonIcon));
        }

        if (itemsToProcess.Count == 0)
        {
            StatusMessage = "В очереди нет файлов для сжатия.";
            return;
        }

        IsCompressing = true;
        TaskbarProgressState = TaskbarItemProgressState.Normal;
        TaskbarProgressValue = 0;
        _compressionCts = new CancellationTokenSource();
        var token = _compressionCts.Token;

        AppendLogLine($"--- Старт сжатия ({itemsToProcess.Count} файлов) ---");

        var processedCount = 0;
        var errorCount = 0;

        try
        {
            for (var i = 0; i < itemsToProcess.Count; i++)
            {
                if (token.IsCancellationRequested) break;

                var item = itemsToProcess[i];
                SelectedItem = item;
                item.IsProcessing = true;
                item.Status = "Сжатие...";
                item.Progress = 0;
                _itemStopwatch.Restart();
                _currentEtaText = string.Empty;

                var outDir = !string.IsNullOrWhiteSpace(CustomOutputFolder)
                    ? CustomOutputFolder
                    : Path.GetDirectoryName(item.FilePath)!;

                Directory.CreateDirectory(outDir);
                var outPath = Path.Combine(outDir, BuildOutputFileName(item.FilePath));
                item.OutputPath = outPath;

                StatusMessage = $"[{i + 1}/{itemsToProcess.Count}] Сжимаю: {item.FileName}";
                var dur = await _ffmpeg.TryGetDurationSecondsAsync(item.FilePath, token);
                string effectiveFps = IsCustomFps
                    ? $"{CustomFps} FPS"
                    : SelectedFps;

                string? trimStart = item.IsTrimEnabled ? item.TrimStart : null;
                string? trimEnd = item.IsTrimEnabled ? item.TrimEnd : null;
                double? overrideDur = null;
                if (item.IsTrimEnabled && TimeSpan.TryParse(item.TrimStart, out var ts1) && TimeSpan.TryParse(item.TrimEnd, out var ts2) && ts2 > ts1)
                {
                    overrideDur = (ts2 - ts1).TotalSeconds;
                }

                var args = _ffmpeg.BuildCompressionArguments(
                    SelectedProfile,
                    IsTargetSizeMode,
                    TargetSizeMb,
                    overrideDur ?? dur,
                    SelectedResolution,
                    effectiveFps,
                    UseInterpolation,
                    IsMuteAudio,
                    UseGpuAcceleration,
                    _gpuInfo ?? new FfmpegService.GpuEncoderInfo(false, false, false, "libx264", "libx265", null),
                    SelectedVideoCodec,
                    SelectedExportFormat);

                AppendLogLine($"Параметры запуска: {args}");
                var result = await _ffmpeg.CompressAsync(
                    item.FilePath,
                    outPath,
                    args,
                    token,
                    seekStart: trimStart,
                    seekEnd: trimEnd,
                    overrideDuration: overrideDur);

                item.IsProcessing = false;
                if (result.Success)
                {
                    item.IsCompleted = true;
                    item.Status = "Готово";
                    item.Progress = 100;

                    if (File.Exists(outPath))
                    {
                        var outSize = new FileInfo(outPath).Length;
                        var inSize = new FileInfo(item.FilePath).Length;
                        var ratio = inSize > 0 ? (1.0 - (double)outSize / inSize) * 100.0 : 0;
                        item.ResultDetails = $"{FormatBytes(outSize)} (-{ratio:0.#}%)";
                        AppendLogLine($"Успех! Было {FormatBytes(inSize)}, стало {FormatBytes(outSize)} (-{ratio:0.#}%)");
                    }
                    processedCount++;
                }
                else
                {
                    item.IsError = true;
                    item.Status = token.IsCancellationRequested ? "Отменено" : "Ошибка";
                    item.ResultDetails = result.Message;
                    AppendLogLine($"Ошибка: {result.Message}");
                    if (!token.IsCancellationRequested)
                    {
                        errorCount++;
                    }
                }
            }
        }
        finally
        {
            IsCompressing = false;
            _compressionCts = null;
            CurrentProgress = 0;
            ProgressDetails = string.Empty;
            _currentEtaText = string.Empty;
            TaskbarProgressValue = 0;
            TaskbarProgressState = errorCount > 0
                ? TaskbarItemProgressState.Error
                : TaskbarItemProgressState.None;
            OnPropertyChanged(nameof(CanStart));
            OnPropertyChanged(nameof(StartButtonText));
            OnPropertyChanged(nameof(StartButtonIcon));

            if (token.IsCancellationRequested)
            {
                StatusMessage = "Сжатие было отменено.";
                AppendLogLine("--- Процесс отменен пользователем ---");
            }
            else
            {
                StatusMessage = $"Завершено! Успешно: {processedCount}, Ошибок: {errorCount}";
                AppendLogLine($"--- Сжатие завершено: {processedCount} успешно, {errorCount} ошибок ---");

                if (processedCount > 0)
                {
                    if (PlaySoundOnComplete)
                    {
                        try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
                    }

                    if (AutoOpenFolderOnComplete && SelectedItem != null && !string.IsNullOrEmpty(SelectedItem.OutputPath))
                    {
                        try
                        {
                            var folder = Path.GetDirectoryName(SelectedItem.OutputPath);
                            if (Directory.Exists(folder))
                            {
                                Process.Start(new ProcessStartInfo
                                {
                                    FileName = folder,
                                    UseShellExecute = true
                                });
                            }
                        }
                        catch { }
                    }

                    if (AutoClearCompleted)
                    {
                        var completedItems = Queue.Where(x => x.IsCompleted).ToList();
                        foreach (var it in completedItems) Queue.Remove(it);
                        SelectedItem = Queue.FirstOrDefault();
                        OnPropertyChanged(nameof(CanStart));
                        OnPropertyChanged(nameof(StartButtonText));
                        OnPropertyChanged(nameof(StartButtonIcon));
                    }

                    if (errorCount == 0)
                    {
                        if (CompletionAction == "Спящий режим")
                        {
                            try
                            {
                                Process.Start(new ProcessStartInfo
                                {
                                    FileName = "rundll32.exe",
                                    Arguments = "powrprof.dll,SetSuspendState 0,1,0",
                                    UseShellExecute = true
                                });
                            }
                            catch { }
                        }
                        else if (CompletionAction == "Выключить ПК")
                        {
                            try
                            {
                                Process.Start(new ProcessStartInfo
                                {
                                    FileName = "shutdown.exe",
                                    Arguments = "/s /t 60 /c \"LowSizeMp4: сжатие видео завершено. Компьютер выключится через 60 секунд.\"",
                                    UseShellExecute = true
                                });
                                StatusMessage = "Компьютер выключится через 60 секунд. (Для отмены: shutdown -a)";
                            }
                            catch { }
                        }
                    }
                }
            }
        }
    }

    [RelayCommand]
    public async Task GenerateQuickSampleAsync(QueueItem? item)
    {
        item ??= SelectedItem;
        if (item == null || IsCompressing) return;

        RefreshFfmpegStatus();
        if (!IsFfmpegInstalled)
        {
            StatusMessage = "Сначала установите FFmpeg в разделе «Настройки»!";
            return;
        }

        IsCompressing = true;
        TaskbarProgressState = TaskbarItemProgressState.Indeterminate;
        StatusMessage = "Генерация 5-сек. тестового сэмпла...";
        _compressionCts = new CancellationTokenSource();
        var token = _compressionCts.Token;

        try
        {
            var outDir = !string.IsNullOrWhiteSpace(CustomOutputFolder)
                ? CustomOutputFolder
                : Path.GetDirectoryName(item.FilePath)!;

            Directory.CreateDirectory(outDir);
            var baseName = Path.GetFileNameWithoutExtension(item.FilePath);
            var ext = SelectedExportFormat switch
            {
                "GIF Анимация" => ".gif",
                "MP3 Аудио" => ".mp3",
                _ => ".mp4"
            };
            var sampleOut = Path.Combine(outDir, $"{baseName}_sample_5s{ext}");

            double startSec = 0;
            if (item.DurationSeconds > 10)
            {
                startSec = Math.Max(0, (item.DurationSeconds / 2.0) - 2.5);
            }
            var startStr = TimeSpan.FromSeconds(startSec).ToString(@"hh\:mm\:ss");

            string effectiveFps = IsCustomFps ? $"{CustomFps} FPS" : SelectedFps;
            var args = _ffmpeg.BuildCompressionArguments(
                SelectedProfile,
                IsTargetSizeMode,
                TargetSizeMb,
                5.0,
                SelectedResolution,
                effectiveFps,
                UseInterpolation,
                IsMuteAudio,
                UseGpuAcceleration,
                _gpuInfo ?? new FfmpegService.GpuEncoderInfo(false, false, false, "libx264", "libx265", null),
                SelectedVideoCodec,
                SelectedExportFormat);

            var result = await _ffmpeg.CompressAsync(
                item.FilePath,
                sampleOut,
                args,
                token,
                seekStart: startStr,
                seekDuration: "5",
                overrideDuration: 5.0);

            if (result.Success && File.Exists(sampleOut))
            {
                StatusMessage = "Тестовый сэмпл готов! Открываю...";
                Process.Start(new ProcessStartInfo { FileName = sampleOut, UseShellExecute = true });
            }
            else
            {
                StatusMessage = "Ошибка сэмпла: " + result.Message;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = "Ошибка сэмпла: " + ex.Message;
        }
        finally
        {
            IsCompressing = false;
            TaskbarProgressState = TaskbarItemProgressState.None;
            CurrentProgress = 0;
        }
    }

    [RelayCommand]
    public void CancelCompression()
    {
        if (!IsCompressing || _compressionCts == null) return;
        StatusMessage = "Отмена процесса...";
        TaskbarProgressState = TaskbarItemProgressState.None;
        TaskbarProgressValue = 0;
        _compressionCts.Cancel();
        AppendLogLine("Запрос отмены...");
    }

    [RelayCommand]
    public async Task DownloadFfmpegAsync()
    {
        if (IsDownloading) return;

        IsDownloading = true;
        DownloadProgress = 0;
        DownloadStatusText = "Подключение к серверу загрузки...";

        var progress = new Progress<double>(val =>
        {
            DownloadProgress = val;
            DownloadStatusText = $"Загрузка: {val:0.#}%";
        });

        try
        {
            var binPath = await _downloader.DownloadAndInstallAsync(progress, CancellationToken.None);
            DownloadStatusText = "FFmpeg успешно скачан и распакован!";
            RefreshFfmpegStatus();
            MessageBox.Show(
                $"FFmpeg успешно скачан и настроен!\nКаталог: {binPath}",
                "Успех",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            DownloadStatusText = $"Ошибка загрузки: {ex.Message}";
            MessageBox.Show(
                $"Не удалось скачать FFmpeg:\n{ex.Message}",
                "Ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsDownloading = false;
        }
    }

    [RelayCommand]
    public void ToggleAddToPath()
    {
        try
        {
            if (IsInPath)
            {
                _downloader.RemoveInstallRootFromUserPath();
                IsInPath = false;
                StatusMessage = "FFmpeg удален из пользовательского PATH.";
            }
            else
            {
                _downloader.AddInstallRootToUserPath();
                IsInPath = true;
                StatusMessage = "FFmpeg добавлен в пользовательский PATH.";
            }
            RefreshFfmpegStatus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Ошибка PATH", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public void BrowseCustomFfmpeg()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Выберите ffmpeg.exe",
            Filter = "ffmpeg.exe|ffmpeg.exe|Все файлы|*.*"
        };

        if (dlg.ShowDialog() == true)
        {
            CustomFfmpegPath = dlg.FileName;
            RefreshFfmpegStatus();
        }
    }

    [RelayCommand]
    public void BrowseOutputFolder()
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Выберите папку для сохранения сжатых видео"
        };

        if (dlg.ShowDialog() == true)
        {
            CustomOutputFolder = dlg.FolderName;
            UpdateAllOutputPaths();
        }
    }

    [RelayCommand]
    public void ResetOutputFolder()
    {
        CustomOutputFolder = null;
        UpdateAllOutputPaths();
    }

    [RelayCommand]
    public void OpenOutputFolder()
    {
        var folder = !string.IsNullOrWhiteSpace(CustomOutputFolder)
            ? CustomOutputFolder
            : SelectedItem != null ? Path.GetDirectoryName(SelectedItem.FilePath) : null;

        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            });
        }
    }

    [RelayCommand]
    public void ClearLogs()
    {
        _logBuffer.Clear();
        LogText = string.Empty;
    }

    [RelayCommand]
    public void CopyLogs()
    {
        if (!string.IsNullOrEmpty(LogText))
        {
            Clipboard.SetText(LogText);
            StatusMessage = "Лог скопирован в буфер обмена.";
        }
    }

    private void UpdateAllOutputPaths()
    {
        foreach (var item in Queue)
        {
            var dir = !string.IsNullOrWhiteSpace(CustomOutputFolder)
                ? CustomOutputFolder
                : Path.GetDirectoryName(item.FilePath)!;

            item.OutputPath = Path.Combine(dir, BuildOutputFileName(item.FilePath));
        }
    }

    private string BuildOutputFileName(string inputPath)
    {
        var name = Path.GetFileNameWithoutExtension(inputPath);
        var suffix = string.IsNullOrWhiteSpace(OutputFileSuffix) ? "_compressed" : OutputFileSuffix.Trim();
        var ext = SelectedExportFormat switch
        {
            "GIF Анимация" => ".gif",
            "MP3 Аудио" => ".mp3",
            _ => ".mp4"
        };
        return $"{name}{suffix}{ext}";
    }

    private string GetDefaultOutputPath(string inputPath)
    {
        var dir = !string.IsNullOrWhiteSpace(CustomOutputFolder)
            ? CustomOutputFolder
            : Path.GetDirectoryName(inputPath)!;

        return Path.Combine(dir, BuildOutputFileName(inputPath));
    }

    private void OnFfmpegProgressChanged(object? sender, FfmpegProgressEventArgs e)
    {
        if (e.Percent.HasValue)
        {
            CurrentProgress = e.Percent.Value;
            TaskbarProgressValue = Math.Clamp(CurrentProgress / 100.0, 0, 1.0);
            TaskbarProgressState = TaskbarItemProgressState.Normal;
            if (SelectedItem != null)
            {
                SelectedItem.Progress = e.Percent.Value;
            }

            if (e.Percent.Value > 1 && e.Percent.Value < 100 && _itemStopwatch.Elapsed.TotalSeconds > 1)
            {
                var totalEstimatedSec = (_itemStopwatch.Elapsed.TotalSeconds / e.Percent.Value) * 100.0;
                var remSec = Math.Max(0, totalEstimatedSec - _itemStopwatch.Elapsed.TotalSeconds);
                _currentEtaText = TimeSpan.FromSeconds(remSec).ToString(@"mm\:ss");
            }
        }

        var details = new StringBuilder();
        if (!string.IsNullOrEmpty(e.CurrentTime))
        {
            details.Append($"Время: {e.CurrentTime}");
        }
        if (!string.IsNullOrEmpty(e.Speed))
        {
            if (details.Length > 0) details.Append(" | ");
            details.Append($"Скорость: {e.Speed}");
        }
        if (!string.IsNullOrEmpty(e.Fps))
        {
            if (details.Length > 0) details.Append(" | ");
            details.Append($"FPS: {e.Fps}");
        }
        if (!string.IsNullOrEmpty(_currentEtaText))
        {
            if (details.Length > 0) details.Append(" | ");
            details.Append($"Осталось: ~{_currentEtaText}");
        }

        if (details.Length > 0)
        {
            ProgressDetails = details.ToString();
        }

        if (!string.IsNullOrEmpty(e.RawLine))
        {
            AppendLogLine(e.RawLine);
        }
    }

    private void AppendLogLine(string line)
    {
        lock (_logBuffer)
        {
            _logBuffer.AppendLine(line);
            // Ограничиваем буфер, чтобы не занимать сотни мегабайт
            if (_logBuffer.Length > 100000)
            {
                _logBuffer.Remove(0, 30000);
            }
            _hasNewLogs = true;
        }
    }

    private void FlushLogsToUi()
    {
        if (!_hasNewLogs) return;

        lock (_logBuffer)
        {
            LogText = _logBuffer.ToString();
            _hasNewLogs = false;
        }
    }

    [RelayCommand]
    public void SetTheme(string theme)
    {
        CurrentTheme = theme;
        var appTheme = theme switch
        {
            "Light" => Wpf.Ui.Appearance.ApplicationTheme.Light,
            "Dark" => Wpf.Ui.Appearance.ApplicationTheme.Dark,
            _ => Wpf.Ui.Appearance.ApplicationTheme.HighContrast
        };
        try
        {
            Wpf.Ui.Appearance.ApplicationThemeManager.Apply(appTheme);
            if (!string.IsNullOrEmpty(CurrentAccentHex))
            {
                SetAccentColor(CurrentAccentHex);
            }
            SaveSettings();
        }
        catch
        {
        }
    }

    [RelayCommand]
    public void SetAccentColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return;
        hex = hex.Trim();
        if (!hex.StartsWith("#")) hex = "#" + hex;

        try
        {
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            CurrentAccentHex = hex;
            CustomAccentHexInput = hex;

            foreach (var item in AccentColors)
            {
                item.IsSelected = string.Equals(item.Hex, hex, StringComparison.OrdinalIgnoreCase);
            }

            var appTheme = CurrentTheme switch
            {
                "Light" => Wpf.Ui.Appearance.ApplicationTheme.Light,
                "Dark" => Wpf.Ui.Appearance.ApplicationTheme.Dark,
                _ => Wpf.Ui.Appearance.ApplicationTheme.HighContrast
            };

            try
            {
                Wpf.Ui.Appearance.ApplicationAccentColorManager.Apply(color, appTheme);
            }
            catch
            {
            }

            if (Application.Current != null)
            {
                var accentBrush = new SolidColorBrush(color);
                accentBrush.Freeze();
                Application.Current.Resources["AppAccentBrush"] = accentBrush;

                var subtleBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x28, color.R, color.G, color.B));
                subtleBrush.Freeze();
                Application.Current.Resources["AppAccentSubtleBrush"] = subtleBrush;

                var lightBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x45, color.R, color.G, color.B));
                lightBrush.Freeze();
                Application.Current.Resources["AppAccentLightBrush"] = lightBrush;

                var borderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x90, color.R, color.G, color.B));
                borderBrush.Freeze();
                Application.Current.Resources["AppAccentBorderBrush"] = borderBrush;

                byte hr = (byte)Math.Min(255, color.R + 60);
                byte hg = (byte)Math.Min(255, color.G + 60);
                byte hb = (byte)Math.Min(255, color.B + 60);
                var highlightBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, hr, hg, hb));
                highlightBrush.Freeze();
                Application.Current.Resources["AppAccentHighlightBrush"] = highlightBrush;
            }

            SaveSettings();
        }
        catch
        {
        }
    }

    [RelayCommand]
    public void ApplyCustomHexColor()
    {
        if (string.IsNullOrWhiteSpace(CustomAccentHexInput)) return;
        var hex = CustomAccentHexInput.Trim();
        if (!hex.StartsWith("#")) hex = "#" + hex;
        SetAccentColor(hex);
    }

    [RelayCommand]
    public void SetBackdrop(string backdrop)
    {
        CurrentBackdrop = backdrop;
        BackdropChanged?.Invoke(backdrop);
        SaveSettings();
    }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = { "Б", "КБ", "МБ", "ГБ", "ТБ" };
        double len = bytes;
        var order = 0;
        while (len >= 1024 && order < suffixes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {suffixes[order]}";
    }
}
