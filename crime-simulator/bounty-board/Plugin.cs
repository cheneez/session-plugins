using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using Fusion;
using HarmonyLib;
using HighlightPlus;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace BountyMod
{
    [BepInPlugin("com.kaboo.crimesim.bounty", "赏金", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        static ManualLogSource Log;
        static int _bounty;
        static int _resetDay = int.MinValue;
        static float _policeHitUntil;
        static readonly HashSet<int> PaidCops = new HashSet<int>();
        static readonly HashSet<int> PaidLoot = new HashSet<int>();
        static TextMeshProUGUI _cutBounty;
        static int _cutHudId;
        static bool _boardOpen = true;
        static string _boardScene;
        static GameObject _toast;
        static TextMeshProUGUI _toastText;
        static string _bountyToast;
        static float _bountyToastUntil;
        static string _heldSkillMsg;
        static float _heldSkillTime = 4f;
        static string _heldSkillKind = "bad";

        const string ChatBounty = "§SCBY:";
        const string ChatHost = "§SCBH:";
        const string ChatPay = "§SCBP:";
        const string ChatCop = "§SCPK:";
        const string ChatExpose = "§SCPE:";
        const int CopNormal = 0;
        const int CopSpeed = 1;
        const int CopTank = 2;
        const int CopOmni = 3;
        static readonly Dictionary<int, int> CopNetKind = new Dictionary<int, int>();
        static readonly Dictionary<int, int> CopInstKind = new Dictionary<int, int>();
        static readonly HashSet<int> CopReady = new HashSet<int>();
        static readonly FieldInfo CopHighlight = AccessTools.Field(typeof(NPC), "highlight");
        static readonly List<NPC> KnownCops = new List<NPC>();
        static readonly Dictionary<int, HighlightEffect> CopFxCache = new Dictionary<int, HighlightEffect>();
        static readonly Dictionary<int, int> CopOutlineOn = new Dictionary<int, int>();
        static float _nextCopScan;
        static float _nextCopTick;
        static float _nextBoardPlace;
        static string _exposedName = "";
        static bool _iAmExposed;
        const string PaidLootTag = "§SCBL";
        const string HomeScene = "PlayerHideout";

        static readonly int[] MissionNeed = { 1000, 2000, 3000, 4000, 5000 };
        static readonly int[] MissionXp = { 100, 150, 200, 300, 500 };
        static readonly int[] MissionCash = { 500, 1000, 2000, 5000, 10000 };

        static int _mission;
        static bool _pending;
        static int _lastPaid = -1;
        static bool _loadedMission;
        static float _nextBroadcast;
        static float _nextHostBroadcast;
        static int _lastSentBounty = int.MinValue;
        static readonly Dictionary<string, int> Scores = new Dictionary<string, int>();
        static readonly Dictionary<string, float> ScoreAt = new Dictionary<string, float>();

        static GameObject _board;
        static TextMeshProUGUI _boardText;
        static TMP_FontAsset _hudFont;
        static Material _hudMat;

        void Awake()
        {
            Log = Logger;
            LoadMission();
            Harmony harmony = new Harmony("com.kaboo.crimesim.bounty");
            harmony.Patch(AccessTools.Method(typeof(FPP_Player), "Update"),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterPlayerUpdate)));
            harmony.Patch(AccessTools.Method(typeof(FPP_Player), "PlayerInfoUI"),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterCutHud)));
            harmony.Patch(AccessTools.Method(typeof(FPP_Player), "PlayerDeath"),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterPlayerDeath)));
            harmony.Patch(AccessTools.Method(typeof(FPP_Player), "ShowMessage"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(BeforeShowMessage)));
            harmony.Patch(AccessTools.Method(typeof(FPP_Player), "RPC_Tazed"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(MarkPoliceHit)));
            harmony.Patch(AccessTools.Method(typeof(PlayerInventory), "SetDroppedValues"),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterDroppedLoot)));
            harmony.Patch(AccessTools.Method(typeof(PlayerInventory), "PickedItem"),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterPickedItem)));
            MethodInfo vanLoot = AccessTools.Method(typeof(SaveTruckItems), "DetectedNewLoot");
            if (vanLoot != null)
                harmony.Patch(vanLoot, postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterVanLoot)));
            MethodInfo trunkLoot = AccessTools.Method(typeof(CarTrunk), "DetectedNewLoot");
            if (trunkLoot != null)
                harmony.Patch(trunkLoot, postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterTrunkLoot)));
            harmony.Patch(AccessTools.Method(typeof(NPC), "RPC_KnockDown"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(BeforeCopDown)),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterCopDown)));
            harmony.Patch(AccessTools.Method(typeof(FPP_Player), "SpawnChatMessage"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(HideBountyChat)));
            MethodInfo attack = AccessTools.Method(typeof(AttackSensor), "TriggerAttack");
            if (attack != null)
                harmony.Patch(attack, prefix: new HarmonyMethod(typeof(Plugin), nameof(BeforeNpcAttack)));
            MethodInfo heli = AccessTools.Method(typeof(PoliceHelicopter), "TryShoot");
            if (heli != null)
                harmony.Patch(heli, prefix: new HarmonyMethod(typeof(Plugin), nameof(MarkPoliceHit)));
            MethodInfo sniper = AccessTools.Method(typeof(NPC_Sniper), "KillPlayer");
            if (sniper != null)
                harmony.Patch(sniper, prefix: new HarmonyMethod(typeof(Plugin), nameof(MarkPoliceHit)));
            harmony.Patch(AccessTools.Method(typeof(PoliceCar), "ArrivedAtScene"),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterCopCarArrive)));
            Log.LogInfo("赏金 ready");
        }

        static FPP_Player LocalPlayer()
        {
            WorldManager world = WorldManager.Instance;
            return world != null ? world.localPlayer : null;
        }

        static bool IsLocal(FPP_Player player)
        {
            return player != null && player == LocalPlayer();
        }

        static bool IsHost()
        {
            FPP_Player player = LocalPlayer();
            if (player == null || player.Runner == null)
                return false;
            return player.Runner.IsSharedModeMasterClient || player.Runner.IsSinglePlayer;
        }

        static string LocalName()
        {
            FPP_Player player = LocalPlayer();
            if (player == null || string.IsNullOrEmpty(player.nickname))
                return "我";
            return player.nickname;
        }

        static bool IsCop(NPC npc)
        {
            try
            {
                return npc != null && (npc.isPolice || npc.isWanderingPolice);
            }
            catch (Exception)
            {
                return false;
            }
        }

        static int CopNetId(NPC npc)
        {
            try
            {
                if (npc != null && npc.Object != null)
                    return (int)npc.Object.Id.Raw;
            }
            catch (Exception)
            {
            }
            return 0;
        }

        static int CurrentStars()
        {
            try
            {
                WorldManager world = WorldManager.Instance;
                if (world != null && world.policeManager != null)
                    return Mathf.Clamp(world.policeManager.currentChaseLevel, 1, 3);
            }
            catch (Exception)
            {
            }
            return 1;
        }

        static int RollCopKind(int stars)
        {
            int roll = UnityEngine.Random.Range(0, 100);
            if (stars >= 3)
            {
                if (roll < 50)
                    return CopNormal;
                if (roll < 70)
                    return CopSpeed;
                if (roll < 90)
                    return CopTank;
                return CopOmni;
            }
            if (stars >= 2)
            {
                if (roll < 60)
                    return CopNormal;
                if (roll < 75)
                    return CopSpeed;
                if (roll < 90)
                    return CopTank;
                return CopOmni;
            }
            if (roll < 70)
                return CopNormal;
            if (roll < 85)
                return CopSpeed;
            if (roll < 95)
                return CopTank;
            return CopOmni;
        }

        static void AfterCopCarArrive(PoliceCar __instance)
        {
            if (__instance == null || __instance.spawnedPoliceman == null)
                return;
            NPC cop = __instance.spawnedPoliceman.GetComponent<NPC>();
            AssignCopKind(cop, true);
        }

        static void AssignCopKind(NPC cop, bool announce)
        {
            if (!IsCop(cop))
                return;
            int localId = cop.GetInstanceID();
            int net = CopNetId(cop);
            int kind;
            if (net != 0 && CopNetKind.TryGetValue(net, out kind))
            {
            }
            else if (!IsHost())
            {
                return;
            }
            else
            {
                kind = RollCopKind(CurrentStars());
                if (net != 0)
                    CopNetKind[net] = kind;
            }
            CopInstKind[localId] = kind;
            if (!CopReady.Contains(localId))
            {
                bool auth = false;
                try
                {
                    auth = cop.HasStateAuthority;
                }
                catch (Exception)
                {
                }
                if (auth)
                    ApplyCopKind(cop, kind);
                CopReady.Add(localId);
                if (IsHost() && net != 0)
                    Chat(LocalName(), ChatCop + net + "," + kind);
                if (kind != CopNormal && Log != null)
                    Log.LogInfo("Special cop " + CopKindName(kind) + " net=" + net);
            }
            RememberCop(cop);
        }

        static void RememberCop(NPC cop)
        {
            if (cop == null)
                return;
            for (int i = 0; i < KnownCops.Count; i++)
            {
                if (KnownCops[i] == cop)
                    return;
            }
            KnownCops.Add(cop);
        }

        static void TickCops()
        {
            if (Time.unscaledTime >= _nextCopScan)
            {
                _nextCopScan = Time.unscaledTime + 2.5f;
                NPC[] all = UnityEngine.Object.FindObjectsOfType<NPC>();
                KnownCops.Clear();
                for (int i = 0; i < all.Length; i++)
                {
                    if (IsCop(all[i]))
                        KnownCops.Add(all[i]);
                }
            }
            if (Time.unscaledTime < _nextCopTick)
                return;
            _nextCopTick = Time.unscaledTime + 0.15f;
            FPP_Player player = LocalPlayer();
            for (int i = KnownCops.Count - 1; i >= 0; i--)
            {
                NPC cop = KnownCops[i];
                if (cop == null)
                {
                    KnownCops.RemoveAt(i);
                    continue;
                }
                AssignCopKind(cop, false);
                int kind;
                if (!CopInstKind.TryGetValue(cop.GetInstanceID(), out kind))
                    continue;
                TickCopVision(cop, kind);
                TickCopOutline(cop, kind, player);
            }
            TickTrackerShare();
        }

        static void ApplyCopKind(NPC cop, int kind)
        {
            if (kind == CopSpeed)
            {
                cop.walkSpeed *= 1.5f;
                cop.runSpeed *= 1.5f;
                cop.runSpeedMulti *= 1.5f;
            }
            else if (kind == CopTank)
            {
                cop.maxHealth *= 5f;
                cop.health *= 5f;
            }
            else if (kind == CopOmni)
            {
                cop.losePlayerTime = 999f;
                cop.extraSightMultiplier = 80f;
                cop.thresholdToNoticePlayer = 0f;
                cop.alwaysRunWhenCheckout = true;
                cop.maxLookAngle = 180f;
            }
        }

        static void TickCopVision(NPC cop, int kind)
        {
            if (kind != CopOmni || cop.knockedDown)
                return;
            try
            {
                if (!cop.HasStateAuthority)
                    return;
            }
            catch (Exception)
            {
                return;
            }
            FPP_Player target = ClosestLivingPlayer(cop);
            if (target != null)
                cop.nearestPlayer = target;
            else
                cop.GetNearestPlayer(true);
            cop.seesPlayer = true;
            cop.playerInFOV = true;
            cop.lostPlayerTimer = 0f;
        }

        static FPP_Player ClosestLivingPlayer(NPC cop)
        {
            if (cop == null)
                return null;
            FPP_Player[] all = UnityEngine.Object.FindObjectsOfType<FPP_Player>();
            FPP_Player best = null;
            float bestDist = float.MaxValue;
            Vector3 pos = cop.transform.position;
            for (int i = 0; i < all.Length; i++)
            {
                FPP_Player player = all[i];
                if (player == null)
                    continue;
                try
                {
                    if (player.isDead)
                        continue;
                }
                catch (Exception)
                {
                }
                float dist = (player.transform.position - pos).sqrMagnitude;
                if (dist >= bestDist)
                    continue;
                bestDist = dist;
                best = player;
            }
            return best;
        }

        static NPC AliveTracker()
        {
            for (int i = 0; i < KnownCops.Count; i++)
            {
                NPC cop = KnownCops[i];
                if (cop == null)
                    continue;
                try
                {
                    if (cop.knockedDown)
                        continue;
                }
                catch (Exception)
                {
                    continue;
                }
                int kind;
                if (!CopInstKind.TryGetValue(cop.GetInstanceID(), out kind))
                    continue;
                if (kind == CopOmni)
                    return cop;
            }
            return null;
        }

        static void TickTrackerShare()
        {
            if (!IsHost())
                return;
            NPC tracker = AliveTracker();
            if (tracker == null)
            {
                SetExposedName("");
                return;
            }
            try
            {
                if (!tracker.HasStateAuthority)
                    return;
            }
            catch (Exception)
            {
                return;
            }
            FPP_Player target = ClosestLivingPlayer(tracker);
            if (target != null)
                tracker.nearestPlayer = target;
            else
                tracker.GetNearestPlayer(true);
            target = tracker.nearestPlayer;
            if (target == null)
            {
                SetExposedName("");
                return;
            }
            try
            {
                WorldManager world = WorldManager.Instance;
                if (world != null && world.policeManager != null)
                    world.policeManager.RPC_UpdateLastPosition(target.transform.position);
            }
            catch (Exception)
            {
            }
            SetExposedName(PlayerKey(target));
        }

        static string PlayerKey(FPP_Player player)
        {
            if (player == null)
                return "";
            if (!string.IsNullOrEmpty(player.nickname))
                return player.nickname;
            try
            {
                if (player.Object != null)
                    return "#" + (int)player.Object.Id.Raw;
            }
            catch (Exception)
            {
            }
            return "";
        }

        static void SetExposedName(string name)
        {
            if (name == null)
                name = "";
            if (name == _exposedName)
                return;
            _exposedName = name;
            if (IsHost())
                Chat(LocalName(), ChatExpose + name);
            ApplyExposeLocal(name);
        }

        static void ApplyExposeLocal(string name)
        {
            bool me = IsMyKey(name);
            if (me == _iAmExposed)
                return;
            _iAmExposed = me;
            if (me)
                Tell("你的位置已暴露", 3.5f, "bad");
            else
                Tell("你的位置现在不会暴露了", 3.5f, "good");
        }

        static bool IsMyKey(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            FPP_Player me = LocalPlayer();
            if (me == null)
                return false;
            if (!string.IsNullOrEmpty(me.nickname)
                && string.Equals(name, me.nickname, StringComparison.OrdinalIgnoreCase))
                return true;
            return name == PlayerKey(me);
        }

        static bool PlayerSeesCop(NPC cop, FPP_Player player)
        {
            if (cop == null || player == null || cop.knockedDown)
                return false;
            Transform cam = player.cameraTransform != null ? player.cameraTransform : player.transform;
            Vector3 head = cop.transform.position + Vector3.up * 1.6f;
            Vector3 from = cam.position;
            Vector3 delta = head - from;
            float dist = delta.magnitude;
            if (dist > 30f)
                return false;
            if (dist < 1.5f)
                return true;
            if (Vector3.Dot(cam.forward, delta / dist) < 0.15f)
                return false;
            return true;
        }

        static void TickCopOutline(NPC cop, int kind, FPP_Player player)
        {
            if (kind == CopNormal)
            {
                SetCopOutline(cop, Color.white, false);
                return;
            }
            bool show = PlayerSeesCop(cop, player);
            Color color = kind == CopSpeed ? new Color(0.15f, 0.55f, 1f, 1f)
                : kind == CopTank ? new Color(1f, 0.12f, 0.1f, 1f)
                : new Color(1f, 0.9f, 0.1f, 1f);
            SetCopOutline(cop, color, show);
        }

        static HighlightEffect CopFx(NPC cop)
        {
            if (cop == null)
                return null;
            int id = cop.GetInstanceID();
            HighlightEffect fx;
            if (CopFxCache.TryGetValue(id, out fx) && fx != null)
                return fx;
            fx = CopHighlight != null ? CopHighlight.GetValue(cop) as HighlightEffect : null;
            if (fx == null)
                fx = cop.GetComponent<HighlightEffect>();
            if (fx == null)
                fx = cop.GetComponentInChildren<HighlightEffect>(true);
            if (fx == null)
                fx = cop.gameObject.AddComponent<HighlightEffect>();
            CopFxCache[id] = fx;
            return fx;
        }

        static void SetCopOutline(NPC cop, Color color, bool on)
        {
            HighlightEffect fx = CopFx(cop);
            if (fx == null)
                return;
            int id = cop.GetInstanceID();
            if (!on)
            {
                int last;
                if (CopOutlineOn.TryGetValue(id, out last) && last == 0)
                    return;
                CopOutlineOn[id] = 0;
                fx.highlighted = false;
                return;
            }
            CopOutlineOn[id] = 1;
            fx.enabled = true;
            fx.profileSync = false;
            fx.ignore = false;
            fx.outline = 1f;
            fx.outlineWidth = 0.35f;
            fx.outlineColor = color;
            fx.outlineVisibility = Visibility.AlwaysOnTop;
            fx.glow = 0.35f;
            fx.glowHQColor = color;
            fx.glowVisibility = Visibility.AlwaysOnTop;
            fx.overlay = 0f;
            fx.innerGlow = 0f;
            fx.seeThrough = SeeThroughMode.Never;
            fx.highlighted = true;
            fx.UpdateMaterialProperties();
        }

        static string CopKindName(int kind)
        {
            if (kind == CopSpeed)
                return "速度警察";
            if (kind == CopTank)
                return "血量警察";
            if (kind == CopOmni)
                return "追踪警察";
            return "普通警察";
        }

        static string MissionPath()
        {
            return Path.Combine(Application.persistentDataPath, "bounty-mission.txt");
        }

        static void LoadMission()
        {
            if (_loadedMission)
                return;
            _loadedMission = true;
            try
            {
                string path = MissionPath();
                if (!File.Exists(path))
                    return;
                string[] lines = File.ReadAllLines(path);
                if (lines.Length > 0)
                    int.TryParse(lines[0], out _mission);
                if (lines.Length > 1)
                    _pending = lines[1] == "1";
                _mission = Mathf.Clamp(_mission, 0, 5);
            }
            catch (Exception)
            {
            }
        }

        static void SaveMission()
        {
            try
            {
                File.WriteAllText(MissionPath(), _mission + "\n" + (_pending ? "1" : "0"));
            }
            catch (Exception)
            {
            }
        }

        static void ResetNight()
        {
            WorldManager world = WorldManager.Instance;
            if (world == null)
                return;
            int day = world.currentDay;
            if (day == _resetDay)
                return;
            _resetDay = day;
            _bounty = 0;
            PaidCops.Clear();
            PaidLoot.Clear();
            CopNetKind.Clear();
            CopInstKind.Clear();
            CopReady.Clear();
            KnownCops.Clear();
            CopFxCache.Clear();
            CopOutlineOn.Clear();
            SetExposedName("");
        }

        static void AfterPlayerUpdate(FPP_Player __instance)
        {
            if (!IsLocal(__instance))
                return;
            ResetNight();
            string scene = SceneManager.GetActiveScene().name;
            if (scene != _boardScene)
            {
                _boardScene = scene;
                if (OnNightJobMap())
                    _boardOpen = true;
            }
            if (Input.GetKeyDown(KeyCode.Tab))
                _boardOpen = !_boardOpen;
            RememberScore(LocalName(), _bounty);
            BroadcastMine();
            if (IsHost())
            {
                UpdatePending();
                TryPayAtHome();
                BroadcastHost();
            }
            RefreshBoard();
            TickDeathToast(__instance);
            TickCops();
        }

        static void RememberScore(string name, int amount)
        {
            if (string.IsNullOrEmpty(name))
                return;
            Scores[name] = Mathf.Max(0, amount);
            ScoreAt[name] = Time.unscaledTime;
        }

        static int TeamTotal()
        {
            PruneScores();
            RememberScore(LocalName(), _bounty);
            int total = 0;
            foreach (KeyValuePair<string, int> pair in Scores)
                total += pair.Value;
            return total;
        }

        static void PruneScores()
        {
            string local = LocalName();
            List<string> drop = null;
            foreach (KeyValuePair<string, float> pair in ScoreAt)
            {
                if (pair.Key == local)
                    continue;
                if (Time.unscaledTime - pair.Value < 20f)
                    continue;
                if (drop == null)
                    drop = new List<string>();
                drop.Add(pair.Key);
            }
            if (drop == null)
                return;
            for (int i = 0; i < drop.Count; i++)
            {
                Scores.Remove(drop[i]);
                ScoreAt.Remove(drop[i]);
            }
        }

        static void UpdatePending()
        {
            if (_pending || _mission >= MissionNeed.Length)
                return;
            if (TeamTotal() < MissionNeed[_mission])
                return;
            _pending = true;
            SaveMission();
        }

        static void TryPayAtHome()
        {
            if (!_pending || _mission >= MissionNeed.Length)
                return;
            if (SceneManager.GetActiveScene().name != HomeScene)
                return;
            PayMission(_mission);
        }

        static void PayMission(int mission)
        {
            if (mission < 0 || mission >= MissionNeed.Length || _lastPaid == mission)
                return;
            _lastPaid = mission;
            GrantReward(mission);
            Tell("悬赏奖励 " + MissionXp[mission] + "XP+" + MissionCash[mission] + "现金", 3f, "good");
            _mission = mission + 1;
            _pending = false;
            SaveMission();
            Chat("", ChatPay + mission);
        }

        static void GrantReward(int mission)
        {
            if (mission < 0 || mission >= MissionNeed.Length)
                return;
            WorldManager world = WorldManager.Instance;
            if (world != null)
            {
                try
                {
                    world.RPC_AddXP(MissionXp[mission]);
                }
                catch (Exception)
                {
                }
            }
            AddCash(MissionCash[mission]);
        }

        static void BroadcastMine()
        {
            if (Time.unscaledTime < _nextBroadcast && _lastSentBounty == _bounty)
                return;
            _nextBroadcast = Time.unscaledTime + 0.6f;
            _lastSentBounty = _bounty;
            Chat(LocalName(), ChatBounty + _bounty);
        }

        static void BroadcastHost()
        {
            if (Time.unscaledTime < _nextHostBroadcast)
                return;
            _nextHostBroadcast = Time.unscaledTime + 2f;
            Chat(LocalName(), ChatHost + _mission + "," + (_pending ? "1" : "0"));
            BroadcastCopKinds();
            Chat(LocalName(), ChatExpose + _exposedName);
        }

        static void BroadcastCopKinds()
        {
            if (CopNetKind.Count == 0)
                return;
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<int, int> pair in CopNetKind)
            {
                if (sb.Length > 0)
                    sb.Append(';');
                sb.Append(pair.Key);
                sb.Append(',');
                sb.Append(pair.Value);
                if (sb.Length < 90)
                    continue;
                Chat(LocalName(), ChatCop + sb);
                sb.Length = 0;
            }
            if (sb.Length > 0)
                Chat(LocalName(), ChatCop + sb);
        }

        static void Chat(string nick, string content)
        {
            WorldManager world = WorldManager.Instance;
            if (world == null || world.loadingScene)
                return;
            try
            {
                world.RPC_SpawnChatMessage(nick ?? "", content, "");
            }
            catch (Exception)
            {
            }
        }

        static bool HideBountyChat(string _nickname, string content)
        {
            if (string.IsNullOrEmpty(content))
                return true;
            if (content.StartsWith(ChatBounty, StringComparison.Ordinal))
            {
                if (int.TryParse(content.Substring(ChatBounty.Length), out int amount))
                    RememberScore(string.IsNullOrEmpty(_nickname) ? "玩家" : _nickname, amount);
                return false;
            }
            if (content.StartsWith(ChatHost, StringComparison.Ordinal))
            {
                string[] parts = content.Substring(ChatHost.Length).Split(',');
                if (parts.Length > 0 && int.TryParse(parts[0], out int mission))
                    _mission = Mathf.Clamp(mission, 0, 5);
                if (parts.Length > 1)
                    _pending = parts[1] == "1";
                return false;
            }
            if (content.StartsWith(ChatPay, StringComparison.Ordinal))
            {
                if (int.TryParse(content.Substring(ChatPay.Length), out int mission) && _lastPaid != mission)
                {
                    _lastPaid = mission;
                    GrantReward(mission);
                    Tell("悬赏奖励 " + MissionXp[mission] + "XP+" + MissionCash[mission] + "现金", 3f, "good");
                    _mission = Mathf.Clamp(mission + 1, 0, 5);
                    _pending = false;
                    SaveMission();
                }
                return false;
            }
            if (content.StartsWith(ChatCop, StringComparison.Ordinal))
            {
                string[] cops = content.Substring(ChatCop.Length).Split(';');
                for (int i = 0; i < cops.Length; i++)
                {
                    string[] bits = cops[i].Split(',');
                    if (bits.Length >= 2 && int.TryParse(bits[0], out int net) && int.TryParse(bits[1], out int kind))
                        CopNetKind[net] = kind;
                }
                return false;
            }
            if (content.StartsWith(ChatExpose, StringComparison.Ordinal))
            {
                string name = content.Substring(ChatExpose.Length);
                _exposedName = name;
                ApplyExposeLocal(name);
                return false;
            }
            return true;
        }

        static void AfterCutHud(FPP_Player __instance)
        {
            if (__instance == null || __instance.yourCutTxt == null)
                return;
            if (!IsLocal(__instance))
                return;
            ResetNight();
            PlaceCutBounty(__instance);
            if (__instance.yourCutTxt.font != null)
            {
                _hudFont = __instance.yourCutTxt.font;
                _hudMat = __instance.yourCutTxt.fontSharedMaterial;
            }
        }

        static TMP_Text FindCutTitle(TMP_Text percent)
        {
            Transform parent = percent.transform.parent;
            if (parent == null)
                return null;
            TMP_Text[] labels = parent.GetComponentsInChildren<TMP_Text>(true);
            TMP_Text best = null;
            float bestX = float.MaxValue;
            for (int i = 0; i < labels.Length; i++)
            {
                TMP_Text label = labels[i];
                if (label == null || label == percent || label == _cutBounty)
                    continue;
                string value = label.text;
                if (string.IsNullOrEmpty(value))
                    continue;
                if (value.IndexOf("提成", StringComparison.Ordinal) < 0
                    && value.IndexOf("cut", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                float x = label.rectTransform.anchoredPosition.x;
                if (x < bestX)
                {
                    bestX = x;
                    best = label;
                }
            }
            return best;
        }

        static void HideVanillaCut(TextMeshProUGUI percent)
        {
            percent.maxVisibleCharacters = 0;
            TMP_Text title = FindCutTitle(percent);
            if (title != null && title != _cutBounty)
                title.maxVisibleCharacters = 0;
        }

        static void PlaceCutBounty(FPP_Player player)
        {
            TextMeshProUGUI percent = player.yourCutTxt;
            int id = percent.GetInstanceID();
            if (_cutBounty == null || _cutHudId != id)
            {
                if (_cutBounty != null)
                    UnityEngine.Object.Destroy(_cutBounty.gameObject);
                GameObject go = new GameObject("BountyCut", typeof(RectTransform), typeof(TextMeshProUGUI));
                go.transform.SetParent(percent.transform, false);
                _cutBounty = go.GetComponent<TextMeshProUGUI>();
                _cutBounty.font = percent.font;
                _cutBounty.fontSharedMaterial = percent.fontSharedMaterial;
                _cutBounty.fontSize = percent.fontSize;
                _cutBounty.color = percent.color;
                _cutBounty.alignment = TextAlignmentOptions.MidlineRight;
                _cutBounty.raycastTarget = false;
                _cutBounty.overflowMode = TextOverflowModes.Overflow;
                RectTransform rect = _cutBounty.rectTransform;
                rect.anchorMin = new Vector2(1f, 0.5f);
                rect.anchorMax = new Vector2(1f, 0.5f);
                rect.pivot = new Vector2(1f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                rect.localScale = Vector3.one;
                rect.localRotation = Quaternion.identity;
                _cutHudId = id;
            }
            HideVanillaCut(percent);
            Color shown = percent.color;
            shown.a = 1f;
            _cutBounty.color = shown;
            string cut = percent.text;
            if (string.IsNullOrEmpty(cut))
                cut = "30%";
            _cutBounty.text = "赏金：$" + _bounty + " | 你的提成：" + cut;
            RectTransform hug = _cutBounty.rectTransform;
            hug.anchoredPosition = Vector2.zero;
            hug.localScale = Vector3.one;
            hug.sizeDelta = new Vector2(_cutBounty.preferredWidth + 4f, Mathf.Max(percent.rectTransform.rect.height, percent.fontSize + 4f));
        }

        static bool OnNightJobMap()
        {
            string scene = SceneManager.GetActiveScene().name;
            if (string.IsNullOrEmpty(scene) || scene == HomeScene)
                return false;
            string lower = scene.ToLowerInvariant();
            if (lower.Contains("menu") || lower.Contains("prison") || lower.Contains("tutorial")
                || lower.Contains("logo") || lower.Contains("benchmark"))
                return false;
            WorldManager world = WorldManager.Instance;
            if (world == null || world.loadingScene)
                return false;
            try
            {
                if (world.summaryActive)
                    return false;
            }
            catch (Exception)
            {
            }
            return LocalPlayer() != null;
        }

        static void RefreshBoard()
        {
            if (!OnNightJobMap() || !_boardOpen)
            {
                if (_board != null)
                    _board.SetActive(false);
                return;
            }
            if (_board == null)
                _boardText = null;
            EnsureBoard();
            if (_board == null || _boardText == null)
                return;
            _board.SetActive(true);
            if (Time.unscaledTime >= _nextBoardPlace)
            {
                _nextBoardPlace = Time.unscaledTime + 0.5f;
                _boardText.text = BoardText();
                PlaceBoardUnderSecurity();
            }
        }

        static string BoardText()
        {
            PruneScores();
            RememberScore(LocalName(), _bounty);
            List<KeyValuePair<string, int>> rows = new List<KeyValuePair<string, int>>(Scores);
            rows.Sort((a, b) =>
            {
                int cmp = b.Value.CompareTo(a.Value);
                return cmp != 0 ? cmp : string.CompareOrdinal(a.Key, b.Key);
            });
            StringBuilder sb = new StringBuilder();
            sb.Append("<color=#FFC440>悬赏令</color>\n");
            for (int i = 0; i < rows.Count; i++)
                sb.Append(rows[i].Key).Append("：$").Append(rows[i].Value).Append('\n');
            sb.Append('\n');
            int total = TeamTotal();
            if (_mission >= MissionNeed.Length)
                sb.Append("<color=#FFC440>总悬赏奖励：已完成</color>");
            else
                sb.Append("<color=#FFC440>总悬赏奖励：$").Append(total).Append("/$").Append(MissionNeed[_mission]).Append("</color>");
            return sb.ToString();
        }

        static void EnsureBoard()
        {
            if (_board != null)
                return;
            _board = new GameObject("BountyBoard");
            UnityEngine.Object.DontDestroyOnLoad(_board);
            Canvas canvas = _board.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40;
            CanvasScaler scaler = _board.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            _board.AddComponent<GraphicRaycaster>().enabled = false;
            GameObject textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(_board.transform, false);
            RectTransform rect = textGo.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-28f, -360f);
            rect.sizeDelta = new Vector2(320f, 280f);
            _boardText = textGo.GetComponent<TextMeshProUGUI>();
            _boardText.fontSize = 22f;
            _boardText.color = Color.white;
            _boardText.alignment = TextAlignmentOptions.TopRight;
            _boardText.richText = true;
            _boardText.raycastTarget = false;
            _boardText.overflowMode = TextOverflowModes.Overflow;
            if (_hudFont != null)
            {
                _boardText.font = _hudFont;
                if (_hudMat != null)
                    _boardText.fontSharedMaterial = _hudMat;
            }
        }

        static float _nextSecurityFind;
        static RectTransform _securityHud;

        static void PlaceBoardUnderSecurity()
        {
            if (_board == null || _boardText == null)
                return;
            RectTransform rect = _boardText.rectTransform;
            if (_securityHud == null || !_securityHud.gameObject.activeInHierarchy)
            {
                if (Time.unscaledTime >= _nextSecurityFind)
                {
                    _nextSecurityFind = Time.unscaledTime + 4f;
                    _securityHud = FindSecurityHud();
                }
            }
            if (_securityHud == null)
            {
                rect.anchoredPosition = new Vector2(-28f, -360f);
                return;
            }
            Vector3[] corners = new Vector3[4];
            _securityHud.GetWorldCorners(corners);
            RectTransform canvas = _board.transform as RectTransform;
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, RectTransformUtility.WorldToScreenPoint(null, corners[0]), null, out local))
            {
                rect.anchoredPosition = new Vector2(-28f, -360f);
                return;
            }
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            float top = canvas.rect.yMax;
            rect.anchoredPosition = new Vector2(-28f, local.y - 14f - top);
        }

        static RectTransform FindSecurityHud()
        {
            TextMeshProUGUI[] labels = UnityEngine.Object.FindObjectsOfType<TextMeshProUGUI>();
            RectTransform best = null;
            float bestScore = float.MinValue;
            for (int i = 0; i < labels.Length; i++)
            {
                TextMeshProUGUI label = labels[i];
                if (label == null || label == _boardText || !label.gameObject.activeInHierarchy)
                    continue;
                string text = label.text;
                if (string.IsNullOrEmpty(text))
                    continue;
                bool security = text.IndexOf("安全", StringComparison.Ordinal) >= 0
                    || text.IndexOf("security", StringComparison.OrdinalIgnoreCase) >= 0;
                bool house = text.IndexOf("Master", StringComparison.OrdinalIgnoreCase) >= 0
                    || (text.IndexOf('-') >= 0 && text.Length < 40 && label.rectTransform.position.x > Screen.width * 0.55f);
                if (!security && !house)
                    continue;
                Vector3 pos = label.rectTransform.position;
                float score = pos.x + pos.y * 0.25f;
                if (security)
                    score += 400f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = label.rectTransform;
                }
            }
            return best;
        }

        static void MarkPoliceHit()
        {
            _policeHitUntil = Time.unscaledTime + 8f;
        }

        static void BeforeNpcAttack(AttackSensor __instance)
        {
            if (__instance != null && IsCop(__instance.npc))
                MarkPoliceHit();
        }

        static void BeforeCopDown(NPC __instance, out bool __state)
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

        static void AddBounty(int amount)
        {
            if (amount <= 0)
                return;
            _bounty += amount;
            _lastSentBounty = int.MinValue;
            if (_cutBounty != null)
                _cutBounty.text = "赏金：$" + _bounty + " |";
        }

        static void AfterCopDown(NPC __instance, bool __state)
        {
            if (__state || __instance == null)
                return;
            int id = __instance.GetInstanceID();
            if (PaidCops.Contains(id))
                return;
            FPP_Player local = LocalPlayer();
            if (local == null)
                return;
            FPP_Player killer = null;
            try
            {
                killer = __instance.nearestPlayer;
            }
            catch (Exception)
            {
            }
            if (killer != null && killer != local)
                return;
            if (killer == null && Vector3.Distance(__instance.transform.position, local.transform.position) > 12f)
                return;
            PaidCops.Add(id);
            if (IsCop(__instance))
            {
                int kind = CopKindOf(__instance);
                int pay = CopKnockBounty(__instance);
                AddBounty(pay);
                if (kind != CopNormal)
                    Tell(CopKindName(kind) + " +$" + pay, 2.8f, "good");
            }
            else if (IsArmedNpc(__instance))
                AddBounty(UnityEngine.Random.Range(3, 6));
            else
                AddBounty(UnityEngine.Random.Range(1, 4));
        }

        static bool IsArmedNpc(NPC npc)
        {
            try
            {
                return npc != null && (npc.hasWeapon || npc.wasArmed);
            }
            catch (Exception)
            {
                return false;
            }
        }

        static int CopKindOf(NPC cop)
        {
            if (cop == null)
                return CopNormal;
            int id = cop.GetInstanceID();
            if (CopInstKind.TryGetValue(id, out int kind))
                return kind;
            int net = CopNetId(cop);
            if (net != 0 && CopNetKind.TryGetValue(net, out kind))
                return kind;
            return CopNormal;
        }

        static int CopKnockBounty(NPC cop)
        {
            int kind = CopKindOf(cop);
            if (kind == CopSpeed)
                return UnityEngine.Random.Range(10, 21);
            if (kind == CopTank)
                return UnityEngine.Random.Range(20, 51);
            if (kind == CopOmni)
                return UnityEngine.Random.Range(50, 101);
            return UnityEngine.Random.Range(5, 11);
        }

        static void AfterPickedItem(PlayerInventory __instance, Pickupable item)
        {
            if (__instance == null || !IsLocal(__instance.player) || item == null || item.assignedItem == null)
                return;
            if (!item.assignedItem.isMoney)
                return;
            if (!IsInteriorCash(item))
                return;
            AddBounty(Mathf.RoundToInt(item.addAmount * 0.1f));
        }

        static bool IsInteriorCash(Pickupable item)
        {
            string prop = null;
            try
            {
                prop = item.customProperty;
            }
            catch (Exception)
            {
            }
            if (!string.IsNullOrEmpty(prop))
            {
                if (prop.StartsWith("cash:", StringComparison.Ordinal) || prop.Contains(":s")
                    || prop.Contains("playerDrop") || prop.Contains("blockScore") || prop.Contains(PaidLootTag))
                    return false;
            }
            try
            {
                if (item.droppedByPlayer)
                    return false;
            }
            catch (Exception)
            {
            }
            return true;
        }

        static bool LootAlreadyPaid(Pickupable pickup, string prop)
        {
            if (!string.IsNullOrEmpty(prop) && (prop.Contains(PaidLootTag) || prop.Contains("blockScore")))
                return true;
            if (pickup == null)
                return true;
            return PaidLoot.Contains(pickup.GetInstanceID());
        }

        static void MarkLootPaid(Pickupable pickup)
        {
            if (pickup == null)
                return;
            PaidLoot.Add(pickup.GetInstanceID());
            try
            {
                string prop = pickup.customProperty ?? "";
                if (!prop.Contains(PaidLootTag))
                    pickup.RPC_SetCustomProperty(prop + PaidLootTag);
            }
            catch (Exception)
            {
            }
        }

        static void AfterDroppedLoot(PlayerInventory __instance, Pickupable item, float dmg, string customProperty)
        {
            if (__instance == null || !IsLocal(__instance.player) || item == null || item.assignedItem == null)
                return;
            if (!item.assignedItem.isLoot)
                return;
            if (LootAlreadyPaid(item, customProperty))
                return;
            if (!NearVan(__instance.player.transform.position))
                return;
            CreditPickup(item, dmg, false);
        }

        static void AfterVanLoot(SaveTruckItems __instance)
        {
            if (__instance == null)
                return;
            CreditSensor(__instance.sensor);
        }

        static void AfterTrunkLoot(CarTrunk __instance)
        {
            if (__instance == null)
                return;
            CreditSensor(__instance.sensor);
        }

        static bool NearVan(Vector3 pos)
        {
            try
            {
                if (PVP_Scoreboard.Instance != null && !PVP_Scoreboard.Instance.checkIfLootDroppedFar(pos))
                    return true;
            }
            catch (Exception)
            {
            }
            try
            {
                WorldManager world = WorldManager.Instance;
                if (world != null && world.truckItemsSaver != null &&
                    Vector3.Distance(pos, world.truckItemsSaver.transform.position) < 15f)
                    return true;
            }
            catch (Exception)
            {
            }
            GameObject truck = GameObject.FindGameObjectWithTag("SpawnLootTruck");
            if (truck != null && Vector3.Distance(pos, truck.transform.position) < 15f)
                return true;
            return false;
        }

        static void CreditSensor(Micosmo.SensorToolkit.TriggerSensor sensor)
        {
            FPP_Player local = LocalPlayer();
            if (local == null || sensor == null)
                return;
            List<GameObject> detections;
            try
            {
                detections = sensor.GetDetections();
            }
            catch (Exception)
            {
                return;
            }
            if (detections == null || detections.Count == 0)
                return;
            if (Vector3.Distance(sensor.transform.position, local.transform.position) > 20f)
                return;
            for (int i = 0; i < detections.Count; i++)
            {
                if (detections[i] == null)
                    continue;
                Pickupable pickup = detections[i].GetComponent<Pickupable>();
                if (pickup == null)
                    continue;
                string prop = null;
                try
                {
                    prop = pickup.customProperty;
                }
                catch (Exception)
                {
                }
                if (!string.IsNullOrEmpty(prop) && prop.Contains("playerDrop"))
                    continue;
                CreditPickup(pickup, pickup.currentDamage, true);
            }
        }

        static void CreditPickup(Pickupable pickup, float dmg, bool skipDropped)
        {
            if (pickup == null || pickup.assignedItem == null || !pickup.assignedItem.isLoot)
                return;
            string prop = null;
            try
            {
                prop = pickup.customProperty;
            }
            catch (Exception)
            {
            }
            if (LootAlreadyPaid(pickup, prop))
                return;
            if (skipDropped && !string.IsNullOrEmpty(prop) && prop.Contains("playerDrop"))
                return;
            int worth = 0;
            try
            {
                WorldManager world = WorldManager.Instance;
                if (world != null)
                    worth = world.ReturnItemValueDamaged(pickup.assignedItem.value, dmg, pickup.assignedItem.hasToBeCarried);
            }
            catch (Exception)
            {
                return;
            }
            if (worth <= 0)
                return;
            MarkLootPaid(pickup);
            int add = Mathf.RoundToInt(worth * 0.05f);
            if (add < 1)
                add = 1;
            AddBounty(add);
        }

        static bool BeforeShowMessage(FPP_Player __instance, string message, float showTime, string messageType)
        {
            if (!IsLocal(__instance) || string.IsNullOrEmpty(message))
                return true;
            if (Time.unscaledTime >= _bountyToastUntil)
                return true;
            if (!IsSkillDeathNotice(message))
                return true;
            _heldSkillMsg = message;
            _heldSkillTime = showTime > 0.1f ? showTime : 4f;
            _heldSkillKind = string.IsNullOrEmpty(messageType) ? "bad" : messageType;
            return false;
        }

        static bool IsSkillDeathNotice(string message)
        {
            return message.IndexOf("升级-1", StringComparison.Ordinal) >= 0
                || message.IndexOf("等级-1", StringComparison.Ordinal) >= 0
                || (message.IndexOf("死亡惩罚", StringComparison.Ordinal) >= 0
                    && message.IndexOf("升级", StringComparison.Ordinal) >= 0);
        }

        static void AfterPlayerDeath(FPP_Player __instance, bool tazed)
        {
            if (!IsLocal(__instance))
                return;
            int had = _bounty;
            if (had <= 0)
                return;
            bool police = tazed || Time.unscaledTime < _policeHitUntil;
            if (police)
            {
                int fine = Mathf.CeilToInt(had * 0.5f);
                _bounty = 0;
                TakeCash(fine);
                BeginDeathToast("警察收走了你的赏金$" + had + " 并且罚款$" + fine, 3.5f);
            }
            else
            {
                _bounty = Mathf.CeilToInt(had * 0.5f);
                BeginDeathToast("死亡惩罚赏金减半", 3.5f);
            }
        }

        static void BeginDeathToast(string message, float seconds)
        {
            _bountyToast = message;
            _bountyToastUntil = Time.unscaledTime + seconds;
            ShowDeathToast(message);
        }

        static void TickDeathToast(FPP_Player player)
        {
            if (!string.IsNullOrEmpty(_bountyToast) && Time.unscaledTime < _bountyToastUntil)
            {
                ShowDeathToast(_bountyToast);
                return;
            }
            if (_toast != null)
                _toast.SetActive(false);
            _bountyToast = null;
            if (string.IsNullOrEmpty(_heldSkillMsg) || player == null || player.isDead)
                return;
            string msg = _heldSkillMsg;
            float time = _heldSkillTime;
            string kind = _heldSkillKind;
            _heldSkillMsg = null;
            player.ShowMessage(msg, time, kind, true);
        }

        static void ShowDeathToast(string message)
        {
            if (_toast == null)
            {
                _toast = new GameObject("BountyDeathToast");
                UnityEngine.Object.DontDestroyOnLoad(_toast);
                Canvas canvas = _toast.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 200;
                CanvasScaler scaler = _toast.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                _toast.AddComponent<GraphicRaycaster>().enabled = false;
                GameObject textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
                textGo.transform.SetParent(_toast.transform, false);
                RectTransform rect = textGo.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 0.78f);
                rect.anchorMax = new Vector2(0.5f, 0.78f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = new Vector2(1400f, 80f);
                _toastText = textGo.GetComponent<TextMeshProUGUI>();
                _toastText.fontSize = 32f;
                _toastText.color = new Color(1f, 0.35f, 0.35f, 1f);
                _toastText.alignment = TextAlignmentOptions.Center;
                _toastText.richText = true;
                _toastText.raycastTarget = false;
                _toastText.overflowMode = TextOverflowModes.Overflow;
            }
            if (_hudFont != null)
            {
                _toastText.font = _hudFont;
                if (_hudMat != null)
                    _toastText.fontSharedMaterial = _hudMat;
            }
            _toast.SetActive(true);
            _toastText.text = message;
        }

        static void Tell(string message, float seconds, string kind)
        {
            FPP_Player player = LocalPlayer();
            if (player != null)
                player.ShowMessage(message, seconds, kind, true);
        }

        static string CashPath()
        {
            return Path.Combine(Application.persistentDataPath, "skill-cash.txt");
        }

        static int ReadCash()
        {
            try
            {
                string path = CashPath();
                if (File.Exists(path) && int.TryParse(File.ReadAllText(path), out int saved))
                    return Mathf.Max(0, saved);
            }
            catch (Exception)
            {
            }
            return 0;
        }

        static void WriteCash(int value)
        {
            try
            {
                File.WriteAllText(CashPath(), Mathf.Max(0, value).ToString());
            }
            catch (Exception ex)
            {
                if (Log != null)
                    Log.LogError("Could not save cash: " + ex.Message);
            }
        }

        static bool CashAdd(int amount)
        {
            try
            {
                Type plugin = AccessTools.TypeByName("SkillCashMod.Plugin");
                Type cash = plugin != null ? AccessTools.Inner(plugin, "Cash") : null;
                MethodInfo add = cash != null ? AccessTools.Method(cash, "Add") : null;
                if (add == null)
                    return false;
                add.Invoke(null, new object[] { amount });
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        static void TakeCash(int amount)
        {
            if (amount <= 0)
                return;
            if (!CashAdd(-amount))
                WriteCash(ReadCash() - amount);
        }

        static void AddCash(int amount)
        {
            if (amount <= 0)
                return;
            if (!CashAdd(amount))
                WriteCash(ReadCash() + amount);
        }
    }
}
