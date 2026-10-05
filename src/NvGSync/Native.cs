// Raw binding for NvAPI's Quadro Sync (GSync) functions, none of which are
// covered by NvAPIWrapper.Net. nvapi64.dll exports exactly one real symbol,
// nvapi_QueryInterface(uint interfaceId) -> function pointer; every actual
// NvAPI_* call is obtained dynamically through it. This file replicates that
// exact mechanism (verified against NvAPIWrapper's own internal, `internal`-
// scoped DelegateFactory, which we can't call directly from outside its
// assembly) using the real interface IDs published in NVIDIA's own
// nvapi_interface.h, and struct layouts transcribed from nvapi.h.
//
// None of this has been run against a real driver -- struct sizes/versions
// are computed from these layouts via Marshal.SizeOf, which is at least
// self-consistent, but the layouts themselves (especially the hand-packed
// C bitfields, which C# has no native support for) need real hardware
// validation before being trusted.

using System.Runtime.InteropServices;
using NvAPIWrapper.Native.General;

namespace NvGSync.Native;

public sealed class GSyncApiException : Exception
{
    public Status Status { get; }

    public GSyncApiException(Status status)
        : base(NvAPIWrapper.Native.GeneralApi.GetErrorMessage(status) ?? status.ToString())
    {
        Status = status;
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct NvGSyncDeviceHandle
{
    internal readonly IntPtr MemoryAddress;
    public bool IsNull => MemoryAddress == IntPtr.Zero;
    public override string ToString() => $"NvGSyncDeviceHandle #{MemoryAddress.ToInt64()}";
}

public enum GSyncGpuTopologyConnector : uint
{
    None = 0,
    Primary = 1,
    Secondary = 2,
    Tertiary = 3,
    Quarternary = 4
}

public enum GSyncDisplaySyncState : uint
{
    Unsynced = 0,
    Slave = 1,
    Master = 2
}

public enum GSyncPolarity : uint
{
    RisingEdge = 0,
    FallingEdge = 1,
    BothEdges = 2
}

public enum GSyncVideoMode : uint
{
    None = 0,
    Ttl = 1,
    NtscPalSecam = 2,
    Hdtv = 3,
    Composite = 4
}

public enum GSyncSyncSource : uint
{
    VSync = 0,
    HouseSync = 1
}

public enum GSyncMultiplyDivideMode : uint
{
    Undefined = 0,
    Multiply = 1,
    Divide = 2
}

public enum GSyncDelayType : uint
{
    Unknown = 0,
    SyncSkew = 1,
    Startup = 2
}

public enum GSyncRJ45IO : uint
{
    Output = 0,
    Input = 1,
    Unused = 2
}

// NV_GSYNC_CAPABILITIES_V3 (the current "_VER"). bIsMulDivSupported:1 +
// reserved:31 packed into Bits -- C# has no bitfield syntax, so the raw
// uint is stored and exposed via a bool property instead.
[StructLayout(LayoutKind.Sequential)]
public struct NV_GSYNC_CAPABILITIES
{
    public uint Version;
    public uint BoardId;
    public uint Revision;
    public uint CapFlags;
    public uint ExtendedRevision;
    public uint Bits;
    public uint MaxMulDivValue;

    public bool IsMulDivSupported => (Bits & 0x1) != 0;
}

// NV_GSYNC_GPU. Handle fields are IntPtr-sized (NV_DECLARE_HANDLE is just an
// opaque pointer), so default Sequential layout should insert the same
// alignment padding a C compiler would -- not independently verified here.
[StructLayout(LayoutKind.Sequential)]
public struct NV_GSYNC_GPU
{
    public uint Version;
    public IntPtr PhysicalGpu;
    public GSyncGpuTopologyConnector Connector;
    public IntPtr ProxyPhysicalGpu;
    public uint Bits; // isSynced:1, reserved:31

    public bool IsSynced => (Bits & 0x1) != 0;
}

// NV_GSYNC_DISPLAY_V2 (the current "_VER").
[StructLayout(LayoutKind.Sequential)]
public struct NV_GSYNC_DISPLAY
{
    public uint Version;
    public uint DisplayId;
    public uint Bits; // isMasterable:1 (read-only), useExactTiming:1, reserved:30
    public GSyncDisplaySyncState SyncState;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
    public uint[] Reserved2;

    public bool IsMasterable => (Bits & 0x1) != 0;
    public bool UseExactTiming => (Bits & 0x2) != 0;

    public static NV_GSYNC_DISPLAY Create(uint displayId, GSyncDisplaySyncState syncState, bool useExactTiming = false)
    {
        return new NV_GSYNC_DISPLAY
        {
            Version = VersionHelper.Make<NV_GSYNC_DISPLAY>(2),
            DisplayId = displayId,
            SyncState = syncState,
            Bits = useExactTiming ? 0x2u : 0u,
            Reserved2 = new uint[20]
        };
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct NV_GSYNC_DELAY
{
    public uint Version;
    public uint NumLines;
    public uint NumPixels;
    public uint MaxLines;  // output only
    public uint MinPixels; // output only
}

// NV_GSYNC_CONTROL_PARAMS_V2 (the current "_VER"). Trailing single-byte
// MultiplyDivideValue is expected to pick up the same trailing padding a C
// compiler would add to round the struct up to 4-byte alignment -- again,
// relying on CLR default layout behavior, not independently verified.
[StructLayout(LayoutKind.Sequential)]
public struct NV_GSYNC_CONTROL_PARAMS
{
    public uint Version;
    public GSyncPolarity Polarity;
    public GSyncVideoMode VMode;
    public uint Interval;
    public GSyncSyncSource Source;
    public uint Bits; // interlaceMode:1, syncSourceIsOutput:1, reserved:30
    public NV_GSYNC_DELAY SyncSkew;
    public NV_GSYNC_DELAY StartupDelay;
    public GSyncMultiplyDivideMode MultiplyDivideMode;
    public byte MultiplyDivideValue;

    public bool InterlaceMode => (Bits & 0x1) != 0;
    public bool SyncSourceIsOutput => (Bits & 0x2) != 0;
}

[StructLayout(LayoutKind.Sequential)]
public struct NV_GSYNC_STATUS
{
    public uint Version;
    public uint IsSynced;
    public uint IsStereoSynced;
    public uint IsSyncSignalAvailable;
}

// NV_GSYNC_STATUS_PARAMS_V2 (the current "_VER").
[StructLayout(LayoutKind.Sequential)]
public struct NV_GSYNC_STATUS_PARAMS
{
    public uint Version;
    public uint RefreshRate;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
    public uint[] RJ45_IO; // GSyncRJ45IO values

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
    public uint[] RJ45_Ethernet;

    public uint HouseSyncIncoming;
    public uint BHouseSync;
    public uint Bits; // bInternalSlave:1, reserved:31

    public bool InternalSlave => (Bits & 0x1) != 0;
}

internal static class VersionHelper
{
    // Mirrors MAKE_NVAPI_VERSION(typeName, ver): (NvU32)(sizeof(typeName) | (ver << 16))
    public static uint Make<T>(ushort version) where T : struct =>
        (uint) Marshal.SizeOf<T>() | ((uint) version << 16);
}

internal static class Delegates
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate Status EnumSyncDevicesDelegate(
        [MarshalAs(UnmanagedType.LPArray, SizeConst = 4)] NvGSyncDeviceHandle[] gsyncHandles,
        out uint gsyncCount);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate Status QueryCapabilitiesDelegate(
        NvGSyncDeviceHandle device,
        ref NV_GSYNC_CAPABILITIES capabilities);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate Status GetTopologyDelegate(
        NvGSyncDeviceHandle device,
        ref uint gsyncGpuCount,
        [MarshalAs(UnmanagedType.LPArray)] NV_GSYNC_GPU[]? gsyncGpus,
        ref uint gsyncDisplayCount,
        [MarshalAs(UnmanagedType.LPArray)] NV_GSYNC_DISPLAY[]? gsyncDisplays);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate Status SetSyncStateSettingsDelegate(
        uint gsyncDisplayCount,
        [MarshalAs(UnmanagedType.LPArray)] NV_GSYNC_DISPLAY[] gsyncDisplays,
        uint flags);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate Status GetControlParametersDelegate(
        NvGSyncDeviceHandle device,
        ref NV_GSYNC_CONTROL_PARAMS controls);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate Status SetControlParametersDelegate(
        NvGSyncDeviceHandle device,
        ref NV_GSYNC_CONTROL_PARAMS controls);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate Status AdjustSyncDelayDelegate(
        NvGSyncDeviceHandle device,
        GSyncDelayType delayType,
        ref NV_GSYNC_DELAY delay,
        out uint syncSteps);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate Status GetSyncStatusDelegate(
        NvGSyncDeviceHandle device,
        IntPtr physicalGpu,
        ref NV_GSYNC_STATUS status);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate Status GetStatusParametersDelegate(
        NvGSyncDeviceHandle device,
        ref NV_GSYNC_STATUS_PARAMS statusParams);
}

internal static class InterfaceIds
{
    // From third_party/nvapi/nvapi_interface.h -- NVIDIA's own published
    // table mapping function name to interface ID.
    public const uint EnumSyncDevices = 0xd9639601;
    public const uint QueryCapabilities = 0x44a3f1d1;
    public const uint GetTopology = 0x4562bc38;
    public const uint SetSyncStateSettings = 0x60acdfdd;
    public const uint GetControlParameters = 0x16de1c6a;
    public const uint SetControlParameters = 0x8bbff88b;
    public const uint AdjustSyncDelay = 0x2d11ff51;
    public const uint GetSyncStatus = 0xf1f5b434;
    public const uint GetStatusParameters = 0x70d404ec;
}

// Replicates NvAPIWrapper.Native.Helpers.DelegateFactory, which is `internal`
// to that assembly and so not callable from here. Only the win-x64 path is
// implemented since this project only ever publishes win-x64.
internal static class NativeDispatch
{
    private static readonly Dictionary<uint, Delegate> Cache = new();

    public static T Get<T>(uint interfaceId) where T : Delegate
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(interfaceId, out var cached))
            {
                return (T) cached;
            }

            var ptr = NvAPI64_QueryInterface(interfaceId);
            if (ptr == IntPtr.Zero)
            {
                throw new GSyncApiException(Status.NoImplementation);
            }

            var del = Marshal.GetDelegateForFunctionPointer<T>(ptr);
            Cache[interfaceId] = del;
            return del;
        }
    }

