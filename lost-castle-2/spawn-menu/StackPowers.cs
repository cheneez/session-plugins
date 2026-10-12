using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

namespace LC2SpawnMenu;

internal static class StackPowers
{
    const int MaxStack = 25;
    static ManualLogSource _log;
    static readonly Dictionary<string, int> StackByPower = new();
    static readonly Dictionary<string, float> OrigInterval = new();

    public static void TryPatch(Harmony harmony, ManualLogSource log)
    {
        _log = log;
        Safe("PFTML update", () => PatchNamed(harmony, "PFTML_TimeInterval", "LC2_Power_IUpdate_PFTMLogic_Update", nameof(PrefixTimerDelta), null));
        Safe("PFTML init", () => PatchNamed(harmony, "PFTML_TimeInterval", "Init", null, nameof(AfterIntervalInit)));
        Safe("PassiveProps.BePickedUp", () => PatchAll(harmony, "LC2.PassiveProps", "BePickedUp", nameof(AfterPickedUp)));
        log.LogInfo("Stack patch 1.3.9 ready.");
    }

    static void Safe(string what, Action act)
    {
        try { act(); }
        catch (Exception ex) { _log?.LogWarning("Skip " + what + ": " + ex.Message); }
    }

    static bool CanPatch(MethodInfo m) =>
        m != null && !m.IsGenericMethod && !m.ContainsGenericParameters && !m.Name.Contains("MethodInfo");

