using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Universal physics-based door handle for Desktop (KB/M) and VR.
/// Allows the player to grab/hold the handle and physically swing the door open or closed
/// along its HingeJoint by moving around (WASD), looking (mouse), or moving VR controllers.
/// Also supports quick tap/click to smoothly toggle the door open or closed.
/// </summary>
public class PhysicsDoorHandle : MonoBehaviour, IInteractable
{
    [Header("Door Components")]
    [Tooltip("The door's Rigidbody that swings")]
    public Rigidbody doorRigidbody;

    [Tooltip("The ConfigurableJoint on the door providing 1-DOF hinge motion with native PhysX projection")]
    public ConfigurableJoint doorConfigJoint;

    [Tooltip("Optional HingeJoint on the door (if not using ConfigurableJoint)")]
    public HingeJoint doorHinge;

    private Quaternion initialDoorRotation;

    [Tooltip("Handle grip transform where the hand latches")]
    public Transform gripPoint;

    [Header("Visual Highlighting")]
    public Outline outline;

    [Header("Audio")]
    public AudioSource audioSource;
    public AudioClip[] openSounds;
    public AudioClip[] closeSounds;

    [Header("Door Mode & Behavior")]
    [Tooltip("If true, tapping E or quick click toggles the door open/closed with impulse. If false, door ONLY moves while physically held.")]
    public bool allowQuickClickToggle = true;

    [Tooltip("If true, the door stays open in place when released instead of coasting or drifting.")]
    public bool stayOpenOnRelease = true;

    [Tooltip("Angular damping applied while held (lower = responsive, higher = heavy)")]
    public float heldAngularDamping = 1.0f;

    [Tooltip("Angular damping applied when released if stayOpenOnRelease is true (stops it dead in place)")]
    public float releasedAngularDamping = 20.0f;

    [Header("PhysX Joint Hold Settings")]
    [Tooltip("Spring strength pulling the door toward the hand (N/m)")]
    public float jointSpring = 2400f;

    [Tooltip("Spring damper reducing oscillations")]
    public float jointDamper = 80f;

    [Tooltip("Maximum linear force the hand can exert on the door before stretching/slipping (prevents ripping door off hinges)")]
    public float maxJointForce = 1200f;

    [Tooltip("Distance the hand can pull away from the handle before the grip slips and releases (meters)")]
    public float slipDistance = 1.2f;

    [Header("Handle Visual Latch Animation")]
    [Tooltip("Whether the handle itself lifts/pivots when held")]
    public bool liftHandleOnHold = true;

    [Tooltip("Optional child or specific transform to rotate (defaults to this handle transform if null)")]
    public Transform handleVisualTransform;

    [Tooltip("Local axis of rotation for the latch lift (Z axis = Vector3.forward per Blender coordinate export)")]
    public Vector3 handleRotationAxis = Vector3.forward;

    [Tooltip("Target angle for handle when grabbed (e.g. 90 deg lifted latch)")]
    public float handleGrabbedAngle = 90f;

    [Tooltip("Target angle for handle when released (resting latch)")]
    public float handleRestingAngle = 0f;

    [Tooltip("Speed at which the latch lifts when grabbed (deg/sec)")]
    public float handleLiftSpeed = 360f;

    [Tooltip("Speed at which the latch drops when released (deg/sec)")]
    public float handleDropSpeed = 450f;

    [Tooltip("Optional legacy HingeJoint reference on the handle (if any)")]
    public HingeJoint handleHinge;

    [Header("Latch Lock System")]
    [Tooltip("If true, when the door is closed (at 0 deg), the latch locks the door until the knob is twisted 45 deg.")]
    public bool requireKnobTwistToUnlock = false;

    [Tooltip("Local axis around which the knob turns when twisted (e.g. Vector3.right for X-axis)")]
    public Vector3 knobRotationAxis = Vector3.right;

    [Tooltip("Wrist twist angle required in VR mode to unlock the latch (degrees)")]
    public float vrUnlockTwistAngle = 40f;

    [Tooltip("Sound played when the latch clicks open")]
    public AudioClip latchUnlockSound;

    [Tooltip("Sound played when the latch clicks shut")]
    public AudioClip latchLockSound;

    [Header("Occlusion & Accessibility")]
    [Tooltip("If true, requires line of sight to the handle (door slab blocks interaction from behind)")]
    public bool checkOcclusion = true;

    [Header("Tracking & Physics")]
    [Tooltip("Inverts the tracking pull direction (useful for mirrored/opposite hinge doors)")]
    public bool invertTracking = false;

    [Tooltip("Spring stiffness tracking hand movement (higher = tighter following)")]
    public float trackingStiffness = 8.0f;

