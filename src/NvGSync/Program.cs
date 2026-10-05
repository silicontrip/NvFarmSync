using System.CommandLine;
using System.Text.Json;
using NvAPIWrapper;
using NvAPIWrapper.Native;
using NvGSync.Native;

var deviceOption = new Option<int>("--device")
{
    Description = "Index into NvAPI_GSync_EnumSyncDevices' result (see 'enum-sync-devices')",
    DefaultValueFactory = _ => 0
};
var gpuOption = new Option<int>("--gpu")
{
    Description = "Index into NvAPI_GPU_GetPhysicalGPUs' result",
    DefaultValueFactory = _ => 0
};
var fileArgument = new Argument<FileInfo>("file")
{
    Description = "JSON file describing the value(s) to write"
};
var outOption = new Option<FileInfo?>("--out")
{
    Description = "Write output to this file instead of stdout. Needed when running via a cross-session " +
                  "launcher (e.g. CreateProcessAsUser-based tools) whose stdout doesn't reach the caller.",
    Recursive = true
};

var enumCommand = new Command("enum-sync-devices", "NvAPI_GSync_EnumSyncDevices -- list Sync devices on this system");
var capsCommand = new Command("query-capabilities", "NvAPI_GSync_QueryCapabilities -- Sync board capabilities")
{
    deviceOption
};
var topologyCommand = new Command("get-topology", "NvAPI_GSync_GetTopology -- GPUs and displays attached to the Sync device")
{
    deviceOption
};
// NVIDIA's own docs: this call requires Administrator privileges.
var setSyncStateCommand = new Command("set-sync-state",
    "NvAPI_GSync_SetSyncStateSettings -- set which displays are synchronized (requires Administrator)")
{
    fileArgument
};
var getControlCommand = new Command("get-control-parameters", "NvAPI_GSync_GetControlParameters -- current sync control parameters")
{
    deviceOption
};
// NVIDIA's own docs: this call requires Administrator privileges.
var setControlCommand = new Command("set-control-parameters",
    "NvAPI_GSync_SetControlParameters -- set sync control parameters (requires Administrator)")
{
    deviceOption,
    fileArgument
};
var delayTypeOption = new Option<string>("--type")
{
    Description = "SyncSkew or Startup",
    Required = true
};
var linesOption = new Option<uint>("--lines") { Description = "Delay in horizontal lines", Required = true };
var pixelsOption = new Option<uint>("--pixels") { Description = "Delay in pixels", Required = true };
var adjustDelayCommand = new Command("adjust-sync-delay",
    "NvAPI_GSync_AdjustSyncDelay -- snap a requested delay to the closest value the hardware supports (run before set-control-parameters)")
{
    deviceOption,
    delayTypeOption,
    linesOption,
    pixelsOption
};
var syncStatusCommand = new Command("get-sync-status", "NvAPI_GSync_GetSyncStatus -- timing/stereo sync status for one GPU")
{
    deviceOption,
    gpuOption
};
var statusParamsCommand = new Command("get-status-parameters", "NvAPI_GSync_GetStatusParameters -- RJ45 I/O, house sync, refresh rate")
{
    deviceOption
};

var rootCommand = new RootCommand("NvGSync -- NVIDIA Quadro Sync (GSync) fleet management");
rootCommand.Options.Add(outOption);
rootCommand.Subcommands.Add(enumCommand);
rootCommand.Subcommands.Add(capsCommand);
rootCommand.Subcommands.Add(topologyCommand);
rootCommand.Subcommands.Add(setSyncStateCommand);
rootCommand.Subcommands.Add(getControlCommand);
rootCommand.Subcommands.Add(setControlCommand);
rootCommand.Subcommands.Add(adjustDelayCommand);
rootCommand.Subcommands.Add(syncStatusCommand);
rootCommand.Subcommands.Add(statusParamsCommand);

