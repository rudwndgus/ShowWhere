using ShowWhere.WindowsAutomation;

namespace ShowWhere.Windows.Tests;

public sealed class WindowsUiObserverSourceKeyTests
{
    [Fact]
    public void Runtime_ids_from_different_processes_cannot_collide()
    {
        var chrome = WindowsUiObserver.ComposeSourceKey(100, [42, 7], "", 0);
        var explorer = WindowsUiObserver.ComposeSourceKey(200, [42, 7], "", 0);

        Assert.NotEqual(chrome, explorer);
        Assert.StartsWith("100:", chrome);
        Assert.StartsWith("200:", explorer);
    }
}
