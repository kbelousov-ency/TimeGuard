using System.Diagnostics;
using System.IO;
using System.Threading;

namespace TimeGuard.Agent;

internal static class Program
{
    // Абсолютный путь в общедоступную папку: запись работает, даже если профиль
    // пользователя не полностью загружен в контексте запуска из службы.
    private static readonly string LogPath = @"C:\Users\Public\TimeGuard-agent.log";

    internal static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}");
        }
        catch
        {
            // логирование не должно ронять агент
        }
    }

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--render-previews")
        {
            Directory.CreateDirectory(args[1]);
            var previewApp = new System.Windows.Application();
            foreach (bool night in new[] { false, true })
            {
                var window = new OverlayWindow(System.Windows.Forms.Screen.PrimaryScreen!, true, night, 21, 9, preview: true);
                // Exercise both transitions, including removal of the code control.
                foreach (bool mode in new[] { !night, night })
                {
                    window.SetNight(mode, 22, 8);
                    var codeField = typeof(OverlayWindow).GetField("_codeBox", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if ((codeField!.GetValue(window) is null) != mode)
                        throw new InvalidOperationException("Incorrect code field visibility after mode change.");
                }
                window.SetNight(night, 21, 9);
                var visual = (System.Windows.FrameworkElement)window.Content;
                visual.Measure(new System.Windows.Size(1536, 1024));
                visual.Arrange(new System.Windows.Rect(0, 0, 1536, 1024));
                visual.UpdateLayout();
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(1536, 1024, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(visual);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(args[1], night ? "night.png" : "day.png"));
                encoder.Save(file);
                window.CloseOverlay();
            }
            previewApp.Shutdown();
            return 0;
        }
        int session = -1;
        try { session = Process.GetCurrentProcess().SessionId; } catch { }
        Log($"=== Main старт. pid={Environment.ProcessId} session={session} user={Environment.UserName} ===");

        Mutex? mutex = null;
        try
        {
            // Один экземпляр агента на сессию.
            mutex = new Mutex(initiallyOwned: true, name: @"Local\TimeGuard.Agent", out bool created);
            Log($"mutex created={created}");
            if (!created)
            {
                Log("Выход: уже запущен другой экземпляр.");
                return 0;
            }

            var app = new AgentApp();
            Log("AgentApp создан, вызываю Run()...");
            int rc = app.Run();
            Log($"app.Run() вернул {rc}");
            return rc;
        }
        catch (Exception ex)
        {
            Log("ФАТАЛЬНО в Main: " + ex);
            return 1;
        }
        finally
        {
            mutex?.Dispose();
        }
    }
}
