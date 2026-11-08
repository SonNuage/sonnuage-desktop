using System.Text;
using SonNuage.Desktop.Services;

namespace SonNuage.Desktop.Tests;

public sealed class DesktopStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sonnuage-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private string SettingsFile => Path.Combine(_dir, "settings.json");
    private string SessionFile => Path.Combine(_dir, "session.json");

    [Fact]
    public async Task A_value_survives_a_restart()
    {
        await new DesktopStore(_dir).SetAsync("sonnuage.theme", "dark");

        Assert.Equal("dark", await new DesktopStore(_dir).GetAsync("sonnuage.theme"));
    }

    [Fact]
    public async Task An_unknown_key_reads_as_null()
    {
        Assert.Null(await new DesktopStore(_dir).GetAsync("sonnuage.theme"));
    }

    [Fact]
    public async Task Setting_null_removes_the_value()
    {
        var store = new DesktopStore(_dir);
        await store.SetAsync("sonnuage.theme", "dark");
        await store.SetAsync("sonnuage.theme", null);

        Assert.Null(await new DesktopStore(_dir).GetAsync("sonnuage.theme"));
    }

    [Fact]
    public async Task Session_tokens_stay_out_of_the_settings_file()
    {
        var key = $"sonnuage.session.{Guid.NewGuid():N}";
        var store = new DesktopStore(_dir);
        await store.SetAsync(key, "secret-token");
        await store.SetAsync("sonnuage.theme", "dark");

        Assert.DoesNotContain("secret-token", File.ReadAllText(SettingsFile));
        Assert.True(File.Exists(SessionFile));
        Assert.Equal("secret-token", await new DesktopStore(_dir).GetAsync(key));
    }

    [Fact]
    public async Task Session_file_is_encrypted_on_windows_and_private_elsewhere()
    {
        await new DesktopStore(_dir).SetAsync("sonnuage.session", "secret-token");

        if (OperatingSystem.IsWindows())
            Assert.DoesNotContain("secret-token", Encoding.UTF8.GetString(File.ReadAllBytes(SessionFile)));
        else
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(SessionFile));
    }

    [Fact]
    public async Task A_damaged_file_starts_over_empty()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsFile, "{ not json");
        File.WriteAllText(SessionFile, "garbage");

        var store = new DesktopStore(_dir);

        Assert.Null(await store.GetAsync("sonnuage.theme"));
        Assert.Null(await store.GetAsync("sonnuage.session"));
    }

    [Fact]
    public async Task Writing_leaves_no_temporary_file()
    {
        await new DesktopStore(_dir).SetAsync("sonnuage.theme", "dark");

        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }
}
