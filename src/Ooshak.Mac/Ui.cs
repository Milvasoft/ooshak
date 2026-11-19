using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Ooshak.Mac;

/// <summary>Colors and small builders shared by the windows, matching the Windows app's palette.</summary>
public static class Ui
{
    public static readonly IBrush Background = Brush("#1B1A18");
    public static readonly IBrush Card = Brush("#25231F");
    public static readonly IBrush Text = Brush("#F4EEE3");
    public static readonly IBrush Muted = Brush("#9C968B");
    public static readonly IBrush Brass = Brush("#E9B44C");
    public static readonly IBrush Teal = Brush("#43C6AC");
    public static readonly IBrush Coral = Brush("#EF6F5C");

    public static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));

    public static TextBlock Heading(string text) => new()
    {
        Text = text,
        FontSize = 15,
        FontWeight = FontWeight.SemiBold,
        Foreground = Text,
        Margin = new Thickness(0, 0, 0, 6),
    };

    public static TextBlock Note(string text) => new()
    {
        Text = text,
        FontSize = 12,
        Foreground = Muted,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 2, 0, 6),
    };

    public static TextBlock Label(string text) => new()
    {
        Text = text,
        Foreground = Text,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 8, 0, 4),
    };

    public static Border Section(params Control[] children)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.AddRange(children);
        return new Border
        {
            Background = Card,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 12),
            Child = panel,
        };
    }

    public static Button Button(string text, EventHandler<Avalonia.Interactivity.RoutedEventArgs> click)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 4, 8, 4) };
        button.Click += click;
        return button;
    }

    public static StackPanel Row(params Control[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.AddRange(children);
        return row;
    }
}
