using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SonNuage.Desktop.Components;
using SonNuage.Desktop.Services;
using SonNuage.UI.Services;

namespace SonNuage.Desktop.Tests;

/// <summary>Panneau de réglages et bandeau de mise à jour, que SonNuage.UI affiche par IPlatformInfo.</summary>
public sealed class DesktopComponentsTests : IAsyncDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sonnuage-tests", Guid.NewGuid().ToString("N"));
    private readonly ServiceProvider _services;
    private readonly UpdateService _updates;

    public DesktopComponentsTests()
    {
        var release = """
            { "tag_name": "v9.0.0", "_links": { "self": "https://git.example.org/r/v9.0.0" },
              "assets": { "links": [ { "name": "SonNuage-9.0.0-win-x64-setup.exe", "url": "https://git.example.org/p/setup.exe" } ] } }
            """;
        _updates = new UpdateService(new HttpClient(new Respond(release)), new Uri("https://git.example.org/latest"), "1.0.0", InstallKind.WindowsSetup, _dir);
        _services = new ServiceCollection().AddLogging().AddSonNuageDesktop(_dir).AddSingleton(_updates)
            // Entrée de démarrage dans le dossier du test, pas dans la session de qui lance les tests.
            .AddSingleton(sp => new DesktopOptions(sp.GetRequiredService<IKeyValueStore>(), new LinuxAutostart(_dir, "/usr/bin/sonnuage")))
            .BuildServiceProvider();
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void The_desktop_declares_its_settings_panel_and_update_banner()
    {
        var platform = new DesktopPlatform();

        Assert.Equal([typeof(DesktopSettingsPanel)], platform.SettingsSections);
        Assert.Equal([typeof(UpdateBanner)], platform.Notices);
    }

    [Fact]
    public async Task The_banner_shows_a_newer_release_until_dismissed()
    {
        Assert.DoesNotContain("update-notice", await RenderAsync<UpdateBanner>());

        await _updates.CheckAsync(TestContext.Current.CancellationToken);
        Assert.Contains("update-notice", await RenderAsync<UpdateBanner>());

        _updates.Dismiss();
        Assert.DoesNotContain("update-notice", await RenderAsync<UpdateBanner>());
    }

    [Fact]
    public async Task The_settings_panel_has_both_start_options_off_by_default()
    {
        var html = await RenderAsync<DesktopSettingsPanel>();

        Assert.Contains("desktop-start-session", html);
        Assert.Contains("desktop-start-hidden", html);
        Assert.DoesNotContain("checked", html);
    }

    private async Task<string> RenderAsync<T>() where T : IComponent
    {
        await using var renderer = new HtmlRenderer(_services, _services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<T>()).ToHtmlString());
    }

    private sealed class Respond(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }
}
