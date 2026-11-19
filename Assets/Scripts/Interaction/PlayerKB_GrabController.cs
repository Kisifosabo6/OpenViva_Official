#if UNITY_EDITOR || UNITY_STANDALONE_WIN

using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Full desktop (KB&M) interaction controller. Handles all non-VR hand interactions:
/// - LMB/RMB: grab/drop items (toggle mode, like old system)
/// - E: interact with world objects (doors, buttons, NPCs)
/// - Q: toggle bag open/close
/// - Scroll: extend/retract hands
/// - Crosshair-based item detection (raycast from camera center)
/// - Item held in hand stays locked relative to head (like old freezeKeyboardLocalPosition)
/// - HUD hints for available actions
///
/// Replaces: PlayerKB_GrabObject (per-item), parts of PlayerKB_BasicActions (scroll),
///           and adds missing interact/use/gesture functionality.
/// </summary>
public class PlayerKB_GrabController : MonoBehaviour
{
    [Header("Hand References")]
    [Tooltip("HandGrabSystem on the left hand")]
    public HandGrabSystem leftHand;
    [Tooltip("HandGrabSystem on the right hand")]
    public HandGrabSystem rightHand;

    [Header("Hand Driver")]
    [Tooltip("Controls desktop hand positioning (extend/retract, head-lock)")]
    public DesktopHandDriver handDriver;

    [Header("Hand Animators (Optional)")]
    public HandAnimator leftHandAnimator;
    public HandAnimator rightHandAnimator;

    [Header("Grab Detection")]
    [Tooltip("Camera used for screen-center raycasting")]
    public Camera playerCamera;
    [Tooltip("Max raycast distance from camera for grab/interact detection")]
    public float cameraReachDistance = 2.5f;
    [Tooltip("Layer mask for grabbable objects")]
    public LayerMask grabLayerMask = ~0;
    [Tooltip("Layer mask for interactable objects (doors, buttons, etc.)")]
    public LayerMask interactLayerMask = ~0;

    [Header("Physical Hand Proximity & Reach")]
    [Tooltip("Maximum physical distance between hand grip point and object surface to allow grab")]
    public float maxHandReachDistance = 0.65f;
    [Tooltip("Layer mask for solid obstacles (walls, doors, floors) blocking line of sight")]
    public LayerMask obstacleLayerMask = ~0;

    [Header("HUD")]
    public HintManager hintManager;

    [Header("Crosshair")]
    [Tooltip("Optional crosshair UI element — enabled when not in menu")]
    public GameObject crosshairUI;

    private static PlayerKB_GrabController _instance;
    public static PlayerKB_GrabController Instance
    {
        get
        {
            if (_instance == null) _instance = Object.FindFirstObjectByType<PlayerKB_GrabController>();
            return _instance;
        }
        private set => _instance = value;
    }

    // Input — shared single instance
    PlayerControls playerInput;

    // Highlight tracking
    GrabbableItem highlightedItem;
    IInteractable highlightedInteractable;
    PhysicsDoorHandle highlightedDoorHandle;
    PhysicsDoorHandle activeDoorHandleLeft;
    PhysicsDoorHandle activeDoorHandleRight;
    float leftDoorHoldStartTime;
    float rightDoorHoldStartTime;
    Vector3 lastPlayerPos;
    string currentGrabPrompt;
    string currentDoorPrompt;

    // Interaction cooldown
    float interactCooldown;

    // Point gesture tracking on empty click
    bool leftGrabHeld;
    bool rightGrabHeld;
    float leftPointDuration;
    float rightPointDuration;

    void Awake()
    {
        Instance = this;
        playerInput = new PlayerControls();
        lastPlayerPos = transform.position;

        // Grab bindings
        playerInput.Viva.DesktopLeftGrab.performed += OnLeftGrab;
        playerInput.Viva.DesktopLeftGrab.canceled += OnLeftGrabReleased;
        playerInput.Viva.DesktopRightGrab.performed += OnRightGrab;
        playerInput.Viva.DesktopRightGrab.canceled += OnRightGrabReleased;

        // Interact (E key)
        playerInput.Viva.UniversalInteract.performed += OnInteract;

        // Scroll (hand extend/retract)
        playerInput.Viva.ScrollUp.performed += OnScrollUp;
        playerInput.Viva.ScrollDown.performed += OnScrollDown;
    }

    void OnEnable()
    {
        playerInput.Viva.Enable();
        SubscribeToHands();
    }

    void OnDisable()
    {
        playerInput.Viva.Disable();
        UnsubscribeFromHands();
    }

