using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace LC2SpawnMenu;

[BepInPlugin(Id, "LC2 Treasure Stack", "1.3.9")]
public class TreasureStackPlugin : BasePlugin
{
    public const string Id = "lc2.treasurestack";

    public override void Load()
    {
        Log.LogInfo("LC2 Treasure Stack 1.3.9 loading.");
        var harmony = new Harmony(Id);
        StackPowers.TryPatch(harmony, Log);
    }
}
