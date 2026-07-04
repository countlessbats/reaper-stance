// Reaper Stance — a permanent, combat-only Reaping Knives modal for Ciphers in
// Pillars of Eternity 1 (White March). See README.md and docs/HOW_IT_WORKS.md.
//
// The namespace / assembly is named "LoomReapingKnivesModal" on purpose: it is the
// identifier the Mono.Cecil patcher injects into GameState.Update(), so the sidecar DLL,
// the namespace, and the injected call must all agree. It is internal-only and never
// shown in-game; the mod itself is "Reaper Stance".
using System;
using System.Collections.Generic;
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

                SoulWhipSuppressor.EnsureSpawned();
                LoomColorfulReapingKnives.ReaperFx.EnsureSpawned();

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

    // Reaping Knives supplies its own forearm blade effects. When Soul Whip is also active,
    // its glow stacks visually on the same character, so keep it hidden while the blades exist.
    public class SoulWhipSuppressor : MonoBehaviour
    {
        private static SoulWhipSuppressor s_instance;
        private readonly HashSet<GameObject> m_hidden = new HashSet<GameObject>();

        public static void EnsureSpawned()
        {
            if (s_instance != null)
            {
                return;
            }

            try
            {
                GameObject go = new GameObject("LoomReaperStanceSoulWhipSuppressor");
                UnityEngine.Object.DontDestroyOnLoad(go);
                s_instance = go.AddComponent<SoulWhipSuppressor>();
            }
            catch (Exception ex)
            {
                Debug.LogError("[LoomReapingKnivesModal] soul whip suppressor spawn failed: " + ex);
            }
        }

        private void LateUpdate()
        {
            if (GameState.IsLoading || PartyMemberAI.PartyMembers == null || !GameState.InCombat)
            {
                RestoreHidden();
                return;
            }

            try
            {
                for (int i = 0; i < PartyMemberAI.PartyMembers.Length; i++)
                {
                    PartyMemberAI pm = PartyMemberAI.PartyMembers[i];
                    if (pm == null || pm.Secondary)
                    {
                        continue;
                    }
                    ProcessCharacter(pm.gameObject);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[LoomReapingKnivesModal] soul whip suppressor process: " + ex);
            }
        }

        private void ProcessCharacter(GameObject owner)
        {
            ParticleSystem[] systems = owner.GetComponentsInChildren<ParticleSystem>(true);
            if (systems.Length == 0)
            {
                return;
            }

            bool bladesPresent = false;
            for (int i = 0; i < systems.Length; i++)
            {
                if (AncestorContains(systems[i].transform, "reaping_knives"))
                {
                    bladesPresent = true;
                    break;
                }
            }

            if (!bladesPresent)
            {
                return;
            }

            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem ps = systems[i];
                if (!AncestorContains(ps.transform, "soul_whip"))
                {
                    continue;
                }

                Transform root = FindEffectRoot(ps.transform, "soul_whip");
                if (root != null && root.gameObject.activeSelf)
                {
                    root.gameObject.SetActive(false);
                    m_hidden.Add(root.gameObject);
                }
            }
        }

        private void RestoreHidden()
        {
            if (m_hidden.Count == 0)
            {
                return;
            }

            foreach (GameObject go in m_hidden)
            {
                if (go != null)
                {
                    go.SetActive(true);
                }
            }
            m_hidden.Clear();
        }

        private static Transform FindEffectRoot(Transform t, string token)
        {
            Transform root = null;
            Transform p = t;
            int guard = 0;
            while (p != null && guard < 16)
            {
                if (p.name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    root = p;
                }
                else if (root != null)
                {
                    break;
                }
                p = p.parent;
                guard++;
            }
            return root;
        }

        private static bool AncestorContains(Transform t, string token)
        {
            Transform p = t;
            int guard = 0;
            while (p != null && guard < 16)
            {
                if (p.name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
                p = p.parent;
                guard++;
            }
            return false;
        }
    }
}
