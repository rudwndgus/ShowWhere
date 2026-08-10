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
    private readonly IWindowsScreenCaptureService _screenCapture;
    private readonly IGuideApiClient _apiClient;
    private readonly ITeachingApiClient _teachingClient;
    private readonly IHighlightOverlay _overlay;
    private readonly ICorrectionSelectionService _correctionSelection;
    private readonly IDeveloperCorrectionStore _correctionStore;
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
    private WindowsScreenCapture? _forcedVisualCapture;
    private WindowsObservation? _lastObservation;
    private GuideDecision? _lastDecision;
    private UiCandidate? _lastHighlightedCandidate;
    private UiBounds? _lastHighlightedBounds;
    private string _submittedGoal = string.Empty;
    private string _correctionIntentText = string.Empty;
    private string _correctionCommentText = string.Empty;
    private string _correctionSelectionSummary = "정답 영역을 아직 선택하지 않았습니다.";
    private bool _isCorrectionEditorVisible;
    private bool _saveCorrectionScreenshot = true;
    private ChatMessageItem? _correctionAnswer;
    private AnswerFeedbackRecord? _correctionFeedback;
    private UiBounds? _pendingCorrectionSelection;
    private UiCandidate? _pendingCorrectionCandidate;
    private WindowsScreenCapture? _pendingCorrectionCapture;
    private string _teachingUserQuestion = string.Empty;
    private string _teachingCurrentStage = string.Empty;
    private string _teachingDeveloperCorrection = string.Empty;
    private string _teachingPreviewJson = string.Empty;
    private string _teachingValidationSummary = "분석 전입니다.";
    private bool _teachingValidationPassed;
    private bool _teachingGoldSaved;

    public GuidanceViewModel(
        IWindowsUiObserver observer,
        IWindowsChangeMonitor changeMonitor,
        IWindowsScreenCaptureService screenCapture,
        IGuideApiClient apiClient,
        ITeachingApiClient teachingClient,
        IHighlightOverlay overlay,
        ICorrectionSelectionService correctionSelection,
        IDeveloperCorrectionStore correctionStore,
        Action exit)
    {
        _observer = observer;
        _changeMonitor = changeMonitor;
        _screenCapture = screenCapture;
        _apiClient = apiClient;
        _teachingClient = teachingClient;
        _overlay = overlay;
        _correctionSelection = correctionSelection;
        _correctionStore = correctionStore;
        _exit = exit;
        SubmitCommand = new AsyncRelayCommand(SubmitAsync, CanSubmit);
        SelectClarificationCommand = new AsyncParameterRelayCommand(SelectClarificationAsync);
        RecoveryCommand = new AsyncRelayCommand(
            RecoverAsync,
            () => _session is not null && !_isPaused && ClarificationChoices.Count == 0);
        CancelCommand = new RelayCommand(CancelCurrentTask, () => _session is not null);
        CaptureCorrectionCommand = new AsyncRelayCommand(CaptureCorrectionAsync, CanCaptureCorrection);
        SaveCorrectionCommand = new AsyncRelayCommand(SaveCorrectionAsync, CanSaveCorrection);
        AnalyzeTeachingCommand = new AsyncRelayCommand(AnalyzeTeachingAsync, CanAnalyzeTeaching);
        ValidateTeachingCommand = new AsyncRelayCommand(ValidateTeachingAsync, CanValidateTeaching);
        SaveApprovedGoldCommand = new AsyncRelayCommand(SaveApprovedGoldAsync, CanSaveApprovedGold);
        CancelCorrectionEditCommand = new RelayCommand(CloseCorrectionEditor);
        MarkAnswerCorrectCommand = new AsyncParameterRelayCommand(MarkAnswerCorrectAsync, CanEvaluateAnswer);
        MarkAnswerIncorrectCommand = new AsyncParameterRelayCommand(MarkAnswerIncorrectAsync, CanEvaluateAnswer);
        MarkTaskCompletedCommand = new AsyncParameterRelayCommand(MarkTaskCompletedAsync, CanMarkTaskCompleted);
        TogglePauseCommand = new RelayCommand(TogglePause);
        ExitCommand = new RelayCommand(_exit);
        Messages.Add(CreateAssistantMessage(
            "assistant",
            "하고 싶은 일을 입력해 주세요. 현재 앱과 Windows 작업표시줄에서 다음에 누를 위치를 찾아드릴게요."));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<UiBounds>? TargetHighlighted;
    public event Action? CorrectionSelectionStarted;
    public event Action? CorrectionSelectionCompleted;
    public event Action? TeachingEditorRequested;
    public event Action? TeachingEditorClosed;
    public ICommand SubmitCommand { get; }
    public ICommand SelectClarificationCommand { get; }
    public ICommand RecoveryCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand CaptureCorrectionCommand { get; }
    public ICommand SaveCorrectionCommand { get; }
    public ICommand AnalyzeTeachingCommand { get; }
    public ICommand ValidateTeachingCommand { get; }
    public ICommand SaveApprovedGoldCommand { get; }
    public ICommand CancelCorrectionEditCommand { get; }
    public ICommand MarkAnswerCorrectCommand { get; }
    public ICommand MarkAnswerIncorrectCommand { get; }
    public ICommand MarkTaskCompletedCommand { get; }
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
    public bool IsCorrectionEditorVisible
    {
        get => _isCorrectionEditorVisible;
        private set { if (Set(ref _isCorrectionEditorVisible, value)) RaiseCommandStates(); }
    }
    public string CorrectionIntentText
    {
        get => _correctionIntentText;
        set { if (Set(ref _correctionIntentText, value)) RaiseCommandStates(); }
    }
    public string CorrectionCommentText
    {
        get => _correctionCommentText;
        set { if (Set(ref _correctionCommentText, value)) RaiseCommandStates(); }
    }
    public string CorrectionSelectionSummary
    {
        get => _correctionSelectionSummary;
        private set => Set(ref _correctionSelectionSummary, value);
    }
    public bool SaveCorrectionScreenshot
    {
        get => _saveCorrectionScreenshot;
        set => Set(ref _saveCorrectionScreenshot, value);
    }
    public string CorrectionDataDirectory => _correctionStore.DataDirectory;
    public string TeachingUserQuestion
    {
        get => _teachingUserQuestion;
        set { if (Set(ref _teachingUserQuestion, value)) InvalidateTeachingValidation(); }
    }
    public string TeachingCurrentStage
    {
        get => _teachingCurrentStage;
        set { if (Set(ref _teachingCurrentStage, value)) InvalidateTeachingValidation(); }
    }
    public string TeachingDeveloperCorrection
    {
        get => _teachingDeveloperCorrection;
        set { if (Set(ref _teachingDeveloperCorrection, value)) InvalidateTeachingValidation(); }
    }
    public string TeachingPreviewJson
    {
        get => _teachingPreviewJson;
        set { if (Set(ref _teachingPreviewJson, value)) InvalidateTeachingValidation(); }
    }
    public string TeachingValidationSummary
    {
        get => _teachingValidationSummary;
        private set => Set(ref _teachingValidationSummary, value);
    }
    public bool TeachingValidationPassed
    {
        get => _teachingValidationPassed;
        private set { if (Set(ref _teachingValidationPassed, value)) RaiseCommandStates(); }
    }
    public string PauseMenuText => IsPaused ? "다시 시작" : "일시 정지";

    private bool CanSubmit() => !IsPaused && !IsLoading && !string.IsNullOrWhiteSpace(GoalText);

    private async Task SubmitAsync()
    {
        var query = GoalText.Trim();
        if (string.IsNullOrWhiteSpace(query)) return;
        Messages.Add(new ChatMessageItem("user", query));
        GoalText = string.Empty;
        ResetCorrectionDraft();
        _submittedGoal = query;
        _lastObservation = null;
        _lastDecision = null;
        _lastHighlightedCandidate = null;
        _lastHighlightedBounds = null;
        CancelRunningWork(markCancelled: false);
        ClearClarificationChoices();
        if (WindowsGoalClarificationResolver.TryCreate(query, out var clarification))
        {
            _session = TaskSessionStateMachine.Create(query);
            Messages.Add(CreateAssistantMessage("assistant", clarification.Message));
            foreach (var choice in clarification.Choices)
                ClarificationChoices.Add(new ClarificationChoiceItem(choice.Label, choice.ResolvedGoal));
            StatusText = "선택이 필요해요";
            RaiseCommandStates();
            return;
        }
        var effectiveGoal = _correctionStore.ResolveIntent(query);
        _session = TaskSessionStateMachine.Create(effectiveGoal);
        if (!string.Equals(effectiveGoal, query, StringComparison.Ordinal))
            Messages.Add(CreateAssistantMessage("assistant", $"저장된 개발자 교정을 적용했어요: {effectiveGoal}"));
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
                pendingMessage = CreateAssistantMessage(
                    "assistant",
                    step == 0 ? "현재 화면에서 누를 위치를 찾고 있어요…" : "화면이 바뀌었어요. 다음 위치를 찾고 있어요…",
                    isPending: true);
                Messages.Add(pendingMessage);
                _session = TaskSessionStateMachine.ObservationStarted(_session);
                currentObservation ??= await _observer.ObserveAsync(
                    _session.OriginalUserMessage,
                    cancellationToken);
                _lastObservation = currentObservation;
                CurrentApplication = string.IsNullOrWhiteSpace(currentObservation.Context.WindowTitle)
                    ? currentObservation.Context.ApplicationName
                    : $"{currentObservation.Context.ApplicationName} — {currentObservation.Context.WindowTitle}";
                if (_correctionStore.TryResolveCompletion(
                    _session.OriginalUserMessage,
                    currentObservation.Context,
                    currentObservation.SnapshotHash,
                    currentObservation.Candidates,
                    out var savedCompletion))
                {
                    DesktopDiagnostics.WriteEvent(
                        "developer_completion_matched",
                        ("completionId", savedCompletion.Id),
                        ("evidenceCount", savedCompletion.VisibleSemantics.Count));
                    var completionMessage = savedCompletion.CompletionMessage
                        ?? "목표에 도달한 화면이에요. 여기서 안내를 마칠게요.";
                    var completionDecision = new GuideDecision(
                        GuideStatuses.Completed,
                        GuideActions.Explain,
                        completionMessage,
                        1);
                    pendingMessage.Text = completionMessage;
                    AttachTrainingContext(pendingMessage, completionDecision);
                    pendingMessage.IsPending = false;
                    pendingMessage.MarkEvaluated("completed");
                    pendingMessage = null;
                    _lastDecision = completionDecision;
                    _overlay.Clear();
                    _session = TaskSessionStateMachine.Completed(_session, completionMessage);
                    StatusText = "저장된 완료 지점에 도달함";
                    IsLoading = false;
                    return;
                }
                var prioritizedCandidates = WindowsCandidatePrioritizer.Prioritize(
                    _session.OriginalUserMessage,
                    currentObservation.Candidates);
                StatusText = $"후보 {prioritizedCandidates.Count}개 분석 중";
                _session = TaskSessionStateMachine.AiRequested(_session);
                var request = new GuideRequest(_session, currentObservation.Context, prioritizedCandidates);
                GuideDecision decision;
                if (_forcedDecision is not null)
                {
                    var forcedDecision = _forcedDecision;
                    var forcedVisualCapture = _forcedVisualCapture;
                    _forcedDecision = null;
                    _forcedVisualCapture = null;
                    if (forcedDecision.Action == GuideActions.HighlightVisual)
                    {
                        if (forcedVisualCapture is null)
                            throw new ContractValidationException("The saved visual correction has no screen capture.");
                        request = request with
                        {
                            Screenshot = forcedVisualCapture.DataUrl,
                            ScreenshotBounds = forcedVisualCapture.Bounds,
                        };
                    }
                    decision = ContractValidator.ValidateDecision(forcedDecision, request);
                    StatusText = "선택한 위치 안내";
                }
                else if (_correctionStore.TryResolveTarget(
                    _session.OriginalUserMessage,
                    currentObservation.Context,
                    prioritizedCandidates,
                    out var correctedTarget,
                    out _))
                {
                    decision = new GuideDecision(
                        GuideStatuses.InProgress,
                        GuideActions.Highlight,
                        $"저장된 개발자 교정에 따라 '{correctedTarget.Label ?? correctedTarget.Role}' 위치를 표시할게요.",
                        1,
                        correctedTarget.Id,
                        "교정된 항목이 열립니다.");
                    StatusText = "개발자 교정 적용";
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

                        if (localDecision is not null)
                        {
                            decision = localDecision;
                        }
                        else
                        {
                            (decision, request) = await RequestVisionDecisionAsync(request, cancellationToken);
                        }
                    }
                    else
                    {
                        StatusText = $"후보 {prioritizedCandidates.Count}개 AI 분석 중";
                        decision = await _apiClient.DecideNextActionAsync(request, cancellationToken);
                        if (decision.Action == GuideActions.RequestVision)
                            (decision, request) = await RequestVisionDecisionAsync(request, cancellationToken);
                    }
                }
                _lastObservation = currentObservation;
                _lastDecision = decision;
                pendingMessage.Text = decision.Message;
                AttachTrainingContext(pendingMessage, decision);
                pendingMessage.IsPending = false;
                var answerMessage = pendingMessage;
                pendingMessage = null;

                if (decision.Action == GuideActions.AskUser
                    && decision.AlternativeTargetIds is { Count: >= 2 })
                {
                    var choices = decision.AlternativeTargetIds
                        .Select(id => prioritizedCandidates.FirstOrDefault(candidate => candidate.Id == id))
                        .Where(candidate => candidate is not null)
                        .Select(candidate => new ClarificationChoiceItem(
                            DescribeClarificationChoice(candidate!),
                            TargetId: candidate!.Id))
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

                if ((decision.Action == GuideActions.Highlight && decision.TargetId is not null)
                    || (decision.Action == GuideActions.HighlightVisual && decision.VisualTarget is not null))
                {
                    DesktopDiagnostics.WriteEvent(
                        "highlight_decision",
                        ("action", decision.Action),
                        ("confidence", decision.Confidence));
                    UiCandidate? selectedCandidate = null;
                    UiBounds bounds;
                    var isOffscreen = false;
                    var selectedLabel = decision.VisualTarget?.Label ?? decision.TargetId ?? "화면 항목";
                    if (decision.Action == GuideActions.HighlightVisual)
                    {
                        if (request.ScreenshotBounds is null || decision.VisualTarget is null)
                            throw new ContractValidationException("The visual target has no screenshot bounds.");
                        bounds = MapVisualTarget(request.ScreenshotBounds, decision.VisualTarget);
                        DesktopDiagnostics.WriteEvent(
                            "visual_target_mapped",
                            ("x", Math.Round(bounds.X)),
                            ("y", Math.Round(bounds.Y)),
                            ("width", Math.Round(bounds.Width)),
                            ("height", Math.Round(bounds.Height)));
                    }
                    else
                    {
                        selectedCandidate = currentObservation.Candidates.FirstOrDefault(candidate =>
                            string.Equals(candidate.Id, decision.TargetId, StringComparison.Ordinal));
                        object? selectedAutomationId = null;
                        object? selectedProcessName = null;
                        object? selectedContainerLabel = null;
                        selectedCandidate?.Attributes?.TryGetValue("automationId", out selectedAutomationId);
                        selectedCandidate?.Attributes?.TryGetValue("processName", out selectedProcessName);
                        selectedCandidate?.Attributes?.TryGetValue("containerLabel", out selectedContainerLabel);
                        DesktopDiagnostics.WriteEvent(
                            "selected_candidate",
                            ("id", decision.TargetId),
                            ("label", selectedCandidate?.Label),
                            ("description", selectedCandidate?.Description),
                            ("role", selectedCandidate?.Role),
                            ("automationId", selectedAutomationId),
                            ("processName", selectedProcessName),
                            ("containerLabel", selectedContainerLabel));
                        if (!currentObservation.Registry.TryResolveState(
                                decision.TargetId!,
                                out bounds,
                                out isOffscreen))
                            throw new ContractValidationException("The selected Windows element is stale.");
                        selectedLabel = selectedCandidate?.Label ?? decision.TargetId!;
                    }
                    _lastHighlightedCandidate = selectedCandidate;
                    _lastHighlightedBounds = bounds;
                    AttachTrainingContext(answerMessage, decision, selectedLabel, bounds);
                    _session = TaskSessionStateMachine.GuidanceReady(_session, decision.Message, decision.ExpectedChange);
                    if (isOffscreen)
                    {
                        _overlay.ShowScrollHint(bounds, decision.Message);
                        StatusText = bounds.Y >= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight
                            ? "아래로 스크롤해 주세요"
                            : "위로 스크롤해 주세요";
                        var visibleBounds = await currentObservation.Registry.WaitForVisibleBoundsAsync(
                            decision.TargetId!,
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
                        DesktopDiagnostics.WriteEvent(
                            "overlay_show_target",
                            ("x", Math.Round(bounds.X)),
                            ("y", Math.Round(bounds.Y)),
                            ("width", Math.Round(bounds.Width)),
                            ("height", Math.Round(bounds.Height)));
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
                    _session = TaskSessionStateMachine.StepCompleted(
                        _session,
                        $"사용자가 '{selectedLabel}' 컨트롤을 클릭함. 이전 안내: {decision.Message}");
                    if (selectedCandidate is not null
                        && WindowsSystemOutcomeResolver.TryResolve(
                            _session.OriginalUserMessage,
                            selectedCandidate,
                            out var completedMessage))
                    {
                        Messages.Add(CreateAssistantMessage("assistant", completedMessage));
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
            StatusText = _session?.Status == TaskStatuses.Completed
                ? "작업 완료"
                : IsPaused ? "일시 정지됨" : "작업 취소됨";
            if (pendingMessage is not null && pendingMessage.IsPending)
                Messages.Remove(pendingMessage);
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

    private async Task<(GuideDecision Decision, GuideRequest Request)> RequestVisionDecisionAsync(
        GuideRequest request,
        CancellationToken cancellationToken)
    {
        _overlay.Clear();
        StatusText = "화면을 보고 정확한 위치를 찾는 중";
        var capture = await _screenCapture.CaptureAsync(cancellationToken);
        var visionRequest = request with
        {
            // Reaching this path means UI Automation did not find the requested
            // control. Do not let a generic candidate such as Search distract the
            // vision model from a more direct control that is visible in pixels.
            Candidates = [],
            Screenshot = capture.DataUrl,
            ScreenshotBounds = capture.Bounds,
        };
        var decision = await _apiClient.DecideNextActionAsync(visionRequest, cancellationToken);
        return (decision, visionRequest);
    }

    private static UiBounds MapVisualTarget(UiBounds screenshotBounds, VisualTarget target)
    {
        var width = Math.Min(screenshotBounds.Width, Math.Max(8, target.Width * screenshotBounds.Width));
        var height = Math.Min(screenshotBounds.Height, Math.Max(8, target.Height * screenshotBounds.Height));
        var x = Math.Clamp(
            screenshotBounds.X + target.X * screenshotBounds.Width,
            screenshotBounds.X,
            screenshotBounds.X + screenshotBounds.Width - width);
        var y = Math.Clamp(
            screenshotBounds.Y + target.Y * screenshotBounds.Height,
            screenshotBounds.Y,
            screenshotBounds.Y + screenshotBounds.Height - height);
        return new UiBounds(x, y, width, height);
    }

    private static string DescribeClarificationChoice(UiCandidate candidate)
    {
        var label = candidate.Label ?? candidate.Description ?? candidate.Role;
        if (candidate.Attributes?.TryGetValue("sourceScope", out var scopeValue) != true)
            return label;
        return Convert.ToString(scopeValue) switch
        {
            "browser_content" => $"웹사이트 안의 {label}",
            "browser_chrome" => $"브라우저 주소창의 {label}",
            _ => label,
        };
    }

    private bool CanCaptureCorrection() =>
        _session is not null && _lastObservation is not null
        && _correctionAnswer is not null && IsCorrectionEditorVisible;

    private bool CanSaveCorrection() =>
        _session is not null && _correctionAnswer is not null
        && IsCorrectionEditorVisible
        && (_pendingCorrectionSelection is not null
            || !string.IsNullOrWhiteSpace(CorrectionIntentText)
            || !string.IsNullOrWhiteSpace(CorrectionCommentText));

    private bool CanAnalyzeTeaching() => IsCorrectionEditorVisible && !IsLoading
        && !string.IsNullOrWhiteSpace(TeachingUserQuestion)
        && !string.IsNullOrWhiteSpace(TeachingCurrentStage)
        && !string.IsNullOrWhiteSpace(TeachingDeveloperCorrection);

    private bool CanValidateTeaching() => IsCorrectionEditorVisible && !IsLoading
        && !string.IsNullOrWhiteSpace(TeachingPreviewJson);

    private bool CanSaveApprovedGold() => IsCorrectionEditorVisible && !IsLoading
        && TeachingValidationPassed && !_teachingGoldSaved;

    private static bool CanEvaluateAnswer(object? parameter) =>
        parameter is ChatMessageItem { CanEvaluate: true };

    private bool CanMarkTaskCompleted(object? parameter) =>
        parameter is ChatMessageItem { CanEvaluate: true }
        && _session is not null
        && _lastObservation is not null;

    private async Task MarkAnswerCorrectAsync(object? parameter)
    {
        if (parameter is not ChatMessageItem message || !message.CanEvaluate) return;
        try
        {
            await _correctionStore.SaveFeedbackAsync(CreateFeedbackRecord(message, "correct"));
            message.MarkEvaluated("correct");
            StatusText = "좋은 답변으로 영구 저장됨";
        }
        catch (Exception exception)
        {
            DesktopDiagnostics.Write(exception);
            StatusText = "평가 저장 오류";
        }
    }

    private async Task MarkAnswerIncorrectAsync(object? parameter)
    {
        if (parameter is not ChatMessageItem message || !message.CanEvaluate) return;
        try
        {
            var feedback = await _correctionStore.SaveFeedbackAsync(CreateFeedbackRecord(message, "incorrect"));
            message.MarkEvaluated("incorrect");
            BeginCorrectionDraft(message, feedback);
            StatusText = "X 저장됨 · 수정 내용을 작성해 주세요";
        }
        catch (Exception exception)
        {
            DesktopDiagnostics.Write(exception);
            StatusText = "평가 저장 오류";
        }
    }

    private async Task MarkTaskCompletedAsync(object? parameter)
    {
        if (parameter is not ChatMessageItem message || !CanMarkTaskCompleted(message)) return;
        var observation = _lastObservation!;
        var session = _session!;
        try
        {
            var goal = message.OriginalGoal
                ?? (string.IsNullOrWhiteSpace(_submittedGoal) ? session.OriginalUserMessage : _submittedGoal);
            var effectiveGoal = message.EffectiveGoal ?? session.OriginalUserMessage;
            var completionMessage = $"좋아요. '{goal}'의 목표는 이 화면에서 완료된 것으로 기억할게요.";
            var record = new DeveloperCompletionRecord(
                1,
                Guid.NewGuid().ToString("D"),
                DateTimeOffset.UtcNow,
                goal,
                effectiveGoal,
                observation.Context,
                observation.SnapshotHash,
                session.CompletedSteps,
                DeveloperCompletionMatcher.CaptureEvidence(observation.Candidates),
                completionMessage);
            await _correctionStore.SaveFeedbackAsync(CreateFeedbackRecord(message, "completed"));
            await _correctionStore.SaveCompletionAsync(record);
            DesktopDiagnostics.WriteEvent(
                "developer_completion_saved",
                ("completionId", record.Id),
                ("evidenceCount", record.VisibleSemantics.Count));
            message.MarkEvaluated("completed");
            var pendingMessages = Messages.Where(item => item.IsPending).ToArray();
            foreach (var pending in pendingMessages)
            {
                pending.Text = completionMessage;
                pending.IsPending = false;
                pending.MarkEvaluated("completed");
            }
            CancelRunningWork(markCancelled: false);
            ClearClarificationChoices();
            _session = TaskSessionStateMachine.Completed(session, completionMessage);
            _lastDecision = new GuideDecision(
                GuideStatuses.Completed,
                GuideActions.Explain,
                completionMessage,
                1);
            if (pendingMessages.Length == 0)
                Messages.Add(new ChatMessageItem("status", completionMessage));
            StatusText = "완료 지점 저장됨 · 작업 종료";
        }
        catch (Exception exception)
        {
            DesktopDiagnostics.Write(exception);
            StatusText = "완료 지점 저장 오류";
        }
        finally
        {
            RaiseCommandStates();
        }
    }

    private void CloseCorrectionEditor()
    {
        ResetCorrectionDraft();
        TeachingEditorClosed?.Invoke();
        _overlay.Clear();
        StatusText = "교정 취소됨";
    }

    private void BeginCorrectionDraft(ChatMessageItem answer, AnswerFeedbackRecord? feedback)
    {
        CancelRunningWork(markCancelled: false);
        _overlay.Clear();
        _correctionAnswer = answer;
        _correctionFeedback = feedback;
        CorrectionIntentText = string.Empty;
        CorrectionCommentText = string.Empty;
        CorrectionSelectionSummary = "정답 영역을 아직 선택하지 않았습니다.";
        _pendingCorrectionSelection = null;
        _pendingCorrectionCandidate = null;
        _pendingCorrectionCapture = null;
        TeachingUserQuestion = answer.OriginalGoal
            ?? (string.IsNullOrWhiteSpace(_submittedGoal) ? _session?.OriginalUserMessage ?? string.Empty : _submittedGoal);
        var context = answer.Context ?? _lastObservation?.Context;
        TeachingCurrentStage = string.Join(Environment.NewLine, new[]
        {
            context is null ? null : $"현재 앱: {context.ApplicationName}",
            string.IsNullOrWhiteSpace(context?.WindowTitle) ? null : $"현재 화면: {context.WindowTitle}",
            string.IsNullOrWhiteSpace(_session?.CurrentStep) ? null : $"현재 안내 단계: {_session.CurrentStep}",
            _session?.CompletedSteps.Count > 0 ? $"완료 단계: {string.Join(" > ", _session.CompletedSteps)}" : null,
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
        TeachingDeveloperCorrection = string.Empty;
        TeachingPreviewJson = string.Empty;
        TeachingValidationSummary = "세 항목을 작성하고 '분석 및 라벨링'을 눌러주세요.";
        TeachingValidationPassed = false;
        _teachingGoldSaved = false;
        IsCorrectionEditorVisible = true;
        TeachingEditorRequested?.Invoke();
    }

    private async Task AnalyzeTeachingAsync()
    {
        if (!CanAnalyzeTeaching()) return;
        try
        {
            IsLoading = true;
            TeachingValidationSummary = "Featherless가 의미 라벨 초안을 만드는 중입니다.";
            var candidates = (_lastObservation?.Candidates ?? [])
                .Where(candidate => candidate.Visible)
                .Take(100)
                .Select(candidate => new TeachingCandidate(
                    candidate.Label ?? candidate.Description ?? candidate.Role,
                    candidate.Role,
                    candidate.Enabled))
                .ToArray();
            var input = new TeachingInput(
                TeachingUserQuestion.Trim(),
                TeachingCurrentStage.Trim(),
                TeachingDeveloperCorrection.Trim(),
                _lastObservation?.Context,
                candidates);
            TeachingPreviewJson = await _teachingClient.AnalyzeAsync(input, CancellationToken.None);
            TeachingValidationSummary = "AI 초안입니다. 오른쪽 JSON을 직접 검토·수정한 뒤 검증하세요.";
            StatusText = "Semantic v2 라벨 초안 생성됨";
        }
        catch (Exception exception)
        {
            DesktopDiagnostics.Write(exception);
            TeachingValidationSummary = "라벨 초안을 만들지 못했습니다. 백엔드와 모델 설정을 확인해 주세요.";
            StatusText = "라벨링 오류";
        }
        finally
        {
            IsLoading = false;
            RaiseCommandStates();
        }
    }

    private async Task ValidateTeachingAsync()
    {
        if (!CanValidateTeaching()) return;
        try
        {
            IsLoading = true;
            var result = await _teachingClient.ValidateAsync(TeachingPreviewJson, CancellationToken.None);
            var details = result.Issues.Count == 0
                ? string.Empty
                : Environment.NewLine + string.Join(Environment.NewLine, result.Issues.Select(issue =>
                    $"[{issue.Severity}] {issue.Code}: {issue.Message}"));
            TeachingValidationSummary = $"{result.ShortReason} (신뢰도 {result.Confidence:P0}){details}";
            TeachingValidationPassed = result.Valid;
            StatusText = result.Valid ? "Gold 저장 가능 · 개발자 최종 승인 필요" : "라벨 검증 실패";
        }
        catch (Exception exception)
        {
            DesktopDiagnostics.Write(exception);
            TeachingValidationPassed = false;
            TeachingValidationSummary = "JSON 형식 또는 의미 검증에 실패했습니다.";
            StatusText = "라벨 검증 오류";
        }
        finally
        {
            IsLoading = false;
            RaiseCommandStates();
        }
    }

    private async Task SaveApprovedGoldAsync()
    {
        if (!CanSaveApprovedGold()) return;
        try
        {
            IsLoading = true;
            await _teachingClient.SaveApprovedAsync(TeachingPreviewJson, Environment.UserName, CancellationToken.None);
            _teachingGoldSaved = true;
            TeachingValidationPassed = false;
            TeachingValidationSummary = "사람이 검토하고 승인한 Semantic v2 Gold로 저장했습니다.";
            StatusText = "Gold 학습 데이터 저장 완료";
            Messages.Add(CreateAssistantMessage("assistant", "검수한 의미 데이터를 Gold에 저장했어요. 좌표는 학습 지식에 포함하지 않았습니다."));
        }
        catch (Exception exception)
        {
            DesktopDiagnostics.Write(exception);
            TeachingValidationPassed = false;
            TeachingValidationSummary = "Gold 저장 직전 검증에서 차단됐습니다. 오류를 수정하고 다시 검증해 주세요.";
            StatusText = "Gold 저장 차단";
        }
        finally
        {
            IsLoading = false;
            RaiseCommandStates();
        }
    }

    private void InvalidateTeachingValidation()
    {
        if (TeachingValidationPassed)
            TeachingValidationPassed = false;
        _teachingGoldSaved = false;
        RaiseCommandStates();
    }

    private async Task CaptureCorrectionAsync()
    {
        if (_session is null || _lastObservation is null || !CanCaptureCorrection()) return;
        var observation = _lastObservation;
        CancelRunningWork(markCancelled: false);
        _overlay.Clear();
        IsCorrectionEditorVisible = false;

        UiBounds? selection = null;
        try
        {
            CorrectionSelectionStarted?.Invoke();
            selection = await _correctionSelection.SelectRegionAsync();
        }
        finally
        {
            CorrectionSelectionCompleted?.Invoke();
        }

        if (selection is null)
        {
            IsCorrectionEditorVisible = true;
            StatusText = "정답 영역 선택 취소됨";
            return;
        }

        try
        {
            StatusText = "선택 영역 미리보기 준비 중";
            var capture = await _screenCapture.CaptureAsync(CancellationToken.None);
            var matchedCandidate = DeveloperCorrectionMatcher.FindSelectedCandidate(
                selection,
                observation.Candidates);
            var targetLabel = matchedCandidate?.Label
                ?? matchedCandidate?.Description
                ?? "사용자가 선택한 정답 영역";
            _pendingCorrectionSelection = selection;
            _pendingCorrectionCandidate = matchedCandidate;
            _pendingCorrectionCapture = capture;
            CorrectionSelectionSummary = matchedCandidate is null
                ? $"선택됨: 화면 영역 {Math.Round(selection.X)}, {Math.Round(selection.Y)} · 저장 전"
                : $"선택됨: {targetLabel} · 저장 전";
            var displayBounds = matchedCandidate?.Bounds ?? selection;
            _overlay.ShowTarget(displayBounds, "교정 미리보기입니다. 확인 후 저장을 눌러 주세요.");
            TargetHighlighted?.Invoke(displayBounds);
            IsCorrectionEditorVisible = true;
            StatusText = "선택 영역 확인 중 · 아직 저장되지 않음";
        }
        catch (Exception exception)
        {
            DesktopDiagnostics.Write(exception);
            ErrorMessage = "선택한 정답 영역을 저장하지 못했어요.";
            StatusText = "교정 저장 오류";
            IsCorrectionEditorVisible = true;
        }
    }

    private async Task SaveCorrectionAsync()
    {
        if (_session is null || _correctionAnswer is null || !CanSaveCorrection()) return;
        try
        {
            var refined = DeveloperCommentRefiner.Refine(CorrectionCommentText);
            var correctedIntent = string.IsNullOrWhiteSpace(CorrectionIntentText)
                ? null
                : CorrectionIntentText.Trim();
            VisualTarget? normalizedTarget = null;
            CorrectionTargetSignature? signature = null;
            if (_pendingCorrectionSelection is not null && _pendingCorrectionCapture is not null)
            {
                var label = _pendingCorrectionCandidate?.Label
                    ?? _pendingCorrectionCandidate?.Description
                    ?? "사용자가 선택한 정답 영역";
                normalizedTarget = DeveloperCorrectionMatcher.NormalizeSelection(
                    _pendingCorrectionCapture.Bounds,
                    _pendingCorrectionSelection,
                    label);
                if (_pendingCorrectionCandidate is not null)
                    signature = DeveloperCorrectionMatcher.CreateSignature(_pendingCorrectionCandidate);
            }

            var record = CreateCorrectionRecord(
                correctedIntent,
                _pendingCorrectionSelection,
                normalizedTarget,
                signature,
                refined);
            var saved = await _correctionStore.SaveAsync(
                record,
                SaveCorrectionScreenshot ? _pendingCorrectionCapture?.DataUrl : null);
            var selectedCandidate = _pendingCorrectionCandidate;
            var selectedLabel = selectedCandidate?.Label
                ?? selectedCandidate?.Description
                ?? normalizedTarget?.Label
                ?? "정답 위치";
            var immediateDecision = DeveloperCorrectionApplication.CreateImmediateDecision(
                selectedCandidate,
                normalizedTarget,
                selectedLabel);
            var immediateCapture = immediateDecision?.Action == GuideActions.HighlightVisual
                ? _pendingCorrectionCapture
                : null;
            var observation = _lastObservation;
            ResetCorrectionDraft(clearOverlay: false);
            StatusText = $"교정 저장됨 · {saved.Id[..8]}";

            if (!string.IsNullOrWhiteSpace(correctedIntent))
                _session = TaskSessionStateMachine.Create(correctedIntent);

            if (immediateDecision is not null)
            {
                _forcedDecision = immediateDecision;
                _forcedVisualCapture = immediateCapture;
                _taskCancellation = new CancellationTokenSource();
                await RunGuidanceLoopAsync(observation, _taskCancellation.Token);
            }
            else if (!string.IsNullOrWhiteSpace(correctedIntent))
            {
                _taskCancellation = new CancellationTokenSource();
                await RunGuidanceLoopAsync(observation, _taskCancellation.Token);
            }
            else
            {
                _overlay.Clear();
                Messages.Add(CreateAssistantMessage(
                    "assistant",
                    "교정 메모를 저장했어요. 정답 위치를 선택하면 현재 화면에도 바로 적용할 수 있어요."));
            }
        }
        catch (Exception exception)
        {
            DesktopDiagnostics.Write(exception);
            ErrorMessage = "교정 데이터를 저장하지 못했어요.";
            StatusText = "교정 저장 오류";
        }
    }

    private DeveloperCorrectionRecord CreateCorrectionRecord(
        string? correctedIntent,
        UiBounds? selectedBounds,
        VisualTarget? normalizedTarget,
        CorrectionTargetSignature? signature,
        RefinedDeveloperComment refinedComment)
    {
        var context = _correctionAnswer?.Context ?? _lastObservation?.Context
            ?? new ApplicationContext(Platforms.Windows, "unknown", Locale: System.Globalization.CultureInfo.CurrentUICulture.Name);
        return new DeveloperCorrectionRecord(
            1,
            Guid.NewGuid().ToString("D"),
            DateTimeOffset.UtcNow,
            _correctionAnswer?.OriginalGoal
                ?? (string.IsNullOrWhiteSpace(_submittedGoal) ? _session!.OriginalUserMessage : _submittedGoal),
            _correctionAnswer?.EffectiveGoal ?? _session!.OriginalUserMessage,
            correctedIntent,
            context,
            _correctionAnswer?.SnapshotHash ?? _lastObservation?.SnapshotHash,
            _correctionAnswer?.Decision?.Action ?? _lastDecision?.Action,
            _correctionAnswer?.Decision?.TargetId ?? _lastDecision?.TargetId,
            _correctionAnswer?.TargetLabel
                ?? _lastHighlightedCandidate?.Label
                ?? _lastDecision?.VisualTarget?.Label,
            _correctionAnswer?.TargetBounds ?? _lastHighlightedBounds,
            selectedBounds,
            normalizedTarget,
            signature,
            null,
            true,
            _correctionFeedback?.Id,
            string.IsNullOrWhiteSpace(refinedComment.Raw) ? null : refinedComment.Raw,
            string.IsNullOrWhiteSpace(refinedComment.Normalized) ? null : refinedComment.Normalized,
            refinedComment.IssueTags.Count == 0 ? null : refinedComment.IssueTags);
    }

    private AnswerFeedbackRecord CreateFeedbackRecord(ChatMessageItem message, string rating) => new(
        1,
        Guid.NewGuid().ToString("D"),
        DateTimeOffset.UtcNow,
        rating,
        message.Id,
        message.Text,
        message.OriginalGoal,
        message.EffectiveGoal,
        message.Context,
        message.SnapshotHash,
        message.Decision?.Action,
        message.Decision?.TargetId,
        message.TargetLabel ?? message.Decision?.VisualTarget?.Label,
        message.TargetBounds);

    private void ResetCorrectionDraft(bool clearOverlay = true)
    {
        IsCorrectionEditorVisible = false;
        CorrectionIntentText = string.Empty;
        CorrectionCommentText = string.Empty;
        CorrectionSelectionSummary = "정답 영역을 아직 선택하지 않았습니다.";
        _correctionAnswer = null;
        _correctionFeedback = null;
        _pendingCorrectionSelection = null;
        _pendingCorrectionCandidate = null;
        _pendingCorrectionCapture = null;
        _teachingUserQuestion = string.Empty;
        _teachingCurrentStage = string.Empty;
        _teachingDeveloperCorrection = string.Empty;
        _teachingPreviewJson = string.Empty;
        _teachingValidationSummary = "분석 전입니다.";
        _teachingValidationPassed = false;
        _teachingGoldSaved = false;
        OnPropertyChanged(nameof(TeachingUserQuestion));
        OnPropertyChanged(nameof(TeachingCurrentStage));
        OnPropertyChanged(nameof(TeachingDeveloperCorrection));
        OnPropertyChanged(nameof(TeachingPreviewJson));
        OnPropertyChanged(nameof(TeachingValidationSummary));
        OnPropertyChanged(nameof(TeachingValidationPassed));
        if (clearOverlay) _overlay.Clear();
    }

    private void CancelCurrentTask()
    {
        CancelRunningWork(markCancelled: true);
        ClearClarificationChoices();
        StatusText = "작업 취소됨";
        Messages.Add(CreateAssistantMessage("assistant", "안내를 취소했어요. 새로운 목표를 입력해 주세요."));
    }

    private void TogglePause()
    {
        IsPaused = !IsPaused;
        if (IsPaused)
        {
            CancelRunningWork(markCancelled: false);
            ClearClarificationChoices();
            StatusText = "일시 정지됨";
            Messages.Add(CreateAssistantMessage("assistant", "화면 관찰을 일시 정지했어요."));
        }
        else
        {
            StatusText = "준비됨";
            Messages.Add(CreateAssistantMessage("assistant", "다시 시작할 목표를 입력해 주세요."));
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
        _forcedVisualCapture = null;
        if (markCancelled && _session is not null)
            _session = TaskSessionStateMachine.Cancelled(_session);
    }

    private void RaiseCommandStates()
    {
        (SubmitCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (RecoveryCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (CancelCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (CaptureCorrectionCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (SaveCorrectionCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (AnalyzeTeachingCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (ValidateTeachingCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (SaveApprovedGoldCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (MarkAnswerCorrectCommand as AsyncParameterRelayCommand)?.RaiseCanExecuteChanged();
        (MarkAnswerIncorrectCommand as AsyncParameterRelayCommand)?.RaiseCanExecuteChanged();
        (MarkTaskCompletedCommand as AsyncParameterRelayCommand)?.RaiseCanExecuteChanged();
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
            Messages.Add(CreateAssistantMessage("assistant", message));
            return;
        }
        pendingMessage.Text = message;
        AttachTrainingContext(pendingMessage, _lastDecision);
        pendingMessage.IsPending = false;
    }

    private ChatMessageItem CreateAssistantMessage(string role, string text, bool isPending = false)
    {
        var message = new ChatMessageItem(role, text, isPending);
        AttachTrainingContext(message, _lastDecision);
        return message;
    }

    private void AttachTrainingContext(
        ChatMessageItem message,
        GuideDecision? decision,
        string? targetLabel = null,
        UiBounds? targetBounds = null)
    {
        message.AttachTrainingContext(
            string.IsNullOrWhiteSpace(_submittedGoal) ? _session?.OriginalUserMessage : _submittedGoal,
            _session?.OriginalUserMessage,
            _lastObservation?.Context,
            _lastObservation?.SnapshotHash,
            decision,
            targetLabel,
            targetBounds);
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
