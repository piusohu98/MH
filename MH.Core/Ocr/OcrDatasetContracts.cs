namespace MH.Core.Ocr;

public static class OcrDatasetContract
{
    public const string Version = "ocr-dataset-v1";

    public static IReadOnlySet<string> SupportedImageExtensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".bmp",
            ".jpeg",
            ".jpg",
            ".png"
        };
}

public sealed record OcrDatasetManifest(
    string Version,
    string DatasetId,
    string SourceKind,
    IReadOnlyList<OcrDatasetSample> Samples);

public sealed record OcrDatasetSample(
    string SampleId,
    string RelativeImagePath,
    string Sha256,
    string PageType,
    string LabelKind,
    bool RecommendedForOcr,
    string? VisibleItemText,
    string? VisiblePriceText,
    string Source,
    string? Notes,
    string DuplicateGroupId);

public sealed record OcrDatasetIssue(string Code, string Detail);

public sealed record OcrDatasetValidationResult(IReadOnlyList<OcrDatasetIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
}
