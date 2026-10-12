using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;

namespace LC2Rpg;

[BepInPlugin(Id, "LC2 RPG", "1.1.7")]
public class Plugin : BasePlugin
{
    public const string Id = "lc2.rpg";

    public override void Load()
    {
        Log.LogInfo("LC2 RPG 1.1.7 — stop GUI.color from tinting the whole screen.");
        RpgState.Init(Log);
        try
        {
            ClassInjector.RegisterTypeInIl2Cpp<RpgGui>();
        }
        catch (Exception ex)
        {
            Log.LogError("Register RpgGui failed: " + ex);
            return;
        }
        AddComponent<RpgGui>();
        RpgHooks.TryPatch(new Harmony(Id), Log);
    }
}
