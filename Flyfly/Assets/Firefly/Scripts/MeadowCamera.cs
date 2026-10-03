using UnityEngine;

namespace Firefly
{
    /// <summary>A distant orbit view following the flight root rather than wing or hover motion.</summary>
    [DefaultExecutionOrder(100)]
    public sealed class MeadowCamera : MonoBehaviour
    {
        [SerializeField] private float orbitDistance = 18f;
        [SerializeField] private float fieldOfView = 60f;
        [SerializeField] private float yaw;
        [SerializeField] private float pitch = 18f;
        [SerializeField] private Vector2 pitchBounds = new Vector2(-15f, 70f);
        [SerializeField] private Vector3 focusOffset = new Vector3(0f, 0.18f, 0f);
        [SerializeField] private float orbitSpeed = 95f;
        [SerializeField] private float mouseSensitivity = 0.12f;
        [SerializeField] private float lookDamping = 12f;
        [SerializeField] private float followDamping = 5f;
        [SerializeField] private float collisionRadius = 0.35f;
        [SerializeField] private LayerMask obstacleMask = Physics.DefaultRaycastLayers;
        [SerializeField] private FlightInput input;
        private readonly RaycastHit[] castHits = new RaycastHit[48];
        private Vector2 angularVelocity;
        private int lastInputResetVersion;

        [field: SerializeField] public Transform Target { get; private set; }
        public float OrbitDistance => orbitDistance;
        public float CurrentDistance => Target != null ? Vector3.Distance(FocusPoint, transform.position) : orbitDistance;
        public float Yaw => yaw;
        public float Pitch => pitch;
        public Vector2 PitchBounds => pitchBounds;
        public Vector3 FocusPoint => Target != null ? Target.position + focusOffset : transform.position;

        private void Awake()
        {
            if (input == null && Target != null)
                input = Target.GetComponent<FlightInput>();
            ConfigureLens();
        }

        public void Configure(Transform target, FlightInput flightInput = null)
        {
            Target = target;
            input = flightInput != null ? flightInput : target != null ? target.GetComponent<FlightInput>() : null;
            lastInputResetVersion = input != null ? input.MovementResetVersion : 0;
            ConfigureLens();
            SnapToTarget();
        }

        public void SetOrbit(float yawDegrees, float pitchDegrees, bool snap = true)
        {
            yaw = Mathf.Repeat(yawDegrees + 180f, 360f) - 180f;
            pitch = Mathf.Clamp(pitchDegrees, pitchBounds.x, pitchBounds.y);
            angularVelocity = Vector2.zero;
            if (snap)
                SnapToTarget();
        }

        private void ConfigureLens()
        {
            Camera cameraComponent = GetComponent<Camera>();
            if (cameraComponent == null)
                return;
            cameraComponent.fieldOfView = fieldOfView;
            cameraComponent.nearClipPlane = 0.08f;
            cameraComponent.farClipPlane = 120f;
        }

        public void SnapToTarget()
        {
            if (Target == null)
                return;
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 desired = FocusPoint - rotation * Vector3.forward * orbitDistance;
            transform.SetPositionAndRotation(ResolveObstruction(FocusPoint, desired), rotation);
        }

        private void LateUpdate()
        {
            if (Target == null || Time.deltaTime <= 0f || (input != null && !input.CanMove))
            {
                angularVelocity = Vector2.zero;
                return;
            }
            if (input != null && lastInputResetVersion != input.MovementResetVersion)
            {
                angularVelocity = Vector2.zero;
                lastInputResetVersion = input.MovementResetVersion;
            }
            float dt = Time.deltaTime;
            Vector2 desiredAngularVelocity = input != null ? input.Look * orbitSpeed : Vector2.zero;
            float decay = Mathf.Exp(-lookDamping * dt);
            Vector2 angularDelta = desiredAngularVelocity * dt + (angularVelocity - desiredAngularVelocity) * ((1f - decay) / lookDamping);
            angularVelocity = desiredAngularVelocity + (angularVelocity - desiredAngularVelocity) * decay;
            if (input != null)
                angularDelta += input.MouseLookDelta * mouseSensitivity;
            yaw = Mathf.Repeat(yaw + angularDelta.x + 180f, 360f) - 180f;
            pitch = Mathf.Clamp(pitch - angularDelta.y, pitchBounds.x, pitchBounds.y);

            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 focus = FocusPoint;
            Vector3 desired = ResolveObstruction(focus, focus - rotation * Vector3.forward * orbitDistance);
            Vector3 smoothed = Vector3.Lerp(transform.position, desired, 1f - Mathf.Exp(-followDamping * dt));
            // The second sweep prevents follow smoothing from travelling through a trunk or ground.
            Vector3 position = ResolveObstruction(focus, smoothed);
            Vector3 toFocus = focus - position;
            transform.SetPositionAndRotation(position, toFocus.sqrMagnitude > 0.001f ? Quaternion.LookRotation(toFocus, Vector3.up) : rotation);
        }

        private Vector3 ResolveObstruction(Vector3 focus, Vector3 desired)
        {
            Vector3 delta = desired - focus;
            float distance = delta.magnitude;
            if (distance < 0.001f)
                return desired;
            Vector3 direction = delta / distance;
            int count = Physics.SphereCastNonAlloc(focus, collisionRadius, direction, castHits, distance,
                obstacleMask, QueryTriggerInteraction.Ignore);
            float nearest = distance;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = castHits[i];
                if (hit.transform == null || hit.transform.IsChildOf(Target) || Vector3.Dot(direction, hit.normal) >= -0.0001f)
                    continue;
                nearest = Mathf.Min(nearest, Mathf.Max(0.12f, hit.distance - 0.06f));
            }
            return focus + direction * nearest;
        }
    }
}