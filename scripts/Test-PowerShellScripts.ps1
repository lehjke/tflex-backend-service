<#
.SYNOPSIS
Performs portable static checks for repository PowerShell deployment scripts.
#>
[CmdletBinding()]
param(
    [switch]$RunScriptAnalyzer
)

$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path $PSScriptRoot -Parent
$scriptFiles = @(
    Get-ChildItem -LiteralPath $PSScriptRoot -Filter "*.ps1" -File |
        Sort-Object FullName
)

if ($scriptFiles.Count -eq 0) {
    throw "No PowerShell scripts were found under '$PSScriptRoot'."
}

$parseFailures = @()
foreach ($scriptFile in $scriptFiles) {
    $tokens = $null
    $parseErrors = $null
    [Management.Automation.Language.Parser]::ParseFile(
        $scriptFile.FullName,
        [ref]$tokens,
        [ref]$parseErrors) | Out-Null

    foreach ($parseError in @($parseErrors)) {
        $parseFailures += "$($scriptFile.Name):$($parseError.Extent.StartLineNumber): $($parseError.Message)"
    }
}

if ($parseFailures.Count -gt 0) {
    throw "PowerShell parser failures:`n$($parseFailures -join [Environment]::NewLine)"
}

$installerPath = Join-Path $PSScriptRoot "Install-TFlexDrawingService.ps1"
$bootstrapPath = Join-Path $PSScriptRoot "Bootstrap-WindowsServer2022.ps1"
$caddyInstallerPath = Join-Path $PSScriptRoot "Install-CaddyAcmeProxy.ps1"
$installerText = Get-Content -LiteralPath $installerPath -Raw
$bootstrapText = Get-Content -LiteralPath $bootstrapPath -Raw
$caddyInstallerText = Get-Content -LiteralPath $caddyInstallerPath -Raw

$requiredInstallerContracts = @(
    'VariableName "DOTNET_ENVIRONMENT"',
    'VariableName "ASPNETCORE_ENVIRONMENT"',
    'VariableName "TFlexAutomation__Mode"',
    'VariableName "TFlexAutomation__CommandPath"',
    'VariableName "TFlexAutomation__HealthCheckEnabled"',
    'HealthCheckEnabled = (-not $SkipRunnerHealthCheck)',
    'Value "ExternalProcess"',
    'Get-ServiceAccountIdentity',
    'Write-BootstrapAdminRecovery',
    'Read-BootstrapAdminRecovery',
    'Remove-BootstrapAdminRecovery',
    'ProtectedData]::Protect',
    'DataProtectionScope]::LocalMachine',
    'S-1-5-18',
    'S-1-5-32-544',
    'Assert-ReadinessUnconfirmed',
    'Restore-ServiceConfiguration',
    'Get-DirectoryAclSnapshots',
    'Restore-DirectoryAclSnapshots',
    'Remove-ExplicitDirectoryAccessForSid',
    'RemoveAccessRuleSpecific',
    '$entry.FileSystemRights -ne $expectedRule.FileSystemRights',
    '$entry.PropagationFlags -ne $expectedRule.PropagationFlags',
    'LsaEnumerateAccountRights',
    'LsaRemoveAccountRights',
    'Test-ServiceAccountSidInUse',
    'Remove-LogOnAsServiceRightIfUnused',
    '/api/projects',
    'appsettings.Development.json'
)
foreach ($contract in $requiredInstallerContracts) {
    if (-not $installerText.Contains($contract)) {
        throw "The service installer is missing required deployment contract '$contract'."
    }
}

$forbiddenInstallerContracts = @(
    'if\s*\([^)]*\$SkipRunnerBuild[^)]*\$SkipRunnerHealthCheck[^)]*\)',
    'if\s*\([^)]*\$SkipRunnerHealthCheck[^)]*\$SkipRunnerBuild[^)]*\)',
    'Invoke-TFlexRunnerHealthCheck\s+\$TFlexAutomationCommandPath'
)
foreach ($contract in $forbiddenInstallerContracts) {
    if ($installerText -match $contract) {
        throw "The service installer contains forbidden deployment contract '$contract'."
    }
}
if ($installerText.Contains('PurgeAccessRules')) {
    throw "The service installer must preserve inherited and unrelated access rules."
}
if ($installerText.Contains(
        '(Get-ItemProperty -LiteralPath $serviceKey -Name Environment -ErrorAction SilentlyContinue).Environment') -or
    -not $installerText.Contains(
        '$serviceProperties = Get-ItemProperty -LiteralPath $serviceKey -ErrorAction Stop') -or
    -not $installerText.Contains(
        '$existing = if ($null -eq $serviceProperties.Environment)')) {
    throw "Fresh Windows service installation must treat a missing Environment registry value as empty."
}

