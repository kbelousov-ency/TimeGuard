using System.Windows;

namespace TimeGuard.Parent;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        if (args.Length == 2 && args[0] == "--render-preview")
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TimeGuard-preview-" + Guid.NewGuid());
            var demoStore = new ParentStore(dir);
            var demo = new ParentConfig { ClientLabel = "Рома", ClientMachineId = "894ae7f5a33f4adc99202a459c7e3f5f", SecretBase64 = Convert.ToBase64String(new byte[32]) };
            demoStore.Save(demo);
            var window = new MainWindow(demoStore, demo);
            var visual = (FrameworkElement)window.Content;
            visual.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            visual.Arrange(new Rect(visual.DesiredSize)); visual.UpdateLayout();
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth), (int)Math.Ceiling(visual.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var output = System.IO.File.Create(args[1]); encoder.Save(output);
            window.Close(); app.Shutdown(); return 0;
        }
        var store = new ParentStore();
        ParentConfig? cfg = store.Load();

        if (cfg is null)
        {
            var setup = new SetupWindow(store);
            bool? ok = setup.ShowDialog();
            if (ok != true || setup.Result is null)
            {
                return 0;
            }
            cfg = setup.Result;
        }

        var main = new MainWindow(store, cfg);
        return app.Run(main);
    }
}
