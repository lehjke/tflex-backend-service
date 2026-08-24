<#
.SYNOPSIS
Deploys TFlexDrawingService with the API in a Windows container and the real
Worker/T-FLEX automation as a Windows service.

.DESCRIPTION
Runs the existing transactional Windows deployment first, validates the native
API and Worker, builds and smoke-tests a Windows Server Core API image, then
hands the loopback API port to the container. The native API service remains
installed but disabled as an emergency fallback.

Docker/Moby configured for Windows containers and an installed, activated
T-FLEX CAD are prerequisites. This script does not install licensed software or
the container runtime.

.EXAMPLE
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\Deploy-TFlexHybridServer2022.ps1 `
  -UseExistingSource `
  -SourceRoot "C:\src\tflex-backend-service" `
  -InstallRoot "C:\Services\TFlexDrawingService" `
  -TFlexCadProgramDir "C:\Program Files\T-FLEX CAD 17\Program" `
  -Domain "lehjke.online" `
  -AcmeEmail "admin@example.com"
#>
[CmdletBinding()]
param(
    [string]$RepositoryUrl = "https://github.com/lehjke/tflex-backend-service.git",
    [string]$Branch = "main",
    [string]$InstallRoot = "C:\Services\TFlexDrawingService",
    [string]$SourceRoot = "",
    [switch]$UseExistingSource,
    [string]$TFlexCadProgramDir = "C:\Program Files\T-FLEX CAD 17\Program",
    [string]$TFlexAutomationCommandPath = "",
    [string]$ServiceUser = "",
    [string]$ServicePassword = "",
    [string]$PreviousServicePassword = "",
    [string]$AdminUser = "admin",
    [string]$AdminPassword = "",
    [string]$AdminPasswordHash = "",
    [int]$MaxActiveJobs = 50,
    [int]$MaxActiveJobsPerUser = 5,
    [int]$FinishedJobRetentionDays = 30,
    [ValidateRange(1, 60)]
    [int]$HealthCheckAttempts = 12,
    [ValidateRange(1, 30)]
    [int]$HealthCheckDelaySeconds = 5,
    [ValidateRange(1024, 65534)]
    [int]$ApiHostPort = 5011,
    [ValidateRange(1024, 65535)]
    [int]$CandidateHostPort = 5012,
    [ValidatePattern("^[A-Za-z0-9][A-Za-z0-9_.-]+$")]
    [string]$ContainerName = "tflex-drawing-api",
    [ValidatePattern("^[a-z0-9][a-z0-9._/-]*$")]
    [string]$ApiImageRepository = "tflex-drawing-service-api",
    [string]$Domain = "",
    [string]$AcmeEmail = "",
    [switch]$SkipCaddy,
    [switch]$SkipDockerPull,
    [switch]$AllowDirtySource,
    [switch]$SkipGitInstall,
    [switch]$SkipDotNetInstall,
    [switch]$SkipNetFx472DeveloperPackInstall,
    [switch]$SkipRunnerBuild,
    [switch]$SkipRunnerHealthCheck,
    [switch]$SkipFirewall,
    [switch]$SkipTFlexCheck,
    [string]$BootstrapScriptUrl = "",
    [string]$WorkDir = "C:\Temp\TFlexDrawingServiceHybridBootstrap"
)

$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$ApiServiceName = "TFlexDrawingService.Api"
$WorkerServiceName = "TFlexDrawingService.Worker"
$AuthenticatedUsersSid = "S-1-5-11"

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

    Write-Host "Running: $FilePath" -ForegroundColor DarkGray
    & $FilePath @Arguments
    $exitCode = $LASTEXITCODE
    if ($AllowedExitCodes -notcontains $exitCode) {
        throw "Native command '$FilePath' failed with exit code $exitCode."
    }
}

function Invoke-NativeCapture {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$Arguments = @(),
        [int[]]$AllowedExitCodes = @(0)
    )

    $output = @(& $FilePath @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    if ($AllowedExitCodes -notcontains $exitCode) {
        $detail = ($output | ForEach-Object { [string]$_ }) -join [Environment]::NewLine
        if ($detail.Length -gt 4096) {
            $detail = $detail.Substring(0, 4096) + " [truncated]"
        }
        throw "Native command '$FilePath' failed with exit code $exitCode`: $detail"
    }

    return @($output | ForEach-Object { [string]$_ })
}

