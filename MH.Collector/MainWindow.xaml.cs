using Microsoft.Win32;
using System.Windows;

namespace MH.Collector;

public partial class MainWindow : Window
{
    private readonly OfflineReplayService replayService = new();
    private string? selectedDirectory;
    private CancellationTokenSource? replayCancellation;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void SelectDirectoryButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择包含 manifest.json 的截图目录",
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        selectedDirectory = dialog.FolderName;
        DirectoryText.Text = selectedDirectory;
        ReplayButton.IsEnabled = true;
        _ = ReplaySelectedDirectoryAsync();
    }

    private async void ReplayButton_Click(object sender, RoutedEventArgs e)
        => await ReplaySelectedDirectoryAsync();

    private void CancelReplayButton_Click(object sender, RoutedEventArgs e)
    {
        CancelReplayButton.IsEnabled = false;
        StatusText.Text = "正在取消回放...";
        replayCancellation?.Cancel();
    }

    private async Task ReplaySelectedDirectoryAsync()
    {
        if (string.IsNullOrWhiteSpace(selectedDirectory))
        {
            return;
        }

        replayCancellation?.Dispose();
        replayCancellation = new CancellationTokenSource();
        var activeCancellation = replayCancellation;
        SelectDirectoryButton.IsEnabled = false;
        ReplayButton.IsEnabled = false;
        CancelReplayButton.IsEnabled = true;
        StatusText.Text = "正在读取本地回放目录...";
        try
        {
            var progress = new Progress<OfflineReplayProgress>(UpdateReplayProgress);
            var result = await replayService.ReplayAsync(
                selectedDirectory,
                activeCancellation.Token,
                progress);
            FramesList.ItemsSource = result.Frames;
            SucceededCountText.Text = result.SucceededCount.ToString();
            ReviewRequiredCountText.Text = result.ReviewRequiredCount.ToString();
            RejectedCountText.Text = result.RejectedCount.ToString();
            StatusText.Text = result.Error is null
                ? result.ReviewRequiredCount > 0
                    ? $"已完成 {result.Frames.Count} 帧回放，其中 {result.ReviewRequiredCount} 帧需要人工复核。"
                    : $"已完成 {result.Frames.Count} 帧回放，无需人工复核。"
                : result.Error;
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "回放已取消。";
        }
        catch (Exception exception)
        {
            FramesList.ItemsSource = Array.Empty<OfflineReplayFrameResult>();
            SucceededCountText.Text = "0";
            ReviewRequiredCountText.Text = "0";
            RejectedCountText.Text = "0";
            StatusText.Text = $"回放失败: {exception.Message}";
        }
        finally
        {
            if (ReferenceEquals(replayCancellation, activeCancellation))
            {
                replayCancellation.Dispose();
                replayCancellation = null;
                SelectDirectoryButton.IsEnabled = true;
                ReplayButton.IsEnabled = !string.IsNullOrWhiteSpace(selectedDirectory);
                CancelReplayButton.IsEnabled = false;
            }
        }
    }

    private void UpdateReplayProgress(OfflineReplayProgress progress)
    {
        StatusText.Text = progress.State switch
        {
            OfflineReplayProgressState.Reading => "正在读取本地回放目录...",
            OfflineReplayProgressState.Recognizing =>
                $"正在处理第 {progress.ProcessedCount + 1}/{progress.TotalCount} 帧：{progress.CurrentFrameId}",
            _ => StatusText.Text
        };
    }
}
