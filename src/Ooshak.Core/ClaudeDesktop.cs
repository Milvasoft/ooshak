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

/// <summary>The Claude desktop window, driven through the platform's accessibility interface.</summary>
public interface IClaudeDesktop : IDisposable
{
    /// <summary>Sends text to one named session and optionally returns the view to where it was.</summary>
    DeliveryStatus Deliver(string title, string text, bool restoreView, string? localId = null);

    /// <summary>Reads the on-screen session, its prompt text and whether Send is offered.</summary>
    (string? Shown, string? Prompt, bool HasSend) ReadState();

    /// <summary>Empties the prompt of the session on screen when it holds exactly the given text.</summary>
    bool ClearIfEquals(string text);
}

public static class ClaudeDesktop
{
    public static IClaudeDesktop Open() =>
#if WINDOWS
        new ClaudeWindow();
#else
        new Mac.MacClaudeWindow();
#endif

    /// <summary>The address that opens a session in the desktop app, even one missing from the sidebar.</summary>
    public static string SessionLink(string localId) => $"claude://claude.ai/epitaxy/{localId}";
}

public static class ClaudeText
{
    public static string Normalize(string? value) =>
        string.Join(' ', (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
