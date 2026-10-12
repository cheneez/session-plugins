using System.Reflection;
using System.Text.Json;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace LC2SpawnMenu;

internal sealed class CatalogItem
{
    public string category { get; set; }
    public string id { get; set; }
    public string manual { get; set; }
    public string name { get; set; }

    public string family { get; set; }
    public string type { get; set; }
    public string quality { get; set; }
    public int rank { get; set; }
}

internal static class ItemCatalog
{
    public static List<CatalogItem> Items { get; private set; } = new();

    public static void Load()
    {
        try
        {
            var dir = Path.GetDirectoryName(typeof(Plugin).Assembly.Location);
            var path = Path.Combine(dir ?? ".", "items.json");
            if (!File.Exists(path))
            {
                Plugin.Instance?.Log.LogWarning("items.json missing at " + path);
                return;
            }
            Items = JsonSerializer.Deserialize<List<CatalogItem>>(File.ReadAllText(path)) ?? new();
            foreach (var item in Items)
                ItemClassify.Apply(item);
            Plugin.Instance?.Log.LogInfo("Loaded " + Items.Count + " catalog entries.");
        }
        catch (Exception ex)
        {
            Plugin.Instance?.Log.LogError("Catalog load failed: " + ex);
        }
    }
}

internal static class Spawner
{
    public static string LastStatus = "Ready. Press Spawn in camp or a dungeon.";

    static readonly string[] SpawnMethods =
    {
        "Editor_DropItem_Rpc",
        "PickUp_Rpc",
        "PickUpPassiveProps_Rpc",
        "ChangeItem_ServerRpc",
        "ChangeWeapon_ServerRpc",
        "ChangeArmor_ServerRpc",
        "DropItem",
        "DropItemByManualID",
        "CreateItemObjectAndDrop"
    };

