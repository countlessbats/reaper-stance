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
    [string]$GameDir
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

function Find-GameDir {
    $guesses = @(
        'C:\Program Files (x86)\Steam\steamapps\common\Pillars of Eternity',
        'C:\Program Files\Steam\steamapps\common\Pillars of Eternity',
        'D:\SteamLibrary\steamapps\common\Pillars of Eternity',
        'E:\SteamLibrary\steamapps\common\Pillars of Eternity'
    )
    foreach ($g in $guesses) {
        if (Test-Path (Join-Path $g 'PillarsOfEternity_Data\Managed\Assembly-CSharp.dll')) { return $g }
    }
    return $null
}

if (-not $GameDir) { $GameDir = Find-GameDir }
if (-not $GameDir) {
    throw "Could not auto-detect the game. Pass -GameDir '<path to Pillars of Eternity>'."
}

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
