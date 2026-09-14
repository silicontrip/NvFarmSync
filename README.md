# Nv Configuration

## NvFarmSync
```
Description:
  NvFarm -- NVIDIA driver settings fleet management

Usage:
  NvFarmSync [command] [options]

Options:
  -?, -h, --help  Show help and usage information
  --version       Show version information

Commands:
  get         NvAPI_DRS_GetSetting -- read a profile's full effective settings to an INI file (stdout)
  set <file>  NvAPI_DRS_SetSetting -- stage an INI file's values in this session; add --save to persist them
```

## NvMosaic
```
Description:
  NvMosaic -- NVIDIA Mosaic display topology fleet management

Usage:
  NvMosaic [command] [options]

Options:
  -?, -h, --help  Show help and usage information
  --version       Show version information

Commands:
  list             NvAPI_Mosaic_EnumDisplayGrids -- list the current display grid topology (JSON, stdout)
  get              NvAPI_Mosaic_GetCurrentTopology -- get the current topology brief and overlap (JSON, stdout)
  set <file>       NvAPI_Mosaic_SetDisplayGrids -- apply a grid topology from a JSON file
  validate <file>  NvAPI_Mosaic_ValidateDisplayGrids -- validate a grid topology from a JSON file without applying it
```
