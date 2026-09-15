<#
.SYNOPSIS
Registers the daily TFlexDrawingService automatic update task.

.EXAMPLE
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\Install-TFlexAutomaticUpdate.ps1 `
  -SourceRoot "C:\Services\TFlexDrawingService\_src" `
  -InstallRoot "C:\Services\TFlexDrawingService" `
  -Domain "alesnichiy.ru" `
  -AcmeEmail "admin@example.com"
#>
[CmdletBinding()]
param(
    [string]$RepositoryUrl = "https://github.com/lehjke/tflex-backend-service.git",
    [string]$Branch = "main",
    [string]$InstallRoot = "C:\Services\TFlexDrawingService",
    [Parameter(Mandatory = $true)]
    [string]$SourceRoot,
    [string]$TFlexCadProgramDir = "C:\Program Files\T-FLEX CAD 17\Program",
    [string]$TFlexAutomationCommandPath = "",
    [string]$AdminUser = "admin",
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
    [string]$ContainerName = "tflex-drawing-api",
    [string]$ApiImageRepository = "tflex-drawing-service-api",
    [string]$Domain = "",
    [string]$AcmeEmail = "",
    [string]$TaskName = "TFlexDrawingService.AutoUpdate",
    [ValidatePattern("^(?:[01]\d|2[0-3]):[0-5]\d$")]
    [string]$DailyAt = "00:00",
    [switch]$SkipFirewall
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

function Test-IsAdmin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Invoke-Native {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$Arguments = @()
    )

    $output = @(& $FilePath @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "$FilePath exited with code ${exitCode}: $($output -join [Environment]::NewLine)"
    }

    return $output
}

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw "The automatic update task can be installed only on Windows Server."
}
if (-not (Test-IsAdmin)) {
    throw "Run PowerShell as Administrator."
}
if ($ApiHostPort -eq $CandidateHostPort) {
    throw "CandidateHostPort must differ from ApiHostPort."
}
if ($TaskName.Contains('"') -or $SourceRoot.Contains('"') -or $InstallRoot.Contains('"')) {
    throw "TaskName, SourceRoot and InstallRoot cannot contain quote characters."
}

$SourceRoot = [IO.Path]::GetFullPath($SourceRoot)
$InstallRoot = [IO.Path]::GetFullPath($InstallRoot)
if (-not (Test-Path -LiteralPath (Join-Path $SourceRoot ".git") -PathType Container)) {
    throw "SourceRoot '$SourceRoot' must be a Git checkout for automatic updates."
}

$sourceRunnerPath = Join-Path $PSScriptRoot "Invoke-TFlexAutomaticUpdate.ps1"
if (-not (Test-Path -LiteralPath $sourceRunnerPath -PathType Leaf)) {
    throw "Automatic update runner was not found at '$sourceRunnerPath'."
}

$autoUpdateRoot = Join-Path $InstallRoot "AutoUpdate"
$runnerPath = Join-Path $autoUpdateRoot "Invoke-TFlexAutomaticUpdate.ps1"
$configPath = Join-Path $autoUpdateRoot "config.json"
$successMarkerPath = Join-Path $autoUpdateRoot "last-successful-revision.txt"
New-Item -ItemType Directory -Path $autoUpdateRoot -Force | Out-Null

$runnerTemporaryPath = "$runnerPath.tmp"
Copy-Item -LiteralPath $sourceRunnerPath -Destination $runnerTemporaryPath -Force
Move-Item -LiteralPath $runnerTemporaryPath -Destination $runnerPath -Force

$config = [ordered]@{
    schemaVersion = 1
    repositoryUrl = $RepositoryUrl
    branch = $Branch
    installRoot = $InstallRoot
    sourceRoot = $SourceRoot
    tFlexCadProgramDir = $TFlexCadProgramDir
    tFlexAutomationCommandPath = $TFlexAutomationCommandPath
    adminUser = $AdminUser
    maxActiveJobs = $MaxActiveJobs
    maxActiveJobsPerUser = $MaxActiveJobsPerUser
    finishedJobRetentionDays = $FinishedJobRetentionDays
    healthCheckAttempts = $HealthCheckAttempts
    healthCheckDelaySeconds = $HealthCheckDelaySeconds
    apiHostPort = $ApiHostPort
    candidateHostPort = $CandidateHostPort
    containerName = $ContainerName
    apiImageRepository = $ApiImageRepository
    domain = $Domain
    acmeEmail = $AcmeEmail
    taskName = $TaskName
    dailyAt = $DailyAt
    skipFirewall = [bool]$SkipFirewall
}
$configTemporaryPath = "$configPath.tmp"
$config | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $configTemporaryPath -Encoding UTF8
Move-Item -LiteralPath $configTemporaryPath -Destination $configPath -Force

# Only LocalSystem and local Administrators may modify the updater or its
# configuration. SIDs keep this independent of the Windows display language.
Invoke-Native -FilePath "icacls.exe" -Arguments @(
    $autoUpdateRoot,
    "/inheritance:r",
    "/grant:r",
    "*S-1-5-18:(OI)(CI)F",
    "*S-1-5-32-544:(OI)(CI)F",
    "/T",
    "/C"
) | Out-Null

$powershellPath = Join-Path $env:SystemRoot "System32\WindowsPowerShell\v1.0\powershell.exe"
$actionArguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File `"$runnerPath`" -ConfigPath `"$configPath`""
$action = New-ScheduledTaskAction -Execute $powershellPath -Argument $actionArguments -WorkingDirectory $autoUpdateRoot
$dailyTime = [DateTime]::ParseExact(
    $DailyAt,
    "HH:mm",
    [Globalization.CultureInfo]::InvariantCulture,
    [Globalization.DateTimeStyles]::None)
$trigger = New-ScheduledTaskTrigger -Daily -At $dailyTime
$principal = New-ScheduledTaskPrincipal `
    -UserId "SYSTEM" `
    -LogonType ServiceAccount `
    -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet `
    -StartWhenAvailable `
    -MultipleInstances IgnoreNew `
    -ExecutionTimeLimit (New-TimeSpan -Hours 6) `
    -RestartCount 2 `
    -RestartInterval (New-TimeSpan -Minutes 15)
$task = New-ScheduledTask `
    -Action $action `
    -Trigger $trigger `
    -Principal $principal `
    -Settings $settings `
    -Description "Checks origin/$Branch daily and transactionally updates TFlexDrawingService when a new revision is available."

Register-ScheduledTask -TaskName $TaskName -InputObject $task -Force | Out-Null
$registeredTask = Get-ScheduledTask -TaskName $TaskName -ErrorAction Stop
$taskInfo = Get-ScheduledTaskInfo -TaskName $TaskName -ErrorAction Stop
if ($registeredTask.State -eq "Disabled") {
    Enable-ScheduledTask -TaskName $TaskName | Out-Null
}

Write-Host "Automatic update task '$TaskName' is installed." -ForegroundColor Green
Write-Host "Schedule: daily at $DailyAt (server local time)." -ForegroundColor Green
Write-Host "Next run: $($taskInfo.NextRunTime)" -ForegroundColor Green
Write-Host "Configuration: $configPath" -ForegroundColor Green
Write-Host "Successful revision marker: $successMarkerPath" -ForegroundColor Green
