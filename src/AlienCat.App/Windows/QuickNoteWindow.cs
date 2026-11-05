using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AlienCat.App.Windows;

internal sealed class QuickNoteWindow : Window
{
    readonly TextBox _text;

    public QuickNoteWindow()
    {
        Ui.StyleWindow(this, "Новая заметка", 400);
        _text = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 90,
            MaxLength = 2000,
            Padding = new Thickness(6),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        var save = Ui.Button("Сохранить", primary: true);
        save.Click += (_, _) => Submit();
        var cancel = Ui.Button("Отмена");
        cancel.Click += (_, _) => Close();

        var stack = new StackPanel();
        stack.Children.Add(Ui.Label("Что запомнить? Котик положит это в Notion.", 13, color: Ui.Muted));
        stack.Children.Add(new Border { Height = 8 });
        stack.Children.Add(_text);
        stack.Children.Add(Ui.Label("Ctrl+Enter — сохранить", 11, color: Ui.Muted));
        stack.Children.Add(Ui.ButtonRow(cancel, save));
        Content = new Border { Padding = new Thickness(16), Child = stack };

        Loaded += (_, _) => { Activate(); _text.Focus(); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control) { Submit(); e.Handled = true; }
            if (e.Key == Key.Escape) Close();
        };
    }

    public event Action<string>? Saved;

    void Submit()
    {
        var text = _text.Text.Trim();
        if (text.Length == 0) return;
        Saved?.Invoke(text);
        Close();
    }
}
