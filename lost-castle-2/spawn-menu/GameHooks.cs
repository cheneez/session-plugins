using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

namespace LC2SpawnMenu;

internal static class GameHooks
{
    public static void TryPatch(Harmony harmony, ManualLogSource log)
    {
        PatchGmFlags(harmony, log);
        PatchDuplicatePickup(harmony, log);
        StackPowers.TryPatch(harmony, log);
    }

    static void PatchGmFlags(Harmony harmony, ManualLogSource log)
    {
        try
        {
            var t = TypeFinder.Find("ApplicationCommandReader");
            if (t == null) return;
            foreach (var name in new[] { "get_GMMode", "get_DebugMode" })
            {
                var m = t.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
                if (m == null || m.ReturnType != typeof(bool)) continue;
                harmony.Patch(m, postfix: new HarmonyMethod(typeof(GameHooks), nameof(ForceTrue)));
                log.LogInfo("Patched " + t.FullName + "." + name);
            }
        }
        catch (Exception ex)
        {
            log.LogWarning("GM hook skipped: " + ex.Message);
        }
    }

    static void PatchDuplicatePickup(Harmony harmony, ManualLogSource log)
    {
        try
        {
            var bag = TypeFinder.Find("BagSystem");
            if (bag == null)
            {
                log.LogWarning("BagSystem not found; duplicate treasures still blocked.");
                return;
            }

            foreach (var name in new[] { "GetPickUpType_PassiveProps", "GetPickUpType" })
            {
                foreach (var m in bag.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Where(x => x.Name == name))
                {
                    harmony.Patch(m, postfix: new HarmonyMethod(typeof(GameHooks), nameof(AllowDuplicatePickup)));
                    log.LogInfo("Patched " + m.DeclaringType.FullName + "." + m.Name);
                }
            }

            foreach (var name in new[] { "IsHaveItem", "IsHaveItem2" })
            {
                foreach (var m in bag.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                             .Where(x => x.Name == name && x.ReturnType == typeof(bool) && x.GetParameters().Length >= 1))
                {
                    harmony.Patch(m, postfix: new HarmonyMethod(typeof(GameHooks), nameof(IgnoreOwnedPassive)));
                    log.LogInfo("Patched " + m.DeclaringType.FullName + "." + m.Name);
                }
            }
        }
        catch (Exception ex)
        {
            log.LogWarning("Duplicate-pickup hook skipped: " + ex.Message);
        }
    }

    static void ForceTrue(ref bool __result)
    {
        if (SpawnGui.IsOpen)
            __result = true;
    }

    static void AllowDuplicatePickup(ref object __result)
    {
        if (__result == null) return;
        var s = __result.ToString();
        if (s is "NotSameItem" or "NotEnoughMaxStack")
        {
            try { __result = Enum.Parse(__result.GetType(), "Succeed"); }
            catch { __result = Activator.CreateInstance(__result.GetType()); }
        }
    }

    static void IgnoreOwnedPassive(object[] __args, ref bool __result)
    {
        if (!__result || __args == null || __args.Length == 0 || __args[0] == null) return;
        if (LooksLikePassive(__args[0]))
            __result = false;
    }

    static bool LooksLikePassive(object obj)
    {
        var s = obj.ToString();
        if (s.IndexOf("PassiveProps", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        foreach (var n in new[] { "ItemType", "itemType", "Type" })
        {
            var p = obj.GetType().GetProperty(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var v = p?.GetValue(obj)?.ToString();
            if (v != null && v.IndexOf("PassiveProps", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }
}
