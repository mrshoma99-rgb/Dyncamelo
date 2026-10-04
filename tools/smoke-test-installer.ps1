<#
.SYNOPSIS
  Installs DyncameloSetup.exe silently on this machine, checks what it installed against the staged bundle, installs over the top,
  uninstalls, and checks nothing is left behind.

.DESCRIPTION
  Runs on the release workflow's Windows runner before anything is published, so a broken installer never reaches a release. It checks:
  the exit codes, that every file of the staged bundle arrived byte for byte, that each year folder holds the plug-in and its libraries,
  that the assemblies carry the release version, that no downloaded-file mark (Zone.Identifier) was left on a DLL, the Add/Remove
  Programs entry, that a second install replaces the first completely (a stale file disappears), that uninstall removes the folder and
  the entry, and (when SIGNED=true) that the installed Dyncamelo assemblies are signed.
#>
param(
    [Parameter(Mandatory = $true)][string]$Installer,
    [Parameter(Mandatory = $true)][string]$StagedBundle,
    [Parameter(Mandatory = $true)][string]$Version
)

$ErrorActionPreference = 'Stop'
$numeric = $Version.TrimStart('v')
$exe = (Resolve-Path $Installer).Path
$staged = (Resolve-Path $StagedBundle).Path
$target = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\Dyncamelo.bundle'
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Dyncamelo'
$years = '2024', '2025', '2026'
$failures = New-Object System.Collections.Generic.List[string]

function Fail([string]$message) {
    Write-Host "::error::Installer smoke test: $message"
    $failures.Add($message)
}

function Run-Setup([string]$path, [string[]]$arguments) {
    $process = Start-Process -FilePath $path -ArgumentList $arguments -PassThru
    if (-not $process.WaitForExit(180000)) {
        $process.Kill()
        throw "$path $($arguments -join ' ') did not finish within 3 minutes."
    }
    return $process.ExitCode
}

# ----- 1. a fresh install ---------------------------------------------------------------------------------------------------------
if (Test-Path $target) { Remove-Item $target -Recurse -Force }
$code = Run-Setup $exe @('/silent')
if ($code -ne 0) { throw "DyncameloSetup.exe /silent exited with code $code." }

if (-not (Test-Path (Join-Path $target 'PackageContents.xml'))) { throw "PackageContents.xml is missing from $target after install." }
[xml]$manifest = Get-Content (Join-Path $target 'PackageContents.xml') -Raw
if ($manifest.ApplicationPackage.Version -ne "$numeric.0") {
    Fail "PackageContents.xml says version $($manifest.ApplicationPackage.Version), expected $numeric.0"
}

