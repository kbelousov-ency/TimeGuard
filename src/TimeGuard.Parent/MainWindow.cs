using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using TimeGuard.Core;

namespace TimeGuard.Parent;

/// <summary>
/// Основное окно родителя по фирменному концепту TimeGuard.
/// Режим офлайн: любое действие со временем даёт КОД для компьютера ребёнка.
/// Иллюстрации рисуются векторно; если в папке assets рядом с exe лежат
/// logo.png / avatar.png / monitor.png — используются они.
/// </summary>
public sealed class MainWindow : Window
{
    // палитра концепта
    private static readonly Brush Bg = Rgb(0xF7, 0xFA, 0xFF);
    private static readonly Brush Navy = Rgb(0x0F, 0x17, 0x2A);
    private static readonly Brush Blue = Rgb(0x25, 0x63, 0xEB);
    private static readonly Brush BlueLight = Rgb(0x5B, 0x8D, 0xEE);
    private static readonly Brush BlueSoft = Rgb(0xDB, 0xEA, 0xFE);
    private static readonly Brush Purple = Rgb(0x6D, 0x5B, 0xD6);
    private static readonly Brush PurpleSoft = Rgb(0xEE, 0xEB, 0xFA);
    private static readonly Brush Green = Rgb(0x22, 0xC5, 0x5E);
    private static readonly Brush GreenSoft = Rgb(0xE6, 0xF7, 0xEE);
    private static readonly Brush Muted = Rgb(0x64, 0x74, 0x8B);
    private static readonly Brush Line = Rgb(0xE2, 0xE8, 0xF0);
    private static readonly Brush Field = Rgb(0xF4, 0xF6, 0xFB);
    private static readonly Brush Skin = Rgb(0xF6, 0xCB, 0xAA);
    private static readonly Brush Hair = Rgb(0x5A, 0x3A, 0x22);

    private readonly ParentStore _store;
    private ParentConfig _cfg;
    private TimeGuardCodec _codec;
    private readonly ParentOnlineServer _onlineServer;

    private int _minutes = 30;
    private TextBox _minutesBox = null!;
    private TextBlock _codeText = null!;
    private TextBlock _hint = null!;

    public MainWindow(ParentStore store, ParentConfig cfg)
    {
        _store = store;
        _cfg = cfg;
        _codec = new TimeGuardCodec(Convert.FromBase64String(cfg.SecretBase64), cfg.ClientMachineId);
        _onlineServer = new ParentOnlineServer(cfg);
        _onlineServer.Start();
        _onlineServer.ClientDiscovered += host => Dispatcher.Invoke(() =>
        {
            _cfg.ClientHost = host;
            _store.Save(_cfg);
        });
        Closed += async (_, _) => await _onlineServer.DisposeAsync();

        Title = "TimeGuard — родитель";
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.CanMinimize;
        Background = Bg;
        FontFamily = new FontFamily("Segoe UI");
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);

