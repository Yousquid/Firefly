using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Firefly.Editor
{
    /// <summary>
    /// Exercises the actual Input System and player loop in the already open editor.
    /// The temporary gamepad and all changed editor/runtime settings are restored.
    /// </summary>
    [InitializeOnLoad]
    public static class FireflySmokeCheck
    {
        const string ScenePath = "Assets/Firefly/Scenes/NightMeadow.unity";
        const string ActiveKey = "Firefly.Smoke.Active";
        const string StageKey = "Firefly.Smoke.Stage";
        const string ReportKey = "Firefly.Smoke.Report";
        const string SetupKey = "Firefly.Smoke.SceneSetup";
        const string StartKey = "Firefly.Smoke.StartTime";
        const string PauseKey = "Firefly.Smoke.EditorPaused";
        const string RestoreScenesKey = "Firefly.Smoke.RestoreScenes";

        [Serializable]
        public sealed class Check
        {
            public string name;
            public bool passed;
            public string detail;
        }

        [Serializable]
        public sealed class Report
        {
            public string status = "running";
            public bool passed;
            public bool captureReady;
            public string startedUtc;
            public double elapsedSeconds;
            public string unityVersion;
            public string scene = ScenePath;
            public int initializedAudioClips;
            public int nearbyPointLightsWhenLit;
            public int nearbyPointLightsWhenDark;
            public float playerPointIntensityWhenLit;
            public float playerPointIntensityWhenDark;
            public int respondingPointLights;
            public float collectiveLightEnergy;
            public float ambientLightEnergy;
            public float playerViewportHeight;
            public float darkFrameMeanLuminance;
            public float collectiveFrameMeanLuminance;
            public string lightScreenshot;
            public string darkScreenshot;
            public string collectiveScreenshot;
            public string screenshotMethod;
            public List<Check> checks = new List<Check>();
            public List<string> errors = new List<string>();
        }

        [Serializable]
        sealed class SavedSceneSetup
        {
            public SceneSetup[] scenes;
        }

        sealed class Hold
        {
            public readonly double seconds;
            public readonly GamepadState state;
            public readonly KeyboardState keyboardState;
            public Hold(double seconds, GamepadState state, KeyboardState keyboardState = default)
            {
                this.seconds = seconds;
                this.state = state;
                this.keyboardState = keyboardState;
            }
        }

        static Report report;
        static IEnumerator routine;
        static Hold hold;
        static double holdStarted;
        static int holdFrames;
        static int lastFrame = -1;
        static Gamepad pad;
        static Gamepad previousGamepad;
        static Keyboard keyboard;
        static Keyboard previousKeyboard;
        static InputSettings.UpdateMode previousUpdateMode;
        static InputSettings.BackgroundBehavior previousBackgroundBehavior;
        static InputSettings.EditorInputBehaviorInPlayMode previousEditorInputBehavior;
        static float previousTimeScale;
        static bool previousAudioListenerPause;
        static bool previousRunInBackground;
        static bool runtimeSettingsChanged;
        static FlightInput input;
        static FireflyPilot pilot;
        static FireflyGlow glow;
        static MeadowCamera meadowCamera;
        static FireflySwarm swarm;
        static GameObject cameraObstacle;
        static GameObject flightObstacle;
        static int glowPresses;
        static int swarmAnswers;

        public static bool IsRunning => SessionState.GetBool(ActiveKey, false);
        public static string ResultPath => Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/FireflyValidation.json"));

        static FireflySmokeCheck()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeAssemblyReload;
            Application.logMessageReceived += OnLog;
            if (IsRunning)
            {
                ReadSavedReport();
                EditorApplication.delayCall += ResumeAfterReload;
            }
        }

        [MenuItem("Firefly/Validate Gamepad Flight")]
        public static void Begin()
        {
            if (IsRunning)
                return;

            SessionState.EraseString(StartKey);
            report = new Report
            {
                startedUtc = DateTime.UtcNow.ToString("o"),
                unityVersion = Application.unityVersion
            };

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Reject("Validation requires edit mode; the current play session was left untouched.");
                return;
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                Reject("Wait for script compilation and asset import before starting validation.");
                return;
            }
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                Reject("The NightMeadow scene has not been generated yet.");
                return;
            }

            var setup = EditorSceneManager.GetSceneManagerSetup();
            bool alreadyOpen = SceneManager.GetActiveScene().path == ScenePath && SceneManager.sceneCount == 1;
            if (!alreadyOpen)
            {
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    if (SceneManager.GetSceneAt(i).isDirty)
                    {
                        Reject("Save the currently edited scene before validating another scene; no edits were changed.");
                        return;
                    }
                }
            }

            SessionState.SetString(SetupKey, JsonUtility.ToJson(new SavedSceneSetup { scenes = setup }));
            SessionState.SetBool(RestoreScenesKey, !alreadyOpen);
            SessionState.SetBool(PauseKey, EditorApplication.isPaused);
            SessionState.SetString(StartKey, EditorApplication.timeSinceStartup.ToString("R", CultureInfo.InvariantCulture));
            SessionState.SetString(StageKey, "entering");
            SessionState.SetBool(ActiveKey, true);
            SaveReport();
            try
            {
                if (!alreadyOpen)
                    EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                EditorApplication.isPaused = false;
                EditorApplication.EnterPlaymode();
            }
            catch (Exception exception)
            {
                Fail(exception.ToString());
                RestoreEditor();
            }
        }

        static void Reject(string reason)
        {
            report.errors.Add(reason);
            report.status = "failed";
            SaveReport();
        }

        static void ResumeAfterReload()
        {
            if (!IsRunning)
                return;
            if (SessionState.GetString(StageKey, "") == "stopping")
            {
                if (EditorApplication.isPlaying)
                    EditorApplication.ExitPlaymode();
                else if (!EditorApplication.isPlayingOrWillChangePlaymode)
                    RestoreEditor();
            }
            else if (EditorApplication.isPlaying && routine == null)
                StartRuntime();
        }

        static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (!IsRunning)
                return;
            if (state == PlayModeStateChange.EnteredPlayMode)
                StartRuntime();
            else if (state == PlayModeStateChange.ExitingPlayMode)
            {
                if (SessionState.GetString(StageKey, "") != "stopping")
                    Fail("The play session ended before validation finished.");
                CleanupRuntime();
                SessionState.SetString(StageKey, "stopping");
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
                RestoreEditor();
        }

        static void StartRuntime()
        {
            if (routine != null || SessionState.GetString(StageKey, "") == "stopping")
                return;
            try
            {
                ReadSavedReport();
                SessionState.SetString(StageKey, "running");
                previousGamepad = Gamepad.current;
                previousKeyboard = Keyboard.current;
                previousUpdateMode = InputSystem.settings.updateMode;
                previousBackgroundBehavior = InputSystem.settings.backgroundBehavior;
                previousEditorInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
                previousTimeScale = Time.timeScale;
                previousAudioListenerPause = AudioListener.pause;
                previousRunInBackground = Application.runInBackground;
                runtimeSettingsChanged = true;
                InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                Application.runInBackground = true;
                Time.timeScale = 1f;
                pad = InputSystem.AddDevice<Gamepad>("FireflySmokeGamepad");
                pad.MakeCurrent();
                keyboard = InputSystem.AddDevice<Keyboard>("FireflySmokeKeyboard");
                keyboard.MakeCurrent();
                routine = RunChecks();
                Advance();
            }
            catch (Exception exception)
            {
                Fail(exception.ToString());
                StopPlay();
            }
        }

        static void Update()
        {
            if (!IsRunning)
                return;
            double started;
            if (double.TryParse(SessionState.GetString(StartKey, "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out started)
                && EditorApplication.timeSinceStartup - started > 75d)
            {
                Fail("Validation timed out after 75 seconds.");
                StopPlay();
                return;
            }
            if (!EditorApplication.isPlaying || routine == null || hold == null)
                return;

            try
            {
                EditorApplication.QueuePlayerLoopUpdate();
                // One explicit Input System update per player frame keeps button edges valid.
                if (Time.frameCount != lastFrame)
                {
                    lastFrame = Time.frameCount;
                    holdFrames++;
                    if (pad != null && pad.added)
                        InputSystem.QueueStateEvent(pad, hold.state);
                    if (keyboard != null && keyboard.added)
                        InputSystem.QueueStateEvent(keyboard, hold.keyboardState);
                    InputSystem.Update();
                }
                if (EditorApplication.timeSinceStartup - holdStarted >= hold.seconds && holdFrames >= 3)
                    Advance();
            }
            catch (Exception exception)
            {
                Fail(exception.ToString());
                StopPlay();
            }
        }

        static void Advance()
        {
            if (routine.MoveNext())
            {
                hold = (Hold)routine.Current;
                holdStarted = EditorApplication.timeSinceStartup;
                holdFrames = 0;
                lastFrame = -1;
            }
            else
                StopPlay();
        }

        static IEnumerator RunChecks()
        {
            yield return Neutral(0.7);
            input = Object.FindFirstObjectByType<FlightInput>();
            pilot = Object.FindFirstObjectByType<FireflyPilot>();
            glow = Object.FindFirstObjectByType<FireflyGlow>();
            meadowCamera = Object.FindFirstObjectByType<MeadowCamera>();
            swarm = Object.FindFirstObjectByType<FireflySwarm>();
            bool present = input != null && pilot != null && glow != null && meadowCamera != null;
            Record("Runtime scene components", present, "FlightInput, FireflyPilot, FireflyGlow and MeadowCamera must exist.");
            if (!present)
                throw new InvalidOperationException("Missing a required runtime component.");

            // Editor smoke tests can run behind Codex. Simulate game-view focus explicitly;
            // the shipping input policy still pauses on genuine application focus loss.
            input.SendMessage("OnApplicationFocus", true, SendMessageOptions.DontRequireReceiver);
            input.SetPaused(false);
            input.GlowPressed += CountGlowPress;
            if (swarm != null)
                swarm.ClearingAnswered += CountSwarmAnswer;
            var clipNames = new List<string>();
            foreach (var source in Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
            {
                if (source.clip != null && source.clip.samples > 0)
                    clipNames.Add(source.clip.name);
            }
            report.initializedAudioClips = clipNames.Count;
            Record("Soundscape clips initialized", clipNames.Count >= 3,
                "Initialized " + clipNames.Count + " clips: " + string.Join(", ", clipNames.ToArray()) + ".");
            Record("Ambient fireflies have real lights", swarm != null && swarm.ActivePointLightCount >= 4
                && SwarmEnabledPointLightCount() == swarm.ActivePointLightCount,
                "Nearby insect billboards must be backed by several enabled Unity point lights.");
            Vector3 center = new Vector3(0f, pilot.FlightHeight, 0f);
            float initialYaw = meadowCamera.Yaw;
            float initialPitch = meadowCamera.Pitch;
            pilot.Teleport(center);
            meadowCamera.SnapToTarget();
            yield return Neutral(0.25);
            Vector3 before = pilot.transform.position;
            yield return Stick(0.4, new Vector2(0.06f, -0.04f));
            Record("Gamepad deadzone", input.Move.sqrMagnitude < 0.00001f && HorizontalDistance(before, pilot.transform.position) < 0.03f,
                "A small physical stick offset must neither produce movement input nor drift.");

            pilot.Teleport(center);
            yield return Stick(0.45, new Vector2(0f, 0.5f));
            Record("Analog movement", input.Move.magnitude > 0.1f && input.Move.magnitude < 0.9f
                && HorizontalDistance(center, pilot.transform.position) > 0.05f && input.LastInputWasGamepad,
                "Half stick must produce analog gamepad movement.");

            pilot.Teleport(center);
            yield return Stick(0.8, Vector2.up);
            float cardinalSpeed = HorizontalMagnitude(pilot.Velocity);
            Vector3 forward = meadowCamera.transform.forward;
            forward.y = 0f;
            forward.Normalize();
            Record("Forward flight", Vector3.Dot(pilot.transform.position - center, forward) > 0.15f && cardinalSpeed > 0.5f,
                "Up on the stick must fly forward relative to the camera.");

            pilot.Teleport(center);
            yield return Stick(0.8, new Vector2(1f, 1f));
            float diagonalSpeed = HorizontalMagnitude(pilot.Velocity);
            Record("Diagonal speed limit", input.Move.magnitude <= 1.001f && diagonalSpeed <= cardinalSpeed * 1.08f
                && diagonalSpeed >= cardinalSpeed * 0.85f,
                "Full diagonal speed " + diagonalSpeed.ToString("F3") + " versus cardinal " + cardinalSpeed.ToString("F3") + ".");

            Vector2 altitudeBounds = pilot.AltitudeBounds;
            Vector3 verticalCenter = new Vector3(center.x, Mathf.Clamp(center.y + 2f, altitudeBounds.x + 1f, altitudeBounds.y - 1f), center.z);
            pilot.Teleport(verticalCenter);
            yield return new Hold(0.65, new GamepadState { rightTrigger = 1f });
            Record("RT ascends", input.Elevation > 0.8f && pilot.transform.position.y > verticalCenter.y + 0.4f
                && HorizontalDistance(verticalCenter, pilot.transform.position) < 0.03f,
                "Right trigger alone must fly upward without horizontal drift.");
            pilot.Teleport(verticalCenter);
            yield return new Hold(0.65, new GamepadState { leftTrigger = 1f });
            Record("LT descends", input.Elevation < -0.8f && pilot.transform.position.y < verticalCenter.y - 0.4f,
                "Left trigger alone must fly downward.");
            pilot.Teleport(verticalCenter);
            yield return new Hold(0.45, new GamepadState { leftTrigger = 1f, rightTrigger = 1f });
            Record("Opposite triggers cancel", Mathf.Abs(input.Elevation) < 0.001f
                && Vector3.Distance(verticalCenter, pilot.transform.position) < 0.03f,
                "Simultaneous fully held triggers must produce zero vertical intent.");

            pilot.Teleport(verticalCenter);
            yield return Keys(0.5, Key.Space);
            bool spaceAscends = input.Elevation > 0.8f && pilot.transform.position.y > verticalCenter.y + 0.25f;
            pilot.Teleport(verticalCenter);
            yield return Keys(0.5, Key.LeftCtrl);
            Record("Keyboard vertical fallback", spaceAscends && input.Elevation < -0.8f
                && pilot.transform.position.y < verticalCenter.y - 0.25f,
                "Synthetic Keyboard Space and Left Ctrl must raise and lower the same flight controller.");

            pilot.Teleport(verticalCenter);
            yield return new Hold(0.8, new GamepadState { leftStick = Vector2.one, rightTrigger = 1f });
            float spatialSpeed = pilot.Velocity.magnitude;
            Record("Three dimensional speed limit", spatialSpeed <= cardinalSpeed * 1.08f && spatialSpeed >= cardinalSpeed * 0.85f
                && pilot.Velocity.y > 0.2f,
                "Combined horizontal diagonal and ascent speed " + spatialSpeed.ToString("F3")
                + " versus cardinal " + cardinalSpeed.ToString("F3") + "; the full xyz vector must be normalized.");

            pilot.Teleport(new Vector3(center.x, altitudeBounds.y - 0.05f, center.z));
            yield return new Hold(0.45, new GamepadState { rightTrigger = 1f });
            bool ceilingBounded = pilot.transform.position.y <= altitudeBounds.y + 0.001f;
            pilot.Teleport(new Vector3(center.x, altitudeBounds.x + 0.05f, center.z));
            yield return new Hold(0.45, new GamepadState { leftTrigger = 1f });
            Record("Altitude boundaries", ceilingBounded && pilot.transform.position.y >= altitudeBounds.x - 0.001f,
                "Sustained vertical input must respect altitude bounds " + altitudeBounds + ".");

            pilot.Teleport(verticalCenter);
            flightObstacle = MakeHiddenObstacle("Temporary flight ceiling", verticalCenter + Vector3.up * 0.95f, new Vector3(2f, 0.25f, 2f));
            yield return new Hold(0.65, new GamepadState { rightTrigger = 1f });
            float underside = flightObstacle.transform.position.y - 0.125f;
            Record("Upward surface collision", pilot.transform.position.y <= underside - pilot.CollisionRadius + 0.015f
                && pilot.transform.position.y > verticalCenter.y + 0.05f,
                "Ascending must stop below a ceiling, including the player's sphere radius.");
            Object.Destroy(flightObstacle);
            flightObstacle = null;
            yield return Neutral(0.18);
            pilot.Teleport(verticalCenter);
            flightObstacle = MakeHiddenObstacle("Temporary flight floor", verticalCenter - Vector3.up * 0.95f, new Vector3(2f, 0.25f, 2f));
            yield return new Hold(0.65, new GamepadState { leftTrigger = 1f });
            float upperSurface = flightObstacle.transform.position.y + 0.125f;
            Record("Downward surface collision", pilot.transform.position.y >= upperSurface + pilot.CollisionRadius - 0.015f
                && pilot.transform.position.y < verticalCenter.y - 0.05f,
                "Descending must stop above a floor; upward normals cannot be ignored in three dimensional flight.");
            Object.Destroy(flightObstacle);
            flightObstacle = null;
            yield return Neutral(0.18);

            Vector3 right = meadowCamera.transform.right;
            right.y = 0f;
            right.Normalize();
            pilot.Teleport(center);
            yield return Stick(0.45, Vector2.right);
            bool rightMoves = Vector3.Dot(pilot.transform.position - center, right) > 0.1f;
            pilot.Teleport(center);
            yield return Stick(0.45, Vector2.left);
            bool leftMoves = Vector3.Dot(pilot.transform.position - center, right) < -0.1f;
            pilot.Teleport(center);
            yield return Stick(0.45, Vector2.down);
            bool backwardMoves = Vector3.Dot(pilot.transform.position - center, forward) < -0.1f;
            Record("Four flight directions", rightMoves && leftMoves && backwardMoves,
                "Right, left and backward stick input must move on the camera's ground plane.");

            pilot.Teleport(center);
            meadowCamera.SnapToTarget();
            Vector3 cameraBefore = meadowCamera.transform.position;
            Vector3 originalOffset = cameraBefore - center;
            yield return Stick(0.9, Vector2.right);
            yield return Neutral(0.7);
            Vector3 currentOffset = meadowCamera.transform.position - pilot.transform.position;
            Record("Camera follows player", meadowCamera.Target == pilot.transform
                && HorizontalDistance(cameraBefore, meadowCamera.transform.position) > 0.35f
                && HorizontalMagnitude(currentOffset - originalOffset) < 0.6f,
                "The camera must follow horizontal movement while preserving its view offset.");

            pilot.Teleport(center);
            meadowCamera.SnapToTarget();
            Vector3 focus = meadowCamera.FocusPoint;
            float originalCameraDistance = Vector3.Distance(focus, meadowCamera.transform.position);
            cameraObstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cameraObstacle.name = "Temporary smoke-test camera obstruction";
            cameraObstacle.hideFlags = HideFlags.HideAndDontSave;
            cameraObstacle.transform.position = Vector3.Lerp(focus, meadowCamera.transform.position, 0.55f);
            cameraObstacle.transform.localScale = Vector3.one * 0.9f;
            cameraObstacle.GetComponent<Renderer>().enabled = false;
            Physics.SyncTransforms();
            meadowCamera.SnapToTarget();
            Record("Camera obstruction", Vector3.Distance(focus, meadowCamera.transform.position) < originalCameraDistance * 0.8f,
                "A temporary obstacle between the target and camera must shorten the view distance.");
            Object.Destroy(cameraObstacle);
            cameraObstacle = null;
            yield return Neutral(0.2);
            meadowCamera.SnapToTarget();

            float yawBefore = meadowCamera.Yaw;
            yield return new Hold(0.55, new GamepadState { rightStick = Vector2.right });
            bool changedYaw = Mathf.Abs(Mathf.DeltaAngle(yawBefore, meadowCamera.Yaw)) > 5f;
            float pitchBefore = meadowCamera.Pitch;
            yield return new Hold(0.35, new GamepadState { rightStick = Vector2.up });
            Record("Right stick camera orbit", changedYaw && Mathf.Abs(meadowCamera.Pitch - pitchBefore) > 3f
                && Vector3.Distance(center, pilot.transform.position) < 0.03f,
                "Right stick must change camera yaw and pitch while an idle player remains still.");
            Vector3 orbitForward = meadowCamera.transform.forward;
            orbitForward.y = 0f;
            orbitForward.Normalize();
            pilot.Teleport(center);
            yield return Stick(0.5, Vector2.up);
            Record("Flight follows orbit orientation", Vector3.Dot(pilot.transform.position - center, orbitForward) > 0.2f,
                "Forward flight must follow the camera's new horizontal heading after orbiting.");
            pilot.Teleport(center);
            meadowCamera.SetOrbit(initialYaw, initialPitch, true);
            yield return Neutral(0.3);
            Camera gameCamera = meadowCamera.GetComponent<Camera>();
            report.playerViewportHeight = PlayerViewportHeight(gameCamera);
            Record("Distant camera and tiny player", meadowCamera.OrbitDistance >= 14f && meadowCamera.CurrentDistance >= 12f
                && report.playerViewportHeight > 0f && report.playerViewportHeight < 0.04f,
                "Player body projected height is " + (report.playerViewportHeight * 100f).ToString("F2")
                + "% of the viewport; unobstructed camera distance is " + meadowCamera.CurrentDistance.ToString("F2") + ".");

            glow.SetLit(true);
            yield return Neutral(0.35);
            bool initialLit = glow.IsLit;
            FireflyModel playerModel = pilot.GetComponentInChildren<FireflyModel>();
            Light playerPointLight = playerModel != null ? playerModel.GlowLight : null;
            report.playerPointIntensityWhenLit = playerPointLight != null ? playerPointLight.intensity : -1f;
            report.nearbyPointLightsWhenLit = NearbyPointLightCount();
            glowPresses = 0;
            yield return Button(0.18, GamepadButton.South);
            bool firstChanged = glow.IsLit != initialLit;
            yield return Button(0.55, GamepadButton.South);
            Record("A press toggles once", firstChanged && glow.IsLit != initialLit && glowPresses == 1,
                "Holding South/A must emit one glow press and keep the new light state.");
            report.nearbyPointLightsWhenDark = NearbyPointLightCount();
            report.playerPointIntensityWhenDark = playerPointLight != null ? playerPointLight.intensity : -1f;
            bool playerLightDisabled = playerPointLight != null && !playerPointLight.enabled && playerPointLight.intensity < 0.001f;
            yield return Neutral(0.18);
            yield return Button(0.18, GamepadButton.South);
            Record("A release and second press", glow.IsLit == initialLit && glowPresses == 2,
                "A fresh press after release must toggle the glow again.");
            yield return Neutral(0.35);
            Record("Player glow is a small local light", playerLightDisabled && playerPointLight.enabled
                && report.playerPointIntensityWhenLit > 0.01f && report.playerPointIntensityWhenLit <= 0.4f
                && playerPointLight.range <= 2f && playerPointLight.intensity > 0.01f,
                "Player-only point intensity " + report.playerPointIntensityWhenLit.ToString("F3") + " lit, "
                + report.playerPointIntensityWhenDark.ToString("F3") + " dark; range "
                + (playerPointLight != null ? playerPointLight.range.ToString("F2") : "missing")
                + ". Other insects' independently flashing lights are excluded from this check.");

            yield return new Hold(0.2, new GamepadState { leftStick = Vector2.right }.WithButton(GamepadButton.Start));
            Vector3 pausedPosition = pilot.transform.position;
            float pausedYaw = meadowCamera.Yaw;
            bool wasPaused = input.IsPaused;
            yield return new Hold(0.45, new GamepadState { leftStick = Vector2.right, rightTrigger = 1f, rightStick = Vector2.right });
            Record("Start pauses flight", wasPaused && input.IsPaused && Mathf.Approximately(Time.timeScale, 0f)
                && Vector3.Distance(pausedPosition, pilot.transform.position) < 0.01f
                && Mathf.Abs(Mathf.DeltaAngle(pausedYaw, meadowCamera.Yaw)) < 0.01f,
                "Start must freeze three dimensional flight and camera orbit despite held stick/trigger input.");
            yield return new Hold(0.2, new GamepadState { leftStick = Vector2.right }.WithButton(GamepadButton.Start));
            yield return Stick(0.4, Vector2.right);
            Record("Start resumes flight", !input.IsPaused && Time.timeScale > 0f
                && HorizontalDistance(pausedPosition, pilot.transform.position) > 0.04f,
                "A second Start press must restore time and movement.");

            pilot.Teleport(center);
            yield return new Hold(0.7, new GamepadState { leftStick = Vector2.right }.WithButton(GamepadButton.RightShoulder));
            Record("RB gentle boost", input.BoostHeld && pilot.IsBoosting && HorizontalMagnitude(pilot.Velocity) > cardinalSpeed * 1.2f,
                "Right shoulder with stick input must increase flight speed.");
            yield return new Hold(0.2, new GamepadState { leftStick = Vector2.right, rightTrigger = 1f, rightStick = Vector2.right }.WithButton(GamepadButton.RightShoulder));
            InputSystem.RemoveDevice(pad);
            pad = null;
            yield return Neutral(0.5);
            Record("Gamepad disconnection", input.Move.sqrMagnitude < 0.00001f && Mathf.Abs(input.Elevation) < 0.001f
                && input.Look.sqrMagnitude < 0.00001f && !input.BoostHeld && pilot.Velocity.sqrMagnitude < 0.00001f,
                "Removing the active gamepad must clear horizontal/vertical/orbit/boost input and stop flight.");

            pad = InputSystem.AddDevice<Gamepad>("FireflySmokeGamepad");
            pad.MakeCurrent();
            yield return Neutral(0.25);
            meadowCamera.SetOrbit(initialYaw, initialPitch, true);
            pilot.Teleport(new Vector3(16.9f, pilot.FlightHeight, 14.9f));
            yield return Stick(0.7, new Vector2(1f, 1f));
            Vector3 upper = pilot.transform.position;
            pilot.Teleport(new Vector3(-16.9f, pilot.FlightHeight, -11.9f));
            yield return Stick(0.7, new Vector2(-1f, -1f));
            Vector3 lower = pilot.transform.position;
            Record("Meadow flight boundaries", upper.x <= 17.001f && upper.z <= 15.001f
                && lower.x >= -17.001f && lower.z >= -12.001f,
                "Continued outward flight must remain within x [-17, 17] and z [-12, 15].");

            input.SendMessage("OnApplicationFocus", false, SendMessageOptions.DontRequireReceiver);
            yield return Stick(0.2, Vector2.right);
            Record("Focus loss pauses safely", !input.HasFocus && input.IsPaused && input.Move.sqrMagnitude < 0.00001f
                && Mathf.Abs(input.Elevation) < 0.001f && input.Look.sqrMagnitude < 0.00001f
                && pilot.Velocity.sqrMagnitude < 0.00001f,
                "A simulated application focus loss must pause and clear flight input.");
            input.SendMessage("OnApplicationFocus", true, SendMessageOptions.DontRequireReceiver);

            input.SetPaused(false);
            glow.SetLit(false);
            yield return Neutral(0.35);
            pilot.Teleport(new Vector3(-8f, 1.3f, -1f));
            meadowCamera.SetOrbit(initialYaw, initialPitch, true);
            meadowCamera.SnapToTarget();
            yield return Neutral(0.2);
            Record("Dark clearing baseline", swarm != null && !swarm.IsResponding && swarm.ResponseLightEnergy < 0.001f,
                "The comparison frame must precede the first collective response, with the player glow switched off.");
            report.darkScreenshot = CaptureScreenshot("NightMeadowDark.png");
            yield return Neutral(0.35);
            report.darkFrameMeanLuminance = ScreenshotMeanLuminance(report.darkScreenshot);
            int previouslyDiscovered = swarm != null ? swarm.DiscoveredCount : 0;
            swarmAnswers = 0;
            glow.SetLit(true);
            yield return Neutral(1.8);
            Record("Clearing answers the player", swarm != null && swarm.IsResponding && swarmAnswers == 1
                && swarm.DiscoveredCount > previouslyDiscovered,
                "Lighting the abdomen at the first clearing must trigger one new discovered group and its response event.");

            report.respondingPointLights = swarm != null ? swarm.RespondingPointLightCount : 0;
            report.collectiveLightEnergy = swarm != null ? swarm.ResponseLightEnergy : 0f;
            report.ambientLightEnergy = swarm != null ? swarm.AmbientLightEnergy : 0f;
            Record("Collective response lights the environment", swarm != null && swarm.RespondingPointLightCount > 6
                && SwarmEnabledPointLightCount() == swarm.ActivePointLightCount
                && swarm.ResponseLightEnergy > 3f && swarm.ResponseLightEnergy > swarm.AmbientLightEnergy,
                "Responding real point lights " + report.respondingPointLights + ", response intensity sum "
                + report.collectiveLightEnergy.ToString("F3") + ", ambient intensity sum "
                + report.ambientLightEnergy.ToString("F3") + ". The group must provide actual Unity lighting beyond billboard emission.");

            glow.SetLit(true);
            meadowCamera.SnapToTarget();
            yield return Neutral(0.8);
            Record("Light reaches visible brightness", glow.IsLit && glow.Brightness > 0.8f,
                "The smooth glow transition must settle into its visible state.");
            report.captureReady = true;
            report.lightScreenshot = CaptureScreenshot("NightMeadow.png");
            yield return Neutral(0.35);
            report.collectiveFrameMeanLuminance = ScreenshotMeanLuminance(report.lightScreenshot);
            if (File.Exists(report.lightScreenshot))
            {
                report.collectiveScreenshot = Path.Combine(Path.GetDirectoryName(report.lightScreenshot), "NightMeadowCollective.png");
                File.Copy(report.lightScreenshot, report.collectiveScreenshot, true);
            }
            Record("Collective frame brightens the meadow", report.darkFrameMeanLuminance > 0f
                && report.collectiveFrameMeanLuminance > report.darkFrameMeanLuminance * 1.02f,
                "Same camera framing: dark PNG mean luminance " + report.darkFrameMeanLuminance.ToString("F5")
                + ", collective response " + report.collectiveFrameMeanLuminance.ToString("F5") + ".");
            SaveReport();
            yield return Neutral(3.0);
        }

        static Hold Neutral(double seconds) => new Hold(seconds, new GamepadState());
        static Hold Stick(double seconds, Vector2 value) => new Hold(seconds, new GamepadState { leftStick = value });
        static Hold Button(double seconds, GamepadButton value) => new Hold(seconds, new GamepadState().WithButton(value));
        static Hold Keys(double seconds, Key value) => new Hold(seconds, new GamepadState(), new KeyboardState(value));
        static float HorizontalMagnitude(Vector3 value) => new Vector2(value.x, value.z).magnitude;
        static float HorizontalDistance(Vector3 a, Vector3 b) => HorizontalMagnitude(a - b);
        static void CountGlowPress() => glowPresses++;
        static void CountSwarmAnswer(int clearing) => swarmAnswers++;

        static GameObject MakeHiddenObstacle(string name, Vector3 position, Vector3 scale)
        {
            var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obstacle.name = name;
            obstacle.hideFlags = HideFlags.HideAndDontSave;
            obstacle.transform.position = position;
            obstacle.transform.localScale = scale;
            obstacle.GetComponent<Renderer>().enabled = false;
            Physics.SyncTransforms();
            return obstacle;
        }

        static float PlayerViewportHeight(Camera camera)
        {
            if (camera == null)
                return -1f;
            float lowest = float.PositiveInfinity;
            float highest = float.NegativeInfinity;
            FireflyModel model = pilot.GetComponentInChildren<FireflyModel>();
            foreach (var renderer in pilot.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled || (model != null && renderer == model.Halo))
                    continue;
                Bounds bounds = renderer.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 world = bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
                    Vector3 viewport = camera.WorldToViewportPoint(world);
                    if (viewport.z <= 0f)
                        continue;
                    lowest = Mathf.Min(lowest, viewport.y);
                    highest = Mathf.Max(highest, viewport.y);
                }
            }
            return float.IsInfinity(lowest) ? -1f : highest - lowest;
        }

        static int SwarmEnabledPointLightCount()
        {
            if (swarm == null)
                return 0;
            int count = 0;
            foreach (var light in swarm.GetComponentsInChildren<Light>(true))
                if (light.type == LightType.Point && light.enabled && light.gameObject.activeInHierarchy && light.intensity > 0.001f)
                    count++;
            return count;
        }

        static float ScreenshotMeanLuminance(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return -1f;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!texture.LoadImage(File.ReadAllBytes(path)))
                    return -1f;
                Color32[] pixels = texture.GetPixels32();
                double sum = 0d;
                foreach (Color32 color in pixels)
                    sum += color.r * 0.2126d + color.g * 0.7152d + color.b * 0.0722d;
                return pixels.Length > 0 ? (float)(sum / (pixels.Length * 255d)) : -1f;
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        static string CaptureScreenshot(string filename)
        {
            string artifacts = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Artifacts"));
            Directory.CreateDirectory(artifacts);
            string path = Path.Combine(artifacts, filename);
            if (!Application.isBatchMode && Application.isFocused && Screen.width > 1 && Screen.height > 1)
            {
                ScreenCapture.CaptureScreenshot(path, 2);
                report.screenshotMethod = "Game View ScreenCapture, 2x supersize";
            }
            else
            {
                CaptureCameraOffscreen(path);
                report.screenshotMethod = "Main game camera, URP SingleCameraRequest, HDR 1920x1080";
            }
            return path;
        }

        static void CaptureCameraOffscreen(string path)
        {
            const int width = 1920;
            const int height = 1080;
            Camera camera = Camera.main;
            if (camera == null)
                throw new InvalidOperationException("The active meadow has no main camera to capture.");
            var oldTarget = camera.targetTexture;
            var oldActive = RenderTexture.active;
            RenderTexture target = null;
            Texture2D hdr = null;
            Texture2D png = null;
            try
            {
                target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
                {
                    name = "Temporary Firefly validation HDR capture",
                    antiAliasing = 1,
                    hideFlags = HideFlags.HideAndDontSave
                };
                target.Create();
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
                if (!RenderPipeline.SupportsRenderRequest(camera, request))
                    throw new InvalidOperationException("The active render pipeline does not support a URP camera render request.");
                // This executes the actual camera's URP passes and post effects without
                // requiring a visible Game View or changing the user's window state.
                RenderPipeline.SubmitRenderRequest(camera, request);
                RenderTexture.active = target;
                hdr = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
                hdr.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
                hdr.Apply(false, false);
                Color[] pixels = hdr.GetPixels();
                if (QualitySettings.activeColorSpace == ColorSpace.Linear)
                {
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        Color color = pixels[i].gamma;
                        color.a = 1f;
                        pixels[i] = color;
                    }
                }
                png = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
                png.SetPixels(pixels);
                png.Apply(false, false);
                File.WriteAllBytes(path, png.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = oldTarget;
                RenderTexture.active = oldActive;
                if (target != null)
                {
                    target.Release();
                    Object.DestroyImmediate(target);
                }
                if (hdr != null)
                    Object.DestroyImmediate(hdr);
                if (png != null)
                    Object.DestroyImmediate(png);
            }
        }

        static int NearbyPointLightCount()
        {
            int count = 0;
            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light.type == LightType.Point && light.enabled && light.gameObject.activeInHierarchy && light.intensity > 0.01f
                    && HorizontalDistance(light.transform.position, pilot.transform.position) < light.range + 1f)
                    count++;
            }
            return count;
        }

        static void Record(string name, bool passed, string detail)
        {
            report.checks.Add(new Check { name = name, passed = passed, detail = detail });
            SaveReport();
        }

        static void Fail(string message)
        {
            if (report == null)
                ReadSavedReport();
            report.errors.Add(message);
            SaveReport();
        }

        static void StopPlay()
        {
            routine = null;
            hold = null;
            SessionState.SetString(StageKey, "stopping");
            CleanupRuntime();
            SaveReport();
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                EditorApplication.ExitPlaymode();
            else
                RestoreEditor();
        }

        static void CleanupRuntime()
        {
            if (cameraObstacle != null)
                Object.DestroyImmediate(cameraObstacle);
            cameraObstacle = null;
            if (flightObstacle != null)
                Object.DestroyImmediate(flightObstacle);
            flightObstacle = null;
            if (input != null)
                input.GlowPressed -= CountGlowPress;
            if (swarm != null)
                swarm.ClearingAnswered -= CountSwarmAnswer;
            if (pad != null && pad.added)
            {
                InputSystem.QueueStateEvent(pad, new GamepadState());
                InputSystem.Update();
                InputSystem.RemoveDevice(pad);
            }
            pad = null;
            if (keyboard != null && keyboard.added)
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                InputSystem.Update();
                InputSystem.RemoveDevice(keyboard);
            }
            keyboard = null;
            if (previousGamepad != null && previousGamepad.added)
                previousGamepad.MakeCurrent();
            if (previousKeyboard != null && previousKeyboard.added)
                previousKeyboard.MakeCurrent();
            if (runtimeSettingsChanged)
            {
                InputSystem.settings.updateMode = previousUpdateMode;
                InputSystem.settings.backgroundBehavior = previousBackgroundBehavior;
                InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorInputBehavior;
                Time.timeScale = previousTimeScale;
                AudioListener.pause = previousAudioListenerPause;
                Application.runInBackground = previousRunInBackground;
                runtimeSettingsChanged = false;
            }
        }

        static void RestoreEditor()
        {
            if (!IsRunning)
                return;
            ReadSavedReport();
            try
            {
                string saved = SessionState.GetString(SetupKey, "");
                if (SessionState.GetBool(RestoreScenesKey, false) && !string.IsNullOrEmpty(saved))
                {
                    var setup = JsonUtility.FromJson<SavedSceneSetup>(saved);
                    if (setup.scenes != null && setup.scenes.Length == 1 && string.IsNullOrEmpty(setup.scenes[0].path))
                        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    else if (setup.scenes != null && setup.scenes.Length > 0)
                        EditorSceneManager.RestoreSceneManagerSetup(setup.scenes);
                }
            }
            catch (Exception exception)
            {
                report.errors.Add("Could not restore editor scene setup: " + exception.Message);
            }
            EditorApplication.isPaused = SessionState.GetBool(PauseKey, false);
            report.passed = report.errors.Count == 0 && report.checks.Count > 0;
            foreach (var check in report.checks)
                report.passed &= check.passed;
            report.status = report.passed ? "passed" : "failed";
            SessionState.SetBool(ActiveKey, false);
            SessionState.EraseString(StageKey);
            SaveReport();
            Debug.Log("Firefly gamepad validation " + report.status + ": " + ResultPath);
        }

        static void BeforeAssemblyReload()
        {
            if (IsRunning && routine != null)
            {
                Fail("Scripts reloaded during the validation run; rerun after compilation completes.");
                CleanupRuntime();
                SessionState.SetString(StageKey, "stopping");
            }
        }

        static void OnLog(string message, string stack, LogType type)
        {
            if (!IsRunning || (type != LogType.Error && type != LogType.Exception && type != LogType.Assert))
                return;
            if (report == null)
                ReadSavedReport();
            if (report.errors.Count >= 32)
                return;
            string error = message + (string.IsNullOrEmpty(stack) ? "" : "\n" + stack);
            if (error.Length > 4000)
                error = error.Substring(0, 4000);
            report.errors.Add(error);
            SaveReport();
        }

        static void ReadSavedReport()
        {
            string saved = SessionState.GetString(ReportKey, "");
            report = string.IsNullOrEmpty(saved) ? new Report() : JsonUtility.FromJson<Report>(saved);
        }

        static void SaveReport()
        {
            if (report == null)
                return;
            double started;
            if (double.TryParse(SessionState.GetString(StartKey, "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out started) && started > 0d)
                report.elapsedSeconds = Math.Max(0d, EditorApplication.timeSinceStartup - started);
            string json = JsonUtility.ToJson(report, true);
            SessionState.SetString(ReportKey, json);
            Directory.CreateDirectory(Path.GetDirectoryName(ResultPath));
            File.WriteAllText(ResultPath, json);
        }
    }
}
