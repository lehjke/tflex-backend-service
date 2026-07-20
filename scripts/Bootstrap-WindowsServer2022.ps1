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
    [switch]$UseExistingSource,
    [string]$Urls = "http://127.0.0.1:5011",
    [string]$TFlexCadProgramDir = "C:\Program Files\T-FLEX CAD 17\Program",
    [string]$TFlexAutomationCommandPath = "",
    [string]$ServiceUser = "",
    [string]$ServicePassword = "",
    [string]$PreviousServicePassword = "",
    [string]$AdminUser = "admin",
    [string]$AdminPassword = "",
    [string]$AdminPasswordHash = "",
    [bool]$RequireAuthentication = $true,
    [int]$MaxActiveJobs = 50,
    [int]$MaxActiveJobsPerUser = 5,
    [int]$FinishedJobRetentionDays = 30,
    [ValidateRange(1, 60)]
    [int]$HealthCheckAttempts = 12,
    [ValidateRange(1, 30)]
    [int]$HealthCheckDelaySeconds = 5,
    [ValidatePattern("^\d+\.\d+\.\d+$")]
    [string]$DotNetSdkVersion = "10.0.302",
    [string]$DotNetInstallDir = "$env:ProgramFiles\dotnet",
    [string]$InstallerScriptUrl = "",
    [string]$WorkDir = "C:\Temp\TFlexDrawingServiceBootstrap",
    [switch]$SkipGitInstall,
    [switch]$SkipDotNetInstall,
    [switch]$SkipNetFx472DeveloperPackInstall,
    [switch]$SkipRunnerBuild,
    [switch]$SkipRunnerHealthCheck,
    [switch]$SkipServiceInstall,
    [switch]$SkipFirewall,
    [switch]$SkipTFlexCheck
)

$ErrorActionPreference = "Stop"

# These pins are reviewed trust anchors. Update each URL/version/hash together from
# the vendor's official release record; never discover a digest from the same
# download response at runtime. The dotnet-install hash below belongs to the
# Microsoft-signed stable endpoint artifact reviewed on 2026-07-20, not to the
# unsigned raw source file in the install-scripts repository.
$DotNetInstallScriptReviewedOn = "2026-07-20"
$DotNetInstallScriptUrl = "https://builds.dotnet.microsoft.com/dotnet/scripts/v1/dotnet-install.ps1"
$DotNetInstallScriptSha256 = "6585899aed55ff6ae13dbe1e8c3b878f2d00433520e7efbe250b75db948b7da9"
$DotNetInstallScriptAllowedHosts = @("builds.dotnet.microsoft.com")
$GitForWindowsVersion = "2.55.0.3"
$GitForWindowsInstallerUrl = "https://github.com/git-for-windows/git/releases/download/v2.55.0.windows.3/Git-2.55.0.3-64-bit.exe"
$GitForWindowsInstallerSha256 = "af12577d0fdff74243a5988197aa49b957d5044edc17004f6ddf0768996f1dca"
$GitForWindowsInstallerAllowedHosts = @("github.com", "release-assets.githubusercontent.com")
$VisualStudioBuildToolsVersion = "17.14.36"
$VisualStudioBuildToolsUrl = "https://download.visualstudio.microsoft.com/download/pr/12aa1305-dd17-4f26-8429-d072cda64c80/5ae95bb02bb3442441a8d891e5bb1d2975445e2e3ee16ada5bc7bd17227f1dd7/vs_BuildTools.exe"
$VisualStudioBuildToolsSha256 = "5ae95bb02bb3442441a8d891e5bb1d2975445e2e3ee16ada5bc7bd17227f1dd7"
$VisualStudioBuildToolsAllowedHosts = @("download.visualstudio.microsoft.com")
$NetFx472DeveloperPackVersion = "4.7.2"
$NetFx472DeveloperPackUrl = "https://download.microsoft.com/download/7/1/7/71795fde-1cca-41b0-b495-00b1ab656994/NDP472-DevPack-ENU.exe"
$NetFx472DeveloperPackSha256 = "1fa87cc7135a5360fd8b692b5118ec60963d4ce73db4a996ca62afa2b5623a6b"
$NetFx472DeveloperPackAllowedHosts = @("download.microsoft.com")
$MicrosoftPublisher = "Microsoft Corporation"

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

    # Do not print arguments. This helper is also used to launch the service
    # installer, and future parameters must not accidentally expose a secret.
    Write-Host "Running: $FilePath" -ForegroundColor DarkGray
    & $FilePath @Arguments 2>&1 | ForEach-Object {
        # Stream child output instead of buffering it. The installer prints the
        # generated bootstrap credential before it can seed persistent storage,
        # so operators must see that output even if deployment later fails.
        Write-Host ([string]$_)
    }
    $exitCode = $LASTEXITCODE
    if ($AllowedExitCodes -notcontains $exitCode) {
        throw "Native command '$FilePath' failed with exit code $exitCode."
    }

    return $exitCode
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
        [string[]]$AllowedHosts,
        [string]$ExpectedPublisher = ""
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

        if (-not [string]::IsNullOrWhiteSpace($ExpectedPublisher)) {
            $signature = Get-AuthenticodeSignature -LiteralPath $DestinationPath
            if ([string]$signature.Status -ne "Valid" -or
                $null -eq $signature.SignerCertificate) {
                throw "Verified download '$ArtifactName' does not have a valid Authenticode signature."
            }

            $publisherPattern = '(^|,\s*)O={0}($|,\s*)' -f (
                [Regex]::Escape($ExpectedPublisher))
            if ([string]$signature.SignerCertificate.Subject -notmatch $publisherPattern) {
                throw "Verified download '$ArtifactName' was not signed by the expected publisher."
            }
        }
    }
    catch {
        Remove-Item -LiteralPath $DestinationPath -Force -ErrorAction SilentlyContinue
        throw
    }
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

