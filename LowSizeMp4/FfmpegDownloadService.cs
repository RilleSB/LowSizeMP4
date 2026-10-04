using System.IO;
using System.IO.Compression;
using System.Net.Http;

namespace LowSizeMp4;

public sealed class FfmpegDownloadService
{
    private static readonly HttpClient Http = new(new HttpClientHandler
    {
        AllowAutoRedirect = true
    })
    {
        Timeout = TimeSpan.FromMinutes(15)
    };

    public const string DownloadUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";

    public string InstallRoot { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LowSizeMp4", "ffmpeg");

    public async Task<string> DownloadAndInstallAsync(
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(InstallRoot);

        var archivePath = Path.Combine(InstallRoot, "ffmpeg.zip");
        await DownloadFileAsync(DownloadUrl, archivePath, progress, cancellationToken);

        ExtractArchive(archivePath, InstallRoot);
        try
        {
            File.Delete(archivePath);
        }
        catch
        {
        }

        var ffmpeg = FindExecutable("ffmpeg.exe");
        var ffprobe = FindExecutable("ffprobe.exe");

        if (ffmpeg is null || ffprobe is null)
        {
            throw new InvalidOperationException("Архив скачался, но ffmpeg.exe или ffprobe.exe не найдены после распаковки.");
        }

        return GetBinDirectory() ?? InstallRoot;
    }

    public string? FindExecutable(string executableName)
    {
        if (!Directory.Exists(InstallRoot))
        {
            return null;
        }

        try
        {
            foreach (var candidate in Directory.EnumerateFiles(InstallRoot, executableName, SearchOption.AllDirectories))
            {
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
        }
        catch
        {
        }

        return null;
    }

    public string? GetBinDirectory()
    {
        var ffmpeg = FindExecutable("ffmpeg.exe");
        return ffmpeg is not null ? Path.GetDirectoryName(ffmpeg) : null;
    }

    public bool AddInstallRootToUserPath()
    {
        var binDir = GetBinDirectory() ?? InstallRoot;
        var paths = GetUserPathEntries();

        if (paths.Any(p => string.Equals(p, binDir, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        paths.Add(binDir);
        var updated = string.Join(Path.PathSeparator, paths);

        Environment.SetEnvironmentVariable("Path", updated, EnvironmentVariableTarget.User);
        Environment.SetEnvironmentVariable("Path", updated, EnvironmentVariableTarget.Process);

        return true;
    }

    public bool RemoveInstallRootFromUserPath()
    {
        var binDir = GetBinDirectory();
        var paths = GetUserPathEntries();
        var updatedPaths = paths
            .Where(p => !string.Equals(p, InstallRoot, StringComparison.OrdinalIgnoreCase) &&
                        (binDir == null || !string.Equals(p, binDir, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var updated = string.Join(Path.PathSeparator, updatedPaths);
        Environment.SetEnvironmentVariable("Path", updated, EnvironmentVariableTarget.User);
        Environment.SetEnvironmentVariable("Path", updated, EnvironmentVariableTarget.Process);

        return true;
    }

    public bool IsInstallRootInUserPath()
    {
        var binDir = GetBinDirectory();
        var userPaths = GetUserPathEntries();

        return userPaths.Any(p =>
            string.Equals(p, InstallRoot, StringComparison.OrdinalIgnoreCase) ||
            (binDir != null && string.Equals(p, binDir, StringComparison.OrdinalIgnoreCase)));
    }

    private static async Task DownloadFileAsync(
        string url,
        string destinationPath,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[81920];
        long receivedBytes = 0;
        int read;

        while ((read = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            receivedBytes += read;

            if (totalBytes is > 0)
            {
                progress?.Report(receivedBytes * 100.0 / totalBytes.Value);
            }
        }
    }

    private static List<string> GetUserPathEntries()
    {
        var currentUserPath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? string.Empty;
        return currentUserPath
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    private static void ExtractArchive(string archivePath, string destinationRoot)
    {
        using var archive = ZipFile.OpenRead(archivePath);

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                continue;
            }

            var relativePath = entry.FullName;
            if (relativePath.StartsWith("/"))
            {
                relativePath = relativePath[1..];
            }

            var targetPath = Path.GetFullPath(Path.Combine(destinationRoot, relativePath));
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            entry.ExtractToFile(targetPath, overwrite: true);
        }
    }
}
