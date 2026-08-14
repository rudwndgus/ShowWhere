using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ShowWhere.Desktop;

public partial class AssistantSpeechBubbleWindow : Window
{
    private readonly AssistantCharacterStore _characterStore;

    public static readonly DependencyProperty BubbleTextProperty = DependencyProperty.Register(
        nameof(BubbleText), typeof(string), typeof(AssistantSpeechBubbleWindow),
        new PropertyMetadata(string.Empty));

    public string BubbleText
    {
        get => (string)GetValue(BubbleTextProperty);
        set => SetValue(BubbleTextProperty, value);
    }

    public AssistantSpeechBubbleWindow(AssistantCharacterStore characterStore)
    {
        _characterStore = characterStore;
        InitializeComponent();
        ApplyCharacterTheme();
        _characterStore.PropertyChanged += OnCharacterChanged;
    }

    protected override void OnSourceInitialized(EventArgs eventArgs)
    {
        base.OnSourceInitialized(eventArgs);
        WindowCaptureProtection.Apply(new WindowInteropHelper(this).Handle);
    }

    public void PositionNear(double assistantLeft, double assistantTop, double assistantWidth)
    {
        var leftEdge = SystemParameters.VirtualScreenLeft + 8;
        var topEdge = SystemParameters.VirtualScreenTop + 8;
        var rightEdge = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Width - 8;
        var bottomEdge = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Height - 8;
        Left = Math.Clamp(assistantLeft - Width + assistantWidth + 20, leftEdge, Math.Max(leftEdge, rightEdge));
        Top = Math.Clamp(assistantTop - Height + 31, topEdge, Math.Max(topEdge, bottomEdge));
    }

    protected override void OnClosed(EventArgs eventArgs)
    {
        _characterStore.PropertyChanged -= OnCharacterChanged;
        base.OnClosed(eventArgs);
    }

    private void OnCharacterChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(AssistantCharacterStore.SelectedCharacterKey))
            ApplyCharacterTheme();
    }

    private void ApplyCharacterTheme()
    {
        var accent = _characterStore.SelectedCharacterKey switch
        {
            "robot" => Color.FromRgb(0x3D, 0x8B, 0xEF),
            "duck" => Color.FromRgb(0xE2, 0x9B, 0x18),
            _ => Color.FromRgb(0x47, 0x23, 0x23),
        };
        Resources["BubbleAccentBrush"] = new SolidColorBrush(accent);
    }
}
