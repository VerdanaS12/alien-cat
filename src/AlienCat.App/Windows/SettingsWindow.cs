using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using AlienCat.Core.Storage;
using AlienCat.Core.Tasks;

namespace AlienCat.App.Windows;

/// <summary>«Настройки»: look and behaviour of the cat plus the Notion connection.</summary>
internal sealed class SettingsWindow : Window
{
    readonly AppSettings _original;
    readonly Func<string?, string, Task<string>> _check;
    readonly ComboBox _scale = Combo("Маленький (×1)", "Обычный (×2)", "Большой (×3)");
    readonly ComboBox _activity = Combo("Спокойный", "Обычный", "Игривый");
    readonly CheckBox _cursor = new() { Content = "Иногда ловить курсор мыши" };
    readonly CheckBox _quiet = new() { Content = "Тихие часы: не напоминать с" };
    readonly TextBox _quietFrom = Time();
    readonly TextBox _quietTo = Time();
    readonly CheckBox _autostart = new() { Content = "Запускать вместе с Windows" };
    readonly PasswordBox _token = new() { Padding = new Thickness(4) };
    readonly TextBox _url = new() { Padding = new Thickness(4) };
    readonly TextBlock _status = Ui.Label("", 13);

    /// <param name="check">Checks the connection with (new token or null for the saved one, database link) and returns a message.</param>
    public SettingsWindow(AppSettings settings, bool autostartOn, Func<string?, string, Task<string>> check)
    {
        _original = settings;
        _check = check;
        Ui.StyleWindow(this, "Настройки котика", 460);

        _scale.SelectedIndex = Math.Clamp(settings.Scale, 1, 3) - 1;
        _activity.SelectedIndex = (int)settings.Activity;
        _cursor.IsChecked = settings.CursorHunt;
        _quiet.IsChecked = settings.Quiet.Enabled;
        _quietFrom.Text = settings.Quiet.Start.ToString("HH:mm");
        _quietTo.Text = settings.Quiet.End.ToString("HH:mm");
        _autostart.IsChecked = autostartOn;
        _url.Text = settings.DatabaseUrl ?? "";

        var look = new StackPanel();
        look.Children.Add(Ui.Label("Котик", 15, true));
        look.Children.Add(Field("Размер", _scale));
        look.Children.Add(Field("Характер", _activity));
        look.Children.Add(Spaced(_cursor));
        var quietRow = new StackPanel { Orientation = Orientation.Horizontal };
        quietRow.Children.Add(_quiet);
        quietRow.Children.Add(_quietFrom);
        quietRow.Children.Add(new TextBlock { Text = " до ", VerticalAlignment = VerticalAlignment.Center });
        quietRow.Children.Add(_quietTo);
        look.Children.Add(Spaced(quietRow));
        look.Children.Add(Spaced(_autostart));

        var notion = new StackPanel();
        notion.Children.Add(Ui.Label("Notion", 15, true));
        notion.Children.Add(Field(settings.ProtectedToken is null ? "Ключ интеграции" : "Ключ интеграции (сохранён; введите новый, чтобы заменить)", _token));
        notion.Children.Add(Field("Ссылка на базу «Задачи котика»", _url));
        var check = Ui.Button("Проверить подключение");
        check.Margin = new Thickness(0, 10, 0, 0);
        check.HorizontalAlignment = HorizontalAlignment.Left;
        check.Click += async (_, _) => await CheckAsync();
        notion.Children.Add(check);
        notion.Children.Add(Spaced(_status));
        var help = new TextBlock { Margin = new Thickness(0, 8, 0, 0) };
        var link = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("Как подключить Notion?"));
        link.Click += (_, _) => OpenHelp();
        help.Inlines.Add(link);
        notion.Children.Add(help);

        var save = Ui.Button("Сохранить", primary: true);
        save.Click += (_, _) => Submit();
        var cancel = Ui.Button("Отмена");
        cancel.Click += (_, _) => Close();

        var root = new StackPanel();
        root.Children.Add(Ui.Card(look));
        root.Children.Add(new Border { Height = 12 });
        root.Children.Add(Ui.Card(notion));
        root.Children.Add(Ui.ButtonRow(cancel, save));
        Content = new Border { Padding = new Thickness(16), Child = root };
        Loaded += (_, _) => Activate();
    }

    /// <summary>New settings and the new plain token (null when unchanged).</summary>
    public event Action<AppSettings, string?>? Saved;

    async Task CheckAsync()
    {
        _status.Foreground = Ui.Muted;
        _status.Text = "Проверяю…";
        try
        {
            var token = _token.Password.Trim();
            var result = await _check(token.Length > 0 ? token : null, _url.Text.Trim());
            _status.Foreground = result == "Всё работает!" ? Ui.Accent : Ui.Urgent;
            _status.Text = result;
        }
        catch (Exception ex)
        {
            _status.Foreground = Ui.Urgent;
            _status.Text = ex.Message;
        }
    }

    void Submit()
    {
        if (!TryTime(_quietFrom.Text, out var from) || !TryTime(_quietTo.Text, out var to))
        {
            _status.Foreground = Ui.Urgent;
            _status.Text = "Время тихих часов укажите в виде ЧЧ:ММ, например 23:00.";
            return;
        }
        var url = _url.Text.Trim();
        if (url.Length > 0)
        {
            try { Core.Notion.NotionIds.ParseDatabaseId(url); }
            catch (ArgumentException ex)
            {
                _status.Foreground = Ui.Urgent;
                _status.Text = ex.Message;
                return;
            }
        }
        var updated = _original with
        {
            Scale = _scale.SelectedIndex + 1,
            Activity = (ActivityLevel)Math.Max(0, _activity.SelectedIndex),
            CursorHunt = _cursor.IsChecked == true,
            Quiet = new QuietHours(_quiet.IsChecked == true, from, to),
            Autostart = _autostart.IsChecked == true,
            DatabaseUrl = url.Length > 0 ? url : null,
        };
        var token = _token.Password.Trim();
        Saved?.Invoke(updated, token.Length > 0 ? token : null);
        Close();
    }

    static void OpenHelp()
    {
        var readme = Path.Combine(AppContext.BaseDirectory, "README.md");
        var target = File.Exists(readme) ? readme : "https://www.notion.so/profile/integrations";
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch { /* nothing to open with */ }
    }

    static bool TryTime(string text, out TimeOnly value) =>
        TimeOnly.TryParseExact(text.Trim(), ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out value);

    static ComboBox Combo(params string[] items)
    {
        var c = new ComboBox { Padding = new Thickness(4) };
        foreach (var i in items) c.Items.Add(i);
        return c;
    }

    static TextBox Time() => new() { Width = 56, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(2), TextAlignment = TextAlignment.Center };

    static UIElement Field(string label, UIElement control)
    {
        var s = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        s.Children.Add(Ui.Label(label, 12, color: Ui.Muted));
        s.Children.Add(control);
        return s;
    }

    static UIElement Spaced(UIElement e)
    {
        if (e is FrameworkElement fe) fe.Margin = new Thickness(0, 10, 0, 0);
        return e;
    }
}
