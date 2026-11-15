#if UNITY_EDITOR || UNITY_STANDALONE_WIN

using UnityEngine;

/// <summary>
/// Desktop hand driver matching old OpenViva's physical hand system.
/// 
/// Physics hands (Rigidbody + Collider) are separate root GameObjects that collide with the world.
/// Visual hands (skinned mesh on wrist bones) are overridden in LateUpdate to follow the physics hands.
/// 
/// This creates the old OpenViva feel: hands are always physical, they push objects,
/// stop at walls, and interact with everything without clipping.
/// </summary>
public class DesktopHandDriver : MonoBehaviour
{
    [Header("Camera")]
    public Transform cameraTransform;

    [Header("Hand Systems (Physics Hands in Scene Root)")]
    public HandGrabSystem leftHandGrab;
    public HandGrabSystem rightHandGrab;

    [Header("Wrist Bones (visual mesh follows physics via LateUpdate)")]
    public Transform leftWristBone;
    public Transform rightWristBone;

    [Header("Resting Offsets (Camera Space)")]
    [Tooltip("Camera-local position offset for left hand")]
    public Vector3 leftRestOffset = new Vector3(-0.16f, -0.18f, 0.48f);
    [Tooltip("Camera-local position offset for right hand")]
    public Vector3 rightRestOffset = new Vector3(0.16f, -0.18f, 0.48f);
    [Tooltip("Camera-local Euler rotation for left hand")]
    public Vector3 leftRestEuler = new Vector3(7.2f, -106.1f, -55.03f);
    [Tooltip("Camera-local Euler rotation for right hand")]
    public Vector3 rightRestEuler = new Vector3(355.1f, -254.1f, -314.97f);

    [Header("Reach Extension")]
    public float maxExtend = 0.5f;
    public float maxRetract = 0.1f;
    public float scrollStep = 0.04f;
    public float reachSmoothSpeed = 10f;

    [Header("Safety Thresholds")]
    [Tooltip("Maximum allowed distance between physics hand and target before snapping (e.g. after teleporting)")]
    public float maxDriftDistance = 1.5f;

    Vector3 leftCameraOffset;
    Vector3 rightCameraOffset;
    Quaternion leftRotOffset;
    Quaternion rightRotOffset;

    float currentReach;
    float targetReach;
    bool initialized;

    void Start()
    {
        InitializeDriver();
    }

    public void InitializeDriver()
    {
        if (cameraTransform == null)
        {
            var cam = GetComponentInChildren<Camera>();
            if (cam != null) cameraTransform = cam.transform;
            if (cameraTransform == null) cameraTransform = Camera.main?.transform;
        }

        // Auto-find wrist bones if unassigned
        if (leftWristBone == null)
            leftWristBone = FindChildRecursive(transform, "LeftWrist") ?? FindChildRecursive(transform, "wrist_l");
        if (rightWristBone == null)
            rightWristBone = FindChildRecursive(transform, "RightWrist") ?? FindChildRecursive(transform, "wrist_r");

        // Use calibrated resting camera-space offsets
        leftCameraOffset = leftRestOffset;
        rightCameraOffset = rightRestOffset;
        leftRotOffset = Quaternion.Euler(leftRestEuler);
        rightRotOffset = Quaternion.Euler(rightRestEuler);

        // Ensure standalone physics hands exist in scene root
        EnsurePhysicsHands();

        // Subscribe to grab events for freeze-to-head system
        if (leftHandGrab != null)
        {
            leftHandGrab.OnItemGrabbed -= OnLeftGrabbed;
            leftHandGrab.OnItemGrabbed += OnLeftGrabbed;
            leftHandGrab.OnItemReleased -= OnLeftReleased;
            leftHandGrab.OnItemReleased += OnLeftReleased;
        }
        if (rightHandGrab != null)
        {
            rightHandGrab.OnItemGrabbed -= OnRightGrabbed;
            rightHandGrab.OnItemGrabbed += OnRightGrabbed;
            rightHandGrab.OnItemReleased -= OnRightReleased;
            rightHandGrab.OnItemReleased += OnRightReleased;
        }

        initialized = true;
    }

