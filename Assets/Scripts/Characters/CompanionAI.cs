using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Viva.Interaction;

/// <summary>
/// Modernized Companion AI for Shinobu.
/// Adapted from legacy Viva project (Loli.cs, Loli.Vision.cs, Loli.Voice.cs)
/// for Unity 6, URP, and the new interaction/movement system.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class CompanionAI : MonoBehaviour
{
    public enum CompanionState
    {
        Idle,
        Walking,
        Running,
        Interacting
    }

    [Header("Navigation & Follow")]
    [Tooltip("Target transform to follow. Defaults to Main Camera if unassigned.")]
    public Transform target;
    [Tooltip("Whether the companion is actively following the target.")]
    public bool isFollowing = true;
    [Tooltip("Distance at which companion stops following.")]
    public float stoppingDistance = 1.8f;
    [Tooltip("Distance beyond which companion runs instead of walks.")]
    public float runDistance = 4.2f;
    [Tooltip("Walking speed in m/s.")]
    public float walkSpeed = 1.5f;
    [Tooltip("Running speed in m/s.")]
    public float runSpeed = 3.2f;
    [Tooltip("Smooth turn speed for facing direction.")]
    public float turnSpeed = 8.0f;

    [Header("Head Tracking (Loli.Vision LookAtBone)")]
    public Transform headBone;
    public Transform neckBone;
    [Tooltip("Enable procedural head-look tracking toward target/camera.")]
    public bool enableHeadTracking = true;
    [Tooltip("Maximum angle (degrees) to rotate head toward target.")]
    public float maxHeadAngle = 40.0f;
    [Tooltip("Maximum neck pitch offset (degrees).")]
    public float maxNeckPitch = 20.0f;
    [Range(0f, 1f)]
    public float lookAtWeight = 1.0f;

    [Header("Face & Blendshapes")]
    public SkinnedMeshRenderer headSMR;
    [Tooltip("Enable procedural eye blinking.")]
    public bool enableBlinking = true;
    public float minBlinkInterval = 2.5f;
    public float maxBlinkInterval = 5.5f;

    [Header("Voice & Audio")]
    public AudioSource voiceSource;
    [Tooltip("Enable periodic idle chatter.")]
    public bool enableIdleVoice = true;
    public float minIdleVoiceInterval = 25.0f;
    public float maxIdleVoiceInterval = 50.0f;

    [Header("Voice Clip Pools")]
    public List<AudioClip> greetClips = new List<AudioClip>();
    public List<AudioClip> followClips = new List<AudioClip>();
    public List<AudioClip> stopClips = new List<AudioClip>();
    public List<AudioClip> headpatClips = new List<AudioClip>();
    public List<AudioClip> idleClips = new List<AudioClip>();
    public List<AudioClip> happyClips = new List<AudioClip>();

    [Header("Components")]
    [SerializeField] private NavMeshAgent agent;
    [SerializeField] private Animator animator;
    [SerializeField] private CharacterInteractable interactable;

    // Animation parameter hashes
    private static readonly int SpeedHash = Animator.StringToHash("speed");
    private static readonly int ForwardSpeedHash = Animator.StringToHash("forwardSpeed");
    private static readonly int SidewaysSpeedHash = Animator.StringToHash("sidewaysSpeed");
    private static readonly int VelocityXHash = Animator.StringToHash("VelocityX");
    private static readonly int HeadpatProperHash = Animator.StringToHash("HeadpatProper");

    // Valid parameter cache to prevent missing parameter errors
    private readonly HashSet<int> validAnimatorParameters = new HashSet<int>();

    // Internal state
    private CompanionState currentState = CompanionState.Idle;
    private float currentAnimSpeed = 0f;
    private float currentForwardSpeed = 0f;
    private float currentSidewaysSpeed = 0f;
    private Vector3 lastPosition = Vector3.zero;
    private Vector3 smoothedVelocity = Vector3.zero;
    private Quaternion lastHeadRot = Quaternion.identity;
    private float smoothedLookWeight = 0f;
    private float nextBlinkTime = 0f;
    private float nextIdleVoiceTime = 0f;
    private float nextRepathTime = 0f;
    private Vector3 lastSampledDestination = Vector3.zero;
    private bool isBlinking = false;
    private float blinkAmount = 0f;
    private float happyTimer = 0f;

    /// <summary>
    /// Returns true if the companion is actively moving horizontally.
    /// </summary>
    public bool IsMoving => smoothedVelocity.sqrMagnitude > 0.01f;

    /// <summary>
    /// Current smoothed world velocity.
    /// </summary>
    public Vector3 CurrentVelocity => smoothedVelocity;

    // BlendShape cache indices
    private List<int> eyelidBSIndices = new List<int>();
    private List<int> smileBSIndices = new List<int>();

    private void Awake()
    {
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        if (agent != null)
        {
            agent.agentTypeID = 0;
            agent.enabled = false;
        }
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (animator != null)
        {
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.applyRootMotion = false;
        }
        if (interactable == null) interactable = GetComponent<CharacterInteractable>();

        CacheAnimatorParameters();
        SetupAudioSource();
        SetupNavAgent();
        AutoDetectBones();
        CacheBlendShapes();
        LoadVoiceClipsIfEmpty();
        EnsureMeshRenderersActiveAndUpdated();
    }

    public void EnsureMeshRenderersActiveAndUpdated()
    {
        foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            smr.updateWhenOffscreen = true;
            if (smr.localBounds.size.sqrMagnitude < 0.01f && smr.sharedMesh != null)
            {
                smr.localBounds = smr.sharedMesh.bounds;
            }
        }
    }

    private void Start()
    {
        lastPosition = transform.position;
        smoothedVelocity = Vector3.zero;

        TrySnapToNavMesh();

        UpdateTargetReference();
        EnsureMeshRenderersActiveAndUpdated();

        InitializeLocomotionAnimation();

        nextBlinkTime = Time.time + Random.Range(minBlinkInterval, maxBlinkInterval);
        nextIdleVoiceTime = Time.time + Random.Range(minIdleVoiceInterval, maxIdleVoiceInterval);

        // Friendly greet on spawn
        PlayRandomVoice(greetClips);
    }

    public void InitializeLocomotionAnimation()
    {
        if (animator == null) return;
        animator.Play("stand_happy_loop", 0, 0f);
        if (animator.layerCount > 1)
        {
            animator.Play("stand_happy_loop", 1, 0f);
        }
        if (animator.layerCount > 2)
        {
            animator.Play("stand_happy_idle", 2, 0f);
        }
    }

    private void OnEnable()
    {
        Globals.OnFollowCalled += HandleFollowHotkey;
        PlayerManager.OnMoveCharaToCamera += HandleMoveCharaToCamera;
        EnsureMeshRenderersActiveAndUpdated();
    }

    private void OnDisable()
    {
        Globals.OnFollowCalled -= HandleFollowHotkey;
        PlayerManager.OnMoveCharaToCamera -= HandleMoveCharaToCamera;
    }

    private void SetupAudioSource()
    {
        if (voiceSource == null)
        {
            voiceSource = GetComponent<AudioSource>();
            if (voiceSource == null)
            {
                voiceSource = gameObject.AddComponent<AudioSource>();
            }
        }
        voiceSource.spatialBlend = 1.0f; // 3D audio
        voiceSource.minDistance = 1.0f;
        voiceSource.maxDistance = 25.0f;
        voiceSource.rolloffMode = AudioRolloffMode.Linear;
        voiceSource.playOnAwake = false;
    }

    private void SetupNavAgent()
    {
        if (agent != null)
        {
            agent.agentTypeID = 0;
            agent.speed = walkSpeed;
            agent.stoppingDistance = stoppingDistance;
            agent.radius = 0.3f;
            agent.height = 1.2f;
            agent.acceleration = 10f;
            agent.angularSpeed = 360f;
            agent.updateRotation = false; // Custom smooth rotation
            agent.autoBraking = true;
            agent.enabled = false;
        }
    }

    public bool TrySnapToNavMesh()
    {
        Vector3 searchPos = transform.position;
        if (searchPos.sqrMagnitude < 1f && target != null)
        {
            searchPos = target.position;
        }

        if (NavMesh.SamplePosition(searchPos, out NavMeshHit hit, 15.0f, NavMesh.AllAreas))
        {
            transform.position = hit.position;
            lastPosition = hit.position;
            smoothedVelocity = Vector3.zero;
            if (agent != null)
            {
                agent.agentTypeID = 0;
                agent.enabled = true;
                if (agent.isOnNavMesh)
                {
                    agent.Warp(hit.position);
                }
            }
            return true;
        }
        else if (target != null && NavMesh.SamplePosition(target.position, out NavMeshHit targetHit, 15.0f, NavMesh.AllAreas))
        {
            transform.position = targetHit.position;
            lastPosition = targetHit.position;
            smoothedVelocity = Vector3.zero;
            if (agent != null)
            {
                agent.agentTypeID = 0;
                agent.enabled = true;
                if (agent.isOnNavMesh)
                {
                    agent.Warp(targetHit.position);
                }
            }
            return true;
        }
        else
        {
            Debug.LogWarning($"[CompanionAI] Could not find NavMesh within 15m of {transform.position}");
            if (agent != null)
            {
                agent.agentTypeID = 0;
                agent.enabled = false;
            }
            return false;
        }
    }

    private void CacheAnimatorParameters()
    {
        validAnimatorParameters.Clear();
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            foreach (var p in animator.parameters)
            {
                validAnimatorParameters.Add(p.nameHash);
            }
        }
    }

    private void SetAnimatorFloat(int hash, float val)
    {
        if (animator != null && validAnimatorParameters.Contains(hash))
        {
            animator.SetFloat(hash, val);
        }
    }

    private void AutoDetectBones()
    {
        if (animator != null && animator.isHuman)
        {
            if (headBone == null) headBone = animator.GetBoneTransform(HumanBodyBones.Head);
            if (neckBone == null) neckBone = animator.GetBoneTransform(HumanBodyBones.Neck);
        }

        if (headBone == null)
        {
            // Search by name
            Transform[] allChildren = GetComponentsInChildren<Transform>();
            foreach (Transform t in allChildren)
            {
                if (headBone == null && t.name.ToLower().Contains("head")) headBone = t;
                if (neckBone == null && t.name.ToLower().Contains("neck")) neckBone = t;
            }
        }

        if (headSMR == null)
        {
            SkinnedMeshRenderer[] smrs = GetComponentsInChildren<SkinnedMeshRenderer>();
            foreach (var smr in smrs)
            {
                if (smr.name.ToLower().Contains("head") || (smr.sharedMesh != null && smr.sharedMesh.name.ToLower().Contains("head")))
                {
                    headSMR = smr;
                    break;
                }
            }
        }
    }

    private void CacheBlendShapes()
    {
        eyelidBSIndices.Clear();
        smileBSIndices.Clear();

        if (headSMR == null || headSMR.sharedMesh == null) return;

        Mesh mesh = headSMR.sharedMesh;
        int count = mesh.blendShapeCount;
        for (int i = 0; i < count; i++)
        {
            string bsName = mesh.GetBlendShapeName(i).ToLowerInvariant();
            if (bsName.Contains("eyelid") || bsName.Contains("blink") || bsName.Contains("wink"))
            {
                eyelidBSIndices.Add(i);
            }
            if (bsName.Contains("smile") || bsName.Contains("happy") || bsName.Contains("joy"))
            {
                smileBSIndices.Add(i);
            }
        }
    }

    private void LoadVoiceClipsIfEmpty()
    {
        if (greetClips.Count == 0 || followClips.Count == 0 || headpatClips.Count == 0)
        {
            AudioClip[] loaded = Resources.LoadAll<AudioClip>("Voices/Shinobu");
            if (loaded != null && loaded.Length > 0)
            {
                foreach (AudioClip clip in loaded)
                {
                    string n = clip.name.ToLowerInvariant();
                    if (n.Contains("giggle") || n.Contains("confident") || n.Contains("ecstatic"))
                        greetClips.Add(clip);
                    if (n.Contains("giggle") || n.Contains("win") || n.Contains("yummy") || n.Contains("relief"))
                        followClips.Add(clip);
                    if (n.Contains("stop") || n.Contains("refuse") || n.Contains("confused") || n.Contains("troubled"))
                        stopClips.Add(clip);
                    if (n.Contains("headpat") || n.Contains("kissedhappy"))
                        headpatClips.Add(clip);
                    if (n.Contains("humming") || n.Contains("yawn") || n.Contains("miscidle"))
                        idleClips.Add(clip);
                    if (n.Contains("laugh") || n.Contains("giggle") || n.Contains("peace"))
                        happyClips.Add(clip);
                }
            }
        }
    }

    private void Update()
    {
        UpdateTargetReference();
        UpdateNavigationAndMovement();
        UpdateAnimations();
        UpdateBlinking();
        UpdateIdleChatter();
        UpdateFacialExpressions();
    }

    private void OnDrawGizmosSelected()
    {
        var col = GetComponent<BoxCollider>();
        if (col != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);
        }

        Gizmos.color = Color.green;
        Gizmos.DrawSphere(transform.position, 0.05f);

        var smrs = GetComponentsInChildren<SkinnedMeshRenderer>();
        foreach (var smr in smrs)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(smr.bounds.center, smr.bounds.size);
            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position, smr.bounds.center);
        }
    }

    private void LateUpdate()
    {
        if (enableHeadTracking)
        {
            UpdateHeadTracking();
        }
    }

    public void UpdateTargetReference()
    {
        if (Camera.main != null)
        {
            // If target is unassigned, inactive in hierarchy, or points to an obsolete camera while mode switched
            if (target == null || !target.gameObject.activeInHierarchy ||
                (target.GetComponent<Camera>() != null && target != Camera.main.transform) ||
                (!Globals.isDesktopMode && target.name.Contains("KB")))
            {
                target = Camera.main.transform;
            }
        }
    }

    private void UpdateNavigationAndMovement()
    {
        if (agent == null) return;

        if (!agent.enabled || !agent.isOnNavMesh)
        {
            TrySnapToNavMesh();
            if (!agent.enabled || !agent.isOnNavMesh) return;
        }

        if (isFollowing && target != null)
        {
            // Calculate horizontal distance ignoring height offset (e.g. camera at eye level)
            float horizontalDist = Vector2.Distance(
                new Vector2(transform.position.x, transform.position.z),
                new Vector2(target.position.x, target.position.z)
            );

            if (horizontalDist > stoppingDistance)
            {
                agent.isStopped = false;

                // Dynamically adjust speed based on distance
                if (horizontalDist > runDistance)
                {
                    agent.speed = runSpeed;
                    currentState = CompanionState.Running;
                }
                else
                {
                    agent.speed = walkSpeed;
                    currentState = CompanionState.Walking;
                }

                // Rate-limited repath check to prevent canceling in-flight paths every frame
                if (!agent.pathPending && Time.time >= nextRepathTime)
                {
                    Vector3 groundSearchPos = new Vector3(target.position.x, transform.position.y, target.position.z);
                    Vector3 dest = target.position;
                    if (NavMesh.SamplePosition(groundSearchPos, out NavMeshHit hit, 5.0f, NavMesh.AllAreas))
                    {
                        dest = hit.position;
                    }
                    else if (NavMesh.SamplePosition(target.position, out NavMeshHit targetHit, 5.0f, NavMesh.AllAreas))
                    {
                        dest = targetHit.position;
                    }

                    if (Vector3.SqrMagnitude(dest - lastSampledDestination) > 0.25f || !agent.hasPath)
                    {
                        lastSampledDestination = dest;
                        agent.SetDestination(dest);
                    }

                    nextRepathTime = Time.time + 0.25f;
                }
            }
            else
            {
                if (!agent.isStopped)
                {
                    agent.isStopped = true;
                    agent.ResetPath();
                }
                currentState = CompanionState.Idle;
            }
        }
        else if (!isFollowing)
        {
            if (agent.hasPath && agent.remainingDistance <= agent.stoppingDistance)
            {
                agent.isStopped = true;
                currentState = CompanionState.Idle;
            }
        }

        // Custom smooth rotation toward movement direction or desired path direction
        Vector3 moveDir = Vector3.zero;
        if (agent.hasPath && !agent.isStopped && agent.desiredVelocity.sqrMagnitude > 0.04f)
        {
            moveDir = agent.desiredVelocity;
        }
        else if (smoothedVelocity.sqrMagnitude > 0.04f)
        {
            moveDir = smoothedVelocity;
        }

        if (moveDir.sqrMagnitude > 0.04f)
        {
            moveDir.y = 0f;
            Quaternion targetRot = Quaternion.LookRotation(moveDir.normalized);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, turnSpeed * Time.deltaTime);
        }
        else if (currentState == CompanionState.Idle && target != null)
        {
            // Gently face player if close enough
            Vector3 toTarget = (target.position - transform.position);
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > 0.1f && toTarget.sqrMagnitude < 25f)
            {
                Quaternion targetRot = Quaternion.LookRotation(toTarget.normalized);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 2.0f * Time.deltaTime);
            }
        }
    }

    private void UpdateAnimations()
    {
        if (animator == null) return;

        // 1. Calculate actual transform-based velocity
        Vector3 positionDelta = transform.position - lastPosition;
        lastPosition = transform.position;

        float dt = Time.deltaTime;
        Vector3 measuredVelocity = dt > 0.0001f ? positionDelta / dt : Vector3.zero;
        measuredVelocity.y = 0f; // Focus on horizontal motion

        // 2. Query NavMeshAgent velocity if active
        Vector3 agentVelocity = Vector3.zero;
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agentVelocity = agent.velocity;
            agentVelocity.y = 0f;
        }

        // 3. Choose the most active velocity (handles NavMesh pathing, physics, or direct transform moves)
        Vector3 activeVelocity = (agentVelocity.sqrMagnitude > measuredVelocity.sqrMagnitude) ? agentVelocity : measuredVelocity;

        // Smooth velocity to prevent jitter
        smoothedVelocity = Vector3.Lerp(smoothedVelocity, activeVelocity, 12f * Time.deltaTime);

        float currentSpeed = smoothedVelocity.magnitude;
        bool isMoving = currentSpeed > 0.08f;

        float targetForwardSpeed = 0f;
        float targetSidewaysSpeed = 0f;
        float targetSpeed = 1f;

        if (isMoving)
        {
            // Convert to character local space
            Vector3 localVel = transform.InverseTransformDirection(smoothedVelocity);

            // Normalized locomotion axes [-1, 1] scaled by walkSpeed
            targetForwardSpeed = Mathf.Clamp(localVel.z / walkSpeed, -1f, 1f);
            targetSidewaysSpeed = Mathf.Clamp(localVel.x / walkSpeed, -1f, 1f);

            // Responsive forward bias: if character is moving primarily forward, ensure a prominent walk animation
            if (targetForwardSpeed > 0.15f)
            {
                targetForwardSpeed = Mathf.Max(targetForwardSpeed, 0.7f);
            }

            // Determine speed parameter for outer blend tree (1 = walk, 3 = run)
            if (currentState == CompanionState.Running || currentSpeed > walkSpeed * 1.15f)
            {
                targetSpeed = Mathf.Lerp(1f, 3f, Mathf.InverseLerp(walkSpeed, runSpeed, currentSpeed));
            }
            else
            {
                targetSpeed = Mathf.Clamp(currentSpeed / walkSpeed, 0.7f, 1.3f);
            }
        }

        currentForwardSpeed = Mathf.Lerp(currentForwardSpeed, targetForwardSpeed, 12f * Time.deltaTime);
        currentSidewaysSpeed = Mathf.Lerp(currentSidewaysSpeed, targetSidewaysSpeed, 12f * Time.deltaTime);
        currentAnimSpeed = Mathf.Lerp(currentAnimSpeed, targetSpeed, 12f * Time.deltaTime);

        SetAnimatorFloat(ForwardSpeedHash, currentForwardSpeed);
        SetAnimatorFloat(SidewaysSpeedHash, currentSidewaysSpeed);
        SetAnimatorFloat(SpeedHash, currentAnimSpeed);

        // Backward compatibility for existing VelocityX blend trees (only if parameter exists)
        SetAnimatorFloat(VelocityXHash, isMoving ? 1f : 0f);
    }

    /// <summary>
    /// Head and neck tracking adapted from legacy Loli.Vision LookAtBone
    /// </summary>
    private void UpdateHeadTracking()
    {
        if (headBone == null || target == null) return;

        Vector3 eyesToTarget = target.position - headBone.position;
        Vector3 forward = transform.forward;

        // Angle between character forward and target
        Vector3 flatDir = eyesToTarget;
        flatDir.y = 0;
        float angleToTarget = Vector3.Angle(forward, flatDir);

        bool inView = angleToTarget < 85.0f && eyesToTarget.sqrMagnitude < 64.0f;
        float targetWeight = inView ? lookAtWeight : 0f;
        smoothedLookWeight = Mathf.MoveTowards(smoothedLookWeight, targetWeight, 3.0f * Time.deltaTime);

        if (smoothedLookWeight <= 0.001f) return;

        float smoothEase = EaseInOutCubic(smoothedLookWeight);

        // 1. Rotate neck
        if (neckBone != null)
        {
            Vector3 neckEuler = neckBone.localEulerAngles;
            float pitchOffset = Mathf.Atan2(-eyesToTarget.y, Mathf.Sqrt(eyesToTarget.x * eyesToTarget.x + eyesToTarget.z * eyesToTarget.z)) * Mathf.Rad2Deg;
            pitchOffset = Mathf.Clamp(pitchOffset * 0.25f, -maxNeckPitch, maxNeckPitch);
            neckEuler.x += pitchOffset;
            neckBone.localEulerAngles = Vector3.LerpUnclamped(neckBone.localEulerAngles, neckEuler, smoothEase * 0.5f);
        }

        // 2. Rotate head
        Quaternion cachedHeadRot = headBone.rotation;
        Quaternion targetLookRot = Quaternion.LookRotation(eyesToTarget.normalized, Vector3.up);

        // Clamp head rotation angle relative to character forward
        Quaternion clampedLookRot = Quaternion.RotateTowards(transform.rotation, targetLookRot, maxHeadAngle);

        headBone.rotation = Quaternion.Slerp(cachedHeadRot, clampedLookRot, smoothEase * 0.8f);
    }

    private void UpdateBlinking()
    {
        if (!enableBlinking || headSMR == null || eyelidBSIndices.Count == 0) return;

        if (!isBlinking && Time.time >= nextBlinkTime)
        {
            isBlinking = true;
            blinkAmount = 0f;
            StartCoroutine(BlinkRoutine());
        }
    }

    private IEnumerator BlinkRoutine()
    {
        // Close eyes
        float t = 0f;
        while (t < 0.08f)
        {
            t += Time.deltaTime;
            blinkAmount = Mathf.Clamp01(t / 0.08f);
            SetEyelidWeight(blinkAmount * 100f);
            yield return null;
        }

        // Open eyes
        t = 0f;
        while (t < 0.12f)
        {
            t += Time.deltaTime;
            blinkAmount = 1f - Mathf.Clamp01(t / 0.12f);
            SetEyelidWeight(blinkAmount * 100f);
            yield return null;
        }

        SetEyelidWeight(0f);
        isBlinking = false;
        nextBlinkTime = Time.time + Random.Range(minBlinkInterval, maxBlinkInterval);
    }

    private void SetEyelidWeight(float weight)
    {
        if (headSMR == null) return;
        foreach (int idx in eyelidBSIndices)
        {
            headSMR.SetBlendShapeWeight(idx, weight);
        }
    }

    private void UpdateFacialExpressions()
    {
        if (headSMR == null || smileBSIndices.Count == 0) return;

        if (happyTimer > 0f)
        {
            happyTimer -= Time.deltaTime;
            float smileWeight = Mathf.Clamp01(happyTimer) * 80f;
            foreach (int idx in smileBSIndices)
            {
                headSMR.SetBlendShapeWeight(idx, smileWeight);
            }
        }
    }

    private void UpdateIdleChatter()
    {
        if (!enableIdleVoice || voiceSource.isPlaying || currentState != CompanionState.Idle) return;

        if (Time.time >= nextIdleVoiceTime)
        {
            nextIdleVoiceTime = Time.time + Random.Range(minIdleVoiceInterval, maxIdleVoiceInterval);
            PlayRandomVoice(idleClips);
        }
    }

    #region Command & Interaction Handling

    public void ReceiveCommand(string commandID)
    {
        Debug.Log($"[CompanionAI] Received command: {commandID}");

        switch (commandID)
        {
            case "Follow":
                SetFollowing(true);
                PlayRandomVoice(followClips);
                break;

            case "Stop":
                SetFollowing(false);
                PlayRandomVoice(stopClips);
                break;

            case "Wave":
                TriggerWave();
                PlayRandomVoice(greetClips);
                break;

            case "Headpat":
                TriggerHeadpat();
                PlayRandomVoice(headpatClips);
                break;

            default:
                Debug.LogWarning($"[CompanionAI] Unhandled command: {commandID}");
                break;
        }
    }

    public void SetFollowing(bool follow)
    {
        isFollowing = follow;
        if (!isFollowing && agent != null && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }
    }

    public void ToggleFollowing()
    {
        SetFollowing(!isFollowing);
        if (isFollowing)
        {
            PlayRandomVoice(followClips);
        }
        else
        {
            PlayRandomVoice(stopClips);
        }
    }

    public void TriggerWave()
    {
        if (animator != null)
        {
            animator.CrossFadeInFixedTime("stand_wave_happy_right", 0.2f);
            StartCoroutine(ResetWaveRoutine());
        }
    }

    private IEnumerator ResetWaveRoutine()
    {
        yield return new WaitForSeconds(2.5f);
        ReturnToLocomotion();
    }

    public void TriggerHeadpat()
    {
        happyTimer = 3.5f;
        if (animator != null)
        {
            SetAnimatorFloat(HeadpatProperHash, 1.0f);
            animator.CrossFadeInFixedTime("stand_headpat_happy_proper_loop", 0.2f);
            StartCoroutine(ResetHeadpatRoutine());
        }
    }

    private IEnumerator ResetHeadpatRoutine()
    {
        yield return new WaitForSeconds(3.0f);
        if (animator != null)
        {
            SetAnimatorFloat(HeadpatProperHash, 0f);
        }
        ReturnToLocomotion();
    }

    public void ReturnToLocomotion()
    {
        if (animator == null) return;
        animator.CrossFadeInFixedTime("stand_happy_loop", 0.3f, 0);
        if (animator.layerCount > 1)
        {
            animator.CrossFadeInFixedTime("stand_happy_loop", 0.3f, 1);
        }
        if (animator.layerCount > 2)
        {
            animator.CrossFadeInFixedTime("stand_happy_idle", 0.3f, 2);
        }
    }

    private void HandleFollowHotkey()
    {
        ToggleFollowing();
    }

    private void HandleMoveCharaToCamera()
    {
        if (Camera.main == null || agent == null || !agent.enabled || !agent.isOnNavMesh) return;

        Ray ray = new Ray(Camera.main.transform.position, Camera.main.transform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, 100f))
        {
            if (NavMesh.SamplePosition(hit.point, out NavMeshHit navHit, 4f, NavMesh.AllAreas))
            {
                isFollowing = false;
                agent.isStopped = false;
                agent.SetDestination(navHit.position);
                PlayRandomVoice(followClips);
            }
        }
    }

    #endregion

    #region Voice Helper Methods

    public void PlayVoice(AudioClip clip)
    {
        if (clip == null || voiceSource == null) return;
        voiceSource.Stop();
        voiceSource.clip = clip;
        voiceSource.Play();
    }

    public void PlayRandomVoice(List<AudioClip> pool)
    {
        if (pool == null || pool.Count == 0 || voiceSource == null) return;
        AudioClip chosen = pool[Random.Range(0, pool.Count)];
        PlayVoice(chosen);
    }

    #endregion

    private static float EaseInOutCubic(float t)
    {
        return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
    }
}