// See NvFarmSync/NvMosaic for why this is caught explicitly rather than
// left as an unhandled crash: no NVIDIA driver present is an expected
// outcome for some hosts, not a bug.
try
{
    NVIDIA.Initialize();
}
catch (DllNotFoundException)
{
    Console.Error.WriteLine("No NVIDIA driver detected on this machine (nvapi64.dll not found).");
    return 2;
}

var jsonOptions = new JsonSerializerOptions { WriteIndented = true };

enumCommand.SetAction(parseResult => RunSafely(parseResult, EnumSyncDevices));
capsCommand.SetAction(parseResult => RunSafely(parseResult, () => QueryCapabilities(parseResult.GetValue(deviceOption))));
topologyCommand.SetAction(parseResult => RunSafely(parseResult, () => GetTopology(parseResult.GetValue(deviceOption))));
setSyncStateCommand.SetAction(parseResult => RunSafely(parseResult, () => SetSyncState(parseResult.GetValue(fileArgument)!)));
getControlCommand.SetAction(parseResult => RunSafely(parseResult, () => GetControlParameters(parseResult.GetValue(deviceOption))));
setControlCommand.SetAction(parseResult =>
    RunSafely(parseResult, () => SetControlParameters(parseResult.GetValue(deviceOption), parseResult.GetValue(fileArgument)!)));
adjustDelayCommand.SetAction(parseResult => RunSafely(parseResult, () => AdjustSyncDelay(
    parseResult.GetValue(deviceOption),
    parseResult.GetValue(delayTypeOption)!,
    parseResult.GetValue(linesOption),
    parseResult.GetValue(pixelsOption))));
syncStatusCommand.SetAction(parseResult =>
    RunSafely(parseResult, () => GetSyncStatus(parseResult.GetValue(deviceOption), parseResult.GetValue(gpuOption))));
statusParamsCommand.SetAction(parseResult => RunSafely(parseResult, () => GetStatusParameters(parseResult.GetValue(deviceOption))));

return rootCommand.Parse(args).Invoke();

int RunSafely(ParseResult parseResult, Func<int> action)
{
    var outFile = parseResult.GetValue(outOption);
    StreamWriter? fileWriter = null;
    var originalOut = Console.Out;
    if (outFile != null)
    {
        fileWriter = new StreamWriter(outFile.FullName, append: false) { AutoFlush = true };
        Console.SetOut(fileWriter);
    }

    try
    {
        return action();
    }
    catch (GSyncApiException ex)
    {
        // Mosaic operations turned out to be session-sensitive (Session 0
        // vs the console-owning Session 1+); GSync is equally display-
        // hardware-adjacent, so the same check is worth having ready here
        // rather than rediscovering it the same way a second time.
        var sessionId = System.Diagnostics.Process.GetCurrentProcess().SessionId;
        Console.Error.WriteLine($"NVAPI call failed: {ex.Status} -- {ex.Message} (PID {Environment.ProcessId}, Session {sessionId})");
        return 3;
    }
    finally
    {
        if (fileWriter != null)
        {
            Console.SetOut(originalOut);
            fileWriter.Dispose();
        }
    }
}

NvGSyncDeviceHandle ResolveDevice(int index)
{
    var devices = GSyncApi.EnumSyncDevices();
    if (index < 0 || index >= devices.Length)
    {
        throw new InvalidOperationException(
            $"--device {index} is out of range: {devices.Length} Sync device(s) found. Run 'enum-sync-devices' first.");
    }
    return devices[index];
}

int EnumSyncDevices()
{
    var devices = GSyncApi.EnumSyncDevices();
    var dto = devices.Select((d, i) => new { Index = i, Handle = d.ToString() }).ToArray();
    Console.WriteLine(JsonSerializer.Serialize(dto, jsonOptions));
    return 0;
}

