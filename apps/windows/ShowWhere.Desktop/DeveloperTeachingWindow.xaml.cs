using System.ComponentModel;
using System.Windows;

namespace ShowWhere.Desktop;

public partial class DeveloperTeachingWindow : Window
{
    private bool _allowClose;

    public DeveloperTeachingWindow()
    {
        InitializeComponent();
    }

    public void CloseForShutdown()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs eventArgs)
    {
        if (!_allowClose)
        {
            eventArgs.Cancel = true;
            Hide();
        }
        base.OnClosing(eventArgs);
    }
}
