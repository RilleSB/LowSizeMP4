using CommunityToolkit.Mvvm.ComponentModel;

namespace LowSizeMp4;

public partial class QueueItem : ObservableObject
{
    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private string _fileSizeFormatted = string.Empty;

    [ObservableProperty]
    private string _durationFormatted = string.Empty;

    [ObservableProperty]
    private string _resolutionFormatted = string.Empty;

    [ObservableProperty]
    private string? _thumbnailPath;

    [ObservableProperty]
    private string _outputPath = string.Empty;

    [ObservableProperty]
    private string _status = "В очереди";

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string? _resultDetails;

    [ObservableProperty]
    private double _durationSeconds;

    [ObservableProperty]
    private bool _isTrimEnabled;

    [ObservableProperty]
    private string _trimStart = "00:00:00";

    [ObservableProperty]
    private string _trimEnd = "00:00:00";

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private bool _isCompleted;

    [ObservableProperty]
    private bool _isError;
}