    void EnsurePhysicsHands()
    {
        if (leftHandGrab == null)
        {
            var existingL = GameObject.Find("PhysicsHand_L");
            if (existingL != null)
                leftHandGrab = existingL.GetComponent<HandGrabSystem>();
            if (leftHandGrab == null)
                leftHandGrab = CreatePhysicsHand("PhysicsHand_L", true, leftRestOffset, leftRotOffset);
        }

        if (rightHandGrab == null)
        {
            var existingR = GameObject.Find("PhysicsHand_R");
            if (existingR != null)
                rightHandGrab = existingR.GetComponent<HandGrabSystem>();
            if (rightHandGrab == null)
                rightHandGrab = CreatePhysicsHand("PhysicsHand_R", false, rightRestOffset, rightRotOffset);
        }

        // Ignore collision with all colliders on player
        IgnorePlayerColliders(leftHandGrab);
        IgnorePlayerColliders(rightHandGrab);
    }

    HandGrabSystem CreatePhysicsHand(string name, bool isLeft, Vector3 localOffset, Quaternion localRot)
    {
        var go = new GameObject(name);
        go.transform.SetParent(null); // Independent root object

        Vector3 spawnPos = cameraTransform != null ? cameraTransform.TransformPoint(localOffset) : Vector3.zero;
        Quaternion spawnRot = cameraTransform != null ? cameraTransform.rotation * localRot : Quaternion.identity;
        go.transform.position = spawnPos;
        go.transform.rotation = spawnRot;

        var sphereCol = go.AddComponent<SphereCollider>();
        sphereCol.radius = 0.08f;
        sphereCol.isTrigger = false;

        var grab = go.AddComponent<HandGrabSystem>();
        grab.isLeftHand = isLeft;
        grab.gripPoint = go.transform;
        grab.handCollider = sphereCol;

        return grab;
    }

    void IgnorePlayerColliders(HandGrabSystem hand)
    {
        if (hand == null || hand.handCollider == null) return;
        var playerColliders = GetComponentsInChildren<Collider>();
        foreach (var pCol in playerColliders)
        {
            if (pCol != null && pCol != hand.handCollider)
                Physics.IgnoreCollision(pCol, hand.handCollider, true);
        }
    }

    void OnDestroy()
    {
        if (leftHandGrab != null)
        {
            leftHandGrab.OnItemGrabbed -= OnLeftGrabbed;
            leftHandGrab.OnItemReleased -= OnLeftReleased;
        }
        if (rightHandGrab != null)
        {
            rightHandGrab.OnItemGrabbed -= OnRightGrabbed;
            rightHandGrab.OnItemReleased -= OnRightReleased;
        }
    }

    void FixedUpdate()
    {
        if (!initialized || cameraTransform == null) return;
        if (Globals.isMenuOpen) return;

        // Keep offsets in sync with Inspector values for live tweaking
        leftCameraOffset = leftRestOffset;
        rightCameraOffset = rightRestOffset;
        leftRotOffset = Quaternion.Euler(leftRestEuler);
        rightRotOffset = Quaternion.Euler(rightRestEuler);

        currentReach = Mathf.Lerp(currentReach, targetReach, reachSmoothSpeed * Time.fixedDeltaTime);

        UpdateHandTarget(leftHandGrab, leftCameraOffset, leftRotOffset);
        UpdateHandTarget(rightHandGrab, rightCameraOffset, rightRotOffset);
    }

    void UpdateHandTarget(HandGrabSystem hand, Vector3 camOffset, Quaternion rotOffset)
    {
        if (hand == null) return;

        if (hand.FrozenLocalPos.HasValue)
        {
            // Frozen to head (holding item)
            hand.TargetPosition = cameraTransform.TransformPoint(hand.FrozenLocalPos.Value);
            hand.TargetRotation = cameraTransform.rotation * hand.FrozenLocalRot;
        }
        else
        {
            // Camera-relative target from stored offset + reach extension
            Vector3 targetPos = cameraTransform.TransformPoint(camOffset);
            targetPos += cameraTransform.forward * currentReach;
            hand.TargetPosition = targetPos;
            hand.TargetRotation = cameraTransform.rotation * rotOffset;
        }
    }