int QueryCapabilities(int deviceIndex)
{
    var caps = GSyncApi.QueryCapabilities(ResolveDevice(deviceIndex));
    var dto = new
    {
        caps.BoardId,
        caps.Revision,
        caps.ExtendedRevision,
        caps.CapFlags,
        caps.IsMulDivSupported,
        caps.MaxMulDivValue
    };
    Console.WriteLine(JsonSerializer.Serialize(dto, jsonOptions));
    return 0;
}

int GetTopology(int deviceIndex)
{
    var (gpus, displays) = GSyncApi.GetTopology(ResolveDevice(deviceIndex));
    var dto = new
    {
        Gpus = gpus.Select(g => new
        {
            PhysicalGpu = $"0x{g.PhysicalGpu.ToInt64():X}",
            Connector = g.Connector.ToString(),
            ProxyPhysicalGpu = g.ProxyPhysicalGpu == IntPtr.Zero ? null : $"0x{g.ProxyPhysicalGpu.ToInt64():X}",
            g.IsSynced
        }),
        Displays = displays.Select(d => new
        {
            d.DisplayId,
            d.IsMasterable,
            d.UseExactTiming,
            SyncState = d.SyncState.ToString()
        })
    };
    Console.WriteLine(JsonSerializer.Serialize(dto, jsonOptions));
    return 0;
}

// Input file: JSON array of { DisplayId, SyncState, UseExactTiming }. Any
// display not listed here becomes un-synchronized (NVIDIA's own documented
// behavior, not a choice made by this tool).
int SetSyncState(FileInfo file)
{
    var entries = JsonSerializer.Deserialize<SyncStateEntryDto[]>(File.ReadAllText(file.FullName), jsonOptions)
                  ?? throw new InvalidDataException("File did not contain a JSON array of display sync states.");

    var displays = entries
        .Select(e => NV_GSYNC_DISPLAY.Create(e.DisplayId, Enum.Parse<GSyncDisplaySyncState>(e.SyncState, true), e.UseExactTiming))
        .ToArray();

    GSyncApi.SetSyncStateSettings(displays);
    Console.WriteLine($"Set sync state for {displays.Length} display(s).");
    return 0;
}

int GetControlParameters(int deviceIndex)
{
    var p = GSyncApi.GetControlParameters(ResolveDevice(deviceIndex));
    Console.WriteLine(JsonSerializer.Serialize(ToControlParamsDto(p), jsonOptions));
    return 0;
}

// Input file: JSON object matching GetControlParameters' output shape.
// Run adjust-sync-delay first if you're changing SyncSkew/StartupDelay --
// NVIDIA's own guidance, not an extra step invented here.
int SetControlParameters(int deviceIndex, FileInfo file)
{
    var dto = JsonSerializer.Deserialize<ControlParamsDto>(File.ReadAllText(file.FullName), jsonOptions)
              ?? throw new InvalidDataException("File did not contain a control parameters object.");

    var p = new NV_GSYNC_CONTROL_PARAMS
    {
        Polarity = Enum.Parse<GSyncPolarity>(dto.Polarity, true),
        VMode = Enum.Parse<GSyncVideoMode>(dto.VMode, true),
        Interval = dto.Interval,
        Source = Enum.Parse<GSyncSyncSource>(dto.Source, true),
        Bits = (dto.InterlaceMode ? 0x1u : 0u) | (dto.SyncSourceIsOutput ? 0x2u : 0u),
        SyncSkew = new NV_GSYNC_DELAY { NumLines = dto.SyncSkew.NumLines, NumPixels = dto.SyncSkew.NumPixels },
        StartupDelay = new NV_GSYNC_DELAY { NumLines = dto.StartupDelay.NumLines, NumPixels = dto.StartupDelay.NumPixels },
        MultiplyDivideMode = Enum.Parse<GSyncMultiplyDivideMode>(dto.MultiplyDivideMode, true),
        MultiplyDivideValue = dto.MultiplyDivideValue
    };

    var applied = GSyncApi.SetControlParameters(ResolveDevice(deviceIndex), p);
    Console.WriteLine(JsonSerializer.Serialize(ToControlParamsDto(applied), jsonOptions));
    return 0;
}

