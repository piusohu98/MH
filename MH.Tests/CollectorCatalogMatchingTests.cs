using System.Text.Json;
using MH.Collector;
using MH.Core.OfflineReplay;

namespace MH.Tests;

public sealed class CollectorCatalogMatchingTests
{
    private static readonly DateTimeOffset CreatedAtUtc = new(2026, 8, 17, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ReplayUsesLocalCatalogForExactMatchAcceptance()
    {
        using var replay = ReplayDirectory.Create();
        await replay.WriteImageAsync();
        await replay.WriteManifestAsync("灵药", [new("item-1", "灵药")]);

        var result = await new OfflineReplayService().ReplayAsync(replay.Path);

        var frame = Assert.Single(result.Frames);
        Assert.Equal(OfflineReplayStatus.Accepted, frame.Status);
        Assert.Equal("灵药", frame.CandidateText);
        Assert.Equal(1, result.SucceededCount);
        Assert.DoesNotContain("match-", frame.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReplayMarksContainsCatalogMatchForReview()
    {
        using var replay = ReplayDirectory.Create();
        await replay.WriteImageAsync();
        await replay.WriteManifestAsync("灵", [new("item-1", "灵药")]);

        var result = await new OfflineReplayService().ReplayAsync(replay.Path);

        var frame = Assert.Single(result.Frames);
        Assert.Equal(OfflineReplayStatus.ReviewRequired, frame.Status);
        Assert.Contains("0.85", frame.Reason, StringComparison.Ordinal);
        Assert.Equal("灵药", frame.CandidateText);
    }

    [Fact]
    public async Task ReplayRejectsTextThatDoesNotMatchLocalCatalog()
    {
        using var replay = ReplayDirectory.Create();
        await replay.WriteImageAsync();
        await replay.WriteManifestAsync("不存在", [new("item-1", "灵药")]);

        var result = await new OfflineReplayService().ReplayAsync(replay.Path);

        var frame = Assert.Single(result.Frames);
        Assert.Equal(OfflineReplayStatus.Rejected, frame.Status);
        Assert.Contains("OCR text did not match", frame.Reason, StringComparison.Ordinal);
        Assert.Equal("无候选", frame.CandidateText);
    }

    [Fact]
    public async Task ReplayRejectsEmptyLocalCatalogInsteadOfUsingManifestHints()
    {
        using var replay = ReplayDirectory.Create();
        await replay.WriteImageAsync();
        await replay.WriteManifestAsync("灵药", []);

        var result = await new OfflineReplayService().ReplayAsync(replay.Path);

        var frame = Assert.Single(result.Frames);
        Assert.Equal(OfflineReplayStatus.Rejected, frame.Status);
        Assert.Contains("item catalog must contain", frame.Reason, StringComparison.Ordinal);
    }

    private sealed record CatalogItem(string Id, string Name);

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
            => new(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"MHCollectorCatalog-{Guid.NewGuid():N}"));

        public async Task WriteImageAsync()
        {
            Directory.CreateDirectory(System.IO.Path.Combine(Path, "frames"));
            await File.WriteAllBytesAsync(
                System.IO.Path.Combine(Path, "frames", "item.png"),
                [0x89, 0x50, 0x4E, 0x47]);
        }

        public async Task WriteManifestAsync(string rawText, IReadOnlyList<CatalogItem> catalog)
        {
            var manifest = new
            {
                version = OfflineReplayContract.ManifestVersion,
                replayId = "catalog-replay",
                createdAtUtc = CreatedAtUtc,
                catalog,
                frames = new[]
                {
                    new
                    {
                        frameId = "frame-1",
                        relativeImagePath = "frames/item.png",
                        capturedAtUtc = CreatedAtUtc.AddSeconds(1),
                        rawText,
                        candidates = Array.Empty<object>()
                    }
                }
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
