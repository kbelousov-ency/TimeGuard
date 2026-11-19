using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TimeGuard.Parent;

/// <summary>Локальная конфигурация родителя (секрет, id клиента, счётчик кодов).</summary>
public sealed class ParentConfig
{
    public string SecretBase64 { get; set; } = string.Empty;
    public string ClientMachineId { get; set; } = string.Empty;
    public string ClientLabel { get; set; } = string.Empty;
    public string MasterHashBase64 { get; set; } = string.Empty;
    public string MasterSaltBase64 { get; set; } = string.Empty;
    /// <summary>Имя или IP клиента в локальной сети для запроса истории.</summary>
    public string ClientHost { get; set; } = string.Empty;
}

/// <summary>
/// Хранит конфигурацию родителя в %APPDATA%\TimeGuardParent, зашифровав DPAPI
/// (CurrentUser). Секрет не лежит в открытом виде.
/// </summary>
public sealed class ParentStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TimeGuardParent.v1");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly string _path;

    public ParentStore(string? baseDir = null)
    {
        string dir = baseDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TimeGuardParent");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "parent.dat");
    }

    private sealed class Profiles
    {
        public List<ParentConfig> Clients { get; set; } = new();
        public string SelectedId { get; set; } = "";
    }
    private Profiles? _profiles;
    public IReadOnlyList<ParentConfig> Clients { get { Load(); return _profiles!.Clients; } }

    public bool Exists => File.Exists(_path);

    public ParentConfig? Load()
    {
        if (_profiles is not null) return _profiles.Clients.FirstOrDefault(c => c.ClientMachineId == _profiles.SelectedId) ?? _profiles.Clients.FirstOrDefault();
        if (!File.Exists(_path))
        {
            _profiles = new Profiles();
            return null;
        }
        try
        {
            byte[] enc = File.ReadAllBytes(_path);
            byte[] plain = ProtectedData.Unprotect(enc, Entropy, DataProtectionScope.CurrentUser);
            using var document = JsonDocument.Parse(plain);
            if (document.RootElement.TryGetProperty("clients", out _))
                _profiles = JsonSerializer.Deserialize<Profiles>(plain, Json) ?? new Profiles();
            else
            {
                var legacy = JsonSerializer.Deserialize<ParentConfig>(plain, Json)!;
                _profiles = new Profiles { Clients = new() { legacy }, SelectedId = legacy.ClientMachineId };
                if (!File.Exists(_path + ".single-client-backup")) File.Copy(_path, _path + ".single-client-backup");
            }
            return Load();
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or IOException)
        {
            throw new InvalidOperationException("Cannot load parent profiles; existing data was preserved.", ex);
        }
    }

    public void Save(ParentConfig config)
    {
        Load();
        int index = _profiles!.Clients.FindIndex(c => c.ClientMachineId == config.ClientMachineId);
        if (index < 0) _profiles.Clients.Add(config); else _profiles.Clients[index] = config;
        _profiles.SelectedId = config.ClientMachineId;
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(_profiles, Json);
        byte[] enc = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_path + ".tmp", enc);
        File.Move(_path + ".tmp", _path, overwrite: true);
    }
}
