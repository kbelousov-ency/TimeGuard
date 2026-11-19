using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinFormsScreen = System.Windows.Forms.Screen;

namespace TimeGuard.Agent;

/// <summary>
/// Небольшой индикатор оставшегося времени в правом нижнем углу. Топмост,
/// не забирает фокус, не в панели задач.
/// </summary>
public sealed class IndicatorWindow : Window
{
    private readonly TextBlock _text;
    private readonly Border _timer;
    private bool _canExtend;
    public event Action? ExtendRequested;

    public IndicatorWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Title = "TimeGuard";

        _text = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
        };

        _text.VerticalAlignment = VerticalAlignment.Center;
        _timer = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x10, 0x12, 0x18)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 7, 12, 7),
            Child = _text,
        };
        _timer.MouseLeftButtonUp += (_, e) =>
        {
            if (!_canExtend) return;
            ExtendRequested?.Invoke();
            e.Handled = true;
        };
        Content = _timer;

        Loaded += (_, _) => Reposition();
        SizeChanged += (_, _) => Reposition();
    }

    public void SetCanExtend(bool enabled)
    {
        _canExtend = enabled;
        _timer.Cursor = enabled ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow;
        _timer.ToolTip = enabled ? "Нажмите, чтобы добавить время кодом родителя" : null;
    }

    public void SetRemaining(long seconds)
    {
        long s = Math.Max(0, seconds);
        _text.Text = $"Осталось: {s / 3600:D2}:{(s % 3600) / 60:D2}:{s % 60:D2}";
    }

    private void Reposition()
    {
        System.Drawing.Rectangle wa = WinFormsScreen.PrimaryScreen!.WorkingArea;
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        Left = (wa.Right / dpi) - ActualWidth - 16;
        Top = (wa.Bottom / dpi) - ActualHeight - 16;
    }
}
