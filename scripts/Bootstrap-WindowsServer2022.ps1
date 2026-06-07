<#
.SYNOPSIS
Bootstraps a clean Windows Server 2022 host for TFlexDrawingService.

.DESCRIPTION
Installs missing build/runtime prerequisites, verifies T-FLEX CAD Open API,
downloads or reuses the repository installer, then publishes and installs
TFlexDrawingService.Api and TFlexDrawingService.Worker as Windows services.

T-FLEX CAD itself is not installed by this script because it is licensed
software. Install and activate T-FLEX CAD first, then pass -TFlexCadProgramDir.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File .\scripts\Bootstrap-WindowsServer2022.ps1 `
  -RepositoryUrl "https://github.com/lehjke/tflex-backend-service.git" `
  -Branch "main" `
  -InstallRoot "C:\Services\TFlexDrawingService" `
  -TFlexCadProgramDir "C:\Program Files\T-FLEX CAD 17\Program"
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
    [string]$ServiceUser = "",
    [string]$ServicePassword = "",
    [string]$DotNetChannel = "10.0",
    [string]$DotNetQuality = "GA",
    [string]$DotNetInstallDir = "$env:ProgramFiles\dotnet",
    [string]$InstallerScriptUrl = "https://raw.githubusercontent.com/lehjke/tflex-backend-service/main/scripts/Install-TFlexDrawingService.ps1",
    [string]$WorkDir = "C:\Temp\TFlexDrawingServiceBootstrap",
    [switch]$SkipGitInstall,
    [switch]$SkipDotNetInstall,
    [switch]$SkipNetFx472DeveloperPackInstall,
    [switch]$SkipRunnerBuild,
    [switch]$SkipServiceInstall,
    [switch]$SkipFirewall,
    [switch]$SkipTFlexCheck
)

$ErrorActionPreference = "Stop"

$NetFx472DeveloperPackUrl = "https://go.microsoft.com/fwlink/?linkid=874338"
$DotNetInstallScriptUrl = "https://dot.net/v1/dotnet-install.ps1"

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Test-IsAdmin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Invoke-Native {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$Arguments = @(),
        [int[]]$AllowedExitCodes = @(0)
    )

    Write-Host "Running: $FilePath $($Arguments -join ' ')" -ForegroundColor DarkGray
    & $FilePath @Arguments
    $exitCode = $LASTEXITCODE
    if ($AllowedExitCodes -notcontains $exitCode) {
        throw "Command failed with exit code ${exitCode}: $FilePath $($Arguments -join ' ')"
    }

    return $exitCode
}

function Set-Tls12 {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
}

function Add-MachinePath {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $machinePath = [Environment]::GetEnvironmentVariable("Path", "Machine")
    $parts = $machinePath -split ";" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    if ($parts -notcontains $Path) {
        [Environment]::SetEnvironmentVariable("Path", ($parts + $Path) -join ";", "Machine")
    }

    $processParts = $env:Path -split ";" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    if ($processParts -notcontains $Path) {
        $env:Path = ($processParts + $Path) -join ";"
    }
}

function Test-DotNetSdkChannel {
    param([string]$Channel)

    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $dotnet) {
        return $false
    }

    $sdks = & dotnet --list-sdks 2>$null
    return [bool]($sdks | Where-Object { $_ -like "$Channel.*" })
}

function Install-DotNetSdk {
    New-Item -ItemType Directory -Path $WorkDir, $DotNetInstallDir -Force | Out-Null
    $scriptPath = Join-Path $WorkDir "dotnet-install.ps1"
    Invoke-WebRequest -Uri $DotNetInstallScriptUrl -OutFile $scriptPath -UseBasicParsing

    $arguments = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", $scriptPath,
        "-Channel", $DotNetChannel,
        "-Architecture", "x64",
        "-InstallDir", $DotNetInstallDir
    )

    if (-not [string]::IsNullOrWhiteSpace($DotNetQuality)) {
        $arguments += @("-Quality", $DotNetQuality)
    }

    $exitCode = Invoke-Native -FilePath "powershell.exe" -Arguments $arguments -AllowedExitCodes @(0, 3010)
    Add-MachinePath $DotNetInstallDir
    [Environment]::SetEnvironmentVariable("DOTNET_ROOT", $DotNetInstallDir, "Machine")
    $env:DOTNET_ROOT = $DotNetInstallDir

    if ($exitCode -eq 3010) {
        Write-Warning ".NET SDK installer requested a reboot. Continue only if dotnet commands work in this session."
    }
}

