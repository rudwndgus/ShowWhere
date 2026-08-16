using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;

namespace ShowWhere.Desktop;

internal sealed record ProductionRelease(Version Version, Uri DownloadUri, string Sha256);

internal static class ProductionUpdateService
{
    private const string LatestReleaseApi = "https://api.github.com/repos/rudwndgus/ShowWhere/releases/latest";
    private const string UpdateArgument = "--apply-update";
    private const long MaximumDownloadBytes = 300L * 1024 * 1024;

    public static bool TryRunInstaller(string[] args)
    {
        if (args.Length != 4 || !string.Equals(args[0], UpdateArgument, StringComparison.Ordinal)) return false;
        try
        {
            if (!int.TryParse(args[1], out var parentProcessId)) throw new InvalidDataException("Invalid parent process id.");
            ApplyWithRollback(parentProcessId, args[2], args[3]);
        }
        catch (Exception exception)
        {
            DesktopDiagnostics.Write(exception);
            MessageBox.Show(
                "The update could not be applied. Your existing ShowWhere installation has been preserved.",
                "ShowWhere Update",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        return true;
    }

    public static async Task CheckAndPromptAsync(
        Uri backendEndpoint,
        bool productionAuthenticated,
        CancellationToken cancellationToken = default)
    {
        if (!productionAuthenticated
            || !string.Equals(backendEndpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || string.Equals(Environment.GetEnvironmentVariable("SHOWWHERE_DISABLE_AUTO_UPDATE"), "1", StringComparison.Ordinal)) return;

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ShowWhere", CurrentVersion().ToString(3)));
            var release = await ReadLatestReleaseAsync(http, cancellationToken).ConfigureAwait(false);
            if (release.Version <= CurrentVersion()) return;

            var accepted = await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var message = $"ShowWhere {release.Version.ToString(3)} is available.\n\nUpdate now?";
                var owner = Application.Current.MainWindow;
                var result = owner is null
                    ? MessageBox.Show(message, "ShowWhere Update", MessageBoxButton.YesNo, MessageBoxImage.Information)
                    : MessageBox.Show(owner, message, "ShowWhere Update", MessageBoxButton.YesNo, MessageBoxImage.Information);
                return result == MessageBoxResult.Yes;
            });
            if (!accepted) return;

            var downloaded = await DownloadVerifiedAsync(http, release, cancellationToken).ConfigureAwait(false);
            var currentExecutable = Environment.ProcessPath ?? throw new InvalidOperationException("Current executable path is unavailable.");
            var startInfo = new ProcessStartInfo(downloaded) { UseShellExecute = false };
            startInfo.ArgumentList.Add(UpdateArgument);
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
            startInfo.ArgumentList.Add(currentExecutable);
            startInfo.ArgumentList.Add(release.Sha256);
            _ = Process.Start(startInfo) ?? throw new InvalidOperationException("Update installer did not start.");
            await Application.Current.Dispatcher.InvokeAsync(Application.Current.Shutdown);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            DesktopDiagnostics.Write(exception);
        }
    }

    internal static Version CurrentVersion()
    {
        var text = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
            ?? "0.0.0";
        var suffix = text.IndexOfAny(['+', '-']);
        var clean = suffix >= 0 ? text[..suffix] : text;
        return Version.TryParse(clean, out var version) ? version : new Version(0, 0, 0);
    }

