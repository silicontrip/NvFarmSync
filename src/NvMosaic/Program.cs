using System.CommandLine;
using System.Text.Json;
using NvAPIWrapper;
using NvAPIWrapper.Native;
using NvAPIWrapper.Native.Display;
using NvAPIWrapper.Native.Exceptions;
using NvAPIWrapper.Native.Interfaces.Mosaic;
using NvAPIWrapper.Native.Mosaic;
using NvAPIWrapper.Native.Mosaic.Structures;

var flagsOption = new Option<string>("--flags")
{
    Description = "NvAPI_Mosaic_Set/ValidateDisplayGrids flags: NoFlag, CurrentGPUTopology, NoDriverReload, " +
                  "MaximizePerformance, AllowInvalid -- comma-separate to combine, e.g. NoDriverReload,AllowInvalid",
    DefaultValueFactory = _ => "NoFlag"
};
var fileArgument = new Argument<FileInfo>("file")
{
    Description = "JSON file produced by 'list'"
};
var outOption = new Option<FileInfo?>("--out")
{
    Description = "Write output to this file instead of stdout. Needed when running via a cross-session " +
                  "launcher (e.g. CreateProcessAsUser-based tools) whose stdout doesn't reach the caller.",
    Recursive = true
};

var listCommand = new Command("list", "NvAPI_Mosaic_EnumDisplayGrids -- list the current display grid topology (JSON, stdout)");
var getCommand = new Command("get", "NvAPI_Mosaic_GetCurrentTopology -- get the current topology brief and overlap (JSON, stdout)");
var setCommand = new Command("set", "NvAPI_Mosaic_SetDisplayGrids -- apply a grid topology from a JSON file")
{
    fileArgument,
    flagsOption
};
var validateCommand = new Command("validate", "NvAPI_Mosaic_ValidateDisplayGrids -- validate a grid topology from a JSON file without applying it")
{
    fileArgument,
    flagsOption
};

var rootCommand = new RootCommand("NvMosaic -- NVIDIA Mosaic display topology fleet management");
rootCommand.Options.Add(outOption);
rootCommand.Subcommands.Add(listCommand);
rootCommand.Subcommands.Add(getCommand);
rootCommand.Subcommands.Add(setCommand);
rootCommand.Subcommands.Add(validateCommand);

// See NvFarmSync for why this is caught explicitly rather than left as an
// unhandled crash: no NVIDIA driver present is an expected outcome for some
// hosts (e.g. software-rendered preview engines), not a bug, and a wrapper
// script iterating the fleet needs a clean exit code to tell the two apart.
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

listCommand.SetAction(parseResult => RunSafely(parseResult, List));
getCommand.SetAction(parseResult => RunSafely(parseResult, Get));
setCommand.SetAction(parseResult =>
    RunSafely(parseResult, () => SetOrValidate(parseResult.GetValue(fileArgument)!, parseResult.GetValue(flagsOption)!, apply: true)));
validateCommand.SetAction(parseResult =>
    RunSafely(parseResult, () => SetOrValidate(parseResult.GetValue(fileArgument)!, parseResult.GetValue(flagsOption)!, apply: false)));

return rootCommand.Parse(args).Invoke();

