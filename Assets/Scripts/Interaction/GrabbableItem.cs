using UnityEngine;

/// <summary>
/// Attach to any object that can be picked up. Replaces the old per-item PlayerKB_GrabObject.
/// Defines how an item behaves when grabbed: hold pose, physics properties, throw behavior.
/// </summary>
public class GrabbableItem : MonoBehaviour
{
    public enum HoldPose
    {
        Default,    // generic grip
        Bag,
        RubberDucky,
        Peach,
        Strawberry,
        Cantaloupe,
        Blueberry,
        Wheat,
        Flashlight,
        Egg,
        FlourJar,
        Knife,
        Lantern,
        MilkCanister,
        MixingBowl,
        MixingSpoon,
        Mortar,
        Pestle,
        Pot,
        Soap,
        Towel,
        Bread
    }

    [Header("Item Identity")]
    [Tooltip("Which hand animation pose to use when held")]
    public HoldPose holdPose = HoldPose.Default;
    [Tooltip("Is this item held with a delicate pinch between thumb and index, rather than a closed fist?")]
    public bool isPinchPose;

    public bool IsPinchItem => isPinchPose ||
                               holdPose == HoldPose.Peach ||
                               holdPose == HoldPose.Strawberry ||
                               holdPose == HoldPose.Blueberry ||
                               holdPose == HoldPose.Egg ||
                               holdPose == HoldPose.Soap ||
                               holdPose == HoldPose.Towel;

    [Header("Hold Offsets (Left Hand)")]
    public Vector3 holdPositionLeft = Vector3.zero;
    public Vector3 holdRotationLeft = Vector3.zero;

    [Header("Hold Offsets (Right Hand)")]
    public Vector3 holdPositionRight = Vector3.zero;
    public Vector3 holdRotationRight = Vector3.zero;

    [Header("Physics (old OpenViva values)")]
    [Tooltip("Mass scale when held — ramps from 0 to this during grab blend (old: 10)")]
    public float heldMassScale = 10f;
    [Tooltip("Spring force of the ConfigurableJoint holding this item (old: 100000)")]
    public float holdSpring = 100000f;
    [Tooltip("Damping of the ConfigurableJoint (old: 10)")]
    public float holdDamper = 10f;
    [Tooltip("Maximum force before the joint breaks and item drops")]
    public float breakForce = 5000f;
    [Tooltip("Use a fixed joint instead of a spring joint (rigid connection)")]
    public bool useFixedJoint;
    [Tooltip("Can multiple characters or hands hold this item at once (two-handed grip)")]
    public bool allowMultipleHolders = true;

    [Header("Grab Settings")]
    [Tooltip("Toggle holding mode — click to grab, click again to drop (desktop)")]
    public bool toggleHolding = true;
    [Tooltip("Time (seconds) to blend the grab joint to full strength (old: 0.6)")]
    public float grabBlendDuration = 0.6f;
    [Tooltip("Item can be picked up")]
    public bool canBePickedUp = true;

    [Header("Throw")]
    [Tooltip("Multiplier for throw velocity")]
    public float throwForceMultiplier = 1.2f;
    [Tooltip("Maximum throw velocity magnitude")]
    public float maxThrowSpeed = 15f;

    // Runtime state
    [HideInInspector] public Rigidbody rb;
    [HideInInspector] public HandGrabSystem heldByHand;
    [HideInInspector] public HandGrabSystem secondaryHand;

    public HandGrabSystem primaryHand => heldByHand;
    public bool IsHeld => heldByHand != null || secondaryHand != null;
    public bool IsTwoHandHeld => heldByHand != null && secondaryHand != null;
    public bool IsHeldBy(HandGrabSystem hand) => (hand != null) && (heldByHand == hand || secondaryHand == hand);

    public bool CanBeGrabbedBy(HandGrabSystem hand)
    {
        if (!canBePickedUp || hand == null) return false;
        if (IsHeldBy(hand)) return false;
        if (IsTwoHandHeld) return false;
        return true;
    }