        BuildView();
    }

    private void BuildView()
    {
        // Макет рисуется в фиксированной «дизайнерской» ширине и компактно
        // уменьшается до ширины родительского окна. Текст использует Ideal,
        // а PNG масштабируются отдельным качественным режимом.
        const double DesignWidth = 1040;
        var root = new StackPanel { Margin = new Thickness(24, 10, 24, 10), Width = DesignWidth, Background = Bg };
        root.Children.Add(BuildHeader());
        root.Children.Add(BuildClientCard());
        root.Children.Add(BuildTimeCard());
        root.Children.Add(BuildCodeCard());
        root.Children.Add(BuildSetupCard());
        root.Children.Add(BuildFooter());

        root.Measure(new Size(DesignWidth, double.PositiveInfinity));
        double designHeight = Math.Max(600, root.DesiredSize.Height);
        Rect wa = SystemParameters.WorkArea;
        double fullWidth = DesignWidth + root.Margin.Left + root.Margin.Right;
        // Keep the compact parent window while using the sharper text and image
        // rendering settings configured above.
        double scale = Math.Min(610 / fullWidth, Math.Min((wa.Height - 70) / designHeight, (wa.Width - 40) / fullWidth));
        if (scale < 0.999)
        {
            root.LayoutTransform = new ScaleTransform(scale, scale);
        }
        Content = new Border { Background = Bg, Child = root, UseLayoutRounding = true, SnapsToDevicePixels = true };
        SizeToContent = SizeToContent.WidthAndHeight;

        Generate();
    }

    // ============================ ШАПКА ============================
    private UIElement BuildHeader()
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var logoRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        logoRow.Children.Add(Art("app.png", 84, 84, DrawLogo));
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
        var brand = new TextBlock { FontSize = 46, FontWeight = FontWeights.Bold, LineHeight = 48, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
        brand.Inlines.Add(new Run("Time") { Foreground = Navy });
        brand.Inlines.Add(new Run("Guard") { Foreground = Blue });
        titles.Children.Add(brand);
        titles.Children.Add(new TextBlock { Text = "Контроль времени. Больше времени на важное.", FontSize = 15, Foreground = Muted, Margin = new Thickness(2, 0, 0, 0) });
        logoRow.Children.Add(titles);
        Grid.SetColumn(logoRow, 0);
        grid.Children.Add(logoRow);

        var settings = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        settings.Children.Add(DrawGear(26));
        settings.Children.Add(new TextBlock { Text = "Настройки", FontSize = 18, Foreground = Rgb(0x3F, 0x47, 0x5A), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) });
        settings.Cursor = System.Windows.Input.Cursors.Hand;
        settings.MouseLeftButtonUp += (_, _) => new ClientSettingsWindow(_store, _cfg) { Owner = this }.ShowDialog();
        settings.ToolTip = "Сменить мастер-пароль выбранного клиента";
        Grid.SetColumn(settings, 2);
        grid.Children.Add(settings);
        return grid;
    }

    // ============================ КЛИЕНТ ============================
    private UIElement BuildClientCard()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var avatar = Art("avatar.png", 150, 150, DrawAvatar);
        avatar.Margin = new Thickness(0, 0, 28, 0);
        left.Children.Add(avatar);
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock { Text = "Клиент", FontSize = 18, Foreground = Muted });
        var select = new Button { Content = _cfg.ClientLabel + "  ⌄", FontSize = 40, FontWeight = FontWeights.Bold, Foreground = Navy, Background = Brushes.Transparent, BorderThickness = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(0, 0, 8, 0), Cursor = System.Windows.Input.Cursors.Hand };
        Rounded(select, 8);
        var menu = new ContextMenu();
        foreach (var client in _store.Clients)
        {
            var item = new MenuItem { Header = client.ClientLabel, IsCheckable = true, IsChecked = client.ClientMachineId == _cfg.ClientMachineId };
            item.Click += (_, _) =>
            {
                _store.Save(client); _cfg = client;
                _codec = new TimeGuardCodec(Convert.FromBase64String(client.SecretBase64), client.ClientMachineId);
                BuildView();
            };
            menu.Items.Add(item);
        }
        select.Click += (_, _) => { menu.PlacementTarget = select; menu.Placement = PlacementMode.Bottom; menu.IsOpen = true; };
        info.Children.Add(select);
        var add = new MenuItem { Header = "+ Добавить клиента" };
        add.Click += (_, _) =>
        {
            var setup = new SetupWindow(_store) { Owner = this };
            if (setup.ShowDialog() == true && setup.Result is ParentConfig next)
            {
                _cfg = next; _codec = new TimeGuardCodec(Convert.FromBase64String(next.SecretBase64), next.ClientMachineId);
                BuildView();
            }
        };
        menu.Items.Add(new Separator());
        menu.Items.Add(add);
        var idRow = new StackPanel { Orientation = Orientation.Horizontal };
        idRow.Children.Add(new TextBlock { Text = $"ID: {_cfg.ClientMachineId}", FontSize = 17, Foreground = Muted, VerticalAlignment = VerticalAlignment.Center });
        var copyId = GhostButton(DrawCopy(20));
        copyId.ToolTip = "Скопировать ID";
        copyId.Click += (_, _) => TryClipboard(_cfg.ClientMachineId);
        idRow.Children.Add(copyId);
        info.Children.Add(idRow);
        left.Children.Add(info);
        Grid.SetColumn(left, 0);
        grid.Children.Add(left);

        var illo = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
        illo.Children.Add(Art("monitor.png", 260, 150, DrawMonitorBlob));
        illo.Children.Add(new TextBlock
        {
            Text = "Управляйте временем\nна компьютере ребёнка", FontSize = 14, Foreground = Muted,
            TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Center,
        });
        Grid.SetColumn(illo, 1);
        grid.Children.Add(illo);

        return CardWrap(grid, new Thickness(28, 18, 28, 16));
    }

    // ===================== УПРАВЛЕНИЕ ВРЕМЕНЕМ =====================
    private UIElement BuildTimeCard()
    {
        var panel = new StackPanel();
        panel.Children.Add(CardTitle(DrawClockIcon(Blue, 30), BlueSoft, "Управление временем",
            "Добавьте время к текущему лимиту или установите новое значение."));

        var row = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.85, GridUnitType.Star) });

        var add = new StackPanel();
        add.Children.Add(SectionLabel("Добавить время"));
        var presets = new UniformGrid { Columns = 4, Rows = 1 };
        foreach (int q in new[] { 15, 30, 60, 120 })
        {
            presets.Children.Add(PresetButton(q));
        }
        add.Children.Add(presets);
        Grid.SetColumn(add, 0);
        row.Children.Add(add);

        var divider = new Border { Width = 1, Background = Line, Margin = new Thickness(28, 4, 34, 4) };
        Grid.SetColumn(divider, 1);
        row.Children.Add(divider);

        var custom = new StackPanel();
        custom.Children.Add(SectionLabel("Установить лимит"));
        var line = new Grid();
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var field = new Border { BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Background = Brushes.White, Height = 66, VerticalAlignment = VerticalAlignment.Center };
        var fieldGrid = new Grid();
        fieldGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
        fieldGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        fieldGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        _minutesBox = new TextBox
        {
            Text = _minutes.ToString(), FontSize = 28, FontWeight = FontWeights.SemiBold, Foreground = Navy,
            BorderThickness = new Thickness(0), Background = Brushes.Transparent,
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(_minutesBox, 0);
        fieldGrid.Children.Add(_minutesBox);
        var sep = new Border { Width = 1, Background = Line, Margin = new Thickness(0, 10, 0, 10) };
        Grid.SetColumn(sep, 1);
        fieldGrid.Children.Add(sep);
        var spin = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        spin.Children.Add(SpinButton(true, +5));
        spin.Children.Add(SpinButton(false, -5));
        Grid.SetColumn(spin, 2);
        fieldGrid.Children.Add(spin);
        field.Child = fieldGrid;
        Grid.SetColumn(field, 0);
        line.Children.Add(field);

        var unit = new TextBlock { Text = "минут", FontSize = 19, Foreground = Navy, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 18, 0) };
        Grid.SetColumn(unit, 1);
        line.Children.Add(unit);

        var setBtn = PrimaryButton(DrawClockIcon(Brushes.White, 24), "Установить");
        setBtn.Height = 66;
        setBtn.MinWidth = 190;
        setBtn.HorizontalAlignment = HorizontalAlignment.Stretch;
        setBtn.Click += (_, _) =>
        {
            if (int.TryParse(_minutesBox.Text, out int m) && m is >= 1 and <= TimeGuardCodec.MaxMinutes)
            {
                _minutes = m;
                Generate();
            }
            else
            {
                _hint.Text = $"Минуты 1..{TimeGuardCodec.MaxMinutes}.";
            }
        };
        Grid.SetColumn(setBtn, 2);
        line.Children.Add(setBtn);

        custom.Children.Add(line);
        Grid.SetColumn(custom, 2);
        row.Children.Add(custom);

        panel.Children.Add(row);
        return CardWrap(panel);
    }

    // ======================== КОД УПРАВЛЕНИЯ ========================
    private UIElement BuildCodeCard()
    {
        var panel = new StackPanel();
        panel.Children.Add(CardTitle(DrawKeyIcon(30), PurpleSoft, "Код управления",
            "Используйте этот код на клиентском компьютере для управления временем."));

        var row = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(400) });

        var codeBox = new Border
        {
            Background = Field, BorderBrush = Line, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14), Padding = new Thickness(24, 12, 24, 12), Margin = new Thickness(0, 0, 26, 0),
        };
        _codeText = new TextBlock
        {
            Text = "—", FontSize = 50, FontWeight = FontWeights.Bold, Foreground = Navy,
            FontFamily = new FontFamily("Consolas"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        codeBox.Child = _codeText;
        Grid.SetColumn(codeBox, 0);
        row.Children.Add(codeBox);

        var actions = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var copy = SecondaryButton(DrawCopy(22), "Скопировать код");
        copy.Height = 42;
        copy.Click += (_, _) => TryClipboard(_codeText.Text);
        var backup = SecondaryButton(DrawDocIcon(22), "Резервные коды");
        backup.Height = 42;
        backup.Margin = new Thickness(0, 8, 0, 0);
        backup.Click += (_, _) => ExportBackupCodes();
        actions.Children.Add(copy);
        actions.Children.Add(backup);
        Grid.SetColumn(actions, 1);
        row.Children.Add(actions);
        panel.Children.Add(row);

        _hint = new TextBlock { Foreground = Muted, FontSize = 13, Margin = new Thickness(2, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(_hint);
        return CardWrap(panel);
    }

    // ======================= НАСТРОЙКА КЛИЕНТА =======================
    private UIElement BuildSetupCard()
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(430) });

        var title = CardTitle(DrawDownloadIcon(Green, 30), GreenSoft, "Настройка клиента",
            "Экспортируйте файл сопряжения для установки на компьютер ребёнка.");
        title.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(title, 0);
        row.Children.Add(title);

        var buttons = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var export = SecondaryButton(DrawDownloadIcon(Blue, 22), "Экспортировать файл сопряжения…");
        export.Background = BlueSoft;
        export.BorderBrush = Rgb(0xC9, 0xDA, 0xF7);
        export.Click += (_, _) => ExportPairing();
        buttons.Children.Add(export);
        var history = SecondaryButton(DrawDocIcon(22), "История приложений");
        history.Margin = new Thickness(0, 8, 0, 0);
        history.Click += (_, _) => new HistoryWindow(_store, _cfg) { Owner = this }.ShowDialog();
        buttons.Children.Add(history);
        var online = SecondaryButton(DrawRefresh(22), "Онлайн-управление");
        online.Margin = new Thickness(0, 8, 0, 0);
        online.Click += (_, _) => new OnlineControlWindow(_store, _cfg, _onlineServer) { Owner = this }.ShowDialog();
        buttons.Children.Add(online);
        var update = SecondaryButton(DrawDownloadIcon(Blue, 22), "Проверить обновление");
        update.Margin = new Thickness(0, 8, 0, 0);
        update.Click += async (_, _) =>
        {
            try { _hint.Text = "Скачиваю обновление…"; await UpdateLauncher.DownloadAndLaunchAsync("TimeGuard-Parent-Setup.exe"); _hint.Text = "Установщик обновления запущен."; }
            catch (Exception ex) { _hint.Text = $"Обновление не запущено: {ex.Message}"; }
        };
        buttons.Children.Add(update);
        Grid.SetColumn(buttons, 1);
        row.Children.Add(buttons);

        return CardWrap(row, new Thickness(22, 10, 22, 10));
    }

    private UIElement BuildFooter()
    {
        var grid = new Grid { Margin = new Thickness(6, 2, 6, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(DrawInfoIcon(18));
        left.Children.Add(new TextBlock { Text = "TimeGuard 1.0.0", FontSize = 15, Foreground = Muted, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(left, 0);
        grid.Children.Add(left);
        var right = new TextBlock { Text = "Больше баланса. Яркое завтра.", FontSize = 15, Foreground = Muted, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }

    // ============================ ЛОГИКА ============================
    private void Generate()
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _codeText.Text = _codec.Generate(_minutes, now);
        int validMin = TimeGuardCodec.WindowSteps * TimeGuardCodec.PeriodSeconds / 60;
        _hint.Text = $"Код на {_minutes} мин. Только цифры, действует ~{validMin} мин, одноразовый.";
    }

    private void ExportPairing()
    {
        var dlg = new SaveFileDialog { FileName = $"pairing-{_cfg.ClientLabel}.tgpair.json", Filter = "Файл сопряжения (*.json)|*.json" };
        if (dlg.ShowDialog() == true)
        {
            Pairing.Export(_cfg, dlg.FileName);
            _hint.Text = "Файл сопряжения сохранён. Импорт на клиенте: TimeGuard.Service.exe --import <файл>.";
        }
    }

    private void ExportBackupCodes()
    {
        var dlg = new SaveFileDialog { FileName = $"backup-codes-{_cfg.ClientLabel}.txt", Filter = "Текстовый файл (*.txt)|*.txt" };
        if (dlg.ShowDialog() != true)
        {
            return;
        }
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Резервные коды TimeGuard — клиент \"{_cfg.ClientLabel}\"");
        sb.AppendLine($"Каждый код одноразовый, БЕЗ срока действия, на {_minutes} мин.");
        sb.AppendLine("Ночью резервные коды не действуют (только мастер-пароль).");
        sb.AppendLine("Храните в надёжном месте — это готовые разблокировки.");
        sb.AppendLine();
        for (int i = 0; i < TimeGuardCodec.BackupCount; i++)
        {
            sb.AppendLine($"{i + 1,2}) {_codec.GenerateBackup(i, _minutes)}");
        }
        File.WriteAllText(dlg.FileName, sb.ToString());
        _hint.Text = $"Сохранено {TimeGuardCodec.BackupCount} резервных кодов на {_minutes} мин.";
    }

    private void TryClipboard(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text == "—")
        {
            return;
        }
        try
        {
            Clipboard.SetText(text);
            _hint.Text = "Скопировано.";
        }
        catch (Exception)
        {
        }
    }

    // ===================== КАРКАСНЫЕ ЭЛЕМЕНТЫ =====================
    private static Border CardWrap(UIElement child, Thickness? padding = null) => new()
    {
        Background = Brushes.White, CornerRadius = new CornerRadius(18),
        Padding = padding ?? new Thickness(24, 14, 24, 14), Margin = new Thickness(0, 0, 0, 14),
        BorderBrush = Line, BorderThickness = new Thickness(1),
        Child = child,
    };

    private static Grid CardTitle(UIElement icon, Brush bg, string title, string subtitle)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var circle = new Border
        {
            Width = 60, Height = 60, CornerRadius = new CornerRadius(30), Background = bg,
            Margin = new Thickness(0, 0, 20, 0), VerticalAlignment = VerticalAlignment.Center, Child = icon,
        };
        Grid.SetColumn(circle, 0);
        row.Children.Add(circle);
        var txt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        txt.Children.Add(new TextBlock { Text = title, FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = Navy });
        txt.Children.Add(new TextBlock { Text = subtitle, FontSize = 15, Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        Grid.SetColumn(txt, 1);
        row.Children.Add(txt);
        return row;
    }

    private static TextBlock SectionLabel(string text) => new()
    {
        Text = text, FontSize = 19, FontWeight = FontWeights.SemiBold, Foreground = Navy, Margin = new Thickness(0, 0, 0, 10),
    };

    private Button PresetButton(int minutes)
    {
        var b = new Button
        {
            Background = BlueSoft, BorderBrush = Rgb(0xC9, 0xDA, 0xF7), BorderThickness = new Thickness(1),
            Height = 66, Margin = new Thickness(0, 0, 12, 0), Cursor = System.Windows.Input.Cursors.Hand,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Content = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = $"+{minutes}", FontSize = 24, FontWeight = FontWeights.Bold, Foreground = Blue, HorizontalAlignment = HorizontalAlignment.Center },
                    new TextBlock { Text = "мин", FontSize = 14, Foreground = Blue, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, -2, 0, 0) },
                },
            },
        };
        Rounded(b, 14);
        b.Click += (_, _) =>
        {
            _minutes = minutes;
            _minutesBox.Text = minutes.ToString();
            Generate();
        };
        return b;
    }

    private Button SpinButton(bool up, int delta)
    {
        var chevron = new Polyline
        {
            Points = up ? new PointCollection { new(2, 8), new(7, 3), new(12, 8) } : new PointCollection { new(2, 3), new(7, 8), new(12, 3) },
            Stroke = Rgb(0x3F, 0x47, 0x5A), StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            Width = 14, Height = 11,
        };
        var b = new Button
        {
            Content = chevron, Width = 40, Height = 28, Padding = new Thickness(0),
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand,
        };
        b.Click += (_, _) =>
        {
            int cur = int.TryParse(_minutesBox.Text, out int m) ? m : _minutes;
            _minutesBox.Text = Math.Clamp(cur + delta, 1, TimeGuardCodec.MaxMinutes).ToString();
        };
        return b;
    }

    private static Button PrimaryButton(UIElement icon, string text)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        content.Children.Add(icon);
        content.Children.Add(new TextBlock { Text = text, FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) });
        var b = new Button
        {
            Content = content, Background = Blue, BorderThickness = new Thickness(0),
            Padding = new Thickness(18, 0, 18, 0), Cursor = System.Windows.Input.Cursors.Hand,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        Rounded(b, 14);
        return b;
    }

    private static Button SecondaryButton(UIElement icon, string text)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(icon);
        content.Children.Add(new TextBlock { Text = text, FontSize = 16, Foreground = Navy, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) });
        var b = new Button
        {
            Content = content, Background = Field, BorderBrush = Line, BorderThickness = new Thickness(1),
            Padding = new Thickness(20, 0, 20, 0), Height = 52, HorizontalContentAlignment = HorizontalAlignment.Left,
            HorizontalAlignment = HorizontalAlignment.Stretch, Cursor = System.Windows.Input.Cursors.Hand,
        };
        Rounded(b, 12);
        return b;
    }

    private static Button GhostButton(UIElement icon) => new()
    {
        Content = icon, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
        Padding = new Thickness(8, 2, 6, 2), Cursor = System.Windows.Input.Cursors.Hand, VerticalAlignment = VerticalAlignment.Center,
    };

    // скруглённый шаблон кнопки: фон/рамка/отступы берутся из свойств кнопки
    private static void Rounded(Button b, double radius)
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
        border.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush") { RelativeSource = RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderThicknessProperty, new Binding("BorderThickness") { RelativeSource = RelativeSource.TemplatedParent });
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetBinding(HorizontalAlignmentProperty, new Binding("HorizontalContentAlignment") { RelativeSource = RelativeSource.TemplatedParent });
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        presenter.SetBinding(ContentPresenter.ContentProperty, new Binding("Content") { RelativeSource = RelativeSource.TemplatedParent });
        presenter.SetBinding(MarginProperty, new Binding("Padding") { RelativeSource = RelativeSource.TemplatedParent });
        border.AppendChild(presenter);
        template.VisualTree = border;
        b.Template = template;
    }

    // ==================== ИЛЛЮСТРАЦИИ / ИКОНКИ ====================

    /// <summary>PNG из папки assets рядом с exe, иначе векторная отрисовка.</summary>
    private static FrameworkElement Art(string file, double w, double h, Func<FrameworkElement> fallback)
    {
        string path = System.IO.Path.Combine(AppContext.BaseDirectory, "assets", file);
        if (File.Exists(path))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.EndInit();
                var image = new Image
                {
                    Source = bmp,
                    Width = w,
                    Height = h,
                    Stretch = Stretch.Uniform,
                    SnapsToDevicePixels = true,
                };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                return image;
            }
            catch (Exception)
            {
            }
        }
        return new Viewbox { Width = w, Height = h, Stretch = Stretch.Uniform, Child = fallback() };
    }

    // Логотип: синий щит с белым циферблатом и зелёной галочкой
    private static FrameworkElement DrawLogo()
    {
        var c = new Canvas { Width = 96, Height = 96 };
        var shield = new System.Windows.Shapes.Path
        {
            Fill = Blue,
            Data = Geometry.Parse("M48,6 L86,20 V46 C86,66 70,82 48,91 C26,82 10,66 10,46 V20 Z"),
        };
        c.Children.Add(shield);
        var inner = new System.Windows.Shapes.Path
        {
            Fill = BlueLight, Opacity = 0.55,
            Data = Geometry.Parse("M48,14 L78,25 V46 C78,62 65,75 48,82 V14 Z"),
        };
        c.Children.Add(inner);
        c.Children.Add(Place(new Ellipse { Width = 40, Height = 40, Fill = Brushes.White }, 28, 26));
        c.Children.Add(Place(new Line { X1 = 20, Y1 = 20, X2 = 20, Y2 = 9, Stroke = Navy, StrokeThickness = 3, StrokeEndLineCap = PenLineCap.Round }, 28, 26));
        c.Children.Add(Place(new Line { X1 = 20, Y1 = 20, X2 = 28, Y2 = 24, Stroke = Navy, StrokeThickness = 3, StrokeEndLineCap = PenLineCap.Round }, 28, 26));
        c.Children.Add(Place(new Ellipse { Width = 30, Height = 30, Fill = Green, Stroke = Brushes.White, StrokeThickness = 3 }, 58, 60));
        c.Children.Add(Place(new Polyline
        {
            Points = new PointCollection { new(7, 14), new(12, 19), new(21, 9) },
            Stroke = Brushes.White, StrokeThickness = 3, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        }, 59, 61));
        return c;
    }

    // Аватар: ребёнок в синей футболке на светло-голубом круге
    private static FrameworkElement DrawAvatar()
    {
        var g = new Grid { Width = 176, Height = 176, Clip = new EllipseGeometry(new Point(88, 88), 88, 88) };
        g.Children.Add(new Ellipse { Fill = BlueSoft });
        var c = new Canvas();
        c.Children.Add(Place(new Border { Width = 132, Height = 90, CornerRadius = new CornerRadius(46, 46, 0, 0), Background = Blue }, 22, 122)); // тело
        c.Children.Add(Place(new Ellipse { Width = 26, Height = 40, Fill = Skin }, 75, 96)); // шея
        c.Children.Add(Place(new Ellipse { Width = 84, Height = 92, Fill = Skin }, 46, 34)); // голова
        c.Children.Add(Place(new System.Windows.Shapes.Path
        {
            Fill = Hair,
            Data = Geometry.Parse("M44,66 C40,28 84,18 110,30 C128,38 132,58 130,68 C122,52 108,48 96,56 C84,44 62,50 44,66 Z"),
        }, 0, 0));
        c.Children.Add(Place(new Ellipse { Width = 9, Height = 11, Fill = Navy }, 70, 72)); // глаза
        c.Children.Add(Place(new Ellipse { Width = 9, Height = 11, Fill = Navy }, 97, 72));
        c.Children.Add(Place(new System.Windows.Shapes.Path
        {
            Stroke = Rgb(0xC9, 0x6B, 0x5B), StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            Data = Geometry.Parse("M78,98 Q88,108 98,98"),
        }, 0, 0)); // улыбка
        g.Children.Add(c);
        return g;
    }

    // Иллюстрация: голубое облако, монитор и часы
    private static FrameworkElement DrawMonitorBlob()
    {
        var c = new Canvas { Width = 300, Height = 180 };
        c.Children.Add(Place(new Ellipse { Width = 180, Height = 130, Fill = BlueSoft }, 20, 20));
        c.Children.Add(Place(new Ellipse { Width = 200, Height = 120, Fill = BlueSoft }, 90, 50));
        c.Children.Add(Place(new Ellipse { Width = 120, Height = 100, Fill = BlueSoft }, 150, 10));
        c.Children.Add(Place(new Border { Width = 150, Height = 100, CornerRadius = new CornerRadius(10), Background = Navy }, 55, 30)); // рамка
        c.Children.Add(Place(new Border { Width = 134, Height = 84, CornerRadius = new CornerRadius(6), Background = BlueLight }, 63, 38)); // экран
        c.Children.Add(Place(new Border { Width = 22, Height = 16, Background = Navy }, 119, 130)); // ножка
        c.Children.Add(Place(new Border { Width = 70, Height = 10, CornerRadius = new CornerRadius(5), Background = Navy }, 95, 146)); // основание
        c.Children.Add(Place(new Ellipse { Width = 74, Height = 74, Fill = Brushes.White, Stroke = Rgb(0xD5, 0xDE, 0xEE), StrokeThickness = 4 }, 200, 80)); // часы
        c.Children.Add(Place(new Ellipse { Width = 54, Height = 54, Stroke = Navy, StrokeThickness = 4 }, 210, 90));
        c.Children.Add(Place(new Line { X1 = 27, Y1 = 27, X2 = 27, Y2 = 12, Stroke = Navy, StrokeThickness = 4, StrokeEndLineCap = PenLineCap.Round }, 210, 90));
        c.Children.Add(Place(new Line { X1 = 27, Y1 = 27, X2 = 38, Y2 = 33, Stroke = Navy, StrokeThickness = 4, StrokeEndLineCap = PenLineCap.Round }, 210, 90));
        return c;
    }

    private static FrameworkElement DrawClockIcon(Brush stroke, double size)
    {
        var c = new Canvas { Width = size, Height = size };
        double r = size / 2 - 2;
        c.Children.Add(Place(new Ellipse { Width = r * 2, Height = r * 2, Stroke = stroke, StrokeThickness = size * 0.09 }, 2, 2));
        double cx = size / 2, cy = size / 2;
        c.Children.Add(new Line { X1 = cx, Y1 = cy, X2 = cx, Y2 = cy - r * 0.55, Stroke = stroke, StrokeThickness = size * 0.09, StrokeEndLineCap = PenLineCap.Round });
        c.Children.Add(new Line { X1 = cx, Y1 = cy, X2 = cx + r * 0.42, Y2 = cy + r * 0.22, Stroke = stroke, StrokeThickness = size * 0.09, StrokeEndLineCap = PenLineCap.Round });
        return Centered(c);
    }

    private static FrameworkElement DrawKeyIcon(double size)
    {
        var c = new Canvas { Width = size, Height = size };
        double t = size * 0.11;
        c.Children.Add(Place(new Ellipse { Width = size * 0.42, Height = size * 0.42, Stroke = Purple, StrokeThickness = t }, size * 0.08, size * 0.50));
        c.Children.Add(new Line { X1 = size * 0.42, Y1 = size * 0.58, X2 = size * 0.92, Y2 = size * 0.08, Stroke = Purple, StrokeThickness = t, StrokeEndLineCap = PenLineCap.Round });
        c.Children.Add(new Line { X1 = size * 0.72, Y1 = size * 0.28, X2 = size * 0.84, Y2 = size * 0.40, Stroke = Purple, StrokeThickness = t, StrokeEndLineCap = PenLineCap.Round });
        c.Children.Add(new Line { X1 = size * 0.60, Y1 = size * 0.40, X2 = size * 0.70, Y2 = size * 0.50, Stroke = Purple, StrokeThickness = t, StrokeEndLineCap = PenLineCap.Round });
        return Centered(c);
    }

    private static FrameworkElement DrawDownloadIcon(Brush stroke, double size)
    {
        var c = new Canvas { Width = size, Height = size };
        double t = size * 0.1, cx = size / 2;
        c.Children.Add(new Line { X1 = cx, Y1 = size * 0.12, X2 = cx, Y2 = size * 0.62, Stroke = stroke, StrokeThickness = t, StrokeEndLineCap = PenLineCap.Round });
        c.Children.Add(new Polyline
        {
            Points = new PointCollection { new(cx - size * 0.22, size * 0.42), new(cx, size * 0.64), new(cx + size * 0.22, size * 0.42) },
            Stroke = stroke, StrokeThickness = t, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        });
        c.Children.Add(new Polyline
        {
            Points = new PointCollection { new(size * 0.12, size * 0.70), new(size * 0.12, size * 0.88), new(size * 0.88, size * 0.88), new(size * 0.88, size * 0.70) },
            Stroke = stroke, StrokeThickness = t, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        });
        return Centered(c);
    }

    private static FrameworkElement DrawCopy(double size)
    {
        var c = new Canvas { Width = size, Height = size };
        double t = size * 0.09;
        c.Children.Add(Place(new Rectangle { Width = size * 0.55, Height = size * 0.62, RadiusX = 3, RadiusY = 3, Stroke = Navy, StrokeThickness = t }, size * 0.34, size * 0.30));
        c.Children.Add(Place(new Rectangle { Width = size * 0.55, Height = size * 0.62, RadiusX = 3, RadiusY = 3, Stroke = Navy, StrokeThickness = t, Fill = Brushes.White }, size * 0.10, size * 0.08));
        return Centered(c);
    }

    private static FrameworkElement DrawRefresh(double size)
    {
        var c = new Canvas { Width = size, Height = size };
        double t = size * 0.1;
        c.Children.Add(new System.Windows.Shapes.Path
        {
            Stroke = Navy, StrokeThickness = t, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            Data = Geometry.Parse($"M{size * 0.82},{size * 0.42} A{size * 0.34},{size * 0.34} 0 1 0 {size * 0.78},{size * 0.66}"),
        });
        c.Children.Add(new Polyline
        {
            Points = new PointCollection { new(size * 0.66, size * 0.40), new(size * 0.84, size * 0.44), new(size * 0.86, size * 0.26) },
            Stroke = Navy, StrokeThickness = t, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        });
        return Centered(c);
    }

    private static FrameworkElement DrawDocIcon(double size)
    {
        var c = new Canvas { Width = size, Height = size };
        double t = size * 0.09;
        c.Children.Add(Place(new Rectangle { Width = size * 0.62, Height = size * 0.80, RadiusX = 3, RadiusY = 3, Stroke = Navy, StrokeThickness = t }, size * 0.19, size * 0.10));
        for (int i = 0; i < 3; i++)
        {
            double y = size * (0.36 + i * 0.16);
            c.Children.Add(new Line { X1 = size * 0.32, Y1 = y, X2 = size * 0.68, Y2 = y, Stroke = Navy, StrokeThickness = t * 0.8, StrokeEndLineCap = PenLineCap.Round });
        }
        return Centered(c);
    }

    private static FrameworkElement DrawGear(double size)
    {
        var c = new Canvas { Width = size, Height = size };
        Brush ink = Rgb(0x3F, 0x47, 0x5A);
        double cx = size / 2, cy = size / 2;
        // зубцы — скруглённые прямоугольники по кругу
        for (int i = 0; i < 8; i++)
        {
            double deg = i * 45;
            var tooth = new Rectangle
            {
                Width = size * 0.22, Height = size * 0.30, RadiusX = size * 0.05, RadiusY = size * 0.05, Fill = ink,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new RotateTransform(deg),
            };
            // центр зубца на радиусе 0.36
            double rad = deg * Math.PI / 180;
            double x = cx + Math.Sin(rad) * size * 0.36 - size * 0.11;
            double y = cy - Math.Cos(rad) * size * 0.36 - size * 0.15;
            c.Children.Add(Place(tooth, x, y));
        }
        c.Children.Add(Place(new Ellipse { Width = size * 0.64, Height = size * 0.64, Fill = ink }, size * 0.18, size * 0.18));
        c.Children.Add(Place(new Ellipse { Width = size * 0.26, Height = size * 0.26, Fill = Bg }, size * 0.37, size * 0.37));
        return c;
    }

    private static FrameworkElement DrawInfoIcon(double size)
    {
        var c = new Canvas { Width = size, Height = size };
        c.Children.Add(Place(new Ellipse { Width = size - 2, Height = size - 2, Stroke = Muted, StrokeThickness = 1.6 }, 1, 1));
        c.Children.Add(new Line { X1 = size / 2, Y1 = size * 0.45, X2 = size / 2, Y2 = size * 0.75, Stroke = Muted, StrokeThickness = 2, StrokeEndLineCap = PenLineCap.Round });
        c.Children.Add(Place(new Ellipse { Width = 2.6, Height = 2.6, Fill = Muted }, size / 2 - 1.3, size * 0.25));
        return c;
    }

    private static T Place<T>(T el, double x, double y) where T : UIElement
    {
        Canvas.SetLeft(el, x);
        Canvas.SetTop(el, y);
        return el;
    }

    private static FrameworkElement Centered(FrameworkElement el)
    {
        el.HorizontalAlignment = HorizontalAlignment.Center;
        el.VerticalAlignment = VerticalAlignment.Center;
        return el;
    }

    private static SolidColorBrush Rgb(byte r, byte g, byte bl) => new(Color.FromRgb(r, g, bl));
}
