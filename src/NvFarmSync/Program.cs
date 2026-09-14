using NvAPIWrapper;
using NvAPIWrapper.Native;
using NvAPIWrapper.Native.DRS.Structures;

NVIDIA.Initialize();
Console.WriteLine($"Driver version: {NVIDIA.DriverVersion}");
Console.WriteLine($"Interface version: {NVIDIA.InterfaceVersionString}");

DRSSessionHandle session = DRSApi.CreateSession();
DRSApi.LoadSettings(session);

if (args.Length >= 2 && args[0] == "effective")
{
    PrintEffectiveSettings(args[1]);
}
else
{
    PrintFullDump();
}

DRSApi.DestroySession(session);
return;

void PrintFullDump()
{
    var baseProfile = DRSApi.GetProfileInfo(session, DRSApi.GetBaseProfile(session));
    var currentGlobalProfile = DRSApi.GetProfileInfo(session, DRSApi.GetCurrentGlobalProfile(session));
    Console.WriteLine($"Base Profile: {baseProfile.Name}");
    Console.WriteLine($"Current Global Profile: {currentGlobalProfile.Name}");

    foreach (var profileHandle in DRSApi.EnumProfiles(session))
    {
        var profile = DRSApi.GetProfileInfo(session, profileHandle);
        Console.WriteLine($"Profile: {profile.Name}");

        if (profile.NumberOfApplications > 0)
        {
            foreach (var app in DRSApi.EnumApplications(session, profileHandle))
            {
                Console.WriteLine($"  Application: {app.ApplicationName}");
            }
        }

        foreach (var setting in DRSApi.EnumSettings(session, profileHandle))
        {
            var name = string.IsNullOrEmpty(setting.Name) ? $"0x{setting.Id:X8}" : setting.Name;
            Console.WriteLine($"  {name} = {setting.CurrentValue}");
        }
    }
}

// Resolves every known driver setting to the value that actually applies to
// the given profile, walking the same fallback chain the driver itself uses:
// explicit override on this profile -> explicit override on the current
// global profile -> the driver's own hardcoded default. Profiles only ever
// store deltas (see EnumSettings above), so this is the only way to get a
// complete, non-sparse picture of what a profile's application actually runs
// with -- which is what's needed to pin every value explicitly rather than
// depend on that fallback chain staying intact.
void PrintEffectiveSettings(string profileName)
{
    var profileHandle = DRSApi.FindProfileByName(session, profileName);
    var globalProfileHandle = DRSApi.GetCurrentGlobalProfile(session);
    var globalProfile = DRSApi.GetProfileInfo(session, globalProfileHandle);

    Console.WriteLine($"Profile: {profileName}");
    Console.WriteLine($"Current Global Profile (fallback): {globalProfile.Name}");
    Console.WriteLine();

    foreach (var id in DRSApi.EnumAvailableSettingIds())
    {
        var name = DRSApi.GetSettingNameFromId(id);
        var label = string.IsNullOrEmpty(name) ? $"0x{id:X8}" : name;

        var onProfile = DRSApi.GetSetting(session, profileHandle, id);
        if (onProfile.HasValue)
        {
            Console.WriteLine($"  {label} = {onProfile.Value.CurrentValue}  [explicit: {profileName}]");
            continue;
        }

        var onGlobal = DRSApi.GetSetting(session, globalProfileHandle, id);
        if (onGlobal.HasValue)
        {
            Console.WriteLine($"  {label} = {onGlobal.Value.CurrentValue}  [explicit: {globalProfile.Name}]");
            continue;
        }

        var available = DRSApi.EnumAvailableSettingValues(id);
        Console.WriteLine($"  {label} = {available.DefaultValue}  [driver default]");
    }
}
