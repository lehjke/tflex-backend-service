<#
.SYNOPSIS
Builds and installs TFlexDrawingService on a Windows server.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File .\scripts\Install-TFlexDrawingService.ps1 -InstallRoot C:\Services\TFlexDrawingService
#>
[CmdletBinding()]
param(
    [string]$RepositoryUrl = "https://github.com/lehjke/tflex-backend-service.git",
    [string]$Branch = "main",
    [string]$InstallRoot = "C:\Services\TFlexDrawingService",
    [string]$SourceRoot = "",
    [string]$Urls = "http://0.0.0.0:5011",
    [string]$TFlexCadProgramDir = "C:\Program Files\T-FLEX CAD 17\Program",
    [string]$TFlexAutomationCommandPath = "",
    [string]$ApiServiceName = "TFlexDrawingService.Api",
    [string]$WorkerServiceName = "TFlexDrawingService.Worker",
    [string]$ServiceUser = "",
    [string]$ServicePassword = "",
    [switch]$SkipServiceInstall,
    [switch]$SkipFirewall,
    [switch]$SkipRunnerBuild
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Resolve-FullPath {
    param([string]$Path)
    return [System.IO.Path]::GetFullPath($Path)
}

function Assert-Command {
    param([string]$Name)
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found in PATH."
    }
}

function Test-IsAdmin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-PlainTextPassword {
    param([Security.SecureString]$SecurePassword)
    $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecurePassword)
    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr)
    }
    finally {
        if ($ptr -ne [IntPtr]::Zero) {
            [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr)
        }
    }
}

function Test-ServiceExists {
    param([string]$Name)
    return [bool](Get-Service -Name $Name -ErrorAction SilentlyContinue)
}

function Stop-ServiceIfExists {
    param([string]$Name)
    $service = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if ($null -eq $service) {
        return
    }

    if ($service.Status -ne "Stopped") {
        Stop-Service -Name $Name -Force -ErrorAction SilentlyContinue
        $service.WaitForStatus("Stopped", [TimeSpan]::FromSeconds(30))
    }
}

function Invoke-Sc {
    param([string[]]$Arguments)
    $output = & sc.exe @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "sc.exe $($Arguments -join ' ') failed: $output"
    }
}

function Install-OrUpdateService {
    param(
        [string]$Name,
        [string]$DisplayName,
        [string]$BinaryPath,
        [string]$User,
        [string]$Password
    )

    if (Test-ServiceExists $Name) {
        Invoke-Sc @("config", $Name, "binPath= $BinaryPath", "DisplayName= $DisplayName", "start= auto")
    }
    else {
        Invoke-Sc @("create", $Name, "binPath= $BinaryPath", "DisplayName= $DisplayName", "start= auto")
    }

    Invoke-Sc @("failure", $Name, "reset= 86400", "actions= restart/60000/restart/60000/restart/60000")

    if (-not [string]::IsNullOrWhiteSpace($User)) {
        if ([string]::IsNullOrWhiteSpace($Password)) {
            $securePassword = Read-Host "Password for service account $User" -AsSecureString
            $Password = Get-PlainTextPassword $securePassword
        }

        Invoke-Sc @("config", $Name, "obj= $User", "password= $Password")
    }
}

function Remove-DirectoryContents {
    param([string]$Path)
    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }

    New-Item -ItemType Directory -Path $Path -Force | Out-Null
}

function Copy-Directory {
    param(
        [string]$Source,
        [string]$Destination
    )

    if (-not (Test-Path -LiteralPath $Source)) {
        throw "Required directory '$Source' was not found."
    }

    Remove-DirectoryContents $Destination
    Copy-Item -Path (Join-Path $Source "*") -Destination $Destination -Recurse -Force
}