function Test-DotNetSdkVersion {
    param([string]$Version)

    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $dotnet) {
        return $false
    }

    $sdks = & $dotnet.Source --list-sdks 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet --list-sdks failed with exit code $LASTEXITCODE."
    }
    return [bool]($sdks | Where-Object {
        ([string]$_).StartsWith("$Version ", [StringComparison]::Ordinal)
    })
}

function Install-DotNetSdk {
    New-Item -ItemType Directory -Path $WorkDir, $DotNetInstallDir -Force | Out-Null
    $scriptPath = Join-Path $WorkDir "dotnet-install.ps1"
    Invoke-VerifiedDownload `
        -Uri $DotNetInstallScriptUrl `
        -DestinationPath $scriptPath `
        -ExpectedSha256 $DotNetInstallScriptSha256 `
        -ArtifactName "Microsoft-signed dotnet-install.ps1 reviewed $DotNetInstallScriptReviewedOn" `
        -AllowedHosts $DotNetInstallScriptAllowedHosts `
        -ExpectedPublisher $MicrosoftPublisher

    $arguments = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", $scriptPath,
        "-Version", $DotNetSdkVersion,
        "-Architecture", "x64",
        "-InstallDir", $DotNetInstallDir
    )

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
    $installerPath = Join-Path $WorkDir "Git-$GitForWindowsVersion-64-bit.exe"
    Invoke-VerifiedDownload `
        -Uri $GitForWindowsInstallerUrl `
        -DestinationPath $installerPath `
        -ExpectedSha256 $GitForWindowsInstallerSha256 `
        -ArtifactName "Git for Windows $GitForWindowsVersion" `
        -AllowedHosts $GitForWindowsInstallerAllowedHosts
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

function Install-VsBuildToolsNetFx472TargetingPack {
    New-Item -ItemType Directory -Path $WorkDir -Force | Out-Null
    $installerPath = Join-Path $WorkDir "vs_buildtools.exe"
    Invoke-VerifiedDownload `
        -Uri $VisualStudioBuildToolsUrl `
        -DestinationPath $installerPath `
        -ExpectedSha256 $VisualStudioBuildToolsSha256 `
        -ArtifactName "Visual Studio Build Tools $VisualStudioBuildToolsVersion" `
        -AllowedHosts $VisualStudioBuildToolsAllowedHosts `
        -ExpectedPublisher $MicrosoftPublisher

    $exitCode = Invoke-Native -FilePath $installerPath -Arguments @(
        "--quiet",
        "--wait",
        "--norestart",
        "--nocache",
        "--add", "Microsoft.Net.Component.4.7.2.TargetingPack"
    ) -AllowedExitCodes @(0, 3010, 1641)

    if ($exitCode -eq 3010 -or $exitCode -eq 1641) {
        Write-Warning "Visual Studio Build Tools requested a reboot."
    }
}

function Install-NetFx472DeveloperPack {
    if (Test-NetFx472TargetingPack) {
        return
    }

    New-Item -ItemType Directory -Path $WorkDir -Force | Out-Null
    $installerPath = Join-Path $WorkDir "NDP472-DevPack-ENU.exe"
    Invoke-VerifiedDownload `
        -Uri $NetFx472DeveloperPackUrl `
        -DestinationPath $installerPath `
        -ExpectedSha256 $NetFx472DeveloperPackSha256 `
        -ArtifactName ".NET Framework $NetFx472DeveloperPackVersion Developer Pack" `
        -AllowedHosts $NetFx472DeveloperPackAllowedHosts `
        -ExpectedPublisher $MicrosoftPublisher
    $exitCode = Invoke-Native -FilePath $installerPath -Arguments @("/q", "/norestart") -AllowedExitCodes @(0, 3010, 1641, 1638, 5100)
    if ($exitCode -eq 3010 -or $exitCode -eq 1641) {
        Write-Warning ".NET Framework 4.7.2 Developer Pack requested a reboot."
    }
    elseif ($exitCode -eq 1638 -or $exitCode -eq 5100) {
        Write-Warning ".NET Framework 4.7.2 Developer Pack was blocked because another/newer .NET Framework is already installed."
    }

    if (-not (Test-NetFx472TargetingPack)) {
        Write-Warning ".NET Framework 4.7.2 Developer Pack did not add reference assemblies. Trying Visual Studio Build Tools targeting pack component."
        Install-VsBuildToolsNetFx472TargetingPack
    }

    if (-not (Test-NetFx472TargetingPack)) {
        throw ".NET Framework 4.7.2 targeting pack was not found after installation. Check Visual Studio Installer logs or pass -SkipRunnerBuild with a prebuilt runner."
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
    if ($UseExistingSource) {
        $sourceInstallerPath = Join-Path $SourceRoot "scripts\Install-TFlexDrawingService.ps1"
        if (-not (Test-Path -LiteralPath $sourceInstallerPath -PathType Leaf)) {
            throw "The service installer was not found under SourceRoot."
        }

        return $sourceInstallerPath
    }

    New-Item -ItemType Directory -Path $WorkDir -Force | Out-Null
    $downloadPath = Join-Path $WorkDir "Install-TFlexDrawingService.ps1"

    $downloadUrl = $InstallerScriptUrl
    if ([string]::IsNullOrWhiteSpace($downloadUrl)) {
        $normalizedRepositoryUrl = $RepositoryUrl.TrimEnd('/')
        if ($normalizedRepositoryUrl.EndsWith(".git", [StringComparison]::OrdinalIgnoreCase)) {
            $normalizedRepositoryUrl = $normalizedRepositoryUrl.Substring(0, $normalizedRepositoryUrl.Length - 4)
        }

        if ($normalizedRepositoryUrl -match '^https://github\.com/([^/]+)/([^/]+)$') {
            $owner = [Uri]::EscapeDataString($Matches[1])
            $repository = [Uri]::EscapeDataString($Matches[2])
            $branchPath = (($Branch -split '/') | ForEach-Object { [Uri]::EscapeDataString($_) }) -join '/'
            $downloadUrl = "https://raw.githubusercontent.com/$owner/$repository/$branchPath/scripts/Install-TFlexDrawingService.ps1"
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($downloadUrl)) {
        Invoke-WebRequest -Uri $downloadUrl -OutFile $downloadPath -UseBasicParsing
    }
    else {
        $git = Get-Command git -ErrorAction SilentlyContinue
        if ($null -eq $git) {
            throw "The installer URL cannot be derived from RepositoryUrl. Install Git or pass -InstallerScriptUrl explicitly."
        }

        $installerCheckout = Join-Path $WorkDir "installer-source"
        if (Test-Path -LiteralPath $installerCheckout) {
            Remove-Item -LiteralPath $installerCheckout -Recurse -Force
        }

        Invoke-Native -FilePath $git.Source -Arguments @(
            "clone", "--depth", "1", "--branch", $Branch, "--", $RepositoryUrl, $installerCheckout
        ) | Out-Null
        $downloadPath = Join-Path $installerCheckout "scripts\Install-TFlexDrawingService.ps1"
    }

    if (-not (Test-Path -LiteralPath $downloadPath -PathType Leaf)) {
        throw "The service installer could not be obtained from the configured repository and branch."
    }

    $tokens = $null
    $parseErrors = $null
    [Management.Automation.Language.Parser]::ParseFile($downloadPath, [ref]$tokens, [ref]$parseErrors) | Out-Null
    if (@($parseErrors).Count -gt 0) {
        throw "The downloaded service installer is not valid PowerShell."
    }

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
        "-TFlexCadProgramDir", $TFlexCadProgramDir,
        "-AdminUser", $AdminUser,
        "-MaxActiveJobs", $MaxActiveJobs.ToString(),
        "-MaxActiveJobsPerUser", $MaxActiveJobsPerUser.ToString(),
        "-FinishedJobRetentionDays", $FinishedJobRetentionDays.ToString(),
        "-HealthCheckAttempts", $HealthCheckAttempts.ToString(),
        "-HealthCheckDelaySeconds", $HealthCheckDelaySeconds.ToString()
    )

    if (-not [string]::IsNullOrWhiteSpace($SourceRoot)) {
        $arguments += @("-SourceRoot", $SourceRoot)
    }

    if ($UseExistingSource) {
        $arguments += "-UseExistingSource"
    }

    if (-not [string]::IsNullOrWhiteSpace($TFlexAutomationCommandPath)) {
        $arguments += @("-TFlexAutomationCommandPath", $TFlexAutomationCommandPath)
    }

    if (-not [string]::IsNullOrWhiteSpace($ServiceUser)) {
        $arguments += @("-ServiceUser", $ServiceUser)
    }

    if ($SkipRunnerBuild) {
        $arguments += "-SkipRunnerBuild"
    }

    if ($SkipRunnerHealthCheck) {
        $arguments += "-SkipRunnerHealthCheck"
    }

    if ($SkipServiceInstall) {
        $arguments += "-SkipServiceInstall"
    }

    if ($SkipFirewall) {
        $arguments += "-SkipFirewall"
    }

    $secretEnvironment = [ordered]@{
        TFLEX_INSTALL_SERVICE_PASSWORD = $ServicePassword
        TFLEX_INSTALL_PREVIOUS_SERVICE_PASSWORD = $PreviousServicePassword
        TFLEX_INSTALL_ADMIN_PASSWORD = $AdminPassword
        TFLEX_INSTALL_ADMIN_PASSWORD_HASH = $AdminPasswordHash
    }
    $previousSecretEnvironment = @{}

    try {
        foreach ($entry in $secretEnvironment.GetEnumerator()) {
            $previousSecretEnvironment[$entry.Key] = [Environment]::GetEnvironmentVariable($entry.Key, "Process")
            $value = if ([string]::IsNullOrEmpty([string]$entry.Value)) { $null } else { [string]$entry.Value }
            [Environment]::SetEnvironmentVariable($entry.Key, $value, "Process")
        }

        Invoke-Native -FilePath "powershell.exe" -Arguments $arguments | Out-Null
    }
    finally {
        foreach ($entry in $secretEnvironment.GetEnumerator()) {
            [Environment]::SetEnvironmentVariable($entry.Key, $previousSecretEnvironment[$entry.Key], "Process")
        }
    }
}

if ([System.Environment]::OSVersion.Platform -ne [System.PlatformID]::Win32NT) {
    throw "This bootstrap script is intended for Windows Server 2022."
}

if (-not (Test-IsAdmin)) {
    throw "Run PowerShell as Administrator."
}

if ($UseExistingSource -and [string]::IsNullOrWhiteSpace($SourceRoot)) {
    throw "SourceRoot must be specified when UseExistingSource is enabled."
}

# The service installer defaults to authenticated production mode. Do not pass
# this [bool] through powershell.exe -File: Windows PowerShell 5.1 exposes the
# native command-line value as a String and cannot bind "True" to Boolean.
if (-not $RequireAuthentication) {
    throw "Windows Server production deployments require authentication."
}

Set-Tls12
New-Item -ItemType Directory -Path $WorkDir -Force | Out-Null

Write-Step "Installing prerequisites"
if (-not $SkipGitInstall -and -not $UseExistingSource) {
    Install-Git
}

if (-not $SkipDotNetInstall -and -not (Test-DotNetSdkVersion $DotNetSdkVersion)) {
    Install-DotNetSdk
}

if (-not (Test-DotNetSdkVersion $DotNetSdkVersion)) {
    throw ".NET SDK version $DotNetSdkVersion was not found. Install it or remove -SkipDotNetInstall."
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
$healthEndpoint = if ($SkipRunnerHealthCheck) { "live" } else { "ready" }
$healthUri = "http://127.0.0.1:5011/api/health/$healthEndpoint"
if ($Urls -match "^https://") {
    $firstUri = [Uri](($Urls -split ";")[0])
    $healthUri = "$($firstUri.GetLeftPart([UriPartial]::Authority))/api/health/$healthEndpoint"
}
elseif ($Urls -match "http://[^:]+:(\d+)") {
    $healthUri = "http://127.0.0.1:$($Matches[1])/api/health/$healthEndpoint"
}

$lastHealthError = ""
for ($attempt = 1; $attempt -le $HealthCheckAttempts; $attempt++) {
    try {
        $response = Invoke-WebRequest -Uri $healthUri -UseBasicParsing -TimeoutSec 20
        if ($response.StatusCode -eq 200) {
            $lastHealthError = ""
            break
        }
        $lastHealthError = "HTTP $($response.StatusCode)"
    }
    catch {
        $lastHealthError = $_.Exception.Message
    }

    if ($attempt -lt $HealthCheckAttempts) {
        Start-Sleep -Seconds $HealthCheckDelaySeconds
    }
}

if (-not [string]::IsNullOrWhiteSpace($lastHealthError)) {
    throw "Final health check failed at '$healthUri': $lastHealthError"
}

if ($SkipRunnerHealthCheck) {
    $readyUri = $healthUri -replace "/api/health/live$", "/api/health/ready"
    try {
        $readyResponse = Invoke-WebRequest -Uri $readyUri -UseBasicParsing -TimeoutSec 20
        throw "Diagnostic bootstrap unexpectedly reported ready at '$readyUri' (HTTP $($readyResponse.StatusCode))."
    }
    catch {
        $statusCode = $null
        if ($null -ne $_.Exception.Response) {
            $statusCode = [int]$_.Exception.Response.StatusCode
        }
        if ($statusCode -ne 503) {
            throw
        }
    }
    Write-Warning "Runner health-check was skipped; liveness passed but readiness remains HTTP 503."
}

if (-not $SkipServiceInstall) {
    foreach ($serviceName in @("TFlexDrawingService.Api", "TFlexDrawingService.Worker")) {
        $service = Get-Service -Name $serviceName -ErrorAction Stop
        if ($service.Status -ne "Running") {
            throw "Final smoke check failed: Windows service '$serviceName' is not running."
        }
    }
}

Write-Host "Health check passed: $healthUri ($($response.StatusCode))" -ForegroundColor Green

Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "InstallRoot: $InstallRoot" -ForegroundColor Green
Write-Host "API URL: $Urls" -ForegroundColor Green
