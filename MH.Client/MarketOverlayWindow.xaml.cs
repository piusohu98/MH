using System.Windows;
using System.Windows.Threading;
using MH.Client.ViewModels;

namespace MH.Client;

public partial class MarketOverlayWindow : Window
{
    private readonly FirstScreenViewModel viewModel;
    private readonly Action<bool>? placementCompleted;
    private long placementGeneration;

    public MarketOverlayWindow(
        FirstScreenViewModel viewModel,
        Action<bool>? placementCompleted = null)
    {
        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        this.placementCompleted = placementCompleted;
        InitializeComponent();
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        RefreshProjection();
    }

    public void Toggle()
    {
        if (IsVisible)
        {
            placementGeneration++;
            Opacity = 1;
            Hide();
            return;
        }

        var generation = ++placementGeneration;
        RefreshProjection();
        Opacity = 0;
        Show();
        UpdateLayout();
        if (!OverlayMonitorPositioner.TryAcquireAndPosition(this, 24, out var target))
        {
            FailPlacement(generation);
            return;
        }

        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            () => CompletePlacement(generation, target));
    }

    protected override void OnClosed(EventArgs e)
    {
        placementGeneration++;
        viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        base.OnClosed(e);
    }

    private void CompletePlacement(long generation, OverlayMonitorTarget target)
    {
        if (generation != placementGeneration || !IsVisible)
        {
            return;
        }

        UpdateLayout();
        if (!OverlayMonitorPositioner.TryPosition(this, target, 24))
        {
            FailPlacement(generation);
            return;
        }

        Opacity = 1;
        placementCompleted?.Invoke(true);
    }

    private void FailPlacement(long generation)
    {
        if (generation != placementGeneration)
        {
            return;
        }

        Opacity = 1;
        Hide();
        placementCompleted?.Invoke(false);
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (Dispatcher.CheckAccess())
        {
            RefreshProjection();
        }
        else
        {
            _ = Dispatcher.BeginInvoke(RefreshProjection);
        }
    }

    private void RefreshProjection()
        => DataContext = MarketOverlayProjection.From(viewModel);
}
