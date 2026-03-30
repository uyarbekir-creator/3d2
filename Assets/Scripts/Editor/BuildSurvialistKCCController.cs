// Assets/Scripts/Editor/BuildSurvialistKCCController.cs
// Run via: Unity menu  Tools > Build Survivalist KCC Controller
// OR called automatically from script-execute during scene setup.
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class BuildSurvialistKCCController
{
    private const string ControllerPath = "Assets/Scripts/Animations/SurvialistKCC.controller";

    // ── Animation clip asset paths ───────────────────────────────────────────
    private const string AnimBase = "Assets/Survivalist/StarterAssets/ThirdPersonController/Character/Animations/";
    private const string ClipIdle         = AnimBase + "Stand--Idle.anim.fbx";
    private const string ClipWalk         = AnimBase + "Locomotion--Walk_N.anim.fbx";
    private const string ClipRun          = AnimBase + "Locomotion--Run_N.anim.fbx";
    private const string ClipSprint       = AnimBase + "Locomotion--Run_S.anim.fbx";
    private const string ClipJump         = AnimBase + "Jump--Jump.anim.fbx";
    private const string ClipInAir        = AnimBase + "Jump--InAir.anim.fbx";
    private const string ClipWalkLand     = AnimBase + "Locomotion--Walk_N_Land.anim.fbx";
    private const string ClipRunLand      = AnimBase + "Locomotion--Run_N_Land.anim.fbx";

    [MenuItem("Tools/Build Survivalist KCC Controller")]
    public static void Build()
    {
        // ── Load clips ───────────────────────────────────────────────────────
        AnimationClip idle      = LoadClip(ClipIdle);
        AnimationClip walk      = LoadClip(ClipWalk);
        AnimationClip run       = LoadClip(ClipRun);
        AnimationClip sprint    = LoadClip(ClipSprint);
        AnimationClip jump      = LoadClip(ClipJump);
        AnimationClip inAir     = LoadClip(ClipInAir);
        AnimationClip walkLand  = LoadClip(ClipWalkLand);
        AnimationClip runLand   = LoadClip(ClipRunLand);

        // ── Create or overwrite controller ───────────────────────────────────
        AnimatorController ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        // ── Parameters ───────────────────────────────────────────────────────
        ctrl.AddParameter("Speed",       AnimatorControllerParameterType.Float);
        ctrl.AddParameter("MotionSpeed", AnimatorControllerParameterType.Float);
        ctrl.AddParameter("Grounded",    AnimatorControllerParameterType.Bool);
        ctrl.AddParameter("Jump",        AnimatorControllerParameterType.Bool);
        ctrl.AddParameter("FreeFall",    AnimatorControllerParameterType.Bool);
        ctrl.AddParameter("IsSprinting", AnimatorControllerParameterType.Bool);

        // Default parameter values
        foreach (var p in ctrl.parameters)
        {
            if (p.name == "Grounded")    { var np = p; np.defaultBool = true; }
            if (p.name == "MotionSpeed") { var np = p; np.defaultFloat = 1f; }
        }

        // ── Base layer ───────────────────────────────────────────────────────
        AnimatorStateMachine sm = ctrl.layers[0].stateMachine;
        sm.entryPosition    = new Vector3(-200,   0, 0);
        sm.anyStatePosition = new Vector3(-200, 200, 0);
        sm.exitPosition     = new Vector3(-200, 400, 0);

        // Enable IK pass on base layer
        var baseLayer = ctrl.layers[0];
        baseLayer.iKPass = true;
        ctrl.layers = new[] { baseLayer };

        // ── State: IdleWalkRunBlend (blend tree) ─────────────────────────────
        AnimatorState blendState = sm.AddState("IdleWalkRunBlend", new Vector3(200, 0, 0));
        blendState.iKOnFeet = true;

        BlendTree locomotionTree;
        ctrl.CreateBlendTreeInController("LocomotionBlend", out locomotionTree);
        locomotionTree.blendParameter      = "Speed";
        locomotionTree.blendType           = BlendTreeType.Simple1D;
        locomotionTree.useAutomaticThresholds = false;

        if (idle    != null) locomotionTree.AddChild(idle,    0f);
        if (walk    != null) locomotionTree.AddChild(walk,    2f);
        if (run     != null) locomotionTree.AddChild(run,     6f);
        if (sprint  != null) locomotionTree.AddChild(sprint,  9f);

        // Wire MotionSpeed as time-scale multiplier on each child
        var children = locomotionTree.children;
        for (int i = 0; i < children.Length; i++)
        {
            children[i].timeScale = 1f;   // kept at 1; MotionSpeed is set on the Animator
        }
        locomotionTree.children = children;

        // The blend tree was created as a root; move it into blendState
        blendState.motion = locomotionTree;

        // Remove the auto-created root blend tree state
        foreach (var s in sm.states)
        {
            if (s.state != blendState && s.state.name == "LocomotionBlend")
                sm.RemoveState(s.state);
        }

        sm.defaultState = blendState;

        // ── State: JumpStart ─────────────────────────────────────────────────
        AnimatorState jumpState = sm.AddState("JumpStart", new Vector3(500, -120, 0));
        jumpState.motion    = jump;
        jumpState.iKOnFeet  = true;
        jumpState.speed     = 1f;

        // ── State: InAir ─────────────────────────────────────────────────────
        AnimatorState inAirState = sm.AddState("InAir", new Vector3(500, 0, 0));
        inAirState.motion   = inAir;
        inAirState.iKOnFeet = true;
        inAirState.speed    = 1f;

        // ── State: JumpLand (blend tree for walk vs run landing) ─────────────
        AnimatorState landState = sm.AddState("JumpLand", new Vector3(500, 120, 0));
        landState.iKOnFeet = true;

        BlendTree landTree;
        ctrl.CreateBlendTreeInController("LandBlend", out landTree);
        landTree.blendParameter         = "Speed";
        landTree.blendType              = BlendTreeType.Simple1D;
        landTree.useAutomaticThresholds = false;
        if (walkLand != null) landTree.AddChild(walkLand, 0f);
        if (runLand  != null) landTree.AddChild(runLand,  4f);
        landState.motion = landTree;

        // Remove auto-created LandBlend root state
        foreach (var s in sm.states)
        {
            if (s.state != landState && s.state.name == "LandBlend")
                sm.RemoveState(s.state);
        }

        // ── Transitions ───────────────────────────────────────────────────────
        // IdleWalkRunBlend → JumpStart  (Jump==true)
        var t1 = blendState.AddTransition(jumpState);
        t1.AddCondition(AnimatorConditionMode.If, 0, "Jump");
        t1.hasExitTime      = false;
        t1.duration         = 0.10f;
        t1.offset           = 0f;

        // JumpStart → InAir  (exit time 0.5)
        var t2 = jumpState.AddTransition(inAirState);
        t2.hasExitTime      = true;
        t2.exitTime         = 0.50f;
        t2.duration         = 0.10f;

        // InAir → JumpLand  (Grounded==true)
        var t3 = inAirState.AddTransition(landState);
        t3.AddCondition(AnimatorConditionMode.If, 0, "Grounded");
        t3.hasExitTime      = false;
        t3.duration         = 0.13f;
        t3.offset           = 0.05f;

        // JumpLand → IdleWalkRunBlend  (exit time 0.9)
        var t4 = landState.AddTransition(blendState);
        t4.hasExitTime      = true;
        t4.exitTime         = 0.90f;
        t4.duration         = 0.25f;

        // IdleWalkRunBlend → InAir  (FreeFall==true)
        var t5 = blendState.AddTransition(inAirState);
        t5.AddCondition(AnimatorConditionMode.If, 0, "FreeFall");
        t5.hasExitTime      = false;
        t5.duration         = 0.15f;

        // ── Save ──────────────────────────────────────────────────────────────
        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[BuildSurvialistKCCController] Controller saved to {ControllerPath}");
    }

    private static AnimationClip LoadClip(string path)
    {
        // FBX files can contain multiple clips; load the first AnimationClip sub-asset
        var assets = AssetDatabase.LoadAllAssetsAtPath(path);
        foreach (var a in assets)
        {
            if (a is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                return clip;
        }
        // Fallback: direct load
        var direct = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (direct == null)
            Debug.LogWarning($"[BuildSurvialistKCCController] Clip not found: {path}");
        return direct;
    }
}
#endif