    [Tooltip("Max reach distance before handle slips from hand")]
    public float maxReachDistance = 2.4f;

    [Tooltip("Impulse speed applied on quick click toggle (rad/s)")]
    public float toggleImpulseSpeed = 3.0f;

    // Interaction state
    private bool isHeld = false;
    private HandGrabSystem holdingHand = null;
    private bool isVR = false;
    private float holdDuration = 0f;
    private ConfigurableJoint activeJoint = null;

    // Sound tracking
    private bool wasClosed = true;
    private float soundCooldown = 0f;

    // Handle latch animation state
    private Quaternion initialHandleLocalRotation;
    private float currentHandleAngle = 0f;

    // Latch lock & knob twist state
    private bool isDoorLatched = false;
    private bool isKnobTwisted = false;
    private Quaternion initialWristRotation;
    private Transform activeKnobTransform;
    private Quaternion initialActiveKnobLocalRotation;
    private float currentKnobVisualAngle = 0f;

    // Collisions ignored between hand and door while holding
    private readonly List<(Collider handCol, Collider doorCol)> ignoredColliders = new List<(Collider handCol, Collider doorCol)>();

    public bool IsHeld => isHeld;
    public HandGrabSystem HoldingHand => holdingHand;
    public bool CanInteract => true;

    private void Awake()
    {
        InitializeHandle();
    }

