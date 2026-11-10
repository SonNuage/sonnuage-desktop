using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace SonNuage.Desktop.Services;

public enum UpdateState
{
    /// <summary>Pas d'adresse de publication (build de développement) : rien n'est vérifié.</summary>
    Disabled,
    Idle,
    Checking,
    UpToDate,
    Available,
    Failed,
    Downloading,
}

/// <summary>Façon dont l'appli a été installée : elle décide du fichier de mise à jour et de son installation.</summary>
public enum InstallKind
{
    /// <summary>Archive décompressée à la main (zip, tar.gz) : on ouvre la page de la version.</summary>
    Portable,
    WindowsSetup,
    MacArm64,
    MacX64,
    LinuxAppImage,
    LinuxDeb,
}

public sealed record ReleaseAsset(string Name, Uri Url);

/// <summary>Dernière version publiée, lue dans la réponse de GitLab (GET /projects/:id/releases/permalink/latest).</summary>
public sealed record ReleaseInfo(Version Version, Uri Page, IReadOnlyList<ReleaseAsset> Assets)
{
    /// <summary>null si la réponse n'est pas une release au tag vX.Y.Z.</summary>
    public static ReleaseInfo? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("tag_name", out var tag) || !TryVersion(tag.GetString()?.TrimStart('v'), out var version)) return null;
            if (!root.TryGetProperty("_links", out var links) || !links.TryGetProperty("self", out var self)
                || !Uri.TryCreate(self.GetString(), UriKind.Absolute, out var page)) return null;
            var assets = new List<ReleaseAsset>();
            if (root.TryGetProperty("assets", out var a) && a.TryGetProperty("links", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
                    var url = item.TryGetProperty("url", out var u) ? u.GetString() : null;
                    if (!string.IsNullOrEmpty(name) && Uri.TryCreate(url, UriKind.Absolute, out var uri)) assets.Add(new ReleaseAsset(name, uri));
                }
            }
            return new ReleaseInfo(version, page, assets);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>X.Y.Z seulement : une préversion (0.1.0-ci.42, build de CI) ne se compare pas.</summary>
    internal static bool TryVersion(string? text, out Version version)
    {
        version = new Version();
        if (!Version.TryParse(text, out var parsed) || parsed.Build < 0 || parsed.Revision >= 0) return false;
        version = parsed;
        return true;
    }
}

/// <summary>Version plus récente que celle qui tourne, avec le fichier qui l'installe ici (null : page de la version).</summary>
public sealed record AvailableUpdate(string Version, Uri Page, ReleaseAsset? Installer, ReleaseAsset? Checksums)
{
    public static AvailableUpdate? Pick(string current, ReleaseInfo release, InstallKind kind)
    {
        if (!ReleaseInfo.TryVersion(current, out var running) || release.Version <= running) return null;
        var suffix = kind switch
        {
            InstallKind.WindowsSetup => "-win-x64-setup.exe",
            InstallKind.MacArm64 => "-osx-arm64.dmg",
            InstallKind.MacX64 => "-osx-x64.dmg",
            InstallKind.LinuxAppImage => "-linux-x64.AppImage",
            InstallKind.LinuxDeb => "_amd64.deb",
            _ => null,
        };
        var installer = suffix is null ? null : release.Assets.FirstOrDefault(a => a.Name.EndsWith(suffix, StringComparison.Ordinal));
        var sums = release.Assets.FirstOrDefault(a => a.Name == "SHA256SUMS");
        return new AvailableUpdate(release.Version.ToString(3), release.Page, installer, sums);
    }
}

/// <summary>Fichier SHA256SUMS (sortie de sha256sum) joint à chaque release par la CI.</summary>
public static class Checksums
{
    public static string? Find(string sums, string fileName)
    {
        foreach (var line in sums.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && parts[1].TrimStart().TrimStart('*') == fileName) return parts[0].ToLowerInvariant();
        }
        return null;
    }
}

