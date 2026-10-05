<#
.SYNOPSIS
Exports untouched T-FLEX catalog templates to native PDF reference files.

.DESCRIPTION
Run on Windows with licensed T-FLEX CAD and the published
TFlexAutomationRunner.exe. Each selected catalog template is copied with its
fragment directories into a fresh isolated workspace, then exported as PDF.
The source catalog and drawings are never modified. Existing per-template
output directories cause the script to stop rather than overwrite prior work.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SourceRoot,

    [Parameter(Mandatory = $true)]
    [string]$RunnerPath,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [string[]]$TemplateIds
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

function Get-Sha256 {
    param([string]$Path)
    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $sha = [System.Security.Cryptography.SHA256]::Create()
        try { return ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace("-", "").ToLowerInvariant() }
        finally { $sha.Dispose() }
    }
    finally { $stream.Dispose() }
}

function Copy-TemplateDirectory {
    param([string]$Source, [string]$Destination)
    if ((Get-Item -LiteralPath $Source -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw "Refusing to copy reparse point '$Source'."
    }
    [void][IO.Directory]::CreateDirectory($Destination)
    foreach ($entry in Get-ChildItem -LiteralPath $Source -Force) {
        if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw "Refusing to copy reparse point '$($entry.FullName)'."
        }
        $target = Join-Path $Destination $entry.Name
        if ($entry.PSIsContainer) { Copy-TemplateDirectory -Source $entry.FullName -Destination $target }
        else { [IO.File]::Copy($entry.FullName, $target, $false) }
    }
}

function Test-PathWithin {
    param([string]$Path, [string]$Parent)
    $prefix = $Parent.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    return $Path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
        $Path.Equals($Parent, [StringComparison]::OrdinalIgnoreCase)
}

