using Microsoft.Extensions.DependencyInjection;
using SonNuage.Desktop.Services;
using SonNuage.UI.Services;

namespace SonNuage.Desktop.Tests;

public sealed class CompositionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sonnuage-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    // Une dépendance nouvelle d'un service de SonNuage.UI (PlaybackSettings → IPlaybackPreferencesApi en 1.4.0) ne se
    // voit qu'à l'exécution : la fenêtre ne s'ouvrirait pas.
    [Fact]
    public async Task Every_service_the_window_uses_resolves()
    {
        var services = new ServiceCollection().AddSonNuageDesktop(_dir);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        // Le lecteur du PC rejoint les autres appareils du compte (en lecture sur…, lire ici).
        Assert.IsType<LinkedPlayer>(provider.GetRequiredService<IPlayer>());
        Assert.NotNull(provider.GetRequiredService<WebPlayer>());
        Assert.IsType<DesktopStore>(provider.GetRequiredService<IKeyValueStore>());
        Assert.IsType<AccountEndpoint>(provider.GetRequiredService<IServerEndpoint>());
        Assert.IsType<DesktopPlatform>(provider.GetRequiredService<IPlatformInfo>());
        Assert.NotNull(provider.GetRequiredService<PlaybackSettings>());
        Assert.NotNull(provider.GetRequiredService<Localizer>());
        Assert.NotNull(provider.GetRequiredService<DesktopOptions>());
        // Build de développement : pas d'adresse de publication, aucune vérification de mise à jour.
        Assert.Equal(UpdateState.Disabled, provider.GetRequiredService<UpdateService>().State);
    }

    [Fact]
    public async Task The_player_talks_to_the_page_through_the_bridge()
    {
        await using var provider = new ServiceCollection().AddSonNuageDesktop(_dir).BuildServiceProvider();

        Assert.Same(provider.GetRequiredService<IPlayer>(), provider.GetRequiredService<IPlayer>());
        Assert.Same(provider.GetRequiredService<PageJsRuntime>(), provider.GetRequiredService<PageJsRuntime>());
    }
}
