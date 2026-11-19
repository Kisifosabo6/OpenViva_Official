#if UNITY_EDITOR || UNITY_ANDROID || UNITY_STANDALONE_WIN

using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// VR grab controller. Uses grip button to grab/release items via HandGrabSystem.
/// Integrates with PhysicsHand for realistic velocity-driven hand movement.
/// Trigger is used for interaction (use items), grip for grab/release.
/// </summary>
public class PlayerVR_GrabController : MonoBehaviour
{
    [Header("Hand Systems")]
    public HandGrabSystem leftHandGrab;
    public HandGrabSystem rightHandGrab;
    public PhysicsHand leftPhysicsHand;
    public PhysicsHand rightPhysicsHand;

    [Header("Hand Animators")]
    public HandAnimator leftHandAnimator;
    public HandAnimator rightHandAnimator;

    [Header("VR Input Actions")]
    [Tooltip("0: Left Grip, 1: Left Trigger, 2: Right Grip, 3: Right Trigger")]
    [SerializeField] private InputActionReference leftGripAction;
    [SerializeField] private InputActionReference leftTriggerAction;
    [SerializeField] private InputActionReference rightGripAction;
    [SerializeField] private InputActionReference rightTriggerAction;

    [Header("Grab Threshold")]
    [Tooltip("Grip value above this triggers a grab")]
    public float gripThreshold = 0.7f;

    [Header("Door Handle Interaction")]
    [Tooltip("Max reach radius from VR hand to door handle for grabbing")]
    public float handleGrabRadius = 0.35f;

    // Grip state tracking
    bool leftGripActive;
    bool rightGripActive;
    bool leftTriggerActive;
    bool rightTriggerActive;
    float leftGripValue;
    float rightGripValue;
    float leftTriggerValue;
    float rightTriggerValue;

    // Door handle tracking
    PhysicsDoorHandle leftHeldHandle;
    PhysicsDoorHandle rightHeldHandle;
    PhysicsDoorHandle leftNearHandle;
    PhysicsDoorHandle rightNearHandle;

    [Header("VR Hand Visual Pinning")]
    [Tooltip("Visual base of left hand (Controller_BaseLeft)")]
    public Transform leftVisualBase;
    [Tooltip("Visual base of right hand (Controller_BaseRight)")]
    public Transform rightVisualBase;
    private Vector3 defaultLeftLocalPos;
    private Quaternion defaultLeftLocalRot;
    private Vector3 defaultRightLocalPos;
    private Quaternion defaultRightLocalRot;
    private bool leftIsPinned = false;
    private bool rightIsPinned = false;

    [Header("VR Hand Wall Collision & Stopping")]
    [Tooltip("Allow visual hands to stop naturally against walls/countertops without clipping or pushing the camera")]
    public bool enableWallCollision = true;
    [Tooltip("Hand collision sphere radius for obstacle detection")]
    public float handCollisionRadius = 0.038f;
    [Tooltip("Offset from wrist to fingertips along hand reach")]
    public float fingerForwardReach = 0.115f;
    [Tooltip("Palm thickness when hand presses flat against a surface")]
    public float palmThickness = 0.020f;
    [Tooltip("Finger width offset to bring visual hands nearly touching surfaces")]
    public float fingerWidthOffset = 0.018f;

    // Direct action fallbacks when InputActionReferences are not set in inspector
    private InputAction fallbackLeftGrip;
    private InputAction fallbackLeftTrigger;
    private InputAction fallbackRightGrip;
    private InputAction fallbackRightTrigger;

    void Awake()
    {
        AutoFindReferences();
    }

    void Start()
    {
        AutoFindReferences();
    }

