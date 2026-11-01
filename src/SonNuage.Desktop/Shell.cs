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
    // Dernier état affiché par l'icône : le menu n'est reconstruit que s'il change.
    private (bool Playing, bool HasTrack, string Tip, string Language)? _shown;

    public Shell(PhotinoBlazorApp app, string icons)
    {
        _app = app;
        _icons = icons;
        _player = app.Services.GetRequiredService<IPlayer>();
        _page = app.Services.GetRequiredService<PageJsRuntime>();
        _localizer = app.Services.GetRequiredService<Localizer>();
        app.MainWindow.RegisterWindowCreatedHandler((_, _) => OnCreated());
        app.MainWindow.RegisterWindowClosingHandler((_, _) => OnClosing());
    }

    private void OnCreated()
    {
        _window = TrayFactory.CreateWindowControl(_app.MainWindow);
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
        if (_quitting || _tray is null || _window is null) return false;
        _window.Hide();
        return true;
    }

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

        string T(string key, string fallback) => l.Has(key) ? l[key] : fallback;
        _tray.SetToolTip(tip);
        _tray.SetMenu([
            new TrayItem(T("desktop.show", "SonNuage"), () => _window?.Show()),
            TrayItem.Separator,
            new TrayItem(state.IsPlaying ? T("player.pause", "Pause") : T("player.play", "Play"), () => Command(p => p.TogglePlayAsync()), track is not null),
            new TrayItem(T("player.next", "Next"), () => Command(p => p.NextAsync()), track is not null),
            new TrayItem(T("player.previous", "Previous"), () => Command(p => p.PreviousAsync()), track is not null),
            TrayItem.Separator,
            new TrayItem(T("desktop.quit", "Quit"), Quit),
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
