using System.Collections.Generic;
using UnityEngine;

namespace LC2SpawnMenu;

public class SpawnGui : MonoBehaviour
{
    public SpawnGui(IntPtr ptr) : base(ptr) { }

    internal static bool IsOpen;
    bool _open;
    bool _loaded;
    string _search = "";
    string _family = "All";
    string _type = "All";
    int _page;
    Rect _win = new(24, 24, 980, 720);
    const int PerPage = 10;

    void Update()
    {
        if (!_loaded)
        {
            _loaded = true;
            ItemCatalog.Load();
        }
        try
        {
            if (Input.GetKeyDown(KeyCode.F9))
                _open = !_open;
            IsOpen = _open;
        }
        catch { }
    }

    void OnGUI()
    {
        if (!_open) return;
        _win = GUI.Window(92001, _win, (GUI.WindowFunction)DrawWindow, "Lost Castle 2 spawn menu  (F9)  v1.3.1");
    }

    public void DrawWindow(int id)
    {
        GUILayout.Label("Single-player. Pick a type, then Spawn. Sorted best to worst.");
        GUILayout.BeginHorizontal();
        GUILayout.Label("Search", GUILayout.Width(70));
        var s = GUILayout.TextField(_search ?? "");
        if (s != _search) { _search = s; _page = 0; }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("All")) { _family = "All"; _type = "All"; _page = 0; }
        if (GUILayout.Button("Weapon")) { _family = "Weapon"; _type = "All"; _page = 0; }
        if (GUILayout.Button("Armor")) { _family = "Armor"; _type = "All"; _page = 0; }
        if (GUILayout.Button("Treasure")) { _family = "Treasure"; _type = "All"; _page = 0; }
        if (GUILayout.Button("Other")) { _family = "Other"; _type = "All"; _page = 0; }
        GUILayout.EndHorizontal();

        if (_family == "Weapon")
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("All types")) { _type = "All"; _page = 0; }
            if (GUILayout.Button("Two-Hand")) { _type = "Two-Hand"; _page = 0; }
            if (GUILayout.Button("One-Hand")) { _type = "One-Hand"; _page = 0; }
            if (GUILayout.Button("Staff")) { _type = "Staff"; _page = 0; }
            if (GUILayout.Button("Musket")) { _type = "Musket"; _page = 0; }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Dual Blades")) { _type = "Dual Blades"; _page = 0; }
            if (GUILayout.Button("Bow")) { _type = "Bow"; _page = 0; }
            if (GUILayout.Button("Katana")) { _type = "Katana"; _page = 0; }
            GUILayout.EndHorizontal();
        }
        else if (_family == "Armor")
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("All types")) { _type = "All"; _page = 0; }
            if (GUILayout.Button("Cloth")) { _type = "Cloth"; _page = 0; }
            if (GUILayout.Button("Light")) { _type = "Light"; _page = 0; }
            if (GUILayout.Button("Heavy")) { _type = "Heavy"; _page = 0; }
            GUILayout.EndHorizontal();
        }
        else if (_family == "Treasure")
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("All types")) { _type = "All"; _page = 0; }
            if (GUILayout.Button("Red")) { _type = "Red"; _page = 0; }
            if (GUILayout.Button("Green")) { _type = "Green"; _page = 0; }
            if (GUILayout.Button("Blue")) { _type = "Blue"; _page = 0; }
            if (GUILayout.Button("Chaos")) { _type = "Chaos"; _page = 0; }
            if (GUILayout.Button("Cursed")) { _type = "Cursed"; _page = 0; }
            if (GUILayout.Button("Knockoff")) { _type = "Knockoff"; _page = 0; }
            GUILayout.EndHorizontal();
        }

        var q = (_search ?? "").Trim();
        var filtered = new List<CatalogItem>();
        var items = ItemCatalog.Items;
        for (var i = 0; i < items.Count; i++)
        {
            var it = items[i];
            if (_family != "All" && it.family != _family) continue;
            if (_type != "All" && it.type != _type) continue;
            if (q.Length > 0)
            {
                var hit = Contains(it.name, q) || Contains(it.id, q) || Contains(it.manual, q)
                    || Contains(it.type, q) || Contains(it.quality, q);
                if (!hit) continue;
            }
            filtered.Add(it);
        }
        filtered.Sort(BestFirst);

        var pages = filtered.Count == 0 ? 1 : (filtered.Count + PerPage - 1) / PerPage;
        if (_page >= pages) _page = pages - 1;
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("<", GUILayout.Width(40))) _page = _page > 0 ? _page - 1 : 0;
        GUILayout.Label((_page + 1) + " / " + pages + "   " + filtered.Count + " matches   " + _family + " / " + _type);
        if (GUILayout.Button(">", GUILayout.Width(40))) _page = _page < pages - 1 ? _page + 1 : pages - 1;
        GUILayout.EndHorizontal();

        var start = _page * PerPage;
        var end = start + PerPage;
        if (end > filtered.Count) end = filtered.Count;
        for (var n = start; n < end; n++)
        {
            var item = filtered[n];
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Spawn", GUILayout.Width(90)))
            {
                try { Spawner.Spawn(item); }
                catch { }
            }
            GUILayout.Label((item.quality ?? "") + " | " + (item.type ?? "") + " | " + (item.name ?? "") + "  [" + (item.id ?? "") + "]");
            GUILayout.EndHorizontal();
        }

        GUILayout.Label(Spawner.LastStatus);
        GUI.DragWindow();
    }

    static bool Contains(string hay, string needle)
    {
        if (string.IsNullOrEmpty(hay)) return false;
        return hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static int BestFirst(CatalogItem a, CatalogItem b)
    {
        var c = b.rank.CompareTo(a.rank);
        if (c != 0) return c;
        c = string.Compare(a.type, b.type, StringComparison.OrdinalIgnoreCase);
        if (c != 0) return c;
        return string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase);
    }
}
