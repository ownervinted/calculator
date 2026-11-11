using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LemieszeDesktop;

public static class UpdateService
{
    private const string ApiUrl = "https://api.github.com/repos/ownervinted/calculator/releases/latest";
    private static readonly string DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LemieszeCalculator");
    private static readonly string LogPath = Path.Combine(DataDirectory, "updates.log");
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(15) };
    private static bool checking;

    static UpdateService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("LemieszeCalculator/1.0");
        Http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public static void ConfirmStartup()
    {
        try
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "--update-health-path");
            if (index < 0 || index + 3 >= args.Length || args[index + 2] != "--update-health-token") return;
            var marker = args[index + 1];
            var token = args[index + 3];
            if (index + 3 >= args.Length || !Guid.TryParseExact(token, "N", out _)) return;
            var expectedDirectory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "LemieszeCalculator")) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(marker).StartsWith(expectedDirectory, StringComparison.OrdinalIgnoreCase)) return;
            File.WriteAllText(marker, token);
            Log("Nowa wersja uruchomiła się poprawnie.");
        }
        catch (Exception ex) { Log("Potwierdzenie uruchomienia nie powiodło się: " + ex); }
    }

    public static async Task CheckAsync(Window owner)
    {
        if (checking || !OperatingSystem.IsWindows()) return;
        checking = true;
        try
        {
            using var response = await Http.GetAsync(ApiUrl);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                Log("Nie znaleziono publicznego wydania GitHub Releases (404).");
                return;
            }
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync();
            using var json = await JsonDocument.ParseAsync(stream);
            var root = json.RootElement;
            var tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest)) return;
            var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0, 0);
            if (latest <= current) return;
            var asset = root.GetProperty("assets").EnumerateArray().FirstOrDefault(a =>
                string.Equals(a.GetProperty("name").GetString(), "LemieszeCalculator.exe", StringComparison.OrdinalIgnoreCase));
            if (asset.ValueKind != JsonValueKind.Object) { Log("Wydanie " + tag + " nie zawiera LemieszeCalculator.exe."); return; }
            var url = asset.GetProperty("browser_download_url").GetString();
            var digest = asset.TryGetProperty("digest", out var property) ? property.GetString() : null;
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
                !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            {
                Log("Nieprawidłowy adres pliku aktualizacji."); return;
            }
            if (digest is null || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ||
                digest.Length != 71 || !digest[7..].All(Uri.IsHexDigit))
            {
                Log("Brak prawidłowej sumy SHA-256 dla wydania " + tag); return;
            }
            var decision = MessageBox.Show(owner,
                $"Dostępna jest nowa wersja {latest}.\nAktualna wersja: {current}\n\nCzy chcesz zainstalować aktualizację?",
                "Dostępna aktualizacja", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (decision != MessageBoxResult.Yes) return;
            await DownloadAndInstallAsync(owner, uri, digest[7..]);
        }
        catch (Exception ex) { Log("Błąd sprawdzania aktualizacji: " + ex); }
        finally { checking = false; }
    }

    private static async Task DownloadAndInstallAsync(Window owner, Uri uri, string expectedHash)
    {
        var folder = Path.Combine(Path.GetTempPath(), "LemieszeCalculator", Guid.NewGuid().ToString("N"));
        var downloaded = Path.Combine(folder, "LemieszeCalculator.exe");
        var dialog = new UpdateProgressWindow { Owner = owner };
        dialog.Show();
        try
        {
            Directory.CreateDirectory(folder);
            using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using (var input = await response.Content.ReadAsStreamAsync())
            await using (var output = File.Create(downloaded))
            {
                var buffer = new byte[128 * 1024];
                long received = 0;
                int count;
                while ((count = await input.ReadAsync(buffer)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, count));
                    received += count;
                    dialog.Report(received, total);
                }
            }
            dialog.SetStatus("Weryfikowanie pliku...");
            await using (var file = File.OpenRead(downloaded))
            {
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(file));
                if (!actual.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Suma kontrolna SHA-256 jest nieprawidłowa.");
            }
            var currentExe = Environment.ProcessPath ?? throw new InvalidOperationException("Nie znaleziono pliku EXE.");
            if (!currentExe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Aktualizacja działa tylko dla opublikowanego pliku EXE.");
            var workerExe = Path.Combine(folder, "LemieszeUpdater.exe");
            File.Copy(currentExe, workerExe);
            var marker = Path.Combine(folder, "started.ok");
            var token = Guid.NewGuid().ToString("N");
            dialog.SetStatus("Instalowanie aktualizacji...");
            var start = new ProcessStartInfo(workerExe)
            {
                UseShellExecute = false,
                WorkingDirectory = folder
            };
            foreach (var argument in new[]
                     {
                         "--update-worker", Environment.ProcessId.ToString(),
                         downloaded, currentExe, marker, token
                     })
                start.ArgumentList.Add(argument);
            if (Process.Start(start) is null)
                throw new InvalidOperationException("Nie można uruchomić instalatora.");
            Log("Pobrano i zweryfikowano aktualizację. Uruchomiono instalator C#.");
            dialog.Close();
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            Log("Błąd pobierania lub instalacji: " + ex);
            dialog.Close();
            MessageBox.Show(owner, "Nie udało się zainstalować aktualizacji.\n\n" + ex.Message +
                "\n\nDziennik: " + LogPath, "Błąd aktualizacji", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            File.AppendAllText(LogPath, $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] {message}{Environment.NewLine}");
        }
        catch { }
    }

    public static int RunWorker(string[] args)
    {
        if (args.Length != 5 || !int.TryParse(args[0], out var previousPid))
        {
            Log("Nieprawidłowe argumenty instalatora.");
            return 1;
        }
        var source = args[1];
        var target = args[2];
        var marker = args[3];
        var token = args[4];
        var backup = target + ".update-backup";
        Process? launched = null;
        var backedUp = false;
        try
        {
            Log("Instalator C#: oczekiwanie na zamknięcie poprzedniej wersji.");
            try
            {
                using var old = Process.GetProcessById(previousPid);
                if (!old.WaitForExit(90000))
                    throw new TimeoutException("Poprzednia wersja nie została zamknięta.");
            }
            catch (ArgumentException) { }
            if (!File.Exists(source)) throw new FileNotFoundException("Brak pobranej aktualizacji.", source);
            if (!File.Exists(target)) throw new FileNotFoundException("Brak aktualnej aplikacji.", target);
            if (File.Exists(backup)) throw new IOException("Istnieje wcześniejsza kopia zapasowa: " + backup);
            File.Move(target, backup);
            backedUp = true;
            Log("Zapisano kopię zapasową starej wersji.");
            File.Copy(source, target);
            Log("Zastąpiono EXE nową wersją.");
            var start = new ProcessStartInfo(target)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(target)!
            };
            foreach (var argument in new[] { "--update-health-path", marker, "--update-health-token", token })
                start.ArgumentList.Add(argument);
            launched = Process.Start(start) ?? throw new InvalidOperationException("Nie można uruchomić nowej wersji.");
            Log("Uruchomiono nową wersję, oczekiwanie na potwierdzenie.");
            var deadline = DateTime.UtcNow.AddSeconds(45);
            var healthy = false;
            while (DateTime.UtcNow < deadline)
            {
                if (File.Exists(marker))
                {
                    try
                    {
                        if (File.ReadAllText(marker).Trim() == token)
                        {
                            healthy = true;
                            break;
                        }
                    }
                    catch (IOException) { }
                }
                if (launched.HasExited) break;
                Thread.Sleep(300);
            }
            if (!healthy) throw new InvalidOperationException("Nowa wersja nie potwierdziła uruchomienia.");
            Log("Aktualizacja zakończona pomyślnie.");
            try { File.Delete(backup); }
            catch (Exception ex) { Log("Nie udało się usunąć kopii zapasowej: " + ex.Message); }
            return 0;
        }
        catch (Exception ex)
        {
            Log("Błąd instalatora C#: " + ex);
            if (backedUp)
            {
                try
                {
                    if (launched is { HasExited: false })
                    {
                        launched.Kill();
                        launched.WaitForExit(15000);
                    }
                    if (File.Exists(target)) File.Delete(target);
                    File.Move(backup, target);
                    Log("Przywrócono poprzednią wersję.");
                    Process.Start(new ProcessStartInfo(target)
                    {
                        UseShellExecute = false,
                        WorkingDirectory = Path.GetDirectoryName(target)!
                    });
                    Log("Uruchomiono poprzednią wersję po przywróceniu.");
                }
                catch (Exception restoreError)
                {
                    Log("BŁĄD PRZYWRACANIA: " + restoreError);
                }
            }
            return 1;
        }
        finally
        {
            launched?.Dispose();
        }
    }

}

