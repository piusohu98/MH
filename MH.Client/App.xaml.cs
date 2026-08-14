using System.Net.Http;
using System.Windows;
using MH.Client.Api;
using MH.Client.ViewModels;

namespace MH.Client;

public partial class App : Application
{
    public static Uri DefaultServerBaseAddress { get; } = new("http://localhost:5002/");

    private HttpClient? httpClient;
    private HttpMarketApiClient apiClient = null!;
    private FirstScreenViewModel viewModel = null!;
    private MainWindow? mainWindow;

    public static Uri ResolveServerBaseAddress(string? value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var candidate)
            || candidate.Scheme is not ("http" or "https")
            || candidate.AbsolutePath != "/"
            || !string.IsNullOrEmpty(candidate.Query)
            || !string.IsNullOrEmpty(candidate.Fragment))
        {
            return DefaultServerBaseAddress;
        }

        var builder = new UriBuilder(candidate);
        builder.Path = "/";

        return builder.Uri;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        httpClient = new HttpClient
        {
            BaseAddress = ResolveServerBaseAddress(Environment.GetEnvironmentVariable("MH_SERVER_BASE_URL"))
        };
        apiClient = new HttpMarketApiClient(httpClient);
        viewModel = new FirstScreenViewModel(apiClient);
        mainWindow = new MainWindow(viewModel);
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        httpClient?.Dispose();
        base.OnExit(e);
    }
}
