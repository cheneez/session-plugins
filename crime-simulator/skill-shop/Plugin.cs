using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx;
using ES3Internal;
using Fusion;
using HarmonyLib;
#if FRIEND
using Steamworks;
#endif
using I2.Loc;
using Rewired;
using HighlightPlus;
using TMPro;
using UnityEngine;
using ECM.Components;
using GraphNode = Pathfinding.GraphNode;
using PathUtilities = Pathfinding.PathUtilities;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SkillCashMod
{
    [BepInPlugin("com.kaboo.crimesim.skillcash", "Skill Cash", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public static readonly Color32 Gold = new Color32(255, 196, 64, 255);
        static readonly int[] BillAmounts = { 1, 5, 10, 20, 50, 100, 200, 500 };

        // Rows: basic, advanced, master lockpick, dial safe, electronic safe. Columns follow BillAmounts.
        static readonly int[][] LootWeights =
        {
            new[] { 39, 20, 15, 10, 7, 5, 3, 1 },
            new[] { 15, 25, 20, 15, 10, 7, 5, 3 },
            new[] { 2, 3, 5, 20, 25, 20, 15, 10 },
            new[] { 3, 7, 15, 25, 20, 15, 10, 5 },
            new[] { 2, 3, 5, 10, 20, 25, 20, 15 }
        };

        static readonly Book[] Books =
        {
            new Book("堆叠一级", 300, "stackskill", "stacker #1"),
            new Book("堆叠二级", 500, "stackskill2", "stacker #2"),
            new Book("堆叠三级", 1000, "stackskill3", "stacker #3"),
            new Book("基础开锁", 300, "simplelockpickskill", "basic lockpicking"),
            new Book("高级开锁", 500, "advancedlockpickskill", "advanced lockpicking"),
            new Book("大师开锁", 1000, "masterlockpickskill", "master lockpicking"),
            new Book("基础电子", 300, "hackerskill", "basic electronics"),
            new Book("高级电子", 500, "hackerskill2", "advanced electronics"),
            new Book("敏捷一级", 300, "agilityskill1", "agility #1"),
            new Book("敏捷二级", 500, "agilityskill2", "agility #2"),
            new Book("敏捷三级", 1000, "agilityskill3", "agility #3"),
            new Book("大力士一级", 300, "strongmanskill", "strong #1"),
            new Book("大力士二级", 500, "strongmanskill2", "strong #2"),
            new Book("感知一级", 300, "perceptionskill1", "perception #1"),
            new Book("感知二级", 500, "perceptionskill2", "perception #2"),
            new Book("感知三级", 1000, "perceptionskill3", "perception #3"),
            new Book("潜行一级", 300, "stealthskill1", "stealth #1"),
            new Book("潜行二级", 500, "stealthskill2", "stealth #2"),
            new Book("背包客", 700, "backpackerskill", "backpacker"),
            new Book("读者", 700, "readerskill", "reader' skill", "reader skill"),
            new Book("种大麻", 700, "weedkeeperskill", "weed keeper"),
            new Book("偷车", 700, "cartheftskill", "car theft"),
            new Book("审讯", 700, "interrogatorskill", "interrogator"),
            new Book("珠宝商", 700, "jewelerskill", "jeweler"),
            new Book("化学家", 700, "chemistskill", "chemist' skill", "chemist skill"),
            new Book("高级修理", 700, "advancedrepairs", "advanced repairs")
        };

        static readonly Book[] Gear =
        {
            new Book("防弹衣", 250, "armor vest"),
            new Book("手枪弹药", 300, "9x19mm"),
            new Book("棒球棍", 500, "baseball bat"),
            new Book("撬棍", 750, "crowbar"),
            new Book("消防斧", 800, "axe", "fire axe"),
            new Book("电击枪", 1000, "stun gun"),
            new Book("消音手枪", 5000, "silenced pistol")
        };

        static int _blockFrame = -1;
        static BepInEx.Logging.ManualLogSource Log;
        static TextMeshProUGUI _cashLabel;
        static int _cashShown = int.MinValue;
        static int _moneyId;
        static int _computerId;
        static bool _buttonMade;
        static GameObject _page;
        static TextMeshProUGUI _pageCash;
        static TextMeshProUGUI _cartText;
        static readonly Dictionary<string, int> Cart = new Dictionary<string, int>();
        static readonly List<Row> Rows = new List<Row>();
        static readonly List<Row> GearRows = new List<Row>();
        static ScrollRect _scroll;
        static RectTransform _bookContent;
        static RectTransform _weaponContent;
        static RectTransform _xpContent;
        static readonly Button[] _tabs = new Button[3];
        static readonly int[] XpAmount = { 1, 10, 100 };
        static readonly int[] _xpCart = new int[3];
        static readonly int[] _xpBought = new int[3];
        static readonly TextMeshProUGUI[] _xpPrice = new TextMeshProUGUI[3];
        static readonly TextMeshProUGUI[] _xpQty = new TextMeshProUGUI[3];
        static bool _xpLoaded;
        // Order matches skill-upgrades.txt; the duffel bag mod reads the bag level at index 2.
        static readonly string[] UpgradeNames = { "奔跑速度", "跳跃高度", "抢劫包容量", "血量", "耐力", "随地修复", "透视", "无敌模式", "治疗", "隐身" };
        static readonly int[] UpgradeMax = { 10, 10, 7, 10, 10, 3, 3, 3, 3, 3 };
        static readonly int[] UpgradeRowOrder = { 0, 1, 3, 4, 2, 8, 5, 7, 6, 9 };
        static readonly int[] LostOnDeath = { 0, 1, 3, 4 };
        static readonly int[] BagPrices = { 300, 500, 700, 1000, 1500, 2000, 3000 };
        const int SkillRepair = 5;
        const int SkillSee = 6;
        const int SkillInvuln = 7;
        const int SkillHeal = 8;
        const int SkillStealth = 9;
        static readonly int[] _upLevel = new int[10];
        static readonly int[] _upCart = new int[10];
        static readonly TextMeshProUGUI[] _upName = new TextMeshProUGUI[10];
        static readonly TextMeshProUGUI[] _upPrice = new TextMeshProUGUI[10];
        static readonly TextMeshProUGUI[] _upQty = new TextMeshProUGUI[10];
        static readonly Image[] _upRowBg = new Image[10];
        static readonly int[] _upSwitch = new int[10];
        static readonly GameObject[] _upSwitchGo = new GameObject[10];
        static int _selectedSkill = -1;
        static readonly float[] _skillCdUntil = new float[10];
        static int _skillActiveKind = -1;
        static float _skillActiveUntil;
        static float _invulnHp;
        static int _lastCountShown = int.MinValue;
        public static bool HasFieldRepair { get { return _upLevel[SkillRepair] > 0 && _selectedSkill == SkillRepair; } }
        public static bool HasPlayerSkill
        {
            get
            {
                for (int i = SkillRepair; i <= SkillStealth; i++)
                {
                    if (_upLevel[i] > 0)
                        return true;
                }
                return false;
            }
        }
        static bool _weaponsLogged;
        static bool _booksResolved;
        static bool _catalogReady;
        static bool _loggedMissingBill;
        static readonly List<SpawnPiece> MoneyBills = new List<SpawnPiece>();
        static readonly List<SpawnPiece> KnownPieces = new List<SpawnPiece>();
        static float _nextSearch;
        static int _spawnFrame = -1;

        void Awake()
        {
            Log = Logger;
            Cash.Load();
            LoadXpPrices();
            LoadUpgrades();
            var harmony = new Harmony("com.kaboo.crimesim.skillcash");
            harmony.Patch(AccessTools.Method(typeof(PlayerInventory), "PickedItem"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(BeforePickup)),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterPickup)));
            harmony.Patch(AccessTools.Method(typeof(QuotaManager), "RPC_AddValue"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(BlockMoneyQuota)));
            harmony.Patch(AccessTools.Method(typeof(FPP_Player), "Update"),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(Tick)));
            harmony.Patch(AccessTools.Method(typeof(Openable), "RPC_Unlocked"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(BeforeFurnitureUnlock)),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterFurnitureUnlock)));
            harmony.Patch(AccessTools.Method(typeof(Safe), "RPC_SetUnlocked"),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterDialSafeUnlock)));
            harmony.Patch(AccessTools.Method(typeof(ElectronicSafe), "RPC_Unlocked"),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterElectronicSafeUnlock)));
            harmony.Patch(AccessTools.Method(typeof(ElectronicSafe), "RPC_UnlockAndAlarm"),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterElectronicSafeUnlock)));
            harmony.Patch(AccessTools.Method(typeof(FPP_Player), "RPC_Tazed"),
                transpiler: new HarmonyMethod(typeof(Plugin), nameof(TaserDamage)));
            harmony.Patch(AccessTools.Method(typeof(Crossbow), "ShotRay"),
                transpiler: new HarmonyMethod(typeof(Plugin), nameof(CrossbowDamage)));
            harmony.Patch(AccessTools.Method(typeof(SilencedPistol), "ShotRay"),
                transpiler: new HarmonyMethod(typeof(Plugin), nameof(PistolDamage)));
            harmony.Patch(AccessTools.Method(typeof(FPP_Player), "CalculateBonusesToPlayer"),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(UpgradeVitals)));
            harmony.Patch(AccessTools.Method(typeof(FPP_Player), "PlayerDeath"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(SkipDeathIfInvuln)));
            harmony.Patch(AccessTools.Method(typeof(FPP_Player), "PlayerDeath"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(BeforeDeath)),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterDeath)));
            harmony.Patch(AccessTools.Method(typeof(FPP_Player), "InteractSensorDetected"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(ShowBillAmount)),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(ShowBillAmount)));
            harmony.Patch(AccessTools.Method(typeof(FPP_Player), "InteractSensorDetected"),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(HealRevivePrompt)));
            MethodInfo drain = AccessTools.Method(typeof(FPP_Player), "DrainStamina");
            if (drain != null)
                harmony.Patch(drain, prefix: new HarmonyMethod(typeof(Plugin), nameof(SkipStaminaIfInvuln)));
            MethodInfo interact = AccessTools.Method(typeof(FPP_Player), "Interaction");
            if (interact != null)
                harmony.Patch(interact, prefix: new HarmonyMethod(typeof(Plugin), nameof(HealReviveUse)));
            harmony.Patch(AccessTools.Method(typeof(SaveSceneObjects), "SaveSceneStuff"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(BeforeSceneSave)),
                finalizer: new HarmonyMethod(typeof(Plugin), nameof(AfterSave)));
            harmony.Patch(AccessTools.Method(typeof(SaveTruckItems), "SaveTruckObjects"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(BeforeTruckSave)),
                finalizer: new HarmonyMethod(typeof(Plugin), nameof(AfterSave)));
            harmony.Patch(AccessTools.Method(typeof(ECM.Controllers.BaseFirstPersonController), "GetTargetSpeed"),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(UpgradeSpeed)));
            harmony.Patch(AccessTools.PropertyGetter(typeof(ECM.Controllers.BaseCharacterController), "jumpImpulse"),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(UpgradeJump)));
            harmony.Patch(AccessTools.Method(typeof(FPP_Player), "CheckMovementDirection"),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(Plugin), nameof(BeforeFallCheck)), Priority.Last),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(Plugin), nameof(AfterFallCheck)), Priority.First));
            harmony.Patch(AccessTools.Method(typeof(FPP_Player), "SpawnChatMessage"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(HideUpgradeChat)));
            harmony.Patch(AccessTools.Method(typeof(PausePlayerInfo), "AssignPlayer"),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(ShowPlayerUpgrades)));
            harmony.Patch(AccessTools.Method(typeof(PausePlayerInfo), "Update"),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(ShowPlayerUpgrades)));
            harmony.Patch(AccessTools.Method(typeof(WeaponManager), "Attack"),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(NoteToolUse)));
            harmony.Patch(AccessTools.Method(typeof(WeaponManager), "ContinousUse"),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(NoteToolUse)));
            harmony.Patch(AccessTools.Method(typeof(PlayerInventory), "AddDamageToCurrentItem"),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(NoteToolUse)));
            harmony.Patch(AccessTools.Method(typeof(NPC), "Update"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(BreakChaseBeforeNpcUpdate)),
                postfix: new HarmonyMethod(typeof(SkillSeeFx), nameof(SkillSeeFx.Postfix)),
                finalizer: new HarmonyMethod(typeof(Plugin), nameof(AfterNpcUpdateSafe)));
            harmony.Patch(AccessTools.Method(typeof(NPC), "Update"),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(StopStealthFollow)));
            harmony.Patch(AccessTools.Method(typeof(NPC), "Hearing"),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterNpcHear)));
            harmony.Patch(AccessTools.Method(typeof(NPC), "RPC_DealDamage"),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterNpcHurt)),
                finalizer: new HarmonyMethod(typeof(Plugin), nameof(AfterNpcRpcSafe)));
            harmony.Patch(AccessTools.Method(typeof(NPC), "RPC_SetHitboxes"),
                finalizer: new HarmonyMethod(typeof(Plugin), nameof(AfterNpcRpcSafe)));
            harmony.Patch(AccessTools.Method(typeof(NPC), "RPC_DisableHitboxes"),
                finalizer: new HarmonyMethod(typeof(Plugin), nameof(AfterNpcRpcSafe)));
            harmony.Patch(AccessTools.Method(typeof(NPC), "WakeUp"),
                finalizer: new HarmonyMethod(typeof(Plugin), nameof(AfterWakeUpSafe)));
            harmony.Patch(AccessTools.Method(typeof(NPC), "RunAway"),
                finalizer: new HarmonyMethod(typeof(Plugin), nameof(AfterRunAwaySafe)));
            harmony.Patch(AccessTools.Method(typeof(NPC), "SetNewBehaviour"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(SkipBadNpcHour)));
            harmony.Patch(AccessTools.Method(typeof(NPC), "RPC_KnockDown"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(BeforeCopKnock)),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterCopKnock)),
                finalizer: new HarmonyMethod(typeof(Plugin), nameof(AfterNpcRpcSafe)));
            harmony.Patch(AccessTools.Method(typeof(NPC), "FightOrFlight"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(BlockNpcAlert)));
            harmony.Patch(AccessTools.Method(typeof(NPC), "LookAtPosition"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(StealthLookAway)));
            MethodInfo nearest = AccessTools.Method(typeof(NPC), "GetNearestPlayer");
            if (nearest != null)
                harmony.Patch(nearest, postfix: new HarmonyMethod(typeof(Plugin), nameof(SkipStealthedNearest)));
            MethodInfo policeSearch = AccessTools.Method(typeof(NPC), "AddPoliceCheckPointsAfterChase");
            if (policeSearch != null)
                harmony.Patch(policeSearch,
                    prefix: new HarmonyMethod(typeof(Plugin), nameof(BeforePoliceSearch)),
                    postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterPoliceSearch)));
            harmony.Patch(AccessTools.Method(typeof(FPP_Player), "DealDamageToPlayer"),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(HoldInvulnHealth)));
            harmony.Patch(AccessTools.Method(typeof(FPP_Player), "RPC_ActivateInvisibility"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(SkipNativeInvisRpc)));
            harmony.Patch(AccessTools.Method(typeof(NPC), "Sight"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(HideFromNpcSight)),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterNpcSight)));
            harmony.Patch(AccessTools.Method(typeof(NPC), "DroneSight"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(HideFromNpcSight)),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterNpcSight)));
            harmony.Patch(AccessTools.Method(typeof(NPC), "SpottedPlayer"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(BlockNpcAlert)));
            harmony.Patch(AccessTools.Method(typeof(NPC), "CallCops"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(BlockNpcAlert)));
            harmony.Patch(AccessTools.Method(typeof(NPC), "CallBackup"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(BlockNpcAlert)));
            harmony.Patch(AccessTools.Method(typeof(NPC), "RPC_CallCopsSound"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(BlockNpcAlert)));
            harmony.Patch(AccessTools.Method(typeof(PoliceManager), "PoliceCalled"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(BlockPoliceTrack)));
            harmony.Patch(AccessTools.Method(typeof(PoliceManager), "RPC_PoliceCalled"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(BlockPoliceTrack)));
            harmony.Patch(AccessTools.Method(typeof(PoliceManager), "RPC_UpdateLastPosition"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(BlockPoliceTrack)));
            harmony.Patch(AccessTools.Method(typeof(PoliceManager), "RPC_ForceUpdateLastPosition"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(BlockPoliceTrack)));
            Type[] rpcTypes = { typeof(NPC), typeof(PoliceCar), typeof(WorldManager), typeof(PoliceManager) };
            for (int t = 0; t < rpcTypes.Length; t++)
            {
                foreach (MethodInfo rpc in AccessTools.GetDeclaredMethods(rpcTypes[t]))
                {
                    if (rpc == null || rpc.IsStatic || !rpc.Name.StartsWith("RPC_") || rpc.Name.Contains("Invoker"))
                        continue;
                    harmony.Patch(rpc, prefix: new HarmonyMethod(typeof(Plugin), nameof(RunRpcLocalIfUnspawned)));
                }
            }
            MethodInfo despawn = AccessTools.Method(typeof(NetworkRunner), "Despawn", new[] { typeof(NetworkObject) });
            if (despawn != null)
                harmony.Patch(despawn, prefix: new HarmonyMethod(typeof(Plugin), nameof(BeforeNetDespawn)));
            MethodInfo hasAuth = AccessTools.PropertyGetter(typeof(NetworkBehaviour), "HasStateAuthority");
            if (hasAuth != null)
                harmony.Patch(hasAuth,
                    postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterHasStateAuth)),
                    finalizer: new HarmonyMethod(typeof(Plugin), nameof(AfterHasStateAuthSafe)));
            foreach (MethodInfo move in AccessTools.GetDeclaredMethods(typeof(CharacterMovement)))
            {
                if (move.Name == "Move" || move.Name == "OverlapRecovery")
                    harmony.Patch(move,
                        prefix: new HarmonyMethod(typeof(Plugin), nameof(BeforeDoorClipMove)),
                        finalizer: new HarmonyMethod(typeof(Plugin), nameof(AfterDoorClipMove)));
                else if (move.Name == "OverlapCapsule" && move.ReturnType == typeof(Collider[]))
                    harmony.Patch(move, postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterDoorOverlap)));
            }
            MethodInfo groundOverlap = AccessTools.Method(typeof(BaseGroundDetection), "OverlapCapsule");
            if (groundOverlap != null)
                harmony.Patch(groundOverlap, postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterGroundOverlap)));
            MethodInfo penetrate = AccessTools.Method(typeof(Physics), "ComputePenetration");
            if (penetrate != null)
                harmony.Patch(penetrate, prefix: new HarmonyMethod(typeof(Plugin), nameof(SkipDoorPenetration)));
            foreach (MethodInfo phys in AccessTools.GetDeclaredMethods(typeof(Physics)))
            {
                if (phys.Name == "OverlapCapsuleNonAlloc")
                    harmony.Patch(phys, postfix: new HarmonyMethod(typeof(Plugin), nameof(FilterOverlapNonAlloc)));
                else if ((phys.Name == "CapsuleCast" || phys.Name == "SphereCast" || phys.Name == "Raycast")
                    && phys.ReturnType == typeof(bool))
                {
                    ParameterInfo[] args = phys.GetParameters();
                    bool hasHit = false;
                    for (int i = 0; i < args.Length; i++)
                    {
                        if (args[i].ParameterType == typeof(RaycastHit).MakeByRefType())
                            hasHit = true;
                    }
                    if (hasHit)
                        harmony.Patch(phys, postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterCapsuleCastDoor)));
                }
            }
            harmony.Patch(AccessTools.Method(typeof(PVP_ModeButton), "Start"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(BeforeModeButtonStart)),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterModeButtonStart)));
            harmony.Patch(AccessTools.Method(typeof(PVP_Scoreboard), "RefreshCompetitionScores"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(BeforeRefreshCompetition)),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterRefreshCompetition)));
            harmony.Patch(AccessTools.Method(typeof(PVP_Scoreboard), "RPC_CompetitionVictory"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(BeforeCompetitionVictory)),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterCompetitionVictory)));
            MethodInfo taserUpdate = AccessTools.Method(typeof(NPCTaser), "Update");
            if (taserUpdate != null)
                harmony.Patch(taserUpdate, prefix: new HarmonyMethod(typeof(Plugin), nameof(SkipDroppedStunShot)));
            MethodInfo dartSensor = AccessTools.Method(typeof(TaserDart), "Sensor");
            if (dartSensor != null)
                harmony.Patch(dartSensor, prefix: new HarmonyMethod(typeof(Plugin), nameof(SkipDroppedStunShot)));
            harmony.Patch(AccessTools.Method(typeof(WorldManager), "GameOver"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(BeforeGameOver)));
            harmony.Patch(AccessTools.Method(typeof(SpectateManager), "Spectate"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(BeforeSpectate)));
            MethodInfo pinOpt = AccessTools.Method(typeof(Pinboard), "Optimization");
            if (pinOpt != null)
                harmony.Patch(pinOpt, prefix: new HarmonyMethod(typeof(Plugin), nameof(BeforePinboardOpt)));
            Logger.LogInfo("Skill cash ready. Paper bills stay on the player who picks them up.");
#if FRIEND
            PatchDemoLobby(harmony);
