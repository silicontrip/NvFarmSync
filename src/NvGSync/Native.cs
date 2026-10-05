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
    internal NvGSyncDeviceHandle(IntPtr memoryAddress) => MemoryAddress = memoryAddress;
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

// Older capability shapes. Fallback chain tries V3 -> V2 -> V1 on
// IncompatibleStructureVersion (see GSyncApi.QueryCapabilities).
[StructLayout(LayoutKind.Sequential)]
public struct NV_GSYNC_CAPABILITIES_V2
{
    public uint Version;
    public uint BoardId;
    public uint Revision;
    public uint CapFlags;
    public uint ExtendedRevision;
}

[StructLayout(LayoutKind.Sequential)]
public struct NV_GSYNC_CAPABILITIES_V1
{
    public uint Version;
    public uint BoardId;
    public uint Revision;
    public uint CapFlags;
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

// NV_GSYNC_DISPLAY_V1 -- lacks useExactTiming and the reserved2[20] tail.
// Some boards/drivers reject V2 with IncompatibleStructureVersion; this is
// the fallback shape (see GSyncApi.GetTopology/SetSyncStateSettings).
[StructLayout(LayoutKind.Sequential)]
public struct NV_GSYNC_DISPLAY_V1
{
    public uint Version;
    public uint DisplayId;
    public uint Bits; // isMasterable:1 (read-only), reserved:31
    public GSyncDisplaySyncState SyncState;

    public bool IsMasterable => (Bits & 0x1) != 0;
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

// NV_GSYNC_CONTROL_PARAMS_V1 -- lacks MultiplyDivideMode/Value. Fallback
// shape when a board/driver rejects V2 with IncompatibleStructureVersion
// (see GSyncApi.GetControlParameters/SetControlParameters).
[StructLayout(LayoutKind.Sequential)]
public struct NV_GSYNC_CONTROL_PARAMS_V1
{
    public uint Version;
    public GSyncPolarity Polarity;
    public GSyncVideoMode VMode;
    public uint Interval;
    public GSyncSyncSource Source;
    public uint Bits; // interlaceMode:1, syncSourceIsOutput:1, reserved:30
    public NV_GSYNC_DELAY SyncSkew;
    public NV_GSYNC_DELAY StartupDelay;

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
    // IntPtr[] rather than NvGSyncDeviceHandle[] -- the first real test
    // showed every enumerated handle coming back as a null pointer despite
    // gsyncCount correctly reporting 1 device found, which points at the
    // custom wrapper struct's array marshaling, not the underlying NVAPI
    // call. IntPtr is the primitive .NET already knows how to marshal as an
    // array without any inference about struct blittability involved.
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate Status EnumSyncDevicesDelegate(
        [MarshalAs(UnmanagedType.LPArray, SizeConst = 4)] IntPtr[] gsyncHandles,
        out uint gsyncCount);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate Status QueryCapabilitiesDelegate(
        NvGSyncDeviceHandle device,
        ref NV_GSYNC_CAPABILITIES capabilities);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate Status QueryCapabilitiesV2Delegate(
        NvGSyncDeviceHandle device,
        ref NV_GSYNC_CAPABILITIES_V2 capabilities);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate Status QueryCapabilitiesV1Delegate(
        NvGSyncDeviceHandle device,
        ref NV_GSYNC_CAPABILITIES_V1 capabilities);

