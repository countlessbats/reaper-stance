// Reaper Stance — a permanent, combat-only Reaping Knives modal for Ciphers in
// Pillars of Eternity 1 (White March). See README.md and docs/HOW_IT_WORKS.md.
//
// The namespace / assembly is named "LoomReapingKnivesModal" on purpose: it is the
// identifier the Mono.Cecil patcher injects into GameState.Update(), so the sidecar DLL,
// the namespace, and the injected call must all agree. It is internal-only and never
// shown in-game; the mod itself is "Reaper Stance".
using System;
using UnityEngine;

namespace LoomReapingKnivesModal
{
    // Grants every Cipher party member a permanent, combat-only "Reaping Knives" modal.
    //
    // Confirmed by live inspection of a real cast recipient (Aloth): the real Reaping Knives effect
    //   (1) summons an intentionally-invisible PX2_Reaping_Knives_Weapon into the melee slots (it only
    //       provides the AttackMelee; it has no visual of its own), and
    //   (2) spawns two persistent blade particle effects (PX2_fx_reaping_knives /
    //       PX2_fx_reaping_knives_alt) on the RightForeArm/LeftForeArm attachment points, and
    //   (3) makes hits generate focus.
    // All three come from the StatusEffectParams on the ability's AttackMelee (AttackBase.StatusEffects),
    // NOT from the ability's own (empty) StatusEffects array. We harvest those REAL params -- so the
    // blade VFX (each param's OnAppliedVisualEffect) and the real summoned weapon are preserved -- and
    // apply them to the cipher itself via a synthetic combat-only modal. The toggle stays on
    // permanently; the effect (and visuals) apply only in combat and drop when combat ends.
    public static class Bootstrap
    {
        private const string AbilityName = "Loom_Reaping_Knives_Modal";
        private const string SpellPrefabName = "PX2_Reaping_Knives";

        private static GenericAbility s_spellPrefab;
        private static StatusEffectParams[] s_realEffects;
        private static bool s_assetsLoaded;
        private static bool s_missingLogged;

        public static void Tick()
        {
            try
            {
                if (GameState.IsLoading || PartyMemberAI.PartyMembers == null)
                {
                    return;
                }

                EnsureAssets();
                if (s_realEffects == null || s_realEffects.Length == 0)
                {
                    return;
                }

                for (int i = 0; i < PartyMemberAI.PartyMembers.Length; i++)
                {
                    PartyMemberAI partyMember = PartyMemberAI.PartyMembers[i];
                    if (partyMember == null || partyMember.Secondary)
                    {
                        continue;
                    }

                    CharacterStats stats = partyMember.GetComponent<CharacterStats>();
                    if (stats == null || stats.CharacterClass != CharacterStats.Class.Cipher)
                    {
                        continue;
                    }

                    EnsureModal(stats);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[LoomReapingKnivesModal] " + ex);
            }
        }

        private static void EnsureAssets()
        {
            if (s_assetsLoaded)
            {
                return;
            }

            s_assetsLoaded = true;
            s_spellPrefab = GameResources.LoadPrefab<GenericAbility>(SpellPrefabName, false);
            if (s_spellPrefab == null)
            {
                if (!s_missingLogged)
                {
                    s_missingLogged = true;
                    Debug.LogError("[LoomReapingKnivesModal] Could not load " + SpellPrefabName + ".");
                }
                return;
            }

            AttackBase attack = s_spellPrefab.GetComponent<AttackBase>();
            if (attack == null || attack.StatusEffects == null || attack.StatusEffects.Count == 0)
            {
                Debug.LogError("[LoomReapingKnivesModal] Ability has no AttackBase.StatusEffects (attack="
                    + (attack != null) + ").");
                return;
            }

            s_realEffects = attack.StatusEffects.ToArray();
        }

        private static void EnsureModal(CharacterStats stats)
        {
            GenericAbility existing = stats.FindAbilityInstance(AbilityName);
            if (existing != null)
            {
                EnsureRuntimeEffects(existing, stats.gameObject);
                if (GameState.InCombat && existing.UiActivated && !existing.Activated && existing.Ready)
                {
                    existing.Activate(stats.gameObject);
                }
                return;
            }

            GameObject templateObject = new GameObject(AbilityName);
            try
            {
                GenericAbility template = templateObject.AddComponent<GenericAbility>();
                ConfigureAbility(template, stats.gameObject);

                GenericAbility instance = stats.InstantiateAbility(template, GenericAbility.AbilityType.Ability);
                if (instance == null)
                {
                    Debug.LogError("[LoomReapingKnivesModal] Failed to add modal to " + stats.name + ".");
                    return;
                }

                instance.name = AbilityName;
                ConfigureAbility(instance, stats.gameObject);
                instance.ForceInit();
                EnsureRuntimeEffects(instance, stats.gameObject);
                instance.ForceTriggerFromUI();
                if (GameState.InCombat && instance.Ready)
                {
                    instance.Activate(stats.gameObject);
                }
            }
            finally
            {
                UnityEngine.Object.Destroy(templateObject);
            }
        }

        private static void ConfigureAbility(GenericAbility ability, GameObject owner)
        {
            if (s_spellPrefab != null)
            {
                ability.DisplayName = s_spellPrefab.DisplayName;
                ability.Description = s_spellPrefab.Description;
                ability.Icon = s_spellPrefab.Icon;
                ability.VocalizationClass = s_spellPrefab.VocalizationClass;
            }
            else
            {
                ability.OverrideName = "Reaping Knives";
            }

            ability.Owner = owner;
            ability.Cooldown = 0f;
            ability.CooldownType = GenericAbility.CooldownMode.None;
            ability.Passive = false;
            ability.Modal = true;
            ability.CombatOnly = true;
            ability.NonCombatOnly = false;
            ability.HideFromUi = false;
            ability.Grouping = GenericAbility.ActivationGroup.F;
            ability.EffectType = GenericAbility.AbilityType.Ability;
            ability.DurationOverride = 0f;
            ability.AppliedViaMod = true;
            ability.StatusEffects = s_realEffects;
            ability.IsVisibleOnUI = true;
        }

        private static void EnsureRuntimeEffects(GenericAbility ability, GameObject owner)
        {
            if (ability.ActiveStatusEffects == null || s_realEffects == null)
            {
                return;
            }

            for (int i = 0; i < s_realEffects.Length; i++)
            {
                StatusEffectParams param = s_realEffects[i];
                if (param == null || HasEffect(ability, param))
                {
                    continue;
                }
                ability.AddStatusEffect(param, GenericAbility.AbilityType.Ability, 0f);
            }

            StampFocusRecipient(ability, owner);
        }

        private static bool HasEffect(GenericAbility ability, StatusEffectParams param)
        {
            for (int i = 0; i < ability.ActiveStatusEffects.Count; i++)
            {
                StatusEffect effect = ability.ActiveStatusEffects[i];
                if (effect != null && effect.Params == param)
                {
                    return true;
                }
            }
            return false;
        }

        // Reaping Knives routes generated focus to the caster. Applied to the cipher itself, the
        // recipient is the cipher.
        private static void StampFocusRecipient(GenericAbility ability, GameObject recipient)
        {
            if (ability.ActiveStatusEffects == null || recipient == null)
            {
                return;
            }

            for (int i = 0; i < ability.ActiveStatusEffects.Count; i++)
            {
                StatusEffect effect = ability.ActiveStatusEffects[i];
                if (effect != null && effect.Params != null
                    && effect.Params.AffectsStat == StatusEffect.ModifiedStat.GrantFocusToExtraObject)
                {
                    effect.ExtraObject = recipient;
                }
            }
        }
    }
}
