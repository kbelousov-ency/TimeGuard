using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TimeGuard.Core;

namespace TimeGuard.Parent;

/// <summary>Первичная настройка пары: метка клиента и мастер-пароль.</summary>
public sealed class SetupWindow : Window
{
    private readonly ParentStore _store;
    private readonly TextBox _label = new() { FontSize = 16, Padding = new Thickness(8) };
    private readonly PasswordBox _pwd = new() { FontSize = 16, Padding = new Thickness(8) };
    private readonly PasswordBox _pwd2 = new() { FontSize = 16, Padding = new Thickness(8) };
    private readonly TextBlock _error = new()
    {
        Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0),
    };

    public ParentConfig? Result { get; private set; }

    public SetupWindow(ParentStore store)
    {
        _store = store;
        Title = "TimeGuard — настройка";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;

        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(Header("Настройка пары родитель ↔ клиент"));
        panel.Children.Add(Caption("Название клиентского ПК (для вашего удобства):"));
        panel.Children.Add(_label);
        panel.Children.Add(Caption("Мастер-пароль (открывает клиента в любой момент):"));
        panel.Children.Add(_pwd);
        panel.Children.Add(Caption("Повторите мастер-пароль:"));
        panel.Children.Add(_pwd2);

        var create = new Button
        {
            Content = "Создать пару",
            FontSize = 16,
            Margin = new Thickness(0, 16, 0, 0),
            Padding = new Thickness(12, 8, 12, 8),
        };
        create.Click += (_, _) => OnCreate();
        panel.Children.Add(create);

        panel.Children.Add(new TextBlock
        {
            Text = "Ставите родителя на второй ПК для того же клиента? " +
                   "Импортируйте файл сопряжения (pairing.json) — тогда коды сойдутся.",
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x5D, 0x6B)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 20, 0, 6),
        });
        var import = new Button
        {
            Content = "Импортировать файл сопряжения…",
            FontSize = 14,
            Padding = new Thickness(12, 6, 12, 6),
        };
        import.Click += (_, _) => OnImport();
        panel.Children.Add(import);
        panel.Children.Add(_error);

        Content = panel;
    }

    private void OnImport()
    {
        _error.Text = string.Empty;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Файл сопряжения (*.json)|*.json|Все файлы (*.*)|*.*",
        };
        if (dlg.ShowDialog() != true)
        {
            return;
        }

        ParentConfig? cfg = Pairing.Import(dlg.FileName);
        if (cfg is null)
        {
            _error.Text = "Не удалось прочитать файл сопряжения.";
            return;
        }
        cfg.ClientLabel = string.IsNullOrWhiteSpace(_label.Text) ? "Клиент" : _label.Text.Trim();
        _store.Save(cfg);
        Result = cfg;
        DialogResult = true;
        Close();
    }

    private void OnCreate()
    {
        _error.Text = string.Empty;
        if (string.IsNullOrWhiteSpace(_label.Text))
        {
            _error.Text = "Укажите название клиента.";
            return;
        }
        if (_pwd.Password.Length < 6)
        {
            _error.Text = "Мастер-пароль не короче 6 символов.";
            return;
        }
        if (_pwd.Password != _pwd2.Password)
        {
            _error.Text = "Пароли не совпадают.";
            return;
        }

        var (hash, salt) = MasterPassword.Create(_pwd.Password);
        var cfg = new ParentConfig
        {
            SecretBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ClientMachineId = Guid.NewGuid().ToString("N"),
            ClientLabel = _label.Text.Trim(),
            MasterHashBase64 = hash,
            MasterSaltBase64 = salt,
        };
        _store.Save(cfg);
        Result = cfg;
        DialogResult = true;
        Close();
    }

    private static TextBlock Header(string t) => new()
    {
        Text = t, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16),
    };

    private static TextBlock Caption(string t) => new()
    {
        Text = t, Margin = new Thickness(0, 12, 0, 4), Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x5D, 0x6B)),
    };
}