    [DllImport("nvapi64", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl,
        PreserveSig = true)]
    private static extern IntPtr NvAPI64_QueryInterface(uint interfaceId);
}

public static class GSyncApi
{
    public static NvGSyncDeviceHandle[] EnumSyncDevices()
    {
        var handles = new NvGSyncDeviceHandle[4]; // NVAPI_MAX_GSYNC_DEVICES
        var fn = NativeDispatch.Get<Delegates.EnumSyncDevicesDelegate>(InterfaceIds.EnumSyncDevices);
        var status = fn(handles, out var count);
        if (status != Status.Ok)
        {
            throw new GSyncApiException(status);
        }

        return handles.Take((int) count).ToArray();
    }

    public static NV_GSYNC_CAPABILITIES QueryCapabilities(NvGSyncDeviceHandle device)
    {
        var caps = new NV_GSYNC_CAPABILITIES { Version = VersionHelper.Make<NV_GSYNC_CAPABILITIES>(3) };
        var fn = NativeDispatch.Get<Delegates.QueryCapabilitiesDelegate>(InterfaceIds.QueryCapabilities);
        var status = fn(device, ref caps);
        if (status != Status.Ok)
        {
            throw new GSyncApiException(status);
        }

        return caps;
    }

    // Matches NVIDIA's documented two-call pattern: call once with null
    // arrays to get counts, allocate, call again to populate.
    public static (NV_GSYNC_GPU[] Gpus, NV_GSYNC_DISPLAY[] Displays) GetTopology(NvGSyncDeviceHandle device)
    {
        var fn = NativeDispatch.Get<Delegates.GetTopologyDelegate>(InterfaceIds.GetTopology);

        uint gpuCount = 0;
        uint displayCount = 0;
        var status = fn(device, ref gpuCount, null, ref displayCount, null);
        if (status != Status.Ok)
        {
            throw new GSyncApiException(status);
        }

        var gpus = new NV_GSYNC_GPU[gpuCount];
        for (var i = 0; i < gpus.Length; i++)
        {
            gpus[i].Version = VersionHelper.Make<NV_GSYNC_GPU>(1);
        }

        var displays = new NV_GSYNC_DISPLAY[displayCount];
        for (var i = 0; i < displays.Length; i++)
        {
            displays[i].Version = VersionHelper.Make<NV_GSYNC_DISPLAY>(2);
            displays[i].Reserved2 = new uint[20];
        }

        status = fn(device, ref gpuCount, gpus, ref displayCount, displays);
        if (status != Status.Ok)
        {
            throw new GSyncApiException(status);
        }

        return (gpus, displays);
    }