function Get-HttpErrorResponseDetail {
    param([System.Management.Automation.ErrorRecord]$ErrorRecord)

    $message = [string]$ErrorRecord.Exception.Message
    $response = $ErrorRecord.Exception.Response
    if ($null -eq $response) {
        return $message
    }

    $statusCode = $null
    try {
        $statusCode = [int]$response.StatusCode
    }
    catch {
        # Preserve the original exception when no HTTP status exists.
    }

    $responseBody = ""
    $reader = $null
    try {
        $stream = $response.GetResponseStream()
        if ($null -ne $stream) {
            $reader = New-Object System.IO.StreamReader($stream)
            $responseBody = $reader.ReadToEnd().Trim()
        }
    }
    catch {
        # Preserve the original exception when the response body is unavailable.
    }
    finally {
        if ($null -ne $reader) {
            $reader.Dispose()
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($responseBody)) {
        if ($responseBody.Length -gt 4096) {
            $responseBody = $responseBody.Substring(0, 4096) + " [truncated]"
        }

        $prefix = if ($null -ne $statusCode) { "HTTP $statusCode" } else { $message }
        return "${prefix}: $responseBody"
    }

    return $message
}

function Test-ContainerExists {
    param([string]$Name)
    & docker container inspect $Name *> $null
    return $LASTEXITCODE -eq 0
}

function Test-ContainerRunning {
    param([string]$Name)
    if (-not (Test-ContainerExists $Name)) {
        return $false
    }

    $state = Invoke-NativeCapture -FilePath "docker" -Arguments @(
        "container", "inspect", "--format", "{{.State.Running}}", $Name
    )
    return [string]::Equals(
        (($state -join "").Trim()),
        "true",
        [StringComparison]::OrdinalIgnoreCase)
}

function Stop-ContainerIfRunning {
    param([string]$Name)
    if (Test-ContainerRunning $Name) {
        Invoke-Native -FilePath "docker" -Arguments @("container", "stop", "--time", "30", $Name)
    }
}

function Remove-ContainerIfExists {
    param([string]$Name)
    if (Test-ContainerExists $Name) {
        Invoke-Native -FilePath "docker" -Arguments @("container", "rm", "--force", $Name)
    }
}

function Get-BootstrapScriptPath {
    $localPath = Join-Path $PSScriptRoot "Bootstrap-WindowsServer2022.ps1"
    if (Test-Path -LiteralPath $localPath -PathType Leaf) {
        return $localPath
    }

    New-Item -ItemType Directory -Path $WorkDir -Force | Out-Null
    $downloadUrl = $BootstrapScriptUrl
    if ([string]::IsNullOrWhiteSpace($downloadUrl)) {
        $normalizedRepositoryUrl = $RepositoryUrl.TrimEnd('/')
        if ($normalizedRepositoryUrl.EndsWith(".git", [StringComparison]::OrdinalIgnoreCase)) {
            $normalizedRepositoryUrl = $normalizedRepositoryUrl.Substring(0, $normalizedRepositoryUrl.Length - 4)
        }

        if ($normalizedRepositoryUrl -notmatch '^https://github\.com/([^/]+)/([^/]+)$') {
            throw "BootstrapScriptUrl is required when RepositoryUrl is not a simple GitHub HTTPS URL."
        }

        $owner = [Uri]::EscapeDataString($Matches[1])
        $repository = [Uri]::EscapeDataString($Matches[2])
        $branchPath = (($Branch -split '/') | ForEach-Object { [Uri]::EscapeDataString($_) }) -join '/'
        $downloadUrl = "https://raw.githubusercontent.com/$owner/$repository/$branchPath/scripts/Bootstrap-WindowsServer2022.ps1"
    }

    $downloadPath = Join-Path $WorkDir "Bootstrap-WindowsServer2022.ps1"
    Invoke-WebRequest -Uri $downloadUrl -OutFile $downloadPath -UseBasicParsing
    return $downloadPath
}

function Invoke-ServiceDeployment {
    param([string]$BootstrapPath)

    $bootstrapParameters = @{
        RepositoryUrl = $RepositoryUrl
        Branch = $Branch
        InstallRoot = $InstallRoot
        Urls = "http://127.0.0.1:$ApiHostPort"
        TFlexCadProgramDir = $TFlexCadProgramDir
        TFlexAutomationCommandPath = $TFlexAutomationCommandPath
        ServiceUser = $ServiceUser
        ServicePassword = $ServicePassword
        PreviousServicePassword = $PreviousServicePassword
        AdminUser = $AdminUser
        AdminPassword = $AdminPassword
        AdminPasswordHash = $AdminPasswordHash
        RequireAuthentication = $true
        MaxActiveJobs = $MaxActiveJobs
        MaxActiveJobsPerUser = $MaxActiveJobsPerUser
        FinishedJobRetentionDays = $FinishedJobRetentionDays
        HealthCheckAttempts = $HealthCheckAttempts
        HealthCheckDelaySeconds = $HealthCheckDelaySeconds
        WorkDir = (Join-Path $WorkDir "service-bootstrap")
    }

    if (-not [string]::IsNullOrWhiteSpace($SourceRoot)) {
        $bootstrapParameters.SourceRoot = $SourceRoot
    }
    if ($UseExistingSource) { $bootstrapParameters.UseExistingSource = $true }
    if ($SkipGitInstall) { $bootstrapParameters.SkipGitInstall = $true }
    if ($SkipDotNetInstall) { $bootstrapParameters.SkipDotNetInstall = $true }
    if ($SkipNetFx472DeveloperPackInstall) { $bootstrapParameters.SkipNetFx472DeveloperPackInstall = $true }
    if ($SkipRunnerBuild) { $bootstrapParameters.SkipRunnerBuild = $true }
    if ($SkipRunnerHealthCheck) { $bootstrapParameters.SkipRunnerHealthCheck = $true }
    if ($SkipFirewall) { $bootstrapParameters.SkipFirewall = $true }
    if ($SkipTFlexCheck) { $bootstrapParameters.SkipTFlexCheck = $true }

    & $BootstrapPath @bootstrapParameters
}

function Get-EffectiveSourceRoot {
    if (-not [string]::IsNullOrWhiteSpace($SourceRoot)) {
        return [IO.Path]::GetFullPath($SourceRoot)
    }

    return Join-Path $InstallRoot "_src"
}

function Assert-CleanSource {
    param([string]$Path)
    if ($AllowDirtySource -or -not (Test-Path -LiteralPath (Join-Path $Path ".git"))) {
        return
    }

    $status = Invoke-NativeCapture -FilePath "git" -Arguments @(
        "-C", $Path, "status", "--short", "--untracked-files=all"
    )
    if (@($status | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }).Count -gt 0) {
        throw "SourceRoot '$Path' contains uncommitted files. Commit/stash them or pass -AllowDirtySource for an intentional diagnostic build."
    }
}

