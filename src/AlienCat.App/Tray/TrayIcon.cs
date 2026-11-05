using WinForms = System.Windows.Forms;

namespace AlienCat.App.Tray;

/// <summary>Tray icon with the cat's menu.</summary>
internal sealed class TrayIcon : IDisposable
{
    readonly WinForms.NotifyIcon _icon;

    public TrayIcon(Action addNote, Action today, Action settings, Action exit)
    {
        using var stream = typeof(TrayIcon).Assembly.GetManifestResourceStream("icon.ico");
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Добавить заметку", null, (_, _) => addNote());
        menu.Items.Add("Задачи на сегодня", null, (_, _) => today());
        menu.Items.Add("Настройки", null, (_, _) => settings());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => exit());

        _icon = new WinForms.NotifyIcon
        {
            Icon = stream is null ? System.Drawing.SystemIcons.Application : new System.Drawing.Icon(stream),
            Text = "Космокот",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => today();
    }

    public void ShowBalloon(string title, string text) =>
        _icon.ShowBalloonTip(6000, title, text, WinForms.ToolTipIcon.Info);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
