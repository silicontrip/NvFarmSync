using System.CommandLine;
using System.Globalization;
using System.Text.RegularExpressions;
using NvAPIWrapper;
using NvAPIWrapper.Native;
using NvAPIWrapper.Native.DRS;
using NvAPIWrapper.Native.DRS.Structures;

var profileOption = new Option<string?>("--profile")
{
    Description = "Profile name to read (mutually exclusive with --app)"
};
var appOption = new Option<string?>("--app")
{
    Description = "Executable name to resolve the bound profile for (mutually exclusive with --profile)"
};
var fileArgument = new Argument<FileInfo>("file")
{
    Description = "INI file produced by 'get'"
};
var andSaveOption = new Option<bool>("--save")
{
    Description = "Also call NvAPI_DRS_SaveSettings in this same session, persisting the staged values. " +
                  "Without this flag, 'set' only ever calls NvAPI_DRS_SetSetting -- the process exits, the " +
                  "session is destroyed, and nothing changes on the machine."
};

var getCommand = new Command("get", "NvAPI_DRS_GetSetting -- read a profile's full effective settings to an INI file (stdout)")
{
    profileOption,
    appOption
};
var setCommand = new Command("set", "NvAPI_DRS_SetSetting -- stage an INI file's values in this session; add --save to persist them")
{
    fileArgument,
    andSaveOption
};
var saveCommand = new Command("save", "NvAPI_DRS_SaveSettings -- persist whatever is already staged in this session (nothing, unless run right after a --save-less 'set' in a future single-session mode)");

var rootCommand = new RootCommand("NvFarm -- NVIDIA driver settings fleet management");
rootCommand.Subcommands.Add(getCommand);
rootCommand.Subcommands.Add(setCommand);
rootCommand.Subcommands.Add(saveCommand);

// nvapi64.dll is placed on disk only by an actual NVIDIA display driver
// install. A DllNotFoundException here means there's no NVIDIA hardware/
// driver on this machine at all (e.g. a software-rendered preview engine) --
// a real, expected outcome for some hosts in this fleet, not a bug. Exit
// code 2 lets a wrapper script tell "no NVIDIA here, skip it" apart from
// exit code 1 (a genuine usage/validation error) without parsing text.
try
{
    NVIDIA.Initialize();
}
catch (DllNotFoundException)
{
    Console.Error.WriteLine("No NVIDIA driver detected on this machine (nvapi64.dll not found). " +
                             "This host likely has no NVIDIA GPU/driver installed.");
    return 2;
}

DRSSessionHandle session = DRSApi.CreateSession();
DRSApi.LoadSettings(session);

getCommand.SetAction(parseResult =>
{
    var profileName = parseResult.GetValue(profileOption);
    var appName = parseResult.GetValue(appOption);
    if (string.IsNullOrEmpty(profileName) == string.IsNullOrEmpty(appName))
    {
        Console.Error.WriteLine("Specify exactly one of --profile or --app.");
        return 1;
    }
    return Get(profileName, appName);
});

setCommand.SetAction(parseResult => Set(parseResult.GetValue(fileArgument)!, andSave: parseResult.GetValue(andSaveOption)));
saveCommand.SetAction(_ => Save());

int exitCode = rootCommand.Parse(args).Invoke();
DRSApi.DestroySession(session);
return exitCode;

