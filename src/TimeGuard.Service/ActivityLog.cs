using System.Text;
using System.Text.Json;
using TimeGuard.Core;

namespace TimeGuard.Service;

/// <summary>
/// Журнал работы службы и обнаруженных приложений.
/// Текстовый activity.log оставлен для диагностики, а history.jsonl содержит
/// структурированные записи, которые можно запросить с родителя.
/// </summary>
public sealed class ActivityLog
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly string _path;
    private readonly string _historyPath;
    private readonly object _gate = new();
    private DateOnly _lastDate;

    public ActivityLog(string dir)
    {
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "activity.log");
        _historyPath = Path.Combine(dir, "history.jsonl");
        try
        {
            // не дублировать заголовок даты при перезапуске службы в тот же день
            if (File.Exists(_path))
            {
                _lastDate = DateOnly.FromDateTime(File.GetLastWriteTime(_path));
            }
        }
        catch (IOException)
        {
        }
    }

    public void Write(string message)
    {
        lock (_gate)
        {
            AppendTextCore(message, DateTimeOffset.Now);
        }
    }

    public void StartProcess(string processName, int processId, DateTimeOffset startedAt)
    {
        var record = new ActivityRecord
        {
            ProcessName = processName,
            ProcessId = processId,
            StartedAt = startedAt,
        };
        lock (_gate)
        {
            AppendTextCore($"Запущено: {processName}.exe (PID {processId})", startedAt);
            AppendHistoryCore(record);
        }
    }

    public void CloseProcess(string processName, int processId, DateTimeOffset startedAt, DateTimeOffset closedAt)
    {
        var record = new ActivityRecord
        {
            ProcessName = processName,
            ProcessId = processId,
            StartedAt = startedAt,
            ClosedAt = closedAt,
        };
        lock (_gate)
        {
            AppendTextCore($"Закрыто: {processName}.exe (PID {processId})", closedAt);
            AppendHistoryCore(record);
        }
    }

    public IReadOnlyList<ActivityRecord> ReadRecent(int limit = HistoryProtocol.MaxEntries)
    {
        lock (_gate)
        {
            try
            {
                var records = new Dictionary<string, ActivityRecord>(StringComparer.Ordinal);
                if (File.Exists(_historyPath))
                {
                    foreach (string line in File.ReadLines(_historyPath, Encoding.UTF8))
                    {
                        try
                        {
                            ActivityRecord? record = JsonSerializer.Deserialize<ActivityRecord>(line, Json);
                            if (record is null || string.IsNullOrWhiteSpace(record.ProcessName))
                            {
                                continue;
                            }
                            records[Key(record)] = record;
                        }
                        catch (JsonException)
                        {
                        }
                    }
                }

                return records.Values
                    .OrderByDescending(r => r.StartedAt)
                    .Take(Math.Clamp(limit, 1, HistoryProtocol.MaxEntries))
                    .ToArray();
            }
            catch (IOException)
            {
                return Array.Empty<ActivityRecord>();
            }
        }
    }

    private static string Key(ActivityRecord record) =>
        $"{record.ProcessId}:{record.StartedAt.UtcDateTime.Ticks}:{record.ProcessName}";

    private void AppendTextCore(string message, DateTimeOffset now)
    {
        try
        {
            DateOnly today = DateOnly.FromDateTime(now.LocalDateTime);
            var sb = new StringBuilder();
            if (today != _lastDate)
            {
                if (_lastDate != default)
                {
                    sb.AppendLine();
                }
                sb.AppendLine($"===== {today:yyyy-MM-dd} =====");
                _lastDate = today;
            }
            sb.AppendLine($"{now:HH:mm:ss}  {message}");
            File.AppendAllText(_path, sb.ToString(), Encoding.UTF8);
        }
        catch (Exception)
        {
            // Журнал не должен останавливать службу.
        }
    }

    private void AppendHistoryCore(ActivityRecord record)
    {
        try
        {
            File.AppendAllText(_historyPath, JsonSerializer.Serialize(record, Json) + Environment.NewLine, Encoding.UTF8);
        }
        catch (Exception)
        {
            // Журнал не должен останавливать службу.
        }
    }
}