$recoveryWriteIndex = $installerText.LastIndexOf('Write-BootstrapAdminRecovery `')
$activationIndex = $installerText.IndexOf('Write-Step "Activating staged deployment"')
$effectiveAclIndex = $installerText.IndexOf('Write-Step "Configuring effective service account file access"')
$aclSnapshotIndex = $installerText.IndexOf('Get-DirectoryAclSnapshots $managedAccessDirectories')
$obsoleteAclRemovalIndex = $installerText.LastIndexOf('Remove-ServiceFileSystemAccess `')
$deploymentFailureIndex = $installerText.IndexOf(
    'Write-Warning "Deployment validation failed: $deploymentErrorMessage Restoring the previous deployment."')
$aclRestoreIndex = $installerText.IndexOf(
    'Restore-DirectoryAclSnapshots $directoryAclSnapshots')
$rollbackPossibleIndex = $installerText.LastIndexOf('Assert-ServiceAccountRollbackPossible')
$rightGrantIndex = $installerText.IndexOf('Grant-LogOnAsServiceRight `')
$rightRollbackIndex = $installerText.LastIndexOf('Remove-LogOnAsServiceRightIfUnused `')
$modeOverrideIndex = $installerText.IndexOf('VariableName "TFlexAutomation__Mode"')
$serviceStartIndex = $installerText.IndexOf('Write-Step "Starting Windows services"')
$credentialPrintIndex = $installerText.LastIndexOf('bootstrap admin password:')
$recoveryRemoveIndex = $installerText.LastIndexOf(
    'Remove-BootstrapAdminRecovery $bootstrapAdminRecoveryPath')
$systemSecurityLoadIndex = $installerText.IndexOf(
    'Add-Type -AssemblyName System.Security -ErrorAction Stop')
$protectedDataUseIndex = $installerText.IndexOf('[Security.Cryptography.ProtectedData]')

if ($systemSecurityLoadIndex -lt 0 -or $systemSecurityLoadIndex -ge $protectedDataUseIndex) {
    throw "The Windows installer must load System.Security before using DPAPI."
}

if ($recoveryWriteIndex -lt 0 -or $recoveryWriteIndex -ge $serviceStartIndex) {
    throw "Bootstrap admin recovery must be persisted before Windows services start."
}
if ($activationIndex -lt 0 -or
    $effectiveAclIndex -le $activationIndex -or
    $effectiveAclIndex -ge $serviceStartIndex) {
    throw "Effective service ACLs must be reapplied after activation and before Windows services start."
}
if ($aclSnapshotIndex -lt 0 -or
    $obsoleteAclRemovalIndex -le $aclSnapshotIndex -or
    $effectiveAclIndex -le $obsoleteAclRemovalIndex) {
    throw "Managed directory ACLs must be snapshotted before obsolete service SID access is removed."
}
if ($deploymentFailureIndex -le $effectiveAclIndex -or
    $aclRestoreIndex -le $deploymentFailureIndex) {
    throw "Managed directory ACLs must be restored during deployment rollback."
}
if (-not $installerText.Contains('$deploymentError = $_') -or
    -not $installerText.Contains(
        'Deployment failed: $deploymentErrorMessage The previous deployment was restored.') -or
    -not $installerText.Contains('sc.exe failed with exit code $LASTEXITCODE`: $details')) {
    throw "Deployment rollback must retain the primary Windows service failure message."
}
if ($rightGrantIndex -le $rollbackPossibleIndex -or
    $rightRollbackIndex -le $deploymentFailureIndex) {
    throw "Installer-owned service logon rights must be granted and rolled back transactionally."
}
if (-not $installerText.Contains(
        '-GrantedByDeployment ([ref]$serviceLogonRightGrantedByDeployment)')) {
    throw "The service logon-right grant must expose partial success to the deployment rollback path."
}
if ($modeOverrideIndex -lt 0 -or $modeOverrideIndex -ge $serviceStartIndex) {
    throw "ExternalProcess service environment overrides must be written before Windows services start."
}
if ($credentialPrintIndex -lt 0 -or
    $recoveryRemoveIndex -le $credentialPrintIndex) {
    throw "The bootstrap admin password must be printed before protected recovery state is removed."
}

