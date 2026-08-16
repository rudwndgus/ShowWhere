using System.Net;
using System.Text;
using ShowWhere.Desktop;

namespace ShowWhere.Windows.Tests;

public sealed class ProductionUpdateServiceTests
{
    [Fact]
    public async Task LatestReleaseRequiresTrustedExeAndSha256()
    {
        var digest = new string('a', 64);
        var json = $$"""
            {"tag_name":"v2.2.0","assets":[{"name":"ShowWhere.exe","browser_download_url":"https://github.com/rudwndgus/ShowWhere/releases/download/v2.2.0/ShowWhere.exe","digest":"sha256:{{digest}}"}]}
            """;
        using var http = new HttpClient(new JsonHandler(json));

        var release = await ProductionUpdateService.ReadLatestReleaseAsync(http, CancellationToken.None);

        Assert.Equal(new Version(2, 2, 0), release.Version);
        Assert.Equal(digest, release.Sha256);
        Assert.Equal("github.com", release.DownloadUri.Host);
    }

    [Fact]
    public void FailedStartupRestoresPreviousExecutable()
    {
        var directory = Path.Combine(Path.GetTempPath(), "showwhere-update-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var source = Path.Combine(directory, "download.tmp");
            var target = Path.Combine(directory, "ShowWhere.exe");
            File.WriteAllText(source, "new executable", Encoding.UTF8);
            File.WriteAllText(target, "working executable", Encoding.UTF8);
            var digest = ProductionUpdateService.ComputeSha256(source);

            Assert.Throws<InvalidOperationException>(() => ProductionUpdateService.ReplaceWithRollback(
                source, target, digest, _ => false));

            Assert.Equal("working executable", File.ReadAllText(target, Encoding.UTF8));
            Assert.False(File.Exists(target + ".previous"));
            Assert.False(File.Exists(target + ".new"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void SuccessfulStartupKeepsNewExecutableAndRemovesBackup()
    {
        var directory = Path.Combine(Path.GetTempPath(), "showwhere-update-success-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var source = Path.Combine(directory, "download.tmp");
            var target = Path.Combine(directory, "ShowWhere.exe");
            File.WriteAllText(source, "new executable", Encoding.UTF8);
            File.WriteAllText(target, "working executable", Encoding.UTF8);

            ProductionUpdateService.ReplaceWithRollback(
                source, target, ProductionUpdateService.ComputeSha256(source), _ => true);

            Assert.Equal("new executable", File.ReadAllText(target, Encoding.UTF8));
            Assert.False(File.Exists(target + ".previous"));
            Assert.False(File.Exists(target + ".new"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private sealed class JsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
    }
}
