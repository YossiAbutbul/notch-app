using System.Diagnostics;
using NotchApp.Interop;
using Windows.System.Power;

namespace NotchApp.Services;

/// <summary>CPU, memory and battery. Sample() is cheap; call it about once a second.</summary>
public sealed class SystemStats
{
    private long _idle, _kernel, _user;
    public double Cpu { get; private set; }
    public double MemUsedGb { get; private set; }
    public double MemTotalGb { get; private set; }
    public readonly double[] CpuHistory = new double[40];

    public int? BatteryPercent => PowerManager.BatteryStatus == BatteryStatus.NotPresent ? null : PowerManager.RemainingChargePercent;
    public bool Charging => PowerManager.PowerSupplyStatus != PowerSupplyStatus.NotPresent && PowerManager.BatteryStatus != BatteryStatus.Discharging;

    /// <summary>Raised (on a background thread) when a charger is plugged in or removed.</summary>
    public event Action<bool>? ChargerChanged;

    public SystemStats()
    {
        Native.GetSystemTimes(out _idle, out _kernel, out _user);
        try { PowerManager.PowerSupplyStatusChanged += (_, _) => ChargerChanged?.Invoke(PowerManager.PowerSupplyStatus != PowerSupplyStatus.NotPresent); }
        catch { }
        Sample();
    }

    public void Sample()
    {
        if (Native.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            long di = idle - _idle, dk = kernel - _kernel, du = user - _user;
            long total = dk + du;   // kernel time includes idle time
            if (total > 0) Cpu = Math.Clamp((total - di) * 100.0 / total, 0, 100);
            _idle = idle; _kernel = kernel; _user = user;
        }
        Array.Copy(CpuHistory, 1, CpuHistory, 0, CpuHistory.Length - 1);
        CpuHistory[^1] = Cpu;

        var m = new Native.MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.MEMORYSTATUSEX>() };
        if (Native.GlobalMemoryStatusEx(ref m))
        {
            MemTotalGb = m.ullTotalPhys / 1073741824.0;
            MemUsedGb = (m.ullTotalPhys - m.ullAvailPhys) / 1073741824.0;
        }
    }

    /// <summary>
    /// Asks every process we're allowed to touch to give back pages it isn't using (EmptyWorkingSet).
    /// Windows already manages memory well, so expect modest gains; pages come back as apps need them.
    /// </summary>
    public static async Task<double> FreeMemoryAsync()
    {
        return await Task.Run(() =>
        {
            var before = Avail();
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    var h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION | Native.PROCESS_SET_QUOTA, false, p.Id);
                    if (h != IntPtr.Zero) { Native.EmptyWorkingSet(h); Native.CloseHandle(h); }
                }
                catch { }
                finally { p.Dispose(); }
            }
            Thread.Sleep(400);
            return Math.Max(0, (Avail() - before) / 1073741824.0);
        });

        static double Avail()
        {
            var m = new Native.MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.MEMORYSTATUSEX>() };
            return Native.GlobalMemoryStatusEx(ref m) ? m.ullAvailPhys : 0;
        }
    }
}
