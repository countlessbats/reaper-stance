# Reaper Stance (for Pillars of Eternity 1)

A small code mod for **Pillars of Eternity: The White March** that gives every Cipher
in your party a permanent, combat-only **Reaping Knives** modal.

Toggle it on once and leave it on. While the Cipher is in combat they gain the real
Reaping Knives enchant — the spectral blades on the forearms, the summoned reaping
weapon, and party Focus generation on hit. When combat ends the visuals switch off and
the toggle stays on, ready for the next fight.

Reaper Stance also hides the Cipher's regular Soul Whip glow while the Reaping Knives
blades are present, so the two weapon effects do not stack on top of each other visually.
It includes the Colorful Reaping Knives visual controls as well: press **F9** in-game to
recolor the blade effect, adjust overall opacity, or use Advanced mode for the individual
blade layers.

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

Requires Pillars of Eternity 1 with **The White March Part II**.

### Option A — Quick install (no compiling) — recommended

1. Download **`ReaperStance-v1.3.0.zip`** from the
   [Releases](https://github.com/countlessbats/reaper-stance/releases) page and extract it.
2. **Close the game.**
3. **Double-click `install.bat`** and approve the administrator prompt.

That's it — no compiler, no .NET SDK, no runtime install, no typing. `install.bat` just runs
the bundled `install.ps1`, which copies the prebuilt sidecar into the game, backs up
`Assembly-CSharp.dll` once, and injects the hook using the bundled (MIT-licensed)
`Mono.Cecil.dll`. It's safe to re-run (it detects an already-patched assembly and does
nothing). It auto-detects a Steam install; if your game is elsewhere, run it from a command
prompt with a path instead:

```bat
install.bat -GameDir "D:\Games\Pillars of Eternity"
```

(If you'd rather not use the `.bat`, you can run the PowerShell installer directly:
`powershell -ExecutionPolicy Bypass -File .\install.ps1 -GameDir "<path>"`.)

4. Launch the game, put a Cipher in the party, and look for the **Reaping Knives** modal on
   the action bar — it defaults to on.

### Option B — Build from source (developers)

Needs the Roslyn C# compiler (`csc.exe`, from Visual Studio / Build Tools) and the .NET SDK
(`dotnet`).

```powershell
# 1. Build + install the sidecar
./build.ps1 -GameDir "E:\SteamLibrary\steamapps\common\Pillars of Eternity"

# 2. Inject the hook (one time). Back up Assembly-CSharp.dll first.
cd patcher
dotnet run -- "<GAME>\PillarsOfEternity_Data\Managed\Assembly-CSharp.dll" `
             "<GAME>\PillarsOfEternity_Data\Managed\LoomReapingKnivesModal.dll"
```

The patcher injects a single call to `LoomReapingKnivesModal.Bootstrap.Tick()` at the top
of `GameState.Update()` and is idempotent (it refuses to patch twice).

---

## Backups & uninstalling

`install.ps1` automatically saves your original assembly to
`Assembly-CSharp.dll.reaperstance-backup` (once). To uninstall:

1. Restore `Assembly-CSharp.dll` from `Assembly-CSharp.dll.reaperstance-backup`.
2. Delete `PillarsOfEternity_Data/Managed/LoomReapingKnivesModal.dll`.

Verifying game files through Steam will also restore the original `Assembly-CSharp.dll`
(but will not remove the sidecar DLL).

---

## Building from source

- `src/ReaperStance.cs` — the modal, Reaping Knives, and Soul Whip behavior.
- `src/ColorfulReapingKnives.cs` — the bundled blade recolor controller. References `Assembly-CSharp.dll`,
  `UnityEngine.dll`, `UnityEngine.CoreModule.dll`, and
  the Unity UI/input/particle modules from `PillarsOfEternity_Data/Managed`.
- `patcher/` — a `net8.0` console app using Mono.Cecil to inject the hook.

See [build.ps1](build.ps1).

---

## Compatibility & caveats

- Built against the retail Steam build of PoE 1 with both expansions. A game patch that
  changes `GameState.Update` or the `PX2_Reaping_Knives` assets could require a rebuild.
- Any mod that also rewrites `Assembly-CSharp.dll` should be applied in a known order;
  re-running this patcher on an already-patched assembly is a no-op.
- Applies to every primary-party Cipher, including companions and the player.
- If the standalone Colorful Reaping Knives mod is installed too, it detects Reaper Stance
  and stays inert. Use Reaper Stance's built-in F9 controls in that setup.

## Version notes

- **v1.3.0**: Bundles Colorful Reaping Knives directly into Reaper Stance. F9 opens the
  recolor overlay; Advanced mode exposes the individual blade layer opacities/colors.
- **v1.2.0**: Reaper Stance now always suppresses the regular Soul Whip glow while
  Reaping Knives blades are active.

---

## License

[MIT](LICENSE). This repository contains only original mod code. It does **not** contain
any Obsidian Entertainment game code or assets — you must own Pillars of Eternity and its
expansions to use it.
