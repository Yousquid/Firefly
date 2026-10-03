using UnityEngine;

namespace Firefly
{
    /// <summary>Camera-relative flight through three dimensions; only the model bobs or banks.</summary>
    public sealed class FireflyPilot : MonoBehaviour
    {
        [SerializeField] private float cruiseSpeed = 2.3f;
        [SerializeField] private float boostSpeed = 3.5f;
        [SerializeField] private float acceleration = 7f;
        [SerializeField] private float braking = 5.5f;
        [SerializeField] private float turnDamping = 7f;
        [SerializeField] private float collisionRadius = 0.23f;
        [SerializeField] private LayerMask obstacleMask = Physics.DefaultRaycastLayers;
        [SerializeField] private Vector2 xBounds = new Vector2(-17f, 17f);
        [SerializeField] private Vector2 zBounds = new Vector2(-12f, 15f);
        [SerializeField] private Vector2 altitudeBounds = new Vector2(0.5f, 9f);

        [SerializeField] private FlightInput input;
        [SerializeField] private Transform visual;
        [SerializeField] private Transform referenceCamera;
        private Vector3 restingVisualPosition;
        private Quaternion heading = Quaternion.identity;
        private readonly RaycastHit[] castHits = new RaycastHit[32];
        private float flightTime;
        private int lastInputResetVersion;

        public Vector3 Velocity { get; private set; }
        public float SpeedNormalized => Mathf.Clamp01(Velocity.magnitude / boostSpeed);
        public bool IsBoosting => input != null && input.CanMove && input.BoostHeld
            && (input.Move.sqrMagnitude + input.Elevation * input.Elevation) > 0.001f;
        public float FlightHeight => transform.position.y;
        public Vector2 AltitudeBounds => altitudeBounds;
        public float CollisionRadius => collisionRadius;
        public float CruiseSpeed => cruiseSpeed;
        public float MaxSpeed => boostSpeed;

        private void Awake()
        {
            restingVisualPosition = visual != null ? visual.localPosition : Vector3.zero;
            heading = visual != null ? visual.rotation : transform.rotation;
        }

        public void Configure(FlightInput flightInput, Transform visualRoot, Transform cameraTransform)
        {
            input = flightInput;
            visual = visualRoot;
            referenceCamera = cameraTransform;
            lastInputResetVersion = input != null ? input.MovementResetVersion : 0;
            restingVisualPosition = visual != null ? visual.localPosition : Vector3.zero;
            heading = visual != null ? visual.rotation : transform.rotation;
            Teleport(transform.position);
        }

        public void Teleport(Vector3 position, bool resetVelocity = true)
        {
            transform.position = new Vector3(Mathf.Clamp(position.x, xBounds.x, xBounds.y), Mathf.Clamp(position.y, altitudeBounds.x, altitudeBounds.y),
                Mathf.Clamp(position.z, zBounds.x, zBounds.y));
            if (resetVelocity)
                Velocity = Vector3.zero;
        }

        private void Update()
        {
            if (input == null || !input.CanMove || Time.deltaTime <= 0f)
            {
                Velocity = Vector3.zero;
                return;
            }

            if (lastInputResetVersion != input.MovementResetVersion)
            {
                Velocity = Vector3.zero;
                lastInputResetVersion = input.MovementResetVersion;
            }

            float dt = Time.deltaTime;
            Vector3 forward = referenceCamera != null ? referenceCamera.forward : Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f)
                forward = Vector3.forward;
            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 horizontalDirection = right * input.Move.x + forward * input.Move.y;
            // One shared speed budget prevents horizontal/vertical diagonals gaining speed.
            Vector3 desiredDirection = Vector3.ClampMagnitude(horizontalDirection + Vector3.up * input.Elevation, 1f);
            float targetSpeed = IsBoosting ? boostSpeed : cruiseSpeed;
            Vector3 desiredVelocity = desiredDirection * targetSpeed;
            float response = desiredDirection.sqrMagnitude > 0.001f ? acceleration : braking;
            // Exact first-order response and integrated displacement avoid frame-rate dependent acceleration.
            float decay = Mathf.Exp(-response * dt);
            Vector3 displacement = desiredVelocity * dt + (Velocity - desiredVelocity) * ((1f - decay) / response);
            Velocity = desiredVelocity + (Velocity - desiredVelocity) * decay;
            Vector3 position = Slide(transform.position, displacement);
            float x = Mathf.Clamp(position.x, xBounds.x, xBounds.y);
            float y = Mathf.Clamp(position.y, altitudeBounds.x, altitudeBounds.y);
            float z = Mathf.Clamp(position.z, zBounds.x, zBounds.y);
            if (!Mathf.Approximately(x, position.x))
                Velocity = new Vector3(0f, Velocity.y, Velocity.z);
            if (!Mathf.Approximately(y, position.y))
                Velocity = new Vector3(Velocity.x, 0f, Velocity.z);
            if (!Mathf.Approximately(z, position.z))
                Velocity = new Vector3(Velocity.x, Velocity.y, 0f);
            transform.position = new Vector3(x, y, z);

            if (visual == null)
                return;
            flightTime += dt;
            if (horizontalDirection.sqrMagnitude > 0.005f)
                heading = Quaternion.Slerp(heading, Quaternion.LookRotation(horizontalDirection, Vector3.up), 1f - Mathf.Exp(-turnDamping * dt));
            Vector3 localVelocity = Quaternion.Inverse(heading) * Velocity;
            Quaternion bank = Quaternion.Euler(-SpeedNormalized * 9f - Velocity.y * 6f, 0f, -localVelocity.x * 9f);
            visual.rotation = heading * bank;
            visual.localPosition = restingVisualPosition + Vector3.up * (Mathf.Sin(flightTime * 2.7f) * 0.055f);
        }

        private Vector3 Slide(Vector3 origin, Vector3 displacement)
        {
            const float skin = 0.025f;
            for (int iteration = 0; iteration < 3 && displacement.sqrMagnitude > 0.000001f; iteration++)
            {
                float distance = displacement.magnitude;
                Vector3 direction = displacement / distance;
                int count = Physics.SphereCastNonAlloc(origin, collisionRadius, direction, castHits, distance + skin,
                    obstacleMask, QueryTriggerInteraction.Ignore);
                int closest = -1;
                float closestDistance = distance + skin;
                for (int i = 0; i < count; i++)
                {
                    RaycastHit hit = castHits[i];
                    if (hit.transform == null || hit.transform.IsChildOf(transform)
                        || Vector3.Dot(direction, hit.normal) >= -0.0001f)
                        continue;
                    if (hit.distance < closestDistance)
                    {
                        closestDistance = hit.distance;
                        closest = i;
                    }
                }
                if (closest < 0)
                    return origin + displacement;

                RaycastHit wall = castHits[closest];
                float safeDistance = Mathf.Max(0f, wall.distance - skin);
                origin += direction * safeDistance;
                Vector3 surfaceNormal = wall.normal.normalized;
                displacement = Vector3.ProjectOnPlane(displacement - direction * safeDistance, surfaceNormal);
                if (Vector3.Dot(Velocity, surfaceNormal) < 0f)
                    Velocity = Vector3.ProjectOnPlane(Velocity, surfaceNormal);
            }
            return origin;
        }
    }
}
