using UnityEngine;
using Unity.Cinemachine;
using KinematicCharacterController;
using KinematicCharacterController.Examples;

/// <summary>
/// Detects the exact landing frame via Motor.GroundingStatus and applies:
///
///   • Squash: instantly compresses MeshRoot Y (and expands XZ to conserve volume).
///   • Stretch: overshoots back past 1 briefly before settling — sells physical rebound.
///   • Camera Dip: fires a CinemachineImpulseSource on impact.
///
/// The component takes ownership of MeshRoot.localScale in LateUpdate so it
/// correctly stacks on top of ExampleCharacterController's crouch scaling
/// (derived from Motor.Capsule.height rather than fighting ExampleCC's calls).
///
/// Setup:
///   1. Add this component to the player GameObject.
///   2. Add CinemachineImpulseSource to the same GameObject; assign it below.
///   3. Add CinemachineImpulseListener to your CinemachineCamera GameObject.
/// </summary>
[RequireComponent(typeof(ExampleCharacterController))]
public class LandingCompressor : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CinemachineImpulseSource _impulseSource;

    [Header("Squash — Shape")]
    [Tooltip("Y scale at peak squash for the heaviest fall.")]
    [SerializeField] private float _maxSquashY          = 0.70f;
    [Tooltip("Y scale overshoot above 1 on the stretch rebound (per unit of t).")]
    [SerializeField] private float _stretchPeak         = 1.14f;
    [Tooltip("Fall speed (m/s) that maps to full squash intensity.")]
    [SerializeField] private float _fallSpeedForMaxEffect = 14f;

    [Header("Squash — Timing")]
    [Tooltip("Total duration of the squash-stretch-settle cycle in seconds.")]
    [SerializeField] private float _totalDuration       = 0.55f;
    [Tooltip("Fraction of _totalDuration spent in the squash phase (0–1).")]
    [SerializeField] private float _squashPhaseFraction = 0.30f;
    [Tooltip("Fraction of _totalDuration spent in the stretch phase (0–1).")]
    [SerializeField] private float _stretchPhaseFraction = 0.35f;

    [Header("Camera Impact")]
    [Tooltip("Max downward impulse force for the heaviest landing.")]
    [SerializeField] private float _maxImpulseForce     = 3.0f;

    // ── Internal state ──────────────────────────────────────────────────────
    private ExampleCharacterController _character;
    private KinematicCharacterMotor    _motor;
    private Transform                  _meshRoot;

    private float _squashY   = 1f;
    private float _squashXZ  = 1f;
    private float _squashYV;
    private float _squashXZV;

    private bool  _recovering;
    private float _recoverTimer;

    // Per-landing targets (set on the landing frame)
    private float _peakSquashY;
    private float _peakSquashXZ;
    private float _peakStretchY;
    private float _peakStretchXZ;

    private float _prevVerticalVelocity;

    // SmoothDamp time constants
    private const float SnapSmooth    = 0.04f;
    private const float StretchSmooth = 0.06f;
    private const float SettleSmooth  = 0.09f;

    private void Awake()
    {
        _character = GetComponent<ExampleCharacterController>();
        _motor     = _character.Motor;
        _meshRoot  = _character.MeshRoot;
    }

    private void Update()
    {
        DetectLanding();
        AdvanceSquash();
    }

    private void LateUpdate()
    {
        // Always own MeshRoot.localScale — stacks cleanly on top of crouch state
        ApplyScale();
    }

    // ── Landing detection ───────────────────────────────────────────────────

    private void DetectLanding()
    {
        bool isGrounded  = _motor.GroundingStatus.IsStableOnGround;
        bool wasGrounded = _motor.LastGroundingStatus.IsStableOnGround;

        if (isGrounded && !wasGrounded)
        {
            // _prevVerticalVelocity holds the last-frame air velocity — more reliable
            // than reading Motor.Velocity on the landing frame (already projected onto ground)
            float fallSpeed = Mathf.Abs(Mathf.Min(_prevVerticalVelocity, 0f));
            float t         = Mathf.Clamp01(fallSpeed / _fallSpeedForMaxEffect);

            // Only react if the fall was meaningful (> 1 m/s down)
            if (t > 0.05f)
                TriggerLanding(t);
        }

        _prevVerticalVelocity = _motor.Velocity.y;
    }

    private void TriggerLanding(float t)
    {
        float squashY  = Mathf.Lerp(1f, _maxSquashY, t);
        float squashXZ = Mathf.Lerp(1f, 1f / _maxSquashY, t);  // volume-conserving reciprocal

        _peakSquashY   = squashY;
        _peakSquashXZ  = squashXZ;
        _peakStretchY  = Mathf.Lerp(1f, _stretchPeak, t * 0.75f);
        _peakStretchXZ = Mathf.Lerp(1f, 1f / _stretchPeak, t * 0.75f);

        // Immediately snap to squash
        _squashY   = squashY;
        _squashXZ  = squashXZ;
        _squashYV  = 0f;
        _squashXZV = 0f;

        _recovering   = true;
        _recoverTimer = 0f;

        // Camera impulse — downward spike, amplitude scaled with fall intensity
        if (_impulseSource != null)
            _impulseSource.GenerateImpulse(Vector3.down * (_maxImpulseForce * t));
    }

    // ── Squash animation state machine ──────────────────────────────────────

    private void AdvanceSquash()
    {
        if (!_recovering) return;

        _recoverTimer += Time.deltaTime;
        float normalised = _recoverTimer / _totalDuration;

        float squashEnd  = _squashPhaseFraction;
        float stretchEnd = _squashPhaseFraction + _stretchPhaseFraction;

        float targetY, targetXZ, smooth;

        if (normalised < squashEnd)
        {
            // Phase 1: hold squash
            targetY  = _peakSquashY;
            targetXZ = _peakSquashXZ;
            smooth   = SnapSmooth;
        }
        else if (normalised < stretchEnd)
        {
            // Phase 2: bounce to stretch overshoot
            targetY  = _peakStretchY;
            targetXZ = _peakStretchXZ;
            smooth   = StretchSmooth;
        }
        else
        {
            // Phase 3: settle back to neutral
            targetY  = 1f;
            targetXZ = 1f;
            smooth   = SettleSmooth;
        }

        _squashY  = Mathf.SmoothDamp(_squashY,  targetY,  ref _squashYV,  smooth);
        _squashXZ = Mathf.SmoothDamp(_squashXZ, targetXZ, ref _squashXZV, smooth);

        // Finished once the cycle is done and values are settled
        bool cycleOver = normalised > 1f + 0.3f;
        bool settled   = Mathf.Abs(_squashY - 1f) < 0.004f && Mathf.Abs(_squashXZ - 1f) < 0.004f;
        if (cycleOver && settled)
        {
            _squashY   = 1f;
            _squashXZ  = 1f;
            _recovering = false;
        }
    }

    // ── Scale application ───────────────────────────────────────────────────

    private void ApplyScale()
    {
        if (_meshRoot == null) return;

        // Derive crouch base scale from Motor.Capsule.height instead of fighting
        // ExampleCharacterController's direct assignments.
        // Standing: height ≈ 2 → ratio = 1.  Crouching: height ≈ 1 → ratio = 0.5
        float capsuleHeight = (_motor.Capsule != null) ? _motor.Capsule.height : 2f;
        float heightRatio   = Mathf.Clamp(capsuleHeight / 2f, 0.1f, 1f);

        _meshRoot.localScale = new Vector3(
            _squashXZ,
            heightRatio * _squashY,
            _squashXZ
        );
    }
}
