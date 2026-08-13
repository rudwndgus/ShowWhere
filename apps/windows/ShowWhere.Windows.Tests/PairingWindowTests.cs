using System.Net.Http;
using System.Threading;
using System.Windows.Threading;
using ShowWhere.ApiClient;
using ShowWhere.Desktop;

namespace ShowWhere.Windows.Tests;

public sealed class PairingWindowTests
{
    [Fact]
    public void Pairing_window_opens_without_a_read_only_binding_exception()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var http = new HttpClient();
                using var coordinator = new MobileRemoteCoordinator(
                    http,
                    new GuideApiClientOptions(
                        new Uri("https://showwhere.example/api/guide"),
                        TimeSpan.FromSeconds(5)),
                    Dispatcher.CurrentDispatcher);
                var window = new PairingWindow { DataContext = coordinator };
                window.Show();
                window.UpdateLayout();
                window.Close();
            }
            catch (Exception exception) { failure = exception; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Pairing window test timed out.");
        Assert.Null(failure);
    }
}
