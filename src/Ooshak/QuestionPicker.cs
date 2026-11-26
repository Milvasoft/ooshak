using System.Windows;
using System.Windows.Controls;
using Ooshak.Core;

namespace Ooshak;

/// <summary>
/// Turns a session's question into buttons. A single question with one answer is sent on the
/// first click; several questions or multiple choice are picked first and sent with Answer.
/// </summary>
public static class QuestionPicker
{
    public static Panel Build(FrameworkElement resources, PermissionRequest request, PermissionBroker broker,
        string optionStyle, string chosenStyle, bool showQuestionText)
    {
        var panel = new StackPanel();
        var instant = request.Questions.Count == 1 && !request.Questions[0].MultiSelect;
        var chosen = request.Questions.ToDictionary(q => q.Question, _ => new List<string>());
        var send = Make(resources, L.T("Cevapla"), chosenStyle);
        send.IsEnabled = false;

        foreach (var question in request.Questions)
        {
            if (showQuestionText)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = question.Question,
                    Style = (Style)resources.FindResource("Body"),
                    Margin = new Thickness(0, panel.Children.Count == 0 ? 0 : 12, 0, 8),
                });
            }

            var options = new WrapPanel();
            foreach (var option in question.Options)
            {
                var button = Make(resources, option.Label, optionStyle);
                if (option.Description.Length > 0)
                    button.ToolTip = option.Description;

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
                            sibling.Style = (Style)resources.FindResource(optionStyle);
                    }

                    if (picks.Remove(option.Label))
                    {
                        button.Style = (Style)resources.FindResource(optionStyle);
                    }
                    else
                    {
                        picks.Add(option.Label);
                        button.Style = (Style)resources.FindResource(chosenStyle);
                    }

                    send.IsEnabled = chosen.Values.All(p => p.Count > 0);
                };
                options.Children.Add(button);
            }

            panel.Children.Add(options);
        }

        var footer = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        if (!instant)
        {
            send.Click += (_, _) => broker.AnswerQuestions(request, chosen.ToDictionary(c => c.Key, c => string.Join(", ", c.Value)));
            footer.Children.Add(send);
        }

        var handOff = Make(resources, L.T("Claude'da cevapla"), optionStyle);
        handOff.ToolTip = L.T("Soruyu Claude'un kendi penceresine bırakır.");
        handOff.Click += (_, _) => broker.HandOff(request);
        footer.Children.Add(handOff);
        panel.Children.Add(footer);
        return panel;
    }

    private static Button Make(FrameworkElement resources, string text, string style) => new()
    {
        Content = text,
        Style = (Style)resources.FindResource(style),
        Margin = new Thickness(0, 0, 8, 8),
        Height = 34,
    };
}
