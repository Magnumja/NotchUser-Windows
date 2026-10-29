using System.Runtime.InteropServices;
using System.Windows;

namespace NotchUser;

/// <summary>
/// The speakers' volume, through Windows' Core Audio (the same control as the volume keys and
/// the taskbar slider). Follows the default output device when it changes, e.g. when Bluetooth
/// headphones connect.
/// </summary>
public sealed class Volume
{
    // Our own changes carry this, so the notch can tell them from the keyboard's.
    private static Guid _ours = new("6E1B7F4C-3A0D-4F7B-9C55-0D7E2B8A91C3");

    private IMMDeviceEnumerator? _enumerator;
    private IAudioEndpointVolume? _endpoint;
    private readonly VolumeCallback _callback;
    private readonly DeviceCallback _devices;
    private string? _deviceId;

    /// <summary>The volume changed (on the UI thread). True if it was the notch itself.</summary>
    public event Action<bool>? Changed;
    /// <summary>Sound now comes out somewhere else (on the UI thread): the device's name.</summary>
    public event Action<string>? DeviceChanged;

    public bool Available => _endpoint != null;
    public float Level { get; private set; }
    public bool Muted { get; private set; }

    public Volume()
    {
        _callback = new VolumeCallback(this);
        _devices = new DeviceCallback(this);
    }

    public void Start()
    {
        try
        {
            _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            _enumerator.RegisterEndpointNotificationCallback(_devices);
        }
        catch { _enumerator = null; return; }
        Attach(announce: false);
    }

    /// <summary>Listen to the current default output (and stop listening to the old one).</summary>
    private void Attach(bool announce)
    {
        if (_enumerator == null) return;
        if (_endpoint != null)
        {
            try { _endpoint.UnregisterControlChangeNotify(_callback); } catch { }
            _endpoint = null;
        }
        try
        {
            _enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out var device);
            device.GetId(out var id);
            var iid = typeof(IAudioEndpointVolume).GUID;
            device.Activate(ref iid, 1 /* CLSCTX_INPROC_SERVER */, IntPtr.Zero, out var o);
            _endpoint = (IAudioEndpointVolume)o;
            _endpoint.RegisterControlChangeNotify(_callback);
            Read();
            var changed = _deviceId != null && id != _deviceId;
            _deviceId = id;
            if (announce && changed) DeviceChanged?.Invoke(FriendlyName(device));
        }
        catch { _endpoint = null; }
        Changed?.Invoke(true);
    }

    private void Read()
    {
        if (_endpoint == null) return;
        try
        {
            _endpoint.GetMasterVolumeLevelScalar(out var level);
            _endpoint.GetMute(out var muted);
            Level = level;
            Muted = muted;
        }
        catch { }
    }

    /// <summary>0 to 1. Turning it up also unmutes, like the volume keys do.</summary>
    public void Set(float level)
    {
        if (_endpoint == null) return;
        level = Math.Clamp(level, 0, 1);
        try
        {
            _endpoint.SetMasterVolumeLevelScalar(level, ref _ours);
            if (Muted && level > 0) _endpoint.SetMute(false, ref _ours);
        }
        catch { }
        Level = level;
    }

    public void ToggleMute()
    {
        if (_endpoint == null) return;
        try { _endpoint.SetMute(!Muted, ref _ours); } catch { }
    }

    private static string FriendlyName(IMMDevice device)
    {
        try
        {
            device.OpenPropertyStore(0 /* STGM_READ */, out var store);
            var key = new PropertyKey { Format = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), Id = 14 };
            store.GetValue(ref key, out var value);
            try { return value.Type == 31 /* VT_LPWSTR */ ? Marshal.PtrToStringUni(value.Pointer) ?? "" : ""; }
            finally { PropVariantClear(ref value); }
        }
        catch { return ""; }
    }

    // Both callbacks arrive on Core Audio's own threads: hop to the UI thread, and never call
    // back into Core Audio from inside them.
    private static void OnUi(Action action) => Application.Current?.Dispatcher.BeginInvoke(action);

    internal sealed class VolumeCallback : IAudioEndpointVolumeCallback
    {
        private readonly Volume _owner;
        public VolumeCallback(Volume owner) => _owner = owner;

        public int OnNotify(IntPtr data)
        {
            var n = Marshal.PtrToStructure<VolumeNotification>(data);
            OnUi(() =>
            {
                _owner.Level = n.MasterVolume;
                _owner.Muted = n.Muted;
                _owner.Changed?.Invoke(n.EventContext == _ours);
            });
            return 0;
        }
    }

    internal sealed class DeviceCallback : IMMNotificationClient
    {
        private readonly Volume _owner;
        public DeviceCallback(Volume owner) => _owner = owner;

        public int OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? id)
        {
            // Fires once per role; the multimedia one is enough.
            if (flow == EDataFlow.Render && role == ERole.Multimedia) OnUi(() => _owner.Attach(announce: true));
            return 0;
        }
        public int OnDeviceStateChanged(string id, int state) => 0;
        public int OnDeviceAdded(string id) => 0;
        public int OnDeviceRemoved(string id) => 0;
        public int OnPropertyValueChanged(string id, PropertyKey key) => 0;
    }

    // MARK: - Core Audio interop

    internal enum EDataFlow { Render, Capture, All }
    internal enum ERole { Console, Multimedia, Communications }

    [StructLayout(LayoutKind.Sequential)]
    internal struct VolumeNotification
    {
        public Guid EventContext;
        [MarshalAs(UnmanagedType.Bool)] public bool Muted;
        public float MasterVolume;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PropertyKey
    {
        public Guid Format;
        public int Id;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PropVariant
    {
        public ushort Type;
        public ushort Reserved1, Reserved2, Reserved3;
        public IntPtr Pointer;
        public IntPtr Pointer2;
    }

    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    internal class MMDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow flow, int stateMask, out IntPtr devices);
        void GetDefaultAudioEndpoint(EDataFlow flow, ERole role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        void RegisterEndpointNotificationCallback(IMMNotificationClient client);
        void UnregisterEndpointNotificationCallback(IMMNotificationClient client);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        void Activate(ref Guid iid, int context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object result);
        void OpenPropertyStore(int access, out IPropertyStore store);
        void GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        void GetState(out int state);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
        void GetCount(out int count);
        void GetAt(int index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioEndpointVolume
    {
        void RegisterControlChangeNotify(IAudioEndpointVolumeCallback callback);
        void UnregisterControlChangeNotify(IAudioEndpointVolumeCallback callback);
        void GetChannelCount(out int count);
        void SetMasterVolumeLevel(float levelDb, ref Guid context);
        void SetMasterVolumeLevelScalar(float level, ref Guid context);
        void GetMasterVolumeLevel(out float levelDb);
        void GetMasterVolumeLevelScalar(out float level);
        void SetChannelVolumeLevel(int channel, float levelDb, ref Guid context);
        void SetChannelVolumeLevelScalar(int channel, float level, ref Guid context);
        void GetChannelVolumeLevel(int channel, out float levelDb);
        void GetChannelVolumeLevelScalar(int channel, out float level);
        void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    [ComImport, Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioEndpointVolumeCallback
    {
        [PreserveSig] int OnNotify(IntPtr data);
    }

    [ComImport, Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMNotificationClient
    {
        [PreserveSig] int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string id, int state);
        [PreserveSig] int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnDefaultDeviceChanged(EDataFlow flow, ERole role, [MarshalAs(UnmanagedType.LPWStr)] string? id);
        [PreserveSig] int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string id, PropertyKey key);
    }
}
