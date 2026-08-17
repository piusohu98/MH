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
    private GlobalHotkeyService? globalHotkey;
    private MarketOverlayWindow? overlayWindow;

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
        mainWindow.SourceInitialized += MainWindow_SourceInitialized;
        mainWindow.Closed += MainWindow_Closed;
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        globalHotkey?.Dispose();
        CloseOverlayWindow();
        httpClient?.Dispose();
        base.OnExit(e);
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        if (mainWindow is null)
        {
            return;
        }

        globalHotkey = new GlobalHotkeyService();
        if (!globalHotkey.TryRegister(mainWindow, ToggleOverlay))
        {
            mainWindow.SetHotkeyStatus(globalHotkey.RegistrationError ?? "悬浮窗热键注册失败。");
        }
        else
        {
            mainWindow.SetHotkeyStatus("悬浮窗热键：Ctrl+Alt+M");
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        globalHotkey?.Dispose();
        CloseOverlayWindow();
    }

    private void ToggleOverlay()
    {
        if (overlayWindow is null || !overlayWindow.IsLoaded)
        {
            overlayWindow = new MarketOverlayWindow(viewModel);
        }

        overlayWindow.Toggle();
    }

    private void CloseOverlayWindow()
    {
        if (overlayWindow is { IsLoaded: true })
        {
            overlayWindow.Close();
        }

        overlayWindow = null;
    }
}
