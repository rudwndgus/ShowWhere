using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ShowWhere.ApiClient;
using ShowWhere.Core;
using ShowWhere.Overlay;
using ShowWhere.WindowsAutomation;

namespace ShowWhere.Desktop;

public sealed class GuidanceViewModel : INotifyPropertyChanged
{
    private readonly IWindowsUiObserver _observer;
    private readonly IWindowsChangeMonitor _changeMonitor;
    private readonly IGuideApiClient _apiClient;
    private readonly IHighlightOverlay _overlay;
    private readonly Action _exit;
    private CancellationTokenSource? _taskCancellation;
    private TaskSession? _session;
    private string _goalText = string.Empty;
    private string _guidanceMessage = "하고 싶은 일을 입력하면 현재 Windows 화면에서 다음 위치를 찾아드릴게요.";
    private string _currentApplication = "관찰 대기 중";
    private string _statusText = "준비됨";
    private string _errorMessage = string.Empty;
    private bool _isLoading;
    private bool _isPaused;

    public GuidanceViewModel(
        IWindowsUiObserver observer,
        IWindowsChangeMonitor changeMonitor,
        IGuideApiClient apiClient,
        IHighlightOverlay overlay,
        Action exit)
    {
        _observer = observer;
        _changeMonitor = changeMonitor;
        _apiClient = apiClient;
        _overlay = overlay;
        _exit = exit;
        SubmitCommand = new AsyncRelayCommand(SubmitAsync, CanSubmit);
        RecoveryCommand = new AsyncRelayCommand(RecoverAsync, () => _session is not null && !_isPaused);
        CancelCommand = new RelayCommand(CancelCurrentTask, () => _session is not null);
        TogglePauseCommand = new RelayCommand(TogglePause);
        ExitCommand = new RelayCommand(_exit);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<UiBounds>? TargetHighlighted;
    public ICommand SubmitCommand { get; }
    public ICommand RecoveryCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand TogglePauseCommand { get; }
    public ICommand ExitCommand { get; }

    public string GoalText
    {
        get => _goalText;
        set { if (Set(ref _goalText, value)) RaiseCommandStates(); }
    }
    public string GuidanceMessage { get => _guidanceMessage; private set => Set(ref _guidanceMessage, value); }
    public string CurrentApplication { get => _currentApplication; private set => Set(ref _currentApplication, value); }
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
    public string ErrorMessage { get => _errorMessage; private set => Set(ref _errorMessage, value); }
    public bool IsLoading { get => _isLoading; private set { if (Set(ref _isLoading, value)) RaiseCommandStates(); } }
    public bool IsPaused { get => _isPaused; private set { if (Set(ref _isPaused, value)) OnPropertyChanged(nameof(PauseMenuText)); } }
    public string PauseMenuText => IsPaused ? "다시 시작" : "일시 정지";

    private bool CanSubmit() => !IsPaused && !IsLoading && !string.IsNullOrWhiteSpace(GoalText);

    private async Task SubmitAsync()
    {
        CancelRunningWork(markCancelled: false);
        _session = TaskSessionStateMachine.Create(GoalText);
        _taskCancellation = new CancellationTokenSource();
        await RunGuidanceLoopAsync(null, _taskCancellation.Token);
    }

    private async Task RecoverAsync()
    {
        if (_session is null) return;
        CancelRunningWork(markCancelled: false);
        _session = TaskSessionStateMachine.Failed(_session, "사용자가 다른 후보를 요청함");
        _taskCancellation = new CancellationTokenSource();
        await RunGuidanceLoopAsync(null, _taskCancellation.Token);
    }

    private async Task RunGuidanceLoopAsync(WindowsObservation? currentObservation, CancellationToken cancellationToken)
    {
        if (_session is null) return;
        ErrorMessage = string.Empty;
        try
        {
            for (var step = 0; step < 20 && !cancellationToken.IsCancellationRequested; step++)
            {
                IsLoading = true;
                StatusText = "현재 앱 관찰 중";
                _session = TaskSessionStateMachine.ObservationStarted(_session);
                currentObservation ??= await _observer.ObserveAsync(cancellationToken);
                CurrentApplication = string.IsNullOrWhiteSpace(currentObservation.Context.WindowTitle)
                    ? currentObservation.Context.ApplicationName
                    : $"{currentObservation.Context.ApplicationName} — {currentObservation.Context.WindowTitle}";
                StatusText = $"후보 {currentObservation.Candidates.Count}개 분석 중";
                _session = TaskSessionStateMachine.AiRequested(_session);
                var request = new GuideRequest(_session, currentObservation.Context, currentObservation.Candidates);
                var decision = await _apiClient.DecideNextActionAsync(request, cancellationToken);
                GuidanceMessage = decision.Message;

                if (decision.Status == GuideStatuses.Completed)
                {
                    _overlay.Clear();
                    _session = TaskSessionStateMachine.Completed(_session, decision.Message);
                    StatusText = "작업 완료";
                    IsLoading = false;
                    return;
                }

                if (decision.Action == GuideActions.Highlight && decision.TargetId is not null)
                {
                    if (!currentObservation.Registry.TryResolveBounds(decision.TargetId, out var bounds))
                        throw new ContractValidationException("The selected Windows element is stale.");
                    _session = TaskSessionStateMachine.GuidanceReady(_session, decision.Message, decision.ExpectedChange);
                    _overlay.ShowTarget(bounds, decision.Message);
                    TargetHighlighted?.Invoke(bounds);
                    StatusText = "표시된 위치에서 직접 작업해 주세요";
                    IsLoading = false;
                    var changed = await _changeMonitor.WaitForMeaningfulChangeAsync(
                        currentObservation,
                        TimeSpan.FromSeconds(60),
                        cancellationToken);
                    if (changed is null)
                    {
                        _session = TaskSessionStateMachine.WaitingForUser(_session);
                        StatusText = "변화를 기다리는 중 — 필요하면 ‘찾을 수 없어요’를 눌러주세요";
                        return;
                    }
                    _overlay.Clear();
                    _session = TaskSessionStateMachine.StepCompleted(_session, $"{decision.TargetId}에서 UI 변화 감지");
                    currentObservation = changed;
                    continue;
                }

                _overlay.Clear();
                IsLoading = false;
                if (decision.Action == GuideActions.RequestNewObservation)
                {
                    currentObservation = null;
                    continue;
                }
                _session = TaskSessionStateMachine.WaitingForUser(_session, decision.Message);
                StatusText = decision.Action == GuideActions.AskUser ? "추가 설명 필요" : "안내 확인";
                return;
            }
            StatusText = "안전을 위해 안내를 멈췄어요";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusText = IsPaused ? "일시 정지됨" : "작업 취소됨";
        }
        catch (WindowsObservationException exception)
        {
            ErrorMessage = exception.Message;
            StatusText = "관찰 오류";
        }
        catch (ContractValidationException)
        {
            ErrorMessage = "안전하게 표시할 대상을 확인하지 못했어요. 다시 시도해 주세요.";
            StatusText = "안내 검증 오류";
        }
        catch (GuideApiException exception)
        {
            ErrorMessage = exception.Message;
            StatusText = "서버 연결 오류";
        }
        catch
        {
            ErrorMessage = "현재 화면을 확인하지 못했어요. 잠시 후 다시 시도해 주세요.";
            StatusText = "오류";
        }
        finally
        {
            IsLoading = false;
            RaiseCommandStates();
        }
    }

    private void CancelCurrentTask()
    {
        CancelRunningWork(markCancelled: true);
        StatusText = "작업 취소됨";
        GuidanceMessage = "새로운 목표를 입력해 주세요.";
    }

    private void TogglePause()
    {
        IsPaused = !IsPaused;
        if (IsPaused)
        {
            CancelRunningWork(markCancelled: false);
            StatusText = "일시 정지됨";
            GuidanceMessage = "ShowWhere가 화면 관찰을 일시 정지했습니다.";
        }
        else
        {
            StatusText = "준비됨";
            GuidanceMessage = "다시 시작할 목표를 입력하거나 안내 시작을 눌러주세요.";
        }
        RaiseCommandStates();
    }

    private void CancelRunningWork(bool markCancelled)
    {
        _taskCancellation?.Cancel();
        _taskCancellation?.Dispose();
        _taskCancellation = null;
        _overlay.Clear();
        if (markCancelled && _session is not null)
            _session = TaskSessionStateMachine.Cancelled(_session);
    }

    private void RaiseCommandStates()
    {
        (SubmitCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (RecoveryCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (CancelCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
