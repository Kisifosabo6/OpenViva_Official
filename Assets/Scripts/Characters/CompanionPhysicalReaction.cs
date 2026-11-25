using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using NaughtyAttributes;
using UnityEssentials.ActiveRagdoll;

/// <summary>
/// Handles character physical perturbations, responsive arm-pull leading with Two-Bone IK,
/// dynamic ragdoll falls when overextended or yanked, and directional floor recovery (getting up) with synchronized multi-layer animations.
/// </summary>
public class CompanionPhysicalReaction : MonoBehaviour
{
    public enum PhysicalState
    {
        Normal,
        Pulled,
        Falling,
        OnFloor,
        GettingUp
    }

    [Header("Current State")]
    [SerializeField] private PhysicalState currentState = PhysicalState.Normal;
    public PhysicalState CurrentState => currentState;

    [Header("Core References")]
    [SerializeField] private CompanionAI companionAI;
    [SerializeField] private Animator animator;
    [SerializeField] private NavMeshAgent agent;

    [Header("Torso & Head Bones")]
    public Transform hipsBone;
    public Transform spineBone;
    public Transform chestBone;
    public Transform headBone;

    [Header("Arm Control Bones (IK & Limbs)")]
    public Transform leftUpperArmBone;   // armControl_l
    public Transform leftLowerArmBone;   // forearmControl_l
    public Transform leftHandBone;       // hand_l
    public Transform leftUpperArmTwistBone; // upperArm_l
    public Transform leftWristTwistBone;    // wrist_l

    public Transform rightUpperArmBone;  // armControl_r
    public Transform rightLowerArmBone;  // forearmControl_r
    public Transform rightHandBone;      // hand_r
    public Transform rightUpperArmTwistBone; // upperArm_r
    public Transform rightWristTwistBone;    // wrist_r

    [Header("Leg Bones")]
    public Transform leftUpperLegBone;
    public Transform leftLowerLegBone;
    public Transform leftFootBone;
    public Transform rightUpperLegBone;
    public Transform rightLowerLegBone;
    public Transform rightFootBone;

    [Header("Arm Pull & Leading Settings")]
    [Tooltip("Distance threshold (meters) for hand displacement to initiate walking towards the pull.")]
    public float pullStartThreshold = 0.20f;
    [Tooltip("Distance threshold (meters) beyond which companion loses balance and falls to the ground.")]
    public float pullKnockdownThreshold = 1.40f;
    [Tooltip("Velocity threshold (m/s) beyond which a violent yank causes a fall to the ground.")]
    public float pullKnockdownVelocity = 5.0f;
    [Tooltip("Smooth speed for the companion to turn and step towards the pull source.")]
    public float pullTurnSpeed = 8.0f;
    [Tooltip("Spine stabilization lean factor during gentle arm pulls.")]
    [Range(0f, 1f)]
    public float spineStabilizationFactor = 0.35f;

    [Header("Knockdown & Fall Settings")]
    [Tooltip("Minimum time (seconds) to remain in falling mode before checking for floor settle.")]
    public float minFallDuration = 0.45f;
    [Tooltip("Velocity squared threshold below which the body is considered settled on the floor.")]
    public float settleVelocitySqrThreshold = 0.05f;
    [Tooltip("Maximum fall time before forcing recovery to prevent getting stuck indefinitely.")]
    public float maxFallTimeout = 4.5f;
    [Tooltip("Layer mask for ground detection when aligning get-up transform.")]
    public LayerMask groundLayerMask = ~0;

    [Header("Optional ActiveRagdoll Integration")]
    [SerializeField] private SlaveController slaveController;
    [SerializeField] private AnimationFollowing animFollow;

    // Internal tracking
    private Rigidbody hipsRigidbody;
    private List<Rigidbody> ragdollRigidbodies = new List<Rigidbody>();
    private List<Collider> ragdollColliders = new List<Collider>();
    private List<ConfigurableJoint> ragdollJoints = new List<ConfigurableJoint>();
    private bool isPullActive = false;
    private bool pullingLeftHand = false;
    private float currentPullWeight = 0f;
    private Vector3 activePullTarget = Vector3.zero;
    private Vector3 lastPullPosition = Vector3.zero;
    private Vector3 lastDestination = Vector3.zero;
    private float smoothedPullVelocity = 0f;
    private float fallTimer = 0f;
    private Coroutine getUpCoroutine = null;

    private void Awake()
    {
        if (companionAI == null) companionAI = GetComponent<CompanionAI>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        if (slaveController == null) slaveController = GetComponentInChildren<SlaveController>();
        if (animFollow == null) animFollow = GetComponentInChildren<AnimationFollowing>();

        // Ensure all ragdoll physics bodies are created and cached immediately
        EnsureRagdollBones();
        CacheRagdollComponents();

        // Ensure skinned meshes do not cull offscreen when ragdolling
        var smrs = GetComponentsInChildren<SkinnedMeshRenderer>();
        foreach (var smr in smrs)
        {
            smr.updateWhenOffscreen = true;
        }
    }

    private void Start()
    {
        SetRagdollKinematic(true);
        EnsureHandPullTargets();
    }

    private static T GetOrAddComponent<T>(Transform t) where T : Component
    {
        if (t.TryGetComponent(out T comp))
        {
            return comp;
        }
        return t.gameObject.AddComponent<T>();
    }

    private void EnsureHandPullTargets()
    {
        if (leftHandBone != null)
        {
            var target = GetOrAddComponent<CompanionHandPullTarget>(leftHandBone);
            target.Initialize(this, true);
        }
        if (rightHandBone != null)
        {
            var target = GetOrAddComponent<CompanionHandPullTarget>(rightHandBone);
            target.Initialize(this, false);
        }
    }