if (-not $bootstrapText.Contains('& $FilePath @Arguments 2>&1 | ForEach-Object')) {
    throw "The Windows bootstrapper must stream child installer output."
}
if ($bootstrapText.Contains('$output = & $FilePath @Arguments')) {
    throw "The Windows bootstrapper must not buffer generated bootstrap credentials until child exit."
}
if ($bootstrapText.Contains('"-RequireAuthentication", $RequireAuthentication.ToString()') -or
    -not $bootstrapText.Contains('if (-not $RequireAuthentication)') -or
    -not $bootstrapText.Contains('Windows Server production deployments require authentication.')) {
    throw "The Windows bootstrapper must not forward Boolean authentication through powershell.exe -File."
}
if (-not $bootstrapText.Contains(
        '$healthEndpoint = if ($SkipRunnerHealthCheck) { "live" } else { "ready" }') -or
    -not $bootstrapText.Contains('$statusCode -ne 503')) {
    throw "Diagnostic bootstrap must accept liveness only while keeping readiness at HTTP 503."
}

$requiredBootstrapDownloadContracts = @(
    'function Invoke-VerifiedDownload',
    '-MaximumRedirection 5',
    '$downloadResponse.BaseResponse.ResponseUri',
    '$normalizedAllowedHosts -notcontains $finalHost',
    'Get-FileHash -LiteralPath $DestinationPath -Algorithm SHA256',
    'Get-AuthenticodeSignature -LiteralPath $DestinationPath',
    '$DotNetSdkVersion = "10.0.302"',
    '$DotNetInstallScriptReviewedOn = "2026-07-20"',
    '$DotNetInstallScriptUrl = "https://builds.dotnet.microsoft.com/dotnet/scripts/v1/dotnet-install.ps1"',
    '$DotNetInstallScriptSha256 = "6585899aed55ff6ae13dbe1e8c3b878f2d00433520e7efbe250b75db948b7da9"',
    '$DotNetInstallScriptAllowedHosts = @("builds.dotnet.microsoft.com")',
    '$GitForWindowsVersion = "2.55.0.3"',
    '$GitForWindowsInstallerUrl = "https://github.com/git-for-windows/git/releases/download/v2.55.0.windows.3/Git-2.55.0.3-64-bit.exe"',
    '$GitForWindowsInstallerSha256 = "af12577d0fdff74243a5988197aa49b957d5044edc17004f6ddf0768996f1dca"',
    '$GitForWindowsInstallerAllowedHosts = @("github.com", "release-assets.githubusercontent.com")',
    '$VisualStudioBuildToolsVersion = "17.14.36"',
    '$VisualStudioBuildToolsUrl = "https://download.visualstudio.microsoft.com/download/pr/12aa1305-dd17-4f26-8429-d072cda64c80/5ae95bb02bb3442441a8d891e5bb1d2975445e2e3ee16ada5bc7bd17227f1dd7/vs_BuildTools.exe"',
    '$VisualStudioBuildToolsSha256 = "5ae95bb02bb3442441a8d891e5bb1d2975445e2e3ee16ada5bc7bd17227f1dd7"',
    '$VisualStudioBuildToolsAllowedHosts = @("download.visualstudio.microsoft.com")',
    '$NetFx472DeveloperPackVersion = "4.7.2"',
    '$NetFx472DeveloperPackUrl = "https://download.microsoft.com/download/7/1/7/71795fde-1cca-41b0-b495-00b1ab656994/NDP472-DevPack-ENU.exe"',
    '$NetFx472DeveloperPackSha256 = "1fa87cc7135a5360fd8b692b5118ec60963d4ce73db4a996ca62afa2b5623a6b"',
    '$NetFx472DeveloperPackAllowedHosts = @("download.microsoft.com")',
    '-ExpectedPublisher $MicrosoftPublisher'
)
foreach ($contract in $requiredBootstrapDownloadContracts) {
    if (-not $bootstrapText.Contains($contract)) {
        throw "The Windows bootstrapper is missing verified-download contract '$contract'."
    }
}

