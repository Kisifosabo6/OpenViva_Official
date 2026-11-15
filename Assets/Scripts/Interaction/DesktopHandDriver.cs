#if UNITY_EDITOR || UNITY_STANDALONE_WIN

using UnityEngine;

/// <summary>
/// Desktop hand driver with smooth camera acceleration tracking, viewmodel inertia sway,
/// movement bobbing, and authentic PhysX wall collision.
/// 
/// Completely eliminates hand model shaking/jittering when walking or running by synchronizing
/// visual hand transforms directly with camera space in LateUpdate (after character movement and camera rotation).
/// Uses the physical hand Rigidbody to detect wall collisions naturally through PhysX contact resolution,
/// smoothly stopping hands at wall surfaces without teleporting or clipping.
/// </summary>
[DefaultExecutionOrder(100)]
public class DesktopHandDriver : MonoBehaviour
{
    [Header("Camera")]
    public Transform cameraTransform;

    [Header("Hand Systems (Physics Hands in Scene Root)")]
    public HandGrabSystem leftHandGrab;
    public HandGrabSystem rightHandGrab;

    [Header("Wrist Bones (visual mesh follows camera + dynamics via LateUpdate)")]
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

    [Header("Camera Acceleration & Inertia")]
    [Tooltip("Enable hands smoothly reacting to camera acceleration and deceleration")]
    public bool enableCameraAcceleration = true;
    [Tooltip("How strongly hands lag behind camera linear acceleration (inertia)")]
    public float accelerationInertia = 0.012f;
    [Tooltip("Maximum displacement allowed from linear acceleration")]
    public float maxAccelerationOffset = 0.06f;
    [Tooltip("Spring smoothing speed for acceleration recovery")]
    public float accelerationSmoothSpeed = 12f;

    [Header("Camera Rotational Sway")]
    [Tooltip("Enable hands swaying in response to mouse / camera rotation")]
    public bool enableRotationalSway = true;
    [Tooltip("How much hands translate when looking around")]
    public float swayPositionAmount = 0.0006f;
    [Tooltip("How much hands tilt/rotate when looking around (degrees)")]
    public float swayRotationAmount = 1.0f;
    [Tooltip("Maximum translational sway offset")]
    public float maxSwayOffset = 0.04f;
    [Tooltip("Maximum rotational sway angle in degrees")]
    public float maxSwayAngle = 7f;
    [Tooltip("Smoothing speed for rotational sway")]
    public float swaySmoothSpeed = 14f;

    [Header("Movement Bobbing")]
    [Tooltip("Enable rhythmic hand bobbing while walking and running")]
    public bool enableMovementBobbing = true;
    [Tooltip("Cadence/frequency of walking bob cycle")]
    public float walkBobFrequency = 7.5f;
    [Tooltip("Horizontal bobbing distance")]
    public float bobAmountX = 0.005f;
    [Tooltip("Vertical bobbing distance")]
    public float bobAmountY = 0.007f;
    [Tooltip("Frequency multiplier when running")]
    public float runBobFrequencyMultiplier = 1.35f;
    [Tooltip("Amplitude multiplier when running")]
    public float runBobAmplitudeMultiplier = 1.5f;

    [Header("Wall Collision Blending")]
    [Tooltip("Allow visual hands to stop against walls based on physical hand collision")]
    public bool enableWallCollision = true;
    [Tooltip("Distance threshold (meters) where wall contact begins blending to physical hand")]
    public float wallContactThreshold = 0.02f;
    [Tooltip("Distance range (meters) over which wall blend reaches 100%")]
    public float wallBlendRange = 0.04f;

    [Header("Safety Thresholds")]
    [Tooltip("Maximum allowed distance between physics hand and target before snapping")]
    public float maxDriftDistance = 1.5f;

    // Offsets & targets
    Vector3 leftCameraOffset;
    Vector3 rightCameraOffset;
    Quaternion leftRotOffset;
    Quaternion rightRotOffset;

    float currentReach;
    float targetReach;
    bool initialized;

    // Camera Kinematics tracking
    Vector3 lastCameraPos;
    Quaternion lastCameraRot;
    Vector3 cameraVelocity;
    Vector3 lastCameraVelocity;
    Vector3 cameraAcceleration;
    Vector3 localAngularVelocity;
    bool firstFrame = true;

