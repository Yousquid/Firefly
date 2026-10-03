using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Firefly
{
    /// <summary>Gamepad-first input with keyboard fallback. Owns actions, never global time scale.</summary>
    [DefaultExecutionOrder(-100)]
    public sealed class FlightInput : MonoBehaviour
    {
        [SerializeField, Range(0.05f, 0.5f)] private float stickDeadzone = 0.18f;
        [SerializeField, Range(0.7f, 1f)] private float stickOuterDeadzone = 0.95f;
        [SerializeField, Range(0f, 0.3f)] private float triggerDeadzone = 0.06f;
        [SerializeField] private bool hapticsEnabled = true;

        private InputAction keyboardMove;
        private InputAction gamepadMove;
        private InputAction keyboardAscend;
        private InputAction keyboardDescend;
        private InputAction mouseDelta;
        private InputAction mouseOrbit;
        private InputAction glow;
        private InputAction pause;
        private InputAction boost;
        private Gamepad activeGamepad;
        private Gamepad rumblingGamepad;
        private float rumbleEndTime;
        private bool requireNeutral;
        private bool mouseCaptured;
        private CursorLockMode previousCursorLock;
        private bool previousCursorVisible;

        public Vector2 Move { get; private set; }
        public float Elevation { get; private set; }
        public Vector2 Look { get; private set; }
        public Vector2 MouseLookDelta { get; private set; }
        public bool MouseOrbitHeld { get; private set; }
        public bool BoostHeld { get; private set; }
        public bool IsPaused { get; private set; }
        public bool LastInputWasGamepad { get; private set; } = true;
        public bool HasFocus { get; private set; } = true;
        public bool HasGamepad => Gamepad.all.Count > 0;
        public bool CanMove => isActiveAndEnabled && HasFocus && !IsPaused && !requireNeutral;
        public int MovementResetVersion { get; private set; }
        public event Action GlowPressed;
        public event Action<bool> PauseChanged;
        public event Action DeviceChanged;

        private void Awake()
        {
            keyboardMove = new InputAction("Keyboard flight", InputActionType.Value, expectedControlType: "Vector2");
            keyboardMove.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            keyboardMove.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            gamepadMove = new InputAction("Gamepad flight", InputActionType.Value, "<Gamepad>/leftStick");
            keyboardAscend = new InputAction("Ascend", InputActionType.Button, "<Keyboard>/space");
            keyboardDescend = new InputAction("Descend", InputActionType.Button);
            keyboardDescend.AddBinding("<Keyboard>/leftCtrl");
            keyboardDescend.AddBinding("<Keyboard>/rightCtrl");
            mouseDelta = new InputAction("Mouse orbit delta", InputActionType.PassThrough, "<Mouse>/delta");
            mouseOrbit = new InputAction("Mouse orbit held", InputActionType.Button, "<Mouse>/rightButton");
            glow = new InputAction("Toggle glow", InputActionType.Button);
            glow.AddBinding("<Gamepad>/buttonSouth");
            glow.AddBinding("<Keyboard>/f");
            pause = new InputAction("Pause", InputActionType.Button);
            pause.AddBinding("<Gamepad>/start");
            pause.AddBinding("<Keyboard>/escape");
            boost = new InputAction("Glide faster", InputActionType.Button);
            boost.AddBinding("<Gamepad>/rightShoulder");
            boost.AddBinding("<Keyboard>/leftShift");
            boost.AddBinding("<Keyboard>/rightShift");
            keyboardMove.performed += OnInputActivity;
            gamepadMove.performed += OnGamepadMoveActivity;
            keyboardAscend.performed += OnInputActivity;
            keyboardDescend.performed += OnInputActivity;
            mouseOrbit.performed += OnInputActivity;
            glow.performed += OnGlow;
            pause.performed += OnPause;
            boost.performed += OnInputActivity;
        }

        private void OnEnable()
        {
            keyboardMove.Enable();
            gamepadMove.Enable();
            keyboardAscend.Enable();
            keyboardDescend.Enable();
            mouseDelta.Enable();
            mouseOrbit.Enable();
            glow.Enable();
            pause.Enable();
            boost.Enable();
            InputSystem.onDeviceChange += OnDeviceChange;
            activeGamepad = Gamepad.current;
        }

        private void OnDisable()
        {
            InputSystem.onDeviceChange -= OnDeviceChange;
            keyboardMove?.Disable();
            gamepadMove?.Disable();
            keyboardAscend?.Disable();
            keyboardDescend?.Disable();
            mouseDelta?.Disable();
            mouseOrbit?.Disable();
            glow?.Disable();
            pause?.Disable();
            boost?.Disable();
            ClearMovement(true);
            StopHaptics();
        }

        private void OnDestroy()
        {
            keyboardMove?.Dispose();
            gamepadMove?.Dispose();
            keyboardAscend?.Dispose();
            keyboardDescend?.Dispose();
            mouseDelta?.Dispose();
            mouseOrbit?.Dispose();
            glow?.Dispose();
            pause?.Dispose();
            boost?.Dispose();
        }

        private void Update()
        {
            if (rumblingGamepad != null && Time.unscaledTime >= rumbleEndTime)
                StopHaptics();

            Vector2 keyboard = keyboardMove.ReadValue<Vector2>();
            Vector2 stick = Vector2.zero;
            Vector2 look = Vector2.zero;
            float elevation = 0f;
            float strongestPad = 0f;
            bool allPadsNeutral = true;
            bool ascendHeld = keyboardAscend.IsPressed();
            bool descendHeld = keyboardDescend.IsPressed();
            float keyboardElevation = (ascendHeld ? 1f : 0f) - (descendHeld ? 1f : 0f);
            // Read raw stick values, then apply exactly one explicit radial deadzone below.
            // Reading the action value here would also apply the layout's stickDeadzone.
            foreach (Gamepad pad in Gamepad.all)
            {
                if (!pad.enabled)
                    continue;
                Vector2 candidate = ApplyRadialDeadzone(pad.leftStick.ReadUnprocessedValue(), stickDeadzone, stickOuterDeadzone);
                Vector2 candidateLook = ApplyRadialDeadzone(pad.rightStick.ReadUnprocessedValue(), stickDeadzone, stickOuterDeadzone);
                float rise = ApplyTriggerDeadzone(pad.rightTrigger.ReadUnprocessedValue());
                float fall = ApplyTriggerDeadzone(pad.leftTrigger.ReadUnprocessedValue());
                float score = candidate.sqrMagnitude + rise * rise + fall * fall + candidateLook.sqrMagnitude;
                allPadsNeutral &= score < 0.001f;
                if (score > strongestPad)
                {
                    strongestPad = score;
                    stick = candidate;
                    look = candidateLook;
                    elevation = rise - fall;
                    activeGamepad = pad;
                }
            }

            if (!HasFocus || IsPaused)
            {
                ClearMovement();
                return;
            }
            if (requireNeutral)
            {
                ClearMovement();
                // Both triggers must be released even if their difference cancels out.
                if (keyboard.sqrMagnitude < 0.001f && allPadsNeutral && !ascendHeld && !descendHeld
                    && !boost.IsPressed() && !mouseOrbit.IsPressed())
                    requireNeutral = false;
                return;
            }

            if (strongestPad > 0.001f)
                SetDeviceSource(true);
            else if (keyboard.sqrMagnitude > 0.001f || ascendHeld || descendHeld || mouseOrbit.IsPressed())
                SetDeviceSource(false);

            Move = Vector2.ClampMagnitude(stick.sqrMagnitude > keyboard.sqrMagnitude ? stick : keyboard, 1f);
            Elevation = Mathf.Abs(elevation) > Mathf.Abs(keyboardElevation) ? elevation : keyboardElevation;
            Look = look;
            MouseOrbitHeld = mouseOrbit.IsPressed();
            MouseLookDelta = MouseOrbitHeld ? mouseDelta.ReadValue<Vector2>() : Vector2.zero;
            SetMouseCapture(MouseOrbitHeld);
            BoostHeld = boost.IsPressed();
        }

        public static Vector2 ApplyRadialDeadzone(Vector2 raw, float inner = 0.18f, float outer = 0.95f)
        {
            float magnitude = raw.magnitude;
            if (magnitude <= inner || magnitude < 0.0001f)
                return Vector2.zero;
            float strength = Mathf.Clamp01((magnitude - inner) / Mathf.Max(0.001f, outer - inner));
            return raw / magnitude * strength;
        }

        private float ApplyTriggerDeadzone(float value)
        {
            return Mathf.Clamp01((value - triggerDeadzone) / Mathf.Max(0.001f, 1f - triggerDeadzone));
        }

        public void SetPaused(bool paused)
        {
            if (IsPaused == paused)
                return;
            IsPaused = paused;
            ClearMovement(true);
            if (paused)
                StopHaptics();
            PauseChanged?.Invoke(paused);
        }

        public void PlayGlowHaptics(bool lit)
        {
            StopHaptics();
            if (!hapticsEnabled || !HasFocus || IsPaused || !LastInputWasGamepad)
                return;
            Gamepad pad = activeGamepad != null && activeGamepad.added ? activeGamepad : Gamepad.current;
            if (pad == null || !pad.enabled)
                return;
            rumblingGamepad = pad;
            pad.SetMotorSpeeds(lit ? 0.07f : 0.035f, lit ? 0.13f : 0.06f);
            rumbleEndTime = Time.unscaledTime + 0.07f;
        }

        private void OnGlow(InputAction.CallbackContext context)
        {
            OnInputActivity(context);
            if (HasFocus && !IsPaused && !requireNeutral)
                GlowPressed?.Invoke();
        }

        private void OnPause(InputAction.CallbackContext context)
        {
            OnInputActivity(context);
            if (HasFocus)
                SetPaused(!IsPaused);
        }

        private void OnInputActivity(InputAction.CallbackContext context)
        {
            if (!HasFocus)
                return;
            bool gamepad = context.control.device is Gamepad;
            if (gamepad)
                activeGamepad = (Gamepad)context.control.device;
            SetDeviceSource(gamepad);
        }

        private void OnGamepadMoveActivity(InputAction.CallbackContext context)
        {
            Gamepad pad = context.control.device as Gamepad;
            if (pad != null && ApplyRadialDeadzone(pad.leftStick.ReadUnprocessedValue(), stickDeadzone, stickOuterDeadzone).sqrMagnitude > 0.001f)
                OnInputActivity(context);
        }

        private void SetDeviceSource(bool gamepad)
        {
            if (LastInputWasGamepad == gamepad)
                return;
            LastInputWasGamepad = gamepad;
            DeviceChanged?.Invoke();
        }

        private void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (!(device is Gamepad))
                return;
            if (change == InputDeviceChange.Removed || change == InputDeviceChange.Disconnected || change == InputDeviceChange.Disabled)
            {
                if (rumblingGamepad == device)
                    StopHaptics();
                if (activeGamepad == device)
                {
                    activeGamepad = null;
                    requireNeutral = true;
                    ClearMovement(true);
                }
            }
            DeviceChanged?.Invoke();
        }

        private void OnApplicationFocus(bool focused)
        {
            HasFocus = focused;
            if (!focused)
            {
                requireNeutral = true;
                ClearMovement(true);
                StopHaptics();
                SetPaused(true);
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
                OnApplicationFocus(false);
            else
                HasFocus = Application.isFocused;
        }

        private void OnApplicationQuit()
        {
            StopHaptics();
        }

        private void ClearMovement(bool interrupt = false)
        {
            Move = Vector2.zero;
            Elevation = 0f;
            Look = Vector2.zero;
            MouseLookDelta = Vector2.zero;
            MouseOrbitHeld = false;
            SetMouseCapture(false);
            BoostHeld = false;
            if (interrupt)
                MovementResetVersion++;
        }

        private void SetMouseCapture(bool capture)
        {
            if (capture == mouseCaptured)
                return;
            if (capture)
            {
                previousCursorLock = Cursor.lockState;
                previousCursorVisible = Cursor.visible;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            else
            {
                Cursor.lockState = previousCursorLock;
                Cursor.visible = previousCursorVisible;
            }
            mouseCaptured = capture;
        }

        private void StopHaptics()
        {
            // Reset cached motor speeds even if the device has just disconnected.
            // Otherwise a platform could restore the old pulse when that pad reconnects.
            if (rumblingGamepad != null)
                rumblingGamepad.ResetHaptics();
            rumblingGamepad = null;
        }
    }
}