    /// <summary>
    /// Re-subscribe in Start() in case references were set after OnEnable
    /// (happens when components are added dynamically at runtime by bootstrap).
    /// </summary>
    void Start()
    {
        SubscribeToHands();

        // Fallback auto-find hand animators if not wired in inspector
        if (leftHandAnimator == null || rightHandAnimator == null)
        {
            var anims = GetComponentsInChildren<HandAnimator>(true);
            foreach (var a in anims)
            {
                if (a.isLeftHand && leftHandAnimator == null) leftHandAnimator = a;
                else if (!a.isLeftHand && rightHandAnimator == null) rightHandAnimator = a;
            }
        }
    }

    void SubscribeToHands()
    {
        // Avoid double-subscription
        UnsubscribeFromHands();

        if (leftHand != null)
        {
            leftHand.OnItemGrabbed += OnLeftHandGrabbed;
            leftHand.OnItemReleased += OnLeftHandReleased;
        }
        if (rightHand != null)
        {
            rightHand.OnItemGrabbed += OnRightHandGrabbed;
            rightHand.OnItemReleased += OnRightHandReleased;
        }
    }

    void UnsubscribeFromHands()
    {
        if (leftHand != null)
        {
            leftHand.OnItemGrabbed -= OnLeftHandGrabbed;
            leftHand.OnItemReleased -= OnLeftHandReleased;
        }
        if (rightHand != null)
        {
            rightHand.OnItemGrabbed -= OnRightHandGrabbed;
            rightHand.OnItemReleased -= OnRightHandReleased;
        }
    }

    void Update()
    {
        if (Globals.isMenuOpen)
        {
            if (crosshairUI != null && crosshairUI.activeSelf)
                crosshairUI.SetActive(false);
            return;
        }

        // Crosshair is disabled/hidden by default unless explicitly configured and enabled
        if (interactCooldown > 0f)
            interactCooldown -= Time.deltaTime;

        // Auto-release point gesture after minimum duration if mouse button was released
        if (leftPointDuration > 0f)
        {
            leftPointDuration -= Time.deltaTime;
            if (leftPointDuration <= 0f && !leftGrabHeld && leftHandAnimator != null && leftHandAnimator.CurrentGesture == HandAnimator.Gesture.Point)
            {
                leftHandAnimator.ClearGesture();
            }
        }

        if (rightPointDuration > 0f)
        {
            rightPointDuration -= Time.deltaTime;
            if (rightPointDuration <= 0f && !rightGrabHeld && rightHandAnimator != null && rightHandAnimator.CurrentGesture == HandAnimator.Gesture.Point)
            {
                rightHandAnimator.ClearGesture();
            }
        }

        // Update door handle physics hold (Desktop)
        Vector3 currentPlayerPos = transform.position;
        Vector3 playerMoveDelta = currentPlayerPos - lastPlayerPos;
        lastPlayerPos = currentPlayerPos;

        Vector2 mouseDelta = Vector2.zero;
        if (Mouse.current != null)
        {
            mouseDelta = Mouse.current.delta.ReadValue();
        }

        if (activeDoorHandleLeft != null)
        {
            activeDoorHandleLeft.UpdateHoldDesktop(leftHand, Time.deltaTime);
            if (!activeDoorHandleLeft.IsHeld)
            {
                activeDoorHandleLeft = null;
            }
        }

        if (activeDoorHandleRight != null)
        {
            activeDoorHandleRight.UpdateHoldDesktop(rightHand, Time.deltaTime);
            if (!activeDoorHandleRight.IsHeld)
            {
                activeDoorHandleRight = null;
            }
        }

        UpdateCameraHighlight();
    }

    #region Input Handlers

    void OnLeftGrab(InputAction.CallbackContext ctx)
    {
        if (Globals.isMenuOpen || leftHand == null) return;
        TryInteractOrGrabWithHand(leftHand, isLeft: true);
    }

    void OnRightGrab(InputAction.CallbackContext ctx)
    {
        if (Globals.isMenuOpen || rightHand == null) return;
        TryInteractOrGrabWithHand(rightHand, isLeft: false);
    }

