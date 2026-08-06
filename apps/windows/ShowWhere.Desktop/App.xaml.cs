using System.Net.Http;
using System.Windows;
using ShowWhere.ApiClient;
using ShowWhere.Overlay;
using ShowWhere.WindowsAutomation;

namespace ShowWhere.Desktop;

public partial class App : Application
{
    private HttpClient? _httpClient;
    private HighlightOverlayWindow? _overlay;
    private GuidancePanelWindow? _panel;
    private FloatingAssistantWindow? _assistant;

    protected override void OnStartup(StartupEventArgs eventArgs)
    {
        base.OnStartup(eventArgs);
        _httpClient = new HttpClient();
        var observer = new WindowsUiObserver();
        var monitor = new WindowsChangeMonitor(observer);
        _overlay = new HighlightOverlayWindow();
        var apiClient = new GuideApiClient(_httpClient, GuideApiClientOptions.FromEnvironment());
        var viewModel = new GuidanceViewModel(observer, monitor, apiClient, _overlay, Shutdown);
        _panel = new GuidancePanelWindow { DataContext = viewModel };
        _assistant = new FloatingAssistantWindow(_panel, new AssistantPositionStore())
        {
            DataContext = viewModel,
        };
        viewModel.TargetHighlighted += _panel.MoveAwayFrom;
        MainWindow = _assistant;
        _assistant.Show();
    }

    protected override void OnExit(ExitEventArgs eventArgs)
    {
        _panel?.CloseForShutdown();
        _overlay?.Close();
        _httpClient?.Dispose();
        base.OnExit(eventArgs);
    }
}
