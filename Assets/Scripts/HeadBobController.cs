using UnityEngine;
using KinematicCharacterController;

/// <summary>
/// Drives CameraFollowPoint with a velocity-scaled sinusoidal head bob.
/// Suppresses bob when airborne and blends it back smoothly on landing.
///
/// Attach to the same GameObject as the Animator (Survivalist child).
/// Wire _cameraFollowPoint and _motor in the Inspector.
/// </summary>
public class HeadBobController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform               _cameraFollowPoint;
    [SerializeField] private KinematicCharacterMotor _motor;

    [Header("Bob Settings")]
    [Tooltip("Base vertical bob frequency in Hz at walk speed.")]
    [SerializeField] private float _walkFrequency    = 2.0f;
    [Tooltip("Base vertical bob amplitude in metres at walk speed.")]
    [SerializeField] private float _walkAmplitude    = 0.030f;
    [Tooltip("Sprint frequency multiplier.")]
    [SerializeField] private float _sprintFreqMult   = 1.55f;
    [Tooltip("Sprint amplitude multiplier.")]
    [SerializeField] private float _sprintAmpMult    = 1.75f;
    [Tooltip("Speed (m/s) at which bob reaches full amplitude.")]
    [SerializeField] private float _fullSpeedRef     = 6f;

    [Header("Rest Position")]
    [Tooltip("Local Y of CameraFollowPoint when not bobbing. Should match scene value (1.6).")]
    [SerializeField] private float _restY            = 1.6f;
    [Tooltip("SmoothDamp time for position transitions.")]
    [SerializeField] private float _smoothTime       = 0.05f;
    [Tooltip("Seconds to suppress bob after leaving ground.")]
    [SerializeField] private float _airSuppressTime  = 0.20f;

    // ── Internal ─────────────────────────────────────────────────────────────
    private float   _bobTimer;
    private float   _suppressWeight  = 1f;
    private float   _suppressVel;
    private Vector3 _currentOffset;
    private Vector3 _offsetVel;

    private void LateUpdate()
    {
        if (_cameraFollowPoint == null || _motor == null) return;

        float dt            = Time.deltaTime;
        bool  grounded      = _motor.GroundingStatus.IsStableOnGround;
        bool  sprinting     = InputReader.Instance != null && InputReader.Instance.SprintHeld;
        Vector3 horizVel    = Vector3.ProjectOnPlane(_motor.Velocity, Vector3.up);
        float horizontalSpd = horizVel.magnitude;

        float speedT = Mathf.Clamp01(horizontalSpd / _fullSpeedRef);

        // ── Air suppression ───────────────────────────────────────────────────
        float suppressTarget = grounded ? 1f : 0f;
        _suppressWeight = Mathf.SmoothDamp(_suppressWeight, suppressTarget,
                                            ref _suppressVel, _airSuppressTime);

        // ── Bob oscillation ───────────────────────────────────────────────────
        float freq = _walkFrequency * (sprinting ? _sprintFreqMult : 1f);
        float amp  = _walkAmplitude * (sprinting ? _sprintAmpMult  : 1f) * speedT;

        _bobTimer += dt * freq * (Mathf.PI * 2f);

        float bobY = Mathf.Sin(_bobTimer)         * amp * _suppressWeight;
        float bobX = Mathf.Sin(_bobTimer * 0.5f)  * amp * 0.5f * _suppressWeight;

        // ── Smoothed application ──────────────────────────────────────────────
        Vector3 target = new Vector3(bobX, _restY + bobY, 0f);
        _currentOffset = Vector3.SmoothDamp(_currentOffset, target, ref _offsetVel, _smoothTime);
        _cameraFollowPoint.localPosition = _currentOffset;
    }

    private void OnEnable()
    {
        // Seed rest position so there is no snap on first frame
        if (_cameraFollowPoint != null)
        {
            _currentOffset = new Vector3(0f, _restY, 0f);
            _cameraFollowPoint.localPosition = _currentOffset;
        }
    }
}
