<#
.SYNOPSIS
Builds and installs TFlexDrawingService on a Windows server.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File .\scripts\Install-TFlexDrawingService.ps1 -InstallRoot C:\Services\TFlexDrawingService

.EXAMPLE
powershell -ExecutionPolicy Bypass -File .\scripts\Install-TFlexDrawingService.ps1 `
  -SourceRoot C:\Users\Administrator\Desktop\tflex-backend-service `
  -UseExistingSource `
  -InstallRoot C:\Services\TFlexDrawingService
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
    [string]$ApiServiceName = "TFlexDrawingService.Api",
    [string]$WorkerServiceName = "TFlexDrawingService.Worker",
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
    [switch]$SkipServiceInstall,
    [switch]$SkipFirewall,
    [switch]$SkipRunnerBuild,
    [switch]$SkipRunnerHealthCheck
)

$ErrorActionPreference = "Stop"

# Windows PowerShell 5.1 does not load the .NET Framework System.Security
# assembly before resolving the DPAPI types used for recoverable bootstrap
# credentials. Load it explicitly before any ProtectedData call.
Add-Type -AssemblyName System.Security -ErrorAction Stop

# The bootstrapper passes secrets through its child-process environment so they
# never appear in the powershell.exe command line. Read them once, then remove
# them before git, dotnet, or the automation runner can inherit them.
if ([string]::IsNullOrEmpty($ServicePassword)) {
    $ServicePassword = [Environment]::GetEnvironmentVariable("TFLEX_INSTALL_SERVICE_PASSWORD", "Process")
}
if ([string]::IsNullOrEmpty($PreviousServicePassword)) {
    $PreviousServicePassword = [Environment]::GetEnvironmentVariable("TFLEX_INSTALL_PREVIOUS_SERVICE_PASSWORD", "Process")
}
if ([string]::IsNullOrEmpty($AdminPassword)) {
    $AdminPassword = [Environment]::GetEnvironmentVariable("TFLEX_INSTALL_ADMIN_PASSWORD", "Process")
}
if ([string]::IsNullOrEmpty($AdminPasswordHash)) {
    $AdminPasswordHash = [Environment]::GetEnvironmentVariable("TFLEX_INSTALL_ADMIN_PASSWORD_HASH", "Process")
}

foreach ($secretEnvironmentVariable in @(
    "TFLEX_INSTALL_SERVICE_PASSWORD",
    "TFLEX_INSTALL_PREVIOUS_SERVICE_PASSWORD",
    "TFLEX_INSTALL_ADMIN_PASSWORD",
    "TFLEX_INSTALL_ADMIN_PASSWORD_HASH"
)) {
    [Environment]::SetEnvironmentVariable($secretEnvironmentVariable, $null, "Process")
}

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
        # Arguments are deliberately omitted: this helper must remain safe if a
        # future caller accidentally supplies a sensitive argument.
        throw "Native command '$FilePath' failed with exit code $exitCode."
    }

}

function Assert-SourceRoot {
    param(
        [string]$Path,
        [bool]$RequireRunnerProject
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "SourceRoot '$Path' was not found or is not a directory."
    }

    $requiredPaths = @(
        "src\TFlexDrawingService.Api\TFlexDrawingService.Api.csproj",
        "src\TFlexDrawingService.Worker\TFlexDrawingService.Worker.csproj",
        "templates\templates.json"
    )

    if ($RequireRunnerProject) {
        $requiredPaths += "src\TFlexAutomationRunner\TFlexAutomationRunner.csproj"
    }

    $missingPaths = @(
        $requiredPaths | Where-Object {
            -not (Test-Path -LiteralPath (Join-Path $Path $_))
        }
    )

    if ($missingPaths.Count -gt 0) {
        throw "SourceRoot '$Path' is not a valid TFlexDrawingService checkout. Missing: $($missingPaths -join ', ')."
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

function New-RandomPassword {
    $bytes = New-Object byte[] 24
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $rng.GetBytes($bytes)
    }
    finally {
        $rng.Dispose()
    }

    return [Convert]::ToBase64String($bytes).TrimEnd("=").Replace("+", "-").Replace("/", "_")
}

function New-PasswordHash {
    param([string]$Password)

    if ([string]::IsNullOrWhiteSpace($Password)) {
        throw "Password cannot be empty."
    }

    $iterations = 210000
    $salt = New-Object byte[] 16
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $rng.GetBytes($salt)
    }
    finally {
        $rng.Dispose()
    }

    $passwordBytes = [Text.Encoding]::UTF8.GetBytes($Password)
    $derive = [Security.Cryptography.Rfc2898DeriveBytes]::new(
        $passwordBytes,
        $salt,
        $iterations,
        [Security.Cryptography.HashAlgorithmName]::SHA256)
    try {
        $hash = $derive.GetBytes(32)
    }
    finally {
        $derive.Dispose()
    }

    return @(
        "pbkdf2-sha256",
        $iterations,
        [Convert]::ToBase64String($salt),
        [Convert]::ToBase64String($hash)
    ) -join '$'
}

function Get-ExistingAdminPasswordHash {
    param(
        [string]$ConfigPath,
        [string]$UserName
    )

    if (-not (Test-Path -LiteralPath $ConfigPath)) {
        return ""
    }

    try {
        $config = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
        $users = @($config.Security.Users)
        $user = $users |
            Where-Object { $_.UserName -ieq $UserName -and -not [string]::IsNullOrWhiteSpace($_.PasswordHash) } |
            Select-Object -First 1

        if ($null -ne $user) {
            return [string]$user.PasswordHash
        }
    }
    catch {
        Write-Warning "Could not reuse the existing admin password hash because the production configuration could not be read."
    }

    return ""
}

function Protect-LocalMachineSecret {
    param([string]$Value)

    $plainBytes = [Text.Encoding]::UTF8.GetBytes($Value)
    $entropy = [Text.Encoding]::UTF8.GetBytes("TFlexDrawingService/bootstrap-admin/v1")
    $protectedBytes = $null
    try {
        $protectedBytes = [Security.Cryptography.ProtectedData]::Protect(
            $plainBytes,
            $entropy,
            [Security.Cryptography.DataProtectionScope]::LocalMachine)
        return [Convert]::ToBase64String($protectedBytes)
    }
    finally {
        if ($null -ne $plainBytes) {
            [Array]::Clear($plainBytes, 0, $plainBytes.Length)
        }
        if ($null -ne $protectedBytes) {
            [Array]::Clear($protectedBytes, 0, $protectedBytes.Length)
        }
        [Array]::Clear($entropy, 0, $entropy.Length)
    }
}

function Unprotect-LocalMachineSecret {
    param([string]$ProtectedValue)

    $protectedBytes = [Convert]::FromBase64String($ProtectedValue)
    $entropy = [Text.Encoding]::UTF8.GetBytes("TFlexDrawingService/bootstrap-admin/v1")
    $plainBytes = $null
    try {
        $plainBytes = [Security.Cryptography.ProtectedData]::Unprotect(
            $protectedBytes,
            $entropy,
            [Security.Cryptography.DataProtectionScope]::LocalMachine)
        return [Text.Encoding]::UTF8.GetString($plainBytes)
    }
    finally {
        if ($null -ne $protectedBytes) {
            [Array]::Clear($protectedBytes, 0, $protectedBytes.Length)
        }
        if ($null -ne $plainBytes) {
            [Array]::Clear($plainBytes, 0, $plainBytes.Length)
        }
        [Array]::Clear($entropy, 0, $entropy.Length)
    }
}

function Set-BootstrapAdminRecoveryDirectoryAcl {
    param([string]$Path)

    $expectedSids = @("S-1-5-18", "S-1-5-32-544")
    $inheritanceFlags = (
        [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor
        [Security.AccessControl.InheritanceFlags]::ObjectInherit
    )
    $acl = [Security.AccessControl.DirectorySecurity]::new()
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($sidValue in $expectedSids) {
        $identity = [Security.Principal.SecurityIdentifier]::new($sidValue)
        $rule = [Security.AccessControl.FileSystemAccessRule]::new(
            $identity,
            [Security.AccessControl.FileSystemRights]::FullControl,
            $inheritanceFlags,
            [Security.AccessControl.PropagationFlags]::None,
            [Security.AccessControl.AccessControlType]::Allow)
        [void]$acl.AddAccessRule($rule)
    }
    Set-Acl -LiteralPath $Path -AclObject $acl
}

function Set-BootstrapAdminRecoveryFileAcl {
    param([string]$Path)

    $expectedSids = @("S-1-5-18", "S-1-5-32-544")
    $acl = [Security.AccessControl.FileSecurity]::new()
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($sidValue in $expectedSids) {
        $identity = [Security.Principal.SecurityIdentifier]::new($sidValue)
        $rule = [Security.AccessControl.FileSystemAccessRule]::new(
            $identity,
            [Security.AccessControl.FileSystemRights]::FullControl,
            [Security.AccessControl.AccessControlType]::Allow)
        [void]$acl.AddAccessRule($rule)
    }
    Set-Acl -LiteralPath $Path -AclObject $acl

    $readBack = Get-Acl -LiteralPath $Path
    $verifiedSids = @()
    foreach ($entry in @($readBack.Access)) {
        $entrySid = $entry.IdentityReference.Translate(
            [Security.Principal.SecurityIdentifier]).Value
        if ($expectedSids -notcontains $entrySid -or
            $entry.AccessControlType -ne [Security.AccessControl.AccessControlType]::Allow -or
            ($entry.FileSystemRights -band [Security.AccessControl.FileSystemRights]::FullControl) -ne
                [Security.AccessControl.FileSystemRights]::FullControl) {
            throw "Bootstrap admin recovery file ACL could not be restricted to local administrators and SYSTEM."
        }
        $verifiedSids += $entrySid
    }

    foreach ($expectedSid in $expectedSids) {
        if ($verifiedSids -notcontains $expectedSid) {
            throw "Bootstrap admin recovery file ACL is missing a required protected principal."
        }
    }
}

function Write-BootstrapAdminRecovery {
    param(
        [string]$Path,
        [string]$UserName,
        [string]$PasswordHash,
        [string]$GeneratedPassword
    )

    $directory = Split-Path $Path -Parent
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    Set-BootstrapAdminRecoveryDirectoryAcl $directory
    $temporaryPath = "$Path.$([Guid]::NewGuid().ToString('N')).tmp"
    try {
        $document = [ordered]@{
            Version = 1
            UserName = $UserName
            PasswordHash = $PasswordHash
            ProtectedPassword = Protect-LocalMachineSecret $GeneratedPassword
        }
        $json = $document | ConvertTo-Json -Depth 3
        [IO.File]::WriteAllText(
            $temporaryPath,
            $json,
            [Text.UTF8Encoding]::new($false))
        Set-BootstrapAdminRecoveryFileAcl $temporaryPath
        Move-Item -LiteralPath $temporaryPath -Destination $Path -Force
    }
    finally {
        Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
    }
}

function Read-BootstrapAdminRecovery {
    param(
        [string]$Path,
        [string]$UserName
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $null
    }

    try {
        Set-BootstrapAdminRecoveryDirectoryAcl (Split-Path $Path -Parent)
        Set-BootstrapAdminRecoveryFileAcl $Path
        $document = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
        if ([int]$document.Version -ne 1 -or
            [string]::IsNullOrWhiteSpace([string]$document.UserName) -or
            [string]::IsNullOrWhiteSpace([string]$document.PasswordHash) -or
            [string]::IsNullOrWhiteSpace([string]$document.ProtectedPassword)) {
            throw "The recovery document is incomplete."
        }
        $passwordHash = [string]$document.PasswordHash
        if ($passwordHash -notmatch
            '^pbkdf2-sha256\$\d+\$[A-Za-z0-9+/=]+\$[A-Za-z0-9+/=]+$') {
            throw "The recovery password hash format is invalid."
        }
        if ([string]$document.UserName -ine $UserName) {
            throw "The recovery document belongs to a different bootstrap admin."
        }

        $password = Unprotect-LocalMachineSecret ([string]$document.ProtectedPassword)
        if ([string]::IsNullOrWhiteSpace($password)) {
            throw "The recovered password is empty."
        }

        return [pscustomobject]@{
            Hash = $passwordHash
            GeneratedPassword = $password
            Reused = $true
            RecoveryRequired = $false
            Recovered = $true
        }
    }
    catch {
        throw "Bootstrap admin recovery at '$Path' could not be read. Preserve this file and storage\drawings.db; reconcile them from a verified backup before removing recovery state or generating a replacement credential."
    }
}

function Remove-BootstrapAdminRecovery {
    param([string]$Path)

    if (Test-Path -LiteralPath $Path -PathType Leaf) {
        Remove-Item -LiteralPath $Path -Force -ErrorAction Stop
    }

    $directory = Split-Path $Path -Parent
    if (Test-Path -LiteralPath $directory -PathType Container) {
        $children = @(Get-ChildItem -LiteralPath $directory -Force -ErrorAction Stop)
        if ($children.Count -eq 0) {
            Remove-Item -LiteralPath $directory -Force -ErrorAction Stop
        }
    }
}

function Resolve-AdminPasswordHash {
    param(
        [string]$ExistingHash,
        [string]$RecoveryPath,
        [string]$UserName
    )

    $recovered = Read-BootstrapAdminRecovery $RecoveryPath $UserName
    if ($null -ne $recovered) {
        if (-not [string]::IsNullOrWhiteSpace($AdminPasswordHash) -or
            -not [string]::IsNullOrWhiteSpace($AdminPassword)) {
            throw "A recoverable bootstrap admin credential from a failed deployment already exists. Retry without -AdminPassword or -AdminPasswordHash so the database and configuration keep the same credential."
        }
        if (-not [string]::IsNullOrWhiteSpace($ExistingHash) -and
            $ExistingHash -cne $recovered.Hash) {
            throw "The protected bootstrap admin recovery hash does not match the active production configuration. Preserve the recovery file and storage\drawings.db, then reconcile them from a verified backup before retrying."
        }
        return $recovered
    }

    if (-not [string]::IsNullOrWhiteSpace($AdminPasswordHash)) {
        return [pscustomobject]@{
            Hash = $AdminPasswordHash
            GeneratedPassword = ""
            Reused = $false
            RecoveryRequired = $false
            Recovered = $false
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($AdminPassword)) {
        return [pscustomobject]@{
            Hash = New-PasswordHash $AdminPassword
            GeneratedPassword = ""
            Reused = $false
            RecoveryRequired = $false
            Recovered = $false
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($ExistingHash)) {
        return [pscustomobject]@{
            Hash = $ExistingHash
            GeneratedPassword = ""
            Reused = $true
            RecoveryRequired = $false
            Recovered = $false
        }
    }

    $password = New-RandomPassword
    return [pscustomobject]@{
        Hash = New-PasswordHash $password
        GeneratedPassword = $password
        Reused = $false
        RecoveryRequired = $true
        Recovered = $false
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
        Stop-Service -Name $Name -Force -ErrorAction Stop
        $service.WaitForStatus("Stopped", [TimeSpan]::FromSeconds(30))
    }
}

function Invoke-Sc {
    param([string[]]$Arguments)
    $output = @(& sc.exe @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        $details = ($output | ForEach-Object { ([string]$_).Trim() } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join " "
        if ([string]::IsNullOrWhiteSpace($details)) {
            $details = "No diagnostic output was returned."
        }
        throw "sc.exe failed with exit code $LASTEXITCODE`: $details"
    }
}