    void TryInteractOrGrabWithHand(HandGrabSystem hand, bool isLeft)
    {
        if (hand == null) return;

        // 1. If this hand is holding a door handle, release it
        if (isLeft && activeDoorHandleLeft != null)
        {
            activeDoorHandleLeft.EndHold(leftHand);
            activeDoorHandleLeft = null;
            return;
        }
        if (!isLeft && activeDoorHandleRight != null)
        {
            activeDoorHandleRight.EndHold(rightHand);
            activeDoorHandleRight = null;
            return;
        }

        // 2. If this hand is holding an item, toggle drop
        if (hand.IsHolding)
        {
            hand.ReleaseItem(true);
            return;
        }

        // 3. Find candidates for this activating hand
        Vector3 handPos = (hand.visualGripPoint != null ? hand.visualGripPoint : (hand.gripPoint != null ? hand.gripPoint : hand.transform)).position;

        // Candidate A: Grabbable item
        GrabbableItem candidateItem = null;
        float distToItem = float.MaxValue;

        var lookedItem = GetLookedAtGrabbable(hand);
        if (lookedItem != null && CanHandReachItem(hand, lookedItem, out float dLookedItem))
        {
            candidateItem = lookedItem;
            distToItem = dLookedItem;
        }
        else
        {
            var nearbyItem = hand.FindBestGrabbable();
            if (nearbyItem != null && CanHandReachItem(hand, nearbyItem, out float dNearItem))
            {
                candidateItem = nearbyItem;
                distToItem = dNearItem;
            }
        }

        // Candidate B: Door handle
        PhysicsDoorHandle candidateHandle = null;
        float distToHandle = float.MaxValue;

        var lookedHandle = GetLookedAtDoorHandle(hand, out float dLookedHandle);
        if (lookedHandle != null)
        {
            candidateHandle = lookedHandle;
            distToHandle = dLookedHandle;
        }
        else
        {
            var nearbyHandle = FindNearbyDoorHandle(hand, out float dNearHandle);
            if (nearbyHandle != null)
            {
                candidateHandle = nearbyHandle;
                distToHandle = dNearHandle;
            }
        }

        // Candidate C: Generic interactable
        IInteractable candidateInteractable = null;
        float distToInt = float.MaxValue;

        var lookedInt = GetLookedAtInteractable(hand, out float dLookedInt);
        if (lookedInt != null)
        {
            candidateInteractable = lookedInt;
            distToInt = dLookedInt;
        }
        else
        {
            var nearbyInt = FindNearbyInteractable(hand, out float dNearInt);
            if (nearbyInt != null)
            {
                candidateInteractable = nearbyInt;
                distToInt = dNearInt;
            }
        }

        // 4. PRIORITY: Whichever candidate is closest to the activating hand model wins!
        float minDist = Mathf.Min(distToItem, Mathf.Min(distToHandle, distToInt));

        if (minDist == float.MaxValue)
        {
            // Nothing reachable to grab or interact -> Point gesture!
            TriggerPointGesture(isLeft);
            return;
        }

        if (minDist == distToItem && candidateItem != null)
        {
            hand.GrabItem(candidateItem);
            return;
        }

        if (minDist == distToHandle && candidateHandle != null)
        {
            if (isLeft)
            {
                activeDoorHandleLeft = candidateHandle;
                leftDoorHoldStartTime = Time.time;
                leftGrabHeld = true;
                activeDoorHandleLeft.BeginHold(leftHand, false);
            }
            else
            {
                activeDoorHandleRight = candidateHandle;
                rightDoorHoldStartTime = Time.time;
                rightGrabHeld = true;
                activeDoorHandleRight.BeginHold(rightHand, false);
            }
            return;
        }

        if (minDist == distToInt && candidateInteractable != null)
        {
            candidateInteractable.Interact(this.gameObject);
            return;
        }

        TriggerPointGesture(isLeft);
    }

    void OnLeftGrabReleased(InputAction.CallbackContext ctx)
    {
        leftGrabHeld = false;

        // If holding a door handle, only release on mouse button release if it was a drag (> 0.35s).
        // A short click (<= 0.35s) keeps holding in toggle mode so player can walk/move freely!
        if (activeDoorHandleLeft != null)
        {
            if (Time.time - leftDoorHoldStartTime > 0.35f)
            {
                activeDoorHandleLeft.EndHold(leftHand);
                activeDoorHandleLeft = null;
            }
        }

        if (leftPointDuration <= 0f && leftHandAnimator != null && leftHandAnimator.CurrentGesture == HandAnimator.Gesture.Point)
        {
            leftHandAnimator.ClearGesture();
        }
    }

    void OnRightGrabReleased(InputAction.CallbackContext ctx)
    {
        rightGrabHeld = false;

        // If holding a door handle, only release on mouse button release if it was a drag (> 0.35s).
        // A short click (<= 0.35s) keeps holding in toggle mode so player can walk/move freely!
        if (activeDoorHandleRight != null)
        {
            if (Time.time - rightDoorHoldStartTime > 0.35f)
            {
                activeDoorHandleRight.EndHold(rightHand);
                activeDoorHandleRight = null;
            }
        }

        if (rightPointDuration <= 0f && rightHandAnimator != null && rightHandAnimator.CurrentGesture == HandAnimator.Gesture.Point)
        {
            rightHandAnimator.ClearGesture();
        }
    }

    void TriggerPointGesture(bool isLeft)
    {
        HandAnimator animator = isLeft ? leftHandAnimator : rightHandAnimator;
        if (animator == null) return;

        animator.PlayGesture(HandAnimator.Gesture.Point);
        if (isLeft)
        {
            leftGrabHeld = true;
            leftPointDuration = 0.35f;
        }
        else
        {
            rightGrabHeld = true;
            rightPointDuration = 0.35f;
        }

        // Broadcast pointing to nearby NPCs / dolls
        var colliders = Physics.OverlapSphere(transform.position, 8f);
        foreach (var col in colliders)
        {
            var receiver = col.GetComponentInParent<IGestureReceiver>();
            if (receiver != null)
                receiver.OnPlayerGesture("point", transform);
        }
    }

