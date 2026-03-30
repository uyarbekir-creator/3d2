using UnityEngine;
using KinematicCharacterController;
using KinematicCharacterController.Examples;

/// <summary>
/// Applies procedural lean and inertia sway to the character's MeshRoot.
///
/// Lean: rotates MeshRoot based on lateral velocity and yaw-rate — the character
/// tilts into turns and leans slightly forward when accelerating.
///
/// Inertia Sway: when the character stops abruptly, triggers a brief forward
/// overshoot and smooth recovery that sells physical weight.
///
/// Setup: Add to the same GameObject as ExampleCharacterController.
/// ExampleCharacterController.MeshRoot must be assigned.
/// </summary>
[RequireComponent(typeof(ExampleCharacterController))]
public class ProceduralAnimator : MonoBehaviour
{
    [Header("Lean — Lateral / Turn")]
    [SerializeField] private float _maxLateralLeanDeg  = 9f;
    [SerializeField] private float _turnLeanMultiplier = 0.45f;   // yaw-rate contribution
    [SerializeField] private float _leanSmoothing      = 0.10f;   // SmoothDamp time (s)

    [Header("Lean — Forward / Back")]
    [SerializeField] private float _maxForwardLeanDeg  = 4f;
    [SerializeField] private float _pitchSmoothing     = 0.14f;

    [Header("Inertia Sway")]
    [SerializeField] private float _swayTriggerSpeed   = 2.5f;  // m/s — stop threshold
    [SerializeField] private float _swayMaxDegrees     = 14f;   // peak overshoot angle
    [SerializeField] private float _swayDuration       = 0.42f; // total sway cycle (s)

    // ── Internal state ──────────────────────────────────────────────────────
    private KinematicCharacterMotor _motor;
    private Transform               _meshRoot;

    // Lean smoothing
    private float _roll;
    private float _pitch;
    private float _rollVel;
    private float _pitchVel;

    // ── Public accessors for SpineAimController ──────────────────────────────
    /// <summary>Combined lateral lean + inertia sway roll (degrees). Used by SpineAimController.</summary>
    public float LeanRoll  => _roll  + _swayRoll;
    /// <summary>Combined forward lean + inertia sway pitch (degrees). Used by SpineAimController.</summary>
    public float LeanPitch => _pitch + _swayPitch;

    // Yaw tracking for angular velocity
    private float _prevYaw;

    // Inertia sway
    private bool  _swayActive;
    private float _swayTimer;
    private float _swayTargetPitch;
    private float _swayTargetRoll;
    private float _swayPitch;
    private float _swayRoll;
    private float _swayPitchVel;
    private float _swayRollVel;

    private Vector3 _prevVelocity;

    private void Awake()
    {
        var character = GetComponent<ExampleCharacterController>();
        _motor        = character.Motor;
        _meshRoot     = character.MeshRoot;
    }

    private void LateUpdate()
    {
        if (_meshRoot == null || _motor == null) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // ── Velocity in character-local space ────────────────────────────────
        Vector3 worldVel      = _motor.Velocity;
        Vector3 localVel      = _motor.transform.InverseTransformDirection(worldVel);
        float   horizontalSpd = Mathf.Sqrt(worldVel.x * worldVel.x + worldVel.z * worldVel.z);

        // ── Yaw angular velocity ─────────────────────────────────────────────
        float currentYaw  = _motor.transform.eulerAngles.y;
        float yawDelta    = Mathf.DeltaAngle(_prevYaw, currentYaw);
        float angularVelY = yawDelta / dt;           // °/s
        _prevYaw = currentYaw;

        // ── Target lean angles ───────────────────────────────────────────────
        // Normalise speed so lean is proportional — peak at ~6 m/s
        float speedT     = Mathf.Clamp01(horizontalSpd / 6f);

        float targetRoll = (-localVel.x * _maxLateralLeanDeg
                            - angularVelY * _turnLeanMultiplier) * speedT;
        targetRoll  = Mathf.Clamp(targetRoll, -_maxLateralLeanDeg, _maxLateralLeanDeg);

        float targetPitch = localVel.z * _maxForwardLeanDeg * 0.15f * speedT;
        targetPitch = Mathf.Clamp(targetPitch, -_maxForwardLeanDeg, _maxForwardLeanDeg);

        _roll  = Mathf.SmoothDamp(_roll,  targetRoll,  ref _rollVel,  _leanSmoothing);
        _pitch = Mathf.SmoothDamp(_pitch, targetPitch, ref _pitchVel, _pitchSmoothing);

        // ── Inertia sway detection ───────────────────────────────────────────
        float prevHorizSpd = Mathf.Sqrt(_prevVelocity.x * _prevVelocity.x
                                        + _prevVelocity.z * _prevVelocity.z);

        bool justStopped = !_swayActive
                           && prevHorizSpd > _swayTriggerSpeed
                           && horizontalSpd < _swayTriggerSpeed * 0.25f
                           && _motor.GroundingStatus.IsStableOnGround;

        if (justStopped)
            BeginInertiaSway(_prevVelocity, prevHorizSpd);

        _prevVelocity = worldVel;

        // ── Animate inertia sway ─────────────────────────────────────────────
        if (_swayActive)
        {
            _swayTimer += dt;
            float phase = _swayTimer / _swayDuration;

            // 0–40 %: snap toward overshoot, 40–100 %: decay back to zero
            float pTarget = phase < 0.4f ? _swayTargetPitch : 0f;
            float rTarget = phase < 0.4f ? _swayTargetRoll  : 0f;

            const float swaySmoothing = 0.055f;
            _swayPitch = Mathf.SmoothDamp(_swayPitch, pTarget, ref _swayPitchVel, swaySmoothing);
            _swayRoll  = Mathf.SmoothDamp(_swayRoll,  rTarget, ref _swayRollVel,  swaySmoothing);

            if (_swayTimer >= _swayDuration)
            {
                _swayActive    = false;
                _swayTimer     = 0f;
                _swayPitch     = 0f;
                _swayRoll      = 0f;
                _swayPitchVel  = 0f;
                _swayRollVel   = 0f;
            }
        }

        // ── Apply combined rotation to MeshRoot ──────────────────────────────
        float finalPitch = _pitch + _swayPitch;
        float finalRoll  = _roll  + _swayRoll;
        _meshRoot.localRotation = Quaternion.Euler(finalPitch, 0f, finalRoll);
    }

    private void BeginInertiaSway(Vector3 stoppedWorldVelocity, float speed)
    {
        _swayActive = true;
        _swayTimer  = 0f;

        Vector3 localStopped = _motor.transform.InverseTransformDirection(stoppedWorldVelocity);
        float   intensity    = Mathf.Clamp01(speed / 10f);

        // Overshoot in the direction of travel (forward sway) + lateral
        _swayTargetPitch = localStopped.z > 0f
            ?  intensity * _swayMaxDegrees
            : -intensity * _swayMaxDegrees * 0.6f;
        _swayTargetRoll = -localStopped.x * intensity * (_swayMaxDegrees * 0.5f);
    }
}
