using System.Runtime.InteropServices;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using WinFormsScreen = System.Windows.Forms.Screen;

namespace TimeGuard.Agent;

public sealed class OverlayWindow : Window
{
    private readonly WinFormsScreen _screen;
    private readonly bool _isPrimaryHost;
    private readonly DispatcherTimer _keepTop;
    private readonly bool _preview;
    private bool _allowClose;
    private bool _night;
    private int? _start, _end;
    private TextBox? _codeBox;
    private PasswordBox? _masterBox;
    private TextBlock? _message;
    private TextBlock? _clock;
    private TextBlock? _schedule;
    private Button? _layoutButton;
    private TextBlock? _layoutText;
    private readonly InputLanguageEventHandler _layoutChanged;
    private Brush Ink => Paint(_night ? "#F5F7FF" : "#091B42");
    private Brush Muted => Paint(_night ? "#B9CBE5" : "#3F5F8B");
    public event Action<string>? CodeSubmitted;
    public event Action<string>? MasterSubmitted;

    public OverlayWindow(WinFormsScreen screen, bool isPrimaryHost, bool night = false,
        int? start = null, int? end = null, bool preview = false)
    {
        _screen = screen; _isPrimaryHost = isPrimaryHost; _night = night;
        _start = start; _end = end; _preview = preview;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = preview; Topmost = !preview; Title = "TimeGuard";
        FontFamily = new FontFamily("Segoe UI");
        _layoutChanged = (_, _) => UpdateLayoutIndicator();
        Build();
        Loaded += (_, _) => { if (!_preview) PositionOnScreen(); };
        StateChanged += (_, _) => { if (!_preview && WindowState != WindowState.Normal) WindowState = WindowState.Normal; };
        Closing += (_, e) => { if (!_allowClose && !_preview) e.Cancel = true; };
        _keepTop = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        // Индикатор раскладки обновляется и по таймеру: событие InputLanguageChanged
        // приходит не при каждом способе переключения (например, по Ctrl+Shift в другом окне).
        _keepTop.Tick += (_, _) => { UpdateClock(); UpdateLayoutIndicator(); if (!_preview) AssertForeground(); };
        _keepTop.Start();
        try { InputLanguageManager.Current.InputLanguageChanged += _layoutChanged; } catch (Exception) { }
        Closed += (_, _) =>
        {
            _keepTop.Stop();
            try { InputLanguageManager.Current.InputLanguageChanged -= _layoutChanged; } catch (Exception) { }
        };
    }

