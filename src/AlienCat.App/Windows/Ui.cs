using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AlienCat.App.Windows;

/// <summary>Shared look for the cat's small windows: soft blue, rounded, friendly.</summary>
internal static class Ui
{
    public static readonly Brush Background = Brush("#F3F6FF");
    public static readonly Brush Surface = Brush("#FFFFFF");
    public static readonly Brush Paper = Brush("#FFF6DD");
    public static readonly Brush PaperBorder = Brush("#C9A46B");
    public static readonly Brush Accent = Brush("#4F7BE8");
    public static readonly Brush AccentDark = Brush("#3A5FC4");
    public static readonly Brush Text = Brush("#1C2152");
    public static readonly Brush Muted = Brush("#6A7398");
    public static readonly Brush Border = Brush("#D5DDF5");
    public static readonly Brush Urgent = Brush("#E5486A");
    public static readonly Brush Important = Brush("#F08C2E");
    public static readonly FontFamily Font = new("Segoe UI");

    public static SolidColorBrush Brush(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    public static void StyleWindow(Window w, string title, double width)
    {
        w.Title = title;
        w.Width = width;
        w.SizeToContent = SizeToContent.Height;
        w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        w.ResizeMode = ResizeMode.NoResize;
        w.Background = Background;
        w.FontFamily = Font;
        w.FontSize = 14;
        w.Foreground = Text;
        w.Topmost = true;
        try
        {
            using var s = typeof(Ui).Assembly.GetManifestResourceStream("icon.ico");
            if (s is not null) w.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(s, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
        }
        catch { /* icon is decorative */ }
    }

    public static Button Button(string text, bool primary = false)
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "bd";
        border.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new CornerRadius(10));
        border.SetValue(System.Windows.Controls.Border.PaddingProperty, new Thickness(14, 6, 14, 6));
        border.SetBinding(System.Windows.Controls.Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(System.Windows.Controls.Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetValue(System.Windows.Controls.Border.BorderThicknessProperty, new Thickness(1));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);

        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.88));
        template.Triggers.Add(hover);

        return new Button
        {
            Content = text,
            Template = template,
            Background = primary ? Accent : Surface,
            BorderBrush = primary ? AccentDark : Border,
            Foreground = primary ? Brushes.White : Text,
            FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal,
            Cursor = System.Windows.Input.Cursors.Hand,
            Margin = new Thickness(6, 0, 0, 0),
            MinWidth = 90,
        };
    }

    public static TextBlock Label(string text, double size = 14, bool bold = false, Brush? color = null) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        Foreground = color ?? Text,
        TextWrapping = TextWrapping.Wrap,
    };

    public static Border Card(UIElement child) => new()
    {
        Background = Surface,
        BorderBrush = Border,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(14),
        Padding = new Thickness(16),
        Child = child,
    };

    public static StackPanel ButtonRow(params Button[] buttons)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        foreach (var b in buttons) row.Children.Add(b);
        return row;
    }
}
