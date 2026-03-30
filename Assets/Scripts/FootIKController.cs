using UnityEngine;
using KinematicCharacterController;

/// <summary>
/// Humanoid foot IK — plants feet precisely on uneven terrain by raycasting
/// from each foot bone and driving Animator IK goals.
///
/// Attach to the same GameObject as the Animator (Survivalist child).
/// Wire _motor from the parent Player.
/// </summary>
public class FootIKController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private KinematicCharacterMotor _motor;

    [Header("Raycast")]
    [SerializeField] private LayerMask _groundMask = ~0;
    [Tooltip("Ray starts this far ABOVE the animated foot bone position.")]
    [SerializeField] private float _rayOriginUp    = 0.5f;
    [Tooltip("Total ray length downward from origin.")]
    [SerializeField] private float _rayLength      = 1.0f;
    [Tooltip("Clearance added above the hit point for the heel.")]
    [SerializeField] private float _footHeelHeight = 0.08f;

    [Header("IK Weights")]
    [Tooltip("Distance above ground at which IK weight begins to fade out.")]
    [SerializeField] private float _ikFadeRange    = 0.30f;
    [SerializeField] private float _weightSmoothTime = 0.12f;

    [Header("Pelvis Anti-Stretch")]
    [SerializeField] private float _pelvisSmoothTime = 0.10f;

    // ── Internal ─────────────────────────────────────────────────────────────
    private Animator _animator;

    private float    _leftWeight,  _rightWeight;
    private float    _leftWVel,    _rightWVel;
    private Vector3  _leftPos,     _rightPos;
    private Quaternion _leftRot,   _rightRot;
    private float    _pelvisOffset, _pelvisVel;

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        // Seed positions to avoid a snap on the first frame
        if (_animator != null && _animator.isHuman)
        {
            Transform lf = _animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform rf = _animator.GetBoneTransform(HumanBodyBones.RightFoot);
            if (lf != null) _leftPos  = lf.position;
            if (rf != null) _rightPos = rf.position;
            _leftRot  = Quaternion.identity;
            _rightRot = Quaternion.identity;
        }
    }

    // OnAnimatorIK is called by Unity after animation sampling, before IK solving.
    private void OnAnimatorIK(int layerIndex)
    {
        if (layerIndex != 0 || _animator == null || !_animator.isHuman) return;

        bool grounded = _motor != null && _motor.GroundingStatus.IsStableOnGround;

        UpdateFootIK(AvatarIKGoal.LeftFoot,  HumanBodyBones.LeftFoot,
                     ref _leftPos,  ref _leftRot,  ref _leftWeight,  ref _leftWVel,  grounded);

        UpdateFootIK(AvatarIKGoal.RightFoot, HumanBodyBones.RightFoot,
                     ref _rightPos, ref _rightRot, ref _rightWeight, ref _rightWVel, grounded);

        AdjustPelvis();
    }

    private void UpdateFootIK(
        AvatarIKGoal   goal,
        HumanBodyBones bone,
        ref Vector3    ikPos,
        ref Quaternion ikRot,
        ref float      weight,
        ref float      weightVel,
        bool           grounded)
    {
        Transform footBone = _animator.GetBoneTransform(bone);
        if (footBone == null) return;

        float targetWeight = 0f;

        Vector3 origin = footBone.position + Vector3.up * _rayOriginUp;
        if (grounded && Physics.Raycast(origin, Vector3.down, out RaycastHit hit,
                                         _rayLength, _groundMask, QueryTriggerInteraction.Ignore))
        {
            float distAboveGround = hit.distance - _rayOriginUp;   // how far foot is above actual ground
            targetWeight = Mathf.Clamp01(1f - distAboveGround / _ikFadeRange);

            // IK target: hit point with heel clearance, foot oriented to surface normal
            ikPos = hit.point + hit.normal * _footHeelHeight;
            ikRot = Quaternion.LookRotation(
                Vector3.ProjectOnPlane(footBone.forward, hit.normal),
                hit.normal);
        }

        weight = Mathf.SmoothDamp(weight, targetWeight, ref weightVel, _weightSmoothTime);

        _animator.SetIKPositionWeight(goal, weight);
        _animator.SetIKRotationWeight(goal, weight);
        _animator.SetIKPosition(goal, ikPos);
        _animator.SetIKRotation(goal, ikRot);
    }

    private void AdjustPelvis()
    {
        // Lower the pelvis by the amount of the most-lowered foot so legs never hyper-extend
        Transform pelvisBone = _animator.GetBoneTransform(HumanBodyBones.Hips);
        if (pelvisBone == null) return;

        Transform lf = _animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        Transform rf = _animator.GetBoneTransform(HumanBodyBones.RightFoot);
        if (lf == null || rf == null) return;

        float leftDelta  = (_leftPos.y  - lf.position.y)  * _leftWeight;
        float rightDelta = (_rightPos.y - rf.position.y) * _rightWeight;
        float target     = Mathf.Min(0f, Mathf.Min(leftDelta, rightDelta));

        _pelvisOffset = Mathf.SmoothDamp(_pelvisOffset, target, ref _pelvisVel, _pelvisSmoothTime);
        _animator.bodyPosition = pelvisBone.position + Vector3.up * _pelvisOffset;
    }
}
