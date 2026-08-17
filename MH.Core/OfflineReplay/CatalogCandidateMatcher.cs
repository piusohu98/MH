using System.Globalization;
using System.Text;
using MH.Core.Models;

namespace MH.Core.OfflineReplay;

public static class CatalogCandidateMatcher
{
    public const decimal ExactMatchConfidence = 1.00m;
    public const decimal ContainsMatchConfidence = 0.80m;

    public static CatalogCandidateMatchResult Match(
        string? rawText,
        IReadOnlyList<Item>? catalog,
        decimal minimumConfidence = OfflineReplayContract.DefaultMinimumConfidence)
    {
        ValidateMinimumConfidence(minimumConfidence);

        var normalizedText = Normalize(rawText);
        var issues = new List<OfflineReplayIssue>();
        if (normalizedText.Length == 0)
        {
            issues.Add(new("match-empty-input", "OCR text must contain searchable characters."));
            return Result(rawText, normalizedText, OfflineReplayStatus.Rejected, [], issues);
        }

        if (catalog is null || catalog.Count == 0)
        {
            issues.Add(new("catalog-empty", "The item catalog must contain at least one item."));
            return Result(rawText, normalizedText, OfflineReplayStatus.Rejected, [], issues);
        }

        var entries = new List<CatalogEntry>(catalog.Count);
        var itemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in catalog)
        {
            if (item is null
                || string.IsNullOrWhiteSpace(item.Id)
                || item.Id != item.Id.Trim())
            {
                issues.Add(new("catalog-invalid-item", "Catalog items must have a non-empty trimmed id."));
                continue;
            }

            if (!itemIds.Add(item.Id))
            {
                issues.Add(new("catalog-duplicate-item-id", $"Catalog item id '{item.Id}' occurs more than once."));
            }

            var normalizedName = Normalize(item.Name);
            if (normalizedName.Length == 0)
            {
                issues.Add(new("catalog-invalid-item", $"Catalog item '{item.Id}' must have a searchable name."));
                continue;
            }

            entries.Add(new(item, normalizedName));
        }

        var exactMatches = entries
            .Where(entry => string.Equals(entry.NormalizedName, normalizedText, StringComparison.Ordinal))
            .OrderBy(entry => entry.NormalizedName, StringComparer.Ordinal)
            .ThenBy(entry => entry.Item.Id, StringComparer.Ordinal)
            .ToArray();

        var matchKind = MatchKind.Exact;
        IReadOnlyList<CatalogEntry> matches = exactMatches;
        if (matches.Count == 0)
        {
            var containsMatches = entries
                .Where(entry => entry.NormalizedName.Contains(normalizedText, StringComparison.Ordinal)
                    || normalizedText.Contains(entry.NormalizedName, StringComparison.Ordinal))
                .OrderBy(entry => GetMatchKind(entry.NormalizedName, normalizedText))
                .ThenBy(entry => GetSpecificityLength(entry.NormalizedName, normalizedText))
                .ThenBy(entry => entry.NormalizedName, StringComparer.Ordinal)
                .ThenBy(entry => entry.Item.Id, StringComparer.Ordinal)
                .ToArray();
            matches = containsMatches;
            matchKind = MatchKind.Contains;
        }

        if (matches.Count == 0)
        {
            issues.Add(new("match-not-found", "OCR text did not match any catalog item name."));
            return Result(rawText, normalizedText, OfflineReplayStatus.Rejected, [], issues);
        }

        var uniqueMatch = matches.Count == 1 && issues.Count == 0;
        var confidence = matchKind == MatchKind.Exact
            ? ExactMatchConfidence
            : ContainsMatchConfidence;
        var candidates = matches
            .Select(match => new OfflineReplayCandidate(
                match.Item.Id,
                match.Item.Name,
                confidence,
                uniqueMatch))
            .ToArray();

        if (matches.Count > 1)
        {
            issues.Add(new("match-ambiguous", "More than one catalog item matches the OCR text."));
        }

        if (confidence < minimumConfidence)
        {
            issues.Add(new(
                "match-low-confidence",
                $"The best catalog match confidence must reach {minimumConfidence:0.##} for automatic acceptance."));
        }

        var status = matches.Count == 1
            && uniqueMatch
            && confidence >= minimumConfidence
            ? OfflineReplayStatus.Accepted
            : OfflineReplayStatus.ReviewRequired;
        return Result(rawText, normalizedText, status, candidates, issues);
    }

    public static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var normalized = value.Normalize(NormalizationForm.FormKC);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (char.IsWhiteSpace(character))
            {
                continue;
            }

            var category = char.GetUnicodeCategory(character);
            if (category is UnicodeCategory.Control
                or UnicodeCategory.Format
                or UnicodeCategory.PrivateUse
                or UnicodeCategory.Surrogate
                or UnicodeCategory.ConnectorPunctuation
                or UnicodeCategory.DashPunctuation
                or UnicodeCategory.ClosePunctuation
                or UnicodeCategory.FinalQuotePunctuation
                or UnicodeCategory.InitialQuotePunctuation
                or UnicodeCategory.OtherPunctuation
                or UnicodeCategory.OpenPunctuation
                or UnicodeCategory.MathSymbol
                or UnicodeCategory.CurrencySymbol
                or UnicodeCategory.ModifierSymbol
                or UnicodeCategory.OtherSymbol)
            {
                continue;
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }

    private static int GetMatchKind(string normalizedName, string normalizedText)
        => normalizedName.Contains(normalizedText, StringComparison.Ordinal) ? 0 : 1;

    private static int GetSpecificityLength(string normalizedName, string normalizedText)
        => normalizedName.Contains(normalizedText, StringComparison.Ordinal)
            ? normalizedName.Length
            : -normalizedName.Length;

    private static CatalogCandidateMatchResult Result(
        string? rawText,
        string normalizedText,
        OfflineReplayStatus status,
        IReadOnlyList<OfflineReplayCandidate> candidates,
        IReadOnlyList<OfflineReplayIssue> issues)
        => new(rawText, normalizedText, status, candidates, issues);

    private static void ValidateMinimumConfidence(decimal minimumConfidence)
    {
        if (minimumConfidence is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumConfidence), "Minimum confidence must be between 0 and 1.");
        }
    }

    private sealed record CatalogEntry(Item Item, string NormalizedName);

    private enum MatchKind
    {
        Exact = 0,
        Contains = 1
    }
}

public sealed record CatalogCandidateMatchResult(
    string? RawText,
    string NormalizedText,
    OfflineReplayStatus Status,
    IReadOnlyList<OfflineReplayCandidate> Candidates,
    IReadOnlyList<OfflineReplayIssue> Issues)
{
    public bool IsAccepted => Status == OfflineReplayStatus.Accepted;

    public bool RequiresReview => Status == OfflineReplayStatus.ReviewRequired;
}
