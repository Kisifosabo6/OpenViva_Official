#if UNITY_EDITOR

using UnityEditor;
using UnityEngine;

/// <summary>
/// One-click setup: configures the PlayerKB with the physics-based
/// interaction system matching old OpenViva's hand architecture.
/// 
/// Physical hands (PhysicsHand_L / PhysicsHand_R) are separate root GameObjects
/// that collide with walls and push objects.
/// Visual wrist bones follow the physics hands in LateUpdate, preventing wall clipping.
/// Finger curling, point/gestures, and item grabbing are fully wired.
/// </summary>
public class InteractionSystemSetup : Editor
{
    [MenuItem("Tools/Setup Interaction System")]
    public static void SetupInteractionSystem()
    {
        // --- Find Player in scene ---
        var playerKB = GameObject.Find("PlayerKB") ?? GameObject.Find("Player");
        if (playerKB == null)
        {
            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.name.Contains("Player") || root.GetComponentInChildren<PlayerController>() != null || root.GetComponentInChildren<Player>() != null)
                {
                    playerKB = root;
                    break;
                }
            }
        }

        if (playerKB == null)
        {
            EditorUtility.DisplayDialog("Setup Failed",
                "Could not find 'Player' or 'PlayerKB' GameObject in the current scene.",
                "OK");
            return;
        }

        Undo.RegisterFullObjectHierarchyUndo(playerKB, "Setup Interaction System");

        // --- Find key child transforms ---
        Transform handL = FindChild(playerKB.transform, "hand_l");
        Transform handR = FindChild(playerKB.transform, "hand_r");
        Transform wristL = FindChild(playerKB.transform, "LeftWrist") ?? FindChild(playerKB.transform, "wrist_l");
        Transform wristR = FindChild(playerKB.transform, "RightWrist") ?? FindChild(playerKB.transform, "wrist_r");
        Transform mainCam = FindChild(playerKB.transform, "MainCamera") ?? FindChild(playerKB.transform, "Main Camera KB") ?? Camera.main?.transform;

        if (wristL == null || wristR == null)
        {
            EditorUtility.DisplayDialog("Setup Failed",
                "Could not find LeftWrist/RightWrist in player hierarchy.",
                "OK");
            return;
        }

        // --- 1. Clean up child bones hand_l and hand_r ---
        // They must NOT have Rigidbody or SphereCollider (which creates feedback loops with wrists)
        // They MUST have BoxCollider (isTrigger = true) for PlayerKB_GrabObject interaction.
        CleanVisualHand(handL);
        CleanVisualHand(handR);

        // --- 2. Enable wrist animators ---
        var wristLAnimator = wristL.GetComponent<Animator>();
        if (wristLAnimator != null && !wristLAnimator.enabled)
            wristLAnimator.enabled = true;

        var wristRAnimator = wristR.GetComponent<Animator>();
        if (wristRAnimator != null && !wristRAnimator.enabled)
            wristRAnimator.enabled = true;

        // --- 3. Create or find independent physics hands in scene root ---
        var leftGrab = SetupIndependentPhysicsHand("PhysicsHand_L", true, wristL, mainCam);
        var rightGrab = SetupIndependentPhysicsHand("PhysicsHand_R", false, wristR, mainCam);

        // Ignore collisions between physics hands and player
        IgnorePlayerColliders(playerKB, leftGrab.handCollider);
        IgnorePlayerColliders(playerKB, rightGrab.handCollider);

        // --- 4. Add HandAnimator to wrists ---
        var leftAnim = GetOrAdd<HandAnimator>(wristL.gameObject);
        leftAnim.isLeftHand = true;
        leftAnim.wrist = wristL;
        leftAnim.handAnimator = wristLAnimator;
        leftAnim.AutoFindFingerBones();

        var rightAnim = GetOrAdd<HandAnimator>(wristR.gameObject);
        rightAnim.isLeftHand = false;
        rightAnim.wrist = wristR;
        rightAnim.handAnimator = wristRAnimator;
        rightAnim.AutoFindFingerBones();

        // --- 5. DesktopHandDriver on Player ---
        var handDriver = GetOrAdd<DesktopHandDriver>(playerKB);
        handDriver.cameraTransform = mainCam;
        handDriver.leftHandGrab = leftGrab;
        handDriver.rightHandGrab = rightGrab;
        handDriver.leftWristBone = wristL;
        handDriver.rightWristBone = wristR;
        handDriver.InitializeDriver();

        // --- 6. PlayerKB_GrabController on Player ---
        var grabController = GetOrAdd<PlayerKB_GrabController>(playerKB);
        grabController.leftHand = leftGrab;
        grabController.rightHand = rightGrab;
        grabController.handDriver = handDriver;
        grabController.leftHandAnimator = leftAnim;
        grabController.rightHandAnimator = rightAnim;
        if (mainCam != null)
            grabController.playerCamera = mainCam.GetComponent<Camera>();

        // Remove any unwanted crosshair
        grabController.crosshairUI = null;
        var existingCrosshairCanvas = GameObject.Find("DesktopCrosshairCanvas");
        if (existingCrosshairCanvas != null)
            Undo.DestroyObjectImmediate(existingCrosshairCanvas);

        // --- 7. DesktopGestures on Player ---
        var gestures = GetOrAdd<DesktopGestures>(playerKB);
        gestures.leftHandAnimator = leftAnim;
        gestures.rightHandAnimator = rightAnim;
        gestures.leftHandGrab = leftGrab;
        gestures.rightHandGrab = rightGrab;

        // --- 8. Wire PlayerManager if present ---
        var playerManager = Object.FindFirstObjectByType<PlayerManager>();
        if (playerManager != null)
        {
            var so = new SerializedObject(playerManager);
            var leftHandProp = so.FindProperty("_leftHandGrab");
            var rightHandProp = so.FindProperty("_rightHandGrab");
            if (leftHandProp != null)
                leftHandProp.objectReferenceValue = leftGrab;
            if (rightHandProp != null)
                rightHandProp.objectReferenceValue = rightGrab;
            so.ApplyModifiedProperties();
        }

        // Mark dirty and save scene
        EditorUtility.SetDirty(playerKB);
        PrefabUtility.RecordPrefabInstancePropertyModifications(playerKB);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(playerKB.scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(playerKB.scene);

        Debug.Log("[Setup] ===== Interaction System successfully configured and saved! =====");
    }

    static void CleanVisualHand(Transform handBone)
    {
        if (handBone == null) return;

        // Remove Rigidbody (belonging only to standalone physics hands)
        var rb = handBone.GetComponent<Rigidbody>();
        if (rb != null) Undo.DestroyObjectImmediate(rb);

        // Remove HandGrabSystem
        var hgs = handBone.GetComponent<HandGrabSystem>();
        if (hgs != null) Undo.DestroyObjectImmediate(hgs);

        // Remove GrabbableItem
        var gi = handBone.GetComponent<GrabbableItem>();
        if (gi != null) Undo.DestroyObjectImmediate(gi);

        // Remove SphereCollider
        var sphere = handBone.GetComponent<SphereCollider>();
        if (sphere != null) Undo.DestroyObjectImmediate(sphere);

        // Remove all MeshColliders (mesh colliders on hand bones create physics explosions)
        var meshCols = handBone.GetComponents<MeshCollider>();
        foreach (var mc in meshCols)
        {
            if (mc != null) Undo.DestroyObjectImmediate(mc);
        }

        // Reset local transform to zero
        handBone.localPosition = Vector3.zero;
        handBone.localRotation = Quaternion.identity;
        handBone.localScale = Vector3.one;

        // Ensure BoxCollider is present and set as trigger (for scene item detection)
        var box = handBone.GetComponent<BoxCollider>();
        if (box == null) box = handBone.gameObject.AddComponent<BoxCollider>();
        box.isTrigger = true;
    }

    static HandGrabSystem SetupIndependentPhysicsHand(string name, bool isLeft, Transform initialWrist, Transform mainCam)
    {
        var existing = GameObject.Find(name);
        GameObject handGO = existing != null ? existing : new GameObject(name);
        Undo.RegisterCreatedObjectUndo(handGO, "Create Physics Hand " + name);

        // Unparent from anything: lives directly in scene root
        handGO.transform.SetParent(null);

        Vector3 spawnPos = initialWrist != null ? initialWrist.position : Vector3.zero;
        if (mainCam != null)
        {
            Vector3 offset = isLeft ? new Vector3(-0.16f, -0.18f, 0.48f) : new Vector3(0.16f, -0.18f, 0.48f);
            spawnPos = mainCam.TransformPoint(offset);
        }
        handGO.transform.position = spawnPos;

        var rb = GetOrAdd<Rigidbody>(handGO);
        rb.mass = 5f;
        rb.useGravity = false;
        rb.isKinematic = false;
        rb.linearDamping = 0f;
        rb.angularDamping = 0f;
        rb.maxAngularVelocity = 64f;
        rb.maxDepenetrationVelocity = 0.001f;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        var sphereCol = GetOrAdd<SphereCollider>(handGO);
        sphereCol.radius = 0.08f;
        sphereCol.isTrigger = false; // Physical collision with walls and world!

        var grab = GetOrAdd<HandGrabSystem>(handGO);
        grab.isLeftHand = isLeft;
        grab.gripPoint = handGO.transform;
        grab.handCollider = sphereCol;

        return grab;
    }

    static void IgnorePlayerColliders(GameObject playerKB, Collider handCol)
    {
        if (playerKB == null || handCol == null) return;
        var cols = playerKB.GetComponentsInChildren<Collider>();
        foreach (var c in cols)
        {
            if (c != null && c != handCol)
                Physics.IgnoreCollision(c, handCol, true);
        }
    }

    static T GetOrAdd<T>(GameObject go) where T : Component
    {
        var existing = go.GetComponent<T>();
        if (existing != null) return existing;
        return go.AddComponent<T>();
    }

    static Transform FindChild(Transform parent, string name)
    {
        if (parent.name.Equals(name, System.StringComparison.OrdinalIgnoreCase)) return parent;
        foreach (Transform child in parent)
        {
            var found = FindChild(child, name);
            if (found != null) return found;
        }
        return null;
    }
}

#endif
