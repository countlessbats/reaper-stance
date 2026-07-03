# How Reaper Stance works

This is the design/reverse-engineering write-up. It contains **no game code** — only a
description of the game's observable behaviour and how the mod interoperates with it.

## The hook

Pillars of Eternity 1 ships its game logic in `Assembly-CSharp.dll` (Mono/Unity). Reaper
Stance runs as a **sidecar assembly** and is driven by a single injected call:

```
GameState.Update()  ->  LoomReapingKnivesModal.Bootstrap.Tick()   // injected, first line
```

The injection is done once by the Mono.Cecil patcher in [`patcher/`](../patcher). Every
frame, `Tick()` walks the primary party, finds Ciphers, and ensures each has the modal.

## What Reaping Knives actually is

The interesting part was discovering how the vanilla effect is built. A pile of wrong
assumptions were burned through first; the confirmed model is:

1. **The ability is a friendly-target cast.** `PX2_Reaping_Knives` is a
   `GenericCipherAbility` with an `AttackMelee`. Normally the Cipher casts it on an ally.
2. **The ability's own `StatusEffects` array is empty.** The real enchant lives on the
   ability's `AttackMelee` component, in `AttackBase.StatusEffects`.
3. **That enchant does three things:**
   - summons an **intentionally-invisible** `PX2_Reaping_Knives_Weapon` into the melee
     slots. That weapon prefab is a single object — `[Weapon, AttackMelee, MeshRenderer,
     ...]` with **no `MeshFilter` and no mesh**. It exists only to provide the melee
     attack. It is *supposed* to be invisible.
   - spawns two persistent blade particle systems on the forearm attachment points:
     `PX2_fx_reaping_knives` on `RightForeArm/RightForeArmAttachmentPoint` and
     `PX2_fx_reaping_knives_alt` on `LeftForeArm/LeftForeArmAttachmentPoint`. These are
     the visible "knives". Each is a `StatusEffectParams.OnAppliedVisualEffect` (a looping
     effect the engine tears down when the effect ends).
   - grants Focus on hit.

## Why reconstructing it from scratch fails

The dead-end approach hand-built minimal `SummonWeapon` status effects and pointed them at
a **separately loaded** copy of the weapon prefab. Two problems:

- Loading the weapon prefab on its own via `GameResources.LoadPrefab` returns it with its
  cross-bundle references **stripped** — the material comes back as Unity's default
  `Standard` shader and the VFX references are null. So even the mechanically-correct
  summon produced an object with nothing to draw.
- The blades are **not** part of the weapon at all; they are separate forearm VFX. A
  weapon-only reconstruction can never show them.

The symptom of all this was a Cipher with fists and no particles, even though the focus
mechanic worked.

## What Reaper Stance does instead

`Tick()`:

1. Loads the real `PX2_Reaping_Knives` ability prefab once.
2. Reads the real `StatusEffectParams` off its `AttackMelee` (`AttackBase.StatusEffects`)
   and keeps that array. These are the game's own param objects — the exact ones the real
   cast uses — so their summoned-weapon reference and `OnAppliedVisualEffect` blade VFX are
   intact.
3. For each Cipher, creates a synthetic **combat-only modal** `GenericAbility` and applies
   those harvested params to it (`AddStatusEffect`), targeting the Cipher itself. Focus
   effects are stamped so the Cipher is the recipient.
4. Leaves the modal toggle triggered on by default. Because it's a `CombatOnly` modal, the
   engine applies the effect (and thus the visuals) only in combat and suppresses it out of
   combat, while the toggle stays on.

Reusing the game's own param objects is the whole trick: the summoned weapon and the blade
VFX keep their real asset linkage, so they render exactly like the vanilla cast.

## Debugging aids used along the way

The working build has no logging beyond errors. During development the sidecar dumped, via
`Debug.LogWarning`, things like: the summoned weapon's component tree and renderer state,
the resolved/`NULL` VFX references, and — decisively — a scan of a real cast **recipient's**
hierarchy, which is where the two forearm blade VFX (`PX2_fx_reaping_knives[/ _alt]`) were
finally spotted. If you need to re-diagnose after a game update, re-adding a recipient
hierarchy scan is the fastest way to see what the current assets do.
