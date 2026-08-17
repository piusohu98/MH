using System.Text.Json;
using MH.Collector;
using MH.Core.OfflineReplay;

namespace MH.Tests;

public sealed class CollectorOfflineReplayTests
{
    private static readonly DateTimeOffset CreatedAtUtc = new(2026, 8, 17, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ReplayReadsLocalImageAndAcceptsConfirmedCandidate()
    {
        using var replay = ReplayDirectory.Create();
        await replay.WriteImageAsync("frames/accepted.png");
        await replay.WriteManifestAsync(
            Frame(
                "frame-accepted",
                "frames/accepted.png",
                CreatedAtUtc.AddSeconds(1),
                [Candidate("item-1", "Accepted item", 0.95m, true)]));

        var result = await new OfflineReplayService().ReplayAsync(replay.Path);

        Assert.Null(result.Error);
        var frame = Assert.Single(result.Frames);
        Assert.Equal("frame-accepted", frame.FrameId);
        Assert.Equal("frames/accepted.png", frame.ImagePath);
        Assert.Equal(OfflineReplayStatus.Accepted, frame.Status);
        Assert.Equal("Accepted item", frame.CandidateText);
        Assert.Equal(1, result.SucceededCount);
        Assert.Equal(0, result.ReviewRequiredCount);
        Assert.Equal(0, result.RejectedCount);
    }

    [Fact]
    public async Task ReplayDowngradesLowConfidenceAndMissingCandidates()
    {
        using var replay = ReplayDirectory.Create();
        await replay.WriteImageAsync("frames/low.png");
        await replay.WriteImageAsync("frames/missing-candidate.png");
        await replay.WriteManifestAsync(
            Frame(
                "frame-low",
                "frames/low.png",
                CreatedAtUtc.AddSeconds(1),
                [Candidate("item-low", "Low confidence", 0.84m, true)]),
            Frame(
                "frame-missing-candidate",
                "frames/missing-candidate.png",
                CreatedAtUtc.AddSeconds(2),
                []));

        var result = await new OfflineReplayService().ReplayAsync(replay.Path);

        Assert.Null(result.Error);
        Assert.Equal(0, result.SucceededCount);
        Assert.Equal(1, result.ReviewRequiredCount);
        Assert.Equal(1, result.RejectedCount);
        Assert.Equal(OfflineReplayStatus.ReviewRequired, result.Frames[0].Status);
        Assert.Contains("confidence", result.Frames[0].Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(OfflineReplayStatus.Rejected, result.Frames[1].Status);
        Assert.Contains("candidate", result.Frames[1].Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReplayRejectsTraversalInManifestBeforeReadingFrames()
    {
        using var replay = ReplayDirectory.Create();
        await replay.WriteManifestAsync(
            Frame(
                "frame-traversal",
                "../outside.png",
                CreatedAtUtc.AddSeconds(1),
                [Candidate("item-1", "Unsafe item", 0.95m, true)]));

        var result = await new OfflineReplayService().ReplayAsync(replay.Path);

        Assert.Empty(result.Frames);
        Assert.NotNull(result.Error);
        Assert.Contains("manifest 校验失败", result.Error, StringComparison.Ordinal);
        Assert.Contains("../outside.png", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReplayRejectsMissingImageFileAsFrameFailure()
    {
        using var replay = ReplayDirectory.Create();
        await replay.WriteManifestAsync(
            Frame(
                "frame-missing-image",
                "frames/missing.png",
                CreatedAtUtc.AddSeconds(1),
                [Candidate("item-1", "Missing image", 0.95m, true)]));

        var result = await new OfflineReplayService().ReplayAsync(replay.Path);

        Assert.Null(result.Error);
        var frame = Assert.Single(result.Frames);
        Assert.Equal(OfflineReplayStatus.Rejected, frame.Status);
        Assert.Contains("图片文件无法读取", frame.Reason, StringComparison.Ordinal);
        Assert.Equal(1, result.RejectedCount);
    }

    [Fact]
    public async Task ReplayUsesDeterministicCapturedTimePathAndFrameOrdering()
    {
        using var replay = ReplayDirectory.Create();
        await replay.WriteImageAsync("frames/z.png");
        await replay.WriteImageAsync("frames/a.png");
        await replay.WriteImageAsync("frames/m.png");
        await replay.WriteManifestAsync(
            Frame(
                "frame-z",
                "frames/z.png",
                CreatedAtUtc.AddSeconds(2),
                [Candidate("item-z", "Z", 0.95m, true)]),
            Frame(
                "frame-m",
                "frames/m.png",
                CreatedAtUtc.AddSeconds(1),
                [Candidate("item-m", "M", 0.95m, true)]),
            Frame(
                "frame-a",
                "frames/a.png",
                CreatedAtUtc.AddSeconds(1),
                [Candidate("item-a", "A", 0.95m, true)]));

        var service = new OfflineReplayService();
        var first = await service.ReplayAsync(replay.Path);
        var second = await service.ReplayAsync(replay.Path);

        Assert.Null(first.Error);
        Assert.Equal(["frame-a", "frame-m", "frame-z"], first.Frames.Select(frame => frame.FrameId));
        Assert.Equal(
            first.Frames.Select(frame => (frame.FrameId, frame.Status, frame.ImagePath)),
            second.Frames.Select(frame => (frame.FrameId, frame.Status, frame.ImagePath)));
    }

    [Fact]
    public async Task ReplayReturnsErrorForNonexistentDirectory()
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), $"MHCollectorMissing-{Guid.NewGuid():N}");

        var result = await new OfflineReplayService().ReplayAsync(directoryPath);

        Assert.Empty(result.Frames);
        Assert.Equal("目录不存在。", result.Error);
    }

    private static ReplayFrame Frame(
        string frameId,
        string relativeImagePath,
        DateTimeOffset capturedAtUtc,
        IReadOnlyList<ReplayCandidate> candidates)
        => new(frameId, relativeImagePath, capturedAtUtc, null, candidates);

    private static ReplayCandidate Candidate(
        string itemId,
        string displayName,
        decimal confidence,
        bool isConfirmed)
        => new(itemId, displayName, confidence, isConfirmed);

    private sealed record ReplayFrame(
        string FrameId,
        string RelativeImagePath,
        DateTimeOffset CapturedAtUtc,
        string? RawText,
        IReadOnlyList<ReplayCandidate> Candidates);

    private sealed record ReplayCandidate(
        string ItemId,
        string DisplayName,
        decimal Confidence,
        bool IsConfirmed);

    private sealed class ReplayDirectory : IDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private ReplayDirectory(string path)
        {
            Path = path;
            Directory.CreateDirectory(path);
        }

        public string Path { get; }

        public static ReplayDirectory Create()
            => new(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"MHCollectorReplay-{Guid.NewGuid():N}"));

        public async Task WriteImageAsync(string relativePath)
        {
            var imagePath = System.IO.Path.Combine(Path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            var imageDirectory = System.IO.Path.GetDirectoryName(imagePath);
            Assert.NotNull(imageDirectory);
            Directory.CreateDirectory(imageDirectory!);
            await File.WriteAllBytesAsync(imagePath, [0x89, 0x50, 0x4E, 0x47]);
        }

        public async Task WriteManifestAsync(params ReplayFrame[] frames)
        {
            var manifest = new
            {
                version = OfflineReplayContract.ManifestVersion,
                replayId = "replay-test",
                createdAtUtc = CreatedAtUtc,
                frames
            };

            await using var stream = File.Create(System.IO.Path.Combine(Path, "manifest.json"));
            await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