$source = [IO.Path]::GetFullPath($SourceRoot)
$runner = [IO.Path]::GetFullPath($RunnerPath)
$output = [IO.Path]::GetFullPath($OutputDirectory)
$catalogPath = Join-Path $source "templates\templates.json"
if (-not (Test-Path -LiteralPath $catalogPath -PathType Leaf)) { throw "Catalog not found: $catalogPath" }
if (-not (Test-Path -LiteralPath $runner -PathType Leaf)) { throw "Runner not found: $runner" }
if ($output.Equals($source, [StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputDirectory cannot be SourceRoot. A child artifact directory is allowed."
}

$catalog = Get-Content -LiteralPath $catalogPath -Raw -Encoding UTF8 | ConvertFrom-Json
$allTemplates = @($catalog.templates)
if ($allTemplates.Count -ne 9) { throw "Expected 9 catalog templates, found $($allTemplates.Count)." }
$selected = if ($TemplateIds -and $TemplateIds.Count -gt 0) {
    foreach ($id in $TemplateIds) {
        $matches = @($allTemplates | Where-Object { $_.id -eq $id })
        if ($matches.Count -ne 1) { throw "Unknown or ambiguous template id '$id'." }
        $matches[0]
    }
} else { $allTemplates }

[void][IO.Directory]::CreateDirectory($output)
$manifest = [Collections.Generic.List[object]]::new()
foreach ($template in $selected) {
    if ([string]$template.id -notmatch '^[A-Za-z0-9._-]+$') { throw "Unsafe template id '$($template.id)'." }
    $relativePath = [string]$template.templateFilePath
    if ([string]::IsNullOrWhiteSpace($relativePath) -or [IO.Path]::IsPathRooted($relativePath)) {
        throw "Template '$($template.id)' has an invalid templateFilePath."
    }
    $templatePath = [IO.Path]::GetFullPath((Join-Path $source $relativePath))
    if (-not (Test-PathWithin -Path $templatePath -Parent $source) -or -not (Test-Path -LiteralPath $templatePath -PathType Leaf)) {
        throw "Template file is missing or outside SourceRoot: $templatePath"
    }
    $templateDirectory = [IO.Path]::GetDirectoryName($templatePath)
    $fragmentDirectories = @(
        (Join-Path $templateDirectory ([IO.Path]::GetFileNameWithoutExtension($templatePath))),
        (Join-Path $templateDirectory "Фрагменты")
    ) | Select-Object -Unique
    foreach ($fragmentDirectory in $fragmentDirectories) {
        if (Test-Path -LiteralPath $fragmentDirectory -PathType Container) {
            if ((Test-PathWithin -Path $output -Parent $fragmentDirectory) -or (Test-PathWithin -Path $fragmentDirectory -Parent $output)) {
                throw "OutputDirectory overlaps source template fragments: $fragmentDirectory"
            }
        }
    }

    $templateOutput = Join-Path $output ([string]$template.id)
    if (Test-Path -LiteralPath $templateOutput) { throw "Output already exists; refusing overwrite: $templateOutput" }
    $working = Join-Path $templateOutput "working"
    $result = Join-Path $templateOutput "result"
    [void][IO.Directory]::CreateDirectory($working)
    [void][IO.Directory]::CreateDirectory($result)
    $workingTemplate = Join-Path $working ([IO.Path]::GetFileName($templatePath))
    [IO.File]::Copy($templatePath, $workingTemplate, $false)
    foreach ($fragmentDirectory in $fragmentDirectories) {
        if (Test-Path -LiteralPath $fragmentDirectory -PathType Container) {
            Copy-TemplateDirectory -Source $fragmentDirectory -Destination (Join-Path $working ([IO.Path]::GetFileName($fragmentDirectory)))
        }
    }

    $responsePath = Join-Path $templateOutput "response.json"
    $requestPath = Join-Path $templateOutput "request.json"
    $request = [ordered]@{
        jobId = "native-reference-$($template.id)"
        templateId = [string]$template.id
        templateCode = [string]$template.code
        workingDirectory = "working"
        templateCopyPath = "working\$([IO.Path]::GetFileName($templatePath))"
        resultDirectory = "result"
        outputFormat = "pdf"
        parameters = @{}
    }
    $request | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $requestPath -Encoding UTF8
    $previousLocation = Get-Location
    try {
        Set-Location -LiteralPath $templateOutput
        & $runner $requestPath $responsePath
        $exitCode = $LASTEXITCODE
    }
    finally { Set-Location $previousLocation }
    if ($exitCode -ne 0) { throw "Runner failed for '$($template.id)' with exit code $exitCode." }
    if (-not (Test-Path -LiteralPath $responsePath -PathType Leaf)) { throw "Runner response missing for '$($template.id)'." }
    $response = Get-Content -LiteralPath $responsePath -Raw -Encoding UTF8 | ConvertFrom-Json
    $errorMessageProperty = $response.PSObject.Properties["errorMessage"]
    if ($null -ne $errorMessageProperty -and $errorMessageProperty.Value) {
        throw "Runner reported failure for '$($template.id)': $($errorMessageProperty.Value)"
    }
    $files = @($response.files)
    if ($files.Count -ne 1 -or [string]$files[0].format -ne "pdf" -or [string]::IsNullOrWhiteSpace([string]$files[0].path)) {
        throw "Runner response did not contain exactly one PDF for '$($template.id)'."
    }
    $pdfPath = [IO.Path]::GetFullPath((Join-Path $result ([string]$files[0].path)))
    if (-not (Test-PathWithin -Path $pdfPath -Parent $result)) {
        throw "Runner PDF path is outside the result directory for '$($template.id)': $pdfPath"
    }
    if (-not (Test-Path -LiteralPath $pdfPath -PathType Leaf) -or (Get-Item -LiteralPath $pdfPath).Length -le 0) {
        throw "Runner PDF missing or empty for '$($template.id)': $pdfPath"
    }
    $pdfStream = [IO.File]::OpenRead($pdfPath)
    try {
        $signature = [byte[]]::new(5)
        if ($pdfStream.Read($signature, 0, 5) -ne 5 -or [Text.Encoding]::ASCII.GetString($signature) -ne "%PDF-") {
            throw "Runner output is not a PDF for '$($template.id)': $pdfPath"
        }
    }
    finally { $pdfStream.Dispose() }
    $manifest.Add([ordered]@{
        templateId = [string]$template.id
        sourceTemplatePath = $templatePath
        sourceSha256 = Get-Sha256 -Path $templatePath
        nativePdfPath = $pdfPath
        parameterState = "source-document"
    })
}

$manifestPath = Join-Path $output "manifest.json"
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
Write-Host "Exported $($manifest.Count) native T-FLEX PDF reference(s). Manifest: $manifestPath"