$bootstrapVerifiedDownloadCalls = [regex]::Matches(
    $bootstrapText,
    '(?m)^\s*Invoke-VerifiedDownload\s+`').Count
if ($bootstrapVerifiedDownloadCalls -ne 4) {
    throw "The Windows bootstrapper must verify exactly four pinned prerequisite downloads."
}

$bootstrapPublisherChecks = [regex]::Matches(
    $bootstrapText,
    '(?m)^\s*-ExpectedPublisher \$MicrosoftPublisher\s*$').Count
if ($bootstrapPublisherChecks -ne 3) {
    throw "The three Microsoft script/installer downloads must enforce the expected Authenticode publisher."
}

$bootstrapDownloadToSinkContracts = @(
    @{
        Uri = '-Uri $DotNetInstallScriptUrl'
        Hash = '-ExpectedSha256 $DotNetInstallScriptSha256'
        Hosts = '-AllowedHosts $DotNetInstallScriptAllowedHosts'
        Publisher = '-ExpectedPublisher $MicrosoftPublisher'
        Sink = 'Invoke-Native -FilePath "powershell.exe"'
    },
    @{
        Uri = '-Uri $GitForWindowsInstallerUrl'
        Hash = '-ExpectedSha256 $GitForWindowsInstallerSha256'
        Hosts = '-AllowedHosts $GitForWindowsInstallerAllowedHosts'
        Publisher = ""
        Sink = 'Invoke-Native -FilePath $installerPath -Arguments @('
    },
    @{
        Uri = '-Uri $VisualStudioBuildToolsUrl'
        Hash = '-ExpectedSha256 $VisualStudioBuildToolsSha256'
        Hosts = '-AllowedHosts $VisualStudioBuildToolsAllowedHosts'
        Publisher = '-ExpectedPublisher $MicrosoftPublisher'
        Sink = 'Invoke-Native -FilePath $installerPath -Arguments @('
    },
    @{
        Uri = '-Uri $NetFx472DeveloperPackUrl'
        Hash = '-ExpectedSha256 $NetFx472DeveloperPackSha256'
        Hosts = '-AllowedHosts $NetFx472DeveloperPackAllowedHosts'
        Publisher = '-ExpectedPublisher $MicrosoftPublisher'
        Sink = 'Invoke-Native -FilePath $installerPath -Arguments @('
    }
)
foreach ($mapping in $bootstrapDownloadToSinkContracts) {
    $uriIndex = $bootstrapText.IndexOf($mapping.Uri)
    $hashIndex = if ($uriIndex -ge 0) {
        $bootstrapText.IndexOf($mapping.Hash, $uriIndex)
    }
    else {
        -1
    }
    $hostsIndex = if ($hashIndex -ge 0) {
        $bootstrapText.IndexOf($mapping.Hosts, $hashIndex)
    }
    else {
        -1
    }
    $publisherIndex = if ([string]::IsNullOrWhiteSpace($mapping.Publisher)) {
        $hostsIndex
    }
    elseif ($hostsIndex -ge 0) {
        $bootstrapText.IndexOf($mapping.Publisher, $hostsIndex)
    }
    else {
        -1
    }
    $sinkIndex = if ($publisherIndex -ge 0) {
        $bootstrapText.IndexOf($mapping.Sink, $publisherIndex)
    }
    else {
        -1
    }

    if ($uriIndex -lt 0 -or
        $hashIndex -le $uriIndex -or
        $hostsIndex -le $hashIndex -or
        $publisherIndex -lt $hostsIndex -or
        $sinkIndex -le $publisherIndex) {
        throw "The Windows bootstrapper must bind '$($mapping.Uri)' to its independent verification before execution."
    }
}

$bootstrapFinalHostCheckIndex = $bootstrapText.IndexOf(
    '$normalizedAllowedHosts -notcontains $finalHost')
$bootstrapHashCheckIndex = $bootstrapText.IndexOf(
    'Get-FileHash -LiteralPath $DestinationPath -Algorithm SHA256')
if ($bootstrapFinalHostCheckIndex -lt 0 -or
    $bootstrapHashCheckIndex -le $bootstrapFinalHostCheckIndex) {
    throw "The Windows bootstrapper must validate the final HTTPS redirect host before trusting the digest."
}

