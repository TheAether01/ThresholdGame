// ============================================================================
// TwinStickAnimatorBuilder.cs — Rebuilds the Player AnimatorController
// for twin-stick controls with directional walk+shoot blend tree.
//
// Run via: Tools > THRESHOLD > Build Twin-Stick Animator
//
// What it does:
//   1. Creates a new AnimatorController at Assets/Animations/
//   2. Adds parameters: Speed, IsFiring, IsReloading, Die,
//      SpeedMultiplier, MoveX, MoveY
//   3. Builds states: Idle, Run, Idle_Shoot, Walk_Shoot (2D blend tree),
//      Reload, Die
//   4. The Walk_Shoot state uses a FreeformCartesian2D blend tree driven
//      by MoveX/MoveY to blend between WalkFront/Back/Left/Right_Shoot
//   5. Assigns the new controller to the MechWarrior prefab's Animator
// ============================================================================

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.IO;

namespace Threshold.Editor
{
    public static class TwinStickAnimatorBuilder
    {
        private const string OutputPath = "Assets/Animations/Player_AnimatorController.controller";
        private const string AnimFolder = "Assets/SciFiWarriorPBRHPPolyart";
        private const string PrefabPath = "Assets/Prefabs/Players/MechWarrior.prefab";

        [MenuItem("Tools/THRESHOLD/Build Twin-Stick Animator")]
        public static void Build()
        {
            // ==============================================================
            // 1. Ensure output directory
            // ==============================================================
            string dir = Path.GetDirectoryName(OutputPath);
            if (!AssetDatabase.IsValidFolder(dir))
            {
                if (!AssetDatabase.IsValidFolder("Assets/Animations"))
                    AssetDatabase.CreateFolder("Assets", "Animations");
            }

            // ==============================================================
            // 2. Create controller + parameters
            // ==============================================================
            var controller = AnimatorController.CreateAnimatorControllerAtPath(OutputPath);

            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("IsFiring", AnimatorControllerParameterType.Bool);
            controller.AddParameter("IsReloading", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Die", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("SpeedMultiplier", AnimatorControllerParameterType.Float);
            controller.AddParameter("MoveX", AnimatorControllerParameterType.Float);
            controller.AddParameter("MoveY", AnimatorControllerParameterType.Float);

            var sm = controller.layers[0].stateMachine;

            // ==============================================================
            // 3. Find animation clips
            // ==============================================================
            var guids = AssetDatabase.FindAssets("t:AnimationClip", new[] { AnimFolder });

            var clipIdle           = FindClip(guids, "Idle_Guard_AR");
            var clipRun            = FindClip(guids, "Run_guard_AR");
            var clipIdleShoot      = FindClip(guids, "Idle_Shoot_Ar");
            var clipWalkShootF     = FindClip(guids, "WalkFront_Shoot_AR");
            var clipWalkShootB     = FindClip(guids, "WalkBack_Shoot_AR");
            var clipWalkShootL     = FindClip(guids, "WalkLeft_Shoot_AR");
            var clipWalkShootR     = FindClip(guids, "WalkRight_Shoot_AR");
            var clipReload         = FindClip(guids, "Reload");
            var clipDie            = FindClip(guids, "Die");
            var clipAutoShot       = FindClip(guids, "Shoot_Autoshot_AR");
            var clipFire           = clipAutoShot ?? FindClip(guids, "Shoot_SingleShot_AR");

            // Fallbacks for missing directional clips
            if (clipWalkShootB == null) clipWalkShootB = clipWalkShootF;
            if (clipWalkShootL == null) clipWalkShootL = clipWalkShootF;
            if (clipWalkShootR == null) clipWalkShootR = clipWalkShootF;

            Debug.Log($"[TwinStickAnimator] Clips: " +
                $"Idle={clipIdle != null}, Run={clipRun != null}, " +
                $"IdleShoot={clipIdleShoot != null}, " +
                $"WalkShootF={clipWalkShootF != null}, B={clipWalkShootB != null}, " +
                $"L={clipWalkShootL != null}, R={clipWalkShootR != null}, " +
                $"Reload={clipReload != null}, Die={clipDie != null}, Fire={clipFire != null}");

            // ==============================================================
            // 4. Create states
            // ==============================================================

            // -- IDLE (default) --
            var idle = sm.AddState("Idle", new Vector3(300, 0));
            idle.motion = clipIdle;
            sm.defaultState = idle;

            // -- RUN --
            var run = sm.AddState("Run", new Vector3(300, 80));
            run.motion = clipRun;
            run.speedParameterActive = true;
            run.speedParameter = "SpeedMultiplier";

            // -- IDLE_SHOOT (stationary + firing) --
            var idleShoot = sm.AddState("Idle_Shoot", new Vector3(550, 0));
            idleShoot.motion = clipIdleShoot ?? clipFire;

            // -- WALK_SHOOT (2D directional blend tree) --
            var blendTree = new BlendTree
            {
                name = "WalkShoot_Directional",
                blendType = BlendTreeType.FreeformCartesian2D,
                blendParameter = "MoveX",
                blendParameterY = "MoveY",
                useAutomaticThresholds = false
            };

            // Add directional clips at cardinal positions
            blendTree.AddChild(clipWalkShootF, new Vector2( 0f,  1f));  // Forward
            blendTree.AddChild(clipWalkShootB, new Vector2( 0f, -1f));  // Backward
            blendTree.AddChild(clipWalkShootL, new Vector2(-1f,  0f));  // Left strafe
            blendTree.AddChild(clipWalkShootR, new Vector2( 1f,  0f));  // Right strafe

            // Store blend tree as sub-asset of controller
            AssetDatabase.AddObjectToAsset(blendTree, controller);

            var walkShoot = sm.AddState("Walk_Shoot", new Vector3(550, 80));
            walkShoot.motion = blendTree;
            walkShoot.speedParameterActive = true;
            walkShoot.speedParameter = "SpeedMultiplier";

            // -- RELOAD --
            var reload = sm.AddState("Reload", new Vector3(550, 200));
            reload.motion = clipReload;

            // -- DIE --
            var die = sm.AddState("Die", new Vector3(300, 300));
            die.motion = clipDie;

            // ==============================================================
            // 5. Transitions
            // ==============================================================

            // --- Locomotion: Idle <-> Run ---
            var t = idle.AddTransition(run);
            t.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
            t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsFiring");
            t.hasExitTime = false; t.duration = 0.15f;

            t = run.AddTransition(idle);
            t.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");
            t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsFiring");
            t.hasExitTime = false; t.duration = 0.15f;

            // --- Firing: stationary ---
            t = idle.AddTransition(idleShoot);
            t.AddCondition(AnimatorConditionMode.If, 0, "IsFiring");
            t.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");
            t.hasExitTime = false; t.duration = 0.1f;

            t = idleShoot.AddTransition(idle);
            t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsFiring");
            t.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");
            t.hasExitTime = false; t.duration = 0.15f;

            // --- Firing: moving ---
            t = run.AddTransition(walkShoot);
            t.AddCondition(AnimatorConditionMode.If, 0, "IsFiring");
            t.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
            t.hasExitTime = false; t.duration = 0.1f;

            t = walkShoot.AddTransition(run);
            t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsFiring");
            t.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
            t.hasExitTime = false; t.duration = 0.15f;

            // --- Cross transitions (walk_shoot <-> idle_shoot) ---
            t = walkShoot.AddTransition(idleShoot);
            t.AddCondition(AnimatorConditionMode.If, 0, "IsFiring");
            t.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");
            t.hasExitTime = false; t.duration = 0.15f;

            t = idleShoot.AddTransition(walkShoot);
            t.AddCondition(AnimatorConditionMode.If, 0, "IsFiring");
            t.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
            t.hasExitTime = false; t.duration = 0.1f;

            // --- Exit transitions ---
            t = idleShoot.AddTransition(run);
            t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsFiring");
            t.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
            t.hasExitTime = false; t.duration = 0.15f;

            t = walkShoot.AddTransition(idle);
            t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsFiring");
            t.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");
            t.hasExitTime = false; t.duration = 0.15f;

            // --- Reload (from any combat state) ---
            t = idle.AddTransition(reload);
            t.AddCondition(AnimatorConditionMode.If, 0, "IsReloading");
            t.hasExitTime = false; t.duration = 0.15f;

            t = run.AddTransition(reload);
            t.AddCondition(AnimatorConditionMode.If, 0, "IsReloading");
            t.hasExitTime = false; t.duration = 0.15f;

            t = idleShoot.AddTransition(reload);
            t.AddCondition(AnimatorConditionMode.If, 0, "IsReloading");
            t.hasExitTime = false; t.duration = 0.1f;

            t = walkShoot.AddTransition(reload);
            t.AddCondition(AnimatorConditionMode.If, 0, "IsReloading");
            t.hasExitTime = false; t.duration = 0.1f;

            t = reload.AddTransition(idle);
            t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsReloading");
            t.hasExitTime = false; t.duration = 0.2f;

            // --- Death (from Any State) ---
            var tDie = sm.AddAnyStateTransition(die);
            tDie.AddCondition(AnimatorConditionMode.If, 0, "Die");
            tDie.hasExitTime = false; tDie.duration = 0.15f;
            tDie.canTransitionToSelf = false;

            // ==============================================================
            // 6. Save controller
            // ==============================================================
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log($"[TwinStickAnimator] ✅ Controller saved: {OutputPath}");

            // ==============================================================
            // 7. Assign to MechWarrior prefab
            // ==============================================================
            AssignToPrefab(controller);

            AssetDatabase.Refresh();
            Debug.Log("[TwinStickAnimator] ✅ Done! Twin-stick animator built and assigned.");
        }

        // ==================================================================
        // Assign controller to prefab
        // ==================================================================

        private static void AssignToPrefab(AnimatorController controller)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                Debug.LogWarning($"[TwinStickAnimator] Prefab not found at {PrefabPath}. " +
                    "Assign the controller manually to your player's Animator.");
                return;
            }

            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var animator = root.GetComponent<Animator>();
                if (animator == null) animator = root.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log("[TwinStickAnimator] ✅ Assigned to MechWarrior prefab.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ==================================================================
        // Clip finder (searches FBX sub-assets by clip name)
        // ==================================================================

        private static AnimationClip FindClip(string[] guids, string clipName)
        {
            // Search within the SciFiWarrior folder first
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var assets = AssetDatabase.LoadAllAssetsAtPath(path);
                foreach (var asset in assets)
                {
                    if (asset is AnimationClip clip && clip.name == clipName)
                        return clip;
                }
            }

            // Broader fallback
            var all = AssetDatabase.FindAssets("t:AnimationClip");
            foreach (var guid in all)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.Contains("SciFiWarrior")) continue;
                var assets = AssetDatabase.LoadAllAssetsAtPath(path);
                foreach (var asset in assets)
                {
                    if (asset is AnimationClip clip && clip.name == clipName)
                        return clip;
                }
            }

            Debug.LogWarning($"[TwinStickAnimator] Clip '{clipName}' not found.");
            return null;
        }
    }
}
#endif
