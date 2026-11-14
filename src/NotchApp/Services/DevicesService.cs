using System.Diagnostics;
using System.Windows;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;
using Windows.Networking.Connectivity;

namespace NotchApp.Services;

public sealed record BtDevice(string Id, string Name, bool Connected, int? Battery, string Kind);

/// <summary>
/// Wi-Fi and Bluetooth radios (switchable), the current network, and paired Bluetooth devices with
/// connection state and battery. All events are raised on the UI thread.
/// </summary>
public sealed class DevicesService
{
    private const string ClassicBt = "{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}";
    private const string LeBt = "{bb7bb05e-5972-42b5-94fc-76eaa7084d49}";
    private const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";   // DEVPKEY_Bluetooth_Battery
    private static readonly string[] AepProps =
    [
        "System.Devices.Aep.IsConnected", "System.Devices.Aep.ContainerId",
        "System.Devices.Aep.Bluetooth.Cod.Major", "System.Devices.Aep.Bluetooth.Cod.Minor",
    ];
    // Windows' own selectors for paired classic and LE devices (association endpoints)
    private static readonly string[] PairedAqs =
    [
        Windows.Devices.Bluetooth.BluetoothDevice.GetDeviceSelectorFromPairingState(true),
        Windows.Devices.Bluetooth.BluetoothLEDevice.GetDeviceSelectorFromPairingState(true),
    ];

    private readonly System.Windows.Threading.Dispatcher _ui = Application.Current.Dispatcher;
    private Radio? _wifi, _bt;
    private readonly List<DeviceWatcher> _watchers = [];
    private readonly Dictionary<string, bool> _connected = new();
    private CancellationTokenSource? _debounce;

    public bool WifiAvailable => _wifi != null;
    public bool BtAvailable => _bt != null;
    public bool WifiOn => _wifi?.State == RadioState.On;
    public bool BtOn => _bt?.State == RadioState.On;
    public string? Network { get; private set; }
    public bool Wired { get; private set; }
    public int Bars { get; private set; }
    public IReadOnlyList<BtDevice> Devices { get; private set; } = [];

    public event Action? Changed;
    public event Action<BtDevice>? DeviceConnected;

    public async Task InitAsync()
    {
        try
        {
            await Radio.RequestAccessAsync();
            foreach (var r in await Radio.GetRadiosAsync())
            {
                if (r.Kind == RadioKind.WiFi && _wifi == null) _wifi = r;
                if (r.Kind == RadioKind.Bluetooth && _bt == null) _bt = r;
            }
            if (_wifi != null) _wifi.StateChanged += (_, _) => _ui.InvokeAsync(() => { ReadNetwork(); Changed?.Invoke(); });
            if (_bt != null) _bt.StateChanged += (_, _) => _ui.InvokeAsync(() => { Changed?.Invoke(); Refresh(); });
        }
        catch { }
        try { NetworkInformation.NetworkStatusChanged += _ => _ui.InvokeAsync(() => { ReadNetwork(); Changed?.Invoke(); }); } catch { }
        ReadNetwork();
        await RefreshDevicesAsync(initial: true);
        StartWatcher();
        Changed?.Invoke();
    }

    private void ReadNetwork()
    {
        try
        {
            var p = NetworkInformation.GetInternetConnectionProfile();
            Wired = p != null && !p.IsWlanConnectionProfile && !p.IsWwanConnectionProfile;
            Network = p == null ? null : p.IsWlanConnectionProfile ? p.WlanConnectionProfileDetails?.GetConnectedSsid() ?? p.ProfileName : p.ProfileName;
            Bars = p?.GetSignalBars() ?? 0;
        }
        catch { Network = null; }
    }

    public async Task SetWifiAsync(bool on)
    {
        if (_wifi == null) { OpenSettings("network-wifi"); return; }
        try { if (await _wifi.SetStateAsync(on ? RadioState.On : RadioState.Off) != RadioAccessStatus.Allowed) OpenSettings("network-wifi"); }
        catch { OpenSettings("network-wifi"); }
    }

    public async Task SetBluetoothAsync(bool on)
    {
        if (_bt == null) { OpenSettings("bluetooth"); return; }
        try { if (await _bt.SetStateAsync(on ? RadioState.On : RadioState.Off) != RadioAccessStatus.Allowed) OpenSettings("bluetooth"); }
        catch { OpenSettings("bluetooth"); }
    }

    public static void OpenSettings(string page)
    {
        try { Process.Start(new ProcessStartInfo($"ms-settings:{page}") { UseShellExecute = true }); } catch { }
    }

    // ---------------------------------------------------------------- Bluetooth devices