function Get-ImageTag {
    param([string]$Path)
    if (Test-Path -LiteralPath (Join-Path $Path ".git")) {
        $revision = Invoke-NativeCapture -FilePath "git" -Arguments @(
            "-C", $Path, "rev-parse", "--short=12", "HEAD"
        )
        $normalizedRevision = (($revision -join "").Trim()).ToLowerInvariant()
        if ($normalizedRevision -match '^[0-9a-f]{7,40}$') {
            return $normalizedRevision
        }
    }

    return [DateTime]::UtcNow.ToString("yyyyMMddHHmmss")
}

function Get-DirectoryAclSnapshots {
    param([string[]]$Paths)
    return @($Paths | Select-Object -Unique | ForEach-Object {
        $acl = Get-Acl -LiteralPath $_
        [pscustomobject]@{
            Path = $_
            Sddl = $acl.GetSecurityDescriptorSddlForm(
                [Security.AccessControl.AccessControlSections]::All)
        }
    })
}

function Restore-DirectoryAclSnapshots {
    param([object[]]$Snapshots)
    foreach ($snapshot in $Snapshots) {
        $acl = Get-Acl -LiteralPath $snapshot.Path
        $acl.SetSecurityDescriptorSddlForm(
            $snapshot.Sddl,
            [Security.AccessControl.AccessControlSections]::All)
        Set-Acl -LiteralPath $snapshot.Path -AclObject $acl
    }
}