    // NVIDIA's own docs: requires Administrator privileges.
    public static void SetSyncStateSettings(NV_GSYNC_DISPLAY[] displays, uint flags = 0)
    {
        var fn = NativeDispatch.Get<Delegates.SetSyncStateSettingsDelegate>(InterfaceIds.SetSyncStateSettings);
        var status = fn((uint) displays.Length, displays, flags);
        if (status != Status.Ok)
        {
            throw new GSyncApiException(status);
        }
    }

    public static NV_GSYNC_CONTROL_PARAMS GetControlParameters(NvGSyncDeviceHandle device)
    {
        var p = new NV_GSYNC_CONTROL_PARAMS { Version = VersionHelper.Make<NV_GSYNC_CONTROL_PARAMS>(2) };
        var fn = NativeDispatch.Get<Delegates.GetControlParametersDelegate>(InterfaceIds.GetControlParameters);
        var status = fn(device, ref p);
        if (status != Status.Ok)
        {
            throw new GSyncApiException(status);
        }

        return p;
    }

    // NVIDIA's own docs: requires Administrator privileges. Returns the
    // struct as updated by the driver -- skew/startDelay reflect applied
    // values, which may differ slightly from what was requested.
    public static NV_GSYNC_CONTROL_PARAMS SetControlParameters(NvGSyncDeviceHandle device, NV_GSYNC_CONTROL_PARAMS parameters)
    {
        parameters.Version = VersionHelper.Make<NV_GSYNC_CONTROL_PARAMS>(2);
        var fn = NativeDispatch.Get<Delegates.SetControlParametersDelegate>(InterfaceIds.SetControlParameters);
        var status = fn(device, ref parameters);
        if (status != Status.Ok)
        {
            throw new GSyncApiException(status);
        }

        return parameters;
    }

