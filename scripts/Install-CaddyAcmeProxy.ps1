<#
.SYNOPSIS
Installs Caddy as an ACME HTTPS reverse proxy for TFlexDrawingService.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File .\scripts\Install-CaddyAcmeProxy.ps1 `
  -Domain "lehjke.online" `
  -Email "admin@example.com" `
  -UpstreamUrl "http://127.0.0.1:5011"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Domain,

    [string]$Email = "",
    [string]$UpstreamUrl = "http://127.0.0.1:5011",
    [string]$InstallDir = "C:\Services\Caddy",
    [string]$ServiceName = "Caddy",
    [switch]$SkipDownload,
    [switch]$SkipFirewall,
    [switch]$Staging
)

$ErrorActionPreference = "Stop"

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

function Resolve-FullPath {
    param([string]$Path)
    return [System.IO.Path]::GetFullPath($Path)
}

function ConvertTo-CaddyPath {
    param([string]$Path)
    return (Resolve-FullPath $Path).Replace("\", "/")
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

function Invoke-Native {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$Arguments = @()
    )

    Write-Host "Running: $FilePath $($Arguments -join ' ')" -ForegroundColor DarkGray
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($Arguments -join ' ')"
    }
}

function Set-Tls12 {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
}

function Install-CaddyBinary {
    param(
        [string]$CaddyExe,
        [string]$WorkDir
    )

    if ($SkipDownload) {
        if (-not (Test-Path -LiteralPath $CaddyExe)) {
            throw "Caddy executable was not found at '$CaddyExe'. Remove -SkipDownload or copy caddy.exe there."
        }

        return
    }

    Write-Step "Downloading Caddy"
    Set-Tls12
    $headers = @{ "User-Agent" = "TFlexDrawingServiceBootstrap" }
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/caddyserver/caddy/releases/latest" -Headers $headers
    $asset = $release.assets |
        Where-Object { $_.name -match "^caddy_.*_windows_amd64\.zip$" } |
        Select-Object -First 1

    if ($null -eq $asset) {
        throw "Could not find a windows_amd64 Caddy zip asset in the latest GitHub release."
    }

    $archivePath = Join-Path $WorkDir $asset.name
    $extractDir = Join-Path $WorkDir "caddy-extract"
    if (Test-Path -LiteralPath $extractDir) {
        Remove-Item -LiteralPath $extractDir -Recurse -Force
    }

    New-Item -ItemType Directory -Path $extractDir -Force | Out-Null
    Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $archivePath -Headers $headers -UseBasicParsing
    Expand-Archive -LiteralPath $archivePath -DestinationPath $extractDir -Force

    $downloadedExe = Get-ChildItem -LiteralPath $extractDir -Filter "caddy.exe" -Recurse |
        Select-Object -First 1

    if ($null -eq $downloadedExe) {
        throw "Downloaded Caddy archive did not contain caddy.exe."
    }

    Copy-Item -LiteralPath $downloadedExe.FullName -Destination $CaddyExe -Force
}

function Write-Caddyfile {
    param(
        [string]$Path,
        [string]$StorageDir,
        [string]$LogFile
    )

    $lines = @(
        "{"
    )

    if (-not [string]::IsNullOrWhiteSpace($Email)) {
        $lines += "    email $Email"
    }

    if ($Staging) {
        $lines += "    acme_ca https://acme-staging-v02.api.letsencrypt.org/directory"
    }

    $lines += "    storage file_system `"$(ConvertTo-CaddyPath $StorageDir)`""
    $lines += "}"
    $lines += ""
    $lines += "$Domain {"
    $lines += "    encode gzip"
    $lines += ""
    $lines += "    log {"
    $lines += "        output file `"$(ConvertTo-CaddyPath $LogFile)`" {"
    $lines += "            roll_size 10MiB"
    $lines += "            roll_keep 10"
    $lines += "            roll_keep_for 720h"
    $lines += "        }"
    $lines += "    }"
    $lines += ""
    $lines += "    reverse_proxy $UpstreamUrl"
    $lines += "}"
    $lines += ""

    ($lines -join [Environment]::NewLine) | Set-Content -LiteralPath $Path -Encoding ASCII
}

function Ensure-FirewallRule {
    param(
        [string]$DisplayName,
        [int]$Port
    )

    $rule = Get-NetFirewallRule -DisplayName $DisplayName -ErrorAction SilentlyContinue
    if ($null -eq $rule) {
        New-NetFirewallRule `
            -DisplayName $DisplayName `
            -Direction Inbound `
            -Protocol TCP `
            -LocalPort $Port `
            -Action Allow | Out-Null
    }
    else {
        Set-NetFirewallRule -DisplayName $DisplayName -Enabled True -Action Allow | Out-Null
    }
}

