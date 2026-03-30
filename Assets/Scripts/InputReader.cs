using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Singleton input hub. Every game-feel system reads from here instead of
/// spinning up its own InputSystem_Actions instance.
///
/// Setup: Add this component to any persistent GameObject (e.g., the Player).
/// </summary>
public class InputReader : MonoBehaviour
{
    public static InputReader Instance { get; private set; }

    // ── Polled values (safe to read from any MonoBehaviour.Update) ──────────
    public Vector2 MoveInput  { get; private set; }
    public Vector2 LookInput  { get; private set; }
    public bool    SprintHeld { get; private set; }
    public bool    CrouchHeld { get; private set; }
    /// <summary>True for exactly one Update frame when Jump is first pressed.</summary>
    public bool    JumpDown   { get; private set; }

    private InputSystem_Actions _actions;
    private bool                _jumpDownThisFrame;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        _actions = new InputSystem_Actions();
    }

    private void OnEnable()
    {
        _actions.Player.Enable();
        _actions.Player.Jump.started += OnJumpStarted;
    }

    private void OnDisable()
    {
        _actions.Player.Jump.started -= OnJumpStarted;
        _actions.Player.Disable();
    }

    private void OnDestroy() => _actions?.Dispose();

    private void OnJumpStarted(InputAction.CallbackContext _) => _jumpDownThisFrame = true;

    private void Update()
    {
        MoveInput  = _actions.Player.Move.ReadValue<Vector2>();
        LookInput  = _actions.Player.Look.ReadValue<Vector2>();
        SprintHeld = _actions.Player.Sprint.IsPressed();
        CrouchHeld = _actions.Player.Crouch.IsPressed();

        // Consume the frame-flag set by the callback
        JumpDown           = _jumpDownThisFrame;
        _jumpDownThisFrame = false;
    }
}
