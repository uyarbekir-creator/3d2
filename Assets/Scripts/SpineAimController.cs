using UnityEngine;

/// <summary>
/// Procedurally rotates the spine bones (spine_01/02/03) to:
///   • Distribute camera pitch (look up/down) across all three vertebrae.
///   • Mirror ProceduralAnimator's lean roll into the spine for physical weight.
///   • Bend forward when PhysicsInteractionDetector reports a push.
///   • Add a slight forward tuck when crouching.
///
/// Attach to the same GameObject as the Animator (Survivalist child).
/// ProceduralAnimator and PhysicsInteractionDetector are auto-resolved from parent.
/// </summary>
public class SpineAimController : MonoBehaviour
{
    [Header("Camera Pitch")]
    [SerializeField] private float _lookSensitivity = 1.8f;
    [SerializeField] private float _minPitch = -70f;
    [SerializeField] private float _maxPitch =  80f;

    [Header("Push Pose")]
    [Tooltip("Max forward bend (degrees) when InteractionWeight = 1.")]
    [SerializeField] private float _maxPushForwardDeg = 12f;

    [Header("Crouch Tuck")]
    [Tooltip("Forward spine bend added when crouching.")]
    [SerializeField] private float _crouchTuckDeg     = 8f;
    [SerializeField] private float _crouchTuckSmooth  = 0.18f;

    // ── Dependencies (auto-resolved) ─────────────────────────────────────────
    private ProceduralAnimator         _procAnim;
    private PhysicsInteractionDetector _detector;
    private Animator                   _animator;

    // ── State ────────────────────────────────────────────────────────────────
    private float _pitchAngle;
    private float _crouchTuck;
    private float _crouchTuckVel;

    private void Awake()
    {
        _animator  = GetComponent<Animator>();
        _procAnim  = GetComponentInParent<ProceduralAnimator>();
        _detector  = GetComponentInParent<PhysicsInteractionDetector>();

        if (_procAnim == null)
            Debug.LogWarning("[SpineAimController] ProceduralAnimator not found in parent hierarchy.");
        if (_detector == null)
            Debug.LogWarning("[SpineAimController] PhysicsInteractionDetector not found in parent hierarchy.");
    }

    private void Update()
    {
        // Accumulate camera pitch from look input (same axis PlayerController uses)
        if (InputReader.Instance != null)
        {
            _pitchAngle -= InputReader.Instance.LookInput.y * _lookSensitivity * Time.deltaTime;
            _pitchAngle  = Mathf.Clamp(_pitchAngle, _minPitch, _maxPitch);
        }

        // Crouch tuck (smooth on/off)
        float crouchTarget = (InputReader.Instance != null && InputReader.Instance.CrouchHeld)
            ? _crouchTuckDeg : 0f;
        _crouchTuck = Mathf.SmoothDamp(_crouchTuck, crouchTarget,
                                        ref _crouchTuckVel, _crouchTuckSmooth);
    }

    // OnAnimatorIK fires after animation sampling — safe to override bone rotations here.
    private void OnAnimatorIK(int layerIndex)
    {
        if (layerIndex != 0 || _animator == null || !_animator.isHuman) return;

        float leanRoll = _procAnim != null ? _procAnim.LeanRoll  : 0f;

        // Push: bend toward PushDirection (convert to local spine space: dot with forward)
        float pushFwd = 0f;
        if (_detector != null && _detector.InteractionWeight > 0.01f)
        {
            Vector3 pushDir = _detector.PushDirection;
            float   fwdDot  = Vector3.Dot(transform.forward, pushDir);
            pushFwd = fwdDot * _detector.InteractionWeight * _maxPushForwardDeg;
        }

        // Distribute evenly across three vertebrae
        float pitchPer  = _pitchAngle / 3f;
        float rollPer   = leanRoll    / 3f;
        float extraFwd  = (pushFwd + _crouchTuck) / 3f;

        Quaternion delta = Quaternion.Euler(pitchPer + extraFwd, 0f, rollPer);

        RotateBone(HumanBodyBones.Spine,      delta);
        RotateBone(HumanBodyBones.Chest,      delta);
        RotateBone(HumanBodyBones.UpperChest, delta);
        // neck_01 / head deliberately excluded — avoids double-bend artefact
    }

    private void RotateBone(HumanBodyBones bone, Quaternion delta)
    {
        Transform t = _animator.GetBoneTransform(bone);
        if (t != null) t.localRotation *= delta;
    }
}
