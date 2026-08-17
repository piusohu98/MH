using System.Security.Cryptography;
using System.Text.Json;
using MH.Collector;
using MH.Core.OfflineReplay;

namespace MH.Tests;

public sealed class CollectorReplayReviewTests
{
    [Fact]
    public async Task SavesAndLoadsOnlyValidReviewDecisionsInFrameOrder()
    {
        using var replay = ReplayDirectory.Create();
        var frames = new[]
        {
            ReviewFrame("frame-1", "item-1"),
            ReviewFrame("frame-2", "item-2")
        };
        var decisions = new[]
        {
            new OfflineReplayReviewDecision("frame-2", OfflineReplayReviewDecisionKind.Rejected, null),
            new OfflineReplayReviewDecision("frame-1", OfflineReplayReviewDecisionKind.Accepted, "item-1")
        };

        var store = new OfflineReplayReviewStore();
        await store.SaveAsync(replay.Path, frames, decisions);
        var loaded = await store.LoadAsync(replay.Path, frames);

        Assert.Equal(
            ["frame-1", "frame-2"],
            loaded.Select(decision => decision.FrameId));
        Assert.Equal(OfflineReplayReviewDecisionKind.Accepted, loaded[0].Kind);
        Assert.Equal("item-1", loaded[0].CandidateItemId);
        Assert.Null(loaded[1].CandidateItemId);
        Assert.True(File.Exists(OfflineReplayReviewStore.GetSidecarPath(replay.Path)));
        Assert.False(File.Exists(OfflineReplayReviewStore.GetSidecarPath(replay.Path) + ".tmp"));
    }

    [Fact]
    public async Task RejectsMissingCandidateAndDecisionsForNonReviewFrames()
    {
        using var replay = ReplayDirectory.Create();
        var store = new OfflineReplayReviewStore();

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(
            replay.Path,
            [ReviewFrame("frame-1", "item-1")],
            [new OfflineReplayReviewDecision("frame-1", OfflineReplayReviewDecisionKind.Accepted, "missing")]));

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(
            replay.Path,
            [new OfflineReplayFrameResult(
                "frame-1",
                "frames/one.png",
                DateTimeOffset.UtcNow,
                OfflineReplayStatus.Accepted,
                "accepted",
                "item-1")
            {
                Candidates = [new OfflineReplayCandidate("item-1", "Item 1", 1.0m, true)]
            }],
            [new OfflineReplayReviewDecision("frame-1", OfflineReplayReviewDecisionKind.Rejected, null)]));

        Assert.False(File.Exists(OfflineReplayReviewStore.GetSidecarPath(replay.Path)));
    }

    [Fact]
    public async Task RefusesToSaveWhenManifestChangedSinceReplay()
    {
        using var replay = ReplayDirectory.Create();
        var store = new OfflineReplayReviewStore();

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(
            replay.Path,
            [ReviewFrame("frame-1", "item-1")],
            [new OfflineReplayReviewDecision("frame-1", OfflineReplayReviewDecisionKind.Accepted, "item-1")],
            expectedManifestSha256: "stale-hash"));

        Assert.False(File.Exists(OfflineReplayReviewStore.GetSidecarPath(replay.Path)));
    }

    [Fact]
    public async Task IgnoresCorruptChangedManifestAndUnknownFrameSidecars()
    {
        using var replay = ReplayDirectory.Create();
        var frames = new[] { ReviewFrame("frame-1", "item-1") };
        var store = new OfflineReplayReviewStore();
        await store.SaveAsync(
            replay.Path,
            frames,
            [new OfflineReplayReviewDecision("frame-1", OfflineReplayReviewDecisionKind.Accepted, "item-1")]);

        await File.WriteAllTextAsync(
            OfflineReplayReviewStore.GetSidecarPath(replay.Path),
            "{ not-json");
        Assert.Empty(await store.LoadAsync(replay.Path, frames));

        await store.SaveAsync(
            replay.Path,
            frames,
            [new OfflineReplayReviewDecision("frame-1", OfflineReplayReviewDecisionKind.Accepted, "item-1")]);
        await File.WriteAllTextAsync(
            replay.ManifestPath,
            "changed-manifest");
        Assert.Empty(await store.LoadAsync(replay.Path, frames));

        await File.WriteAllTextAsync(replay.ManifestPath, "manifest-v1");
        var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(replay.ManifestPath)));
        await File.WriteAllTextAsync(
            OfflineReplayReviewStore.GetSidecarPath(replay.Path),
            JsonSerializer.Serialize(new
            {
                version = "offline-replay-review-v1",
                manifestSha256 = hash,
                decisions = new[]
                {
                    new { frameId = "missing", kind = 0, candidateItemId = "item-1" }
                }
            }));

        Assert.Empty(await store.LoadAsync(replay.Path, frames));
    }

    private static OfflineReplayFrameResult ReviewFrame(string frameId, params string[] candidateIds)
        => new(
            frameId,
            $"frames/{frameId}.png",
            new DateTimeOffset(2026, 8, 17, 1, 0, 0, TimeSpan.Zero).AddSeconds(candidateIds.Length),
            OfflineReplayStatus.ReviewRequired,
            "review",
            string.Join(",", candidateIds))
        {
            Candidates = candidateIds
                .Select(candidateId => new OfflineReplayCandidate(candidateId, candidateId, 0.8m, false))
                .ToArray()
        };

    private sealed class ReplayDirectory : IDisposable
    {
        private ReplayDirectory(string path)
        {
            Path = path;
            Directory.CreateDirectory(path);
            File.WriteAllText(ManifestPath, "manifest-v1");
        }

        public string Path { get; }

        public string ManifestPath => System.IO.Path.Combine(Path, "manifest.json");

        public static ReplayDirectory Create()
            => new(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"MHCollectorReview-{Guid.NewGuid():N}"));

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