    internal static async Task<ProductionRelease> ReadLatestReleaseAsync(HttpClient http, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(LatestReleaseApi, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var tag = root.GetProperty("tag_name").GetString()?.Trim().TrimStart('v', 'V');
        if (!Version.TryParse(tag, out var version)) throw new InvalidDataException("Latest release has an invalid version.");
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            if (!string.Equals(asset.GetProperty("name").GetString(), "ShowWhere.exe", StringComparison.Ordinal)) continue;
            var download = new Uri(asset.GetProperty("browser_download_url").GetString() ?? string.Empty, UriKind.Absolute);
            if (!string.Equals(download.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(download.Host, "github.com", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Release download host is not trusted.");
            var digest = asset.TryGetProperty("digest", out var digestElement) ? digestElement.GetString() : null;
            if (digest is null || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Release asset does not provide a SHA-256 digest.");
            var sha256 = digest[7..].Trim().ToLowerInvariant();
            if (sha256.Length != 64 || sha256.Any(character => !Uri.IsHexDigit(character)))
                throw new InvalidDataException("Release SHA-256 digest is invalid.");
            return new ProductionRelease(version, download, sha256);
        }
        throw new InvalidDataException("Latest release does not contain ShowWhere.exe.");
    }

    internal static async Task<string> DownloadVerifiedAsync(HttpClient http, ProductionRelease release, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShowWhere", "Updates");
        Directory.CreateDirectory(directory);
        foreach (var oldFile in Directory.EnumerateFiles(directory, "*.tmp").Where(path => File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddDays(-2)))
            TryDelete(oldFile);
        var destination = Path.Combine(directory, $"ShowWhere-{release.Version.ToString(3)}-{Guid.NewGuid():N}.tmp");
        try
        {
            using var response = await http.GetAsync(release.DownloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaximumDownloadBytes) throw new InvalidDataException("Update is unexpectedly large.");
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            var buffer = new byte[81920];
            long total = 0;
            while (true)
            {
                var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                total += read;
                if (total > MaximumDownloadBytes) throw new InvalidDataException("Update is unexpectedly large.");
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            if (!FixedTimeEquals(ComputeSha256(destination), release.Sha256)) throw new InvalidDataException("Update integrity check failed.");
            return destination;
        }
        catch
        {
            TryDelete(destination);
            throw;
        }
    }

    internal static void ApplyWithRollback(int parentProcessId, string targetExecutable, string expectedSha256)
    {
        var sourceExecutable = Environment.ProcessPath ?? throw new InvalidOperationException("Updater path is unavailable.");
        targetExecutable = Path.GetFullPath(targetExecutable);
        if (!string.Equals(Path.GetFileName(targetExecutable), "ShowWhere.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Update target is invalid.");
        if (!FixedTimeEquals(ComputeSha256(sourceExecutable), expectedSha256)) throw new InvalidDataException("Updater integrity check failed.");
        try { Process.GetProcessById(parentProcessId).WaitForExit(30_000); }
        catch (ArgumentException) { }

        ReplaceWithRollback(sourceExecutable, targetExecutable, expectedSha256, executable =>
        {
            using var updated = Process.Start(new ProcessStartInfo(executable, "--updated") { UseShellExecute = true })
                ?? throw new InvalidOperationException("Updated ShowWhere did not start.");
            return !updated.WaitForExit(10_000);
        });
    }

    internal static void ReplaceWithRollback(
        string sourceExecutable,
        string targetExecutable,
        string expectedSha256,
        Func<string, bool> startupProbe)
    {
        var staging = targetExecutable + ".new";
        var backup = targetExecutable + ".previous";
        TryDelete(staging);
        TryDelete(backup);
        var replaced = false;
        try
        {
            File.Copy(sourceExecutable, staging, true);
            if (!FixedTimeEquals(ComputeSha256(staging), expectedSha256)) throw new InvalidDataException("Staged update integrity check failed.");
            if (File.Exists(targetExecutable)) File.Move(targetExecutable, backup, true);
            File.Move(staging, targetExecutable, true);
            replaced = true;
            if (!startupProbe(targetExecutable)) throw new InvalidOperationException("Updated ShowWhere exited during startup.");
            TryDelete(backup);
        }
        catch
        {
            TryDelete(staging);
            if (replaced) TryDelete(targetExecutable);
            if (File.Exists(backup)) File.Move(backup, targetExecutable, true);
            try
            {
                if (File.Exists(targetExecutable)) _ = Process.Start(new ProcessStartInfo(targetExecutable) { UseShellExecute = true });
            }
            catch (System.ComponentModel.Win32Exception) { }
            throw;
        }
    }

    internal static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(left), Convert.FromHexString(right)); }
        catch (FormatException) { return false; }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
