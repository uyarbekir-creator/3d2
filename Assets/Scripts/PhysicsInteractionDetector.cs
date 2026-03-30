using UnityEngine;
using KinematicCharacterController;
using KinematicCharacterController.Examples;

/// <summary>
/// Detects physics objects on the "Interactable" layer within the player's
/// view frustum using a SphereCast, then applies a proportional resistance
/// force through ExampleCharacterController.AddVelocity so the character
/// feels like it's actually pushing against physical weight.
///
/// Does NOT touch KinematicCharacterMotor internals — the KCC's UpdateVelocity
/// pipeline is left completely intact.
///
/// Public API:
///   InteractionWeight  — normalised 0–1 push intensity (read by VFX, audio, UI).
///   DetectedCollider   — the object currently being pushed against, or null.
///
/// Setup:
///   1. Add this component to the player GameObject.
///   2. Create a Unity Layer named exactly "Interactable".
///   3. Assign physics objects you want the player to push to that layer.
///   4. Optionally assign _playerCamera; falls back to Camera.main.
/// </summary>
[RequireComponent(typeof(ExampleCharacterController))]
public class PhysicsInteractionDetector : MonoBehaviour
{
    [Header("SphereCast Detection")]
    [Tooltip("Radius of the detection sphere (should be slightly smaller than capsule radius).")]
    [SerializeField] private float _castRadius       = 0.38f;
    [Tooltip("How far forward to cast the sphere (metres).")]
    [SerializeField] private float _castRange        = 1.5f;
    [Tooltip("Half-angle of the view-frustum cone used to cull detections behind the camera.")]
    [SerializeField] private float _frustumHalfAngle = 55f;
    [SerializeField] private Camera _playerCamera;

    [Header("Weight Smoothing")]
    [Tooltip("SmoothDamp time for InteractionWeight transitions.")]
    [SerializeField] private float _weightSmoothTime = 0.08f;

    [Header("Resistance Force")]
    [Tooltip("Maximum counter-force applied per second when fully pushing against an object.")]
    [SerializeField] private float _maxResistanceForce = 7f;
    [Tooltip("Minimum horizontal speed before resistance kicks in (prevents micro-jitter).")]
    [SerializeField] private float _minSpeedToResist   = 0.3f;

    // ── Public read-only state ───────────────────────────────────────────────
    /// <summary>Normalised 0–1 value representing push intensity against an Interactable.</summary>
    public float    InteractionWeight { get; private set; }
    /// <summary>The Interactable collider currently being pushed, or null if none.</summary>
    public Collider DetectedCollider  { get; private set; }
    /// <summary>
    /// World-space direction the player is pushing toward (into the hit surface).
    /// Zero when no contact. Used by SpineAimController to bend spine toward the object.
    /// </summary>
    public Vector3  PushDirection     { get; private set; }

    // ── Internal ─────────────────────────────────────────────────────────────
    private ExampleCharacterController _character;
    private KinematicCharacterMotor    _motor;
    private int                        _interactableLayerMask;
    private float                      _rawWeight;
    private float                      _weightVelocity;
    private float                      _cosHalfFrustum;

    private void Awake()
    {
        _character             = GetComponent<ExampleCharacterController>();
        _motor                 = _character.Motor;
        _interactableLayerMask = LayerMask.GetMask("Interactable");
        _cosHalfFrustum        = Mathf.Cos(_frustumHalfAngle * Mathf.Deg2Rad);

        if (_playerCamera == null)
            _playerCamera = Camera.main;

        if (_interactableLayerMask == 0)
            Debug.LogWarning("[PhysicsInteractionDetector] No layer named 'Interactable' found. " +
                             "Create the layer in Project Settings → Tags and Layers.");
    }

    private void FixedUpdate()
    {
        PerformDetection();
        SmoothWeight();
        ApplyResistance();
    }

    // ── Detection ────────────────────────────────────────────────────────────

    private void PerformDetection()
    {
        // Cast from capsule mid-point forward
        float   capsuleHeight = _motor.Capsule != null ? _motor.Capsule.height : 2f;
        Vector3 origin        = _motor.TransientPosition + Vector3.up * (capsuleHeight * 0.5f);
        Vector3 forward       = _motor.CharacterForward;

        DetectedCollider = null;
        PushDirection    = Vector3.zero;
        _rawWeight       = 0f;

        if (!Physics.SphereCast(origin, _castRadius, forward, out RaycastHit hit,
                                _castRange, _interactableLayerMask,
                                QueryTriggerInteraction.Ignore))
            return;

        // Cull anything not within the camera's view frustum
        if (!IsInFrustum(hit.point))
            return;

        DetectedCollider = hit.collider;
        PushDirection    = -hit.normal;   // direction character is pushing toward the surface

        // Distance weight: 1 when touching, 0 at full range
        float distanceWeight = 1f - Mathf.Clamp01(hit.distance / _castRange);

        // Alignment weight: how directly is the player moving toward the surface?
        Vector3 horizVel        = Vector3.ProjectOnPlane(_motor.Velocity, Vector3.up);
        float   horizontalSpeed = horizVel.magnitude;
        float   alignmentWeight = 0f;

        if (horizontalSpeed > _minSpeedToResist)
        {
            // dot( normalized velocity, surface inward normal ) → 1 when heading straight in
            float alignment = Vector3.Dot(horizVel / horizontalSpeed, -hit.normal);
            alignmentWeight = Mathf.Clamp01(alignment);
        }

        _rawWeight = distanceWeight * alignmentWeight;
    }

    private bool IsInFrustum(Vector3 worldPoint)
    {
        if (_playerCamera == null) return true;
        Vector3 camPos    = _playerCamera.transform.position;
        Vector3 camFwd    = _playerCamera.transform.forward;
        Vector3 toPoint   = (worldPoint - camPos).normalized;
        return Vector3.Dot(camFwd, toPoint) >= _cosHalfFrustum;
    }

    private void SmoothWeight()
    {
        InteractionWeight = Mathf.SmoothDamp(
            InteractionWeight, _rawWeight,
            ref _weightVelocity, _weightSmoothTime);
    }

    // ── Resistance force ─────────────────────────────────────────────────────

    /// <summary>
    /// Injects a counter-velocity through ExampleCharacterController.AddVelocity.
    /// This feeds into KCC's _internalVelocityAdd, applied inside UpdateVelocity —
    /// so the Motor never needs to be touched.
    /// </summary>
    private void ApplyResistance()
    {
        if (InteractionWeight < 0.02f) return;

        Vector3 horizVel = Vector3.ProjectOnPlane(_motor.Velocity, Vector3.up);
        if (horizVel.sqrMagnitude < _minSpeedToResist * _minSpeedToResist) return;

        // Counter-force opposing horizontal movement, scaled by weight and fixed-dt
        Vector3 resistance = -horizVel.normalized
                             * InteractionWeight
                             * _maxResistanceForce
                             * Time.fixedDeltaTime;

        _character.AddVelocity(resistance);
    }
}
