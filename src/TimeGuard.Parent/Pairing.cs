using System.IO;
using System.Text.Json;
using TimeGuard.Core;

namespace TimeGuard.Parent;

/// <summary>
/// Файл сопряжения для переноса на клиент: содержит секрет, id машины и хэш
/// мастер-пароля. Импортируется службой командой
/// <c>TimeGuard.Service.exe --import &lt;файл&gt;</c>.
/// </summary>
public static class Pairing
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static void Export(ParentConfig cfg, string path)
    {
        var guard = new GuardConfig
        {
            SecretBase64 = cfg.SecretBase64,
            MachineId = cfg.ClientMachineId,
            MasterHashBase64 = cfg.MasterHashBase64,
            MasterSaltBase64 = cfg.MasterSaltBase64,
        };
        File.WriteAllText(path, JsonSerializer.Serialize(guard, Json));
    }

    /// <summary>
    /// Импортировать пару из файла сопряжения (для установки родителя на второй ПК).
    /// Возвращает конфигурацию с тем же секретом; метку клиента задаёт вызывающий.
    /// </summary>
    public static ParentConfig? Import(string path)
    {
        try
        {
            GuardConfig? g = JsonSerializer.Deserialize<GuardConfig>(File.ReadAllText(path), Json);
            if (g is null || string.IsNullOrEmpty(g.SecretBase64) || string.IsNullOrEmpty(g.MachineId) ||
                g.GetSecret().Length < 16 || Convert.FromBase64String(g.MasterHashBase64).Length != 32 ||
                Convert.FromBase64String(g.MasterSaltBase64).Length != 16)
            {
                return null;
            }
            return new ParentConfig
            {
                SecretBase64 = g.SecretBase64,
                ClientMachineId = g.MachineId,
                MasterHashBase64 = g.MasterHashBase64,
                MasterSaltBase64 = g.MasterSaltBase64,
                ClientLabel = string.Empty,
            };
        }
        catch (Exception)
        {
            return null;
        }
    }
}
