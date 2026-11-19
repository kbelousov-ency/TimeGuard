using System.Windows;
using System.Windows.Controls;
using TimeGuard.Core;

namespace TimeGuard.Parent;

public sealed class ClientSettingsWindow : Window
{
    public ClientSettingsWindow(ParentStore store, ParentConfig cfg)
    {
        Title = "Настройки — " + cfg.ClientLabel;
        Width = 510; SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "Смена мастер-пароля: " + cfg.ClientLabel, FontSize = 22, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "После сохранения перенесите новый JSON на ПК ребёнка и выберите его в клиентском установщике. До этого там действует старый пароль. Другие клиенты не изменятся.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 14) });
        PasswordBox Field(string label)
        {
            panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 4) });
            var box = new PasswordBox { FontSize = 18, Padding = new Thickness(8) };
            panel.Children.Add(box); return box;
        }
        var old = Field("Текущий мастер-пароль родительского профиля");
        var first = Field("Новый пароль (от 6 символов)");
        var second = Field("Повторите новый пароль");
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
        var save = new Button { Content = "Сменить пароль и экспортировать JSON…", Padding = new Thickness(12), Margin = new Thickness(0, 18, 0, 0) };
        save.Click += (_, _) =>
        {
            if (!MasterPassword.Verify(old.Password, cfg.MasterHashBase64, cfg.MasterSaltBase64)) { error.Text = "Текущий пароль неверен."; return; }
            if (first.Password.Length < 6 || first.Password != second.Password) { error.Text = "Нужно не менее 6 символов; пароли должны совпадать."; return; }
            var dialog = new Microsoft.Win32.SaveFileDialog { FileName = "pairing-update.tgpair.json", Filter = "Файл сопряжения (*.json)|*.json" };
            if (dialog.ShowDialog(this) != true) return;
            var (hash, salt) = MasterPassword.Create(first.Password);
            var next = new ParentConfig { ClientLabel = cfg.ClientLabel, ClientMachineId = cfg.ClientMachineId, SecretBase64 = cfg.SecretBase64, MasterHashBase64 = hash, MasterSaltBase64 = salt };
            try
            {
                Pairing.Export(next, dialog.FileName);
                string previousHash = cfg.MasterHashBase64, previousSalt = cfg.MasterSaltBase64;
                cfg.MasterHashBase64 = hash; cfg.MasterSaltBase64 = salt;
                try { store.Save(cfg); }
                catch { cfg.MasterHashBase64 = previousHash; cfg.MasterSaltBase64 = previousSalt; throw; }
                old.Clear(); first.Clear(); second.Clear();
                error.Text = "Пароль сохранён у родителя. Теперь импортируйте JSON на ПК ребёнка через установщик. На других родительских ПК импортируйте этот же файл через «Добавить клиента».";
            }
            catch (Exception ex) { error.Text = "Не удалось завершить сохранение: " + ex.Message; }
        };
        panel.Children.Add(save); panel.Children.Add(error); Content = panel;
    }
}