    // Raw IntPtr rather than a typed struct array: get-topology's real-
    // machine result came back with every field zeroed (PhysicalGpu 0x0,
    // DisplayId 0 on every entry) despite the call reporting success,
    // while single-struct `ref` parameters (get-sync-status etc.) marshal
    // correctly. That points at [MarshalAs(LPArray)] T[] not round-tripping
    // reliably through a Marshal.GetDelegateForFunctionPointer-obtained
    // delegate specifically -- so array parameters are manually marshaled
    // via AllocHGlobal/StructureToPtr/PtrToStructure instead (see
    // GSyncApi.GetTopology/GetTopologyV1), bypassing that path entirely.
    // One signature now covers both V1 and V2 callers since the pointer
    // itself carries no version information -- that lives in each
    // element's Version field, written manually before the call.
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate Status GetTopologyDelegate(
        NvGSyncDeviceHandle device,
        ref uint gsyncGpuCount,
        IntPtr gsyncGpus,
        ref uint gsyncDisplayCount,
        IntPtr gsyncDisplays);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate Status SetSyncStateSettingsDelegate(
        uint gsyncDisplayCount,
        IntPtr gsyncDisplays,
        uint flags);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate Status GetControlParametersDelegate(
        NvGSyncDeviceHandle device,
        ref NV_GSYNC_CONTROL_PARAMS controls);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate Status GetControlParametersV1Delegate(
        NvGSyncDeviceHandle device,
        ref NV_GSYNC_CONTROL_PARAMS_V1 controls);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate Status SetControlParametersDelegate(
        NvGSyncDeviceHandle device,
        ref NV_GSYNC_CONTROL_PARAMS controls);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate Status SetControlParametersV1Delegate(
        NvGSyncDeviceHandle device,
        ref NV_GSYNC_CONTROL_PARAMS_V1 controls);

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
// Manual marshaling for array-of-struct NVAPI parameters -- see the note
// on Delegates.GetTopologyDelegate for why this exists instead of
// [MarshalAs(UnmanagedType.LPArray)] T[].
internal static class UnmanagedArray
{
    public static IntPtr Alloc<T>(T[] items) where T : struct
    {
        if (items.Length == 0)
        {
            return IntPtr.Zero;
        }

        var size = Marshal.SizeOf<T>();
        var ptr = Marshal.AllocHGlobal(size * items.Length);
        for (var i = 0; i < items.Length; i++)
        {
            Marshal.StructureToPtr(items[i], IntPtr.Add(ptr, i * size), false);
        }

        return ptr;
    }

    public static T[] Read<T>(IntPtr ptr, int count) where T : struct
    {
        if (ptr == IntPtr.Zero || count == 0)
        {
            return Array.Empty<T>();
        }

        var size = Marshal.SizeOf<T>();
        var result = new T[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = Marshal.PtrToStructure<T>(IntPtr.Add(ptr, i * size))!;
        }

        return result;
    }

    public static void Free(IntPtr ptr)
    {
        if (ptr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(ptr);
        }
    }
}

internal static class NativeDispatch
{
    // Keyed by (interfaceId, delegate type), not interfaceId alone: the
    // same native function is called with different delegate signatures
    // when trying multiple struct versions against it (see the V1/V2
    // fallback in GSyncApi below), and a single-key cache would silently
    // hand back a wrongly-typed cached delegate for the second shape.
    private static readonly Dictionary<(uint, Type), Delegate> Cache = new();

    public static T Get<T>(uint interfaceId) where T : Delegate
    {
        var key = (interfaceId, typeof(T));
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var cached))
            {
                return (T) cached;
            }

            var ptr = NvAPI64_QueryInterface(interfaceId);
            if (ptr == IntPtr.Zero)
            {
                throw new GSyncApiException(Status.NoImplementation);
            }

            var del = Marshal.GetDelegateForFunctionPointer<T>(ptr);
            Cache[key] = del;
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
        var handles = new IntPtr[4]; // NVAPI_MAX_GSYNC_DEVICES
        var fn = NativeDispatch.Get<Delegates.EnumSyncDevicesDelegate>(InterfaceIds.EnumSyncDevices);
        var status = fn(handles, out var count);
        if (status != Status.Ok)
        {
            throw new GSyncApiException(status);
        }

        return handles.Take((int) count).Select(h => new NvGSyncDeviceHandle(h)).ToArray();
    }

