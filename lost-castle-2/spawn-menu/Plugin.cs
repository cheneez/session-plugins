using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace LC2SpawnMenu;

[BepInPlugin(Id, "LC2 Spawn Menu", "1.3.2")]
public class Plugin : BasePlugin
{
    public const string Id = "lc2.spawnmenu";
    internal static Plugin Instance;
    internal static Harmony Harmony;

    public override void Load()
    {
        Instance = this;
        Log.LogInfo("LC2 Spawn Menu 1.3.2 — GM flags only while F9 menu is open.");

        try
        {
            ClassInjector.RegisterTypeInIl2Cpp<SpawnGui>();
        }
        catch (Exception ex)
        {
            Log.LogError("RegisterTypeInIl2Cpp failed: " + ex);
            return;
        }

        AddComponent<SpawnGui>();
        Harmony = new Harmony(Id);
        GameHooks.TryPatch(Harmony, Log);
    }
}