    void OnInteract(InputAction.CallbackContext ctx)
    {
        if (Globals.isMenuOpen) return;
        if (interactCooldown > 0f) return;

        // If currently holding a door handle, release it and toggle
        if (activeDoorHandleLeft != null)
        {
            var h = activeDoorHandleLeft;
            activeDoorHandleLeft.EndHold(leftHand);
            activeDoorHandleLeft = null;
            h.NudgeToggle();
            interactCooldown = 0.3f;
            return;
        }
        if (activeDoorHandleRight != null)
        {
            var h = activeDoorHandleRight;
            activeDoorHandleRight.EndHold(rightHand);
            activeDoorHandleRight = null;
            h.NudgeToggle();
            interactCooldown = 0.3f;
            return;
        }

        // Try door handle first
        if (highlightedDoorHandle != null)
        {
            if (highlightedDoorHandle.allowQuickClickToggle)
            {
                highlightedDoorHandle.NudgeToggle();
                interactCooldown = 0.3f;
            }
            return;
        }

        // Try to interact with whatever we're looking at
        if (highlightedInteractable != null)
        {
            highlightedInteractable.Interact(this.gameObject);
            interactCooldown = 0.3f;
            return;
        }

        // Also check for any Collider with IInteractable via raycast
        if (playerCamera != null)
        {
            Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            if (Physics.Raycast(ray, out RaycastHit hit, cameraReachDistance, interactLayerMask))
            {
                var interactable = hit.collider.GetComponentInParent<IInteractable>();
                if (interactable != null)
                {
                    interactable.Interact(this.gameObject);
                    interactCooldown = 0.3f;
                    return;
                }
            }
        }

        // If holding an interactable item in either hand, interact with it
        if (rightHand != null && rightHand.IsHolding && rightHand.HeldItem != null)
        {
            var interactable = rightHand.HeldItem.GetComponent<IInteractable>();
            if (interactable != null && interactable.CanInteract)
            {
                interactable.Interact(this.gameObject);
                interactCooldown = 0.3f;
                return;
            }
        }
        if (leftHand != null && leftHand.IsHolding && leftHand.HeldItem != null)
        {
            var interactable = leftHand.HeldItem.GetComponent<IInteractable>();
            if (interactable != null && interactable.CanInteract)
            {
                interactable.Interact(this.gameObject);
                interactCooldown = 0.3f;
                return;
            }
        }
    }

    void OnScrollUp(InputAction.CallbackContext ctx)
    {
        if (Globals.isMenuOpen) return;
        if (handDriver != null)
            handDriver.ExtendHands();
    }

    void OnScrollDown(InputAction.CallbackContext ctx)
    {
        if (Globals.isMenuOpen) return;
        if (handDriver != null)
            handDriver.RetractHands();
    }

    #endregion

    #region Physical Reach & Line of Sight Checks

    /// <summary>
    /// Checks whether a specific hand is physically close enough to reach an item and has clear line of sight.
    /// </summary>
    public bool CanHandReachItem(HandGrabSystem hand, GrabbableItem item, out float distance)
    {
        distance = float.MaxValue;
        if (hand == null || item == null) return false;
        Transform grip = hand.visualGripPoint != null ? hand.visualGripPoint : hand.gripPoint;
        if (grip == null) return false;

        Vector3 handPos = grip.position;

        var colliders = item.GetComponentsInChildren<Collider>();
        if (colliders != null && colliders.Length > 0)
        {
            foreach (var col in colliders)
            {
                if (col == null || !col.enabled || col.isTrigger) continue;
                Vector3 closest = GetClosestPointSafe(col, handPos);
                float d = Vector3.Distance(handPos, closest);
                if (d < distance) distance = d;
            }
        }

        if (distance == float.MaxValue)
        {
            distance = Vector3.Distance(handPos, item.transform.position);
        }

        if (distance > maxHandReachDistance) return false;

        return HasLineOfSight(handPos, item);
    }

    Vector3 GetClosestPointSafe(Collider col, Vector3 point)
    {
        if (col is BoxCollider || col is SphereCollider || col is CapsuleCollider)
            return col.ClosestPoint(point);
        if (col is MeshCollider mc)
        {
            if (mc.convex) return mc.ClosestPoint(point);
            return col.bounds.ClosestPoint(point);
        }
        return col.bounds.ClosestPoint(point);
    }