function Grant-ContainerDirectoryAccess {
    param(
        [string]$Path,
        [Security.AccessControl.FileSystemRights]$Rights
    )

    $identity = [Security.Principal.SecurityIdentifier]::new($AuthenticatedUsersSid)
    $inheritance = [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor
        [Security.AccessControl.InheritanceFlags]::ObjectInherit
    $rule = [Security.AccessControl.FileSystemAccessRule]::new(
        $identity,
        $Rights,
        $inheritance,
        [Security.AccessControl.PropagationFlags]::None,
        [Security.AccessControl.AccessControlType]::Allow)
    $acl = Get-Acl -LiteralPath $Path
    $acl.SetAccessRule($rule)
    Set-Acl -LiteralPath $Path -AclObject $acl
}

function Get-DockerNatGateway {
    $gateway = Invoke-NativeCapture -FilePath "docker" -Arguments @(
        "network", "inspect", "nat", "--format", "{{(index .IPAM.Config 0).Gateway}}"
    )
    $value = (($gateway -join "").Trim())
    $address = $null
    if (-not [Net.IPAddress]::TryParse($value, [ref]$address)) {
        throw "Docker NAT gateway '$value' is not a valid IP address."
    }

    return $value
}

function Start-ApiContainer {
    param(
        [string]$Name,
        [string]$Image,
        [int]$HostPort,
        [string]$DockerNatGateway,
        [bool]$RestartAutomatically
    )

    $apiDirectory = Join-Path $InstallRoot "Api"
    $storageDirectory = Join-Path $InstallRoot "storage"
    $templatesDirectory = Join-Path $InstallRoot "templates"
    $arguments = @(
        "container", "run", "--detach",
        "--name", $Name,
        "--isolation", "process",
        "--publish", "127.0.0.1:${HostPort}:8080",
        "--mount", "type=bind,source=$apiDirectory,target=C:\tflex-config,readonly",
        "--mount", "type=bind,source=$storageDirectory,target=$storageDirectory",
        "--mount", "type=bind,source=$templatesDirectory,target=$templatesDirectory",
        "--env", "TFLEX_CONFIGURATION_FILE=C:\tflex-config\appsettings.Production.json",
        "--env", "ReverseProxy__KnownProxies__0=$DockerNatGateway"
    )
    if ($RestartAutomatically) {
        $arguments += @("--restart", "unless-stopped")
    }
    $arguments += @(
        $Image,
        "--urls", "http://+:8080"
    )

    Invoke-Native -FilePath "docker" -Arguments $arguments
}

function Wait-ApiHealth {
    param(
        [int]$Port,
        [bool]$RequireReady
    )

    $healthPath = if ($RequireReady) { "ready" } else { "live" }
    $healthUrl = "http://127.0.0.1:$Port/api/health/$healthPath"
    $lastError = ""
    for ($attempt = 1; $attempt -le $HealthCheckAttempts; $attempt++) {
        try {
            $response = Invoke-WebRequest -Uri $healthUrl -UseBasicParsing -TimeoutSec 20
            if ($response.StatusCode -eq 200) {
                return $healthUrl
            }
            $lastError = "HTTP $($response.StatusCode)"
        }
        catch {
            $lastError = Get-HttpErrorResponseDetail $_
        }

        if ($attempt -lt $HealthCheckAttempts) {
            Start-Sleep -Seconds $HealthCheckDelaySeconds
        }
    }

    throw "Container API health check failed at '$healthUrl': $lastError"
}

function Assert-AuthenticationBoundary {
    param([int]$Port)
    $url = "http://127.0.0.1:$Port/api/projects"
    try {
        $response = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 20
        throw "Container authentication smoke check failed: $url returned HTTP $($response.StatusCode) without a session."
    }
    catch {
        $statusCode = $null
        if ($null -ne $_.Exception.Response) {
            $statusCode = [int]$_.Exception.Response.StatusCode
        }
        if ($statusCode -eq 401) {
            return
        }
        if ($_.Exception.Message -like "Container authentication smoke check failed:*") {
            throw
        }

        throw "Container authentication smoke check could not confirm HTTP 401 at '$url'."
    }
}

function Enable-NativeApiFallback {
    $service = Get-Service -Name $ApiServiceName -ErrorAction SilentlyContinue
    if ($null -eq $service) {
        throw "Native API fallback service '$ApiServiceName' was not found."
    }

    Set-Service -Name $ApiServiceName -StartupType Automatic
    if ($service.Status -ne "Running") {
        Start-Service -Name $ApiServiceName
        (Get-Service -Name $ApiServiceName).WaitForStatus(
            "Running",
            [TimeSpan]::FromSeconds(30))
    }
}

function Disable-NativeApiService {
    $service = Get-Service -Name $ApiServiceName -ErrorAction Stop
    if ($service.Status -ne "Stopped") {
        Stop-Service -Name $ApiServiceName -Force
        $service.WaitForStatus("Stopped", [TimeSpan]::FromSeconds(30))
    }
    Set-Service -Name $ApiServiceName -StartupType Disabled
}

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw "This hybrid deployment script is intended for Windows Server 2022."
}
if (-not (Test-IsAdmin)) {
    throw "Run PowerShell as Administrator."
}
if ($ApiHostPort -eq $CandidateHostPort) {
    throw "CandidateHostPort must differ from ApiHostPort."
}
if ($UseExistingSource -and [string]::IsNullOrWhiteSpace($SourceRoot)) {
    throw "SourceRoot must be specified when UseExistingSource is enabled."
}
if ([string]::IsNullOrWhiteSpace($Domain) -and -not $SkipCaddy) {
    throw "Domain is required for the complete HTTPS deployment, or pass -SkipCaddy for a loopback-only API."
}
if ($InstallRoot.Contains(",")) {
    throw "InstallRoot cannot contain a comma because Docker --mount uses comma-delimited fields."
}