    public void AutoFindReferences()
    {
        // 1. Resolve Input Actions
        if (leftGripAction == null || leftTriggerAction == null || rightGripAction == null || rightTriggerAction == null)
        {
            var iam = GetComponentInParent<UnityEngine.XR.Interaction.Toolkit.Inputs.InputActionManager>()
                   ?? Object.FindFirstObjectByType<UnityEngine.XR.Interaction.Toolkit.Inputs.InputActionManager>();

            if (iam != null && iam.actionAssets != null)
            {
                foreach (var asset in iam.actionAssets)
                {
                    if (asset == null) continue;
                    if (fallbackLeftGrip == null) fallbackLeftGrip = asset.FindAction("XRI Left Interaction/Select Value") ?? asset.FindAction("XRI Left/Select Value");
                    if (fallbackLeftTrigger == null) fallbackLeftTrigger = asset.FindAction("XRI Left Interaction/Activate Value") ?? asset.FindAction("XRI Left/Activate Value");
                    if (fallbackRightGrip == null) fallbackRightGrip = asset.FindAction("XRI Right Interaction/Select Value") ?? asset.FindAction("XRI Right/Select Value");
                    if (fallbackRightTrigger == null) fallbackRightTrigger = asset.FindAction("XRI Right Interaction/Activate Value") ?? asset.FindAction("XRI Right/Activate Value");
                }
            }
        }

        EnableResolvedActions();

        // 2. Resolve Hand Animators on VR Wrists
        Transform lWrist = transform.Find("Camera Offset/Left Controller/Controller_BaseLeft/Armature/L_Wrist");
        Transform rWrist = transform.Find("Camera Offset/Right Controller/Controller_BaseRight/Armature/R_Wrist");

        if (lWrist == null)
        {
            var pvr = GameObject.Find("PlayerVR") ?? GameObject.Find("Player")?.transform.Find("PlayerVR")?.gameObject;
            if (pvr != null)
            {
                lWrist = pvr.transform.Find("Camera Offset/Left Controller/Controller_BaseLeft/Armature/L_Wrist");
                rWrist = pvr.transform.Find("Camera Offset/Right Controller/Controller_BaseRight/Armature/R_Wrist");
            }
        }

        if (lWrist != null)
        {
            if (leftHandAnimator == null) leftHandAnimator = lWrist.GetComponent<HandAnimator>();
            if (leftHandAnimator == null)
            {
                leftHandAnimator = lWrist.gameObject.AddComponent<HandAnimator>();
                leftHandAnimator.isLeftHand = true;
                leftHandAnimator.wrist = lWrist;
                leftHandAnimator.handAnimator = lWrist.GetComponent<Animator>();
                leftHandAnimator.enableIdleLife = true;
                leftHandAnimator.idleBreathAmount = 0.07f;
                leftHandAnimator.idleBreathSpeed = 1.8f;
                leftHandAnimator.AutoFindFingerBones();
            }
        }

        if (rWrist != null)
        {
            if (rightHandAnimator == null) rightHandAnimator = rWrist.GetComponent<HandAnimator>();
            if (rightHandAnimator == null)
            {
                rightHandAnimator = rWrist.gameObject.AddComponent<HandAnimator>();
                rightHandAnimator.isLeftHand = false;
                rightHandAnimator.wrist = rWrist;
                rightHandAnimator.handAnimator = rWrist.GetComponent<Animator>();
                rightHandAnimator.enableIdleLife = true;
                rightHandAnimator.idleBreathAmount = 0.07f;
                rightHandAnimator.idleBreathSpeed = 1.8f;
                rightHandAnimator.AutoFindFingerBones();
            }
        }

        // 2b. Resolve Visual Base transforms for Hand Pinning
        if (leftVisualBase == null && lWrist != null)
        {
            leftVisualBase = lWrist.parent != null ? lWrist.parent.parent : null;
        }
        if (leftVisualBase != null)
        {
            defaultLeftLocalPos = leftVisualBase.localPosition;
            defaultLeftLocalRot = leftVisualBase.localRotation;
        }

        if (rightVisualBase == null && rWrist != null)
        {
            rightVisualBase = rWrist.parent != null ? rWrist.parent.parent : null;
        }
        if (rightVisualBase != null)
        {
            defaultRightLocalPos = rightVisualBase.localPosition;
            defaultRightLocalRot = rightVisualBase.localRotation;
        }

        // 3. Resolve HandGrabSystems on Controllers
        Transform lCtrl = transform.Find("Camera Offset/Left Controller") ?? (lWrist != null ? lWrist.parent?.parent?.parent : null);
        Transform rCtrl = transform.Find("Camera Offset/Right Controller") ?? (rWrist != null ? rWrist.parent?.parent?.parent : null);

        if (lCtrl != null)
        {
            var lrb = lCtrl.GetComponent<Rigidbody>();
            if (lrb != null) lrb.isKinematic = true;

            if (leftHandGrab == null) leftHandGrab = lCtrl.GetComponent<HandGrabSystem>();
            if (leftHandGrab == null)
            {
                leftHandGrab = lCtrl.gameObject.AddComponent<HandGrabSystem>();
                var col = lCtrl.GetComponent<Collider>();
                if (col == null)
                {
                    var sc = lCtrl.gameObject.AddComponent<SphereCollider>();
                    sc.radius = 0.12f;
                    sc.isTrigger = true;
                    leftHandGrab.handCollider = sc;
                }
                else
                {
                    leftHandGrab.handCollider = col;
                }
            }

            if (leftHandGrab != null)
            {
                leftHandGrab.isLeftHand = true;
                leftHandGrab.isKinematicallyDriven = true;
                Transform bestGripL = lWrist != null ? lWrist : (leftHandGrab.gripPoint != null ? leftHandGrab.gripPoint : lCtrl);
                leftHandGrab.gripPoint = bestGripL;
                // Pin visual grip point to VR wrist; prevent it from ever picking up Shinobu's hand
                if (leftHandGrab.visualGripPoint == null || leftHandGrab.visualGripPoint.root != transform.root || leftHandGrab.visualGripPoint.name == "hand_l")
                {
                    leftHandGrab.visualGripPoint = bestGripL;
                }
                if (leftHandGrab.grabRadius < 0.25f) leftHandGrab.grabRadius = 0.25f;
                if (leftHandGrab.grabRayDistance < 0.35f) leftHandGrab.grabRayDistance = 0.35f;
            }
        }

        if (rCtrl != null)
        {
            var rrb = rCtrl.GetComponent<Rigidbody>();
            if (rrb != null) rrb.isKinematic = true;

            if (rightHandGrab == null) rightHandGrab = rCtrl.GetComponent<HandGrabSystem>();
            if (rightHandGrab == null)
            {
                rightHandGrab = rCtrl.gameObject.AddComponent<HandGrabSystem>();
                var col = rCtrl.GetComponent<Collider>();
                if (col == null)
                {
                    var sc = rCtrl.gameObject.AddComponent<SphereCollider>();
                    sc.radius = 0.12f;
                    sc.isTrigger = true;
                    rightHandGrab.handCollider = sc;
                }
                else
                {
                    rightHandGrab.handCollider = col;
                }
            }

            if (rightHandGrab != null)
            {
                rightHandGrab.isLeftHand = false;
                rightHandGrab.isKinematicallyDriven = true;
                Transform bestGripR = rWrist != null ? rWrist : (rightHandGrab.gripPoint != null ? rightHandGrab.gripPoint : rCtrl);
                rightHandGrab.gripPoint = bestGripR;
                // Pin visual grip point to VR wrist; prevent it from ever picking up Shinobu's hand
                if (rightHandGrab.visualGripPoint == null || rightHandGrab.visualGripPoint.root != transform.root || rightHandGrab.visualGripPoint.name == "hand_r")
                {
                    rightHandGrab.visualGripPoint = bestGripR;
                }
                if (rightHandGrab.grabRadius < 0.25f) rightHandGrab.grabRadius = 0.25f;
                if (rightHandGrab.grabRayDistance < 0.35f) rightHandGrab.grabRayDistance = 0.35f;
            }
        }

        CleanHandColliders();
        IgnorePlayerBodyCollisions();
        ConfigureVRLocomotionCollision();

        SubscribeToHands();
    }