/// <summary>
/// Mises à jour de l'appli : dernière release du projet sonnuage-desktop sur GitLab (adresse fixée au build par la
/// CI, <c>UpdateFeed</c>), vérifiée au démarrage puis toutes les 6 heures. Installer télécharge le fichier qui
/// convient (contrôlé par SHA256SUMS) et le lance ; à défaut, la page de la version s'ouvre dans le navigateur.
/// </summary>
public sealed class UpdateService(HttpClient http, Uri? feed, string currentVersion, InstallKind kind, string downloadDirectory)
{
    private static readonly TimeSpan FirstCheck = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private Timer? _timer;

    public UpdateState State { get; private set; } = feed is null ? UpdateState.Disabled : UpdateState.Idle;
    public AvailableUpdate? Available { get; private set; }
    public string CurrentVersion => currentVersion;
    /// <summary>Le dernier essai d'installation a échoué : la page de la version a été ouverte à la place.</summary>
    public bool InstallFailed { get; private set; }
    /// <summary>« Plus tard » dans le bandeau : il ne revient qu'à la prochaine version.</summary>
    public string? DismissedVersion { get; private set; }

    public event Action? Changed;
    /// <summary>L'installeur prend la suite et relancera l'appli : elle doit s'arrêter.</summary>
    public event Action? QuitRequested;

