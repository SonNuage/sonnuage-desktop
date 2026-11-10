using SonNuage.Desktop.Services;

namespace SonNuage.Desktop.Tests;

public sealed class AutostartTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sonnuage-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Linux_writes_an_xdg_autostart_entry_and_removes_it()
    {
        var autostart = new LinuxAutostart(_dir, "/opt/SonNuage/SonNuage");
        var entry = Path.Combine(_dir, "autostart", "sonnuage.desktop");

        Assert.False(autostart.IsEnabled);
        autostart.Enable();

        Assert.True(autostart.IsEnabled);
        var text = File.ReadAllText(entry);
        Assert.Contains("[Desktop Entry]", text);
        Assert.Contains("Type=Application", text);
        Assert.Contains("Exec=\"/opt/SonNuage/SonNuage\" --autostart", text);

        autostart.Disable();
        Assert.False(autostart.IsEnabled);
        Assert.False(File.Exists(entry));
    }

    [Theory]
    [InlineData("/home/léa/opt/SonNuage/SonNuage", "\"/home/léa/opt/SonNuage/SonNuage\"")]
    [InlineData("/home/a b/$HOME/x\"y`z\\w", "\"/home/a b/\\\\$HOME/x\\\\\"y\\\\`z\\\\\\\\w\"")]
    [InlineData("/opt/100%/SonNuage", "\"/opt/100%%/SonNuage\"")]
    public void Linux_quotes_the_program_as_the_desktop_entry_spec_wants(string program, string quoted)
    {
        Assert.Equal(quoted, LinuxAutostart.Quote(program));
    }

    [Fact]
    public void Disabling_twice_is_harmless()
    {
        var autostart = new LinuxAutostart(_dir, "/usr/bin/sonnuage");
        autostart.Disable();
        autostart.Disable();
        Assert.False(autostart.IsEnabled);
    }

    [Fact]
    public void Mac_writes_a_launch_agent_that_opens_the_app_bundle()
    {
        var autostart = new MacAutostart(_dir, "/Applications/Son & Nuage.app/Contents/MacOS/SonNuage");
        var plist = Path.Combine(_dir, "Library", "LaunchAgents", "org.sonnuage.desktop.plist");

        autostart.Enable();

        Assert.True(autostart.IsEnabled);
        var text = File.ReadAllText(plist);
        Assert.Contains("<string>org.sonnuage.desktop</string>", text);
        Assert.Contains("<key>RunAtLoad</key>", text);
        Assert.Contains("<string>/usr/bin/open</string>", text);
        // Le paquet .app, pas l'exécutable : macOS le lance comme depuis le Finder.
        Assert.Contains("<string>/Applications/Son &amp; Nuage.app</string>", text);
        Assert.Contains("<string>--autostart</string>", text);

        autostart.Disable();
        Assert.False(autostart.IsEnabled);
    }

    [Fact]
    public void Mac_outside_a_bundle_runs_the_program_itself()
    {
        Assert.Equal(["/Users/a/dev/SonNuage", "--autostart"], MacAutostart.Arguments("/Users/a/dev/SonNuage"));
    }

    [Fact]
    public void Windows_registers_the_program_under_the_run_key()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "registre Windows");
        var key = $@"Software\SonNuageTests\{Guid.NewGuid():N}";
        try
        {
            var autostart = new WindowsAutostart(@"C:\Program Files\SonNuage\SonNuage.exe", key);
            Assert.False(autostart.IsEnabled);

            autostart.Enable();
            Assert.True(autostart.IsEnabled);
            Assert.Equal("\"C:\\Program Files\\SonNuage\\SonNuage.exe\" --autostart", autostart.Command);

            autostart.Disable();
            Assert.False(autostart.IsEnabled);
        }
        finally
        {
            if (OperatingSystem.IsWindows()) Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\SonNuageTests", throwOnMissingSubKey: false);
        }
    }

    [Theory]
    [InlineData(new[] { "--autostart" }, true, true)]
    [InlineData(new[] { "--autostart" }, false, false)]
    [InlineData(new string[0], true, false)]
    public void The_window_starts_hidden_only_when_launched_with_the_session(string[] args, bool startHidden, bool expected)
    {
        Assert.Equal(expected, DesktopOptions.ShouldStartHidden(args, startHidden));
    }

    [Fact]
    public async Task Start_hidden_is_off_until_chosen_and_remembered()
    {
        var store = new DesktopStore(_dir);
        var options = new DesktopOptions(store, new LinuxAutostart(_dir, "/usr/bin/sonnuage"));

        Assert.False(await options.StartHiddenAsync());
        await options.SetStartHiddenAsync(true);

        Assert.True(await new DesktopOptions(new DesktopStore(_dir), new LinuxAutostart(_dir, "/usr/bin/sonnuage")).StartHiddenAsync());
    }

    [Fact]
    public async Task Start_with_session_is_off_by_default_and_follows_the_system_entry()
    {
        var autostart = new LinuxAutostart(_dir, "/usr/bin/sonnuage");
        var options = new DesktopOptions(new DesktopStore(_dir), autostart);

        Assert.False(options.StartWithSession);
        await options.SetStartWithSessionAsync(true);
        Assert.True(autostart.IsEnabled);
        Assert.True(options.StartWithSession);
        await options.SetStartWithSessionAsync(false);
        Assert.False(options.StartWithSession);
    }
}
