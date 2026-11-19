using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TimeGuard.Core;

namespace TimeGuard.Service.Storage;

/// <summary>
/// Хранение конфигурации и состояния в %ProgramData%\TimeGuard, зашифрованных
/// DPAPI на уровне машины (LocalMachine). Файлы читаемы только на этой машине.
/// </summary>
public sealed class StateStore : IGuardStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TimeGuard.v1");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    private readonly string _dir;
    private readonly string _configPath;
    private readonly string _statePath;
    private readonly object _gate = new();

    public StateStore(string? baseDir = null)
    {
        _dir = baseDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "TimeGuard");
        Directory.CreateDirectory(_dir);
        _configPath = Path.Combine(_dir, "config.dat");
        _statePath = Path.Combine(_dir, "state.dat");
    }

    public bool ConfigExists => File.Exists(_configPath);

    /// <summary>Папка данных (%ProgramData%\TimeGuard).</summary>
    public string DirectoryPath => _dir;

    public GuardConfig? LoadConfig()
    {
        lock (_gate)
        {
            return Read<GuardConfig>(_configPath);
        }
    }

    public void SaveConfig(GuardConfig config)
    {
        lock (_gate)
        {
            Write(_configPath, config);
        }
    }

    public GuardState LoadState()
    {
        lock (_gate)
        {
            return Read<GuardState>(_statePath) ?? new GuardState();
        }
    }

    public void SaveState(GuardState state)
    {
        lock (_gate)
        {
            state.SavedAtUtc = DateTimeOffset.UtcNow;
            Write(_statePath, state);
        }
    }

    private static T? Read<T>(string path) where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }
        try
        {
            byte[] encrypted = File.ReadAllBytes(path);
            byte[] plain = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.LocalMachine);
            return JsonSerializer.Deserialize<T>(plain, Json);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or IOException)
        {
            return null;
        }
    }

    private static void Write<T>(string path, T value)
    {
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        byte[] encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.LocalMachine);
        string tmp = path + ".tmp";
        File.WriteAllBytes(tmp, encrypted);
        File.Move(tmp, path, overwrite: true); // атомарная замена
    }
}