    [Button("Auto Detect Bones")]
    public void AutoDetectBones()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();

        // 1. Explicit search for OpenViva / Shinobu rig control bones first
        Transform[] all = GetComponentsInChildren<Transform>(true);
        foreach (var t in all)
        {
            string lower = t.name.ToLowerInvariant();
            if (hipsBone == null && (lower == "spine1" || lower == "hips" || lower == "pelvis")) hipsBone = t;
            if (spineBone == null && (lower == "spine2" || lower == "spine")) spineBone = t;
            if (chestBone == null && (lower == "spine3" || lower == "chest")) chestBone = t;
            if (headBone == null && lower == "head") headBone = t;

            // Arm limb control hierarchy
            if (leftUpperArmBone == null && lower == "armcontrol_l") leftUpperArmBone = t;
            if (leftLowerArmBone == null && lower == "forearmcontrol_l") leftLowerArmBone = t;
            if (leftHandBone == null && lower == "hand_l") leftHandBone = t;
            if (leftUpperArmTwistBone == null && lower == "upperarm_l") leftUpperArmTwistBone = t;
            if (leftWristTwistBone == null && lower == "wrist_l") leftWristTwistBone = t;

            if (rightUpperArmBone == null && lower == "armcontrol_r") rightUpperArmBone = t;
            if (rightLowerArmBone == null && lower == "forearmcontrol_r") rightLowerArmBone = t;
            if (rightHandBone == null && lower == "hand_r") rightHandBone = t;
            if (rightUpperArmTwistBone == null && lower == "upperarm_r") rightUpperArmTwistBone = t;
            if (rightWristTwistBone == null && lower == "wrist_r") rightWristTwistBone = t;

            // Legs
            if (leftUpperLegBone == null && (lower == "upperlegcontrol_l" || lower == "thigh_l" || lower == "legcontrol_l")) leftUpperLegBone = t;
            if (leftLowerLegBone == null && (lower == "leg_l" || lower == "calf_l" || lower == "lowerleg_l")) leftLowerLegBone = t;
            if (leftFootBone == null && (lower == "foot_l" || lower == "footcontrol_l")) leftFootBone = t;

            if (rightUpperLegBone == null && (lower == "upperlegcontrol_r" || lower == "thigh_r" || lower == "legcontrol_r")) rightUpperLegBone = t;
            if (rightLowerLegBone == null && (lower == "leg_r" || lower == "calf_r" || lower == "lowerleg_r")) rightLowerLegBone = t;
            if (rightFootBone == null && (lower == "foot_r" || lower == "footcontrol_r")) rightFootBone = t;
        }

