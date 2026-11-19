using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using TimeGuard.Core;

namespace TimeGuard.Parent;

public sealed class OnlineControlWindow : Window
{
    private readonly ParentStore _store;
    private readonly ParentConfig _config;
    private readonly ParentOnlineServer _server;
    private readonly TextBox _host;
    private readonly TextBlock _status;
    private readonly TextBlock _timer;

    public OnlineControlWindow(ParentStore store, ParentConfig config, ParentOnlineServer server)
    {
        _store = store;
        _config = config;
        _server = server;
        Title = $"Онлайн-управление — {config.ClientLabel}";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.White;
        FontFamily = new FontFamily("Segoe UI");

        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "Онлайн-управление клиентом", FontSize = 25, FontWeight = FontWeights.Bold });
        panel.Children.Add(new TextBlock { Text = "Команды работают даже при заблокированном экране клиента.", Margin = new Thickness(0, 6, 0, 16), Foreground = Brushes.SlateGray, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "Имя или IP компьютера ребёнка", Margin = new Thickness(0, 0, 0, 5) });
        _host = new TextBox { Text = config.ClientHost, FontSize = 17, Padding = new Thickness(8), IsReadOnly = true, ToolTip = "Адрес определяется автоматически в локальной сети" };
        panel.Children.Add(_host);

        _timer = new TextBlock { Text = "Статус ещё не запрошен", FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 14) };
        panel.Children.Add(_timer);
        _status = new TextBlock { Text = "", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0), Foreground = Brushes.DarkSlateGray };

        var row = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 10) };
        row.Children.Add(Command("+15 минут", () => ControlAsync("add", 15)));
        row.Children.Add(Command("+30 минут", () => ControlAsync("add", 30)));
        row.Children.Add(Command("Сбросить", () => ControlAsync("reset", 0)));
        panel.Children.Add(row);

        var history = new Button { Content = "Открыть историю", FontSize = 16, Padding = new Thickness(10), Margin = new Thickness(0, 2, 0, 0) };
        history.Click += (_, _) => new HistoryWindow(_store, _config) { Owner = this }.ShowDialog();
        panel.Children.Add(history);
        panel.Children.Add(new TextBlock { Text = "Запросы времени от клиента", FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 6) });
        var requests = new StackPanel();
        panel.Children.Add(requests);
        void AddRequest(PendingOnlineRequest request)
        {
            Dispatcher.Invoke(() =>
            {
                var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new TextBlock { Text = $"+{request.Minutes} мин. {request.Reason}", VerticalAlignment = VerticalAlignment.Center });
                var approve = new Button { Content = "Одобрить", Margin = new Thickness(6, 0, 0, 0) };
                var reject = new Button { Content = "Отклонить", Margin = new Thickness(6, 0, 0, 0) };
                async Task ResolveAsync(bool accepted)
                {
                    approve.IsEnabled = reject.IsEnabled = false;
                    if (accepted) _status.Text = $"Добавляем +{request.Minutes} мин. на компьютере ребёнка…";
                    OnlineTimeResponse? response = await _server.ResolveAsync(request.Id, accepted);
                    row.Visibility = Visibility.Collapsed;
                    if (response is null)
                    {
                        _status.Foreground = Brushes.DarkSlateGray;
                        _status.Text = "Запрос уже не актуален: истёк срок ожидания.";
                        return;
                    }
                    _status.Foreground = accepted && !response.Accepted ? Brushes.Firebrick : Brushes.DarkSlateGray;
                    _status.Text = response.Message;
                    if (response.Accepted) _timer.Text = $"Осталось: {Format(response.RemainingSeconds)}";
                }
                approve.Click += async (_, _) => await ResolveAsync(true);
                Grid.SetColumn(approve, 1); row.Children.Add(approve);
                reject.Click += async (_, _) => await ResolveAsync(false);
                Grid.SetColumn(reject, 2); row.Children.Add(reject);
                requests.Children.Add(row);
            });
        }
        foreach (PendingOnlineRequest pending in _server.Pending) AddRequest(pending);
        _server.RequestReceived += AddRequest;
        _server.ClientDiscovered += host => Dispatcher.Invoke(() => _host.Text = host);
        panel.Children.Add(_status);
        Content = panel;
        Loaded += async (_, _) => await ControlAsync("status", 0);
        Closed += (_, _) => _server.RequestReceived -= AddRequest;
    }

    private Button Command(string text, Func<Task> action)
    {
        var button = new Button { Content = text, FontSize = 16, Padding = new Thickness(8), Margin = new Thickness(3) };
        button.Click += async (_, _) => await action();
        return button;
    }

    private async Task ControlAsync(string action, int minutes)
    {
        try
        {
            string host = _host.Text.Trim();
            OnlineControlResult result = await HistoryClient.ControlAsync(
                _config, host, new OnlineControlCommand { Action = action, Minutes = minutes }, CancellationToken.None);
            _config.ClientHost = host;
            _store.Save(_config);
            _timer.Text = $"Осталось: {Format(result.RemainingSeconds)}";
            _status.Foreground = Brushes.DarkSlateGray;
            _status.Text = result.Message;
        }
        catch (Exception ex)
        {
            _status.Foreground = Brushes.Firebrick;
            _status.Text = $"Не удалось связаться с клиентом: {ex.Message}";
        }
    }

    private static string Format(long seconds) => $"{Math.Max(0, seconds) / 3600:D2}:{Math.Max(0, seconds) % 3600 / 60:D2}:{Math.Max(0, seconds) % 60:D2}";
}
