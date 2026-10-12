using BepInEx.Logging;
using LC2;
using UnityEngine;

namespace LC2Rpg;

internal static class RpgState
{
    public const float AtkPerPoint = 0.05f;
    public const float DefPerPoint = 0.03f;
    public const float HpPerPoint = 20f;
    public const float StealPerPoint = 0.01f;
    public const float AspdPerPoint = 0.05f;
    public const float MovePerPoint = 0.025f;
    const string Src = "lc2rpg";

    public static int Level = 1;
    public static int Xp;
    public static int Unspent;
    public static int AtkPts;
    public static int HpPts;
    public static int DefPts;
    public static int StealPts;
    public static int AspdPts;
    public static int MovePts;
    public static bool MenuOpen;
    public static bool DebugOpen;
    public static float SavedTimeScale = 1f;
    public static CursorLockMode SavedLock;
    public static bool SavedCursor;
    public static Hero LocalHero;

    static ManualLogSource _log;
    static float _syncedHpBonus;
    static readonly HashSet<int> _xpOnce = new();
    static readonly Dictionary<int, float> _localHits = new();
    static float _nextFind;
    public static bool NeedsApply;

    public static void Init(ManualLogSource log) => _log = log;

    public static int XpToNext() => 15 + (Level - 1) * 5;

    public static void SetHero(Hero hero)
    {
        if (!IsLocalHero(hero)) return;
        LocalHero = hero;
        if (HpPts > 0 || AtkPts > 0 || DefPts > 0 || AspdPts > 0 || MovePts > 0 || StealPts > 0)
            ApplyToHero();
    }

    public static Hero GetHero()
    {
        if (HeroAlive(LocalHero) && IsLocalHero(LocalHero))
            return LocalHero;
        var found = FindLocalHero();
        if (found != null)
            LocalHero = found;
        return LocalHero;
    }

    static bool HeroAlive(Hero hero)
    {
        if (hero == null) return false;
        try { return ((UnityEngine.Object)(object)hero) != null; }
        catch { return false; }
    }

    static Hero FindLocalHero()
    {
        try
        {
            var pm = PlayerManager.Instance;
            var h = pm != null && pm.LocalPlayer != null ? pm.LocalPlayer.OwnerHero : null;
            if (HeroAlive(h) && IsLocalHero(h)) return h;
            var c = pm != null && pm.LocalPlayer != null ? pm.LocalPlayer.OwnerCreature : null;
            if (c is Hero h2 && IsLocalHero(h2)) return h2;
        }
        catch { }

        try
        {
            var go = GameObject.Find("LC2_Hero_0");
            if (go != null)
            {
                var h = go.GetComponent<Hero>() ?? go.GetComponentInParent<Hero>() ?? go.GetComponentInChildren<Hero>();
                if (HeroAlive(h) && IsLocalHero(h)) return h;
            }
        }
        catch { }

        try
        {
            foreach (var h in UnityEngine.Object.FindObjectsOfType<Hero>())
            {
                if (HeroAlive(h) && IsLocalHero(h))
                    return h;
            }
        }
        catch { }

        return null;
    }

    public static void TickApply()
    {
        if (!NeedsApply) return;
        if (Time.unscaledTime < _nextFind) return;
        _nextFind = Time.unscaledTime + 0.4f;
        ApplyToHero();
    }

    public static bool IsLocalHero(Hero hero)
    {
        if (!HeroAlive(hero)) return false;
        try
        {
            if (hero.IsAIHero) return false;
        }
        catch { }
        try
        {
            var p = hero.OwnerPlayer ?? hero.OwnerPlayerIncludeMaster;
            if (p != null) return p.IsLocal;
        }
        catch { }
        return true;
    }

    public static bool IsMultiplayer()
    {
        try
        {
            var nm = Unity.Netcode.NetworkManager.Singleton;
            if (nm != null && nm.IsListening)
                return true;
        }
        catch { }
        return false;
    }

