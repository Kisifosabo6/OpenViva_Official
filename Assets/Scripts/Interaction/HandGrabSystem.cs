using UnityEngine;

/// <summary>
/// Physics-based hand grab system matching old OpenViva's OccupyState + RigidBodyBlend.
/// Hands are REAL Rigidbodies that interact with the world physically.
/// Items are grabbed via ConfigurableJoint (spring-driven) or FixedJoint.
/// Blend system ramps massScale from 0→heldMassScale over blendDuration.
/// Works for both Desktop and VR — the hand Rigidbody is ALWAYS required.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class HandGrabSystem : MonoBehaviour
{
    [Header("Hand Setup")]
    public bool isLeftHand;
    public Transform gripPoint;
    [Tooltip("Visual hand bone (hand_l / hand_r) where held items are visually parented")]
    public Transform visualGripPoint;
    public Collider handCollider;

    [Header("Hand Physics (old values: mass=5, gravity=false)")]
    [Tooltip("Force multiplier for hand chasing target position (old: 64)")]
    public float positionForce = 64f;
    [Tooltip("Torque multiplier for hand chasing target rotation (old: 128)")]
    public float rotationForce = 128f;
    [Tooltip("Distance threshold to teleport hand instead of force-chase (old: ~0.86)")]
    public float teleportThreshold = 0.75f;

    [Header("Detection")]
    public float grabRadius = 0.15f;
    public LayerMask grabLayerMask = ~0;
    public float grabRayDistance = 0.15f;

    [Header("Grab Physics (old: spring=100000, damper=10, massScale ramps 0→10)")]
    [Tooltip("Grab blend duration in seconds (old: 0.6)")]
    public float blendDuration = 0.6f;
    [Tooltip("Release blend duration (old: 0.4)")]
    public float releaseBlendDuration = 0.4f;

    [Header("Throw")]
    public int velocityFrameWindow = 6;

    // Events
    public System.Action<GrabbableItem> OnItemGrabbed;
    public System.Action<GrabbableItem> OnItemReleased;

    // State
    public GrabbableItem HeldItem { get; private set; }
    public bool IsHolding => HeldItem != null;
    public Rigidbody HandRigidbody { get; private set; }

    // Target transform that the hand chases (set by DesktopHandDriver or VR tracker)
    public Vector3 TargetPosition { get; set; }
    public Quaternion TargetRotation { get; set; }

    // Camera velocity fed by DesktopHandDriver for smooth physics tracking without lagging
    public Vector3 CameraVelocity { get; set; }

    // Frozen head-local position when holding item (desktop freeze-to-head)
    public Vector3? FrozenLocalPos;
    public Quaternion FrozenLocalRot;

    [Tooltip("Whether this hand's Rigidbody position is driven directly (e.g. by DesktopHandDriver)")]
    public bool isKinematicallyDriven = false;

    // Nearest item
    GrabbableItem nearestItem;

    // Physics joint for grab
    ConfigurableJoint grabJoint;
    FixedJoint fixedGrabJoint;
    Quaternion jointStartRotation;

    // Blend
    float blendProgress;
    float blendTarget;
    float blendSpeed;

    // Velocity tracking
    Vector3[] velocityHistory;
    Vector3[] angularVelocityHistory;
    int velocityIndex;
    Vector3 lastPosition;
    Quaternion lastRotation;

    // Held item layer management
    int originalItemLayer;
    Vector3 originalItemScale = Vector3.one;
    Vector3 heldLocalPos;
    Quaternion heldLocalRot;

    // Player collider to ignore when holding items
    Collider playerCollider;
    Collider[] heldItemColliders;

    void Awake()
    {
        HandRigidbody = GetComponent<Rigidbody>();
        if (handCollider == null)
            handCollider = GetComponent<Collider>();
        if (gripPoint == null)
            gripPoint = transform;

        // Old OpenViva hand Rigidbody setup
        HandRigidbody.mass = 5f;
        HandRigidbody.useGravity = false;
        HandRigidbody.isKinematic = false;
        HandRigidbody.linearDamping = 0f;
        HandRigidbody.angularDamping = 0f;
        HandRigidbody.maxAngularVelocity = 64f;
        HandRigidbody.maxDepenetrationVelocity = 5.0f;
        HandRigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        HandRigidbody.interpolation = RigidbodyInterpolation.Interpolate;

        velocityHistory = new Vector3[velocityFrameWindow];
        angularVelocityHistory = new Vector3[velocityFrameWindow];
        lastPosition = transform.position;
        lastRotation = transform.rotation;

        TargetPosition = transform.position;
        TargetRotation = transform.rotation;
    }

    void FixedUpdate()
    {
        ApplyHandPhysics();
        UpdateVelocityTracking();
        UpdateBlend();
    }

    void Update()
    {
        if (!IsHolding)
            UpdateNearestItem();
    }

    void LateUpdate()
    {
        // Keep held item rigidly locked at calibrated local position & rotation
        if (IsHolding && HeldItem != null && visualGripPoint != null)
        {
            HeldItem.transform.localPosition = heldLocalPos;
            HeldItem.transform.localRotation = heldLocalRot;
        }
    }

    #region Hand Physics Following (old: ApplyRigidBodyTransform)

    /// <summary>
    /// Move hand toward target by directly setting velocity each step.
    /// Unlike AddForce (which accumulates and oscillates), this converges cleanly.
    /// Still respects physics — walls block movement, objects get pushed.
    /// </summary>
    void ApplyHandPhysics()
    {
        float dt = Time.fixedDeltaTime;
        if (dt <= 0f) return;

        if (isKinematicallyDriven)
        {
            HandRigidbody.MovePosition(TargetPosition);
            HandRigidbody.MoveRotation(TargetRotation);
            return;
        }

        Vector3 posDelta = TargetPosition - transform.position;

        // Teleport safety net: ONLY snap if the camera/player actually teleported (velocity > 15 m/s)
        // Never snap into walls when simply walking against geometry!
        if (CameraVelocity.sqrMagnitude > 225f)
        {
            HandRigidbody.position = TargetPosition;
            HandRigidbody.rotation = TargetRotation;
            HandRigidbody.linearVelocity = CameraVelocity;
            HandRigidbody.angularVelocity = Vector3.zero;
            return;
        }

        // Clamp maximum chase speed to 10 m/s to prevent violent penetration against walls
        Vector3 desiredVel = CameraVelocity + (posDelta / dt);
        if (desiredVel.sqrMagnitude > 100f)
            desiredVel = desiredVel.normalized * 10f;

        HandRigidbody.linearVelocity = desiredVel;

        // Rotation: compute angular velocity from rotation delta
        Quaternion rotDelta = TargetRotation * Quaternion.Inverse(transform.rotation);
        rotDelta.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f) angle -= 360f;
        if (float.IsInfinity(axis.x) || float.IsNaN(axis.x))
        {
            HandRigidbody.angularVelocity = Vector3.zero;
            return;
        }
        HandRigidbody.angularVelocity = axis.normalized * (angle * Mathf.Deg2Rad / dt);
    }

    #endregion

    #region Grab / Release

    public bool AttemptGrab()
    {
        if (IsHolding) return false;

        var item = FindBestGrabbable();
        if (item == null) return false;

        return GrabItem(item);
    }

    public bool GrabItem(GrabbableItem item)
    {
        if (item == null || !item.canBePickedUp) return false;

        // If another hand is holding it, force drop
        if (item.IsHeld && !item.allowMultipleHolders)
            item.heldByHand.ReleaseItem(false);

        if (IsHolding)
            ReleaseItem(false);

        HeldItem = item;
        item.OnGrabbed(this);
        ClearHighlight();

        // Save original layer and move to held items layer
        originalItemLayer = item.gameObject.layer;
        originalItemScale = item.transform.localScale;

        // Ignore collision between held item and player capsule
        IgnorePlayerCollision(item, true);

        // Auto-find visual hand bone (hand_l / hand_r) if not assigned
        if (visualGripPoint == null)
        {
            string handBoneName = isLeftHand ? "hand_l" : "hand_r";
            var handGo = GameObject.Find(handBoneName);
            if (handGo != null) visualGripPoint = handGo.transform;
        }

        // Snap item to visual grip point if available, else gripPoint
        Transform attachTarget = visualGripPoint != null ? visualGripPoint : gripPoint;
        item.GetHoldTransform(isLeftHand, out Vector3 localPos, out Quaternion localRot);
        item.transform.SetParent(attachTarget, false);
        item.transform.localPosition = localPos;
        item.transform.localRotation = localRot;

        heldLocalPos = localPos;
        heldLocalRot = localRot;

        // Ensure scale is not affected by parent bone lossyScale
        Vector3 parentLossy = attachTarget.lossyScale;
        if (parentLossy.x > 0.0001f && parentLossy.y > 0.0001f && parentLossy.z > 0.0001f)
        {
            item.transform.localScale = new Vector3(originalItemScale.x / parentLossy.x, originalItemScale.y / parentLossy.y, originalItemScale.z / parentLossy.z);
        }

        // When held in hand, disable collisions and make Rigidbody kinematic
        // so it stays 100% rigidly locked in the player's palm without floating in air or trailing
        if (item.rb != null)
        {
            item.rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            item.rb.isKinematic = true;
            item.rb.useGravity = false;
            item.rb.detectCollisions = false;
            item.rb.interpolation = RigidbodyInterpolation.None;
            item.rb.linearVelocity = Vector3.zero;
            item.rb.angularVelocity = Vector3.zero;
        }

        // Only create joint if not kinematic and no visual direct parent
        if (visualGripPoint == null && item.rb != null && !item.rb.isKinematic)
        {
            CreateGrabJoint(item);
        }

        // Start blend 0→1
        blendProgress = 0f;
        blendTarget = 1f;
        blendSpeed = 1f / Mathf.Max(blendDuration, 0.01f);

        OnItemGrabbed?.Invoke(item);

        var legacy = item.GetComponent<PlayerKB_GrabObject>();
        if (legacy != null)
        {
            if (isLeftHand) legacy.IsGrabbedLeft = true;
            else legacy.IsGrabbedRight = true;
        }

        return true;
    }

    public void ReleaseItem(bool applyThrow = true)
    {
        if (!IsHolding) return;

        var item = HeldItem;
        HeldItem = null;

        DestroyGrabJoint();

        item.transform.SetParent(null);
        item.transform.localScale = originalItemScale;

        // Restore item layer
        item.gameObject.layer = originalItemLayer;

        // Restore collision between item and player
        IgnorePlayerCollision(item, false);

        // Throw velocity
        Vector3 throwVel = Vector3.zero;
        Vector3 angularVel = Vector3.zero;
        if (applyThrow)
            GetAverageVelocity(out throwVel, out angularVel);

        if (item.rb != null)
            item.rb.detectCollisions = true;

        item.OnReleased(throwVel, angularVel);

        // Start blend out
        blendTarget = 0f;
        blendSpeed = 1f / releaseBlendDuration;

        // Clear freeze
        FrozenLocalPos = null;

        var legacy = item.GetComponent<PlayerKB_GrabObject>();
        if (legacy != null)
        {
            if (isLeftHand) legacy.IsGrabbedLeft = false;
            else legacy.IsGrabbedRight = false;
        }

        OnItemReleased?.Invoke(item);
    }

    #endregion

    #region Joint Management (old: RigidBodyBlend)

    void CreateGrabJoint(GrabbableItem item)
    {
        jointStartRotation = item.transform.localRotation;

        if (item.useFixedJoint)
        {
            fixedGrabJoint = gripPoint.gameObject.AddComponent<FixedJoint>();
            fixedGrabJoint.connectedBody = item.rb;
            fixedGrabJoint.breakForce = item.breakForce;
            fixedGrabJoint.breakTorque = item.breakForce;
            fixedGrabJoint.enablePreprocessing = false;
        }
        else
        {
            // Old OpenViva ConfigurableJoint setup
            grabJoint = gripPoint.gameObject.AddComponent<ConfigurableJoint>();
            grabJoint.connectedBody = item.rb;
            grabJoint.autoConfigureConnectedAnchor = false;
            grabJoint.anchor = Vector3.zero;
            grabJoint.connectedAnchor = Vector3.zero;

            // Linear: X locked, Y/Z limited (old setup)
            grabJoint.xMotion = ConfigurableJointMotion.Locked;
            grabJoint.yMotion = ConfigurableJointMotion.Limited;
            grabJoint.zMotion = ConfigurableJointMotion.Limited;

            var linearLimit = new SoftJointLimit { limit = 10f };
            grabJoint.linearLimit = linearLimit;

            // Spring drives (old: spring=100000, damper=10)
            var drive = new JointDrive
            {
                positionSpring = item.holdSpring,
                positionDamper = item.holdDamper,
                maximumForce = float.MaxValue
            };
            grabJoint.xDrive = drive;
            grabJoint.yDrive = drive;
            grabJoint.zDrive = drive;

            // Rotation via slerp (old: same spring/damper)
            grabJoint.rotationDriveMode = RotationDriveMode.Slerp;
            grabJoint.slerpDrive = drive;

            grabJoint.angularXMotion = ConfigurableJointMotion.Free;
            grabJoint.angularYMotion = ConfigurableJointMotion.Free;
            grabJoint.angularZMotion = ConfigurableJointMotion.Free;

            grabJoint.targetRotation = Quaternion.identity;

            grabJoint.breakForce = item.breakForce;
            grabJoint.breakTorque = item.breakForce;
            grabJoint.enablePreprocessing = false;
            grabJoint.massScale = 0.0001f; // starts at 0, blends up
        }
    }

    void DestroyGrabJoint()
    {
        if (grabJoint != null)
        {
            Destroy(grabJoint);
            grabJoint = null;
        }
        if (fixedGrabJoint != null)
        {
            Destroy(fixedGrabJoint);
            fixedGrabJoint = null;
        }
    }

    void OnJointBreak(float breakForce)
    {
        if (IsHolding)
            ReleaseItem(false);
    }

    #endregion

    #region Player Collision Ignore

    /// <summary>
    /// Finds the player capsule collider (cached) and ignores/restores
    /// collision with all colliders on the held item.
    /// Prevents the knife from bouncing off your own body when sprinting.
    /// </summary>
    void IgnorePlayerCollision(GrabbableItem item, bool ignore)
    {
        if (playerCollider == null)
            FindPlayerCollider();

        var cols = item.GetComponentsInChildren<Collider>();
        foreach (var col in cols)
        {
            if (col != null && col != handCollider)
            {
                if (playerCollider != null)
                    Physics.IgnoreCollision(playerCollider, col, ignore);
                if (handCollider != null)
                    Physics.IgnoreCollision(handCollider, col, ignore);
            }
        }

        if (ignore)
            heldItemColliders = cols;
        else
            heldItemColliders = null;
    }

    void FindPlayerCollider()
    {
        var player = Object.FindFirstObjectByType<Player>();
        if (player != null)
        {
            playerCollider = player.GetComponentInChildren<CharacterController>();
            if (playerCollider == null)
                playerCollider = player.GetComponentInChildren<Collider>();
        }
        if (playerCollider == null)
        {
            playerCollider = Object.FindFirstObjectByType<CharacterController>();
        }
    }

    #endregion

    #region Blend (old: blendProgress 0→1 over 0.6s, massScale ramps)

    void UpdateBlend()
    {
        if (Mathf.Approximately(blendProgress, blendTarget)) return;

        blendProgress = Mathf.MoveTowards(blendProgress, blendTarget, blendSpeed * Time.fixedDeltaTime);

        // Ramp massScale with blend (old: 1.0 + blendValue * additionalConnectedMassScale)
        if (grabJoint != null && HeldItem != null)
        {
            grabJoint.massScale = 1f + blendProgress * HeldItem.heldMassScale;
        }
    }

    #endregion

    #region Detection

    public GrabbableItem FindBestGrabbable()
    {
        Transform detectPoint = visualGripPoint != null ? visualGripPoint : gripPoint;

        // Raycast from hand
        if (Physics.Raycast(detectPoint.position, -detectPoint.forward + detectPoint.up, out RaycastHit hit, grabRayDistance, grabLayerMask))
        {
            var item = GetOrAddGrabbable(hit.collider.gameObject);
            if (item != null && item.canBePickedUp && !item.IsHeld && HasLineOfSightTo(item))
                return item;
        }

        // Overlap sphere fallback
        var colliders = Physics.OverlapSphere(detectPoint.position, grabRadius, grabLayerMask);
        GrabbableItem closest = null;
        float closestDist = float.MaxValue;

        foreach (var col in colliders)
        {
            var item = GetOrAddGrabbable(col.gameObject);
            if (item == null || !item.canBePickedUp || item.IsHeld) continue;

            Vector3 closestPt;
            if (col is BoxCollider || col is SphereCollider || col is CapsuleCollider)
                closestPt = col.ClosestPoint(detectPoint.position);
            else if (col is MeshCollider mc && mc.convex)
                closestPt = mc.ClosestPoint(detectPoint.position);
            else
                closestPt = col.bounds.ClosestPoint(detectPoint.position);

            float dist = Vector3.Distance(detectPoint.position, closestPt);
            if (dist < closestDist)
            {
                if (HasLineOfSightTo(item))
                {
                    closestDist = dist;
                    closest = item;
                }
            }
        }

        return closest;
    }

    /// <summary>
    /// Verifies line of sight between the hand's grip point and the item surface.
    /// </summary>
    public bool HasLineOfSightTo(GrabbableItem item)
    {
        Transform detectPoint = visualGripPoint != null ? visualGripPoint : gripPoint;
        if (item == null || detectPoint == null) return false;
        Vector3 handPos = detectPoint.position;
        Vector3 targetPoint = item.transform.position;
        var col = item.GetComponentInChildren<Collider>();
        if (col != null && !col.isTrigger)
        {
            if (col is BoxCollider || col is SphereCollider || col is CapsuleCollider)
                targetPoint = col.ClosestPoint(handPos);
            else if (col is MeshCollider mc && mc.convex)
                targetPoint = mc.ClosestPoint(handPos);
            else
                targetPoint = col.bounds.ClosestPoint(handPos);
        }

        Vector3 toTarget = targetPoint - handPos;
        float dist = toTarget.magnitude;
        if (dist <= 0.05f) return true;

        Vector3 dir = toTarget / dist;
        float rayDist = Mathf.Max(0.01f, dist - 0.02f);
        RaycastHit[] hits = Physics.RaycastAll(handPos, dir, rayDist, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits.Length; i++)
        {
            var hitCol = hits[i].collider;
            if (hitCol == null) continue;
            if (hitCol.transform.IsChildOf(item.transform) || hitCol.gameObject == item.gameObject) continue;
            if (hitCol.CompareTag("Player") || hitCol.GetComponentInParent<CharacterController>() != null) continue;
            if (hitCol.name.StartsWith("hand_") || hitCol.name.StartsWith("PhysicsHand_") || hitCol.name.StartsWith("wrist_")) continue;
            if (hitCol.transform.IsChildOf(transform)) continue;

            return false;
        }
        return true;
    }

    public static GrabbableItem GetOrAddGrabbable(GameObject target)
    {
        if (target == null) return null;

        // Never make player body parts, physics hands, or UI elements grabbable
        string targetName = target.name.ToLower();
        if (target.CompareTag("Player") ||
            targetName.StartsWith("hand_") ||
            targetName.StartsWith("wrist_") ||
            targetName.StartsWith("physicshand_") ||
            targetName.Contains("playerarmature") ||
            target.GetComponentInParent<CharacterController>() != null)
        {
            return null;
        }

        // Reject interactables (doors, candles, faucets) and environmental/furniture objects
        if (target.GetComponentInParent<IInteractable>() != null ||
            target.GetComponentInParent<InteractDoorKB>() != null ||
            target.GetComponentInParent<InteractFaucet>() != null)
        {
            return null;
        }

        if (targetName.Contains("door") || targetName.Contains("handle") || targetName.Contains("table") ||
            targetName.Contains("desk") || targetName.Contains("chair") || targetName.Contains("candle") ||
            targetName.Contains("wardrobe") || targetName.Contains("cabinet") || targetName.Contains("bed") ||
            targetName.Contains("wall") || targetName.Contains("floor") || targetName.Contains("roof") ||
            targetName.Contains("ceiling") || targetName.Contains("stair") || targetName.Contains("window") ||
            targetName.Contains("faucet") || targetName.Contains("sink"))
        {
            return null;
        }

        var item = target.GetComponentInParent<GrabbableItem>();
        if (item != null) return item;

        var legacy = target.GetComponentInParent<PlayerKB_GrabObject>();
        if (legacy == null)
        {
            // Only items explicitly designated with GrabbableItem or PlayerKB_GrabObject can be grabbed!
            return null;
        }

        GameObject root = legacy.gameObject;
        if (root.CompareTag("Player") || root.GetComponent<CharacterController>() != null)
            return null;

        item = root.GetComponent<GrabbableItem>();
        if (item != null) return item;

        item = root.AddComponent<GrabbableItem>();
        item.rb = root.GetComponent<Rigidbody>();
        if (item.rb == null) item.rb = root.AddComponent<Rigidbody>();

        string nameLower = root.name.ToLower();
        if (nameLower.Contains("duck")) item.holdPose = GrabbableItem.HoldPose.RubberDucky;
        else if (nameLower.Contains("bag")) item.holdPose = GrabbableItem.HoldPose.Bag;
        else if (nameLower.Contains("flash")) item.holdPose = GrabbableItem.HoldPose.Flashlight;
        else if (nameLower.Contains("peach")) item.holdPose = GrabbableItem.HoldPose.Peach;
        else if (nameLower.Contains("straw")) item.holdPose = GrabbableItem.HoldPose.Strawberry;
        else if (nameLower.Contains("knife")) item.holdPose = GrabbableItem.HoldPose.Knife;
        else if (nameLower.Contains("bread") || nameLower.Contains("wheat")) item.holdPose = GrabbableItem.HoldPose.Wheat;
        else if (nameLower.Contains("egg")) item.holdPose = GrabbableItem.HoldPose.Egg;
        else if (nameLower.Contains("pot")) item.holdPose = GrabbableItem.HoldPose.Pot;
        else if (nameLower.Contains("soap")) item.holdPose = GrabbableItem.HoldPose.Soap;
        else if (nameLower.Contains("lantern")) item.holdPose = GrabbableItem.HoldPose.Lantern;
        else if (nameLower.Contains("jar") || nameLower.Contains("flour")) item.holdPose = GrabbableItem.HoldPose.FlourJar;
        else if (nameLower.Contains("bowl")) item.holdPose = GrabbableItem.HoldPose.MixingBowl;
        else if (nameLower.Contains("spoon")) item.holdPose = GrabbableItem.HoldPose.MixingSpoon;
        else if (nameLower.Contains("mortar")) item.holdPose = GrabbableItem.HoldPose.Mortar;
        else if (nameLower.Contains("pestle")) item.holdPose = GrabbableItem.HoldPose.Pestle;
        else if (nameLower.Contains("towel")) item.holdPose = GrabbableItem.HoldPose.Towel;
        else if (nameLower.Contains("milk") || nameLower.Contains("canister")) item.holdPose = GrabbableItem.HoldPose.MilkCanister;
        else if (nameLower.Contains("melon") || nameLower.Contains("cantaloupe")) item.holdPose = GrabbableItem.HoldPose.Cantaloupe;
        else if (nameLower.Contains("berry") || nameLower.Contains("blueberry")) item.holdPose = GrabbableItem.HoldPose.Blueberry;
        else item.holdPose = GrabbableItem.HoldPose.Default;

        return item;
    }

    void UpdateNearestItem()
    {
        var best = FindBestGrabbable();
        if (best != nearestItem)
        {
            if (nearestItem != null)
                nearestItem.SetHighlight(false);
            nearestItem = best;
            if (nearestItem != null)
                nearestItem.SetHighlight(true);
        }
    }

    void ClearHighlight()
    {
        if (nearestItem != null)
        {
            nearestItem.SetHighlight(false);
            nearestItem = null;
        }
    }

    public GrabbableItem GetNearestItem() => nearestItem;

    #endregion

    #region Velocity Tracking

    void UpdateVelocityTracking()
    {
        float dt = Time.fixedDeltaTime;
        if (dt <= 0f) return;

        Transform trackTransform = visualGripPoint != null ? visualGripPoint : gripPoint;
        velocityHistory[velocityIndex] = (trackTransform.position - lastPosition) / dt;

        Quaternion deltaRot = trackTransform.rotation * Quaternion.Inverse(lastRotation);
        deltaRot.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f) angle -= 360f;
        angularVelocityHistory[velocityIndex] = axis * (angle * Mathf.Deg2Rad / dt);

        lastPosition = trackTransform.position;
        lastRotation = trackTransform.rotation;
        velocityIndex = (velocityIndex + 1) % velocityFrameWindow;
    }

    void GetAverageVelocity(out Vector3 velocity, out Vector3 angularVelocity)
    {
        velocity = Vector3.zero;
        angularVelocity = Vector3.zero;

        for (int i = 0; i < velocityFrameWindow; i++)
        {
            velocity += velocityHistory[i];
            angularVelocity += angularVelocityHistory[i];
        }

        velocity /= velocityFrameWindow;
        angularVelocity /= velocityFrameWindow;
    }

    #endregion

    void OnDrawGizmosSelected()
    {
        var gp = visualGripPoint != null ? visualGripPoint : (gripPoint != null ? gripPoint : transform);
        Gizmos.color = IsHolding ? Color.green : Color.yellow;
        Gizmos.DrawWireSphere(gp.position, grabRadius);
        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(gp.position, (-gp.forward + gp.up).normalized * grabRayDistance);
    }
}