    /// <summary>
    /// Ensure all hand meshes are triggers so they do not cause violent PhysX penetration against static geometry.
    /// </summary>
    public void CleanHandColliders()
    {
        var mcs = GetComponentsInChildren<MeshCollider>(true);
        foreach (var mc in mcs)
        {
            if (mc != null && !mc.isTrigger)
            {
                mc.isTrigger = true;
            }
        }
    }

    /// <summary>
    /// Ignore collisions between the player's CharacterController/body and all hand colliders,
    /// preventing feedback loops or character displacement when hands are near the body.
    /// </summary>
    public void IgnorePlayerBodyCollisions()
    {
        var playerCC = GetComponentInParent<CharacterController>() ?? GetComponent<CharacterController>();
        if (playerCC == null)
        {
            var pvr = GameObject.Find("PlayerVR") ?? GameObject.Find("Player")?.transform.Find("PlayerVR")?.gameObject;
            if (pvr != null) playerCC = pvr.GetComponent<CharacterController>();
        }

        if (playerCC != null)
        {
            var colliders = GetComponentsInChildren<Collider>(true);
            foreach (var col in colliders)
            {
                if (col != null && col != playerCC)
                    Physics.IgnoreCollision(playerCC, col, true);
            }
        }
    }

    /// <summary>
    /// Configure the VR character controller and locomotion providers to match OpenViva 0.2.1
    /// (anchoring the CharacterController at the player's base so leaning over countertops never pushes the player back).
    /// </summary>
    public void ConfigureVRLocomotionCollision()
    {
        var cc = GetComponentInParent<CharacterController>() ?? GetComponent<CharacterController>();
        if (cc == null)
        {
            var pvr = GameObject.Find("PlayerVR") ?? GameObject.Find("Player")?.transform.Find("PlayerVR")?.gameObject;
            if (pvr != null) cc = pvr.GetComponent<CharacterController>();
        }

        if (cc != null)
        {
            cc.radius = 0.12f;
            cc.center = new Vector3(0f, cc.center.y, 0f);
        }

        var grav = GetComponentInChildren<UnityEngine.XR.Interaction.Toolkit.Locomotion.Gravity.GravityProvider>(true);
        if (grav == null)
        {
            var pvr = GameObject.Find("PlayerVR") ?? GameObject.Find("Player")?.transform.Find("PlayerVR")?.gameObject;
            if (pvr != null) grav = pvr.GetComponentInChildren<UnityEngine.XR.Interaction.Toolkit.Locomotion.Gravity.GravityProvider>(true);
        }
        if (grav != null)
        {
            grav.updateCharacterControllerCenterEachFrame = false;
        }

        var transformer = GetComponentInChildren<UnityEngine.XR.Interaction.Toolkit.Locomotion.XRBodyTransformer>(true);
        if (transformer == null)
        {
            var pvr = GameObject.Find("PlayerVR") ?? GameObject.Find("Player")?.transform.Find("PlayerVR")?.gameObject;
            if (pvr != null) transformer = pvr.GetComponentInChildren<UnityEngine.XR.Interaction.Toolkit.Locomotion.XRBodyTransformer>(true);
        }
        if (transformer != null)
        {
            transformer.bodyPositionEvaluator = VRFixedBodyPositionEvaluator.SharedInstance;
        }
    }

