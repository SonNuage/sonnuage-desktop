namespace SonNuage.Desktop.Tray;

/// <summary>Entrée du menu de l'icône. Sans libellé : un séparateur.</summary>
public sealed record TrayItem(string? Label, Action? OnClick = null, bool Enabled = true)
{
    public static readonly TrayItem Separator = new((string?)null);
}

/// <summary>
/// Icône de la barre système (zone de notification sous Windows, barre des menus sous macOS, indicateur
/// StatusNotifierItem sous Linux). Créée et appelée sur le thread de la fenêtre.
/// </summary>
public interface ITrayIcon : IDisposable
{
    /// <summary>Remplace le menu.</summary>
    void SetMenu(IReadOnlyList<TrayItem> items);

    /// <summary>Texte au survol (titre en cours). Ignoré là où l'OS n'en affiche pas.</summary>
    void SetToolTip(string text);

    /// <summary>Clic principal sur l'icône (Windows seulement : ailleurs, un clic ouvre le menu).</summary>
    event Action? Activated;
}

/// <summary>Montrer et cacher la fenêtre principale : Photino ne sait que la réduire.</summary>
public interface IWindowControl
{
    bool IsVisible { get; }
    void Hide();
    void Show();
}

public static class TrayFactory
{
    /// <summary>
    /// null si l'OS n'offre pas d'icône (Linux sans libayatana-appindicator) : fermer la fenêtre quitte alors l'appli.
    /// </summary>
    public static ITrayIcon? Create(string iconDirectory, string title)
    {
        try
        {
            if (OperatingSystem.IsWindows()) return new WindowsTray(Path.Combine(iconDirectory, "sonnuage.ico"), title);
            if (OperatingSystem.IsMacOS()) return new MacTray(Path.Combine(iconDirectory, "tray.png"), title);
            if (OperatingSystem.IsLinux()) return LinuxTray.TryCreate(iconDirectory, "tray", title);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            Console.Error.WriteLine($"Icône de barre système indisponible : {ex.Message}");
        }
        return null;
    }

    public static IWindowControl CreateWindowControl(Photino.NET.PhotinoWindow window)
    {
        if (OperatingSystem.IsWindows()) return new WindowsWindow(window);
        if (OperatingSystem.IsMacOS()) return new MacWindow(window.Title);
        if (OperatingSystem.IsLinux()) return new GtkWindow(window.Title);
        throw new PlatformNotSupportedException();
    }
}
