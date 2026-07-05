<#
.SYNOPSIS
    Installs Reaper Stance into Pillars of Eternity 1 with NO compilation required.

.DESCRIPTION
    This script:
      1. copies the prebuilt sidecar (LoomReapingKnivesModal.dll) into the game's
         Managed folder,
      2. backs up Assembly-CSharp.dll (once), and
      3. injects a single call to LoomReapingKnivesModal.Bootstrap.Tick() at the top of
         GameState.Update() using the bundled Mono.Cecil.dll.

    It needs only Windows PowerShell (5.1+, built into Windows) -- no .NET SDK, no
    C# compiler, no .NET runtime install. Run it from the folder that also contains
    LoomReapingKnivesModal.dll and Mono.Cecil.dll (i.e. the extracted release zip).

    Close the game before running.

.PARAMETER GameDir
    Path to the Pillars of Eternity install directory (the folder that contains
    PillarsOfEternity_Data). If omitted, common Steam locations are probed.

.EXAMPLE
    ./install.ps1 -GameDir "E:\SteamLibrary\steamapps\common\Pillars of Eternity"

.EXAMPLE
    ./install.ps1        # auto-detect a Steam install
#>
[CmdletBinding()]
param(
    [string]$GameDir,

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$RemainingArgs
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

function Normalize-PathInput([string]$path) {
    if ([string]::IsNullOrWhiteSpace($path)) {
        return $null
    }

    $p = $path.Trim()
    if ($p.Length -ge 2 -and (($p.StartsWith('"') -and $p.EndsWith('"')) -or ($p.StartsWith("'") -and $p.EndsWith("'")))) {
        $p = $p.Substring(1, $p.Length - 2).Trim()
    }

    return [Environment]::ExpandEnvironmentVariables($p)
}

function Get-SteamRoots {
    $roots = New-Object System.Collections.Generic.List[string]

    foreach ($regPath in @(
        'HKCU:\Software\Valve\Steam',
        'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam',
        'HKLM:\SOFTWARE\Valve\Steam'
    )) {
        try {
            $props = Get-ItemProperty -LiteralPath $regPath -ErrorAction Stop
            foreach ($name in @('SteamPath', 'InstallPath')) {
                $value = Normalize-PathInput $props.$name
                if ($value -and (Test-Path -LiteralPath $value)) {
                    $roots.Add($value)
                }
            }
        } catch {
            # Registry lookup is best-effort; portable Steam installs may not have these keys.
        }
    }

    foreach ($root in @($roots.ToArray())) {
        $vdf = Join-Path $root 'steamapps\libraryfolders.vdf'
        if (-not (Test-Path -LiteralPath $vdf)) {
            continue
        }

        try {
            foreach ($line in Get-Content -LiteralPath $vdf) {
                if ($line -match '"path"\s+"([^"]+)"') {
                    $library = ($Matches[1] -replace '\\\\', '\')
                    if ($library -and (Test-Path -LiteralPath $library)) {
                        $roots.Add($library)
                    }
                }
            }
        } catch {
            # A malformed or locked VDF should not prevent prompting.
        }
    }

    return $roots.ToArray() | Where-Object { $_ } | Select-Object -Unique
}

function Get-CandidateGameDirs {
    $guesses = New-Object System.Collections.Generic.List[string]

    foreach ($root in Get-SteamRoots) {
        $guesses.Add((Join-Path $root 'steamapps\common\Pillars of Eternity'))
    }

    foreach ($g in @(
        'C:\Program Files (x86)\Steam\steamapps\common\Pillars of Eternity',
        'C:\Program Files\Steam\steamapps\common\Pillars of Eternity',
        'D:\SteamLibrary\steamapps\common\Pillars of Eternity',
        'E:\SteamLibrary\steamapps\common\Pillars of Eternity'
    )) {
        $guesses.Add($g)
    }

    return $guesses.ToArray() | Where-Object { $_ } | Select-Object -Unique
}

function Find-GameDir {
    foreach ($g in Get-CandidateGameDirs) {
        if (Test-Path (Join-Path $g 'PillarsOfEternity_Data\Managed\Assembly-CSharp.dll')) { return $g }
    }
    return $null
}

function Test-GameDir([string]$dir) {
    if ([string]::IsNullOrWhiteSpace($dir)) {
        return $false
    }
    return Test-Path -LiteralPath (Join-Path $dir 'PillarsOfEternity_Data\Managed\Assembly-CSharp.dll')
}

# Be forgiving if the user points at the game folder, the exe, PillarsOfEternity_Data,
# Managed, or Assembly-CSharp.dll. Walk upward until we reach the game root.
function Resolve-GameDir([string]$dir) {
    $try = Normalize-PathInput $dir
    if ([string]::IsNullOrWhiteSpace($try)) {
        return $dir
    }

    try {
        $leaf = Split-Path -Leaf $try
        if ($leaf -ieq 'Assembly-CSharp.dll' -or $leaf -ieq 'PillarsOfEternity.exe') {
            $try = Split-Path -Parent $try
        }
        if (Test-Path -LiteralPath $try) {
            $try = (Get-Item -LiteralPath $try).FullName
        } else {
            $try = [System.IO.Path]::GetFullPath($try)
        }
    } catch {
        return $dir
    }

    while ($try -and -not (Test-GameDir $try)) {
        $parent = Split-Path $try -Parent
        if ([string]::IsNullOrEmpty($parent) -or $parent -eq $try) { break }
        $try = $parent
    }
    if (Test-GameDir $try) { return $try }
    return $dir
}

# Resolve the game directory: explicit -GameDir, then auto-detect, then prompt the user.
if ($RemainingArgs -and $RemainingArgs.Count -gt 0) {
    # If someone typed an unquoted path with spaces after -GameDir, PowerShell may hand us
    # the split-off pieces here. Rejoin them and let Resolve-GameDir validate the result.
    $GameDir = (($GameDir, $RemainingArgs) | Where-Object { $_ }) -join ' '
}
if ($GameDir) { $GameDir = Resolve-GameDir $GameDir }
if (-not (Test-GameDir $GameDir)) {
    $auto = Find-GameDir
    if (Test-GameDir $auto) { $GameDir = $auto }
}
if (-not (Test-GameDir $GameDir)) {
    Write-Host "Could not find your Pillars of Eternity installation automatically." -ForegroundColor Yellow
    Write-Host "Paste the folder that contains 'PillarsOfEternity.exe' or 'PillarsOfEternity_Data'." -ForegroundColor DarkGray
    Write-Host "You can also paste a path to PillarsOfEternity.exe, PillarsOfEternity_Data, Managed, or Assembly-CSharp.dll." -ForegroundColor DarkGray
    Write-Host "Quotes are optional here; paths with spaces and parentheses are OK." -ForegroundColor DarkGray
    Write-Host "Example: C:\Program Files (x86)\Steam\steamapps\common\Pillars of Eternity" -ForegroundColor DarkGray
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        $entry = Read-Host "Pillars of Eternity install path (leave blank to cancel)"
        if ([string]::IsNullOrWhiteSpace($entry)) { throw "Installation cancelled." }
        $candidate = Resolve-GameDir $entry
        if (Test-GameDir $candidate) { $GameDir = $candidate; break }
        Write-Host "I could not find PillarsOfEternity_Data\Managed\Assembly-CSharp.dll from that path." -ForegroundColor Yellow
        Write-Host "Try the main game folder, not the release zip folder. For Steam, it usually ends in '\steamapps\common\Pillars of Eternity'." -ForegroundColor DarkGray
    }
    if (-not (Test-GameDir $GameDir)) { throw "Could not locate the game after several attempts." }
}

Write-Host "Game folder: $GameDir" -ForegroundColor DarkGray

$managed  = Join-Path $GameDir 'PillarsOfEternity_Data\Managed'
$asmPath  = Join-Path $managed 'Assembly-CSharp.dll'
$sidecarSrc = Join-Path $here 'LoomReapingKnivesModal.dll'
$cecilPath  = Join-Path $here 'Mono.Cecil.dll'

foreach ($p in @($asmPath, $sidecarSrc, $cecilPath)) {
    if (-not (Test-Path $p)) { throw "Required file not found: $p" }
}

# Refuse to run while the game holds the assembly open.
$proc = Get-Process -Name 'PillarsOfEternity*' -ErrorAction SilentlyContinue
if ($proc) { throw "Pillars of Eternity is running (pid $($proc.Id)). Close it and re-run." }

# 1. Install the sidecar DLL.
$sidecarDst = Join-Path $managed 'LoomReapingKnivesModal.dll'
Copy-Item -LiteralPath $sidecarSrc -Destination $sidecarDst -Force
Write-Host "Installed sidecar -> $sidecarDst" -ForegroundColor Green

# 2. Back up the original assembly once.
$backup = "$asmPath.reaperstance-backup"
if (-not (Test-Path $backup)) {
    Copy-Item -LiteralPath $asmPath -Destination $backup -Force
    Write-Host "Backed up Assembly-CSharp.dll -> $backup" -ForegroundColor Green
} else {
    Write-Host "Backup already exists: $backup" -ForegroundColor DarkGray
}

# 3. Inject the hook with Mono.Cecil.
Add-Type -Path $cecilPath

$resolver = New-Object Mono.Cecil.DefaultAssemblyResolver
$resolver.AddSearchDirectory($managed)

$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadWrite = $false
$rp.InMemory  = $true
$rp.AssemblyResolver = $resolver

$module = [Mono.Cecil.ModuleDefinition]::ReadModule($asmPath, $rp)
try {
    if ($module.AssemblyReferences | Where-Object { $_.Name -eq 'LoomReapingKnivesModal' }) {
        Write-Host "Already patched (sidecar reference present). Nothing to do." -ForegroundColor Yellow
        return
    }

    $gameState = $module.Types | Where-Object { $_.Name -eq 'GameState' } | Select-Object -First 1
    if (-not $gameState) { throw "Could not find type GameState in Assembly-CSharp.dll." }

    $update = $gameState.Methods | Where-Object {
        $_.Name -eq 'Update' -and -not $_.IsStatic -and -not $_.HasParameters -and $_.HasBody
    } | Select-Object -First 1
    if (-not $update) { throw "Could not find GameState.Update()." }

    $sidecar   = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($sidecarSrc)
    $bootstrap = $sidecar.MainModule.Types | Where-Object { $_.FullName -eq 'LoomReapingKnivesModal.Bootstrap' } | Select-Object -First 1
    if (-not $bootstrap) { throw "Bootstrap type not found in sidecar." }
    $tick = $bootstrap.Methods | Where-Object { $_.Name -eq 'Tick' -and $_.IsStatic -and -not $_.HasParameters } | Select-Object -First 1
    if (-not $tick) { throw "Bootstrap.Tick() not found in sidecar." }

    $importedTick = $module.ImportReference($tick)
    $il    = $update.Body.GetILProcessor()
    $first = $update.Body.Instructions[0]
    $call  = $il.Create([Mono.Cecil.Cil.OpCodes]::Call, $importedTick)
    $il.InsertBefore($first, $call)

    $anr = New-Object Mono.Cecil.AssemblyNameReference('LoomReapingKnivesModal', $sidecar.Name.Version)
    $module.AssemblyReferences.Add($anr)

    $tmp = "$asmPath.reaperstance-patched"
    if (Test-Path $tmp) { Remove-Item -LiteralPath $tmp -Force }
    $module.Write($tmp)
    $module.Dispose()

    Copy-Item -LiteralPath $tmp -Destination $asmPath -Force
    Remove-Item -LiteralPath $tmp -Force
    Write-Host "Patched GameState.Update -> LoomReapingKnivesModal.Bootstrap.Tick()." -ForegroundColor Green
}
finally {
    if ($module) { $module.Dispose() }
}

Write-Host "`nReaper Stance installed. Launch the game and look for the Reaping Knives modal on a Cipher." -ForegroundColor Cyan
Write-Host "To uninstall: restore '$backup' over Assembly-CSharp.dll and delete LoomReapingKnivesModal.dll." -ForegroundColor DarkGray
