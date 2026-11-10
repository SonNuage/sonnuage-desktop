using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace SonNuage.Desktop.Services;

/// <summary>
/// Une seule appli par utilisateur. La première tient un verrou exclusif sur instance.lock (dossier de données ; le
/// système le libère si elle plante) et écoute sur une socket Unix, app.sock (Windows 10 les connaît aussi). Un
/// lancement suivant n'obtient pas le verrou : il se connecte, la première ramène sa fenêtre, et il s'arrête.
/// </summary>
public sealed partial class SingleInstance : IDisposable
{
    /// <summary>Taille utile de sun_path : 104 octets sous macOS, 108 sous Linux et Windows, zéro final compris.</summary>
    public const int MaxSocketPath = 100;

    private readonly FileStream _lock;
    private readonly Socket? _listener;
    private readonly string _socketPath;
    private readonly CancellationTokenSource _stop = new();

    /// <summary>Un autre lancement demande la fenêtre. Appelé hors du thread de la fenêtre.</summary>
    public event Action? Activated;

    private SingleInstance(FileStream lockFile, Socket? listener, string socketPath)
    {
        _lock = lockFile;
        _listener = listener;
        _socketPath = socketPath;
        if (listener is not null) _ = ListenAsync(listener);
    }

    /// <summary>Adresse de la socket : dans le dossier de données, ou dans le dossier temporaire s'il est trop profond.</summary>
    public static string SocketPath(string directory)
    {
        var path = Path.Combine(directory, "app.sock");
        if (Encoding.UTF8.GetByteCount(path) <= MaxSocketPath) return path;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(directory)))[..16].ToLowerInvariant();
        return Path.Combine(Path.GetTempPath(), $"sonnuage-{hash}.sock");
    }

    /// <summary>null : une autre appli tient déjà le verrou.</summary>
    public static SingleInstance? TryAcquire(string directory)
    {
        Directory.CreateDirectory(directory);
        FileStream lockFile;
        try
        {
            lockFile = new FileStream(Path.Combine(directory, "instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return null;
        }
        var path = SocketPath(directory);
        return new SingleInstance(lockFile, Listen(path), path);
    }

    /// <summary>
    /// Demande à l'appli déjà lancée de montrer sa fenêtre. Elle peut être en train de démarrer (verrou pris, socket
    /// pas encore ouverte) : on réessaie jusqu'à <paramref name="timeout"/>.
    /// </summary>
    public static async Task<bool> ActivateRunningAsync(string directory, TimeSpan timeout)
    {
        // Windows ne laisse passer au premier plan que l'appli qui a la main : ce lancement-ci la cède à l'autre.
        if (OperatingSystem.IsWindows()) AllowSetForegroundWindow(-1 /* ASFW_ANY */);
        var endpoint = new UnixDomainSocketEndPoint(SocketPath(directory));
        var deadline = DateTime.UtcNow + timeout;
        do
        {
            try
            {
                using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                await socket.ConnectAsync(endpoint);
                return true;
            }
            catch (SocketException)
            {
                await Task.Delay(100);
            }
        }
        while (DateTime.UtcNow < deadline);
        return false;
    }

    /// <summary>null si la socket ne s'ouvre pas : l'appli reste unique, seul le retour au premier plan manque.</summary>
    private static Socket? Listen(string path)
    {
        Socket? socket = null;
        try
        {
            // Le verrou est à nous : une socket encore là vient d'une appli qui a planté.
            File.Delete(path);
            socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Bind(new UnixDomainSocketEndPoint(path));
            socket.Listen(4);
            return socket;
        }
        catch (Exception ex) when (ex is SocketException or IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            socket?.Dispose();
            Console.Error.WriteLine($"Retour au premier plan indisponible : {ex.Message}");
            return null;
        }
    }

    private async Task ListenAsync(Socket listener)
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                using var client = await listener.AcceptAsync(_stop.Token);
                Activated?.Invoke();
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                // Connexion coupée avant d'être acceptée : on attend la suivante.
            }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener?.Dispose();
        try
        {
            if (_listener is not null) File.Delete(_socketPath);
        }
        catch (IOException)
        {
        }
        _lock.Dispose();
        _stop.Dispose();
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(int processId);
}