    /// <summary>
    /// Verifies there is no solid wall or obstacle between the hand position and the item surface.
    /// </summary>
    public bool HasLineOfSight(Vector3 handPos, GrabbableItem item)
    {
        if (item == null) return false;

        Vector3 targetPoint = item.transform.position;
        var cols = item.GetComponentsInChildren<Collider>();
        if (cols != null && cols.Length > 0)
        {
            foreach (var col in cols)
            {
                if (col != null && col.enabled && !col.isTrigger)
                {
                    targetPoint = GetClosestPointSafe(col, handPos);
                    break;
                }
            }
        }

        Vector3 toTarget = targetPoint - handPos;
        float dist = toTarget.magnitude;
        if (dist <= 0.05f) return true;

        Vector3 dir = toTarget / dist;
        float rayDist = Mathf.Max(0.01f, dist - 0.02f);
        RaycastHit[] hits = Physics.RaycastAll(handPos, dir, rayDist, obstacleLayerMask, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hits.Length; i++)
        {
            var hitCol = hits[i].collider;
            if (hitCol == null) continue;

            if (hitCol.transform.IsChildOf(item.transform) || hitCol.gameObject == item.gameObject)
                continue;
            if (hitCol.CompareTag("Player") || hitCol.GetComponentInParent<CharacterController>() != null)
                continue;
            if (hitCol.name.StartsWith("hand_") || hitCol.name.StartsWith("PhysicsHand_") || hitCol.name.StartsWith("wrist_"))
                continue;
            if (hitCol.transform.IsChildOf(transform))
                continue;

            // Blocked by a solid wall or obstacle!
            return false;
        }

        return true;
    }

    #endregion

    #region Camera-based detection (Desktop specific)

    /// <summary>
    /// Raycasts from screen center to find what grabbable item the player is looking at.
    /// Supports two-handed pickup: allows targeting an item already held by another hand.
    /// </summary>
    public GrabbableItem GetLookedAtGrabbable(HandGrabSystem requestingHand = null)
    {
        if (playerCamera == null) return null;

        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        RaycastHit[] hits = Physics.RaycastAll(ray, cameraReachDistance, grabLayerMask, QueryTriggerInteraction.Ignore);
        GrabbableItem bestItem = null;
        float bestRayDist = float.MaxValue;

        for (int i = 0; i < hits.Length; i++)
        {
            var hit = hits[i];
            var col = hit.collider;
            if (col == null || col.CompareTag("Player") || col.GetComponentInParent<CharacterController>() != null)
                continue;

            // Reject world architecture and door bodies
            string nameLower = col.name.ToLower();
            if (nameLower.Contains("door") || nameLower.Contains("handle") || nameLower.Contains("wall") || nameLower.Contains("floor"))
                continue;

            var item = HandGrabSystem.GetOrAddGrabbable(col.gameObject);
            if (item != null && item.canBePickedUp)
            {
                if (requestingHand != null)
                {
                    if (item.IsHeldBy(requestingHand) || item.IsTwoHandHeld) continue;
                }
                else if (item.IsTwoHandHeld)
                {
                    continue;
                }

                if (hit.distance < bestRayDist)
                {
                    bestRayDist = hit.distance;
                    bestItem = item;
                }
            }
        }
        return bestItem;
    }

    public PhysicsDoorHandle GetLookedAtDoorHandle(HandGrabSystem requestingHand, out float distToHand)
    {
        distToHand = float.MaxValue;
        if (playerCamera == null) return null;

        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        RaycastHit[] hits = Physics.RaycastAll(ray, cameraReachDistance, ~0, QueryTriggerInteraction.Collide);
        PhysicsDoorHandle bestHandle = null;
        float bestRayDist = float.MaxValue;

        Vector3 handPos = requestingHand != null
            ? (requestingHand.visualGripPoint != null ? requestingHand.visualGripPoint.position : requestingHand.gripPoint.position)
            : ray.origin;

        for (int i = 0; i < hits.Length; i++)
        {
            var col = hits[i].collider;
            if (col == null || col.CompareTag("Player") || col.GetComponentInParent<CharacterController>() != null) continue;

            PhysicsDoorHandle pdh = col.GetComponentInParent<PhysicsDoorHandle>();
            if (pdh == null)
            {
                var childPdh = col.GetComponentInChildren<PhysicsDoorHandle>();
                if (childPdh != null && childPdh.doorRigidbody != null)
                {
                    if (col.transform == childPdh.doorRigidbody.transform || col.transform.IsChildOf(childPdh.doorRigidbody.transform))
                        pdh = childPdh;
                }
            }

            if (pdh != null)
            {
                Vector3 gripPos = pdh.GetGripPosition(pdh.ResolveBestGrip(requestingHand));
                float dHand = Vector3.Distance(handPos, gripPos);

                if (dHand <= 1.8f && pdh.CanReachFrom(handPos))
                {
                    if (hits[i].distance < bestRayDist)
                    {
                        bestRayDist = hits[i].distance;
                        bestHandle = pdh;
                        distToHand = dHand;
                    }
                }
            }
        }
        return bestHandle;
    }

