using System;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace ShopMod
{
    [BepInPlugin("com.kaboo.crimesim.shop", "Shop", "1.0.0")]
    [BepInDependency("com.kaboo.crimesim.skillcash")]
    public class Plugin : BaseUnityPlugin
    {
        static BepInEx.Logging.ManualLogSource Log;
        static float _nextCheck;

        void Awake()
        {
            Log = Logger;
            var harmony = new Harmony("com.kaboo.crimesim.shop");
            harmony.Patch(AccessTools.Method(typeof(ComputerStore), "RestockItems"),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(RememberFullStock)));
            harmony.Patch(AccessTools.Method(typeof(WorldManager), "AdvanceTime"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(BeforeAdvanceTime)),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterAdvanceTime)));
            harmony.Patch(AccessTools.Method(typeof(Computer), "Update"),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(FixBadStock)));
            harmony.Patch(AccessTools.Method(typeof(FurnitureWebsite), "CheckIfCanBuy"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(RaiseFurnitureLimit)));
            Logger.LogInfo("Shop stock refills every day. Furniture limit raised to " + FurnitureLimit + ".");
        }

        const int FurnitureLimit = 10;

        public static void RaiseFurnitureLimit(Furniture ___selectedFurniture)
        {
            if (___selectedFurniture != null && ___selectedFurniture.maxLimit < FurnitureLimit)
                ___selectedFurniture.maxLimit = FurnitureLimit;
        }

        static ES3Settings Settings()
        {
            return new ES3Settings(ES3.Location.Cache);
        }

        static string FullKey(ShopButton button)
        {
            return button.assignedItem.itemName + "_stockFull";
        }

        static ComputerStore HostStore()
        {
            Computer computer = Computer.Instance;
            if (computer == null || !computer.HasStateAuthority)
                return null;
            return computer.GetComponentInChildren<ComputerStore>(true);
        }

        static int FullStock(ShopButton button, ES3Settings settings)
        {
            string key = FullKey(button);
            if (ES3.KeyExists(key, settings))
                return ES3.Load<int>(key, settings);
            int full = UnityEngine.Random.Range(button.stockMinRandom, button.stockMaxRandom);
            ES3.Save(key, full, settings);
            return full;
        }

        public static void RememberFullStock(ComputerStore __instance)
        {
            try
            {
                ES3Settings settings = Settings();
                foreach (ShopButton button in __instance.shopButtons)
                {
                    if (button != null && button.assignedItem != null)
                        ES3.Save(FullKey(button), button.amountInStock, settings);
                }
            }
            catch (Exception ex)
            {
                Log.LogError("Could not remember stock: " + ex.Message);
            }
        }

        public static void BeforeAdvanceTime(WorldManager __instance, out int __state)
        {
            __state = __instance.currentDay;
        }

        public static void AfterAdvanceTime(WorldManager __instance, int __state)
        {
            if (__instance.currentDay == __state)
                return;
            try
            {
                ComputerStore store = HostStore();
                if (store == null)
                    return;
                ES3Settings settings = Settings();
                foreach (ShopButton button in store.shopButtons)
                {
                    if (button != null && button.assignedItem != null)
                        button.amountInStock = FullStock(button, settings);
                }
                AccessTools.Method(typeof(ComputerStore), "SaveAmountOfStock").Invoke(store, null);
                Log.LogInfo("New day, shop restocked.");
            }
            catch (Exception ex)
            {
                Log.LogError("Daily restock failed: " + ex.Message);
            }
        }

        public static void FixBadStock()
        {
            if (Time.unscaledTime < _nextCheck)
                return;
            _nextCheck = Time.unscaledTime + 2f;
            try
            {
                ComputerStore store = HostStore();
                if (store == null)
                    return;
                ES3Settings settings = Settings();
                bool changed = false;
                foreach (ShopButton button in store.shopButtons)
                {
                    if (button == null || button.assignedItem == null)
                        continue;
                    int full = FullStock(button, settings);
                    if (button.amountInStock <= Math.Max(full, button.stockMaxRandom))
                        continue;
                    button.amountInStock = full;
                    changed = true;
                }
                if (changed)
                {
                    AccessTools.Method(typeof(ComputerStore), "SaveAmountOfStock").Invoke(store, null);
                    Log.LogInfo("Put leftover 99999 stock back to normal.");
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
