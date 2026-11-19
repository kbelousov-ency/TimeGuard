using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TimeGuard.Core;

namespace TimeGuard.Parent;

public sealed class HistoryWindow : Window
{
    private readonly ParentStore _store;
    private readonly ParentConfig _config;
    private readonly TextBox _hostBox;
    private readonly TextBlock _status;
    private readonly DataGrid _grid;
    private readonly Button _refresh;

    public HistoryWindow(ParentStore store, ParentConfig config)
    {
        _store = store;
        _config = config;
        Title = $"История приложений — {config.ClientLabel}";
        Width = 820;
        Height = 560;
        MinWidth = 650;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.White;
        FontFamily = new FontFamily("Segoe UI");

        var root = new DockPanel { Margin = new Thickness(20) };
        var toolbar = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        toolbar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new TextBlock
        {
            Text = "Компьютер ребёнка в локальной сети",
            FontSize = 15,
            Foreground = Brushes.DarkSlateGray,
            Margin = new Thickness(0, 0, 0, 6),
        };
        Grid.SetColumnSpan(label, 2);
        toolbar.Children.Add(label);

        _hostBox = new TextBox
        {
            Text = config.ClientHost,
            FontSize = 17,
            Padding = new Thickness(9, 6, 9, 6),
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = "Например, 192.168.1.25 или имя компьютера (порт 38765)",
        };
        Grid.SetRow(_hostBox, 1);
        toolbar.Children.Add(_hostBox);

        _refresh = new Button
        {
            Content = "Обновить историю",
            FontSize = 16,
            Padding = new Thickness(18, 7, 18, 7),
            Margin = new Thickness(10, 0, 0, 0),
            MinWidth = 180,
        };
        _refresh.Click += async (_, _) => await RefreshAsync();
        Grid.SetColumn(_refresh, 1);
        Grid.SetRow(_refresh, 1);
        toolbar.Children.Add(_refresh);

        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);

        _status = new TextBlock
        {
            Text = "История пока не загружена.",
            Foreground = Brushes.SlateGray,
            Margin = new Thickness(0, 0, 0, 8),
        };
        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(_status);

        _grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            RowHeaderWidth = 0,
            FontSize = 15,
        };
        _grid.Columns.Add(new DataGridTextColumn { Header = "Программа", Binding = new System.Windows.Data.Binding(nameof(ActivityRecord.ProcessName)) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "PID", Binding = new System.Windows.Data.Binding(nameof(ActivityRecord.ProcessId)) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Запущено", Binding = new System.Windows.Data.Binding(nameof(ActivityRecord.StartedAt)) { StringFormat = "dd.MM.yyyy HH:mm:ss" } });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Закрыто", Binding = new System.Windows.Data.Binding(nameof(ActivityRecord.ClosedAt)) { StringFormat = "dd.MM.yyyy HH:mm:ss" } });
        root.Children.Add(_grid);

        Content = root;
        Loaded += async (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_hostBox.Text))
            {
                await RefreshAsync();
            }
        };
    }

    private async Task RefreshAsync()
    {
        _refresh.IsEnabled = false;
        _status.Text = "Запрашиваю историю…";
        try
        {
            string host = _hostBox.Text.Trim();
            IReadOnlyList<ActivityRecord> records = await HistoryClient.FetchAsync(
                _config, host, HistoryProtocol.MaxEntries, CancellationToken.None);
            _grid.ItemsSource = records;
            if (!string.Equals(_config.ClientHost, host, StringComparison.Ordinal))
            {
                _config.ClientHost = host;
                _store.Save(_config);
            }
            _status.Text = $"Получено записей: {records.Count}. Обновлено {DateTime.Now:HH:mm:ss}.";
        }
        catch (Exception ex)
        {
            _status.Text = $"Не удалось получить историю: {ex.Message}";
        }
        finally
        {
            _refresh.IsEnabled = true;
        }
    }
}
