using System.Diagnostics;
using Ooshak.Core;

namespace Ooshak.Tests;

public class SpeechGateTests
{
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(100);

    [Fact]
    public async Task A_quiet_room_does_not_delay_the_reply()
    {
        var watch = Stopwatch.StartNew();
        await SpeechGate.WaitAsync(() => false, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), default);
        Assert.True(watch.ElapsedMilliseconds < 1000);
    }

    [Fact]
    public async Task The_reply_waits_until_the_user_has_been_quiet()
    {
        var watch = Stopwatch.StartNew();
        await SpeechGate.WaitAsync(() => watch.ElapsedMilliseconds < 300, Quiet, TimeSpan.FromSeconds(5), default);

        // The gate polls every 25 ms, so the last "still talking" sample can come up to one poll before 300 ms.
        Assert.InRange(watch.ElapsedMilliseconds, 360, 3000);
    }

    [Fact]
    public async Task Endless_noise_cannot_hold_the_reply_forever()
    {
        var watch = Stopwatch.StartNew();
        await SpeechGate.WaitAsync(() => true, Quiet, TimeSpan.FromMilliseconds(300), default);
        Assert.InRange(watch.ElapsedMilliseconds, 250, 3000);
    }
}
