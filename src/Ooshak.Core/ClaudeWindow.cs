using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

namespace Ooshak.Core;

public enum DeliveryStatus
{
    Sent,
    WindowNotFound,
    UserIsTyping,
    SessionNotInSidebar,
    SwitchTimedOut,
    PromptNotFound,
    TargetHasDraft,
    NoSendButton,
    NotConfirmed,
}

/// <summary>
/// Drives the Claude desktop window through UI Automation, so text reaches a session
/// without taking keyboard focus from whatever the user is doing.
/// </summary>
public sealed class ClaudeWindow : IDisposable
{
    private const string DocumentSuffix = " - Claude Code";
    private const string PromptName = "Prompt";
    private const string PromptClass = "ProseMirror";
    private const string FocusedClass = "ProseMirror-focused";
    private static readonly TimeSpan SwitchTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ElementTimeout = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    private readonly UIA3Automation _automation = new();

    public AutomationElement? Find()
    {
        var process = Process.GetProcessesByName("claude")
            .FirstOrDefault(p => p.MainWindowTitle == "Claude" && p.MainWindowHandle != IntPtr.Zero);
        return process is null ? null : _automation.FromHandle(process.MainWindowHandle);
    }

    /// <summary>Sends text to one named session and optionally returns the view to where it was.</summary>
    public DeliveryStatus Deliver(string title, string text, bool restoreView, string? localId = null)
    {
        var window = Find();
        if (window is null)
            return DeliveryStatus.WindowNotFound;

        var previous = ShownTitle(window);

        if (previous != title)
        {
            if (IsUserTyping(window))
                return DeliveryStatus.UserIsTyping;

            var switched = Show(window, title, localId);
            if (switched != DeliveryStatus.Sent)
                return switched;
        }

        try
        {
            return Send(window, text);
        }
        finally
        {
            if (restoreView && previous is not null && previous != title)
                Show(window, previous);
        }
    }

    /// <summary>Reads the on-screen session, its prompt text and whether Send is offered.</summary>
    public (string? Shown, string? Prompt, bool HasSend) ReadState()
    {
        var window = Find();
        if (window is null)
            return (null, null, false);

        var prompt = FindPrompt(window);
        return (ShownTitle(window), prompt is null ? null : ReadValue(prompt), FindSend(window) is not null);
    }

    /// <summary>Empties the prompt of the session on screen when it holds exactly the given text.</summary>
    public bool ClearIfEquals(string text)
    {
        var window = Find();
        var prompt = window is null ? null : FindPrompt(window);
        if (prompt is null || ReadValue(prompt) != text.Trim())
            return false;

        prompt.Patterns.Value.Pattern.SetValue("");
        return true;
    }

    private static DeliveryStatus Send(AutomationElement window, string text)
    {
        var prompt = WaitFor(() => FindPrompt(window), ElementTimeout);
        if (prompt is null)
            return DeliveryStatus.PromptNotFound;

        // A draft belongs to the user: merging speech into it would send their half-written text.
        var draft = ReadValue(prompt);
        if (draft.Length > 0 && draft != text.Trim())
            return DeliveryStatus.TargetHasDraft;

        if (draft.Length == 0)
            prompt.Patterns.Value.Pattern.SetValue(text);

        var send = WaitFor(() => FindSend(window), ElementTimeout);
        if (send is null)
        {
            // Never leave our text behind where the user could send it by accident.
            if (FindPrompt(window) is { } box)
                box.Patterns.Value.Pattern.SetValue("");
            return DeliveryStatus.NoSendButton;
        }

        send.AsButton().Invoke();

        var cleared = WaitUntil(() => FindPrompt(window) is { } p && ReadValue(p).Length == 0, ElementTimeout);
        return cleared ? DeliveryStatus.Sent : DeliveryStatus.NotConfirmed;
    }

    // The app rebuilds its tree as it renders, so an element found a moment ago can be gone when read.
    private static string NameOf(AutomationElement element)
    {
        try
        {
            return Normalize(element.Properties.Name.ValueOrDefault);
        }
        catch (COMException)
        {
            return "";
        }
    }

    private static string ClassOf(AutomationElement element)
    {
        try
        {
            return element.Properties.ClassName.ValueOrDefault ?? "";
        }
        catch (COMException)
        {
            return "";
        }
    }

    private static DeliveryStatus Show(AutomationElement window, string title, string? localId = null)
    {
        var entry = FindSidebarEntry(window, title);
        if (entry is not null)
            entry.AsButton().Invoke();
        else if (localId is null || !OpenByLink(localId))
            return DeliveryStatus.SessionNotInSidebar;

        if (!WaitUntil(() => ShownTitle(window) == title, SwitchTimeout))
            return DeliveryStatus.SwitchTimedOut;

        return DeliveryStatus.Sent;
    }

    // Nested or collapsed sessions have no sidebar entry. The link reaches them but can raise the window.
    private static bool OpenByLink(string localId)
    {
        try
        {
            Process.Start(new ProcessStartInfo($"claude://claude.ai/epitaxy/{localId}") { UseShellExecute = true });
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    private static bool IsUserTyping(AutomationElement window)
    {
        var prompt = FindPrompt(window);
        if (prompt is null)
            return false;

        return ClassOf(prompt).Contains(FocusedClass, StringComparison.Ordinal) && ReadValue(prompt).Length > 0;
    }

    /// <summary>Title of the session on screen, or null when no Code session is shown.</summary>
    public static string? ShownTitle(AutomationElement window)
    {
        foreach (var doc in window.FindAllDescendants(cf => cf.ByControlType(ControlType.Document)))
        {
            var name = NameOf(doc);
            if (name.EndsWith(DocumentSuffix, StringComparison.Ordinal))
                return name[..^DocumentSuffix.Length];
        }
        return null;
    }

    // The browser pane exposes its own inputs in the same tree, so the prompt is matched by name and editor class.
    private static AutomationElement? FindPrompt(AutomationElement window) =>
        window.FindAllDescendants(cf => cf.ByControlType(ControlType.Edit))
            .FirstOrDefault(e => NameOf(e) == PromptName && ClassOf(e).Contains(PromptClass, StringComparison.Ordinal));

    private static AutomationElement? FindSend(AutomationElement window) =>
        window.FindAllDescendants(cf => cf.ByControlType(ControlType.Button))
            .FirstOrDefault(e => NameOf(e) == "Send");

    private static AutomationElement? FindSidebarEntry(AutomationElement window, string title) =>
        window.FindAllDescendants(cf => cf.ByControlType(ControlType.Button))
            .FirstOrDefault(e => NameOf(e) == title);

    private static string ReadValue(AutomationElement element) =>
        (element.Patterns.Value.Pattern.Value.ValueOrDefault ?? "").Trim();

    private static AutomationElement? WaitFor(Func<AutomationElement?> find, TimeSpan timeout)
    {
        AutomationElement? found = null;
        WaitUntil(() => (found = find()) is not null, timeout);
        return found;
    }

    private static bool WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < timeout)
        {
            if (condition())
                return true;
            Thread.Sleep(PollInterval);
        }
        return condition();
    }

    public static string Normalize(string? value) =>
        string.Join(' ', (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public void Dispose() => _automation.Dispose();
}
