using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace RunAcross.EditorTools
{
    /// <summary>
    /// Generates Art/Animation/Courier.controller from the Unity Starter Assets – ThirdPerson clips
    /// (Humanoid, retargeted onto the rigged courier). Run by Build All Levels.
    ///
    ///   Locomotion (1D blend on Speed: Idle 0 · Walk_N 2 · Run_N 5.335, the clips' natural m/s;
    ///               MotionSpeed speeds it up when sprinting past the run clip's pace)
    ///   Jump → JumpStart → InAir → Land (blend on Speed: JumpLand · Walk_N_Land · Run_N_Land) → Locomotion
    ///   Walking off a ledge goes Locomotion → InAir.
    ///
    /// The clips are Asset Store content (Standard EULA), so Assets/Starter Assets is gitignored: import
    /// "Starter Assets – ThirdPerson", folder Runtime/ThirdPersonController/Character/Animations only.
    /// </summary>
    public static class CourierAnimatorBuilder
    {
        public const string ControllerPath = "Assets/_Project/Art/Animation/Courier.controller";
        const string ClipDir = "Assets/Starter Assets/Runtime/ThirdPersonController/Character/Animations/";
        public const float WalkClipSpeed = 2f, RunClipSpeed = 5.335f;

        public static AnimatorController Build()
        {
            AnimationClip Clip(string file, string clip)
            {
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(ClipDir + file))
                    if (o is AnimationClip c && c.name == clip) return c;
                throw new System.IO.FileNotFoundException(
                    $"Animation clip '{clip}' not found in {ClipDir}{file}. Import Starter Assets – ThirdPerson " +
                    "(only Runtime/ThirdPersonController/Character/Animations) from the Package Manager's My Assets.");
            }

            var idle = Clip("Stand--Idle.anim.fbx", "Idle");
            var walk = Clip("Locomotion--Walk_N.anim.fbx", "Walk_N");
            var run = Clip("Locomotion--Run_N.anim.fbx", "Run_N");
            var jumpStart = Clip("Jump--Jump.anim.fbx", "JumpStart");
            var jumpLand = Clip("Jump--Jump.anim.fbx", "JumpLand");
            var inAir = Clip("Jump--InAir.anim.fbx", "InAir");
            var walkLand = Clip("Locomotion--Walk_N_Land.anim.fbx", "Walk_N_Land");
            var runLand = Clip("Locomotion--Run_N_Land.anim.fbx", "Run_N_Land");

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ControllerPath));
            AssetDatabase.DeleteAsset(ControllerPath);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("MotionSpeed", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            ctrl.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("VerticalSpeed", AnimatorControllerParameterType.Float);
            foreach (var p in ctrl.parameters) // defaults: standing, normal pace
            {
                if (p.name == "Grounded") p.defaultBool = true;
                if (p.name == "MotionSpeed") p.defaultFloat = 1f;
            }
            ctrl.parameters = ctrl.parameters;

            var layers = ctrl.layers;
            layers[0].iKPass = true; // PlayerAnimator raises the arm to aim in OnAnimatorIK
            ctrl.layers = layers;
            var sm = ctrl.layers[0].stateMachine;

            var loco = ctrl.CreateBlendTreeInController("Locomotion", out BlendTree locoTree, 0);
            locoTree.blendType = BlendTreeType.Simple1D;
            locoTree.blendParameter = "Speed";
            locoTree.useAutomaticThresholds = false;
            locoTree.AddChild(idle, 0f);
            locoTree.AddChild(walk, WalkClipSpeed);
            locoTree.AddChild(run, RunClipSpeed);
            loco.speedParameterActive = true;
            loco.speedParameter = "MotionSpeed";

            var start = sm.AddState("JumpStart");
            start.motion = jumpStart;
            var air = sm.AddState("InAir");
            air.motion = inAir;

            var landTree = new BlendTree
            {
                name = "Land", blendType = BlendTreeType.Simple1D, blendParameter = "Speed",
                useAutomaticThresholds = false, hideFlags = HideFlags.HideInHierarchy,
            };
            AssetDatabase.AddObjectToAsset(landTree, ctrl);
            landTree.AddChild(jumpLand, 0f);
            landTree.AddChild(walkLand, WalkClipSpeed);
            landTree.AddChild(runLand, RunClipSpeed);
            var land = sm.AddState("Land");
            land.motion = landTree;
            sm.defaultState = loco;

            AnimatorStateTransition T(AnimatorState from, AnimatorState to, float duration, float? exitTime = null)
            {
                var t = from.AddTransition(to);
                t.duration = duration;
                t.hasExitTime = exitTime.HasValue;
                if (exitTime.HasValue) t.exitTime = exitTime.Value;
                t.hasFixedDuration = true;
                return t;
            }

            T(loco, start, 0.08f).AddCondition(AnimatorConditionMode.If, 0, "Jump");
            T(loco, air, 0.2f).AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded"); // walked off a ledge
            T(start, air, 0.15f, 0.8f);
            T(start, land, 0.1f, 0.5f).AddCondition(AnimatorConditionMode.If, 0, "Grounded"); // jumped onto a ledge
            T(air, start, 0.08f).AddCondition(AnimatorConditionMode.If, 0, "Jump"); // coyote-time jump after leaving a ledge
            T(air, land, 0.1f).AddCondition(AnimatorConditionMode.If, 0, "Grounded");
            T(land, start, 0.08f).AddCondition(AnimatorConditionMode.If, 0, "Jump"); // bunny hop
            T(land, loco, 0.2f, 0.55f);

            EditorUtility.SetDirty(ctrl);
            AssetDatabase.SaveAssets();
            return ctrl;
        }
    }
}