    void SubscribeToHands()
    {
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
    }

    void EnableResolvedActions()
    {
        fallbackLeftGrip?.Enable();
        fallbackLeftTrigger?.Enable();
        fallbackRightGrip?.Enable();
        fallbackRightTrigger?.Enable();
    }

    void OnEnable()
    {
        EnableAction(leftGripAction);
        EnableAction(leftTriggerAction);
        EnableAction(rightGripAction);
        EnableAction(rightTriggerAction);
        EnableResolvedActions();

        SubscribeToHands();
    }

    void OnDisable()
    {
        DisableAction(leftGripAction);
        DisableAction(leftTriggerAction);
        DisableAction(rightGripAction);
        DisableAction(rightTriggerAction);

        fallbackLeftGrip?.Disable();
        fallbackLeftTrigger?.Disable();
        fallbackRightGrip?.Disable();
        fallbackRightTrigger?.Disable();

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

        if (leftIsPinned && leftVisualBase != null)
        {
            leftVisualBase.localPosition = defaultLeftLocalPos;
            leftVisualBase.localRotation = defaultLeftLocalRot;
            leftIsPinned = false;
        }
        if (rightIsPinned && rightVisualBase != null)
        {
            rightVisualBase.localPosition = defaultRightLocalPos;
            rightVisualBase.localRotation = defaultRightLocalRot;
            rightIsPinned = false;
        }

        if (leftHandAnimator != null) leftHandAnimator.SetWallContact(0f);
        if (rightHandAnimator != null) rightHandAnimator.SetWallContact(0f);
    }

