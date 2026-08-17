using System.IO;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using MH.Core.OfflineReplay;

namespace MH.Collector;

public partial class MainWindow : Window
{
    private readonly OfflineReplayService replayService = new();
    private readonly OfflineReplayReviewStore reviewStore = new();
    private IReadOnlyList<ReviewRow> reviewRows = [];
    private string? selectedDirectory;
    private string? replayManifestSha256;
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
            var progress = new Progress<OfflineReplayProgress>(update =>
            {
                if (ReferenceEquals(replayCancellation, activeCancellation)
                    && !activeCancellation.IsCancellationRequested)
                {
                    UpdateReplayProgress(update);
                }
            });
            var result = await replayService.ReplayAsync(
                selectedDirectory,
                activeCancellation.Token,
                progress);
            replayManifestSha256 = await reviewStore.GetManifestSha256Async(
                selectedDirectory,
                activeCancellation.Token);
            var decisions = await reviewStore.LoadAsync(
                selectedDirectory,
                result.Frames,
                activeCancellation.Token);
            reviewRows = result.Frames
                .Select(frame => ReviewRow.Create(frame, decisions))
                .ToArray();
            FramesList.ItemsSource = reviewRows;
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
            reviewRows = [];
            replayManifestSha256 = null;
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

    private async void AcceptReviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ReviewRow row })
        {
            await SaveReviewDecisionAsync(row, OfflineReplayReviewDecisionKind.Accepted);
        }
    }

    private async void RejectReviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ReviewRow row })
        {
            await SaveReviewDecisionAsync(row, OfflineReplayReviewDecisionKind.Rejected);
        }
    }

    private async Task SaveReviewDecisionAsync(
        ReviewRow row,
        OfflineReplayReviewDecisionKind kind)
    {
        if (string.IsNullOrWhiteSpace(selectedDirectory)
            || !row.CanReview
            || (kind == OfflineReplayReviewDecisionKind.Accepted
                && string.IsNullOrWhiteSpace(row.SelectedCandidateId)))
        {
            StatusText.Text = "请选择当前复核帧已有候选，或明确拒绝该帧。";
            return;
        }

        var decision = new OfflineReplayReviewDecision(
            row.FrameId,
            kind,
            kind == OfflineReplayReviewDecisionKind.Accepted
                ? row.SelectedCandidateId
                : null);
        var decisions = reviewRows
            .Where(reviewRow => reviewRow.Decision is not null && !string.Equals(
                reviewRow.FrameId,
                row.FrameId,
                StringComparison.Ordinal))
            .Select(reviewRow => new OfflineReplayReviewDecision(
                reviewRow.FrameId,
                reviewRow.Decision!.Value,
                reviewRow.Decision == OfflineReplayReviewDecisionKind.Accepted
                    ? reviewRow.SelectedCandidateId
                    : null))
            .Append(decision)
            .ToArray();

        try
        {
            await reviewStore.SaveAsync(
                selectedDirectory,
                reviewRows.Select(reviewRow => reviewRow.Frame).ToArray(),
                decisions,
                expectedManifestSha256: replayManifestSha256);
            row.Decision = kind;
            StatusText.Text = kind == OfflineReplayReviewDecisionKind.Accepted
                ? $"已接受 {row.FrameId} 的人工选择。"
                : $"已拒绝 {row.FrameId}，不会自动写入行情。";
            FramesList.Items.Refresh();
        }
        catch (InvalidOperationException exception)
        {
            StatusText.Text = $"复核结果未保存：{exception.Message}";
        }
        catch (IOException exception)
        {
            StatusText.Text = $"复核结果未保存：{exception.Message}";
        }
        catch (UnauthorizedAccessException exception)
        {
            StatusText.Text = $"复核结果未保存：{exception.Message}";
        }
    }

    private sealed class ReviewRow
    {
        private ReviewRow(OfflineReplayFrameResult frame)
        {
            Frame = frame;
        }

        public OfflineReplayFrameResult Frame { get; }

        public string FrameId => Frame.FrameId;

        public string CapturedAtText => Frame.CapturedAtText;

        public OfflineReplayStatus Status => Frame.Status;

        public string StatusText => Frame.StatusText;

        public string ImagePath => Frame.ImagePath;

        public string CandidateText => Frame.CandidateText;

        public string Reason => Frame.Reason;

        public IReadOnlyList<OfflineReplayCandidate> Candidates => Frame.Candidates;

        public bool CanReview => Frame.Status == OfflineReplayStatus.ReviewRequired;

        public bool CanAccept => CanReview && Candidates.Count > 0;

        public bool CanReject => CanReview;

        public string? SelectedCandidateId { get; set; }

        public OfflineReplayReviewDecisionKind? Decision { get; set; }

        public string ReviewDecisionText => Decision switch
        {
            OfflineReplayReviewDecisionKind.Accepted => "已接受",
            OfflineReplayReviewDecisionKind.Rejected => "已拒绝",
            _ => "未处理"
        };

        public static ReviewRow Create(
            OfflineReplayFrameResult frame,
            IReadOnlyList<OfflineReplayReviewDecision> decisions)
        {
            var row = new ReviewRow(frame);
            var decision = decisions.FirstOrDefault(existing =>
                string.Equals(existing.FrameId, frame.FrameId, StringComparison.Ordinal));
            if (decision is not null)
            {
                row.Decision = decision.Kind;
                row.SelectedCandidateId = decision.CandidateItemId;
            }

            return row;
        }
    }
}
