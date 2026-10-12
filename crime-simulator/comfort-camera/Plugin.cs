using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Rewired;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace AntiSickness
{
    [BepInPlugin("com.kaboo.crimesim.antisickness", "防晕3D", "1.0.0")]
    [BepInDependency("com.kaboo.crimesim.skillcash")]
    public class Plugin : BaseUnityPlugin
    {
        static ManualLogSource Log;
        static ConfigFile Cfg;

        static ConfigEntry<int> Fov;
        static ConfigEntry<bool> NoHeadBob;
        static ConfigEntry<bool> NoShake;
        static ConfigEntry<bool> NoFovKick;
        static ConfigEntry<float> HandSwayScale;
        static ConfigEntry<bool> NoMotionBlur;
        static ConfigEntry<bool> NoLensEffects;
        static ConfigEntry<bool> StartThirdPerson;
        static ConfigEntry<float> TpDistance;
        static ConfigEntry<float> BodyAlpha;

        const float MinDistance = 1f;
        const float MaxDistance = 6f;
        const float ZoomSpeed = 4f;

        static readonly AccessTools.FieldRef<FPP_Player, MeshRenderer[]> AllRenderers =
            AccessTools.FieldRefAccess<FPP_Player, MeshRenderer[]>("allRenderers");

        static readonly AccessTools.FieldRef<FPP_Player, SkinnedMeshRenderer[]> CharacterMeshes =
            AccessTools.FieldRefAccess<FPP_Player, SkinnedMeshRenderer[]>("characterMeshes");

        void Awake()
        {
            Log = Logger;
            Cfg = Config;
            Fov = Config.Bind("防晕", "视野", 0, "视野大小 FOV，0 = 用游戏设置。越大越不容易晕，推荐 90-110，范围 40-130");
            NoHeadBob = Config.Bind("防晕", "关闭走路镜头晃动", true, "走路和奔跑时镜头不上下晃");
            NoShake = Config.Bind("防晕", "关闭屏幕震动", true, "受伤、爆炸等不再震屏");
            NoFovKick = Config.Bind("防晕", "关闭视野缩放", true, "奔跑、跳跃、闪避时视野不忽大忽小（瞄准缩放保留）");
            HandSwayScale = Config.Bind("防晕", "手部摇摆", 0.3f, "转视角时手和物品的摇摆幅度，0 = 不摇，1 = 游戏原样");
            NoMotionBlur = Config.Bind("防晕", "关闭动态模糊", true, "关闭画面的运动模糊");
            NoLensEffects = Config.Bind("防晕", "关闭色差和镜头畸变", true, "关闭画面边缘的彩边和变形");
            StartThirdPerson = Config.Bind("第三人称", "默认第三人称", false, "进游戏时直接用第三人称，按 O 切换");
            TpDistance = Config.Bind("第三人称", "距离", 3f, "镜头在人物身后多远（米），游戏里按住 - 拉远、= 拉近，范围 1-6");
            BodyAlpha = Config.Bind("第三人称", "自己身体透明度", 0.4f, "镜头贴近身后（2米以内）时自己身体的透明度，只有自己看得到，拉远后恢复不透明。0 = 看不见，1 = 不透明");
            _third = StartThirdPerson.Value;
            _configStamp = ConfigStamp();

            var harmony = new Harmony("com.kaboo.crimesim.antisickness");
            harmony.Patch(AccessTools.Method(typeof(FPP_Player), "Update"), postfix: new HarmonyMethod(typeof(Plugin), nameof(Tick)));
            harmony.Patch(AccessTools.Method(typeof(FPP_Player), "CheckFoV"), postfix: new HarmonyMethod(typeof(Plugin), nameof(FovPostfix)));
            harmony.Patch(AccessTools.Method(typeof(FPP_Player), "CharacterAnimations"), postfix: new HarmonyMethod(typeof(Plugin), nameof(HeadBobPostfix)));
            harmony.Patch(AccessTools.Method(typeof(CameraShake), "Shake"), prefix: new HarmonyMethod(typeof(Plugin), nameof(ShakePrefix)));
            harmony.Patch(AccessTools.Method(typeof(CameraShake), "Rotate"), prefix: new HarmonyMethod(typeof(Plugin), nameof(ShakePrefix)));
            harmony.Patch(AccessTools.Method(typeof(ShowControls), "Update"), postfix: new HarmonyMethod(typeof(Plugin), nameof(ControlsPostfix)));
            var rig = new GameObject("防晕3D camera rig");
            UnityEngine.Object.DontDestroyOnLoad(rig);
            rig.hideFlags = HideFlags.HideAndDontSave;
            rig.AddComponent<EarlyRig>();
            rig.AddComponent<LateRig>();
            Logger.LogInfo("防晕3D ready. O switches first/third person. Settings: " + Config.ConfigFilePath);
        }

        static bool IsLocal(FPP_Player player)
        {
            WorldManager world = WorldManager.Instance;
            return player != null && world != null && world.localPlayer == player;
        }

        public static void Tick(FPP_Player __instance)
        {
            if (!IsLocal(__instance))
                return;
            try
            {
                ReloadConfigIfChanged();
                if (!__instance.isDead && !__instance.chatActive && !__instance.pauseActive)
                {
                    if (KeyDown(KeyCode.O))
                    {
                        _third = !_third;
                        __instance.ShowMessage(_third ? "第三人称" : "第一人称", 1.5f, "good", true);
                    }
                    Zoom(__instance);
                }
                UpdateView(__instance);
                ApplyBodyAlpha(__instance);
                ApplyHandSway(__instance);
                ApplyPostEffects();
            }
            catch (Exception ex)
            {
                Log.LogError("防晕3D tick failed: " + ex);
            }
        }

        static float _distance = -1f;
        static bool _zooming;

        static float Distance
        {
            get
            {
                if (_distance < 0f)
                    _distance = Mathf.Clamp(TpDistance.Value, MinDistance, MaxDistance);
                return _distance;
            }
        }

        static void Zoom(FPP_Player player)
        {
            bool zoomOut = KeyHeld(KeyCode.Minus) || KeyHeld(KeyCode.KeypadMinus);
            bool zoomIn = KeyHeld(KeyCode.Equals) || KeyHeld(KeyCode.KeypadPlus);
            if (zoomOut == zoomIn)
            {
                if (_zooming)
                {
                    _zooming = false;
                    if (_third)
                    {
                        TpDistance.Value = Distance;
                        _configStamp = ConfigStamp();
                    }
                }
                return;
            }
            _zooming = true;
            float d = Distance;
            if (zoomOut)
            {
                if (!_third)
                {
                    _third = true;
                    d = MinDistance;
                }
                else
                {
                    d = Mathf.Min(MaxDistance, d + ZoomSpeed * Time.unscaledDeltaTime);
                }
            }
            else if (_third)
            {
                d -= ZoomSpeed * Time.unscaledDeltaTime;
                if (d <= MinDistance)
                {
                    d = MinDistance;
                    _third = false;
                }
            }
            _distance = d;
        }

        static long _configStamp;
        static float _nextConfigCheck;

        static long ConfigStamp()
        {
            try
            {
                return File.Exists(Cfg.ConfigFilePath) ? File.GetLastWriteTimeUtc(Cfg.ConfigFilePath).Ticks : 0L;
            }
            catch (Exception)
            {
                return 0L;
            }
        }

        static void ReloadConfigIfChanged()
        {
            if (Time.unscaledTime < _nextConfigCheck)
                return;
            _nextConfigCheck = Time.unscaledTime + 2f;
            long stamp = ConfigStamp();
            if (stamp == _configStamp)
                return;
            _configStamp = stamp;
            Cfg.Reload();
            _postEffectsAt = 0f;
            _distance = -1f;
            Log.LogInfo("Settings reloaded.");
        }

        static bool _third;
        static bool _viewApplied;
        static FPP_Player _viewPlayer;
        static float _nextViewRefresh;

        static void UpdateView(FPP_Player player)
        {
            bool want = _third && !player.isDead && !player.minigameActive;
            if (_viewPlayer != player)
            {
                _viewPlayer = player;
                _viewApplied = false;
                if (want)
                    SetView(player, true);
                return;
            }
            if (want != _viewApplied)
            {
                SetView(player, want);
                return;
            }
            if (want && Time.unscaledTime >= _nextViewRefresh)
            {
                _nextViewRefresh = Time.unscaledTime + 0.25f;
                SkinnedMeshRenderer[] meshes = CharacterMeshes(player);
                if (meshes != null)
                {
                    foreach (SkinnedMeshRenderer mesh in meshes)
                    {
                        if (mesh != null && !mesh.enabled)
                        {
                            player.ShowOrHideCharacter(true);
                            break;
                        }
                    }
                }
                HideOwnOutline(player, true);
            }
        }

        static void SetView(FPP_Player player, bool third)
        {
            _viewApplied = third;
            if (!third)
            {
                RestoreCameras();
                RestoreCullingMasks();
                RestoreHandRenderers();
            }
            HideOwnOutline(player, third);
            if (player.isDead && !third)
                return;
            player.ShowOrHideCharacter(third);
            if (third)
                _logView = true;
        }

        static readonly Dictionary<Behaviour, bool> OutlineState = new Dictionary<Behaviour, bool>();

        static void HideOwnOutline(FPP_Player player, bool hide)
        {
            if (!hide)
            {
                foreach (KeyValuePair<Behaviour, bool> pair in OutlineState)
                {
                    if (pair.Key != null)
                        pair.Key.enabled = pair.Value;
                }
                OutlineState.Clear();
                return;
            }
            foreach (Behaviour behaviour in player.GetComponentsInChildren<Behaviour>(true))
            {
                if (behaviour == null || behaviour.GetType().Name != "HighlightEffect")
                    continue;
                if (!OutlineState.ContainsKey(behaviour))
                    OutlineState[behaviour] = behaviour.enabled;
                behaviour.enabled = false;
            }
        }

        [DefaultExecutionOrder(-32000)]
        class EarlyRig : MonoBehaviour
        {
            void FixedUpdate()
            {
                RestoreCameras();
            }

            void Update()
            {
                RestoreCameras();
            }
        }

        [DefaultExecutionOrder(32000)]
        class LateRig : MonoBehaviour
        {
            void LateUpdate()
            {
                try
                {
                    MoveCameras();
                }
                catch (Exception ex)
                {
                    Log.LogError("Third person camera failed: " + ex);
                    RestoreCameras();
                }
            }
        }

        static bool _logView;
        static readonly Dictionary<Transform, Vector3> MovedCameras = new Dictionary<Transform, Vector3>();
        static readonly Dictionary<Camera, int> CullingMasks = new Dictionary<Camera, int>();
        static readonly List<Renderer> HiddenHands = new List<Renderer>();

        static void RestoreCameras()
        {
            foreach (KeyValuePair<Transform, Vector3> pair in MovedCameras)
            {
                if (pair.Key != null)
                    pair.Key.localPosition = pair.Value;
            }
            MovedCameras.Clear();
        }

        static void RestoreCullingMasks()
        {
            foreach (KeyValuePair<Camera, int> pair in CullingMasks)
            {
                if (pair.Key != null)
                    pair.Key.cullingMask = pair.Value;
            }
            CullingMasks.Clear();
        }

        static void RestoreHandRenderers()
        {
            foreach (Renderer renderer in HiddenHands)
            {
                if (renderer != null)
                    renderer.enabled = true;
            }
            HiddenHands.Clear();
        }

        static List<Camera> RigCameras(FPP_Player player)
        {
            var cameras = new List<Camera>();
            foreach (Camera cam in Camera.allCameras)
            {
                if (cam == null || !cam.enabled)
                    continue;
                if (cam == player.myCamera || cam == player.exclamationCamera || cam.transform.IsChildOf(player.transform)
                    || (player.cameraObject != null && cam.transform.IsChildOf(player.cameraObject.transform)))
                    cameras.Add(cam);
            }
            return cameras;
        }

        static void MoveCameras()
        {
            FPP_Player player = _viewPlayer;
            if (!_viewApplied || player == null || player.myCamera == null || player.isDead)
                return;
            RestoreCameras();
            List<Camera> cameras = RigCameras(player);
            HideHands(player, cameras);
            Vector3 offset = CameraOffset(player, out string blocker);
            _lastPull = offset.magnitude;
            foreach (Camera cam in cameras)
            {
                bool parentMoved = false;
                foreach (Camera other in cameras)
                {
                    if (other != cam && cam.transform.IsChildOf(other.transform))
                        parentMoved = true;
                }
                if (parentMoved || MovedCameras.ContainsKey(cam.transform))
                    continue;
                MovedCameras[cam.transform] = cam.transform.localPosition;
                cam.transform.position += offset;
            }
            if (_logView)
            {
                _logView = false;
                var names = new List<string>();
                foreach (Camera cam in cameras)
                    names.Add(cam.name + (MovedCameras.ContainsKey(cam.transform) ? " (moved)" : ""));
                Log.LogInfo("Third person: camera pulled back " + offset.magnitude.ToString("0.0") + "m"
                    + (blocker != null ? ", stopped by " + blocker : "") + ". Cameras: " + string.Join(", ", names));
            }
        }

        static void HideHands(FPP_Player player, List<Camera> cameras)
        {
            HandSway sway = player.weaponManager != null ? player.weaponManager.handSway : null;
            if (sway == null)
                return;
            int body = 0;
            SkinnedMeshRenderer[] meshes = CharacterMeshes(player);
            if (meshes != null)
            {
                foreach (SkinnedMeshRenderer mesh in meshes)
                {
                    if (mesh != null)
                        body |= 1 << mesh.gameObject.layer;
                }
            }
            int hands = 0;
            Renderer[] handRenderers = sway.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in handRenderers)
                hands |= 1 << renderer.gameObject.layer;
            hands &= ~body;
            if (hands != 0 && (hands & 1) == 0)
            {
                foreach (Camera cam in cameras)
                {
                    if (!CullingMasks.ContainsKey(cam))
                        CullingMasks[cam] = cam.cullingMask;
                    cam.cullingMask = CullingMasks[cam] & ~hands;
                }
                return;
            }
            foreach (Renderer renderer in handRenderers)
            {
                if (renderer != null && renderer.enabled)
                {
                    renderer.enabled = false;
                    HiddenHands.Add(renderer);
                }
            }
        }

        static Vector3 CameraOffset(FPP_Player player, out string blocker)
        {
            blocker = null;
            Transform eye = player.myCamera.transform;
            Vector3 dir = -eye.forward * Distance;
            float length = dir.magnitude;
            if (length < 0.01f)
                return Vector3.zero;
            Vector3 unit = dir / length;
            float reach = length;
            Transform playerRoot = player.transform.root;
            foreach (RaycastHit hit in Physics.SphereCastAll(eye.position, 0.2f, unit, length, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider == null || hit.distance <= 0f)
                    continue;
                Transform t = hit.collider.transform;
                if (t.IsChildOf(player.transform) || t.root == playerRoot
                    || (player.cameraObject != null && t.IsChildOf(player.cameraObject.transform))
                    || t.root == eye.root)
                    continue;
                if (hit.collider.attachedRigidbody != null && hit.collider.attachedRigidbody.transform.root == playerRoot)
                    continue;
                if (hit.distance < reach)
                {
                    reach = hit.distance;
                    blocker = hit.collider.name;
                }
            }
            return unit * Mathf.Max(0f, reach - 0.05f);
        }

        static float _fov;

        public static void FovPostfix(FPP_Player __instance)
        {
            if (!IsLocal(__instance) || __instance.myCamera == null)
                return;
            Camera cam = __instance.myCamera;
            bool aiming = __instance.weaponManager != null && __instance.weaponManager.isAiming;
            if ((Fov.Value <= 0 && !NoFovKick.Value) || (aiming && Fov.Value <= 0))
            {
                _fov = cam.fieldOfView;
                return;
            }
            float baseFov = Fov.Value > 0 ? Mathf.Clamp(Fov.Value, 40, 130) : __instance.WM.gameSettings.gameSettingsData.fieldOfView;
            float target = baseFov * (NoFovKick.Value && !aiming ? 1f : __instance.foV_Multiplier);
            if (_fov <= 0f)
                _fov = cam.fieldOfView;
            _fov = Mathf.Lerp(_fov, target, Time.deltaTime * Mathf.Max(1f, __instance.foV_lerpSpeed));
            cam.fieldOfView = _fov;
            if (__instance.exclamationCamera != null)
                __instance.exclamationCamera.fieldOfView = _fov;
        }

        public static void HeadBobPostfix(FPP_Player __instance)
        {
            if (!NoHeadBob.Value || !IsLocal(__instance) || __instance.cameraAnimator == null)
                return;
            __instance.cameraAnimator.SetBool("Walking", false);
            __instance.cameraAnimator.SetBool("Running", false);
        }

        public static bool ShakePrefix()
        {
            return !NoShake.Value;
        }

        static HandSway _sway;
        static float _swayBase;

        static void ApplyHandSway(FPP_Player player)
        {
            HandSway sway = player.weaponManager != null ? player.weaponManager.handSway : null;
            if (sway == null)
                return;
            if (sway != _sway)
            {
                _sway = sway;
                _swayBase = sway.multiplier;
            }
            sway.multiplier = _swayBase * Mathf.Clamp01(HandSwayScale.Value);
        }

        static float _postEffectsAt;
        static readonly Dictionary<PostProcessEffectSettings, bool> PostOriginal = new Dictionary<PostProcessEffectSettings, bool>();

        static void ApplyPostEffects()
        {
            if (Time.unscaledTime < _postEffectsAt)
                return;
            _postEffectsAt = Time.unscaledTime + 2f;
            foreach (PostProcessVolume volume in UnityEngine.Object.FindObjectsOfType<PostProcessVolume>())
            {
                PostProcessProfile profile = volume.HasInstantiatedProfile() ? volume.profile : volume.sharedProfile;
                if (profile == null || profile.settings == null)
                    continue;
                foreach (PostProcessEffectSettings setting in profile.settings)
                {
                    bool off;
                    if (setting is MotionBlur)
                        off = NoMotionBlur.Value;
                    else if (setting is ChromaticAberration || setting is LensDistortion)
                        off = NoLensEffects.Value;
                    else
                        continue;
                    if (!PostOriginal.ContainsKey(setting))
                        PostOriginal[setting] = setting.enabled.value;
                    setting.enabled.value = off ? false : PostOriginal[setting];
                }
            }
        }

        static readonly HashSet<int> Decorated = new HashSet<int>();

        public static void ControlsPostfix(ShowControls __instance)
        {
            if (__instance == null || __instance.shownControlsObjs == null || __instance.shownControlsObjs.Count == 0)
                return;
            try
            {
                if (Decorated.Add(__instance.GetInstanceID()))
                    AddRows(__instance);
                if (Time.unscaledTime >= _repairRowAt)
                {
                    _repairRowAt = Time.unscaledTime + 1f;
                    EnsureRepairRow(__instance);
                }
            }
            catch (Exception ex)
            {
                Log.LogError("Could not add keys to the controls list: " + ex);
            }
        }

        static List<KeyValuePair<string, string>> Rows()
        {
            var rows = new List<KeyValuePair<string, string>>();
            bool cash = Chainloader.PluginInfos.ContainsKey("com.kaboo.crimesim.skillcash");
            if (cash)
                rows.Add(new KeyValuePair<string, string>("CTRL", "丢现金"));
            rows.Add(new KeyValuePair<string, string>("O", "换视角"));
            rows.Add(new KeyValuePair<string, string>("-/=", "视角拉远/拉近"));
            if (SkillCashSkillUnlocked())
                rows.Add(new KeyValuePair<string, string>("Z", "玩家技能"));
#if !FRIEND
            if (cash)
            {
                rows.Add(new KeyValuePair<string, string>("N", "花500现金复活"));
            }
            if (Chainloader.PluginInfos.ContainsKey("com.kaboo.crimesim.hostbuffs"))
            {
                rows.Add(new KeyValuePair<string, string>("J", "跳跃x2"));
                rows.Add(new KeyValuePair<string, string>("R", "速度x1.5"));
                rows.Add(new KeyValuePair<string, string>("P", "NPC透视"));
                rows.Add(new KeyValuePair<string, string>("M", "加$100"));
                rows.Add(new KeyValuePair<string, string>("L", "时间倒退1小时"));
            }
            if (cash)
            {
                rows.Add(new KeyValuePair<string, string>("I", "刷小额钞票"));
                rows.Add(new KeyValuePair<string, string>("K", "刷大额钞票"));
                rows.Add(new KeyValuePair<string, string>("中键", "刷10000钞票"));
                rows.Add(new KeyValuePair<string, string>("Y", "出售家里地上物品"));
            }
            if (Chainloader.PluginInfos.ContainsKey("com.kaboo.crimesim.duffelbag"))
            {
                rows.Add(new KeyValuePair<string, string>("0/滚轮", "选择背包"));
                rows.Add(new KeyValuePair<string, string>("G", "丢出背包物品"));
            }
#endif
            return rows;
        }

        static void AddRows(ShowControls controls)
        {
            GameObject template = null;
            int lastIndex = -1;
            foreach (GameObject row in controls.shownControlsObjs)
            {
                if (row == null || row.GetComponentInChildren<TMP_Text>(true) == null)
                    continue;
                if (template == null || (row.transform.parent == template.transform.parent && row.transform.GetSiblingIndex() > lastIndex))
                {
                    template = row;
                    lastIndex = row.transform.GetSiblingIndex();
                }
            }
            if (template == null)
            {
                Log.LogWarning("Controls list has no text rows, keys not added.");
                return;
            }

            List<KeyValuePair<string, string>> rows = Rows();
            TMP_Text[] templateTexts = template.GetComponentsInChildren<TMP_Text>(true);
            if (templateTexts.Length == 1 && templateTexts[0].text.Contains("\n"))
            {
                foreach (KeyValuePair<string, string> row in rows)
                    templateTexts[0].text += "\n" + row.Key + " - " + row.Value;
                RefreshLayout(controls);
                Log.LogInfo("Added " + rows.Count + " keys to the controls text.");
                return;
            }

            Transform parent = template.transform.parent;
            var holder = new GameObject("防晕3D rows");
            holder.SetActive(false);
            int index = lastIndex;
            foreach (KeyValuePair<string, string> row in rows)
            {
                GameObject clone = UnityEngine.Object.Instantiate(template, holder.transform, false);
                clone.name = "Mod key " + row.Key;
                StripScripts(clone);
                SetRowText(clone, row.Key, row.Value);
                clone.transform.SetParent(parent, false);
                clone.transform.SetSiblingIndex(++index);
                clone.SetActive(template.activeSelf);
                controls.shownControlsObjs.Add(clone);
            }
            UnityEngine.Object.Destroy(holder);
            RefreshLayout(controls);
            Log.LogInfo("Added " + rows.Count + " keys to the controls list.");
        }

        static readonly HashSet<int> RepairKeyAdded = new HashSet<int>();
        static float _repairRowAt;
        static PropertyInfo _repairProp;

        static bool SkillCashSkillUnlocked()
        {
            try
            {
                if (_repairProp == null)
                {
                    Type type = AccessTools.TypeByName("SkillCashMod.Plugin");
                    _repairProp = type != null ? AccessTools.Property(type, "HasPlayerSkill") : null;
                }
                if (_repairProp != null)
                    return (bool)_repairProp.GetValue(null, null);
            }
            catch (Exception)
            {
            }
            return false;
        }

        static void EnsureRepairRow(ShowControls controls)
        {
            if (!SkillCashSkillUnlocked())
                return;
            int id = controls.GetInstanceID();
            if (RepairKeyAdded.Contains(id))
                return;
            foreach (GameObject row in controls.shownControlsObjs)
            {
                if (row != null && row.name == "Mod key Z")
                {
                    RepairKeyAdded.Add(id);
                    return;
                }
            }
            GameObject template = null;
            int lastIndex = -1;
            foreach (GameObject row in controls.shownControlsObjs)
            {
                if (row == null || row.GetComponentInChildren<TMP_Text>(true) == null)
                    continue;
                if (template == null || (row.transform.parent == template.transform.parent && row.transform.GetSiblingIndex() > lastIndex))
                {
                    template = row;
                    lastIndex = row.transform.GetSiblingIndex();
                }
            }
            if (template == null)
                return;
            TMP_Text[] templateTexts = template.GetComponentsInChildren<TMP_Text>(true);
            if (templateTexts.Length == 1 && templateTexts[0].text.Contains("\n"))
            {
                if (!templateTexts[0].text.Contains("Z - 玩家技能"))
                    templateTexts[0].text += "\nZ - 玩家技能";
                RepairKeyAdded.Add(id);
                RefreshLayout(controls);
                return;
            }
            var holder = new GameObject("防晕3D repair row");
            holder.SetActive(false);
            GameObject clone = UnityEngine.Object.Instantiate(template, holder.transform, false);
            clone.name = "Mod key Z";
            StripScripts(clone);
            SetRowText(clone, "Z", "玩家技能");
            clone.transform.SetParent(template.transform.parent, false);
            clone.transform.SetSiblingIndex(lastIndex + 1);
            clone.SetActive(template.activeSelf);
            controls.shownControlsObjs.Add(clone);
            UnityEngine.Object.Destroy(holder);
            RepairKeyAdded.Add(id);
            RefreshLayout(controls);
            Log.LogInfo("Added Z 玩家技能 to the controls list.");
        }

        static void RefreshLayout(ShowControls controls)
        {
            try
            {
                if (controls.refreshLayout != null)
                    controls.refreshLayout.Refresh();
            }
            catch (Exception)
            {
            }
        }

        static void StripScripts(GameObject clone)
        {
            foreach (MonoBehaviour script in clone.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (script != null && !IsUiScript(script.GetType()))
                    UnityEngine.Object.DestroyImmediate(script);
            }
        }

        static bool IsUiScript(Type type)
        {
            for (Type t = type; t != null && t != typeof(MonoBehaviour); t = t.BaseType)
            {
                string space = t.Namespace ?? "";
                if (space.StartsWith("UnityEngine") || space.StartsWith("TMPro"))
                    return true;
            }
            return false;
        }

        static void SetRowText(GameObject clone, string key, string label)
        {
            TMP_Text[] texts = clone.GetComponentsInChildren<TMP_Text>(true);
            if (texts.Length == 1)
            {
                texts[0].text = key + " - " + label;
                return;
            }
            TMP_Text shortest = texts[0];
            TMP_Text longest = texts[0];
            foreach (TMP_Text text in texts)
            {
                if (text.text.Length < shortest.text.Length)
                    shortest = text;
                if (text.text.Length > longest.text.Length)
                    longest = text;
            }
            shortest.text = key;
            longest.text = longest.text.TrimStart().StartsWith("-") ? "- " + label : label;
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

        static readonly Dictionary<Renderer, Material[]> OriginalMaterials = new Dictionary<Renderer, Material[]>();
        static readonly List<Material> FadedMaterials = new List<Material>();
        static float _fadedAlpha = -1f;
        static float _lastPull;
        const float SeeThroughNear = 1.4f;
        const float SeeThroughFar = 2f;
        static float _nextAlphaRefresh;
        static bool _loggedShaders;

        static void ApplyBodyAlpha(FPP_Player player)
        {
            float alpha = Mathf.Lerp(Mathf.Clamp01(BodyAlpha.Value), 1f, Mathf.InverseLerp(SeeThroughNear, SeeThroughFar, _lastPull));
            bool want = _viewApplied && !player.isDead && !player.isInvisible && alpha < 0.99f;
            if (!want)
            {
                RestoreBodyMaterials();
                return;
            }
            if (Mathf.Abs(alpha - _fadedAlpha) > 0.01f)
            {
                _fadedAlpha = alpha;
                foreach (Material material in FadedMaterials)
                {
                    if (material == null)
                        continue;
                    Color c = material.GetColor("_Color");
                    c.a = alpha;
                    material.SetColor("_Color", c);
                }
            }
            if (Time.unscaledTime < _nextAlphaRefresh && OriginalMaterials.Count + OriginalShadows.Count > 0)
                return;
            _nextAlphaRefresh = Time.unscaledTime + 0.5f;
            var renderers = new List<Renderer>();
            SkinnedMeshRenderer[] meshes = CharacterMeshes(player);
            if (meshes != null)
                renderers.AddRange(meshes);
            MeshRenderer[] others = AllRenderers(player);
            if (others != null)
                renderers.AddRange(others);
            if (SeeThroughTemplate() == null)
            {
                HideBodyWhenClose(renderers, true);
                return;
            }
            if (OriginalShadows.Count > 0)
                HideBodyWhenClose(new List<Renderer>(OriginalShadows.Keys), false);
            var shaders = new HashSet<string>();
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || OriginalMaterials.ContainsKey(renderer))
                    continue;
                Material[] original = renderer.sharedMaterials;
                var faded = new Material[original.Length];
                for (int i = 0; i < original.Length; i++)
                {
                    if (original[i] == null)
                        continue;
                    shaders.Add(original[i].shader != null ? original[i].shader.name : "none");
                    faded[i] = SeeThroughCopy(original[i], alpha);
                    if (faded[i] != null)
                        FadedMaterials.Add(faded[i]);
                }
                OriginalMaterials[renderer] = original;
                renderer.sharedMaterials = faded;
            }
            if (!_loggedShaders && shaders.Count > 0)
            {
                _loggedShaders = true;
                Log.LogInfo("Body see-through applied. Shaders: " + string.Join(", ", shaders));
            }
        }

        static readonly Dictionary<Renderer, UnityEngine.Rendering.ShadowCastingMode> OriginalShadows = new Dictionary<Renderer, UnityEngine.Rendering.ShadowCastingMode>();

        static void HideBodyWhenClose(List<Renderer> renderers, bool hide)
        {
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                    continue;
                if (!OriginalShadows.ContainsKey(renderer))
                    OriginalShadows[renderer] = renderer.shadowCastingMode;
                renderer.shadowCastingMode = hide ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly : OriginalShadows[renderer];
            }
        }

        static void RestoreBodyMaterials()
        {
            foreach (KeyValuePair<Renderer, UnityEngine.Rendering.ShadowCastingMode> pair in OriginalShadows)
            {
                if (pair.Key != null)
                    pair.Key.shadowCastingMode = pair.Value;
            }
            OriginalShadows.Clear();
            if (OriginalMaterials.Count == 0)
                return;
            foreach (KeyValuePair<Renderer, Material[]> pair in OriginalMaterials)
            {
                if (pair.Key != null)
                    pair.Key.sharedMaterials = pair.Value;
            }
            OriginalMaterials.Clear();
            foreach (Material material in FadedMaterials)
            {
                if (material != null)
                    UnityEngine.Object.Destroy(material);
            }
            FadedMaterials.Clear();
            _fadedAlpha = -1f;
        }

        static Material _template;
        static bool _templateSearched;
        static float _templateRetryAt;

        static Material SeeThroughTemplate()
        {
            if (_template != null || (_templateSearched && Time.unscaledTime < _templateRetryAt))
                return _template;
            bool firstSearch = !_templateSearched;
            _templateSearched = true;
            _templateRetryAt = Time.unscaledTime + 10f;
            Material premultiplied = null;
            foreach (Material m in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (m == null || m.shader == null || m.shader.name != "Standard" || m.renderQueue < 2900)
                    continue;
                if (m.IsKeywordEnabled("_ALPHABLEND_ON"))
                {
                    _template = m;
                    break;
                }
                if (premultiplied == null && m.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON"))
                    premultiplied = m;
            }
            if (_template == null)
                _template = premultiplied;
            if (_template != null || firstSearch)
                Log.LogInfo(_template != null
                ? "See-through body uses the game's material " + _template.name + "."
                : "No see-through material in this map, your body hides when the camera is close instead.");
            return _template;
        }

        static Material SeeThroughCopy(Material source, float alpha)
        {
            Material template = SeeThroughTemplate();
            if (template == null)
                return null;
            var m = new Material(template);
            Texture main = source.HasProperty("_MainTex") ? source.GetTexture("_MainTex") : null;
            m.SetTexture("_MainTex", main);
            if (source.HasProperty("_MainTex"))
            {
                m.SetTextureScale("_MainTex", source.GetTextureScale("_MainTex"));
                m.SetTextureOffset("_MainTex", source.GetTextureOffset("_MainTex"));
            }
            if (m.IsKeywordEnabled("_NORMALMAP"))
                m.SetTexture("_BumpMap", source.HasProperty("_BumpMap") ? source.GetTexture("_BumpMap") : null);
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
    }
}
