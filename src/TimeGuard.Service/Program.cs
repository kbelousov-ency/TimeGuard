using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TimeGuard.Core;
using TimeGuard.Service;
using TimeGuard.Service.Interop;
using TimeGuard.Service.Storage;

// Импорт файла сопряжения: TimeGuard.Service.exe --import <path>
if (args.Length >= 2 && args[0] == "--import")
{
    try
    {
        string json = File.ReadAllText(args[1]);
        GuardConfig? cfg = JsonSerializer.Deserialize<GuardConfig>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (cfg is null || string.IsNullOrEmpty(cfg.SecretBase64) || string.IsNullOrEmpty(cfg.MachineId))
        {
            Console.Error.WriteLine("Некорректный файл сопряжения.");
            return 2;
        }
        var store = new StateStore();
        var existing = store.LoadConfig();
        if (existing is not null)
        {
            if (existing.MachineId != cfg.MachineId || existing.SecretBase64 != cfg.SecretBase64)
                throw new InvalidOperationException("Этот файл предназначен для другой пары. Существующая пара сохранена.");
            existing.MasterHashBase64 = cfg.MasterHashBase64;
            existing.MasterSaltBase64 = cfg.MasterSaltBase64;
            cfg = existing; // пароль обновляется без изменения расписания и целевой учётки
        }
        if (Convert.FromBase64String(cfg.MasterHashBase64).Length != 32 ||
            Convert.FromBase64String(cfg.MasterSaltBase64).Length != 16 || cfg.GetSecret().Length < 16)
            throw new InvalidOperationException("Некорректные данные сопряжения.");
        store.SaveConfig(cfg);
        Console.WriteLine("Конфигурация сопряжения импортирована.");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Ошибка импорта: {ex.Message}");
        return 1;
    }
}

// Задать целевую учётку ребёнка: TimeGuard.Service.exe --set-user <имя>
if (args.Length >= 2 && args[0] == "--set-user")
{
    try
    {
        var store = new StateStore();
        GuardConfig? cfg = store.LoadConfig();
        if (cfg is null)
        {
            Console.Error.WriteLine("Сначала импортируйте сопряжение (--import).");
            return 2;
        }
        cfg.TargetUser = args[1];
        store.SaveConfig(cfg);
        Console.WriteLine($"Целевая учётка задана: {args[1]}");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Ошибка: {ex.Message}");
        return 1;
    }
}

// Настроить ночной запрет: TimeGuard.Service.exe --set-curfew <start> <end> [on|off]
if (args.Length >= 3 && args[0] == "--set-curfew")
{
    try
    {
        var store = new StateStore();
        GuardConfig? cfg = store.LoadConfig();
        if (cfg is null)
        {
            Console.Error.WriteLine("Сначала импортируйте сопряжение (--import).");
            return 2;
        }
        if (!int.TryParse(args[1], out int start) || start is < 0 or > 23 ||
            !int.TryParse(args[2], out int end) || end is < 0 or > 23)
        {
            Console.Error.WriteLine("Часы должны быть в диапазоне 0..23.");
            return 2;
        }
        cfg.CurfewStartHour = start;
        cfg.CurfewEndHour = end;
        if (args.Length >= 4)
        {
            cfg.CurfewEnabled = !string.Equals(args[3], "off", StringComparison.OrdinalIgnoreCase);
        }
        store.SaveConfig(cfg);
        Console.WriteLine($"Ночной запрет: {(cfg.CurfewEnabled ? "вкл" : "выкл")}, с {start}:00 до {end}:00.");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Ошибка: {ex.Message}");
        return 1;
    }
}

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "TimeGuard";
});

builder.Services.AddSingleton<StateStore>();
builder.Services.AddSingleton<SessionLauncher>();
builder.Services.AddSingleton<PolicyGuard>();
builder.Services.AddHostedService<Worker>();

builder.Logging.AddEventLog(settings =>
{
    settings.SourceName = "TimeGuard";
});
// По умолчанию журнал событий пишет только Warning+. Поднимаем до Information,
// чтобы был виден операционный след (запуск агента, состояние сессии).
builder.Logging.AddFilter<Microsoft.Extensions.Logging.EventLog.EventLogLoggerProvider>(
    null, LogLevel.Information);

IHost host = builder.Build();
host.Run();
return 0;
