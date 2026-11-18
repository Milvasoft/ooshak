using System.Runtime.InteropServices;

namespace Ooshak.Core.Mac;

/// <summary>Thin bindings to the macOS Accessibility API and the Core Foundation types it returns.</summary>
public static class MacAccessibility
{
    private const string ApplicationServices = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    private static readonly Lazy<IntPtr> CoreFoundationHandle = new(() => NativeLibrary.Load(CoreFoundation));
    private static readonly Lazy<IntPtr> ApplicationServicesHandle = new(() => NativeLibrary.Load(ApplicationServices));

    public static IntPtr True => Marshal.ReadIntPtr(NativeLibrary.GetExport(CoreFoundationHandle.Value, "kCFBooleanTrue"));

    /// <summary>
    /// True when Ooshak may read and drive other apps. With <paramref name="prompt"/> macOS shows its
    /// own dialog that leads to System Settings, Privacy and Security, Accessibility.
    /// </summary>
    public static bool IsTrusted(bool prompt)
    {
        if (!prompt)
            return AXIsProcessTrustedWithOptions(IntPtr.Zero);

        var key = Marshal.ReadIntPtr(NativeLibrary.GetExport(ApplicationServicesHandle.Value, "kAXTrustedCheckOptionPrompt"));
        var keyCallbacks = NativeLibrary.GetExport(CoreFoundationHandle.Value, "kCFTypeDictionaryKeyCallBacks");
        var valueCallbacks = NativeLibrary.GetExport(CoreFoundationHandle.Value, "kCFTypeDictionaryValueCallBacks");

        var keys = new[] { key };
        var values = new[] { True };
        var options = CFDictionaryCreate(IntPtr.Zero, keys, values, 1, keyCallbacks, valueCallbacks);
        try
        {
            return AXIsProcessTrustedWithOptions(options);
        }
        finally
        {
            CFRelease(options);
        }
    }

    public static IntPtr CreateString(string value)
    {
        var chars = value.ToCharArray();
        return CFStringCreateWithCharacters(IntPtr.Zero, chars, chars.Length);
    }

    public static string? ReadString(IntPtr value)
    {
        if (value == IntPtr.Zero || CFGetTypeID(value) != CFStringGetTypeID())
            return null;

        var length = CFStringGetLength(value);
        var buffer = new char[length];
        CFStringGetCharacters(value, new CFRange(0, length), buffer);
        return new string(buffer);
    }

    public static bool IsArray(IntPtr value) => value != IntPtr.Zero && CFGetTypeID(value) == CFArrayGetTypeID();

    public static bool IsBoolean(IntPtr value) => value != IntPtr.Zero && CFGetTypeID(value) == CFBooleanGetTypeID();

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct CFRange(nint location, nint length)
    {
        public readonly nint Location = location;
        public readonly nint Length = length;
    }

    [DllImport(ApplicationServices)]
    public static extern IntPtr AXUIElementCreateApplication(int pid);

    [DllImport(ApplicationServices)]
    public static extern int AXUIElementCopyAttributeValue(IntPtr element, IntPtr attribute, out IntPtr value);

    [DllImport(ApplicationServices)]
    public static extern int AXUIElementSetAttributeValue(IntPtr element, IntPtr attribute, IntPtr value);

    [DllImport(ApplicationServices)]
    public static extern int AXUIElementPerformAction(IntPtr element, IntPtr action);

    [DllImport(ApplicationServices)]
    public static extern int AXUIElementSetMessagingTimeout(IntPtr element, float seconds);

    [DllImport(ApplicationServices)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool AXIsProcessTrustedWithOptions(IntPtr options);

    [DllImport(CoreFoundation)]
    public static extern void CFRelease(IntPtr value);

    [DllImport(CoreFoundation)]
    public static extern IntPtr CFRetain(IntPtr value);

    [DllImport(CoreFoundation)]
    public static extern nuint CFGetTypeID(IntPtr value);

    [DllImport(CoreFoundation)]
    private static extern nuint CFStringGetTypeID();

    [DllImport(CoreFoundation)]
    private static extern nuint CFArrayGetTypeID();

    [DllImport(CoreFoundation)]
    private static extern nuint CFBooleanGetTypeID();

    [DllImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool CFBooleanGetValue(IntPtr value);

    [DllImport(CoreFoundation)]
    public static extern nint CFArrayGetCount(IntPtr array);

    [DllImport(CoreFoundation)]
    public static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, nint index);

    [DllImport(CoreFoundation, CharSet = CharSet.Unicode)]
    private static extern IntPtr CFStringCreateWithCharacters(IntPtr allocator, char[] chars, nint length);

    [DllImport(CoreFoundation)]
    private static extern nint CFStringGetLength(IntPtr value);

    [DllImport(CoreFoundation, CharSet = CharSet.Unicode)]
    private static extern void CFStringGetCharacters(IntPtr value, CFRange range, [Out] char[] buffer);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFDictionaryCreate(IntPtr allocator, IntPtr[] keys, IntPtr[] values, nint count,
        IntPtr keyCallbacks, IntPtr valueCallbacks);

    [DllImport(CoreGraphics)]
    public static extern IntPtr CGEventCreateKeyboardEvent(IntPtr source, ushort key, [MarshalAs(UnmanagedType.U1)] bool down);

    [DllImport(CoreGraphics)]
    public static extern void CGEventSetFlags(IntPtr evt, ulong flags);

    [DllImport(CoreGraphics)]
    public static extern void CGEventPostToPid(int pid, IntPtr evt);
}

/// <summary>One accessibility element, retained until disposed.</summary>
public sealed class AxElement : IDisposable
{
    private static readonly Dictionary<string, IntPtr> Names = [];
    private IntPtr _handle;
    private string? _role;

