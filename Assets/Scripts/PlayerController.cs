using UnityEngine;
using UnityEngine.InputSystem;
using KinematicCharacterController;
using KinematicCharacterController.Examples;

public class PlayerController : MonoBehaviour
{
    public ExampleCharacterController Character;
    public ExampleCharacterCamera CharacterCamera;

    [Header("Mouse Sensitivity")]
    public float MouseSensitivity = 1f;

    [Header("Sprint")]
    public float NormalSpeed = 5f;
    public float SprintSpeed = 9f;

    [Header("ADS (Aim Down Sights)")]
    public float ADSDistance = 0.25f;
    public float ADSFramingX = 0.15f;
    public float HipFramingX = 0.65f;
    public float FramingLerpSpeed = 10f;

    public bool IsADS => _isADS;

    private InputSystem_Actions _actions;
    private bool _jumpQueued;
    private bool _crouchDown;
    private bool _crouchUp;
    private bool _isADS;

    private void Awake()
    {
        _actions = new InputSystem_Actions();
    }

    private void OnEnable()
    {
        _actions.Player.Enable();
        _actions.Player.Jump.started += OnJump;
        _actions.Player.Crouch.started += OnCrouchDown;
        _actions.Player.Crouch.canceled += OnCrouchUp;
    }

    private void OnDisable()
    {
        _actions.Player.Jump.started -= OnJump;
        _actions.Player.Crouch.started -= OnCrouchDown;
        _actions.Player.Crouch.canceled -= OnCrouchUp;
        _actions.Player.Disable();
    }

    private void OnDestroy()
    {
        _actions.Dispose();
    }

    private void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;

        CharacterCamera.SetFollowTransform(Character.CameraFollowPoint);
        CharacterCamera.IgnoredColliders.Clear();
        CharacterCamera.IgnoredColliders.AddRange(Character.GetComponentsInChildren<Collider>());

        // PUBG-style camera defaults
        CharacterCamera.DefaultDistance = 2.5f;
        CharacterCamera.TargetDistance = 2.5f;
        CharacterCamera.MaxDistance = 3.5f;
        CharacterCamera.MinDistance = 0f;
        CharacterCamera.MinVerticalAngle = -60f;
        CharacterCamera.MaxVerticalAngle = 75f;
        CharacterCamera.FollowPointFraming = new Vector2(HipFramingX, 0f);
    }

    private void Update()
    {
        // Re-lock cursor on left click
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            Cursor.lockState = CursorLockMode.Locked;

        HandleCharacterInput();
    }

    private void LateUpdate()
    {
        // Sync camera with physics mover rotation if needed
        if (CharacterCamera.RotateWithPhysicsMover && Character.Motor.AttachedRigidbody != null)
        {
            var mover = Character.Motor.AttachedRigidbody.GetComponent<PhysicsMover>();
            if (mover != null)
            {
                CharacterCamera.PlanarDirection =
                    mover.RotationDeltaFromInterpolation * CharacterCamera.PlanarDirection;
                CharacterCamera.PlanarDirection =
                    Vector3.ProjectOnPlane(CharacterCamera.PlanarDirection, Character.Motor.CharacterUp).normalized;
            }
        }

        HandleCameraInput();

        // Reset per-frame flags after passing to character
        _jumpQueued = false;
        _crouchDown = false;
        _crouchUp = false;
    }

    private void HandleCameraInput()
    {
        Vector2 look = _actions.Player.Look.ReadValue<Vector2>() * MouseSensitivity;

        if (Cursor.lockState != CursorLockMode.Locked)
            look = Vector2.zero;

        CharacterCamera.UpdateWithInput(Time.deltaTime, 0f, new Vector3(look.x, look.y, 0f));

        // Right-click ADS toggle
        if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
            _isADS = !_isADS;

        // ADS camera behavior: smooth transition to tight over-shoulder + first-person-ready position
        float targetDist = _isADS ? ADSDistance : CharacterCamera.DefaultDistance;
        CharacterCamera.TargetDistance = targetDist;

        // Smooth shoulder-offset lerp between hip-fire and ADS
        float targetFramingX = _isADS ? ADSFramingX : HipFramingX;
        float currentFraming = CharacterCamera.FollowPointFraming.x;
        CharacterCamera.FollowPointFraming = new Vector2(
            Mathf.Lerp(currentFraming, targetFramingX, Time.deltaTime * FramingLerpSpeed),
            0f
        );
    }

    private void HandleCharacterInput()
    {
        Vector2 move = _actions.Player.Move.ReadValue<Vector2>();

        // Apply sprint speed
        bool sprinting = InputReader.Instance != null && InputReader.Instance.SprintHeld;
        Character.MaxStableMoveSpeed = sprinting ? SprintSpeed : NormalSpeed;

        // Orientation: face movement direction when running free, face camera when aiming
        Character.OrientationMethod = _isADS
            ? OrientationMethod.TowardsCamera
            : OrientationMethod.TowardsMovement;

        PlayerCharacterInputs inputs = new PlayerCharacterInputs
        {
            MoveAxisForward  = move.y,
            MoveAxisRight    = move.x,
            CameraRotation   = CharacterCamera.Transform.rotation,
            JumpDown         = _jumpQueued,
            CrouchDown       = _crouchDown,
            CrouchUp         = _crouchUp,
        };

        Character.SetInputs(ref inputs);
    }

    private void OnJump(InputAction.CallbackContext ctx)    => _jumpQueued  = true;
    private void OnCrouchDown(InputAction.CallbackContext ctx) => _crouchDown = true;
    private void OnCrouchUp(InputAction.CallbackContext ctx)   => _crouchUp   = true;
}
