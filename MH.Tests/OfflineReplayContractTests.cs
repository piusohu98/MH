using MH.Core.OfflineReplay;

namespace MH.Tests;

public sealed class OfflineReplayContractTests
{
    private static readonly DateTimeOffset CreatedAtUtc = new(2026, 8, 17, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ValidManifestAcceptsRelativeSupportedImages()
    {
        var result = OfflineReplayValidator.ValidateManifest(
            new(
                OfflineReplayContract.ManifestVersion,
                "replay-1",
                CreatedAtUtc,
                [
                    new("frame-1", "screenshots/frame-1.PNG", CreatedAtUtc.AddSeconds(1)),
                    new("frame-2", "screenshots/frame-2.jpeg", CreatedAtUtc.AddSeconds(2))
                ]));

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void RejectsUnsupportedVersionMissingIdsAndNonUtcTime()
    {
        var result = OfflineReplayValidator.ValidateManifest(
            new("offline-replay-v0", " ", CreatedAtUtc.ToOffset(TimeSpan.FromHours(8)), [
                new(" ", "frame.png", default)
            ]));

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "unsupported-version");
        Assert.Contains(result.Issues, issue => issue.Code == "invalid-replay-id");
        Assert.Contains(result.Issues, issue => issue.Code == "invalid-manifest-time");
        Assert.Contains(result.Issues, issue => issue.Code == "invalid-frame-id");
        Assert.Contains(result.Issues, issue => issue.Code == "invalid-frame-time");
    }

    [Theory]
    [InlineData("../frame.png")]
    [InlineData("screenshots/../../frame.png")]
    [InlineData("C:/screenshots/frame.png")]
    [InlineData("\\\\server\\share\\frame.png")]
    [InlineData("screenshots//frame.png")]
    public void RejectsAbsoluteAndTraversalImagePaths(string path)
    {
        var result = OfflineReplayValidator.ValidateManifest(
            Manifest(new OfflineReplayFrame("frame-1", path, CreatedAtUtc)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "unsafe-image-path");
    }

    [Fact]
    public void RejectsUnsupportedImageExtension()
    {
        var result = OfflineReplayValidator.ValidateManifest(
            Manifest(new OfflineReplayFrame("frame-1", "frame.gif", CreatedAtUtc)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "unsupported-image-extension");
    }

    [Fact]
    public void RejectsDuplicateFrameIdsAndImagePathsCaseInsensitively()
    {
        var result = OfflineReplayValidator.ValidateManifest(
            Manifest(
                new OfflineReplayFrame("frame-1", "frames/one.png", CreatedAtUtc),
                new OfflineReplayFrame("FRAME-1", "frames/ONE.PNG", CreatedAtUtc.AddSeconds(1))));

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "duplicate-frame-id");
        Assert.Contains(result.Issues, issue => issue.Code == "duplicate-image-path");
    }

    [Fact]
    public void EmptyFrameListIsValidAndCanRepresentAnEmptyReplayDirectory()
    {
        var result = OfflineReplayValidator.ValidateManifest(Manifest());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void HighConfidenceConfirmedSingleCandidateIsAccepted()
    {
        var result = OfflineReplayValidator.Classify(
            "replay-1",
            new("frame-1", "frame.png", CreatedAtUtc),
            [new("item-1", "商品 1", 0.95m, true)]);

        Assert.Equal(OfflineReplayStatus.Accepted, result.Status);
        Assert.Empty(result.Issues);
    }

    [Theory]
    [InlineData(0.84, "candidate-low-confidence")]
    [InlineData(0.95, "candidate-unconfirmed")]
    public void LowConfidenceOrUnconfirmedCandidateRequiresReview(double confidence, string issueCode)
    {
        var isConfirmed = issueCode != "candidate-unconfirmed";
        var result = OfflineReplayValidator.Classify(
            "replay-1",
            new("frame-1", "frame.png", CreatedAtUtc),
            [new("item-1", "商品 1", (decimal)confidence, isConfirmed)]);

        Assert.Equal(OfflineReplayStatus.ReviewRequired, result.Status);
        Assert.Contains(result.Issues, issue => issue.Code == issueCode);
    }

    [Fact]
    public void MissingOrInvalidCandidatesAreRejected()
    {
        var missing = OfflineReplayValidator.Classify(
            "replay-1",
            new("frame-1", "frame.png", CreatedAtUtc),
            []);
        var invalid = OfflineReplayValidator.Classify(
            "replay-1",
            new("frame-1", "frame.png", CreatedAtUtc),
            [new("item-1", null, 1.1m, true)]);

        Assert.Equal(OfflineReplayStatus.Rejected, missing.Status);
        Assert.Contains(missing.Issues, issue => issue.Code == "candidate-missing");
        Assert.Equal(OfflineReplayStatus.Rejected, invalid.Status);
        Assert.Contains(invalid.Issues, issue => issue.Code == "invalid-confidence");
    }

    [Fact]
    public void DirectClassificationRejectsUnsafeImageMetadata()
    {
        var result = OfflineReplayValidator.Classify(
            "replay-1",
            new("frame-1", "../frame.gif", CreatedAtUtc),
            [new("item-1", "商品 1", 0.95m, true)]);

        Assert.Equal(OfflineReplayStatus.Rejected, result.Status);
        Assert.Contains(result.Issues, issue => issue.Code == "unsafe-image-path");
    }

    [Fact]
    public void DuplicateCandidatesAreRejectedInsteadOfSilentlyChoosingOne()
    {
        var result = OfflineReplayValidator.Classify(
            "replay-1",
            new("frame-1", "frame.png", CreatedAtUtc),
            [
                new("item-1", "商品 1", 0.95m, true),
                new("ITEM-1", "商品 1", 0.95m, true)
            ]);

        Assert.Equal(OfflineReplayStatus.Rejected, result.Status);
        Assert.Contains(result.Issues, issue => issue.Code == "duplicate-candidate-id");
    }

    private static OfflineReplayManifest Manifest(params OfflineReplayFrame[] frames)
        => new(OfflineReplayContract.ManifestVersion, "replay-1", CreatedAtUtc, frames);
}
