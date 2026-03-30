using UnityEngine;
using KinematicCharacterController;

/// <summary>
/// Triggers dust-puff particle effects at the correct foot bone world-position
/// using distance-accumulation (no animation events required).
///
/// Attach to the same GameObject as the Animator (Survivalist child).
/// Assign _motor and _dustParticle in the Inspector.
/// _dustParticle should be a child ParticleSystem configured as:
///   Stop Action = None, Play On Awake = false, Loop = false, Simulation Space = World,
///   Shape = Cone (angle 30°, radius 0.1m), small lifetime (0.35s), 2–4 particles per burst.
/// </summary>
public class FootstepController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private KinematicCharacterMotor _motor;
    [SerializeField] private ParticleSystem          _dustParticle;

    [Header("Step Distances")]
    [Tooltip("Horizontal metres between steps at walk speed.")]
    [SerializeField] private float _walkStepDistance   = 1.20f;
    [Tooltip("Horizontal metres between steps when sprinting.")]
    [SerializeField] private float _sprintStepDistance = 1.75f;
    [Tooltip("Minimum horizontal speed (m/s) required to trigger steps.")]
    [SerializeField] private float _minSpeed           = 0.25f;

    [Header("Particle Burst")]
    [Tooltip("Particles emitted per footstep.")]
    [SerializeField] private int _particleCount = 3;

    // ── Internal ─────────────────────────────────────────────────────────────
    private Animator _animator;
    private float    _distAccumulated;
    private bool     _lastStepWasLeft;

    private void Awake()
    {
        _animator = GetComponent<Animator>();
    }

    private void Update()
    {
        if (_motor == null || _animator == null || !_animator.isHuman) return;
        if (!_motor.GroundingStatus.IsStableOnGround)
        {
            _distAccumulated = 0f;
            return;
        }

        Vector3 horizVel      = Vector3.ProjectOnPlane(_motor.Velocity, Vector3.up);
        float   horizontalSpd = horizVel.magnitude;

        if (horizontalSpd < _minSpeed) return;

        _distAccumulated += horizontalSpd * Time.deltaTime;

        bool  sprinting = InputReader.Instance != null && InputReader.Instance.SprintHeld;
        float threshold = sprinting ? _sprintStepDistance : _walkStepDistance;

        if (_distAccumulated >= threshold)
        {
            _distAccumulated -= threshold;
            _lastStepWasLeft  = !_lastStepWasLeft;
            TriggerFootstep(_lastStepWasLeft);
        }
    }

    private void TriggerFootstep(bool leftFoot)
    {
        if (_dustParticle == null) return;

        HumanBodyBones footBone = leftFoot ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot;
        Transform       foot    = _animator.GetBoneTransform(footBone);

        Vector3 spawnPos = foot != null ? foot.position : transform.position;
        // Keep Y at ground level — avoid spawning inside terrain
        spawnPos.y = _motor.TransientPosition.y;

        _dustParticle.transform.position = spawnPos;
        _dustParticle.Emit(_particleCount);
    }
}