$forbiddenBootstrapDownloadContracts = @(
    'api.github.com/repos/git-for-windows/git/releases/latest',
    'git-for-windows/git/releases/latest',
    'aka.ms/vs/17/release',
    'go.microsoft.com/fwlink/?linkid=874338',
    '-Channel $DotNetChannel',
    '-Quality $DotNetQuality'
)
foreach ($contract in $forbiddenBootstrapDownloadContracts) {
    if ($bootstrapText.Contains($contract)) {
        throw "The Windows bootstrapper contains forbidden moving-download contract '$contract'."
    }
}

$requiredCaddyContracts = @(
    'Validating staged Caddy binary and configuration',
    'Invoke-PublicHealthCheck',
    'Restore-CaddyServiceSnapshot',
    'The previous deployment was restored',
    'function Invoke-VerifiedDownload',
    '-MaximumRedirection 5',
    '$downloadResponse.BaseResponse.ResponseUri',
    '$normalizedAllowedHosts -notcontains $finalHost',
    'Get-FileHash -LiteralPath $DestinationPath -Algorithm SHA256',
    '$CaddyVersion = "2.11.4"',
    '$CaddyArchiveUrl = "https://github.com/caddyserver/caddy/releases/download/v2.11.4/caddy_2.11.4_windows_amd64.zip"',
    '$CaddyArchiveSha256 = "1708333f79e274c7697285afe6d592ab39314e0b131e9ec6bea08ad27df62ebf"',
    '$CaddyArchiveAllowedHosts = @("github.com", "release-assets.githubusercontent.com")'
)
foreach ($contract in $requiredCaddyContracts) {
    if (-not $caddyInstallerText.Contains($contract)) {
        throw "The Caddy installer is missing required deployment contract '$contract'."
    }
}

$caddyVerifiedDownloadCalls = [regex]::Matches(
    $caddyInstallerText,
    '(?m)^\s*Invoke-VerifiedDownload\s+`').Count
if ($caddyVerifiedDownloadCalls -ne 1) {
    throw "The Caddy installer must verify exactly one pinned release archive download."
}

$caddyFinalHostCheckIndex = $caddyInstallerText.IndexOf(
    '$normalizedAllowedHosts -notcontains $finalHost')
$caddyHashCheckIndex = $caddyInstallerText.IndexOf(
    'Get-FileHash -LiteralPath $DestinationPath -Algorithm SHA256')
$caddyArchiveVerificationIndex = $caddyInstallerText.LastIndexOf(
    'Invoke-VerifiedDownload `')
$caddyArchiveExpansionIndex = $caddyInstallerText.IndexOf(
    'Expand-Archive -LiteralPath $archivePath')
if ($caddyFinalHostCheckIndex -lt 0 -or
    $caddyHashCheckIndex -le $caddyFinalHostCheckIndex -or
    $caddyArchiveVerificationIndex -lt 0 -or
    $caddyArchiveExpansionIndex -le $caddyArchiveVerificationIndex) {
    throw "The Caddy installer must validate redirect host and SHA-256 before expanding the archive."
}

if ($caddyInstallerText.Contains(
        'api.github.com/repos/caddyserver/caddy/releases/latest') -or
    $caddyInstallerText.Contains('$asset.browser_download_url')) {
    throw "The Caddy installer must not discover or trust a moving latest-release asset at runtime."
}

if ($RunScriptAnalyzer) {
    $analyzer = Get-Command Invoke-ScriptAnalyzer -ErrorAction SilentlyContinue
    if ($null -eq $analyzer) {
        throw "PSScriptAnalyzer is not installed."
    }

    $analysisFailures = @(
        Invoke-ScriptAnalyzer `
            -Path $PSScriptRoot `
            -Recurse `
            -Severity Error
    )
    if ($analysisFailures.Count -gt 0) {
        $formattedFailures = $analysisFailures | ForEach-Object {
            "$($_.ScriptName):$($_.Line): [$($_.RuleName)] $($_.Message)"
        }
        throw "PSScriptAnalyzer failures:`n$($formattedFailures -join [Environment]::NewLine)"
    }
}

Write-Host "PowerShell checks passed for $($scriptFiles.Count) script(s) in '$repositoryRoot'." -ForegroundColor Green