// Reads the full effective-settings sweep for a profile (resolved either by
// name or, more robustly, by the executable currently bound to it -- see
// FindApplicationByName usage below for why resolving by name alone can
// silently show a profile that governs nothing). Every setting the driver
// knows about is written, tagged only by its numeric ID (never by name
// alone -- NvAPIWrapper's name table has collisions, e.g. two distinct
// setting IDs both display as "G-SYNC") so the file round-trips
// unambiguously through set/save regardless of naming quirks.
int Get(string? profileName, string? appName)
{
    DRSProfileHandle profileHandle;
    string resolvedProfileName;

    if (appName != null)
    {
        var app = DRSApi.FindApplicationByName(session, appName, out var found);
        if (app == null || !found.HasValue)
        {
            Console.Error.WriteLine($"No profile is bound to '{appName}'.");
            return 1;
        }
        profileHandle = found.Value;
        resolvedProfileName = DRSApi.GetProfileInfo(session, profileHandle).Name;
    }
    else
    {
        profileHandle = DRSApi.FindProfileByName(session, profileName!);
        resolvedProfileName = profileName!;
    }

    var globalProfileHandle = DRSApi.GetCurrentGlobalProfile(session);

    Console.WriteLine($"[{resolvedProfileName}]");
    foreach (var id in DRSApi.EnumAvailableSettingIds())
    {
        // EnumAvailableSettingIds and the per-ID calls below (name lookup,
        // GetSetting, EnumAvailableSettingValues) don't necessarily agree
        // with each other -- an ID this driver enumerates as "available"
        // can still throw NVAPI_SETTING_NOT_FOUND from GetSettingNameFromId
        // (observed in practice: one of two otherwise-identical-class
        // machines threw here and the other didn't, which is itself a
        // signal the two aren't running quite the same driver build). One
        // bad ID should degrade to a single reported line, not take down
        // the whole export.
        try
        {
            var name = DRSApi.GetSettingNameFromId(id);
            var key = string.IsNullOrEmpty(name) ? $"0x{id:X8}" : $"{name} (0x{id:X8})";

            var onProfile = DRSApi.GetSetting(session, profileHandle, id);
            object? value;
            string source;
            if (onProfile.HasValue)
            {
                value = onProfile.Value.CurrentValue;
                // This is the driver's own authoritative answer (NVDRS_SETTING's
                // settingLocation field, documented as describing "where the
                // value in CurrentValue comes from"), not our guess.
                // GetSetting against a specific profile can still resolve to a
                // value actually backed by the global/base profile or the
                // driver default -- exactly what NVIDIA Control Panel's
                // "Use the global setting" reflects. Trusting HasValue alone
                // (the old logic) mislabeled these as "explicit".
                source = onProfile.Value.SettingLocation switch
                {
                    DRSSettingLocation.CurrentProfile => "explicit",
                    DRSSettingLocation.GlobalProfile => "global profile",
                    DRSSettingLocation.BaseProfile => "base profile",
                    DRSSettingLocation.DefaultProfile => "driver default",
                    _ => $"unknown location ({onProfile.Value.SettingLocation})"
                };
            }
            else
            {
                var onGlobal = DRSApi.GetSetting(session, globalProfileHandle, id);
                if (onGlobal.HasValue)
                {
                    value = onGlobal.Value.CurrentValue;
                    source = "global profile";
                }
                else
                {
                    value = DRSApi.EnumAvailableSettingValues(id).DefaultValue;
                    source = "driver default";
                }
            }
            // The source comment is not decorative: a value that matches only
            // because it's inherited from the global profile or driver default
            // is NOT the same thing as it being explicitly pinned on this
            // profile, and someone reading this file (rather than running
            // 'set' and reading its diff) has no other way to tell the two
            // apart. 'set'/'save' ignore this comment; it's for humans.
            Console.WriteLine($"{key} = {value}  ; {source}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"0x{id:X8}: skipped -- {ex.Message}");
        }
    }
    return 0;
}

// Calls NvAPI_DRS_SetSetting once per entry in the file and prints old -> new.
// A DRS session is scoped to the calling process -- it is not a running-config
// that persists independently of who's holding it, the way a daemon's live
// state might. When this process exits without calling SaveSettings, the
// session (and everything staged in it) is simply gone: not "not yet
// reloaded," but never visible to anything else at all. So by default this
// changes nothing on the machine, which is the literal, honest behavior of
// SetSetting alone. --save exists for the same reason `rm -f` exists: an
// explicit flag that performs the one further step (SaveSettings, in this
// same session) needed to fulfill a further, explicitly requested intent --
// never done silently.
int Set(FileInfo file, bool andSave)
{
    var sectionRegex = new Regex(@"^\[(.+)\]$");
    var lineRegex = new Regex(@"^(.*?)(?:\s*\(0x([0-9A-Fa-f]{8})\))?\s*=\s*(.*)$");
    var bareHexRegex = new Regex(@"^0x([0-9A-Fa-f]{8})$", RegexOptions.IgnoreCase);
    // Strips the trailing "; explicit" / "; global profile" / "; driver default"
    // comment 'get' appends -- informational for humans reading the file,
    // ignored here since it plays no part in what gets staged.
    var trailingCommentRegex = new Regex(@"\s*;[^;]*$");

    DRSProfileHandle profileHandle = default;
    bool haveProfile = false;
    int changes = 0;

    foreach (var rawLine in File.ReadLines(file.FullName))
    {
        var line = trailingCommentRegex.Replace(rawLine.Trim(), "").Trim();
        if (line.Length == 0) continue;

        var sectionMatch = sectionRegex.Match(line);
        if (sectionMatch.Success)
        {
            var profileName = sectionMatch.Groups[1].Value;
            try
            {
                profileHandle = DRSApi.FindProfileByName(session, profileName);
                haveProfile = true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Profile '{profileName}' not found on this machine: {ex.Message}");
                haveProfile = false;
            }
            Console.WriteLine($"[{profileName}]");
            continue;
        }

        if (!haveProfile)
        {
            Console.Error.WriteLine($"Skipping line (no valid [Profile] section yet): {line}");
            continue;
        }

        var m = lineRegex.Match(line);
        if (!m.Success) continue;

        var keyLabel = m.Groups[1].Value.Trim();
        var valueText = m.Groups[3].Value;

        uint id;
        DRSSettingValues typeInfo;
        object newValue;
        // As with 'get', a setting ID a file references (even one this same
        // machine wrote, if the driver was updated in between, or one
        // written by a different machine of the same class) isn't
        // guaranteed to resolve here -- GetSettingIdFromName and
        // EnumAvailableSettingValues can both throw NVAPI_SETTING_NOT_FOUND.
        // One bad line should be skipped, not take down the whole file.
        try
        {
            if (m.Groups[2].Success)
            {
                id = Convert.ToUInt32(m.Groups[2].Value, 16);
            }
            else
            {
                var bare = bareHexRegex.Match(keyLabel);
                id = bare.Success ? Convert.ToUInt32(bare.Groups[1].Value, 16) : DRSApi.GetSettingIdFromName(keyLabel);
            }

            typeInfo = DRSApi.EnumAvailableSettingValues(id);
            newValue = typeInfo.SettingType switch
            {
                DRSSettingType.Integer => ParseIntegerValue(valueText),
                DRSSettingType.String or DRSSettingType.UnicodeString => valueText,
                _ => throw new NotSupportedException(
                    $"Setting type {typeInfo.SettingType} for 0x{id:X8} is not yet supported by set/save.")
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  {keyLabel}: skipped -- {ex.Message}");
            continue;
        }

        var current = DRSApi.GetSetting(session, profileHandle, id);
        var currentText = current.HasValue ? current.Value.CurrentValue?.ToString() ?? "" : "(not set on this profile)";
        var label = string.IsNullOrEmpty(keyLabel) ? $"0x{id:X8}" : keyLabel;

        // A match is only truly a no-op if it's already explicit on THIS
        // profile (SettingLocation == CurrentProfile). GetSetting can return
        // a value that happens to equal newValue while actually resolving
        // from GlobalProfile/BaseProfile/DefaultProfile -- HasValue alone
        // doesn't distinguish that. Skipping on HasValue alone (the old
        // logic) would silently leave such a setting un-pinned forever,
        // exactly the "looks the same, isn't actually local" risk this tool
        // exists to close.
        bool alreadyExplicit = current.HasValue && current.Value.SettingLocation == DRSSettingLocation.CurrentProfile;
        if (alreadyExplicit && currentText == newValue.ToString())
        {
            Console.WriteLine($"  {label} = {newValue}  (unchanged)");
            continue;
        }

        // The driver, not our own client-side parsing, is the actual authority
        // on whether this value is acceptable (NvAPI_DRS_SetSetting can return
        // NVAPI_ERROR; we don't replicate that validation ourselves against
        // EnumAvailableSettingValues). This call is what "set" is actually
        // for even without --save: a real, driver-validated dry run.
        try
        {
            DRSApi.SetSetting(session, profileHandle, new DRSSettingV1(id, typeInfo.SettingType, newValue));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  {label}: REJECTED by driver -- {ex.Message}");
            continue;
        }

        Console.WriteLine($"  {label}: {currentText} -> {newValue}");
        changes++;
    }

    if (andSave)
    {
        DRSApi.SaveSettings(session);
        Console.WriteLine($"Saved ({changes} change(s)).");
    }
    else
    {
        Console.WriteLine($"{changes} change(s) staged in this session only -- nothing persisted. Re-run with --save to persist.");
    }
    return 0;
}

// The literal, argument-less NvAPI_DRS_SaveSettings call. In a fresh process
// there is nothing staged to persist (LoadSettings above just read back
// whatever is already durable), so this is normally a no-op -- it exists as
// the direct primitive, not because it's useful in isolation from a
// separate CLI invocation. See Set's --save flag for the only way this
// tool can actually persist new values, since staging and saving must
// happen in the same session.
int Save()
{
    DRSApi.SaveSettings(session);
    Console.WriteLine("Saved.");
    return 0;
}

uint ParseIntegerValue(string text)
{
    text = text.Trim();
    return text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
        ? Convert.ToUInt32(text, 16)
        : uint.Parse(text, CultureInfo.InvariantCulture);
}
