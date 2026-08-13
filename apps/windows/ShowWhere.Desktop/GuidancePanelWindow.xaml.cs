using System.ComponentModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ShowWhere.Core;

namespace ShowWhere.Desktop;

public partial class GuidancePanelWindow : Window
{
    private bool _shutdown;
    private INotifyCollectionChanged? _observedMessages;

    public AssistantCharacterStore CharacterStore { get; }
    public event Action? GoalSubmittedFromKeyboard;

    public GuidancePanelWindow(AssistantCharacterStore characterStore)
    {
        CharacterStore = characterStore;
        InitializeComponent();
        CharacterStore.PropertyChanged += OnCharacterStorePropertyChanged;
        ApplyCharacterTheme();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnCharacterStorePropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(AssistantCharacterStore.SelectedCharacterKey))
            ApplyCharacterTheme();
    }

    private void ApplyCharacterTheme()
    {
        var accent = CharacterStore.SelectedCharacterKey == "robot"
            ? Color.FromRgb(0x3D, 0x8B, 0xEF)
            : Color.FromRgb(0x47, 0x23, 0x23);
        Resources["AccentBrush"] = new SolidColorBrush(accent);
        Resources["AccentDarkBrush"] = new SolidColorBrush(accent);
        Resources["AccentSoftBrush"] = new SolidColorBrush(Color.FromArgb(0x80, accent.R, accent.G, accent.B));
    }

    protected override void OnSourceInitialized(EventArgs eventArgs)
    {
        base.OnSourceInitialized(eventArgs);
        WindowCaptureProtection.Apply(new WindowInteropHelper(this).Handle);
    }

    public void PositionNear(double assistantLeft, double assistantTop, double assistantWidth, double assistantHeight)
    {
        var work = SystemParameters.WorkArea;
        var candidateLeft = assistantLeft + assistantWidth + 12;
        Left = candidateLeft + Width <= work.Right ? candidateLeft : assistantLeft - Width - 12;
        Top = Math.Clamp(assistantTop - Height / 2 + assistantHeight / 2, work.Top, Math.Max(work.Top, work.Bottom - Height));
    }

    public void MoveAwayFrom(UiBounds target)
    {
        Dispatcher.Invoke(() =>
        {
            var panel = new Rect(Left, Top, ActualWidth > 0 ? ActualWidth : Width, ActualHeight > 0 ? ActualHeight : Height);
            var targetRectangle = new Rect(target.X, target.Y, target.Width, target.Height);
            if (!panel.IntersectsWith(targetRectangle)) return;
            var work = SystemParameters.WorkArea;
            Left = targetRectangle.Right + Width + 16 <= work.Right
                ? targetRectangle.Right + 16
                : Math.Max(work.Left, targetRectangle.Left - Width - 16);
            Top = Math.Clamp(Top, work.Top, Math.Max(work.Top, work.Bottom - Height));
        });
    }

    public void FocusGoalInput() => GoalInput.Focus();

    public void CloseForShutdown()
    {
        _shutdown = true;
        Close();
    }

    private void OnClosing(object? sender, CancelEventArgs eventArgs)
    {
        if (_shutdown) return;
        eventArgs.Cancel = true;
        Hide();
    }

    private void OnMinimize(object sender, RoutedEventArgs eventArgs) => WindowState = WindowState.Minimized;

    private void OnHide(object sender, RoutedEventArgs eventArgs) => Hide();

    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.LeftButton != MouseButtonState.Pressed
            || FindAncestor<ButtonBase>(eventArgs.OriginalSource as DependencyObject) is not null) return;
        try
        {
            DragMove();
            eventArgs.Handled = true;
        }
        catch (InvalidOperationException) { }
    }

    private static T? FindAncestor<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element is not null)
        {
            if (element is T match) return match;
            element = VisualTreeHelper.GetParent(element);
        }
        return null;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs eventArgs)
    {
        if (_observedMessages is not null) _observedMessages.CollectionChanged -= OnMessagesChanged;
        _observedMessages = (eventArgs.NewValue as GuidanceViewModel)?.Messages;
        if (_observedMessages is not null) _observedMessages.CollectionChanged += OnMessagesChanged;
        ScrollChatToEnd();
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs) => ScrollChatToEnd();

    private void ScrollChatToEnd() => Dispatcher.BeginInvoke(
        () => ChatScroll.ScrollToEnd(),
        DispatcherPriority.Background);

    private void OnGoalInputPreviewKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
        eventArgs.Handled = true;
        if (DataContext is not GuidanceViewModel viewModel || !viewModel.SubmitCommand.CanExecute(null)) return;
        viewModel.SubmitCommand.Execute(null);
        GoalSubmittedFromKeyboard?.Invoke();
    }

    private void OnBubbleActionClick(object sender, RoutedEventArgs eventArgs) =>
        GoalSubmittedFromKeyboard?.Invoke();

    private void OnCharacterSelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (!IsLoaded || SettingsPopup is null) return;
        SettingsPopup.IsOpen = false;
        SettingsButton.IsChecked = false;
    }
}
