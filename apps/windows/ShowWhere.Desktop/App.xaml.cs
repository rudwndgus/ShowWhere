using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using ShowWhere.ApiClient;
using ShowWhere.Core;
using ShowWhere.Overlay;
using ShowWhere.WindowsAutomation;

namespace ShowWhere.Desktop;

public partial class App : Application
{
    private HttpClient? _httpClient;
    private HighlightOverlayWindow? _overlay;
    private GuidancePanelWindow? _panel;
    private FloatingAssistantWindow? _assistant;
    private DeveloperTeachingWindow? _teachingWindow;

    protected override void OnStartup(StartupEventArgs eventArgs)
    {
        base.OnStartup(eventArgs);
        _httpClient = new HttpClient();
        var observer = new WindowsUiObserver();
        var monitor = new WindowsChangeMonitor(observer);
        var screenCapture = new WindowsScreenCaptureService();
        _overlay = new HighlightOverlayWindow();
        var apiClient = new GuideApiClient(_httpClient, GuideApiClientOptions.FromEnvironment());
        var teachingClient = new TeachingApiClient(_httpClient, GuideApiClientOptions.FromEnvironment());
        var correctionSelection = new DeveloperRegionSelectionService();
        var correctionStore = new JsonlDeveloperCorrectionStore();
        var viewModel = new GuidanceViewModel(
            observer,
            monitor,
            screenCapture,
            apiClient,
            teachingClient,
            _overlay,
            correctionSelection,
            correctionStore,
            Shutdown);
        _panel = new GuidancePanelWindow { DataContext = viewModel };
        _teachingWindow = new DeveloperTeachingWindow { DataContext = viewModel };
        viewModel.TeachingEditorRequested += () =>
        {
            _teachingWindow.Show();
            _teachingWindow.Activate();
        };
        viewModel.TeachingEditorClosed += () => _teachingWindow.Hide();
        _panel.Deactivated += (_, _) => _panel.Dispatcher.BeginInvoke(
            observer.RememberCurrentForegroundWindow,
            DispatcherPriority.Background);
        _assistant = new FloatingAssistantWindow(
            _panel,
            new AssistantPositionStore(),
            observer.RememberCurrentForegroundWindow)
        {
            DataContext = viewModel,
        };
        viewModel.TargetHighlighted += _panel.MoveAwayFrom;
        viewModel.CorrectionSelectionStarted += () =>
        {
            observer.RememberCurrentForegroundWindow();
            _panel.Hide();
            _assistant.Hide();
            _teachingWindow.Hide();
        };
        viewModel.CorrectionSelectionCompleted += () =>
        {
            _assistant.Show();
            _panel.Show();
            _panel.Activate();
            if (viewModel.IsCorrectionEditorVisible)
            {
                _teachingWindow.Show();
                _teachingWindow.Activate();
            }
        };
        MainWindow = _assistant;
        _assistant.Show();
    }

    protected override void OnExit(ExitEventArgs eventArgs)
    {
        _panel?.CloseForShutdown();
        _teachingWindow?.CloseForShutdown();
        _overlay?.Close();
        _httpClient?.Dispose();
        base.OnExit(eventArgs);
    }
}
