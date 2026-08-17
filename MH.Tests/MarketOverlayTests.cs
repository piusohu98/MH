using System.Windows;
using MH.Client;
using MH.Client.ViewModels;

namespace MH.Tests;

public sealed class MarketOverlayTests
{
    [Fact]
    public void EmptyProjectionDoesNotInventMarketDataOrScreenRecognition()
    {
        var projection = MarketOverlayProjection.Empty;

        Assert.Equal(MarketOverlayDataState.NoSnapshot, projection.State);
        Assert.Equal("—", projection.ServerName);
        Assert.Equal("—", projection.ItemName);
        Assert.Equal("数据不足", projection.ReferencePriceText);
        Assert.Contains("未接入屏幕 OCR", projection.SafetyText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, MarketViewState.Idle, false, MarketOverlayDataState.NoSnapshot)]
    [InlineData(true, MarketViewState.Ready, false, MarketOverlayDataState.Ready)]
    [InlineData(true, MarketViewState.Offline, false, MarketOverlayDataState.Offline)]
    [InlineData(true, MarketViewState.Ready, true, MarketOverlayDataState.Stale)]
    [InlineData(true, MarketViewState.Offline, true, MarketOverlayDataState.Stale)]
    public void StateResolverFailsClosedForMissingOrStaleSnapshots(
        bool hasSnapshot,
        MarketViewState state,
        bool isStale,
        MarketOverlayDataState expected)
    {
        Assert.Equal(
            expected,
            MarketOverlayProjection.ResolveDataState(hasSnapshot, state, isStale));
    }

    [Fact]
    public void CtrlAltMUsesExpectedGlobalHotkeyModifiers()
    {
        Assert.Equal(0x0003u, GlobalHotkeyGesture.CtrlAltM.Modifiers);
        Assert.Equal(0x4Du, GlobalHotkeyGesture.CtrlAltM.VirtualKey);
    }

    [Fact]
    public void OverlayPositionStaysInsideNegativeCoordinateWorkArea()
    {
        var position = OverlayPositionCalculator.Calculate(
            new Rect(-1920, 0, 1920, 1080),
            new Size(420, 260),
            24);

        Assert.Equal(-444, position.Left);
        Assert.Equal(796, position.Top);
        Assert.True(position.Right <= 0);
        Assert.True(position.Bottom <= 1080);
    }
}
