using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Ooshak.Core;

namespace Ooshak;

/// <summary>Builds the waiting sessions page: one group per project, one card per session.</summary>
public sealed class WaitingCards(FrameworkElement resources, SessionBoard board, PermissionBroker permissions, Action<string> log)
{
    private const double AccentWidth = 3;

    public void Fill(Panel list)
    {
        list.Children.Clear();

        foreach (var group in board.Items.GroupBy(i => i.Cwd).OrderBy(g => g.Min(i => i.Kind)).ThenBy(g => g.Key))
        {
            list.Children.Add(GroupHeader(group.First(), group.Count()));
            foreach (var item in group)
                list.Children.Add(Card(item));
        }
    }

    private UIElement GroupHeader(WaitingSession first, int count)
    {
        var name = new TextBlock
        {
            Text = first.Project.Length > 0 ? first.Project : L.T("Klasörsüz"),
            Style = StyleOf("CardTitle"),
        };
        var badge = new TextBlock
        {
            Text = count.ToString(),
            Margin = new Thickness(8, 0, 0, 0),
            Style = StyleOf("Eyebrow"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var path = new TextBlock
        {
            Text = first.Cwd,
            Style = StyleOf("Eyebrow"),
            FontFamily = Resource<FontFamily>("MonoFont"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var title = new StackPanel { Orientation = Orientation.Horizontal, Children = { name, badge } };
        return new StackPanel { Margin = new Thickness(2, 18, 0, 8), Children = { title, path } };
    }

    private UIElement Card(WaitingSession item)
    {
        var accent = AccentOf(item.Kind);

        var dot = new Ellipse { Width = 8, Height = 8, Fill = accent, VerticalAlignment = VerticalAlignment.Center };
        var kind = new TextBlock
        {
            Text = KindName(item.Kind),
            Margin = new Thickness(8, 0, 0, 0),
            Foreground = accent,
            FontWeight = FontWeights.SemiBold,
            Style = StyleOf("Eyebrow"),
        };
        var age = new TextBlock
        {
            Text = Age(item.Since),
            HorizontalAlignment = HorizontalAlignment.Right,
            Style = StyleOf("Eyebrow"),
        };
        var head = new Grid { Children = { new StackPanel { Orientation = Orientation.Horizontal, Children = { dot, kind } }, age } };

        var title = new TextBlock
        {
            Text = item.Title,
            Margin = new Thickness(0, 8, 0, 0),
            Style = StyleOf("CardTitle"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var content = new StackPanel { Children = { head, title } };

        if (item.Detail.Length > 0)
        {
            var detail = new TextBlock
            {
                Text = item.Detail,
                Margin = new Thickness(0, 4, 0, 0),
                Style = StyleOf(item.Kind == WaitKind.Permission ? "Body" : "Hint"),
                MaxHeight = 120,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            if (item.Kind == WaitKind.Permission)
            {
                detail.FontFamily = Resource<FontFamily>("MonoFont");
                detail.FontSize = 12.5;
            }

            content.Children.Add(detail);
        }

        content.Children.Add(Actions(item));
        content.Children.Add(new TextBlock
        {
            Text = item.InApp ? item.ResumeCommand : $"{L.T("Terminal oturumu")} · {item.ResumeCommand}",
            Margin = new Thickness(0, 8, 0, 0),
            Style = StyleOf("Eyebrow"),
            FontFamily = Resource<FontFamily>("MonoFont"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        var strip = new Border { Width = AccentWidth, Background = accent, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 2, 14, 2) };
        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(strip, Dock.Left);
        row.Children.Add(strip);
        row.Children.Add(content);

        return new Border { Style = StyleOf("Card"), Margin = new Thickness(0, 0, 0, 10), Child = row };
    }

    private StackPanel Actions(WaitingSession item)
    {
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };

        if (item.Request is { } request)
        {
            actions.Children.Add(MakeButton(L.T("İzin ver"), "PrimaryAction", () => permissions.Decide(request, allow: true)));
            actions.Children.Add(MakeButton(L.T("Reddet"), "GhostAction", () => permissions.Decide(request, allow: false)));
        }

        if (item.LocalId is { } localId)
            actions.Children.Add(MakeButton(L.T("Aç"), item.Request is null ? "PrimaryAction" : "GhostAction", () => Open(localId)));

        actions.Children.Add(MakeButton(L.T("Komutu kopyala"), "GhostAction", () => Copy(item.ResumeCommand)));
        actions.Children.Add(MakeButton(L.T("Kaldır"), "GhostAction", () => board.Dismiss(item.SessionId)));
        return actions;
    }

    private Button MakeButton(string text, string style, Action click)
    {
        var button = new Button { Content = text, Style = StyleOf(style), Margin = new Thickness(0, 0, 8, 0), Height = 32 };
        button.Click += (_, _) => click();
        return button;
    }

    private void Open(string localId)
    {
        try
        {
            Process.Start(new ProcessStartInfo(ClaudeDesktop.SessionLink(localId)) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            log(L.T("Oturum açılamadı: {0}", e.Message));
        }
    }

    private void Copy(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (System.Runtime.InteropServices.COMException e)
        {
            log(L.T("Panoya kopyalanamadı: {0}", e.Message));
        }
    }

    public static string KindName(WaitKind kind) => L.T(kind switch
    {
        WaitKind.Permission => "İzin istiyor",
        WaitKind.Question => "Soru soruyor",
        _ => "Seni bekliyor",
    });

    /// <summary>How long ago, in the largest unit that fits.</summary>
    public static string Age(DateTimeOffset since)
    {
        var elapsed = DateTimeOffset.UtcNow - since;
        return elapsed.TotalMinutes < 1 ? L.T("şimdi")
            : elapsed.TotalHours < 1 ? $"{(int)elapsed.TotalMinutes} {L.T("dk")}"
            : elapsed.TotalDays < 1 ? $"{(int)elapsed.TotalHours} {L.T("sa")}"
            : $"{(int)elapsed.TotalDays} {L.T("gün")}";
    }

    private Brush AccentOf(WaitKind kind) => Resource<Brush>(kind switch
    {
        WaitKind.Permission => "Coral",
        WaitKind.Question => "Brass",
        _ => "Teal",
    });

    private Style StyleOf(string key) => (Style)resources.FindResource(key);

    private T Resource<T>(string key) => (T)resources.FindResource(key);
}
