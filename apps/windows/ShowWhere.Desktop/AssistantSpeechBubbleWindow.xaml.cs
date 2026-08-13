using System.Windows;

namespace ShowWhere.Desktop;

public partial class AssistantSpeechBubbleWindow : Window
{
    public static readonly DependencyProperty BubbleTextProperty = DependencyProperty.Register(
        nameof(BubbleText), typeof(string), typeof(AssistantSpeechBubbleWindow),
        new PropertyMetadata(string.Empty));

    public string BubbleText
    {
        get => (string)GetValue(BubbleTextProperty);
        set => SetValue(BubbleTextProperty, value);
    }

    public AssistantSpeechBubbleWindow() => InitializeComponent();

    public void PositionNear(double assistantLeft, double assistantTop, double assistantWidth)
    {
        var work = SystemParameters.WorkArea;
        Left = Math.Clamp(assistantLeft - Width + assistantWidth + 20, work.Left + 8, Math.Max(work.Left + 8, work.Right - Width - 8));
        Top = Math.Clamp(assistantTop - Height + 31, work.Top + 8, Math.Max(work.Top + 8, work.Bottom - Height - 8));
    }
}
