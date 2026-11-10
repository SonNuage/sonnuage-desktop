using System.Runtime.Versioning;
using System.Security;
using System.Text;
using SonNuage.UI.Services;

namespace SonNuage.Desktop.Services;

/// <summary>
/// Lancement à l'ouverture de session, par le mécanisme de chaque système. L'entrée du système fait foi : la retirer
/// par ses réglages décoche l'option. L'appli lancée ainsi reçoit <see cref="Argument"/>.
/// </summary>
public interface IAutostart
{
    bool IsEnabled { get; }
    void Enable();
    void Disable();
}

public static class Autostart
{
    public const string Argument = "--autostart";

    /// <summary>Entrée du système courant pour ce programme ; null là où aucune n'est prévue.</summary>
    public static IAutostart? ForCurrentSystem()
    {
        var program = Environment.ProcessPath;
        if (program is null) return null;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsWindows()) return new WindowsAutostart(program);
        if (OperatingSystem.IsMacOS()) return new MacAutostart(home, program);
        if (OperatingSystem.IsLinux())
        {
            // AppImage : le programme tourne depuis un montage temporaire, c'est le fichier .AppImage qu'il faut lancer.
            var appImage = Environment.GetEnvironmentVariable("APPIMAGE");
            var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            return new LinuxAutostart(string.IsNullOrEmpty(config) ? Path.Combine(home, ".config") : config,
                string.IsNullOrEmpty(appImage) ? program : appImage);
        }
        return null;
    }
}

/// <summary>Valeur « SonNuage » sous HKCU\Software\Microsoft\Windows\CurrentVersion\Run.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsAutostart(string program, string runKey = @"Software\Microsoft\Windows\CurrentVersion\Run") : IAutostart
{
    private const string Name = "SonNuage";

    public string Command => $"\"{program}\" {Autostart.Argument}";

    public bool IsEnabled
    {
        get
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(runKey);
            return key?.GetValue(Name) is string;
        }
    }

    public void Enable()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(runKey);
        key.SetValue(Name, Command);
    }

    public void Disable()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(runKey, writable: true);
        key?.DeleteValue(Name, throwOnMissingValue: false);
    }
}

/// <summary>Entrée XDG : <c>$XDG_CONFIG_HOME/autostart/sonnuage.desktop</c> (GNOME, KDE, Xfce…).</summary>
public sealed class LinuxAutostart(string configHome, string program) : IAutostart
{
    private string EntryPath => Path.Combine(configHome, "autostart", "sonnuage.desktop");

    public bool IsEnabled => File.Exists(EntryPath);

    public void Enable()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(EntryPath)!);
        File.WriteAllText(EntryPath, $"""
            [Desktop Entry]
            Type=Application
            Name=SonNuage
            Exec={Quote(program)} {Autostart.Argument}
            Icon=sonnuage
            Terminal=false
            X-GNOME-Autostart-enabled=true

            """.ReplaceLineEndings("\n"));
    }

    public void Disable()
    {
        if (IsEnabled) File.Delete(EntryPath);
    }

    /// <summary>
    /// Programme entre guillemets pour la clé Exec : \ " ` $ échappés par une barre oblique inverse, elle-même doublée
    /// par l'échappement des chaînes du fichier ; % doublé (codes de champ).
    /// </summary>
    public static string Quote(string program)
    {
        var text = new StringBuilder("\"");
        foreach (var c in program)
        {
            if (c is '"' or '`' or '$') text.Append(@"\\").Append(c);
            else if (c == '\\') text.Append(@"\\\\");
            else if (c == '%') text.Append("%%");
            else text.Append(c);
        }
        return text.Append('"').ToString();
    }
}

/// <summary>Agent launchd de l'utilisateur : <c>~/Library/LaunchAgents/org.sonnuage.desktop.plist</c>.</summary>
public sealed class MacAutostart(string home, string program) : IAutostart
{
    private const string Label = "org.sonnuage.desktop";

    private string PlistPath => Path.Combine(home, "Library", "LaunchAgents", Label + ".plist");

    public bool IsEnabled => File.Exists(PlistPath);

    /// <summary>
    /// Dans un paquet .app, celui-ci est ouvert par <c>open</c>, comme depuis le Finder (Dock, menus, une seule appli).
    /// Hors paquet (développement), le programme est lancé tel quel.
    /// </summary>
    public static string[] Arguments(string program)
    {
        var marker = ".app/Contents/MacOS/";
        var at = program.IndexOf(marker, StringComparison.Ordinal);
        return at < 0
            ? [program, Autostart.Argument]
            : ["/usr/bin/open", "-a", program[..(at + 4)], "--args", Autostart.Argument];
    }

    public void Enable()
    {
        var arguments = string.Concat(Arguments(program).Select(a => $"\n        <string>{SecurityElement.Escape(a)}</string>"));
        Directory.CreateDirectory(Path.GetDirectoryName(PlistPath)!);
        File.WriteAllText(PlistPath, $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
                <key>Label</key>
                <string>{Label}</string>
                <key>ProgramArguments</key>
                <array>{arguments}
                </array>
                <key>RunAtLoad</key>
                <true/>
                <key>ProcessType</key>
                <string>Interactive</string>
            </dict>
            </plist>

            """.ReplaceLineEndings("\n"));
    }

    public void Disable()
    {
        if (IsEnabled) File.Delete(PlistPath);
    }
}

/// <summary>
/// Réglages propres à l'ordinateur : lancement avec la session (entrée du système) et démarrage caché (settings.json).
/// </summary>
public sealed class DesktopOptions(IKeyValueStore store, IAutostart? autostart)
{
    public const string StartHiddenKey = "sonnuage.desktop.startHidden";

    /// <summary>Le système offre le lancement avec la session (sinon l'option est masquée).</summary>
    public bool CanStartWithSession => autostart is not null;

    public bool StartWithSession => autostart?.IsEnabled ?? false;

    public Task SetStartWithSessionAsync(bool enabled)
    {
        if (autostart is null) return Task.CompletedTask;
        if (enabled) autostart.Enable();
        else autostart.Disable();
        return Task.CompletedTask;
    }

    public async Task<bool> StartHiddenAsync() => await store.GetAsync(StartHiddenKey) == "1";

    public Task SetStartHiddenAsync(bool enabled) => store.SetAsync(StartHiddenKey, enabled ? "1" : null);

    /// <summary>Fenêtre cachée au démarrage : seulement lancée avec la session, et si l'option est cochée.</summary>
    public static bool ShouldStartHidden(IEnumerable<string> args, bool startHidden) => startHidden && args.Contains(Autostart.Argument);
}
