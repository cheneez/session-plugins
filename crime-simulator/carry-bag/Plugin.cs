using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using ES3Internal;
using HarmonyLib;
using Rewired;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DuffelBagMod
{
    [BepInPlugin("com.kaboo.crimesim.duffelbag", "Duffel Bag", "1.0.0")]
    [BepInDependency("com.kaboo.crimesim.skillcash")]
    public class Plugin : BaseUnityPlugin
    {
        public const int BaseCapacity = 3;
        public const int MaxCapacity = 10;
        public static int Capacity = BaseCapacity;
        public static readonly Color32 OutlineBlue = new Color32(70, 96, 155, 255);
        public static readonly Color32 FillGreen = new Color32(111, 170, 112, 255);
        public static readonly Color32 OutlineRed = new Color32(196, 72, 72, 255);

        static readonly List<StoredLoot> Items = new List<StoredLoot>();
        static readonly Dictionary<int, Texture2D> Icons = new Dictionary<int, Texture2D>();

        public static bool Selected;
        static int _dropFrame = -1;
        static int _worldId;
        static FieldInfo _slotDelay;
        static FieldInfo _delayTimer;
        static RawImage _icon;
        static TextMeshProUGUI _quotaLabel;
        static float _nextQuotaSearch;
        static int _iconKey = -999;
        static int _placedQuotaId;
        static int _promptKey = int.MinValue;
        static string _promptText;
        static bool _loggedIcon;
        static bool _loggedTick;
        static bool _iconFailed;
        static bool _bypass;
        static bool _bagReady;
        static BepInEx.Logging.ManualLogSource Log;

        void Awake()
        {
            Log = Logger;
            Capacity = SavedCapacity();
            _slotDelay = AccessTools.Field(typeof(PlayerInventory), "slotChangeDelay");
            _delayTimer = AccessTools.Field(typeof(PlayerInventory), "delayTimer");
            var harmony = new Harmony("com.kaboo.crimesim.duffelbag");
            harmony.PatchAll(typeof(Patches));
            harmony.Patch(AccessTools.Method(typeof(FPP_Player), "Update"), postfix: new HarmonyMethod(typeof(Plugin), nameof(Tick)));
            Logger.LogInfo("Duffel bag ready. Sellable loot goes in first. 0 or scroll selects it, G drops the newest item.");
        }

        public static void SetCapacity(int capacity)
        {
            Capacity = Mathf.Clamp(capacity, BaseCapacity, MaxCapacity);
            _iconKey = -999;
            Log.LogInfo("Bag capacity " + Capacity + ".");
        }

        static int SavedCapacity()
        {
            try
            {
                string path = System.IO.Path.Combine(Application.persistentDataPath, "skill-upgrades.txt");
                if (!System.IO.File.Exists(path))
                    return BaseCapacity;
                string[] parts = System.IO.File.ReadAllText(path).Split(',');
                if (parts.Length > 2 && int.TryParse(parts[2], out int bought))
                    return Mathf.Clamp(BaseCapacity + bought, BaseCapacity, MaxCapacity);
            }
            catch (Exception)
            {
            }
            return BaseCapacity;
        }

        public static void Tick(FPP_Player __instance)
        {
            WorldManager world = WorldManager.Instance;
            if (world == null || __instance != world.localPlayer)
                return;

            int id = world.GetInstanceID();
            if (_worldId != 0 && id != _worldId)
            {
                Selected = false;
                _quotaLabel = null;
                _placedQuotaId = 0;
                _bagReady = false;
            }
            _worldId = id;
            if (!_bagReady)
                _bagReady = LoadBag(false);

            if (!_loggedTick)
            {
                _loggedTick = true;
                Log.LogInfo("HUD tick. quota=" + (world.localPlayer.quotaTxt != null));
            }

            try
            {
                RefreshIcon();
            }
            catch (Exception ex)
            {
                if (!_iconFailed)
                {
                    _iconFailed = true;
                    Log.LogError("Icon failed: " + ex);
                }
            }

            PlayerInventory inventory = world.localPlayer.inventory;
            if (inventory == null)
                return;
            if (Mouse5Pressed())
                SelectBag(inventory);
            if (Selected)
            {
                DimHotbar(inventory);
                if (Items.Count > 0 && DropPressed(world.localPlayer))
                    DropOne(inventory);
                ShowPrompt(inventory);
            }
        }

        void Update()
        {
            WorldManager world = WorldManager.Instance;
            if (world == null || world.localPlayer == null)
                return;

            int id = world.GetInstanceID();
            if (_worldId != 0 && id != _worldId)
                Selected = false;
            _worldId = id;

            if (!Selected)
                return;

            if (Input.GetKeyDown(KeyCode.G))
                DropOne(world.localPlayer.inventory);
        }

        static void RefreshIcon()
        {
            FPP_Player player = WorldManager.Instance != null ? WorldManager.Instance.localPlayer : null;
            TextMeshProUGUI quota = FindQuota(player);
            if (player == null && quota == null)
            {
                if (_icon != null)
                    _icon.enabled = false;
                return;
            }

            EnsureIcon();
            _icon.enabled = true;
            PlaceIcon(quota);

            int key = Items.Count + Capacity * 100 + (Selected ? 10000 : 0) + (Items.Count >= Capacity ? 20000 : 0);
            if (key == _iconKey && _icon.texture != null)
                return;

            _iconKey = key;
            if (!Icons.TryGetValue(key, out Texture2D icon) || icon == null)
            {
                icon = Paint(Items.Count, Capacity, Selected, Items.Count >= Capacity);
                Icons[key] = icon;
            }
            _icon.texture = icon;
            _icon.color = Color.white;
        }

        static TextMeshProUGUI FindQuota(FPP_Player player)
        {
            if (player != null && player.quotaTxt != null)
                return player.quotaTxt;
            if (_quotaLabel != null)
                return _quotaLabel;
            if (Time.unscaledTime < _nextQuotaSearch)
                return null;
            _nextQuotaSearch = Time.unscaledTime + 1f;

            TextMeshProUGUI best = null;
            float bestX = float.MinValue;
            TextMeshProUGUI[] all = UnityEngine.Object.FindObjectsOfType<TextMeshProUGUI>();
            for (int i = 0; i < all.Length; i++)
            {
                TextMeshProUGUI label = all[i];
                if (label == null || !label.gameObject.activeInHierarchy || string.IsNullOrEmpty(label.text))
                    continue;
                if (label.text.IndexOf('$') < 0 || label.text.IndexOf('/') < 0)
                    continue;
                float x = label.rectTransform.position.x;
                if (x < bestX)
                    continue;
                bestX = x;
                best = label;
            }
            _quotaLabel = best;
            return best;
        }

        static void EnsureIcon()
        {
            if (_icon != null)
                return;

            var go = new GameObject("DuffelBagIcon");
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(150f, 96f);
            _icon = go.AddComponent<RawImage>();
            _icon.raycastTarget = false;
            var layout = go.AddComponent<LayoutElement>();
            layout.ignoreLayout = true;
        }

        static void PlaceIcon(TextMeshProUGUI quota)
        {
            if (quota == null || quota.canvas == null)
                return;

            RectTransform root = quota.canvas.transform as RectTransform;
            RectTransform rect = _icon.rectTransform;
            RectTransform label = quota.rectTransform;
            int quotaId = quota.GetInstanceID();
            if (_placedQuotaId == quotaId && rect.parent == root)
                return;
            _placedQuotaId = quotaId;

            if (rect.parent != root)
            {
                rect.SetParent(root, false);
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0f);
                rect.localScale = Vector3.one;
                rect.localRotation = Quaternion.identity;
                rect.SetAsLastSibling();
                if (!_loggedIcon)
                {
                    _loggedIcon = true;
                    Debug.Log("[Duffel Bag] icon attached to quota HUD.");
                }
            }

            rect.pivot = new Vector2(1f, 0f);
            float width = label.rect.width * 0.72f;
            if (width < 88f)
                width = 88f;
            if (width > 128f)
                width = 128f;
            float height = _icon.texture != null ? width * _icon.texture.height / _icon.texture.width : 70f;
            var size = new Vector2(width, height);
            if (rect.sizeDelta != size)
                rect.sizeDelta = size;

            Vector3 topRight = label.TransformPoint(new Vector3(label.rect.xMax, label.rect.yMax, 0f));
            Vector3 local = root.InverseTransformPoint(topRight);
            float blankUnderBag = height * (73f / 402f);
            var pos = new Vector3(local.x - 10f, local.y + 1f - blankUnderBag, 0f);
            if ((rect.localPosition - pos).sqrMagnitude > 0.25f)
                rect.localPosition = pos;
        }

        public static bool Accepts(Item item)
        {
            if (item == null || item.itemDropPrefab == null)
                return false;
            if (!item.isLoot || item.isWeapon || item.isMoney || item.isKey || item.isAmmo || item.isFirstAid)
                return false;
            if (item.isSkillBook || item.isArmorVest || item.isDrone || item.isItemUpgrade || item.isCarFuel)
                return false;
            if (item.isAccessCard || item.isVoucher || item.isGoldenCard || item.hasToBeCarried)
                return false;
            return true;
        }

        static bool CurrentSlotEmpty(PlayerInventory inventory)
        {
            if (inventory == null || inventory.itemSlots == null)
                return true;
            int index = inventory.currentItemIndex;
            if (index < 0 || index >= inventory.itemSlots.Count)
                return HotbarEmpty(inventory);
            ItemSlot slot = inventory.itemSlots[index];
            return slot == null || slot.assignedItem == null;
        }

        static bool HotbarEmpty(PlayerInventory inventory)
        {
            if (inventory == null || inventory.itemSlots == null)
                return true;
            for (int i = 0; i < inventory.itemSlots.Count; i++)
            {
                ItemSlot slot = inventory.itemSlots[i];
                if (slot != null && slot.assignedItem != null)
                    return false;
            }
            return true;
        }

        public static bool TryStore(Item item, float damage, string property)
        {
            if (!Accepts(item) || Items.Count >= Capacity)
                return false;

            Items.Add(new StoredLoot
            {
                Item = item,
                Damage = damage,
                Property = property ?? "",
                PrefabId = PrefabId(item)
            });
            SaveBag();
            return true;
        }

        public static void SelectBag(PlayerInventory inventory)
        {
            if (inventory == null)
                return;
            Selected = true;
            MarkDelay(inventory);
            DimHotbar(inventory);
            if (inventory.player != null && inventory.changeSlotClip != null)
                inventory.player.sfxSource.PlayOneShot(inventory.changeSlotClip, 0.25f);
        }

        public static void LeaveBag(PlayerInventory inventory, int slot)
        {
            Selected = false;
            if (inventory == null || inventory.itemSlots == null || inventory.itemSlots.Count == 0)
                return;

            if (slot < 0)
                slot = inventory.itemSlots.Count - 1;
            if (slot >= inventory.itemSlots.Count)
                slot = 0;

            inventory.currentItemIndex = slot;
            MarkDelay(inventory);
            if (inventory.player != null && inventory.changeSlotClip != null)
                inventory.player.sfxSource.PlayOneShot(inventory.changeSlotClip, 0.25f);
            inventory.ActivateCurrentSlot();
        }

        public static void DropOne(PlayerInventory inventory)
        {
            if (_bypass || !Selected || inventory == null || Items.Count == 0)
                return;
            if (Time.frameCount == _dropFrame)
                return;
            if (inventory.itemSlots == null || inventory.currentItemIndex < 0 || inventory.currentItemIndex >= inventory.itemSlots.Count)
                return;
            _dropFrame = Time.frameCount;

            ItemSlot slot = inventory.itemSlots[inventory.currentItemIndex];
            StoredLoot stored = Items[Items.Count - 1];
            Item previous = slot.assignedItem;
            int previousAmount = slot.amount;
            var previousDamage = slot.damagesPerItem != null ? new List<float>(slot.damagesPerItem) : new List<float>();
            var previousProps = slot.customPropertyPerItem != null ? new List<string>(slot.customPropertyPerItem) : new List<string>();

            slot.AssignItem(stored.Item, stored.Damage, stored.Property ?? "");
            bool dropped = false;
            _bypass = true;
            try
            {
                inventory.DropItem(2f, true);
                dropped = slot.assignedItem != stored.Item;
                if (!dropped)
                {
                    inventory.DropItemToGround(0f, false);
                    if (slot.assignedItem == stored.Item)
                        slot.RemoveFromSlot();
                    dropped = slot.assignedItem != stored.Item;
                }
            }
            finally
            {
                _bypass = false;
            }

            if (!dropped)
            {
                RestoreSlot(slot, previous, previousAmount, previousDamage, previousProps);
                return;
            }

            Items.RemoveAt(Items.Count - 1);
            SaveBag();
            if (previous != null)
            {
                RestoreSlot(slot, previous, previousAmount, previousDamage, previousProps);
                inventory.ActivateCurrentSlot();
            }
            Debug.Log("[Duffel Bag] " + Items.Count + "/" + Capacity + " placed " + stored.Item.itemName);
        }

        static void RestoreSlot(ItemSlot slot, Item item, int amount, List<float> damages, List<string> props)
        {
            if (item == null)
            {
                slot.AssignItem(null, 0f, "");
                return;
            }

            float firstDamage = damages.Count > 0 ? damages[0] : 0f;
            string firstProp = props.Count > 0 ? props[0] ?? "" : "";
            slot.AssignItem(item, firstDamage, firstProp);
            for (int i = 1; i < amount; i++)
            {
                float damage = i < damages.Count ? damages[i] : firstDamage;
                string prop = i < props.Count ? props[i] ?? "" : firstProp;
                slot.AddExtra(damage, prop);
            }
        }

        static void MarkDelay(PlayerInventory inventory)
        {
            if (_slotDelay != null)
                _slotDelay.SetValue(inventory, true);
            if (_delayTimer != null)
                _delayTimer.SetValue(inventory, 0f);
        }

        static void DimHotbar(PlayerInventory inventory)
        {
            if (inventory.itemSlots == null)
                return;
            for (int i = 0; i < inventory.itemSlots.Count; i++)
            {
                ItemSlot slot = inventory.itemSlots[i];
                if (slot == null)
                    continue;
                if (slot.slotOutline != null && slot.slotOutline.color != inventory.unselectedSlotOutline)
                    slot.slotOutline.color = inventory.unselectedSlotOutline;
                if (slot.slotBackground != null && slot.slotBackground.color != inventory.unselectedSlotBG)
                    slot.slotBackground.color = inventory.unselectedSlotBG;
            }
        }

        static Texture2D _art;
        static MethodInfo _dropButton;

        static bool Mouse5Pressed()
        {
            try
            {
                if (Input.GetMouseButtonDown(4))
                    return true;
            }
            catch (Exception)
            {
            }

            try
            {
                if (ReInput.isReady && ReInput.controllers.Mouse != null && ReInput.controllers.Mouse.GetButtonDown(4))
                    return true;
            }
            catch (Exception)
            {
            }

            return false;
        }

        static bool DropPressed(FPP_Player player)
        {
            if (player == null || player.rewiredPlayer == null)
                return false;
            if (_dropButton == null)
                _dropButton = player.rewiredPlayer.GetType().GetMethod("GetButtonDown", new[] { typeof(string) });
            if (_dropButton == null)
                return false;
            return (bool)_dropButton.Invoke(player.rewiredPlayer, new object[] { "Drop" });
        }

        static void ShowPrompt(PlayerInventory inventory)
        {
            if (!Selected || Items.Count == 0 || inventory == null || inventory.currentItemInfoTxt == null)
            {
                _promptKey = int.MinValue;
                return;
            }
            if (inventory.itemInfoObj != null && !inventory.itemInfoObj.activeSelf)
                inventory.itemInfoObj.SetActive(true);

            Item item = Items[Items.Count - 1].Item;
            int promptKey = Items.Count * 397 + item.GetInstanceID();
            if (promptKey != _promptKey)
            {
                _promptKey = promptKey;
                string name = item.GetItemName(true);
                string key = "G";
                if (inventory.player != null && inventory.player.interactUI != null)
                    key = inventory.player.interactUI.GetAndReturnKey("Drop");
                string action = "放下";
                try
                {
                    string translated = I2.Loc.LocalizationManager.GetTranslation("drop or place", true, 0, true, false, null, null, true);
                    if (!string.IsNullOrEmpty(translated))
                        action = translated;
                }
                catch (Exception)
                {
                }
                _promptText = name + " | [" + key + "] " + action;
            }

            if (inventory.currentItemInfoTxt.text == _promptText)
                return;
            inventory.currentItemInfoTxt.text = _promptText;
            if (inventory.refreshLayoutInfo != null)
                inventory.refreshLayoutInfo.Refresh();
        }

        static Texture2D Art()
        {
            if (_art != null)
                return _art;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("duffel.png"))
            {
                var bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);
                _art = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                _art.LoadImage(bytes);
                _art.filterMode = FilterMode.Bilinear;
            }
            return _art;
        }

        static bool IsBlue(Color32 color)
        {
            return color.a > 40 && color.b > 48 && color.b > color.r + 8 && color.b + 12 >= color.g;
        }

        static Texture2D Paint(int count, int capacity, bool selected, bool full)
        {
            Texture2D art = Art();
            int w = art.width;
            int h = art.height;
            Color32[] src = art.GetPixels32();
            var left = new int[h];
            var right = new int[h];
            int maxSpan = 0;
            for (int y = 0; y < h; y++)
            {
                left[y] = w;
                right[y] = -1;
                for (int x = 0; x < w; x++)
                {
                    if (!IsBlue(src[y * w + x]))
                        continue;
                    if (x < left[y])
                        left[y] = x;
                    if (x > right[y])
                        right[y] = x;
                }
                int span = right[y] - left[y];
                if (span > maxSpan)
                    maxSpan = span;
            }

            int bodyMin = h;
            int bodyMax = 0;
            int bodySpan = (int)(maxSpan * 0.68f);
            for (int y = 0; y < h; y++)
            {
                if (right[y] - left[y] < bodySpan)
                    continue;
                if (y < bodyMin)
                    bodyMin = y;
                if (y > bodyMax)
                    bodyMax = y;
            }
            float fillLine = bodyMin + (bodyMax - bodyMin) * Mathf.Min(1f, count / (float)capacity);
            Color32 outline = full ? OutlineRed : (selected ? new Color32(118, 176, 236, 255) : OutlineBlue);
            var clear = new Color32(0, 0, 0, 0);
            var interior = new Color32(8, 10, 14, 255);
            var pixels = new Color32[src.Length];

            for (int y = 0; y < h; y++)
            {
                bool body = right[y] >= 0 && right[y] - left[y] >= bodySpan;
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    bool inside = body && x > left[y] + 3 && x < right[y] - 3 && y > bodyMin + 2 && y < bodyMax - 2;
                    if (IsBlue(src[i]))
                        pixels[i] = outline;
                    else if (inside && count > 0 && y <= fillLine)
                        pixels[i] = FillGreen;
                    else if (inside)
                        pixels[i] = interior;
                    else
                        pixels[i] = clear;
                }
            }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        class StoredLoot
        {
            public Item Item;
            public float Damage;
            public string Property;
            public long PrefabId;
        }

        static ES3Settings SaveSettings()
        {
            return new ES3Settings(ES3.Location.Cache);
        }

        static long PrefabId(Item item)
        {
            if (item == null || item.itemDropPrefab == null)
                return 0;
            ES3Prefab marker = item.itemDropPrefab.GetComponent<ES3Prefab>();
            return marker != null ? marker.prefabId : 0;
        }

        static void SaveBag()
        {
            try
            {
                ES3Settings settings = SaveSettings();
                if (Items.Count == 0)
                {
                    if (ES3.KeyExists("DuffelBagIds", settings))
                        ES3.DeleteKey("DuffelBagIds", settings);
                    if (ES3.KeyExists("DuffelBagDamage", settings))
                        ES3.DeleteKey("DuffelBagDamage", settings);
                    if (ES3.KeyExists("DuffelBagProps", settings))
                        ES3.DeleteKey("DuffelBagProps", settings);
                    return;
                }

                var ids = new long[Items.Count];
                var damage = new float[Items.Count];
                var props = new string[Items.Count];
                for (int i = 0; i < Items.Count; i++)
                {
                    StoredLoot stored = Items[i];
                    ids[i] = stored.PrefabId != 0 ? stored.PrefabId : PrefabId(stored.Item);
                    damage[i] = stored.Damage;
                    props[i] = stored.Property ?? "";
                }
                ES3.Save("DuffelBagIds", ids, settings);
                ES3.Save("DuffelBagDamage", damage, settings);
                ES3.Save("DuffelBagProps", props, settings);
            }
            catch (Exception ex)
            {
                Debug.Log("[Duffel Bag] save failed: " + ex.Message);
            }
        }

        static bool LoadBag(bool emptyIfMissing)
        {
            try
            {
                ES3PrefabDatabase database = ES3PrefabDatabase.Instance;
                if (database == null || database.prefabs == null || database.prefabs.Count == 0)
                    return false;

                ES3Settings settings = SaveSettings();
                if (!ES3.KeyExists("DuffelBagIds", settings))
                {
                    if (emptyIfMissing)
                    {
                        Items.Clear();
                        _iconKey = -999;
                    }
                    return true;
                }

                long[] ids = ES3.Load<long[]>("DuffelBagIds", settings);
                float[] damage = ES3.KeyExists("DuffelBagDamage", settings) ? ES3.Load<float[]>("DuffelBagDamage", settings) : new float[0];
                string[] props = ES3.KeyExists("DuffelBagProps", settings) ? ES3.Load<string[]>("DuffelBagProps", settings) : new string[0];
                Items.Clear();
                if (ids != null)
                {
                    for (int i = 0; i < ids.Length && Items.Count < MaxCapacity; i++)
                    {
                        Item item = FindSavedItem(database, ids[i]);
                        if (item == null)
                            continue;
                        Items.Add(new StoredLoot
                        {
                            Item = item,
                            Damage = i < damage.Length ? damage[i] : 0f,
                            Property = i < props.Length ? props[i] ?? "" : "",
                            PrefabId = ids[i]
                        });
                    }
                }
                _iconKey = -999;
                Debug.Log("[Duffel Bag] restored " + Items.Count + "/" + Capacity);
                return true;
            }
            catch (Exception ex)
            {
                Debug.Log("[Duffel Bag] load failed: " + ex.Message);
                return false;
            }
        }

        static Item FindSavedItem(ES3PrefabDatabase database, long id)
        {
            for (int i = 0; i < database.prefabs.Count; i++)
            {
                ES3Prefab prefab = database.prefabs[i];
                if (prefab == null || prefab.prefabId != id)
                    continue;
                Pickupable pickup = prefab.GetComponent<Pickupable>();
                if (pickup != null && pickup.assignedItem != null)
                    return pickup.assignedItem;
            }
            return null;
        }

        static class Patches
        {
            [HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.SaveSlots))]
            [HarmonyPostfix]
            static void SaveBagWithPlayer(PlayerInventory __instance)
            {
                WorldManager world = WorldManager.Instance;
                if (world == null || world.localPlayer == null || __instance != world.localPlayer.inventory)
                    return;
                SaveBag();
            }

            [HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.CheckSlots))]
            [HarmonyPostfix]
            static void LoadBagWithPlayer(PlayerInventory __instance, bool load)
            {
                if (!load)
                    return;
                WorldManager world = WorldManager.Instance;
                if (world == null || world.localPlayer == null || __instance != world.localPlayer.inventory)
                    return;
                _bagReady = LoadBag(true);
            }

            [HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.ClearSlots))]
            [HarmonyPostfix]
            static void ClearBagWithReset(PlayerInventory __instance)
            {
                WorldManager world = WorldManager.Instance;
                if (world == null || world.localPlayer == null || __instance != world.localPlayer.inventory)
                    return;
                Items.Clear();
                Selected = false;
                _iconKey = -999;
                SaveBag();
            }

            [HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.AddItem))]
            [HarmonyPrefix]
            static bool StoreLoot(PlayerInventory __instance, Item item, float dmg, string customProperty)
            {
                WorldManager world = WorldManager.Instance;
                if (world == null || world.localPlayer == null || __instance != world.localPlayer.inventory)
                    return true;
                if (!Accepts(item) || (!Selected && CurrentSlotEmpty(__instance)))
                    return true;
                if (!TryStore(item, dmg, customProperty))
                    return true;
                Debug.Log("[Duffel Bag] " + Items.Count + "/" + Capacity + " " + item.itemName);
                return false;
            }

            [HarmonyPatch(typeof(PlayerInventory), "ChangeItemSlot")]
            [HarmonyPrefix]
            static bool ScrollSlots(PlayerInventory __instance, int amount)
            {
                if (__instance == null || __instance.itemSlots == null || __instance.itemSlots.Count == 0)
                    return true;

                if (Selected)
                {
                    LeaveBag(__instance, amount > 0 ? 0 : __instance.itemSlots.Count - 1);
                    return false;
                }

                int next = __instance.currentItemIndex + amount;
                if (next >= 0 && next < __instance.itemSlots.Count)
                    return true;

                SelectBag(__instance);
                return false;
            }

            [HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.ChangeItemSlotNr))]
            [HarmonyPrefix]
            static bool NumberKey(PlayerInventory __instance, int nr)
            {
                if (nr < 0)
                {
                    SelectBag(__instance);
                    return false;
                }

                Selected = false;
                return true;
            }

            [HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.RefreshItemInfo))]
            [HarmonyPostfix]
            static void BagPrompt(PlayerInventory __instance)
            {
                ShowPrompt(__instance);
            }

            [HarmonyPatch(typeof(PlayerInventory), "DropItem")]
            [HarmonyPrefix]
            static bool DropFromBag(PlayerInventory __instance)
            {
                if (_bypass || !Selected)
                    return true;
                DropOne(__instance);
                return false;
            }
        }
    }
}