    void Update()
    {
        // Read continuous grip/trigger values (prefer inspector action, fallback to auto-resolved)
        leftGripValue = ReadActionOrFallback(leftGripAction, fallbackLeftGrip);
        leftTriggerValue = ReadActionOrFallback(leftTriggerAction, fallbackLeftTrigger);
        rightGripValue = ReadActionOrFallback(rightGripAction, fallbackRightGrip);
        rightTriggerValue = ReadActionOrFallback(rightTriggerAction, fallbackRightTrigger);

        // Update hand animations with analog values
        if (leftHandAnimator != null)
        {
            leftHandAnimator.SetGrip(leftGripValue);
            leftHandAnimator.SetTrigger(leftTriggerValue);
        }
        if (rightHandAnimator != null)
        {
            rightHandAnimator.SetGrip(rightGripValue);
            rightHandAnimator.SetTrigger(rightTriggerValue);
        }

        if (leftHandGrab != null)
        {
            leftHandGrab.TargetPosition = leftHandGrab.transform.position;
            leftHandGrab.TargetRotation = leftHandGrab.transform.rotation;
        }
        if (rightHandGrab != null)
        {
            rightHandGrab.TargetPosition = rightHandGrab.transform.position;
            rightHandGrab.TargetRotation = rightHandGrab.transform.rotation;
        }

        // Update door handle physics hold (VR)
        if (leftHeldHandle != null)
        {
            leftHeldHandle.UpdateHoldVR(leftHandGrab, Time.deltaTime);
            if (!leftHeldHandle.IsHeld) leftHeldHandle = null;
        }
        if (rightHeldHandle != null)
        {
            rightHeldHandle.UpdateHoldVR(rightHandGrab, Time.deltaTime);
            if (!rightHeldHandle.IsHeld) rightHeldHandle = null;
        }

        // Proximity highlight for nearby door handles when hands are free
        UpdateHandleProximityHighlight(leftHandGrab, ref leftNearHandle, leftHeldHandle);
        UpdateHandleProximityHighlight(rightHandGrab, ref rightNearHandle, rightHeldHandle);

        // Handle left and right hand grab/release with hysteresis
        ProcessGrip(leftGripValue, ref leftGripActive, leftHandGrab, ref leftHeldHandle);
        ProcessGrip(rightGripValue, ref rightGripActive, rightHandGrab, ref rightHeldHandle);

        // Handle trigger clicks (quick door toggle or item use)
        ProcessTrigger(leftTriggerValue, ref leftTriggerActive, leftHandGrab, leftHeldHandle);
        ProcessTrigger(rightTriggerValue, ref rightTriggerActive, rightHandGrab, rightHeldHandle);

        // Two-handed item separation check (auto-release secondary hand if hands pull apart too far)
        if (leftHandGrab != null && rightHandGrab != null &&
            leftHandGrab.IsHolding && rightHandGrab.IsHolding &&
            leftHandGrab.HeldItem != null && leftHandGrab.HeldItem == rightHandGrab.HeldItem)
        {
            Vector3 lPos = (leftHandGrab.visualGripPoint != null ? leftHandGrab.visualGripPoint : leftHandGrab.gripPoint).position;
            Vector3 rPos = (rightHandGrab.visualGripPoint != null ? rightHandGrab.visualGripPoint : rightHandGrab.gripPoint).position;
            if (Vector3.Distance(lPos, rPos) > 0.7f)
            {
                var item = leftHandGrab.HeldItem;
                if (item.secondaryHand != null)
                {
                    item.secondaryHand.ReleaseItem(false);
                }
            }
        }
    }

    void LateUpdate()
    {
        UpdateVisualHandPinning(leftHandGrab, leftHeldHandle, leftVisualBase, defaultLeftLocalPos, defaultLeftLocalRot, leftHandAnimator, ref leftIsPinned, isLeft: true);
        UpdateVisualHandPinning(rightHandGrab, rightHeldHandle, rightVisualBase, defaultRightLocalPos, defaultRightLocalRot, rightHandAnimator, ref rightIsPinned, isLeft: false);
    }

