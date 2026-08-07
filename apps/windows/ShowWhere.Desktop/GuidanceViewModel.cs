using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Windows;
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
    private WindowsObservation? _clarificationObservation;
    private GuideDecision? _forcedDecision;

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
        SelectClarificationCommand = new AsyncParameterRelayCommand(SelectClarificationAsync);
        RecoveryCommand = new AsyncRelayCommand(
            RecoverAsync,
            () => _session is not null && !_isPaused && ClarificationChoices.Count == 0);
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
    public ICommand SelectClarificationCommand { get; }
    public ICommand RecoveryCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand TogglePauseCommand { get; }
    public ICommand ExitCommand { get; }
    public ObservableCollection<ChatMessageItem> Messages { get; } = [];
    public ObservableCollection<ClarificationChoiceItem> ClarificationChoices { get; } = [];

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
        ClearClarificationChoices();
        if (WindowsGoalClarificationResolver.TryCreate(query, out var clarification))
        {
            _session = TaskSessionStateMachine.Create(query);
            Messages.Add(new ChatMessageItem("assistant", clarification.Message));
            foreach (var choice in clarification.Choices)
                ClarificationChoices.Add(new ClarificationChoiceItem(choice.Label, choice.ResolvedGoal));
            StatusText = "선택이 필요해요";
            RaiseCommandStates();
            return;
        }
        _session = TaskSessionStateMachine.Create(query);
        _taskCancellation = new CancellationTokenSource();
        await RunGuidanceLoopAsync(null, _taskCancellation.Token);
    }

    private async Task SelectClarificationAsync(object? parameter)
    {
        if (parameter is not ClarificationChoiceItem choice || IsPaused || IsLoading) return;
        Messages.Add(new ChatMessageItem("user", choice.Label));
        var observation = _clarificationObservation;
        ClearClarificationChoices();
        CancelRunningWork(markCancelled: false);
        if (choice.TargetId is not null && observation is not null && _session is not null)
        {
            _forcedDecision = new GuideDecision(
                GuideStatuses.InProgress,
                GuideActions.Highlight,
                $"좋아요. '{choice.Label}' 위치를 표시할게요.",
                1,
                choice.TargetId,
                "선택한 항목이 열립니다.");
        }
        else if (choice.ResolvedGoal is not null)
        {
            _session = TaskSessionStateMachine.Create(choice.ResolvedGoal);
            observation = null;
        }
        else
        {
            return;
        }
        _taskCancellation = new CancellationTokenSource();
        await RunGuidanceLoopAsync(observation, _taskCancellation.Token);
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
                currentObservation ??= await _observer.ObserveAsync(
                    _session.OriginalUserMessage,
                    cancellationToken);
                CurrentApplication = string.IsNullOrWhiteSpace(currentObservation.Context.WindowTitle)
                    ? currentObservation.Context.ApplicationName
                    : $"{currentObservation.Context.ApplicationName} — {currentObservation.Context.WindowTitle}";
                var prioritizedCandidates = WindowsCandidatePrioritizer.Prioritize(
                    _session.OriginalUserMessage,
                    currentObservation.Candidates);
                StatusText = $"후보 {prioritizedCandidates.Count}개 분석 중";
                _session = TaskSessionStateMachine.AiRequested(_session);
                var request = new GuideRequest(_session, currentObservation.Context, prioritizedCandidates);
                GuideDecision decision;
                if (_forcedDecision is not null)
                {
                    decision = ContractValidator.ValidateDecision(_forcedDecision, request);
                    _forcedDecision = null;
                    StatusText = "선택한 위치 안내";
                }
                else if (WindowsFastPathResolver.TryResolve(request, out var fastDecision))
                {
                    StatusText = "Windows 빠른 안내";
                    decision = ContractValidator.ValidateDecision(fastDecision, request);
                }
                else
                {
                    GuideDecision? deferredDecision = null;
                    if (currentObservation.ForegroundScanDeferred)
                    {
                        StatusText = "열린 Windows 화면 확인 중";
                        await Task.Delay(250, cancellationToken);
                        currentObservation = await _observer.ObserveAsync(
                            _session.OriginalUserMessage,
                            cancellationToken);
                        CurrentApplication = string.IsNullOrWhiteSpace(currentObservation.Context.WindowTitle)
                            ? currentObservation.Context.ApplicationName
                            : $"{currentObservation.Context.ApplicationName} — {currentObservation.Context.WindowTitle}";
                        prioritizedCandidates = WindowsCandidatePrioritizer.Prioritize(
                            _session.OriginalUserMessage,
                            currentObservation.Candidates);
                        request = new GuideRequest(_session, currentObservation.Context, prioritizedCandidates);
                        if (WindowsFastPathResolver.TryResolve(request, out fastDecision))
                        {
                            StatusText = "Windows 빠른 안내";
                            deferredDecision = ContractValidator.ValidateDecision(fastDecision, request);
                        }
                    }
                    if (deferredDecision is not null)
                    {
                        decision = deferredDecision;
                    }
                    else if (WindowsFastPathResolver.IsKnownSystemGoal(_session.OriginalUserMessage))
                    {
                        GuideDecision? localDecision = null;
                        for (var retry = 0; retry < 4 && localDecision is null; retry++)
                        {
                            StatusText = "Windows 메뉴가 열리기를 기다리는 중";
                            await Task.Delay(300, cancellationToken);
                            currentObservation = await _observer.ObserveAsync(
                                _session.OriginalUserMessage,
                                cancellationToken);
                            CurrentApplication = string.IsNullOrWhiteSpace(currentObservation.Context.WindowTitle)
                                ? currentObservation.Context.ApplicationName
                                : $"{currentObservation.Context.ApplicationName} — {currentObservation.Context.WindowTitle}";
                            prioritizedCandidates = WindowsCandidatePrioritizer.Prioritize(
                                _session.OriginalUserMessage,
                                currentObservation.Candidates);
                            request = new GuideRequest(_session, currentObservation.Context, prioritizedCandidates);
                            if (WindowsFastPathResolver.TryResolve(request, out fastDecision))
                                localDecision = ContractValidator.ValidateDecision(fastDecision, request);
                        }

                        decision = localDecision ?? new GuideDecision(
                            GuideStatuses.NeedsClarification,
                            GuideActions.AskUser,
                            "Windows 메뉴에서 다음 항목을 아직 읽지 못했어요. 시작 메뉴나 설정 창을 열린 상태로 두고 ‘찾을 수 없어요’를 눌러주시겠어요?",
                            0);
                    }
                    else
                    {
                        StatusText = $"후보 {prioritizedCandidates.Count}개 AI 분석 중";
                        decision = await _apiClient.DecideNextActionAsync(request, cancellationToken);
                    }
                }
                pendingMessage.Text = decision.Message;
                pendingMessage.IsPending = false;
                pendingMessage = null;

                if (decision.Action == GuideActions.AskUser
                    && decision.AlternativeTargetIds is { Count: >= 2 })
                {
                    var choices = decision.AlternativeTargetIds
                        .Select(id => prioritizedCandidates.FirstOrDefault(candidate => candidate.Id == id))
                        .Where(candidate => candidate is not null)
                        .Select(candidate => new ClarificationChoiceItem(
                            candidate!.Label ?? candidate.Description ?? candidate.Role,
                            TargetId: candidate.Id))
                        .ToArray();
                    if (choices.Length >= 2)
                    {
                        _clarificationObservation = currentObservation;
                        foreach (var choice in choices) ClarificationChoices.Add(choice);
                        _session = TaskSessionStateMachine.WaitingForUser(_session, decision.Message);
                        StatusText = "선택이 필요해요";
                        IsLoading = false;
                        RaiseCommandStates();
                        return;
                    }
                }

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
                    if (!currentObservation.Registry.TryResolveState(
                            decision.TargetId,
                            out var bounds,
                            out var isOffscreen))
                        throw new ContractValidationException("The selected Windows element is stale.");
                    _session = TaskSessionStateMachine.GuidanceReady(_session, decision.Message, decision.ExpectedChange);
                    if (isOffscreen)
                    {
                        _overlay.ShowScrollHint(bounds, decision.Message);
                        StatusText = bounds.Y >= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight
                            ? "아래로 스크롤해 주세요"
                            : "위로 스크롤해 주세요";
                        var visibleBounds = await currentObservation.Registry.WaitForVisibleBoundsAsync(
                            decision.TargetId,
                            TimeSpan.FromSeconds(60),
                            cancellationToken);
                        if (visibleBounds is null)
                        {
                            _session = TaskSessionStateMachine.WaitingForUser(_session);
                            StatusText = "스크롤을 기다리는 중";
                            return;
                        }
                        bounds = visibleBounds;
                        _overlay.ShowTarget(bounds, decision.Message);
                        TargetHighlighted?.Invoke(bounds);
                    }
                    else
                    {
                        _overlay.ShowTarget(bounds, decision.Message);
                        TargetHighlighted?.Invoke(bounds);
                    }
                    StatusText = "표시된 위치에서 직접 작업해 주세요";
                    IsLoading = false;
                    var changed = await _changeMonitor.WaitForTargetInteractionAsync(
                        bounds,
                        _session.OriginalUserMessage,
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
        catch (Exception exception)
        {
            DesktopDiagnostics.Write(exception);
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
        ClearClarificationChoices();
        StatusText = "작업 취소됨";
        Messages.Add(new ChatMessageItem("assistant", "안내를 취소했어요. 새로운 목표를 입력해 주세요."));
    }

    private void TogglePause()
    {
        IsPaused = !IsPaused;
        if (IsPaused)
        {
            CancelRunningWork(markCancelled: false);
            ClearClarificationChoices();
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
        _forcedDecision = null;
        if (markCancelled && _session is not null)
            _session = TaskSessionStateMachine.Cancelled(_session);
    }

    private void RaiseCommandStates()
    {
        (SubmitCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (RecoveryCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (CancelCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (SelectClarificationCommand as AsyncParameterRelayCommand)?.RaiseCanExecuteChanged();
    }

    private void ClearClarificationChoices()
    {
        ClarificationChoices.Clear();
        _clarificationObservation = null;
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
