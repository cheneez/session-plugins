using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using BepInEx;
using BepInEx.Logging;
using Enviro;
using Fusion;
using HarmonyLib;
using HighlightPlus;
using Rewired;
using UnityEngine;

namespace HostBuffs
{
    [BepInPlugin("com.kaboo.crimesim.hostbuffs", "Host Buffs", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        const int VkJ = 0x4A;
        const int VkM = 0x4D;
        const int VkL = 0x4C;
        const int VkR = 0x52;
        const int VkP = 0x50;
        const float NpcSeeDistance = 30f;
        const float SyncBase = 1000f;

        static bool _doubleJump;
        static bool _fastMove;
        static bool _seeNpcs;
        static bool _published;

        static readonly object KeyLock = new object();
        static readonly HashSet<int> KeysDown = new HashSet<int>();
        static readonly HashSet<int> KeysPressed = new HashSet<int>();
        static IntPtr _hook;
        static LowLevelKeyboardProc _hookProc;
        static int _hookError;
        static bool _loggedHook;
        static bool _loggedHost;
        static bool _loggedGuest;
        static bool _jumpRepaired;
        static ManualLogSource Log;

        static FieldInfo _timeMultiplierField;

        void Awake()
        {
            _timeMultiplierField = typeof(WorldManager).GetField("_timeMultiplier", BindingFlags.Instance | BindingFlags.NonPublic);

            var harmony = new Harmony("com.kaboo.crimesim.hostbuffs");
            var jumpImpulse = AccessTools.PropertyGetter(typeof(ECM.Controllers.BaseCharacterController), "jumpImpulse");
            var speedMethod = AccessTools.Method(typeof(ECM.Controllers.BaseFirstPersonController), "GetTargetSpeed");
            var fallMethod = AccessTools.Method(typeof(FPP_Player), "CheckMovementDirection");
            if (jumpImpulse == null || speedMethod == null || fallMethod == null)
            {
                Logger.LogError("Could not find jump, movement, or fall methods. The game may have updated.");
                return;
            }

            harmony.Patch(jumpImpulse, postfix: new HarmonyMethod(typeof(JumpPatch), nameof(JumpPatch.Postfix)));
            harmony.Patch(speedMethod, postfix: new HarmonyMethod(typeof(SpeedPatch), nameof(SpeedPatch.Postfix)));
            harmony.Patch(fallMethod, prefix: new HarmonyMethod(typeof(FallPatch), nameof(FallPatch.Prefix)), postfix: new HarmonyMethod(typeof(FallPatch), nameof(FallPatch.Postfix)));
            harmony.Patch(AccessTools.Method(typeof(FPP_Player), "Update"), postfix: new HarmonyMethod(typeof(Plugin), nameof(Tick)));
            Log = Logger;
            InstallKeyHook();
            harmony.Patch(AccessTools.Method(typeof(NPC), "Update"), postfix: new HarmonyMethod(typeof(NpcVisionPatch), nameof(NpcVisionPatch.Postfix)));
            Logger.LogInfo("Host Buffs ready. Host keys: J jump, M money, L clock, R speed, P NPC vision.");
        }

        void OnDestroy()
        {
            if (_hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
        }

        public static void Tick(FPP_Player __instance)
        {
            WorldManager world = WorldManager.Instance;
            if (world == null || __instance == null || world.localPlayer != __instance)
                return;

            try
            {
                TickInner();
            }
            catch (Exception ex)
            {
                if (Log != null)
                    Log.LogError("Host Buffs tick failed: " + ex);
            }
        }

        static void TickInner()
        {
            if (!_loggedHook && _hookError != 0)
            {
                _loggedHook = true;
                Log.LogWarning("Keyboard hook failed, Windows error " + _hookError + ". Using the game input instead.");
            }

            PollKeys();

            WorldManager world = WorldManager.Instance;
            if (!IsHost(world))
            {
                if (world != null && !_loggedGuest)
                {
                    _loggedGuest = true;
                    Log.LogInfo("Game is running, but this PC is not the host yet.");
                }

                if (world != null)
                    ReadSharedState(world);
                return;
            }

            if (!_loggedHost)
            {
                _loggedHost = true;
                Log.LogInfo("Host controls are live. Press J, M, L, R, or P.");
            }

            if (!_jumpRepaired && world.localPlayer != null)
            {
                world.localPlayer.CalculateBonusesToPlayer();
                _jumpRepaired = true;
                Log.LogInfo("Jump height reset so the 2x boost cannot stack.");
            }

            if (Consume(VkJ))
                ToggleJump(world);
            if (Consume(VkM))
                AddMoney(world);
            if (Consume(VkL))
                RewindOneHour(world);
            if (Consume(VkR))
                ToggleSpeed(world);
            if (Consume(VkP))
                ToggleNpcVision(world);

            if (world.localPlayer != null)
                NpcVisionPatch.Maintain(world.localPlayer.transform.position);
            else if (_seeNpcs)
                NpcVisionPatch.Clear();

            if (_published)
                Publish(world, _doubleJump, _fastMove);
        }

        void OnGUI()
        {
            WorldManager world = WorldManager.Instance;
            GUI.depth = -1000;
            GUI.color = Color.yellow;
            string text;
            if (!IsHost(world))
                text = "HOST BUFFS LOADED\nHost a game, then J / M / L / R / P";
            else
                text = "HOST BUFFS\nJ jump x2: " + (_doubleJump ? "ON" : "OFF") +
                       "   R speed x1.5: " + (_fastMove ? "ON" : "OFF") +
                       "   P NPCs: " + (_seeNpcs ? "ON" : "OFF") +
                       "\nM +$100    L clock -1 hour";

            GUI.Label(new Rect(12f, 12f, 640f, 80f), text);
            GUI.color = Color.white;
        }

        static void ToggleJump(WorldManager world)
        {
            _doubleJump = !_doubleJump;
            _published = true;
            Publish(world, _doubleJump, _fastMove);
            if (world.localPlayer != null)
                world.localPlayer.CalculateBonusesToPlayer();
            Log.LogInfo(_doubleJump ? "Jump x2 on." : "Jump back to normal.");
            Tell(world, _doubleJump ? "Everyone jumps 2x higher" : "Jump height back to normal");
        }

        static void ToggleSpeed(WorldManager world)
        {
            _fastMove = !_fastMove;
            _published = true;
            Publish(world, _doubleJump, _fastMove);
            Log.LogInfo(_fastMove ? "Speed x1.5 on." : "Speed back to normal.");
            Tell(world, _fastMove ? "Everyone moves 1.5x faster" : "Move speed back to normal");
        }

        static void ToggleNpcVision(WorldManager world)
        {
            _seeNpcs = !_seeNpcs;
            if (!_seeNpcs)
                NpcVisionPatch.Clear();
            Log.LogInfo(_seeNpcs ? "NPC wall vision on, 30m." : "NPC wall vision off.");
            Tell(world, _seeNpcs ? "NPCs within 30m show through walls" : "NPC wall vision off");
        }

        static void AddMoney(WorldManager world)
        {
            world.RPC_AddMoney(100);
            Log.LogInfo("Added $100.");
            Tell(world, "+$100");
        }

        static void RewindOneHour(WorldManager world)
        {
            Log.LogInfo("Clock key pressed.");
            if (world.enviro == null || world.enviro.Time == null)
                return;

            EnviroTimeModule time = world.enviro.Time;
            float hour = time.GetTimeOfDay() - 1f;
            while (hour < 0f)
                hour += 24f;
            while (hour >= 24f)
                hour -= 24f;
            time.SetTimeOfDay(hour);
            Log.LogInfo("Clock moved back 1 hour.");
            Tell(world, "Clock moved back 1 hour");
        }

        static void Publish(WorldManager world, bool jump, bool speed)
        {
            if (!world.HasStateAuthority)
                return;

            float encoded = SyncBase + (jump ? 1f : 0f) + (speed ? 2f : 0f);
            try
            {
                if (Mathf.Abs(world.timeMultiplier - encoded) < 0.01f)
                    return;
            }
            catch (Exception)
            {
                return;
            }

            if (_timeMultiplierField != null)
                _timeMultiplierField.SetValue(world, encoded);
            world.timeMultiplier = encoded;
        }

        static void ReadSharedState(WorldManager world)
        {
            float encoded;
            try
            {
                encoded = world.timeMultiplier;
            }
            catch (Exception)
            {
                return;
            }

            if (encoded < SyncBase || encoded >= SyncBase + 16f)
                return;

            int flags = Mathf.RoundToInt(encoded - SyncBase);
            _doubleJump = (flags & 1) != 0;
            _fastMove = (flags & 2) != 0;
        }

        static bool IsHost(WorldManager world)
        {
            if (world == null)
                return false;
            if (world.isMasterClient)
                return true;

            try
            {
                NetworkRunner runner = world.Runner;
                if (runner != null && (runner.IsSharedModeMasterClient || runner.IsSinglePlayer))
                    return true;
                if (world.HasStateAuthority)
                    return true;
            }
            catch (Exception)
            {
                return false;
            }

            return false;
        }

        static void Tell(WorldManager world, string message)
        {
            if (world.localPlayer != null)
                world.localPlayer.ShowMessage(message, 3f, "good", true);
        }

        static void InstallKeyHook()
        {
            _hookProc = HookCallback;
            var thread = new Thread(HookLoop);
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        static void HookLoop()
        {
            _hook = SetWindowsHookEx(13, _hookProc, GetModuleHandle("user32.dll"), 0);
            if (_hook == IntPtr.Zero)
            {
                _hookError = Marshal.GetLastWin32Error();
                return;
            }

            MSG msg;
            while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }

        static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                int vk = Marshal.ReadInt32(lParam);
                int flags = Marshal.ReadInt32(lParam, 8);
                bool wentUp = (flags & 0x80) != 0 || msg == 0x0101 || msg == 0x0105;
                bool wentDown = !wentUp && (msg == 0x0100 || msg == 0x0104);
                if (vk == VkJ || vk == VkM || vk == VkL || vk == VkR || vk == VkP)
                {
                    lock (KeyLock)
                    {
                        if (wentDown && KeysDown.Add(vk))
                            KeysPressed.Add(vk);
                        if (wentUp)
                            KeysDown.Remove(vk);
                    }
                }
            }

            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        static int _polledFrame = -1;

        static void PollKeys()
        {
            if (Time.frameCount == _polledFrame)
                return;
            _polledFrame = Time.frameCount;

            Note(KeyCode.J, VkJ);
            Note(KeyCode.M, VkM);
            Note(KeyCode.L, VkL);
            Note(KeyCode.R, VkR);
            Note(KeyCode.P, VkP);
        }

        static void Note(KeyCode key, int vk)
        {
            bool down = false;
            try
            {
                down = Input.GetKeyDown(key);
            }
            catch (Exception)
            {
            }

            try
            {
                if (!down && ReInput.isReady && ReInput.controllers.Keyboard != null)
                    down = ReInput.controllers.Keyboard.GetKeyDown(key);
            }
            catch (Exception)
            {
            }

            if (!down)
                return;

            lock (KeyLock)
                KeysPressed.Add(vk);
        }

        static int _usedFrame = -1;
        static readonly HashSet<int> UsedThisFrame = new HashSet<int>();

        static bool Consume(int vk)
        {
            lock (KeyLock)
            {
                if (Time.frameCount != _usedFrame)
                {
                    _usedFrame = Time.frameCount;
                    UsedThisFrame.Clear();
                }

                if (!KeysPressed.Remove(vk))
                    return false;
                return UsedThisFrame.Add(vk);
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        static extern int GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);

        [DllImport("user32.dll")]
        static extern bool TranslateMessage(ref MSG msg);

        [DllImport("user32.dll")]
        static extern IntPtr DispatchMessage(ref MSG msg);

        [StructLayout(LayoutKind.Sequential)]
        struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int x;
            public int y;
        }

        delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        public static class JumpPatch
        {
            public static void Postfix(ECM.Controllers.BaseCharacterController __instance, ref float __result)
            {
                if (_doubleJump && __instance is ECM.Controllers.BaseFirstPersonController)
                    __result *= Mathf.Sqrt(2f);
            }
        }

        public static class FallPatch
        {
            static FieldInfo _minVelocity;

            public static void Prefix(FPP_Player __instance, ref float __state)
            {
                if (_minVelocity == null)
                    _minVelocity = AccessTools.Field(typeof(FPP_Player), "heavyFallMinVelocity");

                __state = (float)_minVelocity.GetValue(__instance);
                _minVelocity.SetValue(__instance, __state * Mathf.Sqrt(2f));
            }

            public static void Postfix(FPP_Player __instance, float __state)
            {
                if (_minVelocity != null)
                    _minVelocity.SetValue(__instance, __state);
            }
        }

        public static class SpeedPatch
        {
            public static void Postfix(ref float __result)
            {
                if (_fastMove)
                    __result *= 1.5f;
            }
        }

        public static class NpcVisionPatch
        {
            static readonly HashSet<NPC> Revealed = new HashSet<NPC>();
            static readonly Dictionary<NPC, SkinnedMeshRenderer[]> Renderers = new Dictionary<NPC, SkinnedMeshRenderer[]>();
            static FieldInfo _highlightField;

            public static void Postfix(NPC __instance)
            {
                if (__instance == null)
                    return;

                if (!_seeNpcs)
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

                float distance = Vector3.Distance(__instance.transform.position, player.transform.position);
                if (distance <= NpcSeeDistance)
                    Reveal(__instance);
            }

            public static void Maintain(Vector3 playerPosition)
            {
                if (!_seeNpcs)
                {
                    if (Revealed.Count > 0)
                        Clear();
                    return;
                }

                if (Revealed.Count == 0 || (Time.frameCount & 7) != 0)
                    return;

                NPC[] copy = new NPC[Revealed.Count];
                Revealed.CopyTo(copy);
                float limit = NpcSeeDistance * NpcSeeDistance;
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

            static HighlightEffect HighlightOf(NPC npc)
            {
                if (_highlightField == null)
                    _highlightField = AccessTools.Field(typeof(NPC), "highlight");
                if (_highlightField == null)
                    return null;
                return _highlightField.GetValue(npc) as HighlightEffect;
            }
        }
    }
}
