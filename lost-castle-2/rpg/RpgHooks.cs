using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using LC2;

namespace LC2Rpg;

internal static class RpgHooks
{
    static ManualLogSource _log;
    static readonly HashSet<int> _scaledHits = new();
    static readonly HashSet<int> _stealHits = new();
    static int _scaleFrame;
    static int _stealFrame;

    public static void TryPatch(Harmony harmony, ManualLogSource log)
    {
        _log = log;
        Safe(harmony, typeof(Creature), "CheckAndSendKillEvent", 1, nameof(AfterKill), null);
        Safe(harmony, typeof(Creature), "Death_Hp", 1, nameof(AfterDeathHp), null);
        Safe(harmony, typeof(Creature), "Death_Entity", 0, nameof(AfterDeathEntity), null);
        Safe(harmony, typeof(DamageInfo), "CalculateDamage", 2, nameof(AfterDamage), null);
        Safe(harmony, typeof(Creature), "HitActual", 1, nameof(AfterHitActual), null);
        Safe(harmony, typeof(Creature), "BeHitActual", 1, nameof(AfterBeHitActual), null);
        Safe(harmony, typeof(Hero), "SetUIOwner", 0, nameof(AfterHeroReady), null);
        Safe(harmony, typeof(Hero), "InitHeroAsOwner", 0, nameof(AfterHeroReady), null);
        _log.LogInfo("RPG hooks ready.");
    }

    static void Safe(Harmony harmony, Type t, string method, int argc, string postfix, string prefix)
    {
        try
        {
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                         .Where(x => x.Name == method
                                     && x.GetParameters().Length == argc
                                     && !x.IsGenericMethod
                                     && !x.ContainsGenericParameters
                                     && !x.Name.Contains("MethodInfo")))
            {
                HarmonyMethod pre = prefix == null ? null : new HarmonyMethod(typeof(RpgHooks), prefix);
                HarmonyMethod post = postfix == null ? null : new HarmonyMethod(typeof(RpgHooks), postfix);
                harmony.Patch(m, prefix: pre, postfix: post);
                _log.LogInfo("Patched " + t.FullName + "." + m.Name);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning("Skip " + t.Name + "." + method + ": " + ex.Message);
        }
    }

    static void AfterKill(Creature __instance)
    {
        try { RpgState.AddKillXp(__instance); }
        catch (Exception ex) { _log?.LogWarning("Kill XP: " + ex.Message); }
    }

    static void AfterDeathHp(Creature __instance, bool sendEventToAtker)
    {
        try { RpgState.AddKillXp(__instance); }
        catch { }
    }

    static void AfterDeathEntity(Creature __instance)
    {
        try { RpgState.AddKillXp(__instance); }
        catch { }
    }

    static void AfterHeroReady(Hero __instance)
    {
        try { RpgState.SetHero(__instance); }
        catch { }
    }

    static void AfterDamage(DamageInfo __instance, DisposeHitInfo disposeHitInfo)
    {
        try
        {
            if (__instance == null) return;

            var id = disposeHitInfo != null ? disposeHitInfo.ID : 0;
            if (id == 0) id = __instance.GetHashCode();
            var frame = UnityEngine.Time.frameCount;
            if (frame != _scaleFrame)
            {
                _scaledHits.Clear();
                _scaleFrame = frame;
            }
            if (!_scaledHits.Add(id)) return;

            var dmg = __instance.mFinalDamage;
            if (dmg <= 0) return;

            var atker = disposeHitInfo != null ? disposeHitInfo.mAtker : null;
            var be = disposeHitInfo != null ? disposeHitInfo.mBeAtker : null;
            var atkerHero = RpgState.ResolveHero(atker);
            var beHero = RpgState.ResolveHero(be);
            if (RpgState.AtkPts > 0 && RpgState.IsLocalHero(atkerHero))
                dmg *= 1f + RpgState.AtkPts * RpgState.AtkPerPoint;
            if (RpgState.DefPts > 0 && RpgState.IsLocalHero(beHero))
                dmg *= Math.Max(0.25f, 1f - RpgState.DefPts * RpgState.DefPerPoint);

            __instance.mFinalDamage = dmg;
            __instance.mFinalDamage_Clamp = dmg;

            if (RpgState.IsLocalHero(atkerHero))
            {
                RpgState.SetHero(atkerHero);
                RpgState.NoteLocalHit(be);
            }

            TryLifesteal(disposeHitInfo, dmg);
        }
        catch { }
    }

