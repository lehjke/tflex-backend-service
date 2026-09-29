<#
.SYNOPSIS
Checks the configured Git branch and deploys a new TFlexDrawingService revision.

.DESCRIPTION
This script is intended to run as LocalSystem from Windows Task Scheduler. It
never stores or accepts service/admin passwords. Existing service identities
and the admin password hash are preserved by the transactional deployment.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ConfigPath
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

function Get-ConfigValue {
    param(
        [object]$Config,
        [string]$Name,
        [object]$DefaultValue = $null,
        [switch]$Required
    )

    $property = $Config.PSObject.Properties[$Name]
    if ($null -ne $property -and $null -ne $property.Value) {
        $value = $property.Value
        if (-not ($value -is [string]) -or -not [string]::IsNullOrWhiteSpace($value)) {
            return $value
        }
    }

    if ($Required) {
        throw "Automatic update configuration is missing '$Name'."
    }

    return $DefaultValue
}

function Invoke-NativeResult {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$Arguments = @()
    )

    Write-Host "Running: $FilePath $($Arguments -join ' ')" -ForegroundColor DarkGray
    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        $output = @(& $FilePath @Arguments 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    foreach ($line in $output) {
        Write-Host ([string]$line)
    }

    return [pscustomobject]@{
        ExitCode = $exitCode
        Output = @($output | ForEach-Object { [string]$_ })
    }
}

function Invoke-Native {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$Arguments = @()
    )

    $result = Invoke-NativeResult -FilePath $FilePath -Arguments $Arguments
    if ($result.ExitCode -ne 0) {
        throw "$FilePath exited with code $($result.ExitCode)."
    }

    return $result.Output
}

function Get-GitScalar {
    param(
        [string]$GitPath,
        [string]$SourceRoot,
        [string[]]$Arguments
    )

    $output = @(Invoke-Native -FilePath $GitPath -Arguments (@("-C", $SourceRoot) + $Arguments))
    return (($output -join "").Trim())
}

function Write-UpdateState {
    param(
        [string]$Path,
        [string]$Status,
        [string]$CurrentRevision,
        [string]$TargetRevision,
        [string]$Message
    )

    $state = [ordered]@{
        status = $Status
        checkedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
        currentRevision = $CurrentRevision
        targetRevision = $TargetRevision
        message = $Message
    }
    $temporaryPath = "$Path.tmp"
    $state | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $temporaryPath -Encoding UTF8
    Move-Item -LiteralPath $temporaryPath -Destination $Path -Force
}

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw "Automatic server updates are supported only on Windows Server."
}

$ConfigPath = [IO.Path]::GetFullPath($ConfigPath)
if (-not (Test-Path -LiteralPath $ConfigPath -PathType Leaf)) {
    throw "Automatic update configuration was not found at '$ConfigPath'."
}

$config = Get-Content -LiteralPath $ConfigPath -Encoding UTF8 -Raw | ConvertFrom-Json
$sourceRoot = [IO.Path]::GetFullPath([string](Get-ConfigValue $config "sourceRoot" -Required))
$installRoot = [IO.Path]::GetFullPath([string](Get-ConfigValue $config "installRoot" -Required))
$repositoryUrl = [string](Get-ConfigValue $config "repositoryUrl" -Required)
$branch = [string](Get-ConfigValue $config "branch" -Required)
$taskName = [string](Get-ConfigValue $config "taskName" "TFlexDrawingService.AutoUpdate")
$updateTime = [string](Get-ConfigValue $config "dailyAt" "00:00")
$updateIntervalMinutes = [int](Get-ConfigValue $config "automaticUpdateIntervalMinutes" 0)
$autoUpdateRoot = Split-Path $ConfigPath -Parent
$logDirectory = Join-Path $installRoot "logs\auto-update"
$statusPath = Join-Path $autoUpdateRoot "status.json"
$successMarkerPath = Join-Path $autoUpdateRoot "last-successful-revision.txt"
$lockPath = Join-Path $autoUpdateRoot "update.lock"

New-Item -ItemType Directory -Path $logDirectory, $autoUpdateRoot -Force | Out-Null
Get-ChildItem -LiteralPath $logDirectory -Filter "update-*.log" -File -ErrorAction SilentlyContinue |
    Where-Object { $_.LastWriteTimeUtc -lt [DateTime]::UtcNow.AddDays(-30) } |
    Remove-Item -Force -ErrorAction SilentlyContinue
$logPath = Join-Path $logDirectory ("update-{0}.log" -f [DateTime]::Now.ToString("yyyyMMdd-HHmmss"))

$lockStream = $null
$transcriptStarted = $false
$currentRevision = ""
$targetRevision = ""
$sourceAdvanced = $false
$deploymentStarted = $false
$previousGitTerminalPrompt = [Environment]::GetEnvironmentVariable("GIT_TERMINAL_PROMPT", "Process")

