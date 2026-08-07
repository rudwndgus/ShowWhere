using System.Security.Cryptography;
using System.Text;
using System.Windows.Automation;
using ShowWhere.Core;

namespace ShowWhere.WindowsAutomation;

public sealed class CandidateRegistry
{
    private readonly IReadOnlyDictionary<string, AutomationElement> _elements;

    internal CandidateRegistry(IReadOnlyDictionary<string, AutomationElement> elements) => _elements = elements;

    public bool TryResolveBounds(string candidateId, out UiBounds bounds)
    {
        bounds = new UiBounds(0, 0, 0, 0);
        if (!_elements.TryGetValue(candidateId, out var element)) return false;
        try
        {
            var rectangle = element.Current.BoundingRectangle;
            if (rectangle.IsEmpty || rectangle.Width <= 1 || rectangle.Height <= 1 || element.Current.IsOffscreen)
                return false;
            bounds = new UiBounds(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
            return true;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }
}

public sealed class WindowsObservation
{
    internal WindowsObservation(
        ApplicationContext context,
        IReadOnlyList<UiCandidate> candidates,
        CandidateRegistry registry,
        string snapshotHash,
        IReadOnlyList<AutomationElement> roots,
        string? focusedElementKey)
    {
        Context = context;
        Candidates = candidates;
        Registry = registry;
        SnapshotHash = snapshotHash;
        Roots = roots;
        FocusedElementKey = focusedElementKey;
    }

    public ApplicationContext Context { get; }
    public IReadOnlyList<UiCandidate> Candidates { get; }
    public CandidateRegistry Registry { get; }
    public string SnapshotHash { get; }
    internal IReadOnlyList<AutomationElement> Roots { get; }
    internal string? FocusedElementKey { get; }

    internal static string ComputeHash(
        ApplicationContext context,
        IEnumerable<UiCandidate> candidates,
        string? focusedElementKey)
    {
        var content = new StringBuilder(context.ApplicationName).Append('|').Append(context.WindowTitle)
            .Append("|focus:").Append(focusedElementKey);
        foreach (var candidate in candidates)
        {
            if (candidate.Attributes?.TryGetValue("sourceScope", out var sourceScope) == true
                && string.Equals(Convert.ToString(sourceScope), "windows_taskbar", StringComparison.Ordinal))
                continue;
            content.Append('|').Append(candidate.Id).Append(':').Append(candidate.Label)
                .Append(':').Append(Math.Round(candidate.Bounds.X)).Append(',').Append(Math.Round(candidate.Bounds.Y))
                .Append(',').Append(Math.Round(candidate.Bounds.Width)).Append(',').Append(Math.Round(candidate.Bounds.Height));
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content.ToString()))).ToLowerInvariant();
    }
}

public sealed class WindowsObservationException : Exception
{
    public WindowsObservationException() : base("현재 활성 Windows 화면을 확인할 수 없어요.") { }
}

public interface IWindowsUiObserver
{
    void RememberCurrentForegroundWindow();
    Task<WindowsObservation> ObserveAsync(CancellationToken cancellationToken);
}
