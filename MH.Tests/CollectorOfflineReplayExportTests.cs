using System.Security.Cryptography;
using MH.Collector;
using MH.Core.OfflineReplay;

namespace MH.Tests;

public sealed class CollectorOfflineReplayExportTests
{
    [Fact]
    public async Task ExportIsByteStableAndSeparatesAutomaticManualAndUnprocessedDecisions()
    {
        using var replay = ReplayDirectory.Create();
        var frames = replay.CreateFrames();
        var scan = replay.CreateScan(frames);
        var reviewStore = new OfflineReplayReviewStore();
        await reviewStore.SaveAsync(
            replay.Path,
            frames,
            [new OfflineReplayReviewDecision(
                "frame-2",
                OfflineReplayReviewDecisionKind.Accepted,
                "item-2")]);
        var exportedAtUtc = new DateTimeOffset(2026, 8, 17, 2, 0, 0, TimeSpan.Zero);
        var exporter = new OfflineReplayExportService(reviewStore);

        var first = await exporter.ExportAsync(replay.Path, scan, exportedAtUtc);
        var firstBytes = await File.ReadAllBytesAsync(replay.ExportPath);
        var second = await exporter.ExportAsync(replay.Path, scan, exportedAtUtc);
        var secondBytes = await File.ReadAllBytesAsync(replay.ExportPath);

        Assert.Equal(firstBytes, secondBytes);
        Assert.Equal(
            [
                OfflineReplayEffectiveDecision.AutoAccepted,
                OfflineReplayEffectiveDecision.ManuallyAccepted,
                OfflineReplayEffectiveDecision.Unprocessed
            ],
            first.Frames.Select(frame => frame.EffectiveDecision));
        Assert.Equal([true, true, false], first.Frames.Select(frame => frame.IsDecisionAccepted));
        Assert.Equal("item-2", first.Frames[1].EffectiveCandidateItemId);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData([2, 3, 5])),
            first.Frames[1].ImageSha256);
        Assert.True(first.ReviewSidecarPresent);
    }

    [Fact]
    public async Task ExportAllowsNoReviewSidecarButRejectsCorruptSidecar()
    {
        using var replay = ReplayDirectory.Create();
        var frames = replay.CreateFrames();
        var scan = replay.CreateScan(frames);
        var exporter = new OfflineReplayExportService();

        var withoutReview = await exporter.ExportAsync(
            replay.Path,
            scan,
            new DateTimeOffset(2026, 8, 17, 2, 0, 0, TimeSpan.Zero));
        Assert.False(withoutReview.ReviewSidecarPresent);
        Assert.Equal(OfflineReplayEffectiveDecision.Unprocessed, withoutReview.Frames[1].EffectiveDecision);

        await File.WriteAllTextAsync(
            OfflineReplayReviewStore.GetSidecarPath(replay.Path),
            "{ not-json");
        await Assert.ThrowsAsync<InvalidOperationException>(() => exporter.ExportAsync(
            replay.Path,
            scan,
            new DateTimeOffset(2026, 8, 17, 2, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public async Task ExportRejectsChangedManifestAndKeepsSourceFilesUnchanged()
    {
        using var replay = ReplayDirectory.Create();
        var frames = replay.CreateFrames();
        var scan = replay.CreateScan(frames);
        var manifestBefore = await File.ReadAllBytesAsync(replay.ManifestPath);
        var exporter = new OfflineReplayExportService();

        await exporter.ExportAsync(
            replay.Path,
            scan,
            new DateTimeOffset(2026, 8, 17, 2, 0, 0, TimeSpan.Zero));
        Assert.Equal(manifestBefore, await File.ReadAllBytesAsync(replay.ManifestPath));

        await File.WriteAllTextAsync(replay.ManifestPath, "changed-manifest");
        await Assert.ThrowsAsync<InvalidOperationException>(() => exporter.ExportAsync(
            replay.Path,
            scan,
            new DateTimeOffset(2026, 8, 17, 2, 0, 0, TimeSpan.Zero)));
    }

    private sealed class ReplayDirectory : IDisposable
    {
        private ReplayDirectory(string path)
        {
            Path = path;
            Directory.CreateDirectory(path);
            Directory.CreateDirectory(System.IO.Path.Combine(path, "frames"));
            ManifestPath = System.IO.Path.Combine(path, "manifest.json");
            ExportPath = System.IO.Path.Combine(path, OfflineReplayExportService.ExportFileName);
            File.WriteAllText(ManifestPath, "manifest-v1");
            File.WriteAllBytes(System.IO.Path.Combine(path, "frames", "one.png"), [1]);
            File.WriteAllBytes(System.IO.Path.Combine(path, "frames", "two.png"), [2, 3, 5]);
            File.WriteAllBytes(System.IO.Path.Combine(path, "frames", "three.png"), [8, 13]);
        }

        public string Path { get; }

        public string ManifestPath { get; }

        public string ExportPath { get; }

        public static ReplayDirectory Create()
            => new(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"MHCollectorExport-{Guid.NewGuid():N}"));

        public OfflineReplayFrameResult[] CreateFrames()
            =>
            [
                new OfflineReplayFrameResult(
                    "frame-2",
                    "frames/two.png",
                    new DateTimeOffset(2026, 8, 17, 1, 0, 2, TimeSpan.Zero),
                    OfflineReplayStatus.ReviewRequired,
                    "needs review",
                    "item-2")
                {
                    RawText = "candidate two",
                    Candidates = [new OfflineReplayCandidate("item-2", "Item 2", 0.8m, false)],
                    Issues = [new OfflineReplayIssue("low-confidence", "low confidence")]
                },
                new OfflineReplayFrameResult(
                    "frame-1",
                    "frames/one.png",
                    new DateTimeOffset(2026, 8, 17, 1, 0, 1, TimeSpan.Zero),
                    OfflineReplayStatus.Accepted,
                    "accepted",
                    "item-1")
                {
                    RawText = "item one",
                    Candidates = [new OfflineReplayCandidate("item-1", "Item 1", 1.0m, true)],
                    Issues = []
                },
                new OfflineReplayFrameResult(
                    "frame-3",
                    "frames/three.png",
                    new DateTimeOffset(2026, 8, 17, 1, 0, 3, TimeSpan.Zero),
                    OfflineReplayStatus.Rejected,
                    "rejected",
                    "无候选")
                {
                    RawText = "unknown",
                    Candidates = [],
                    Issues = [new OfflineReplayIssue("no-match", "no match")]
                }
            ];

        public OfflineReplayScanResult CreateScan(IReadOnlyList<OfflineReplayFrameResult> frames)
        {
            var manifestSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(ManifestPath)));
            return new OfflineReplayScanResult(frames, null, manifestSha256)
            {
                ReplayId = "export-replay"
            };
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