    static void AfterHitActual(Creature __instance, DisposeHitInfo hitInfo)
    {
        try
        {
            if (__instance is Hero hero && RpgState.IsLocalHero(hero))
            {
                RpgState.SetHero(hero);
                if (hitInfo != null)
                    RpgState.NoteLocalHit(hitInfo.mBeAtker);
            }
            var dmg = HitDamage(hitInfo);
            TryLifesteal(hitInfo, dmg);
        }
        catch { }
    }

    static void AfterBeHitActual(Creature __instance, DisposeHitInfo hitInfo)
    {
        try
        {
            var dmg = HitDamage(hitInfo);
            TryLifesteal(hitInfo, dmg);
        }
        catch { }
    }

    static float HitDamage(DisposeHitInfo hitInfo)
    {
        if (hitInfo == null) return 0f;
        var info = hitInfo.mDamageInfo;
        if (info == null) return 0f;
        var dmg = info.mFinalDamage;
        if (dmg <= 0f) dmg = info.mFinalDamage_Clamp;
        return dmg;
    }

    static void TryLifesteal(DisposeHitInfo hitInfo, float dmg)
    {
        if (RpgState.StealPts <= 0 || hitInfo == null || dmg <= 0f) return;

        var id = hitInfo.ID;
        if (id == 0) id = hitInfo.GetHashCode();
        var frame = UnityEngine.Time.frameCount;
        if (frame != _stealFrame)
        {
            _stealHits.Clear();
            _stealFrame = frame;
        }
        if (!_stealHits.Add(id)) return;

        var atker = RpgState.ResolveHero(hitInfo.mAtker)
            ?? RpgState.ResolveHero(hitInfo.mAtkerInHierarchy);
        if (!RpgState.IsLocalHero(atker)) return;
        RpgState.SetHero(atker);
        if (!IsMonsterTarget(hitInfo.mBeAtker)) return;

        var rt = atker.RuntimeData;
        if (rt == null) return;
        float cur = rt.CurHP;
        float max = rt.MaxHP;
        if (max <= 0f || cur >= max - 0.05f) return;

        var raw = dmg * RpgState.StealPts * RpgState.StealPerPoint;
        if (raw <= 0f) return;
        var heal = (float)Math.Round(raw, MidpointRounding.AwayFromZero);
        if (heal < 1f) heal = 1f;
        heal = Math.Min(heal, max - cur);
        if (heal <= 0f) return;

        rt.ChangeCurrentHp(heal, DoInjuryType.Normal, false, false, "lc2rpg");
        try { rt.RecoverFloating(heal); }
        catch { }
        _log?.LogInfo("Lifesteal +" + heal + " from dmg " + dmg + " (" + RpgState.StealPts + "%)");
    }

    static bool IsMonsterTarget(Entity target)
    {
        if (target == null || target is Hero) return false;
        var c = target as Creature;
        if (c == null) return false;
        var rt = c.RuntimeData;
        if (rt == null || rt.IsNotMonster) return false;
        var n = "";
        try { n = ((UnityEngine.Object)(object)c).name ?? ""; }
        catch { }
        if (n.IndexOf("Chest", StringComparison.OrdinalIgnoreCase) >= 0) return false;
        if (n.IndexOf("Barrel", StringComparison.OrdinalIgnoreCase) >= 0) return false;
        if (n.IndexOf("Crate", StringComparison.OrdinalIgnoreCase) >= 0) return false;
        if (n.IndexOf("Box_", StringComparison.OrdinalIgnoreCase) >= 0) return false;
        if (n.IndexOf("Facility", StringComparison.OrdinalIgnoreCase) >= 0) return false;
        if (n.IndexOf("Npc", StringComparison.OrdinalIgnoreCase) >= 0) return false;
        if (n.IndexOf("Scarecrow", StringComparison.OrdinalIgnoreCase) >= 0) return false;
        return true;
    }
}