    // NVIDIA's own docs: call this before SetControlParameters for skew or
    // startDelay, to snap requested values to the closest values the
    // hardware can actually produce.
    public static (NV_GSYNC_DELAY Delay, uint SyncSteps) AdjustSyncDelay(
        NvGSyncDeviceHandle device, GSyncDelayType delayType, NV_GSYNC_DELAY delay)
    {
        delay.Version = VersionHelper.Make<NV_GSYNC_DELAY>(1);
        var fn = NativeDispatch.Get<Delegates.AdjustSyncDelayDelegate>(InterfaceIds.AdjustSyncDelay);
        var status = fn(device, delayType, ref delay, out var steps);
        if (status != Status.Ok)
        {
            throw new GSyncApiException(status);
        }

        return (delay, steps);
    }

    // physicalGpu: pass the .MemoryAddress of an NvAPIWrapper
    // PhysicalGPUHandle (e.g. from NvAPIWrapper.Native.GPUApi.EnumPhysicalGPUs())
    // -- both are just opaque pointer-sized handles with identical layout.
    public static NV_GSYNC_STATUS GetSyncStatus(NvGSyncDeviceHandle device, IntPtr physicalGpu)
    {
        var s = new NV_GSYNC_STATUS { Version = VersionHelper.Make<NV_GSYNC_STATUS>(1) };
        var fn = NativeDispatch.Get<Delegates.GetSyncStatusDelegate>(InterfaceIds.GetSyncStatus);
        var status = fn(device, physicalGpu, ref s);
        if (status != Status.Ok)
        {
            throw new GSyncApiException(status);
        }

        return s;
    }

    public static NV_GSYNC_STATUS_PARAMS GetStatusParameters(NvGSyncDeviceHandle device)
    {
        var p = new NV_GSYNC_STATUS_PARAMS { Version = VersionHelper.Make<NV_GSYNC_STATUS_PARAMS>(2) };
        var fn = NativeDispatch.Get<Delegates.GetStatusParametersDelegate>(InterfaceIds.GetStatusParameters);
        var status = fn(device, ref p);
        if (status != Status.Ok)
        {
            throw new GSyncApiException(status);
        }

        return p;
    }
}