$docker = Get-Command docker -ErrorAction SilentlyContinue
if ($null -eq $docker) {
    throw "docker.exe was not found. Install a supported Windows Server container runtime before running this script."
}
$dockerOs = Invoke-NativeCapture -FilePath $docker.Source -Arguments @(
    "info", "--format", "{{.OSType}}"
)
if (-not [string]::Equals(
        (($dockerOs -join "").Trim()),
        "windows",
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "Docker is not configured for Windows containers."
}

$InstallRoot = [IO.Path]::GetFullPath($InstallRoot)
$deploymentId = [Guid]::NewGuid().ToString("N")
$candidateContainerName = "$ContainerName-candidate-$deploymentId"
$rollbackContainerName = "$ContainerName-rollback-$deploymentId"
$previousContainerExisted = Test-ContainerExists $ContainerName
$previousContainerWasRunning = $previousContainerExisted -and (Test-ContainerRunning $ContainerName)
$serviceDeploymentSucceeded = $false
$hybridDeploymentSucceeded = $false
$aclSnapshots = @()
$image = ""

try {
    if ($previousContainerExisted) {
        Write-Step "Stopping the current API container before the transactional service update"
        Stop-ContainerIfRunning $ContainerName
        Invoke-Native -FilePath "docker" -Arguments @(
            "container", "rename", $ContainerName, $rollbackContainerName
        )
    }

    Write-Step "Updating Worker, Runner, templates and the native API fallback"
    $bootstrapPath = Get-BootstrapScriptPath
    Invoke-ServiceDeployment $bootstrapPath
    $serviceDeploymentSucceeded = $true

    $effectiveSourceRoot = Get-EffectiveSourceRoot
    $dockerfilePath = Join-Path $effectiveSourceRoot "Dockerfile.api"
    if (-not (Test-Path -LiteralPath $dockerfilePath -PathType Leaf)) {
        throw "Dockerfile.api was not found under SourceRoot '$effectiveSourceRoot'."
    }
    Assert-CleanSource $effectiveSourceRoot

    $imageTag = Get-ImageTag $effectiveSourceRoot
    $image = "${ApiImageRepository}:$imageTag"
    Write-Step "Building API image $image"
    $buildArguments = @(
        "build",
        "--file", $dockerfilePath,
        "--tag", $image
    )
    if (-not $SkipDockerPull) {
        $buildArguments += "--pull"
    }
    $buildArguments += $effectiveSourceRoot
    Invoke-Native -FilePath "docker" -Arguments $buildArguments

    $apiDirectory = Join-Path $InstallRoot "Api"
    $storageDirectory = Join-Path $InstallRoot "storage"
    $templatesDirectory = Join-Path $InstallRoot "templates"
    $aclSnapshots = Get-DirectoryAclSnapshots @(
        $apiDirectory,
        $storageDirectory,
        $templatesDirectory
    )

    Write-Step "Granting the restricted container identity access to bind-mounted data"
    Grant-ContainerDirectoryAccess `
        -Path $apiDirectory `
        -Rights ([Security.AccessControl.FileSystemRights]::ReadAndExecute)
    Grant-ContainerDirectoryAccess `
        -Path $storageDirectory `
        -Rights ([Security.AccessControl.FileSystemRights]::Modify)
    Grant-ContainerDirectoryAccess `
        -Path $templatesDirectory `
        -Rights ([Security.AccessControl.FileSystemRights]::Modify)

    $dockerNatGateway = Get-DockerNatGateway
    Write-Step "Smoke-testing the candidate API container"
    Start-ApiContainer `
        -Name $candidateContainerName `
        -Image $image `
        -HostPort $CandidateHostPort `
        -DockerNatGateway $dockerNatGateway `
        -RestartAutomatically $false
    Wait-ApiHealth `
        -Port $CandidateHostPort `
        -RequireReady (-not $SkipRunnerHealthCheck) | Out-Null
    Assert-AuthenticationBoundary $CandidateHostPort
    Remove-ContainerIfExists $candidateContainerName

    Write-Step "Handing the production API port to the container"
    Disable-NativeApiService
    Start-ApiContainer `
        -Name $ContainerName `
        -Image $image `
        -HostPort $ApiHostPort `
        -DockerNatGateway $dockerNatGateway `
        -RestartAutomatically $true
    $healthUrl = Wait-ApiHealth `
        -Port $ApiHostPort `
        -RequireReady (-not $SkipRunnerHealthCheck)
    Assert-AuthenticationBoundary $ApiHostPort

    $worker = Get-Service -Name $WorkerServiceName -ErrorAction Stop
    if ($worker.Status -ne "Running") {
        throw "Windows service '$WorkerServiceName' is not running after container handoff."
    }

    if (Test-ContainerExists $rollbackContainerName) {
        Remove-ContainerIfExists $rollbackContainerName
    }
    $hybridDeploymentSucceeded = $true

    Write-Host "Hybrid API health check passed: $healthUrl" -ForegroundColor Green
    Write-Host "API image: $image" -ForegroundColor Green
    Write-Host "Worker service: $WorkerServiceName (Running)" -ForegroundColor Green
}
catch {
    $deploymentError = $_
    Remove-ContainerIfExists $candidateContainerName
    Remove-ContainerIfExists $ContainerName

    if ($aclSnapshots.Count -gt 0) {
        try { Restore-DirectoryAclSnapshots $aclSnapshots } catch { Write-Warning $_.Exception.Message }
    }

    if ($serviceDeploymentSucceeded) {
        try {
            Enable-NativeApiFallback
            if (Test-ContainerExists $rollbackContainerName) {
                Invoke-Native -FilePath "docker" -Arguments @(
                    "container", "rename", $rollbackContainerName, $ContainerName
                )
            }
            Write-Warning "Container handoff failed. The updated native API service was restored on port $ApiHostPort."
        }
        catch {
            Write-Warning "Container handoff and native API fallback both failed: $($_.Exception.Message)"
        }
    }
    elseif (Test-ContainerExists $rollbackContainerName) {
        try {
            Invoke-Native -FilePath "docker" -Arguments @(
                "container", "rename", $rollbackContainerName, $ContainerName
            )
            if ($previousContainerWasRunning) {
                Invoke-Native -FilePath "docker" -Arguments @("container", "start", $ContainerName)
            }
        }
        catch {
            Write-Warning "The previous API container could not be restored automatically: $($_.Exception.Message)"
        }
    }

    throw $deploymentError
}

if (-not $SkipCaddy) {
    Write-Step "Installing or updating the Caddy HTTPS proxy"
    $effectiveSourceRoot = Get-EffectiveSourceRoot
    $caddyInstallerPath = Join-Path $effectiveSourceRoot "scripts\Install-CaddyAcmeProxy.ps1"
    if (-not (Test-Path -LiteralPath $caddyInstallerPath -PathType Leaf)) {
        throw "Caddy installer was not found at '$caddyInstallerPath'."
    }

    $caddyParameters = @{
        Domain = $Domain
        Email = $AcmeEmail
        UpstreamUrl = "http://127.0.0.1:$ApiHostPort"
        HealthCheckAttempts = $HealthCheckAttempts
        HealthCheckDelaySeconds = $HealthCheckDelaySeconds
    }
    if ($SkipFirewall) { $caddyParameters.SkipFirewall = $true }
    & $caddyInstallerPath @caddyParameters
}

if (-not $hybridDeploymentSucceeded) {
    throw "Hybrid deployment did not reach its successful terminal state."
}

Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "Container: $ContainerName" -ForegroundColor Green
Write-Host "Image: $image" -ForegroundColor Green
Write-Host "Local API: http://127.0.0.1:$ApiHostPort" -ForegroundColor Green
if (-not $SkipCaddy) {
    Write-Host "Public API: https://$Domain" -ForegroundColor Green
}
