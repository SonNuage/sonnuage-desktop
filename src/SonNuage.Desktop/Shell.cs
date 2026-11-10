using Photino.Blazor;
using SonNuage.Desktop.Services;
using SonNuage.Desktop.Tray;
using SonNuage.UI.Services;
using Microsoft.Extensions.DependencyInjection;

namespace SonNuage.Desktop;

/// <summary>
/// Fenêtre et icône de la barre système. Fermer la fenêtre la cache : la musique continue, l'icône la rouvre.
/// « Quitter » (menu de l'icône) arrête l'appli. Sans icône (Linux sans indicateur), fermer quitte.
/// </summary>
public sealed class Shell : IDisposable
{
    public const string Title = "SonNuage";

    private readonly PhotinoBlazorApp _app;
    private readonly string _icons;
    private readonly IPlayer _player;
    private readonly PageJsRuntime _page;
    private readonly Localizer _localizer;
    private ITrayIcon? _tray;
    private IWindowControl? _window;
    private bool _quitting;
    // Fenêtre native fermée (l'appli s'arrête) : plus d'Invoke possible.
    private volatile bool _closed;
    // Dernier état affiché par l'icône : le menu n'est reconstruit que s'il change.
    private (bool Playing, bool HasTrack, string Tip, string Language)? _shown;

    private readonly SingleInstance? _instance;

    public Shell(PhotinoBlazorApp app, string icons, SingleInstance? instance)
    {
        _app = app;
        _icons = icons;
        _instance = instance;
        _player = app.Services.GetRequiredService<IPlayer>();
        _page = app.Services.GetRequiredService<PageJsRuntime>();
        _localizer = app.Services.GetRequiredService<Localizer>();
        app.MainWindow.RegisterWindowCreatedHandler((_, _) => OnCreated());
        app.MainWindow.RegisterWindowClosingHandler((_, _) => OnClosing());
    }

    private void OnCreated()
    {
        _window = TrayFactory.CreateWindowControl(_app.MainWindow);
        // macOS ne relance pas l'appli (Finder, Dock) : il demande à celle qui tourne de rouvrir sa fenêtre.
        if (OperatingSystem.IsMacOS()) MacReopen.Register(Show);
        // Un second lancement : la fenêtre revient. Abonné ici, une fois la fenêtre native créée (Invoke l'exige).
        if (_instance is not null) _instance.Activated += () =>
        {
            if (!_closed) _app.MainWindow.Invoke(Show);
        };
        _tray = TrayFactory.Create(_icons, Title);
        if (_tray is null) return;
        _tray.Activated += Toggle;
        _player.StateChanged += () => _app.MainWindow.Invoke(Refresh);
        _localizer.Changed += () => _app.MainWindow.Invoke(Refresh);
        Refresh();
    }

    /// <summary>true : fermeture annulée, la fenêtre est seulement cachée.</summary>
    private bool OnClosing()
    {
        if (_quitting || _tray is null || _window is null)
        {
            _closed = true;
            return false;
        }
        _window.Hide();
        return true;
    }

    /// <summary>Montre la fenêtre et la passe au premier plan (thread de la fenêtre). Rien avant sa création.</summary>
    public void Show() => _window?.Show();

    private void Toggle()
    {
        if (_window is null) return;
        if (_window.IsVisible) _window.Hide();
        else _window.Show();
    }

    private void Refresh()
    {
        if (_tray is null) return;
        var state = _player.State;
        var track = state.Current;
        var l = _localizer;
        var tip = track is null ? Title : $"{track.Title} · {track.ArtistName}";
        var shown = (state.IsPlaying, track is not null, tip, l.Language);
        if (_shown == shown) return;
        _shown = shown;

        _tray.SetToolTip(tip);
        _tray.SetMenu([
            new TrayItem(l["desktop.show"], () => _window?.Show()),
            TrayItem.Separator,
            new TrayItem(state.IsPlaying ? l["player.pause"] : l["player.play"], () => Command(p => p.TogglePlayAsync()), track is not null),
            new TrayItem(l["player.next"], () => Command(p => p.NextAsync()), track is not null),
            new TrayItem(l["player.previous"], () => Command(p => p.PreviousAsync()), track is not null),
            TrayItem.Separator,
            new TrayItem(l["desktop.quit"], Quit),
        ]);
    }

    private void Command(Func<IPlayer, Task> command) => _ = _page.DispatchAsync(() => command(_player));

    private void Quit()
    {
        _quitting = true;
        _app.MainWindow.Close();
    }

    public void Dispose()
    {
        _tray?.Dispose();
        _tray = null;
    }
}