try {
    try {
        $lockStream = [IO.File]::Open(
            $lockPath,
            [IO.FileMode]::OpenOrCreate,
            [IO.FileAccess]::ReadWrite,
            [IO.FileShare]::None)
    }
    catch [IO.IOException] {
        Write-Host "Another automatic update process is already running."
        return
    }

    Start-Transcript -LiteralPath $logPath -Append | Out-Null
    $transcriptStarted = $true
    Write-Host "TFlexDrawingService automatic update check started for $branch at $updateTime."

    if (-not (Test-Path -LiteralPath (Join-Path $sourceRoot ".git") -PathType Container)) {
        throw "SourceRoot '$sourceRoot' is not a Git checkout."
    }

    $deployScript = Join-Path $sourceRoot "scripts\Deploy-TFlexHybridServer2022.ps1"
    if (-not (Test-Path -LiteralPath $deployScript -PathType Leaf)) {
        throw "Hybrid deployment script was not found at '$deployScript'."
    }

    $git = Get-Command git.exe -ErrorAction SilentlyContinue
    if ($null -eq $git) {
        $git = Get-Command git -ErrorAction SilentlyContinue
    }
    if ($null -eq $git) {
        throw "git.exe was not found."
    }

    [Environment]::SetEnvironmentVariable("GIT_TERMINAL_PROMPT", "0", "Process")
    $originUrl = Get-GitScalar $git.Source $sourceRoot @("remote", "get-url", "origin")
    $normalizeRemote = {
        param([string]$Value)
        return $Value.Trim().TrimEnd('/').Replace(".git", "").ToLowerInvariant()
    }
    if ((& $normalizeRemote $originUrl) -ne (& $normalizeRemote $repositoryUrl)) {
        throw "Git origin '$originUrl' does not match configured repository '$repositoryUrl'."
    }

    $dirtyEntries = @(Invoke-Native -FilePath $git.Source -Arguments @(
        "-C", $sourceRoot, "status", "--porcelain=v1", "--untracked-files=all")) |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    if ($dirtyEntries.Count -gt 0) {
        throw "Automatic update refused because SourceRoot contains uncommitted files."
    }

    $currentRevision = Get-GitScalar $git.Source $sourceRoot @("rev-parse", "HEAD")
    Invoke-Native -FilePath $git.Source -Arguments @(
        "-C", $sourceRoot, "-c", "maintenance.auto=false", "-c", "gc.auto=0",
        "fetch", "--prune", "origin", $branch) | Out-Null
    $remoteReference = "refs/remotes/origin/$branch"
    $targetRevision = Get-GitScalar $git.Source $sourceRoot @("rev-parse", $remoteReference)
    $deployedRevision = if (Test-Path -LiteralPath $successMarkerPath -PathType Leaf) {
        (Get-Content -LiteralPath $successMarkerPath -Encoding UTF8 -Raw).Trim()
    }
    else {
        ""
    }

    if ($deployedRevision -eq $targetRevision -and $currentRevision -eq $targetRevision) {
        $message = "No update is available. Deployed revision is $targetRevision."
        Write-Host $message -ForegroundColor Green
        Write-UpdateState $statusPath "up-to-date" $currentRevision $targetRevision $message
        return
    }

    if ($repositoryUrl -notmatch '^https://github\.com/([A-Za-z0-9-]+)/([A-Za-z0-9_.-]+?)(?:\.git)?/?$') {
        throw "Automatic deployment requires a public GitHub repository URL: '$repositoryUrl'."
    }
    $owner = $Matches[1]
    $repository = $Matches[2]
    $query = "head_sha=$([Uri]::EscapeDataString($targetRevision))&branch=$([Uri]::EscapeDataString($branch))&event=push&per_page=100"
    $workflowRunsUrl = "https://api.github.com/repos/$owner/$repository/actions/workflows/ci.yml/runs?$query"
    $workflowRuns = Invoke-RestMethod -Method Get -Uri $workflowRunsUrl -TimeoutSec 20 -Headers @{
        Accept = "application/vnd.github+json"
        "User-Agent" = "TFlexDrawingService-AutoUpdate"
    }
    $matchingRuns = @($workflowRuns.workflow_runs | Where-Object {
        $_.head_sha -eq $targetRevision -and $_.head_branch -eq $branch -and $_.event -eq "push"
    } | Sort-Object run_number -Descending)
    if ($matchingRuns.Count -eq 0) {
        $message = "Waiting for a successful CI run for revision $targetRevision on $branch."
        Write-Host $message -ForegroundColor Yellow
        Write-UpdateState $statusPath "waiting-for-ci" $currentRevision $targetRevision $message
        return
    }

    $latestRun = $matchingRuns[0]
    if ($latestRun.status -ne "completed") {
        $message = "Waiting for CI run $($latestRun.run_number) to complete for revision $targetRevision."
        Write-Host $message -ForegroundColor Yellow
        Write-UpdateState $statusPath "waiting-for-ci" $currentRevision $targetRevision $message
        return
    }
    if ($latestRun.conclusion -ne "success") {
        $message = "CI run $($latestRun.run_number) concluded '$($latestRun.conclusion)' for revision $targetRevision."
        Write-Host $message -ForegroundColor Red
        Write-UpdateState $statusPath "ci-failed" $currentRevision $targetRevision $message
        return
    }

    $ancestorCheck = Invoke-NativeResult -FilePath $git.Source -Arguments @(
        "-C", $sourceRoot, "merge-base", "--is-ancestor", $currentRevision, $targetRevision)
    if ($ancestorCheck.ExitCode -ne 0) {
        throw "Automatic update refused because origin/$branch is not a fast-forward from the local revision."
    }

    if ($currentRevision -ne $targetRevision) {
        $sourceAdvanced = $true
        Invoke-Native -FilePath $git.Source -Arguments @("-C", $sourceRoot, "checkout", $branch) | Out-Null
        Invoke-Native -FilePath $git.Source -Arguments @(
            "-C", $sourceRoot, "merge", "--ff-only", $remoteReference) | Out-Null
    }

    $deployParameters = @{
        RepositoryUrl = $repositoryUrl
        Branch = $branch
        InstallRoot = $installRoot
        SourceRoot = $sourceRoot
        UseExistingSource = $true
        TFlexCadProgramDir = [string](Get-ConfigValue $config "tFlexCadProgramDir" "C:\Program Files\T-FLEX CAD 17\Program")
        TFlexAutomationCommandPath = [string](Get-ConfigValue $config "tFlexAutomationCommandPath" "")
        AdminUser = [string](Get-ConfigValue $config "adminUser" "admin")
        MaxActiveJobs = [int](Get-ConfigValue $config "maxActiveJobs" 50)
        MaxActiveJobsPerUser = [int](Get-ConfigValue $config "maxActiveJobsPerUser" 5)
        FinishedJobRetentionDays = [int](Get-ConfigValue $config "finishedJobRetentionDays" 30)
        HealthCheckAttempts = [int](Get-ConfigValue $config "healthCheckAttempts" 12)
        HealthCheckDelaySeconds = [int](Get-ConfigValue $config "healthCheckDelaySeconds" 5)
        ApiHostPort = [int](Get-ConfigValue $config "apiHostPort" 5011)
        CandidateHostPort = [int](Get-ConfigValue $config "candidateHostPort" 5012)
        ContainerName = [string](Get-ConfigValue $config "containerName" "tflex-drawing-api")
        ApiImageRepository = [string](Get-ConfigValue $config "apiImageRepository" "tflex-drawing-service-api")
        Domain = [string](Get-ConfigValue $config "domain" "")
        AcmeEmail = [string](Get-ConfigValue $config "acmeEmail" "")
        AutomaticUpdateTaskName = $taskName
        AutomaticUpdateTime = $updateTime
        AutomaticUpdateIntervalMinutes = $updateIntervalMinutes
        SkipCaddy = $true
    }
    if ([bool](Get-ConfigValue $config "skipFirewall" $false)) {
        $deployParameters.SkipFirewall = $true
    }

    Write-Host "Deploying revision $targetRevision." -ForegroundColor Cyan
    $deploymentStarted = $true
    & $deployScript @deployParameters
    if (-not $?) {
        throw "Hybrid deployment returned an unsuccessful result."
    }

    $recordedRevision = if (Test-Path -LiteralPath $successMarkerPath -PathType Leaf) {
        (Get-Content -LiteralPath $successMarkerPath -Encoding UTF8 -Raw).Trim()
    }
    else {
        ""
    }
    if ($recordedRevision -ne $targetRevision) {
        throw "Deployment completed without recording the expected successful revision."
    }

    $message = "Successfully deployed revision $targetRevision."
    Write-Host $message -ForegroundColor Green
    Write-UpdateState $statusPath "updated" $currentRevision $targetRevision $message
}
catch {
    $failureRecord = $_
    $message = $failureRecord.Exception.Message
    Write-Host $message -ForegroundColor Red
    try {
        Write-UpdateState $statusPath "failed" $currentRevision $targetRevision $message
    }
    catch {
        Write-Warning "Failed to write automatic update failure status: $($_.Exception.Message)"
    }

    if ($sourceAdvanced -and -not $deploymentStarted -and -not [string]::IsNullOrWhiteSpace($currentRevision)) {
        try {
            $gitForRollback = Get-Command git.exe -ErrorAction SilentlyContinue
            if ($null -eq $gitForRollback) {
                $gitForRollback = Get-Command git -ErrorAction Stop
            }
            Invoke-Native -FilePath $gitForRollback.Source -Arguments @(
                "-C", $sourceRoot, "reset", "--hard", $currentRevision) | Out-Null
            Write-Warning "SourceRoot was restored to revision $currentRevision after the failed deployment."
        }
        catch {
            Write-Warning "SourceRoot rollback also failed: $($_.Exception.Message)"
        }
    }

    throw $failureRecord
}
finally {
    [Environment]::SetEnvironmentVariable("GIT_TERMINAL_PROMPT", $previousGitTerminalPrompt, "Process")
    if ($transcriptStarted) {
        try { Stop-Transcript | Out-Null } catch { }
    }
    if ($null -ne $lockStream) {
        $lockStream.Dispose()
    }
}
