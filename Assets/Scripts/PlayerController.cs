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

    private InputSystem_Actions _actions;
    private bool _jumpQueued;
    private bool _crouchDown;
    private bool _crouchUp;

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

        // Scroll to zoom
        float scroll = 0f;
#if !UNITY_WEBGL
        if (Mouse.current != null)
            scroll = -Mouse.current.scroll.ReadValue().y * 0.01f;
#endif

        CharacterCamera.UpdateWithInput(Time.deltaTime, scroll, new Vector3(look.x, look.y, 0f));

        // Right-click ADS toggle
        if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
            CharacterCamera.TargetDistance =
                CharacterCamera.TargetDistance == 0f ? CharacterCamera.DefaultDistance : 0f;
    }

    private void HandleCharacterInput()
    {
        Vector2 move = _actions.Player.Move.ReadValue<Vector2>();

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
