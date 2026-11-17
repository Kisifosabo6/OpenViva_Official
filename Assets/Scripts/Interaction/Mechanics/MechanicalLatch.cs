using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Couples a MechanicalPart (such as a MechanicalRotator handle or MechanicalSlider pin)
/// to a physical door or cabinet HingeJoint, managing realistic locking, unlatching,
/// latch bolt retraction, and auditory rattling feedback.
/// </summary>
public class MechanicalLatch : MonoBehaviour
{
    [Header("Driver Part")]
    [Tooltip("The handle or pin that operates this latch")]
    public MechanicalPart driverPart;

    [Tooltip("Normalized value (0.0 to 1.0) required to unlatch the door")]
    [Range(0.1f, 0.95f)]
    public float unlockThreshold = 0.65f;

    [Header("Target Door Hinge")]
    [Tooltip("The door's HingeJoint that gets locked/unlocked")]
    public HingeJoint targetHinge;

    [Tooltip("Custom unlocked limits (if left 0/0, captured automatically from targetHinge)")]
    public JointLimits unlockedLimits;

    [Header("Visual Bolt (Optional)")]
    [Tooltip("Physical bolt/tongue that retracts into the door")]
    public Transform visualBolt;
    public Vector3 boltRetractAxis = Vector3.right;
    public float boltRetractDistance = 0.015f; // 1.5 cm

    [Header("Audio")]
    public AudioSource audioSource;
    public AudioClip unlockSound;
    public AudioClip lockSound;
    public AudioClip rattleLockedSound;

    [Header("Events")]
    public UnityEvent onUnlocked = new UnityEvent();
    public UnityEvent onLocked = new UnityEvent();

    // Internal State
    private bool isUnlocked = false;
    private JointLimits lockedLimits;
    private Vector3 boltBaseLocalPos;
    private float rattleCooldown = 0f;

    public bool IsUnlocked => isUnlocked;

    private void Awake()
    {
        if (targetHinge == null)
        {
            targetHinge = GetComponent<HingeJoint>() ?? GetComponentInParent<HingeJoint>();
        }

        if (targetHinge != null)
        {
            // If unlocked limits were not explicitly defined, capture current hinge limits
            if (Mathf.Approximately(unlockedLimits.min, 0f) && Mathf.Approximately(unlockedLimits.max, 0f))
            {
                unlockedLimits = targetHinge.limits;
            }

            lockedLimits = new JointLimits
            {
                min = 0f,
                max = 0f,
                bounciness = 0f,
                bounceMinVelocity = 0f
            };

            // Start locked if door is near resting angle
            if (Mathf.Abs(targetHinge.angle) < 3f)
            {
                LockDoor(silent: true);
            }
            else
            {
                UnlockDoor(silent: true);
            }
        }

        if (visualBolt != null)
        {
            boltBaseLocalPos = visualBolt.localPosition;
        }

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>() ?? GetComponentInParent<AudioSource>();
        }

        if (driverPart != null)
        {
            driverPart.onValueChanged.AddListener(OnDriverValueChanged);
        }
    }

    private void OnDestroy()
    {
        if (driverPart != null)
        {
            driverPart.onValueChanged.RemoveListener(OnDriverValueChanged);
        }
    }

    private void Update()
    {
        if (rattleCooldown > 0f) rattleCooldown -= Time.deltaTime;

        // Auto relock when door returns to closed position and driver is released
        if (isUnlocked && targetHinge != null && driverPart != null)
        {
            bool doorIsClosed = Mathf.Abs(targetHinge.angle) < 2.5f;
            bool driverIsReleased = driverPart.NormalizedValue < 0.2f && !driverPart.IsManipulated;

            if (doorIsClosed && driverIsReleased)
            {
                LockDoor();
            }
        }
    }

    private void OnDriverValueChanged(float normalized)
    {
        // Animate visual bolt retraction linearly with handle movement
        if (visualBolt != null)
        {
            visualBolt.localPosition = boltBaseLocalPos + (boltRetractAxis.normalized * (normalized * boltRetractDistance));
        }

        // Check unlock threshold
        if (!isUnlocked && normalized >= unlockThreshold)
        {
            UnlockDoor();
        }
    }

    public void UnlockDoor(bool silent = false)
    {
        if (isUnlocked) return;

        isUnlocked = true;

        if (targetHinge != null)
        {
            targetHinge.limits = unlockedLimits;
        }

        if (!silent && audioSource != null && unlockSound != null)
        {
            audioSource.PlayOneShot(unlockSound);
        }

        onUnlocked.Invoke();
    }

    public void LockDoor(bool silent = false)
    {
        if (!isUnlocked) return;

        isUnlocked = false;

        if (targetHinge != null)
        {
            targetHinge.limits = lockedLimits;
        }

        if (visualBolt != null)
        {
            visualBolt.localPosition = boltBaseLocalPos;
        }

        if (!silent && audioSource != null && lockSound != null)
        {
            audioSource.PlayOneShot(lockSound);
        }

        onLocked.Invoke();
    }

    /// <summary>
    /// Play rattle sound when player pulls or pushes door while it is locked shut
    /// </summary>
    public void PlayLockedRattle()
    {
        if (!isUnlocked && rattleCooldown <= 0f && audioSource != null && rattleLockedSound != null)
        {
            audioSource.PlayOneShot(rattleLockedSound);
            rattleCooldown = 0.4f;
        }
    }
}
