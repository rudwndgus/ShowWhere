using System.Net.Http;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using ShowWhere.ApiClient;
using ShowWhere.Core;
using ShowWhere.Overlay;
using ShowWhere.WindowsAutomation;

namespace ShowWhere.Desktop;

public partial class App : Application
{
    private const string SingleInstanceMutexName = "Local\\ShowWhere.Desktop.SingleInstance.v1";
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private HttpClient? _httpClient;
    private HighlightOverlayWindow? _overlay;
    private GuidancePanelWindow? _panel;
    private FloatingAssistantWindow? _assistant;
    private MobileRemoteCoordinator? _mobileRemote;
    private CentralLearningClient? _centralLearning;

    protected override void OnStartup(StartupEventArgs eventArgs)
    {
        if (ProductionUpdateService.TryRunInstaller(eventArgs.Args))
        {
            Shutdown();
            return;
        }
        _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Shutdown();
            return;
        }
        _ownsSingleInstanceMutex = true;
        base.OnStartup(eventArgs);
        _httpClient = new HttpClient();
        var observer = new WindowsUiObserver();
        var monitor = new WindowsChangeMonitor(
            observer,
            trigger => DesktopDiagnostics.WriteEvent(
                "target_interaction_monitor",
                ("trigger", trigger)));
        var screenCapture = new WindowsScreenCaptureService();
        _overlay = new HighlightOverlayWindow();
        var apiOptions = GuideApiClientOptions.FromEnvironment();
        var apiClient = new GuideApiClient(_httpClient, apiOptions);
        var correctionSelection = new DeveloperRegionSelectionService();
        var trainingDirectory = JsonlDeveloperCorrectionStore.ResolveDefaultDataDirectory();
        PackagedTrainingSeeder.Seed(trainingDirectory);
        DesktopDiagnostics.WriteEvent("training_store_ready", ("path", trainingDirectory));
        var localCorrectionStore = new JsonlDeveloperCorrectionStore(trainingDirectory);
        _centralLearning = new CentralLearningClient(_httpClient, apiOptions, trainingDirectory);
        _centralLearning.Start(localCorrectionStore);
        IDeveloperCorrectionStore correctionStore = new SynchronizedDeveloperCorrectionStore(localCorrectionStore, _centralLearning);
        _mobileRemote = new MobileRemoteCoordinator(_httpClient, apiOptions, Dispatcher);
        var viewModel = new GuidanceViewModel(
            observer,
            monitor,
            screenCapture,
            apiClient,
            _overlay,
            correctionSelection,
            correctionStore,
            Shutdown,
            _mobileRemote);
        _mobileRemote.Attach(viewModel);
        var characterStore = new AssistantCharacterStore();
        _panel = new GuidancePanelWindow(characterStore) { DataContext = viewModel };
        _panel.Deactivated += (_, _) => _panel.Dispatcher.BeginInvoke(
            observer.RememberCurrentForegroundWindow,
            DispatcherPriority.Background);
        _assistant = new FloatingAssistantWindow(
            _panel,
            new AssistantPositionStore(),
            characterStore,
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
        };
        viewModel.CorrectionSelectionCompleted += () =>
        {
            _assistant.Show();
            _panel.Show();
            _panel.Activate();
        };
        MainWindow = _assistant;
        _assistant.Show();
        _ = ProductionUpdateService.CheckAndPromptAsync(
            apiOptions.Endpoint,
            !string.IsNullOrWhiteSpace(apiOptions.ClientToken));
    }

    protected override void OnExit(ExitEventArgs eventArgs)
    {
        _panel?.CloseForShutdown();
        _overlay?.Close();
        _mobileRemote?.Dispose();
        _centralLearning?.Dispose();
        _httpClient?.Dispose();
        if (_ownsSingleInstanceMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
            _ownsSingleInstanceMutex = false;
        }
        _singleInstanceMutex?.Dispose();
        base.OnExit(eventArgs);
    }
}