    // Smoothed dynamic offsets
    Vector3 currentInertia;
    Vector3 currentSwayPos;
    Vector3 currentSwayRotEuler;
    Vector3 currentBob;
    float bobTimer;
    float bobFade;

    // Cached current dynamic offsets for grab coordinate capture
    Vector3 currentDynamicPosOffset;
    Quaternion currentDynamicRotOffset = Quaternion.identity;

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

        // Find child hand bones for visual grip points
        Transform leftHandVisual = FindChildRecursive(leftWristBone, "hand_l") ?? leftWristBone;
        Transform rightHandVisual = FindChildRecursive(rightWristBone, "hand_r") ?? rightWristBone;

        if (leftHandVisual == null)
        {
            var go = GameObject.Find("hand_l");
            if (go != null) leftHandVisual = go.transform;
        }
        if (rightHandVisual == null)
        {
            var go = GameObject.Find("hand_r");
            if (go != null) rightHandVisual = go.transform;
        }

        // Use calibrated resting camera-space offsets
        leftCameraOffset = leftRestOffset;
        rightCameraOffset = rightRestOffset;
        leftRotOffset = Quaternion.Euler(leftRestEuler);
        rightRotOffset = Quaternion.Euler(rightRestEuler);

        // Ensure standalone physics hands exist in scene root
        EnsurePhysicsHands();

        // Wire grip points to the actual visual hand bones so held items stay locked in the hand
        if (leftHandGrab != null && leftHandVisual != null)
        {
            leftHandGrab.visualGripPoint = leftHandVisual;
            leftHandGrab.gripPoint = leftHandVisual;
            leftHandGrab.isKinematicallyDriven = false;
            leftHandGrab.OnItemGrabbed -= OnLeftGrabbed;
            leftHandGrab.OnItemGrabbed += OnLeftGrabbed;
            leftHandGrab.OnItemReleased -= OnLeftReleased;
            leftHandGrab.OnItemReleased += OnLeftReleased;
        }
        if (rightHandGrab != null && rightHandVisual != null)
        {
            rightHandGrab.visualGripPoint = rightHandVisual;
            rightHandGrab.gripPoint = rightHandVisual;
            rightHandGrab.isKinematicallyDriven = false;
            rightHandGrab.OnItemGrabbed -= OnRightGrabbed;
            rightHandGrab.OnItemGrabbed += OnRightGrabbed;
            rightHandGrab.OnItemReleased -= OnRightReleased;
            rightHandGrab.OnItemReleased += OnRightReleased;
        }

        if (cameraTransform != null)
        {
            lastCameraPos = cameraTransform.position;
            lastCameraRot = cameraTransform.rotation;
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

        // Configure physics hands for smooth PhysX dynamic collision
        ConfigurePhysicsHand(leftHandGrab);
        ConfigurePhysicsHand(rightHandGrab);

        // Ignore collision between left and right hand
        if (leftHandGrab != null && rightHandGrab != null &&
            leftHandGrab.handCollider != null && rightHandGrab.handCollider != null)
        {
            Physics.IgnoreCollision(leftHandGrab.handCollider, rightHandGrab.handCollider, true);
        }

        // Ignore collision with all colliders on player
        IgnorePlayerColliders(leftHandGrab);
        IgnorePlayerColliders(rightHandGrab);
    }

    void ConfigurePhysicsHand(HandGrabSystem hand)
    {
        if (hand == null || hand.HandRigidbody == null) return;
        hand.isKinematicallyDriven = false;
        hand.HandRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
    }

