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
    // Lancée avec la session et « démarrer caché » : la fenêtre reste cachée tant qu'on ne la demande pas.
    private volatile bool _hiddenAtStart;

    public Shell(PhotinoBlazorApp app, string icons, SingleInstance? instance, bool startHidden)
    {
        _app = app;
        _icons = icons;
        _instance = instance;
        _hiddenAtStart = startHidden;
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
        if (_hiddenAtStart) HideAtStart();
    }

    /// <summary>
    /// Seule l'icône reste (sans icône, la fenêtre reste réduite : rien d'autre ne la rouvrirait). Un système qui montre
    /// la fenêtre après sa création la ferait réapparaître : on la recache une fois la boucle de messages partie.
    /// </summary>
    private void HideAtStart()
    {
        _window?.Hide();
        _ = Task.Delay(TimeSpan.FromSeconds(1)).ContinueWith(_ =>
        {
            if (_hiddenAtStart && !_closed) _app.MainWindow.Invoke(() =>
            {
                if (_hiddenAtStart) _window?.Hide();
            });
        }, TaskScheduler.Default);
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
    public void Show()
    {
        _hiddenAtStart = false;
        _window?.Show();
    }

    private void Toggle()
    {
        if (_window is null) return;
        if (_window.IsVisible) _window.Hide();
        else Show();
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
            new TrayItem(l["desktop.show"], Show),
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
