using UnityEngine;

namespace LC2Rpg;

public class RpgGui : MonoBehaviour
{
    public RpgGui(IntPtr ptr) : base(ptr) { }

    GUIStyle _title;
    GUIStyle _btn;
    GUIStyle _hud;
    GUIStyle _lv;
    GUIStyle _line;
    GUIStyle _hint;
    GUIStyle _barFill;
    GUIStyle _skillTitle;
    GUIStyle _skillSub;
    GUIStyle _skillName;
    GUIStyle _skillLv;
    GUIStyle _skillBonus;
    GUIStyle _skillBtn;
    GUIStyle _iconMark;
    bool _styles;

    void Update()
    {
        try { RpgState.TickApply(); }
        catch { }
    }

    void OnGUI()
    {
        try
        {
            GUI.color = Color.white;
            GUI.contentColor = Color.white;
            GUI.backgroundColor = Color.white;
            EnsureStyles();
            var e = Event.current;
            if (e != null && e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.L)
                {
                    e.Use();
                    RpgState.ToggleMenu();
                }
                else if (e.keyCode == KeyCode.F8)
                {
                    e.Use();
                    RpgState.DebugOpen = !RpgState.DebugOpen;
                }
            }

            DrawHud();
            if (RpgState.DebugOpen)
                GUI.Window(92012, new Rect(20f, 90f, 420f, 220f), (GUI.WindowFunction)DrawDebug, "RPG Debug  (F8)");
            if (RpgState.MenuOpen)
                DrawSkillPanel();
        }
        catch { }
        finally
        {
            GUI.color = Color.white;
            GUI.contentColor = Color.white;
            GUI.backgroundColor = Color.white;
        }
    }

    void DrawHud()
    {
        var need = RpgState.XpToNext();
        var xp = RpgState.Xp;
        var panelW = 520f;
        var extra = RpgState.Unspent > 0 && !RpgState.MenuOpen ? 22f : 0f;
        var panelH = 118f + extra;
        var x = (Screen.width - panelW) / 2f;
        var y = 8f;
        var oldBg = GUI.backgroundColor;

        GUI.backgroundColor = Color.white;
        GUI.Box(new Rect(x - 2f, y - 2f, panelW + 4f, panelH + 4f), "");
        GUI.backgroundColor = Color.black;
        GUI.Box(new Rect(x, y, panelW, panelH), "");

        _lv.normal.textColor = Color.yellow;
        GUI.Label(new Rect(x, y + 2f, panelW, 24f),
            "Lv." + RpgState.Level + "     经验  " + xp + " / " + need, _lv);

        var barW = 400f;
        var barH = 16f;
        var barX = x + (panelW - barW) / 2f;
        var barY = y + 30f;
        GUI.backgroundColor = Color.white;
        GUI.Box(new Rect(barX - 2f, barY - 2f, barW + 4f, barH + 4f), "");
        GUI.backgroundColor = Color.black;
        GUI.Box(new Rect(barX, barY, barW, barH), "");

        var pct = need <= 0 ? 0f : Mathf.Clamp01(xp / (float)need);
        if (pct > 0f)
        {
            var fillW = Mathf.Max(6f, barW * pct);
            GUI.backgroundColor = Color.yellow;
            GUI.Box(new Rect(barX, barY, fillW, barH), "", _barFill);
        }

        _line.normal.textColor = Color.white;
        GUI.Label(new Rect(x, y + 50f, panelW, 20f),
            "攻击 " + RpgState.AtkPts
            + "   生命 " + RpgState.HpPts
            + "   防御 " + RpgState.DefPts
            + "   吸血 " + RpgState.StealPts + "%", _line);
        GUI.Label(new Rect(x, y + 72f, panelW, 22f),
            "攻速 " + RpgState.AspdPts
            + "   移速 " + RpgState.MovePts, _line);

        if (RpgState.Unspent > 0 && !RpgState.MenuOpen)
        {
            _hint.normal.textColor = Color.yellow;
            GUI.Label(new Rect(x, y + 96f, panelW, 20f),
                "未使用技能点 " + RpgState.Unspent + "   按 L 打开/关闭", _hint);
        }

        GUI.backgroundColor = oldBg;
    }

    void DrawSkillPanel()
    {
        var w = 520f;
        var h = 612f;
        var x = (Screen.width - w) / 2f;
        var y = (Screen.height - h) / 2f;
        var oldBg = GUI.backgroundColor;
        var gold = new Color(1f, 0.88f, 0.35f, 1f);
        var goldDim = new Color(0.75f, 0.58f, 0.18f, 1f);
        var panel = new Color(0.28f, 0.26f, 0.24f, 1f);
        var rowBg = new Color(0.42f, 0.40f, 0.38f, 1f);

        GUI.backgroundColor = gold;
        GUI.Box(new Rect(x - 6f, y - 6f, w + 12f, h + 12f), "");
        GUI.backgroundColor = goldDim;
        GUI.Box(new Rect(x - 3f, y - 3f, w + 6f, h + 6f), "");
        GUI.backgroundColor = panel;
        GUI.Box(new Rect(x, y, w, h), "");

        SetBright(_skillTitle, gold);
        GUI.Label(new Rect(x, y + 18f, w, 40f), "LEVEL UP  升级", _skillTitle);

        GUI.backgroundColor = goldDim;
        GUI.Box(new Rect(x + 90f, y + 66f, w - 180f, 2f), "");
        SetBright(_skillSub, gold);
        GUI.Label(new Rect(x, y + 58f, w, 28f), "获得  " + RpgState.Unspent + "  点属性点", _skillSub);

        var rowY = y + 104f;
        var rowH = 72f;
        var gap = 8f;
        DrawSkillRow(x + 28f, rowY, w - 56f, rowH, rowBg, new Color(0.95f, 0.28f, 0.22f), "攻击", RpgState.AtkPts, "+5% 伤害", 0);
        rowY += rowH + gap;
        DrawSkillRow(x + 28f, rowY, w - 56f, rowH, rowBg, new Color(0.35f, 0.85f, 0.40f), "生命", RpgState.HpPts, "+20 最大HP", 1);
        rowY += rowH + gap;
        DrawSkillRow(x + 28f, rowY, w - 56f, rowH, rowBg, new Color(0.35f, 0.70f, 1f), "防御", RpgState.DefPts, "+3% 伤害减免", 2);
        rowY += rowH + gap;
        DrawSkillRow(x + 28f, rowY, w - 56f, rowH, rowBg, new Color(0.72f, 0.38f, 0.95f), "吸血", RpgState.StealPts, "+1% 伤害回复", 3);
        rowY += rowH + gap;
        DrawSkillRow(x + 28f, rowY, w - 56f, rowH, rowBg, new Color(1f, 0.82f, 0.20f), "攻速", RpgState.AspdPts, "+5% 攻击速度", 4);
        rowY += rowH + gap;
        DrawSkillRow(x + 28f, rowY, w - 56f, rowH, rowBg, new Color(0.25f, 0.90f, 0.90f), "移速", RpgState.MovePts, "+2.5% 移动速度", 5);

        SetBright(_hint, Color.white);
        GUI.Label(new Rect(x, y + h - 36f, w, 22f), "点击一项升级  ·  再按 L 关闭", _hint);

        GUI.backgroundColor = oldBg;
    }

    void DrawSkillRow(float x, float y, float w, float h, Color rowBg, Color accent, string name, int lv, string bonus, int which)
    {
        var oldBg = GUI.backgroundColor;
        GUI.backgroundColor = rowBg;
        if (GUI.Button(new Rect(x, y, w, h), "", _skillBtn))
            RpgState.Spend(which);

        GUI.backgroundColor = accent;
        GUI.Box(new Rect(x, y, 10f, h), "", _barFill);
        var icon = new Rect(x + 22f, y + 14f, 44f, 44f);
        GUI.Box(icon, "", _barFill);

        SetBright(_iconMark, Color.white);
        GUI.Label(icon, name.Substring(0, 1), _iconMark);

        SetBright(_skillName, Color.white);
        GUI.Label(new Rect(x + 78f, y, 90f, h), name, _skillName);
        SetBright(_skillLv, Color.white);
        GUI.Label(new Rect(x + 168f, y, 90f, h), "Lv. " + lv, _skillLv);
        SetBright(_skillBonus, new Color(1f, 0.92f, 0.40f));
        GUI.Label(new Rect(x + 250f, y, w - 266f, h), bonus, _skillBonus);
        GUI.backgroundColor = oldBg;
    }

    public void DrawDebug(int id)
    {
        GUILayout.Label("Lv." + RpgState.Level + "  XP " + RpgState.Xp + "/" + RpgState.XpToNext() + "  unused " + RpgState.Unspent, _hud);
        GUILayout.Space(8);
        if (GUILayout.Button("+1 Level and +1 skill point", _btn))
            RpgState.DebugAddLevel();
        GUILayout.Space(8);
        if (GUILayout.Button("Reset level and skill points", _btn))
            RpgState.DebugReset();
        GUI.DragWindow();
    }

    void EnsureStyles()
    {
        if (_styles) return;
        _styles = true;
        _title = new GUIStyle(GUI.skin.label)
        {
            fontSize = 22,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        _btn = new GUIStyle(GUI.skin.button)
        {
            fontSize = 15,
            fixedHeight = 42
        };
        _hud = new GUIStyle(GUI.skin.label) { fontSize = 14 };
        _lv = new GUIStyle(GUI.skin.label)
        {
            fontSize = 18,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        _line = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            alignment = TextAnchor.MiddleCenter
        };
        _barFill = new GUIStyle(GUI.skin.button)
        {
            border = new RectOffset(2, 2, 2, 2),
            padding = new RectOffset(0, 0, 0, 0),
            overflow = new RectOffset(0, 0, 0, 0),
            alignment = TextAnchor.MiddleCenter
        };
        _hint = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            alignment = TextAnchor.MiddleCenter
        };
        _skillTitle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 28,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        _skillSub = new GUIStyle(GUI.skin.label)
        {
            fontSize = 16,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        _skillName = new GUIStyle(GUI.skin.label)
        {
            fontSize = 20,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft
        };
        _skillLv = new GUIStyle(GUI.skin.label)
        {
            fontSize = 16,
            alignment = TextAnchor.MiddleLeft
        };
        _skillBonus = new GUIStyle(GUI.skin.label)
        {
            fontSize = 16,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleRight
        };
        _skillBtn = new GUIStyle(GUI.skin.button)
        {
            border = new RectOffset(4, 4, 4, 4),
            padding = new RectOffset(0, 0, 0, 0)
        };
        _iconMark = new GUIStyle(GUI.skin.label)
        {
            fontSize = 22,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
    }

    static void SetBright(GUIStyle s, Color c)
    {
        s.normal.textColor = c;
        s.hover.textColor = c;
        s.active.textColor = c;
        s.focused.textColor = c;
        s.onNormal.textColor = c;
        s.onHover.textColor = c;
        s.onActive.textColor = c;
    }
}
