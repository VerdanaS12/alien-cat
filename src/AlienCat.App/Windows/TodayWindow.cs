using System.Windows;
using System.Windows.Controls;
using AlienCat.Core.Tasks;

namespace AlienCat.App.Windows;

/// <summary>«Задачи на сегодня»: today's open tasks plus the ones missed earlier.</summary>
internal sealed class TodayWindow : Window
{
    readonly StackPanel _list = new();
    readonly Func<IReadOnlyList<CatTask>> _source;

    public TodayWindow(Func<IReadOnlyList<CatTask>> source)
    {
        _source = source;
        Ui.StyleWindow(this, "Задачи на сегодня", 440);
        var close = Ui.Button("Закрыть");
        close.Click += (_, _) => Close();
        var root = new StackPanel();
        root.Children.Add(new ScrollViewer { Content = _list, MaxHeight = 520, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        root.Children.Add(Ui.ButtonRow(close));
        Content = new Border { Padding = new Thickness(16), Child = root };
        Refresh();
    }

    public event Action<CatTask>? DoneClicked;

    public void Refresh()
    {
        _list.Children.Clear();
        var now = DateTimeOffset.Now;
        var tasks = ReminderEngine.MorningSummary(_source(), now, TimeZoneInfo.Local);
        var missed = tasks.Where(t => t.Due < now).ToList();
        var upcoming = tasks.Where(t => t.Due >= now).ToList();

        if (tasks.Count == 0)
        {
            _list.Children.Add(Ui.Card(Ui.Label("На сегодня всё сделано! Котик доволен 💙", 15)));
            return;
        }
        if (missed.Count > 0) AddSection("Пропущено", missed, true);
        if (upcoming.Count > 0) AddSection("Сегодня", upcoming, false);
    }

    void AddSection(string title, List<CatTask> tasks, bool overdue)
    {
        _list.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = overdue ? Ui.Urgent : Ui.Muted,
            Margin = new Thickness(2, _list.Children.Count == 0 ? 0 : 12, 0, 6),
        });
        foreach (var t in tasks) _list.Children.Add(Row(t, overdue));
    }

    UIElement Row(CatTask task, bool overdue)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var due = task.Due!.Value.ToLocalTime();
        var when = overdue && due.Date != DateTime.Today ? due.ToString("dd.MM") : due.ToString("HH:mm");
        var time = Ui.Label(when, 14, true, overdue ? Ui.Urgent : Ui.Accent);
        time.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(time, 0);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Ui.Label(task.Title, 14));
        var badge = task.Priority switch
        {
            Priority.Urgent => Ui.Label("Срочный", 11, true, Ui.Urgent),
            Priority.Important => Ui.Label("Важный", 11, true, Ui.Important),
            _ => null,
        };
        if (badge is not null) text.Children.Add(badge);
        Grid.SetColumn(text, 1);

        var done = Ui.Button("Готово", primary: true);
        done.MinWidth = 0;
        done.Click += (_, _) => DoneClicked?.Invoke(task);
        Grid.SetColumn(done, 2);

        grid.Children.Add(time);
        grid.Children.Add(text);
        grid.Children.Add(done);
        var card = Ui.Card(grid);
        card.Padding = new Thickness(12, 8, 12, 8);
        card.Margin = new Thickness(0, 0, 0, 6);
        return card;
    }
}