    public static Hero ResolveHero(Entity entity)
    {
        if (entity == null) return null;
        if (entity is Hero h) return h;
        var c = entity as Creature;
        if (c == null) return null;
        if (c is Hero h2) return h2;
        try
        {
            if (c.OwnerEntityInHierarchy is Hero oh) return oh;
        }
        catch { }
        try
        {
            var p = c.OwnerPlayer ?? c.OwnerPlayerIncludeMaster;
            if (p != null && p.OwnerHero != null) return p.OwnerHero;
        }
        catch { }
        return null;
    }

    public static Hero GetKiller(Creature dead)
    {
        if (dead == null) return null;
        try
        {
            var info = dead.LastAtkerDisposeHitInfo;
            var fromInfo = ResolveHero(info != null ? info.mAtker : null)
                ?? ResolveHero(info != null ? info.mAtkerInHierarchy : null);
            if (fromInfo != null) return fromInfo;
        }
        catch { }
        return null;
    }

    static bool IsNonEnemy(Creature dead)
    {
        var n = "";
        try { n = ((UnityEngine.Object)(object)dead).name ?? ""; }
        catch { }
        if (n.IndexOf("Chest", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (n.IndexOf("Barrel", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (n.IndexOf("Crate", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (n.IndexOf("Box_", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (n.IndexOf("Facility", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (n.IndexOf("Npc", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (n.IndexOf("Scarecrow", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (n.IndexOf("ThePuppy", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    public static void NoteLocalHit(Entity target)
    {
        var c = target as Creature;
        if (c == null || c is Hero) return;
        try
        {
            var id = ((UnityEngine.Object)(object)c).GetInstanceID();
            if (id != 0) _localHits[id] = Time.time;
        }
        catch { }
    }

    static bool RecentlyHit(Creature dead)
    {
        try
        {
            var id = ((UnityEngine.Object)(object)dead).GetInstanceID();
            if (id == 0) return false;
            if (!_localHits.TryGetValue(id, out var t)) return false;
            return Time.time - t < 12f;
        }
        catch { return false; }
    }

    public static void AddKillXp(Creature dead)
    {
        if (dead == null || dead is Hero) return;
        if (IsNonEnemy(dead)) return;

        var weHit = RecentlyHit(dead);
        var killer = GetKiller(dead);
        if (killer != null && !IsLocalHero(killer))
            return;
        if (killer == null && !weHit && IsMultiplayer())
            return;

        var id = ((UnityEngine.Object)(object)dead).GetInstanceID();
        if (id != 0 && !_xpOnce.Add(id)) return;

        var rt = dead.RuntimeData;

        float maxHp = 0f;
        int atk = 0;
        var special = false;
        try
        {
            if (rt != null)
            {
                maxHp = rt.MaxHP;
                atk = rt.Atk;
                special = rt.IsSpecial;
            }
        }
        catch { }
        var deadName = ((UnityEngine.Object)(object)dead).name ?? "";
        var boss = special && maxHp >= 600
            || deadName.IndexOf("Boss", StringComparison.OrdinalIgnoreCase) >= 0
            || dead.GetType().Name.IndexOf("Boss", StringComparison.OrdinalIgnoreCase) >= 0;

        int xp;
        string kind;
        if (boss)
        {
            xp = 50;
            kind = "boss";
        }
        else if (special || maxHp >= 200 || atk >= 30)
        {
            xp = 5;
            kind = "elite";
        }
        else
        {
            xp = 1;
            kind = "small";
        }

        Xp += xp;
        _log?.LogInfo("XP +" + xp + " (" + kind + ") total=" + Xp + "/" + XpToNext());

        var gained = 0;
        while (Xp >= XpToNext())
        {
            Xp -= XpToNext();
            Level++;
            Unspent++;
            gained++;
        }
        if (gained > 0)
        {
            _log?.LogInfo("Level up x" + gained + " -> Lv." + Level + " skill points=" + Unspent);
            OpenMenu();
        }
    }

    public static void ToggleMenu()
    {
        if (MenuOpen) CloseMenu();
        else OpenMenu();
    }

    public static void OpenMenu()
    {
        if (Unspent <= 0) return;
        if (!MenuOpen)
        {
            try
            {
                SavedTimeScale = Time.timeScale;
                if (SavedTimeScale <= 0.01f) SavedTimeScale = 1f;
                SavedLock = Cursor.lockState;
                SavedCursor = Cursor.visible;
            }
            catch { }
        }
        MenuOpen = true;
        try
        {
            if (!IsMultiplayer())
                Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        catch { }
    }

    public static void CloseMenu()
    {
        MenuOpen = false;
        try
        {
            Time.timeScale = SavedTimeScale > 0.01f ? SavedTimeScale : 1f;
            Cursor.lockState = SavedLock;
            Cursor.visible = SavedCursor;
        }
        catch { }
    }

    public static void Spend(int which)
    {
        if (Unspent <= 0) return;
        Unspent--;
        if (which == 0) AtkPts++;
        else if (which == 1) HpPts++;
        else if (which == 2) DefPts++;
        else if (which == 3) StealPts++;
        else if (which == 4) AspdPts++;
        else MovePts++;
        ApplyToHero();
        CloseMenu();
    }

    public static void DebugAddLevel()
    {
        Level++;
        Unspent++;
        _log?.LogInfo("DEBUG +1 level -> Lv." + Level + " skill points=" + Unspent);
    }

    public static void DebugReset()
    {
        AtkPts = HpPts = DefPts = StealPts = AspdPts = MovePts = 0;
        Unspent = 0;
        Level = 1;
        Xp = 0;
        ApplyToHero();
        _log?.LogInfo("DEBUG reset level and skills");
    }

    public static void ApplyToHero()
    {
        var hero = GetHero();
        if (hero == null || hero.RuntimeData == null)
        {
            NeedsApply = true;
            _log?.LogWarning("Cannot apply skills: no Hero yet.");
            return;
        }

        NeedsApply = false;

        var rt = hero.RuntimeData;
        var wantHp = HpPts * HpPerPoint;
        var wantAtk = AtkPts * 4f;
        var wantDef = DefPts * 2f;
        var wantReduce = DefPts * DefPerPoint;

        SetAttr(rt, StatValueType.MaxHP_Add_0, wantHp, true);
        SetAttr(rt, StatValueType.Atk_Add_0, wantAtk, false);
        SetAttr(rt, StatValueType.Def_Add_final, wantDef, false);
        SetAttr(rt, StatValueType.ReduceDmg, wantReduce, false);
        SetAttr(rt, StatValueType.AtkSpd_Mul_0, AspdPts * AspdPerPoint, false);
        SetAttr(rt, StatValueType.Spd_Mul_0, MovePts * MovePerPoint, false);

        var gainedHp = wantHp - _syncedHpBonus;
        _syncedHpBonus = wantHp;
        if (gainedHp > 0.01f)
            rt.ChangeCurrentHp(gainedHp, DoInjuryType.Normal, false, false, Src);

        float maxHp = rt.MaxHP;
        int atk = rt.Atk;
        _log?.LogInfo("Applied skills HP+" + wantHp + " ATK+" + wantAtk + " DEF+" + wantDef
            + " ASPD+" + (AspdPts * AspdPerPoint) + " SPD+" + (MovePts * MovePerPoint)
            + " => MaxHP=" + maxHp + " Atk=" + atk);
    }

    static void SetAttr(CreatureRuntimeData rt, StatValueType type, float value, bool maxHp)
    {
        try { rt.RemoveAttributeChange(type, Src, true, true); }
        catch { }

        if (Math.Abs(value) < 0.001f) return;

        if (maxHp)
        {
            try { rt.BeforeChangeMaxHP_KeepPercent(); }
            catch { }
        }

        try { rt.AddAttributeChange(type, value, 999999f, Src, true); }
        catch (Exception ex) { _log?.LogWarning("AddAttributeChange " + type + ": " + ex.Message); }

        if (maxHp)
        {
            try { rt.AfterChangeMaxHP_KeepPercent(Src); }
            catch { }
        }
    }
}