    HandGrabSystem CreatePhysicsHand(string name, bool isLeft, Vector3 localOffset, Quaternion localRot)
    {
        var go = new GameObject(name);
        go.transform.SetParent(null); // Independent root object

        Vector3 spawnPos = cameraTransform != null ? cameraTransform.TransformPoint(localOffset) : Vector3.zero;
        Quaternion spawnRot = cameraTransform != null ? cameraTransform.rotation * localRot : Quaternion.identity;
        go.transform.position = spawnPos;
        go.transform.rotation = spawnRot;

        var rb = go.AddComponent<Rigidbody>();
        rb.mass = 5f;
        rb.useGravity = false;
        rb.isKinematic = false;
        rb.linearDamping = 0f;
        rb.angularDamping = 0f;
        rb.maxAngularVelocity = 64f;
        rb.maxDepenetrationVelocity = 0.001f;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        var sphereCol = go.AddComponent<SphereCollider>();
        sphereCol.radius = 0.08f;
        sphereCol.isTrigger = false;

        var grab = go.AddComponent<HandGrabSystem>();
        grab.isLeftHand = isLeft;
        grab.gripPoint = go.transform;
        grab.handCollider = sphereCol;
        grab.isKinematicallyDriven = false;

        return grab;
    }

    void IgnorePlayerColliders(HandGrabSystem hand)
    {
        if (hand == null || hand.handCollider == null) return;
        var playerColliders = GetComponentsInChildren<Collider>(true);
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
        if (!Globals.isDesktopMode || Globals.isMenuOpen) return;

        // Keep offsets in sync with Inspector values for live tweaking
        leftCameraOffset = leftRestOffset;
        rightCameraOffset = rightRestOffset;
        leftRotOffset = Quaternion.Euler(leftRestEuler);
        rightRotOffset = Quaternion.Euler(rightRestEuler);

        // Keep targets updated in FixedUpdate and pass camera velocity for smooth physics tracking
        UpdateHandTargetFixed(leftHandGrab, leftCameraOffset, leftRotOffset);
        UpdateHandTargetFixed(rightHandGrab, rightCameraOffset, rightRotOffset);
    }

    void UpdateHandTargetFixed(HandGrabSystem hand, Vector3 camOffset, Quaternion rotOffset)
    {
        if (hand == null) return;

        hand.CameraVelocity = cameraVelocity;

        if (hand.FrozenLocalPos.HasValue)
        {
            hand.TargetPosition = cameraTransform.TransformPoint(hand.FrozenLocalPos.Value + currentDynamicPosOffset);
            hand.TargetRotation = cameraTransform.rotation * hand.FrozenLocalRot * currentDynamicRotOffset;
        }
        else
        {
            Vector3 targetPos = cameraTransform.TransformPoint(camOffset + currentDynamicPosOffset);
            targetPos += cameraTransform.forward * currentReach;
            hand.TargetPosition = targetPos;
            hand.TargetRotation = cameraTransform.rotation * rotOffset * currentDynamicRotOffset;
        }
    }

    /// <summary>
    /// Executes after PlayerController.LateUpdate and Animator.
    /// Smoothly positions visual wrist bones in camera space with camera acceleration inertia,
    /// viewmodel look sway, and walk bobbing. Blends naturally with physical hand when hitting walls.
    /// </summary>
    void LateUpdate()
    {
        if (!initialized || cameraTransform == null) return;
        if (!Globals.isDesktopMode || Globals.isMenuOpen) return;

        float dt = Time.deltaTime;
        if (dt <= 0.00001f) return;

        // 1. Calculate camera kinematics (velocity, acceleration, angular velocity)
        UpdateCameraKinematics(dt);

        // 2. Calculate dynamic camera-space offsets (inertia, sway, bobbing)
        currentDynamicPosOffset = CalculateDynamicOffset(dt);
        currentDynamicRotOffset = CalculateDynamicRotationOffset(dt);

        // 3. Smooth reach extension from mouse scroll
        currentReach = Mathf.Lerp(currentReach, targetReach, reachSmoothSpeed * dt);

        // 4. Update visual hands and synchronize with physics
        UpdateHand(leftWristBone, leftHandGrab, leftCameraOffset, leftRotOffset, currentDynamicPosOffset, currentDynamicRotOffset);
        UpdateHand(rightWristBone, rightHandGrab, rightCameraOffset, rightRotOffset, currentDynamicPosOffset, currentDynamicRotOffset);
    }