    // Three on-wire versions exist; tries newest-to-oldest on
    // IncompatibleStructureVersion, same reasoning as the DISPLAY/CONTROL_PARAMS
    // fallbacks elsewhere in this file.
    public static NV_GSYNC_CAPABILITIES QueryCapabilities(NvGSyncDeviceHandle device)
    {
        var caps = new NV_GSYNC_CAPABILITIES { Version = VersionHelper.Make<NV_GSYNC_CAPABILITIES>(3) };
        var fn = NativeDispatch.Get<Delegates.QueryCapabilitiesDelegate>(InterfaceIds.QueryCapabilities);
        var status = fn(device, ref caps);
        if (status == Status.Ok)
        {
            return caps;
        }
        if (status != Status.IncompatibleStructureVersion)
        {
            throw new GSyncApiException(status);
        }

        var caps2 = new NV_GSYNC_CAPABILITIES_V2 { Version = VersionHelper.Make<NV_GSYNC_CAPABILITIES_V2>(2) };
        var fn2 = NativeDispatch.Get<Delegates.QueryCapabilitiesV2Delegate>(InterfaceIds.QueryCapabilities);
        status = fn2(device, ref caps2);
        if (status == Status.Ok)
        {
            return ToCapabilitiesV3(caps2);
        }
        if (status != Status.IncompatibleStructureVersion)
        {
            throw new GSyncApiException(status);
        }

        var caps1 = new NV_GSYNC_CAPABILITIES_V1 { Version = VersionHelper.Make<NV_GSYNC_CAPABILITIES_V1>(1) };
        var fn1 = NativeDispatch.Get<Delegates.QueryCapabilitiesV1Delegate>(InterfaceIds.QueryCapabilities);
        status = fn1(device, ref caps1);
        if (status != Status.Ok)
        {
            throw new GSyncApiException(status);
        }

        return ToCapabilitiesV3(caps1);
    }

    // Matches NVIDIA's documented two-call pattern: call once with null
    // arrays to get counts, allocate, call again to populate. Falls back to
    // NV_GSYNC_DISPLAY_V1 on IncompatibleStructureVersion, re-running both
    // calls -- we don't know from the status alone which of the two a
    // board/driver actually rejected V2 on, so the whole sequence reruns.
    public static (NV_GSYNC_GPU[] Gpus, NV_GSYNC_DISPLAY[] Displays) GetTopology(NvGSyncDeviceHandle device)
    {
        var fn = NativeDispatch.Get<Delegates.GetTopologyDelegate>(InterfaceIds.GetTopology);

        uint gpuCount = 0;
        uint displayCount = 0;
        var status = fn(device, ref gpuCount, IntPtr.Zero, ref displayCount, IntPtr.Zero);
        if (status == Status.IncompatibleStructureVersion)
        {
            return GetTopologyV1(device);
        }
        if (status != Status.Ok)
        {
            throw new GSyncApiException(status);
        }

        var gpuTemplate = new NV_GSYNC_GPU[gpuCount];
        for (var i = 0; i < gpuTemplate.Length; i++)
        {
            gpuTemplate[i].Version = VersionHelper.Make<NV_GSYNC_GPU>(1);
        }

        var displayTemplate = new NV_GSYNC_DISPLAY[displayCount];
        for (var i = 0; i < displayTemplate.Length; i++)
        {
            displayTemplate[i].Version = VersionHelper.Make<NV_GSYNC_DISPLAY>(2);
            displayTemplate[i].Reserved2 = new uint[20];
        }

        var gpuPtr = UnmanagedArray.Alloc(gpuTemplate);
        var displayPtr = UnmanagedArray.Alloc(displayTemplate);
        try
        {
            status = fn(device, ref gpuCount, gpuPtr, ref displayCount, displayPtr);
            if (status == Status.IncompatibleStructureVersion)
            {
                return GetTopologyV1(device);
            }
            if (status != Status.Ok)
            {
                throw new GSyncApiException(status);
            }

            return (
                UnmanagedArray.Read<NV_GSYNC_GPU>(gpuPtr, (int) gpuCount),
                UnmanagedArray.Read<NV_GSYNC_DISPLAY>(displayPtr, (int) displayCount));
        }
        finally
        {
            UnmanagedArray.Free(gpuPtr);
            UnmanagedArray.Free(displayPtr);
        }
    }

