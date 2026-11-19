using System.Text.Json;

namespace TimeGuard.Core;

/// <summary>Имена сообщений в канале служба↔агент.</summary>
public static class IpcKind
{
    public const string Hello = "Hello";           // агент → служба
    public const string Status = "Status";         // служба → агент (push)
    public const string SubmitCode = "SubmitCode"; // агент → служба
    public const string SubmitMaster = "SubmitMaster"; // агент → служба
    public const string Result = "Result";         // служба → агент (ответ)
}

/// <summary>
/// Плоское сообщение канала. Передаётся как одна строка JSON с завершающим '\n'
/// (newline-delimited JSON) по именованному каналу. Плоская схема выбрана намеренно,
/// чтобы не тянуть полиморфную сериализацию.
/// </summary>
public sealed class IpcMessage
{
    public string Kind { get; set; } = string.Empty;

    // Status
    public long? RemainingSeconds { get; set; }
    public bool? IsLocked { get; set; }
    public bool? IsNight { get; set; }
    public int? CurfewStartHour { get; set; }
    public int? CurfewEndHour { get; set; }

    // SubmitCode / SubmitMaster
    public string? Text { get; set; }

    // Result
    public bool? Accepted { get; set; }
    public int? AddedMinutes { get; set; }

    public static IpcMessage Status(long remainingSeconds, bool isLocked, bool isNight = false, int? curfewStartHour = null, int? curfewEndHour = null) => new()
    {
        Kind = IpcKind.Status,
        RemainingSeconds = remainingSeconds,
        IsLocked = isLocked,
        IsNight = isNight,
        CurfewStartHour = curfewStartHour,
        CurfewEndHour = curfewEndHour,
    };

    public static IpcMessage Result(bool accepted, string message, int addedMinutes = 0) => new()
    {
        Kind = IpcKind.Result,
        Accepted = accepted,
        Text = message,
        AddedMinutes = addedMinutes,
    };

    public static IpcMessage Hello() => new() { Kind = IpcKind.Hello };
    public static IpcMessage Code(string code) => new() { Kind = IpcKind.SubmitCode, Text = code };
    public static IpcMessage Master(string pwd) => new() { Kind = IpcKind.SubmitMaster, Text = pwd };
}

/// <summary>Сериализация newline-delimited JSON.</summary>
public static class IpcSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(IpcMessage message) =>
        JsonSerializer.Serialize(message, Options);

    public static IpcMessage? Deserialize(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize<IpcMessage>(line, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
