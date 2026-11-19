using System.Runtime.InteropServices;

namespace NotchApp.Interop;

internal static class Native
{
    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TOOLWINDOW = 0x00000080;   // no taskbar button, no Alt+Tab
    public const long WS_EX_NOACTIVATE = 0x08000000;   // clicking the notch never steals focus
    public const long WS_EX_TRANSPARENT = 0x00000020;  // every click passes through to the window below

    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010, SWP_SHOWWINDOW = 0x0040;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT p);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    public const int WM_CLIPBOARDUPDATE = 0x031D;

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);
    public const int VK_LBUTTON = 0x01, VK_RBUTTON = 0x02;

    // QUNS_BUSY (2) = a full-screen app, QUNS_RUNNING_D3D_FULL_SCREEN (3) = a game, QUNS_PRESENTATION_MODE (4)
    [DllImport("shell32.dll")]
    public static extern int SHQueryUserNotificationState(out int state);

    [DllImport("kernel32.dll")]
    public static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);

    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000, PROCESS_SET_QUOTA = 0x0100;

    [DllImport("kernel32.dll")]
    public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll")]
    public static extern bool CloseHandle(IntPtr h);

    [DllImport("psapi.dll")]
    public static extern bool EmptyWorkingSet(IntPtr hProcess);
}

// ---------- Core Audio: master volume of the default output ----------

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumeratorComObject { }

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
    [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
}

[ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolume
{
    [PreserveSig] int RegisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
    [PreserveSig] int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
    [PreserveSig] int GetChannelCount(out uint count);
    [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid context);
    [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
    [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
    [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
    [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid context);
    [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
    [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
    [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
    [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
    [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
}

[ComImport, Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolumeCallback
{
    [PreserveSig] int OnNotify(IntPtr notifyData);
}

/// <summary>Windows calls this (on its own thread) whenever the endpoint's volume or mute changes.</summary>
[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class VolumeCallback : IAudioEndpointVolumeCallback
{
    private readonly Action _changed;
    internal VolumeCallback(Action changed) => _changed = changed;
    int IAudioEndpointVolumeCallback.OnNotify(IntPtr notifyData) { try { _changed(); } catch { } return 0; }
}

/// <summary>
/// Reads and sets the volume of the default output without showing the Windows volume flyout. Windows tells us when
/// the volume or mute changes (from anywhere: keys, the flyout, other apps) and when the default output changes
/// (headphones connecting), so nothing polls.
/// </summary>
internal sealed class SystemVolume
{
    private readonly IMMDeviceEnumerator? _en;
    private readonly VolumeCallback _cb;
    private IAudioEndpointVolume? _ep;

    /// <summary>Volume or mute changed. Raised on a background thread.</summary>
    public event Action? Changed;
    /// <summary>The default output changed; call <see cref="Rebind"/> on the UI thread. Raised on a background thread.</summary>
    public event Action? OutputChanged;
    /// <summary>The default output's name, e.g. "Headphones (WH-1000XM4)", once <see cref="RefreshNameAsync"/> has run.</summary>
    public string? OutputName { get; private set; }

    public SystemVolume()
    {
        _cb = new VolumeCallback(() => Changed?.Invoke());
        try { _en = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject(); }
        catch { _en = null; }
        Rebind();
        try { Windows.Media.Devices.MediaDevice.DefaultAudioRenderDeviceChanged += (_, _) => OutputChanged?.Invoke(); } catch { }
    }

    /// <summary>Attach to the current default output.</summary>
    public void Rebind()
    {
        try { _ep?.UnregisterControlChangeNotify(_cb); } catch { }
        _ep = null;
        try
        {
            if (_en == null || _en.GetDefaultAudioEndpoint(0 /* render */, 1 /* multimedia */, out var dev) != 0) return;
            var iid = typeof(IAudioEndpointVolume).GUID;
            if (dev.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out var o) != 0) return;
            _ep = (IAudioEndpointVolume)o;
            _ep.RegisterControlChangeNotify(_cb);
        }
        catch { _ep = null; }
    }

    public async Task<string?> RefreshNameAsync()
    {
        try
        {
            var id = Windows.Media.Devices.MediaDevice.GetDefaultAudioRenderId(Windows.Media.Devices.AudioDeviceRole.Default);
            OutputName = string.IsNullOrEmpty(id) ? null : (await Windows.Devices.Enumeration.DeviceInformation.CreateFromIdAsync(id)).Name;
        }
        catch { OutputName = null; }
        return OutputName;
    }

    public bool Available => _ep != null;

    public double Level
    {
        get { try { if (_ep == null) return 0; _ep.GetMasterVolumeLevelScalar(out var v); return v; } catch { return 0; } }
        set { try { if (_ep == null) return; var g = Guid.Empty; _ep.SetMasterVolumeLevelScalar((float)Math.Clamp(value, 0, 1), ref g); } catch { } }
    }

    public bool Muted
    {
        get { try { if (_ep == null) return false; _ep.GetMute(out var m); return m; } catch { return false; } }
        set { try { if (_ep == null) return; var g = Guid.Empty; _ep.SetMute(value, ref g); } catch { } }
    }
}
