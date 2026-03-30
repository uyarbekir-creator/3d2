using UnityEngine;
using Unity.Cinemachine;
using KinematicCharacterController;
using KinematicCharacterController.Examples;

/// <summary>
/// Smoothly adjusts a CinemachineCamera's Field of View based on character
/// speed and sprint state read from InputReader.Instance.SprintHeld.
///
/// • Walking slowly → narrower FOV (focused, deliberate).
/// • At run speed     → default FOV (balanced view).
/// • Sprinting        → wider FOV (kinetic, sense of scale and speed).
///
/// All transitions use Mathf.SmoothDamp for frame-rate-independent smoothing.
///
/// Setup:
///   1. Add this component to the player GameObject.
///   2. Assign a CinemachineCamera to _virtualCamera in the Inspector.
///   3. Ensure InputReader is present in the scene.
/// </summary>
[RequireComponent(typeof(ExampleCharacterController))]
public class CinemachineFOVController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CinemachineCamera _virtualCamera;

    [Header("FOV Targets (degrees)")]
    [Tooltip("FOV when moving slowly / standing still — narrow for focus.")]
    [SerializeField] private float _walkFOV    = 60f;
    [Tooltip("FOV at normal run speed.")]
    [SerializeField] private float _defaultFOV = 68f;
    [Tooltip("FOV when sprinting — wide for speed sensation.")]
    [SerializeField] private float _sprintFOV  = 80f;

    [Header("Transition")]
    [Tooltip("SmoothDamp time for FOV changes (seconds). Lower = snappier.")]
    [SerializeField] private float _smoothTime         = 0.28f;
    [Tooltip("Horizontal speed (m/s) considered 'at full sprint'. FOV widens beyond this.")]
    [SerializeField] private float _fullSpeedThreshold = 8f;

    // ── Internal state ──────────────────────────────────────────────────────
    private KinematicCharacterMotor _motor;
    private float _currentFOV;
    private float _fovVelocity;

    private void Awake()
    {
        _motor = GetComponent<ExampleCharacterController>().Motor;

        // Seed from whichever camera source is available
        if (_virtualCamera != null)
            _currentFOV = _virtualCamera.Lens.FieldOfView;
        else if (Camera.main != null)
            _currentFOV = Camera.main.fieldOfView;
        else
            _currentFOV = _defaultFOV;
    }

    private void Update()
    {
        float targetFOV = ComputeTargetFOV();
        _currentFOV = Mathf.SmoothDamp(_currentFOV, targetFOV, ref _fovVelocity, _smoothTime);

        if (_virtualCamera != null)
        {
            // Lens is a value-type in Cinemachine 3.x — must copy, modify, reassign
            LensSettings lens   = _virtualCamera.Lens;
            lens.FieldOfView    = _currentFOV;
            _virtualCamera.Lens = lens;
        }
        else if (Camera.main != null)
        {
            // Fallback: drive the plain Camera directly when no CinemachineCamera is wired
            Camera.main.fieldOfView = _currentFOV;
        }
    }

    private float ComputeTargetFOV()
    {
        Vector3 vel           = _motor.Velocity;
        float   horizontalSpd = Mathf.Sqrt(vel.x * vel.x + vel.z * vel.z);
        bool    sprinting     = InputReader.Instance != null && InputReader.Instance.SprintHeld;

        if (sprinting)
        {
            // When sprint is held, blend between defaultFOV and sprintFOV based on speed
            float sprintT = Mathf.Clamp01(horizontalSpd / _fullSpeedThreshold);
            return Mathf.Lerp(_defaultFOV, _sprintFOV, sprintT);
        }
        else
        {
            // Walk→Run: blend between walkFOV (narrow) and defaultFOV (wide)
            float walkT = Mathf.Clamp01(horizontalSpd / (_fullSpeedThreshold * 0.65f));
            return Mathf.Lerp(_walkFOV, _defaultFOV, walkT);
        }
    }
}
