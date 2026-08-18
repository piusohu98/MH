using System.Text.RegularExpressions;

namespace MH.Core.Ocr;

public static partial class OcrDatasetValidator
{
    private static readonly IReadOnlySet<string> LabelKinds =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "positive",
            "auxiliary",
            "negative"
        };

    public static OcrDatasetValidationResult Validate(OcrDatasetManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var issues = new List<OcrDatasetIssue>();
        if (!string.Equals(manifest.Version, OcrDatasetContract.Version, StringComparison.Ordinal))
        {
            issues.Add(new("unsupported-version", $"Dataset version must be {OcrDatasetContract.Version}."));
        }

        ValidateIdentifier(manifest.DatasetId, "dataset-id", issues);
        if (string.IsNullOrWhiteSpace(manifest.SourceKind))
        {
            issues.Add(new("source-kind-missing", "Dataset source kind must be provided."));
        }

        if (manifest.Samples is null)
        {
            issues.Add(new("samples-missing", "Dataset samples must be provided."));
            return new(issues);
        }

        var sampleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sample in manifest.Samples)
        {
            if (sample is null)
            {
                issues.Add(new("sample-missing", "Dataset cannot contain a null sample."));
                continue;
            }

            ValidateIdentifier(sample.SampleId, "sample-id", issues);
            if (!string.IsNullOrEmpty(sample.SampleId) && !sampleIds.Add(sample.SampleId))
            {
                issues.Add(new("duplicate-sample-id", $"Sample id '{sample.SampleId}' occurs more than once."));
            }

            if (!TryNormalizeRelativePath(sample.RelativeImagePath, out var normalizedPath))
            {
                issues.Add(new("unsafe-image-path", $"Image path '{sample.RelativeImagePath}' must be a normalized relative path."));
            }
            else
            {
                if (!paths.Add(normalizedPath))
                {
                    issues.Add(new("duplicate-image-path", $"Image path '{sample.RelativeImagePath}' occurs more than once."));
                }

                if (!OcrDatasetContract.SupportedImageExtensions.Contains(Path.GetExtension(normalizedPath)))
                {
                    issues.Add(new("unsupported-image-extension", $"Image path '{sample.RelativeImagePath}' has an unsupported extension."));
                }
            }

            if (!Sha256Pattern().IsMatch(sample.Sha256 ?? string.Empty))
            {
                issues.Add(new("invalid-sha256", $"Sample '{sample.SampleId}' must contain a 64-character SHA-256 hash."));
            }

            if (string.IsNullOrWhiteSpace(sample.PageType))
            {
                issues.Add(new("page-type-missing", $"Sample '{sample.SampleId}' must contain a page type."));
            }

            if (!LabelKinds.Contains(sample.LabelKind))
            {
                issues.Add(new("invalid-label-kind", $"Sample '{sample.SampleId}' has an unsupported label kind."));
            }

            if (string.IsNullOrWhiteSpace(sample.Source))
            {
                issues.Add(new("source-missing", $"Sample '{sample.SampleId}' must contain a source."));
            }

            if (string.IsNullOrWhiteSpace(sample.DuplicateGroupId))
            {
                issues.Add(new("duplicate-group-missing", $"Sample '{sample.SampleId}' must contain a duplicate group id."));
            }

            if (sample.LabelKind == "positive")
            {
                if (!sample.RecommendedForOcr)
                {
                    issues.Add(new("positive-not-recommended", $"Positive sample '{sample.SampleId}' must be recommended for OCR."));
                }

                if (string.IsNullOrWhiteSpace(sample.VisibleItemText)
                    || string.IsNullOrWhiteSpace(sample.VisiblePriceText))
                {
                    issues.Add(new("positive-label-missing", $"Positive sample '{sample.SampleId}' must contain item and price labels."));
                }
            }

            if (sample.RecommendedForOcr
                && (sample.LabelKind != "positive" || !string.Equals(sample.PageType, "MarketList", StringComparison.Ordinal)))
            {
                issues.Add(new("unsafe-ocr-recommendation", $"Sample '{sample.SampleId}' is recommended for OCR but is not a positive MarketList sample."));
            }
        }

        return new(issues);
    }

    private static void ValidateIdentifier(string? value, string code, ICollection<OcrDatasetIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value != value.Trim()
            || value.Length > 128
            || value.Any(char.IsControl)
            || value.Contains('/')
            || value.Contains('\\'))
        {
            issues.Add(new($"invalid-{code}", $"{code} must be a non-empty path-free identifier."));
        }
    }

    private static bool TryNormalizeRelativePath(string? relativePath, out string normalizedPath)
    {
        normalizedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(relativePath) || relativePath != relativePath.Trim())
        {
            return false;
        }

        if (relativePath[0] is '/' or '\\'
            || Path.IsPathRooted(relativePath)
            || relativePath.Contains(':'))
        {
            return false;
        }

        var segments = relativePath.Replace('\\', '/').Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment is "." or ".." || segment.Any(char.IsControl)))
        {
            return false;
        }

        normalizedPath = string.Join('/', segments);
        return true;
    }

    [GeneratedRegex("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();
}
