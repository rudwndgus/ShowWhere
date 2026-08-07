using System.ComponentModel;
using System.Collections.ObjectModel;
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
        Messages.Add(new ChatMessageItem(
            "assistant",
            "하고 싶은 일을 입력해 주세요. 현재 앱과 Windows 작업표시줄에서 다음에 누를 위치를 찾아드릴게요."));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<UiBounds>? TargetHighlighted;
    public ICommand SubmitCommand { get; }
    public ICommand RecoveryCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand TogglePauseCommand { get; }
    public ICommand ExitCommand { get; }
    public ObservableCollection<ChatMessageItem> Messages { get; } = [];

    public string GoalText
    {
        get => _goalText;
        set { if (Set(ref _goalText, value)) RaiseCommandStates(); }
    }
    public string CurrentApplication { get => _currentApplication; private set => Set(ref _currentApplication, value); }
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
    public string ErrorMessage { get => _errorMessage; private set => Set(ref _errorMessage, value); }
    public bool IsLoading { get => _isLoading; private set { if (Set(ref _isLoading, value)) RaiseCommandStates(); } }
    public bool IsPaused { get => _isPaused; private set { if (Set(ref _isPaused, value)) OnPropertyChanged(nameof(PauseMenuText)); } }
    public string PauseMenuText => IsPaused ? "다시 시작" : "일시 정지";

    private bool CanSubmit() => !IsPaused && !IsLoading && !string.IsNullOrWhiteSpace(GoalText);

    private async Task SubmitAsync()
    {
        var query = GoalText.Trim();
        if (string.IsNullOrWhiteSpace(query)) return;
        Messages.Add(new ChatMessageItem("user", query));
        GoalText = string.Empty;
        CancelRunningWork(markCancelled: false);
        _session = TaskSessionStateMachine.Create(query);
        _taskCancellation = new CancellationTokenSource();
        await RunGuidanceLoopAsync(null, _taskCancellation.Token);
    }

    private async Task RecoverAsync()
    {
        if (_session is null) return;
        Messages.Add(new ChatMessageItem("user", "다른 위치를 찾아줘"));
        CancelRunningWork(markCancelled: false);
        _session = TaskSessionStateMachine.Failed(_session, "사용자가 다른 후보를 요청함");
        _taskCancellation = new CancellationTokenSource();
        await RunGuidanceLoopAsync(null, _taskCancellation.Token);
    }

    private async Task RunGuidanceLoopAsync(WindowsObservation? currentObservation, CancellationToken cancellationToken)
    {
        if (_session is null) return;
        ErrorMessage = string.Empty;
        ChatMessageItem? pendingMessage = null;
        try
        {
            for (var step = 0; step < 20 && !cancellationToken.IsCancellationRequested; step++)
            {
                IsLoading = true;
                StatusText = step == 0 ? "현재 화면 확인 중" : "다음 화면 확인 중";
                pendingMessage = new ChatMessageItem(
                    "assistant",
                    step == 0 ? "현재 화면에서 누를 위치를 찾고 있어요…" : "화면이 바뀌었어요. 다음 위치를 찾고 있어요…",
                    isPending: true);
                Messages.Add(pendingMessage);
                _session = TaskSessionStateMachine.ObservationStarted(_session);
                currentObservation ??= await _observer.ObserveAsync(cancellationToken);
                CurrentApplication = string.IsNullOrWhiteSpace(currentObservation.Context.WindowTitle)
                    ? currentObservation.Context.ApplicationName
                    : $"{currentObservation.Context.ApplicationName} — {currentObservation.Context.WindowTitle}";
                var prioritizedCandidates = WindowsCandidatePrioritizer.Prioritize(
                    _session.OriginalUserMessage,
                    currentObservation.Candidates);
                StatusText = $"후보 {prioritizedCandidates.Count}개 분석 중";
                _session = TaskSessionStateMachine.AiRequested(_session);
                var request = new GuideRequest(_session, currentObservation.Context, prioritizedCandidates);
                var decision = await _apiClient.DecideNextActionAsync(request, cancellationToken);
                pendingMessage.Text = decision.Message;
                pendingMessage.IsPending = false;
                pendingMessage = null;

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
                    var selectedCandidate = currentObservation.Candidates.FirstOrDefault(candidate =>
                        string.Equals(candidate.Id, decision.TargetId, StringComparison.Ordinal));
                    if (!currentObservation.Registry.TryResolveBounds(decision.TargetId, out var bounds))
                        throw new ContractValidationException("The selected Windows element is stale.");
                    _session = TaskSessionStateMachine.GuidanceReady(_session, decision.Message, decision.ExpectedChange);
                    _overlay.ShowTarget(bounds, decision.Message);
                    TargetHighlighted?.Invoke(bounds);
                    StatusText = "표시된 위치에서 직접 작업해 주세요";
                    IsLoading = false;
                    var changed = await _changeMonitor.WaitForTargetInteractionAsync(
                        bounds,
                        TimeSpan.FromSeconds(60),
                        cancellationToken);
                    if (changed is null)
                    {
                        _session = TaskSessionStateMachine.WaitingForUser(_session);
                        StatusText = "변화를 기다리는 중 — 필요하면 ‘찾을 수 없어요’를 눌러주세요";
                        return;
                    }
                    _overlay.Clear();
                    var selectedLabel = selectedCandidate?.Label ?? decision.TargetId;
                    _session = TaskSessionStateMachine.StepCompleted(
                        _session,
                        $"사용자가 '{selectedLabel}' 컨트롤을 클릭함. 이전 안내: {decision.Message}");
                    if (selectedCandidate is not null
                        && WindowsSystemOutcomeResolver.TryResolve(
                            _session.OriginalUserMessage,
                            selectedCandidate,
                            out var completedMessage))
                    {
                        Messages.Add(new ChatMessageItem("assistant", completedMessage));
                        _session = TaskSessionStateMachine.Completed(_session, completedMessage);
                        StatusText = "작업 완료";
                        return;
                    }
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
            if (pendingMessage is not null) Messages.Remove(pendingMessage);
        }
        catch (WindowsObservationException exception)
        {
            ErrorMessage = exception.Message;
            StatusText = "관찰 오류";
            CompletePendingOrAddError(pendingMessage, ErrorMessage);
        }
        catch (ContractValidationException)
        {
            ErrorMessage = "안전하게 표시할 대상을 확인하지 못했어요. 다시 시도해 주세요.";
            StatusText = "안내 검증 오류";
            CompletePendingOrAddError(pendingMessage, ErrorMessage);
        }
        catch (GuideApiException exception)
        {
            ErrorMessage = exception.Message;
            StatusText = "서버 연결 오류";
            CompletePendingOrAddError(pendingMessage, ErrorMessage);
        }
        catch
        {
            ErrorMessage = "현재 화면을 확인하지 못했어요. 잠시 후 다시 시도해 주세요.";
            StatusText = "오류";
            CompletePendingOrAddError(pendingMessage, ErrorMessage);
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
        Messages.Add(new ChatMessageItem("assistant", "안내를 취소했어요. 새로운 목표를 입력해 주세요."));
    }

    private void TogglePause()
    {
        IsPaused = !IsPaused;
        if (IsPaused)
        {
            CancelRunningWork(markCancelled: false);
            StatusText = "일시 정지됨";
            Messages.Add(new ChatMessageItem("assistant", "화면 관찰을 일시 정지했어요."));
        }
        else
        {
            StatusText = "준비됨";
            Messages.Add(new ChatMessageItem("assistant", "다시 시작할 목표를 입력해 주세요."));
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

    private void CompletePendingOrAddError(ChatMessageItem? pendingMessage, string message)
    {
        if (pendingMessage is null)
        {
            Messages.Add(new ChatMessageItem("assistant", message));
            return;
        }
        pendingMessage.Text = message;
        pendingMessage.IsPending = false;
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