    private AxElement(IntPtr handle) => _handle = handle;

    public IntPtr Handle => _handle;

    /// <summary>AXRole, read once: tree searches ask for it on every element.</summary>
    public string Role => _role ??= String("AXRole") ?? "";

    public static AxElement Application(int pid)
    {
        var element = new AxElement(MacAccessibility.AXUIElementCreateApplication(pid));
        MacAccessibility.AXUIElementSetMessagingTimeout(element._handle, 1.0f);
        return element;
    }

    /// <summary>Interned attribute and action names; Core Foundation strings are cheap to keep.</summary>
    public static IntPtr Name(string name)
    {
        lock (Names)
        {
            if (!Names.TryGetValue(name, out var value))
                Names[name] = value = MacAccessibility.CreateString(name);
            return value;
        }
    }

    public string? String(string attribute)
    {
        if (MacAccessibility.AXUIElementCopyAttributeValue(_handle, Name(attribute), out var value) != 0)
            return null;

        try
        {
            return MacAccessibility.ReadString(value);
        }
        finally
        {
            MacAccessibility.CFRelease(value);
        }
    }

    public bool Bool(string attribute)
    {
        if (MacAccessibility.AXUIElementCopyAttributeValue(_handle, Name(attribute), out var value) != 0)
            return false;

        try
        {
            return MacAccessibility.IsBoolean(value) && MacAccessibility.CFBooleanGetValue(value);
        }
        finally
        {
            MacAccessibility.CFRelease(value);
        }
    }

    public List<string> Strings(string attribute)
    {
        var result = new List<string>();
        if (MacAccessibility.AXUIElementCopyAttributeValue(_handle, Name(attribute), out var value) != 0)
            return result;

        try
        {
            if (MacAccessibility.IsArray(value))
            {
                var count = MacAccessibility.CFArrayGetCount(value);
                for (nint i = 0; i < count; i++)
                {
                    if (MacAccessibility.ReadString(MacAccessibility.CFArrayGetValueAtIndex(value, i)) is { } text)
                        result.Add(text);
                }
            }
        }
        finally
        {
            MacAccessibility.CFRelease(value);
        }

        return result;
    }

    public AxElement? Element(string attribute)
    {
        if (MacAccessibility.AXUIElementCopyAttributeValue(_handle, Name(attribute), out var value) != 0 || value == IntPtr.Zero)
            return null;

        return new AxElement(value);
    }

    public List<AxElement> Elements(string attribute)
    {
        var result = new List<AxElement>();
        if (MacAccessibility.AXUIElementCopyAttributeValue(_handle, Name(attribute), out var value) != 0)
            return result;

        try
        {
            if (MacAccessibility.IsArray(value))
            {
                var count = MacAccessibility.CFArrayGetCount(value);
                for (nint i = 0; i < count; i++)
                    result.Add(new AxElement(MacAccessibility.CFRetain(MacAccessibility.CFArrayGetValueAtIndex(value, i))));
            }
        }
        finally
        {
            MacAccessibility.CFRelease(value);
        }

        return result;
    }

    public bool SetString(string attribute, string text)
    {
        var value = MacAccessibility.CreateString(text);
        try
        {
            return MacAccessibility.AXUIElementSetAttributeValue(_handle, Name(attribute), value) == 0;
        }
        finally
        {
            MacAccessibility.CFRelease(value);
        }
    }

    public bool SetTrue(string attribute) =>
        MacAccessibility.AXUIElementSetAttributeValue(_handle, Name(attribute), MacAccessibility.True) == 0;

    public bool Perform(string action) => MacAccessibility.AXUIElementPerformAction(_handle, Name(action)) == 0;

    public void Dispose()
    {
        if (_handle != IntPtr.Zero)
            MacAccessibility.CFRelease(_handle);
        _handle = IntPtr.Zero;
    }
}