    private void StartWatcher()
    {
        foreach (var aqs in PairedAqs)
        {
            try
            {
                var w = DeviceInformation.CreateWatcher(aqs, AepProps, DeviceInformationKind.AssociationEndpoint);
                w.Added += (_, _) => Refresh();
                w.Updated += (_, _) => Refresh();
                w.Removed += (_, _) => Refresh();
                w.Start();
                _watchers.Add(w);
            }
            catch (Exception ex) { Log.Error("Bluetooth watcher", ex); }
        }
    }

    /// <summary>Coalesces bursts of watcher events into one refresh.</summary>
    private void Refresh()
    {
        _debounce?.Cancel();
        var cts = _debounce = new CancellationTokenSource();
        _ = Task.Delay(400, cts.Token).ContinueWith(t => { if (!t.IsCanceled) _ui.InvokeAsync(async () => await RefreshDevicesAsync(initial: false)); }, TaskScheduler.Default);
    }

    private async Task RefreshDevicesAsync(bool initial)
    {
        try
        {
            var found = new List<DeviceInformation>();
            foreach (var aqs in PairedAqs)
            {
                try { found.AddRange(await DeviceInformation.FindAllAsync(aqs, AepProps, DeviceInformationKind.AssociationEndpoint)); }
                catch (Exception ex) { Log.Error("Bluetooth query", ex); }
            }
            if (initial) Log.Write($"Bluetooth: {found.Count} paired endpoints found");
            var list = new List<BtDevice>();
            foreach (var d in found)
            {
                if (string.IsNullOrWhiteSpace(d.Name)) continue;
                bool conn = d.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var c) && c is true;
                int? battery = null;
                if (d.Properties.TryGetValue("System.Devices.Aep.ContainerId", out var cid) && cid is Guid g) battery = await BatteryOf(g);
                uint major = Num(d.Properties, "System.Devices.Aep.Bluetooth.Cod.Major");
                uint minor = Num(d.Properties, "System.Devices.Aep.Bluetooth.Cod.Minor");
                list.Add(new BtDevice(d.Id, d.Name, conn, battery, KindOf(d.Name, major, minor)));
            }
            // dual-mode devices appear twice (classic + LE): keep one per name, preferring the connected entry
            Devices = list.GroupBy(x => x.Name)
                          .Select(gp => gp.OrderByDescending(x => x.Connected).ThenByDescending(x => x.Battery.HasValue).First())
                          .OrderByDescending(x => x.Connected).ThenBy(x => x.Name).ToList();

            foreach (var dev in Devices)
            {
                bool was = _connected.TryGetValue(dev.Name, out var w) && w;
                if (!initial && dev.Connected && !was) DeviceConnected?.Invoke(dev);
                _connected[dev.Name] = dev.Connected;
            }
            Changed?.Invoke();
        }
        catch (Exception ex) { Log.Error("Bluetooth refresh", ex); }
    }

    private static uint Num(IReadOnlyDictionary<string, object> props, string key)
    {
        try { return props.TryGetValue(key, out var v) && v != null ? Convert.ToUInt32(v) : 0; } catch { return 0; }
    }

    private static async Task<int?> BatteryOf(Guid container)
    {
        try
        {
            var devs = await DeviceInformation.FindAllAsync($"System.Devices.ContainerId:=\"{{{container}}}\"", [BatteryKey], DeviceInformationKind.Device);
            foreach (var d in devs)
                if (d.Properties.TryGetValue(BatteryKey, out var v) && v != null) return Convert.ToInt32(v);
        }
        catch { }
        return null;
    }

    /// <summary>Icon kind from the name first (most reliable), then the Bluetooth class of device.</summary>
    private static string KindOf(string name, uint major, uint minor)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("bud") || n.Contains("pods") || n.Contains("earbud") || n.Contains("wf-") || n.Contains("ear")) return "buds";
        if (n.Contains("headphone") || n.Contains("headset") || n.Contains("wh-") || n.Contains("qc") || n.Contains("max")) return "headphones";
        if (n.Contains("keyboard") || n.Contains("keys")) return "keyboard";
        if (n.Contains("mouse") || n.Contains("mx master") || n.Contains("anywhere")) return "mouse";
        if (n.Contains("controller") || n.Contains("xbox") || n.Contains("dualsense") || n.Contains("gamepad")) return "pad";
        if (n.Contains("phone") || n.Contains("galaxy") || n.Contains("pixel")) return "phone";
        return major switch
        {
            4 => "headphones",
            2 => "phone",
            5 => ((minor >> 4) & 3) == 1 ? "keyboard" : ((minor >> 4) & 3) == 2 ? "mouse" : "pad",
            _ => "bt",
        };
    }
}