    private static (NV_GSYNC_GPU[], NV_GSYNC_DISPLAY[]) GetTopologyV1(NvGSyncDeviceHandle device)
    {
        var fn = NativeDispatch.Get<Delegates.GetTopologyDelegate>(InterfaceIds.GetTopology);

        uint gpuCount = 0;
        uint displayCount = 0;
        var status = fn(device, ref gpuCount, IntPtr.Zero, ref displayCount, IntPtr.Zero);
        if (status != Status.Ok)
        {
            throw new GSyncApiException(status);
        }

        var gpuTemplate = new NV_GSYNC_GPU[gpuCount];
        for (var i = 0; i < gpuTemplate.Length; i++)
        {
            gpuTemplate[i].Version = VersionHelper.Make<NV_GSYNC_GPU>(1);
        }

        var displayTemplate = new NV_GSYNC_DISPLAY_V1[displayCount];
        for (var i = 0; i < displayTemplate.Length; i++)
        {
            displayTemplate[i].Version = VersionHelper.Make<NV_GSYNC_DISPLAY_V1>(1);
        }

        var gpuPtr = UnmanagedArray.Alloc(gpuTemplate);
        var displayPtr = UnmanagedArray.Alloc(displayTemplate);
        try
        {
            status = fn(device, ref gpuCount, gpuPtr, ref displayCount, displayPtr);
            if (status != Status.Ok)
            {
                throw new GSyncApiException(status);
            }

            var gpus = UnmanagedArray.Read<NV_GSYNC_GPU>(gpuPtr, (int) gpuCount);
            var displaysV1 = UnmanagedArray.Read<NV_GSYNC_DISPLAY_V1>(displayPtr, (int) displayCount);
            return (gpus, displaysV1.Select(ToDisplayV2).ToArray());
        }
        finally
        {
            UnmanagedArray.Free(gpuPtr);
            UnmanagedArray.Free(displayPtr);
        }
    }

    // NVIDIA's own docs: requires Administrator privileges.
    public static void SetSyncStateSettings(NV_GSYNC_DISPLAY[] displays, uint flags = 0)
    {
        var fn = NativeDispatch.Get<Delegates.SetSyncStateSettingsDelegate>(InterfaceIds.SetSyncStateSettings);
        var ptr = UnmanagedArray.Alloc(displays);
        try
        {
            var status = fn((uint) displays.Length, ptr, flags);
            if (status == Status.IncompatibleStructureVersion)
            {
                var v1 = displays.Select(ToDisplayV1).ToArray();
                var ptrV1 = UnmanagedArray.Alloc(v1);
                try
                {
                    status = fn((uint) v1.Length, ptrV1, flags);
                }
                finally
                {
                    UnmanagedArray.Free(ptrV1);
                }
            }

            if (status != Status.Ok)
            {
                throw new GSyncApiException(status);
            }
        }
        finally
        {
            UnmanagedArray.Free(ptr);
        }
    }

