# Reaper Stance (for Pillars of Eternity 1)

A small code mod for **Pillars of Eternity: The White March** that gives every Cipher
in your party a permanent, combat-only **Reaping Knives** modal.

Toggle it on once and leave it on. While the Cipher is in combat they gain the real
Reaping Knives enchant — the spectral blades on the forearms, the summoned reaping
weapon, and party Focus generation on hit. When combat ends the visuals switch off and
the toggle stays on, ready for the next fight.

Unlike the vanilla ability (which a Cipher casts on a friendly target for a limited
time and a Focus cost), Reaper Stance applies the effect to the Cipher themselves,
permanently, for free.

> Requires the **White March Part II** content (the `PX2_Reaping_Knives` assets).

---

## How it works (short version)

Vanilla Reaping Knives is not stored as a simple buff. The real effect lives as a set of
`StatusEffectParams` on the ability's `AttackMelee` component, and it does three things:

1. summons an intentionally-invisible `PX2_Reaping_Knives_Weapon` into the melee slots
   (it only carries the melee attack — it has no mesh of its own), and
2. spawns two persistent blade particle effects (`PX2_fx_reaping_knives` /
   `PX2_fx_reaping_knives_alt`) on the right/left forearm attachment points, and
3. makes hits generate Focus.

Reaper Stance loads the real ability prefab, **harvests those real params**, and applies
them to each Cipher through a synthetic combat-only modal. Because it reuses the game's
own param objects, the summoned weapon and blade VFX keep their real asset linkage —
which is the part that every from-scratch reconstruction gets wrong (you end up with an
invisible weapon and no blades). See [docs/HOW_IT_WORKS.md](docs/HOW_IT_WORKS.md) for the
full reverse-engineering story.

The mod runs as a **sidecar assembly** that is called once per frame from
`GameState.Update()`. A tiny [Mono.Cecil patcher](patcher/) injects that one call into the
game's `Assembly-CSharp.dll`.

> **Note on the internal name.** The compiled sidecar and injected hook use the identifier
> `LoomReapingKnivesModal` (namespace, DLL name, and patch reference). This is intentional
> and internal-only — it keeps the patch/sidecar consistent. The mod/project is
> "Reaper Stance"; the internal name is not visible in-game.

---

## Installation

You need:

- Pillars of Eternity 1 with **The White March Part II**.
- The .NET Roslyn C# compiler (`csc.exe`, ships with Visual Studio / Build Tools) to build
  the sidecar.
- The .NET SDK (`dotnet`) to build the one-time patcher.

### 1. Build the sidecar

From a shell, with `GAME` set to your install directory
(e.g. `E:\SteamLibrary\steamapps\common\Pillars of Eternity`):

```powershell
# PowerShell
./build.ps1 -GameDir "E:\SteamLibrary\steamapps\common\Pillars of Eternity"
```

`build.ps1` compiles `src/ReaperStance.cs` into
`PillarsOfEternity_Data/Managed/LoomReapingKnivesModal.dll`.

### 2. Patch the game assembly (one time)

```powershell
cd patcher
dotnet run -- "<GAME>\PillarsOfEternity_Data\Managed\Assembly-CSharp.dll" `
             "<GAME>\PillarsOfEternity_Data\Managed\LoomReapingKnivesModal.dll"
```

The patcher **backs up nothing automatically** — make a copy of `Assembly-CSharp.dll`
first (see below). It injects a single call to `LoomReapingKnivesModal.Bootstrap.Tick()`
at the top of `GameState.Update()`, and is idempotent (it refuses to patch twice).

### 3. Play

Launch the game, put a Cipher in the party, and look for the **Reaping Knives** modal on
the action bar — it defaults to on.

---

## Backups & uninstalling

Before patching, copy your game assembly somewhere safe:

```
PillarsOfEternity_Data/Managed/Assembly-CSharp.dll  ->  Assembly-CSharp.dll.backup
```

To uninstall:

1. Restore `Assembly-CSharp.dll` from your backup.
2. Delete `PillarsOfEternity_Data/Managed/LoomReapingKnivesModal.dll`.

Verifying game files through Steam will also restore the original `Assembly-CSharp.dll`
(but will not remove the sidecar DLL).

---

## Building from source

- `src/ReaperStance.cs` — the sidecar. References `Assembly-CSharp.dll`,
  `UnityEngine.dll`, `UnityEngine.CoreModule.dll` from `PillarsOfEternity_Data/Managed`.
- `patcher/` — a `net8.0` console app using Mono.Cecil to inject the hook.

See [build.ps1](build.ps1).

---

## Compatibility & caveats

- Built against the retail Steam build of PoE 1 with both expansions. A game patch that
  changes `GameState.Update` or the `PX2_Reaping_Knives` assets could require a rebuild.
- Any mod that also rewrites `Assembly-CSharp.dll` should be applied in a known order;
  re-running this patcher on an already-patched assembly is a no-op.
- Applies to every primary-party Cipher, including companions and the player.

---

## License

[MIT](LICENSE). This repository contains only original mod code. It does **not** contain
any Obsidian Entertainment game code or assets — you must own Pillars of Eternity and its
expansions to use it.
