using MH.Core.Models;
using MH.Core.OfflineReplay;

namespace MH.Tests;

public sealed class OfflineReplayCandidateMatcherTests
{
    [Fact]
    public void NormalizesChinesePunctuationWhitespaceFullWidthTextAndCase()
    {
        var result = CatalogCandidateMatcher.Match(
            "　ＤＥＭＯ－Ore　０１！",
            [Item("item-1", "DEMO Ore 01")]);

        Assert.True(result.IsAccepted);
        Assert.Equal("demoore01", result.NormalizedText);
        Assert.Equal("item-1", Assert.Single(result.Candidates).ItemId);
        Assert.Equal(CatalogCandidateMatcher.ExactMatchConfidence, result.Candidates[0].Confidence);
    }

    [Fact]
    public void ExactMatchesWinAndLowerPriorityContainsMatchesAreNotReturned()
    {
        var result = CatalogCandidateMatcher.Match(
            "铜矿",
            [
                Item("item-long", "高级铜矿"),
                Item("item-exact", "铜矿"),
                Item("item-short", "铜")
            ]);

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal("item-exact", candidate.ItemId);
        Assert.Equal(OfflineReplayStatus.Accepted, result.Status);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void ContainsMatchesAreDeterministicAndKeepBothDirections()
    {
        var catalog = new[]
        {
            Item("item-short", "铜"),
            Item("item-long", "高级铜矿"),
            Item("item-middle", "铜矿")
        };

        var first = CatalogCandidateMatcher.Match("高级铜矿石", catalog);
        var second = CatalogCandidateMatcher.Match("高级铜矿石", catalog);

        Assert.Equal(OfflineReplayStatus.ReviewRequired, first.Status);
        Assert.Contains(first.Issues, issue => issue.Code == "match-ambiguous");
        Assert.Equal(
            new[] { "item-long", "item-middle", "item-short" },
            first.Candidates.Select(candidate => candidate.ItemId));
        Assert.Equal(
            first.Candidates.Select(candidate => candidate.ItemId),
            second.Candidates.Select(candidate => candidate.ItemId));
        Assert.All(first.Candidates, candidate =>
            Assert.Equal(CatalogCandidateMatcher.ContainsMatchConfidence, candidate.Confidence));
    }

    [Fact]
    public void SameNormalizedNameReturnsAllCandidatesInsteadOfChoosingOne()
    {
        var result = CatalogCandidateMatcher.Match(
            "灵药",
            [Item("item-a", "灵 药"), Item("item-b", "灵-药")]);

        Assert.Equal(OfflineReplayStatus.ReviewRequired, result.Status);
        Assert.Equal(new[] { "item-a", "item-b" }, result.Candidates.Select(candidate => candidate.ItemId));
        Assert.All(result.Candidates, candidate => Assert.False(candidate.IsConfirmed));
        Assert.Contains(result.Issues, issue => issue.Code == "match-ambiguous");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("　！！！")]
    public void EmptyInputIsRejectedWithoutCandidates(string? rawText)
    {
        var result = CatalogCandidateMatcher.Match(rawText, [Item("item-1", "灵药")]);

        Assert.Equal(OfflineReplayStatus.Rejected, result.Status);
        Assert.Empty(result.Candidates);
        Assert.Contains(result.Issues, issue => issue.Code == "match-empty-input");
    }

    [Fact]
    public void NoMatchAndEmptyCatalogAreRejected()
    {
        var noMatch = CatalogCandidateMatcher.Match("不存在", [Item("item-1", "灵药")]);
        var noCatalog = CatalogCandidateMatcher.Match("灵药", []);

        Assert.Equal(OfflineReplayStatus.Rejected, noMatch.Status);
        Assert.Contains(noMatch.Issues, issue => issue.Code == "match-not-found");
        Assert.Equal(OfflineReplayStatus.Rejected, noCatalog.Status);
        Assert.Contains(noCatalog.Issues, issue => issue.Code == "catalog-empty");
    }

    [Fact]
    public void ContainsConfidenceThresholdIsInclusiveAtTheBoundary()
    {
        var accepted = CatalogCandidateMatcher.Match(
            "灵",
            [Item("item-1", "灵药")],
            CatalogCandidateMatcher.ContainsMatchConfidence);
        var review = CatalogCandidateMatcher.Match(
            "灵",
            [Item("item-1", "灵药")],
            CatalogCandidateMatcher.ContainsMatchConfidence + 0.0001m);

        Assert.Equal(OfflineReplayStatus.Accepted, accepted.Status);
        Assert.Empty(accepted.Issues);
        Assert.Equal(OfflineReplayStatus.ReviewRequired, review.Status);
        Assert.Contains(review.Issues, issue => issue.Code == "match-low-confidence");
    }

    [Fact]
    public void ExactConfidenceCanReachOneAndInvalidThresholdIsRejected()
    {
        var result = CatalogCandidateMatcher.Match(
            "灵药",
            [Item("item-1", "灵药")],
            1m);

        Assert.Equal(OfflineReplayStatus.Accepted, result.Status);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CatalogCandidateMatcher.Match("灵药", [Item("item-1", "灵药")], 1.01m));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CatalogCandidateMatcher.Match("灵药", [Item("item-1", "灵药")], -0.01m));
    }

    [Fact]
    public void InvalidCatalogEntryPreventsSilentAcceptance()
    {
        var result = CatalogCandidateMatcher.Match(
            "灵药",
            [Item("item-1", "灵药"), Item("item-2", "！！！")]);

        Assert.Equal(OfflineReplayStatus.ReviewRequired, result.Status);
        Assert.Single(result.Candidates);
        Assert.False(result.Candidates[0].IsConfirmed);
        Assert.Contains(result.Issues, issue => issue.Code == "catalog-invalid-item");
    }

    private static Item Item(string id, string name)
        => new()
        {
            Id = id,
            Name = name,
            Category = "材料",
            Unit = "个",
            CatalogKind = CatalogKind.Demo
        };
}