    public static NV_GSYNC_CONTROL_PARAMS GetControlParameters(NvGSyncDeviceHandle device)
    {
        var p = new NV_GSYNC_CONTROL_PARAMS { Version = VersionHelper.Make<NV_GSYNC_CONTROL_PARAMS>(2) };
        var fn = NativeDispatch.Get<Delegates.GetControlParametersDelegate>(InterfaceIds.GetControlParameters);
        var status = fn(device, ref p);
        if (status == Status.IncompatibleStructureVersion)
        {
            var p1 = new NV_GSYNC_CONTROL_PARAMS_V1 { Version = VersionHelper.Make<NV_GSYNC_CONTROL_PARAMS_V1>(1) };
            var fnV1 = NativeDispatch.Get<Delegates.GetControlParametersV1Delegate>(InterfaceIds.GetControlParameters);
            status = fnV1(device, ref p1);
            if (status != Status.Ok)
            {
                throw new GSyncApiException(status);
            }
            return ToControlParamsV2(p1);
        }
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
        if (status == Status.IncompatibleStructureVersion)
        {
            var p1 = ToControlParamsV1(parameters);
            var fnV1 = NativeDispatch.Get<Delegates.SetControlParametersV1Delegate>(InterfaceIds.SetControlParameters);
            status = fnV1(device, ref p1);
            if (status != Status.Ok)
            {
                throw new GSyncApiException(status);
            }
            return ToControlParamsV2(p1);
        }
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

    private static NV_GSYNC_CAPABILITIES ToCapabilitiesV3(NV_GSYNC_CAPABILITIES_V2 v2) => new()
    {
        Version = VersionHelper.Make<NV_GSYNC_CAPABILITIES>(3),
        BoardId = v2.BoardId,
        Revision = v2.Revision,
        CapFlags = v2.CapFlags,
        ExtendedRevision = v2.ExtendedRevision
    };

    private static NV_GSYNC_CAPABILITIES ToCapabilitiesV3(NV_GSYNC_CAPABILITIES_V1 v1) => new()
    {
        Version = VersionHelper.Make<NV_GSYNC_CAPABILITIES>(3),
        BoardId = v1.BoardId,
        Revision = v1.Revision,
        CapFlags = v1.CapFlags
    };

    // V1 <-> V2 conversions, used only by the IncompatibleStructureVersion
    // fallback paths above. V1 lacks UseExactTiming (NV_GSYNC_DISPLAY) and
    // MultiplyDivideMode/Value (NV_GSYNC_CONTROL_PARAMS) entirely -- those
    // come back as false/Undefined/0 when a board only supports V1, which
    // is a real information loss, not a bug, since V1 genuinely has nowhere
    // to carry that data.
    private static NV_GSYNC_DISPLAY ToDisplayV2(NV_GSYNC_DISPLAY_V1 v1) => new()
    {
        Version = VersionHelper.Make<NV_GSYNC_DISPLAY>(2),
        DisplayId = v1.DisplayId,
        Bits = v1.Bits & 0x1,
        SyncState = v1.SyncState,
        Reserved2 = new uint[20]
    };

    private static NV_GSYNC_DISPLAY_V1 ToDisplayV1(NV_GSYNC_DISPLAY v2) => new()
    {
        Version = VersionHelper.Make<NV_GSYNC_DISPLAY_V1>(1),
        DisplayId = v2.DisplayId,
        Bits = v2.Bits & 0x1,
        SyncState = v2.SyncState
    };

    private static NV_GSYNC_CONTROL_PARAMS ToControlParamsV2(NV_GSYNC_CONTROL_PARAMS_V1 v1) => new()
    {
        Version = VersionHelper.Make<NV_GSYNC_CONTROL_PARAMS>(2),
        Polarity = v1.Polarity,
        VMode = v1.VMode,
        Interval = v1.Interval,
        Source = v1.Source,
        Bits = v1.Bits,
        SyncSkew = v1.SyncSkew,
        StartupDelay = v1.StartupDelay,
        MultiplyDivideMode = GSyncMultiplyDivideMode.Undefined,
        MultiplyDivideValue = 0
    };

    private static NV_GSYNC_CONTROL_PARAMS_V1 ToControlParamsV1(NV_GSYNC_CONTROL_PARAMS v2) => new()
    {
        Version = VersionHelper.Make<NV_GSYNC_CONTROL_PARAMS_V1>(1),
        Polarity = v2.Polarity,
        VMode = v2.VMode,
        Interval = v2.Interval,
        Source = v2.Source,
        Bits = v2.Bits,
        SyncSkew = v2.SyncSkew,
        StartupDelay = v2.StartupDelay
    };
}