    static void PatchAll(Harmony harmony, string typeName, string method, string postfix)
    {
        var t = TypeFinder.Find(typeName);
        if (t == null) { _log?.LogWarning("Type not found: " + typeName); return; }
        var n = 0;
        foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                     .Where(x => CanPatch(x) && x.Name == method))
        {
            harmony.Patch(m, postfix: new HarmonyMethod(typeof(StackPowers), postfix));
            _log?.LogInfo("Patched " + t.FullName + "." + m.Name);
            n++;
        }
        if (n == 0) _log?.LogWarning("No methods: " + t.FullName + "." + method);
    }

    static void PatchNamed(Harmony harmony, string typeName, string method, string prefix, string postfix)
    {
        var t = TypeFinder.Find(typeName);
        if (t == null) { _log?.LogWarning("Type not found: " + typeName); return; }
        foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                     .Where(x => CanPatch(x) && x.Name == method))
        {
            harmony.Patch(m,
                prefix: prefix == null ? null : new HarmonyMethod(typeof(StackPowers), prefix),
                postfix: postfix == null ? null : new HarmonyMethod(typeof(StackPowers), postfix));
            _log?.LogInfo("Patched " + t.FullName + "." + m.Name);
        }
    }

    static void AfterPickedUp(object __instance)
    {
        try
        {
            if (__instance == null) return;
            var rt = Get(__instance, "RuntimeData");
            var entry = Get(rt, "PowerEntry") ?? Get(Get(__instance, "PassivePropPowerBase"), "PowerEntry");
            var key = PowerKey(entry);
            if (string.IsNullOrEmpty(key)) return;
            var n = StackByPower.TryGetValue(key, out var cur) ? cur + 1 : 1;
            n = Math.Clamp(n, 1, MaxStack);
            foreach (var k in KeysFor(key))
                StackByPower[k] = n;
            _log?.LogInfo("Pickup stack " + key + " x" + n);
        }
        catch (Exception ex)
        {
            _log?.LogWarning("AfterPickedUp: " + ex.Message);
        }
    }

    static int _applyLog;

    static void AfterIntervalInit(object __instance, object powerBase)
    {
        try
        {
            var iv = ToFloat(Get(__instance, "_interval"));
            RememberOrig(PowerKey(Get(powerBase, "PowerEntry")), iv);
        }
        catch { }
    }

    static void PrefixTimerDelta(object __instance, ref float __0)
    {
        try
        {
            var pb = Get(__instance, "_powerBase");
            var key = PowerKey(Get(pb, "PowerEntry"));
            var stack = LookupStack(key);
            if (stack <= 1) return;
            __0 *= stack;
            if (_applyLog < 8)
            {
                _applyLog++;
                _log?.LogInfo("Tick x" + stack + " for " + key + " dt=" + __0);
            }
        }
        catch { }
    }

    static void RememberOrig(string key, float iv)
    {
        if (iv < 1f || string.IsNullOrEmpty(key)) return;
        foreach (var k in KeysFor(key))
            if (!OrigInterval.ContainsKey(k))
                OrigInterval[k] = iv;
    }

    static bool TryOrig(string key, out float orig)
    {
        orig = 0f;
        foreach (var k in KeysFor(key))
            if (OrigInterval.TryGetValue(k, out orig)) return true;
        orig = 0f;
        return false;
    }

    static int LookupStack(string key)
    {
        foreach (var k in KeysFor(key))
            if (StackByPower.TryGetValue(k, out var s)) return Math.Clamp(s, 1, MaxStack);
        var n = Norm(key);
        foreach (var kv in StackByPower)
            if (Norm(kv.Key) == n)
                return Math.Clamp(kv.Value, 1, MaxStack);
        return 1;
    }

    static IEnumerable<string> KeysFor(string key)
    {
        if (string.IsNullOrEmpty(key)) yield break;
        yield return key;
        var n = Norm(key);
        if (!string.IsNullOrEmpty(n) && n != key) yield return n;
        yield return "Item#" + n;
    }

    static string Norm(string key)
    {
        if (string.IsNullOrEmpty(key)) return "";
        var i = key.LastIndexOf('#');
        return i >= 0 && i < key.Length - 1 ? key[(i + 1)..] : key;
    }

    static string PowerKey(object entry)
    {
        if (entry == null) return "";
        var asset = Str(Get(entry, "PowerTypeForAsset"));
        if (!string.IsNullOrEmpty(asset) && asset != "null") return asset;
        return Str(Get(entry, "mPowerType"));
    }

    static object Get(object obj, string name)
    {
        if (obj == null) return null;
        const BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        try
        {
            var p = obj.GetType().GetProperty(name, f);
            if (p != null) return p.GetValue(obj);
            var getter = obj.GetType().GetMethod("get_" + name, f) ?? obj.GetType().GetMethod("get_" + name.TrimStart('_'), f);
            if (getter != null && getter.GetParameters().Length == 0) return getter.Invoke(obj, null);
            return obj.GetType().GetField(name, f)?.GetValue(obj);
        }
        catch { return null; }
    }

    static void Set(object obj, string name, object value)
    {
        if (obj == null) return;
        const BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        try
        {
            var p = obj.GetType().GetProperty(name, f);
            if (p != null && p.CanWrite) { p.SetValue(obj, Convert.ChangeType(value, p.PropertyType)); return; }
            var field = obj.GetType().GetField(name, f);
            if (field != null) field.SetValue(obj, Convert.ChangeType(value, field.FieldType));
        }
        catch { }
    }

    static object Invoke(object obj, string name, params object[] args)
    {
        if (obj == null) return null;
        const BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        args ??= Array.Empty<object>();
        try
        {
            foreach (var m in obj.GetType().GetMethods(f).Where(x => x.Name == name && x.GetParameters().Length == args.Length && !x.IsGenericMethod))
            {
                try { return m.Invoke(obj, args); }
                catch { }
            }
        }
        catch { }
        return null;
    }

    static string Str(object o) => o?.ToString() ?? "";
    static int ToInt(object o)
    {
        try { return o == null ? 0 : Convert.ToInt32(o); }
        catch { return 0; }
    }
    static float ToFloat(object o)
    {
        try { return o == null ? 0f : Convert.ToSingle(o); }
        catch { return 0f; }
    }
}
