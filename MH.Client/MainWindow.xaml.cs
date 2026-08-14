using System.Windows;
using MH.Client.ViewModels;

namespace MH.Client;

public partial class MainWindow : Window
{
    private readonly FirstScreenViewModel viewModel;
    private bool initialized;

    public MainWindow(FirstScreenViewModel viewModel)
    {
        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = viewModel;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        await viewModel.InitializeAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        => await viewModel.RefreshAsync();

    private async void InitializeButton_Click(object sender, RoutedEventArgs e)
        => await viewModel.InitializeAsync();
}