    public void InitializeHandle()
    {
        // 1. Resolve Door Rigidbody (check parent first, then this)
        if (doorRigidbody == null)
        {
            if (transform.parent != null)
                doorRigidbody = transform.parent.GetComponent<Rigidbody>();
            if (doorRigidbody == null)
                doorRigidbody = GetComponentInParent<Rigidbody>();
        }

        if (doorRigidbody != null)
        {
            initialDoorRotation = doorRigidbody.transform.localRotation;
            doorRigidbody.useGravity = false;
        }

        // 2. Resolve Joint on door
        if (doorConfigJoint == null && doorHinge == null && doorRigidbody != null)
        {
            doorConfigJoint = doorRigidbody.GetComponent<ConfigurableJoint>();
            if (doorConfigJoint == null)
            {
                doorHinge = doorRigidbody.GetComponent<HingeJoint>();
            }
        }

        // Configure PhysX projection on ConfigurableJoint to prevent door ripping off its frame
        if (doorConfigJoint != null)
        {
            doorConfigJoint.projectionMode = JointProjectionMode.PositionAndRotation;
            doorConfigJoint.projectionDistance = 0.01f;
            doorConfigJoint.projectionAngle = 3.0f;
        }

        // 2b. Initialize Handle Latch visual transform and state
        Transform targetHandleTransform = handleVisualTransform != null ? handleVisualTransform : transform;
        initialHandleLocalRotation = targetHandleTransform.localRotation;
        currentHandleAngle = handleRestingAngle;

        var handleRb = GetComponent<Rigidbody>();
        if (handleRb != null)
        {
            handleRb.isKinematic = true;
        }

        if (handleHinge == null)
        {
            var hj = GetComponent<HingeJoint>();
            if (hj != null)
            {
                handleHinge = hj;
            }
        }

        if (handleHinge != null)
        {
            handleHinge.connectedBody = null;
        }

        // 2c. Initialize Latch Lock State
        if (requireKnobTwistToUnlock)
        {
            bool closed = Mathf.Abs(GetDoorAngle()) < 2.5f;
            isDoorLatched = closed;
            LockDoorJoint(closed);
        }

        // 3. Resolve GripPoint (at collider center if offset)
        if (gripPoint == null)
        {
            var col = GetComponent<Collider>();
            if (col != null)
            {
                Vector3 localCenter = Vector3.zero;
                if (col is CapsuleCollider cc) localCenter = cc.center;
                else if (col is BoxCollider bc) localCenter = bc.center;
                else if (col is SphereCollider sc) localCenter = sc.center;

                if (localCenter.sqrMagnitude > 0.001f)
                {
                    var existing = transform.Find("GripPoint");
                    if (existing != null)
                    {
                        gripPoint = existing;
                    }
                    else
                    {
                        var gp = new GameObject("GripPoint");
                        gp.transform.SetParent(transform, false);
                        gp.transform.localPosition = localCenter;
                        gripPoint = gp.transform;
                    }
                }
                else
                {
                    gripPoint = transform;
                }
            }
            else
            {
                gripPoint = transform;
            }
        }

        Transform enterKnobInit = transform.Find("Bathroom_DoorKnobEnter");
        Transform exitKnobInit = transform.Find("Bathroom_DoorKnobExit");
        if (enterKnobInit != null) EnsureKnobGripPoint(enterKnobInit);
        if (exitKnobInit != null) EnsureKnobGripPoint(exitKnobInit);

        // 4. Resolve Outline
        if (outline == null)
            outline = GetComponent<Outline>() ?? GetComponentInParent<Outline>();

        if (outline != null)
        {
            outline.OutlineMode = Outline.Mode.OutlineAll;
            outline.OutlineColor = new Color(1f, 0.85f, 0.2f, 1f); // Warm gold outline
            outline.OutlineWidth = 3.5f;
            outline.enabled = false;
        }

        // 5. Resolve AudioSource
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>() ?? GetComponentInParent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.spatialBlend = 1f; // 3D sound
                audioSource.minDistance = 1f;
                audioSource.maxDistance = 12f;
                audioSource.playOnAwake = false;
            }
        }

        if (doorConfigJoint != null || doorHinge != null)
        {
            wasClosed = Mathf.Abs(GetDoorAngle()) < 2.5f;
        }

        // 6. Ignore collision with frame colliders to prevent sticking/snagging
        if (doorRigidbody != null)
        {
            var doorCols = doorRigidbody.GetComponentsInChildren<Collider>();
            if (doorRigidbody.transform.parent != null)
            {
                var parentCols = doorRigidbody.transform.parent.GetComponentsInChildren<Collider>();
                foreach (var pc in parentCols)
                {
                    if (pc.transform.IsChildOf(doorRigidbody.transform)) continue;
                    foreach (var dc in doorCols)
                    {
                        if (pc != null && dc != null && pc != dc)
                            Physics.IgnoreCollision(pc, dc, true);
                    }
                }
            }

            var doorway = GameObject.Find("Bathroom_Doorway");
            if (doorway != null && (doorRigidbody.name.Contains("Bathroom") || transform.name.Contains("Bathroom")))
            {
                foreach (var dwCol in doorway.GetComponentsInChildren<Collider>())
                {
                    foreach (var dc in doorCols)
                    {
                        if (dwCol != null && dc != null)
                            Physics.IgnoreCollision(dwCol, dc, true);
                    }
                }
            }
        }
    }

    private void Update()
    {
        if (soundCooldown > 0f)
            soundCooldown -= Time.deltaTime;

        UpdateSoundDetector();
        UpdateHandleLatchAnimation();
        UpdateKnobLatchAnimation();

        if (isHeld && holdingHand != null)
        {
            holdDuration += Time.deltaTime;

            Transform activeGrip = ResolveBestGrip(holdingHand);
            Vector3 gripPos = GetGripPosition(activeGrip);
            Vector3 handPos = holdingHand.transform.position;

            // In VR, ensure kinematic controller rigidbody tracks with the controller transform
            if (isVR && holdingHand.HandRigidbody != null && holdingHand.HandRigidbody.isKinematic)
            {
                holdingHand.HandRigidbody.position = handPos;
                holdingHand.HandRigidbody.rotation = holdingHand.transform.rotation;
            }

            // In VR, check wrist twist to unlock latched door
            if (requireKnobTwistToUnlock && isVR && isDoorLatched && !isKnobTwisted)
            {
                float wristAngle = Quaternion.Angle(holdingHand.transform.rotation, initialWristRotation);
                Quaternion deltaRot = holdingHand.transform.rotation * Quaternion.Inverse(initialWristRotation);
                deltaRot.ToAngleAxis(out float twistAngle, out Vector3 twistAxis);
                if (twistAngle > 180f) twistAngle -= 360f;
                Vector3 spindleAxis = activeKnobTransform != null ? activeKnobTransform.TransformDirection(knobRotationAxis) : Vector3.right;
                float spindleTwist = Mathf.Abs(twistAngle * Vector3.Dot(twistAxis, spindleAxis));
                float effectiveTwist = Mathf.Max(spindleTwist, wristAngle * 0.85f);

                currentKnobVisualAngle = Mathf.Clamp(effectiveTwist, 0f, 45f);
                if (activeKnobTransform != null)
                {
                    activeKnobTransform.localRotation = initialActiveKnobLocalRotation * Quaternion.AngleAxis(currentKnobVisualAngle, knobRotationAxis);
                }

                if (effectiveTwist >= vrUnlockTwistAngle)
                {
                    isKnobTwisted = true;
                    isDoorLatched = false;
                    LockDoorJoint(false);
                    PlaySound(latchUnlockSound ?? (openSounds != null && openSounds.Length > 0 ? openSounds[0] : null));
                }
            }

            // Check if hand has pulled too far from handle (slip distance)
            float distFromHandle = Vector3.Distance(gripPos, handPos);
            if (distFromHandle > slipDistance)
            {
                EndHold(holdingHand);
                return;
            }

            // Check if hand has moved too far from the door hinge line
            Vector3 hingePos = transform.position;
            if (doorConfigJoint != null)
                hingePos = doorConfigJoint.transform.TransformPoint(doorConfigJoint.anchor);
            else if (doorHinge != null)
                hingePos = doorHinge.transform.TransformPoint(doorHinge.anchor);

            float distFromHinge = Vector3.Distance(handPos, hingePos);
            if (distFromHinge > maxReachDistance)
            {
                EndHold(holdingHand);
                return;
            }
        }
    }

    private void UpdateHandleLatchAnimation()
    {
        if (!liftHandleOnHold) return;

        Transform targetTransform = handleVisualTransform != null ? handleVisualTransform : transform;
        float targetAngle = isHeld ? handleGrabbedAngle : handleRestingAngle;
        float speed = isHeld ? handleLiftSpeed : handleDropSpeed;

        currentHandleAngle = Mathf.MoveTowards(currentHandleAngle, targetAngle, speed * Time.deltaTime);
        targetTransform.localRotation = initialHandleLocalRotation * Quaternion.AngleAxis(currentHandleAngle, handleRotationAxis);
    }

    private void UpdateKnobLatchAnimation()
    {
        if (!requireKnobTwistToUnlock) return;

        if (isHeld)
        {
            if (isKnobTwisted && currentKnobVisualAngle < 45f)
            {
                currentKnobVisualAngle = Mathf.MoveTowards(currentKnobVisualAngle, 45f, 360f * Time.deltaTime);
                if (activeKnobTransform != null)
                {
                    activeKnobTransform.localRotation = initialActiveKnobLocalRotation * Quaternion.AngleAxis(currentKnobVisualAngle, knobRotationAxis);
                }
            }
        }
        else
        {
            if (activeKnobTransform != null && currentKnobVisualAngle > 0.01f)
            {
                currentKnobVisualAngle = Mathf.MoveTowards(currentKnobVisualAngle, 0f, 450f * Time.deltaTime);
                activeKnobTransform.localRotation = initialActiveKnobLocalRotation * Quaternion.AngleAxis(currentKnobVisualAngle, knobRotationAxis);
                if (currentKnobVisualAngle <= 0.01f)
                {
                    activeKnobTransform.localRotation = initialActiveKnobLocalRotation;
                }
            }
        }
    }

    private void LockDoorJoint(bool locked)
    {
        if (doorConfigJoint != null)
        {
            doorConfigJoint.angularXMotion = locked ? ConfigurableJointMotion.Locked : ConfigurableJointMotion.Limited;
        }
    }

    private void PlaySound(AudioClip clip)
    {
        if (clip != null && audioSource != null)
        {
            audioSource.PlayOneShot(clip);
        }
    }

    private void OnJointBreak(float breakForce)
    {
        if (isHeld)
        {
            EndHold(holdingHand);
        }
    }

    #region Highlighting

    public void SetHighlight(bool highlighted)
    {
        if (outline != null)
        {
            outline.enabled = highlighted;
        }
    }

    #endregion

    #region IInteractable (E key / Quick Click)

    public void Interact(GameObject interactor)
    {
        if (allowQuickClickToggle)
        {
            NudgeToggle();
        }
    }

    #endregion

    #region Hold Interaction (LMB / RMB / VR Grip)

    /// <summary>
    /// Checks if the handle can be reached from originPos without being occluded by the door slab.
    /// </summary>
    public bool CanReachFrom(Vector3 originPos, float maxDistance = -1f)
    {
        Transform grip = ResolveBestGrip(originPos);
        if (grip == null) return false;

        if (maxDistance > 0f)
        {
            Vector3 gripPos = GetGripPosition(grip);
            if (Vector3.Distance(originPos, gripPos) > maxDistance) return false;
        }

        if (!checkOcclusion || doorRigidbody == null) return true;

        return IsKnobVisibleFrom(originPos, grip);
    }

    /// <summary>
    /// Ensures a child GripPoint transform exists at the collider center if offset.
    /// </summary>
    public static Transform EnsureKnobGripPoint(Transform knob)
    {
        if (knob == null) return null;
        var existing = knob.Find("GripPoint");
        if (existing != null) return existing;

        Vector3 localCenter = Vector3.zero;
        var col = knob.GetComponent<Collider>();
        if (col != null)
        {
            if (col is SphereCollider sc) localCenter = sc.center;
            else if (col is BoxCollider bc) localCenter = bc.center;
            else if (col is CapsuleCollider cc) localCenter = cc.center;
        }

        if (localCenter.sqrMagnitude > 0.0001f)
        {
            var gp = new GameObject("GripPoint");
            gp.transform.SetParent(knob, false);
            gp.transform.localPosition = localCenter;
            return gp.transform;
        }
        return knob;
    }

    /// <summary>
    /// Returns the world position of the knob or grip point, taking into account any collider bounds center.
    /// </summary>
    public static Vector3 GetKnobPosition(Transform knob)
    {
        if (knob == null) return Vector3.zero;
        var gp = knob.Find("GripPoint");
        if (gp != null) return gp.position;
        var col = knob.GetComponent<Collider>();
        if (col != null) return col.bounds.center;
        return knob.position;
    }

    /// <summary>
    /// Returns the resolved grip position for the given hand or reference position.
    /// </summary>
    public Vector3 GetGripPosition(Transform targetGrip = null)
    {
        if (targetGrip == null) targetGrip = ResolveBestGrip(holdingHand);
        if (targetGrip == null) targetGrip = gripPoint != null ? gripPoint : transform;
        return GetKnobPosition(targetGrip);
    }

    public Transform ResolveBestGrip(HandGrabSystem hand)
    {
        Camera cam = Camera.main;
        Vector3 refPos = (!isVR && cam != null) ? cam.transform.position : (hand != null ? hand.transform.position : transform.position);
        return ResolveBestGrip(refPos);
    }

    public Transform ResolveBestGrip(Vector3 refPos)
    {
        Transform enterKnob = transform.Find("Bathroom_DoorKnobEnter");
        Transform exitKnob = transform.Find("Bathroom_DoorKnobExit");

        if (enterKnob != null && exitKnob != null)
        {
            Transform enterGrip = EnsureKnobGripPoint(enterKnob);
            Transform exitGrip = EnsureKnobGripPoint(exitKnob);

            bool enterVisible = IsKnobVisibleFrom(refPos, enterGrip);
            bool exitVisible = IsKnobVisibleFrom(refPos, exitGrip);

            // If one knob is clearly visible and the other is occluded by the door slab, choose the visible one
            if (enterVisible && !exitVisible) return enterGrip;
            if (exitVisible && !enterVisible) return exitGrip;

            // Facing check: vector from knob grip center to player
            Vector3 enterGripPos = GetKnobPosition(enterGrip);
            Vector3 exitGripPos = GetKnobPosition(exitGrip);
            Vector3 toPlayerEnter = (refPos - enterGripPos).normalized;
            Vector3 toPlayerExit = (refPos - exitGripPos).normalized;
            float dotEnter = Vector3.Dot(enterKnob.forward, toPlayerEnter);
            float dotExit = Vector3.Dot(exitKnob.forward, toPlayerExit);

            if (dotEnter > 0.05f && dotExit <= 0.05f) return enterGrip;
            if (dotExit > 0.05f && dotEnter <= 0.05f) return exitGrip;

            float dEnter = Vector3.Distance(refPos, enterGripPos);
            float dExit = Vector3.Distance(refPos, exitGripPos);
            return (dEnter <= dExit) ? enterGrip : exitGrip;
        }

        return gripPoint != null ? gripPoint : transform;
    }

    private bool IsKnobVisibleFrom(Vector3 fromPos, Transform knob)
    {
        if (doorRigidbody == null || knob == null) return true;

        Vector3 knobPos = GetKnobPosition(knob);
        Vector3 dir = (knobPos - fromPos);
        float dist = dir.magnitude;
        if (dist < 0.05f) return true;

        if (Physics.Linecast(fromPos, knobPos, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider != null && hit.collider.transform != transform && !hit.collider.transform.IsChildOf(transform))
            {
                if (hit.collider.transform == doorRigidbody.transform || hit.collider.transform.IsChildOf(doorRigidbody.transform))
                {
                    return false;
                }
            }
        }
        return true;
    }

    private void SetHandDoorCollisionIgnored(HandGrabSystem hand, bool ignore)
    {
        if (doorRigidbody == null || hand == null) return;

        if (ignore)
        {
            var doorCols = doorRigidbody.GetComponentsInChildren<Collider>(true);
            var handCols = hand.GetComponentsInChildren<Collider>(true);
            var parentCols = hand.transform.parent != null ? hand.transform.parent.GetComponentsInChildren<Collider>(true) : null;

            void IgnorePairs(Collider[] colliders)
            {
                if (colliders == null) return;
                foreach (var hc in colliders)
                {
                    if (hc == null) continue;
                    foreach (var dc in doorCols)
                    {
                        if (dc != null && hc != dc && !Physics.GetIgnoreCollision(hc, dc))
                        {
                            Physics.IgnoreCollision(hc, dc, true);
                            ignoredColliders.Add((hc, dc));
                        }
                    }
                }
            }

            IgnorePairs(handCols);
            IgnorePairs(parentCols);
        }
        else
        {
            for (int i = 0; i < ignoredColliders.Count; i++)
            {
                var (hc, dc) = ignoredColliders[i];
                if (hc != null && dc != null)
                {
                    Physics.IgnoreCollision(hc, dc, false);
                }
            }
            ignoredColliders.Clear();
        }
    }

    public void BeginHold(HandGrabSystem hand, bool vrMode)
    {
        if (hand == null || hand.HandRigidbody == null || doorRigidbody == null) return;

        isHeld = true;
        holdingHand = hand;
        isVR = vrMode;
        holdDuration = 0f;

        Transform activeGrip = ResolveBestGrip(hand);
        Vector3 gripPos = GetGripPosition(activeGrip);

        // If activeGrip is the child GripPoint, the rotating visual knob is its parent
        activeKnobTransform = (activeGrip != null && activeGrip.name == "GripPoint" && activeGrip.parent != null)
            ? activeGrip.parent
            : activeGrip;
        if (activeKnobTransform != null)
        {
            initialActiveKnobLocalRotation = activeKnobTransform.localRotation;
        }
        currentKnobVisualAngle = 0f;

        if (requireKnobTwistToUnlock)
        {
            bool isClosed = Mathf.Abs(GetDoorAngle()) < 2.5f;
            if (!isClosed)
            {
                // Door already open - latch does not block swinging
                isDoorLatched = false;
                isKnobTwisted = true;
                LockDoorJoint(false);
            }
            else
            {
                if (!vrMode)
                {
                    // Desktop mode: automatic twist & unlock
                    isDoorLatched = false;
                    isKnobTwisted = true;
                    LockDoorJoint(false);
                    currentKnobVisualAngle = 45f;
                    PlaySound(latchUnlockSound ?? (openSounds != null && openSounds.Length > 0 ? openSounds[0] : null));
                }
                else
                {
                    // VR mode: requires wrist twist to unlock
                    isDoorLatched = true;
                    isKnobTwisted = false;
                    initialWristRotation = hand.transform.rotation;
                    LockDoorJoint(true);
                }
            }
        }

        // Ensure door is dynamic so it can swing freely
        doorRigidbody.isKinematic = false;
        doorRigidbody.angularDamping = heldAngularDamping;

        // Ensure hinge joint projection is active so door cannot separate from hinge axis
        if (doorConfigJoint != null)
        {
            doorConfigJoint.projectionMode = JointProjectionMode.PositionAndRotation;
            doorConfigJoint.projectionDistance = 0.01f;
            doorConfigJoint.projectionAngle = 3.0f;
        }

        // In VR, ensure the controller Rigidbody is kinematic so it follows OpenXR tracking with infinite authority
        if (isVR && hand.HandRigidbody != null)
        {
            hand.HandRigidbody.isKinematic = true;
        }

        // Clean up any existing joint
        if (activeJoint != null)
        {
            Destroy(activeJoint);
            activeJoint = null;
        }

        // Attach hand to door via ConfigurableJoint (pure PhysX joint)
        activeJoint = doorRigidbody.gameObject.AddComponent<ConfigurableJoint>();
        activeJoint.connectedBody = hand.HandRigidbody;
        activeJoint.autoConfigureConnectedAnchor = false;
        activeJoint.anchor = doorRigidbody.transform.InverseTransformPoint(gripPos);
        activeJoint.connectedAnchor = hand.HandRigidbody.transform.InverseTransformPoint(gripPos);

        // Linear spring drive: pulls door 1:1 along its hinge, but stretches and slips if player walks away
        activeJoint.xMotion = ConfigurableJointMotion.Free;
        activeJoint.yMotion = ConfigurableJointMotion.Free;
        activeJoint.zMotion = ConfigurableJointMotion.Free;

        var drive = new JointDrive
        {
            positionSpring = jointSpring,
            positionDamper = jointDamper,
            maximumForce = maxJointForce
        };
        activeJoint.xDrive = drive;
        activeJoint.yDrive = drive;
        activeJoint.zDrive = drive;
        activeJoint.targetPosition = Vector3.zero;

        // Free angular motion: hand rotation does not twist or torque the door slab
        activeJoint.angularXMotion = ConfigurableJointMotion.Free;
        activeJoint.angularYMotion = ConfigurableJointMotion.Free;
        activeJoint.angularZMotion = ConfigurableJointMotion.Free;

        activeJoint.breakForce = maxJointForce * 1.5f;
        activeJoint.breakTorque = maxJointForce * 1.5f;
        activeJoint.enablePreprocessing = false;

        // Prevent hand colliders from depenetrating/slamming the dynamic door
        SetHandDoorCollisionIgnored(hand, true);

        // Visual hand pose (Fist gesture + grip curled)
        var handAnim = hand.GetComponentInChildren<HandAnimator>() ?? hand.GetComponentInParent<HandAnimator>();
        if (handAnim != null)
        {
            handAnim.PlayGesture(HandAnimator.Gesture.Fist);
            handAnim.SetGrip(1f);
        }

        // In Desktop mode, pin the visual hand mesh to the handle
        if (!isVR && DesktopHandDriver.Instance != null)
        {
            DesktopHandDriver.Instance.SetPinnedTarget(hand, activeGrip != null ? activeGrip : transform);
        }

        SetHighlight(true);
    }

    public void UpdateHoldDesktop(HandGrabSystem hand, float dt)
    {
        // PhysX ConfigurableJoint handles the door movement directly!
    }

    public void UpdateHoldVR(HandGrabSystem hand, float dt)
    {
        // PhysX ConfigurableJoint handles the door movement directly!
    }

    public void EndHold(HandGrabSystem hand)
    {
        if (hand != holdingHand && holdingHand != null) return;

        isHeld = false;

        // Destroy the physics joint
        if (activeJoint != null)
        {
            Destroy(activeJoint);
            activeJoint = null;
        }

        if (holdingHand != null)
        {
            SetHandDoorCollisionIgnored(holdingHand, false);

            if (!isVR && DesktopHandDriver.Instance != null)
            {
                DesktopHandDriver.Instance.ClearPinnedTarget(holdingHand);
            }

            var handAnim = holdingHand.GetComponentInChildren<HandAnimator>() ?? holdingHand.GetComponentInParent<HandAnimator>();
            if (handAnim != null)
            {
                handAnim.ClearGesture();
                handAnim.SetGrip(0f);
            }
            holdingHand = null;
        }

        if (requireKnobTwistToUnlock)
        {
            bool isClosed = Mathf.Abs(GetDoorAngle()) < 2.5f;
            if (isClosed)
            {
                isDoorLatched = true;
                LockDoorJoint(true);
                PlaySound(latchLockSound ?? (closeSounds != null && closeSounds.Length > 0 ? closeSounds[0] : null));
            }
            isKnobTwisted = false;
        }

        // Stop door momentum and freeze in place so it stays open at current angle without slow closing or drifting
        if (doorRigidbody != null)
        {
            doorRigidbody.angularVelocity = Vector3.zero;
            doorRigidbody.linearVelocity = Vector3.zero;

            if (stayOpenOnRelease)
            {
                doorRigidbody.isKinematic = true;
                doorRigidbody.angularDamping = releasedAngularDamping;
            }
            else
            {
                doorRigidbody.angularDamping = 2.0f;
            }
        }

        SetHighlight(false);
    }

    public void NudgeToggle()
    {
        if (!allowQuickClickToggle || doorRigidbody == null) return;
        if (doorConfigJoint == null && doorHinge == null) return;

        if (requireKnobTwistToUnlock)
        {
            isDoorLatched = false;
            isKnobTwisted = true;
            LockDoorJoint(false);
            currentKnobVisualAngle = 45f;
            PlaySound(latchUnlockSound ?? (openSounds != null && openSounds.Length > 0 ? openSounds[0] : null));
        }

        GetHingeVectors(out Vector3 hingeAxis, out _, out _);

        float angle = GetDoorAngle();
        float minLimit = (doorConfigJoint != null) ? doorConfigJoint.lowAngularXLimit.limit : (doorHinge != null ? doorHinge.limits.min : -90f);
        float maxLimit = (doorConfigJoint != null) ? doorConfigJoint.highAngularXLimit.limit : (doorHinge != null ? doorHinge.limits.max : 0f);

        float sign = 1f;

        // Negative limit door (Bathroom door: -80..0, Right wardrobe door: -98.5..0)
        if (minLimit < -5f && maxLimit <= 5f)
        {
            // If open past -6 deg, swing closed (+1 torque towards 0)
            // Otherwise swing open (-1 torque towards min)
            sign = (angle < -6f) ? 1f : -1f;
        }
        else
        {
            // Positive limit door (Left wardrobe door: 0..130)
            // If open past +6 deg, swing closed (-1 torque towards 0)
            // Otherwise swing open (+1 torque towards max)
            sign = (angle > 6f) ? -1f : 1f;
        }

        doorRigidbody.angularVelocity = hingeAxis * (sign * toggleImpulseSpeed);
    }

    #endregion

    #region Geometry Math

    public float GetDoorAngle()
    {
        if (doorHinge != null) return doorHinge.angle;
        if (doorRigidbody == null) return 0f;
        Quaternion delta = Quaternion.Inverse(initialDoorRotation) * doorRigidbody.transform.localRotation;
        delta.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f) angle -= 360f;
        Vector3 localHingeAxis = (doorConfigJoint != null) ? doorConfigJoint.axis : (doorHinge != null ? doorHinge.axis : Vector3.up);
        if (Vector3.Dot(axis, localHingeAxis) < 0f) angle = -angle;
        return angle;
    }

    public static ConfigurableJoint UpgradeHingeToConfigurable(HingeJoint hj)
    {
        if (hj == null) return null;
        var go = hj.gameObject;
        var cj = go.AddComponent<ConfigurableJoint>();
        cj.connectedBody = hj.connectedBody;
        cj.autoConfigureConnectedAnchor = hj.autoConfigureConnectedAnchor;
        cj.anchor = hj.anchor;
        cj.axis = hj.axis;
        cj.connectedAnchor = hj.connectedAnchor;

        // Lock linear axes to rigidly prevent the door from tearing off its pivot
        cj.xMotion = ConfigurableJointMotion.Locked;
        cj.yMotion = ConfigurableJointMotion.Locked;
        cj.zMotion = ConfigurableJointMotion.Locked;

        // Angular rotation around hinge axis (Primary Axis X in ConfigurableJoint)
        cj.angularXMotion = hj.useLimits ? ConfigurableJointMotion.Limited : ConfigurableJointMotion.Free;
        cj.angularYMotion = ConfigurableJointMotion.Locked;
        cj.angularZMotion = ConfigurableJointMotion.Locked;

        if (hj.useLimits)
        {
            cj.lowAngularXLimit = new SoftJointLimit { limit = hj.limits.min, bounciness = hj.limits.bounciness, contactDistance = hj.limits.contactDistance };
            cj.highAngularXLimit = new SoftJointLimit { limit = hj.limits.max, bounciness = hj.limits.bounciness, contactDistance = hj.limits.contactDistance };
        }

        cj.projectionMode = JointProjectionMode.PositionAndRotation;
        cj.projectionDistance = 0.01f;
        cj.projectionAngle = 3.0f;

        Destroy(hj);
        return cj;
    }

    public void GetHingeVectors(out Vector3 hingeAxisWorld, out Vector3 swingTangentWorld, out float armLength)
    {
        hingeAxisWorld = Vector3.up;
        swingTangentWorld = Vector3.forward;
        armLength = 0.5f;

        Vector3 localAxis = Vector3.up;
        Vector3 localAnchor = Vector3.zero;
        Transform jointTransform = doorRigidbody != null ? doorRigidbody.transform : transform;

        if (doorConfigJoint != null)
        {
            localAxis = doorConfigJoint.axis;
            localAnchor = doorConfigJoint.anchor;
            jointTransform = doorConfigJoint.transform;
        }
        else if (doorHinge != null)
        {
            localAxis = doorHinge.axis;
            localAnchor = doorHinge.anchor;
            jointTransform = doorHinge.transform;
        }

        hingeAxisWorld = jointTransform.TransformDirection(localAxis).normalized;
        Vector3 hingeAnchorWorld = jointTransform.TransformPoint(localAnchor);

        // Vector from hinge to handle
        Vector3 gripPos = gripPoint != null ? gripPoint.position : transform.position;
        Vector3 radialVector = gripPos - hingeAnchorWorld;

        // Project radial vector onto plane perpendicular to hinge axis
        Vector3 radialArm = Vector3.ProjectOnPlane(radialVector, hingeAxisWorld);
        armLength = radialArm.magnitude;

        if (armLength > 0.01f)
        {
            swingTangentWorld = Vector3.Cross(hingeAxisWorld, radialArm.normalized).normalized;
        }
    }

    #endregion

    #region Audio

    void UpdateSoundDetector()
    {
        if (audioSource == null) return;
        if (doorConfigJoint == null && doorHinge == null) return;

        float currentAngle = Mathf.Abs(GetDoorAngle());
        bool isNowClosed = currentAngle < 2.5f;

        // Opened from closed position
        if (wasClosed && !isNowClosed)
        {
            if (soundCooldown <= 0f && openSounds != null && openSounds.Length > 0)
            {
                audioSource.PlayOneShot(openSounds[Random.Range(0, openSounds.Length)]);
                soundCooldown = 0.6f;
            }
            wasClosed = false;
        }
        // Closed from open position
        else if (!wasClosed && isNowClosed)
        {
            if (soundCooldown <= 0f && closeSounds != null && closeSounds.Length > 0)
            {
                audioSource.PlayOneShot(closeSounds[Random.Range(0, closeSounds.Length)]);
                soundCooldown = 0.6f;
            }
            wasClosed = true;

            if (requireKnobTwistToUnlock && !isHeld)
            {
                isDoorLatched = true;
                LockDoorJoint(true);
            }
        }
    }

    #endregion
}