    public PhysicsDoorHandle FindNearbyDoorHandle(HandGrabSystem hand, out float distance)
    {
        distance = float.MaxValue;
        if (hand == null) return null;

        Vector3 handPos = (hand.visualGripPoint != null ? hand.visualGripPoint : (hand.gripPoint != null ? hand.gripPoint : hand.transform)).position;
        Collider[] hits = Physics.OverlapSphere(handPos, 0.45f, ~0, QueryTriggerInteraction.Collide);
        PhysicsDoorHandle bestHandle = null;

        for (int i = 0; i < hits.Length; i++)
        {
            var col = hits[i];
            if (col == null || col.CompareTag("Player") || col.GetComponentInParent<CharacterController>() != null) continue;

            PhysicsDoorHandle pdh = col.GetComponentInParent<PhysicsDoorHandle>();
            if (pdh == null)
            {
                var childPdh = col.GetComponentInChildren<PhysicsDoorHandle>();
                if (childPdh != null && childPdh.doorRigidbody != null)
                {
                    if (col.transform == childPdh.doorRigidbody.transform || col.transform.IsChildOf(childPdh.doorRigidbody.transform))
                        pdh = childPdh;
                }
            }

            if (pdh != null && pdh.CanReachFrom(handPos))
            {
                Vector3 gripPos = pdh.GetGripPosition(pdh.ResolveBestGrip(hand));
                float d = Vector3.Distance(handPos, gripPos);
                if (d < distance)
                {
                    distance = d;
                    bestHandle = pdh;
                }
            }
        }
        return bestHandle;
    }

    public IInteractable GetLookedAtInteractable(HandGrabSystem requestingHand, out float distToHand)
    {
        distToHand = float.MaxValue;
        if (playerCamera == null) return null;

        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        RaycastHit[] hits = Physics.RaycastAll(ray, cameraReachDistance, interactLayerMask, QueryTriggerInteraction.Collide);
        IInteractable bestInteractable = null;
        float bestRayDist = float.MaxValue;

        Vector3 handPos = requestingHand != null
            ? (requestingHand.visualGripPoint != null ? requestingHand.visualGripPoint.position : requestingHand.gripPoint.position)
            : ray.origin;

        for (int i = 0; i < hits.Length; i++)
        {
            var col = hits[i].collider;
            if (col == null || col.CompareTag("Player") || col.GetComponentInParent<CharacterController>() != null) continue;

            var interactable = col.GetComponentInParent<IInteractable>();
            if (interactable == null && col.name.ToLower().Contains("candle"))
                interactable = col.GetComponentInParent<InteractCandle>() ?? col.gameObject.AddComponent<InteractCandle>();

            // Exclude door handles and grabbable items here (they are handled as specialized candidates)
            if (interactable != null && !(interactable is PhysicsDoorHandle) && col.GetComponentInParent<GrabbableItem>() == null)
            {
                if (interactable.CanInteract && hits[i].distance < bestRayDist)
                {
                    bestRayDist = hits[i].distance;
                    bestInteractable = interactable;
                    distToHand = Vector3.Distance(handPos, col.bounds.ClosestPoint(handPos));
                }
            }
        }
        return bestInteractable;
    }

    public IInteractable FindNearbyInteractable(HandGrabSystem hand, out float distance)
    {
        distance = float.MaxValue;
        if (hand == null) return null;

        Vector3 handPos = (hand.visualGripPoint != null ? hand.visualGripPoint : (hand.gripPoint != null ? hand.gripPoint : hand.transform)).position;
        Collider[] hits = Physics.OverlapSphere(handPos, 0.45f, interactLayerMask, QueryTriggerInteraction.Collide);
        IInteractable bestInteractable = null;

        for (int i = 0; i < hits.Length; i++)
        {
            var col = hits[i];
            if (col == null || col.CompareTag("Player") || col.GetComponentInParent<CharacterController>() != null) continue;

            var interactable = col.GetComponentInParent<IInteractable>();
            if (interactable != null && !(interactable is PhysicsDoorHandle) && col.GetComponentInParent<GrabbableItem>() == null)
            {
                if (interactable.CanInteract)
                {
                    float d = Vector3.Distance(handPos, col.bounds.ClosestPoint(handPos));
                    if (d < distance)
                    {
                        distance = d;
                        bestInteractable = interactable;
                    }
                }
            }
        }
        return bestInteractable;
    }

