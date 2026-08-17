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
        var calculated = OverlayPositionCalculator.TryCalculate(
            new Rect(-1920, 0, 1920, 1080),
            new Size(420, 260),
            24,
            out var position);

        Assert.True(calculated);
        Assert.Equal(-444, position.X);
        Assert.Equal(796, position.Y);
    }

    [Theory]
    [InlineData(96u, 24)]
    [InlineData(120u, 30)]
    [InlineData(144u, 36)]
    [InlineData(192u, 48)]
    public void OverlayMarginScalesToPerMonitorDpi(uint dpi, double expectedPixels)
    {
        Assert.True(OverlayPositionCalculator.TryScaleMargin(24, dpi, out var marginPixels));
        Assert.Equal(expectedPixels, marginPixels);
    }

    [Fact]
    public void OverlayPositionUsesSelectedMonitorWorkAreaWithoutPrimaryFallback()
    {
        var calculated = OverlayPositionCalculator.TryCalculate(
            new Rect(1920, -100, 2560, 1400),
            new Size(495, 390),
            36,
            out var position);

        Assert.True(calculated);
        Assert.Equal(3949, position.X);
        Assert.Equal(874, position.Y);
    }

    [Fact]
    public void OverlayPositionFailsClosedWhenWindowCannotFit()
    {
        Assert.False(OverlayPositionCalculator.TryCalculate(
            new Rect(0, 0, 320, 200),
            new Size(330, 180),
            24,
            out _));
    }

    [Fact]
    public void OverlayPositionRejectsInvalidMarginAndDpi()
    {
        Assert.False(OverlayPositionCalculator.TryCalculate(
            new Rect(0, 0, 1920, 1080),
            new Size(330, 240),
            -1,
            out _));
        Assert.False(OverlayPositionCalculator.TryScaleMargin(24, 0, out _));
        Assert.False(OverlayPositionCalculator.TryScaleMargin(double.NaN, 144, out _));
    }
}
