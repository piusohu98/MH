using MH.Core.Ocr;

namespace MH.Tests;

public sealed class OcrDatasetTests
{
    [Fact]
    public void ValidManifestPasses()
    {
        var manifest = new OcrDatasetManifest(
            OcrDatasetContract.Version,
            "fixture",
            "public-web-debug-only",
            [PositiveSample()]);

        var result = OcrDatasetValidator.Validate(manifest);

        Assert.True(result.IsValid, string.Join("; ", result.Issues.Select(issue => issue.Detail)));
    }

    [Fact]
    public void PositiveSampleRequiresMarketLabels()
    {
        var sample = PositiveSample() with
        {
            PageType = "Shop",
            VisiblePriceText = null
        };
        var result = OcrDatasetValidator.Validate(
            new OcrDatasetManifest(OcrDatasetContract.Version, "fixture", "public-web-debug-only", [sample]));

        Assert.Contains(result.Issues, issue => issue.Code == "positive-label-missing");
        Assert.Contains(result.Issues, issue => issue.Code == "unsafe-ocr-recommendation");
    }

    [Fact]
    public void UnsafePathAndHashAreRejected()
    {
        var sample = PositiveSample() with
        {
            RelativeImagePath = "../outside.png",
            Sha256 = "not-a-hash"
        };
        var result = OcrDatasetValidator.Validate(
            new OcrDatasetManifest(OcrDatasetContract.Version, "fixture", "public-web-debug-only", [sample]));

        Assert.Contains(result.Issues, issue => issue.Code == "unsafe-image-path");
        Assert.Contains(result.Issues, issue => issue.Code == "invalid-sha256");
    }

    [Fact]
    public void RecommendedAuxiliarySampleIsRejected()
    {
        var sample = PositiveSample() with
        {
            LabelKind = "auxiliary",
            RecommendedForOcr = true
        };
        var result = OcrDatasetValidator.Validate(
            new OcrDatasetManifest(OcrDatasetContract.Version, "fixture", "public-web-debug-only", [sample]));

        Assert.Contains(result.Issues, issue => issue.Code == "unsafe-ocr-recommendation");
    }

    [Fact]
    public void DuplicateSampleIdsAreRejected()
    {
        var first = PositiveSample();
        var second = PositiveSample() with
        {
            RelativeImagePath = "other.png",
            Sha256 = new string('b', 64),
            DuplicateGroupId = new string('b', 64)
        };
        var result = OcrDatasetValidator.Validate(
            new OcrDatasetManifest(OcrDatasetContract.Version, "fixture", "public-web-debug-only", [first, second]));

        Assert.Contains(result.Issues, issue => issue.Code == "duplicate-sample-id");
    }

    private static OcrDatasetSample PositiveSample()
        => new(
            "sample-aaaaaaaaaaaaaaaa",
            "screenshots/item.png",
            new string('a', 64),
            "MarketList",
            "positive",
            true,
            "金香玉",
            "656|729",
            "fixture",
            null,
            new string('a', 64));
}