function Write-ProductionConfig {
    param(
        [string]$Directory,
        [string]$ProjectRoot,
        [string]$RunnerPath
    )

    $config = [ordered]@{
        Paths = [ordered]@{
            ProjectRoot = $ProjectRoot
        }
        TemplateCatalog = [ordered]@{
            ConfigPath = "templates\templates.json"
        }
        Storage = [ordered]@{
            RootPath = "storage"
            DatabasePath = "storage\drawings.db"
        }
        Queue = [ordered]@{
            PollIntervalSeconds = 1
        }
        TFlexAutomation = [ordered]@{
            Mode = "ExternalProcess"
            CommandPath = $RunnerPath
            Arguments = "`"{requestPath}`" `"{responsePath}`""
            TimeoutSeconds = 600
            WriteParameterFile = $true
        }
        Logging = [ordered]@{
            LogLevel = [ordered]@{
                Default = "Information"
                "Microsoft.AspNetCore" = "Warning"
                "Microsoft.Hosting.Lifetime" = "Information"
            }
        }
        AllowedHosts = "*"
    }

    $path = Join-Path $Directory "appsettings.Production.json"
    $config | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $path -Encoding UTF8
}

function Get-HealthUrl {
    param([string]$ServiceUrls)
    if ($ServiceUrls -match "https?://[^:]+:(\d+)") {
        return "http://localhost:$($Matches[1])/api/templates"
    }

    return "http://localhost:5011/api/templates"
}

function Get-ApiPort {
    param([string]$ServiceUrls)
    if ($ServiceUrls -match "https?://[^:]+:(\d+)") {
        return [int]$Matches[1]
    }

    return 5011
}

if ([System.Environment]::OSVersion.Platform -ne [System.PlatformID]::Win32NT) {
    throw "This installer is intended for Windows servers."
}

if (-not $SkipServiceInstall -or -not $SkipFirewall) {
    if (-not (Test-IsAdmin)) {
        throw "Run PowerShell as Administrator, or pass -SkipServiceInstall -SkipFirewall."
    }
}

Assert-Command "git"
Assert-Command "dotnet"

$InstallRoot = Resolve-FullPath $InstallRoot
if ([string]::IsNullOrWhiteSpace($SourceRoot)) {
    $SourceRoot = Join-Path $InstallRoot "_src"
}
$SourceRoot = Resolve-FullPath $SourceRoot

$apiDir = Join-Path $InstallRoot "Api"
$workerDir = Join-Path $InstallRoot "Worker"
$runnerDir = Join-Path $InstallRoot "Runner"
$templatesDir = Join-Path $InstallRoot "templates"
$storageDir = Join-Path $InstallRoot "storage"

if ([string]::IsNullOrWhiteSpace($TFlexAutomationCommandPath)) {
    $TFlexAutomationCommandPath = Join-Path $runnerDir "TFlexAutomationRunner.exe"
}
$TFlexAutomationCommandPath = Resolve-FullPath $TFlexAutomationCommandPath

Write-Step "Preparing directories"
New-Item -ItemType Directory -Path $InstallRoot, $storageDir -Force | Out-Null

if (-not $SkipServiceInstall) {
    Write-Step "Stopping services"
    Stop-ServiceIfExists $WorkerServiceName
    Stop-ServiceIfExists $ApiServiceName
}

Write-Step "Downloading source"
if (Test-Path -LiteralPath (Join-Path $SourceRoot ".git")) {
    Push-Location $SourceRoot
    try {
        git fetch origin $Branch
        git checkout $Branch
        git pull --ff-only origin $Branch
    }
    finally {
        Pop-Location
    }
}
elseif (Test-Path -LiteralPath $SourceRoot) {
    $children = Get-ChildItem -LiteralPath $SourceRoot -Force -ErrorAction SilentlyContinue
    if ($children.Count -gt 0) {
        throw "SourceRoot '$SourceRoot' exists and is not an empty git checkout."
    }

    git clone --branch $Branch $RepositoryUrl $SourceRoot
}
else {
    git clone --branch $Branch $RepositoryUrl $SourceRoot
}

