#if UNITY_EDITOR || UNITY_ANDROID

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
                leftHandGrab.isLeftHand = true;
                leftHandGrab.gripPoint = lWrist != null ? lWrist : lCtrl;
                leftHandGrab.isKinematicallyDriven = true;
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
        }

        if (rCtrl != null)
        {
            var rrb = rCtrl.GetComponent<Rigidbody>();
            if (rrb != null) rrb.isKinematic = true;

            if (rightHandGrab == null) rightHandGrab = rCtrl.GetComponent<HandGrabSystem>();
            if (rightHandGrab == null)
            {
                rightHandGrab = rCtrl.gameObject.AddComponent<HandGrabSystem>();
                rightHandGrab.isLeftHand = false;
                rightHandGrab.gripPoint = rWrist != null ? rWrist : rCtrl;
                rightHandGrab.isKinematicallyDriven = true;
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
        }

        SubscribeToHands();
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
    }

    void LateUpdate()
    {
        UpdateVisualHandPinning(leftHandGrab, leftHeldHandle, leftVisualBase, defaultLeftLocalPos, defaultLeftLocalRot, ref leftIsPinned, isLeft: true);
        UpdateVisualHandPinning(rightHandGrab, rightHeldHandle, rightVisualBase, defaultRightLocalPos, defaultRightLocalRot, ref rightIsPinned, isLeft: false);
    }

    void UpdateVisualHandPinning(HandGrabSystem hand, PhysicsDoorHandle heldHandle, Transform visualBase, Vector3 defaultPos, Quaternion defaultRot, ref bool isPinned, bool isLeft)
    {
        if (visualBase == null) return;

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
        }
        else if (isPinned)
        {
            visualBase.localPosition = defaultPos;
            visualBase.localRotation = defaultRot;
            isPinned = false;
        }
    }

    void ProcessTrigger(float triggerValue, ref bool triggerActive, HandGrabSystem hand, PhysicsDoorHandle heldHandle)
    {
        if (hand == null) return;

        if (!triggerActive && triggerValue >= gripThreshold)
        {
            triggerActive = true;

            // If holding a handle and trigger is pulled, toggle it (if allowed)
            if (heldHandle != null)
            {
                if (heldHandle.allowQuickClickToggle)
                {
                    heldHandle.NudgeToggle();
                }
                return;
            }

            // If near a handle and hand is free, toggle it (if allowed)
            if (!hand.IsHolding)
            {
                var nearbyHandle = FindNearbyDoorHandle(hand);
                if (nearbyHandle != null)
                {
                    if (nearbyHandle.allowQuickClickToggle)
                    {
                        nearbyHandle.NudgeToggle();
                    }
                    return;
                }
            }

            // Normal item interact if holding an item
            if (hand.IsHolding && hand.HeldItem != null)
            {
                var interactable = hand.HeldItem.GetComponent<IInteractable>();
                if (interactable != null && interactable.CanInteract)
                {
                    interactable.Interact(gameObject);
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

            // Check for nearby door handle first!
            var nearbyHandle = FindNearbyDoorHandle(hand);
            if (nearbyHandle != null)
            {
                heldHandle = nearbyHandle;
                heldHandle.BeginHold(hand, true);
                return;
            }

            if (!hand.IsHolding)
                hand.AttemptGrab();
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
        Vector3 handPos = hand.transform.position;
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
        if (found != nearHandle)
        {
            if (nearHandle != null) nearHandle.SetHighlight(false);
            nearHandle = found;
            if (nearHandle != null) nearHandle.SetHighlight(true);
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
