using System.Reflection;

namespace LC2SpawnMenu;

internal static class TypeFinder
{
    public static Type Find(string simpleName)
    {
        if (string.IsNullOrEmpty(simpleName)) return null;
        if (simpleName.Contains('.'))
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var exact = asm.GetType(simpleName);
                    if (exact != null) return exact;
                }
                catch { }
            }
        }

        Type fallback = null;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var t = asm.GetType("LC2." + simpleName) ?? asm.GetType("LC2.Power." + simpleName);
                if (t != null) return t;
            }
            catch { }

            try
            {
                foreach (var x in asm.GetTypes())
                {
                    if (x.FullName == "LC2." + simpleName || x.FullName == "LC2.Power." + simpleName)
                        return x;
                    if (fallback == null && x.Name == simpleName && (x.Namespace ?? "").StartsWith("LC2"))
                        fallback = x;
                }
            }
            catch { }
        }
        return fallback;
    }

    public static object GetSingleton(Type t)
    {
        if (t == null) return null;
        const BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        foreach (var n in new[] { "Instance", "instance", "_instance", "Singleton", "Current", "Inst" })
        {
            var p = t.GetProperty(n, f);
            var v = p?.GetValue(null);
            if (v != null) return v;
            var field = t.GetField(n, f);
            v = field?.GetValue(null);
            if (v != null) return v;
        }
        return null;
    }
}