#endif
        }

        public static void Tick(FPP_Player __instance)
        {
            WorldManager world = WorldManager.Instance;
            if (world == null || __instance != world.localPlayer)
                return;

            FlushCopDrops();
            PaintAllCompetitionBoards();

            try
            {
                RefreshCashLabel(__instance);
            }
            catch (Exception ex)
            {
                Log.LogError("Cash label failed: " + ex.Message);
            }

            try
            {
                EnsureBookButton();
                Computer open = Computer.Instance;
                if (_page != null && _page.activeSelf && (open == null || !open.computerActive))
                    _page.SetActive(false);
            }
            catch (Exception ex)
            {
                Log.LogError("Skill book button failed: " + ex.Message);
            }

            KeepSkillIconYellow();
            SyncBillAmounts();
            if (_vitalsPlayer != __instance.GetInstanceID())
            {
                _vitalsPlayer = __instance.GetInstanceID();
                RefreshVitals();
            }
            if (_deathNotice && (__instance.isDead || Ressurecting(__instance)))
                _deathNoticeAt = 0f;
            else if (_deathNotice && _deathNoticeAt == 0f)
                _deathNoticeAt = Time.unscaledTime + 1f;
            else if (_deathNotice && Time.unscaledTime >= _deathNoticeAt)
            {
                _deathNotice = false;
                _deathNoticeAt = 0f;
                __instance.ShowMessage("死亡惩罚：所有升级-1", 4f, "bad", true);
            }

            try
            {
                SelfRevive(__instance, world);
            }
            catch (Exception ex)
            {
                Log.LogError("Self revive failed: " + ex.Message);
            }

            try
            {
                TickAllDeadCountdown(world, __instance);
            }
            catch (Exception ex)
            {
                Log.LogError("All-dead countdown failed: " + ex.Message);
            }

            try
            {
                StreetCash(__instance, world);
            }
            catch (Exception ex)
            {
                Log.LogError("Street cash failed: " + ex.Message);
            }

#if !FRIEND
            int[] spawnAmounts = IsHost(world) && Time.frameCount != _spawnFrame ? SpawnPressed() : null;
            if (spawnAmounts != null)
            {
                Computer computer = Computer.Instance;
                if (computer == null || !computer.computerActive)
                {
                    _spawnFrame = Time.frameCount;
                    SpawnBill(__instance, spawnAmounts);
                }
            }
#endif

            BroadcastUpgrades(__instance, world, false);
            TickZSkills(__instance);
            if (HasFieldRepair || _repairFill != null)
                FieldRepair(__instance);
            _usedToolThisFrame = false;

            try
            {
                TickHomeSell(__instance, world);
            }
            catch (Exception ex)
            {
                Log.LogError("Home sell failed: " + ex.Message);
            }

            if (!__instance.isDead && Time.frameCount != _spawnFrame && KeyDown(KeyCode.LeftControl))
            {
                Computer computer = Computer.Instance;
                if (computer == null || !computer.computerActive)
                {
                    _spawnFrame = Time.frameCount;
                    DropCash(__instance);
                }
            }
        }

        const int ReviveCost = 500;
        const string RevivePromptText = "按N花费500现金复活";
        const string ReviveNoCashText = "现金不足";
        static GameObject _reviveCanvas;
        static RectTransform _revivePanel;
        static Image _reviveBg;
        static TextMeshProUGUI _reviveLabel;
        static SpectateManager _reviveStyledFor;
        static Color _revivePromptColor = new Color32(0, 0, 0, 160);
        static bool _reviveSpotKnown;
        static Vector2 _reviveSpot;
        static float _reviveNoCashUntil;
        static bool _reviveLogged;

        static void SelfRevive(FPP_Player player, WorldManager world)
        {
            bool waiting = player.isDead && !Ressurecting(player) && !world.allDeadFailure && !world.summaryActive;
            if (!waiting)
            {
                _reviveNoCashUntil = 0f;
                _reviveSpotKnown = false;
                _reviveLogged = false;
                if (_reviveCanvas != null && _reviveCanvas.activeSelf)
                    _reviveCanvas.SetActive(false);
                return;
            }

            if (_reviveCanvas == null)
                BuildReviveUi(player);
            if (_reviveCanvas == null)
                return;

            SpectateManager spectate = UnityEngine.Object.FindObjectOfType<SpectateManager>();
            StyleRevivePanel(spectate);
            PlaceRevivePanel(spectate);

            bool noCash = Time.unscaledTime < _reviveNoCashUntil;
            string text = noCash ? ReviveNoCashText : RevivePromptText;
            if (_reviveLabel.text != text)
                _reviveLabel.text = text;
            _reviveBg.color = noCash ? (Color)player.badMessageColor : _revivePromptColor;
            bool visible = !player.pauseActive;
            if (_reviveCanvas.activeSelf != visible)
            {
                _reviveCanvas.SetActive(visible);
                if (visible)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(_revivePanel);
            }
            if (visible && !_reviveLogged)
            {
                _reviveLogged = true;
                Log.LogInfo("Revive prompt shown at " + _revivePanel.anchoredPosition + ", text alpha " + _reviveLabel.color.a.ToString("0.00") + ", background alpha " + _reviveBg.color.a.ToString("0.00") + ".");
            }

            if (player.pauseActive || player.chatActive || Time.frameCount == _spawnFrame || !KeyDown(KeyCode.N))
                return;
            _spawnFrame = Time.frameCount;
            if (Cash.Value < ReviveCost)
            {
                _reviveNoCashUntil = Time.unscaledTime + 3f;
                return;
            }

            Cash.Add(-ReviveCost);
            _cashShown = int.MinValue;
            player.RPC_RessurectPlayer(false, true);
            _reviveCanvas.SetActive(false);
            Log.LogInfo("Revived for " + ReviveCost + " cash. Balance " + Cash.Value);
        }

        const float AllDeadWait = 10f;
        static float _allDeadUntil;
        static bool _allDeadWaiting;
        static bool _allDeadEnding;
        static GameObject _allDeadCanvas;
        static TextMeshProUGUI _allDeadLabel;
        static int _allDeadShown = -1;
        static readonly AccessTools.FieldRef<Pinboard, GameObject> PinboardCam =
            AccessTools.FieldRefAccess<Pinboard, GameObject>("currentCamera");

        static bool AnyoneAlive(WorldManager world)
        {
            if (world == null)
                return false;
            List<GameObject> all = world.allPlayers;
            if (all == null)
                return false;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] == null)
                    continue;
                FPP_Player p = all[i].GetComponent<FPP_Player>();
                if (p == null)
                    continue;
                if (!p.isDead || Ressurecting(p))
                    return true;
            }
            return false;
        }

        static void BeginAllDeadWait()
        {
            if (_allDeadWaiting || _allDeadEnding)
                return;
            _allDeadWaiting = true;
            _allDeadUntil = Time.unscaledTime + AllDeadWait;
            _allDeadShown = -1;
            Log.LogInfo("Everyone is down. 10 seconds to revive.");
        }

        static bool BeforeSpectate(SpectateManager __instance)
        {
            if (__instance == null || __instance.playersInGame == null || __instance.playersInGame.Count > 0)
                return true;
            WorldManager world = WorldManager.Instance;
            if (world == null || world.allDeadFailure || world.summaryActive || _allDeadEnding)
                return true;
            BeginAllDeadWait();
            return false;
        }

        static bool BeforeGameOver(WorldManager __instance)
        {
            if (__instance == null)
                return true;
            if (_allDeadEnding || __instance.allDeadFailure || __instance.summaryActive)
                return true;
            try
            {
                if (__instance.quotaFailed)
                    return true;
            }
            catch (Exception)
            {
            }
            if (AnyoneAlive(__instance))
            {
                _allDeadWaiting = false;
                return false;
            }
            BeginAllDeadWait();
            if (Time.unscaledTime < _allDeadUntil)
                return false;
            _allDeadEnding = true;
            _allDeadWaiting = false;
            HideAllDeadCountdown();
            return true;
        }

        static bool BeforePinboardOpt(Pinboard __instance)
        {
            try
            {
                GameObject cam = PinboardCam(__instance);
                if (cam != null && cam.activeInHierarchy)
                    return true;
                return Camera.main != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        static void TickAllDeadCountdown(WorldManager world, FPP_Player player)
        {
            if (world == null || player == null)
                return;
            if (world.loadingScene)
            {
                _allDeadWaiting = false;
                _allDeadEnding = false;
                HideAllDeadCountdown();
                return;
            }
            if (world.summaryActive || world.allDeadFailure)
            {
                _allDeadWaiting = false;
                HideAllDeadCountdown();
                return;
            }
            if (AnyoneAlive(world))
            {
                _allDeadWaiting = false;
                _allDeadEnding = false;
                HideAllDeadCountdown();
                return;
            }
            if (_allDeadEnding || !_allDeadWaiting)
                return;
            float left = _allDeadUntil - Time.unscaledTime;
            if (left < 0f)
                left = 0f;
            ShowAllDeadCountdown(player, Mathf.CeilToInt(left));
            if (Time.unscaledTime < _allDeadUntil)
                return;
            _allDeadEnding = true;
            _allDeadWaiting = false;
            HideAllDeadCountdown();
            Log.LogInfo("Nobody revived. Ending the night.");
            world.GameOver();
        }

        static void ShowAllDeadCountdown(FPP_Player player, int secs)
        {
            if (secs < 0)
                secs = 0;
            if (_allDeadCanvas == null)
                BuildAllDeadUi(player);
            if (_allDeadCanvas == null || _allDeadLabel == null)
                return;
            if (!_allDeadCanvas.activeSelf)
                _allDeadCanvas.SetActive(true);
            if (_allDeadShown == secs)
                return;
            _allDeadShown = secs;
            _allDeadLabel.SetText("全员倒下  {0}  秒后结束夜晚", secs);
            _allDeadLabel.ForceMeshUpdate(true, true);
        }

        static void HideAllDeadCountdown()
        {
            _allDeadShown = -1;
            if (_allDeadCanvas != null && _allDeadCanvas.activeSelf)
                _allDeadCanvas.SetActive(false);
        }

        static void BuildAllDeadUi(FPP_Player player)
        {
            if (_allDeadCanvas != null)
                UnityEngine.Object.Destroy(_allDeadCanvas);
            _allDeadCanvas = new GameObject("AllDeadCountdownCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            UnityEngine.Object.DontDestroyOnLoad(_allDeadCanvas);
            Canvas canvas = _allDeadCanvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32100;
            CanvasScaler scaler = _allDeadCanvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            var labelObj = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObj.transform.SetParent(_allDeadCanvas.transform, false);
            RectTransform rect = (RectTransform)labelObj.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(1200f, 80f);
            _allDeadLabel = labelObj.GetComponent<TextMeshProUGUI>();
            _allDeadLabel.fontSize = 48f;
            _allDeadLabel.alignment = TextAlignmentOptions.Center;
            _allDeadLabel.color = Color.white;
            _allDeadLabel.raycastTarget = false;
            _allDeadLabel.textWrappingMode = TextWrappingModes.NoWrap;
            _allDeadLabel.overflowMode = TextOverflowModes.Overflow;
            if (player != null && player.messageTxt != null)
                _allDeadLabel.font = player.messageTxt.font;
        }

        static readonly AccessTools.FieldRef<FPP_Player, bool> Ressurecting = AccessTools.FieldRefAccess<FPP_Player, bool>("ressurecting");

        static void BuildReviveUi(FPP_Player player)
        {
            _reviveCanvas = new GameObject("SelfReviveCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            UnityEngine.Object.DontDestroyOnLoad(_reviveCanvas);
            Canvas canvas = _reviveCanvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            CanvasScaler scaler = _reviveCanvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var panel = new GameObject("SelfRevivePanel", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            panel.transform.SetParent(_reviveCanvas.transform, false);
            _revivePanel = (RectTransform)panel.transform;
            _revivePanel.anchorMin = new Vector2(0.5f, 1f);
            _revivePanel.anchorMax = new Vector2(0.5f, 1f);
            _revivePanel.pivot = new Vector2(0.5f, 1f);
            _revivePanel.anchoredPosition = new Vector2(0f, -100f);
            _reviveBg = panel.GetComponent<Image>();
            _reviveBg.color = _revivePromptColor;
            _reviveBg.raycastTarget = false;
            var layout = panel.GetComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(18, 18, 6, 6);
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            var fitter = panel.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var labelObj = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObj.transform.SetParent(panel.transform, false);
            _reviveLabel = labelObj.GetComponent<TextMeshProUGUI>();
            _reviveLabel.raycastTarget = false;
            _reviveLabel.textWrappingMode = TextWrappingModes.NoWrap;
            _reviveLabel.alignment = TextAlignmentOptions.Center;
            _reviveLabel.fontSize = 24f;
            _reviveLabel.color = Color.white;
            if (player.messageTxt != null)
                _reviveLabel.font = player.messageTxt.font;
            _reviveLabel.text = RevivePromptText;
            _reviveStyledFor = null;
            Log.LogInfo("Self revive notification created.");
        }

        static void StyleRevivePanel(SpectateManager spectate)
        {
            if (spectate == null || spectate == _reviveStyledFor)
                return;
            _reviveStyledFor = spectate;
            TextMeshProUGUI source = spectate.nicknameUI;
            if (source != null && source.font != null)
            {
                _reviveLabel.font = source.font;
                _reviveLabel.fontSharedMaterial = source.font.material;
                if (source.fontSize > 8f)
                    _reviveLabel.fontSize = source.fontSize;
                Color c = source.color;
                c.a = 1f;
                _reviveLabel.color = c;
            }
            Image sourceBg = spectate.nicknameObjUI != null ? spectate.nicknameObjUI.GetComponent<Image>() : null;
            if (sourceBg != null)
            {
                _reviveBg.sprite = sourceBg.sprite;
                _reviveBg.type = sourceBg.type;
                _reviveBg.pixelsPerUnitMultiplier = sourceBg.pixelsPerUnitMultiplier;
                Color bg = sourceBg.color;
                bg.a = Mathf.Max(bg.a, 0.6f);
                _revivePromptColor = bg;
            }
        }

        static void PlaceRevivePanel(SpectateManager spectate)
        {
            var canvasRect = (RectTransform)_reviveCanvas.transform;
            GameObject nickname = spectate != null ? spectate.nicknameObjUI : null;
            if (nickname != null && nickname.activeInHierarchy)
            {
                var corners = new Vector3[4];
                ((RectTransform)nickname.transform).GetWorldCorners(corners);
                Canvas source = nickname.GetComponentInParent<Canvas>();
                Camera cam = source != null && source.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? source.rootCanvas.worldCamera : null;
                Vector2 bottom = RectTransformUtility.WorldToScreenPoint(cam, (corners[0] + corners[3]) * 0.5f);
                bool onScreen = bottom.x >= 0f && bottom.y >= 0f && bottom.x <= Screen.width && bottom.y <= Screen.height;
                if (onScreen && RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, bottom, null, out Vector2 local))
                {
                    _reviveSpot = new Vector2(local.x, local.y - 8f - canvasRect.rect.height * 0.5f);
                    _reviveSpotKnown = true;
                }
            }
            _revivePanel.anchoredPosition = _reviveSpotKnown ? _reviveSpot : new Vector2(0f, -canvasRect.rect.height * 0.085f);
        }


        const string HomeScene = "PlayerHideout";
        const float HomeSellHold = 1f;
        static float _homeSellHeld;
        static float _homeSellDoneAt;
        static bool _homeSellTold;

        static void TickHomeSell(FPP_Player player, WorldManager world)
        {
            if (player == null || world == null || !IsHost(world) || player.isDead)
            {
                _homeSellHeld = 0f;
                _homeSellTold = false;
                return;
            }
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != HomeScene)
            {
                _homeSellHeld = 0f;
                _homeSellTold = false;
                return;
            }
            Computer computer = Computer.Instance;
            if (player.pauseActive || player.chatActive || (computer != null && computer.computerActive))
            {
                _homeSellHeld = 0f;
                _homeSellTold = false;
                return;
            }
            if (!KeyHeld(KeyCode.Y))
            {
                _homeSellHeld = 0f;
                _homeSellTold = false;
                return;
            }
            if (Time.unscaledTime < _homeSellDoneAt)
                return;
            if (!_homeSellTold)
            {
                _homeSellTold = true;
                Tell("按住出售家里地上的物品", 1.2f, "good");
            }
            _homeSellHeld += Time.unscaledDeltaTime;
            if (_homeSellHeld < HomeSellHold)
                return;
            _homeSellHeld = 0f;
            _homeSellTold = false;
            _homeSellDoneAt = Time.unscaledTime + 1.5f;
            SellHomeFloorLoot(player);
        }

        static bool InDropOffBox(Pickupable pickup, DropOffBox[] boxes)
        {
            if (pickup == null || boxes == null)
                return false;
            Vector3 pos = pickup.transform.position;
            for (int i = 0; i < boxes.Length; i++)
            {
                DropOffBox box = boxes[i];
                if (box == null)
                    continue;
                Collider[] cols = box.GetComponentsInChildren<Collider>(true);
                for (int c = 0; c < cols.Length; c++)
                {
                    if (cols[c] != null && cols[c].enabled && cols[c].bounds.Contains(pos))
                        return true;
                }
            }
            return false;
        }

        static bool CanSellHomeLoot(Pickupable pickup, FPP_Player player, DropOffBox[] boxes)
        {
            if (pickup == null || pickup.assignedItem == null || pickup.blockPickup)
                return false;
            Item item = pickup.assignedItem;
            if (!item.isLoot || item.isMoney || item.isWeapon || item.isKey || item.isAmmo || item.isFirstAid)
                return false;
            if (item.isSkillBook || item.isArmorVest || item.isDrone || item.isItemUpgrade || item.isCarFuel)
                return false;
            if (item.isAccessCard || item.isVoucher || item.isGoldenCard)
                return false;
            if (pickup.doNotDespawn)
                return false;
            Transform t = pickup.transform;
            if (player != null && t.IsChildOf(player.transform))
                return false;
            if (t.root != null && t.root.CompareTag("SpawnLootTruck"))
                return false;
            if (InDropOffBox(pickup, boxes) || IsItemRequestLoot(pickup))
                return false;
            return true;
        }

        static bool SameRequestItem(Item a, Item b)
        {
            if (a == null || b == null)
                return false;
            if (a == b)
                return true;
            try
            {
                string na = a.GetItemName(true);
                string nb = b.GetItemName(true);
                if (!string.IsNullOrEmpty(na) && na == nb)
                    return true;
            }
            catch (Exception)
            {
            }
            return !string.IsNullOrEmpty(a.itemName) && a.itemName == b.itemName;
        }

        static bool MeetsRequestQuality(Pickupable pickup, int requiredQuality)
        {
            try
            {
                return pickup != null && pickup.currentDamage <= 100 - requiredQuality;
            }
            catch (Exception)
            {
                return false;
            }
        }

        static List<ItemRequestUI> AllItemRequests()
        {
            List<ItemRequestUI> lists = new List<ItemRequestUI>();
            ItemRequestUI[] found = UnityEngine.Object.FindObjectsOfType<ItemRequestUI>(true);
            for (int i = 0; i < found.Length; i++)
            {
                if (found[i] != null && !lists.Contains(found[i]))
                    lists.Add(found[i]);
            }
            RequestPage[] pages = UnityEngine.Object.FindObjectsOfType<RequestPage>(true);
            for (int p = 0; p < pages.Length; p++)
            {
                RequestPage page = pages[p];
                if (page == null || page.allRequests == null)
                    continue;
                for (int i = 0; i < page.allRequests.Count; i++)
                {
                    ItemRequestUI list = page.allRequests[i];
                    if (list != null && !lists.Contains(list))
                        lists.Add(list);
                }
            }
            return lists;
        }

        static bool IsItemRequestLoot(Pickupable pickup)
        {
            if (pickup == null || pickup.assignedItem == null)
                return false;
            Item item = pickup.assignedItem;
            List<ItemRequestUI> lists = AllItemRequests();
            for (int i = 0; i < lists.Count; i++)
            {
                ItemRequestUI list = lists[i];
                bool done = false;
                try
                {
                    done = list.isCompleted;
                }
                catch (Exception)
                {
                }
                if (done)
                    continue;
                int quality = 0;
                try
                {
                    quality = list.requiredQuality;
                }
                catch (Exception)
                {
                }
                bool matchedOpenSlot = false;
                if (list.spawnedItems != null)
                {
                    for (int s = 0; s < list.spawnedItems.Count; s++)
                    {
                        RequestedItemUI row = list.spawnedItems[s];
                        if (row == null)
                            continue;
                        if (row.itemOnGround == pickup)
                            return true;
                        if (!SameRequestItem(row.assignedItem, item))
                            continue;
                        if (row.hasItem && row.itemOnGround != pickup)
                            continue;
                        matchedOpenSlot = true;
                    }
                }
                if (!matchedOpenSlot && list.requests != null)
                {
                    for (int r = 0; r < list.requests.Count; r++)
                    {
                        ItemRequest req = list.requests[r];
                        if (req != null && SameRequestItem(req.requestedItem, item))
                            matchedOpenSlot = true;
                    }
                }
                if (matchedOpenSlot && MeetsRequestQuality(pickup, quality))
                    return true;
            }
            return false;
        }

        static int LootCashValue(Pickupable pickup, WorldManager world)
        {
            try
            {
                float worth = pickup.assignedItem.value;
                float dmg = pickup.currentDamage;
                if (dmg > 0.01f)
                    worth *= Mathf.Clamp01(1f - dmg / 100f);
                if (world != null && world.lootValueMulti > 0.01f)
                    worth *= world.lootValueMulti;
                int cash = Mathf.RoundToInt(worth);
                return cash < 0 ? 0 : cash;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        static void SellHomeFloorLoot(FPP_Player player)
        {
            WorldManager world = WorldManager.Instance;
            DropOffBox[] boxes = UnityEngine.Object.FindObjectsOfType<DropOffBox>(true);
            Pickupable[] all = UnityEngine.Object.FindObjectsOfType<Pickupable>();
            int count = 0;
            int cash = 0;
            for (int i = 0; i < all.Length; i++)
            {
                Pickupable pickup = all[i];
                if (!CanSellHomeLoot(pickup, player, boxes))
                    continue;
                cash += LootCashValue(pickup, world);
                count++;
                try
                {
                    pickup.RPC_Destroy(true, true);
                }
                catch (Exception)
                {
                    UnityEngine.Object.Destroy(pickup.gameObject);
                }
            }
            if (count <= 0)
            {
                Tell("没有可出售的地上物品", 3f, "bad");
                return;
            }
            if (cash > 0 && world != null)
            {
                try
                {
                    world.RPC_AddMoney(cash);
                }
                catch (Exception)
                {
                    world.sharedMoney += cash;
                }
            }
            Tell("出售" + count + "件，+$" + cash, 4f, "good");
            Log.LogInfo("Sold " + count + " home items for $" + cash + " shared money. Balance " + (world != null ? world.sharedMoney : cash));
        }

        static readonly int[] StreetBillAmounts = { 1, 5, 10, 20, 50, 100 };
        const int StreetBillCount = 10;
        const float StreetBillSpacing = 8f;
        static readonly List<NetworkObject> _streetBills = new List<NetworkObject>();
        static NetworkObject _lastSpawnedBill;
        static string _streetScene;
        static float _streetAt;
        static bool _streetChecked;
        static int _streetDay = int.MinValue;

        static void StreetCash(FPP_Player player, WorldManager world)
        {
            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (scene != _streetScene)
            {
                _streetScene = scene;
                _streetAt = Time.unscaledTime + 5f;
                _streetChecked = false;
            }
            if (_streetChecked || Time.unscaledTime < _streetAt || player.isDead || world.loadingScene)
                return;
            if (!IsHost(world) || !world.isReadyToPlay())
                return;
            _streetChecked = true;
            int day = world.currentDay;
            if (!IsJobMap(world, scene))
            {
                Log.LogInfo("Street bills: " + scene + " is not a job map, none spawned.");
                return;
            }
            if (day == _streetDay)
            {
                Log.LogInfo("Street bills: already spawned for day " + day + ", none on " + scene + ".");
                return;
            }
            _streetDay = day;
            SpawnStreetBills(player, scene, day);
        }

        static bool IsJobMap(WorldManager world, string scene)
        {
            if (string.IsNullOrEmpty(scene) || scene == HomeScene)
                return false;
            string lower = scene.ToLowerInvariant();
            if (lower.Contains("menu") || lower.Contains("prison") || lower.Contains("tutorial") || lower.Contains("logo") || lower.Contains("benchmark"))
                return false;
            return true;
        }

        static void SpawnStreetBills(FPP_Player player, string scene, int day)
        {
            NetworkRunner runner = player.Runner;
            if (runner == null)
                return;
            foreach (NetworkObject old in _streetBills)
            {
                try
                {
                    if (old != null && old.IsValid)
                        runner.Despawn(old);
                }
                catch (Exception)
                {
                }
            }
            _streetBills.Clear();

            LoadCatalog();
            var nodes = new List<GraphNode>();
            if (AstarPath.active != null && AstarPath.active.data != null)
            {
                AstarPath.active.data.GetNodes(node =>
                {
                    if (node != null && node.Walkable)
                        nodes.Add(node);
                });
            }
            if (nodes.Count == 0)
            {
                Log.LogWarning("No walkable area on " + scene + ", no street bills.");
                return;
            }
            int buildings;
            List<GraphNode> nearBuildings = NodesNearBuildings(nodes, out buildings);
            if (nearBuildings.Count >= StreetBillCount * 4)
                nodes = nearBuildings;
            else
                Log.LogWarning("Street bills: only " + nearBuildings.Count + " walkable spots near buildings on " + scene + ", using the whole map.");
            var cumulative = new float[nodes.Count];
            float total = 0f;
            for (int i = 0; i < nodes.Count; i++)
            {
                float area = nodes[i].SurfaceArea();
                total += area > 0f ? area : 1f;
                cumulative[i] = total;
            }

            Vector3 origin = player.transform.position;
            GraphNode start = AstarPath.active.GetNearest(origin).node;
            var placed = new List<Vector3>();
            var rejected = new int[6];
            int want = StreetBillCount;
            for (int tries = 0; placed.Count < want && tries < want * 60; tries++)
            {
                int index = Array.BinarySearch(cumulative, UnityEngine.Random.value * total);
                if (index < 0)
                    index = ~index;
                GraphNode node = nodes[Mathf.Clamp(index, 0, nodes.Count - 1)];
                int reason = StreetSpot(node.RandomPointOnSurface(), node, origin, start, placed, out Vector3 pos, out Vector3 normal);
                if (reason >= 0)
                {
                    rejected[reason]++;
                    continue;
                }
                int amount = StreetBillAmounts[UnityEngine.Random.Range(0, StreetBillAmounts.Length)];
                GameObject prefab = BillVisual(amount);
                if (prefab == null)
                    break;
                if (!SpawnBillAt(runner, prefab, amount, pos, normal, true))
                    continue;
                placed.Add(pos);
                if (_lastSpawnedBill != null)
                    _streetBills.Add(_lastSpawnedBill);
            }
            Log.LogInfo("Spawned " + placed.Count + "/" + want + " street bills on " + scene + " for day " + day
                + " near " + buildings + " doors and cabinets. Skipped spots: crowded " + rejected[0] + ", roofed " + rejected[1] + ", no ground " + rejected[2]
                + ", steep or loose " + rejected[3] + ", map edge " + rejected[4] + ", unreachable " + rejected[5] + ".");
        }

        const float BuildingReach = 20f;

        static List<GraphNode> NodesNearBuildings(List<GraphNode> nodes, out int buildings)
        {
            var cells = new Dictionary<long, List<Vector3>>();
            buildings = 0;
            var anchors = new List<Component>();
            anchors.AddRange(UnityEngine.Object.FindObjectsOfType<Openable>());
            anchors.AddRange(UnityEngine.Object.FindObjectsOfType<Door>());
            anchors.AddRange(UnityEngine.Object.FindObjectsOfType<DoorScript>());
            foreach (Component anchor in anchors)
            {
                if (anchor == null)
                    continue;
                Vector3 p = anchor.transform.position;
                long key = CellKey(Mathf.FloorToInt(p.x / BuildingReach), Mathf.FloorToInt(p.z / BuildingReach));
                if (!cells.TryGetValue(key, out List<Vector3> list))
                    cells[key] = list = new List<Vector3>();
                list.Add(p);
                buildings++;
            }

            var near = new List<GraphNode>();
            if (buildings == 0)
                return near;
            float reach = BuildingReach * BuildingReach;
            foreach (GraphNode node in nodes)
            {
                Vector3 p = (Vector3)node.position;
                int cx = Mathf.FloorToInt(p.x / BuildingReach), cz = Mathf.FloorToInt(p.z / BuildingReach);
                bool close = false;
                for (int dx = -1; dx <= 1 && !close; dx++)
                {
                    for (int dz = -1; dz <= 1 && !close; dz++)
                    {
                        if (!cells.TryGetValue(CellKey(cx + dx, cz + dz), out List<Vector3> list))
                            continue;
                        foreach (Vector3 a in list)
                        {
                            float ax = a.x - p.x, az = a.z - p.z;
                            if (ax * ax + az * az <= reach && Mathf.Abs(a.y - p.y) < 8f)
                            {
                                close = true;
                                break;
                            }
                        }
                    }
                }
                if (close)
                    near.Add(node);
            }
            return near;
        }

        static long CellKey(int x, int z)
        {
            return ((long)x << 32) ^ (uint)z;
        }

        static int StreetSpot(Vector3 nav, GraphNode node, Vector3 origin, GraphNode start, List<Vector3> placed, out Vector3 pos, out Vector3 normal)
        {
            pos = nav;
            normal = Vector3.up;
            if ((nav - origin).sqrMagnitude < 100f)
                return 0;
            foreach (Vector3 other in placed)
            {
                if ((other - nav).sqrMagnitude < StreetBillSpacing * StreetBillSpacing)
                    return 0;
            }
            if (Physics.Raycast(nav + Vector3.up * 0.5f, Vector3.up, 25f, ~0, QueryTriggerInteraction.Ignore))
                return 1;
            if (!Physics.Raycast(nav + Vector3.up * 2.5f, Vector3.down, out RaycastHit hit, 5f, ~0, QueryTriggerInteraction.Ignore))
                return 2;
            if (hit.normal.y < 0.7f || (hit.rigidbody != null && !hit.rigidbody.isKinematic) || hit.collider.gameObject.layer == LayerMask.NameToLayer("Water"))
                return 3;
            foreach (Collider zone in Physics.OverlapSphere(hit.point, 4f, ~0, QueryTriggerInteraction.Collide))
            {
                if (zone.CompareTag("MapExit") || zone.GetComponentInParent<OutOfMapTrigger>() != null || zone.GetComponentInParent<KillOutOfMap>() != null)
                    return 4;
            }
            if (start != null && !PathUtilities.IsPathPossible(start, node))
                return 5;
            pos = hit.point;
            normal = hit.normal;
            return -1;
        }

        static void DropCash(FPP_Player player)
        {
            LoadCatalog();
            int have = Cash.Value;
            var options = new List<int>();
            for (int i = 0; i < BillAmounts.Length; i++)
            {
                if (BillAmounts[i] <= have)
                    options.Add(BillAmounts[i]);
            }
            if (options.Count == 0)
            {
                Tell("现金不够");
                return;
            }

            int amount = options[UnityEngine.Random.Range(0, options.Count)];
            GameObject prefab = BillVisual(amount);
            NetworkRunner runner = player.Runner;
            if (prefab == null || runner == null || !TrySurface(player, out Vector3 pos, out Vector3 normal))
                return;
            if (!SpawnBillAt(runner, prefab, amount, pos, normal))
                return;
            Cash.Add(-amount);
            _cashShown = int.MinValue;
            Log.LogInfo("Dropped a " + amount + " bill. Balance " + Cash.Value);
        }

        static void BeforePickup(PlayerInventory __instance, Pickupable item)
        {
            if (item == null || item.assignedItem == null || !item.assignedItem.isMoney)
                return;

            _blockFrame = Time.frameCount;
            WorldManager world = WorldManager.Instance;
            if (world == null || world.localPlayer == null || __instance.player != world.localPlayer)
                return;

            int worth = BillWorth(item);
            if (worth <= 0)
                return;
            Cash.Add(worth);
            Log.LogInfo("Picked up " + worth + " cash. Balance " + Cash.Value);
        }

        static void AfterPickup()
        {
            _blockFrame = -1;
        }

        static bool BlockMoneyQuota()
        {
            if (_blockFrame != Time.frameCount)
                return true;
            _blockFrame = -1;
            return false;
        }

        static int BillWorth(Pickupable item)
        {
            try
            {
                if (TryCashTag(item.customProperty, out int tagged, out _))
                    return tagged;
            }
            catch (Exception)
            {
            }

            return item.addAmount > 0 ? item.addAmount : 0;
        }

        static void RefreshCashLabel(FPP_Player player)
        {
            TextMeshProUGUI money = player.moneyTxt;
            if (money == null)
                return;

            int id = money.GetInstanceID();
            if (_cashLabel == null || _moneyId != id)
            {
                _moneyId = id;
                if (_cashLabel != null)
                    Destroy(_cashLabel.gameObject);

                var go = new GameObject("SkillCash", typeof(RectTransform));
                var rect = go.GetComponent<RectTransform>();
                rect.SetParent(money.rectTransform.parent, false);
                rect.localScale = Vector3.one;
                rect.localRotation = Quaternion.identity;
                var layout = go.AddComponent<LayoutElement>();
                layout.ignoreLayout = true;
                _cashLabel = go.AddComponent<TextMeshProUGUI>();
                _cashLabel.font = money.font;
                _cashLabel.fontSharedMaterial = money.fontSharedMaterial;
                _cashLabel.fontSize = money.fontSize;
                _cashLabel.color = Gold;
                _cashLabel.alignment = TextAlignmentOptions.MidlineRight;
                _cashLabel.richText = true;
                _cashLabel.raycastTarget = false;
                PlaceCashLabel(money);
                _cashShown = int.MinValue;
            }

            if (_cashShown == Cash.Value && _streetShown == _streetLeft)
                return;
            _cashShown = Cash.Value;
            _streetShown = _streetLeft;
            string street = _streetLeft > 0 ? "<color=#F2F2F2>街上还有" + _streetLeft + "张</color>  " : "";
            _cashLabel.text = street + "<color=#FFC440>" + Cash.Value + "现金</color>  <color=#F2F2F2>|</color>";
            if (_pageCash != null)
                _pageCash.text = Cash.Value + "现金";
        }

        static void PlaceCashLabel(TextMeshProUGUI money)
        {
            RectTransform rect = _cashLabel.rectTransform;
            RectTransform source = money.rectTransform;
            rect.anchorMin = source.anchorMin;
            rect.anchorMax = source.anchorMax;
            rect.pivot = new Vector2(1f, source.pivot.y);
            float height = source.sizeDelta.y > 1f ? source.sizeDelta.y : source.rect.height;
            if (height < 8f)
                height = 28f;
            rect.sizeDelta = new Vector2(190f, height);
            float moneyLeft = source.anchoredPosition.x - source.rect.width * source.pivot.x;
            if (source.rect.width < 1f)
                moneyLeft = source.anchoredPosition.x - 70f;
            rect.anchoredPosition = new Vector2(moneyLeft - 2f, source.anchoredPosition.y);
        }

        static void EnsureBookButton()
        {
            Computer computer = Computer.Instance;
            if (computer == null)
                return;
            int id = computer.GetInstanceID();
            if (_computerId != id)
            {
                _computerId = id;
                _buttonMade = false;
                _page = null;
                Rows.Clear();
            }
            if (_buttonMade || Time.unscaledTime < _nextSearch)
                return;
            _nextSearch = Time.unscaledTime + 1f;

            TextMeshProUGUI[] labels = computer.GetComponentsInChildren<TextMeshProUGUI>(true);
            TextMeshProUGUI custom = FindLabel(labels, "定制", "custom");
            TextMeshProUGUI furniture = FindLabel(labels, "家具", "furniture");
            if (custom == null)
                return;

            Button source = custom.GetComponentInParent<Button>();
            if (source == null)
                return;

            var clone = Instantiate(source.gameObject, source.transform.parent);
            clone.name = "SkillBooksButton";
            foreach (var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null)
                    continue;
                string typeName = behaviour.GetType().Name;
                if (behaviour is Button || behaviour is Graphic || typeName.Contains("Layout"))
                    continue;
                behaviour.enabled = false;
            }

            TextMeshProUGUI shopLabel = FindLabel(labels, "商店", "shop");
            TextMeshProUGUI cloneLabel = clone.GetComponentInChildren<TextMeshProUGUI>(true);
            if (cloneLabel != null)
            {
                cloneLabel.text = "七七商店";
                cloneLabel.color = shopLabel != null ? shopLabel.color : Color.white;
            }
            if (shopLabel != null)
            {
                Button shopButton = shopLabel.GetComponentInParent<Button>();
                if (shopButton != null)
                    CopyShopLook(shopButton.gameObject, clone);
            }

            WebsiteButton website = clone.GetComponent<WebsiteButton>();
            if (website != null)
            {
                if (website.normalObj != null)
                    website.normalObj.SetActive(true);
                if (website.lockedObj != null)
                    website.lockedObj.SetActive(false);
                website.enabled = false;
            }

            Button button = clone.GetComponent<Button>();
            button.interactable = true;
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(OpenShop);

            var grid = source.transform.parent.GetComponent<GridLayoutGroup>();
            if (grid == null)
            {
                Vector3 step = new Vector3(source.GetComponent<RectTransform>().rect.width + 12f, 0f, 0f);
                if (furniture != null)
                {
                    Button furnitureButton = furniture.GetComponentInParent<Button>();
                    if (furnitureButton != null)
                        step = source.transform.localPosition - furnitureButton.transform.localPosition;
                }
                if (step.sqrMagnitude < 4f)
                    step = new Vector3(160f, 0f, 0f);
                clone.transform.localPosition = source.transform.localPosition + step;
                clone.transform.localRotation = source.transform.localRotation;
                clone.transform.localScale = source.transform.localScale;
            }

            BuildPage(source, cloneLabel != null ? cloneLabel.font : custom.font, custom.fontSize);
            _buttonMade = true;
            Log.LogInfo("Skill book button added.");
        }

        static TextMeshProUGUI FindLabel(TextMeshProUGUI[] labels, string chinese, string english)
        {
            for (int i = 0; i < labels.Length; i++)
            {
                TextMeshProUGUI label = labels[i];
                if (label == null || string.IsNullOrEmpty(label.text))
                    continue;
                if (label.text.Contains(chinese) || label.text.IndexOf(english, StringComparison.OrdinalIgnoreCase) >= 0)
                    return label;
            }
            return null;
        }

        static void BuildPage(Button source, TMP_FontAsset font, float fontSize)
        {
            Canvas canvas = source.GetComponentInParent<Canvas>();
            if (canvas == null)
                return;

            _page = new GameObject("SkillBookPage", typeof(RectTransform), typeof(Image));
            var pageRect = _page.GetComponent<RectTransform>();
            pageRect.SetParent(canvas.transform, false);
            pageRect.anchorMin = Vector2.zero;
            pageRect.anchorMax = Vector2.one;
            pageRect.offsetMin = new Vector2(36f, 28f);
            pageRect.offsetMax = new Vector2(-36f, -28f);
            pageRect.localScale = Vector3.one;
            _page.GetComponent<Image>().color = new Color(0.03f, 0.04f, 0.07f, 0.96f);

            var title = MakeText("Title", pageRect, font, fontSize + 6f, Gold, TextAlignmentOptions.Left);
            title.text = "七七商店";
            SetBar(title.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(16f, -8f), new Vector2(-8f, -48f));

            _pageCash = MakeText("PageCash", pageRect, font, fontSize, Gold, TextAlignmentOptions.Right);
            _pageCash.text = Cash.Value + "现金";
            SetBar(_pageCash.rectTransform, new Vector2(0.5f, 1f), new Vector2(1f, 1f), new Vector2(8f, -8f), new Vector2(-16f, -48f));

            var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect), typeof(Image));
            var scrollRect = scrollGo.GetComponent<RectTransform>();
            scrollRect.SetParent(pageRect, false);
            scrollRect.anchorMin = Vector2.zero;
            scrollRect.anchorMax = Vector2.one;
            scrollRect.offsetMin = new Vector2(12f, 30f);
            scrollRect.offsetMax = new Vector2(-12f, -76f);
            scrollGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.2f);
            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.scrollSensitivity = 40f;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D), typeof(Image));
            var viewRect = viewport.GetComponent<RectTransform>();
            viewRect.SetParent(scrollRect, false);
            viewRect.anchorMin = Vector2.zero;
            viewRect.anchorMax = Vector2.one;
            viewRect.offsetMin = Vector2.zero;
            viewRect.offsetMax = Vector2.zero;
            viewport.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.01f);
            scroll.viewport = viewRect;

            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.SetParent(viewRect, false);
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(0f, 0f);
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 4f;
            layout.padding = new RectOffset(6, 6, 6, 6);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = content.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = contentRect;
            _scroll = scroll;
            _bookContent = contentRect;

            var tabBar = new GameObject("Tabs", typeof(RectTransform));
            var tabRect = tabBar.GetComponent<RectTransform>();
            tabRect.SetParent(pageRect, false);
            SetBar(tabRect, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -68f), new Vector2(-16f, -52f));
            _tabs[0] = MakeTab(tabRect, "技能书", font, fontSize, new Vector2(0f, 0f), new Vector2(0.32f, 1f), () => ShowTab(0));
            _tabs[1] = MakeTab(tabRect, "武器", font, fontSize, new Vector2(0.34f, 0f), new Vector2(0.66f, 1f), () => ShowTab(1));
            _tabs[2] = MakeTab(tabRect, "升级", font, fontSize, new Vector2(0.68f, 0f), new Vector2(1f, 1f), () => ShowTab(2));

            _weaponContent = MakeList("Weapons", viewRect);
            var gearSorted = new List<Book>(Gear);
            gearSorted.Sort(CompareBooks);
            for (int i = 0; i < gearSorted.Count; i++)
                GearRows.Add(MakeRow(_weaponContent, gearSorted[i], font, fontSize));
            _xpContent = MakeList("Xp", viewRect);
            for (int i = 0; i < 3; i++)
                MakeXpRow(_xpContent, i, font, fontSize);
            for (int i = 0; i < UpgradeRowOrder.Length; i++)
                MakeUpgradeRow(_xpContent, UpgradeRowOrder[i], font, fontSize);

            ResolveBooks();
            var sorted = new List<Book>(Books);
            sorted.Sort(CompareBooks);
            for (int i = 0; i < sorted.Count; i++)
                Rows.Add(MakeRow(contentRect, sorted[i], font, fontSize));
            ShowTab(0);

            var bottom = new GameObject("Bottom", typeof(RectTransform));
            var bottomRect = bottom.GetComponent<RectTransform>();
            bottomRect.SetParent(pageRect, false);
            SetBar(bottomRect, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(12f, 6f), new Vector2(-12f, 24f));

            _cartText = MakeText("Cart", bottomRect, font, fontSize, Color.white, TextAlignmentOptions.MidlineLeft);
            _cartText.enableAutoSizing = true;
            _cartText.fontSizeMin = 8f;
            _cartText.fontSizeMax = fontSize;
            _cartText.text = "已选 0 样";
            var cartRect = _cartText.rectTransform;
            cartRect.anchorMin = Vector2.zero;
            cartRect.anchorMax = new Vector2(0.55f, 1f);
            cartRect.offsetMin = Vector2.zero;
            cartRect.offsetMax = Vector2.zero;

            MakeAction(bottomRect, "结账", font, fontSize, new Color(0.25f, 0.55f, 0.28f, 1f), new Vector2(0.58f, 0f), new Vector2(0.78f, 1f), Checkout);
            MakeAction(bottomRect, "返回", font, fontSize, new Color(0.28f, 0.3f, 0.36f, 1f), new Vector2(0.8f, 0f), new Vector2(0.98f, 1f), CloseShop);
            _page.SetActive(false);
        }

        static Row MakeRow(RectTransform parent, Book book, TMP_FontAsset font, float fontSize)
        {
            var go = new GameObject(book.Name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.1f, 0.11f, 0.15f, 1f);
            var element = go.GetComponent<LayoutElement>();
            element.minHeight = fontSize + 16f;
            element.preferredHeight = fontSize + 16f;
            var rowLayout = go.GetComponent<HorizontalLayoutGroup>();
            rowLayout.padding = new RectOffset(10, 10, 2, 2);
            rowLayout.spacing = 8f;
            rowLayout.childAlignment = TextAnchor.MiddleLeft;
            rowLayout.childControlWidth = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandHeight = true;

            var name = MakeText("Name", rect, font, fontSize, book.Item == null ? new Color(0.55f, 0.55f, 0.55f, 1f) : Color.white, TextAlignmentOptions.MidlineLeft);
            name.text = book.Name;
            var nameElement = name.gameObject.AddComponent<LayoutElement>();
            nameElement.flexibleWidth = 1f;

            var price = MakeText("Price", rect, font, fontSize, Gold, TextAlignmentOptions.MidlineRight);
            price.text = book.Price + "现金";
            var priceElement = price.gameObject.AddComponent<LayoutElement>();
            priceElement.preferredWidth = 110f;

            var qty = MakeText("Qty", rect, font, fontSize, Gold, TextAlignmentOptions.MidlineRight);
            qty.text = "";
            var qtyElement = qty.gameObject.AddComponent<LayoutElement>();
            qtyElement.preferredWidth = 48f;

            var minusGo = new GameObject("Minus", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            var minusRect = minusGo.GetComponent<RectTransform>();
            minusRect.SetParent(rect, false);
            minusGo.GetComponent<Image>().color = new Color(0.35f, 0.18f, 0.16f, 1f);
            var minusElement = minusGo.GetComponent<LayoutElement>();
            minusElement.preferredWidth = 36f;
            var minusLabel = MakeText("MinusLabel", minusRect, font, fontSize, Color.white, TextAlignmentOptions.Center);
            minusLabel.text = "-";
            minusLabel.rectTransform.anchorMin = Vector2.zero;
            minusLabel.rectTransform.anchorMax = Vector2.one;
            minusLabel.rectTransform.offsetMin = Vector2.zero;
            minusLabel.rectTransform.offsetMax = Vector2.zero;

            Book captured = book;
            go.GetComponent<Button>().onClick.AddListener(() => AddBook(captured));
            minusGo.GetComponent<Button>().onClick.AddListener(() => RemoveBook(captured));
            return new Row { Book = book, Name = name, Qty = qty };
        }

        static void MakeAction(RectTransform parent, string text, TMP_FontAsset font, float fontSize, Color color, Vector2 anchorMin, Vector2 anchorMax, UnityEngine.Events.UnityAction action)
        {
            var go = new GameObject(text, typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = color;
            var label = MakeText("Label", rect, font, fontSize, Color.white, TextAlignmentOptions.Center);
            label.text = text;
            label.enableAutoSizing = true;
            label.fontSizeMin = 8f;
            label.fontSizeMax = fontSize;
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;
            go.GetComponent<Button>().onClick.AddListener(action);
        }

        static TextMeshProUGUI MakeText(string name, RectTransform parent, TMP_FontAsset font, float fontSize, Color color, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = align;
            text.raycastTarget = false;
            return text;
        }

        static void SetBar(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        static void OpenShop()
        {
            if (_page == null)
                return;
            Computer computer = Computer.Instance;
            if (computer != null)
                computer.PlayMouseClick();
            ResolveBooks();
            ResolveGear();
            RefreshBookRows();
            RefreshGearRows();
            _page.SetActive(true);
            _page.transform.SetAsLastSibling();
            RefreshCartText();
        }

        static void CloseShop()
        {
            if (_page != null)
                _page.SetActive(false);
        }

        static void AddBook(Book book)
        {
            if (BookPrefab(book) == null)
            {
                if (IsGear(book))
                {
                    ResolveGear();
                    RefreshGearRows();
                }
                else
                {
                    ResolveBooks();
                    RefreshBookRows();
                }
            }
            if (BookPrefab(book) == null)
            {
                Tell(IsGear(book) ? "找不到这个" : "找不到这本书");
                return;
            }
            if (!Cart.ContainsKey(book.Name))
                Cart[book.Name] = 0;
            Cart[book.Name]++;
            RefreshCartText();
        }

        static void RemoveBook(Book book)
        {
            if (!Cart.TryGetValue(book.Name, out int qty) || qty <= 0)
                return;
            if (qty == 1)
                Cart.Remove(book.Name);
            else
                Cart[book.Name] = qty - 1;
            RefreshCartText();
        }

        static void RefreshCartText()
        {
            int count = 0;
            int total = 0;
            for (int i = 0; i < Books.Length; i++)
            {
                int qty = 0;
                Cart.TryGetValue(Books[i].Name, out qty);
                count += qty;
                total += qty * Books[i].Price;
                if (i < Rows.Count && Rows[i].Qty != null && Rows[i].Book == Books[i])
                    Rows[i].Qty.text = qty > 0 ? "x" + qty : "";
            }
            for (int i = 0; i < Rows.Count; i++)
            {
                if (Rows[i].Book == null || Rows[i].Qty == null)
                    continue;
                int qty = 0;
                Cart.TryGetValue(Rows[i].Book.Name, out qty);
                Rows[i].Qty.text = qty > 0 ? "x" + qty : "";
            }
            for (int i = 0; i < Gear.Length; i++)
            {
                int qty = 0;
                Cart.TryGetValue(Gear[i].Name, out qty);
                count += qty;
                total += qty * Gear[i].Price;
            }
            for (int i = 0; i < GearRows.Count; i++)
            {
                if (GearRows[i].Book == null || GearRows[i].Qty == null)
                    continue;
                int qty = 0;
                Cart.TryGetValue(GearRows[i].Book.Name, out qty);
                GearRows[i].Qty.text = qty > 0 ? "x" + qty : "";
            }
            for (int i = 0; i < 3; i++)
            {
                count += _xpCart[i];
                total += XpCartCost(i);
                if (_xpQty[i] != null)
                    _xpQty[i].text = _xpCart[i] > 0 ? "x" + _xpCart[i] : "";
                if (_xpPrice[i] != null)
                    _xpPrice[i].text = XpUnitPrice(i, _xpBought[i] + _xpCart[i]) + "现金";
            }
            for (int i = 0; i < UpgradeNames.Length; i++)
            {
                int n = _upCart[i];
                int sw = _upSwitch[i];
                count += n + sw;
                total += UpgradeCartCost(i) + sw * 100;
                int next = _upLevel[i] + n;
                bool equipped = IsZSkill(i) && _selectedSkill == i && _upLevel[i] > 0;
                if (_upName[i] != null)
                {
                    _upName[i].text = UpgradeLabel(i);
                    _upName[i].color = equipped ? new Color(0.08f, 0.06f, 0.02f, 1f) : Color.white;
                }
                if (_upQty[i] != null)
                {
                    if (sw > 0)
                        _upQty[i].text = "切换";
                    else
                        _upQty[i].text = n > 0 ? "x" + n : "";
                    _upQty[i].color = equipped ? new Color(0.08f, 0.06f, 0.02f, 1f) : Gold;
                }
                if (_upPrice[i] != null)
                {
                    if (sw > 0)
                        _upPrice[i].text = "换技能 100现金";
                    else if (next >= UpgradeMax[i])
                        _upPrice[i].text = "已满";
                    else
                        _upPrice[i].text = UpgradePrice(i, next) + "现金";
                    _upPrice[i].color = equipped ? new Color(0.08f, 0.06f, 0.02f, 1f) : Gold;
                }
                if (_upSwitchGo[i] != null)
                    _upSwitchGo[i].SetActive(IsZSkill(i) && _upLevel[i] > 0 && _selectedSkill != i);
                if (_upRowBg[i] != null)
                    _upRowBg[i].color = equipped
                        ? new Color(0.86f, 0.68f, 0.16f, 1f)
                        : new Color(0.1f, 0.11f, 0.15f, 1f);
            }
            if (_cartText != null)
                _cartText.text = "已选 " + count + " 样  共 " + total + "现金";
            if (_pageCash != null)
                _pageCash.text = Cash.Value + "现金";
        }

        static void Checkout()
        {
            int total = 0;
            var prefabs = new List<GameObject>();
            for (int i = 0; i < Books.Length; i++)
            {
                if (!Cart.TryGetValue(Books[i].Name, out int qty) || qty <= 0)
                    continue;
                GameObject drop = BookPrefab(Books[i]);
                if (drop == null)
                    continue;
                total += qty * Books[i].Price;
                for (int n = 0; n < qty; n++)
                    prefabs.Add(drop);
            }
            for (int i = 0; i < Gear.Length; i++)
            {
                if (!Cart.TryGetValue(Gear[i].Name, out int qty) || qty <= 0)
                    continue;
                GameObject drop = BookPrefab(Gear[i]);
                if (drop == null)
                    continue;
                total += qty * Gear[i].Price;
                for (int n = 0; n < qty; n++)
                    prefabs.Add(drop);
            }

            int xpTotal = 0;
            int xpPoints = 0;
            for (int i = 0; i < 3; i++)
            {
                if (_xpCart[i] <= 0)
                    continue;
                xpTotal += XpCartCost(i);
                xpPoints += _xpCart[i] * XpAmount[i];
            }
            total += xpTotal;

            int upgrades = 0;
            int switches = 0;
            for (int i = 0; i < UpgradeNames.Length; i++)
            {
                upgrades += _upCart[i];
                switches += _upSwitch[i];
                total += UpgradeCartCost(i) + _upSwitch[i] * 100;
            }

            if (prefabs.Count == 0 && xpPoints == 0 && upgrades == 0 && switches == 0)
            {
                Tell("还没有选择");
                return;
            }
            if (Cash.Value < total)
            {
                Tell("现金不够");
                return;
            }

            DroneDeliverySystem drone = null;
            if (prefabs.Count > 0)
            {
                drone = UnityEngine.Object.FindObjectOfType<DroneDeliverySystem>();
                if (drone == null)
                {
                    Tell("现在不能下单");
                    return;
                }
            }

            if (prefabs.Count > 0)
            {
                try
                {
                    drone.SpawnDrone(false, prefabs.ToArray());
                }
                catch (Exception ex)
                {
                    Log.LogError("Book delivery failed: " + ex.Message);
                    Tell("现在不能下单");
                    return;
                }
            }

            if (xpPoints > 0)
            {
                FPP_Player player = WorldManager.Instance != null ? WorldManager.Instance.localPlayer : null;
                if (player == null || player.skillManager == null)
                {
                    Tell("现在不能下单");
                    return;
                }
                player.skillManager.ReceiveXP(xpPoints);
                for (int i = 0; i < 3; i++)
                {
                    _xpBought[i] += _xpCart[i];
                    _xpCart[i] = 0;
                }
                SaveXpPrices();
            }

            bool skillSwitch = false;
            bool skillLevel = false;
            if (upgrades > 0 || switches > 0)
            {
                int switched = -1;
                for (int i = 0; i < UpgradeNames.Length; i++)
                {
                    if (_upSwitch[i] > 0 && _upLevel[i] > 0)
                    {
                        skillSwitch = true;
                        switched = i;
                    }
                    if (_upCart[i] > 0)
                    {
                        int before = _upLevel[i];
                        _upLevel[i] = Math.Min(UpgradeMax[i], _upLevel[i] + _upCart[i]);
                        if (IsZSkill(i) && before <= 0 && _upLevel[i] > 0)
                            switched = i;
                        else if (IsZSkill(i) && _upLevel[i] > before)
                            skillLevel = true;
                    }
                    _upCart[i] = 0;
                    _upSwitch[i] = 0;
                }
                SaveUpgrades();
                PushBagCapacity();
                RefreshVitals();
                BroadcastUpgrades(WorldManager.Instance != null ? WorldManager.Instance.localPlayer : null, WorldManager.Instance, true);
                if (switched >= 0)
                    SelectZSkill(switched);
                else if (_selectedSkill < SkillRepair || _selectedSkill > SkillStealth || _upLevel[_selectedSkill] <= 0)
                {
                    int first = FirstOwnedZSkill();
                    if (first >= 0)
                        SelectZSkill(first);
                }
            }

            Cash.Add(-total);
            Cart.Clear();
            RefreshCartText();
            _cashShown = int.MinValue;
            string done = "经验已购买";
            if (prefabs.Count > 0)
                done = "已下单";
            else if (skillLevel)
                done = "技能升级成功";
            else if (skillSwitch)
                done = "技能更换成功";
            else if (upgrades > 0)
                done = "升级成功";
            Tell(done);
            Log.LogInfo("Ordered for " + total + ". Balance " + Cash.Value);
        }

        static int CompareBooks(Book a, Book b)
        {
            int price = a.Price.CompareTo(b.Price);
            if (price != 0)
                return price;
            return string.Compare(a.Name, b.Name, StringComparison.Ordinal);
        }

        static Button MakeTab(RectTransform parent, string text, TMP_FontAsset font, float fontSize, Vector2 anchorMin, Vector2 anchorMax, UnityEngine.Events.UnityAction action)
        {
            var go = new GameObject(text, typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = new Color(0.16f, 0.17f, 0.22f, 1f);
            var label = MakeText("Label", rect, font, fontSize, Color.white, TextAlignmentOptions.Center);
            label.text = text;
            label.enableAutoSizing = true;
            label.fontSizeMin = 8f;
            label.fontSizeMax = fontSize;
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;
            Button button = go.GetComponent<Button>();
            button.onClick.AddListener(action);
            return button;
        }

        static RectTransform MakeList(string name, RectTransform viewport)
        {
            var content = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.SetParent(viewport, false);
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(0f, 0f);
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 4f;
            layout.padding = new RectOffset(6, 6, 6, 6);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            content.SetActive(false);
            return contentRect;
        }

        static void ShowTab(int tab)
        {
            if (_scroll == null)
                return;
            RectTransform content = tab == 1 ? _weaponContent : tab == 2 ? _xpContent : _bookContent;
            if (content != null)
                content.gameObject.SetActive(true);
            if (_bookContent != null && _bookContent != content)
                _bookContent.gameObject.SetActive(false);
            if (_weaponContent != null && _weaponContent != content)
                _weaponContent.gameObject.SetActive(false);
            if (_xpContent != null && _xpContent != content)
                _xpContent.gameObject.SetActive(false);
            _scroll.content = content;
            _scroll.verticalNormalizedPosition = 1f;
            for (int i = 0; i < _tabs.Length; i++)
            {
                if (_tabs[i] == null)
                    continue;
                Image image = _tabs[i].GetComponent<Image>();
                if (image != null)
                    image.color = i == tab ? new Color(0.85f, 0.62f, 0.12f, 1f) : new Color(0.16f, 0.17f, 0.22f, 1f);
            }
        }

        static void MakeXpRow(RectTransform parent, int tier, TMP_FontAsset font, float fontSize)
        {
            int captured = tier;
            MakeCounterRow(parent, XpAmount[tier] + " XP", font, fontSize, out _xpPrice[tier], out _xpQty[tier], () => AddXp(captured), () => RemoveXp(captured));
            _xpPrice[tier].text = XpUnitPrice(tier, _xpBought[tier]) + "现金";
        }

        static void MakeUpgradeRow(RectTransform parent, int kind, TMP_FontAsset font, float fontSize)
        {
            int captured = kind;
            _upName[kind] = MakeCounterRow(parent, UpgradeLabel(kind), font, fontSize, out _upPrice[kind], out _upQty[kind], () => AddUpgrade(captured), () => RemoveUpgrade(captured));
            if (_upName[kind] == null || _upName[kind].transform.parent == null)
                return;
            Transform row = _upName[kind].transform.parent;
            _upRowBg[kind] = row.GetComponent<Image>();
            if (!IsZSkill(kind))
                return;
            Button rowBtn = row.GetComponent<Button>();
            if (rowBtn != null)
            {
                rowBtn.onClick.RemoveAllListeners();
                UnityEngine.Object.DestroyImmediate(rowBtn);
            }
            AddSkillPlus(row, captured, font, fontSize);
            AddSkillSwitch(row, captured, font, fontSize);
        }

        static void AddSkillPlus(Transform row, int kind, TMP_FontAsset font, float fontSize)
        {
            Transform minus = row.Find("Minus");
            var plusGo = new GameObject("Plus", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            var plusRect = plusGo.GetComponent<RectTransform>();
            plusRect.SetParent(row, false);
            if (minus != null)
                plusGo.transform.SetSiblingIndex(minus.GetSiblingIndex());
            plusGo.GetComponent<Image>().color = new Color(0.18f, 0.35f, 0.22f, 1f);
            plusGo.GetComponent<LayoutElement>().preferredWidth = 36f;
            var plusLabel = MakeText("PlusLabel", plusRect, font, fontSize, Color.white, TextAlignmentOptions.Center);
            plusLabel.text = "+";
            plusLabel.raycastTarget = false;
            plusLabel.rectTransform.anchorMin = Vector2.zero;
            plusLabel.rectTransform.anchorMax = Vector2.one;
            plusLabel.rectTransform.offsetMin = Vector2.zero;
            plusLabel.rectTransform.offsetMax = Vector2.zero;
            int captured = kind;
            plusGo.GetComponent<Button>().onClick.AddListener(() => AddUpgrade(captured));
        }

        static void AddSkillSwitch(Transform row, int kind, TMP_FontAsset font, float fontSize)
        {
            Transform minus = row.Find("Minus");
            var go = new GameObject("Switch", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(row, false);
            if (minus != null)
                go.transform.SetSiblingIndex(minus.GetSiblingIndex());
            go.GetComponent<Image>().color = new Color(0.85f, 0.62f, 0.12f, 1f);
            go.GetComponent<LayoutElement>().preferredWidth = 36f;
            go.GetComponent<LayoutElement>().preferredHeight = 36f;
            var label = MakeText("SwitchLabel", rect, font, fontSize, Color.white, TextAlignmentOptions.Center);
            label.text = "切换";
            label.enableAutoSizing = true;
            label.fontSizeMin = 8f;
            label.fontSizeMax = fontSize;
            label.raycastTarget = false;
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;
            int captured = kind;
            go.GetComponent<Button>().onClick.AddListener(() => AddSwitch(captured));
            _upSwitchGo[kind] = go;
            go.SetActive(false);
        }

        static TextMeshProUGUI MakeCounterRow(RectTransform parent, string title, TMP_FontAsset font, float fontSize, out TextMeshProUGUI price, out TextMeshProUGUI qty, UnityEngine.Events.UnityAction add, UnityEngine.Events.UnityAction remove)
        {
            var go = new GameObject(title, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.1f, 0.11f, 0.15f, 1f);
            var element = go.GetComponent<LayoutElement>();
            element.minHeight = fontSize + 16f;
            element.preferredHeight = fontSize + 16f;
            var rowLayout = go.GetComponent<HorizontalLayoutGroup>();
            rowLayout.padding = new RectOffset(10, 10, 2, 2);
            rowLayout.spacing = 8f;
            rowLayout.childAlignment = TextAnchor.MiddleLeft;
            rowLayout.childControlWidth = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandHeight = true;

            var name = MakeText("Name", rect, font, fontSize, Color.white, TextAlignmentOptions.MidlineLeft);
            name.text = title;
            name.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            price = MakeText("Price", rect, font, fontSize, Gold, TextAlignmentOptions.MidlineRight);
            price.text = "";
            price.gameObject.AddComponent<LayoutElement>().preferredWidth = 110f;
            qty = MakeText("Qty", rect, font, fontSize, Gold, TextAlignmentOptions.MidlineRight);
            qty.text = "";
            qty.gameObject.AddComponent<LayoutElement>().preferredWidth = 48f;

            go.GetComponent<Button>().onClick.AddListener(add);
            var minusGo = new GameObject("Minus", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            var minusRect = minusGo.GetComponent<RectTransform>();
            minusRect.SetParent(rect, false);
            minusGo.GetComponent<Image>().color = new Color(0.35f, 0.18f, 0.16f, 1f);
            minusGo.GetComponent<LayoutElement>().preferredWidth = 36f;
            var minusLabel = MakeText("MinusLabel", minusRect, font, fontSize, Color.white, TextAlignmentOptions.Center);
            minusLabel.text = "-";
            minusLabel.rectTransform.anchorMin = Vector2.zero;
            minusLabel.rectTransform.anchorMax = Vector2.one;
            minusLabel.rectTransform.offsetMin = Vector2.zero;
            minusLabel.rectTransform.offsetMax = Vector2.zero;
            minusGo.GetComponent<Button>().onClick.AddListener(remove);
            return name;
        }

        static string UpgradeLabel(int kind)
        {
            int level = _upLevel[kind];
            if (kind == 0)
                return UpgradeNames[0] + "  +" + level * 5 + "%";
            if (kind == 1)
                return UpgradeNames[1] + "  +" + level * 10 + "%";
            if (kind == 3)
                return UpgradeNames[3] + "  +" + level * 20;
            if (kind == 4)
                return UpgradeNames[4] + "  +" + level * 5;
            if (IsZSkill(kind))
            {
                if (level <= 0)
                    return UpgradeNames[kind] + "  未解锁";
                string extra = " " + SkillRank(level) + "  冷却" + Mathf.RoundToInt(SkillCooldown(kind)) + "秒";
                if (_selectedSkill == kind)
                    extra += "  使用中";
                return UpgradeNames[kind] + extra;
            }
            return UpgradeNames[2] + "  " + (3 + level) + "/10";
        }

        static string SkillRank(int level)
        {
            if (level <= 1)
                return "I";
            if (level == 2)
                return "II";
            return "III";
        }

        static int UpgradePrice(int kind, int level)
        {
            if (kind == SkillRepair)
            {
                if (level <= 0)
                    return 5000;
                if (level == 1)
                    return 300;
                return 1000;
            }
            if (kind == SkillSee)
            {
                if (level <= 0)
                    return 10000;
                if (level == 1)
                    return 1000;
                return 3000;
            }
            if (kind == SkillInvuln)
            {
                if (level <= 0)
                    return 7500;
                if (level == 1)
                    return 1000;
                return 3000;
            }
            if (kind == SkillHeal)
            {
                if (level <= 0)
                    return 3000;
                if (level == 1)
                    return 500;
                return 1000;
            }
            if (kind == SkillStealth)
            {
                if (level <= 0)
                    return 10000;
                if (level == 1)
                    return 3000;
                return 5000;
            }
            if (kind == 0 || kind == 4)
                return 100 + 50 * level;
            if (kind == 1 || kind == 3)
                return 100 + 100 * level;
            return BagPrices[Math.Min(level, BagPrices.Length - 1)];
        }

        static int UpgradeCartCost(int kind)
        {
            int sum = 0;
            for (int i = 0; i < _upCart[kind]; i++)
                sum += UpgradePrice(kind, _upLevel[kind] + i);
            return sum;
        }

        static void AddUpgrade(int kind)
        {
            if (_upLevel[kind] + _upCart[kind] >= UpgradeMax[kind])
            {
                Tell("已经满级");
                return;
            }
            _upCart[kind]++;
            RefreshCartText();
        }

        static void AddSwitch(int kind)
        {
            if (!IsZSkill(kind) || _upLevel[kind] <= 0)
                return;
            if (_selectedSkill == kind)
            {
                Tell("已经在使用");
                return;
            }
            for (int i = 0; i < 10; i++)
                _upSwitch[i] = 0;
            _upSwitch[kind] = 1;
            RefreshCartText();
        }

        static bool IsZSkill(int kind)
        {
            return kind >= SkillRepair && kind <= SkillStealth;
        }

        static int FirstOwnedZSkill()
        {
            for (int i = SkillRepair; i <= SkillStealth; i++)
            {
                if (_upLevel[i] > 0)
                    return i;
            }
            return -1;
        }

        static string ZSkillLabel(int kind)
        {
            if (kind == SkillRepair)
                return "随地修复";
            if (kind == SkillSee)
                return "透视";
            if (kind == SkillInvuln)
                return "无敌模式";
            if (kind == SkillHeal)
                return "治疗";
            if (kind == SkillStealth)
                return "隐身";
            return "";
        }

        static void SelectZSkill(int kind)
        {
            if (!IsZSkill(kind) || _upLevel[kind] <= 0)
                return;
            _selectedSkill = kind;
            SaveUpgrades();
            RefreshCartText();
            Tell("技能：" + ZSkillLabel(kind));
        }

        static void RemoveUpgrade(int kind)
        {
            if (_upSwitch[kind] > 0)
            {
                _upSwitch[kind] = 0;
                RefreshCartText();
                return;
            }
            if (_upCart[kind] <= 0)
                return;
            _upCart[kind]--;
            RefreshCartText();
        }

        static void LoadUpgrades()
        {
            try
            {
                string path = Path.Combine(Application.persistentDataPath, "skill-upgrades.txt");
                if (!File.Exists(path))
                    return;
                string[] parts = File.ReadAllText(path).Split(',');
                for (int i = 0; i < _upLevel.Length && i < parts.Length; i++)
                {
                    if (int.TryParse(parts[i], out int n))
                        _upLevel[i] = Mathf.Clamp(n, 0, UpgradeMax[i]);
                }
                if (parts.Length > _upLevel.Length && int.TryParse(parts[_upLevel.Length], out int sel))
                    _selectedSkill = sel;
                if (_selectedSkill < SkillRepair || _selectedSkill > SkillStealth || _upLevel[_selectedSkill] <= 0)
                    _selectedSkill = FirstOwnedZSkill();
            }
            catch (Exception)
            {
            }
        }

        static void SaveUpgrades()
        {
            try
            {
                string path = Path.Combine(Application.persistentDataPath, "skill-upgrades.txt");
                File.WriteAllText(path, string.Join(",", _upLevel) + "," + _selectedSkill);
            }
            catch (Exception ex)
            {
                Log.LogError("Could not save upgrades: " + ex.Message);
            }
        }

        static void PushBagCapacity()
        {
            try
            {
                Type bag = AccessTools.TypeByName("DuffelBagMod.Plugin");
                MethodInfo set = bag != null ? AccessTools.Method(bag, "SetCapacity") : null;
                if (set != null)
                    set.Invoke(null, new object[] { 3 + _upLevel[2] });
            }
            catch (Exception ex)
            {
                Log.LogError("Could not update duffel bag: " + ex.Message);
            }
        }

        static IEnumerable<CodeInstruction> TaserDamage(IEnumerable<CodeInstruction> instructions)
        {
            return SwapDamage(instructions, 120f, 100f, 2, "Taser");
        }

        static IEnumerable<CodeInstruction> CrossbowDamage(IEnumerable<CodeInstruction> instructions)
        {
            return SwapDamage(instructions, 999f, 150f, 1, "Crossbow");
        }

        static IEnumerable<CodeInstruction> PistolDamage(IEnumerable<CodeInstruction> instructions)
        {
            return SwapDamage(instructions, 999f, 200f, 1, "Silenced pistol");
        }

        static IEnumerable<CodeInstruction> SwapDamage(IEnumerable<CodeInstruction> instructions, float from, float to, int expected, string what)
        {
            var codes = new List<CodeInstruction>(instructions);
            int changed = 0;
            foreach (CodeInstruction code in codes)
            {
                if (code.opcode == System.Reflection.Emit.OpCodes.Ldc_R4 && code.operand is float value && value == from)
                {
                    code.operand = to;
                    changed++;
                }
            }
            if (changed != expected)
                Log.LogWarning(what + " damage patch changed " + changed + " values instead of " + expected + ".");
            return codes;
        }

        static void RefreshVitals()
        {
            try
            {
                FPP_Player player = WorldManager.Instance != null ? WorldManager.Instance.localPlayer : null;
                if (player != null)
                    player.CalculateBonusesToPlayer();
            }
            catch (Exception ex)
            {
                Log.LogError("Could not refresh health and stamina: " + ex.Message);
            }
        }

        static void UpgradeVitals(FPP_Player __instance)
        {
            WorldManager world = WorldManager.Instance;
            if (world == null || __instance == null || __instance != world.localPlayer)
                return;
            __instance.maxDamage += 20f * _upLevel[3];
            __instance.maxStamina += 5f * _upLevel[4];
        }

        static void BeforeDeath(FPP_Player __instance, out bool __state)
        {
            __state = __instance != null && !__instance.isDead;
        }

        static void AfterDeath(FPP_Player __instance, bool __state)
        {
            WorldManager world = WorldManager.Instance;
            if (!__state || world == null || __instance != world.localPlayer || !__instance.isDead)
                return;
            EndActiveSkill(__instance, true);
            bool lost = false;
            for (int i = 0; i < LostOnDeath.Length; i++)
            {
                int kind = LostOnDeath[i];
                if (_upLevel[kind] <= 0)
                    continue;
                _upLevel[kind]--;
                lost = true;
            }
            if (!lost)
                return;
            SaveUpgrades();
            RefreshVitals();
            RefreshCartText();
            _deathNotice = true;
            BroadcastUpgrades(__instance, world, true);
            Log.LogInfo("Died, upgrades went down by 1: " + string.Join(",", _upLevel));
        }

        const string UpgradeChatPrefix = "§SCUP:";
        const string StealthChatPrefix = "§SCST:";
        static readonly Dictionary<string, int[]> _remoteUp = new Dictionary<string, int[]>();
        static readonly Dictionary<string, float> _remoteStealthUntil = new Dictionary<string, float>();
        static float _nextStealthBroadcast;
        static FPP_Player[] _cachedPlayers = new FPP_Player[0];
        static float _nextPlayerScan;
        static readonly Dictionary<int, TextMeshProUGUI> _pauseUpLabels = new Dictionary<int, TextMeshProUGUI>();
        static string _lastUpBroadcast;
        static float _nextUpBroadcast;

        static void BroadcastUpgrades(FPP_Player player, WorldManager world, bool force)
        {
            if (player == null || world == null || player != world.localPlayer)
                return;
            if (world.loadingScene)
                return;
            string payload = UpgradeChatPrefix + _upLevel[0] + "," + _upLevel[1] + "," + _upLevel[2] + "," + _upLevel[3] + "," + _upLevel[4];
            if (!force && payload == _lastUpBroadcast && Time.unscaledTime < _nextUpBroadcast)
                return;
            _nextUpBroadcast = Time.unscaledTime + 5f;
            _lastUpBroadcast = payload;
            try
            {
                world.RPC_SpawnChatMessage(player.nickname ?? "", payload, "");
            }
            catch (Exception)
            {
            }
        }

        static bool HideUpgradeChat(string _nickname, string content)
        {
            if (HandleStealthChat(_nickname, content))
                return false;
            if (string.IsNullOrEmpty(content) || !content.StartsWith(UpgradeChatPrefix, StringComparison.Ordinal))
                return true;
            string[] parts = content.Substring(UpgradeChatPrefix.Length).Split(',');
            var lv = new int[UpgradeNames.Length];
            for (int i = 0; i < lv.Length && i < parts.Length; i++)
            {
                if (int.TryParse(parts[i], out int n))
                    lv[i] = Mathf.Clamp(n, 0, UpgradeMax[i]);
            }
            if (!string.IsNullOrEmpty(_nickname))
                _remoteUp[_nickname] = lv;
            return false;
        }

        static bool HandleStealthChat(string nickname, string content)
        {
            if (string.IsNullOrEmpty(content) || !content.StartsWith(StealthChatPrefix, StringComparison.Ordinal))
                return false;
            if (string.IsNullOrEmpty(nickname))
                return true;
            string payload = content.Substring(StealthChatPrefix.Length);
            string[] parts = payload.Split(',');
            bool on = parts.Length > 0 && parts[0] == "1";
            if (!on)
            {
                EndRemoteStealthNick(nickname);
                return true;
            }
            float last = 20f;
            if (parts.Length > 1)
                float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out last);
            if (last < 1f)
                last = 1f;
            bool justOn = !_remoteStealthUntil.ContainsKey(nickname) || Time.unscaledTime >= _remoteStealthUntil[nickname];
            _remoteStealthUntil[nickname] = Time.unscaledTime + last + 1f;
            if (!justOn)
                return true;
            FPP_Player local = LocalPlayer();
            FPP_Player other = FindPlayerByNick(nickname);
            if (other != null && other != local)
            {
                ApplyStealthLayers(other, true);
                FreezeLastKnown(other);
            }
            return true;
        }

        static void BroadcastStealth(bool on)
        {
            WorldManager world = WorldManager.Instance;
            FPP_Player player = world != null ? world.localPlayer : null;
            if (player == null || world == null || world.loadingScene)
                return;
            float left = on ? Mathf.Max(1f, _skillActiveUntil - Time.unscaledTime) : 0f;
            string payload = StealthChatPrefix + (on ? "1," + left.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : "0");
            try
            {
                world.RPC_SpawnChatMessage(player.nickname ?? "", payload, "");
            }
            catch (Exception)
            {
            }
        }

        static void EndRemoteStealthNick(string nickname)
        {
            _remoteStealthUntil.Remove(nickname);
            FPP_Player gone = FindPlayerByNick(nickname);
            if (gone != null && gone != LocalPlayer())
                ApplyStealthLayers(gone, false);
            if (!StealthActive() && _remoteStealthUntil.Count == 0)
                RestoreNpcVisionAfterStealth();
        }

        static void PruneRemoteStealth()
        {
            if (_remoteStealthUntil.Count == 0)
                return;
            float now = Time.unscaledTime;
            var dead = new List<string>();
            foreach (KeyValuePair<string, float> pair in _remoteStealthUntil)
            {
                if (now >= pair.Value)
                    dead.Add(pair.Key);
            }
            if (dead.Count == 0)
                return;
            for (int i = 0; i < dead.Count; i++)
            {
                string nick = dead[i];
                _remoteStealthUntil.Remove(nick);
                FPP_Player gone = FindPlayerByNick(nick);
                if (gone != null && gone != LocalPlayer())
                    ApplyStealthLayers(gone, false);
            }
            if (!StealthActive() && _remoteStealthUntil.Count == 0)
                RestoreNpcVisionAfterStealth();
        }

        static FPP_Player[] Players()
        {
            if (Time.unscaledTime >= _nextPlayerScan)
            {
                _nextPlayerScan = Time.unscaledTime + 0.4f;
                _cachedPlayers = UnityEngine.Object.FindObjectsOfType<FPP_Player>();
            }
            return _cachedPlayers;
        }

        static FPP_Player FindPlayerByNick(string nickname)
        {
            if (string.IsNullOrEmpty(nickname))
                return null;
            FPP_Player[] all = Players();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && !string.IsNullOrEmpty(all[i].nickname)
                    && string.Equals(all[i].nickname, nickname, StringComparison.OrdinalIgnoreCase))
                    return all[i];
            }
            return null;
        }

        static bool PlayerStealthed(FPP_Player player)
        {
            if (player == null)
                return false;
            if (player == LocalPlayer() && StealthActive())
                return true;
            PruneRemoteStealth();
            return !string.IsNullOrEmpty(player.nickname) && _remoteStealthUntil.ContainsKey(player.nickname);
        }

        static bool AnyStealthed()
        {
            if (StealthActive())
                return true;
            PruneRemoteStealth();
            return _remoteStealthUntil.Count > 0;
        }

        static bool HasVisiblePlayer()
        {
            FPP_Player[] all = Players();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && !all[i].isDead && !PlayerStealthed(all[i]))
                    return true;
            }
            return false;
        }

        static bool VisiblePlayerNear(NPC npc, float range)
        {
            if (npc == null)
                return HasVisiblePlayer();
            Vector3 pos = npc.transform.position;
            FPP_Player[] all = Players();
            for (int i = 0; i < all.Length; i++)
            {
                FPP_Player p = all[i];
                if (p == null || p.isDead || PlayerStealthed(p))
                    continue;
                if (Vector3.Distance(pos, p.transform.position) <= range)
                    return true;
            }
            return false;
        }

        static bool _npcPlayerLooked;
        static FieldInfo _npcPlayerField;

        static FPP_Player NpcFocus(NPC npc)
        {
            if (npc == null)
                return null;
            if (!_npcPlayerLooked)
            {
                _npcPlayerLooked = true;
                _npcPlayerField = typeof(NPC).GetField("player", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?? typeof(NPC).GetField("Player", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }
            if (_npcPlayerField == null)
                return null;
            return _npcPlayerField.GetValue(npc) as FPP_Player;
        }

        static FPP_Player NearestStealthed(NPC npc)
        {
            if (npc == null)
                return null;
            Vector3 pos = npc.transform.position;
            FPP_Player best = null;
            float bestDist = 40f;
            FPP_Player[] all = Players();
            for (int i = 0; i < all.Length; i++)
            {
                FPP_Player p = all[i];
                if (p == null || p.isDead || !PlayerStealthed(p))
                    continue;
                float d = Vector3.Distance(pos, p.transform.position);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = p;
                }
            }
            return best;
        }

        static string UpgradeHud(int[] lv)
        {
            if (lv == null || lv.Length < 5)
                lv = new int[5];
            return "+" + (lv[0] * 5) + "%速度/+" + (lv[1] * 10) + "%跳跃/+" + (lv[3] * 20) + "血量/+" + (lv[4] * 5) + "耐力/" + (3 + lv[2]) + "包容量";
        }

        static void ShowPlayerUpgrades(PausePlayerInfo __instance)
        {
            try
            {
                WorldManager world = WorldManager.Instance;
                FPP_Player local = world != null ? world.localPlayer : null;
                if (local == null || !local.pauseActive)
                    return;
                TextMeshProUGUI level = __instance.levelTxt;
                if (level == null)
                    return;
                FPP_Player assigned = __instance.assignedPlayer;
                int[] lv = null;
                if (IsHost(world) && assigned != null)
                {
                    if (assigned == world.localPlayer)
                        lv = _upLevel;
                    else if (!string.IsNullOrEmpty(assigned.nickname) && _remoteUp.TryGetValue(assigned.nickname, out int[] remote))
                        lv = remote;
                }
                int id = __instance.GetInstanceID();
                _pauseUpLabels.TryGetValue(id, out TextMeshProUGUI extra);
                if (extra != null && extra.Equals(null))
                    extra = null;
                if (lv == null)
                {
                    if (extra != null && extra.gameObject.activeSelf)
                        extra.gameObject.SetActive(false);
                    return;
                }
                if (extra == null)
                {
                    var go = new GameObject("SkillCashUpgrades", typeof(RectTransform));
                    go.transform.SetParent(level.transform.parent, false);
                    extra = go.AddComponent<TextMeshProUGUI>();
                    extra.font = level.font;
                    extra.fontSharedMaterial = level.fontSharedMaterial;
                    extra.fontSize = Mathf.Max(14f, level.fontSize * 0.78f);
                    extra.color = level.color;
                    extra.alignment = TextAlignmentOptions.MidlineRight;
                    extra.textWrappingMode = TextWrappingModes.NoWrap;
                    extra.overflowMode = TextOverflowModes.Overflow;
                    extra.raycastTarget = false;
                    extra.rectTransform.sizeDelta = new Vector2(900f, Mathf.Max(24f, level.rectTransform.sizeDelta.y));
                    extra.rectTransform.pivot = new Vector2(1f, 0.5f);
                    _pauseUpLabels[id] = extra;
                }
                extra.alignment = TextAlignmentOptions.MidlineRight;
                extra.rectTransform.pivot = new Vector2(1f, 0.5f);
                if (!extra.gameObject.activeSelf)
                    extra.gameObject.SetActive(true);
                string text = UpgradeHud(lv);
                if (extra.text != text)
                    extra.text = text;
                PlaceUpgradeHud(level, extra);
            }
            catch (Exception)
            {
            }
        }

        static void PlaceUpgradeHud(TextMeshProUGUI level, TextMeshProUGUI extra)
        {
            level.ForceMeshUpdate();
            Bounds bounds = level.textBounds;
            Vector3 world = level.transform.TransformPoint(new Vector3(bounds.min.x - 8f, bounds.center.y, 0f));
            var parent = extra.rectTransform.parent as RectTransform;
            Camera cam = null;
            Canvas canvas = extra.canvas;
            if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                cam = canvas.worldCamera;
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, world);
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, cam, out Vector2 local))
                extra.rectTransform.anchoredPosition = local;
        }

        static bool IsLocalController(ECM.Controllers.BaseCharacterController controller)
        {
            WorldManager world = WorldManager.Instance;
            return world != null && world.localPlayer != null && controller != null && world.localPlayer.characterController == controller;
        }

        static bool InvulnIIIActive()
        {
            return _skillActiveKind == SkillInvuln && _upLevel[SkillInvuln] >= 3 && Time.unscaledTime < _skillActiveUntil;
        }

        static bool SkipStaminaIfInvuln()
        {
            return !InvulnIIIActive();
        }

        static void UpgradeSpeed(ECM.Controllers.BaseFirstPersonController __instance, ref float __result)
        {
            if (_upLevel[0] > 0 && IsLocalController(__instance))
                __result *= 1f + 0.05f * _upLevel[0];
            if (InvulnIIIActive() && IsLocalController(__instance))
                __result *= 1.2f;
        }

        static void UpgradeJump(ECM.Controllers.BaseCharacterController __instance, ref float __result)
        {
            if (_upLevel[1] > 0 && IsLocalController(__instance))
                __result *= Mathf.Sqrt(1f + 0.1f * _upLevel[1]);
            if (InvulnIIIActive() && IsLocalController(__instance))
                __result *= 1.5f;
        }

        static FieldInfo _heavyFall;

        static void BeforeFallCheck(FPP_Player __instance, out float __state)
        {
            __state = -1f;
            WorldManager world = WorldManager.Instance;
            if (_upLevel[1] <= 0 || world == null || __instance != world.localPlayer)
                return;
            if (_heavyFall == null)
                _heavyFall = AccessTools.Field(typeof(FPP_Player), "heavyFallMinVelocity");
            if (_heavyFall == null)
                return;
            __state = (float)_heavyFall.GetValue(__instance);
            _heavyFall.SetValue(__instance, __state * Mathf.Sqrt(1f + 0.1f * _upLevel[1]));
        }

        static void AfterFallCheck(FPP_Player __instance, float __state)
        {
            if (__state >= 0f && _heavyFall != null)
                _heavyFall.SetValue(__instance, __state);
        }

        static void AddXp(int tier)
        {
            _xpCart[tier]++;
            RefreshCartText();
        }

        static void RemoveXp(int tier)
        {
            if (_xpCart[tier] <= 0)
                return;
            _xpCart[tier]--;
            RefreshCartText();
        }

        static int XpUnitPrice(int tier, int already)
        {
            if (tier == 0)
                return Math.Min(20, 1 + already);
            if (tier == 1)
                return Math.Min(100, 10 + 5 * already);
            return Math.Min(500, 100 + 50 * already);
        }

        static int XpCartCost(int tier)
        {
            int sum = 0;
            for (int i = 0; i < _xpCart[tier]; i++)
                sum += XpUnitPrice(tier, _xpBought[tier] + i);
            return sum;
        }

        static void LoadXpPrices()
        {
            if (_xpLoaded)
                return;
            _xpLoaded = true;
            try
            {
                string path = Path.Combine(Application.persistentDataPath, "skill-xp.txt");
                if (!File.Exists(path))
                    return;
                string[] parts = File.ReadAllText(path).Split(',');
                for (int i = 0; i < 3 && i < parts.Length; i++)
                {
                    int n;
                    if (int.TryParse(parts[i], out n))
                        _xpBought[i] = Math.Max(0, n);
                }
            }
            catch (Exception)
            {
            }
        }

        static void SaveXpPrices()
        {
            try
            {
                string path = Path.Combine(Application.persistentDataPath, "skill-xp.txt");
                File.WriteAllText(path, _xpBought[0] + "," + _xpBought[1] + "," + _xpBought[2]);
            }
            catch (Exception ex)
            {
                Log.LogError("Could not save XP prices: " + ex.Message);
            }
        }

        static bool IsGear(Book book)
        {
            for (int i = 0; i < Gear.Length; i++)
            {
                if (Gear[i] == book)
                    return true;
            }
            return false;
        }

        static void ResolveGear()
        {
            LoadCatalog();
            for (int i = 0; i < Gear.Length; i++)
            {
                if (BookPrefab(Gear[i]) != null)
                    continue;
                SpawnPiece found = MatchGear(Gear[i]);
                if (found == null)
                {
                    if (!_weaponsLogged)
                        Log.LogWarning("No item for " + Gear[i].Name);
                    continue;
                }
                Gear[i].Item = found.Item;
                Gear[i].Prefab = found.Prefab;
                Log.LogInfo(Gear[i].Name + " -> " + found.Item.itemName);
            }
            _weaponsLogged = true;
        }

        static void RefreshGearRows()
        {
            for (int i = 0; i < GearRows.Count; i++)
            {
                TextMeshProUGUI name = GearRows[i].Name;
                if (name == null)
                    continue;
                bool ready = GearRows[i].Book != null && BookPrefab(GearRows[i].Book) != null;
                name.color = ready ? Color.white : new Color(0.55f, 0.55f, 0.55f, 1f);
            }
        }

        static SpawnPiece MatchGear(Book gear)
        {
            SpawnPiece best = null;
            int bestScore = 0;
            for (int i = 0; i < KnownPieces.Count; i++)
            {
                SpawnPiece piece = KnownPieces[i];
                Item item = piece.Item;
                if (item == null || piece.Prefab == null || gear.Keys == null)
                    continue;
                string itemName = (item.itemName ?? "").Trim().ToLowerInvariant();
                string assetName = (item.name ?? "").Trim().ToLowerInvariant();
                if (itemName.Contains("golden") || assetName.Contains("golden") || itemName.Contains("card"))
                    continue;
                int keyScore = 0;
                for (int k = 0; k < gear.Keys.Length; k++)
                {
                    string key = gear.Keys[k];
                    if (itemName == key || assetName == key)
                    {
                        keyScore = 40 - k * 10;
                        break;
                    }
                }
                if (keyScore == 0)
                    continue;
                string prefabName = piece.Prefab.name.ToLowerInvariant();
                if (prefabName.Contains("carry") || prefabName.Contains("display"))
                    keyScore -= 15;
                if (item.itemDropPrefab == piece.Prefab)
                    keyScore += 10;
                if (keyScore > bestScore)
                {
                    best = piece;
                    bestScore = keyScore;
                }
            }
            return best;
        }

        static GameObject BookPrefab(Book book)
        {
            if (book == null)
                return null;
            if (book.Prefab != null)
                return book.Prefab;
            if (book.Item != null && book.Item.itemDropPrefab != null)
                return book.Item.itemDropPrefab;
            return null;
        }

        static void ResolveBooks()
        {
            LoadCatalog();
            for (int i = 0; i < Books.Length; i++)
            {
                if (BookPrefab(Books[i]) != null)
                    continue;
                SpawnPiece found = MatchBook(Books[i]);
                if (found == null)
                {
                    if (!_booksResolved)
                        Log.LogWarning("No item for " + Books[i].Name);
                    continue;
                }
                Books[i].Item = found.Item;
                Books[i].Prefab = found.Prefab;
                Log.LogInfo(Books[i].Name + " -> " + found.Item.itemName);
            }
            _booksResolved = true;
        }

        static void RefreshBookRows()
        {
            for (int i = 0; i < Rows.Count && i < Books.Length; i++)
            {
                TextMeshProUGUI name = Rows[i].Name;
                if (name == null)
                    continue;
                bool ready = Rows[i].Book != null && BookPrefab(Rows[i].Book) != null;
                name.color = ready ? Color.white : new Color(0.55f, 0.55f, 0.55f, 1f);
            }
        }

        static SpawnPiece MatchBook(Book book)
        {
            SpawnPiece best = null;
            int bestScore = 0;
            for (int i = 0; i < KnownPieces.Count; i++)
            {
                SpawnPiece piece = KnownPieces[i];
                Item item = piece.Item;
                if (item == null || piece.Prefab == null)
                    continue;
                string hay = ((item.itemName ?? "") + " " + (item.skillLearnID ?? "") + " " + item.name + " " + piece.Prefab.name).ToLowerInvariant();
                if (hay.Contains("reward box") || hay.Contains("leaflet box"))
                    continue;
                string id = (item.skillLearnID ?? "").ToLowerInvariant();
                if (!item.isSkillBook || id.Length == 0)
                    continue;
                bool hit = false;
                for (int k = 0; k < book.Keys.Length; k++)
                {
                    string key = book.Keys[k];
                    if (key.IndexOf(' ') >= 0 || key.IndexOf('#') >= 0)
                        continue;
                    if (id == key)
                    {
                        hit = true;
                        break;
                    }
                }
                if (!hit)
                    continue;
                int score = hay.Contains("leaflet") ? 30 : 20;
                if (score > bestScore)
                {
                    best = piece;
                    bestScore = score;
                }
            }
            return best;
        }

        static void LoadCatalog()
        {
            if (_catalogReady)
                return;
            ES3PrefabDatabase database = ES3PrefabDatabase.Instance;
            if (database != null && database.prefabs != null)
            {
                for (int i = 0; i < database.prefabs.Count; i++)
                {
                    ES3Prefab entry = database.prefabs[i];
                    if (entry == null)
                        continue;
                    RememberPickup(entry.gameObject);
                }
            }

            ScanNetworkPrefabs();
            CollectLoadedItems();

            _catalogReady = true;
            Log.LogInfo("Loaded " + MoneyBills.Count + " paper bills from " + KnownPieces.Count + " pickups.");
            for (int i = 0; i < MoneyBills.Count; i++)
                Log.LogInfo("Bill " + MoneyBills[i].Amount + " " + MoneyBills[i].Prefab.name + " id " + MoneyBills[i].Item.skillLearnID);
            int skills = 0;
            for (int i = 0; i < KnownPieces.Count; i++)
            {
                Item item = KnownPieces[i].Item;
                if (item == null || !item.isSkillBook)
                    continue;
                skills++;
                Log.LogInfo("Skill item " + item.skillLearnID + " / " + item.itemName);
            }
            Log.LogInfo("Skill book items found: " + skills);
        }

        static int CountSkillPieces()
        {
            int count = 0;
            for (int i = 0; i < KnownPieces.Count; i++)
            {
                Item item = KnownPieces[i].Item;
                if (item != null && (item.isSkillBook || !string.IsNullOrEmpty(item.skillLearnID)))
                    count++;
            }
            return count;
        }

        static void CollectLoadedItems()
        {
            Item[] items = Resources.FindObjectsOfTypeAll<Item>();
            for (int i = 0; i < items.Length; i++)
            {
                Item item = items[i];
                if (item == null || item.itemDropPrefab == null)
                    continue;
                int amount = 0;
                Pickupable pickup = item.itemDropPrefab.GetComponent<Pickupable>();
                if (pickup == null)
                    pickup = item.itemDropPrefab.GetComponentInChildren<Pickupable>(true);
                if (pickup != null)
                    amount = pickup.addAmount;
                RememberPiece(item, item.itemDropPrefab, amount);
            }
        }

        static void ScanNetworkPrefabs()
        {
            NetworkPrefabTable table = null;
            try
            {
                table = NetworkProjectConfig.Global.PrefabTable;
            }
            catch (Exception ex)
            {
                Log.LogWarning("Prefab table unavailable: " + ex.Message);
                return;
            }

            if (table == null || table.Prefabs == null)
                return;

            for (int i = 0; i < table.Prefabs.Count; i++)
            {
                NetworkObject spawned = null;
                try
                {
                    spawned = table.Load(NetworkPrefabId.FromIndex(i), true);
                }
                catch (Exception)
                {
                    continue;
                }

                if (spawned == null)
                    continue;
                RememberPickup(spawned.gameObject);
            }
        }

        static void RememberPickup(GameObject root)
        {
            if (root == null)
                return;
            Pickupable[] pickups = root.GetComponentsInChildren<Pickupable>(true);
            for (int i = 0; i < pickups.Length; i++)
            {
                Pickupable pickup = pickups[i];
                if (pickup == null || pickup.assignedItem == null)
                    continue;
                Item item = pickup.assignedItem;
                GameObject prefab = item.itemDropPrefab != null ? item.itemDropPrefab : root;
                if (prefab.GetComponent<NetworkObject>() == null && root.GetComponent<NetworkObject>() != null)
                    prefab = root;
                RememberPiece(item, prefab, pickup.addAmount);
            }
        }

        static void RememberPiece(Item item, GameObject prefab, int amount)
        {
            if (item == null || prefab == null)
                return;
            int named = NamedDollars(item, prefab);
            if (named > 0)
                amount = named;
            for (int n = 0; n < KnownPieces.Count; n++)
            {
                if (KnownPieces[n].Item == item && KnownPieces[n].Prefab == prefab)
                    return;
            }
            var piece = new SpawnPiece { Item = item, Prefab = prefab, Amount = amount };
            KnownPieces.Add(piece);
            if (IsPaperBill(item, prefab))
                MoneyBills.Add(piece);
        }

        static bool IsPaperBill(Item item, GameObject prefab)
        {
            string name = PieceName(item, prefab);
            if (name.Contains("card") || name.Contains("golden") || name.Contains("printer") || name.Contains("bag"))
                return false;
            if (NamedDollars(item, prefab) > 0)
                return true;
            return item.isMoney;
        }

        static int NamedDollars(Item item, GameObject prefab)
        {
            string name = PieceName(item, prefab);
            int[] values = { 100, 50, 20, 10, 5, 1 };
            for (int i = 0; i < values.Length; i++)
            {
                string value = values[i].ToString();
                if (HasToken(name, value + "dollars") || HasToken(name, value + "dollar") || HasToken(name, value + " dollars") || HasToken(name, value + " dollar") || HasToken(name, value + "$"))
                    return values[i];
            }
            return 0;
        }

        static string PieceName(Item item, GameObject prefab)
        {
            return ((item.itemName ?? "") + " " + item.name + " " + (item.skillLearnID ?? "") + " " + (prefab != null ? prefab.name : "")).ToLowerInvariant();
        }

        static bool HasToken(string hay, string token)
        {
            if (string.IsNullOrEmpty(hay) || string.IsNullOrEmpty(token))
                return false;
            int at = 0;
            while ((at = hay.IndexOf(token, at, StringComparison.Ordinal)) >= 0)
            {
                bool before = at == 0 || !char.IsLetterOrDigit(hay[at - 1]);
                int end = at + token.Length;
                bool after = end >= hay.Length || !char.IsLetterOrDigit(hay[end]);
                if (before && after)
                    return true;
                at = end;
            }
            return false;
        }

        static readonly Color IconYellow = new Color(1f, 0.78f, 0.08f, 1f);
        static GameObject _skillRoot;
        static Image _skillPlate;
        static Image _skillIcon;

        static void CopyShopLook(GameObject shop, GameObject skill)
        {
            Image[] shopImages = ActiveImages(shop);
            Image[] skillImages = ActiveImages(skill);
            Image shopIcon = IconImage(shopImages);
            _skillRoot = skill;
            _skillPlate = LargestImage(skillImages);
            _skillIcon = IconImage(skillImages);
            if (shopIcon != null && _skillIcon != null && _skillIcon != _skillPlate)
            {
                _skillIcon.sprite = shopIcon.sprite;
                _skillIcon.preserveAspect = true;
                _skillIcon.color = Color.white;
            }

            Button skillButton = skill.GetComponent<Button>();
            if (skillButton != null && _skillPlate != null)
                skillButton.targetGraphic = _skillPlate;
            if (skillButton != null)
            {
                ColorBlock colors = skillButton.colors;
                colors.normalColor = IconYellow;
                colors.highlightedColor = new Color(1f, 0.86f, 0.25f, 1f);
                colors.pressedColor = new Color(0.92f, 0.64f, 0.02f, 1f);
                colors.selectedColor = IconYellow;
                colors.disabledColor = IconYellow;
                skillButton.colors = colors;
            }

            KeepSkillIconYellow();
        }

        static void KeepSkillIconYellow()
        {
            if (_skillRoot == null)
                return;
            Image[] images = _skillRoot.GetComponentsInChildren<Image>(false);
            for (int i = 0; i < images.Length; i++)
                images[i].color = images[i] == _skillIcon ? Color.white : IconYellow;
        }

        static Image[] ActiveImages(GameObject root)
        {
            return root.GetComponentsInChildren<Image>(false);
        }

        static float ImageArea(Image image)
        {
            if (image == null)
                return 0f;
            Rect rect = image.rectTransform.rect;
            float area = Mathf.Abs(rect.width * rect.height);
            if (area > 1f)
                return area;
            return Mathf.Abs(image.rectTransform.sizeDelta.x * image.rectTransform.sizeDelta.y);
        }

        static Image LargestImage(Image[] images)
        {
            Image best = null;
            float area = -1f;
            for (int i = 0; i < images.Length; i++)
            {
                float size = ImageArea(images[i]);
                if (size > area)
                {
                    area = size;
                    best = images[i];
                }
            }
            return best;
        }

        static Image IconImage(Image[] images)
        {
            Image plate = LargestImage(images);
            Image best = null;
            float area = float.MaxValue;
            for (int i = 0; i < images.Length; i++)
            {
                if (images[i] == plate || images[i].sprite == null)
                    continue;
                float size = ImageArea(images[i]);
                if (size < area)
                {
                    area = size;
                    best = images[i];
                }
            }
            return best;
        }

        static readonly int[] SmallBills = { 1, 5, 10, 20, 50 };
        static readonly int[] BigBills = { 100, 200, 500 };

        static int[] SpawnPressed()
        {
            if (MiddleClickDown())
                return new int[] { 10000 };
            if (KeyDown(KeyCode.I))
                return SmallBills;
            if (KeyDown(KeyCode.K))
                return BigBills;
            return null;
        }

        static bool MiddleClickDown()
        {
            try
            {
                if (Input.GetMouseButtonDown(2) || Input.GetKeyDown(KeyCode.Mouse2))
                    return true;
            }
            catch (Exception)
            {
            }
            return false;
        }

        static bool KeyDown(KeyCode key)
        {
            try
            {
                if (Input.GetKeyDown(key))
                    return true;
            }
            catch (Exception)
            {
            }

            try
            {
                if (ReInput.isReady && ReInput.controllers.Keyboard != null && ReInput.controllers.Keyboard.GetKeyDown(key))
                    return true;
            }
            catch (Exception)
            {
            }

            return false;
        }

        static bool KeyHeld(KeyCode key)
        {
            try
            {
                if (Input.GetKey(key))
                    return true;
            }
            catch (Exception)
            {
            }

            try
            {
                if (ReInput.isReady && ReInput.controllers.Keyboard != null && ReInput.controllers.Keyboard.GetKey(key))
                    return true;
            }
            catch (Exception)
            {
            }

            return false;
        }

        static bool InvulnActive()
        {
            return _skillActiveKind == SkillInvuln && Time.unscaledTime < _skillActiveUntil;
        }

        static bool SkipDeathIfInvuln(FPP_Player __instance)
        {
            if (!InvulnActive())
                return true;
            if (__instance != null)
                __instance.damage = _invulnHp;
            return false;
        }

        static void HoldInvulnHealth(FPP_Player __instance)
        {
            if (!InvulnActive() || __instance == null)
                return;
            __instance.damage = _invulnHp;
        }

        static bool StealthActive()
        {
            return _skillActiveKind == SkillStealth && Time.unscaledTime < _skillActiveUntil;
        }

        static bool SkipNativeInvisRpc(bool tru)
        {
            return !StealthActive();
        }

        static bool HideFromNpcSight(NPC __instance)
        {
            if (__instance == null || !AnyStealthed())
                return true;
            if (VisiblePlayerNear(__instance, 45f))
                return true;
            __instance.seesPlayer = false;
            __instance.playerInFOV = false;
            return false;
        }

        static void AfterNpcSight(NPC __instance)
        {
            if (__instance == null)
                return;
            FPP_Player focus = NpcFocus(__instance);
            if (!PlayerStealthed(focus))
                return;
            __instance.seesPlayer = false;
            __instance.playerInFOV = false;
            __instance.runAtPlayer = false;
        }

        static bool BlockNpcAlert(NPC __instance)
        {
            if (PlayerStealthed(NpcFocus(__instance)))
                return false;
            if (AnyStealthed() && !VisiblePlayerNear(__instance, 50f))
                return false;
            return true;
        }

        static bool BlockPoliceTrack()
        {
            if (!AnyStealthed())
                return true;
            return HasVisiblePlayer();
        }

        static bool FusionObjectReady(NetworkBehaviour behaviour)
        {
            try
            {
                return behaviour != null && behaviour.Object != null && behaviour.Object.IsValid;
            }
            catch (Exception)
            {
                return false;
            }
        }

        static FieldInfo _invokeRpc;

        static void SetInvokeRpc(NetworkBehaviour behaviour)
        {
            if (behaviour == null)
                return;
            if (_invokeRpc == null)
                _invokeRpc = AccessTools.Field(typeof(NetworkBehaviour), "InvokeRpc");
            if (_invokeRpc != null)
                _invokeRpc.SetValue(behaviour, true);
        }

        static void RunRpcLocalIfUnspawned(NetworkBehaviour __instance)
        {
            if (__instance == null || FusionObjectReady(__instance))
                return;
            SetInvokeRpc(__instance);
        }

        static void AfterHasStateAuth(NetworkBehaviour __instance, ref bool __result)
        {
            if (__result || FusionObjectReady(__instance))
                return;
            __result = true;
        }

        static Exception AfterHasStateAuthSafe(Exception __exception, ref bool __result)
        {
            if (__exception == null)
                return null;
            __result = true;
            return null;
        }

        static bool FusionUninit(Exception ex)
        {
            if (ex == null)
                return false;
            string msg = ex.Message;
            return ex is InvalidOperationException && msg != null
                && msg.IndexOf("not initialized", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool NpcIsDown(NPC npc)
        {
            try
            {
                return npc != null && npc.knockedDown;
            }
            catch (Exception)
            {
                return false;
            }
        }

        static Exception AfterNpcRpcSafe(NPC __instance, Exception __exception)
        {
            if (__exception == null)
                return null;
            if (!FusionUninit(__exception))
                return __exception;
            if (!NpcIsDown(__instance))
            {
                try
                {
                    RestoreNpcHits(__instance);
                }
                catch (Exception)
                {
                }
            }
            return null;
        }

        static Exception AfterWakeUpSafe(NPC __instance, Exception __exception)
        {
            if (__exception == null)
                return null;
            try
            {
                ForceGetUp(__instance);
            }
            catch (Exception)
            {
            }
            return FusionUninit(__exception) ? null : __exception;
        }

        static Exception AfterRunAwaySafe(NPC __instance, Exception __exception)
        {
            if (__exception == null)
                return null;
            if (!NpcIsDown(__instance))
            {
                try
                {
                    ForceFlee(__instance);
                }
                catch (Exception)
                {
                }
            }
            return FusionUninit(__exception) ? null : __exception;
        }

        static bool BeforeNetDespawn(NetworkObject networkObject)
        {
            if (networkObject == null)
                return false;
            try
            {
                if (networkObject.IsValid)
                    return true;
            }
            catch (Exception)
            {
            }
            try
            {
                UnityEngine.Object.Destroy(networkObject.gameObject);
            }
            catch (Exception)
            {
            }
            return false;
        }

        static Exception AfterNpcUpdateSafe(NPC __instance, Exception __exception)
        {
            try
            {
                RestoreNpc(__instance);
            }
            catch (Exception)
            {
            }
            if (__exception == null)
                return null;
            if (!FusionUninit(__exception))
                return __exception;
            try
            {
                UnstickNpc(__instance);
            }
            catch (Exception)
            {
            }
            return null;
        }

        static void HoldNpcDown(NPC npc)
        {
            if (npc == null)
                return;
            try
            {
                if (npc.AI != null)
                {
                    npc.AI.canMove = false;
                    npc.AI.maxSpeed = 0f;
                }
            }
            catch (Exception)
            {
            }
        }

        static void RestoreNpc(NPC npc)
        {
            if (npc == null)
                return;
            bool down = false;
            try
            {
                down = npc.knockedDown;
            }
            catch (Exception)
            {
                return;
            }
            if (down)
            {
                HoldNpcDown(npc);
                return;
            }
            if (npc.surprised && npc.checkOutTimer > 1.7f)
            {
                try
                {
                    npc.RunAway();
                }
                catch (Exception)
                {
                    ForceFlee(npc);
                }
            }
            RestoreNpcHits(npc);
        }

        static void RestoreNpcHits(NPC npc)
        {
            if (npc == null || NpcIsDown(npc))
                return;
            try
            {
                if (npc.hitboxList != null)
                {
                    for (int i = 0; i < npc.hitboxList.Count; i++)
                    {
                        NPC_Hitbox box = npc.hitboxList[i];
                        if (box == null)
                            continue;
                        box.gameObject.layer = 16;
                        Collider col = box.GetComponent<Collider>();
                        if (col != null)
                            col.enabled = true;
                    }
                    return;
                }
            }
            catch (Exception)
            {
            }
            NPC_Hitbox[] boxes = npc.GetComponentsInChildren<NPC_Hitbox>(true);
            for (int i = 0; i < boxes.Length; i++)
            {
                if (boxes[i] == null)
                    continue;
                boxes[i].gameObject.layer = 16;
                Collider col = boxes[i].GetComponent<Collider>();
                if (col != null)
                    col.enabled = true;
            }
        }

        static void RestoreNpcMove(NPC npc)
        {
            if (npc == null || npc.AI == null || NpcIsDown(npc))
                return;
            npc.AI.canMove = true;
            npc.AI.enabled = true;
            float speed = npc.runSpeed > 0.1f ? npc.runSpeed : 3.5f;
            if (npc.AI.maxSpeed < 0.1f)
                npc.AI.maxSpeed = speed;
        }

        static void ForceGetUp(NPC npc)
        {
            if (npc == null)
                return;
            try
            {
                npc.AllBoolsFalse(true);
            }
            catch (Exception)
            {
            }
            try
            {
                npc.AllAnimatorFalse();
            }
            catch (Exception)
            {
            }
            npc.wakingUp = true;
            RestoreNpcHits(npc);
            RestoreNpcMove(npc);
        }

        static void ForceFlee(NPC npc)
        {
            if (npc == null || NpcIsDown(npc))
                return;
            try
            {
                npc.AllBoolsFalse(true);
            }
            catch (Exception)
            {
            }
            npc.surprised = false;
            npc.runAway = true;
            RestoreNpcHits(npc);
            RestoreNpcMove(npc);
        }

        static void UnstickNpc(NPC npc)
        {
            if (npc == null)
                return;
            if (npc.returnPolice)
            {
                PoliceCar car = npc.assignedPoliceCar;
                if (car != null)
                {
                    car.goToCrime = false;
                    car.leaving = true;
                }
                TryDestroyNet(npc);
                return;
            }
            if (npc.knockedDown)
            {
                HoldNpcDown(npc);
                return;
            }
            ForceFlee(npc);
        }

        static void TryDestroyNet(NetworkBehaviour behaviour)
        {
            if (behaviour == null)
                return;
            try
            {
                NetworkObject net = behaviour.Object != null ? behaviour.Object : behaviour.GetComponent<NetworkObject>();
                if (net != null && behaviour.Runner != null && net.IsValid)
                {
                    behaviour.Runner.Despawn(net);
                    return;
                }
                if (net != null)
                    UnityEngine.Object.Destroy(net.gameObject);
                else
                    UnityEngine.Object.Destroy(behaviour.gameObject);
            }
            catch (Exception)
            {
                try
                {
                    UnityEngine.Object.Destroy(behaviour.gameObject);
                }
                catch (Exception)
                {
                }
            }
        }

        static Vector3 _stealthLostAt;
        static bool _stealthLostSet;
        static readonly HashSet<int> StealthHunt = new HashSet<int>();
        static readonly HashSet<int> StealthFlail = new HashSet<int>();
        static readonly Dictionary<int, Vector3> StealthHuntAt = new Dictionary<int, Vector3>();
        static readonly Dictionary<int, float> StealthFlailNext = new Dictionary<int, float>();

        static FPP_Player LocalPlayer()
        {
            WorldManager world = WorldManager.Instance;
            return world != null ? world.localPlayer : null;
        }

        static void ClearStealthHunt()
        {
            StealthHunt.Clear();
            StealthFlail.Clear();
            StealthHuntAt.Clear();
            StealthFlailNext.Clear();
        }

        static void RandomSwing(NPC npc)
        {
            if (npc == null)
                return;
            Vector3 origin = npc.transform.position;
            Vector3 dir = UnityEngine.Random.insideUnitSphere;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.05f)
                dir = npc.transform.right;
            dir.Normalize();
            Vector3 pos = origin + dir * UnityEngine.Random.Range(2.5f, 8f);
            int id = npc.GetInstanceID();
            StealthFlail.Add(id);
            StealthHunt.Add(id);
            StealthHuntAt[id] = pos;
            StealthFlailNext[id] = Time.unscaledTime + UnityEngine.Random.Range(0.3f, 0.65f);
            npc.runAtPlayer = false;
            npc.seesPlayer = false;
            npc.playerInFOV = false;
            try
            {
                npc.LookAtPosition(pos);
            }
            catch (Exception)
            {
            }
            try
            {
                npc.TriggerMeleeAttack(UnityEngine.Random.Range(0, 3));
            }
            catch (Exception)
            {
            }
            if (npc.AI != null)
                npc.AI.destination = pos;
        }

        static void HuntSound(NPC npc, Vector3 pos, bool refresh)
        {
            if (npc == null || NpcIsDown(npc))
                return;
            int id = npc.GetInstanceID();
            bool already = StealthHunt.Contains(id);
            StealthHunt.Add(id);
            StealthHuntAt[id] = pos;
            npc.runAtPlayer = false;
            npc.seesPlayer = false;
            npc.playerInFOV = false;
            try
            {
                npc.meleeAttack = false;
            }
            catch (Exception)
            {
            }
            if (already && !refresh)
                return;
            try
            {
                if (npc.isPolice || npc.isWanderingPolice)
                {
                    PoliceManager police = UnityEngine.Object.FindObjectOfType<PoliceManager>();
                    if (police != null)
                        police.lastSeenPosition = pos;
                    npc.AddPoliceCheckPointsAfterChase();
                }
                npc.CheckOutArea(pos);
            }
            catch (Exception)
            {
            }
        }

        static bool NpcWasChasing(NPC npc)
        {
            if (npc == null)
                return false;
            if (StealthHunt.Contains(npc.GetInstanceID()))
                return true;
            try
            {
                if (npc.runAtPlayer || npc.seesPlayer)
                    return true;
            }
            catch (Exception)
            {
            }
            return false;
        }

        static void AfterNpcHear(NPC __instance)
        {
            if (__instance == null || !__instance.hearsPlayer || !AnyStealthed())
                return;
            if (!NpcWasChasing(__instance))
                return;
            FPP_Player focus = NpcFocus(__instance);
            if (focus == null || !PlayerStealthed(focus))
                return;
            HuntSound(__instance, _stealthLostSet ? _stealthLostAt : focus.transform.position, false);
        }

        static void AfterNpcHurt(NPC __instance)
        {
            if (__instance == null)
                return;
            if (NpcIsDown(__instance))
            {
                HoldNpcDown(__instance);
                return;
            }
            FPP_Player focus = NpcFocus(__instance);
            if (!PlayerStealthed(focus) && !(StealthActive() && !HasVisiblePlayer()))
                return;
            __instance.sawPlayerBeforeKnockdown = false;
            __instance.runAtPlayer = false;
            __instance.seesPlayer = false;
            __instance.playerInFOV = false;
            try
            {
                __instance.meleeAttack = false;
            }
            catch (Exception)
            {
            }
            HuntSound(__instance, __instance.transform.position, true);
        }

        static void BeforeCopKnock(NPC __instance, out bool __state)
        {
            __state = false;
            try
            {
                __state = __instance != null && __instance.knockedDown;
            }
            catch (Exception)
            {
            }
        }

        static void AfterCopKnock(NPC __instance, bool __state)
        {
            if (__state || __instance == null)
                return;
            try
            {
                if (!__instance.HasStateAuthority)
                    return;
                if (!__instance.isPolice && !__instance.isWanderingPolice)
                    return;
                DropCopSupplies(__instance);
            }
            catch (Exception ex)
            {
                Log.LogError("Cop loot failed: " + ex.Message);
            }
        }

        static GameObject _firstAidDrop;
        static GameObject _stunDartDrop;
        static GameObject _armorVestDrop;
        static bool _copDropLooked;
        static readonly List<PendingCopDrop> PendingCopDrops = new List<PendingCopDrop>();

        struct PendingCopDrop
        {
            public NetworkRunner Runner;
            public GameObject Prefab;
            public Vector3 Position;
            public int CashAmount;
        }

        static void DropCopSupplies(NPC npc)
        {
            LoadCopDrops();
            int scatter = 0;
            for (int slot = 0; slot < 8; slot++)
            {
                if (slot > 0 && UnityEngine.Random.value >= Mathf.Pow(0.5f, slot))
                    break;
                scatter = RollOneCopDrop(npc, scatter);
            }
        }

        static int RollOneCopDrop(NPC npc, int scatter)
        {
            int roll = UnityEngine.Random.Range(0, 100);
            if (roll < 15)
            {
                QueueNpcDrop(npc, _firstAidDrop, scatter);
                return scatter + 1;
            }
            if (roll < 30)
            {
                QueueNpcDrop(npc, _stunDartDrop, scatter);
                return scatter + 1;
            }
            if (roll < 40)
            {
                QueueNpcDrop(npc, _stunDartDrop, scatter);
                QueueNpcDrop(npc, _stunDartDrop, scatter + 1);
                return scatter + 2;
            }
            if (roll < 45)
            {
                QueueNpcDrop(npc, _stunDartDrop, scatter);
                QueueNpcDrop(npc, _stunDartDrop, scatter + 1);
                QueueNpcDrop(npc, _stunDartDrop, scatter + 2);
                return scatter + 3;
            }
            if (roll < 55)
            {
                QueueCopCash(npc, 20, scatter);
                return scatter + 1;
            }
            if (roll < 62)
            {
                QueueCopCash(npc, 50, scatter);
                return scatter + 1;
            }
            if (roll < 65)
            {
                QueueCopCash(npc, 100, scatter);
                return scatter + 1;
            }
            if (roll < 70)
            {
                QueueNpcDrop(npc, _armorVestDrop, scatter);
                return scatter + 1;
            }
            return scatter;
        }

        static void QueueCopCash(NPC npc, int amount, int scatter)
        {
            if (npc == null || npc.Runner == null || amount <= 0)
                return;
            LoadCatalog();
            GameObject prefab = BillVisual(amount);
            if (prefab == null)
                return;
            Vector3 pos = npc.transform.position + Vector3.up * 0.75f + npc.transform.forward * 0.25f;
            pos += npc.transform.right * (scatter * 0.12f);
            PendingCopDrops.Add(new PendingCopDrop
            {
                Runner = npc.Runner,
                Prefab = prefab,
                Position = pos,
                CashAmount = amount
            });
        }

        static void LoadCopDrops()
        {
            if (_copDropLooked)
                return;
            _copDropLooked = true;
            LoadCatalog();
            _firstAidDrop = FindDropPrefab(true, false);
            _stunDartDrop = FindStunDartPrefab();
            if (_stunDartDrop == null)
                _stunDartDrop = FindDropPrefab(false, true);
            _armorVestDrop = FindArmorVestPrefab();
            Log.LogInfo("Cop drops: first aid " + DropName(_firstAidDrop)
                + ", stun dart " + DropName(_stunDartDrop)
                + ", vest " + DropName(_armorVestDrop)
                + ", cash bills " + MoneyBills.Count);
        }

        static string DropName(GameObject prefab)
        {
            return prefab != null ? prefab.name : "none";
        }

        static GameObject NetworkRoot(GameObject go)
        {
            if (go == null)
                return null;
            NetworkObject net = go.GetComponent<NetworkObject>();
            if (net != null)
                return go;
            net = go.GetComponentInParent<NetworkObject>();
            return net != null ? net.gameObject : null;
        }

        static Item PickupItem(Pickupable pickup)
        {
            if (pickup == null)
                return null;
            try
            {
                return pickup.assignedItem;
            }
            catch (Exception)
            {
                return null;
            }
        }

        static string WorldPickupFail(GameObject prefab)
        {
            if (prefab == null)
                return "null";
            GameObject root = NetworkRoot(prefab);
            if (root == null)
                return prefab.name + " no NetworkObject";
            if (root.GetComponentInChildren<NPCTaser>(true) != null)
                return root.name + " NPCTaser projectile";
            if (root.GetComponentInChildren<StunGun>(true) != null)
                return root.name + " StunGun weapon";
            string n = root.name.ToLowerInvariant();
            if (n.Contains("stungundart") || n.Contains("taserdart") || n.Contains("shot"))
                return root.name + " projectile name";
            Pickupable pickup = root.GetComponent<Pickupable>();
            if (pickup == null)
                pickup = root.GetComponentInChildren<Pickupable>(true);
            if (pickup == null)
                return root.name + " no Pickupable";
            if (PickupItem(pickup) == null)
                return root.name + " no assignedItem";
            return null;
        }

        static bool IsWorldPickup(GameObject prefab)
        {
            return WorldPickupFail(prefab) == null;
        }

        static bool IsDiversionName(string n)
        {
            return n.Contains("diversion") || n.Contains("distract") || n.Contains("干扰");
        }

        static bool IsStunDartName(string n)
        {
            if (string.IsNullOrEmpty(n) || IsDiversionName(n))
                return false;
            if (n.Contains("sleep") || n.Contains("hack") || n.Contains("crossbow"))
                return false;
            bool stun = n.Contains("stun") || n.Contains("taser") || n.Contains("眩晕") || n.Contains("电击");
            bool dart = n.Contains("dart") || n.Contains("镖") || n.Contains("bolt") || n.Contains("ammo");
            return stun && dart;
        }

        static bool SameItem(Item a, Item b)
        {
            if (a == null || b == null)
                return false;
            if (a == b)
                return true;
            string an = (a.itemName ?? "").Trim();
            string bn = (b.itemName ?? "").Trim();
            return an.Length > 0 && string.Equals(an, bn, StringComparison.OrdinalIgnoreCase);
        }

        const string CopStunTag = "copstundart";

        static GameObject FindStunDartPrefab()
        {
            ShopButton[] shops = Resources.FindObjectsOfTypeAll<ShopButton>();
            for (int i = 0; i < shops.Length; i++)
            {
                ShopButton shop = shops[i];
                if (shop == null || shop.assignedItem == null)
                    continue;
                Item item = shop.assignedItem;
                string label = "";
                try
                {
                    if (shop.itemNameTxt != null)
                        label = shop.itemNameTxt.text ?? "";
                }
                catch (Exception)
                {
                }
                string n = (PieceName(item, item.itemDropPrefab) + " " + label).ToLowerInvariant();
                if (IsDiversionName(n) || n.Contains("昏睡") || n.Contains("sleep"))
                    continue;
                bool shopStun = label.IndexOf("眩晕镖", StringComparison.Ordinal) >= 0
                    || n.Contains("眩晕镖")
                    || IsStunDartName(n);
                if (!shopStun)
                    continue;
                GameObject prefab = item.itemDropPrefab;
                if (prefab == null || prefab.GetComponent<NetworkObject>() == null)
                    continue;
                Log.LogInfo("Stun ammo shop " + item.itemName + " / " + prefab.name + " / " + label);
                return prefab;
            }

            Weapon[] weapons = Resources.FindObjectsOfTypeAll<Weapon>();
            for (int w = 0; w < weapons.Length; w++)
            {
                Weapon weapon = weapons[w];
                if (weapon == null || weapon.ammoTypes == null)
                    continue;
                for (int a = 0; a < weapon.ammoTypes.Count; a++)
                {
                    AmmoType ammo = weapon.ammoTypes[a];
                    if (ammo == null || ammo.sleep || ammo.hackElectronic || ammo.diversion)
                        continue;
                    Item item = ammo.ammoItem;
                    if (item == null)
                        continue;
                    string n = (item.itemName ?? "").ToLowerInvariant();
                    if (IsDiversionName(n) || !IsStunDartName(n))
                        continue;
                    GameObject prefab = item.itemDropPrefab;
                    if (prefab == null || prefab.GetComponent<NetworkObject>() == null)
                        continue;
                    Log.LogInfo("Stun ammo drop " + item.itemName + " / " + prefab.name);
                    return prefab;
                }
            }
            return null;
        }

        static bool IsStunShotPrefab(GameObject prefab)
        {
            if (prefab == null)
                return false;
            string n = prefab.name.ToLowerInvariant();
            return n.Contains("stungundart") || n.Contains("taserdart");
        }

        static bool SkipDroppedStunShot(MonoBehaviour __instance)
        {
            if (__instance == null)
                return true;
            Pickupable pickup = __instance.GetComponent<Pickupable>();
            if (pickup == null)
                pickup = __instance.GetComponentInParent<Pickupable>();
            if (pickup == null)
                return true;
            try
            {
                string prop = pickup.customProperty;
                if (!string.IsNullOrEmpty(prop) && prop.IndexOf(CopStunTag, StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;
            }
            catch (Exception)
            {
            }
            return true;
        }

        static void StopStunProjectile(GameObject root)
        {
            if (root == null)
                return;
            NPCTaser[] tasers = root.GetComponentsInChildren<NPCTaser>(true);
            for (int i = 0; i < tasers.Length; i++)
            {
                if (tasers[i] != null)
                    tasers[i].enabled = false;
            }
            TaserDart[] darts = root.GetComponentsInChildren<TaserDart>(true);
            for (int i = 0; i < darts.Length; i++)
            {
                if (darts[i] != null)
                    darts[i].enabled = false;
            }
            Rigidbody[] bodies = root.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < bodies.Length; i++)
            {
                Rigidbody body = bodies[i];
                if (body == null)
                    continue;
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }

        static GameObject FindArmorVestPrefab()
        {
            ShopButton[] shops = Resources.FindObjectsOfTypeAll<ShopButton>();
            for (int i = 0; i < shops.Length; i++)
            {
                ShopButton shop = shops[i];
                if (shop == null || shop.assignedItem == null)
                    continue;
                Item item = shop.assignedItem;
                string label = "";
                try
                {
                    if (shop.itemNameTxt != null)
                        label = shop.itemNameTxt.text ?? "";
                }
                catch (Exception)
                {
                }
                string n = (PieceName(item, item.itemDropPrefab) + " " + label).ToLowerInvariant();
                bool vest = item.isArmorVest
                    || label.IndexOf("防弹背心", StringComparison.Ordinal) >= 0
                    || n.Contains("防弹")
                    || n.Contains("armor vest")
                    || n.Contains("armour vest");
                if (!vest)
                    continue;
                GameObject prefab = item.itemDropPrefab;
                if (prefab != null && IsWorldPickup(prefab))
                {
                    Log.LogInfo("Armor vest shop " + item.itemName + " / " + prefab.name);
                    return prefab;
                }
            }
            GameObject best = null;
            int bestScore = -999;
            for (int i = 0; i < KnownPieces.Count; i++)
            {
                SpawnPiece piece = KnownPieces[i];
                Item item = piece.Item;
                if (item == null || !IsWorldPickup(piece.Prefab))
                    continue;
                string n = PieceName(item, piece.Prefab);
                if (!item.isArmorVest && !n.Contains("armor vest") && !n.Contains("防弹"))
                    continue;
                int score = item.isArmorVest ? 20 : 5;
                if (n.Contains("vest") || n.Contains("背心"))
                    score += 10;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = piece.Prefab;
                }
            }
            return best;
        }

        static GameObject FindDropPrefab(bool firstAid, bool stunDart)
        {
            GameObject best = null;
            int bestScore = -999;
            for (int i = 0; i < KnownPieces.Count; i++)
            {
                SpawnPiece piece = KnownPieces[i];
                Item item = piece.Item;
                if (item == null || !IsWorldPickup(piece.Prefab))
                    continue;
                string n = PieceName(item, piece.Prefab);
                int score = 0;
                if (firstAid)
                {
                    if (!item.isFirstAid)
                        continue;
                    if (n.Contains("machine") || n.Contains("reusable") || n.Contains("station"))
                        score -= 20;
                    if (n.Contains("kit") || n.Contains("pack") || n.Contains("aid") || n.Contains("急救"))
                        score += 10;
                }
                else if (stunDart)
                {
                    if (item.isWeapon || item.isFirstAid || IsDiversionName(n) || !IsStunDartName(n))
                        continue;
                    if (n.Contains("hack") || n.Contains("crossbow") || n.Contains("gun") || n.Contains("pistol") || n.Contains("sleep"))
                        continue;
                    score += 20;
                    if (item.isAmmo)
                        score += 5;
                }
                else
                    continue;
                if (item.itemDropPrefab == piece.Prefab)
                    score += 8;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = piece.Prefab;
                }
            }
            return best;
        }

        static void QueueNpcDrop(NPC npc, GameObject prefab, int scatter)
        {
            if (npc == null || prefab == null || npc.Runner == null)
                return;
            Vector3 pos = npc.transform.position + Vector3.up * 0.75f + npc.transform.forward * 0.25f;
            pos += npc.transform.right * (scatter * 0.12f);
            PendingCopDrops.Add(new PendingCopDrop
            {
                Runner = npc.Runner,
                Prefab = prefab,
                Position = pos
            });
        }

        static void FlushCopDrops()
        {
            if (PendingCopDrops.Count == 0)
                return;
            for (int i = 0; i < PendingCopDrops.Count; i++)
            {
                PendingCopDrop drop = PendingCopDrops[i];
                try
                {
                    if (drop.CashAmount > 0)
                    {
                        SpawnBillAt(drop.Runner, drop.Prefab, drop.CashAmount, drop.Position, Vector3.up);
                        continue;
                    }
                    SpawnQueuedDrop(drop);
                }
                catch (Exception ex)
                {
                    Log.LogError("Cop drop spawn failed: " + ex.Message);
                }
            }
            PendingCopDrops.Clear();
        }

        static void SpawnQueuedDrop(PendingCopDrop drop)
        {
            if (drop.Runner == null || drop.Prefab == null)
                return;
            bool stunShot = IsStunShotPrefab(drop.Prefab);
            if (!stunShot && !IsWorldPickup(drop.Prefab))
                return;
            if (stunShot && drop.Prefab.GetComponent<NetworkObject>() == null)
                return;
            NetworkObject spawned = drop.Runner.Spawn(drop.Prefab, drop.Position, Quaternion.identity);
            if (spawned == null)
                return;
            if (stunShot)
                StopStunProjectile(spawned.gameObject);
            Pickupable pickup = spawned.GetComponent<Pickupable>();
            if (pickup == null)
                pickup = spawned.GetComponentInChildren<Pickupable>(true);
            if (pickup != null)
            {
                pickup.currentDamage = 0f;
                if (stunShot)
                {
                    try
                    {
                        pickup.RPC_SetCustomProperty(CopStunTag);
                    }
                    catch (Exception ex)
                    {
                        Log.LogWarning("Stun dart tag failed: " + ex.Message);
                    }
                }
                pickup.RPC_SpawnMarkerForAll();
            }
            Rigidbody body = spawned.GetComponent<Rigidbody>();
            if (body != null)
                body.AddExplosionForce(8f, drop.Position, 5f, 0.4f, ForceMode.Impulse);
        }

        static void StealthLookAway(NPC __instance, ref Vector3 pos)
        {
            if (__instance == null || !AnyStealthed())
                return;
            int id = __instance.GetInstanceID();
            Vector3 hunt;
            if (StealthHuntAt.TryGetValue(id, out hunt))
            {
                pos = hunt;
                return;
            }
            FPP_Player focus = NpcFocus(__instance);
            if (focus == null || !PlayerStealthed(focus))
                focus = NearestStealthed(__instance);
            if (focus == null || Vector3.Distance(pos, focus.transform.position) > 3f)
                return;
            if (_stealthLostSet)
                pos = _stealthLostAt;
            else
            {
                Vector3 dir = __instance.transform.right;
                pos = __instance.transform.position + dir * 5f;
            }
        }

        static bool SkipBadNpcHour(NPC __instance)
        {
            try
            {
                if (__instance == null || __instance.randomizedBehaviours == null
                    || __instance.randomizedBehaviours.Count == 0)
                    return false;
                WorldManager world = WorldManager.Instance;
                if (world == null || world.enviro == null || world.enviro.Time == null)
                    return false;
                int hour = world.enviro.Time.hours;
                if (hour < 0 || hour >= __instance.randomizedBehaviours.Count)
                    return false;
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }

        static void BreakChaseBeforeNpcUpdate(NPC __instance)
        {
            if (__instance == null || !AnyStealthed())
                return;
            FPP_Player focus = NpcFocus(__instance);
            if (!PlayerStealthed(focus) && VisiblePlayerNear(__instance, 45f))
                return;
            __instance.seesPlayer = false;
            __instance.playerInFOV = false;
            __instance.callingCops = false;
        }

        static void SkipStealthedNearest(NPC __instance)
        {
            if (__instance == null || __instance.nearestPlayer == null || !PlayerStealthed(__instance.nearestPlayer))
                return;
            __instance.nearestPlayer = NearestVisibleForNpc(__instance);
        }

        static FPP_Player NearestVisibleForNpc(NPC npc)
        {
            Vector3 pos = npc.transform.position;
            FPP_Player best = null;
            float bestDist = float.MaxValue;
            FPP_Player[] all = Players();
            for (int i = 0; i < all.Length; i++)
            {
                FPP_Player p = all[i];
                if (p == null || p.isDead || PlayerStealthed(p))
                    continue;
                float d = Vector3.Distance(pos, p.transform.position);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = p;
                }
            }
            return best;
        }

        static void BeforePoliceSearch(NPC __instance, out FPP_Player __state)
        {
            __state = null;
        }

        static void AfterPoliceSearch(NPC __instance, FPP_Player __state)
        {
            if (__instance != null && __state != null && __instance.nearestPlayer == null)
                __instance.nearestPlayer = __state;
        }

        static void StopStealthFollow(NPC __instance)
        {
            if (__instance == null || NpcIsDown(__instance) || !AnyStealthed())
                return;
            FPP_Player focus = NpcFocus(__instance);
            bool focusStealthed = PlayerStealthed(focus);
            bool chasing = __instance.runAtPlayer || __instance.seesPlayer || StealthHunt.Contains(__instance.GetInstanceID());
            if (!focusStealthed && !chasing)
                return;
            __instance.seesPlayer = false;
            __instance.playerInFOV = false;
            __instance.callingCops = false;
            __instance.runAtPlayer = false;
            try
            {
                __instance.meleeAttack = false;
            }
            catch (Exception)
            {
            }
            int id = __instance.GetInstanceID();
            if (chasing && !StealthHunt.Contains(id) && _stealthLostSet)
                HuntSound(__instance, _stealthLostAt, false);
            if (__instance.AI == null || focus == null)
                return;
            Vector3 hunt;
            if (!StealthHuntAt.TryGetValue(id, out hunt))
                return;
            Vector3 playerPos = focus.transform.position;
            if (Vector3.Distance(__instance.AI.destination, playerPos) < 4f && Vector3.Distance(playerPos, hunt) > 2.5f)
                __instance.AI.destination = hunt;
        }

        static void FreezeLastKnown(FPP_Player player)
        {
            if (player == null)
                return;
            ClearStealthHunt();
            _stealthLostAt = player.transform.position;
            _stealthLostSet = true;
            bool copChase = false;
            NPC[] npcs = UnityEngine.Object.FindObjectsOfType<NPC>();
            for (int i = 0; i < npcs.Length; i++)
            {
                NPC npc = npcs[i];
                if (npc == null)
                    continue;
                bool chasing = false;
                try
                {
                    chasing = npc.runAtPlayer || npc.seesPlayer;
                }
                catch (Exception)
                {
                }
                npc.seesPlayer = false;
                npc.playerInFOV = false;
                npc.callingCops = false;
                npc.runAtPlayer = false;
                if (!chasing)
                    continue;
                try
                {
                    if (npc.isPolice || npc.isWanderingPolice)
                    {
                        copChase = true;
                        npc.AddPoliceCheckPointsAfterChase();
                    }
                }
                catch (Exception)
                {
                }
                HuntSound(npc, _stealthLostAt, false);
            }
            if (copChase)
            {
                PoliceManager police = UnityEngine.Object.FindObjectOfType<PoliceManager>();
                if (police != null)
                    police.lastSeenPosition = _stealthLostAt;
            }
        }

        static void TickZSkills(FPP_Player player)
        {
            if (player == null)
                return;
            MaintainActiveSkill(player);
            if (_stealthWasOn && !StealthActive())
                StopStealth(player);
            if (player.isDead)
                return;
            Computer computer = Computer.Instance;
            bool busy = player.pauseActive || player.chatActive || (computer != null && computer.computerActive);
            if (busy || !KeyDown(KeyCode.Z))
                return;
            if (_selectedSkill < SkillRepair || _selectedSkill > SkillStealth || _upLevel[_selectedSkill] <= 0)
                return;
            if (_selectedSkill == SkillRepair)
                return;
            if (_skillActiveKind == _selectedSkill && Time.unscaledTime < _skillActiveUntil)
                return;
            if (Time.unscaledTime < _skillCdUntil[_selectedSkill])
            {
                int left = Mathf.Max(1, Mathf.CeilToInt(_skillCdUntil[_selectedSkill] - Time.unscaledTime));
                Tell("技能冷却中：" + left + "s", 3f, "bad");
                return;
            }
            ActivateZSkill(player, _selectedSkill);
        }

        static void ActivateZSkill(FPP_Player player, int kind)
        {
            float now = Time.unscaledTime;
            if (kind == SkillSee)
            {
                _skillActiveKind = SkillSee;
                _skillActiveUntil = now + SkillDuration(kind);
                SkillSeeFx.Enabled = true;
                SkillSeeFx.ShowLoot = _upLevel[SkillSee] >= 3;
                Tell("技能透视", SkillDuration(kind), "good");
                return;
            }
            if (kind == SkillInvuln)
            {
                _skillActiveKind = SkillInvuln;
                _skillActiveUntil = now + SkillDuration(kind);
                _invulnHp = player.damage;
                _lastCountShown = int.MinValue;
                return;
            }
            if (kind == SkillHeal)
            {
                if (player.damage <= 0f)
                {
                    Tell("满状态时治疗个P啊", 3f, "bad");
                    return;
                }
                player.damage = 0f;
                BeginSkillCooldown(kind);
                Tell("技能：治疗");
                return;
            }
            if (kind == SkillStealth)
            {
                _skillActiveKind = SkillStealth;
                _skillActiveUntil = now + SkillDuration(kind);
                _lastCountShown = int.MinValue;
                StartStealth(player);
            }
        }

        // Real cooldowns: 透视 60s, 无敌模式 180s, 治疗 60s, 隐身 180s, 修复 30s.
        const bool BugCheckShortCooldown = false;

        static float SkillDuration(int kind)
        {
            int lv = Math.Max(1, _upLevel[kind]);
            if (kind == SkillSee)
                return lv >= 2 ? 10f : 5f;
            if (kind == SkillInvuln)
            {
                if (lv >= 3)
                    return 20f;
                if (lv >= 2)
                    return 15f;
                return 10f;
            }
            if (kind == SkillStealth)
            {
                if (lv >= 3)
                    return 30f;
                if (lv >= 2)
                    return 20f;
                return 15f;
            }
            return 0f;
        }

        static float SkillCooldown(int kind)
        {
            int lv = Math.Max(1, _upLevel[kind]);
            if (BugCheckShortCooldown)
                return 1f;
            if (kind == SkillRepair)
            {
                if (lv >= 3)
                    return 5f;
                if (lv >= 2)
                    return 15f;
                return 30f;
            }
            if (kind == SkillSee)
            {
                if (lv >= 3)
                    return 30f;
                if (lv >= 2)
                    return 45f;
                return 60f;
            }
            if (kind == SkillHeal)
            {
                if (lv >= 3)
                    return 30f;
                if (lv >= 2)
                    return 45f;
                return 60f;
            }
            if (kind == SkillInvuln)
            {
                if (lv >= 3)
                    return 60f;
                if (lv >= 2)
                    return 120f;
                return 180f;
            }
            if (kind == SkillStealth)
            {
                if (lv >= 3)
                    return 60f;
                if (lv >= 2)
                    return 120f;
                return 180f;
            }
            return 0f;
        }

        static void BeginSkillCooldown(int kind)
        {
            if (kind < 0 || kind >= _skillCdUntil.Length)
                return;
            _skillCdUntil[kind] = Time.unscaledTime + SkillCooldown(kind);
        }

        static void MaintainActiveSkill(FPP_Player player)
        {
            if (_skillActiveKind < 0)
                return;
            if (player.isDead || Time.unscaledTime >= _skillActiveUntil)
            {
                EndActiveSkill(player, false);
                return;
            }
            if (_skillActiveKind == SkillSee)
                SkillSeeFx.Maintain(player.transform.position);
            if (_skillActiveKind == SkillInvuln && player.damage > _invulnHp)
                player.damage = _invulnHp;
            if (_skillActiveKind == SkillStealth)
            {
                if (!_stealthApplied)
                    ApplyStealthFade(player);
                if (Time.unscaledTime >= _nextStealthBroadcast)
                {
                    _nextStealthBroadcast = Time.unscaledTime + 2f;
                    BroadcastStealth(true);
                }
            }
            if (_skillActiveKind == SkillInvuln || _skillActiveKind == SkillStealth)
            {
                int sec = Mathf.Max(1, Mathf.CeilToInt(_skillActiveUntil - Time.unscaledTime));
                if (sec != _lastCountShown)
                {
                    _lastCountShown = sec;
                    Tell(sec.ToString(), 1.2f, "good");
                }
            }
        }

        static void EndActiveSkill(FPP_Player player, bool silent)
        {
            int kind = _skillActiveKind;
            _skillActiveKind = -1;
            _skillActiveUntil = 0f;
            _lastCountShown = int.MinValue;
            if (kind == SkillSee)
            {
                SkillSeeFx.Enabled = false;
                SkillSeeFx.ShowLoot = false;
                SkillSeeFx.Clear();
            }
            if (kind == SkillStealth)
                StopStealth(player);
            if (kind == SkillSee || kind == SkillInvuln || kind == SkillStealth)
                BeginSkillCooldown(kind);
        }

        static bool HoldingFirstAid(FPP_Player player)
        {
            ItemSlot slot = CurrentSlot(player);
            return slot != null && slot.assignedItem != null && slot.assignedItem.isFirstAid && slot.amount > 0;
        }

        static bool HealSkillReady(FPP_Player player)
        {
            return player != null && !player.isDead && _selectedSkill == SkillHeal && _upLevel[SkillHeal] >= 3
                && Time.unscaledTime >= _skillCdUntil[SkillHeal] && !HoldingFirstAid(player);
        }

        static RessurectTrigger FindRessurect(FPP_Player player)
        {
            GameObject go = player != null ? player.currentDetected : null;
            if (go == null)
                return null;
            RessurectTrigger trigger = go.GetComponent<RessurectTrigger>();
            if (trigger == null)
                trigger = go.GetComponentInParent<RessurectTrigger>();
            if (trigger == null)
                trigger = go.GetComponentInChildren<RessurectTrigger>(true);
            return trigger;
        }

        static void HealRevivePrompt(FPP_Player __instance)
        {
            if (!HealSkillReady(__instance) || __instance.interactUI == null)
                return;
            try
            {
                if (FindRessurect(__instance) == null || HoldingFirstAid(__instance))
                    return;
                __instance.currentObjectBlockInteract = false;
                string key = __instance.interactUI.GetAndReturnKey("Interact");
                string text = LocalizationManager.GetTranslation("ressurect", true, 0, true, false, null, null, true);
                if (string.IsNullOrEmpty(text) || text == "ressurect")
                    text = "复活";
                __instance.interactUI.SpawnPrompt(key, text, true, null);
            }
            catch (Exception)
            {
            }
        }

        static bool HealReviveUse(FPP_Player __instance)
        {
            if (!HealSkillReady(__instance))
                return true;
            try
            {
                RessurectTrigger trigger = FindRessurect(__instance);
                if (trigger == null || HoldingFirstAid(__instance))
                    return true;
                Computer computer = Computer.Instance;
                if (__instance.pauseActive || __instance.chatActive || (computer != null && computer.computerActive))
                    return true;
                bool hold = false;
                try
                {
                    if (__instance.rewiredPlayer != null)
                        hold = __instance.rewiredPlayer.GetButton("Interact");
                }
                catch (Exception)
                {
                }
                if (!hold)
                    hold = KeyHeld(KeyCode.E);
                if (hold)
                {
                    __instance.currentObjectBlockInteract = false;
                    __instance.interactUI.FillInteract(0.75f);
                    if (__instance.interactUI.interactCrosshairFilled)
                    {
                        __instance.InteractDelay(0.5f);
                        trigger.RPC_TriggerRessurect();
                        __instance.RPC_HealSound(0);
                        BeginSkillCooldown(SkillHeal);
                        Tell("技能：复活");
                    }
                }
                else if (__instance.interactUI != null)
                    __instance.interactUI.StopInteractFill();
                return false;
            }
            catch (Exception)
            {
                return true;
            }
        }

        static readonly List<Collider> DoorClipDoors = new List<Collider>();
        static readonly List<Collider> DoorClipMine = new List<Collider>();
        static readonly HashSet<int> DoorClipIds = new HashSet<int>();
        static readonly HashSet<int> DoorClipSeen = new HashSet<int>();
        static bool _doorClipMove;

        static readonly Dictionary<Type, FieldInfo> MeshColFields = new Dictionary<Type, FieldInfo>();
        static readonly HashSet<Type> MeshColMissing = new HashSet<Type>();

        static bool StealthDoorWalk()
        {
            return StealthActive() && _upLevel[SkillStealth] >= 3 && DoorClipIds.Count > 0;
        }

        static bool StealthDoorWalk(CharacterMovement move)
        {
            if (!StealthDoorWalk() || move == null)
                return false;
            FPP_Player player = LocalPlayer();
            return player != null && player.characterController != null && player.characterController.movement == move;
        }

        static void BeforeDoorClipMove(CharacterMovement __instance)
        {
            _doorClipMove = StealthDoorWalk(__instance);
        }

        static void AfterDoorClipMove()
        {
            _doorClipMove = false;
        }

        static void DropDoorOverlaps(Collider[] hits, ref int count)
        {
            if (hits == null || count <= 0)
                return;
            int limit = count;
            if (limit > hits.Length)
                limit = hits.Length;
            int write = 0;
            for (int i = 0; i < limit; i++)
            {
                Collider col = hits[i];
                if (col != null && DoorClipIds.Contains(col.GetInstanceID()))
                    continue;
                hits[write++] = col;
            }
            count = write;
        }

        static void AfterDoorOverlap(CharacterMovement __instance, Collider[] __result, ref int overlapCount)
        {
            if (!StealthDoorWalk(__instance))
                return;
            DropDoorOverlaps(__result, ref overlapCount);
        }

        static void AfterGroundOverlap(BaseGroundDetection __instance, Collider[] __result, ref int overlapCount)
        {
            if (__instance == null || !StealthDoorWalk(__instance.GetComponent<CharacterMovement>()))
                return;
            DropDoorOverlaps(__result, ref overlapCount);
        }

        static void FilterOverlapNonAlloc(Collider[] results, ref int __result)
        {
            if (!_doorClipMove || results == null || __result <= 0)
                return;
            int write = 0;
            for (int i = 0; i < __result && i < results.Length; i++)
            {
                Collider col = results[i];
                if (col != null && DoorClipIds.Contains(col.GetInstanceID()))
                    continue;
                results[write++] = col;
            }
            __result = write;
        }

        static bool SkipDoorPenetration(Collider colliderA, Collider colliderB, ref Vector3 direction, ref float distance, ref bool __result)
        {
            if (!_doorClipMove || colliderA == null || colliderB == null)
                return true;
            if (!DoorClipIds.Contains(colliderA.GetInstanceID()) && !DoorClipIds.Contains(colliderB.GetInstanceID()))
                return true;
            __result = false;
            direction = Vector3.zero;
            distance = 0f;
            return false;
        }

        static void AfterCapsuleCastDoor(ref bool __result, ref RaycastHit hitInfo)
        {
            if (!_doorClipMove || !__result)
                return;
            Collider col = hitInfo.collider;
            if (col != null && DoorClipIds.Contains(col.GetInstanceID()))
                __result = false;
        }

        static void StartDoorClip(FPP_Player player)
        {
            StopDoorClip();
            if (player == null || _upLevel[SkillStealth] < 3)
                return;
            RememberDoors(UnityEngine.Object.FindObjectsOfType<Door>(true));
            RememberDoors(UnityEngine.Object.FindObjectsOfType<SlidingDoor>(true));
            RememberDoors(UnityEngine.Object.FindObjectsOfType<CottageDoor>(true));
            RememberDoors(UnityEngine.Object.FindObjectsOfType<GarageDoor>(true));
            Openable[] opens = UnityEngine.Object.FindObjectsOfType<Openable>(true);
            for (int i = 0; i < opens.Length; i++)
            {
                Openable open = opens[i];
                if (open == null || IsSafeContainer(open.transform) || IsLootFurniture(open.transform) || !IsLockpickPassage(open))
                    continue;
                RememberDoorObject(open);
            }
            Type windowType = AccessTools.TypeByName("CottageWindow");
            if (windowType != null)
            {
                UnityEngine.Object[] windows = UnityEngine.Object.FindObjectsOfType(windowType, true);
                for (int i = 0; i < windows.Length; i++)
                    RememberDoorObject(windows[i] as Component);
            }
            RememberPlayerColliders(player);
            for (int i = 0; i < DoorClipDoors.Count; i++)
            {
                Collider door = DoorClipDoors[i];
                if (door == null)
                    continue;
                for (int a = 0; a < DoorClipMine.Count; a++)
                {
                    if (DoorClipMine[a] == null)
                        continue;
                    Physics.IgnoreCollision(DoorClipMine[a], door, true);
                }
            }
            Log.LogInfo("Stealth III door clip: player walk-through only, NPCs blocked, punch still works. doors=" + DoorClipDoors.Count);
        }

        static void RememberPlayerColliders(FPP_Player player)
        {
            AddPlayerCols(player.GetComponentsInChildren<Collider>(true));
            if (player.characterController == null)
                return;
            CharacterMovement move = player.characterController.movement;
            if (move != null && move.capsuleCollider != null)
                AddPlayerCols(new Collider[] { move.capsuleCollider });
            AddPlayerCols(player.characterController.GetComponentsInChildren<Collider>(true));
        }

        static void AddPlayerCols(Collider[] cols)
        {
            if (cols == null)
                return;
            for (int i = 0; i < cols.Length; i++)
            {
                Collider col = cols[i];
                if (col == null || col.isTrigger)
                    continue;
                if (!DoorClipSeen.Add(col.GetInstanceID()))
                    continue;
                DoorClipMine.Add(col);
            }
        }

        static void RememberDoors(Component[] doors)
        {
            if (doors == null)
                return;
            for (int i = 0; i < doors.Length; i++)
                RememberDoorObject(doors[i]);
        }

        static bool IsSafeContainer(Transform t)
        {
            if (t == null)
                return false;
            if (t.GetComponentInParent<Safe>() != null || t.GetComponentInParent<ElectronicSafe>() != null)
                return true;
            string names = "";
            Transform walk = t;
            for (int i = 0; i < 4 && walk != null; i++, walk = walk.parent)
                names += " " + walk.name.ToLowerInvariant();
            return names.Contains("safe") || names.Contains("vault") || names.Contains("保险箱");
        }

        static bool IsLockpickPassage(Openable open)
        {
            if (open == null || IsSafeContainer(open.transform))
                return false;
            if (!string.IsNullOrEmpty(open.lockpickSkillID) || open.isLocked)
                return true;
            string n = open.name.ToLowerInvariant();
            if (open.transform.parent != null)
                n += " " + open.transform.parent.name.ToLowerInvariant();
            return n.Contains("door") || n.Contains("window") || n.Contains("gate")
                || n.Contains("fence") || n.Contains("hatch");
        }

        static void RememberDoorObject(Component door)
        {
            if (door == null || IsSafeContainer(door.transform))
                return;
            Type type = door.GetType();
            FieldInfo meshField = null;
            if (!MeshColMissing.Contains(type) && !MeshColFields.TryGetValue(type, out meshField))
            {
                meshField = type.GetField("meshCollider", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (meshField == null)
                    MeshColMissing.Add(type);
                else
                    MeshColFields[type] = meshField;
            }
            if (meshField != null)
                RememberDoorCol(meshField.GetValue(door) as Collider);
            Collider[] cols = door.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
            {
                Collider col = cols[i];
                if (col == null || col.isTrigger)
                    continue;
                Vector3 size = col.bounds.size;
                if (size.x > 25f && size.z > 25f)
                    continue;
                RememberDoorCol(col);
            }
        }

        static void RememberDoorCol(Collider col)
        {
            if (col == null || col.isTrigger || IsSafeContainer(col.transform))
                return;
            int id = col.GetInstanceID();
            if (!DoorClipSeen.Add(id + 1000003))
                return;
            DoorClipDoors.Add(col);
            DoorClipIds.Add(id);
        }

        static void StopDoorClip()
        {
            for (int i = 0; i < DoorClipDoors.Count; i++)
            {
                Collider door = DoorClipDoors[i];
                if (door == null)
                    continue;
                for (int a = 0; a < DoorClipMine.Count; a++)
                {
                    if (DoorClipMine[a] == null)
                        continue;
                    Physics.IgnoreCollision(DoorClipMine[a], door, false);
                }
            }
            DoorClipDoors.Clear();
            DoorClipMine.Clear();
            DoorClipIds.Clear();
            DoorClipSeen.Clear();
            _doorClipMove = false;
        }

        static readonly Dictionary<Renderer, Material[]> StealthOriginal = new Dictionary<Renderer, Material[]>();
        static readonly List<Material> StealthCopies = new List<Material>();
        static readonly AccessTools.FieldRef<FPP_Player, MeshRenderer[]> StealthAllRenderers =
            AccessTools.FieldRefAccess<FPP_Player, MeshRenderer[]>("allRenderers");
        static readonly AccessTools.FieldRef<FPP_Player, SkinnedMeshRenderer[]> StealthCharacterMeshes =
            AccessTools.FieldRefAccess<FPP_Player, SkinnedMeshRenderer[]>("characterMeshes");
        static readonly AccessTools.FieldRef<FPP_Player, SkinnedMeshRenderer[]> StealthHandRenderers =
            AccessTools.FieldRefAccess<FPP_Player, SkinnedMeshRenderer[]>("handRenderers");
        static readonly Dictionary<int, int> StealthLayers = new Dictionary<int, int>();
        static bool _stealthWasOn;
        static bool _stealthApplied;
        const float StealthAlpha = 0.35f;

        static void StartStealth(FPP_Player player)
        {
            CallAntiSicknessRestore();
            ApplyStealthLayers(player, true);
            ApplyStealthFade(player);
            FreezeLastKnown(player);
            StartDoorClip(player);
            _stealthWasOn = true;
            _nextStealthBroadcast = 0f;
            BroadcastStealth(true);
        }

        static void StopStealth(FPP_Player player)
        {
            CallAntiSicknessRestore();
            RestoreStealthMaterials();
            ApplyStealthLayers(player, false);
            StopDoorClip();
            BroadcastStealth(false);
            if (player != null)
            {
                player.isInvisible = false;
                player.invisibilityTimer = 0f;
            }
            RestoreNpcVisionAfterStealth();
            _stealthLostSet = false;
            _stealthWasOn = false;
        }

        static void RestoreNpcVisionAfterStealth()
        {
            NPC[] npcs = UnityEngine.Object.FindObjectsOfType<NPC>();
            for (int i = 0; i < npcs.Length; i++)
            {
                NPC npc = npcs[i];
                if (npc == null)
                    continue;
                int id = npc.GetInstanceID();
                bool confused = StealthHunt.Contains(id) || StealthFlail.Contains(id);
                try
                {
                    npc.meleeAttack = false;
                    npc.checkOut = false;
                    npc.callingCops = false;
                }
                catch (Exception)
                {
                }
                if (!confused)
                    continue;
                try
                {
                    npc.SetNewBehaviour();
                }
                catch (Exception)
                {
                }
            }
            ClearStealthHunt();
            _stealthLostSet = false;
        }

        static void ApplyStealthLayers(FPP_Player player, bool hide)
        {
            if (player == null)
                return;
            Transform[] all = player.GetComponentsInChildren<Transform>(true);
            if (hide)
            {
                StealthLayers.Clear();
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] == null)
                        continue;
                    StealthLayers[all[i].GetInstanceID()] = all[i].gameObject.layer;
                    all[i].gameObject.layer = 2;
                }
                return;
            }
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null)
                    continue;
                int saved;
                if (StealthLayers.TryGetValue(all[i].GetInstanceID(), out saved) && saved != 2)
                    all[i].gameObject.layer = saved;
                else
                    all[i].gameObject.layer = 7;
            }
            player.gameObject.layer = 7;
            StealthLayers.Clear();
        }

        static void CallAntiSicknessRestore()
        {
            try
            {
                Type anti = AccessTools.TypeByName("AntiSickness.Plugin");
                MethodInfo restore = anti != null ? AccessTools.Method(anti, "RestoreBodyMaterials") : null;
                if (restore != null)
                    restore.Invoke(null, null);
            }
            catch (Exception)
            {
            }
        }

        static void ApplyStealthFade(FPP_Player player)
        {
            if (player == null)
                return;
            var renderers = new List<Renderer>();
            AddRenderers(renderers, StealthCharacterMeshes(player));
            AddRenderers(renderers, StealthHandRenderers(player));
            AddRenderers(renderers, StealthAllRenderers(player));
            Renderer[] extra = player.GetComponentsInChildren<Renderer>(true);
            AddRenderers(renderers, extra);
            bool added = false;
            for (int i = 0; i < renderers.Count; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || StealthOriginal.ContainsKey(renderer))
                    continue;
                if (renderer is SpriteRenderer || renderer.GetType().Name == "ParticleSystemRenderer")
                    continue;
                if (renderer.GetComponentInParent<Canvas>() != null)
                    continue;
                Material[] source = renderer.sharedMaterials;
                if (source == null || source.Length == 0)
                    continue;
                var faded = new Material[source.Length];
                for (int m = 0; m < source.Length; m++)
                {
                    if (source[m] == null)
                        continue;
                    faded[m] = FadeCopy(source[m], StealthAlpha);
                    if (faded[m] != null)
                        StealthCopies.Add(faded[m]);
                    else
                        faded[m] = source[m];
                }
                StealthOriginal[renderer] = source;
                renderer.sharedMaterials = faded;
                added = true;
            }
            if (added || StealthOriginal.Count > 0)
                _stealthApplied = true;
        }

        static void AddRenderers(List<Renderer> list, Renderer[] source)
        {
            if (source == null)
                return;
            for (int i = 0; i < source.Length; i++)
            {
                if (source[i] != null)
                    list.Add(source[i]);
            }
        }

        static Material _fadeTemplate;
        static bool _fadeTemplateSearched;

        static Material FadeTemplate()
        {
            if (_fadeTemplate != null || _fadeTemplateSearched)
                return _fadeTemplate;
            _fadeTemplateSearched = true;
            Material premultiplied = null;
            Material[] all = Resources.FindObjectsOfTypeAll<Material>();
            for (int i = 0; i < all.Length; i++)
            {
                Material m = all[i];
                if (m == null || m.shader == null || m.shader.name != "Standard" || m.renderQueue < 2900)
                    continue;
                if (m.IsKeywordEnabled("_ALPHABLEND_ON"))
                {
                    _fadeTemplate = m;
                    break;
                }
                if (premultiplied == null && m.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON"))
                    premultiplied = m;
            }
            if (_fadeTemplate == null)
                _fadeTemplate = premultiplied;
            if (_fadeTemplate == null)
            {
                Shader standard = Shader.Find("Standard");
                if (standard != null)
                {
                    _fadeTemplate = new Material(standard);
                    _fadeTemplate.SetFloat("_Mode", 2f);
                    _fadeTemplate.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    _fadeTemplate.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    _fadeTemplate.SetInt("_ZWrite", 0);
                    _fadeTemplate.DisableKeyword("_ALPHATEST_ON");
                    _fadeTemplate.EnableKeyword("_ALPHABLEND_ON");
                    _fadeTemplate.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    _fadeTemplate.renderQueue = 3000;
                }
            }
            return _fadeTemplate;
        }

        static Material FadeCopy(Material source, float alpha)
        {
            Material template = FadeTemplate();
            if (template == null || source == null)
                return null;
            var m = new Material(template);
            if (source.HasProperty("_MainTex"))
            {
                m.SetTexture("_MainTex", source.GetTexture("_MainTex"));
                m.SetTextureScale("_MainTex", source.GetTextureScale("_MainTex"));
                m.SetTextureOffset("_MainTex", source.GetTextureOffset("_MainTex"));
            }
            if (m.IsKeywordEnabled("_NORMALMAP") && source.HasProperty("_BumpMap"))
                m.SetTexture("_BumpMap", source.GetTexture("_BumpMap"));
            if (m.HasProperty("_MetallicGlossMap"))
                m.SetTexture("_MetallicGlossMap", null);
            if (m.HasProperty("_Metallic"))
                m.SetFloat("_Metallic", 0f);
            if (m.HasProperty("_Glossiness"))
                m.SetFloat("_Glossiness", 0.2f);
            if (m.HasProperty("_EmissionColor"))
                m.SetColor("_EmissionColor", Color.black);
            Color color = source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white;
            color.a = alpha;
            m.SetColor("_Color", color);
            return m;
        }

        static void RestoreStealthMaterials()
        {
            foreach (KeyValuePair<Renderer, Material[]> pair in StealthOriginal)
            {
                if (pair.Key == null || pair.Value == null)
                    continue;
                pair.Key.sharedMaterials = pair.Value;
            }
            StealthOriginal.Clear();
            for (int i = 0; i < StealthCopies.Count; i++)
            {
                if (StealthCopies[i] != null)
                    UnityEngine.Object.Destroy(StealthCopies[i]);
            }
            StealthCopies.Clear();
            _stealthApplied = false;
        }

        static Image _repairFill;
        static ItemSlot _repairSlot;
        static float _repairHeld;
        static readonly Color RepairFillColor = new Color(0.12f, 0.28f, 0.62f, 0.92f);

        static void FieldRepair(FPP_Player player)
        {
            if (!HasFieldRepair || player == null || player.isDead)
            {
                if (_repairFill != null || _repairHeld > 0f)
                    CancelRepair(player, true);
                return;
            }
            Computer computer = Computer.Instance;
            bool busy = player.pauseActive || player.chatActive || (computer != null && computer.computerActive);
            bool z = !busy && KeyHeld(KeyCode.Z);
            ItemSlot slot = CurrentSlot(player);
            float damage = SlotDamage(slot);
            bool broken = slot != null && slot.assignedItem != null && damage > 0.01f;
            int cost = broken ? RepairCashCost(damage) : 0;

            if (Time.unscaledTime < _skillCdUntil[SkillRepair])
            {
                if (z)
                {
                    int left = Mathf.Max(1, Mathf.CeilToInt(_skillCdUntil[SkillRepair] - Time.unscaledTime));
                    string wait = "技能冷却中：" + left + "s";
                    if (_lastRepairTell != wait)
                    {
                        _lastRepairTell = wait;
                        Tell(wait, 3f, "bad");
                    }
                }
                if (_repairFill != null || _repairHeld > 0f)
                    CancelRepair(player, true);
                return;
            }

            TickRepairNotice(player, z && broken, cost);

            if (!z || !broken)
            {
                if (_repairFill != null || _repairHeld > 0f)
                    CancelRepair(player, true);
                return;
            }

            if (_repairSlot != slot)
            {
                CancelRepair(player, true);
                _repairSlot = slot;
            }
            EnsureRepairFill(slot);
            if (ToolInUse(player))
            {
                _repairHeld = 0f;
                if (_repairFill != null)
                    _repairFill.fillAmount = 0f;
                _usedToolThisFrame = false;
                return;
            }
            _usedToolThisFrame = false;
            _repairHeld += Time.unscaledDeltaTime;
            float fill = Mathf.Clamp01(_repairHeld / 3f);
            if (_repairFill != null)
                _repairFill.fillAmount = fill;
            if (fill < 1f)
                return;

            _repairHeld = 0f;
            if (Cash.Value < cost)
            {
                Tell("现金不足", 3f, "bad");
                _lastRepairTell = null;
                CancelRepair(player, true);
                return;
            }

            Cash.Add(-cost);
            _cashShown = int.MinValue;
            RepairCurrentSlot(player, slot);
            BeginSkillCooldown(SkillRepair);
            CancelRepair(player, true);
            Log.LogInfo("Repaired held item for " + cost + " cash. Balance " + Cash.Value);
        }

        static string _lastRepairTell;

        static void TickRepairNotice(FPP_Player player, bool holding, int cost)
        {
            if (!holding)
            {
                _lastRepairTell = null;
                return;
            }
            string text = "花费$" + cost + "修复";
            if (_lastRepairTell == text)
                return;
            _lastRepairTell = text;
            Tell(text, 4f, "good");
        }

        static bool _usedToolThisFrame;

        static void NoteToolUse()
        {
            _usedToolThisFrame = true;
        }

        static bool ToolInUse(FPP_Player player)
        {
            if (_usedToolThisFrame)
                return true;
            if (KeyHeld(KeyCode.Mouse0) || KeyDown(KeyCode.Mouse0))
                return true;
            WeaponManager weapons = player.weaponManager;
            if (weapons == null && player.inventory != null)
                weapons = player.inventory.weaponManager;
            try
            {
                if (weapons != null && (weapons.attacking || weapons.attackTimer > 0.01f))
                    return true;
            }
            catch (Exception)
            {
            }
            try
            {
                if (ReInput.isReady)
                {
                    Rewired.Player pad = ReInput.players.GetPlayer(0);
                    if (pad != null && (pad.GetButton("Use") || pad.GetButtonDown("Use")))
                        return true;
                }
            }
            catch (Exception)
            {
            }
            return false;
        }

        static ItemSlot CurrentSlot(FPP_Player player)
        {
            PlayerInventory inv = player.inventory;
            if (inv == null || inv.itemSlots == null || inv.itemSlots.Count == 0)
                return null;
            int index = inv.currentItemIndex;
            if (index < 0 || index >= inv.itemSlots.Count)
                return null;
            return inv.itemSlots[index];
        }

        static float SlotDamage(ItemSlot slot)
        {
            if (slot == null)
                return 0f;
            try
            {
                return slot.ReturnCurrentDamage();
            }
            catch (Exception)
            {
                if (slot.damagesPerItem != null && slot.amount > 0 && slot.damagesPerItem.Count >= slot.amount)
                    return slot.damagesPerItem[slot.amount - 1];
                return 0f;
            }
        }

        static int RepairCashCost(float damage)
        {
            float broken = Mathf.Clamp01(damage / 100f);
            int cost;
            if (broken <= 0.10f)
                cost = 10;
            else if (broken <= 0.20f)
                cost = 25;
            else if (broken <= 0.30f)
                cost = 50;
            else if (broken <= 0.40f)
                cost = 60;
            else if (broken <= 0.50f)
                cost = 70;
            else if (broken <= 0.60f)
                cost = 80;
            else if (broken <= 0.70f)
                cost = 90;
            else if (broken <= 0.80f)
                cost = 100;
            else if (broken <= 0.90f)
                cost = 125;
            else
                cost = 150;
            if (_upLevel[SkillRepair] >= 3)
                cost = Math.Max(1, cost / 2);
            return cost;
        }

        static void EnsureRepairFill(ItemSlot slot)
        {
            if (slot == null)
                return;
            if (slot.slotBackground != null)
                slot.slotBackground.color = new Color(0.22f, 0.48f, 0.92f, 1f);
            if (slot.slotOutline != null)
                slot.slotOutline.color = new Color(0.35f, 0.62f, 1f, 1f);
            if (_repairFill != null && _repairFill)
            {
                if (_repairFill.transform.parent == FillParent(slot))
                    return;
                UnityEngine.Object.Destroy(_repairFill.gameObject);
                _repairFill = null;
            }
            Transform parent = FillParent(slot);
            var go = new GameObject("SkillCashRepairFill", typeof(RectTransform), typeof(Image));
            go.layer = parent.gameObject.layer;
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            if (slot.itemImage != null)
                rect.SetSiblingIndex(slot.itemImage.transform.GetSiblingIndex());
            Image fill = go.GetComponent<Image>();
            fill.raycastTarget = false;
            fill.color = RepairFillColor;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Vertical;
            fill.fillOrigin = (int)Image.OriginVertical.Bottom;
            fill.fillAmount = 0f;
            if (slot.slotBackground != null && slot.slotBackground.sprite != null)
            {
                fill.sprite = slot.slotBackground.sprite;
                fill.pixelsPerUnitMultiplier = slot.slotBackground.pixelsPerUnitMultiplier;
            }
            _repairFill = fill;
            _repairSlot = slot;
        }

        static Transform FillParent(ItemSlot slot)
        {
            if (slot.itemImage != null)
                return slot.itemImage.transform.parent;
            return slot.transform;
        }

        static void RepairCurrentSlot(FPP_Player player, ItemSlot slot)
        {
            if (slot.damagesPerItem != null && slot.amount > 0 && slot.damagesPerItem.Count >= slot.amount)
                slot.damagesPerItem[slot.amount - 1] = 0f;
            else if (slot.damagesPerItem != null)
            {
                for (int i = 0; i < slot.damagesPerItem.Count; i++)
                    slot.damagesPerItem[i] = 0f;
            }
            if (slot.itemDamageImage != null)
                slot.itemDamageImage.fillAmount = 0f;
            try
            {
                Pickupable[] held = player.GetComponentsInChildren<Pickupable>(true);
                for (int i = 0; i < held.Length; i++)
                {
                    if (held[i] != null && held[i].currentDamage > 0f)
                        held[i].RPC_SetDamage(0f);
                }
            }
            catch (Exception)
            {
            }
            WorldManager world = WorldManager.Instance;
            if (world != null)
            {
                try
                {
                    world.itemsRepaired++;
                }
                catch (Exception)
                {
                }
            }
        }

        static void CancelRepair(FPP_Player player, bool restoreSlot)
        {
            if (_repairFill != null)
            {
                UnityEngine.Object.Destroy(_repairFill.gameObject);
                _repairFill = null;
            }
            if (restoreSlot && _repairSlot != null && player != null && player.inventory != null && CurrentSlot(player) == _repairSlot)
            {
                try
                {
                    _repairSlot.ActivateSlot();
                }
                catch (Exception)
                {
                }
            }
            _repairSlot = null;
            _repairHeld = 0f;
        }

        static void SpawnBill(FPP_Player player, int[] amounts)
        {
            LoadCatalog();
            int amount = amounts[UnityEngine.Random.Range(0, amounts.Length)];
            GameObject prefab = BillVisual(amount);
            if (prefab == null)
            {
                if (!_loggedMissingBill)
                {
                    _loggedMissingBill = true;
                    Log.LogWarning("No paper bill prefab found.");
                }
                return;
            }

            NetworkRunner runner = player.Runner;
            if (runner == null)
                return;

            if (!TrySurface(player, out Vector3 pos, out Vector3 normal))
                return;

            if (SpawnBillAt(runner, prefab, amount, pos, normal))
                Log.LogInfo("Spawned a " + amount + " bill.");
        }

        const string StreetTag = ":s";

        static bool TryCashTag(string prop, out int amount, out bool street)
        {
            amount = 0;
            street = false;
            if (string.IsNullOrEmpty(prop) || !prop.StartsWith("cash:"))
                return false;
            string value = prop.Substring(5);
            if (value.EndsWith(StreetTag))
            {
                street = true;
                value = value.Substring(0, value.Length - StreetTag.Length);
            }
            return int.TryParse(value, out amount) && amount > 0;
        }

        static bool SpawnBillAt(NetworkRunner runner, GameObject prefab, int amount, Vector3 pos, Vector3 normal, bool street = false)
        {
            Quaternion rot = FlatRotation(prefab, normal, out float lift);
            pos += normal * lift;
            try
            {
                NetworkObject spawned = runner.Spawn(prefab, pos, rot);
                _lastSpawnedBill = spawned;
                if (spawned == null)
                    return false;
                Pickupable pickup = spawned.GetComponent<Pickupable>();
                if (pickup == null)
                    pickup = spawned.GetComponentInChildren<Pickupable>(true);
                if (pickup != null)
                {
                    pickup.addAmount = amount;
                    pickup.RPC_SetCustomProperty("cash:" + amount + (street ? StreetTag : ""));
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.LogError("Bill spawn failed: " + ex.Message);
                return false;
            }
        }

        static int _vitalsPlayer;
        static bool _deathNotice;
        static float _deathNoticeAt;
        static float _nextBillSync;
        static bool _loggedBillSync;

        static void ShowBillAmount(FPP_Player __instance)
        {
            if (__instance != null)
                FixBillAmount(__instance.currentDetected);
        }

        static void FixBillAmount(GameObject obj)
        {
            if (obj == null)
                return;
            try
            {
                Pickupable pickup = obj.GetComponent<Pickupable>();
                if (pickup == null || pickup.assignedItem == null || !pickup.assignedItem.isMoney)
                    return;
                if (!TryCashTag(pickup.customProperty, out int amount, out bool street))
                    return;
                if (street)
                {
                    _streetSeen++;
                    MarkStreetBill(pickup);
                }
                if (pickup.addAmount != amount)
                {
                    pickup.addAmount = amount;
                    if (!_loggedBillSync)
                    {
                        _loggedBillSync = true;
                        Log.LogInfo("Bill amounts now match the host.");
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        static void SyncBillAmounts()
        {
            if (Time.unscaledTime < _nextBillSync)
                return;
            _nextBillSync = Time.unscaledTime + 2f;
            _streetSeen = 0;
            Pickupable[] all = UnityEngine.Object.FindObjectsOfType<Pickupable>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null)
                    FixBillAmount(all[i].gameObject);
            }
            _streetLeft = _streetSeen;
        }

        static int _streetSeen;
        static int _streetLeft;
        static int _streetShown = -1;
        static Material _beamMat;
        static readonly Color StreetGlow = new Color(1f, 0.85f, 0.25f);

        static void MarkStreetBill(Pickupable pickup)
        {
            Transform bill = pickup.transform;
            Transform glow = bill.Find("StreetGlow");
            if (glow != null && (glow.Find("Halo") != null || glow.GetComponent<Light>() == null))
            {
                UnityEngine.Object.Destroy(glow.gameObject);
                glow = null;
            }
            if (glow == null)
                glow = BuildStreetGlow(bill);
            AlignStreetGlow(bill, glow);
        }

        static Transform BuildStreetGlow(Transform bill)
        {
            Transform glow = new GameObject("StreetGlow").transform;
            glow.SetParent(bill, true);
            if (_beamMat == null)
            {
                Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color") ?? Shader.Find("UI/Default");
                if (shader != null)
                    _beamMat = new Material(shader) { color = new Color(StreetGlow.r, StreetGlow.g, StreetGlow.b, 0.85f) };
            }
            if (_beamMat != null)
                AddGlowMesh(glow, PrimitiveType.Cylinder, "Beam", Vector3.up * 1.6f, new Vector3(0.055f, 1.6f, 0.055f));
            var light = glow.gameObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = StreetGlow;
            light.intensity = 1.8f;
            light.range = 3.5f;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForceVertex;
            return glow;
        }

        static void AddGlowMesh(Transform glow, PrimitiveType type, string name, Vector3 localPos, Vector3 localScale)
        {
            GameObject mesh = GameObject.CreatePrimitive(type);
            UnityEngine.Object.DestroyImmediate(mesh.GetComponent<Collider>());
            mesh.name = name;
            mesh.layer = 2;
            mesh.transform.SetParent(glow, false);
            mesh.transform.localPosition = localPos;
            mesh.transform.localRotation = Quaternion.identity;
            mesh.transform.localScale = localScale;
            var renderer = mesh.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _beamMat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        }

        static void AlignStreetGlow(Transform bill, Transform glow)
        {
            if (glow == null || bill == null)
                return;
            glow.position = bill.position + Vector3.up * 0.12f;
            glow.rotation = Quaternion.identity;
        }

        static readonly string[] SavedTags = { "Furniture", "Pickupable" };

        class SaveFix
        {
            public readonly List<KeyValuePair<GameObject, string>> Tags = new List<KeyValuePair<GameObject, string>>();
            public readonly List<KeyValuePair<Pickupable, Item>> Lent = new List<KeyValuePair<Pickupable, Item>>();
        }

        static readonly Dictionary<GameObject, Item> BillSaveItems = new Dictionary<GameObject, Item>();

        static void BeforeTruckSave(out SaveFix __state)
        {
            __state = LendBillItems();
        }

        static SaveFix LendBillItems()
        {
            var fix = new SaveFix();
            try
            {
                LoadCatalog();
                Pickupable[] all = UnityEngine.Object.FindObjectsOfType<Pickupable>();
                for (int i = 0; i < all.Length; i++)
                {
                    Pickupable pickup = all[i];
                    if (pickup == null || pickup.assignedItem == null || pickup.assignedItem.itemDropPrefab != null)
                        continue;
                    Item stand = BillSaveItem(pickup);
                    if (stand == null)
                        continue;
                    fix.Lent.Add(new KeyValuePair<Pickupable, Item>(pickup, pickup.assignedItem));
                    pickup.assignedItem = stand;
                }
            }
            catch (Exception ex)
            {
                Log.LogError("Bill save prep failed: " + ex.Message);
            }
            return fix;
        }

        static Item BillSaveItem(Pickupable pickup)
        {
            string name = pickup.gameObject.name;
            GameObject best = null;
            for (int i = 0; i < MoneyBills.Count; i++)
            {
                GameObject prefab = MoneyBills[i].Prefab;
                if (prefab == null || prefab.GetComponent<ES3Prefab>() == null || !name.StartsWith(prefab.name, StringComparison.Ordinal))
                    continue;
                if (best == null || prefab.name.Length > best.name.Length)
                    best = prefab;
            }
            if (best == null)
                return null;
            if (!BillSaveItems.TryGetValue(best, out Item stand) || stand == null)
            {
                stand = UnityEngine.Object.Instantiate(pickup.assignedItem);
                stand.name = pickup.assignedItem.name;
                stand.itemDropPrefab = best;
                stand.hideFlags = HideFlags.HideAndDontSave;
                BillSaveItems[best] = stand;
            }
            return stand;
        }

        static void BeforeSceneSave(out SaveFix __state)
        {
            __state = LendBillItems();
            for (int t = 0; t < SavedTags.Length; t++)
            {
                GameObject[] objects;
                try
                {
                    objects = GameObject.FindGameObjectsWithTag(SavedTags[t]);
                }
                catch (Exception)
                {
                    continue;
                }
                for (int i = 0; i < objects.Length; i++)
                {
                    GameObject obj = objects[i];
                    if (obj == null || CanSave(obj))
                        continue;
                    __state.Tags.Add(new KeyValuePair<GameObject, string>(obj, obj.tag));
                    obj.tag = "Untagged";
                }
            }
            if (__state.Tags.Count > 0)
                Log.LogInfo("Left " + __state.Tags.Count + " unsaveable objects out of the save.");
        }

        static Exception AfterSave(Exception __exception, SaveFix __state)
        {
            if (__state != null)
            {
                for (int i = 0; i < __state.Tags.Count; i++)
                {
                    if (__state.Tags[i].Key != null)
                        __state.Tags[i].Key.tag = __state.Tags[i].Value;
                }
                for (int i = 0; i < __state.Lent.Count; i++)
                {
                    if (__state.Lent[i].Key != null)
                        __state.Lent[i].Key.assignedItem = __state.Lent[i].Value;
                }
            }
            if (__exception != null)
                Log.LogError("Item save failed, the rest of the save still runs: " + __exception);
            return null;
        }

        static bool CanSave(GameObject obj)
        {
            Pickupable pickup = obj.GetComponent<Pickupable>();
            if (pickup != null && pickup.assignedItem != null)
                return pickup.assignedItem.itemDropPrefab != null;
            FurnitureObject furniture = obj.GetComponent<FurnitureObject>();
            if (furniture == null)
                return true;
            return furniture.assignedFurniture != null && furniture.assignedFurniture.prefab != null;
        }

        static void BeforeFurnitureUnlock(Openable __instance, out bool __state)
        {
            __state = __instance != null && __instance.isLocked;
        }

        static void AfterFurnitureUnlock(Openable __instance, bool __state)
        {
            if (!__state || __instance == null)
                return;
            try
            {
                if (!IsHost(WorldManager.Instance) || !IsLootFurniture(__instance.transform))
                    return;
                string skill = (__instance.lockpickSkillID ?? "").ToLowerInvariant();
                int tier = skill.Contains("master") ? 2 : skill.Contains("advanced") ? 1 : 0;
                int count = tier == 2 ? UnityEngine.Random.Range(5, 11)
                    : tier == 1 ? UnityEngine.Random.Range(3, 6)
                    : UnityEngine.Random.Range(1, 4);
                Transform owner = FurnitureOwner(__instance.transform);
                Vector3 inside = FurnitureInside(__instance);
                FillContainer(owner, count, tier, inside, 0.08f);
            }
            catch (Exception ex)
            {
                Log.LogError("Cabinet cash failed: " + ex.Message);
            }
        }

        static void AfterDialSafeUnlock(Safe __instance)
        {
            SafeOpened(__instance, UnityEngine.Random.Range(7, 11), 3);
        }

        static void AfterElectronicSafeUnlock(ElectronicSafe __instance)
        {
            SafeOpened(__instance, UnityEngine.Random.Range(10, 16), 4);
        }

        static void SafeOpened(NetworkBehaviour safe, int count, int tier)
        {
            if (safe == null)
                return;
            try
            {
                if (!IsHost(WorldManager.Instance))
                    return;
                Vector3 inside = safe.transform.position + Vector3.up * 0.3f;
                float radius = 0.08f;
                Component trigger = AccessTools.Field(safe.GetType(), "itemTrigger")?.GetValue(safe) as Component;
                if (trigger != null)
                {
                    Collider area = trigger.GetComponent<Collider>();
                    if (area != null)
                    {
                        inside = area.bounds.center;
                        radius = Mathf.Clamp(Mathf.Min(area.bounds.extents.x, area.bounds.extents.z) * 0.5f, 0.03f, 0.15f);
                    }
                    else
                        inside = trigger.transform.position;
                }
                FillContainer(safe.transform, count, tier, inside, radius);
            }
            catch (Exception ex)
            {
                Log.LogError("Safe cash failed: " + ex.Message);
            }
        }

        static int RollBill(int tier)
        {
            int[] weights = LootWeights[Mathf.Clamp(tier, 0, LootWeights.Length - 1)];
            int sum = 0;
            for (int i = 0; i < weights.Length; i++)
                sum += Math.Max(0, weights[i]);
            int roll = UnityEngine.Random.Range(0, Math.Max(1, sum));
            for (int i = 0; i < weights.Length; i++)
            {
                roll -= Math.Max(0, weights[i]);
                if (roll < 0)
                    return BillAmounts[i];
            }
            return BillAmounts[0];
        }

        static void FillContainer(Transform container, int count, int tier, Vector3 inside, float radius)
        {
            WorldManager world = WorldManager.Instance;
            FPP_Player player = world != null ? world.localPlayer : null;
            if (player == null || player.Runner == null || container == null)
                return;
            if (!LootRounds.Claim(world, ContainerKey(container)))
                return;
            LoadCatalog();
            int total = 0;
            for (int i = 0; i < count; i++)
            {
                int amount = RollBill(tier);
                GameObject prefab = BillVisual(amount);
                if (prefab == null)
                    break;
                Vector2 offset = UnityEngine.Random.insideUnitCircle * radius;
                Vector3 start = inside + new Vector3(offset.x, 0f, offset.y);
                Vector3 normal = Vector3.up;
                Vector3 pos = start;
                if (FloorBelow(start, out RaycastHit hit))
                {
                    pos = hit.point + hit.normal * (i * 0.003f);
                    normal = hit.normal;
                }
                if (SpawnBillAt(player.Runner, prefab, amount, pos, normal))
                    total += amount;
            }
            Log.LogInfo("Put " + count + " bills (" + total + ") in " + container.name);
        }

        static bool FloorBelow(Vector3 start, out RaycastHit floor)
        {
            floor = default;
            RaycastHit[] hits = Physics.RaycastAll(start, Vector3.down, 1.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            bool found = false;
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i].normal.y < 0.5f || hits[i].distance >= best)
                    continue;
                best = hits[i].distance;
                floor = hits[i];
                found = true;
            }
            return found;
        }

        static readonly string[] FurnitureWords = { "cabinet", "closet", "locker", "shelf", "drawer", "cash register" };

        static bool IsLootFurniture(Transform door)
        {
            string names = "";
            Transform t = door;
            for (int i = 0; i < 3 && t != null; i++, t = t.parent)
                names += " " + t.name.ToLowerInvariant();
            if (names.Contains("window") || names.Contains("fence") || names.Contains("gate"))
                return false;
            for (int i = 0; i < FurnitureWords.Length; i++)
            {
                if (names.Contains(FurnitureWords[i]))
                    return true;
            }
            return false;
        }

        static Transform FurnitureOwner(Transform door)
        {
            if (door.name.ToLowerInvariant().Contains("locker") || door.parent == null)
                return door;
            return door.parent;
        }

        static Vector3 FurnitureInside(Openable door)
        {
            Bounds own;
            if (!RendererBounds(door.transform, out own))
                return door.transform.position;
            if (door.openByPosition)
                return own.center + Vector3.up * 0.05f;

            MeshFilter filter = door.GetComponentInChildren<MeshFilter>(true);
            if (filter == null || filter.sharedMesh == null)
                return own.center;
            Vector3 size = Vector3.Scale(filter.sharedMesh.bounds.size, Abs(filter.transform.lossyScale));
            Vector3 axis = size.x <= size.y && size.x <= size.z ? Vector3.right
                : size.y <= size.z ? Vector3.up : Vector3.forward;
            Vector3 inward = filter.transform.TransformDirection(axis).normalized;
            if (door.transform.parent != null && RendererBounds(door.transform.parent, out Bounds whole))
            {
                if (Vector3.Dot(whole.center - own.center, inward) < 0f)
                    inward = -inward;
            }
            return own.center + inward * 0.2f;
        }

        static Vector3 Abs(Vector3 v)
        {
            return new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
        }

        static bool RendererBounds(Transform root, out Bounds bounds)
        {
            bounds = default;
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            bool any = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null)
                    continue;
                if (!any)
                {
                    bounds = renderers[i].bounds;
                    any = true;
                }
                else
                    bounds.Encapsulate(renderers[i].bounds);
            }
            return any;
        }

        static string ContainerKey(Transform container)
        {
            string path = container.name;
            for (Transform t = container.parent; t != null; t = t.parent)
                path = t.name + "/" + path;
            Vector3 p = container.position;
            return container.gameObject.scene.name + ":" + path + "@" + Mathf.RoundToInt(p.x) + "," + Mathf.RoundToInt(p.y) + "," + Mathf.RoundToInt(p.z);
        }

        static GameObject BillVisual(int amount)
        {
            if (amount == 1)
                amount = 20;
            else if (amount == 5)
                amount = 50;
            else if (amount == 10)
                amount = 100;
            else if (amount == 200)
                amount = 20;
            else if (amount == 500)
                amount = 50;
            else if (amount == 10000)
                amount = 100;
            GameObject fallback = null;
            for (int i = 0; i < MoneyBills.Count; i++)
            {
                if (MoneyBills[i].Prefab == null)
                    continue;
                if (fallback == null || MoneyBills[i].Amount == 20)
                    fallback = MoneyBills[i].Prefab;
                if (MoneyBills[i].Amount == amount)
                    return MoneyBills[i].Prefab;
            }
            return fallback;
        }

        static bool TrySurface(FPP_Player player, out Vector3 point, out Vector3 normal)
        {
            point = player.transform.position;
            normal = Vector3.up;
            Transform view = player.cameraTransform != null ? player.cameraTransform : player.transform;
            RaycastHit[] hits = Physics.RaycastAll(view.position, view.forward, 8f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i].collider == null || hits[i].collider.transform.IsChildOf(player.transform))
                    continue;
                if (hits[i].normal.y >= 0.5f)
                {
                    point = hits[i].point;
                    normal = hits[i].normal;
                    return true;
                }
                Vector3 from = hits[i].point + hits[i].normal * 0.15f;
                if (FloorBelowSelf(player, from, 10f, out RaycastHit floor))
                {
                    point = floor.point;
                    normal = floor.normal;
                    return true;
                }
                break;
            }

            Vector3 ahead = view.position + view.forward * 2f;
            if (Physics.Raycast(ahead, Vector3.down, out RaycastHit down, 6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                point = down.point;
                normal = down.normal;
                return true;
            }
            return false;
        }

        static bool FloorBelowSelf(FPP_Player player, Vector3 start, float depth, out RaycastHit floor)
        {
            floor = default;
            RaycastHit[] hits = Physics.RaycastAll(start, Vector3.down, depth, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            bool found = false;
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i].collider == null || hits[i].collider.transform.IsChildOf(player.transform))
                    continue;
                if (hits[i].normal.y < 0.5f || hits[i].distance >= best)
                    continue;
                best = hits[i].distance;
                floor = hits[i];
                found = true;
            }
            return found;
        }

        static Quaternion FlatRotation(GameObject prefab, Vector3 normal, out float lift)
        {
            lift = 0.02f;
            float yaw = UnityEngine.Random.Range(0f, 360f);
            bool standing = false;
            MeshFilter filter = prefab.GetComponentInChildren<MeshFilter>(true);
            Bounds mesh = default;
            bool hasMesh = filter != null && filter.sharedMesh != null;
            if (hasMesh)
            {
                mesh = filter.sharedMesh.bounds;
                Vector3 size = mesh.size;
                standing = size.y >= size.x && size.y >= size.z;
            }
            Quaternion spin = standing ? Quaternion.Euler(90f, yaw, 0f) : Quaternion.Euler(0f, yaw, 0f);
            Quaternion rot = Quaternion.FromToRotation(Vector3.up, normal) * spin;
            if (!hasMesh)
                return rot;

            if (UnityEngine.Random.value < 0.5f)
            {
                Vector3 s = Vector3.Scale(mesh.size, Abs(filter.transform.lossyScale));
                int thin = s.x <= s.y && s.x <= s.z ? 0 : s.y <= s.z ? 1 : 2;
                Vector3 flipAxis = thin == 0 ? Vector3.forward : Vector3.right;
                rot *= Quaternion.AngleAxis(180f, flipAxis);
            }

            Matrix4x4 local = prefab.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            Matrix4x4 placed = Matrix4x4.TRS(Vector3.zero, rot, prefab.transform.localScale) * local;
            float lowest = float.MaxValue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3(
                    (i & 1) == 0 ? mesh.min.x : mesh.max.x,
                    (i & 2) == 0 ? mesh.min.y : mesh.max.y,
                    (i & 4) == 0 ? mesh.min.z : mesh.max.z);
                lowest = Mathf.Min(lowest, Vector3.Dot(placed.MultiplyPoint3x4(corner), normal));
            }
            lift = Mathf.Clamp(0.002f - lowest, -0.2f, 0.2f);
            return rot;
        }

        static bool IsHost(WorldManager world)
        {
            if (world == null)
                return false;
            try
            {
                if (world.isMasterClient)
                    return true;
            }
            catch (Exception)
            {
            }

            try
            {
                if (world.HasStateAuthority)
                    return true;
            }
            catch (Exception)
            {
            }

            FPP_Player player = world.localPlayer;
            if (player == null || player.Runner == null)
                return false;
            return player.Runner.IsSharedModeMasterClient || player.Runner.IsSinglePlayer;
        }