Write-Step "Publishing API"
Remove-DirectoryContents $apiDir
dotnet publish (Join-Path $SourceRoot "src\TFlexDrawingService.Api\TFlexDrawingService.Api.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -o $apiDir

Write-Step "Publishing Worker"
Remove-DirectoryContents $workerDir
dotnet publish (Join-Path $SourceRoot "src\TFlexDrawingService.Worker\TFlexDrawingService.Worker.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -o $workerDir

if (-not $SkipRunnerBuild) {
    Write-Step "Publishing T-FLEX automation runner"
    if (-not (Test-Path -LiteralPath (Join-Path $TFlexCadProgramDir "TFlexAPI.dll"))) {
        throw "TFlexAPI.dll was not found in '$TFlexCadProgramDir'. Install T-FLEX CAD or pass -TFlexCadProgramDir."
    }

    Remove-DirectoryContents $runnerDir
    dotnet publish (Join-Path $SourceRoot "src\TFlexAutomationRunner\TFlexAutomationRunner.csproj") `
        -c Release `
        -p:TFlexCadProgramDir="$TFlexCadProgramDir" `
        -o $runnerDir
}
elseif (-not (Test-Path -LiteralPath $TFlexAutomationCommandPath)) {
    throw "Runner build was skipped, but '$TFlexAutomationCommandPath' does not exist."
}

Write-Step "Copying templates"
Copy-Directory (Join-Path $SourceRoot "templates") $templatesDir

Write-Step "Writing production configuration"
Write-ProductionConfig $apiDir $InstallRoot $TFlexAutomationCommandPath
Write-ProductionConfig $workerDir $InstallRoot $TFlexAutomationCommandPath

if (-not $SkipFirewall) {
    Write-Step "Configuring firewall"
    $apiPort = Get-ApiPort $Urls
    $firewallRuleName = "TFlex Drawing API $apiPort"
    if (-not (Get-NetFirewallRule -DisplayName $firewallRuleName -ErrorAction SilentlyContinue)) {
        New-NetFirewallRule `
            -DisplayName $firewallRuleName `
            -Direction Inbound `
            -Protocol TCP `
            -LocalPort $apiPort `
            -Action Allow | Out-Null
    }
}

if (-not $SkipServiceInstall) {
    Write-Step "Installing Windows services"
    $apiExe = Join-Path $apiDir "TFlexDrawingService.Api.exe"
    $workerExe = Join-Path $workerDir "TFlexDrawingService.Worker.exe"

    Install-OrUpdateService `
        -Name $ApiServiceName `
        -DisplayName "T-FLEX Drawing Service API" `
        -BinaryPath "`"$apiExe`" --urls $Urls" `
        -User $ServiceUser `
        -Password $ServicePassword

    Install-OrUpdateService `
        -Name $WorkerServiceName `
        -DisplayName "T-FLEX Drawing Service Worker" `
        -BinaryPath "`"$workerExe`"" `
        -User $ServiceUser `
        -Password $ServicePassword

    Write-Step "Starting Windows services"
    Start-Service -Name $ApiServiceName
    Start-Service -Name $WorkerServiceName

    Start-Sleep -Seconds 3
    $healthUrl = Get-HealthUrl $Urls
    try {
        Invoke-WebRequest -Uri $healthUrl -UseBasicParsing -TimeoutSec 15 | Out-Null
        Write-Host "API health check passed: $healthUrl" -ForegroundColor Green
    }
    catch {
        Write-Warning "Services were started, but API health check failed at $healthUrl. Check Windows Event Viewer and service logs."
    }
}

Write-Host ""
Write-Host "Installed to: $InstallRoot" -ForegroundColor Green
Write-Host "API URL: $Urls" -ForegroundColor Green
Write-Host "Runner: $TFlexAutomationCommandPath" -ForegroundColor Green
