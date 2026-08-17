using Microsoft.Win32;
using System.Windows;

namespace MH.Collector;

public partial class MainWindow : Window
{
    private readonly OfflineReplayService replayService = new();
    private string? selectedDirectory;

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

    private async Task ReplaySelectedDirectoryAsync()
    {
        if (string.IsNullOrWhiteSpace(selectedDirectory))
        {
            return;
        }

        ReplayButton.IsEnabled = false;
        StatusText.Text = "正在读取本地回放目录...";
        try
        {
            var result = await replayService.ReplayAsync(selectedDirectory);
            FramesList.ItemsSource = result.Frames;
            SucceededCountText.Text = result.SucceededCount.ToString();
            ReviewRequiredCountText.Text = result.ReviewRequiredCount.ToString();
            RejectedCountText.Text = result.RejectedCount.ToString();
            StatusText.Text = result.Error is null
                ? $"已完成 {result.Frames.Count} 帧回放。当前使用确定性离线回放，不上传、不调用服务、不自动点击。"
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
            ReplayButton.IsEnabled = true;
        }
    }
}
