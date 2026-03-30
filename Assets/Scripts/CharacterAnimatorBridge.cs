using UnityEngine;
using UnityEngine.InputSystem;
using KinematicCharacterController;
using KinematicCharacterController.Examples;

/// <summary>
/// Translates KCC Motor state + InputReader into Animator parameters every frame.
/// Attach to the Player GameObject (same as ExampleCharacterController).
/// Drag the Survivalist child's Animator into _animator in the Inspector.
/// </summary>
[RequireComponent(typeof(ExampleCharacterController))]
public class CharacterAnimatorBridge : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Animator _animator;

    [Header("Speed Mapping")]
    [Tooltip("Motor speed (m/s) that maps to the blend-tree's Run threshold (6).")]
    [SerializeField] private float _runSpeedReference = 10f;
    [Tooltip("FreeFall grace period — seconds in air before FreeFall triggers.")]
    [SerializeField] private float _freeFallGrace = 0.15f;
    [Tooltip("Jump detection window — seconds after jump input that we allow the Jump bool to fire.")]
    [SerializeField] private float _jumpWindow = 0.35f;

    // ── Cached Animator hashes ───────────────────────────────────────────────
    private static readonly int SpeedHash       = Animator.StringToHash("Speed");
    private static readonly int MotionSpeedHash = Animator.StringToHash("MotionSpeed");
    private static readonly int GroundedHash    = Animator.StringToHash("Grounded");
    private static readonly int JumpHash        = Animator.StringToHash("Jump");
    private static readonly int FreeFallHash    = Animator.StringToHash("FreeFall");
    private static readonly int IsSprintingHash = Animator.StringToHash("IsSprinting");

    // ── Internal state ───────────────────────────────────────────────────────
    private ExampleCharacterController _character;
    private KinematicCharacterMotor    _motor;
    private InputSystem_Actions        _actions;

    private float _speedSmooth;
    private float _speedSmoothVel;
    private float _fallTimer;
    private bool  _prevGrounded;
    private float _jumpRequestedTime = -999f;   // Time.time when jump was last pressed

    private void Awake()
    {
        _character = GetComponent<ExampleCharacterController>();
        _motor     = _character.Motor;

        // Subscribe to jump input independently — allows reading the frame-exact press
        _actions = new InputSystem_Actions();
        _actions.Player.Jump.started += OnJumpPressed;
    }

    private void OnEnable()  => _actions.Player.Enable();
    private void OnDisable() => _actions.Player.Disable();
    private void OnDestroy() => _actions?.Dispose();

    private void OnJumpPressed(InputAction.CallbackContext _) =>
        _jumpRequestedTime = Time.time;

    private void Update()
    {
        if (_animator == null) return;

        float dt = Time.deltaTime;

        // ── Horizontal speed ─────────────────────────────────────────────────
        Vector3 horizVel      = Vector3.ProjectOnPlane(_motor.Velocity, _motor.CharacterUp);
        float   horizontalSpd = horizVel.magnitude;
        bool    grounded      = _motor.GroundingStatus.IsStableOnGround;
        bool    sprinting     = InputReader.Instance != null && InputReader.Instance.SprintHeld;
        bool    crouching     = InputReader.Instance != null && InputReader.Instance.CrouchHeld;

        // Map 0…_runSpeedReference → 0…6 (walk=2, run=6) for the blend tree
        float speedNorm = horizontalSpd * (6f / Mathf.Max(_runSpeedReference, 0.01f));
        if (sprinting)
            speedNorm = Mathf.Max(speedNorm, 9f * Mathf.Clamp01(horizontalSpd / _runSpeedReference));

        // Halve speed contribution when crouching so walk/idle clip plays
        if (crouching) speedNorm *= 0.5f;

        _speedSmooth = Mathf.SmoothDamp(_speedSmooth, speedNorm, ref _speedSmoothVel, 0.10f);
        _animator.SetFloat(SpeedHash, _speedSmooth);

        // ── MotionSpeed — analog input magnitude ─────────────────────────────
        float motionSpeed = (InputReader.Instance != null)
            ? InputReader.Instance.MoveInput.magnitude
            : Mathf.Clamp01(horizontalSpd / _runSpeedReference);
        _animator.SetFloat(MotionSpeedHash, motionSpeed);

        // ── Grounded ─────────────────────────────────────────────────────────
        _animator.SetBool(GroundedHash, grounded);

        // ── Jump bool (pulse: set true for one frame when leaving ground after jump input) ──
        bool justLeftGround = _prevGrounded && !grounded;
        bool jumpRecent     = (Time.time - _jumpRequestedTime) < _jumpWindow;

        if (justLeftGround && jumpRecent)
            _animator.SetBool(JumpHash, true);
        if (grounded)
            _animator.SetBool(JumpHash, false);

        _prevGrounded = grounded;

        // ── FreeFall (airborne longer than grace period, not a jump) ─────────
        if (!grounded && !jumpRecent)
            _fallTimer += dt;
        else
            _fallTimer = 0f;
        _animator.SetBool(FreeFallHash, _fallTimer > _freeFallGrace);

        // ── IsSprinting ───────────────────────────────────────────────────────
        _animator.SetBool(IsSprintingHash, sprinting && horizontalSpd > 1f);
    }
}
