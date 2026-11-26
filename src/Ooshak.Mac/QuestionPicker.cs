using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Ooshak.Core;

namespace Ooshak.Mac;

/// <summary>
/// Turns a session's question into buttons. A single question with one answer is sent on the
/// first click; several questions or multiple choice are picked first and sent with Answer.
/// </summary>
public static class QuestionPicker
{
    public static StackPanel Build(PermissionRequest request, PermissionBroker broker, bool showQuestionText)
    {
        var panel = new StackPanel { Spacing = 4 };
        var instant = request.Questions.Count == 1 && !request.Questions[0].MultiSelect;
        var chosen = request.Questions.ToDictionary(q => q.Question, _ => new List<string>());
        var send = Make(L.T("Cevapla"), chosen: true);
        send.IsEnabled = false;

        foreach (var question in request.Questions)
        {
            if (showQuestionText)
                panel.Children.Add(new TextBlock { Text = question.Question, Foreground = Ui.Text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 2) });

            var options = new WrapPanel();
            foreach (var option in question.Options)
            {
                var button = Make(option.Label, chosen: false);
                if (option.Description.Length > 0)
                    ToolTip.SetTip(button, option.Description);

                button.Click += (_, _) =>
                {
                    if (instant)
                    {
                        broker.AnswerQuestions(request, new Dictionary<string, string> { [question.Question] = option.Label });
                        return;
                    }

                    var picks = chosen[question.Question];
                    if (!question.MultiSelect)
                    {
                        picks.Clear();
                        foreach (var sibling in options.Children.OfType<Button>())
                            Paint(sibling, chosen: false);
                    }

                    var added = !picks.Remove(option.Label);
                    if (added)
                        picks.Add(option.Label);
                    Paint(button, added);
                    send.IsEnabled = chosen.Values.All(p => p.Count > 0);
                };
                options.Children.Add(button);
            }

            panel.Children.Add(options);
        }

        var footer = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        if (!instant)
        {
            send.Click += (_, _) => broker.AnswerQuestions(request, chosen.ToDictionary(c => c.Key, c => string.Join(", ", c.Value)));
            footer.Children.Add(send);
        }

        var handOff = Make(L.T("Claude'da cevapla"), chosen: false);
        ToolTip.SetTip(handOff, L.T("Soruyu Claude'un kendi penceresine bırakır."));
        handOff.Click += (_, _) => broker.HandOff(request);
        footer.Children.Add(handOff);
        panel.Children.Add(footer);
        return panel;
    }

    public static Button Make(string text, bool chosen)
    {
        var button = new Button
        {
            Content = text,
            Margin = new Thickness(0, 0, 8, 6),
            BorderBrush = Ui.Brush("#2E2820"),
            CornerRadius = new CornerRadius(8),
        };
        Paint(button, chosen);
        return button;
    }

    public static void Paint(Button button, bool chosen)
    {
        button.Background = chosen ? Ui.Brass : Brushes.Transparent;
        button.Foreground = chosen ? Ui.Brush("#17130E") : Ui.Text;
        button.BorderThickness = new Thickness(chosen ? 0 : 1);
    }
}