function Install-GitWithWinget {
    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if ($null -eq $winget) {
        return $false
    }

    Invoke-Native -FilePath $winget.Source -Arguments @(
        "install",
        "--id", "Git.Git",
        "--exact",
        "--source", "winget",
        "--accept-package-agreements",
        "--accept-source-agreements",
        "--silent"
    ) -AllowedExitCodes @(0, -1978335189)

    return $true
}

function Install-GitFromGitHub {
    New-Item -ItemType Directory -Path $WorkDir -Force | Out-Null
    $headers = @{ "User-Agent" = "TFlexDrawingServiceBootstrap" }
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/git-for-windows/git/releases/latest" -Headers $headers
    $asset = $release.assets |
        Where-Object { $_.name -match "^Git-.*-64-bit\.exe$" } |
        Select-Object -First 1

    if ($null -eq $asset) {
        throw "Could not find a 64-bit Git for Windows installer in the latest GitHub release."
    }

    $installerPath = Join-Path $WorkDir $asset.name
    Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $installerPath -Headers $headers -UseBasicParsing
    Invoke-Native -FilePath $installerPath -Arguments @(
        "/VERYSILENT",
        "/NORESTART",
        "/NOCANCEL",
        "/SP-",
        "/CLOSEAPPLICATIONS"
    ) -AllowedExitCodes @(0, 3010)
}

function Install-Git {
    if (Get-Command git -ErrorAction SilentlyContinue) {
        return
    }

    if (-not (Install-GitWithWinget)) {
        Install-GitFromGitHub
    }

    Add-MachinePath "C:\Program Files\Git\cmd"
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        throw "Git was installed, but git.exe is still not available in PATH. Reopen PowerShell or check installation."
    }
}

function Test-NetFx472Runtime {
    $key = "HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"
    $release = (Get-ItemProperty -Path $key -Name Release -ErrorAction SilentlyContinue).Release
    return ($release -ge 461808)
}

function Test-NetFx472TargetingPack {
    $path = "${env:ProgramFiles(x86)}\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2"
    return Test-Path -LiteralPath $path
}

function Install-NetFx472DeveloperPack {
    if (Test-NetFx472TargetingPack) {
        return
    }

    New-Item -ItemType Directory -Path $WorkDir -Force | Out-Null
    $installerPath = Join-Path $WorkDir "NDP472-DevPack-ENU.exe"
    Invoke-WebRequest -Uri $NetFx472DeveloperPackUrl -OutFile $installerPath -UseBasicParsing
    $exitCode = Invoke-Native -FilePath $installerPath -Arguments @("/quiet", "/norestart") -AllowedExitCodes @(0, 3010, 1641)
    if ($exitCode -eq 3010 -or $exitCode -eq 1641) {
        Write-Warning ".NET Framework 4.7.2 Developer Pack requested a reboot."
    }

    if (-not (Test-NetFx472TargetingPack)) {
        throw ".NET Framework 4.7.2 targeting pack was not found after installation."
    }
}

function Assert-TFlexApi {
    if ($SkipRunnerBuild -or $SkipTFlexCheck) {
        return
    }

    $required = @("TFlexAPI.dll", "TFlexAPI3D.dll")
    foreach ($file in $required) {
        $path = Join-Path $TFlexCadProgramDir $file
        if (-not (Test-Path -LiteralPath $path)) {
            throw "T-FLEX Open API file was not found: $path. Install T-FLEX CAD or pass -TFlexCadProgramDir."
        }
    }
}

