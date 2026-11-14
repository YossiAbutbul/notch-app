using System.Runtime.InteropServices;

namespace NotchApp.Services;

/// <summary>
/// Whether Windows "Do not disturb" (Focus) is on. Windows has no public API for this, so it reads the same
/// system state the notification center listens to (the "quiet hours" profile: 0 off, 1 priority only, 2 alarms only).
/// If Windows ever stops exposing it, this reports "off" and Notch simply behaves as before.
/// </summary>
public static class QuietMode
{
    private const ulong WNF_SHEL_QUIETHOURS_ACTIVE_PROFILE_CHANGED = 0x0D83063EA3BF1C75;

    [DllImport("ntdll.dll")]
    private static extern int NtQueryWnfStateData(ref ulong stateName, IntPtr typeId, IntPtr explicitScope, out uint changeStamp, byte[] buffer, ref uint bufferSize);

    public static int? Profile()
    {
        try
        {
            ulong name = WNF_SHEL_QUIETHOURS_ACTIVE_PROFILE_CHANGED;
            var buf = new byte[4];
            uint size = 4;
            if (NtQueryWnfStateData(ref name, IntPtr.Zero, IntPtr.Zero, out _, buf, ref size) != 0 || size < 4) return null;
            return BitConverter.ToInt32(buf, 0);
        }
        catch { return null; }
    }

    public static bool On => Profile() is > 0;
}
