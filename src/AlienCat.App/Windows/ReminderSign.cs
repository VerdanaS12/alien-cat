using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using AlienCat.App.Win32;
using AlienCat.Core.Tasks;

namespace AlienCat.App.Windows;

/// <summary>The paper sign the cat holds up: task title plus «Готово» and «Отложить».</summary>
internal sealed class ReminderSign : Window
{
    const int MaxTitle = 60;

    public ReminderSign(Reminder reminder)
    {
        Reminder = reminder;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        FontFamily = Ui.Font;

        var stack = new StackPanel { MaxWidth = 260 };
        if (reminder.Kind == ReminderKind.Early)
            stack.Children.Add(Ui.Label("Скоро, через 10 минут:", 12, color: Ui.Muted));
        else if (reminder.Task.Priority == Priority.Urgent)
            stack.Children.Add(Ui.Label("Срочно!", 12, true, Ui.Urgent));
        stack.Children.Add(Ui.Label(Shorten(reminder.Task.Title), 15, true));
        if (reminder.Task.Due is { } due)
            stack.Children.Add(Ui.Label(due.ToLocalTime().ToString("HH:mm"), 12, color: Ui.Muted));

        var done = Ui.Button("Готово", primary: true);
        done.Margin = new Thickness(0);
        done.Click += (_, _) => Done?.Invoke();
        var later = Ui.Button("Отложить");
        var menu = new ContextMenu();
        foreach (var (label, option) in new[]
                 {
                     ("на 10 минут", SnoozeOption.TenMinutes),
                     ("на час", SnoozeOption.OneHour),
                     ("до завтра", SnoozeOption.TomorrowMorning),
                 })
        {
            var item = new MenuItem { Header = label };
            item.Click += (_, _) => Snoozed?.Invoke(option);
            menu.Items.Add(item);
        }
        later.Click += (_, _) => { menu.PlacementTarget = later; menu.IsOpen = true; };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        row.Children.Add(done);
        row.Children.Add(later);
        stack.Children.Add(row);

        Content = new Border
        {
            Background = Ui.Paper,
            BorderBrush = Ui.PaperBorder,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 10, 14, 12),
            Margin = new Thickness(6),
            Child = stack,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = 0.25 },
        };
    }

    public Reminder Reminder { get; }
    public event Action? Done;
    public event Action<SnoozeOption>? Snoozed;

    /// <summary>Keeps the sign above the cat, inside the screen.</summary>
    public void Follow(double catCenterX, double catTop, double screenLeft, double screenRight)
    {
        if (ActualWidth <= 0) return;
        var left = Math.Clamp(catCenterX - ActualWidth / 2, screenLeft, Math.Max(screenLeft, screenRight - ActualWidth));
        var top = Math.Max(0, catTop - ActualHeight + 8);
        if (double.IsNaN(Left) || Math.Abs(Left - left) > 0.5) Left = left;
        if (double.IsNaN(Top) || Math.Abs(Top - top) > 0.5) Top = top;
    }

    static string Shorten(string title)
    {
        var t = string.IsNullOrWhiteSpace(title) ? "Без названия" : title.Trim();
        return t.Length <= MaxTitle ? t : t[..(MaxTitle - 1)] + "…";
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var ex = (long)Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE);
        Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(ex | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE));
    }
}