function Get-InstallerScriptPath {
    $localPath = Join-Path $PSScriptRoot "Install-TFlexDrawingService.ps1"
    if (Test-Path -LiteralPath $localPath) {
        return $localPath
    }

    New-Item -ItemType Directory -Path $WorkDir -Force | Out-Null
    $downloadPath = Join-Path $WorkDir "Install-TFlexDrawingService.ps1"
    Invoke-WebRequest -Uri $InstallerScriptUrl -OutFile $downloadPath -UseBasicParsing
    return $downloadPath
}

function Invoke-ServiceInstaller {
    $installerPath = Get-InstallerScriptPath
    $arguments = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", $installerPath,
        "-RepositoryUrl", $RepositoryUrl,
        "-Branch", $Branch,
        "-InstallRoot", $InstallRoot,
        "-Urls", $Urls,
        "-TFlexCadProgramDir", $TFlexCadProgramDir
    )

    if (-not [string]::IsNullOrWhiteSpace($SourceRoot)) {
        $arguments += @("-SourceRoot", $SourceRoot)
    }

    if (-not [string]::IsNullOrWhiteSpace($TFlexAutomationCommandPath)) {
        $arguments += @("-TFlexAutomationCommandPath", $TFlexAutomationCommandPath)
    }

    if (-not [string]::IsNullOrWhiteSpace($ServiceUser)) {
        $arguments += @("-ServiceUser", $ServiceUser)
    }

    if (-not [string]::IsNullOrWhiteSpace($ServicePassword)) {
        $arguments += @("-ServicePassword", $ServicePassword)
    }

    if ($SkipRunnerBuild) {
        $arguments += "-SkipRunnerBuild"
    }

    if ($SkipServiceInstall) {
        $arguments += "-SkipServiceInstall"
    }

    if ($SkipFirewall) {
        $arguments += "-SkipFirewall"
    }

    Invoke-Native -FilePath "powershell.exe" -Arguments $arguments
}

if ([System.Environment]::OSVersion.Platform -ne [System.PlatformID]::Win32NT) {
    throw "This bootstrap script is intended for Windows Server 2022."
}

if (-not (Test-IsAdmin)) {
    throw "Run PowerShell as Administrator."
}

Set-Tls12
New-Item -ItemType Directory -Path $WorkDir -Force | Out-Null

Write-Step "Installing prerequisites"
if (-not $SkipGitInstall) {
    Install-Git
}

if (-not $SkipDotNetInstall -and -not (Test-DotNetSdkChannel $DotNetChannel)) {
    Install-DotNetSdk
}

if (-not (Test-DotNetSdkChannel $DotNetChannel)) {
    throw ".NET SDK channel $DotNetChannel was not found. Install it or remove -SkipDotNetInstall."
}

if (-not (Test-NetFx472Runtime)) {
    throw ".NET Framework 4.7.2 or newer runtime was not detected. Windows Server 2022 normally includes .NET Framework 4.8."
}

if (-not $SkipNetFx472DeveloperPackInstall) {
    Install-NetFx472DeveloperPack
}

if (-not $SkipRunnerBuild -and -not (Test-NetFx472TargetingPack)) {
    throw ".NET Framework 4.7.2 targeting pack is required to build TFlexAutomationRunner. Install the Developer Pack or pass -SkipRunnerBuild with a prebuilt runner."
}

Write-Step "Checking T-FLEX Open API"
Assert-TFlexApi

Write-Step "Installing TFlexDrawingService"
Invoke-ServiceInstaller

Write-Step "Final health check"
$healthUri = "http://localhost:5011/api/templates"
if ($Urls -match "https?://[^:]+:(\d+)") {
    $healthUri = "http://localhost:$($Matches[1])/api/templates"
}

try {
    $response = Invoke-WebRequest -Uri $healthUri -UseBasicParsing -TimeoutSec 20
    Write-Host "Health check passed: $healthUri ($($response.StatusCode))" -ForegroundColor Green
}
catch {
    Write-Warning "Install completed, but health check failed at $healthUri. Check services and logs."
}

Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "InstallRoot: $InstallRoot" -ForegroundColor Green
Write-Host "API URL: $Urls" -ForegroundColor Green
