using SonNuage.Desktop.Services;

namespace SonNuage.Desktop.Tests;

public sealed class SingleInstanceTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sonnuage-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Only_the_first_launch_owns_the_app()
    {
        using var first = SingleInstance.TryAcquire(_dir);

        Assert.NotNull(first);
        Assert.Null(SingleInstance.TryAcquire(_dir));
    }

    [Fact]
    public async Task A_second_launch_brings_the_first_to_front()
    {
        using var first = SingleInstance.TryAcquire(_dir)!;
        var activated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        first.Activated += () => activated.TrySetResult();

        Assert.True(await SingleInstance.ActivateRunningAsync(_dir, Wait));
        await activated.Task.WaitAsync(Wait, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Every_later_launch_is_heard()
    {
        using var first = SingleInstance.TryAcquire(_dir)!;
        var count = 0;
        var twice = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        first.Activated += () =>
        {
            if (Interlocked.Increment(ref count) == 2) twice.TrySetResult();
        };

        Assert.True(await SingleInstance.ActivateRunningAsync(_dir, Wait));
        Assert.True(await SingleInstance.ActivateRunningAsync(_dir, Wait));
        await twice.Task.WaitAsync(Wait, TestContext.Current.CancellationToken);
    }

    [Fact]
    public void The_app_can_start_again_once_the_first_has_quit()
    {
        SingleInstance.TryAcquire(_dir)!.Dispose();

        using var again = SingleInstance.TryAcquire(_dir);
        Assert.NotNull(again);
    }

    [Fact]
    public void A_socket_left_by_a_crash_does_not_block_the_next_start()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SingleInstance.SocketPath(_dir), "");

        using var instance = SingleInstance.TryAcquire(_dir);
        Assert.NotNull(instance);
    }

    [Fact]
    public async Task Without_a_running_app_activation_gives_up()
    {
        Assert.False(await SingleInstance.ActivateRunningAsync(_dir, TimeSpan.FromMilliseconds(300)));
    }

    [Fact]
    public void A_socket_path_too_long_for_unix_moves_to_the_temp_folder()
    {
        var deep = Path.Combine(_dir, new string('a', 120));

        Assert.True(SingleInstance.SocketPath(deep).Length <= SingleInstance.MaxSocketPath);
        Assert.Equal(Path.Combine(_dir, "app.sock"), SingleInstance.SocketPath(_dir));
    }
}
