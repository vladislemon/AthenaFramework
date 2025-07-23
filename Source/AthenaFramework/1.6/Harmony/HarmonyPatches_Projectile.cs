using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace AthenaFramework
{
    [HarmonyPatch(typeof(Projectile), nameof(Projectile.Launch), new Type[] { typeof(Thing), typeof(Vector3), typeof(LocalTargetInfo), typeof(LocalTargetInfo), typeof(ProjectileHitFlags), typeof(bool), typeof(Thing), typeof(ThingDef) })]
    public static class Projectile_PostLaunch
    {
        public static void Postfix(Projectile __instance, Thing launcher, ref Vector3 origin, LocalTargetInfo usedTarget, LocalTargetInfo intendedTarget, ProjectileHitFlags hitFlags, bool preventFriendlyFire, Thing equipment, ThingDef targetCoverDef)
        {
            if (!AthenaCache.projectileCache.TryGetValue(__instance.thingIDNumber, out List<IProjectile> mods))
            {
                return;
            }

            for (int i = mods.Count - 1; i >= 0; i--)
            {
                IProjectile projectile = mods[i];
                projectile.Launch(launcher, origin, usedTarget, intendedTarget, hitFlags, preventFriendlyFire, equipment, targetCoverDef);
            }
        }
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.CanHit))]
    public static class Projectile_PostCanHit
    {
        static void Postfix(Projectile __instance, Thing thing, ref bool __result)
        {
            if (!AthenaCache.projectileCache.TryGetValue(__instance.thingIDNumber, out List<IProjectile> mods))
            {
                return;
            }

            for (int i = mods.Count - 1; i >= 0; i--)
            {
                IProjectile projectile = mods[i];
                projectile.CanHit(thing, ref __result);
            }
        }
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.Impact))]
    public static class Projectile_PreImpact
    {
        static void Prefix(Projectile __instance, Thing hitThing, ref bool blockedByShield)
        {
            if (AthenaCache.projectileCache.TryGetValue(__instance.thingIDNumber, out List<IProjectile> mods))
            {
                for (int i = mods.Count - 1; i >= 0; i--)
                {
                    IProjectile projectile = mods[i];
                    projectile.Impact(hitThing, ref blockedByShield);
                }
            }
        }
    }

    // Well, as I found out, arrows are bullets, but maybe some projectiles are not,
    // so we might need to patch multiple classes - children of Projectile TODO
    [HarmonyPatch(typeof(Bullet), "Impact")]
    public class Bullet_Impact
    {
        private static MethodInfo _takeDamageMethod = typeof(Thing).Method("TakeDamage");

        private static MethodInfo _applyDamageModifierMethod =
            typeof(AthenaCombatUtility).Method("ApplyProjectileDamageModifier");

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int matchCount = 0;
            CodeInstruction lastLdlocS = null;
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldloc_S)
                {
                    lastLdlocS = instruction;
                }

                if (instruction.opcode == OpCodes.Callvirt && instruction.operand as MethodInfo == _takeDamageMethod)
                {
                    if (lastLdlocS != null)
                    {
                        matchCount++;
                        yield return new CodeInstruction(OpCodes.Pop);
                        yield return new CodeInstruction(OpCodes.Ldarg_0);
                        yield return new CodeInstruction(OpCodes.Ldarg_1);
                        yield return new CodeInstruction(OpCodes.Ldloca_S, lastLdlocS.operand);
                        yield return new CodeInstruction(OpCodes.Call, _applyDamageModifierMethod);
                        yield return new CodeInstruction(OpCodes.Ldloc_S, lastLdlocS.operand);
                    }
                }

                yield return instruction;
            }

            if (matchCount == 0)
            {
                Log.Error("Failed to patch method Bullet.Impact");
            }
        }
    }
}
