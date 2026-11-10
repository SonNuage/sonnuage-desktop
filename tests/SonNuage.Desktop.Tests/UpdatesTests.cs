using System.Net;
using System.Text;
using SonNuage.Desktop.Services;

namespace SonNuage.Desktop.Tests;

public sealed class UpdatesTests : IDisposable
{
    private const string Feed = "https://git.example.org/api/v4/projects/9/releases/permalink/latest";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sonnuage-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    // Réponse de GitLab (GET /projects/:id/releases/permalink/latest), réduite à ce que l'appli lit.
    private static string Release(string tag = "v1.3.0") => $$"""
        {
          "tag_name": "{{tag}}",
          "name": "{{tag}}",
          "_links": { "self": "https://git.example.org/sonnuage/sonnuage-desktop/-/releases/{{tag}}" },
          "assets": {
            "links": [
              { "name": "SonNuage-1.3.0-win-x64-setup.exe", "url": "https://git.example.org/p/SonNuage-1.3.0-win-x64-setup.exe" },
              { "name": "SonNuage-1.3.0-win-x64.zip", "url": "https://git.example.org/p/SonNuage-1.3.0-win-x64.zip" },
              { "name": "SonNuage-1.3.0-osx-arm64.dmg", "url": "https://git.example.org/p/SonNuage-1.3.0-osx-arm64.dmg" },
              { "name": "SonNuage-1.3.0-osx-x64.dmg", "url": "https://git.example.org/p/SonNuage-1.3.0-osx-x64.dmg" },
              { "name": "SonNuage-1.3.0-linux-x64.AppImage", "url": "https://git.example.org/p/SonNuage-1.3.0-linux-x64.AppImage" },
              { "name": "sonnuage_1.3.0_amd64.deb", "url": "https://git.example.org/p/sonnuage_1.3.0_amd64.deb" },
              { "name": "SonNuage-1.3.0-linux-x64.tar.gz", "url": "https://git.example.org/p/SonNuage-1.3.0-linux-x64.tar.gz" },
              { "name": "SHA256SUMS", "url": "https://git.example.org/p/SHA256SUMS" }
            ]
          }
        }
        """;