# Every staged file arrived, unchanged.
$stagedFiles = Get-ChildItem $staged -Recurse -File
foreach ($file in $stagedFiles) {
    $relative = $file.FullName.Substring($staged.Length).TrimStart('\')
    $installed = Join-Path $target $relative
    if (-not (Test-Path $installed)) { Fail "missing after install: $relative"; continue }
    if ((Get-FileHash $installed -Algorithm SHA256).Hash -ne (Get-FileHash $file.FullName -Algorithm SHA256).Hash) {
        Fail "differs from the staged file: $relative"
    }
}

$required = 'Dyncamelo.App.dll', 'Dyncamelo.Core.dll', 'Dyncamelo.Nodes.dll', 'Dyncamelo.Navisworks.dll', 'Dyncamelo.UI.dll',
            'Nodify.dll', 'Newtonsoft.Json.dll', 'AutomaticGraphLayout.dll'
foreach ($year in $years) {
    $folder = Join-Path $target $year
    foreach ($name in $required) {
        if (-not (Test-Path (Join-Path $folder $name))) { Fail "$year\$name is missing" }
    }
    if (-not (Test-Path (Join-Path $folder 'en-US\Dyncamelo.xaml'))) { Fail "$year\en-US\Dyncamelo.xaml is missing" }
    if (@(Get-ChildItem (Join-Path $folder 'Samples') -Filter *.dyc -ErrorAction SilentlyContinue).Count -lt 1) { Fail "$year has no sample graphs" }
    if (@(Get-ChildItem (Join-Path $folder 'Resources') -Filter *.png -ErrorAction SilentlyContinue).Count -lt 1) { Fail "$year has no icons" }

    foreach ($name in $required | Where-Object { $_ -like 'Dyncamelo.*' }) {
        $path = Join-Path $folder $name
        if (-not (Test-Path $path)) { continue }
        try {
            $assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($path).Version
            if ($assemblyVersion.ToString(3) -ne $numeric) {
                Fail "$year\$name has assembly version $assemblyVersion, expected $numeric (was Directory.Build.props bumped with dist/RELEASE_VERSION?)"
            }
        }
        catch { Fail "$year\$name is not a loadable .NET assembly: $($_.Exception.Message)" }
    }
}

# No "downloaded from the internet" mark on any DLL (Navisworks refuses to load such a plug-in).
foreach ($dll in Get-ChildItem $target -Recurse -Filter *.dll) {
    if (Get-Item $dll.FullName -Stream Zone.Identifier -ErrorAction SilentlyContinue) { Fail "$($dll.Name) still carries the downloaded-file mark" }
}

if ($env:SIGNED -eq 'true') {
    foreach ($dll in Get-ChildItem $target -Recurse -Filter 'Dyncamelo.*.dll') {
        $signature = Get-AuthenticodeSignature $dll.FullName
        if ($signature.Status -ne 'Valid') { Fail "$($dll.FullName.Substring($target.Length)) is not validly signed: $($signature.Status)" }
    }
}

if (-not (Test-Path $uninstallKey)) { Fail "no Add/Remove Programs entry ($uninstallKey)" }
else {
    $entry = Get-ItemProperty $uninstallKey
    if ($entry.DisplayName -ne 'CamelGraph for Navisworks') { Fail "Add/Remove Programs shows '$($entry.DisplayName)'" }
    if ($entry.DisplayVersion -notlike "$numeric*") { Fail "Add/Remove Programs shows version '$($entry.DisplayVersion)', expected $numeric" }
    if (-not $entry.UninstallString) { Fail "Add/Remove Programs entry has no uninstall command" }
}
if (-not (Test-Path (Join-Path $target 'DyncameloSetup.exe'))) { Fail "the uninstaller (DyncameloSetup.exe) was not copied into the bundle" }

# ----- 2. installing over the top replaces everything ---------------------------------------------------------------------------------
Set-Content -Path (Join-Path $target '2024\left-over-from-an-older-version.dll') -Value 'stale'
$code = Run-Setup $exe @('/silent')
if ($code -ne 0) { Fail "installing over an existing install exited with code $code" }
if (Test-Path (Join-Path $target '2024\left-over-from-an-older-version.dll')) { Fail "a file of the previous install survived the upgrade" }
if (-not (Test-Path (Join-Path $target 'PackageContents.xml'))) { Fail "the upgrade left no PackageContents.xml" }

# ----- 3. uninstall ---------------------------------------------------------------------------------------------------------------------
$code = Run-Setup (Join-Path $target 'DyncameloSetup.exe') @('/uninstall', '/silent')
if ($code -ne 0) { Fail "uninstall exited with code $code" }
# The uninstaller moves itself to %TEMP% and removes the folder just after it returns; give it a moment.
for ($i = 0; $i -lt 30 -and (Test-Path $target); $i++) { Start-Sleep -Seconds 1 }
if (Test-Path $target) { Fail "the bundle folder is still there after uninstall" }
if (Test-Path $uninstallKey) { Fail "the Add/Remove Programs entry is still there after uninstall" }

if ($failures.Count -gt 0) {
    throw "The installer smoke test found $($failures.Count) problem(s):`n - " + ($failures -join "`n - ")
}
Write-Host "Installer smoke test passed: fresh install, byte-for-byte payload, upgrade over the top, uninstall."