// System.CommandLine 2.0.12 doesn't catch action exceptions itself (confirmed:
// an uncaught one reaches the process as a raw .NET stack trace). NVAPI calls
// fail with only a status code and, often, no further message text (the
// driver's own error-message lookup can return nothing beyond the status
// name itself) -- so there's nothing to gain from a full stack trace here,
// only noise. Exit code 3 distinguishes "a real NVAPI call failed" from
// exit code 2 (no driver present) and exit code 1 (usage/validation error).
int RunSafely(ParseResult parseResult, Func<int> action)
{
    // --out redirects Console.Out only (not Console.Error) so a wrapper
    // watching stderr, or a human running this directly, still sees errors
    // immediately even when the actual payload is going to a file -- the
    // same split a --logfile convention would give you, without needing
    // every call site to pick a writer explicitly.
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
    catch (NVIDIAApiException ex)
    {
        // Mosaic operations can be session-sensitive: enumerating the actual
        // active grid/display topology is tied to the session that owns the
        // physical console (Session 1+), while a service-spawned process
        // (e.g. anything launched via sshd, which runs in Session 0 by
        // default) may not have that access even with full privileges.
        // Printing this makes that theory directly checkable by comparing
        // against `query session` at the console, instead of guessing.
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

int List()
{
    var dtos = MosaicApi.EnumDisplayGrids().Select(ToGridTopologyDto).ToArray();
    Console.WriteLine(JsonSerializer.Serialize(dtos, jsonOptions));
    return 0;
}

int Get()
{
    MosaicApi.GetCurrentTopology(out var topoBrief, out var displaySettings, out var overlapX, out var overlapY);
    var dto = new
    {
        Topology = topoBrief.Topology.ToString(),
        topoBrief.IsEnable,
        topoBrief.IsPossible,
        DisplaySettings = ToDisplaySettingsDto(displaySettings),
        OverlapX = overlapX,
        OverlapY = overlapY
    };
    Console.WriteLine(JsonSerializer.Serialize(dto, jsonOptions));
    return 0;
}

int SetOrValidate(FileInfo file, string flagsText, bool apply)
{
    SetDisplayTopologyFlag flags;
    try
    {
        flags = Enum.Parse<SetDisplayTopologyFlag>(flagsText, ignoreCase: true);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Invalid --flags value '{flagsText}': {ex.Message}");
        return 1;
    }

    GridTopologyDto[] dtos;
    try
    {
        dtos = JsonSerializer.Deserialize<GridTopologyDto[]>(File.ReadAllText(file.FullName), jsonOptions)
               ?? throw new InvalidDataException("File did not contain a JSON array of grid topologies.");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Failed to parse {file.FullName}: {ex.Message}");
        return 1;
    }

    var gridTopologies = dtos.Select(ToGridTopologyV1).Cast<IGridTopology>().ToArray();

    if (apply)
    {
        MosaicApi.SetDisplayGrids(gridTopologies, flags);
        Console.WriteLine($"Applied {gridTopologies.Length} grid topolog{(gridTopologies.Length == 1 ? "y" : "ies")}.");
    }
    else
    {
        var statuses = MosaicApi.ValidateDisplayGrids(gridTopologies, flags);
        for (var i = 0; i < statuses.Length; i++)
        {
            var s = statuses[i];
            Console.WriteLine($"Topology {i}: Errors={s.Errors} Warnings={s.Warnings}");
            foreach (var d in s.Displays)
            {
                Console.WriteLine($"  Display 0x{d.DisplayId:X8}: Errors={d.Errors} Warnings={d.Warnings} SupportsRotation={d.SupportsRotation}");
            }
        }
    }
    return 0;
}

GridTopologyDto ToGridTopologyDto(IGridTopology t) => new(
    t.Rows,
    t.Columns,
    ToDisplaySettingsDto(t.DisplaySettings),
    t.ApplyWithBezelCorrectedResolution,
    t.ImmersiveGaming,
    t.BaseMosaicPanoramic,
    t.DriverReloadAllowed,
    t.AcceleratePrimaryDisplay,
    t.Displays.Select(ToGridDisplayDto).ToArray()
);

DisplaySettingsDto ToDisplaySettingsDto(IDisplaySettings s) => new(s.Width, s.Height, s.BitsPerPixel, s.Frequency);

GridDisplayDto ToGridDisplayDto(IGridTopologyDisplay d) => new(d.DisplayId, d.OverlapX, d.OverlapY, d.Rotation.ToString(), d.CloneGroup);

GridTopologyV1 ToGridTopologyV1(GridTopologyDto dto) => new(
    dto.Rows,
    dto.Columns,
    dto.Displays.Select(ToGridTopologyDisplayV1).ToArray(),
    new DisplaySettingsV1(dto.DisplaySettings.Width, dto.DisplaySettings.Height, dto.DisplaySettings.BitsPerPixel, dto.DisplaySettings.Frequency),
    dto.ApplyWithBezelCorrectedResolution,
    dto.ImmersiveGaming,
    dto.BaseMosaicPanoramic,
    dto.DriverReloadAllowed,
    dto.AcceleratePrimaryDisplay
);

GridTopologyDisplayV1 ToGridTopologyDisplayV1(GridDisplayDto dto) => new(
    dto.DisplayId,
    dto.OverlapX,
    dto.OverlapY,
    Enum.Parse<Rotate>(dto.Rotation, ignoreCase: true),
    dto.CloneGroup
);

record GridTopologyDto(
    int Rows,
    int Columns,
    DisplaySettingsDto DisplaySettings,
    bool ApplyWithBezelCorrectedResolution,
    bool ImmersiveGaming,
    bool BaseMosaicPanoramic,
    bool DriverReloadAllowed,
    bool AcceleratePrimaryDisplay,
    GridDisplayDto[] Displays
);

record DisplaySettingsDto(int Width, int Height, int BitsPerPixel, int Frequency);

record GridDisplayDto(uint DisplayId, int OverlapX, int OverlapY, string Rotation, uint CloneGroup);