    [Fact]
    public void A_gitlab_release_is_read()
    {
        var release = ReleaseInfo.Parse(Release())!;

        Assert.Equal(new Version(1, 3, 0), release.Version);
        Assert.Equal("https://git.example.org/sonnuage/sonnuage-desktop/-/releases/v1.3.0", release.Page.ToString());
        Assert.Equal(8, release.Assets.Count);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "tag_name": "nightly" }""")]
    [InlineData("not json")]
    public void An_unreadable_release_is_ignored(string json)
    {
        Assert.Null(ReleaseInfo.Parse(json));
    }

    [Theory]
    [InlineData("1.2.9", true)]
    [InlineData("1.3.0", false)]
    [InlineData("1.10.0", false)]
    // Builds de développement et de CI : jamais remplacés par une version publiée.
    [InlineData("0.1.0-ci.42", false)]
    [InlineData("garbage", false)]
    public void Only_a_newer_release_is_offered(string current, bool offered)
    {
        var release = ReleaseInfo.Parse(Release())!;

        Assert.Equal(offered, AvailableUpdate.Pick(current, release, InstallKind.WindowsSetup) is not null);
    }

    [Theory]
    [InlineData(InstallKind.WindowsSetup, "SonNuage-1.3.0-win-x64-setup.exe")]
    [InlineData(InstallKind.MacArm64, "SonNuage-1.3.0-osx-arm64.dmg")]
    [InlineData(InstallKind.MacX64, "SonNuage-1.3.0-osx-x64.dmg")]
    [InlineData(InstallKind.LinuxAppImage, "SonNuage-1.3.0-linux-x64.AppImage")]
    [InlineData(InstallKind.LinuxDeb, "sonnuage_1.3.0_amd64.deb")]
    [InlineData(InstallKind.Portable, null)]
    public void The_installer_matches_how_the_app_was_installed(InstallKind kind, string? asset)
    {
        var update = AvailableUpdate.Pick("1.2.0", ReleaseInfo.Parse(Release())!, kind)!;

        Assert.Equal(asset, update.Installer?.Name);
        Assert.Equal("1.3.0", update.Version);
    }

    [Fact]
    public void The_checksum_of_an_asset_is_found_in_sha256sums()
    {
        var sums = "0f00  SonNuage-1.3.0-win-x64.zip\nabcdef0123  SonNuage-1.3.0-win-x64-setup.exe\n";

        Assert.Equal("abcdef0123", Checksums.Find(sums, "SonNuage-1.3.0-win-x64-setup.exe"));
        Assert.Equal("0f00", Checksums.Find("0F00 *SonNuage-1.3.0-win-x64.zip", "SonNuage-1.3.0-win-x64.zip"));
        Assert.Null(Checksums.Find(sums, "other"));
    }

    [Fact]
    public async Task A_newer_release_becomes_available()
    {
        var updates = Service("1.2.0", _ => Ok(Release()));

        await updates.CheckAsync();

        Assert.Equal(UpdateState.Available, updates.State);
        Assert.Equal("1.3.0", updates.Available!.Version);
    }

    [Fact]
    public async Task The_same_release_means_up_to_date()
    {
        var updates = Service("1.3.0", _ => Ok(Release()));

        await updates.CheckAsync();

        Assert.Equal(UpdateState.UpToDate, updates.State);
        Assert.Null(updates.Available);
    }

    [Fact]
    public async Task A_failed_check_is_reported_and_tried_again_later()
    {
        var calls = 0;
        var updates = Service("1.2.0", _ => ++calls == 1 ? new HttpResponseMessage(HttpStatusCode.NotFound) : Ok(Release()));

        await updates.CheckAsync();
        Assert.Equal(UpdateState.Failed, updates.State);

        await updates.CheckAsync();
        Assert.Equal(UpdateState.Available, updates.State);
    }

    [Fact]
    public async Task Without_a_feed_nothing_is_checked()
    {
        var updates = new UpdateService(new HttpClient(new Handler(_ => throw new InvalidOperationException())), null, "1.2.0", InstallKind.WindowsSetup, _dir);

        await updates.CheckAsync();

        Assert.Equal(UpdateState.Disabled, updates.State);
    }

    [Fact]
    public async Task The_installer_is_downloaded_and_checked_against_sha256sums()
    {
        var installer = Encoding.UTF8.GetBytes("setup");
        var sum = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(installer)).ToLowerInvariant();
        var updates = Service("1.2.0", r => r.RequestUri!.AbsolutePath switch
        {
            "/p/SHA256SUMS" => Ok($"{sum}  SonNuage-1.3.0-win-x64-setup.exe\n"),
            "/p/SonNuage-1.3.0-win-x64-setup.exe" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(installer) },
            _ => Ok(Release()),
        });
        await updates.CheckAsync();

        var file = await updates.DownloadAsync(updates.Available!.Installer!, CancellationToken.None);

        Assert.Equal(installer, await File.ReadAllBytesAsync(file, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_download_that_does_not_match_its_checksum_is_refused()
    {
        var updates = Service("1.2.0", r => r.RequestUri!.AbsolutePath switch
        {
            "/p/SHA256SUMS" => Ok("00  SonNuage-1.3.0-win-x64-setup.exe\n"),
            "/p/SonNuage-1.3.0-win-x64-setup.exe" => Ok("tampered"),
            _ => Ok(Release()),
        });
        await updates.CheckAsync();

        await Assert.ThrowsAsync<InvalidDataException>(() => updates.DownloadAsync(updates.Available!.Installer!, CancellationToken.None));
        Assert.Empty(Directory.Exists(_dir) ? Directory.GetFiles(_dir) : []);
    }

    private UpdateService Service(string current, Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new HttpClient(new Handler(respond)), new Uri(Feed), current, InstallKind.WindowsSetup, _dir);

    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