    void UpdateCameraKinematics(float dt)
    {
        Vector3 currentCamPos = cameraTransform.position;
        Quaternion currentCamRot = cameraTransform.rotation;

        if (firstFrame)
        {
            lastCameraPos = currentCamPos;
            lastCameraRot = currentCamRot;
            cameraVelocity = Vector3.zero;
            lastCameraVelocity = Vector3.zero;
            cameraAcceleration = Vector3.zero;
            localAngularVelocity = Vector3.zero;
            firstFrame = false;
            return;
        }

        // Camera world velocity with responsive smoothing
        Vector3 rawVelocity = (currentCamPos - lastCameraPos) / dt;
        cameraVelocity = Vector3.Lerp(cameraVelocity, rawVelocity, Mathf.Clamp01(25f * dt));

        // Camera world acceleration with responsive smoothing
        Vector3 rawAcceleration = (cameraVelocity - lastCameraVelocity) / dt;
        cameraAcceleration = Vector3.Lerp(cameraAcceleration, rawAcceleration, Mathf.Clamp01(20f * dt));

        // Angular velocity in camera-local space
        Quaternion deltaRot = Quaternion.Inverse(lastCameraRot) * currentCamRot;
        deltaRot.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f) angle -= 360f;
        Vector3 rawAngVel = axis * (angle * Mathf.Deg2Rad / dt);
        if (float.IsNaN(rawAngVel.x) || float.IsInfinity(rawAngVel.x)) rawAngVel = Vector3.zero;
        localAngularVelocity = Vector3.Lerp(localAngularVelocity, rawAngVel, Mathf.Clamp01(25f * dt));

