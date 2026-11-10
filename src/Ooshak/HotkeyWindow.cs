using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Ooshak;

internal static partial class NativeMethods
{
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnregisterHotKey(IntPtr hWnd, int id);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyIcon(IntPtr handle);
}

/// <summary>Hidden window that receives global hotkeys.</summary>
internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x1;
    private const uint ModControl = 0x2;
    private const uint ModNoRepeat = 0x4000;

    private readonly List<int> _registered = [];

    public HotkeyWindow() => CreateHandle(new CreateParams());

    public event Action<int>? Pressed;

    public void Register(int id, Keys key)
    {
        if (NativeMethods.RegisterHotKey(Handle, id, ModControl | ModAlt | ModNoRepeat, (uint)key))
            _registered.Add(id);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey)
            Pressed?.Invoke((int)m.WParam);

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        foreach (var id in _registered)
            NativeMethods.UnregisterHotKey(Handle, id);

        DestroyHandle();
    }
}