#if FRIEND
        const ulong AllowedHostSteamId = 0UL;

        static void PatchDemoLobby(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(MainMenu), "ClickPlay"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(BlockCreateGame)));
            harmony.Patch(AccessTools.Method(typeof(MainMenu), "PlayOnline"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(BlockCreateGame)));
            harmony.Patch(AccessTools.Method(typeof(MainMenu), "PlaySinglePlayer"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(BlockCreateGame)));
            MethodInfo entered = AccessTools.Method(typeof(SteamManager), "OnLobbyEntered");
            if (entered != null)
                harmony.Patch(entered, prefix: new HarmonyMethod(typeof(Plugin), nameof(BeforeLobbyEntered)));
            harmony.Patch(AccessTools.Method(typeof(Lobby), "Spawned"),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(KickIfHosted)));
            Log.LogInfo("Demo lock: cannot create a lobby. Join the host with invite or room code.");
        }

        static bool HostIdAllowed(ulong id)
        {
            return id == AllowedHostSteamId;
        }

        static bool HostIdAllowed(string id)
        {
            return !string.IsNullOrEmpty(id) && id.Trim() == AllowedHostSteamId.ToString();
        }

        static void RefuseMenu(string message)
        {
            Log.LogWarning(message);
            MainMenu menu = UnityEngine.Object.FindObjectOfType<MainMenu>();
            if (menu == null)
                return;
            try
            {
                menu.ActivatePage("MAIN");
            }
            catch (Exception)
            {
            }
            FPP_Player local = WorldManager.Instance != null ? WorldManager.Instance.localPlayer : null;
            if (local != null)
                local.ShowMessage(message, 5f, "bad", true);
        }

        static bool BlockCreateGame()
        {
            RefuseMenu("只能加入房主的房间，不能自己开房");
            return false;
        }

        static bool BeforeLobbyEntered(LobbyEnter_t callback)
        {
            CSteamID lobby = new CSteamID(callback.m_ulSteamIDLobby);
            ulong owner = 0;
            string tagged = "";
            try
            {
                owner = SteamMatchmaking.GetLobbyOwner(lobby).m_SteamID;
            }
            catch (Exception)
            {
            }
            try
            {
                tagged = SteamMatchmaking.GetLobbyData(lobby, "hostSteamID") ?? "";
            }
            catch (Exception)
            {
            }
            Log.LogInfo("Steam lobby owner=" + owner + " hostSteamID=" + tagged + " allowed=" + AllowedHostSteamId);
            if (owner == 0 && string.IsNullOrEmpty(tagged))
                return true;
            if (HostIdAllowed(owner) || HostIdAllowed(tagged))
                return true;
            try
            {
                SteamManager sm = UnityEngine.Object.FindObjectOfType<SteamManager>();
                if (sm != null)
                    sm.LeaveLobby();
            }
            catch (Exception)
            {
            }
            RefuseMenu("只能加入房主的房间");
            return false;
        }

        static void KickIfHosted(Lobby __instance)
        {
            try
            {
                if (__instance != null && __instance.isMasterClient)
                {
                    Log.LogWarning("Demo client became lobby host, leaving.");
                    RefuseMenu("只能加入房主的房间，不能自己开房");
                    if (NetworkManager.Instance != null)
                        NetworkManager.Instance.Shutdown();
                }
            }
            catch (Exception ex)
            {
                Log.LogError("Host check failed: " + ex.Message);
            }
        }