int AdjustSyncDelay(int deviceIndex, string delayTypeText, uint lines, uint pixels)
{
    var delayType = Enum.Parse<GSyncDelayType>(delayTypeText, true);
    var (adjusted, syncSteps) = GSyncApi.AdjustSyncDelay(
        ResolveDevice(deviceIndex), delayType, new NV_GSYNC_DELAY { NumLines = lines, NumPixels = pixels });

    var dto = new
    {
        adjusted.NumLines,
        adjusted.NumPixels,
        adjusted.MaxLines,
        adjusted.MinPixels,
        SyncSteps = syncSteps
    };
    Console.WriteLine(JsonSerializer.Serialize(dto, jsonOptions));
    if (syncSteps == 0)
    {
        Console.Error.WriteLine("SyncSteps is 0: NumPixels is below MinPixels, or NumLines exceeds MaxLines, at the current display mode.");
    }
    return 0;
}

int GetSyncStatus(int deviceIndex, int gpuIndex)
{
    var gpus = NvAPIWrapper.Native.GPUApi.EnumPhysicalGPUs();
    if (gpuIndex < 0 || gpuIndex >= gpus.Length)
    {
        throw new InvalidOperationException($"--gpu {gpuIndex} is out of range: {gpus.Length} physical GPU(s) found.");
    }

    var status = GSyncApi.GetSyncStatus(ResolveDevice(deviceIndex), gpus[gpuIndex].MemoryAddress);
    var dto = new
    {
        IsSynced = status.IsSynced != 0,
        IsStereoSynced = status.IsStereoSynced != 0,
        IsSyncSignalAvailable = status.IsSyncSignalAvailable != 0
    };
    Console.WriteLine(JsonSerializer.Serialize(dto, jsonOptions));
    return 0;
}

int GetStatusParameters(int deviceIndex)
{
    var p = GSyncApi.GetStatusParameters(ResolveDevice(deviceIndex));
    var dto = new
    {
        p.RefreshRate,
        RJ45_IO = p.RJ45_IO.Select(v => ((GSyncRJ45IO) v).ToString()).ToArray(),
        RJ45_Ethernet = p.RJ45_Ethernet,
        p.HouseSyncIncoming,
        BHouseSync = p.BHouseSync != 0,
        p.InternalSlave
    };
    Console.WriteLine(JsonSerializer.Serialize(dto, jsonOptions));
    return 0;
}

object ToControlParamsDto(NV_GSYNC_CONTROL_PARAMS p) => new
{
    Polarity = p.Polarity.ToString(),
    VMode = p.VMode.ToString(),
    p.Interval,
    Source = p.Source.ToString(),
    p.InterlaceMode,
    p.SyncSourceIsOutput,
    SyncSkew = new { p.SyncSkew.NumLines, p.SyncSkew.NumPixels, p.SyncSkew.MaxLines, p.SyncSkew.MinPixels },
    StartupDelay = new { p.StartupDelay.NumLines, p.StartupDelay.NumPixels, p.StartupDelay.MaxLines, p.StartupDelay.MinPixels },
    MultiplyDivideMode = p.MultiplyDivideMode.ToString(),
    p.MultiplyDivideValue
};

record SyncStateEntryDto(uint DisplayId, string SyncState, bool UseExactTiming);

record ControlParamsDto(
    string Polarity,
    string VMode,
    uint Interval,
    string Source,
    bool InterlaceMode,
    bool SyncSourceIsOutput,
    DelayDto SyncSkew,
    DelayDto StartupDelay,
    string MultiplyDivideMode,
    byte MultiplyDivideValue);

record DelayDto(uint NumLines, uint NumPixels, uint MaxLines = 0, uint MinPixels = 0);