    void UpdateVisualHandPinning(HandGrabSystem hand, PhysicsDoorHandle heldHandle, Transform visualBase, Vector3 defaultPos, Quaternion defaultRot, HandAnimator handAnim, ref bool isPinned, bool isLeft)
    {
        if (visualBase == null) return;

        // 1. Door handle hold pinning
        if (heldHandle != null && heldHandle.IsHeld)
        {
            isPinned = true;
            Transform grip = heldHandle.ResolveBestGrip(hand);
            Vector3 gripPos = heldHandle.GetGripPosition(grip);

            Camera cam = Camera.main;
            Vector3 camPos = cam != null ? cam.transform.position : transform.position;
            Vector3 toCam = (camPos - gripPos).normalized;

            // Pin hand visual to grip position with slight offset towards camera so palm wraps around
            visualBase.position = gripPos + toCam * 0.05f;

            // Orient hand facing the handle
            Quaternion lookRot = Quaternion.LookRotation(-toCam, Vector3.up);
            Quaternion handTilt = isLeft ? Quaternion.Euler(0f, 0f, 90f) : Quaternion.Euler(0f, 0f, 270f);
            visualBase.rotation = lookRot * handTilt;
            if (handAnim != null) handAnim.SetWallContact(0f);
            return;
        }

        // 2. Wall / countertop / obstacle collision stopping and surface conforming (matching OpenViva 0.2.1)
        if (enableWallCollision && (hand == null || !hand.IsHolding))
        {
            Transform ctrl = visualBase.parent;
            if (ctrl != null)
            {
                Vector3 targetWorldPos = ctrl.TransformPoint(defaultPos);
                Quaternion targetWorldRot = ctrl.rotation * defaultRot;

                Camera cam = Camera.main;
                Vector3 camPos = cam != null ? cam.transform.position : transform.position;
                Vector3 shoulderOffset = isLeft ? new Vector3(-0.18f, -0.15f, -0.05f) : new Vector3(0.18f, -0.15f, -0.05f);
                Vector3 shoulderOrigin = cam != null ? cam.transform.TransformPoint(shoulderOffset) : camPos;

                Vector3 toTarget = targetWorldPos - shoulderOrigin;
                float targetDist = toTarget.magnitude;

                if (targetDist > 0.05f)
                {
                    Vector3 rayDir = toTarget / targetDist;
                    int layerMask = ~LayerMask.GetMask("Ignore Raycast");

                    var hits = Physics.SphereCastAll(shoulderOrigin, handCollisionRadius, rayDir, targetDist + fingerForwardReach, layerMask, QueryTriggerInteraction.Ignore);
                    float nearestHitDist = float.MaxValue;
                    Vector3 hitNormal = Vector3.zero;
                    Vector3 nearestHitPoint = Vector3.zero;
                    bool hitObstacle = false;

                    if (hits != null && hits.Length > 0)
                    {
                        foreach (var h in hits)
                        {
                            if (h.collider == null || h.collider.isTrigger) continue;
                            if (h.collider.transform.root == transform.root) continue;
                            if (h.distance < nearestHitDist)
                            {
                                nearestHitDist = h.distance;
                                hitNormal = h.normal;
                                nearestHitPoint = h.point;
                                hitObstacle = true;
                            }
                        }
                    }

                    if (hitObstacle)
                    {
                        isPinned = true;
                        float reachAhead = (targetDist + fingerForwardReach) - nearestHitDist;
                        float penetration = Mathf.Clamp01(reachAhead / fingerForwardReach);

                        float effectiveReach = Mathf.Lerp(fingerForwardReach, palmThickness, penetration);
                        float clampedDist = Mathf.Max(0.04f, nearestHitDist - effectiveReach + fingerWidthOffset);

                        visualBase.position = shoulderOrigin + rayDir * clampedDist;

                        // When colliding down on a table or upward-facing surface (normal pointing up)
                        if (hitNormal.y > 0.5f)
                        {
                            float targetTableY = nearestHitPoint.y + fingerWidthOffset;
                            if (visualBase.position.y > targetTableY)
                            {
                                visualBase.position = new Vector3(visualBase.position.x, Mathf.Lerp(visualBase.position.y, targetTableY, penetration), visualBase.position.z);
                            }
                            else
                            {
                                visualBase.position = new Vector3(visualBase.position.x, Mathf.Max(visualBase.position.y, targetTableY), visualBase.position.z);
                            }
                        }

                        // Surface tilt conforming (matching OpenViva 0.2.1)
                        if (hitNormal.sqrMagnitude > 0.01f && penetration > 0.02f)
                        {
                            if (hitNormal.y > 0.5f)
                            {
                                Vector3 flatFwd = Vector3.ProjectOnPlane(targetWorldRot * Vector3.forward, Vector3.up).normalized;
                                if (flatFwd.sqrMagnitude > 0.01f)
                                {
                                    Quaternion flatRot = Quaternion.LookRotation(flatFwd, Vector3.up);
                                    visualBase.rotation = Quaternion.Slerp(targetWorldRot, flatRot, penetration * 0.7f);
                                }
                                else
                                {
                                    visualBase.rotation = targetWorldRot;
                                }
                            }
                            else
                            {
                                Vector3 wallUp = Vector3.ProjectOnPlane(Vector3.up, hitNormal).normalized;
                                if (wallUp.sqrMagnitude < 0.01f && cam != null)
                                    wallUp = Vector3.ProjectOnPlane(cam.transform.up, hitNormal).normalized;

                                Vector3 tiltAxis = -Vector3.Cross(hitNormal, wallUp).normalized;
                                float tiltAngle = Mathf.Lerp(0f, 45f, penetration);
                                Quaternion wallTilt = Quaternion.AngleAxis(tiltAngle, tiltAxis);
                                visualBase.rotation = wallTilt * targetWorldRot;
                            }
                        }
                        else
                        {
                            visualBase.rotation = targetWorldRot;
                        }

                        if (handAnim != null) handAnim.SetWallContact(penetration);
                        return;
                    }
                }
            }
        }

        // 3. Normal untethered pose
        if (isPinned)
        {
            visualBase.localPosition = defaultPos;
            visualBase.localRotation = defaultRot;
            isPinned = false;
        }
        else
        {
            visualBase.localPosition = defaultPos;
            visualBase.localRotation = defaultRot;
        }

        if (handAnim != null) handAnim.SetWallContact(0f);
    }

