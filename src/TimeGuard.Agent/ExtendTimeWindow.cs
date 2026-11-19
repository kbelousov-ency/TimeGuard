using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TimeGuard.Agent;

/// <summary>Немодальное продление: ожидание ввода не приостанавливает учёт времени.</summary>
public sealed class ExtendTimeWindow : Window
{
    private readonly TextBox _code;
    private readonly TextBlock _remaining;
    private readonly TextBlock _message;
    private readonly Button _submit;
    public event Action<string>? CodeSubmitted;

    public ExtendTimeWindow()
    {
        Title = "TimeGuard — добавить время";
        Width = 460; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false; Topmost = true;
        Background = new SolidColorBrush(Color.FromRgb(240, 247, 255));
        Foreground = new SolidColorBrush(Color.FromRgb(9, 27, 66));
        var panel = new StackPanel { Margin = new Thickness(26) };
        panel.Children.Add(new TextBlock { Text = "Добавить время", FontSize = 26, FontWeight = FontWeights.Bold });
        _remaining = new TextBlock { FontSize = 16, Margin = new Thickness(0, 12, 0, 12) };
        panel.Children.Add(_remaining);
        panel.Children.Add(new TextBlock
        {
            Text = "Введите код родителя. Минуты добавятся к текущему остатку. Таймер продолжает идти.",
            TextWrapping = TextWrapping.Wrap, FontSize = 15, Margin = new Thickness(0, 0, 0, 18),
        });
        panel.Children.Add(new TextBlock { Text = "Код родителя", Margin = new Thickness(0, 0, 0, 6) });
        _code = new TextBox { FontSize = 24, Padding = new Thickness(10), MaxLength = 12 };
        System.Windows.Automation.AutomationProperties.SetName(_code, "Код родителя");
        _code.PreviewKeyDown += (_, e) =>
        {
            // Физические цифровые клавиши работают при любой раскладке.
            int digit = e.Key >= Key.D0 && e.Key <= Key.D9 ? e.Key - Key.D0
                : e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9 ? e.Key - Key.NumPad0 : -1;
            if (digit >= 0 && Keyboard.Modifiers == ModifierKeys.None)
            {
                if (_code.Text.Length - _code.SelectionLength < _code.MaxLength)
                {
                    int at = _code.SelectionStart;
                    _code.SelectedText = digit.ToString();
                    _code.CaretIndex = at + 1;
                }
                e.Handled = true;
            }
        };
        panel.Children.Add(_code);
        _submit = new Button
        {
            Content = "Добавить время", IsDefault = true, FontSize = 18,
            Padding = new Thickness(12), Margin = new Thickness(0, 16, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(0, 112, 245)), Foreground = Brushes.White,
        };
        _submit.Click += (_, _) => Submit();
        panel.Children.Add(_submit);
        _message = new TextBlock { FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0) };
        panel.Children.Add(_message);
        Content = panel;
        Loaded += (_, _) => _code.Focus();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    public void SetRemaining(long seconds) =>
        _remaining.Text = $"Осталось: {seconds / 3600:D2}:{seconds % 3600 / 60:D2}:{seconds % 60:D2}";

    private void Submit()
    {
        string digits = new(_code.Text.Where(c => c is >= '0' and <= '9').ToArray());
        if (digits.Length is not (9 or 10) || _code.Text.Any(c => !(c is >= '0' and <= '9') && c != '-' && !char.IsWhiteSpace(c)))
        {
            _message.Text = "Введите 9 цифр обычного или 10 цифр резервного кода.";
            return;
        }
        _submit.IsEnabled = false;
        _message.Text = "Проверяем код…";
        CodeSubmitted?.Invoke(digits);
    }

    public void SetResult(bool accepted, string message)
    {
        _submit.IsEnabled = true;
        _message.Text = message;
        if (accepted) _code.Clear();
        else _code.SelectAll();
        _code.Focus();
    }
}
