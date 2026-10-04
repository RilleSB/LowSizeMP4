using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Wpf.Ui.Controls;

namespace LowSizeMp4;

public partial class MainWindow : FluentWindow
{
    private static readonly Brush DragOverBrush = new SolidColorBrush(Color.FromArgb(50, 0, 120, 212));
    private static readonly Brush DragNormalBrush = new SolidColorBrush(Color.FromArgb(12, 255, 255, 255));

    public MainWindow()
    {
        InitializeComponent();
        ViewModel.BackdropChanged += OnBackdropChanged;
        OnBackdropChanged(ViewModel.CurrentBackdrop);
        Loaded += OnMainWindowLoaded;
    }

    private void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1)
        {
            var files = args.Skip(1).Where(File.Exists).ToArray();
            if (files.Length > 0)
            {
                ViewModel.AddFiles(files);
            }
        }
    }

    private void OnBackdropChanged(string backdrop)
    {
        WindowBackdropType = backdrop switch
        {
            "Acrylic" => WindowBackdropType.Acrylic,
            "None" => WindowBackdropType.None,
            _ => WindowBackdropType.Mica
        };
    }

    private void NavButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tagStr } && int.TryParse(tagStr, out var index))
        {
            ViewModel.SelectedNavIndex = index;
        }
    }

    private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source is TabControl tc && tc.Parent is FrameworkElement container)
        {
            if (Resources["TabFadeInAnimation"] is System.Windows.Media.Animation.Storyboard sb)
            {
                sb.Begin(container);
            }
        }
    }

    private void PickFilesButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Выберите видео для сжатия",
            Filter = "Видеофайлы (*.mp4;*.mkv;*.mov;*.avi;*.webm;*.flv;*.ts)|*.mp4;*.mkv;*.mov;*.avi;*.webm;*.flv;*.ts|Все файлы (*.*)|*.*",
            Multiselect = true
        };

        if (dlg.ShowDialog() == true)
        {
            ViewModel.AddFiles(dlg.FileNames);
        }
    }

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            HighlightDropZone(true);
            e.Handled = true;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }

    private void DropZone_DragLeave(object sender, DragEventArgs e)
    {
        HighlightDropZone(false);
    }

    private void DropZone_Drop(object sender, DragEventArgs e)
    {
        HighlightDropZone(false);

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0)
            {
                ViewModel.AddFiles(files);
            }
        }
    }

    private void HighlightDropZone(bool active)
    {
        if (HeroDropZone?.Children.Count > 0 && HeroDropZone.Children[0] is Border b)
        {
            if (active)
            {
                b.Background = Application.Current.TryFindResource("AppAccentSubtleBrush") as Brush ?? DragOverBrush;
                b.BorderBrush = Application.Current.TryFindResource("AppAccentBrush") as Brush ?? Brushes.DodgerBlue;
            }
            else
            {
                b.Background = DragNormalBrush;
                b.BorderBrush = new SolidColorBrush(Color.FromArgb(55, 255, 255, 255));
            }
        }
    }

    private void LogTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBox tb)
        {
            tb.ScrollToEnd();
        }
    }
}
