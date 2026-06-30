# TFlexAutomationRunner

`TFlexAutomationRunner.exe` is the Windows process launched by
`ExternalProcessTFlexAutomationClient`.

Build:

```powershell
dotnet build .\src\TFlexAutomationRunner\TFlexAutomationRunner.csproj -c Release
```

Configure the worker:

```json
{
  "TFlexAutomation": {
    "Mode": "ExternalProcess",
    "CommandPath": "C:\\Path\\To\\TFlexAutomationRunner.exe",
    "Arguments": "\"{requestPath}\" \"{responsePath}\"",
    "TimeoutSeconds": 600,
    "WriteParameterFile": true
  }
}
```

The runner uses the local T-FLEX CAD 17 Open API. By default it searches
the registry and `C:\Program Files\T-FLEX CAD 17\Program`. If T-FLEX is
installed elsewhere, set:

```powershell
$env:TFLEX_CAD_PROGRAM_DIR = "D:\Apps\T-FLEX CAD 17\Program"
```

The implementation uses these local API members verified from the installed
T-FLEX CAD 17 assemblies and samples:

- `TFlex.Application.InitSession(...)` and `ExitSession()`
- `TFlex.Application.OpenDocument(path, visible: false, readOnly: false)`
- `Document.FindVariable(...)`, `Document.GetVariables()`
- `Variable.Expression`, `Variable.RealValue`, `Variable.TextValue`
- `Document.Regenerate(new RegenerateOptions { ... })`
- `ExportToPDF.Export(...)`, `ExportToDWG.Export(...)`, `ExportToDXF.Export(...)`

Inspect template controls and variables:

```powershell
dotnet run --project .\src\TFlexAutomationRunner\TFlexAutomationRunner.csproj -c Release -- `
  --inspect-controls "C:\Shared\LEHY-L-PRO [320-1050].grb" ".\docs\inspect-320-1050-controls.json"

dotnet run --project .\src\TFlexAutomationRunner\TFlexAutomationRunner.csproj -c Release -- `
  --inspect-variables "C:\Shared\LEHY-L-PRO [320-1050].grb" ".\docs\inspect-320-1050-variables.json"
```

To inspect calculated values after applying input parameters, pass a JSON object
as the optional fourth argument:

```powershell
dotnet run --project .\src\TFlexAutomationRunner\TFlexAutomationRunner.csproj -c Release -- `
  --inspect-variables "C:\Shared\LEHY-PRO [REAR CWT].grb" ".\docs\inspect-rear-variables.json" ".\parameters.json"
```

Use absolute template paths for inspection. The T-FLEX API bootstrap switches
the current directory to the T-FLEX program folder while loading native
dependencies.
