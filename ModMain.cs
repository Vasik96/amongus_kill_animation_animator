using AmongUs.Data;
using AmongUs.Data.Player;
using AmongUs.GameOptions;
using amongus_menu;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using BepInEx.Unity.IL2CPP.Utils;
using HarmonyLib;
using Hazel;
using Il2CppInterop.Runtime.Injection;
using Il2CppSystem.Collections;
using InnerNet;
using Rewired;
using Sentry.Internal;
using Sentry.Internal.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.SceneManagement;
using static Il2CppSystem.Xml.XmlWellFormedWriter.AttributeValueCache;
using static NetworkedPlayerInfo;
using static UnityEngine.GraphicsBuffer;

namespace amongus_fortegreen
{
    [BepInPlugin("com.vasik96.amongus_menu", "AmongUs_Menu", "1.1.0")]
    public class ModMain : BasePlugin
    {
        public static ManualLogSource LoggerInstance = null!;
        private GameObject? updateHandler;

        private static bool showWindow = true;

        public override void Load()
        {
            LoggerInstance = Log;
            LoggerInstance.LogInfo("[Among Us Menu] -> Mod loaded.");

            var harmony = new Harmony("com.vasik96.amongus_menu");
            harmony.PatchAll();

            ClassInjector.RegisterTypeInIl2Cpp<UpdateLoop>();
            SceneManager.add_sceneLoaded(new Action<Scene, LoadSceneMode>(OnSceneLoaded));
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            LoggerInstance.LogInfo($"[Among Us Menu] -> Scene loaded: {scene.name}");
            StartUpdateLoop();
        }

        private void StartUpdateLoop()
        {
            if (updateHandler == null)
            {
                updateHandler = new GameObject("AmongUsMenu");
                updateHandler.AddComponent<UpdateLoop>();
                UnityEngine.Object.DontDestroyOnLoad(updateHandler);
                LoggerInstance.LogInfo("[Among Us Menu] -> UpdateLoop started.");
            }
        }
    }


    public class UpdateLoop : MonoBehaviour
    {

        public static UpdateLoop Instance { get; private set; }

        public List<string> impostors = new();
        public List<string> impostorNames = new();

        private Transform chatBubbleContainer;

        // Hook variables
        private static IntPtr hookId = IntPtr.Zero;
        private static WindowsInterop.LowLevelMouseProc proc = HookCallback;

        // Key hold timers
        private float numpad1HeldTime = 0f;
        private float numpad2HeldTime = 0f;
        private float numpad3HeldTime = 0f;
        private float numpad4HeldTime = 0f;
        private float numpad5HeldTime = 0f;
        private float numpad6HeldTime = 0f;
        private float numpad7HeldTime = 0f;
        private float numpad8HeldTime = 0f;
        private float numpad9HeldTime = 0f;

        //mod vars
        private bool scannerState = false;
        private bool noclipEnabled = false;
        private bool speed_enabled = false;
        private bool fullbright_enabled = false;
        private bool chatAlways = true;
        public static bool showImpostors = true;
        private bool force_all_doors_closed = false;

        public static bool walkInVent = false;
        public static bool seeGhostChat = false;
        public static bool ventAsCrew = false;

        public static bool unfixable_lights = false;
        private bool hasTriggeredUnfixableLights = false;

        public static bool fix_sabotages = false;
        public static bool instareport_when_killed = false;

        public static bool zoom_enabled = false;

        //previous states vars
        private bool prevScannerState = false;
        private bool prevNoclipState = false;
        private float prev_speed = 1.0f;

        public static bool instafix_sabotages = false;

        //other
        private float roleCheckTimer = 0f;
        private static bool showWindow = true;


        private List<(string role, string name, string color)> impostorInfo = new();



        private static bool _isInVent = false;
        public static bool IsInVent() => _isInVent;
        public static void SetInVent(bool value) => _isInVent = value;

        private static float lastVentToggleTime = 0f;
        private static float ventToggleCooldown = 1.0f; // half a second cooldown

        public static bool og_report_behavior = true;

        public static bool CanToggleVent()
        {
            float now = Time.time; // or Time.unscaledTime if you want real time
            if (now - lastVentToggleTime > ventToggleCooldown)
            {
                lastVentToggleTime = now;
                return true;
            }
            return false;
        }


        private static string lastKillMessage = "";
        private static float lastKillTime = 0f;
        private static readonly float displayDuration = 3f;


        //among us variables
        public static bool isFreePlay => AmongUsClient.Instance && AmongUsClient.Instance.NetworkMode == NetworkModes.FreePlay;


        string colorInput = "0";


        private Rect windowRect = new Rect(40, 20, 254, 1040);
        private Rect impostorWindowRect = new Rect(Screen.width - (250 + 20), 0 + (Screen.height * 0.15f), 250, 150);

        public KillOverlayAnimator kill_animator;
        Transform quadParent;

        public bool is_killoverlay_start_pending = false;
        public bool is_meeting_alter_pending = false;
        public bool waitForMeetingAnimation = false;

        void OnGUI()
        {
            /* show a medium size window at the top center with some margin from the top signaling who killed */
            // example: Red (playerName) killed Blue (playerName2)
            if (!string.IsNullOrEmpty(lastKillMessage) && Time.time - lastKillTime < displayDuration)
            {
                // Create a centered rect at the top of the screen
                var width = 400f;
                var height = 50f;
                Rect rect = new Rect(
                    (Screen.width - width) / 2f,
                    30f, // margin from top
                    width,
                    height
                );

                GUIStyle style = new GUIStyle(GUI.skin.label);
                style.alignment = TextAnchor.MiddleCenter;
                style.fontSize = 20;
                style.normal.textColor = Color.white;

                GUI.Box(rect, ""); // background box
                GUI.Label(rect, lastKillMessage, style);
            }

            if (showImpostors && impostorInfo.Count > 0)
            {
                impostorWindowRect = GUI.Window(1, impostorWindowRect, (GUI.WindowFunction)DrawImpostorWindow, "Impostors");
            }

            if (!showWindow) return;

            GUI.color = new Color(0f, 0f, 0f, 0.7f); // semi-transparent
            GUI.color = Color.white;

            windowRect = GUI.Window(0, windowRect, (GUI.WindowFunction)DrawMenuWindow, "Among Us Menu");

            
        }

        void DrawImpostorWindow(int windowID)
        {
            var compactLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                richText = true,
                padding = new RectOffset(),
                margin = new RectOffset()
            };
            compactLabelStyle.padding.left = 2;
            compactLabelStyle.padding.right = 2;
            compactLabelStyle.margin.top = 0;
            compactLabelStyle.margin.bottom = 0;

            foreach (var (role, name, color) in impostorInfo)
            {
                GUILayout.Label($"- {color} ({role}), [Name: {name}]", compactLabelStyle);
            }
        }