function Assert-PortAvailable {
    param([int]$Port)

    $listeners = Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue
    if ($null -eq $listeners) {
        return
    }

    $owners = $listeners |
        Select-Object -ExpandProperty OwningProcess -Unique |
        ForEach-Object {
            $process = Get-Process -Id $_ -ErrorAction SilentlyContinue
            if ($null -eq $process) {
                "PID $_"
            }
            else {
                "$($process.ProcessName) (PID $_)"
            }
        }

    throw "Port $Port is already in use by $($owners -join ', '). Move TFlexDrawingService.Api to an internal port, for example -Urls 'http://127.0.0.1:5011', then run this script again."
}

function Install-OrUpdateCaddyService {
    param(
        [string]$CaddyExe,
        [string]$Caddyfile
    )

    $binaryPath = "`"$CaddyExe`" run --config `"$Caddyfile`" --adapter caddyfile"

    if (Test-ServiceExists $ServiceName) {
        Invoke-Sc @("config", $ServiceName, "binPath=", $binaryPath, "DisplayName=", "Caddy ACME Reverse Proxy", "start=", "auto")
    }
    else {
        Invoke-Sc @("create", $ServiceName, "binPath=", $binaryPath, "DisplayName=", "Caddy ACME Reverse Proxy", "start=", "auto")
    }

    Invoke-Sc @("failure", $ServiceName, "reset=", "86400", "actions=", "restart/60000/restart/60000/restart/60000")
}

if ([System.Environment]::OSVersion.Platform -ne [System.PlatformID]::Win32NT) {
    throw "This installer is intended for Windows servers."
}

if (-not (Test-IsAdmin)) {
    throw "Run PowerShell as Administrator."
}

if ($Domain -match "^\s*$" -or $Domain -match "[/:]") {
    throw "Domain must be a hostname such as 'lehjke.online', not a URL."
}

if ($UpstreamUrl -notmatch "^https?://") {
    throw "UpstreamUrl must include http:// or https://."
}

$InstallDir = Resolve-FullPath $InstallDir
$configDir = Join-Path $InstallDir "config"
$dataDir = Join-Path $InstallDir "data"
$logsDir = Join-Path $InstallDir "logs"
$workDir = Join-Path $InstallDir "_download"
$caddyExe = Join-Path $InstallDir "caddy.exe"
$caddyfile = Join-Path $configDir "Caddyfile"
$accessLog = Join-Path $logsDir "access.log"

Write-Step "Preparing directories"
New-Item -ItemType Directory -Path $InstallDir, $configDir, $dataDir, $logsDir, $workDir -Force | Out-Null

Write-Step "Stopping Caddy service if it exists"
Stop-ServiceIfExists $ServiceName

Install-CaddyBinary -CaddyExe $caddyExe -WorkDir $workDir

Write-Step "Checking public ports"
Assert-PortAvailable 80
Assert-PortAvailable 443

Write-Step "Writing Caddyfile"
Write-Caddyfile -Path $caddyfile -StorageDir $dataDir -LogFile $accessLog

Write-Step "Validating Caddy configuration"
Invoke-Native -FilePath $caddyExe -Arguments @("validate", "--config", $caddyfile, "--adapter", "caddyfile")

if (-not $SkipFirewall) {
    Write-Step "Configuring firewall"
    Ensure-FirewallRule -DisplayName "Caddy HTTP 80" -Port 80
    Ensure-FirewallRule -DisplayName "Caddy HTTPS 443" -Port 443
}

Write-Step "Installing Caddy Windows service"
Install-OrUpdateCaddyService -CaddyExe $caddyExe -Caddyfile $caddyfile

Write-Step "Starting Caddy"
Start-Service -Name $ServiceName
Start-Sleep -Seconds 5

$publicHealthUrl = "https://$Domain/api/health"
Write-Step "Checking HTTPS endpoint"
$healthOk = $false
for ($i = 1; $i -le 12; $i++) {
    try {
        Invoke-WebRequest -Uri $publicHealthUrl -UseBasicParsing -TimeoutSec 15 | Out-Null
        $healthOk = $true
        break
    }
    catch {
        Start-Sleep -Seconds 5
    }
}

if ($healthOk) {
    Write-Host "HTTPS health check passed: $publicHealthUrl" -ForegroundColor Green
}
else {
    Write-Warning "Caddy started, but HTTPS health check did not pass from this machine. Check DNS, ports 80/443, Caddy logs, and upstream API."
}

Write-Host ""
Write-Host "Caddy installed to: $InstallDir" -ForegroundColor Green
Write-Host "Caddyfile: $caddyfile" -ForegroundColor Green
Write-Host "Proxy: https://$Domain -> $UpstreamUrl" -ForegroundColor Green