        // 2. Fallback to Unity Humanoid avatar if any bones remained unmapped
        if (animator != null && animator.isHuman)
        {
            if (hipsBone == null) hipsBone = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (spineBone == null) spineBone = animator.GetBoneTransform(HumanBodyBones.Spine);
            if (chestBone == null) chestBone = animator.GetBoneTransform(HumanBodyBones.Chest);
            if (headBone == null) headBone = animator.GetBoneTransform(HumanBodyBones.Head);

            if (leftUpperArmBone == null) leftUpperArmBone = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            if (leftLowerArmBone == null) leftLowerArmBone = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            if (leftHandBone == null) leftHandBone = animator.GetBoneTransform(HumanBodyBones.LeftHand);

            if (rightUpperArmBone == null) rightUpperArmBone = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            if (rightLowerArmBone == null) rightLowerArmBone = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            if (rightHandBone == null) rightHandBone = animator.GetBoneTransform(HumanBodyBones.RightHand);

            if (leftUpperLegBone == null) leftUpperLegBone = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            if (leftLowerLegBone == null) leftLowerLegBone = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            if (leftFootBone == null) leftFootBone = animator.GetBoneTransform(HumanBodyBones.LeftFoot);

            if (rightUpperLegBone == null) rightUpperLegBone = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            if (rightLowerLegBone == null) rightLowerLegBone = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            if (rightFootBone == null) rightFootBone = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        }
    }

    [Button("Build/Ensure Ragdoll Bones")]
    public void EnsureRagdollBones()
    {
        AutoDetectBones();

        if (hipsBone == null)
        {
            Debug.LogWarning("[CompanionPhysicalReaction] Cannot ensure ragdoll bones: hipsBone not found!");
            return;
        }

        // 1. Hips (Pelvis Root)
        var hipsRb = SetupRigidbody(hipsBone, 10.0f);
        var hipsCol = GetOrAddComponent<BoxCollider>(hipsBone);
        hipsCol.size = new Vector3(0.24f, 0.16f, 0.18f);
        hipsCol.center = Vector3.zero;

        // 2. Spine & Chest
        Rigidbody spineRb = null;
        if (spineBone != null)
        {
            spineRb = SetupRigidbody(spineBone, 7.0f);
            var col = GetOrAddComponent<BoxCollider>(spineBone);
            col.size = new Vector3(0.22f, 0.14f, 0.16f);
            col.center = Vector3.zero;
            SetupConfigurableJoint(spineBone, hipsRb, 30f);
            IgnorePair(col, hipsCol);
        }

        Rigidbody chestRb = null;
        if (chestBone != null)
        {
            chestRb = SetupRigidbody(chestBone, 8.0f);
            var col = GetOrAddComponent<BoxCollider>(chestBone);
            col.size = new Vector3(0.24f, 0.18f, 0.18f);
            col.center = Vector3.zero;
            SetupConfigurableJoint(chestBone, spineRb != null ? spineRb : hipsRb, 30f);
            if (spineBone != null) IgnorePair(col, spineBone.GetComponent<Collider>());
        }

        // 3. Head
        if (headBone != null)
        {
            var headRb = SetupRigidbody(headBone, 3.5f);
            var col = GetOrAddComponent<SphereCollider>(headBone);
            col.radius = 0.11f;
            col.center = new Vector3(0f, 0.05f, 0.02f);
            SetupConfigurableJoint(headBone, chestRb != null ? chestRb : (spineRb != null ? spineRb : hipsRb), 35f);
            if (chestBone != null) IgnorePair(col, chestBone.GetComponent<Collider>());
        }

        Rigidbody torsoUpperRb = chestRb != null ? chestRb : (spineRb != null ? spineRb : hipsRb);

        // 4. Arms
        SetupLimbChain(leftUpperArmBone, leftLowerArmBone, leftHandBone, torsoUpperRb, 2.0f, 1.5f, 0.8f, isLeg: false);
        SetupLimbChain(rightUpperArmBone, rightLowerArmBone, rightHandBone, torsoUpperRb, 2.0f, 1.5f, 0.8f, isLeg: false);

        // 5. Legs
        SetupLimbChain(leftUpperLegBone, leftLowerLegBone, leftFootBone, hipsRb, 5.0f, 3.5f, 1.2f, isLeg: true);
        SetupLimbChain(rightUpperLegBone, rightLowerLegBone, rightFootBone, hipsRb, 5.0f, 3.5f, 1.2f, isLeg: true);

        // Ignore collisions with any root trigger/interaction collider
        Collider rootCol = GetComponent<Collider>();
        if (rootCol != null)
        {
            foreach (var col in GetComponentsInChildren<Collider>())
            {
                if (col != rootCol)
                {
                    Physics.IgnoreCollision(col, rootCol, true);
                }
            }
        }

        SetRagdollKinematic(true);
        Debug.Log("[CompanionPhysicalReaction] Ensured ragdoll bones initialized successfully.");
    }

    private void SetupLimbChain(
        Transform upper,
        Transform lower,
        Transform end,
        Rigidbody rootConnectedRb,
        float upperMass,
        float lowerMass,
        float endMass,
        bool isLeg = false)
    {
        if (upper == null) return;

        // Upper limb
        var upperRb = SetupRigidbody(upper, upperMass);
        var upperCol = SetupCapsuleCollider(upper, lower, 0.05f);
        SetupConfigurableJoint(upper, rootConnectedRb, isLeg ? 50f : 60f);
        if (rootConnectedRb != null)
        {
            var pCol = rootConnectedRb.GetComponent<Collider>();
            if (pCol != null && upperCol != null) IgnorePair(upperCol, pCol);
        }

        // Lower limb
        if (lower != null)
        {
            var lowerRb = SetupRigidbody(lower, lowerMass);
            var lowerCol = SetupCapsuleCollider(lower, end, 0.045f);
            SetupConfigurableJoint(lower, upperRb, isLeg ? 80f : 75f);
            if (upperCol != null && lowerCol != null) IgnorePair(lowerCol, upperCol);

            // End (Hand / Foot)
            if (end != null)
            {
                var endRb = SetupRigidbody(end, endMass);
                Collider endCol;
                if (isLeg)
                {
                    var box = GetOrAddComponent<BoxCollider>(end);
                    box.size = new Vector3(0.08f, 0.06f, 0.14f);
                    box.center = new Vector3(0f, -0.02f, 0.04f);
                    endCol = box;
                }
                else
                {
                    var sphere = GetOrAddComponent<SphereCollider>(end);
                    sphere.radius = 0.038f;
                    sphere.center = Vector3.zero;
                    endCol = sphere;
                }
                SetupConfigurableJoint(end, lowerRb, 30f);
                if (lowerCol != null && endCol != null) IgnorePair(endCol, lowerCol);
            }
        }
    }

    private CapsuleCollider SetupCapsuleCollider(Transform bone, Transform child, float defaultRadius)
    {
        var col = GetOrAddComponent<CapsuleCollider>(bone);
        if (child != null)
        {
            Vector3 worldDelta = child.position - bone.position;
            float length = worldDelta.magnitude;
            Vector3 localDelta = bone.InverseTransformVector(worldDelta);

            col.height = Mathf.Max(length, 0.1f);
            col.radius = Mathf.Clamp(length * 0.22f, 0.03f, 0.075f);
            col.center = localDelta * 0.5f;

            float absX = Mathf.Abs(localDelta.x);
            float absY = Mathf.Abs(localDelta.y);
            float absZ = Mathf.Abs(localDelta.z);
            if (absX >= absY && absX >= absZ) col.direction = 0;
            else if (absY >= absX && absY >= absZ) col.direction = 1;
            else col.direction = 2;
        }
        else
        {
            col.height = 0.18f;
            col.radius = defaultRadius;
            col.center = new Vector3(0f, 0.09f, 0f);
            col.direction = 1;
        }
        return col;
    }

    private Rigidbody SetupRigidbody(Transform t, float mass)
    {
        var rb = GetOrAddComponent<Rigidbody>(t);
        rb.mass = mass;
        rb.linearDamping = 0.5f;
        rb.angularDamping = 1.0f;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.isKinematic = true;
        rb.useGravity = false;
        return rb;
    }

    private ConfigurableJoint SetupConfigurableJoint(Transform bone, Rigidbody connectedRb, float angularLimit)
    {
        if (connectedRb == null) return null;

        var joint = GetOrAddComponent<ConfigurableJoint>(bone);
        joint.connectedBody = connectedRb;
        joint.autoConfigureConnectedAnchor = true;

        joint.xMotion = ConfigurableJointMotion.Locked;
        joint.yMotion = ConfigurableJointMotion.Locked;
        joint.zMotion = ConfigurableJointMotion.Locked;

        joint.angularXMotion = ConfigurableJointMotion.Limited;
        joint.angularYMotion = ConfigurableJointMotion.Limited;
        joint.angularZMotion = ConfigurableJointMotion.Limited;

        var lowX = joint.lowAngularXLimit;
        lowX.limit = -angularLimit;
        joint.lowAngularXLimit = lowX;

        var highX = joint.highAngularXLimit;
        highX.limit = angularLimit;
        joint.highAngularXLimit = highX;

        var angY = joint.angularYLimit;
        angY.limit = angularLimit * 0.7f;
        joint.angularYLimit = angY;

        var angZ = joint.angularZLimit;
        angZ.limit = angularLimit * 0.7f;
        joint.angularZLimit = angZ;

        // Soft joint spring drive
        var drive = new JointDrive
        {
            positionSpring = 600f,
            positionDamper = 40f,
            maximumForce = Mathf.Infinity
        };
        joint.slerpDrive = drive;
        joint.rotationDriveMode = RotationDriveMode.Slerp;
        joint.projectionMode = JointProjectionMode.PositionAndRotation;

        return joint;
    }

    private void IgnorePair(Collider c1, Collider c2)
    {
        if (c1 != null && c2 != null)
        {
            Physics.IgnoreCollision(c1, c2, true);
        }
    }

    private void CacheRagdollComponents()
    {
        ragdollRigidbodies.Clear();
        ragdollColliders.Clear();
        ragdollJoints.Clear();

        // Search entire character tree to ensure all bones are captured
        Rigidbody[] rbs = GetComponentsInChildren<Rigidbody>(true);
        foreach (var rb in rbs)
        {
            if (rb.gameObject == gameObject) continue;
            ragdollRigidbodies.Add(rb);
        }

        if (hipsBone != null && hipsBone.TryGetComponent<Rigidbody>(out var hRb))
        {
            hipsRigidbody = hRb;
        }
        else if (ragdollRigidbodies.Count > 0)
        {
            hipsRigidbody = ragdollRigidbodies[0];
        }

        Collider[] cols = GetComponentsInChildren<Collider>(true);
        foreach (var col in cols)
        {
            if (col.gameObject == gameObject) continue;
            ragdollColliders.Add(col);
        }

        ConfigurableJoint[] joints = GetComponentsInChildren<ConfigurableJoint>(true);
        foreach (var j in joints)
        {
            ragdollJoints.Add(j);
        }

        Debug.Log($"[CompanionPhysicalReaction] Cached {ragdollRigidbodies.Count} ragdoll rigidbodies (hipsRb={(hipsRigidbody != null ? hipsRigidbody.name : "null")}), {ragdollColliders.Count} colliders, {ragdollJoints.Count} joints.");
    }

    public void SetRagdollKinematic(bool kinematic)
    {
        foreach (var rb in ragdollRigidbodies)
        {
            if (rb == null) continue;
            if (kinematic)
            {
                // In Unity 6, only zero velocity when transitioning from dynamic to kinematic
                if (!rb.isKinematic)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                rb.isKinematic = true;
                rb.useGravity = false;
            }
            else
            {
                rb.isKinematic = false;
                rb.useGravity = true;
                rb.WakeUp();
            }
        }

        // When ragdolling (kinematic = false), set joints limp (spring 0, damper 15) so body folds naturally
        foreach (var joint in ragdollJoints)
        {
            if (joint == null) continue;
            var drive = joint.slerpDrive;
            drive.positionSpring = kinematic ? 600f : 0f;
            drive.positionDamper = kinematic ? 40f : 15f;
            joint.slerpDrive = drive;
        }

        if (animator != null)
        {
            animator.enabled = kinematic;
        }
    }

    private void Update()
    {
        HandleKeyboardTesting();

        float targetWeight = (currentState == PhysicalState.Pulled && isPullActive) ? 1.0f : 0.0f;
        currentPullWeight = Mathf.MoveTowards(currentPullWeight, targetWeight, 6.0f * Time.deltaTime);

        if (currentState == PhysicalState.Pulled && isPullActive)
        {
            UpdateArmPullReaction();
        }
    }

    private void FixedUpdate()
    {
        if (currentState == PhysicalState.Falling)
        {
            fallTimer += Time.fixedDeltaTime;
            if (fallTimer >= minFallDuration)
            {
                CheckForFloorSettle();
            }
        }
    }

    #region Gentle Pull & Leading

    /// <summary>
    /// Computes the world target position for pulling companion directly towards the main character's hand/camera.
    /// </summary>
    public Vector3 GetPlayerPullTarget()
    {
        // 1. Desktop physical hands (PlayerKB_GrabController)
        var kbGrab = PlayerKB_GrabController.Instance;
        if (kbGrab != null)
        {
            var hand = pullingLeftHand ? (kbGrab.leftHand != null ? kbGrab.leftHand : kbGrab.rightHand)
                                       : (kbGrab.rightHand != null ? kbGrab.rightHand : kbGrab.leftHand);
            if (hand != null) return hand.transform.position;
        }

        // 2. VR physical hands (PlayerVR_GrabController)
        var vrGrab = FindFirstObjectByType<PlayerVR_GrabController>();
        if (vrGrab != null)
        {
            var vrHand = pullingLeftHand ? (vrGrab.leftHandGrab != null ? vrGrab.leftHandGrab : vrGrab.rightHandGrab)
                                         : (vrGrab.rightHandGrab != null ? vrGrab.rightHandGrab : vrGrab.leftHandGrab);
            if (vrHand != null) return vrHand.transform.position;
        }

        // 3. PlayerManager desktop hands
        if (PlayerManager.Instance != null)
        {
            var kbHand = pullingLeftHand ? PlayerManager.Instance.LeftHandKB : PlayerManager.Instance.RightHandKB;
            if (kbHand != null) return kbHand.transform.position;
        }

        // 4. Player/Camera view ray (natural forward hand holding position)
        Transform camT = (companionAI != null && companionAI.target != null) ? companionAI.target : (Camera.main != null ? Camera.main.transform : null);
        if (camT != null)
        {
            float sideOffset = pullingLeftHand ? -0.22f : 0.22f;
            return camT.position + camT.forward * 0.65f + camT.right * sideOffset - camT.up * 0.25f;
        }

        // Fallback in front of companion
        return transform.position + transform.forward * 1.2f + Vector3.up * 0.8f;
    }

    /// <summary>
    /// Begins a pull interaction on one of the companion's hands.
    /// </summary>
    public void BeginPull(bool isLeft, Vector3 pullWorldPosition)
    {
        if (currentState == PhysicalState.Falling || currentState == PhysicalState.GettingUp || currentState == PhysicalState.OnFloor) return;

        isPullActive = true;
        pullingLeftHand = isLeft;
        activePullTarget = pullWorldPosition;
        lastPullPosition = pullWorldPosition;
        lastDestination = Vector3.zero;
        smoothedPullVelocity = 0f;
        currentState = PhysicalState.Pulled;

        if (companionAI != null)
        {
            companionAI.SetState(CompanionAI.CompanionState.Pulled);
        }
    }

    /// <summary>
    /// Updates the pull position as the player's hand moves.
    /// </summary>
    public void UpdatePull(Vector3 pullWorldPosition)
    {
        if (!isPullActive) return;
        activePullTarget = pullWorldPosition;
    }

    /// <summary>
    /// Ends the pull interaction, smoothly restoring normal locomotion.
    /// </summary>
    public void EndPull()
    {
        if (!isPullActive) return;

        isPullActive = false;
        if (currentState == PhysicalState.Pulled)
        {
            currentState = PhysicalState.Normal;
            if (companionAI != null)
            {
                companionAI.SetState(CompanionAI.CompanionState.Idle);
            }
        }
    }

    private void UpdateArmPullReaction()
    {
        Transform pulledHand = pullingLeftHand ? leftHandBone : rightHandBone;
        if (pulledHand == null)
        {
            EndPull();
            return;
        }

        Vector3 handPos = pulledHand.position;
        Vector3 handToTarget = activePullTarget - handPos;
        float displacement = handToTarget.magnitude;

        // Instantaneous & smoothed pull velocity
        float instantVel = (activePullTarget - lastPullPosition).magnitude / Mathf.Max(Time.deltaTime, 0.001f);
        smoothedPullVelocity = Mathf.Lerp(smoothedPullVelocity, instantVel, 10f * Time.deltaTime);
        lastPullPosition = activePullTarget;

        // "she should try and keep up the pace OR fall down to the ground, after all she is being pulled"
        // If pulled too hard (displacement > 1.40m) or yanked violently (vel > 5.0 m/s), she loses balance and falls down!
        if (displacement > pullKnockdownThreshold || (instantVel > pullKnockdownVelocity && smoothedPullVelocity > 3.8f))
        {
            Debug.Log($"[CompanionPhysicalReaction] Lost balance from excessive pull (disp={displacement:F2}m, vel={instantVel:F2}m/s) -> Falling to the ground!");
            Vector3 stumbleImpulse = (activePullTarget - transform.position).normalized * 4.2f + Vector3.up * 1.0f;
            TriggerKnockdown(stumbleImpulse);
            return;
        }

        // Gentle / active pull leading: she turns towards the pull and keeps up the pace!
        Vector3 flatDir = new Vector3(handToTarget.x, 0f, handToTarget.z);
        if (displacement > pullStartThreshold)
        {
            if (flatDir.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(flatDir.normalized);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, pullTurnSpeed * Time.deltaTime);
            }

            // Dynamically scale speed so she matches pace with the player's walk or run:
            // - Close (0.2m - 0.4m): gentle walk (1.6 m/s)
            // - Medium (0.4m - 0.75m): fast walk / jog (2.2 - 3.2 m/s)
            // - Extended (0.75m - 1.35m): run / sprint (3.5 - 4.5 m/s) to keep up with player!
            float walkSpd = companionAI != null ? companionAI.walkSpeed : 1.5f;
            float runSpd = companionAI != null ? companionAI.runSpeed : 3.2f;
            float targetSpeed = Mathf.Lerp(walkSpd * 1.1f, runSpd * 1.4f, Mathf.InverseLerp(0.30f, 0.95f, displacement));

            bool movedByAgent = false;
            if (agent != null && agent.enabled && agent.isOnNavMesh)
            {
                agent.isStopped = false;
                agent.speed = targetSpeed;
                agent.acceleration = 25f; // Instantaneous acceleration so she doesn't drag behind
                agent.angularSpeed = 480f;

                Vector3 targetWalkPos = activePullTarget - flatDir.normalized * 0.40f;
                if (NavMesh.SamplePosition(targetWalkPos, out NavMeshHit hit, 2.5f, NavMesh.AllAreas))
                {
                    if (Vector3.SqrMagnitude(hit.position - lastDestination) > 0.03f || !agent.hasPath)
                    {
                        agent.SetDestination(hit.position);
                        lastDestination = hit.position;
                    }
                    movedByAgent = true;
                }
            }

            // Fallback direct follow or assist if off-navmesh or overextended
            if (!movedByAgent && flatDir.sqrMagnitude > 0.001f)
            {
                transform.position += flatDir.normalized * (targetSpeed * Time.deltaTime);
            }
            else if (displacement > 0.75f && flatDir.sqrMagnitude > 0.001f)
            {
                // Gentle follow assistance so she stays tight with the player even on sharp turns
                float assistRatio = Mathf.InverseLerp(0.75f, 1.40f, displacement);
                transform.position += flatDir.normalized * (assistRatio * 1.8f * Time.deltaTime);
            }
        }
        else
        {
            if (agent != null && agent.enabled && agent.isOnNavMesh)
            {
                agent.isStopped = true;
            }
        }
    }

    private void LateUpdate()
    {
        if (currentPullWeight > 0.001f)
        {
            Transform pulledArm = pullingLeftHand ? leftUpperArmBone : rightUpperArmBone;
            Transform pulledForearm = pullingLeftHand ? leftLowerArmBone : rightLowerArmBone;
            Transform pulledHand = pullingLeftHand ? leftHandBone : rightHandBone;
            Transform twistUpper = pullingLeftHand ? leftUpperArmTwistBone : rightUpperArmTwistBone;
            Transform twistWrist = pullingLeftHand ? leftWristTwistBone : rightWristTwistBone;

            // Solve Two-Bone IK so Shinobu's hand reaches out and holds the player's hand
            SolveTwoBoneIK(pulledArm, pulledForearm, pulledHand, twistUpper, twistWrist, activePullTarget, pullingLeftHand, currentPullWeight);

            // Stabilize spine posture slightly towards the pull direction
            if (spineBone != null)
            {
                Vector3 toTarget = (activePullTarget - transform.position).normalized;
                Quaternion leanRot = Quaternion.Euler(toTarget.z * 10f * spineStabilizationFactor, 0f, -toTarget.x * 10f * spineStabilizationFactor);
                spineBone.localRotation = Quaternion.Slerp(spineBone.localRotation, spineBone.localRotation * leanRot, currentPullWeight);
            }
        }
    }

    /// <summary>
    /// Analytical Two-Bone IK solver connecting shoulder to hand target position.
    /// </summary>
    private void SolveTwoBoneIK(
        Transform upperArm,
        Transform lowerArm,
        Transform hand,
        Transform twistUpper,
        Transform twistWrist,
        Vector3 targetPos,
        bool isLeft,
        float weight)
    {
        if (upperArm == null || lowerArm == null || hand == null || weight <= 0.001f) return;

        Vector3 shoulderPos = upperArm.position;
        Vector3 elbowPos = lowerArm.position;
        Vector3 handPos = hand.position;

        float upperLen = Vector3.Distance(shoulderPos, elbowPos);
        float lowerLen = Vector3.Distance(elbowPos, handPos);
        float totalReach = upperLen + lowerLen;
        if (upperLen < 0.01f || lowerLen < 0.01f) return;

        Vector3 toTarget = targetPos - shoulderPos;
        float targetDist = toTarget.magnitude;
        if (targetDist < 0.005f) return;

        Vector3 targetDir = toTarget / targetDist;
        float clampedDist = Mathf.Clamp(targetDist, 0.05f, totalReach * 0.999f);

        // Law of Cosines
        float cosShoulder = (upperLen * upperLen + clampedDist * clampedDist - lowerLen * lowerLen) / (2f * upperLen * clampedDist);
        float angleShoulder = Mathf.Acos(Mathf.Clamp(cosShoulder, -1f, 1f));

        // Bend direction: elbow bends downward and slightly outward
        Vector3 bendHint = -transform.up + (isLeft ? -transform.right : transform.right) * 0.35f;
        Vector3 planeNormal = Vector3.Cross(targetDir, bendHint).normalized;
        if (planeNormal.sqrMagnitude < 0.001f)
        {
            planeNormal = transform.forward;
        }
        Vector3 bendDir = Vector3.Cross(planeNormal, targetDir).normalized;

        // Desired elbow and hand positions
        Vector3 desiredElbow = shoulderPos + (targetDir * Mathf.Cos(angleShoulder) + bendDir * Mathf.Sin(angleShoulder)) * upperLen;
        Vector3 desiredHand = (targetDist <= totalReach) ? targetPos : (shoulderPos + targetDir * totalReach);

        // Rotate upper arm towards desired elbow position
        Vector3 curUpperDir = (elbowPos - shoulderPos).normalized;
        Vector3 desUpperDir = (desiredElbow - shoulderPos).normalized;
        if (curUpperDir.sqrMagnitude > 0.01f && desUpperDir.sqrMagnitude > 0.01f)
        {
            Quaternion upperOffset = Quaternion.FromToRotation(curUpperDir, desUpperDir);
            upperArm.rotation = Quaternion.Slerp(upperArm.rotation, upperOffset * upperArm.rotation, weight);
        }

        // Recalculate lower arm rotation after upper arm updated
        Vector3 newElbowPos = lowerArm.position;
        Vector3 curLowerDir = (hand.position - newElbowPos).normalized;
        Vector3 desLowerDir = (desiredHand - newElbowPos).normalized;
        if (curLowerDir.sqrMagnitude > 0.01f && desLowerDir.sqrMagnitude > 0.01f)
        {
            Quaternion lowerOffset = Quaternion.FromToRotation(curLowerDir, desLowerDir);
            lowerArm.rotation = Quaternion.Slerp(lowerArm.rotation, lowerOffset * lowerArm.rotation, weight);
        }

        // Place hand at desired position
        hand.position = Vector3.Lerp(hand.position, desiredHand, weight);

        // Orient hand naturally towards puller
        Vector3 graspForward = (targetPos - desiredHand).sqrMagnitude > 0.001f ? (targetPos - desiredHand).normalized : transform.forward;
        Quaternion graspRot = Quaternion.LookRotation(graspForward, transform.up);
        hand.rotation = Quaternion.Slerp(hand.rotation, graspRot, weight);

        // Smoothly update cosmetic twist bones to eliminate shoulder/wrist pinching
        if (twistUpper != null)
        {
            Vector3 armLocal = Vector3.zero;
            armLocal.y = Mathf.DeltaAngle(upperArm.localEulerAngles.y, 0f) * 0.5f;
            twistUpper.localEulerAngles = armLocal;
        }
        if (twistWrist != null)
        {
            Vector3 wristLocal = Vector3.zero;
            wristLocal.y = Mathf.DeltaAngle(0f, hand.localEulerAngles.y) * 0.5f;
            twistWrist.localEulerAngles = wristLocal;
        }
    }

    #endregion

    #region Knockdown, Falling, and Recovery

    /// <summary>
    /// Triggers a dynamic ragdoll knockdown with an optional impact impulse.
    /// </summary>
    [Button("Trigger Knockdown", EButtonEnableMode.Playmode)]
    public void TriggerKnockdown()
    {
        Vector3 defaultImpulse = -transform.forward * 4.0f + Vector3.up * 1.5f;
        TriggerKnockdown(defaultImpulse);
    }

    public void TriggerKnockdown(Vector3 impulse)
    {
        if (currentState == PhysicalState.Falling || currentState == PhysicalState.GettingUp) return;

        EndPull();
        currentPullWeight = 0f;
        StopAllCoroutines();

        currentState = PhysicalState.Falling;
        fallTimer = 0f;

        if (companionAI != null)
        {
            companionAI.SetState(CompanionAI.CompanionState.Falling);
            // Play vocal reaction
            if (companionAI.stopClips != null && companionAI.stopClips.Count > 0 && companionAI.voiceSource != null)
            {
                var clip = companionAI.stopClips[Random.Range(0, companionAI.stopClips.Count)];
                companionAI.voiceSource.PlayOneShot(clip);
            }
        }

        // Disable navigation
        if (agent != null && agent.enabled)
        {
            if (agent.isOnNavMesh) agent.isStopped = true;
            agent.enabled = false;
        }

        // Enable dynamic physics
        SetRagdollKinematic(false);

        if (slaveController != null)
        {
            slaveController.Die();
        }
        if (animFollow != null)
        {
            animFollow.isAlive = false;
        }

        // Apply impulse
        Transform hitBone = chestBone != null ? chestBone : (hipsBone != null ? hipsBone : transform);
        Rigidbody hitRb = hitBone.GetComponent<Rigidbody>();
        if (hitRb != null)
        {
            hitRb.AddForce(impulse, ForceMode.Impulse);
        }

        Debug.Log($"[CompanionPhysicalReaction] Knockdown triggered with impulse {impulse}. Dynamic rigidbodies count: {ragdollRigidbodies.Count}");
    }

    private void CheckForFloorSettle()
    {
        Rigidbody checkRb = hipsRigidbody != null ? hipsRigidbody : GetComponentInChildren<Rigidbody>();
        if (checkRb == null)
        {
            Debug.LogWarning("[CompanionPhysicalReaction] CheckForFloorSettle: No hipsRigidbody found!");
            return;
        }

        float sqrVel = checkRb.linearVelocity.sqrMagnitude;
        if (sqrVel < settleVelocitySqrThreshold || fallTimer > maxFallTimeout)
        {
            Debug.Log($"[CompanionPhysicalReaction] Ragdoll settled on floor (sqrVel={sqrVel:F3}, time={fallTimer:F2}s). Starting get-up sequence.");
            currentState = PhysicalState.OnFloor;
            if (getUpCoroutine != null) StopCoroutine(getUpCoroutine);
            getUpCoroutine = StartCoroutine(GetUpSequenceRoutine());
        }
    }

    private IEnumerator GetUpSequenceRoutine()
    {
        yield return new WaitForSeconds(0.40f); // Settle pause on floor

        // 1. Detect body orientation on the floor
        Transform refHips = hipsBone != null ? hipsBone : transform;
        Transform refHead = headBone != null ? headBone : refHips;
        Transform refChest = chestBone != null ? chestBone : refHips;

        Vector3 spineToHead = (refHead.position - refHips.position).normalized;
        Vector3 chestFacing = refChest.forward;

        string standUpAnimation = "floor_face_down_to_stand";
        float standUpDuration = 3.0f;
        bool faceDown = true;

        // If chest faces upwards, she is on her back
        if (chestFacing.y > 0.25f)
        {
            standUpAnimation = "floor_face_up_to_stand";
            standUpDuration = 2.5f;
            faceDown = false;
        }
        else
        {
            standUpAnimation = "floor_face_down_to_stand";
            standUpDuration = 3.0f;
            faceDown = true;
        }

        Debug.Log($"[CompanionPhysicalReaction] Settled on floor (chest.y={chestFacing.y:F2}). Selected get-up: {standUpAnimation}");

        // 2. Align root transform to physics hips position grounded on floor
        Vector3 hipsPos = refHips.position;
        Vector3 groundPos = hipsPos;
        if (Physics.Raycast(hipsPos + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 3.0f, groundLayerMask))
        {
            groundPos = hit.point;
        }

        Vector3 flatSpineDir = new Vector3(spineToHead.x, 0f, spineToHead.z).normalized;
        if (flatSpineDir.sqrMagnitude > 0.01f)
        {
            // Face along spine direction if prone, or opposite if supine
            Vector3 facingDir = faceDown ? flatSpineDir : -flatSpineDir;
            transform.rotation = Quaternion.LookRotation(facingDir);
        }
        transform.position = groundPos;

        // 3. Switch back to kinematic for clean animation replay
        SetRagdollKinematic(true);

        if (slaveController != null) slaveController.ComeAlive();
        if (animFollow != null) animFollow.isAlive = true;

        currentState = PhysicalState.GettingUp;
        if (companionAI != null)
        {
            companionAI.SetState(CompanionAI.CompanionState.GettingUp);
        }

        // 4. Play stand-up animation across ALL main layers (legs, torso, face) simultaneously!
        // This completely eliminates the A-pose torso glitch by animating both torso and legs in lockstep.
        if (animator != null)
        {
            animator.CrossFade(standUpAnimation, 0.15f, 0); // Layer 0: legs
            if (animator.layerCount > 1)
            {
                animator.SetLayerWeight(1, 1.0f); // Layer 1: torso
                animator.CrossFade(standUpAnimation, 0.15f, 1);
            }
            if (animator.layerCount > 2)
            {
                animator.SetLayerWeight(2, 1.0f); // Layer 2: face
                animator.CrossFade(standUpAnimation, 0.15f, 2);
            }
            // Mute any higher gesture layers
            for (int i = 3; i < animator.layerCount; i++)
            {
                animator.SetLayerWeight(i, 0f);
            }
        }

        // Wait for stand-up animation completion
        yield return new WaitForSeconds(standUpDuration);

        // 5. Restore navigation and normal AI state
        if (agent != null)
        {
            agent.enabled = true;
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit navHit, 3.0f, NavMesh.AllAreas))
            {
                agent.Warp(navHit.position);
            }
        }

        currentState = PhysicalState.Normal;
        if (companionAI != null)
        {
            companionAI.SetState(CompanionAI.CompanionState.Idle);
            companionAI.isFollowing = true;
        }

        // Smoothly restore standard layer weights
        yield return StartCoroutine(RestoreLayerWeightsRoutine(0.4f));

        Debug.Log("[CompanionPhysicalReaction] Character successfully recovered and returned to normal locomotion.");
        getUpCoroutine = null;
    }

    private IEnumerator RestoreLayerWeightsRoutine(float duration)
    {
        if (animator == null || animator.layerCount <= 1) yield break;

        float timer = 0f;
        while (timer < duration)
        {
            timer += Time.deltaTime;
            float weight = Mathf.Clamp01(timer / duration);
            if (animator.layerCount > 1) animator.SetLayerWeight(1, weight); // torso
            if (animator.layerCount > 2) animator.SetLayerWeight(2, weight); // face
            yield return null;
        }

        if (animator.layerCount > 1) animator.SetLayerWeight(1, 1f);
        if (animator.layerCount > 2) animator.SetLayerWeight(2, 1f);
    }

    /// <summary>
    /// Instantly restores standing pose and navigation.
    /// </summary>
    [Button("Reset to Standing Pose", EButtonEnableMode.Playmode)]
    public void ResetToStanding()
    {
        if (getUpCoroutine != null)
        {
            StopCoroutine(getUpCoroutine);
            getUpCoroutine = null;
        }

        EndPull();
        currentPullWeight = 0f;
        SetRagdollKinematic(true);

        if (slaveController != null) slaveController.ComeAlive();
        if (animFollow != null) animFollow.isAlive = true;

        if (animator != null)
        {
            if (animator.layerCount > 1) animator.SetLayerWeight(1, 1f);
            if (animator.layerCount > 2) animator.SetLayerWeight(2, 1f);
            animator.Play("stand_happy_loop", 0);
        }

        if (agent != null)
        {
            agent.enabled = true;
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit navHit, 3.0f, NavMesh.AllAreas))
            {
                agent.Warp(navHit.position);
            }
        }

        currentState = PhysicalState.Normal;
        if (companionAI != null)
        {
            companionAI.SetState(CompanionAI.CompanionState.Idle);
            companionAI.isFollowing = true;
        }
    }

    #endregion

    #region Testing Controls

    private void HandleKeyboardTesting()
    {
        bool pDown = Input.GetKeyDown(KeyCode.P) || (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.pKey.wasPressedThisFrame);
        bool pHeld = Input.GetKey(KeyCode.P) || (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.pKey.isPressed);
        bool pUp = Input.GetKeyUp(KeyCode.P) || (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.pKey.wasReleasedThisFrame);

        bool kDown = Input.GetKeyDown(KeyCode.K) || (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.kKey.wasPressedThisFrame);
        bool rDown = Input.GetKeyDown(KeyCode.R) || (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.rKey.wasPressedThisFrame);

        // 'P' key: Pull Shinobu towards the main character
        if (pDown || (pHeld && !isPullActive && currentState == PhysicalState.Normal))
        {
            Vector3 pullTarget = GetPlayerPullTarget();
            bool useLeftHand = false;
            if (leftHandBone != null && rightHandBone != null)
            {
                float distLeft = Vector3.SqrMagnitude(leftHandBone.position - pullTarget);
                float distRight = Vector3.SqrMagnitude(rightHandBone.position - pullTarget);
                useLeftHand = distLeft < distRight;
            }
            BeginPull(useLeftHand, pullTarget);
            Debug.Log($"[CompanionPhysicalReaction] P active: Leading Shinobu ({ (useLeftHand ? "left" : "right") } hand) towards player at {pullTarget}");
        }
        else if (pHeld && isPullActive)
        {
            Vector3 pullTarget = GetPlayerPullTarget();
            UpdatePull(pullTarget);
        }
        else if (pUp && isPullActive)
        {
            EndPull();
            Debug.Log("[CompanionPhysicalReaction] P released: Released pull towards main character.");
        }

        // 'K' key: Knockdown
        if (kDown)
        {
            Debug.Log("[CompanionPhysicalReaction] K pressed: Knocking down Shinobu into dynamic ragdoll.");
            TriggerKnockdown();
        }

        // 'R' key: Reset
        if (rDown)
        {
            Debug.Log("[CompanionPhysicalReaction] R pressed: Resetting Shinobu to standing pose.");
            ResetToStanding();
        }
    }

    #endregion
}
