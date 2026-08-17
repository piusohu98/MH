using System.Windows;
using MH.Client.ViewModels;

namespace MH.Client;

public partial class MarketOverlayWindow : Window
{
    private readonly FirstScreenViewModel viewModel;

    public MarketOverlayWindow(FirstScreenViewModel viewModel)
    {
        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        RefreshProjection();
    }

    public void Toggle()
    {
        if (IsVisible)
        {
            Hide();
            return;
        }

        RefreshProjection();
        Show();
        UpdateLayout();
        var position = OverlayPositionCalculator.Calculate(
            SystemParameters.WorkArea,
            new Size(ActualWidth, ActualHeight),
            24);
        Left = position.Left;
        Top = position.Top;
    }

    protected override void OnClosed(EventArgs e)
    {
        viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        base.OnClosed(e);
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

public static class OverlayPositionCalculator
{
    public static Rect Calculate(Rect workArea, Size windowSize, double margin)
    {
        var left = Math.Max(workArea.Left, workArea.Right - windowSize.Width - margin);
        var top = Math.Max(workArea.Top, workArea.Bottom - windowSize.Height - margin);
        return new Rect(left, top, windowSize.Width, windowSize.Height);
    }
}