    /// <summary>
    /// After Animator runs, override wrist bone world positions to match physics hands.
    /// Visual hands stop at walls and push objects physically without clipping.
    /// </summary>
    void LateUpdate()
    {
        if (!initialized || cameraTransform == null) return;

        SyncBone(leftWristBone, leftHandGrab, leftCameraOffset, leftRotOffset);
        SyncBone(rightWristBone, rightHandGrab, rightCameraOffset, rightRotOffset);
    }

    void SyncBone(Transform bone, HandGrabSystem hand, Vector3 camOffset, Quaternion rotOffset)
    {
        if (bone == null || hand == null) return;

        Vector3 expectedPos = cameraTransform.TransformPoint(camOffset) + cameraTransform.forward * currentReach;
        Vector3 physicsPos = hand.transform.position;

        float driftSqr = (physicsPos - expectedPos).sqrMagnitude;
        float maxDriftSqr = maxDriftDistance * maxDriftDistance;

        if (driftSqr > maxDriftSqr)
        {
            // Teleport or extreme separation: snap physics hand back to expected position
            hand.transform.position = expectedPos;
            hand.transform.rotation = cameraTransform.rotation * rotOffset;
            if (hand.HandRigidbody != null)
            {
                hand.HandRigidbody.position = expectedPos;
                hand.HandRigidbody.rotation = cameraTransform.rotation * rotOffset;
                hand.HandRigidbody.linearVelocity = Vector3.zero;
                hand.HandRigidbody.angularVelocity = Vector3.zero;
            }
            physicsPos = expectedPos;
        }

        // Set visual wrist position and rotation to follow physics hand
        bone.position = physicsPos;
        bone.rotation = hand.transform.rotation;
        // Bone localScale is strictly preserved (e.g. 5)

        // Ensure child hand bone (hand_l/hand_r) stays locked at wrist origin
        for (int i = 0; i < bone.childCount; i++)
        {
            var child = bone.GetChild(i);
            if (child.name.StartsWith("hand_"))
            {
                child.localPosition = Vector3.zero;
                child.localRotation = Quaternion.identity;
                var rogueRb = child.GetComponent<Rigidbody>();
                if (rogueRb != null) Destroy(rogueRb);
            }
        }
    }

    #region Reach Controls (called by PlayerKB_GrabController)

    public void ExtendHands()
    {
        targetReach = Mathf.Min(targetReach + scrollStep, maxExtend);
    }

    public void RetractHands()
    {
        targetReach = Mathf.Max(targetReach - scrollStep, -maxRetract);
    }

    #endregion

    #region Freeze-to-Head

    void OnLeftGrabbed(GrabbableItem item)
    {
        if (leftHandGrab == null || cameraTransform == null) return;
        leftHandGrab.FrozenLocalPos = cameraTransform.InverseTransformPoint(leftHandGrab.transform.position);
        leftHandGrab.FrozenLocalRot = Quaternion.Inverse(cameraTransform.rotation) * leftHandGrab.transform.rotation;
    }

    void OnLeftReleased(GrabbableItem item)
    {
        if (leftHandGrab != null)
            leftHandGrab.FrozenLocalPos = null;
    }

    void OnRightGrabbed(GrabbableItem item)
    {
        if (rightHandGrab == null || cameraTransform == null) return;
        rightHandGrab.FrozenLocalPos = cameraTransform.InverseTransformPoint(rightHandGrab.transform.position);
        rightHandGrab.FrozenLocalRot = Quaternion.Inverse(cameraTransform.rotation) * rightHandGrab.transform.rotation;
    }

    void OnRightReleased(GrabbableItem item)
    {
        if (rightHandGrab != null)
            rightHandGrab.FrozenLocalPos = null;
    }

    #endregion

    static Transform FindChildRecursive(Transform parent, string name)
    {
        if (parent.name.Equals(name, System.StringComparison.OrdinalIgnoreCase)) return parent;
        foreach (Transform child in parent)
        {
            var found = FindChildRecursive(child, name);
            if (found != null) return found;
        }
        return null;
    }
}

#endif
