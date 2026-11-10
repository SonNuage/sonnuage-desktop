using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using SonNuage.UI.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace SonNuage.Desktop.Services;

/// <summary>
/// Réglages dans un fichier JSON du profil (settings.json). La session et ses jetons vont dans un fichier à part
/// (session.json) : chiffré par DPAPI sous Windows, lisible par le seul utilisateur sous macOS et Linux. Clés de
/// session : celles d'<see cref="AccountStore"/>, <see cref="AccountStore.SessionBaseKey"/> et ses variantes par compte.
/// </summary>
public sealed class DesktopStore : IKeyValueStore
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly string _settingsPath;
    private readonly string _sessionPath;
    private Dictionary<string, string>? _settings;
    private Dictionary<string, string>? _session;

    public DesktopStore(string directory)
    {
        Directory.CreateDirectory(directory);
        _settingsPath = Path.Combine(directory, "settings.json");
        _sessionPath = Path.Combine(directory, "session.json");
    }

    public async Task<string?> GetAsync(string key)
    {
        await _lock.WaitAsync();
        try
        {
            return File(key).TryGetValue(key, out var value) ? value : null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SetAsync(string key, string? value)
    {
        await _lock.WaitAsync();
        try
        {
            var secure = IsSecure(key);
            var values = File(key);
            if (value is null) values.Remove(key);
            else values[key] = value;
            var json = JsonSerializer.SerializeToUtf8Bytes(values);
            await WriteAsync(secure ? _sessionPath : _settingsPath, secure ? Protect(json) : json, secure);
        }
        finally
        {
            _lock.Release();
        }
    }

    private static bool IsSecure(string key) => key.StartsWith(AccountStore.SessionBaseKey, StringComparison.Ordinal);

    private Dictionary<string, string> File(string key) => IsSecure(key)
        ? _session ??= Read(_sessionPath, secure: true)
        : _settings ??= Read(_settingsPath, secure: false);

    private static Dictionary<string, string> Read(string path, bool secure)
    {
        try
        {
            if (!System.IO.File.Exists(path)) return [];
            var bytes = System.IO.File.ReadAllBytes(path);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(secure ? Unprotect(bytes) : bytes) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or CryptographicException)
        {
            // Fichier abîmé, ou session chiffrée sur une autre machine (profil copié) : on repart de zéro.
            return [];
        }
    }

    /// <summary>Écriture dans un fichier temporaire puis remplacement : un arrêt brutal ne laisse pas un fichier tronqué.</summary>
    private static async Task WriteAsync(string path, byte[] bytes, bool secure)
    {
        var temp = path + ".tmp";
        await System.IO.File.WriteAllBytesAsync(temp, bytes);
        if (secure && !OperatingSystem.IsWindows()) System.IO.File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        System.IO.File.Move(temp, path, overwrite: true);
    }

    private static byte[] Protect(byte[] data) =>
        OperatingSystem.IsWindows() ? ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser) : data;

    private static byte[] Unprotect(byte[] data) =>
        OperatingSystem.IsWindows() ? ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser) : data;
}

/// <summary>
/// Adresse du serveur : celle du compte actif, ou celle saisie pour le compte en cours d'ajout tant qu'aucun compte
/// n'est actif (comme sur Android).
/// </summary>
public sealed class AccountEndpoint : IServerEndpoint
{
    private readonly AccountStore _accounts;

    public AccountEndpoint(AccountStore accounts)
    {
        _accounts = accounts;
        accounts.ActiveChanged += () => Changed?.Invoke();
        accounts.Changed += () => Changed?.Invoke();
    }

    public Uri? BaseAddress => _accounts.Active?.Server ?? _accounts.PendingServer;
    public bool IsConfigurable => true;
    public event Action? Changed;

    public Task SetAsync(Uri? address)
    {
        // Uri relative "api/..." doit se résoudre sous le chemin éventuel du serveur.
        if (address is not null && !address.AbsolutePath.EndsWith('/')) address = new Uri(address + "/");
        _accounts.SetPendingServer(address);
        return Task.CompletedTask;
    }
}

public sealed class DesktopPlatform : IPlatformInfo
{
    public bool HasMeteredNetworkDetection => false;
    public string Name => "desktop";
    /// <summary>Version publiée de l'application, affichée dans l'aide et le menu du compte.</summary>
    public string? AppVersion => SonNuage.Contracts.AppVersion.Of(typeof(DesktopPlatform).Assembly);
    // Composants que SonNuage.UI affiche à partir de sa version 1.25 (IPlatformInfo) ; sans effet avec une version
    // plus ancienne, qui ne les demande pas. Seuls des types de base : le code compile avec les deux.
    /// <summary>Lancement avec la session, démarrage caché, mises à jour : dans Réglages.</summary>
    public IReadOnlyList<Type> SettingsSections => [typeof(Components.DesktopSettingsPanel)];
    /// <summary>Bandeau « nouvelle version », en haut de l'interface.</summary>
    public IReadOnlyList<Type> Notices => [typeof(Components.UpdateBanner)];
}

/// <summary>
/// IJSRuntime de la page, pour le lecteur qui vit toute la durée de l'appli. Dans une webview, IJSRuntime est propre
/// à la page (scoped) : <see cref="JsBridge"/> le transmet ici à son ouverture, et les appels faits avant attendent
/// ce moment. Donne aussi accès au fil de Blazor, pour les commandes venues de l'icône de la barre système.
/// </summary>
public sealed class PageJsRuntime : IJSRuntime
{
    private TaskCompletionSource<IJSRuntime> _page = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Func<Func<Task>, Task>? _dispatch;

    public void Attach(IJSRuntime js, Func<Func<Task>, Task> dispatch)
    {
        if (_page.Task.IsCompleted) _page = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _dispatch = dispatch;
        _page.SetResult(js);
    }

    /// <summary>Exécute sur le fil de Blazor (rien tant que la page n'est pas ouverte).</summary>
    public Task DispatchAsync(Func<Task> work) => _dispatch?.Invoke(work) ?? Task.CompletedTask;

    public async ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)] TValue>(string identifier, object?[]? args) =>
        await (await _page.Task).InvokeAsync<TValue>(identifier, args);

    public async ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)] TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
        await (await _page.Task).InvokeAsync<TValue>(identifier, cancellationToken, args);
}

/// <summary>Composant racine invisible : donne l'IJSRuntime de la page à <see cref="PageJsRuntime"/>.</summary>
public sealed class JsBridge : ComponentBase
{
    [Inject] private IJSRuntime Js { get; set; } = default!;
    [Inject] private PageJsRuntime Page { get; set; } = default!;

    protected override void OnInitialized() => Page.Attach(Js, InvokeAsync);
}