internal sealed class UpdateProgressWindow : Window
{
    private readonly ProgressBar bar;
    private readonly TextBlock status;

    public UpdateProgressWindow()
    {
        Title = "Aktualizacja";
        Width = 410;
        Height = 165;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.ToolWindow;
        Background = new SolidColorBrush(Color.FromRgb(20, 29, 48));
        Foreground = Brushes.White;
        ShowInTaskbar = false;
        var layout = new StackPanel { Margin = new Thickness(24) };
        layout.Children.Add(new TextBlock { Text = "Pobieranie aktualizacji", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 14) });
        bar = new ProgressBar { Height = 9, Minimum = 0, Maximum = 100, IsIndeterminate = true, Foreground = new SolidColorBrush(Color.FromRgb(83, 124, 255)) };
        layout.Children.Add(bar);
        status = new TextBlock { Text = "Łączenie z GitHub...", FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(181, 196, 224)), Margin = new Thickness(0, 12, 0, 0) };
        layout.Children.Add(status);
        Content = layout;
        Closing += (_, e) => e.Cancel = !Application.Current.Dispatcher.HasShutdownStarted && !allowClose;
    }

    private bool allowClose;
    public new void Close() { allowClose = true; base.Close(); }
    public void SetStatus(string text) { bar.IsIndeterminate = true; status.Text = text; }
    public void Report(long received, long? total)
    {
        if (total is > 0)
        {
            bar.IsIndeterminate = false;
            bar.Value = Math.Clamp(100.0 * received / total.Value, 0, 100);
            status.Text = $"{received / 1048576.0:0.0} / {total.Value / 1048576.0:0.0} MB ({bar.Value:0}%)";
        }
        else status.Text = $"Pobrano {received / 1048576.0:0.0} MB";
    }
}