    public void SetMessage(string text) { if (_message is not null) _message.Text = text; }
    public void SetNight(bool night, int? start = null, int? end = null)
    {
        _start = start; _end = end;
        if (_night != night) { _night = night; Build(); if (IsLoaded) FocusInput(); }
        if (_schedule is not null) _schedule.Text = ScheduleText();
    }
    public void CloseOverlay() { _allowClose = true; _keepTop.Stop(); Close(); }
    private void FocusInput() { if (_night) _masterBox?.Focus(); else _codeBox?.Focus(); }
    private static Brush Paint(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;
    private TextBlock Text(string text, double size, Brush? brush = null, bool bold = false) => new()
    {
        Text = text, FontSize = size, Foreground = brush ?? Ink,
        FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
        TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap
    };
    private string ScheduleText() => _start is int s && _end is int e
        ? $"Ночной режим включается ежедневно\nс {s:00}:00 до {e:00}:00."
        : "Ночной режим включается по расписанию родителя.";

    private void Build()
    {
        _codeBox = null; _masterBox = null; _message = null; _clock = null; _schedule = null; _layoutButton = null; _layoutText = null;
        var root = new Grid { Background = Paint(_night ? "#051328" : "#CAE4FF") };
        root.Children.Add(new LockArtwork(_night));
        var stage = new Grid { Width = 1536, Height = 1024 };
        var card = new Border
        {
            Width = _night ? 714 : 746, Height = _night ? 832 : 868,
            CornerRadius = new CornerRadius(26), BorderThickness = new Thickness(1),
            BorderBrush = Paint(_night ? "#355378" : "#FFFFFF"),
            Background = Paint(_night ? "#ED081A32" : "#EDF6FAFF"),
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 78, 0, 0),
            Effect = new DropShadowEffect { Color = _night ? Colors.Black : Color.FromRgb(66,130,220), BlurRadius = 60, ShadowDepth = 15, Opacity = .22 }
        };
        var body = new StackPanel { Width = 540, Margin = new Thickness(0, 26, 0, 0) };
        body.Children.Add(new BrandMark { Width = 132, Height = 140, HorizontalAlignment = HorizontalAlignment.Center });
        var brand = Text("", 48, bold: true);
        brand.Inlines.Add(new Run("Time")); brand.Inlines.Add(new Run("Guard") { Foreground = Paint("#087AFF") });
        body.Children.Add(brand);
        var slogan = Text("Контроль времени. Больше времени на важное.", 17, Muted);
        slogan.Margin = new Thickness(0, 0, 0, _night ? 30 : 28); body.Children.Add(slogan);
        var title = Text(_night ? "☾  Ночной режим" : "Время за компьютером\nна сегодня закончилось", _night ? 44 : 41, bold: true);
        body.Children.Add(title);
        var intro = Text(_night ? "Сейчас ночь, поэтому доступ к компьютеру\nограничен." : "Чтобы продолжить, введите код или\nмастер-пароль от родителя.", 23, _night ? Ink : Muted, _night);
        intro.Margin = new Thickness(0, 16, 0, _night ? 3 : 34); body.Children.Add(intro);
        if (_night)
        {
            var explanation = Text("Разблокировать можно только по мастер-паролю\nот родителя.", 20, Muted);
            explanation.Margin = new Thickness(0, 0, 0, 30); body.Children.Add(explanation);
        }
        if (_isPrimaryHost)
        {
            if (!_night)
            {
                _codeBox = new TextBox { FontSize = 21, Foreground = Ink, Background = Brushes.Transparent, BorderThickness = new Thickness(0), VerticalContentAlignment = VerticalAlignment.Center };
                _codeBox.PreviewKeyDown += CodeBox_PreviewKeyDown;
                _codeBox.PreviewTextInput += (_, e) => e.Handled = true;
                var field = Field(_codeBox, "Код с родительского приложения", "key", out var hint);
                _codeBox.TextChanged += (_, _) => hint.Visibility = _codeBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                body.Children.Add(field);
            }
            _masterBox = new PasswordBox { FontSize = 21, Foreground = Ink, Background = Brushes.Transparent, BorderThickness = new Thickness(0), VerticalContentAlignment = VerticalAlignment.Center };
            // Пароль зависит от раскладки, поэтому рядом с полем показываем текущий язык и даём его сменить.
            var masterField = Field(_masterBox, "Мастер-пароль", "lock", out var masterHint, LayoutSwitch());
            _masterBox.PasswordChanged += (_, _) => masterHint.Visibility = _masterBox.Password.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            _masterBox.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { Submit(); e.Handled = true; } };
            body.Children.Add(masterField);
            var button = new Button { Content = "Разблокировать", Height = 68, FontSize = 23, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Background = new LinearGradientBrush(Color.FromRgb(20,142,255), Color.FromRgb(0,88,237), 90), BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand, Margin = new Thickness(0, 14, 0, 0) };
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.CornerRadiusProperty, new CornerRadius(14));
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            var content = new FrameworkElementFactory(typeof(ContentPresenter)); content.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center); content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content); template.VisualTree = border;
            var hover = new Trigger { Property = IsMouseOverProperty, Value = true }; hover.Setters.Add(new Setter(OpacityProperty, .9)); template.Triggers.Add(hover);
            button.Template = template; button.Click += (_, _) => Submit(); body.Children.Add(button);
        }
        else
        {
            var other = Text("Введите данные на основном мониторе", 22, Muted); other.Margin = new Thickness(0, 25, 0, 25); body.Children.Add(other);
        }
        if (_night)
        {
            var info = new StackPanel { Margin = new Thickness(26, 18, 18, 18) };
            _schedule = Text(ScheduleText(), 16, Ink); _schedule.TextAlignment = TextAlignment.Left;
            info.Children.Add(_schedule);
            var detail = Text("В это время доступ возможен только по мастер-паролю.", 15, Muted); detail.TextAlignment = TextAlignment.Left; detail.Margin = new Thickness(0, 6, 0, 0); info.Children.Add(detail);
            body.Children.Add(new Border { Child = info, BorderBrush = Paint("#29496E"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(15), Margin = new Thickness(0, 22, 0, 0) });
        }
        else if (_isPrimaryHost)
        {
            var help = new Button { Content = "ⓘ  Нужна помощь?", FontSize = 17, Foreground = Muted, Background = Brushes.Transparent, BorderThickness = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 24, 0, 0), Cursor = System.Windows.Input.Cursors.Hand };
            help.Click += (_, _) => SetMessage("Попросите родителя передать код или ввести мастер-пароль."); body.Children.Add(help);
        }
        _message = Text("", 15, Paint(_night ? "#FFD39A" : "#9A4026")); _message.Margin = new Thickness(0, 10, 0, 0); body.Children.Add(_message);
        card.Child = body; stage.Children.Add(card);
        if (_night)
        {
            _clock = Text("", 22); _clock.HorizontalAlignment = HorizontalAlignment.Right; _clock.VerticalAlignment = VerticalAlignment.Top; _clock.Margin = new Thickness(0, 24, 42, 0); stage.Children.Add(_clock); UpdateClock();
            var footer = Text("—\nХороший отдых — залог больших достижений!", 17, Paint("#82AAD5")); footer.VerticalAlignment = VerticalAlignment.Bottom; footer.Margin = new Thickness(0, 0, 0, 38); stage.Children.Add(footer);
        }
        root.Children.Add(new Viewbox { Stretch = Stretch.Uniform, Child = stage }); Content = root;
    }
    private Border Field(Control input, string placeholder, string icon, out TextBlock hint, UIElement? trailing = null)
    {
        System.Windows.Automation.AutomationProperties.SetName(input, placeholder);
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) }); grid.ColumnDefinitions.Add(new ColumnDefinition());
        if (trailing is not null)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(trailing, 2); grid.Children.Add(trailing);
        }
        var symbol = new System.Windows.Shapes.Path { Data = Geometry.Parse(icon == "lock"
            ? "M5,12 L5,8 C5,0 19,0 19,8 L19,12 M2,12 L22,12 L22,29 L2,29 Z M12,19 L12,24"
            : "M15,2 C26,2 26,16 16,17 L9,25 L5,25 L5,29 L0,29 L0,24 L12,12 C7,7 10,2 15,2 Z M17,6 L17,10"), Stroke = Ink, StrokeThickness = 2.6, Width = 27, Height = 31, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(symbol);
        var area = new Grid { Margin = new Thickness(10, 0, 16, 0) }; Grid.SetColumn(area, 1);
        hint = Text(placeholder, 20, Paint(_night ? "#8294AF" : "#7A8EAD")); hint.TextAlignment = TextAlignment.Left; hint.VerticalAlignment = VerticalAlignment.Center; hint.IsHitTestVisible = false;
        area.Children.Add(hint); area.Children.Add(input); grid.Children.Add(area);
        return new Border { Height = 68, Margin = new Thickness(0, 0, 0, 16), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1.4), BorderBrush = Paint(_night ? "#365176" : "#BECEE3"), Background = Paint(_night ? "#A0172944" : "#BFFFFFFF"), Child = grid };
    }
    /// <summary>Кнопка «⌨ RU» внутри поля пароля: показывает язык ввода, по клику переключает на следующий.</summary>
    private Button LayoutSwitch()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M1,4 L27,4 L27,20 L1,20 Z M5,8 L7,8 M10,8 L12,8 M15,8 L17,8 M20,8 L22,8 M5,12 L7,12 M10,12 L12,12 M15,12 L17,12 M20,12 L22,12 M8,16 L20,16"),
            Stroke = Ink, StrokeThickness = 2, Width = 24, Height = 16, Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
        });
        _layoutText = new TextBlock { FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = Ink, VerticalAlignment = VerticalAlignment.Center, MinWidth = 30, TextAlignment = TextAlignment.Center };
        row.Children.Add(_layoutText);
        _layoutButton = new Button
        {
            Content = row, Background = Paint(_night ? "#2A3F60" : "#E3EEFC"), BorderThickness = new Thickness(0),
            Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 12, 12, 12), Cursor = System.Windows.Input.Cursors.Hand,
            Focusable = false, // фокус остаётся в поле пароля, язык меняется для него
        };
        System.Windows.Automation.AutomationProperties.SetName(_layoutButton, "Сменить язык ввода");
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        border.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        var content = new FrameworkElementFactory(typeof(ContentPresenter)); content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content); template.VisualTree = border;
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true }; hover.Setters.Add(new Setter(OpacityProperty, .8)); template.Triggers.Add(hover);
        _layoutButton.Template = template;
        _layoutButton.Click += (_, _) => SwitchLayout();
        UpdateLayoutIndicator();
        return _layoutButton;
    }

    private static CultureInfo[] AvailableLayouts()
    {
        try
        {
            return InputLanguageManager.Current.AvailableInputLanguages?.Cast<CultureInfo>().ToArray() ?? [];
        }
        catch (Exception) { return []; }
    }

    private void SwitchLayout()
    {
        CultureInfo[] layouts = AvailableLayouts();
        if (layouts.Length < 2) return;
        try
        {
            CultureInfo current = InputLanguageManager.Current.CurrentInputLanguage;
            int index = Array.FindIndex(layouts, l => l.Equals(current));
            InputLanguageManager.Current.CurrentInputLanguage = layouts[(index + 1) % layouts.Length];
        }
        catch (Exception) { }
        UpdateLayoutIndicator();
        _masterBox?.Focus();
    }

    private void UpdateLayoutIndicator()
    {
        if (_layoutText is null || _layoutButton is null) return;
        CultureInfo? current = null;
        try { current = InputLanguageManager.Current.CurrentInputLanguage; } catch (Exception) { }
        string code = current is null ? "?" : current.TwoLetterISOLanguageName.ToUpperInvariant();
        if (_layoutText.Text != code) _layoutText.Text = code;
        int count = AvailableLayouts().Length;
        _layoutButton.IsEnabled = count > 1;
        _layoutButton.ToolTip = count > 1
            ? $"Язык ввода: {current?.NativeName ?? "неизвестно"}. Нажмите, чтобы сменить (или Alt+Shift)."
            : $"Язык ввода: {current?.NativeName ?? "неизвестно"}. Других раскладок в системе нет.";
    }

    private void Submit()
    {
        if (_masterBox is null) return;
        if (_masterBox.Password.Length > 0) { MasterSubmitted?.Invoke(_masterBox.Password); _masterBox.Clear(); }
        else if (!_night && !string.IsNullOrWhiteSpace(_codeBox?.Text)) CodeSubmitted?.Invoke(_codeBox.Text);
        else SetMessage(_night ? "Введите мастер-пароль родителя." : "Введите код или мастер-пароль родителя.");
    }
    private void UpdateClock() { if (_clock is not null) _clock.Text = DateTime.Now.ToString("HH:mm\nddd, d MMM", CultureInfo.GetCultureInfo("ru-RU")); }

    private const int CodeDigits = 10; // 9 — временный код, 10 — резервный

    private void CodeBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var box = (TextBox)sender;
        System.Windows.Input.Key k = e.Key;

        if (k == System.Windows.Input.Key.Enter)
        {
            Submit();
            e.Handled = true;
            return;
        }

        if (k == System.Windows.Input.Key.Tab) return;
        string digits = Digits(box.Text);
        if (k == System.Windows.Input.Key.Back)
        {
            if (digits.Length > 0)
            {
                digits = digits[..^1];
            }
        }
        else
        {
            int? d = MapKeyToDigit(k);
            if (d.HasValue && digits.Length < CodeDigits)
            {
                digits += (char)('0' + d.Value);
            }
        }

        box.Text = FormatCode(digits);   // цифры вида XXX-XXX-XXX
        box.CaretIndex = box.Text.Length;
        e.Handled = true; // только цифры, раскладка не важна
    }

    private static string Digits(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (char c in s)
        {
            if (c is >= '0' and <= '9')
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private static string FormatCode(string digits)
    {
        var sb = new System.Text.StringBuilder(11);
        for (int i = 0; i < digits.Length; i++)
        {
            if (i == 3 || i == 6)
            {
                sb.Append('-');
            }
            sb.Append(digits[i]);
        }
        return sb.ToString();
    }

    private static int? MapKeyToDigit(System.Windows.Input.Key k)
    {
        if (k >= System.Windows.Input.Key.D0 && k <= System.Windows.Input.Key.D9)
        {
            return k - System.Windows.Input.Key.D0;
        }
        if (k >= System.Windows.Input.Key.NumPad0 && k <= System.Windows.Input.Key.NumPad9)
        {
            return k - System.Windows.Input.Key.NumPad0;
        }
        return null;
    }

    private void PositionOnScreen()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        System.Drawing.Rectangle r = _screen.Bounds; // пиксели
        MoveWindow(hwnd, r.X, r.Y, r.Width, r.Height, true);
        AssertForeground();
        if (_isPrimaryHost)
        {
            FocusInput();
        }
    }

    private void AssertForeground()
    {
        if (!IsLoaded)
        {
            return;
        }
        Topmost = false;
        Topmost = true;
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            SetForegroundWindow(hwnd);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool MoveWindow(IntPtr hWnd, int x, int y, int w, int h, bool repaint);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