        lastCameraPos = currentCamPos;
        lastCameraRot = currentCamRot;
        lastCameraVelocity = cameraVelocity;
    }

    Vector3 CalculateDynamicOffset(float dt)
    {
        Vector3 totalOffset = Vector3.zero;

        // 1. Camera Linear Acceleration Inertia
        if (enableCameraAcceleration)
        {
            Vector3 localAccel = cameraTransform.InverseTransformDirection(cameraAcceleration);

            Vector3 targetInertia = new Vector3(
                -localAccel.x * accelerationInertia,
                -localAccel.y * (accelerationInertia * 0.8f),
                -localAccel.z * accelerationInertia
            );

            targetInertia = Vector3.ClampMagnitude(targetInertia, maxAccelerationOffset);
            currentInertia = Vector3.Lerp(currentInertia, targetInertia, Mathf.Clamp01(accelerationSmoothSpeed * dt));
            totalOffset += currentInertia;
        }
        else
        {
            currentInertia = Vector3.zero;
        }

        // 2. Rotational Look Sway (position shift from mouse aim)
        if (enableRotationalSway)
        {
            Vector3 targetSway = new Vector3(
                -localAngularVelocity.y * swayPositionAmount,
                localAngularVelocity.x * (swayPositionAmount * 0.7f),
                0f
            );

            targetSway = Vector3.ClampMagnitude(targetSway, maxSwayOffset);
            currentSwayPos = Vector3.Lerp(currentSwayPos, targetSway, Mathf.Clamp01(swaySmoothSpeed * dt));
            totalOffset += currentSwayPos;
        }
        else
        {
            currentSwayPos = Vector3.zero;
        }

        // 3. Movement Bobbing (synchronized with walking and running cadence)
        if (enableMovementBobbing)
        {
            Vector3 localVel = cameraTransform.InverseTransformDirection(cameraVelocity);
            float horizontalSpeed = new Vector2(localVel.x, localVel.z).magnitude;

            if (horizontalSpeed > 0.2f)
            {
                bool isRunning = horizontalSpeed > 3.2f;
                float freq = walkBobFrequency * (isRunning ? runBobFrequencyMultiplier : 1.0f);
                float ampX = bobAmountX * (isRunning ? runBobAmplitudeMultiplier : 1.0f);
                float ampY = bobAmountY * (isRunning ? runBobAmplitudeMultiplier : 1.0f);

                bobTimer += dt * freq;
                Vector3 targetBob = new Vector3(
                    Mathf.Cos(bobTimer) * ampX,
                    Mathf.Abs(Mathf.Sin(bobTimer)) * ampY,
                    0f
                );

                bobFade = Mathf.MoveTowards(bobFade, 1f, dt * 6f);
                currentBob = Vector3.Lerp(currentBob, targetBob * bobFade, Mathf.Clamp01(12f * dt));
            }
            else
            {
                bobFade = Mathf.MoveTowards(bobFade, 0f, dt * 6f);
                currentBob = Vector3.Lerp(currentBob, Vector3.zero, Mathf.Clamp01(12f * dt));
            }

            totalOffset += currentBob;
        }
        else
        {
            currentBob = Vector3.zero;
        }

        return totalOffset;
    }

    Quaternion CalculateDynamicRotationOffset(float dt)
    {
        if (!enableRotationalSway) return Quaternion.identity;

        float targetPitch = -localAngularVelocity.x * (swayRotationAmount * 0.6f);
        float targetYaw = -localAngularVelocity.y * (swayRotationAmount * 0.5f);
        float targetRoll = localAngularVelocity.y * (swayRotationAmount * 0.8f);

        targetPitch = Mathf.Clamp(targetPitch, -maxSwayAngle, maxSwayAngle);
        targetYaw = Mathf.Clamp(targetYaw, -maxSwayAngle, maxSwayAngle);
        targetRoll = Mathf.Clamp(targetRoll, -maxSwayAngle, maxSwayAngle);

        Vector3 targetEuler = new Vector3(targetPitch, targetYaw, targetRoll);
        currentSwayRotEuler = Vector3.Lerp(currentSwayRotEuler, targetEuler, Mathf.Clamp01(swaySmoothSpeed * dt));

        return Quaternion.Euler(currentSwayRotEuler);
    }

    bool IsIgnoredCollider(Collider col)
    {
        if (col == null || col.isTrigger) return true;
        if (col.CompareTag("Player")) return true;
        if (leftHandGrab != null && col == leftHandGrab.handCollider) return true;
        if (rightHandGrab != null && col == rightHandGrab.handCollider) return true;
        if (leftHandGrab != null && leftHandGrab.HeldItem != null && col.transform.IsChildOf(leftHandGrab.HeldItem.transform)) return true;
        if (rightHandGrab != null && rightHandGrab.HeldItem != null && col.transform.IsChildOf(rightHandGrab.HeldItem.transform)) return true;
        if (col.GetComponentInParent<CharacterController>() != null) return true;
        if (col.transform.IsChildOf(transform)) return true;
        return false;
    }

    void UpdateHand(Transform bone, HandGrabSystem hand, Vector3 camOffset, Quaternion rotOffset, Vector3 dynamicPosOffset, Quaternion dynamicRotOffset)
    {
        if (bone == null || hand == null || cameraTransform == null) return;

        // 1. Calculate ideal camera-space target position and rotation
        Vector3 localPos;
        Quaternion localRot;

        if (hand.FrozenLocalPos.HasValue)
        {
            localPos = hand.FrozenLocalPos.Value + dynamicPosOffset;
            localRot = hand.FrozenLocalRot * dynamicRotOffset;
        }
        else
        {
            localPos = camOffset + dynamicPosOffset + Vector3.forward * currentReach;
            localRot = rotOffset * dynamicRotOffset;
        }

        Vector3 targetWorldPos = cameraTransform.TransformPoint(localPos);
        Quaternion expectedRot = cameraTransform.rotation * localRot;
        Vector3 expectedPos = targetWorldPos;

        // 2. Obstacle / Wall avoidance from player shoulder towards target:
        // Clamps target position in front of walls so it NEVER penetrates inside geometry!
        if (enableWallCollision)
        {
            Vector3 shoulderOrigin = cameraTransform.position;
            shoulderOrigin += cameraTransform.right * (camOffset.x * 0.6f) + cameraTransform.up * (camOffset.y * 0.6f);

            Vector3 toTarget = targetWorldPos - shoulderOrigin;
            float targetDist = toTarget.magnitude;

            if (targetDist > 0.02f)
            {
                Vector3 rayDir = toTarget / targetDist;
                float handRadius = 0.065f;
                int layerMask = ~LayerMask.GetMask("Ignore Raycast");

                var hits = Physics.SphereCastAll(shoulderOrigin, handRadius, rayDir, targetDist, layerMask, QueryTriggerInteraction.Ignore);
                float nearestHitDist = float.MaxValue;
                bool hitObstacle = false;

                if (hits != null && hits.Length > 0)
                {
                    foreach (var h in hits)
                    {
                        if (IsIgnoredCollider(h.collider)) continue;
                        if (h.distance < nearestHitDist)
                        {
                            nearestHitDist = h.distance;
                            hitObstacle = true;
                        }
                    }
                }

                if (hitObstacle)
                {
                    float clampedDist = Mathf.Max(0.02f, nearestHitDist);
                    expectedPos = shoulderOrigin + rayDir * clampedDist;
                }
            }
        }

        Vector3 physicsPos = hand.transform.position;

        // Keep target position and camera velocity updated on physics hand
        hand.TargetPosition = expectedPos;
        hand.TargetRotation = expectedRot;
        hand.CameraVelocity = cameraVelocity;

        // 3. Measure physical deflection (how much PhysX has stopped the hand against an obstacle)
        float driftSqr = (physicsPos - expectedPos).sqrMagnitude;

        // Only snap physics hand if it has separated by a massive distance in open air (e.g. game teleport)
        if (driftSqr > 4.0f && cameraVelocity.sqrMagnitude > 25f)
        {
            hand.transform.position = expectedPos;
            hand.transform.rotation = expectedRot;
            if (hand.HandRigidbody != null)
            {
                hand.HandRigidbody.position = expectedPos;
                hand.HandRigidbody.rotation = expectedRot;
                hand.HandRigidbody.linearVelocity = cameraVelocity;
                hand.HandRigidbody.angularVelocity = Vector3.zero;
            }
            physicsPos = expectedPos;
            driftSqr = 0f;
        }

        // 4. Smooth visual placement:
        // In open air: visual hand follows expectedPos (100% stable, zero shake, smooth acceleration & bobbing).
        // At walls: expectedPos already stops at wall surface, and any secondary PhysX deflection blends seamlessly.
        Vector3 finalPos = expectedPos;
        Quaternion finalRot = expectedRot;

        if (enableWallCollision && driftSqr > 0.0004f) // > 2cm deflection
        {
            float driftDist = Mathf.Sqrt(driftSqr);
            float blend = Mathf.Clamp01((driftDist - 0.02f) / 0.06f);
            finalPos = Vector3.Lerp(expectedPos, physicsPos, blend);
            finalRot = Quaternion.Slerp(expectedRot, hand.transform.rotation, blend);
        }

        // 5. Set visual wrist bone directly
        bone.position = finalPos;
        bone.rotation = finalRot;

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

    [Tooltip("Freeze hand position in camera space when holding item")]
    public bool freezeHandOnGrab = false;

    void OnLeftGrabbed(GrabbableItem item)
    {
        if (!freezeHandOnGrab) return;
        if (leftHandGrab == null || cameraTransform == null || leftWristBone == null) return;
        Vector3 baseLocal = cameraTransform.InverseTransformPoint(leftWristBone.position) - currentDynamicPosOffset;
        Quaternion baseRot = Quaternion.Inverse(cameraTransform.rotation) * leftWristBone.rotation * Quaternion.Inverse(currentDynamicRotOffset);
        leftHandGrab.FrozenLocalPos = baseLocal;
        leftHandGrab.FrozenLocalRot = baseRot;
    }

    void OnLeftReleased(GrabbableItem item)
    {
        if (leftHandGrab != null)
            leftHandGrab.FrozenLocalPos = null;
    }

    void OnRightGrabbed(GrabbableItem item)
    {
        if (!freezeHandOnGrab) return;
        if (rightHandGrab == null || cameraTransform == null || rightWristBone == null) return;
        Vector3 baseLocal = cameraTransform.InverseTransformPoint(rightWristBone.position) - currentDynamicPosOffset;
        Quaternion baseRot = Quaternion.Inverse(cameraTransform.rotation) * rightWristBone.rotation * Quaternion.Inverse(currentDynamicRotOffset);
        rightHandGrab.FrozenLocalPos = baseLocal;
        rightHandGrab.FrozenLocalRot = baseRot;
    }

    void OnRightReleased(GrabbableItem item)
    {
        if (rightHandGrab != null)
            rightHandGrab.FrozenLocalPos = null;
    }

    #endregion

    static Transform FindChildRecursive(Transform parent, string name)
    {
        if (parent == null) return null;
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