    void UpdateCameraHighlight()
    {
        // 1. Door holding prompts for either hand
        string holdPrompt = null;
        if (activeDoorHandleLeft != null && activeDoorHandleRight != null)
            holdPrompt = "[LMB] / [RMB]: Release Door | Move/WASD to swing";
        else if (activeDoorHandleLeft != null)
            holdPrompt = "[LMB]: Release Door | Move/WASD to swing";
        else if (activeDoorHandleRight != null)
            holdPrompt = "[RMB]: Release Door | Move/WASD to swing";

        // Determine which hands are available for world target interactions
        bool leftAvailable = leftHand != null && !leftHand.IsHolding && activeDoorHandleLeft == null;
        bool rightAvailable = rightHand != null && !rightHand.IsHolding && activeDoorHandleRight == null;

        PhysicsDoorHandle foundDoorHandle = null;
        GrabbableItem foundItem = null;
        IInteractable foundInteractable = null;
        string grabPrompt = null;
        string doorPrompt = holdPrompt;

        // Find candidate targets in front of camera
        if (playerCamera != null && (leftAvailable || rightAvailable))
        {
            // Evaluate looked-at candidates
            float dItemHand = float.MaxValue;
            GrabbableItem itemCandidate = null;

            // Check if looking at an item
            var lookedItem = GetLookedAtGrabbable(null);
            if (lookedItem != null)
            {
                float dL = float.MaxValue;
                float dR = float.MaxValue;
                bool leftCan = leftAvailable && CanHandReachItem(leftHand, lookedItem, out dL);
                bool rightCan = rightAvailable && CanHandReachItem(rightHand, lookedItem, out dR);

                if (leftCan && rightCan)
                {
                    itemCandidate = lookedItem;
                    dItemHand = Mathf.Min(dL, dR);
                    grabPrompt = (lookedItem.IsHeld) ? "[LMB] or [RMB]: Hold with Both Hands" : "[LMB] or [RMB]: Grab";
                }
                else if (leftCan)
                {
                    itemCandidate = lookedItem;
                    dItemHand = dL;
                    grabPrompt = (lookedItem.IsHeld) ? "[LMB]: Hold with Both Hands" : "[LMB]: Grab";
                }
                else if (rightCan)
                {
                    itemCandidate = lookedItem;
                    dItemHand = dR;
                    grabPrompt = (lookedItem.IsHeld) ? "[RMB]: Hold with Both Hands" : "[RMB]: Grab";
                }
            }

            // Check if looking at a door handle
            HandGrabSystem preferHand = leftAvailable ? leftHand : rightHand;
            float dHandleHand = float.MaxValue;
            var lookedHandle = GetLookedAtDoorHandle(preferHand, out dHandleHand);

            // Check if looking at an interactable
            float dIntHand = float.MaxValue;
            var lookedInt = GetLookedAtInteractable(preferHand, out dIntHand);

            // PRIORITIZE BY DISTANCE TO AVAILABLE HANDS:
            // Whichever object is closer to the player's available hands gets highlighted!
            float bestDist = Mathf.Min(dItemHand, Mathf.Min(dHandleHand, dIntHand));

            if (bestDist < float.MaxValue)
            {
                if (bestDist == dItemHand && itemCandidate != null)
                {
                    foundItem = itemCandidate;
                }
                else if (bestDist == dHandleHand && lookedHandle != null)
                {
                    foundDoorHandle = lookedHandle;
                    string action = foundDoorHandle.allowQuickClickToggle ? "Hold Handle | [E]: Toggle" : "Hold & Move Door";
                    if (leftAvailable && rightAvailable)
                        doorPrompt = $"[LMB] or [RMB]: {action}";
                    else if (leftAvailable)
                        doorPrompt = $"[LMB]: {action}";
                    else if (rightAvailable)
                        doorPrompt = $"[RMB]: {action}";
                    else if (foundDoorHandle.allowQuickClickToggle)
                        doorPrompt = "[E]: Toggle";
                }
                else if (bestDist == dIntHand && lookedInt != null)
                {
                    foundInteractable = lookedInt;
                }
            }
        }

        // Update door handle highlight & HUD hint
        if (foundDoorHandle != highlightedDoorHandle)
        {
            if (highlightedDoorHandle != null && activeDoorHandleLeft != highlightedDoorHandle && activeDoorHandleRight != highlightedDoorHandle)
            {
                highlightedDoorHandle.SetHighlight(false);
            }
            if (!string.IsNullOrEmpty(currentDoorPrompt))
            {
                ClearHudHint(currentDoorPrompt);
                currentDoorPrompt = null;
            }

            highlightedDoorHandle = foundDoorHandle;

            if (highlightedDoorHandle != null && !string.IsNullOrEmpty(doorPrompt))
            {
                highlightedDoorHandle.SetHighlight(true, playerCamera != null ? playerCamera.transform.position : (Vector3?)null);
                currentDoorPrompt = doorPrompt;
                ShowHudHint(currentDoorPrompt);
            }
            else if (!string.IsNullOrEmpty(doorPrompt))
            {
                currentDoorPrompt = doorPrompt;
                ShowHudHint(currentDoorPrompt);
            }
        }
        else if (!string.IsNullOrEmpty(doorPrompt) && currentDoorPrompt != doorPrompt)
        {
            if (!string.IsNullOrEmpty(currentDoorPrompt)) ClearHudHint(currentDoorPrompt);
            currentDoorPrompt = doorPrompt;
            ShowHudHint(currentDoorPrompt);
        }
        else if (string.IsNullOrEmpty(doorPrompt) && !string.IsNullOrEmpty(currentDoorPrompt))
        {
            ClearHudHint(currentDoorPrompt);
            currentDoorPrompt = null;
        }

        // Update item highlight & HUD hint
        if (foundItem != highlightedItem)
        {
            if (highlightedItem != null)
            {
                highlightedItem.SetHighlight(false);
                if (!string.IsNullOrEmpty(currentGrabPrompt))
                {
                    ClearHudHint(currentGrabPrompt);
                    currentGrabPrompt = null;
                }
            }

            highlightedItem = foundItem;

            if (highlightedItem != null && !string.IsNullOrEmpty(grabPrompt))
            {
                highlightedItem.SetHighlight(true);
                currentGrabPrompt = grabPrompt;
                ShowHudHint(currentGrabPrompt);
            }
        }
        else if (highlightedItem != null && currentGrabPrompt != grabPrompt)
        {
            if (!string.IsNullOrEmpty(currentGrabPrompt)) ClearHudHint(currentGrabPrompt);
            currentGrabPrompt = grabPrompt;
            if (!string.IsNullOrEmpty(currentGrabPrompt)) ShowHudHint(currentGrabPrompt);
        }
        else if (foundItem == null && !string.IsNullOrEmpty(currentGrabPrompt))
        {
            ClearHudHint(currentGrabPrompt);
            currentGrabPrompt = null;
        }

        // Update interactable highlight
        if (foundInteractable != highlightedInteractable)
        {
            if (highlightedInteractable != null)
                ClearHudHint("[E]: Interact");

            highlightedInteractable = foundInteractable;

            if (highlightedInteractable != null)
                ShowHudHint("[E]: Interact");
        }
    }