    /// <summary>Adresse de publication posée au build (-p:UpdateFeed=…) ; aucune en développement.</summary>
    public static Uri? FeedOf(Assembly assembly) =>
        assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "UpdateFeed")?.Value is { Length: > 0 } value
        && Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;

    public void Start()
    {
        if (feed is null || _timer is not null) return;
        _timer = new Timer(_ => _ = CheckAsync(), null, FirstCheck, Interval);
    }

    public void Dismiss()
    {
        DismissedVersion = Available?.Version;
        Changed?.Invoke();
    }

    public async Task CheckAsync(CancellationToken ct = default)
    {
        if (feed is null || State is UpdateState.Checking or UpdateState.Downloading) return;
        Set(UpdateState.Checking);
        try
        {
            using var response = await http.GetAsync(feed, ct);
            response.EnsureSuccessStatusCode();
            var release = ReleaseInfo.Parse(await response.Content.ReadAsStringAsync(ct));
            if (release is null)
            {
                Set(UpdateState.Failed);
                return;
            }
            Available = AvailableUpdate.Pick(currentVersion, release, kind);
            Set(Available is null ? UpdateState.UpToDate : UpdateState.Available);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Hors ligne, serveur GitLab arrêté ou projet privé : on réessaiera au prochain tour.
            Set(UpdateState.Failed);
        }
    }

    /// <summary>Télécharge un fichier de la release, contrôlé par SHA256SUMS quand la release en a un.</summary>
    public async Task<string> DownloadAsync(ReleaseAsset asset, CancellationToken ct)
    {
        var name = Path.GetFileName(asset.Name);
        if (string.IsNullOrEmpty(name) || name != asset.Name) throw new InvalidDataException($"Nom de fichier refusé : {asset.Name}");
        Directory.CreateDirectory(downloadDirectory);
        var path = Path.Combine(downloadDirectory, name);
        var temp = path + ".part";
        try
        {
            using (var response = await http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                await using var file = File.Create(temp);
                await response.Content.CopyToAsync(file, ct);
            }
            if (Available?.Checksums is { } sums)
            {
                var expected = Checksums.Find(await http.GetStringAsync(sums.Url, ct), name);
                string actual;
                await using (var file = File.OpenRead(temp)) actual = Convert.ToHexString(await SHA256.HashDataAsync(file, ct)).ToLowerInvariant();
                if (expected != actual) throw new InvalidDataException($"Somme SHA-256 inattendue pour {name}");
            }
            File.Move(temp, path, overwrite: true);
            return path;
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    /// <summary>Télécharge et lance l'installation ; en cas d'échec, ouvre la page de la version.</summary>
    public async Task InstallAsync()
    {
        if (Available is not { } update || State == UpdateState.Downloading) return;
        if (update.Installer is null)
        {
            UpdateLauncher.OpenPage(update.Page);
            return;
        }
        InstallFailed = false;
        Set(UpdateState.Downloading);
        try
        {
            var file = await DownloadAsync(update.Installer, CancellationToken.None);
            var quit = UpdateLauncher.Run(kind, file);
            Set(UpdateState.Available);
            if (quit) QuitRequested?.Invoke();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Mise à jour impossible : {ex.Message}");
            InstallFailed = true;
            Set(UpdateState.Available);
            UpdateLauncher.OpenPage(update.Page);
        }
    }

    private void Set(UpdateState state)
    {
        State = state;
        Changed?.Invoke();
    }
}

/// <summary>Lancement d'un fichier de mise à jour, selon l'installation (effets de bord seulement).</summary>
public static class UpdateLauncher
{
    /// <summary>Façon dont l'appli qui tourne a été installée.</summary>
    public static InstallKind CurrentKind()
    {
        var program = Environment.ProcessPath ?? "";
        if (OperatingSystem.IsWindows())
            return File.Exists(Path.Combine(AppContext.BaseDirectory, "Uninstall.exe")) ? InstallKind.WindowsSetup : InstallKind.Portable;
        if (OperatingSystem.IsMacOS() && program.Contains(".app/Contents/MacOS/", StringComparison.Ordinal))
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? InstallKind.MacArm64 : InstallKind.MacX64;
        if (OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPIMAGE"))) return InstallKind.LinuxAppImage;
            if (program.StartsWith("/opt/sonnuage/", StringComparison.Ordinal)) return InstallKind.LinuxDeb;
        }
        return InstallKind.Portable;
    }

    /// <summary>true : l'appli doit s'arrêter, l'installeur la relancera.</summary>
    public static bool Run(InstallKind kind, string file)
    {
        switch (kind)
        {
            case InstallKind.WindowsSetup:
                // Installeur NSIS silencieux : il attend la fin de l'appli, remplace les fichiers et la relance.
                Process.Start(new ProcessStartInfo(file, "/S /relaunch") { UseShellExecute = true });
                return true;
            case InstallKind.MacArm64 or InstallKind.MacX64:
                // L'image disque s'ouvre dans le Finder : glisser SonNuage dans Applications remplace l'ancienne.
                Process.Start("open", [file]);
                return false;
            case InstallKind.LinuxDeb:
                // Logithèque du bureau (ou gestionnaire de paquets) : elle demande le mot de passe d'administration.
                Process.Start("xdg-open", [file]);
                return false;
            case InstallKind.LinuxAppImage:
                ReplaceAppImage(file);
                return true;
            default:
                throw new InvalidOperationException($"Pas d'installation automatique pour {kind}");
        }
    }

    /// <summary>
    /// Le fichier .AppImage est remplacé (renommage atomique dans son dossier : l'appli qui tourne garde l'ancien),
    /// puis relancé dès que ce processus s'est arrêté (sinon l'instance unique le renverrait vers lui).
    /// </summary>
    private static void ReplaceAppImage(string file)
    {
        var target = Environment.GetEnvironmentVariable("APPIMAGE") ?? throw new InvalidOperationException("APPIMAGE absent");
        var staged = target + ".new";
        File.Copy(file, staged, overwrite: true);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(staged, File.GetUnixFileMode(target) | UnixFileMode.UserExecute | UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(staged, target, overwrite: true);
        Process.Start("/bin/sh", ["-c", "while kill -0 \"$1\" 2>/dev/null; do sleep 0.5; done; exec \"$2\"", "sh",
            Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture), target]);
    }

    /// <summary>Page de la version dans le navigateur par défaut.</summary>
    public static void OpenPage(Uri page)
    {
        try
        {
            if (OperatingSystem.IsWindows()) Process.Start(new ProcessStartInfo(page.ToString()) { UseShellExecute = true });
            else Process.Start(OperatingSystem.IsMacOS() ? "open" : "xdg-open", [page.ToString()]);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Console.Error.WriteLine($"Navigateur introuvable : {ex.Message}");
        }
    }
}
