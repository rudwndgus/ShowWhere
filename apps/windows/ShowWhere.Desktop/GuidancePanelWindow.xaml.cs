using System.ComponentModel;
using System.Windows;
using ShowWhere.Core;

namespace ShowWhere.Desktop;

public partial class GuidancePanelWindow : Window
{
    private bool _shutdown;

    public GuidancePanelWindow() => InitializeComponent();

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
}
