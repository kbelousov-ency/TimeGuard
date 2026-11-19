using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace TimeGuard.Agent;

public sealed class ClientControlWindow : Window
{
    private readonly TextBox _code;
    private readonly TextBox _parentHost;
    private readonly TextBox _minutes;
    private readonly TextBlock _remaining;
    private readonly TextBlock _message;
    private readonly Button _codeButton;
    private readonly Button _updateButton;
    private readonly ProgressBar _updateProgress;
    public event Action<string>? CodeSubmitted;
    public event Action? ResetRequested;
    public event Action<string, int>? TimeRequested;

    public ClientControlWindow()
    {
        Title = "TimeGuard — управление";
        Width = 520; SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true; Background = Brushes.White; FontFamily = new FontFamily("Segoe UI");
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "Управление временем", FontSize = 25, FontWeight = FontWeights.Bold });
        _remaining = new TextBlock { FontSize = 21, Margin = new Thickness(0, 10, 0, 14) };
        panel.Children.Add(_remaining);
        panel.Children.Add(new TextBlock { Text = "Код родителя", Margin = new Thickness(0, 0, 0, 5) });
        _code = new TextBox { FontSize = 22, Padding = new Thickness(8), MaxLength = 12 };
        panel.Children.Add(_code);
        _codeButton = Button("Проверить код", SubmitCode);
        panel.Children.Add(_codeButton);

        var actions = new UniformGrid { Columns = 2, Margin = new Thickness(0, 10, 0, 0) };
        actions.Children.Add(Button("Сбросить таймер", () => ResetRequested?.Invoke()));
        _updateButton = Button("Проверить обновление", UpdateAsync);
        actions.Children.Add(_updateButton);
        panel.Children.Add(actions);
        _updateProgress = new ProgressBar { Height = 7, Minimum = 0, Maximum = 100, Visibility = Visibility.Collapsed, Margin = new Thickness(3, 5, 3, 0) };
        panel.Children.Add(_updateProgress);

        panel.Children.Add(new TextBlock { Text = "Соединение с родителем", Margin = new Thickness(0, 16, 0, 5) });
        _parentHost = new TextBox { Text = "Автоопределение в локальной сети", FontSize = 16, Padding = new Thickness(8), IsReadOnly = true };
        panel.Children.Add(_parentHost);
        var requestRow = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        _message = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0), Foreground = Brushes.DarkSlateGray };
        requestRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        requestRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        requestRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _minutes = new TextBox { Text = "30", FontSize = 17, Padding = new Thickness(8) };
        requestRow.Children.Add(_minutes);
        var label = new TextBlock { Text = "минут", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
        Grid.SetColumn(label, 1); requestRow.Children.Add(label);
        var request = Button("Запросить время", () =>
        {
            if (int.TryParse(_minutes.Text, out int minutes)) TimeRequested?.Invoke(string.Empty, minutes);
            else _message.Text = "Введите количество минут.";
        });
        Grid.SetColumn(request, 2); requestRow.Children.Add(request);
        panel.Children.Add(requestRow);
        panel.Children.Add(_message);
        Content = panel;
        Loaded += (_, _) => _code.Focus();
    }

    public void SetRemaining(long seconds) => _remaining.Text = $"Текущее время: {Math.Max(0, seconds) / 3600:D2}:{Math.Max(0, seconds) % 3600 / 60:D2}:{Math.Max(0, seconds) % 60:D2}";
    public void SetResult(bool accepted, string message) { _message.Text = message; _codeButton.IsEnabled = true; if (accepted) _code.Clear(); }
    public void SetMessage(string message) => _message.Text = message;

    private void SubmitCode()
    {
        string digits = new(_code.Text.Where(char.IsDigit).ToArray());
        if (digits.Length is not (9 or 10)) { _message.Text = "Введите 9 или 10 цифр кода."; return; }
        _codeButton.IsEnabled = false; _message.Text = "Проверяем код…"; CodeSubmitted?.Invoke(digits);
    }

    private static Button Button(string text, Action action)
    {
        var button = new Button { Content = text, FontSize = 15, Padding = new Thickness(8), Margin = new Thickness(3) };
        button.Click += (_, _) => action(); return button;
    }

    private async void UpdateAsync()
    {
        _updateButton.IsEnabled = false;
        _updateProgress.Visibility = Visibility.Visible;
        _updateProgress.IsIndeterminate = true;
        _message.Text = "Проверка обновлений…";
        try
        {
            TimeGuard.Core.UpdateCheckResult check = await TimeGuard.Core.UpdateChecker.CheckAsync();
            if (!check.IsNewer)
            {
                _message.Text = $"Установлена последняя версия ({check.Current.ToString(3)}).";
                return;
            }
            _message.Text = $"Найдена версия {check.Latest.ToString(3)} (установлена {check.Current.ToString(3)}). Подготовка загрузки…";
            var progress = new Progress<TimeGuard.Core.DownloadProgress>(p =>
            {
                if (p.Percent is int percent)
                {
                    _updateProgress.IsIndeterminate = false;
                    _updateProgress.Value = percent;
                    _message.Text = $"Загрузка обновления: {percent}%";
                }
                else _message.Text = $"Загрузка обновления: {p.ReceivedBytes / 1024 / 1024} МБ";
            });
            string path = await TimeGuard.Core.UpdateDownloader.DownloadAsync("TimeGuard-Client-Setup.exe", progress);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "runas" });
            _message.Text = "Установщик запущен с правами администратора.";
        }
        catch (Exception ex) { _message.Text = $"Не удалось проверить или скачать обновление: {ex.Message}"; }
        finally
        {
            _updateButton.IsEnabled = true;
            _updateProgress.Visibility = Visibility.Collapsed;
        }
    }
}