    void ProcessTrigger(float triggerValue, ref bool triggerActive, HandGrabSystem hand, PhysicsDoorHandle heldHandle)
    {
        if (hand == null) return;

        if (!triggerActive && triggerValue >= gripThreshold)
        {
            triggerActive = true;

            // 1. If holding a handle and trigger is pulled, toggle it (if allowed)
            if (heldHandle != null)
            {
                if (heldHandle.allowQuickClickToggle)
                {
                    heldHandle.NudgeToggle();
                }
                return;
            }

            // 2. Normal item interact if holding an item
            if (hand.IsHolding && hand.HeldItem != null)
            {
                var interactable = hand.HeldItem.GetComponent<IInteractable>();
                if (interactable != null && interactable.CanInteract)
                {
                    interactable.Interact(gameObject);
                }
                return;
            }

            // 3. If hand is free, check nearby door handle or generic interactable
            if (!hand.IsHolding)
            {
                var nearbyHandle = FindNearbyDoorHandle(hand);
                if (nearbyHandle != null && nearbyHandle.allowQuickClickToggle)
                {
                    nearbyHandle.NudgeToggle();
                    return;
                }

                var nearbyInteractable = FindNearbyInteractable(hand);
                if (nearbyInteractable != null && nearbyInteractable.CanInteract)
                {
                    nearbyInteractable.Interact(gameObject);
                    return;
                }
            }
        }
        else if (triggerActive && triggerValue < gripThreshold * 0.5f)
        {
            triggerActive = false;
        }
    }

    void ProcessGrip(float gripValue, ref bool gripActive, HandGrabSystem hand, ref PhysicsDoorHandle heldHandle)
    {
        if (hand == null) return;

        if (!gripActive && gripValue >= gripThreshold)
        {
            gripActive = true;

            // If already holding a handle, do not re-grab
            if (heldHandle != null) return;

            // If already holding an item, this hand is busy
            if (hand.IsHolding) return;

            Vector3 handPos = (hand.visualGripPoint != null ? hand.visualGripPoint : (hand.gripPoint != null ? hand.gripPoint : hand.transform)).position;

            // Gather candidates for this hand
            var nearbyItem = hand.FindBestGrabbable();
            float distToItem = nearbyItem != null ? nearbyItem.GetDistanceTo(handPos) : float.MaxValue;

            var nearbyHandle = FindNearbyDoorHandle(hand);
            float distToHandle = float.MaxValue;
            if (nearbyHandle != null)
            {
                Transform grip = nearbyHandle.ResolveBestGrip(hand);
                distToHandle = Vector3.Distance(handPos, nearbyHandle.GetGripPosition(grip));
            }

            // PRIORITY: Whichever candidate is closest to the activating hand model wins!
            if (nearbyItem != null && distToItem <= distToHandle)
            {
                hand.GrabItem(nearbyItem);
                return;
            }
            else if (nearbyHandle != null && distToHandle <= distToItem)
            {
                heldHandle = nearbyHandle;
                heldHandle.BeginHold(hand, true);
                return;
            }
            else if (nearbyItem != null)
            {
                hand.GrabItem(nearbyItem);
                return;
            }
            else if (nearbyHandle != null)
            {
                heldHandle = nearbyHandle;
                heldHandle.BeginHold(hand, true);
                return;
            }
        }
        else if (gripActive && gripValue < gripThreshold * 0.5f) // Hysteresis: release at half threshold
        {
            gripActive = false;

            if (heldHandle != null)
            {
                heldHandle.EndHold(hand);
                heldHandle = null;
            }

            if (hand.IsHolding)
                hand.ReleaseItem(true);
        }
    }