    [HideInInspector] public Vector3 initialScale = Vector3.one;
    private bool hasInitializedScale = false;

    int originalLayer;
    CollisionDetectionMode originalCollisionMode;
    Outline outline;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null)
            rb = gameObject.AddComponent<Rigidbody>();
        outline = GetComponent<Outline>();
        InitInitialScale();
    }

    public void InitInitialScale()
    {
        if (!hasInitializedScale && transform != null)
        {
            initialScale = transform.localScale;
            hasInitializedScale = true;
        }
    }

    /// <summary>
    /// Called when a hand grabs this item.
    /// Supports two-handed holding when secondaryHand attaches.
    /// </summary>
    public void OnGrabbed(HandGrabSystem hand)
    {
        InitInitialScale();

        if (heldByHand == null)
        {
            heldByHand = hand;
            originalLayer = gameObject.layer;
            originalCollisionMode = rb != null ? rb.collisionDetectionMode : CollisionDetectionMode.ContinuousDynamic;

            // Immediately make kinematic and zero velocities BEFORE any transform move/reparenting
            // to prevent PhysX penetration impulses or velocity spikes
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.detectCollisions = false;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                rb.interpolation = RigidbodyInterpolation.None;
            }

            if (outline != null)
                outline.enabled = false;
        }
        else if (secondaryHand == null && hand != heldByHand)
        {
            secondaryHand = hand;
            if (outline != null)
                outline.enabled = false;
        }
    }

    /// <summary>
    /// Called when a specific hand releases this item.
    /// Returns true if the item was completely released by all hands and should restore physics.
    /// </summary>
    public bool OnReleasedBy(HandGrabSystem hand, Vector3 throwVelocity, Vector3 angularVelocity)
    {
        if (hand == secondaryHand)
        {
            secondaryHand = null;
            return false; // Still held by primary hand
        }
        else if (hand == heldByHand)
        {
            if (secondaryHand != null)
            {
                // Promote secondary hand to primary hand
                heldByHand = secondaryHand;
                secondaryHand = null;
                return false; // Still held by promoted hand
            }
            else
            {
                heldByHand = null;
                return true; // Completely released
            }
        }
        return false;
    }

    /// <summary>
    /// Called when the hand releases this item. Restores gravity and applies throw velocity.
    /// </summary>
    public void OnReleased(Vector3 throwVelocity, Vector3 angularVelocity)
    {
        heldByHand = null;
        secondaryHand = null;
        gameObject.layer = originalLayer;

        if (rb != null)
        {
            // Dropped items must ALWAYS react to gravity and fall cleanly
            rb.isKinematic = false;
            rb.useGravity = true;
            // Clear position-freezing constraints (e.g. FreezePositionY) from scene placement
            rb.constraints = RigidbodyConstraints.None;
            rb.detectCollisions = true;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            // Sanitize throw velocities to prevent NaN / Infinity yeets
            Vector3 safeVel = throwVelocity * throwForceMultiplier;
            if (float.IsNaN(safeVel.x) || float.IsInfinity(safeVel.x) ||
                float.IsNaN(safeVel.y) || float.IsInfinity(safeVel.y) ||
                float.IsNaN(safeVel.z) || float.IsInfinity(safeVel.z))
            {
                safeVel = Vector3.zero;
            }
            if (safeVel.magnitude > maxThrowSpeed)
                safeVel = safeVel.normalized * maxThrowSpeed;

            Vector3 safeAngVel = angularVelocity;
            if (float.IsNaN(safeAngVel.x) || float.IsInfinity(safeAngVel.x) ||
                float.IsNaN(safeAngVel.y) || float.IsInfinity(safeAngVel.y) ||
                float.IsNaN(safeAngVel.z) || float.IsInfinity(safeAngVel.z))
            {
                safeAngVel = Vector3.zero;
            }
            if (safeAngVel.magnitude > 25f)
                safeAngVel = safeAngVel.normalized * 25f;

            rb.linearVelocity = safeVel;
            rb.angularVelocity = safeAngVel;
        }
    }

    /// <summary>
    /// Get physical distance from a point to the closest point on this item's colliders.
    /// </summary>
    public float GetDistanceTo(Vector3 point)
    {
        float minDist = float.MaxValue;
        var colliders = GetComponentsInChildren<Collider>();
        if (colliders != null && colliders.Length > 0)
        {
            for (int i = 0; i < colliders.Length; i++)
            {
                var col = colliders[i];
                if (col == null || !col.enabled || col.isTrigger) continue;
                Vector3 closestPt;
                if (col is BoxCollider || col is SphereCollider || col is CapsuleCollider)
                    closestPt = col.ClosestPoint(point);
                else if (col is MeshCollider mc && mc.convex)
                    closestPt = mc.ClosestPoint(point);
                else
                    closestPt = col.bounds.ClosestPoint(point);

                float d = Vector3.Distance(point, closestPt);
                if (d < minDist) minDist = d;
            }
        }
        if (minDist == float.MaxValue)
        {
            minDist = Vector3.Distance(point, transform.position);
        }
        return minDist;
    }

    /// <summary>
    /// Show/hide the outline highlight.
    /// </summary>
    public void SetHighlight(bool show)
    {
        if (outline != null)
            outline.enabled = show;
    }

    /// <summary>
    /// Returns the hold position for the given hand side.
    /// Uses inspector-configured offsets if non-zero; otherwise falls back to ObjectHoldPositions calibrated values.
    /// </summary>
    public void GetHoldTransform(bool isLeftHand, out Vector3 pos, out Quaternion rot)
    {
        if (isLeftHand)
        {
            if (holdPositionLeft != Vector3.zero || holdRotationLeft != Vector3.zero)
            {
                pos = holdPositionLeft;
                rot = Quaternion.Euler(holdRotationLeft);
                return;
            }
        }
        else
        {
            if (holdPositionRight != Vector3.zero || holdRotationRight != Vector3.zero)
            {
                pos = holdPositionRight;
                rot = Quaternion.Euler(holdRotationRight);
                return;
            }
        }

        int index = HoldPoseToIndex(holdPose);
        if (index >= 0 && ObjectHoldPositions.Instance != null)
        {
            pos = isLeftHand ? ObjectHoldPositions.Instance.GetObjectPositionLeft(index) : ObjectHoldPositions.Instance.GetObjectPositionRight(index);
            rot = isLeftHand ? ObjectHoldPositions.Instance.GetObjectRotationLeft(index) : ObjectHoldPositions.Instance.GetObjectRotationRight(index);
            return;
        }

        pos = isLeftHand ? holdPositionLeft : holdPositionRight;
        rot = isLeftHand ? Quaternion.Euler(holdRotationLeft) : Quaternion.Euler(holdRotationRight);
    }

    public static int HoldPoseToIndex(HoldPose pose)
    {
        return pose switch
        {
            HoldPose.Bag => 0,
            HoldPose.RubberDucky => 1,
            HoldPose.Peach => 2,
            HoldPose.Strawberry => 3,
            HoldPose.Cantaloupe => 4,
            HoldPose.Blueberry => 5,
            HoldPose.Wheat => 6,
            HoldPose.Flashlight => 7,
            HoldPose.Egg => 8,
            HoldPose.FlourJar => 9,
            HoldPose.Knife => 10,
            HoldPose.Lantern => 11,
            HoldPose.MilkCanister => 12,
            HoldPose.MixingBowl => 13,
            HoldPose.MixingSpoon => 14,
            HoldPose.Mortar => 15,
            HoldPose.Pestle => 16,
            HoldPose.Pot => 17,
            HoldPose.Soap => 18,
            HoldPose.Towel => 19,
            HoldPose.Bread => 20,
            _ => -1
        };
    }
}
