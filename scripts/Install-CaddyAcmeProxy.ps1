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
    [ValidateRange(1, 60)]
    [int]$HealthCheckAttempts = 12,
    [ValidateRange(1, 30)]
    [int]$HealthCheckDelaySeconds = 5,
    [switch]$SkipDownload,
    [switch]$SkipFirewall,
    [switch]$Staging
)

$ErrorActionPreference = "Stop"

# Reviewed release trust anchor. Update version, URL and digest together from the
# official Caddy release record; never discover the digest from the same runtime
# response used to download the archive.
$CaddyVersion = "2.11.4"
$CaddyArchiveUrl = "https://github.com/caddyserver/caddy/releases/download/v2.11.4/caddy_2.11.4_windows_amd64.zip"
$CaddyArchiveSha256 = "1708333f79e274c7697285afe6d592ab39314e0b131e9ec6bea08ad27df62ebf"
$CaddyArchiveAllowedHosts = @("github.com", "release-assets.githubusercontent.com")

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
        Stop-Service -Name $Name -Force -ErrorAction Stop
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

function Get-ServiceRegistryValueSnapshot {
    param(
        [string]$Name,
        [string]$ValueName
    )

    $serviceKeyPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$Name"
    $serviceKey = Get-Item -LiteralPath $serviceKeyPath -ErrorAction Stop
    if (@($serviceKey.GetValueNames()) -notcontains $ValueName) {
        return [pscustomobject]@{ Exists = $false; Kind = $null; Value = $null }
    }

    return [pscustomobject]@{
        Exists = $true
        Kind = $serviceKey.GetValueKind($ValueName)
        Value = $serviceKey.GetValue(
            $ValueName,
            $null,
            [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
    }
}

function Restore-ServiceRegistryValue {
    param(
        [string]$Name,
        [string]$ValueName,
        [object]$Snapshot
    )

    $serviceKeyPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$Name"
    if ($Snapshot.Exists) {
        New-ItemProperty `
            -LiteralPath $serviceKeyPath `
            -Name $ValueName `
            -PropertyType $Snapshot.Kind `
            -Value $Snapshot.Value `
            -Force | Out-Null
    }
    else {
        Remove-ItemProperty -LiteralPath $serviceKeyPath -Name $ValueName -ErrorAction SilentlyContinue
    }
}

function Get-CaddyServiceSnapshot {
    if (-not (Test-ServiceExists $ServiceName)) {
        return [pscustomobject]@{ Exists = $false; Name = $ServiceName; WasRunning = $false; ProcessId = 0 }
    }

    $escapedName = $ServiceName.Replace("'", "''")
    $service = Get-CimInstance -ClassName Win32_Service -Filter "Name='$escapedName'"
    if ($null -eq $service) {
        throw "Windows service '$ServiceName' could not be queried before the update."
    }

    return [pscustomobject]@{
        Exists = $true
        Name = $ServiceName
        PathName = [string]$service.PathName
        DisplayName = [string]$service.DisplayName
        StartMode = [string]$service.StartMode
        StartName = [string]$service.StartName
        WasRunning = ([string]$service.State -eq "Running")
        ProcessId = [int]$service.ProcessId
        Environment = Get-ServiceRegistryValueSnapshot $ServiceName "Environment"
        FailureActions = Get-ServiceRegistryValueSnapshot $ServiceName "FailureActions"
        FailureActionsOnNonCrashFailures = Get-ServiceRegistryValueSnapshot $ServiceName "FailureActionsOnNonCrashFailures"
        DelayedAutoStart = Get-ServiceRegistryValueSnapshot $ServiceName "DelayedAutoStart"
    }
}

function Restore-CaddyServiceSnapshot {
    param([object]$Snapshot)

    if (-not $Snapshot.Exists) {
        return
    }
    if (-not (Test-ServiceExists $Snapshot.Name)) {
        throw "Windows service '$($Snapshot.Name)' disappeared and cannot be restored."
    }

    $startMode = switch ($Snapshot.StartMode.ToLowerInvariant()) {
        "auto" { "auto" }
        "automatic" { "auto" }
        "manual" { "demand" }
        "disabled" { "disabled" }
        default { "demand" }
    }

    Invoke-Sc @(
        "config",
        $Snapshot.Name,
        "binPath=", $Snapshot.PathName,
        "DisplayName=", $Snapshot.DisplayName,
        "start=", $startMode
    )

    $escapedName = $Snapshot.Name.Replace("'", "''")
    $currentService = Get-CimInstance -ClassName Win32_Service -Filter "Name='$escapedName'"
    if ([string]$currentService.StartName -ine $Snapshot.StartName) {
        throw "Caddy service account changed unexpectedly; automatic rollback cannot restore its password."
    }

    Restore-ServiceRegistryValue $Snapshot.Name "Environment" $Snapshot.Environment
    Restore-ServiceRegistryValue $Snapshot.Name "FailureActions" $Snapshot.FailureActions
    Restore-ServiceRegistryValue `
        $Snapshot.Name `
        "FailureActionsOnNonCrashFailures" `
        $Snapshot.FailureActionsOnNonCrashFailures
    Restore-ServiceRegistryValue $Snapshot.Name "DelayedAutoStart" $Snapshot.DelayedAutoStart
}

function Invoke-Native {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$Arguments = @()
    )

    Write-Host "Running: $FilePath" -ForegroundColor DarkGray
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Native command '$FilePath' failed with exit code $LASTEXITCODE."
    }
}

function Set-Tls12 {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
}

function Invoke-VerifiedDownload {
    param(
        [Parameter(Mandatory = $true)][string]$Uri,
        [Parameter(Mandatory = $true)][string]$DestinationPath,
        [Parameter(Mandatory = $true)]
        [ValidatePattern("^[0-9a-fA-F]{64}$")]
        [string]$ExpectedSha256,
        [Parameter(Mandatory = $true)][string]$ArtifactName,
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string[]]$AllowedHosts
    )

    $parsedUri = $null
    if (-not [Uri]::TryCreate($Uri, [UriKind]::Absolute, [ref]$parsedUri) -or
        $parsedUri.Scheme -ne [Uri]::UriSchemeHttps -or
        -not $parsedUri.IsDefaultPort -or
        -not [string]::IsNullOrEmpty($parsedUri.UserInfo)) {
        throw "Verified download '$ArtifactName' must use an absolute HTTPS URL without credentials or a custom port."
    }

    $normalizedAllowedHosts = @(
        $AllowedHosts | ForEach-Object {
            if ([string]::IsNullOrWhiteSpace($_)) {
                throw "Verified download '$ArtifactName' contains an empty allowed host."
            }

            $_.Trim().TrimEnd([char]'.').ToLowerInvariant()
        }
    )
    $requestedHost = $parsedUri.DnsSafeHost.TrimEnd([char]'.').ToLowerInvariant()
    if ($normalizedAllowedHosts -notcontains $requestedHost) {
        throw "Verified download '$ArtifactName' uses a source host outside its allowlist."
    }

    $destinationDirectory = Split-Path -Parent $DestinationPath
    if (-not [string]::IsNullOrWhiteSpace($destinationDirectory)) {
        New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
    }

    try {
        $downloadResponse = Invoke-WebRequest `
            -Uri $parsedUri.AbsoluteUri `
            -OutFile $DestinationPath `
            -PassThru `
            -MaximumRedirection 5 `
            -Headers @{ "User-Agent" = "TFlexDrawingServiceBootstrap" } `
            -UseBasicParsing

        $finalUri = $null
        if ($null -ne $downloadResponse.BaseResponse) {
            $finalUri = $downloadResponse.BaseResponse.ResponseUri
            if ($null -eq $finalUri -and
                $null -ne $downloadResponse.BaseResponse.RequestMessage) {
                $finalUri = $downloadResponse.BaseResponse.RequestMessage.RequestUri
            }
        }
        if ($null -eq $finalUri) {
            throw "Verified download '$ArtifactName' could not determine the final response URL."
        }

        $finalUri = [Uri]$finalUri
        $finalHost = $finalUri.DnsSafeHost.TrimEnd([char]'.').ToLowerInvariant()
        if ($finalUri.Scheme -ne [Uri]::UriSchemeHttps -or
            -not $finalUri.IsDefaultPort -or
            -not [string]::IsNullOrEmpty($finalUri.UserInfo) -or
            $normalizedAllowedHosts -notcontains $finalHost) {
            throw "Verified download '$ArtifactName' ended at a URL outside its HTTPS host allowlist."
        }

        if (-not (Test-Path -LiteralPath $DestinationPath -PathType Leaf)) {
            throw "Verified download '$ArtifactName' did not produce a file."
        }

        $actualSha256 = (Get-FileHash -LiteralPath $DestinationPath -Algorithm SHA256).Hash
        if (-not [string]::Equals(
                $actualSha256,
                $ExpectedSha256,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw "Verified download '$ArtifactName' failed SHA-256 validation."
        }
    }
    catch {
        Remove-Item -LiteralPath $DestinationPath -Force -ErrorAction SilentlyContinue
        throw
    }
}

function Install-CaddyBinary {
    param(
        [string]$CaddyExe,
        [string]$WorkDir
    )

    if ($SkipDownload) {
        $existingCaddyExe = Join-Path $InstallDir "caddy.exe"
        if (-not (Test-Path -LiteralPath $existingCaddyExe -PathType Leaf)) {
            throw "Caddy executable was not found at '$existingCaddyExe'. Remove -SkipDownload or copy caddy.exe there."
        }

        Copy-Item -LiteralPath $existingCaddyExe -Destination $CaddyExe -Force
        return
    }

    Write-Step "Downloading Caddy"
    Set-Tls12
    $archivePath = Join-Path $WorkDir "caddy_$($CaddyVersion)_windows_amd64.zip"
    $extractDir = Join-Path $WorkDir "caddy-extract"
    if (Test-Path -LiteralPath $extractDir) {
        Remove-Item -LiteralPath $extractDir -Recurse -Force
    }

    New-Item -ItemType Directory -Path $extractDir -Force | Out-Null
    Invoke-VerifiedDownload `
        -Uri $CaddyArchiveUrl `
        -DestinationPath $archivePath `
        -ExpectedSha256 $CaddyArchiveSha256 `
        -ArtifactName "Caddy $CaddyVersion windows_amd64 archive" `
        -AllowedHosts $CaddyArchiveAllowedHosts
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
        return $true
    }

    foreach ($existingRule in @($rule)) {
        if ([string]$existingRule.Enabled -ne "True" -or [string]$existingRule.Action -ne "Allow") {
            throw "Existing firewall rule '$DisplayName' is not enabled with Allow action. Correct it explicitly before running the installer."
        }
    }

    return $false
}

function Assert-PortAvailable {
    param(
        [int]$Port,
        [int]$AllowedProcessId = 0
    )

    $listeners = Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue
    if ($null -eq $listeners) {
        return
    }

    $unexpectedListeners = @($listeners | Where-Object {
        $AllowedProcessId -le 0 -or $_.OwningProcess -ne $AllowedProcessId
    })
    if ($unexpectedListeners.Count -eq 0) {
        return
    }

    $owners = $unexpectedListeners |
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

function Invoke-PublicHealthCheck {
    param([string]$Url)

    $previousCertificateCallback = [Net.ServicePointManager]::ServerCertificateValidationCallback
    if ($Staging) {
        # ACME staging certificates are intentionally not trusted by Windows.
        [Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
    }

    try {
        $lastError = ""
        for ($attempt = 1; $attempt -le $HealthCheckAttempts; $attempt++) {
            try {
                $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 15
                if ($response.StatusCode -eq 200) {
                    return
                }
                $lastError = "HTTP $($response.StatusCode)"
            }
            catch {
                $lastError = $_.Exception.Message
            }

            if ($attempt -lt $HealthCheckAttempts) {
                Start-Sleep -Seconds $HealthCheckDelaySeconds
            }
        }

        throw "HTTPS health check failed after $HealthCheckAttempts attempt(s): $lastError"
    }
    finally {
        [Net.ServicePointManager]::ServerCertificateValidationCallback = $previousCertificateCallback
    }
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
$caddyExe = Join-Path $InstallDir "caddy.exe"
$caddyfile = Join-Path $configDir "Caddyfile"
$accessLog = Join-Path $logsDir "access.log"
$deploymentId = [Guid]::NewGuid().ToString("N")
$deploymentRoot = Join-Path $InstallDir "_deployment"
$stageRoot = Join-Path $deploymentRoot "staging-$deploymentId"
$rollbackRoot = Join-Path $deploymentRoot "rollback-$deploymentId"
$stageCaddyExe = Join-Path $stageRoot "caddy.exe"
$stageCaddyfile = Join-Path $stageRoot "Caddyfile"
$stageWorkDir = Join-Path $stageRoot "download"
$backupCaddyExe = Join-Path $rollbackRoot "caddy.exe"
$backupCaddyfile = Join-Path $rollbackRoot "Caddyfile"
$publicHealthUrl = "https://$Domain/api/health/ready"
$createdFirewallRules = @()
$deploymentSucceeded = $false
$rollbackSucceeded = $true
$activationStarted = $false
$promotedCaddyExe = $false
$promotedCaddyfile = $false
$backedUpCaddyExe = $false
$backedUpCaddyfile = $false

Write-Step "Preparing directories"
New-Item `
    -ItemType Directory `
    -Path $InstallDir, $configDir, $dataDir, $logsDir, $deploymentRoot, $stageRoot, $stageWorkDir `
    -Force | Out-Null

$serviceSnapshot = Get-CaddyServiceSnapshot

try {
    Write-Step "Preparing staged Caddy binary"
    Install-CaddyBinary -CaddyExe $stageCaddyExe -WorkDir $stageWorkDir

    Write-Step "Writing staged Caddyfile"
    Write-Caddyfile -Path $stageCaddyfile -StorageDir $dataDir -LogFile $accessLog

    Write-Step "Validating staged Caddy binary and configuration"
    Invoke-Native -FilePath $stageCaddyExe -Arguments @("version")
    Invoke-Native -FilePath $stageCaddyExe -Arguments @(
        "validate",
        "--config", $stageCaddyfile,
        "--adapter", "caddyfile"
    )

    Write-Step "Checking public ports"
    $allowedProcessId = if ($serviceSnapshot.WasRunning) { $serviceSnapshot.ProcessId } else { 0 }
    Assert-PortAvailable -Port 80 -AllowedProcessId $allowedProcessId
    Assert-PortAvailable -Port 443 -AllowedProcessId $allowedProcessId

    New-Item -ItemType Directory -Path $rollbackRoot -Force | Out-Null

    Write-Step "Stopping Caddy only after staged validation"
    $activationStarted = $true
    Stop-ServiceIfExists $ServiceName

    Write-Step "Activating staged Caddy deployment"
    if (Test-Path -LiteralPath $caddyExe -PathType Leaf) {
        Move-Item -LiteralPath $caddyExe -Destination $backupCaddyExe
        $backedUpCaddyExe = $true
    }
    if (Test-Path -LiteralPath $caddyfile -PathType Leaf) {
        Move-Item -LiteralPath $caddyfile -Destination $backupCaddyfile
        $backedUpCaddyfile = $true
    }

    Move-Item -LiteralPath $stageCaddyExe -Destination $caddyExe
    $promotedCaddyExe = $true
    Move-Item -LiteralPath $stageCaddyfile -Destination $caddyfile
    $promotedCaddyfile = $true

    if (-not $SkipFirewall) {
        Write-Step "Configuring firewall"
        if (Ensure-FirewallRule -DisplayName "Caddy HTTP 80" -Port 80) {
            $createdFirewallRules += "Caddy HTTP 80"
        }
        if (Ensure-FirewallRule -DisplayName "Caddy HTTPS 443" -Port 443) {
            $createdFirewallRules += "Caddy HTTPS 443"
        }
    }

    Write-Step "Installing Caddy Windows service"
    Install-OrUpdateCaddyService -CaddyExe $caddyExe -Caddyfile $caddyfile

    Write-Step "Starting Caddy"
    Start-Service -Name $ServiceName
    (Get-Service -Name $ServiceName).WaitForStatus("Running", [TimeSpan]::FromSeconds(30))

    Write-Step "Checking HTTPS endpoint"
    Invoke-PublicHealthCheck $publicHealthUrl

    Start-Sleep -Seconds 5
    if ((Get-Service -Name $ServiceName).Status -ne "Running") {
        throw "Caddy service stopped after the HTTPS smoke check."
    }

    $deploymentSucceeded = $true
}
catch {
    $deploymentError = $_.Exception.Message
    if (-not $activationStarted) {
        throw "Caddy staging or validation failed before the live service was changed: $deploymentError"
    }

    Write-Warning "Caddy deployment failed. Restoring the previous binary, configuration, and service state."

    try { Stop-ServiceIfExists $ServiceName } catch { $rollbackSucceeded = $false }

    if (-not $serviceSnapshot.Exists -and (Test-ServiceExists $ServiceName)) {
        try { Invoke-Sc @("delete", $ServiceName) } catch { $rollbackSucceeded = $false }
    }
    elseif ($serviceSnapshot.Exists) {
        try { Restore-CaddyServiceSnapshot $serviceSnapshot } catch { $rollbackSucceeded = $false }
    }

    try {
        if ($promotedCaddyExe -and (Test-Path -LiteralPath $caddyExe)) {
            Remove-Item -LiteralPath $caddyExe -Force
        }
        if ($backedUpCaddyExe -and (Test-Path -LiteralPath $backupCaddyExe)) {
            Move-Item -LiteralPath $backupCaddyExe -Destination $caddyExe
        }
    }
    catch {
        $rollbackSucceeded = $false
    }

    try {
        if ($promotedCaddyfile -and (Test-Path -LiteralPath $caddyfile)) {
            Remove-Item -LiteralPath $caddyfile -Force
        }
        if ($backedUpCaddyfile -and (Test-Path -LiteralPath $backupCaddyfile)) {
            Move-Item -LiteralPath $backupCaddyfile -Destination $caddyfile
        }
    }
    catch {
        $rollbackSucceeded = $false
    }

    if ($serviceSnapshot.WasRunning -and (Test-ServiceExists $ServiceName)) {
        try {
            Start-Service -Name $ServiceName
            (Get-Service -Name $ServiceName).WaitForStatus("Running", [TimeSpan]::FromSeconds(30))
        }
        catch {
            $rollbackSucceeded = $false
        }
    }

    foreach ($firewallRuleName in $createdFirewallRules) {
        try {
            Remove-NetFirewallRule -DisplayName $firewallRuleName -ErrorAction Stop
        }
        catch {
            $rollbackSucceeded = $false
        }
    }

    if (-not $rollbackSucceeded) {
        throw "Caddy deployment failed ('$deploymentError') and automatic rollback was incomplete. Recovery files remain in '$rollbackRoot'."
    }

    throw "Caddy deployment failed ('$deploymentError'). The previous deployment was restored."
}
finally {
    if (Test-Path -LiteralPath $stageRoot) {
        Remove-Item -LiteralPath $stageRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
    if (($deploymentSucceeded -or $rollbackSucceeded) -and (Test-Path -LiteralPath $rollbackRoot)) {
        Remove-Item -LiteralPath $rollbackRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path -LiteralPath $deploymentRoot) {
        $deploymentChildren = @(Get-ChildItem -LiteralPath $deploymentRoot -Force -ErrorAction SilentlyContinue)
        if ($deploymentChildren.Count -eq 0) {
            Remove-Item -LiteralPath $deploymentRoot -Force -ErrorAction SilentlyContinue
        }
    }
}

Write-Host ""
Write-Host "HTTPS health check passed: $publicHealthUrl" -ForegroundColor Green
Write-Host "Caddy installed to: $InstallDir" -ForegroundColor Green
Write-Host "Caddyfile: $caddyfile" -ForegroundColor Green
Write-Host "Proxy: https://$Domain -> $UpstreamUrl" -ForegroundColor Green