        void DrawMenuWindow(int windowID)
        {
            //GUILayout.Label("<color=#66AACC><b>Among Us Menu</b></color>", new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true, alignment = TextAnchor.MiddleCenter });

            //GUILayout.Space(10);

            GUILayout.Space(5);

            noclipEnabled = GUILayout.Toggle(noclipEnabled, " Noclip");
            speed_enabled = GUILayout.Toggle(speed_enabled, " Speed");
            zoom_enabled = GUILayout.Toggle(zoom_enabled, " Zoom");
            fullbright_enabled = GUILayout.Toggle(fullbright_enabled, " Fullbright");
            showImpostors = GUILayout.Toggle(showImpostors, " Show Impostors");
            chatAlways = GUILayout.Toggle(chatAlways, " Show chat");
            ventAsCrew = GUILayout.Toggle(ventAsCrew, " Vent as Crewmate");
            walkInVent = GUILayout.Toggle(walkInVent, " Walk in vent");
            ModVars.killAnyone = GUILayout.Toggle(ModVars.killAnyone, " Kill anyone");
            seeGhostChat = GUILayout.Toggle(seeGhostChat, "See ghosts + chat");
            ModVars.infiniteKillReach = GUILayout.Toggle(ModVars.infiniteKillReach, "Infinite kill distance");
            instafix_sabotages = GUILayout.Toggle(instafix_sabotages, "Disable sabotages");
            instareport_when_killed = GUILayout.Toggle(instareport_when_killed, "Auto-report");
            force_all_doors_closed = GUILayout.Toggle(force_all_doors_closed, "Doors closed");
            unfixable_lights = GUILayout.Toggle(unfixable_lights, "Unfixable lights");
            og_report_behavior = GUILayout.Toggle(og_report_behavior, "og call/report behavior");

            GUILayout.Space(3);

            if (GUILayout.Button("Save"))
            {
                SavePreferences();
            }
            if (GUILayout.Button("Load"))
            {
                LoadPreferences();
            }

            GUILayout.Space(15);

            if (GUILayout.Button("Set Color (RPC + name): Fortegreen"))
            {
                var player = PlayerControl.LocalPlayer;
                player?.RpcSetName("\v\v\v\v\v\v\v\v???");
                player?.RpcSetColor(18);
            }

            if (GUILayout.Button("Set Color (test, client-side): Fortegreen"))
            {

                var player = PlayerControl.LocalPlayer;
                player?.RawSetColor(-1);

                TrySettingFortegreen();
            }

            if (GUILayout.Button("kill yourself"))
            {
                MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)RpcCalls.MurderPlayer, SendOption.None, AmongUsClient.Instance.GetClientIdFromCharacter(PlayerControl.LocalPlayer));
                writer.WriteNetObject(PlayerControl.LocalPlayer);
                writer.Write((int)MurderResultFlags.Succeeded);
                AmongUsClient.Instance.FinishRpcImmediately(writer);
            }

            if (GUILayout.Button("kill yourself (freeplay)"))
            {
                ModMain.LoggerInstance.LogInfo("[Among Us Menu] -> killed");
                PlayerControl.LocalPlayer.RpcMurderPlayer(PlayerControl.LocalPlayer, true);
                
            }

            if (GUILayout.Button("Force Impostor (Self + idk)"))
            {
                var player = PlayerControl.LocalPlayer;
                player?.RpcSetRole(AmongUs.GameOptions.RoleTypes.Impostor, false);

                foreach (var p in PlayerControl.AllPlayerControls)
                {
                    if (p?.Data?.PlayerName == "sahur")
                    {
                        p.RpcSetRole(AmongUs.GameOptions.RoleTypes.Impostor, false);
                        break;
                    }
                    else if (p?.Data?.PlayerName == "idk")
                    {
                        p.RpcSetRole(AmongUs.GameOptions.RoleTypes.Impostor, false);
                        break;
                    }
                }
            }
            if (GUILayout.Button("Everyone same color"))
            {
                everyoneSomething();
            }

            if (GUILayout.Button("Kill all players"))
            {
                foreach (var player in PlayerControl.AllPlayerControls)
                {
                    murderPlayer(player, MurderResultFlags.Succeeded);
                }
            }

            if (GUILayout.Button("Send hacked chat msg"))
            {
                string message = "!!! THIS LOBBY HAS BEEN HACKED !!!\n\n!!! LEAVE TO PLAY NORMALLY !!!\n\n\n" +
                    "THE IMPOSTORS ARE:\n";

                for (int i = 0; i < impostorInfo.Count; i++)
                {
                    var impostor = impostorInfo[i];
                    message += $"{impostor.color.ToUpper()}\n";
                }

                PlayerControl.LocalPlayer.RpcSendChat(message);
            }

            // official character limit of among us servers is: 120
            if (GUILayout.Button("Clear chat"))
            {
                int lines = 60;
                string blankLine = "·"; // visible, tiny character
                string message = string.Join("\n", Enumerable.Repeat(blankLine, lines));
                PlayerControl.LocalPlayer.RpcSendChat(message);
            }


            if (GUILayout.Button("Report nearest body"))
            {
                PlayerControl nearestBody = null;
                float closestDistance = float.MaxValue;

                var localPlayer = PlayerControl.LocalPlayer;
                var allPlayers = PlayerControl.AllPlayerControls;

                foreach (var player in allPlayers)
                {
                    if (player.Data == null || !player.Data.IsDead)
                        continue;

                    float distance = Vector2.Distance(player.GetTruePosition(), localPlayer.GetTruePosition());

                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        nearestBody = player;
                    }
                }


                if (nearestBody != null)
                {
                    reportDeadBody(nearestBody.Data);
                }
                else
                {
                    ModMain.LoggerInstance.LogInfo("No body exists");

                    //disabled due to being kicked in the new AU version
                    
                    // Pick the first alive player as fallback
                    foreach (var player in allPlayers)
                    {
                        if (player.Data != null && player.Data.IsDead) // pick any dead player, alive players kick u from the game
                        {
                            reportDeadBody(player.Data);
                            break; // just the first alive player, then stop
                        }
                    }
                }
            }

            if (GUILayout.Button("Sabotage EVERYTHING"))
            {
                SabotageSystem.reactorSab = true;
                SabotageSystem.oxygenSab = true;
                SabotageSystem.commsSab = true;
                SabotageSystem.elecSab = true;
                SabotageSystem.mushSab = true;
                //SabotageSystem.doorsSab = true;

                ModMain.LoggerInstance.LogInfo("[Among Us Menu] -> Sabotaged everything.");
            }

            if (GUILayout.Button("test"))
            {
                ModMain.LoggerInstance.LogInfo("[Among Us Menu] -> test");

                kill_animator.StartAnimation(2f);

            }



            if (GUILayout.Button("Fix all sabotages"))
            {
                force_all_doors_closed = false;
                unfixable_lights = false;
                fix_sabotages = true;

                SabotageSystem.reactorSab = false;
                SabotageSystem.oxygenSab = false;
                SabotageSystem.commsSab = false;
                SabotageSystem.elecSab = false;
                SabotageSystem.unfixableLights = false;
                SabotageSystem.mushSab = false;
                SabotageSystem.doorsSab = false;


                ModMain.LoggerInstance.LogInfo("[Among Us Menu] -> Fixed sabotages.");
            }

            if (GUILayout.Button("Fake revive"))
            {
                PlayerControl.LocalPlayer.Revive();
            }





            if (GUILayout.Button("Call meeting"))
            {
                ModMain.LoggerInstance.LogInfo("[Among Us Menu] -> Meeting called");
                PlayerControl.LocalPlayer.CmdReportDeadBody(null);
            }

            if (GUILayout.Button("End meeting (local)"))
            {
                var cam = GameObject.Find("Main Camera");
                var hudTransform = cam?.transform.Find("Hud/MeetingHub(Clone)");
                var meetingHud = hudTransform?.GetComponent<MeetingHud>();
                meetingHud?.Close();
            }
            if (GUILayout.Button("Complete all tasks"))
            {
                completeMyTasks();
            }







            /*** FAKE VISUAL TASKS ***/
            GUILayout.Label("Fake visual tasks:", new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold });
            scannerState = GUILayout.Toggle(scannerState, " Fake scan");
            if (GUILayout.Button("Trash task"))
            {
                SendFakeVisual((byte)10);
            }
            if (GUILayout.Button("Weapons task"))
            {
                SendFakeVisual((byte)6);
            }
            if (GUILayout.Button("Shields task"))
            {
                SendFakeVisual((byte)1);
            }

            GUILayout.Label("Zoom: Shift + Scroll");
            if (ModVars.ZoomLevel != 3f)
            {
                GUILayout.Label($"Zoom amount: {ModVars.ZoomLevel}");
            }
            GUILayout.Label("Press 'Home' to hide");

            /*
            if (showImpostors && impostorInfo.Count > 0)
            {
                GUILayout.Label("Impostors:", new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold });

                var compactLabelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 12,
                    richText = true,
                    padding = new RectOffset(),
                    margin = new RectOffset()
                };
                compactLabelStyle.padding.left = 2;
                compactLabelStyle.padding.right = 2;
                compactLabelStyle.margin.top = 0;
                compactLabelStyle.margin.bottom = 0;

                foreach (var (role, name, color) in impostorInfo)
                {
                    GUILayout.Label($"- {color} ({role}), [Name: {name}]", compactLabelStyle);
                }

                GUILayout.Space(10);
            }*/

            //GUILayout.EndArea();

            GUI.DragWindow(new Rect(0, 0, windowRect.width, 20));
        }

        void SavePreferences()
        {
            PlayerPrefs.SetInt("noclipEnabled", noclipEnabled ? 1 : 0);
            PlayerPrefs.SetInt("speed_enabled", speed_enabled ? 1 : 0);
            PlayerPrefs.SetInt("zoom_enabled", zoom_enabled ? 1 : 0);
            PlayerPrefs.SetInt("fullbright_enabled", fullbright_enabled ? 1 : 0);
            PlayerPrefs.SetInt("showImpostors", showImpostors ? 1 : 0);
            PlayerPrefs.SetInt("chatAlways", chatAlways ? 1 : 0);
            PlayerPrefs.SetInt("ventAsCrew", ventAsCrew ? 1 : 0);
            PlayerPrefs.SetInt("walkInVent", walkInVent ? 1 : 0);
            PlayerPrefs.SetInt("killAnyone", ModVars.killAnyone ? 1 : 0);
            PlayerPrefs.SetInt("seeGhostChat", seeGhostChat ? 1 : 0);
            PlayerPrefs.SetInt("infiniteKillReach", ModVars.infiniteKillReach ? 1 : 0);
            PlayerPrefs.SetInt("instafix_sabotages", instafix_sabotages ? 1 : 0);
            PlayerPrefs.SetInt("instareport_when_killed", instareport_when_killed ? 1 : 0);
            PlayerPrefs.SetInt("force_all_doors_closed", force_all_doors_closed ? 1 : 0);
            PlayerPrefs.SetInt("unfixable_lights", unfixable_lights ? 1 : 0);
            PlayerPrefs.SetInt("og_report_behavior", og_report_behavior ? 1 : 0);
            PlayerPrefs.Save();
        }

        // Load all toggles from PlayerPrefs
        void LoadPreferences()
        {
            noclipEnabled = PlayerPrefs.GetInt("noclipEnabled", 0) == 1;
            speed_enabled = PlayerPrefs.GetInt("speed_enabled", 0) == 1;
            zoom_enabled = PlayerPrefs.GetInt("zoom_enabled", 0) == 1;
            fullbright_enabled = PlayerPrefs.GetInt("fullbright_enabled", 0) == 1;
            showImpostors = PlayerPrefs.GetInt("showImpostors", 0) == 1;
            chatAlways = PlayerPrefs.GetInt("chatAlways", 0) == 1;
            ventAsCrew = PlayerPrefs.GetInt("ventAsCrew", 0) == 1;
            walkInVent = PlayerPrefs.GetInt("walkInVent", 0) == 1;
            ModVars.killAnyone = PlayerPrefs.GetInt("killAnyone", 0) == 1;
            seeGhostChat = PlayerPrefs.GetInt("seeGhostChat", 0) == 1;
            ModVars.infiniteKillReach = PlayerPrefs.GetInt("infiniteKillReach", 0) == 1;
            instafix_sabotages = PlayerPrefs.GetInt("instafix_sabotages", 0) == 1;
            instareport_when_killed = PlayerPrefs.GetInt("instareport_when_killed", 0) == 1;
            force_all_doors_closed = PlayerPrefs.GetInt("force_all_doors_closed", 0) == 1;
            unfixable_lights = PlayerPrefs.GetInt("unfixable_lights", 0) == 1;
            og_report_behavior = PlayerPrefs.GetInt("og_report_behavior", 0) == 1;
        }

















        private Transform reportAnimTransform;
        private GameObject originalReportText;
        private GameObject originalReportStab;
        private GameObject clonedReportText;
        private GameObject clonedReportStab;
        private Transform killOverlayParent;

        // Helper to set position correctly for UI vs normal transforms
        private void SetClonePosition(ref GameObject go, Vector3 localPos)
        {
            if (go == null) return;
            go.transform.localPosition = localPos; //0 1 -135
        }

        // Call this once when you find the HUD report animation object (or every time if HUD is re-created)
        private void CacheReportOriginals(Transform reportAnim)
        {
          
            reportAnimTransform = reportAnim;
            if (reportAnimTransform == null) return;
            

                var textT = reportAnimTransform.Find("Text (TMP)");
            originalReportText = textT != null ? textT.gameObject : null;

            var stabT = reportAnimTransform.Find("killstabplayerstill");
            originalReportStab = stabT != null ? stabT.gameObject : null;

            // Ensure killOverlayParent exists
            if (killOverlayParent == null)
            {
                var hud = reportAnimTransform.root.Find("Hud");
                if (hud != null)
                    killOverlayParent = hud.Find("KillOverlay/QuadParent") ?? hud.Find("KillOverlay/QuadParent");
            }
        }

        // Spawn clones (call when starting the overlay animation)
        private void SpawnReportClones(bool isMeeting)
        {
            // safety
            if (reportAnimTransform == null)
            {
                //ModMain.LoggerInstance.LogMessage("SpawnReportClones: reportAnimTransform is null");
                return;
            }

            // Always re-find HUD root so parenting is consistent
            var hud = reportAnimTransform.root.Find("Hud");
            if (hud == null)
            {
                ModMain.LoggerInstance.LogMessage("SpawnReportClones: could not find Hud");
                return;
            }

            // Text clone
            if (clonedReportText == null && originalReportText != null)
            {
                clonedReportText = UnityEngine.Object.Instantiate(originalReportText, hud, false);
                clonedReportText.SetActive(true);
                originalReportText.SetActive(false);

                clonedReportText.transform.localPosition = new Vector3(0f, -0.2f, -136f);
                clonedReportText.transform.localRotation = Quaternion.identity;

                ModMain.LoggerInstance.LogMessage($"Cloned Text TMP localPos: {clonedReportText.transform.localPosition}");
            }

            // Stab clone
            if (clonedReportStab == null && originalReportStab != null)
            {
                clonedReportStab = UnityEngine.Object.Instantiate(originalReportStab, hud, false);
                clonedReportStab.SetActive(true);
                originalReportStab.SetActive(false);

                clonedReportStab.transform.localRotation = Quaternion.identity;

                if (isMeeting)
                {
                    clonedReportStab.transform.localPosition = new Vector3(-0.7f, 1.45f, -135f);
                    clonedReportStab.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
                }
                else
                {
                    clonedReportStab.transform.localPosition = new Vector3(0f, 1f, -135f);
                    clonedReportStab.transform.localScale = Vector3.one;
                }


                // if meeting, scale all to 0,5

                ModMain.LoggerInstance.LogMessage($"Cloned Stab localPos: {clonedReportStab.transform.localPosition}");
            }
        }

        private void DisableOriginalReportObjects()
        {
            if (originalReportText != null)
                originalReportText.SetActive(false);

            if (originalReportStab != null)
                originalReportStab.SetActive(false);

            ModMain.LoggerInstance.LogMessage("Original report objects disabled.");
        }


        // Called when shrinking / animation ends to clean up
        private void CleanupReportClones()
        {
            ModMain.LoggerInstance.LogMessage("CleanupReportClones: destroying clones and restoring originals");

            if (clonedReportText != null)
            {
                UnityEngine.Object.Destroy(clonedReportText);
                clonedReportText = null;
            }
            if (clonedReportStab != null)
            {
                UnityEngine.Object.Destroy(clonedReportStab);
                clonedReportStab = null;
            }
        }










        public static void ShowKillMessage(string killerColor, string killerName, string victimColor, string victimName)
        {
            lastKillMessage = $"{killerColor} ({killerName}) killed {victimColor} ({victimName})";
            lastKillTime = Time.time;
        }

        void SendFakeVisual(byte animId)
        {
            // Play locally
            PlayerControl.LocalPlayer.PlayAnimation(animId);

            // Forge + send RPC to everyone
            var client = AmongUsClient.Instance;
            var writer = client.StartRpcImmediately(
                PlayerControl.LocalPlayer.NetId,   // the sender's NetId
                (byte)RpcCalls.PlayAnimation,      // the RPC type
                SendOption.Reliable,               // not None; Reliable is correct for this
                -1                                 // targetId (-1 = everyone)
            );

            writer.Write(animId);                  // write the animation id
            client.FinishRpcImmediately(writer);   // finish + send
        }


        void SendFakeName(string newName)
        {
            // Change locally
            PlayerControl.LocalPlayer.SetName(newName);

            // Send RPC to everyone else
            var client = AmongUsClient.Instance;
            var writer = client.StartRpcImmediately(
                PlayerControl.LocalPlayer.NetId,
                (byte)RpcCalls.SetName,
                SendOption.Reliable,
                -1
            );

            writer.Write(newName);
            client.FinishRpcImmediately(writer);
        }

        

        private bool lastActive;
        private Vector3 lastLocalPos, lastLocalScale;
        private Quaternion lastLocalRot;
        private float lastPosChangeTime = 0f;
        private float lastScaleChangeTime = 0f;
        private float lastRotChangeTime = 0f;
        private float lastActiveChangeTime = 0f;

        
        void Awake()
        {
            Instance = this;

            hookId = WindowsInterop.SetHook(proc);

            prevScannerState = scannerState;
            prev_speed = -1f;

            SetInVent(false);

            quadParent = null;
            kill_animator = null;
        }










        private Transform fullscreenTransform;
        private SpriteRenderer fullscreenRenderer;

        // Last-known states for logging
        private bool lastEnabled;
        private float lastEnabledChangeTime = 0f;

        private void FindFullScreen()
        {
            var cam = GameObject.Find("Main Camera");
            if (cam != null)
            {
                var hud = cam.transform.Find("Hud/KillOverlay/FullScreen");
                if (hud != null)
                {
                    fullscreenTransform = hud;
                    fullscreenRenderer = hud.GetComponent<SpriteRenderer>();

                    if (fullscreenRenderer != null)
                    {
                        lastEnabled = fullscreenRenderer.enabled;
                    }
                }
            }
        }

        public void UpdateFullScreenLogging(float currentTime)
        {
            if (fullscreenTransform == null || fullscreenRenderer == null)
            {
                FindFullScreen();
                return;
            }

            // --- ENABLED STATE ---
            if (fullscreenRenderer.enabled != lastEnabled)
            {
                float delta = currentTime - lastEnabledChangeTime;
                ModMain.LoggerInstance.LogInfo(
                    $"[FullScreen] Enabled: {lastEnabled} -> {fullscreenRenderer.enabled} (Δt={delta:F3}s)"
                );
                lastEnabled = fullscreenRenderer.enabled;
                lastEnabledChangeTime = currentTime;
            }
        }



















        public GameObject flameObj;
        public Transform flameTransform;

        private float lastHighlightTime = 0f;
        private const float highlightInterval = 0.5f;

        private const float sabotages_interval = 3.0f;
        private float lastSabotagesTime = 0f;


        private GameObject meetingBackground;

        Transform? meetingAnim;
        Transform? reportAnim;

        private static readonly string[] KillAnimations = new string[]
        {
            "KillStabAnimation(Clone)",
            "KillNeckAnimation(Clone)",
            "KillTongueAnimation(Clone)",
            "PunchShootKill(Clone)",
            "KillViperAnimation(Clone)",
            "KillTongueAnimationSeeker(Clone)",
            "KillNeckAnimationSeeker(Clone)",
            "HorseKill(Clone)",
            "LongKillHorse(Clone)",
            "LongKill(Clone)",
            "PunchShootKillSeeker(Clone)",
            "KillStabAnimationSeeker(Clone)",
            "RHMKill(Clone)",
            "WerewolfKill(Clone)"
        };

        // Call this to check if a kill animation is running
        private bool IsKillAnimationActive()
        {
            var hud = GameObject.Find("Hud")?.transform; // or Main Camera/Hud
            if (hud == null) return false;

            foreach (var animName in KillAnimations)
            {
                var anim = hud.Find($"KillOverlay/{animName}");
                if (anim != null && anim.gameObject.activeSelf)
                    return true;
            }
            return false;
        }

        private bool IsReportOrMeetingAnimationActive()
        {
            var cam = GameObject.Find("Main Camera");
            if (cam == null) return false;

            var hud = cam.transform.Find("Hud");
            if (hud == null) return false;

            var reportBodyAnim = hud.Find("KillOverlay/ReportBodyAnimation(Clone)");
            var emergencyAnim = hud.Find("KillOverlay/EmergencyAnimation(Clone)");

            return reportBodyAnim != null || emergencyAnim != null;
        }

        

        void Update()
        {
            try
            {
                float currentTime = Time.time;


                if (kill_animator == null)
                {
                    kill_animator = new KillOverlayAnimator();
                }

                if (og_report_behavior)
                {
                    var cam = GameObject.Find("Main Camera");
                    if (cam != null)
                    {
                        var hud = cam.transform.Find("Hud");
                        if (hud != null)
                        {

                            var reportBodyAnim = hud.Find("KillOverlay/ReportBodyAnimation(Clone)");
                            if (reportBodyAnim != null)
                            {
                                reportBodyAnim.gameObject.SetActive(false); // disable immediately
                                reportAnim = reportBodyAnim;
                                CacheReportOriginals(reportAnim);
                            }

                            var emergencyAnim = hud.Find("KillOverlay/EmergencyAnimation(Clone)");
                            if (emergencyAnim != null)
                            {
                                emergencyAnim.gameObject.SetActive(false); // disable immediately
                                meetingAnim = emergencyAnim;
                                CacheReportOriginals(meetingAnim);
                            }
                        }
                    }
                }


                if (waitForMeetingAnimation)
                {
                    //ModMain.LoggerInstance.LogInfo("checking for meeting animation");
                    if (IsReportOrMeetingAnimationActive())
                    {
                        //ModMain.LoggerInstance.LogInfo("variables set to replace meeting animation");
                        is_meeting_alter_pending = true;
                        is_killoverlay_start_pending = true;

                        waitForMeetingAnimation = false; // stop checking
                    }
                }


                if (is_meeting_alter_pending)
                {

                    // Initialize references if needed
                    if (meetingBackground == null)
                    {
                        var cam = GameObject.Find("Main Camera");
                        if (cam != null)
                        {
                            var hud = cam.transform.Find("Hud");
                            if (hud != null)
                            {
                                //meeting animation
                                var textBg = hud.Find("KillOverlay/EmergencyAnimation(Clone)/TextBg");
                                if (textBg != null)
                                {
                                    textBg.gameObject.SetActive(false);
                                }
                                var speedLines = hud.Find("KillOverlay/EmergencyAnimation(Clone)/SpeedLines");
                                if (speedLines != null)
                                {
                                    speedLines.gameObject.SetActive(false);
                                }
                                var yellowTape = hud.Find("KillOverlay/EmergencyAnimation(Clone)/yellowtape");
                                if (yellowTape != null)
                                {
                                    yellowTape.gameObject.SetActive(false);
                                }
                                meetingAnim = hud.Find("KillOverlay/EmergencyAnimation(Clone)");
                                if (meetingAnim != null)
                                {
                                    if (reportAnimTransform == null || reportAnimTransform != meetingAnim) // the meeting animation has the same names and structure,
                                                                                                           // so we can reuse the variables and methods
                                    {
                                        CacheReportOriginals(meetingAnim);
                                    }
                                }


                                //reportbody animation (same as meeting animation)
                                var reportbody_textBg = hud.Find("KillOverlay/ReportBodyAnimation(Clone)/TextBg");
                                if (reportbody_textBg != null)
                                {
                                    reportbody_textBg.gameObject.SetActive(false);
                                }
                                var reportbody_speedLines = hud.Find("KillOverlay/ReportBodyAnimation(Clone)/SpeedLines");
                                if (reportbody_speedLines != null)
                                {
                                    reportbody_speedLines.gameObject.SetActive(false);
                                }
                                var reportbody_yellowTape = hud.Find("KillOverlay/ReportBodyAnimation(Clone)/yellowtape");
                                if (reportbody_yellowTape != null)
                                {
                                    reportbody_yellowTape.gameObject.SetActive(false);
                                }
                                // inside your is_meeting_alter_pending block
                                reportAnim = hud.Find("KillOverlay/ReportBodyAnimation(Clone)");
                                if (reportAnim != null)
                                {
                                    if (reportAnimTransform == null || reportAnimTransform != reportAnim)
                                    {
                                        CacheReportOriginals(reportAnim);
                                    }
                                }

                                var meetingHubBg = hud.Find("MeetingHub(Clone)/Background");
                                if (meetingHubBg != null)
                                {
                                    meetingBackground = meetingHubBg.gameObject;
                                    //ModMain.LoggerInstance.LogMessage("meeting appeared, cleaning up clones");


                                    // Reset the flag here so next report can spawn clones
                                    is_killoverlay_start_pending = true;
                                }

                            }
                        }
                    }

                    // Start kill animation if pending
                    if (is_killoverlay_start_pending)
                    {
                        kill_animator.StartAnimation(1.9f);
                        is_killoverlay_start_pending = false;
                        DisableOriginalReportObjects();

                        if (meetingBackground != null)
                            meetingBackground.SetActive(false);
                    }

                }



                if (kill_animator != null)
                    kill_animator.Update();

                UpdateFullScreenLogging(Time.time);

                // Debug logging – always see current phase
                if (kill_animator != null)
                {
                    //ModMain.LoggerInstance.LogMessage($"KillAnimator Phase: {kill_animator.CurrentPhase}");

                    if (kill_animator.CurrentPhase == KillOverlayAnimator.AnimationPhase.Paused)
                    {
                        // Spawn clones only once when we first enter Paused
                        if (clonedReportText == null && clonedReportStab == null)
                        {
                            bool isMeeting = false;

                            if (meetingAnim != null)
                                isMeeting = true;
                            else if (reportAnim != null)
                                isMeeting = false;

                            SpawnReportClones(isMeeting);
                            //Debug.Log("Clones spawned at Paused phase");
                        }

                        // Force their positions in case anything drifts
                        /*
                        if (clonedReportText != null)
                            clonedReportText.transform.localPosition = new Vector3(-2.6f, 0f, -136f);

                        if (clonedReportStab != null)
                            clonedReportStab.transform.localPosition = new Vector3(0f, 1f, -135f);*/
                    }

                    // Cleanup after animation fully done
                    if (kill_animator.CurrentPhase == KillOverlayAnimator.AnimationPhase.Idle ||
                        kill_animator.CurrentPhase == KillOverlayAnimator.AnimationPhase.None ||
                        kill_animator.CurrentPhase == KillOverlayAnimator.AnimationPhase.Shrinking)
                    {
                        if (clonedReportText != null || clonedReportStab != null)
                        {
                            //ModMain.LoggerInstance.LogMessage("Cleaning up clones (anim finished)...");
                            CleanupReportClones();
                        }

                        is_meeting_alter_pending = false;
                        is_killoverlay_start_pending = false;
                    }
                }


                /*
                if (flameTransform == null)
                {
                    var cam = GameObject.Find("Main Camera");
                    if (cam != null)
                    {
                        var target = cam.transform.Find("Hud/KillOverlay/QuadParent");
                        if (target != null)
                        {
                            flameObj = target.gameObject;
                            flameTransform = target;

                            // store initial values
                            lastLocalPos = flameTransform.localPosition;
                            lastLocalScale = flameTransform.localScale;
                            lastLocalRot = flameTransform.localRotation;
                            lastActive = flameObj.activeSelf;

                            ModMain.LoggerInstance.LogInfo("[QuadParent] Found and monitoring started.");
                        }
                    }
                }

                if (flameTransform != null)
                {
                    // --- LOCAL POSITION ---
                    if ((flameTransform.localPosition - lastLocalPos).sqrMagnitude > 0.0001f)
                    {
                        float delta = currentTime - lastPosChangeTime;
                        ModMain.LoggerInstance.LogInfo($"[QuadParent] LocalPos: {lastLocalPos} -> {flameTransform.localPosition} (Δt={delta:F3}s)");
                        lastLocalPos = flameTransform.localPosition;
                        lastPosChangeTime = currentTime;
                    }

                    // --- LOCAL SCALE ---
                    if ((flameTransform.localScale - lastLocalScale).sqrMagnitude > 0.0001f)
                    {
                        float delta = currentTime - lastScaleChangeTime;
                        ModMain.LoggerInstance.LogInfo($"[QuadParent] Scale: {lastLocalScale} -> {flameTransform.localScale} (Δt={delta:F3}s)");
                        lastLocalScale = flameTransform.localScale;
                        lastScaleChangeTime = currentTime;
                    }

                    // --- LOCAL ROTATION ---
                    if (Quaternion.Angle(flameTransform.localRotation, lastLocalRot) > 0.01f)
                    {
                        float delta = currentTime - lastRotChangeTime;
                        ModMain.LoggerInstance.LogInfo($"[QuadParent] LocalRot: {lastLocalRot.eulerAngles} -> {flameTransform.localRotation.eulerAngles} (Δt={delta:F3}s)");
                        lastLocalRot = flameTransform.localRotation;
                        lastRotChangeTime = currentTime;
                    }

                    // --- ACTIVE STATE ---
                    if (flameObj.activeSelf != lastActive)
                    {
                        float delta = currentTime - lastActiveChangeTime;
                        ModMain.LoggerInstance.LogMessage($"[QuadParent] Active: {lastActive} -> {flameObj.activeSelf} (Δt={delta:F3}s)");
                        lastActive = flameObj.activeSelf;
                        lastActiveChangeTime = currentTime;
                    }
                }
                */





                if (Input.GetKeyDown(KeyCode.Home))
                {
                    showWindow = !showWindow;
                }

                HandleZoom();

                HandlePlayerModifiers();
                if (showImpostors) 
                {
                    HandleRoleChecking();
                }

                
                HandleShadowQuad();
                //HandleKeybinds();
                
                HandleChatVisibility();


                if (seeGhostChat)
                {
                    seeGhostsCheat();
                }


               

                if (Time.realtimeSinceStartup - lastSabotagesTime >= sabotages_interval)
                {
                    lastSabotagesTime = Time.realtimeSinceStartup;

                    if (!force_all_doors_closed && !unfixable_lights)
                        return;

                    if (force_all_doors_closed)
                        SabotageSystem.doorsSab = true;

                    if (unfixable_lights && !hasTriggeredUnfixableLights)
                    {
                        SabotageSystem.unfixableLights = true;
                        hasTriggeredUnfixableLights = true;
                    }

                    ModMain.LoggerInstance.LogInfo("unfixable sabotages enabled");
                }


                ////////HIGHLIGHT RED NAMES IN CHAT

                if (!showImpostors) return;
                if (impostors.Count == 0) return;

                if (chatBubbleContainer == null)
                {
                    var cam = GameObject.Find("Main Camera");
                    if (cam == null) return;

                    chatBubbleContainer = cam.transform.Find("Hud/ChatUi/ChatScreenRoot/ChatScreenContainer/Scroller/Items");
                }

                if (chatBubbleContainer == null) return;

                if (Time.realtimeSinceStartup - lastHighlightTime >= highlightInterval)
                {
                    // do demanding stuff, that would lag the game if executed every frame, here:
                    //HighlightChatImpostors(impostorNames);
                    
                    lastHighlightTime = Time.realtimeSinceStartup;
                }

                /*************************************/


            }
            catch (Exception ex)
            {
                ModMain.LoggerInstance.LogError($"[Among Us Menu] -> Exception in Update(): {ex}");
            }
        }

        void OnDestroy()
        {
            if (hookId != IntPtr.Zero)
            {
                WindowsInterop.UnhookWindowsHookEx(hookId);
                hookId = IntPtr.Zero;
            }
        }

        private void everyoneSomething()
        {
            // Loop through all players
            foreach (var player in PlayerControl.AllPlayerControls)
            {
                if (player == null) continue;

                player.RpcSetColor(0);
            }
        }



        public static void sabotageCheat(ShipStatus shipStatus)
        {
            byte currentMapID = getCurrentMapID();


            SabotageSystem.handleReactor(shipStatus, currentMapID);
            SabotageSystem.handleOxygen(shipStatus, currentMapID);
            SabotageSystem.handleComms(shipStatus, currentMapID);
            SabotageSystem.handleElectrical(shipStatus, currentMapID);
            SabotageSystem.handleMushMix(shipStatus, currentMapID);
            SabotageSystem.handleDoors(shipStatus);
        }

        public static byte getCurrentMapID()
        {
            // If playing the tutorial
            if (isFreePlay)
            {
                return (byte)AmongUsClient.Instance.TutorialMapId;

            }
            else
            {
                // Works for local/online games
                return GameOptionsManager.Instance.currentGameOptions.MapId;
            }
        }


        public static void TrySettingFortegreen()
        {
            //PlayerControl.LocalPlayer.color

            PlayerControl.LocalPlayer.Data.UpdateColor(-1);
        }

        public static void bypassEngineerVents(EngineerRole engineerRole)
        {
            // Makes vent time so incredibly long (float.MaxValue) so that it never ends
            engineerRole.inVentTimeRemaining = float.MaxValue;

            if (engineerRole.cooldownSecondsRemaining > 0f)
            {

                engineerRole.cooldownSecondsRemaining = 0f;

                DestroyableSingleton<HudManager>.Instance.AbilityButton.ResetCoolDown();
                DestroyableSingleton<HudManager>.Instance.AbilityButton.SetCooldownFill(0f);
            } 
        }


        public static void reportDeadBody(NetworkedPlayerInfo playerData)
        {
            var HostData = AmongUsClient.Instance.GetHost();
            if (HostData != null && !HostData.Character.Data.Disconnected)
            {
                MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)RpcCalls.ReportDeadBody, SendOption.None, HostData.Id);
                writer.Write(playerData.PlayerId);
                AmongUsClient.Instance.FinishRpcImmediately(writer);
            }
        }


        public static void completeMyTasks()
        {

            var HostData = AmongUsClient.Instance.GetHost();
            if (HostData != null && !HostData.Character.Data.Disconnected)
            {
                foreach (PlayerTask task in PlayerControl.LocalPlayer.myTasks)
                {
                    if (!task.IsComplete)
                    {

                        foreach (var item in PlayerControl.AllPlayerControls)
                        {
                            MessageWriter messageWriter = AmongUsClient.Instance.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)RpcCalls.CompleteTask, SendOption.None, AmongUsClient.Instance.GetClientIdFromCharacter(item));
                            messageWriter.WritePacked(task.Id);
                            AmongUsClient.Instance.FinishRpcImmediately(messageWriter);
                        }

                    }
                }
            }
        }

        public static void seeGhostsCheat()
        {
            try
            {
                if (!PlayerControl.LocalPlayer.Data.IsDead)
                {
                    foreach (var player in PlayerControl.AllPlayerControls)
                    {
                        if (player.Data.IsDead)
                        {
                            player.Visible = seeGhostChat;
                        }
                    }
                }
            }
            catch { }
        }



        public static void walkInVentCheat()
        {
            try
            {

                if (walkInVent)
                {
                    PlayerControl.LocalPlayer.inVent = false;
                    PlayerControl.LocalPlayer.moveable = true;
                }

            }
            catch { }
        }

        public static int? GetCurrentHighlightedVentId()
        {
            if (HudManager.Instance == null || ShipStatus.Instance == null || ShipStatus.Instance.AllVents == null)
                return null;

            var ventButton = HudManager.Instance.ImpostorVentButton;
            if (ventButton == null)
                return null;

            var vent = ventButton.currentTarget;
            if (vent == null)
                return null;

            // Log vent name and its actual Id
            ModMain.LoggerInstance.LogInfo($"Current highlighted vent: {vent.name} with ID: {vent.Id}");

            // Return the vent's actual ID, not the index
            return vent.Id;
        }


        public static void murderPlayer(PlayerControl target, MurderResultFlags result)
        {
            foreach (var item in PlayerControl.AllPlayerControls)
            {
                MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)RpcCalls.MurderPlayer, SendOption.None, AmongUsClient.Instance.GetClientIdFromCharacter(item));
                writer.WriteNetObject(target);
                writer.Write((int)result);
                AmongUsClient.Instance.FinishRpcImmediately(writer);
            }
        }

        private void HandleZoom()
        {
            if (!zoom_enabled)
                return;

            if (ModVars.ZoomUsed)
            {
                var camObj = GameObject.Find("Main Camera");
                if (camObj != null)
                {
                    var camera = camObj.GetComponent<Camera>();
                    if (camera != null)
                    {
                        camera.orthographicSize = ModVars.ZoomLevel;
                    }
                    fullbright_enabled = true;
                }
                ModVars.ZoomUsed = false;
            }
        }

        private void HandlePlayerModifiers()
        {
            var player = PlayerControl.LocalPlayer;
            if (player == null) return;

            //noclip
            var collider = player.GetComponent<CircleCollider2D>();
            if (collider != null)
            {
                if (noclipEnabled)
                {
                    if (collider.enabled)
                    {
                        collider.enabled = false; // spam-disable each frame if re-enabled
                    }

                    // Only log once when noclip is first enabled
                    if (!prevNoclipState)
                    {
                        ModMain.LoggerInstance.LogInfo("Enabled noclip");
                        prevNoclipState = true;
                    }
                }
                else
                {
                    if (!collider.enabled)
                    {
                        collider.enabled = true;
                        ModMain.LoggerInstance.LogInfo("Disabled noclip");
                    }

                    prevNoclipState = false;
                }
            }


            //speed
            var player_physics = player.GetComponent<PlayerPhysics>();
            if (player_physics != null)
            {
                if (speed_enabled)
                {
                    if (prev_speed <= 0f) // Only save original once
                    {
                        prev_speed = player_physics.Speed;
                    }

                    player_physics.Speed = prev_speed * 2f;
                }
                else
                {
                    if (prev_speed > 0f)
                    {
                        player_physics.Speed = prev_speed;
                        prev_speed = -1f; // Reset marker
                    }
                }
            }

            //fake scan
            if (scannerState != prevScannerState)
            {
                player.RpcSetScanner(scannerState);
                prevScannerState = scannerState;
                ModMain.LoggerInstance.LogInfo($"RPC scan method called: {scannerState}");
            }
        }

        private void HandleRoleChecking()
        {
            roleCheckTimer += Time.deltaTime;
            if (roleCheckTimer >= ModVars.RoleCheckInterval)
            {
                CheckImpostors();
                roleCheckTimer = 0f;
            }
        }

        private void HandleShadowQuad()
        {
            var cam = GameObject.Find("Main Camera");
            if (cam == null) return;

            var shadowQuad = cam.transform.Find("ShadowQuad");
            if (shadowQuad == null) return;

            var shadowObj = shadowQuad.gameObject;
            if (fullbright_enabled)
            {
                if (shadowObj.activeSelf)
                {
                    shadowObj.SetActive(false);
                    ModMain.LoggerInstance.LogInfo("[Among Us Menu] -> ShadowQuad disabled (fullbright enabled).");
                }
            }
            else
            {
                if (!shadowObj.activeSelf)
                {
                    shadowObj.SetActive(true);
                    ModMain.LoggerInstance.LogInfo("[Among Us Menu] -> ShadowQuad re-enabled (fullbright disabled).");
                }
            }
        }

        private void HandleChatVisibility()
        {
            var cam = GameObject.Find("Main Camera");
            if (cam == null) return;

            var chatUI = cam.transform.Find("Hud/ChatUi");
            if (chatAlways)
            {
                if (chatUI != null && !chatUI.gameObject.activeSelf)
                {
                    chatUI.gameObject.SetActive(true);
                }
            }
        }



        private void CheckImpostors()
        {
            impostorInfo.Clear();
            impostors.Clear();
            impostorNames.Clear();

            // for the meeting and chat impostors, dont remove this.
            //var impostors = new List<string>();

            foreach (var player in PlayerControl.AllPlayerControls)
            {
                string? playerName = player?.Data?.PlayerName;
                if (string.IsNullOrEmpty(playerName)) continue;

                string dataObjectName = playerName + "Data";
                GameObject? dataObject = GameObject.Find(dataObjectName);
                if (dataObject == null) continue;

                string? foundRole = null;
                for (int i = 0; i < dataObject.transform.childCount; i++)
                {
                    var child = dataObject.transform.GetChild(i);
                    if (child == null) continue;

                    string roleName = child.name;
                    if (roleName == ModVars.ImpostorRoleNames[0]) foundRole = "impostor";
                    else if (roleName == ModVars.ImpostorRoleNames[1]) foundRole = "shapeshifter";
                    else if (roleName == ModVars.ImpostorRoleNames[2]) foundRole = "phantom";
                    else if (roleName == ModVars.ImpostorRoleNames[3]) foundRole = "viper";

                    if (foundRole != null) break;
                }

                if (foundRole != null)
                {
                    string colorName = "unknown";
                    var info = dataObject.GetComponent<NetworkedPlayerInfo>();
                    if (info != null && !string.IsNullOrEmpty(info.ColorName))
                        colorName = info.ColorName;

                    impostorInfo.Add((foundRole, playerName, colorName.Replace("(", "").Replace(")", "")));
                    impostors.Add($" - {foundRole}, {playerName}, {colorName}");
                    impostorNames.Add(playerName);

                    var playerObj = GameObject.Find(playerName);
                    if (playerObj != null)
                    {
                        var nameTextTransform = playerObj.transform.Find("Names/NameText_TMP");
                        if (nameTextTransform != null)
                        {
                            var tmpText = nameTextTransform.GetComponent<TMPro.TMP_Text>();
                            if (tmpText != null)
                            {
                                tmpText.color = new Color(1f, 0f, 0f, 1f); // red
                            }
                            else
                            {
                                ModMain.LoggerInstance.LogWarning($"[Among Us Menu] -> TMP_Text not found on '{playerName}' text.");
                            }
                        }
                        else
                        {
                            ModMain.LoggerInstance.LogWarning($"[Among Us Menu] -> NameText_TMP not found under {playerName}.");
                        }
                    }
                    else
                    {
                        ModMain.LoggerInstance.LogWarning($"[Among Us Menu] -> Player GameObject '{playerName}' not found.");
                    }
                }
            }

            if (impostors.Count > 0)
            {
                ModMain.LoggerInstance.LogInfo("[Among Us Menu] -> Impostors:\n" + string.Join("\n", impostors));

                var impostorNames = impostors.Select(line =>
                {
                    var parts = line.Split(',');
                    return parts.Length > 1 ? parts[1].Trim() : null;
                })
                .Where(name => !string.IsNullOrEmpty(name))
                .Cast<string>()
                .ToList();

                HighlightMeetingImpostors(impostorNames);
            }
            else
            {
                ModMain.LoggerInstance.LogInfo("[Among Us Menu] -> No impostors found.");
            }
        }

        private void HighlightMeetingImpostors(List<string> impostorNames)
        {
            ModMain.LoggerInstance.LogInfo($"[Among Us Menu] -> HighlightMeetingImpostors called with {impostorNames.Count} impostor(s).");

            try
            {
                var cam = GameObject.Find("Main Camera");
                if (cam == null) return;

                var meetingHub = cam.transform.Find("Hud/MeetingHub(Clone)");
                if (meetingHub == null) return;

                var contents = meetingHub.Find("MeetingContents/ButtonStuff");
                if (contents == null) return;

                for (int i = 0; i < contents.childCount; i++)
                {
                    var voteArea = contents.GetChild(i);
                    if (voteArea == null || !voteArea.name.StartsWith("PlayerVoteArea")) continue;

                    var nameTextTransform = voteArea.Find("NameText");
                    if (nameTextTransform != null)
                    {
                        var tmpText = nameTextTransform.GetComponent<TMPro.TMP_Text>();
                        if (tmpText != null)
                        {
                            string displayedName = tmpText.text?.Trim();
                            if (!string.IsNullOrEmpty(displayedName) && impostorNames.Contains(displayedName))
                            {
                                tmpText.color = new Color(1f, 0f, 0f, 1f); // Red
                                ModMain.LoggerInstance.LogInfo($"[Among Us Menu] -> Highlighted impostor name in meeting: {displayedName}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ModMain.LoggerInstance.LogError($"[Among Us Menu] -> Exception in HighlightMeetingImpostors: {ex}");
            }
        }



        /*


        public static RoleBehaviour getBehaviourByRoleType(RoleTypes roleType)
        {
            return RoleManager.Instance.AllRoles.First(r => r.Role == roleType);
        }

        public static string getRoleName(NetworkedPlayerInfo playerData)
        {
            var translatedRole = DestroyableSingleton<TranslationController>.Instance.GetString(playerData.Role.StringName, Il2CppSystem.Array.Empty<Il2CppSystem.Object>());
            return translatedRole;
        }


        public static string getNameTag(NetworkedPlayerInfo playerInfo, string playerName, bool isChat = false)
        {
            string nameTag = playerName;

            if (!playerInfo.Role.IsNull() && !playerInfo.IsNull() && !playerInfo.Disconnected && !playerInfo.Object.CurrentOutfit.IsNull())
            {

                if (showImpostors)
                {

                    if (isChat)
                    {
                        nameTag = $"<color=#{ColorUtility.ToHtmlStringRGB(playerInfo.Role.TeamColor)}><size=70%>{getRoleName(playerInfo)}</size> {nameTag}</color>";
                        return nameTag;
                    }

                    nameTag = $"<color=#{ColorUtility.ToHtmlStringRGB(playerInfo.Role.TeamColor)}><size=70%>{getRoleName(playerInfo)}</size>\r\n{nameTag}</color>";

                }
                else if (PlayerControl.LocalPlayer.Data.Role.NameColor == playerInfo.Role.NameColor)
                {

                    if (isChat)
                    {
                        return nameTag;
                    }

                    nameTag = $"<color=#{ColorUtility.ToHtmlStringRGB(playerInfo.Role.NameColor)}>{nameTag}</color>";

                }
            }

            return nameTag;
        }


        */





        /*
        public void HighlightChatImpostors(List<string> impostorNames)
        {
            //ModMain.LoggerInstance.LogInfo($"[Among Us Menu] -> HighlightChatImpostors called with {impostorNames.Count} impostor(s).");
            
            try
            {
                var cam = GameObject.Find("Main Camera");
                if (cam == null)
                {
                    ModMain.LoggerInstance.LogWarning("[Among Us Menu] -> Main Camera not found.");
                    return;
                }

                var chatContainer = cam.transform.Find("Hud/ChatUi/ChatScreenRoot/ChatScreenContainer/Scroller/Items");
                if (chatContainer == null)
                {
                    ModMain.LoggerInstance.LogWarning("[Among Us Menu] -> Chat container not found.");
                    return;
                }

                for (int i = 0; i < chatContainer.childCount; i++)
                {
                    var bubble = chatContainer.GetChild(i);
                    if (bubble == null || !bubble.name.StartsWith("ChatBubble")) continue;

                    var nameTextTransform = bubble.Find("NameText (TMP)");
                    if (nameTextTransform == null)
                    {
                        ModMain.LoggerInstance.LogWarning($"[Among Us Menu] -> NameText (TMP) not found under bubble {bubble.name}.");
                        continue;
                    }
                   
                    var tmpText = nameTextTransform.GetComponent<TMPro.TMP_Text>();
                    if (tmpText == null)
                    {
                        ModMain.LoggerInstance.LogWarning($"[Among Us Menu] -> TMP_Text component missing in bubble {bubble.name}.");
                        continue;
                    }

                    string displayedName = tmpText.text?.Trim();
                    if (!string.IsNullOrEmpty(displayedName) && impostorNames.Contains(displayedName))
                    {
                        tmpText.color = new Color(1f, 0f, 0f, 1f); // Red
                    }
                }
            }
            catch (Exception ex)
            {
                ModMain.LoggerInstance.LogError($"[Among Us Menu] -> Exception in HighlightChatImpostors: {ex}");
            }
        }

        */














        private void HandleKeybinds()
        {
            HandleKey(ModVars.Numpad1, ref numpad1HeldTime, ModVars.HoldDuration, () =>
            {
                var player = PlayerControl.LocalPlayer;
                if (player != null)
                {
                    ModMain.LoggerInstance.LogInfo("[Among Us Menu] -> RpcSetColor(18)");
                    player.RpcSetColor(18);
                }
            });

            HandleKey(ModVars.Numpad4, ref numpad4HeldTime, ModVars.HoldDuration, () =>
            {
                var player = PlayerControl.LocalPlayer;
                if (player != null)
                {
                    ModMain.LoggerInstance.LogInfo("[Among Us Menu] -> SetColor(18)");
                    player.SetColor(18);
                }
            });

            HandleKey(ModVars.Numpad7, ref numpad7HeldTime, ModVars.HoldDuration, () =>
            {
                var player = PlayerControl.LocalPlayer;
                if (player != null)
                {
                    scannerState = !scannerState;
                    ModMain.LoggerInstance.LogInfo($"[Among Us Menu] -> RpcSetScanner({scannerState.ToString().ToLowerInvariant()})");
                    player.RpcSetScanner(scannerState);
                }
            });

            HandleKey(ModVars.Numpad8, ref numpad8HeldTime, ModVars.HoldDuration, () =>
            {
                var player = PlayerControl.LocalPlayer;
                if (player != null)
                {
                    var collider = player.GetComponent<CircleCollider2D>();
                    if (collider != null)
                    {
                        noclipEnabled = !noclipEnabled;
                        collider.enabled = !noclipEnabled;
                        ModMain.LoggerInstance.LogInfo($"[Among Us Menu] -> Noclip {(noclipEnabled ? "enabled" : "disabled")}");
                    }
                    else
                    {
                        ModMain.LoggerInstance.LogWarning("[Among Us Menu] -> CircleCollider2D not found!");
                    }
                }
            });

            HandleKey(ModVars.Numpad5, ref numpad5HeldTime, ModVars.HoldDuration, () =>
            {
                fullbright_enabled = !fullbright_enabled;
                if (fullbright_enabled)
                    ModMain.LoggerInstance.LogInfo("[Among Us Menu] -> fullbright enabled!");
                else
                    ModMain.LoggerInstance.LogInfo("[Among Us Menu] -> fullbright disabled!");
            });

            HandleKey(ModVars.Numpad2, ref numpad2HeldTime, ModVars.HoldDuration, () =>
            {
                var player = PlayerControl.LocalPlayer;
                if (player == null) return;

                var player_physics = player.GetComponent<PlayerPhysics>();
                if (player_physics == null) return;

                if (!speed_enabled)
                {
                    prev_speed = player_physics.Speed;
                    player_physics.Speed = player_physics.Speed * 1.67f;
                    ModMain.LoggerInstance.LogInfo("[Among Us Menu] -> Speed enabled");
                }
                else
                {
                    player_physics.Speed = prev_speed;
                    ModMain.LoggerInstance.LogInfo("[Among Us Menu] -> Speed disabled");
                }

                speed_enabled = !speed_enabled;
            });

            HandleKey(ModVars.Numpad9, ref numpad9HeldTime, ModVars.HoldDurationShort, () =>
            {
                var player = PlayerControl.LocalPlayer;
                if (player != null)
                {
                    ModMain.LoggerInstance.LogInfo("[Among Us Menu] -> Forcing role: Shapeshifter (self)");
                    player.RpcSetRole(AmongUs.GameOptions.RoleTypes.Shapeshifter, false);
                }
                else
                {
                    ModMain.LoggerInstance.LogWarning("[Among Us Menu] -> LocalPlayer not found.");
                }

                PlayerControl? target = null;
                foreach (var p in PlayerControl.AllPlayerControls)
                {
                    if (p?.Data?.PlayerName == "idk")
                    {
                        target = p;
                        break;
                    }
                }

                if (target != null)
                {
                    ModMain.LoggerInstance.LogInfo("[Among Us Menu] -> Forcing role: Shapeshifter for player 'idk'");
                    target.RpcSetRole(AmongUs.GameOptions.RoleTypes.Shapeshifter, false);
                }
                else
                {
                    ModMain.LoggerInstance.LogWarning("[Among Us Menu] -> Player 'idk' not found.");
                }
            });

            HandleKey(ModVars.Numpad6, ref numpad6HeldTime, ModVars.HoldDurationShort, () =>
            {
                PlayerControl.LocalPlayer.CmdReportDeadBody(null);
            });

            HandleKey(ModVars.Numpad3, ref numpad3HeldTime, ModVars.HoldDuration, () =>
            {
                var cam = GameObject.Find("Main Camera");
                if (cam != null)
                {
                    var hudTransform = cam.transform.Find("Hud/MeetingHub(Clone)");
                    if (hudTransform != null)
                    {
                        var meetingHud = hudTransform.GetComponent<MeetingHud>();
                        if (meetingHud != null)
                        {
                            meetingHud.Close();
                            ModMain.LoggerInstance.LogInfo("[Among Us Menu] -> Closed meeting early via Numpad3.");
                        }
                        else
                        {
                            ModMain.LoggerInstance.LogWarning("[Among Us Menu] -> MeetingHud component not found on MeetingHub(Clone).");
                        }
                    }
                    else
                    {
                        ModMain.LoggerInstance.LogWarning("[Among Us Menu] -> MeetingHub(Clone) not found under Main Camera > Hud.");
                    }
                }
                else
                {
                    ModMain.LoggerInstance.LogWarning("[Among Us Menu] -> Main Camera not found.");
                }
            });
        }

        private void HandleKey(int keyCode, ref float heldTime, float activation_time, Action action)
        {
            if ((WindowsInterop.GetAsyncKeyState(keyCode) & 0x8000) != 0)
            {
                heldTime += Time.deltaTime;
                if (heldTime >= activation_time)
                {
                    action?.Invoke();
                    heldTime = 0f;
                }
            }
            else
            {
                heldTime = 0f;
            }
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WindowsInterop.WM_MOUSEWHEEL)
            {
                var hookStruct = Marshal.PtrToStructure<WindowsInterop.MSLLHOOKSTRUCT>(lParam);
                int delta = (short)((hookStruct.mouseData >> 16) & 0xffff);

                if ((WindowsInterop.GetAsyncKeyState(WindowsInterop.VK_SHIFT) & 0x8000) != 0)
                {
                    ModVars.ZoomLevel -= Mathf.Sign(delta) * 1f; // Scroll up = zoom out
                    ModVars.ZoomLevel = Mathf.Clamp(ModVars.ZoomLevel, 3f, 20f);
                    ModVars.ZoomUsed = true;
                }
            }

            return WindowsInterop.CallNextHookEx(hookId, nCode, wParam, lParam);
        }

    }
}
