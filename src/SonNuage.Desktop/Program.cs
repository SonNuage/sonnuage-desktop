using Photino.Blazor;
using SonNuage.Desktop.Services;
using SonNuage.UI;
using SonNuage.UI.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace SonNuage.Desktop;

public static class Program
{
    // STA : WebView2 l'exige sous Windows. La fenêtre et sa boucle de messages restent sur ce thread.
    [STAThread]
    public static void Main(string[] args)
    {
        // Données de l'appli : %APPDATA%\SonNuage, ~/Library/Application Support/SonNuage ou ~/.config/SonNuage.
        var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SonNuage");

        // Une seule appli : un nouveau lancement ramène la fenêtre de celle qui tourne. Sans réponse de sa part
        // (verrou tenu pour une autre raison), on démarre quand même plutôt que de ne rien ouvrir.
        using var instance = SingleInstance.TryAcquire(dataDir);
        if (instance is null && SingleInstance.ActivateRunningAsync(dataDir, TimeSpan.FromSeconds(3)).GetAwaiter().GetResult()) return;

        var builder = PhotinoBlazorAppBuilder.CreateDefault(args);
        builder.RootComponents.Add<App>("#app");
        builder.RootComponents.Add<HeadOutlet>("head::after");
        builder.RootComponents.Add<JsBridge>("#bridge");

        builder.Services.AddSonNuageDesktop(dataDir);

        var app = builder.Build();

        // Comptes et réglages de lecture du compte actif chargés avant l'interface, comme sur Android. Hors du thread
        // de la fenêtre, qui doit rester celui de Main.
        var accounts = app.Services.GetRequiredService<AccountStore>();
        var settings = app.Services.GetRequiredService<PlaybackSettings>();
        Task.Run(async () =>
        {
            await accounts.LoadAsync();
            await settings.LoadAsync();
        }).GetAwaiter().GetResult();

        var icons = Path.Combine(AppContext.BaseDirectory, "wwwroot", "icons");
        app.MainWindow
            // Photino trace chaque message de la page (contenu des formulaires compris) : silence.
            .SetLogVerbosity(0)
            .SetTitle(Shell.Title)
            .SetIconFile(Path.Combine(icons, OperatingSystem.IsWindows() ? "sonnuage.ico" : "sonnuage.png"))
            .SetUseOsDefaultSize(false)
            .SetSize(1280, 820)
            .SetMinSize(420, 560)
            .Center()
            // Reprise de la lecture au démarrage, sans clic dans la page.
            .SetMediaAutoplayEnabled(true)
            // Profil de la webview (cache, stockage) avec les données de l'appli plutôt que dans le dossier courant.
            .SetTemporaryFilesPath(Path.Combine(dataDir, "webview"));
#if !DEBUG
        app.MainWindow.SetContextMenuEnabled(false);
#endif

        using var shell = new Shell(app, icons, instance);
        app.Run();
    }
}
