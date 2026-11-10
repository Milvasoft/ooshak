using System.Diagnostics;

namespace Ooshak.Core;

/// <summary>Holds a reply back until the user has been quiet for a moment.</summary>
public static class SpeechGate
{
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(25);

    /// <param name="hold">True while the reply must wait. Null means never wait.</param>
    /// <param name="quiet">How long the hold must stay false, so a pause between words does not count.</param>
    /// <param name="maxWait">Upper limit, so constant background noise cannot silence replies forever.</param>
    public static async Task WaitAsync(Func<bool>? hold, TimeSpan quiet, TimeSpan maxWait, CancellationToken cancellationToken)
    {
        if (hold is null || !hold())
            return;

        var total = Stopwatch.StartNew();
        var calm = Stopwatch.StartNew();

        // The hold is checked right after every wake-up: a slow timer must not count talking as quiet.
        while (total.Elapsed < maxWait)
        {
            if (hold())
                calm.Restart();
            else if (calm.Elapsed >= quiet)
                return;

            await Task.Delay(Poll, cancellationToken);
        }
    }
}
