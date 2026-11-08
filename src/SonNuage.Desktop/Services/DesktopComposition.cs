using Microsoft.Extensions.DependencyInjection;
using SonNuage.UI;
using SonNuage.UI.Services;

namespace SonNuage.Desktop.Services;

public static class DesktopComposition
{
    /// <summary>Services de l'appli : stockage dans <paramref name="dataDir"/>, plusieurs comptes, lecteur de la page.</summary>
    public static IServiceCollection AddSonNuageDesktop(this IServiceCollection services, string dataDir)
    {
        services.AddSingleton<IKeyValueStore>(new DesktopStore(dataDir));
        services.AddSingleton<IServerEndpoint, AccountEndpoint>();
        services.AddSingleton<IPlatformInfo, DesktopPlatform>();
        services.AddSingleton<PageJsRuntime>();
        // Le lecteur vit toute la durée de l'appli : il parle à la page par PageJsRuntime, pas par l'IJSRuntime de la
        // page (scoped). Ses autres dépendances viennent du conteneur, quel que soit son constructeur.
        services.AddSingleton<IPlayer>(sp => ActivatorUtilities.CreateInstance<WebPlayer>(sp, sp.GetRequiredService<PageJsRuntime>()));
        services.AddSonNuageUi(multiAccount: true);
        return services;
    }
}