    PhysicsDoorHandle FindNearbyDoorHandle(HandGrabSystem hand)
    {
        if (hand == null) return null;
        Vector3 handPos = (hand.visualGripPoint != null ? hand.visualGripPoint : (hand.gripPoint != null ? hand.gripPoint : hand.transform)).position;
        Collider[] hits = Physics.OverlapSphere(handPos, handleGrabRadius, ~0, QueryTriggerInteraction.Collide);
        PhysicsDoorHandle bestHandle = null;
        float bestDist = float.MaxValue;

        for (int i = 0; i < hits.Length; i++)
        {
            var col = hits[i];
            if (col.CompareTag("Player") || col.GetComponentInParent<CharacterController>() != null) continue;

            // Handle must belong to this collider or its immediate door body, NOT arbitrary parent furniture/room triggers
            PhysicsDoorHandle handle = col.GetComponentInParent<PhysicsDoorHandle>();
            if (handle == null)
            {
                var childHandle = col.GetComponentInChildren<PhysicsDoorHandle>();
                if (childHandle != null && childHandle.doorRigidbody != null)
                {
                    if (col.transform == childHandle.doorRigidbody.transform || col.transform.IsChildOf(childHandle.doorRigidbody.transform))
                    {
                        handle = childHandle;
                    }
                }
            }

            if (handle != null)
            {
                // Check occlusion by door slab
                if (!handle.CanReachFrom(handPos)) continue;

                Transform bestGrip = handle.ResolveBestGrip(hand);
                Vector3 gripPos = handle.GetGripPosition(bestGrip);
                float d = Vector3.Distance(handPos, gripPos);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestHandle = handle;
                }
            }
        }
        return bestHandle;
    }

    IInteractable FindNearbyInteractable(HandGrabSystem hand)
    {
        if (hand == null) return null;
        Vector3 handPos = (hand.visualGripPoint != null ? hand.visualGripPoint : (hand.gripPoint != null ? hand.gripPoint : hand.transform)).position;
        Collider[] hits = Physics.OverlapSphere(handPos, handleGrabRadius, ~0, QueryTriggerInteraction.Collide);
        IInteractable bestInteractable = null;
        float bestDist = float.MaxValue;

        for (int i = 0; i < hits.Length; i++)
        {
            var col = hits[i];
            if (col.CompareTag("Player") || col.GetComponentInParent<CharacterController>() != null) continue;

            var interactable = col.GetComponentInParent<IInteractable>();
            if (interactable != null && !(interactable is PhysicsDoorHandle) && col.GetComponentInParent<GrabbableItem>() == null)
            {
                if (interactable.CanInteract)
                {
                    float d = Vector3.Distance(handPos, col.bounds.ClosestPoint(handPos));
                    if (d < bestDist)
                    {
                        bestDist = d;
                        bestInteractable = interactable;
                    }
                }
            }
        }
        return bestInteractable;
    }

    void UpdateHandleProximityHighlight(HandGrabSystem hand, ref PhysicsDoorHandle nearHandle, PhysicsDoorHandle currentlyHeld)
    {
        if (hand == null || currentlyHeld != null || hand.IsHolding)
        {
            if (nearHandle != null)
            {
                nearHandle.SetHighlight(false);
                nearHandle = null;
            }
            return;
        }

        var found = FindNearbyDoorHandle(hand);

        // If this hand is closer to a grabbable item than to the door handle, do not highlight the door handle!
        if (found != null)
        {
            var bestItem = hand.FindBestGrabbable();
            if (bestItem != null)
            {
                Vector3 handPos = (hand.visualGripPoint != null ? hand.visualGripPoint : (hand.gripPoint != null ? hand.gripPoint : hand.transform)).position;
                float distItem = bestItem.GetDistanceTo(handPos);
                Transform grip = found.ResolveBestGrip(hand);
                float distHandle = Vector3.Distance(handPos, found.GetGripPosition(grip));
                if (distItem < distHandle)
                {
                    found = null;
                }
            }
        }

        if (found != nearHandle)
        {
            if (nearHandle != null) nearHandle.SetHighlight(false);
            nearHandle = found;
            if (nearHandle != null) nearHandle.SetHighlight(true, hand.transform.position);
        }
    }

    #region Grab/Release Callbacks

    void OnLeftGrabbed(GrabbableItem item)
    {
        if (leftHandAnimator != null)
            leftHandAnimator.SetHoldPose(item);
    }

    void OnLeftReleased(GrabbableItem item)
    {
        if (leftHandAnimator != null)
            leftHandAnimator.ClearHoldPose();
    }

    void OnRightGrabbed(GrabbableItem item)
    {
        if (rightHandAnimator != null)
            rightHandAnimator.SetHoldPose(item);
    }

    void OnRightReleased(GrabbableItem item)
    {
        if (rightHandAnimator != null)
            rightHandAnimator.ClearHoldPose();
    }

    #endregion

    #region Utility

    float ReadActionOrFallback(InputActionReference actionRef, InputAction fallbackAction)
    {
        if (actionRef != null && actionRef.action != null)
        {
            if (actionRef.action.type == InputActionType.Button)
                return actionRef.action.IsPressed() ? 1f : 0f;
            return actionRef.action.ReadValue<float>();
        }
        if (fallbackAction != null)
        {
            if (fallbackAction.type == InputActionType.Button)
                return fallbackAction.IsPressed() ? 1f : 0f;
            return fallbackAction.ReadValue<float>();
        }
        return 0f;
    }

    float ReadActionValue(InputActionReference actionRef)
    {
        if (actionRef == null || actionRef.action == null) return 0f;
        return actionRef.action.ReadValue<float>();
    }

    void EnableAction(InputActionReference actionRef)
    {
        if (actionRef != null && actionRef.action != null)
            actionRef.action.Enable();
    }

    void DisableAction(InputActionReference actionRef)
    {
        if (actionRef != null && actionRef.action != null)
            actionRef.action.Disable();
    }

    #endregion
}

#endif
