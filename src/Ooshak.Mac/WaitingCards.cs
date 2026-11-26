using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Ooshak.Core;

namespace Ooshak.Mac;

/// <summary>The waiting sessions page: one group per project, one card per session.</summary>
public sealed class WaitingCards(TopLevelProvider topLevel, SessionBoard board, PermissionBroker permissions, Action<string> log)
{
    private static readonly FontFamily Mono = new("Menlo, monospace");

    public void Fill(StackPanel list)
    {
        list.Children.Clear();

        var items = board.Items;
        if (items.Count == 0)
        {
            list.Children.Add(Ui.Note(L.T("Şu an seni bekleyen oturum yok.")));
            return;
        }

        foreach (var group in items.GroupBy(i => i.Cwd).OrderBy(g => g.Min(i => i.Kind)).ThenBy(g => g.Key))
        {
            var first = group.First();
            list.Children.Add(new StackPanel
            {
                Margin = new Thickness(2, 14, 0, 6),
                Children =
                {
                    new TextBlock
                    {
                        Text = $"{(first.Project.Length > 0 ? first.Project : L.T("Klasörsüz"))}  {group.Count()}",
                        FontWeight = FontWeight.SemiBold,
                        Foreground = Ui.Text,
                    },
                    new TextBlock { Text = first.Cwd, FontFamily = Mono, FontSize = 11, Foreground = Ui.Muted },
                },
            });

            foreach (var item in group)
                list.Children.Add(Card(item));
        }
    }

    private Control Card(WaitingSession item)
    {
        var accent = item.Kind switch
        {
            WaitKind.Permission => Ui.Coral,
            WaitKind.Question => Ui.Brass,
            _ => Ui.Teal,
        };

        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        head.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new Ellipse { Width = 8, Height = 8, Fill = accent, VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = KindName(item.Kind), Foreground = accent, FontSize = 12, FontWeight = FontWeight.SemiBold },
            },
        });
        var age = new TextBlock { Text = Age(item.Since), Foreground = Ui.Muted, FontSize = 12 };
        Grid.SetColumn(age, 1);
        head.Children.Add(age);

        var content = new StackPanel { Spacing = 4 };
        content.Children.Add(head);
        content.Children.Add(new TextBlock
        {
            Text = item.Title,
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Foreground = Ui.Text,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 4, 0, 0),
        });

        if (item.Detail.Length > 0)
        {
            content.Children.Add(new TextBlock
            {
                Text = item.Detail,
                Foreground = item.Kind == WaitKind.Permission ? Ui.Text : Ui.Muted,
                FontFamily = item.Kind == WaitKind.Permission ? Mono : FontFamily.Default,
                FontSize = item.Kind == WaitKind.Permission ? 12 : 13,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 110,
            });
        }

        // The question is already the card's text, so only its options are added.
        if (item.Request is { IsQuestion: true } asked)
            content.Children.Add(QuestionPicker.Build(asked, permissions, showQuestionText: asked.Questions.Count > 1));

        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        if (item.Request is { IsQuestion: false } request)
        {
            actions.Children.Add(Action(L.T("İzin ver"), primary: true, () => permissions.Decide(request, allow: true)));
            actions.Children.Add(Action(L.T("Reddet"), primary: false, () => permissions.Decide(request, allow: false)));
        }

        if (item.LocalId is { } localId)
            actions.Children.Add(Action(L.T("Aç"), primary: item.Request is null, () => MacController.Open(ClaudeDesktop.SessionLink(localId))));
        else
            actions.Children.Add(Action(L.T("Komutu kopyala"), primary: false, () => _ = CopyAsync(item.ResumeCommand)));

        actions.Children.Add(Action(L.T("Kaldır"), primary: false, () => board.Dismiss(item.SessionId)));
        content.Children.Add(actions);

        // A terminal session cannot be opened from here; its resume command is the way back.
        if (!item.InApp)
        {
            content.Children.Add(new TextBlock
            {
                Text = $"{L.T("Terminal oturumu")} · {item.ResumeCommand}",
                FontFamily = Mono,
                FontSize = 11,
                Foreground = Ui.Muted,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }

        var strip = new Border { Width = 3, Background = accent, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 2, 14, 2) };
        var row = new DockPanel();
        DockPanel.SetDock(strip, Dock.Left);
        row.Children.Add(strip);
        row.Children.Add(content);

        return new Border
        {
            Background = Ui.Card,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 14),
            Margin = new Thickness(0, 0, 0, 10),
            Child = row,
        };
    }

    private static Button Action(string text, bool primary, Action click)
    {
        var button = new Button
        {
            Content = text,
            Margin = new Thickness(0, 0, 8, 4),
            Background = primary ? Ui.Brass : Brushes.Transparent,
            Foreground = primary ? Ui.Brush("#17130E") : Ui.Text,
            BorderBrush = Ui.Brush("#2E2820"),
            BorderThickness = new Thickness(primary ? 0 : 1),
            CornerRadius = new CornerRadius(8),
        };
        button.Click += (_, _) => click();
        return button;
    }

    private async Task CopyAsync(string text)
    {
        if (topLevel()?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
        else
            log(L.T("Panoya kopyalanamadı: {0}", text));
    }

    public static string KindName(WaitKind kind) => L.T(kind switch
    {
        WaitKind.Permission => "İzin istiyor",
        WaitKind.Question => "Soru soruyor",
        _ => "Seni bekliyor",
    });

    public static string Age(DateTimeOffset since)
    {
        var elapsed = DateTimeOffset.UtcNow - since;
        return elapsed.TotalMinutes < 1 ? L.T("şimdi")
            : elapsed.TotalHours < 1 ? $"{(int)elapsed.TotalMinutes} {L.T("dk")}"
            : elapsed.TotalDays < 1 ? $"{(int)elapsed.TotalHours} {L.T("sa")}"
            : $"{(int)elapsed.TotalDays} {L.T("gün")}";
    }
}

public delegate TopLevel? TopLevelProvider();