    #endregion

    #region Grab/Release Callbacks

    void OnLeftHandGrabbed(GrabbableItem item)
    {
        if (!string.IsNullOrEmpty(currentGrabPrompt))
        {
            ClearHudHint(currentGrabPrompt);
            currentGrabPrompt = null;
        }
        ShowHudHint("[LMB]: Drop");

        if (leftHandAnimator != null)
            leftHandAnimator.SetHoldPose(item);
    }

    void OnLeftHandReleased(GrabbableItem item)
    {
        ClearHudHint("[LMB]: Drop");

        if (leftHandAnimator != null)
            leftHandAnimator.ClearHoldPose();
    }

    void OnRightHandGrabbed(GrabbableItem item)
    {
        if (!string.IsNullOrEmpty(currentGrabPrompt))
        {
            ClearHudHint(currentGrabPrompt);
            currentGrabPrompt = null;
        }
        ShowHudHint("[RMB]: Drop");

        if (rightHandAnimator != null)
            rightHandAnimator.SetHoldPose(item);
    }

    void OnRightHandReleased(GrabbableItem item)
    {
        ClearHudHint("[RMB]: Drop");

        if (rightHandAnimator != null)
            rightHandAnimator.ClearHoldPose();
    }

    #endregion

    #region HUD Helpers

    void ShowHudHint(string text)
    {
        if (hintManager == null)
            hintManager = Object.FindFirstObjectByType<HintManager>();
        if (hintManager != null)
            hintManager.CreateHint(text);
    }

    void ClearHudHint(string text)
    {
        if (hintManager == null)
            hintManager = Object.FindFirstObjectByType<HintManager>();
        if (hintManager != null)
            hintManager.ClearHint(text);
    }

    #endregion

    #region Public Queries

    /// <summary>
    /// Get what item the player is currently looking at (for other systems to use).
    /// </summary>
    public GrabbableItem GetLookedAtItem() => highlightedItem;

    /// <summary>
    /// Check if a specific hand is holding something.
    /// </summary>
    public bool IsHandHolding(bool left) => left ? (leftHand != null && leftHand.IsHolding) : (rightHand != null && rightHand.IsHolding);

    /// <summary>
    /// Get the held item for a hand.
    /// </summary>
    public GrabbableItem GetHeldItem(bool left) => left ? leftHand?.HeldItem : rightHand?.HeldItem;

    #endregion

    void OnDestroy()
    {
        if (playerInput != null)
        {
            playerInput.Viva.DesktopLeftGrab.performed -= OnLeftGrab;
            playerInput.Viva.DesktopRightGrab.performed -= OnRightGrab;
            playerInput.Viva.UniversalInteract.performed -= OnInteract;
            playerInput.Viva.ScrollUp.performed -= OnScrollUp;
            playerInput.Viva.ScrollDown.performed -= OnScrollDown;
            playerInput.Dispose();
        }
    }
}

#endif
