using System.Reflection;

namespace LC2Rpg;

internal static class TypeFinder
{
    static readonly Dictionary<string, Type> _cache = new();

    public static Type Find(string simpleName)
    {
        if (string.IsNullOrEmpty(simpleName)) return null;
        if (_cache.TryGetValue(simpleName, out var cached)) return cached;
        Type fallback = null;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var t = asm.GetType("LC2." + simpleName)
                    ?? asm.GetType("LC2.Power." + simpleName)
                    ?? (simpleName.Contains('.') ? asm.GetType(simpleName) : null);
                if (t != null)
                {
                    _cache[simpleName] = t;
                    return t;
                }
            }
            catch { }
        }
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                foreach (var x in asm.GetTypes())
                {
                    if (x.FullName == "LC2." + simpleName)
                    {
                        _cache[simpleName] = x;
                        return x;
                    }
                    if (fallback == null && x.Name == simpleName && (x.Namespace ?? "").StartsWith("LC2"))
                        fallback = x;
                }
            }
            catch { }
        }
        _cache[simpleName] = fallback;
        return fallback;
    }
}

internal static class Refl
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    const BindingFlags S = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static readonly Dictionary<string, MemberInfo> _members = new();

    static string Key(Type t, string name) => t.TypeHandle.Value.ToString() + "." + name;

    public static object Get(object obj, string name)
    {
        if (obj == null) return null;
        try
        {
            var t = obj.GetType();
            var k = Key(t, name);
            if (!_members.TryGetValue(k, out var mem))
            {
                mem = (MemberInfo)t.GetProperty(name, F)
                    ?? t.GetMethod("get_" + name, F)
                    ?? (MemberInfo)t.GetField(name, F);
                _members[k] = mem;
            }
            if (mem is PropertyInfo p) return p.GetValue(obj);
            if (mem is MethodInfo m && m.GetParameters().Length == 0) return m.Invoke(obj, null);
            if (mem is FieldInfo f) return f.GetValue(obj);
            return null;
        }
        catch { return null; }
    }

    public static void Set(object obj, string name, object value)
    {
        if (obj == null) return;
        try
        {
            var t = obj.GetType();
            var p = t.GetProperty(name, F);
            if (p != null && p.CanWrite)
            {
                p.SetValue(obj, Coerce(value, p.PropertyType));
                return;
            }
            var s = t.GetMethod("set_" + name, F);
            if (s != null && s.GetParameters().Length == 1)
            {
                s.Invoke(obj, new[] { Coerce(value, s.GetParameters()[0].ParameterType) });
                return;
            }
            t.GetField(name, F)?.SetValue(obj, Coerce(value, t.GetField(name, F).FieldType));
        }
        catch { }
    }

    static object Coerce(object value, Type target)
    {
        if (value == null || target.IsInstanceOfType(value)) return value;
        if (target == typeof(float) || target == typeof(double)) return Flt(value);
        if (target == typeof(int)) return Int(value);
        if (target == typeof(bool)) return value is true;
        try { return Convert.ChangeType(value, Nullable.GetUnderlyingType(target) ?? target); }
        catch { return value; }
    }

    public static object Call(object obj, string name, params object[] args)
    {
        if (obj == null) return null;
        args ??= Array.Empty<object>();
        try
        {
            foreach (var m in obj.GetType().GetMethods(F).Where(x => x.Name == name && x.GetParameters().Length == args.Length && !x.IsGenericMethod))
            {
                try { return m.Invoke(obj, args); }
                catch { }
            }
        }
        catch { }
        return null;
    }

    public static bool CallLogged(object obj, string name, BepInEx.Logging.ManualLogSource log, params object[] args)
    {
        if (obj == null) return false;
        args ??= Array.Empty<object>();
        foreach (var m in obj.GetType().GetMethods(F).Where(x => x.Name == name && x.GetParameters().Length == args.Length && !x.IsGenericMethod))
        {
            try
            {
                m.Invoke(obj, args);
                return true;
            }
            catch (Exception ex)
            {
                log?.LogWarning(name + " failed: " + (ex.InnerException ?? ex).Message);
            }
        }
        return false;
    }

    public static float Flt(object o)
    {
        if (o == null) return 0;
        switch (o)
        {
            case float f: return f;
            case double d: return (float)d;
            case int i: return i;
            case uint u: return u;
            case long l: return l;
            case bool b: return b ? 1 : 0;
        }
        try { return Convert.ToSingle(o); }
        catch { }
        try
        {
            var t = o.GetType();
            var decrypted = t.GetMethod("GetDecrypted", S);
            if (decrypted != null && decrypted.GetParameters().Length == 0)
                return Flt(decrypted.Invoke(o, null));
            float.TryParse(o.ToString(), out var v);
            return v;
        }
        catch { return 0; }
    }

    public static int Int(object o) => (int)Math.Round(Flt(o));

    public static bool Flag(object o) => o is true || string.Equals(o?.ToString(), "True", StringComparison.OrdinalIgnoreCase);

    public static bool IsHero(object creature) => creature != null && creature.GetType().Name == "Hero";

    public static bool IsLocalHero(object creature) => IsHero(creature) && !Flag(Get(creature, "IsAIHero"));

    public static int InstanceId(object obj)
    {
        try
        {
            if (obj is UnityEngine.Object u && u) return u.GetInstanceID();
        }
        catch { }
        return obj?.GetHashCode() ?? 0;
    }
}