    public static bool Spawn(CatalogItem item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.id))
        {
            LastStatus = "No item selected.";
            return false;
        }

        var ids = new[] { item.id, item.manual }.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToArray();
        var errors = new List<string>();

        var gmType = TypeFinder.Find("GMTools");
        var gm = FindInstance(gmType);
        var playerId = GetPlayerId(gmType, gm, out var pos);

        foreach (var id in ids)
        {
            if (TryDropViaItemMgr(id, pos, playerId, out var dropErr))
            {
                LastStatus = "OK: " + item.name + " via ItemMgr.CreateItemObjectAndDrop";
                Plugin.Instance?.Log.LogInfo(LastStatus);
                return true;
            }
            if (!string.IsNullOrEmpty(dropErr))
                errors.Add(dropErr);
        }

        foreach (var typeName in new[] { "GMTools", "HTIngameDebugConsole", "ItemMgr" })
        {
            var t = TypeFinder.Find(typeName);
            if (t == null)
            {
                errors.Add(typeName + " type missing");
                continue;
            }
            var inst = typeName == "GMTools" ? gm : FindInstance(t);
            foreach (var method in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            {
                if (!SpawnMethods.Contains(method.Name)) continue;
                if (!method.IsStatic && inst == null)
                {
                    errors.Add(typeName + "." + method.Name + " needs instance");
                    continue;
                }
                foreach (var id in ids)
                {
                    if (TryInvoke(method, inst, id, pos, playerId, item, out var err))
                    {
                        LastStatus = "OK: " + item.name + " via " + typeName + "." + method.Name;
                        Plugin.Instance?.Log.LogInfo(LastStatus);
                        return true;
                    }
                    if (!string.IsNullOrEmpty(err))
                        errors.Add(typeName + "." + method.Name + ": " + err);
                }
            }
        }

        LastStatus = "Spawn failed for " + item.id + ". " + string.Join(" | ", errors.Take(4));
        Plugin.Instance?.Log.LogWarning(LastStatus);
        return false;
    }

    static bool TryDropViaItemMgr(string id, Vector3 pos, ulong playerId, out string err)
    {
        err = null;
        try
        {
            var itemData = GetItemData(id);
            if (itemData == null) { err = "No ItemData for " + id; return false; }

            var itemMgr = FindInstance(TypeFinder.Find("ItemMgr"));
            if (itemMgr == null) { err = "ItemMgr missing"; return false; }
            var drop = itemMgr.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "CreateItemObjectAndDrop" && m.GetParameters().Length == 7
                    && m.GetParameters()[0].ParameterType.Name == "ItemData");
            if (drop == null) { err = "CreateItemObjectAndDrop(ItemData) missing"; return false; }

            var ps = drop.GetParameters();
            var ownerType = ps[2].ParameterType.IsEnum ? Activator.CreateInstance(ps[2].ParameterType) : null;
            drop.Invoke(itemMgr, new object[] { itemData, pos, ownerType, playerId, Vector3.zero, 0f, 2f });
            return true;
        }
        catch (Exception ex)
        {
            var inner = (ex as TargetInvocationException)?.InnerException ?? ex;
            err = "ItemMgr: " + inner.GetType().Name + ": " + inner.Message;
            return false;
        }
    }

    internal static object GetItemData(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var dataMgr = FindInstance(TypeFinder.Find("EntityDataAssetMgr"));
        if (dataMgr == null) return null;
        var get = dataMgr.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(m => m.Name == "GetItemDataByID" && m.GetParameters().Length == 1);
        var itemData = get?.Invoke(dataMgr, new object[] { id });
        if (itemData == null)
        {
            var byManual = dataMgr.GetType().GetMethod("GetItemDataAssetByManualID", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            itemData = byManual?.Invoke(dataMgr, new object[] { id });
        }
        if (itemData == null)
        {
            var byName = dataMgr.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "GetItemDataAssetByName" && m.GetParameters().Length == 1);
            itemData = byName?.Invoke(dataMgr, new object[] { id });
        }
        return itemData;
    }

    internal static object FindInstance(Type t)
    {
        if (t == null) return null;
        var inst = TypeFinder.GetSingleton(t);
        if (inst != null) return inst;
        try
        {
            var all = Resources.FindObjectsOfTypeAll(Il2CppType.From(t));
            if (all != null && all.Length > 0) return all[0];
        }
        catch (Exception ex)
        {
            Plugin.Instance?.Log.LogWarning("FindObjectsOfTypeAll " + t.Name + ": " + ex.Message);
        }
        return null;
    }

    static ulong GetPlayerId(Type gmType, object gm, out Vector3 pos)
    {
        pos = GuessCameraPos();
        try
        {
            var tryGet = gmType?.GetMethod("TryGetLocalHeroForGMTool", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (tryGet != null)
            {
                var args = new object[] { null };
                var ok = tryGet.Invoke(null, args);
                if (Equals(ok, true) && args[0] != null)
                {
                    var hero = args[0];
                    if (hero is Component c && c.transform != null)
                        pos = c.transform.position + Vector3.up * 0.5f;
                    foreach (var n in new[] { "_playerID", "PlayerID", "playerID" })
                    {
                        var p = hero.GetType().GetProperty(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        var v = p?.GetValue(hero);
                        if (v != null) return Convert.ToUInt64(v);
                        var f = hero.GetType().GetField(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        v = f?.GetValue(hero);
                        if (v != null) return Convert.ToUInt64(v);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Instance?.Log.LogWarning("GetPlayerId: " + ex.Message);
        }
        return 0;
    }

    static Vector3 GuessCameraPos()
    {
        try
        {
            var cam = Camera.main;
            if (cam != null) return cam.transform.position + Vector3.up;
        }
        catch { /* ignore */ }
        return Vector3.zero;
    }

    static bool TryInvoke(MethodInfo method, object inst, string id, Vector3 pos, ulong playerId, CatalogItem item, out string err)
    {
        err = null;
        try
        {
            var args = BuildArgs(method.GetParameters(), id, pos, playerId, item);
            if (args == null)
            {
                err = "bad signature";
                return false;
            }
            method.Invoke(method.IsStatic ? null : inst, args);
            return true;
        }
        catch (TargetInvocationException ex)
        {
            err = (ex.InnerException ?? ex).GetType().Name + ": " + (ex.InnerException ?? ex).Message;
            return false;
        }
        catch (Exception ex)
        {
            err = ex.GetType().Name + ": " + ex.Message;
            return false;
        }
    }

    static object[] BuildArgs(ParameterInfo[] ps, string id, Vector3 pos, ulong playerId, CatalogItem item)
    {
        var args = new object[ps.Length];
        int strings = 0;
        for (int i = 0; i < ps.Length; i++)
        {
            var pt = ps[i].ParameterType;
            var name = pt.Name;
            var pname = ps[i].Name ?? "";

            if (name is "String" || pt == typeof(string))
            {
                args[i] = strings++ == 0 ? id : "";
            }
            else if (name is "UInt64" || pt == typeof(ulong))
            {
                args[i] = playerId;
            }
            else if (name is "Int32" || pt == typeof(int))
            {
                args[i] = 1;
            }
            else if (name is "Boolean" || pt == typeof(bool))
            {
                args[i] = pname.IndexOf("discard", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            else if (name is "Single" || pt == typeof(float))
            {
                args[i] = 0f;
            }
            else if (name.Contains("Vector3"))
            {
                args[i] = pos;
            }
            else if (pt.IsEnum)
            {
                args[i] = GuessEnum(pt, pname, item);
            }
            else if (ps[i].HasDefaultValue)
            {
                args[i] = ps[i].DefaultValue;
            }
            else if (!pt.IsValueType)
            {
                args[i] = null;
            }
            else
            {
                return null;
            }
        }
        return args;
    }

    static object GuessEnum(Type enumType, string paramName, CatalogItem item)
    {
        var names = Enum.GetNames(enumType);
        var cat = item?.category ?? "";
        string want = null;
        if (paramName.IndexOf("Weapon", StringComparison.OrdinalIgnoreCase) >= 0)
            want = GuessWeaponType(item?.id);
        else if (cat.Contains("Weapon"))
            want = GuessWeaponType(item?.id);
        else if (cat.Contains("Armor"))
            want = names.FirstOrDefault(n => n.IndexOf("Armor", StringComparison.OrdinalIgnoreCase) >= 0);
        else if (cat.Contains("Treasure") || cat.Contains("Passive"))
            want = names.FirstOrDefault(n => n.IndexOf("Passive", StringComparison.OrdinalIgnoreCase) >= 0);

        if (want != null)
        {
            foreach (var n in names)
                if (string.Equals(n, want, StringComparison.OrdinalIgnoreCase))
                    return Enum.Parse(enumType, n);
        }
        return Activator.CreateInstance(enumType);
    }

    static string GuessWeaponType(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (id.StartsWith("THW")) return "TwoHand";
        if (id.StartsWith("SHW")) return "OneHand";
        if (id.StartsWith("ST")) return "Staff";
        if (id.StartsWith("DB")) return "DualBlades";
        if (id.StartsWith("MS") || id.StartsWith("Gun")) return "Musket";
        if (id.StartsWith("BW") || id.StartsWith("Bow")) return "Bow";
        return null;
    }
}