function Resolve-ServiceAccountSid {
    param([string]$AccountName)

    if ([string]::IsNullOrWhiteSpace($AccountName)) {
        return ""
    }

    switch ($AccountName.Trim().ToLowerInvariant()) {
        "localsystem" { return "S-1-5-18" }
        "nt authority\system" { return "S-1-5-18" }
        "localservice" { return "S-1-5-19" }
        "nt authority\local service" { return "S-1-5-19" }
        "networkservice" { return "S-1-5-20" }
        "nt authority\network service" { return "S-1-5-20" }
    }

    $normalizedName = $AccountName
    if ($normalizedName.StartsWith(".\")) {
        $normalizedName = "$env:COMPUTERNAME\$($normalizedName.Substring(2))"
    }

    try {
        $account = [Security.Principal.NTAccount]::new($normalizedName)
        return $account.Translate([Security.Principal.SecurityIdentifier]).Value
    }
    catch {
        throw "Service account '$AccountName' could not be resolved. Create the account or correct -ServiceUser before installing the services."
    }
}

function Get-ServiceAccountIdentity {
    param([string]$ServiceName)

    $escapedName = $ServiceName.Replace("'", "''")
    $service = Get-CimInstance -ClassName Win32_Service -Filter "Name='$escapedName'"
    if ($null -eq $service -or [string]::IsNullOrWhiteSpace([string]$service.StartName)) {
        throw "Windows service '$ServiceName' account could not be queried."
    }

    $startName = [string]$service.StartName
    return [pscustomobject]@{
        ServiceName = $ServiceName
        StartName = $startName
        Sid = Resolve-ServiceAccountSid $startName
    }
}

function Grant-LogOnAsServiceRight {
    param(
        [string]$AccountName,
        [string]$Sid,
        [Parameter(Mandatory = $true)][ref]$GrantedByDeployment
    )

    $GrantedByDeployment.Value = $false
    if ([string]::IsNullOrWhiteSpace($Sid)) {
        return
    }

    if (-not ("TFlexDrawingService.Install.AccountRights" -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace TFlexDrawingService.Install
{
    public static class AccountRights
    {
        private const uint PolicyLookupNames = 0x00000800;
        private const uint PolicyCreateAccount = 0x00000010;
        private const uint StatusObjectNameNotFound = 0xC0000034;

        [StructLayout(LayoutKind.Sequential)]
        private struct LsaObjectAttributes
        {
            public uint Length;
            public IntPtr RootDirectory;
            public IntPtr ObjectName;
            public uint Attributes;
            public IntPtr SecurityDescriptor;
            public IntPtr SecurityQualityOfService;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LsaUnicodeString
        {
            public ushort Length;
            public ushort MaximumLength;
            public IntPtr Buffer;
        }

        [DllImport("advapi32.dll")]
        private static extern uint LsaOpenPolicy(
            IntPtr systemName,
            ref LsaObjectAttributes objectAttributes,
            uint desiredAccess,
            out IntPtr policyHandle);

        [DllImport("advapi32.dll")]
        private static extern uint LsaAddAccountRights(
            IntPtr policyHandle,
            IntPtr accountSid,
            LsaUnicodeString[] userRights,
            uint countOfRights);

        [DllImport("advapi32.dll")]
        private static extern uint LsaEnumerateAccountRights(
            IntPtr policyHandle,
            IntPtr accountSid,
            out IntPtr userRights,
            out uint countOfRights);

        [DllImport("advapi32.dll")]
        private static extern uint LsaRemoveAccountRights(
            IntPtr policyHandle,
            IntPtr accountSid,
            bool allRights,
            LsaUnicodeString[] userRights,
            uint countOfRights);

        [DllImport("advapi32.dll")]
        private static extern uint LsaNtStatusToWinError(uint status);

        [DllImport("advapi32.dll")]
        private static extern uint LsaFreeMemory(IntPtr buffer);

        [DllImport("advapi32.dll")]
        private static extern uint LsaClose(IntPtr policyHandle);

        public static void Grant(string sidValue, string rightName)
        {
            var objectAttributes = new LsaObjectAttributes();
            objectAttributes.Length = (uint)Marshal.SizeOf(typeof(LsaObjectAttributes));

            IntPtr policyHandle;
            uint status = LsaOpenPolicy(
                IntPtr.Zero,
                ref objectAttributes,
                PolicyLookupNames | PolicyCreateAccount,
                out policyHandle);
            ThrowIfFailed(status, "LsaOpenPolicy");

            IntPtr sidPointer = IntPtr.Zero;
            IntPtr rightPointer = IntPtr.Zero;
            try
            {
                var sid = new SecurityIdentifier(sidValue);
                var sidBytes = new byte[sid.BinaryLength];
                sid.GetBinaryForm(sidBytes, 0);
                sidPointer = Marshal.AllocHGlobal(sidBytes.Length);
                Marshal.Copy(sidBytes, 0, sidPointer, sidBytes.Length);

                rightPointer = Marshal.StringToHGlobalUni(rightName);
                var right = new LsaUnicodeString
                {
                    Buffer = rightPointer,
                    Length = checked((ushort)(rightName.Length * 2)),
                    MaximumLength = checked((ushort)((rightName.Length + 1) * 2))
                };

                status = LsaAddAccountRights(policyHandle, sidPointer, new[] { right }, 1);
                ThrowIfFailed(status, "LsaAddAccountRights");
            }
            finally
            {
                if (rightPointer != IntPtr.Zero) Marshal.FreeHGlobal(rightPointer);
                if (sidPointer != IntPtr.Zero) Marshal.FreeHGlobal(sidPointer);
                LsaClose(policyHandle);
            }
        }

        public static bool Has(string sidValue, string rightName)
        {
            var objectAttributes = new LsaObjectAttributes();
            objectAttributes.Length = (uint)Marshal.SizeOf(typeof(LsaObjectAttributes));

            IntPtr policyHandle;
            uint status = LsaOpenPolicy(
                IntPtr.Zero,
                ref objectAttributes,
                PolicyLookupNames,
                out policyHandle);
            ThrowIfFailed(status, "LsaOpenPolicy");

            IntPtr sidPointer = IntPtr.Zero;
            IntPtr rightsPointer = IntPtr.Zero;
            try
            {
                var sid = new SecurityIdentifier(sidValue);
                var sidBytes = new byte[sid.BinaryLength];
                sid.GetBinaryForm(sidBytes, 0);
                sidPointer = Marshal.AllocHGlobal(sidBytes.Length);
                Marshal.Copy(sidBytes, 0, sidPointer, sidBytes.Length);

                uint countOfRights;
                status = LsaEnumerateAccountRights(
                    policyHandle,
                    sidPointer,
                    out rightsPointer,
                    out countOfRights);
                if (status == StatusObjectNameNotFound)
                {
                    return false;
                }
                ThrowIfFailed(status, "LsaEnumerateAccountRights");

                int rightSize = Marshal.SizeOf(typeof(LsaUnicodeString));
                int count = checked((int)countOfRights);
                for (int index = 0; index < count; index++)
                {
                    IntPtr itemPointer = IntPtr.Add(rightsPointer, checked(index * rightSize));
                    var right = (LsaUnicodeString)Marshal.PtrToStructure(
                        itemPointer,
                        typeof(LsaUnicodeString));
                    string name = Marshal.PtrToStringUni(right.Buffer, right.Length / 2);
                    if (string.Equals(name, rightName, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                return false;
            }
            finally
            {
                if (rightsPointer != IntPtr.Zero) LsaFreeMemory(rightsPointer);
                if (sidPointer != IntPtr.Zero) Marshal.FreeHGlobal(sidPointer);
                LsaClose(policyHandle);
            }
        }

        public static void Remove(string sidValue, string rightName)
        {
            var objectAttributes = new LsaObjectAttributes();
            objectAttributes.Length = (uint)Marshal.SizeOf(typeof(LsaObjectAttributes));

            IntPtr policyHandle;
            uint status = LsaOpenPolicy(
                IntPtr.Zero,
                ref objectAttributes,
                PolicyLookupNames,
                out policyHandle);
            ThrowIfFailed(status, "LsaOpenPolicy");

            IntPtr sidPointer = IntPtr.Zero;
            IntPtr rightPointer = IntPtr.Zero;
            try
            {
                var sid = new SecurityIdentifier(sidValue);
                var sidBytes = new byte[sid.BinaryLength];
                sid.GetBinaryForm(sidBytes, 0);
                sidPointer = Marshal.AllocHGlobal(sidBytes.Length);
                Marshal.Copy(sidBytes, 0, sidPointer, sidBytes.Length);

                rightPointer = Marshal.StringToHGlobalUni(rightName);
                var right = new LsaUnicodeString
                {
                    Buffer = rightPointer,
                    Length = checked((ushort)(rightName.Length * 2)),
                    MaximumLength = checked((ushort)((rightName.Length + 1) * 2))
                };

                status = LsaRemoveAccountRights(
                    policyHandle,
                    sidPointer,
                    false,
                    new[] { right },
                    1);
                if (status != StatusObjectNameNotFound)
                {
                    ThrowIfFailed(status, "LsaRemoveAccountRights");
                }
            }
            finally
            {
                if (rightPointer != IntPtr.Zero) Marshal.FreeHGlobal(rightPointer);
                if (sidPointer != IntPtr.Zero) Marshal.FreeHGlobal(sidPointer);
                LsaClose(policyHandle);
            }
        }

        private static void ThrowIfFailed(uint status, string operation)
        {
            if (status == 0) return;
            int error = unchecked((int)LsaNtStatusToWinError(status));
            throw new Win32Exception(error, operation + " failed");
        }
    }
}
'@
    }

    try {
        if ([TFlexDrawingService.Install.AccountRights]::Has($Sid, "SeServiceLogonRight")) {
            return
        }

        [TFlexDrawingService.Install.AccountRights]::Grant($Sid, "SeServiceLogonRight")
        $GrantedByDeployment.Value = $true
        if (-not [TFlexDrawingService.Install.AccountRights]::Has($Sid, "SeServiceLogonRight")) {
            throw "The account right could not be verified after it was granted."
        }
    }
    catch {
        throw "Could not grant 'Log on as a service' to '$AccountName'. Check local or domain security policy."
    }
}

function Test-ServiceAccountSidInUse {
    param(
        [string]$Sid,
        [string]$AccountName
    )

    foreach ($service in @(Get-CimInstance -ClassName Win32_Service -ErrorAction Stop)) {
        $startName = [string]$service.StartName
        if ([string]::IsNullOrWhiteSpace($startName)) {
            continue
        }
        if (-not [string]::IsNullOrWhiteSpace($AccountName) -and
            $startName -ieq $AccountName) {
            return $true
        }

        try {
            if ((Resolve-ServiceAccountSid $startName) -eq $Sid) {
                return $true
            }
        }
        catch {
            # An unrelated stale service account must not prevent rollback of the
            # installer-owned right for a different, resolvable SID.
            continue
        }
    }

    return $false
}

function Remove-LogOnAsServiceRightIfUnused {
    param(
        [string]$AccountName,
        [string]$Sid
    )

    if (Test-ServiceAccountSidInUse -Sid $Sid -AccountName $AccountName) {
        Write-Warning "Preserved 'Log on as a service' for '$AccountName' because a Windows service still uses that SID."
        return $false
    }

    try {
        [TFlexDrawingService.Install.AccountRights]::Remove($Sid, "SeServiceLogonRight")
        if ([TFlexDrawingService.Install.AccountRights]::Has($Sid, "SeServiceLogonRight")) {
            throw "The account right is still assigned."
        }
        return $true
    }
    catch {
        throw "Could not roll back 'Log on as a service' for '$AccountName'. Check local or domain security policy."
    }
}

function Grant-DirectoryAccess {
    param(
        [string]$Path,
        [string]$Sid,
        [Security.AccessControl.FileSystemRights]$Rights,
        [Security.AccessControl.InheritanceFlags]$InheritanceFlags
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "Directory '$Path' was not found while configuring service account access."
    }

    $identity = [Security.Principal.SecurityIdentifier]::new($Sid)
    $acl = Get-Acl -LiteralPath $Path
    $rule = [Security.AccessControl.FileSystemAccessRule]::new(
        $identity,
        $Rights,
        $InheritanceFlags,
        [Security.AccessControl.PropagationFlags]::None,
        [Security.AccessControl.AccessControlType]::Allow)
    [void]$acl.AddAccessRule($rule)
    Set-Acl -LiteralPath $Path -AclObject $acl

    $readBack = Get-Acl -LiteralPath $Path
    $hasExpectedRule = $false
    foreach ($entry in @($readBack.Access)) {
        $entrySid = ""
        try {
            $entrySid = $entry.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value
        }
        catch {
            continue
        }

        if ($entrySid -eq $Sid -and
            $entry.AccessControlType -eq [Security.AccessControl.AccessControlType]::Allow -and
            ($entry.FileSystemRights -band $Rights) -eq $Rights -and
            ($entry.InheritanceFlags -band $InheritanceFlags) -eq $InheritanceFlags) {
            $hasExpectedRule = $true
            break
        }
    }

    if (-not $hasExpectedRule) {
        throw "Access rights for the service account could not be verified on '$Path'."
    }
}

function Get-DirectoryAclSnapshots {
    param([string[]]$Paths)

    $seenPaths = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    $snapshots = @()
    foreach ($path in @($Paths)) {
        if ([string]::IsNullOrWhiteSpace($path) -or
            -not (Test-Path -LiteralPath $path -PathType Container)) {
            continue
        }

        $fullPath = Resolve-FullPath $path
        if (-not $seenPaths.Add($fullPath)) {
            continue
        }

        $acl = Get-Acl -LiteralPath $fullPath
        $snapshots += [pscustomobject]@{
            Path = $fullPath
            AccessSddl = $acl.GetSecurityDescriptorSddlForm(
                [Security.AccessControl.AccessControlSections]::Access)
        }
    }

    return @($snapshots)
}

function Restore-DirectoryAclSnapshots {
    param([object[]]$Snapshots)

    foreach ($snapshot in @($Snapshots)) {
        if (-not (Test-Path -LiteralPath $snapshot.Path -PathType Container)) {
            throw "Directory '$($snapshot.Path)' disappeared before its ACL could be restored."
        }

        $acl = Get-Acl -LiteralPath $snapshot.Path
        $acl.SetSecurityDescriptorSddlForm(
            $snapshot.AccessSddl,
            [Security.AccessControl.AccessControlSections]::Access)
        Set-Acl -LiteralPath $snapshot.Path -AclObject $acl

        $readBack = Get-Acl -LiteralPath $snapshot.Path
        $restoredSddl = $readBack.GetSecurityDescriptorSddlForm(
            [Security.AccessControl.AccessControlSections]::Access)
        if ($restoredSddl -cne $snapshot.AccessSddl) {
            throw "Directory ACL could not be restored exactly on '$($snapshot.Path)'."
        }
    }
}

function Remove-ExplicitDirectoryAccessForSid {
    param(
        [string]$Path,
        [string]$Sid,
        [Security.AccessControl.FileSystemRights]$Rights,
        [Security.AccessControl.InheritanceFlags]$InheritanceFlags
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        return
    }

    $identity = [Security.Principal.SecurityIdentifier]::new($Sid)
    $expectedRule = [Security.AccessControl.FileSystemAccessRule]::new(
        $identity,
        $Rights,
        $InheritanceFlags,
        [Security.AccessControl.PropagationFlags]::None,
        [Security.AccessControl.AccessControlType]::Allow)
    $acl = Get-Acl -LiteralPath $Path
    $rulesToRemove = @()
    foreach ($entry in @($acl.Access)) {
        if ($entry.IsInherited -or
            $entry.AccessControlType -ne [Security.AccessControl.AccessControlType]::Allow -or
            $entry.FileSystemRights -ne $expectedRule.FileSystemRights -or
            $entry.InheritanceFlags -ne $expectedRule.InheritanceFlags -or
            $entry.PropagationFlags -ne $expectedRule.PropagationFlags) {
            continue
        }

        $entrySid = ""
        try {
            $entrySid = $entry.IdentityReference.Translate(
                [Security.Principal.SecurityIdentifier]).Value
        }
        catch {
            continue
        }

        if ($entrySid -eq $Sid) {
            $rulesToRemove += $entry
        }
    }

    if ($rulesToRemove.Count -eq 0) {
        return
    }

    foreach ($rule in $rulesToRemove) {
        $acl.RemoveAccessRuleSpecific($rule)
    }
    Set-Acl -LiteralPath $Path -AclObject $acl

    $readBack = Get-Acl -LiteralPath $Path
    foreach ($entry in @($readBack.Access)) {
        if ($entry.IsInherited -or
            $entry.AccessControlType -ne [Security.AccessControl.AccessControlType]::Allow -or
            $entry.FileSystemRights -ne $expectedRule.FileSystemRights -or
            $entry.InheritanceFlags -ne $expectedRule.InheritanceFlags -or
            $entry.PropagationFlags -ne $expectedRule.PropagationFlags) {
            continue
        }

        $entrySid = ""
        try {
            $entrySid = $entry.IdentityReference.Translate(
                [Security.Principal.SecurityIdentifier]).Value
        }
        catch {
            continue
        }

        if ($entrySid -eq $Sid) {
            throw "Installer-managed access for obsolete service SID '$Sid' could not be removed from '$Path'."
        }
    }
}

function Grant-ServiceFileSystemAccess {
    param(
        [string]$Sid,
        [string[]]$ReadExecuteDirectories,
        [string[]]$ModifyDirectories = @()
    )

    $containerAndObjectInheritance = (
        [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor
        [Security.AccessControl.InheritanceFlags]::ObjectInherit
    )

    # Allow traversal of the root itself without exposing the source checkout or
    # other future directories through an inheritable rule.
    Grant-DirectoryAccess `
        -Path $InstallRoot `
        -Sid $Sid `
        -Rights ([Security.AccessControl.FileSystemRights]::ReadAndExecute) `
        -InheritanceFlags ([Security.AccessControl.InheritanceFlags]::None)

    Grant-DirectoryAccess `
        -Path $storageDir `
        -Sid $Sid `
        -Rights ([Security.AccessControl.FileSystemRights]::Modify) `
        -InheritanceFlags $containerAndObjectInheritance

    foreach ($directory in @($ReadExecuteDirectories | Select-Object -Unique)) {
        Grant-DirectoryAccess `
            -Path $directory `
            -Sid $Sid `
            -Rights ([Security.AccessControl.FileSystemRights]::ReadAndExecute) `
            -InheritanceFlags $containerAndObjectInheritance
    }

    foreach ($directory in @($ModifyDirectories | Select-Object -Unique)) {
        Grant-DirectoryAccess `
            -Path $directory `
            -Sid $Sid `
            -Rights ([Security.AccessControl.FileSystemRights]::Modify) `
            -InheritanceFlags $containerAndObjectInheritance
    }
}

function Remove-ServiceFileSystemAccess {
    param(
        [string]$Sid,
        [string[]]$ReadExecuteDirectories,
        [string[]]$ModifyDirectories = @()
    )

    $containerAndObjectInheritance = (
        [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor
        [Security.AccessControl.InheritanceFlags]::ObjectInherit
    )

    # Remove only the exact explicit Allow rule shapes installed by
    # Grant-ServiceFileSystemAccess. Broader, inherited, or Deny ACEs may have
    # been configured independently and must remain untouched.
    Remove-ExplicitDirectoryAccessForSid `
        -Path $InstallRoot `
        -Sid $Sid `
        -Rights ([Security.AccessControl.FileSystemRights]::ReadAndExecute) `
        -InheritanceFlags ([Security.AccessControl.InheritanceFlags]::None)

    Remove-ExplicitDirectoryAccessForSid `
        -Path $storageDir `
        -Sid $Sid `
        -Rights ([Security.AccessControl.FileSystemRights]::Modify) `
        -InheritanceFlags $containerAndObjectInheritance

    foreach ($directory in @($ReadExecuteDirectories | Select-Object -Unique)) {
        Remove-ExplicitDirectoryAccessForSid `
            -Path $directory `
            -Sid $Sid `
            -Rights ([Security.AccessControl.FileSystemRights]::ReadAndExecute) `
            -InheritanceFlags $containerAndObjectInheritance
    }

    foreach ($directory in @($ModifyDirectories | Select-Object -Unique)) {
        Remove-ExplicitDirectoryAccessForSid `
            -Path $directory `
            -Sid $Sid `
            -Rights ([Security.AccessControl.FileSystemRights]::Modify) `
            -InheritanceFlags $containerAndObjectInheritance
    }
}

function Set-ServiceAccount {
    param(
        [string]$Name,
        [string]$User,
        [string]$Password,
        [string]$ExpectedSid
    )

    if ([string]::IsNullOrWhiteSpace($User)) {
        return
    }

    $escapedName = $Name.Replace("'", "''")
    $service = Get-CimInstance -ClassName Win32_Service -Filter "Name='$escapedName'"
    if ($null -eq $service) {
        throw "Windows service '$Name' could not be queried after creation."
    }

    $changeArguments = @{ StartName = $User }
    if (-not [string]::IsNullOrEmpty($Password)) {
        $changeArguments.StartPassword = $Password
    }

    $result = Invoke-CimMethod -InputObject $service -MethodName Change -Arguments $changeArguments
    if ($null -eq $result -or $result.ReturnValue -ne 0) {
        $returnValue = if ($null -eq $result) { "unknown" } else { $result.ReturnValue }
        throw "Windows service '$Name' rejected the configured service account (Win32_Service.Change result: $returnValue)."
    }

    $configuredService = Get-CimInstance -ClassName Win32_Service -Filter "Name='$escapedName'"
    $configuredSid = if ($null -eq $configuredService) {
        ""
    }
    else {
        Resolve-ServiceAccountSid ([string]$configuredService.StartName)
    }
    if ($configuredSid -ne $ExpectedSid) {
        throw "Windows service '$Name' did not retain the requested service account."
    }
}

function Set-ServiceEnvironmentVariable {
    param(
        [string]$ServiceName,
        [string]$VariableName,
        [string]$Value
    )

    $serviceKey = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
    if (-not (Test-Path -LiteralPath $serviceKey)) {
        throw "Registry configuration for Windows service '$ServiceName' was not found."
    }

    # A newly created Windows service has no Environment registry value yet.
    # Read the key itself so a clean installation produces an empty collection
    # instead of dereferencing the null result of Get-ItemProperty -Name.
    $serviceProperties = Get-ItemProperty -LiteralPath $serviceKey -ErrorAction Stop
    $existing = if ($null -eq $serviceProperties.Environment) {
        @()
    }
    else {
        @($serviceProperties.Environment)
    }
    $prefix = "$VariableName="
    $updated = @($existing | Where-Object { -not $_.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) })
    $updated += "$VariableName=$Value"

    New-ItemProperty `
        -LiteralPath $serviceKey `
        -Name Environment `
        -PropertyType MultiString `
        -Value $updated `
        -Force | Out-Null

    $readBack = @((Get-ItemProperty -LiteralPath $serviceKey -Name Environment).Environment)
    if ($readBack -notcontains "$VariableName=$Value") {
        throw "Environment variable '$VariableName' could not be configured for Windows service '$ServiceName'."
    }
}

function Get-ServiceRegistryValueSnapshot {
    param(
        [string]$ServiceName,
        [string]$ValueName
    )

    $serviceKeyPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
    $serviceKey = Get-Item -LiteralPath $serviceKeyPath -ErrorAction Stop
    $exists = @($serviceKey.GetValueNames()) -contains $ValueName
    if (-not $exists) {
        return [pscustomobject]@{
            Exists = $false
            Kind = $null
            Value = $null
        }
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

function Get-ServiceEnvironmentSnapshotValue {
    param(
        [object]$ServiceSnapshot,
        [string]$VariableName
    )

    if (-not $ServiceSnapshot.Exists -or
        -not $ServiceSnapshot.Environment.Exists) {
        return ""
    }

    $prefix = "$VariableName="
    foreach ($entry in @($ServiceSnapshot.Environment.Value)) {
        $text = [string]$entry
        if ($text.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
            return $text.Substring($prefix.Length)
        }
    }

    return ""
}

function Restore-ServiceRegistryValue {
    param(
        [string]$ServiceName,
        [string]$ValueName,
        [object]$Snapshot
    )

    $serviceKeyPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
    if ($Snapshot.Exists) {
        New-ItemProperty `
            -LiteralPath $serviceKeyPath `
            -Name $ValueName `
            -PropertyType $Snapshot.Kind `
            -Value $Snapshot.Value `
            -Force | Out-Null
    }
    else {
        Remove-ItemProperty `
            -LiteralPath $serviceKeyPath `
            -Name $ValueName `
            -ErrorAction SilentlyContinue
    }
}

function Get-ServiceConfigurationSnapshot {
    param([string]$Name)

    if (-not (Test-ServiceExists $Name)) {
        return [pscustomobject]@{
            Exists = $false
            Name = $Name
        }
    }

    $escapedName = $Name.Replace("'", "''")
    $service = Get-CimInstance -ClassName Win32_Service -Filter "Name='$escapedName'"
    if ($null -eq $service) {
        throw "Windows service '$Name' could not be queried before deployment."
    }

    return [pscustomobject]@{
        Exists = $true
        Name = $Name
        PathName = [string]$service.PathName
        DisplayName = [string]$service.DisplayName
        StartMode = [string]$service.StartMode
        StartName = [string]$service.StartName
        WasRunning = ([string]$service.State -eq "Running")
        Environment = Get-ServiceRegistryValueSnapshot $Name "Environment"
        FailureActions = Get-ServiceRegistryValueSnapshot $Name "FailureActions"
        FailureActionsOnNonCrashFailures = Get-ServiceRegistryValueSnapshot $Name "FailureActionsOnNonCrashFailures"
        DelayedAutoStart = Get-ServiceRegistryValueSnapshot $Name "DelayedAutoStart"
    }
}

function Test-PasswordlessServiceAccountSid {
    param([string]$Sid)
    return @("S-1-5-18", "S-1-5-19", "S-1-5-20") -contains $Sid
}

function Assert-ServiceAccountRollbackPossible {
    param(
        [object]$Snapshot,
        [string]$RequestedAccount,
        [string]$RequestedSid
    )

    if (-not $Snapshot.Exists -or [string]::IsNullOrWhiteSpace($RequestedAccount)) {
        return
    }

    $previousSid = Resolve-ServiceAccountSid $Snapshot.StartName
    if ($previousSid -eq $RequestedSid) {
        return
    }

    $isPasswordless = Test-PasswordlessServiceAccountSid $previousSid
    if (-not $isPasswordless -and
        -not $Snapshot.StartName.EndsWith('$') -and
        [string]::IsNullOrEmpty($PreviousServicePassword)) {
        throw "Changing service '$($Snapshot.Name)' from '$($Snapshot.StartName)' to '$RequestedAccount' requires -PreviousServicePassword so rollback can restore the previous account."
    }
}

function Restore-ServiceConfiguration {
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

    $previousSid = Resolve-ServiceAccountSid $Snapshot.StartName
    $escapedName = $Snapshot.Name.Replace("'", "''")
    $currentService = Get-CimInstance -ClassName Win32_Service -Filter "Name='$escapedName'"
    $currentSid = Resolve-ServiceAccountSid ([string]$currentService.StartName)
    if ($currentSid -ne $previousSid) {
        $rollbackPassword = if ((Test-PasswordlessServiceAccountSid $previousSid) -or $Snapshot.StartName.EndsWith('$')) {
            ""
        }
        else {
            $PreviousServicePassword
        }

        Set-ServiceAccount `
            -Name $Snapshot.Name `
            -User $Snapshot.StartName `
            -Password $rollbackPassword `
            -ExpectedSid $previousSid
    }

    Restore-ServiceRegistryValue $Snapshot.Name "Environment" $Snapshot.Environment
    Restore-ServiceRegistryValue $Snapshot.Name "FailureActions" $Snapshot.FailureActions
    Restore-ServiceRegistryValue `
        $Snapshot.Name `
        "FailureActionsOnNonCrashFailures" `
        $Snapshot.FailureActionsOnNonCrashFailures
    Restore-ServiceRegistryValue $Snapshot.Name "DelayedAutoStart" $Snapshot.DelayedAutoStart
}

function Test-ServiceEnvironmentValue {
    param(
        [string]$ServiceName,
        [string]$VariableName,
        [string]$ExpectedValue
    )

    $serviceKey = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
    $environment = @((Get-ItemProperty -LiteralPath $serviceKey -Name Environment -ErrorAction Stop).Environment)
    return $environment -contains "$VariableName=$ExpectedValue"
}

function Install-OrUpdateService {
    param(
        [string]$Name,
        [string]$DisplayName,
        [string]$BinaryPath
    )

    if (Test-ServiceExists $Name) {
        Invoke-Sc @("config", $Name, "binPath=", $BinaryPath, "DisplayName=", $DisplayName, "start=", "auto")
    }
    else {
        Invoke-Sc @("create", $Name, "binPath=", $BinaryPath, "DisplayName=", $DisplayName, "start=", "auto")
    }

    Invoke-Sc @("failure", $Name, "reset=", "86400", "actions=", "restart/60000/restart/60000/restart/60000")

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

function Merge-RuntimeTemplatesIntoStage {
    param(
        [string]$LiveTemplatesDirectory,
        [string]$StagedTemplatesDirectory
    )

    $liveCatalogPath = Join-Path $LiveTemplatesDirectory "templates.json"
    if (-not (Test-Path -LiteralPath $liveCatalogPath -PathType Leaf)) {
        return
    }

    $stagedCatalogPath = Join-Path $StagedTemplatesDirectory "templates.json"
    $liveCatalog = Get-Content -LiteralPath $liveCatalogPath -Raw | ConvertFrom-Json
    $stagedCatalog = Get-Content -LiteralPath $stagedCatalogPath -Raw | ConvertFrom-Json
    $runtimeTemplates = @($liveCatalog.templates | Where-Object {
        $templatePath = ([string]$_.templateFilePath).Replace("\", "/")
        $templatePath.StartsWith("templates/imported/", [StringComparison]::OrdinalIgnoreCase)
    })

    $liveImportedDirectory = Join-Path $LiveTemplatesDirectory "imported"
    $stagedImportedDirectory = Join-Path $StagedTemplatesDirectory "imported"
    if (Test-Path -LiteralPath $stagedImportedDirectory) {
        Remove-Item -LiteralPath $stagedImportedDirectory -Recurse -Force
    }
    if (Test-Path -LiteralPath $liveImportedDirectory -PathType Container) {
        Copy-Item `
            -LiteralPath $liveImportedDirectory `
            -Destination $stagedImportedDirectory `
            -Recurse `
            -Force
    }

    $mergedTemplates = @($stagedCatalog.templates)
    foreach ($runtimeTemplate in $runtimeTemplates) {
        $runtimeId = [string]$runtimeTemplate.id
        $runtimeCode = [string]$runtimeTemplate.code
        $collision = $mergedTemplates | Where-Object {
            ([string]$_.id -ieq $runtimeId) -or ([string]$_.code -ieq $runtimeCode)
        } | Select-Object -First 1
        if ($null -ne $collision) {
            throw "Runtime template '$runtimeId' conflicts with source template '$([string]$collision.id)'. Resolve the id/code collision before upgrading."
        }

        $runtimeRelativePath = ([string]$runtimeTemplate.templateFilePath).Replace("/", "\")
        $runtimeFilePath = Resolve-FullPath (Join-Path $InstallRoot $runtimeRelativePath)
        $normalizedImportedRoot = (Resolve-FullPath $liveImportedDirectory).TrimEnd("\") + "\"
        if (-not $runtimeFilePath.StartsWith($normalizedImportedRoot, [StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-Path -LiteralPath $runtimeFilePath -PathType Leaf)) {
            throw "Runtime template '$runtimeId' references a missing or unsafe imported file."
        }

        $mergedTemplates += $runtimeTemplate
    }

    $stagedCatalog.templates = $mergedTemplates
    $temporaryCatalogPath = "$stagedCatalogPath.merge-$([Guid]::NewGuid().ToString('N')).tmp"
    try {
        $stagedCatalog |
            ConvertTo-Json -Depth 100 |
            Set-Content -LiteralPath $temporaryCatalogPath -Encoding UTF8
        Move-Item -LiteralPath $temporaryCatalogPath -Destination $stagedCatalogPath -Force
    }
    finally {
        Remove-Item -LiteralPath $temporaryCatalogPath -Force -ErrorAction SilentlyContinue
    }
}

function Assert-StagedDeployment {
    param(
        [string]$ApiDirectory,
        [string]$WorkerDirectory,
        [string]$RunnerDirectory,
        [string]$TemplatesDirectory,
        [bool]$RequireRunner
    )

    $requiredFiles = @(
        (Join-Path $ApiDirectory "TFlexDrawingService.Api.exe"),
        (Join-Path $ApiDirectory "appsettings.Production.json"),
        (Join-Path $WorkerDirectory "TFlexDrawingService.Worker.exe"),
        (Join-Path $WorkerDirectory "appsettings.Production.json"),
        (Join-Path $TemplatesDirectory "templates.json")
    )

    if ($RequireRunner) {
        $requiredFiles += Join-Path $RunnerDirectory "TFlexAutomationRunner.exe"
    }

    $missingFiles = @($requiredFiles | Where-Object { -not (Test-Path -LiteralPath $_ -PathType Leaf) })
    if ($missingFiles.Count -gt 0) {
        throw "Staged deployment is incomplete. Missing $($missingFiles.Count) required file(s)."
    }

    $developmentFiles = @(
        (Join-Path $ApiDirectory "appsettings.Development.json"),
        (Join-Path $WorkerDirectory "appsettings.Development.json")
    )
    if (@($developmentFiles | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf }).Count -gt 0) {
        throw "Staged deployment contains appsettings.Development.json. Development settings must not be published."
    }

    try {
        Get-Content -LiteralPath (Join-Path $TemplatesDirectory "templates.json") -Raw | ConvertFrom-Json | Out-Null
        Get-Content -LiteralPath (Join-Path $ApiDirectory "appsettings.Production.json") -Raw | ConvertFrom-Json | Out-Null
        Get-Content -LiteralPath (Join-Path $WorkerDirectory "appsettings.Production.json") -Raw | ConvertFrom-Json | Out-Null
    }
    catch {
        throw "Staged deployment contains an invalid JSON configuration file."
    }
}

function Write-ProductionConfig {
    param(
        [string]$Directory,
        [string]$ProjectRoot,
        [string]$RunnerPath,
        [object]$SecurityConfig
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
            MaxActiveJobs = $MaxActiveJobs
            MaxActiveJobsPerUser = $MaxActiveJobsPerUser
        }
        Cleanup = [ordered]@{
            Enabled = $true
            FinishedJobRetentionDays = $FinishedJobRetentionDays
            BatchSize = 100
            IntervalHours = 24
        }
        TFlexAutomation = [ordered]@{
            Mode = "ExternalProcess"
            CommandPath = $RunnerPath
            Arguments = "`"{requestPath}`" `"{responsePath}`""
            TimeoutSeconds = 600
            WriteParameterFile = $true
            HealthCheckEnabled = (-not $SkipRunnerHealthCheck)
            HealthCheckIntervalSeconds = 300
            HealthCheckTimeoutSeconds = 60
        }
        Logging = [ordered]@{
            LogLevel = [ordered]@{
                Default = "Information"
                "Microsoft.AspNetCore" = "Warning"
                "Microsoft.Hosting.Lifetime" = "Information"
            }
        }
        Security = $SecurityConfig
        AllowedHosts = "*"
    }

    $path = Join-Path $Directory "appsettings.Production.json"
    $config | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $path -Encoding UTF8
}

function Get-HealthUrl {
    param(
        [string]$ServiceUrls,
        [bool]$RequireReady = $true
    )

    $uri = Get-FirstServiceUri $ServiceUrls
    $healthPath = if ($RequireReady) { "ready" } else { "live" }
    if ($null -ne $uri) {
        if ($uri.Scheme -ieq "https") {
            return "$($uri.GetLeftPart([UriPartial]::Authority))/api/health/$healthPath"
        }

        $port = Get-ApiPort $ServiceUrls
        return "http://127.0.0.1:$port/api/health/$healthPath"
    }

    return "http://127.0.0.1:5011/api/health/$healthPath"
}

function Get-ApiPort {
    param([string]$ServiceUrls)

    $uri = Get-FirstServiceUri $ServiceUrls
    if ($null -eq $uri) {
        return 5011
    }

    if ($uri.IsDefaultPort) {
        if ($uri.Scheme -ieq "https") {
            return 443
        }

        return 80
    }

    return $uri.Port
}

function Get-FirstServiceUri {
    param([string]$ServiceUrls)

    $firstUrl = ($ServiceUrls -split ";")[0]
    try {
        return [Uri]$firstUrl
    }
    catch {
        return $null
    }
}

function Test-IsLoopbackUrl {
    param([string]$ServiceUrls)

    $uri = Get-FirstServiceUri $ServiceUrls
    if ($null -eq $uri) {
        return $false
    }

    $hostName = $uri.Host.ToLowerInvariant()
    return $hostName -eq "localhost" -or $hostName -eq "127.0.0.1" -or $hostName -eq "::1"
}

function Assert-ProductionServiceUrls {
    param([string]$ServiceUrls)

    $configuredUrls = @($ServiceUrls -split ";" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($configuredUrls.Count -eq 0) {
        throw "At least one API URL must be configured."
    }

    foreach ($configuredUrl in $configuredUrls) {
        $uri = $null
        if (-not [Uri]::TryCreate($configuredUrl, [UriKind]::Absolute, [ref]$uri)) {
            throw "Invalid API URL '$configuredUrl'."
        }

        $hostName = $uri.Host.ToLowerInvariant()
        $isLoopback = $uri.IsLoopback -or @("localhost", "127.0.0.1", "::1") -contains $hostName
        if ($uri.Scheme -ieq "http" -and -not $isLoopback) {
            throw "Public plain HTTP is not supported because authentication cookies are Secure. Bind HTTP only to loopback behind an HTTPS reverse proxy, or configure a direct HTTPS endpoint."
        }
        if ($uri.Scheme -ine "http" -and $uri.Scheme -ine "https") {
            throw "API URL '$configuredUrl' must use http or https."
        }
    }
}

function Invoke-ApiHealthCheck {
    param([string]$HealthUrl)

    $lastError = ""
    for ($attempt = 1; $attempt -le $HealthCheckAttempts; $attempt++) {
        try {
            $response = Invoke-WebRequest -Uri $HealthUrl -UseBasicParsing -TimeoutSec 15
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

    throw "API health check failed after $HealthCheckAttempts attempt(s): $lastError"
}

function Assert-AuthenticationBoundary {
    param([string]$HealthUrl)

    if (-not $RequireAuthentication) {
        return
    }

    $authUrl = $HealthUrl -replace "/api/health(?:/(?:ready|live))?$", "/api/projects"
    try {
        $response = Invoke-WebRequest -Uri $authUrl -UseBasicParsing -TimeoutSec 15
        throw "Production authentication smoke check failed: $authUrl returned HTTP $($response.StatusCode) without a session."
    }
    catch {
        $statusCode = $null
        if ($null -ne $_.Exception.Response) {
            $statusCode = [int]$_.Exception.Response.StatusCode
        }
        if ($statusCode -eq 401) {
            return
        }
        if ($_.Exception.Message -like "Production authentication smoke check failed:*") {
            throw
        }

        throw "Production authentication smoke check could not confirm HTTP 401 at protected endpoint '$authUrl'."
    }
}

function Assert-ReadinessUnconfirmed {
    param([string]$LivenessUrl)

    $readyUrl = $LivenessUrl -replace "/api/health/live$", "/api/health/ready"
    try {
        $response = Invoke-WebRequest -Uri $readyUrl -UseBasicParsing -TimeoutSec 15
        throw "Diagnostic deployment unexpectedly reported ready at '$readyUrl' (HTTP $($response.StatusCode))."
    }
    catch {
        $statusCode = $null
        if ($null -ne $_.Exception.Response) {
            $statusCode = [int]$_.Exception.Response.StatusCode
        }
        if ($statusCode -eq 503) {
            return
        }
        if ($_.Exception.Message -like "Diagnostic deployment unexpectedly reported ready:*") {
            throw
        }

        throw "Diagnostic deployment could not confirm HTTP 503 at readiness endpoint '$readyUrl'."
    }
}

Assert-ProductionServiceUrls $Urls

if ([System.Environment]::OSVersion.Platform -ne [System.PlatformID]::Win32NT) {
    throw "This installer is intended for Windows servers."
}

if (-not $SkipServiceInstall -or -not $SkipFirewall) {
    if (-not (Test-IsAdmin)) {
        throw "Run PowerShell as Administrator, or pass -SkipServiceInstall -SkipFirewall."
    }
}

Assert-Command "dotnet"
if (-not $UseExistingSource) {
    Assert-Command "git"
}

$InstallRoot = Resolve-FullPath $InstallRoot
$sourceRootWasSpecified = -not [string]::IsNullOrWhiteSpace($SourceRoot)
if ($UseExistingSource -and -not $sourceRootWasSpecified) {
    throw "SourceRoot must be specified when UseExistingSource is enabled."
}

if ([string]::IsNullOrWhiteSpace($SourceRoot)) {
    $SourceRoot = Join-Path $InstallRoot "_src"
}
$SourceRoot = Resolve-FullPath $SourceRoot

if ($UseExistingSource) {
    Assert-SourceRoot $SourceRoot (-not $SkipRunnerBuild)
}

$apiDir = Join-Path $InstallRoot "Api"
$workerDir = Join-Path $InstallRoot "Worker"
$runnerDir = Join-Path $InstallRoot "Runner"
$templatesDir = Join-Path $InstallRoot "templates"
$storageDir = Join-Path $InstallRoot "storage"
$deploymentRoot = Join-Path $InstallRoot "_deployment"
$installerStateRoot = Join-Path $InstallRoot "_installer-state"
$bootstrapAdminRecoveryPath = Join-Path $installerStateRoot "pending-bootstrap-admin.json"

Write-Step "Preparing directories"
New-Item `
    -ItemType Directory `
    -Path $InstallRoot, $storageDir, $deploymentRoot `
    -Force | Out-Null

if ([string]::IsNullOrWhiteSpace($TFlexAutomationCommandPath)) {
    $TFlexAutomationCommandPath = Join-Path $runnerDir "TFlexAutomationRunner.exe"
}
$TFlexAutomationCommandPath = Resolve-FullPath $TFlexAutomationCommandPath

$existingApiConfigPath = Join-Path $apiDir "appsettings.Production.json"
$existingAdminHash = Get-ExistingAdminPasswordHash $existingApiConfigPath $AdminUser
$adminCredential = Resolve-AdminPasswordHash `
    -ExistingHash $existingAdminHash `
    -RecoveryPath $bootstrapAdminRecoveryPath `
    -UserName $AdminUser
if ($adminCredential.RecoveryRequired) {
    Write-BootstrapAdminRecovery `
        -Path $bootstrapAdminRecoveryPath `
        -UserName $AdminUser `
        -PasswordHash $adminCredential.Hash `
        -GeneratedPassword $adminCredential.GeneratedPassword
}
if (-not [string]::IsNullOrWhiteSpace($adminCredential.GeneratedPassword)) {
    Write-Host "Bootstrap admin user: $AdminUser" -ForegroundColor Yellow
    Write-Host "Bootstrap admin password: $($adminCredential.GeneratedPassword)" -ForegroundColor Yellow
    Write-Host "Record this credential now; it is reused automatically if deployment rolls back." -ForegroundColor Yellow
}
$securityConfig = [ordered]@{
    RequireAuthentication = $RequireAuthentication
    CookieName = "TFlexDrawingService.Auth"
    SessionMinutes = 480
    MaxRequestBodyBytes = 1048576
    RequireCsrfHeader = $true
    RedactJobErrors = $true
    ExposeWorkingDirectory = $false
    LoginRateLimitPermitLimit = 10
    LoginRateLimitWindowSeconds = 60
    JobCreateRateLimitPermitLimit = 10
    JobCreateRateLimitWindowSeconds = 60
    Users = @(
        [ordered]@{
            UserName = $AdminUser
            DisplayName = $AdminUser
            PasswordHash = $adminCredential.Hash
            Enabled = $true
            Roles = @("Admin", "Operator", "Viewer")
        }
    )
}

$serviceAccountSid = ""
$resolvedServicePassword = $ServicePassword
if (-not $SkipServiceInstall -and -not [string]::IsNullOrWhiteSpace($ServiceUser)) {
    $serviceAccountSid = Resolve-ServiceAccountSid $ServiceUser
    $passwordlessAccounts = @(
        "localsystem",
        "localservice",
        "networkservice",
        "nt authority\system",
        "nt authority\local service",
        "nt authority\network service"
    )
    $normalizedServiceUser = $ServiceUser.Trim().ToLowerInvariant()
    $canUseEmptyPassword = $passwordlessAccounts -contains $normalizedServiceUser -or $ServiceUser.EndsWith('$')
    if ([string]::IsNullOrEmpty($resolvedServicePassword) -and -not $canUseEmptyPassword) {
        $securePassword = Read-Host "Password for service account $ServiceUser" -AsSecureString
        $resolvedServicePassword = Get-PlainTextPassword $securePassword
    }
}

if ($UseExistingSource) {
    Write-Step "Using existing source without Git operations: $SourceRoot"
}
else {
    Write-Step "Downloading source"
    if (Test-Path -LiteralPath (Join-Path $SourceRoot ".git")) {
        Push-Location $SourceRoot
        try {
            Invoke-Native -FilePath "git" -Arguments @("fetch", "origin", $Branch)
            Invoke-Native -FilePath "git" -Arguments @("checkout", $Branch)
            Invoke-Native -FilePath "git" -Arguments @("pull", "--ff-only", "origin", $Branch)
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

        Invoke-Native -FilePath "git" -Arguments @("clone", "--branch", $Branch, "--", $RepositoryUrl, $SourceRoot)
    }
    else {
        Invoke-Native -FilePath "git" -Arguments @("clone", "--branch", $Branch, "--", $RepositoryUrl, $SourceRoot)
    }

    Assert-SourceRoot $SourceRoot (-not $SkipRunnerBuild)
}

$deploymentId = [Guid]::NewGuid().ToString("N")
$stageRoot = Join-Path $deploymentRoot "staging-$deploymentId"
$rollbackRoot = Join-Path $deploymentRoot "rollback-$deploymentId"
$stageApiDir = Join-Path $stageRoot "Api"
$stageWorkerDir = Join-Path $stageRoot "Worker"
$stageRunnerDir = Join-Path $stageRoot "Runner"
$stageTemplatesDir = Join-Path $stageRoot "templates"
$deploymentSucceeded = $false
$rollbackSucceeded = $true
$createdFirewallRuleName = ""
$directoryAclSnapshots = @()
$serviceLogonRightGrantedByDeployment = $false

New-Item -ItemType Directory -Path $stageRoot -Force | Out-Null

try {
    Write-Step "Publishing API to staging"
    Remove-DirectoryContents $stageApiDir
    Invoke-Native -FilePath "dotnet" -Arguments @(
        "publish",
        (Join-Path $SourceRoot "src\TFlexDrawingService.Api\TFlexDrawingService.Api.csproj"),
        "-c", "Release",
        "-r", "win-x64",
        "--self-contained", "false",
        "-o", $stageApiDir
    )

    Write-Step "Publishing Worker to staging"
    Remove-DirectoryContents $stageWorkerDir
    Invoke-Native -FilePath "dotnet" -Arguments @(
        "publish",
        (Join-Path $SourceRoot "src\TFlexDrawingService.Worker\TFlexDrawingService.Worker.csproj"),
        "-c", "Release",
        "-r", "win-x64",
        "--self-contained", "false",
        "-o", $stageWorkerDir
    )

    if (-not $SkipRunnerBuild) {
        Write-Step "Publishing T-FLEX automation runner to staging"
        if (-not (Test-Path -LiteralPath (Join-Path $TFlexCadProgramDir "TFlexAPI.dll"))) {
            throw "TFlexAPI.dll was not found in '$TFlexCadProgramDir'. Install T-FLEX CAD or pass -TFlexCadProgramDir."
        }

        Remove-DirectoryContents $stageRunnerDir
        Invoke-Native -FilePath "dotnet" -Arguments @(
            "publish",
            (Join-Path $SourceRoot "src\TFlexAutomationRunner\TFlexAutomationRunner.csproj"),
            "-c", "Release",
            "-p:TFlexCadProgramDir=$TFlexCadProgramDir",
            "-o", $stageRunnerDir
        )
    }
    elseif (-not (Test-Path -LiteralPath $TFlexAutomationCommandPath -PathType Leaf)) {
        throw "Runner build was skipped, but the configured runner executable does not exist."
    }

    $defaultRunnerPath = Resolve-FullPath (Join-Path $runnerDir "TFlexAutomationRunner.exe")
    if ($TFlexAutomationCommandPath -ine $defaultRunnerPath -and -not (Test-Path -LiteralPath $TFlexAutomationCommandPath -PathType Leaf)) {
        throw "The configured external automation command does not exist."
    }

    Write-Step "Copying templates to staging"
    Copy-Directory (Join-Path $SourceRoot "templates") $stageTemplatesDir
    Merge-RuntimeTemplatesIntoStage $templatesDir $stageTemplatesDir

    Write-Step "Writing staged production configuration"
    Write-ProductionConfig $stageApiDir $InstallRoot $TFlexAutomationCommandPath $securityConfig
    Write-ProductionConfig $stageWorkerDir $InstallRoot $TFlexAutomationCommandPath $securityConfig

    Assert-StagedDeployment `
        -ApiDirectory $stageApiDir `
        -WorkerDirectory $stageWorkerDir `
        -RunnerDirectory $stageRunnerDir `
        -TemplatesDirectory $stageTemplatesDir `
        -RequireRunner (-not $SkipRunnerBuild)

    if (-not $SkipFirewall) {
        Write-Step "Configuring firewall"
        if (Test-IsLoopbackUrl $Urls) {
            Write-Host "Skipping API firewall rule because API is bound to loopback only: $Urls" -ForegroundColor DarkGray
        }
        else {
            $apiPort = Get-ApiPort $Urls
            $firewallRuleName = "TFlex Drawing API $apiPort"
            if (-not (Get-NetFirewallRule -DisplayName $firewallRuleName -ErrorAction SilentlyContinue)) {
                New-NetFirewallRule `
                    -DisplayName $firewallRuleName `
                    -Direction Inbound `
                    -Protocol TCP `
                    -LocalPort $apiPort `
                    -Action Allow | Out-Null
                $createdFirewallRuleName = $firewallRuleName
            }
        }
    }

    $apiServiceSnapshot = Get-ServiceConfigurationSnapshot $ApiServiceName
    $workerServiceSnapshot = Get-ServiceConfigurationSnapshot $WorkerServiceName
    $apiServiceExisted = $apiServiceSnapshot.Exists
    $workerServiceExisted = $workerServiceSnapshot.Exists
    $apiWasRunning = $apiServiceSnapshot.Exists -and $apiServiceSnapshot.WasRunning
    $workerWasRunning = $workerServiceSnapshot.Exists -and $workerServiceSnapshot.WasRunning

    if (-not $SkipServiceInstall -and -not [string]::IsNullOrWhiteSpace($ServiceUser)) {
        Assert-ServiceAccountRollbackPossible $apiServiceSnapshot $ServiceUser $serviceAccountSid
        Assert-ServiceAccountRollbackPossible $workerServiceSnapshot $ServiceUser $serviceAccountSid
    }

    New-Item -ItemType Directory -Path $rollbackRoot -Force | Out-Null
    $components = @(
        [pscustomobject]@{ Name = "Api"; Stage = $stageApiDir; Live = $apiDir; Backup = (Join-Path $rollbackRoot "Api"); Order = 1 },
        [pscustomobject]@{ Name = "Worker"; Stage = $stageWorkerDir; Live = $workerDir; Backup = (Join-Path $rollbackRoot "Worker"); Order = 2 },
        [pscustomobject]@{ Name = "templates"; Stage = $stageTemplatesDir; Live = $templatesDir; Backup = (Join-Path $rollbackRoot "templates"); Order = 4 }
    )
    if (-not $SkipRunnerBuild) {
        $components += [pscustomobject]@{ Name = "Runner"; Stage = $stageRunnerDir; Live = $runnerDir; Backup = (Join-Path $rollbackRoot "Runner"); Order = 3 }
    }

    $backedUpComponents = @()
    $promotedComponents = @()
    try {
        if (-not $SkipServiceInstall -and -not [string]::IsNullOrWhiteSpace($ServiceUser)) {
            Write-Step "Granting the service account required local rights"
            Grant-LogOnAsServiceRight `
                -AccountName $ServiceUser `
                -Sid $serviceAccountSid `
                -GrantedByDeployment ([ref]$serviceLogonRightGrantedByDeployment)
        }

        if (-not $SkipServiceInstall) {
            Write-Step "Stopping services after staging validation"
            Stop-ServiceIfExists $WorkerServiceName
            Stop-ServiceIfExists $ApiServiceName
        }

        # Refresh the runtime-import merge after writers are stopped so an admin
        # import completed during staging cannot be lost in the activation race.
        Write-Step "Refreshing runtime templates after stopping writers"
        Copy-Directory (Join-Path $SourceRoot "templates") $stageTemplatesDir
        Merge-RuntimeTemplatesIntoStage $templatesDir $stageTemplatesDir
        Assert-StagedDeployment `
            -ApiDirectory $stageApiDir `
            -WorkerDirectory $stageWorkerDir `
            -RunnerDirectory $stageRunnerDir `
            -TemplatesDirectory $stageTemplatesDir `
            -RequireRunner (-not $SkipRunnerBuild)

        Write-Step "Activating staged deployment"
        foreach ($component in $components) {
            if (Test-Path -LiteralPath $component.Live) {
                Move-Item -LiteralPath $component.Live -Destination $component.Backup
                $backedUpComponents += $component.Name
            }

            Move-Item -LiteralPath $component.Stage -Destination $component.Live
            $promotedComponents += $component.Name
        }

        if (-not (Test-Path -LiteralPath $TFlexAutomationCommandPath -PathType Leaf)) {
            throw "The automation runner executable is unavailable after deployment activation."
        }

        if (-not $SkipServiceInstall) {
            Write-Step "Installing Windows services"
            $apiExe = Join-Path $apiDir "TFlexDrawingService.Api.exe"
            $workerExe = Join-Path $workerDir "TFlexDrawingService.Worker.exe"

            Install-OrUpdateService `
                -Name $ApiServiceName `
                -DisplayName "T-FLEX Drawing Service API" `
                -BinaryPath "`"$apiExe`" --urls $Urls"

            Install-OrUpdateService `
                -Name $WorkerServiceName `
                -DisplayName "T-FLEX Drawing Service Worker" `
                -BinaryPath "`"$workerExe`""

            Set-ServiceEnvironmentVariable `
                -ServiceName $ApiServiceName `
                -VariableName "DOTNET_ENVIRONMENT" `
                -Value "Production"
            Set-ServiceEnvironmentVariable `
                -ServiceName $ApiServiceName `
                -VariableName "ASPNETCORE_ENVIRONMENT" `
                -Value "Production"
            Set-ServiceEnvironmentVariable `
                -ServiceName $WorkerServiceName `
                -VariableName "DOTNET_ENVIRONMENT" `
                -Value "Production"

            if (-not [string]::IsNullOrWhiteSpace($ServiceUser)) {
                Set-ServiceAccount `
                    -Name $ApiServiceName `
                    -User $ServiceUser `
                    -Password $resolvedServicePassword `
                    -ExpectedSid $serviceAccountSid
                Set-ServiceAccount `
                    -Name $WorkerServiceName `
                    -User $ServiceUser `
                    -Password $resolvedServicePassword `
                    -ExpectedSid $serviceAccountSid
            }

            $apiServiceAccount = Get-ServiceAccountIdentity $ApiServiceName
            $workerServiceAccount = Get-ServiceAccountIdentity $WorkerServiceName
            $apiReadDirectories = @($apiDir)
            $workerReadDirectories = @($workerDir, $templatesDir, $TFlexCadProgramDir)
            if (Test-Path -LiteralPath $runnerDir -PathType Container) {
                $workerReadDirectories += $runnerDir
            }

            $externalRunnerDirectory = Split-Path $TFlexAutomationCommandPath -Parent
            if (-not [string]::IsNullOrWhiteSpace($externalRunnerDirectory)) {
                $workerReadDirectories += $externalRunnerDirectory
            }

            $previousApiServiceSid = if ($apiServiceSnapshot.Exists) {
                Resolve-ServiceAccountSid $apiServiceSnapshot.StartName
            }
            else {
                ""
            }
            $previousWorkerServiceSid = if ($workerServiceSnapshot.Exists) {
                Resolve-ServiceAccountSid $workerServiceSnapshot.StartName
            }
            else {
                ""
            }
            $currentServiceSids = @(
                $apiServiceAccount.Sid,
                $workerServiceAccount.Sid
            ) | Select-Object -Unique

            $previousWorkerReadDirectories = @($workerDir, $templatesDir)
            if (Test-Path -LiteralPath $runnerDir -PathType Container) {
                $previousWorkerReadDirectories += $runnerDir
            }
            $previousTFlexCadProgramDir = Get-ServiceEnvironmentSnapshotValue `
                -ServiceSnapshot $workerServiceSnapshot `
                -VariableName "TFLEX_CAD_PROGRAM_DIR"
            if (-not [string]::IsNullOrWhiteSpace($previousTFlexCadProgramDir)) {
                $previousWorkerReadDirectories += $previousTFlexCadProgramDir
            }
            $previousRunnerPath = Get-ServiceEnvironmentSnapshotValue `
                -ServiceSnapshot $workerServiceSnapshot `
                -VariableName "TFlexAutomation__CommandPath"
            if (-not [string]::IsNullOrWhiteSpace($previousRunnerPath)) {
                $previousRunnerDirectory = Split-Path $previousRunnerPath -Parent
                if (-not [string]::IsNullOrWhiteSpace($previousRunnerDirectory)) {
                    $previousWorkerReadDirectories += $previousRunnerDirectory
                }
            }

            $managedAccessDirectories = @(
                $InstallRoot,
                $storageDir,
                $templatesDir
            )
            $managedAccessDirectories += $apiReadDirectories
            $managedAccessDirectories += $workerReadDirectories
            $managedAccessDirectories += $previousWorkerReadDirectories
            $directoryAclSnapshots = @(
                Get-DirectoryAclSnapshots $managedAccessDirectories
            )

            if (-not [string]::IsNullOrWhiteSpace($previousApiServiceSid) -and
                $currentServiceSids -notcontains $previousApiServiceSid) {
                Remove-ServiceFileSystemAccess `
                    -Sid $previousApiServiceSid `
                    -ReadExecuteDirectories @($apiDir) `
                    -ModifyDirectories @($templatesDir)
            }
            if (-not [string]::IsNullOrWhiteSpace($previousWorkerServiceSid) -and
                $currentServiceSids -notcontains $previousWorkerServiceSid) {
                Remove-ServiceFileSystemAccess `
                    -Sid $previousWorkerServiceSid `
                    -ReadExecuteDirectories $previousWorkerReadDirectories
            }

            # The templates directory is replaced during every activation. Always
            # reapply access for the effective identities. Persistent and external
            # directories first lose explicit Allow ACEs owned by service SIDs that
            # are no longer used by either managed service.
            Write-Step "Configuring effective service account file access"
            Grant-ServiceFileSystemAccess `
                -Sid $apiServiceAccount.Sid `
                -ReadExecuteDirectories $apiReadDirectories `
                -ModifyDirectories @($templatesDir)
            Grant-ServiceFileSystemAccess `
                -Sid $workerServiceAccount.Sid `
                -ReadExecuteDirectories $workerReadDirectories

            # The runner resolves TFlexAPI.dll at runtime from this variable.
            # A service-specific environment value works for non-default T-FLEX
            # installations without changing the machine-wide PATH.
            Set-ServiceEnvironmentVariable `
                -ServiceName $WorkerServiceName `
                -VariableName "TFLEX_CAD_PROGRAM_DIR" `
                -Value $TFlexCadProgramDir

            # Service-specific values override stale machine/service settings,
            # preventing a previous development Mode=Mock from taking precedence
            # over appsettings.Production.json.
            Set-ServiceEnvironmentVariable `
                -ServiceName $WorkerServiceName `
                -VariableName "TFlexAutomation__Mode" `
                -Value "ExternalProcess"
            Set-ServiceEnvironmentVariable `
                -ServiceName $WorkerServiceName `
                -VariableName "TFlexAutomation__CommandPath" `
                -Value $TFlexAutomationCommandPath
            $runnerHealthCheckEnabled = if ($SkipRunnerHealthCheck) { "false" } else { "true" }
            Set-ServiceEnvironmentVariable `
                -ServiceName $WorkerServiceName `
                -VariableName "TFlexAutomation__HealthCheckEnabled" `
                -Value $runnerHealthCheckEnabled

            Write-Step "Starting Windows services"
            Start-Service -Name $ApiServiceName
            Start-Service -Name $WorkerServiceName
            (Get-Service -Name $ApiServiceName).WaitForStatus("Running", [TimeSpan]::FromSeconds(30))
            (Get-Service -Name $WorkerServiceName).WaitForStatus("Running", [TimeSpan]::FromSeconds(30))

            Start-Sleep -Seconds 5
            if ((Get-Service -Name $ApiServiceName).Status -ne "Running" -or
                (Get-Service -Name $WorkerServiceName).Status -ne "Running") {
                throw "One or more Windows services stopped during deployment validation."
            }

            if (-not (Test-ServiceEnvironmentValue $ApiServiceName "DOTNET_ENVIRONMENT" "Production") -or
                -not (Test-ServiceEnvironmentValue $ApiServiceName "ASPNETCORE_ENVIRONMENT" "Production") -or
                -not (Test-ServiceEnvironmentValue $WorkerServiceName "DOTNET_ENVIRONMENT" "Production") -or
                -not (Test-ServiceEnvironmentValue $WorkerServiceName "TFlexAutomation__Mode" "ExternalProcess") -or
                -not (Test-ServiceEnvironmentValue $WorkerServiceName "TFlexAutomation__CommandPath" $TFlexAutomationCommandPath) -or
                -not (Test-ServiceEnvironmentValue $WorkerServiceName "TFlexAutomation__HealthCheckEnabled" $runnerHealthCheckEnabled)) {
                throw "One or more Windows services did not retain the required Production environment."
            }

            $healthUrl = Get-HealthUrl `
                -ServiceUrls $Urls `
                -RequireReady (-not $SkipRunnerHealthCheck)
            Invoke-ApiHealthCheck $healthUrl
            Assert-AuthenticationBoundary $healthUrl
            if ($SkipRunnerHealthCheck) {
                Assert-ReadinessUnconfirmed $healthUrl
                Write-Warning "Runner health-check was skipped for diagnostics; liveness passed but readiness remains HTTP 503."
            }

            Start-Sleep -Seconds 5
            if ((Get-Service -Name $ApiServiceName).Status -ne "Running" -or
                (Get-Service -Name $WorkerServiceName).Status -ne "Running") {
                throw "One or more Windows services stopped after the deployment smoke checks."
            }

            Write-Host "API health check passed: $healthUrl" -ForegroundColor Green
            if ($RequireAuthentication) {
                Write-Host "Authentication boundary check passed: anonymous /api/projects returned HTTP 401." -ForegroundColor Green
            }
        }

        $deploymentSucceeded = $true
    }
    catch {
        $deploymentError = $_
        $deploymentErrorMessage = [string]$deploymentError.Exception.Message
        if ([string]::IsNullOrWhiteSpace($deploymentErrorMessage)) {
            $deploymentErrorMessage = "Unknown deployment validation error."
        }
        Write-Warning "Deployment validation failed: $deploymentErrorMessage Restoring the previous deployment."

        if (-not $SkipServiceInstall) {
            try { Stop-ServiceIfExists $WorkerServiceName } catch { $rollbackSucceeded = $false }
            try { Stop-ServiceIfExists $ApiServiceName } catch { $rollbackSucceeded = $false }
        }

        if ($directoryAclSnapshots.Count -gt 0) {
            try {
                Restore-DirectoryAclSnapshots $directoryAclSnapshots
            }
            catch {
                $rollbackSucceeded = $false
            }
        }

        foreach ($component in @($components | Sort-Object Order -Descending)) {
            try {
                if ($promotedComponents -contains $component.Name -and (Test-Path -LiteralPath $component.Live)) {
                    Remove-Item -LiteralPath $component.Live -Recurse -Force
                }
                if ($backedUpComponents -contains $component.Name -and (Test-Path -LiteralPath $component.Backup)) {
                    Move-Item -LiteralPath $component.Backup -Destination $component.Live
                }
            }
            catch {
                $rollbackSucceeded = $false
            }
        }

        if (-not $SkipServiceInstall) {
            if (-not $apiServiceExisted -and (Test-ServiceExists $ApiServiceName)) {
                try { Invoke-Sc @("delete", $ApiServiceName) } catch { $rollbackSucceeded = $false }
            }
            elseif ($apiServiceExisted) {
                try { Restore-ServiceConfiguration $apiServiceSnapshot } catch { $rollbackSucceeded = $false }
            }
            if (-not $workerServiceExisted -and (Test-ServiceExists $WorkerServiceName)) {
                try { Invoke-Sc @("delete", $WorkerServiceName) } catch { $rollbackSucceeded = $false }
            }
            elseif ($workerServiceExisted) {
                try { Restore-ServiceConfiguration $workerServiceSnapshot } catch { $rollbackSucceeded = $false }
            }

            if ($apiWasRunning -and (Test-ServiceExists $ApiServiceName)) {
                try {
                    Start-Service -Name $ApiServiceName
                    (Get-Service -Name $ApiServiceName).WaitForStatus("Running", [TimeSpan]::FromSeconds(30))
                }
                catch { $rollbackSucceeded = $false }
            }
            if ($workerWasRunning -and (Test-ServiceExists $WorkerServiceName)) {
                try {
                    Start-Service -Name $WorkerServiceName
                    (Get-Service -Name $WorkerServiceName).WaitForStatus("Running", [TimeSpan]::FromSeconds(30))
                }
                catch { $rollbackSucceeded = $false }
            }
        }

        if ($serviceLogonRightGrantedByDeployment) {
            try {
                $rightRemoved = Remove-LogOnAsServiceRightIfUnused `
                    -AccountName $ServiceUser `
                    -Sid $serviceAccountSid
                if ($rightRemoved) {
                    $serviceLogonRightGrantedByDeployment = $false
                }
            }
            catch {
                $rollbackSucceeded = $false
            }
        }

        if (-not $rollbackSucceeded) {
            throw "Deployment failed: $deploymentErrorMessage Automatic rollback was incomplete. Recovery files were preserved in '$rollbackRoot'."
        }

        throw "Deployment failed: $deploymentErrorMessage The previous deployment was restored."
    }
}
finally {
    if (-not $deploymentSucceeded -and -not [string]::IsNullOrWhiteSpace($createdFirewallRuleName)) {
        Remove-NetFirewallRule -DisplayName $createdFirewallRuleName -ErrorAction SilentlyContinue
    }
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
Write-Host "Installed to: $InstallRoot" -ForegroundColor Green
Write-Host "API URL: $Urls" -ForegroundColor Green
Write-Host "Runner: $TFlexAutomationCommandPath" -ForegroundColor Green
Write-Host "Bootstrap admin user: $AdminUser" -ForegroundColor Green
if (-not [string]::IsNullOrWhiteSpace($adminCredential.GeneratedPassword)) {
    $credentialSource = if ($adminCredential.Recovered) { "Recovered" } else { "Generated" }
    Write-Host "$credentialSource bootstrap admin password: $($adminCredential.GeneratedPassword)" -ForegroundColor Yellow
    Write-Host "This password is used only if the admin user does not already exist in storage\drawings.db." -ForegroundColor Yellow
}
elseif ($adminCredential.Reused) {
    Write-Host "Bootstrap admin password hash was reused from the previous production config." -ForegroundColor Green
}

try {
    Remove-BootstrapAdminRecovery $bootstrapAdminRecoveryPath
}
catch {
    Write-Warning "Deployment succeeded, but protected bootstrap admin recovery state could not be removed from '$bootstrapAdminRecoveryPath'."
}