#endif

        static void Tell(string message)
        {
            Tell(message, 3f, "good");
        }

        static void Tell(string message, float seconds, string kind)
        {
            WorldManager world = WorldManager.Instance;
            if (world != null && world.localPlayer != null)
                world.localPlayer.ShowMessage(message, seconds, kind, true);
        }

        class Book
        {
            public readonly string Name;
            public readonly int Price;
            public readonly string[] Keys;
            public Item Item;
            public GameObject Prefab;

            public Book(string name, int price, params string[] keys)
            {
                Name = name;
                Price = price;
                Keys = keys;
            }
        }

        class SpawnPiece
        {
            public Item Item;
            public GameObject Prefab;
            public int Amount;
        }

        class Row
        {
            public Book Book;
            public TextMeshProUGUI Name;
            public TextMeshProUGUI Qty;
        }

        static class LootRounds
        {
            static readonly Dictionary<int, int> LastDay = new Dictionary<int, int>();
            static readonly Dictionary<int, int> Round = new Dictionary<int, int>();
            static readonly Dictionary<int, HashSet<string>> Looted = new Dictionary<int, HashSet<string>>();
            static bool _loaded;

            static string FilePath
            {
                get { return Path.Combine(Application.persistentDataPath, "skill-loot.txt"); }
            }

            public static bool Claim(WorldManager world, string key)
            {
                Load();
                int day;
                int left;
                try
                {
                    day = world.currentDay;
                    left = world.quotaManager != null ? world.quotaManager.daysRemaining : 0;
                }
                catch (Exception)
                {
                    return false;
                }
                int slot = PlayerPrefs.GetInt("SaveSlot", 0);
                int round = day + left;

                if (!Looted.TryGetValue(slot, out HashSet<string> keys))
                {
                    keys = new HashSet<string>();
                    Looted[slot] = keys;
                }
                if ((LastDay.TryGetValue(slot, out int last) && day < last) || !Round.TryGetValue(slot, out int saved) || saved != round)
                    keys.Clear();
                LastDay[slot] = day;
                Round[slot] = round;

                bool fresh = keys.Add(key);
                Save();
                return fresh;
            }

            static void Load()
            {
                if (_loaded)
                    return;
                _loaded = true;
                try
                {
                    if (!File.Exists(FilePath))
                        return;
                    foreach (string line in File.ReadAllLines(FilePath))
                    {
                        string[] parts = line.Split(new[] { '|' }, 4);
                        if (parts.Length < 4 || !int.TryParse(parts[0], out int slot) || !int.TryParse(parts[1], out int day) || !int.TryParse(parts[2], out int round))
                            continue;
                        LastDay[slot] = day;
                        Round[slot] = round;
                        if (!Looted.TryGetValue(slot, out HashSet<string> keys))
                        {
                            keys = new HashSet<string>();
                            Looted[slot] = keys;
                        }
                        if (parts[3].Length > 0)
                            keys.Add(parts[3]);
                    }
                }
                catch (Exception)
                {
                }
            }

            static void Save()
            {
                try
                {
                    var lines = new List<string>();
                    foreach (var pair in Looted)
                    {
                        int day = LastDay.TryGetValue(pair.Key, out int d) ? d : 0;
                        int round = Round.TryGetValue(pair.Key, out int r) ? r : 0;
                        if (pair.Value.Count == 0)
                            lines.Add(pair.Key + "|" + day + "|" + round + "|");
                        foreach (string key in pair.Value)
                            lines.Add(pair.Key + "|" + day + "|" + round + "|" + key);
                    }
                    File.WriteAllLines(FilePath, lines.ToArray());
                }
                catch (Exception ex)
                {
                    if (Log != null)
                        Log.LogError("Could not save looted containers: " + ex.Message);
                }
            }
        }

        public static class SkillSeeFx
        {
            public static bool Enabled;
            public static bool ShowLoot;
            static readonly HashSet<NPC> Revealed = new HashSet<NPC>();
            static readonly Dictionary<NPC, SkinnedMeshRenderer[]> Renderers = new Dictionary<NPC, SkinnedMeshRenderer[]>();
            static readonly HashSet<HighlightEffect> LootFx = new HashSet<HighlightEffect>();
            const float Distance = 30f;

            public static void Postfix(NPC __instance)
            {
                if (__instance == null)
                    return;
                if (!Enabled)
                {
                    if (Revealed.Contains(__instance))
                        Restore(__instance);
                    return;
                }
                FPP_Player player = WorldManager.Instance != null ? WorldManager.Instance.localPlayer : null;
                if (player == null)
                    return;
                if (Revealed.Contains(__instance))
                    return;
                if (Vector3.Distance(__instance.transform.position, player.transform.position) <= Distance)
                    Reveal(__instance);
            }

            public static void Maintain(Vector3 playerPosition)
            {
                if (!Enabled)
                {
                    if (Revealed.Count > 0)
                        Clear();
                    return;
                }
                if (ShowLoot && (Time.frameCount & 15) == 0)
                    MaintainLoot(playerPosition);
                if (Revealed.Count == 0 || (Time.frameCount & 7) != 0)
                    return;
                NPC[] copy = new NPC[Revealed.Count];
                Revealed.CopyTo(copy);
                float limit = Distance * Distance;
                for (int i = 0; i < copy.Length; i++)
                {
                    NPC npc = copy[i];
                    if (npc == null)
                    {
                        Revealed.Remove(npc);
                        Renderers.Remove(npc);
                        continue;
                    }
                    Vector3 delta = npc.transform.position - playerPosition;
                    if (delta.sqrMagnitude > limit)
                        Restore(npc);
                }
            }

            public static void Clear()
            {
                NPC[] copy = new NPC[Revealed.Count];
                Revealed.CopyTo(copy);
                for (int i = 0; i < copy.Length; i++)
                    Restore(copy[i]);
                Revealed.Clear();
                Renderers.Clear();
                ClearLoot();
            }

            static void MaintainLoot(Vector3 playerPosition)
            {
                if (!ShowLoot)
                {
                    ClearLoot();
                    return;
                }
                Pickupable[] items = UnityEngine.Object.FindObjectsOfType<Pickupable>();
                for (int i = 0; i < items.Length; i++)
                    MaybeLoot(items[i], playerPosition);
                Safe[] safes = UnityEngine.Object.FindObjectsOfType<Safe>();
                for (int i = 0; i < safes.Length; i++)
                    MaybeLoot(safes[i], playerPosition);
                ElectronicSafe[] eSafes = UnityEngine.Object.FindObjectsOfType<ElectronicSafe>();
                for (int i = 0; i < eSafes.Length; i++)
                    MaybeLoot(eSafes[i], playerPosition);
            }

            static void MaybeLoot(Component thing, Vector3 playerPosition)
            {
                if (thing == null)
                    return;
                if ((thing.transform.position - playerPosition).sqrMagnitude > Distance * Distance)
                    return;
                HighlightEffect fx = thing.GetComponent<HighlightEffect>();
                if (fx == null)
                    fx = thing.gameObject.AddComponent<HighlightEffect>();
                fx.enabled = true;
                fx.seeThrough = SeeThroughMode.AlwaysWhenOccluded;
                fx.seeThroughIntensity = 0.35f;
                fx.seeThroughTintAlpha = 0.4f;
                fx.seeThroughTintColor = new Color(1f, 0.85f, 0.2f, 1f);
                fx.seeThroughBorder = 1f;
                fx.seeThroughBorderOnly = true;
                fx.seeThroughBorderWidth = 0.12f;
                fx.outline = 0.35f;
                fx.outlineWidth = 0.15f;
                fx.outlineColor = new Color(1f, 0.85f, 0.2f, 1f);
                fx.outlineVisibility = Visibility.AlwaysOnTop;
                fx.highlighted = true;
                fx.UpdateMaterialProperties();
                LootFx.Add(fx);
            }

            static void ClearLoot()
            {
                foreach (HighlightEffect fx in LootFx)
                {
                    if (fx == null)
                        continue;
                    fx.highlighted = false;
                    fx.seeThrough = SeeThroughMode.Never;
                    fx.enabled = false;
                }
                LootFx.Clear();
            }

            static void Reveal(NPC npc)
            {
                bool first = Revealed.Add(npc);
                if (first)
                {
                    HighlightEffect effect = HighlightOf(npc);
                    if (effect == null)
                    {
                        Revealed.Remove(npc);
                        return;
                    }
                    effect.enabled = true;
                    effect.seeThrough = SeeThroughMode.AlwaysWhenOccluded;
                    effect.seeThroughIntensity = 0.35f;
                    effect.seeThroughTintAlpha = 0.45f;
                    effect.seeThroughTintColor = new Color(1f, 0.15f, 0.15f, 1f);
                    effect.seeThroughBorder = 1f;
                    effect.seeThroughBorderOnly = true;
                    effect.seeThroughBorderWidth = 0.15f;
                    effect.outline = 0.45f;
                    effect.outlineWidth = 0.2f;
                    effect.outlineColor = Color.red;
                    effect.outlineVisibility = Visibility.AlwaysOnTop;
                    effect.highlighted = true;
                    effect.UpdateMaterialProperties();
                    Renderers[npc] = npc.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                }
                if (!Renderers.TryGetValue(npc, out SkinnedMeshRenderer[] renderers) || renderers == null)
                    return;
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] != null)
                        renderers[i].enabled = true;
                }
            }

            static void Restore(NPC npc)
            {
                Revealed.Remove(npc);
                Renderers.Remove(npc);
                if (npc == null)
                    return;
                HighlightEffect effect = HighlightOf(npc);
                if (effect == null)
                    return;
                effect.seeThrough = SeeThroughMode.Never;
                effect.seeThroughBorderOnly = false;
                effect.outlineVisibility = Visibility.Normal;
                if (!npc.isHighlighted)
                    effect.highlighted = false;
                effect.UpdateMaterialProperties();
            }

            static bool _highlightLooked;
            static FieldInfo _highlightField;

            static HighlightEffect HighlightOf(NPC npc)
            {
                if (!_highlightLooked)
                {
                    _highlightLooked = true;
                    _highlightField = typeof(NPC).GetField("highlight", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                }
                if (_highlightField == null)
                    return null;
                return _highlightField.GetValue(npc) as HighlightEffect;
            }
        }

        static float _nextBoardPaint;

        static void PaintAllCompetitionBoards()
        {
            if (Time.unscaledTime < _nextBoardPaint)
                return;
            _nextBoardPaint = Time.unscaledTime + 0.35f;
            PVP_ModeButton[] buttons = UnityEngine.Object.FindObjectsOfType<PVP_ModeButton>();
            for (int i = 0; i < buttons.Length; i++)
                PaintModeButton(buttons[i]);
            PVP_Scoreboard board = PVP_Scoreboard.Instance;
            if (board != null)
                PaintScoreboardReward(board);
        }

        static bool BeforeModeButtonStart(PVP_ModeButton __instance)
        {
            RewireCompetition(__instance, out _, out _);
            return true;
        }

        static void AfterModeButtonStart(PVP_ModeButton __instance)
        {
            PaintModeButton(__instance);
        }

        static void BeforeRefreshCompetition(PVP_Scoreboard __instance)
        {
            if (__instance != null)
                RewireCompetition(__instance.competitionModeButton, out _, out _);
        }

        static void AfterRefreshCompetition(PVP_Scoreboard __instance)
        {
            PaintScoreboardReward(__instance);
        }

        static void BeforeCompetitionVictory(PVP_Scoreboard __instance)
        {
            if (__instance != null)
                RewireCompetition(__instance.competitionModeButton, out _, out _);
        }

        static void AfterCompetitionVictory(PVP_Scoreboard __instance, NetworkId playerId)
        {
            if (__instance == null || __instance.Runner == null)
                return;
            if (!RewireCompetition(__instance.competitionModeButton, out int xp, out int cash))
                return;
            WorldManager world = WorldManager.Instance;
            if (world == null || world.localPlayer == null)
                return;
            NetworkObject obj = __instance.Runner.FindObject(playerId);
            if (obj == null)
                return;
            FPP_Player winner = obj.GetComponent<FPP_Player>();
            if (winner == null || winner.nickname != world.localPlayer.nickname)
                return;
            if (cash > 0)
            {
                Cash.Add(cash);
                _cashShown = int.MinValue;
            }
            Tell("竞赛获胜：" + xp + "XP " + cash + "现金", 5f, "good");
            Log.LogInfo("Competition win " + xp + " XP " + cash + " cash. Balance " + Cash.Value);
        }

        static bool RewireCompetition(PVP_ModeButton button, out int xp, out int cash)
        {
            xp = 0;
            cash = 0;
            if (button == null)
                return false;
            if (button.scoreGoal == 2500)
            {
                xp = 25;
                cash = 500;
            }
            else if (button.scoreGoal == 5000)
            {
                xp = 50;
                cash = 1000;
            }
            else if (button.scoreGoal == 7500)
            {
                xp = 100;
                cash = 3000;
            }
            else
                return false;
            button.rewardIsXP = true;
            button.rewardIsItem = false;
            button.xpReward = xp;
            return true;
        }

        static void PaintModeButton(PVP_ModeButton button)
        {
            if (button == null || button.infoTxt == null)
                return;
            if (!RewireCompetition(button, out int xp, out int cash))
                return;
            PVP_Scoreboard board = button.scoreboard != null ? button.scoreboard : PVP_Scoreboard.Instance;
            string scoreWord = Loc("score");
            string rewardWord = Loc("reward:");
            string t = scoreWord + ": <color=#" + Html(board != null ? board.scoreCol : new Color32(80, 220, 80, 255)) + ">" + button.scoreGoal + "</color>";
            if (button.timeLimit > 0)
                t += "\n" + Loc("time limit") + ": " + button.timeLimit + ":00";
            t += "\n" + rewardWord + " " + RewardRich(board, xp, cash);
            button.infoTxt.text = t;
        }

        static void PaintScoreboardReward(PVP_Scoreboard board)
        {
            if (board == null || board.competitionRewardTxt == null)
                return;
            if (!RewireCompetition(board.competitionModeButton, out int xp, out int cash))
                return;
            board.competitionRewardTxt.text = Loc("reward:") + " " + RewardRich(board, xp, cash);
        }

        static string RewardRich(PVP_Scoreboard board, int xp, int cash)
        {
            return "<color=#" + Html(board != null ? board.xpRewardCol : new Color32(80, 220, 80, 255)) + ">" + xp + "XP</color> <color=#FFC440>" + cash + "现金</color>";
        }

        static string Loc(string key)
        {
            try
            {
                string t = LocalizationManager.GetTranslation(key, true, 0, true, false, null, null, true);
                if (!string.IsNullOrEmpty(t))
                    return t;
            }
            catch (Exception)
            {
            }
            return key;
        }

        static string Html(Color32 color)
        {
            return ColorUtility.ToHtmlStringRGBA(color);
        }

        static class Cash
        {
            static int _value = -1;

            static string FilePath
            {
                get { return Path.Combine(Application.persistentDataPath, "skill-cash.txt"); }
            }

            public static int Value
            {
                get
                {
                    Load();
                    return _value;
                }
            }

            public static void Load()
            {
                if (_value >= 0)
                    return;
                _value = 0;
                try
                {
                    if (File.Exists(FilePath) && int.TryParse(File.ReadAllText(FilePath), out int saved))
                        _value = Mathf.Max(0, saved);
                }
                catch (Exception)
                {
                }
            }

            public static void Add(int amount)
            {
                Load();
                _value = Mathf.Max(0, _value + amount);
                try
                {
                    File.WriteAllText(FilePath, _value.ToString());
                }
                catch (Exception ex)
                {
                    if (Log != null)
                        Log.LogError("Could not save cash: " + ex.Message);
                }
            }
        }
    }
}
