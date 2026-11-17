using UnityEngine;

/// <summary>
/// Modular 1-DOF rotational interactable for VR & Desktop.
/// Ideal for door handles, levers, spherical knobs, steering wheels, faucets, and water valves.
/// Emits normalized values (0.0 to 1.0) and fires events at limits.
/// </summary>
public class MechanicalRotator : MechanicalPart
{
    public enum TrackingMode
    {
        [Tooltip("Tracks hand position moving along a circle around the pivot (levers, long door handles, wheels)")]
        HandPositionArc,
        [Tooltip("Tracks wrist/controller rotation around the local axis (round knobs, key locks, faucet valves)")]
        WristTwist
    }

    [Header("Rotation Setup")]
    [Tooltip("Local axis around which this part rotates")]
    public Vector3 localRotationAxis = Vector3.forward;

    [Tooltip("Minimum angle in degrees (usually 0)")]
    public float minAngle = 0f;

    [Tooltip("Maximum angle in degrees (e.g. 45 for handles, 90 for levers)")]
    public float maxAngle = 45f;

    [Tooltip("Resting/neutral angle when released")]
    public float neutralAngle = 0f;

    [Header("Continuous Rotation (Valves / Faucets)")]
    [Tooltip("If true, rotates continuously past 360 degrees without hard stops")]
    public bool isContinuous = false;

    [Tooltip("Total number of full revolutions for 100% value (when isContinuous is true)")]
    public float maxRevolutions = 3f;

    [Header("Spring Return")]
    [Tooltip("Whether the rotator snaps back to neutralAngle when released")]
    public bool springReturn = true;

    [Tooltip("Speed of spring return in degrees per second")]
    public float returnSpeed = 160f;

    [Header("Tracking Mode")]
    public TrackingMode trackingMode = TrackingMode.HandPositionArc;

    [Tooltip("Sensitivity multiplier for rotation tracking")]
    public float sensitivity = 1.0f;

    [Header("Audio")]
    public AudioSource audioSource;
    public AudioClip rotateSound;
    public AudioClip limitSound;
    public float soundAngleStep = 15f;

    // Internal State
    private Quaternion baseLocalRotation;
    private float currentAngle = 0f;
    private float totalContinuousAngle = 0f;
    private Quaternion handStartRot;
    private Vector3 handStartOffset;
    private float angleAtGrab = 0f;
    private float lastSoundAngle = 0f;

    public float CurrentAngle => currentAngle;

    protected override void Awake()
    {
        base.Awake();
        baseLocalRotation = transform.localRotation;
        currentAngle = neutralAngle;
        ApplyRotation(currentAngle);
    }

    private void Update()
    {
        if (!isManipulated && springReturn)
        {
            if (!Mathf.Approximately(currentAngle, neutralAngle))
            {
                float newAngle = Mathf.MoveTowards(currentAngle, neutralAngle, returnSpeed * Time.deltaTime);
                SetAngle(newAngle);
            }
        }
    }

    public override void BeginManipulation(HandGrabSystem hand, bool vrMode)
    {
        base.BeginManipulation(hand, vrMode);

        angleAtGrab = currentAngle;
        handStartRot = hand.transform.rotation;
        handStartOffset = transform.InverseTransformPoint(hand.transform.position);

        // Visual hand gesture
        var handAnim = hand.GetComponentInChildren<HandAnimator>() ?? hand.GetComponentInParent<HandAnimator>();
        if (handAnim != null)
        {
            handAnim.PlayGesture(HandAnimator.Gesture.Fist);
            handAnim.SetGrip(1f);
        }

        // Desktop hand pinning
        if (!isVR && DesktopHandDriver.Instance != null)
        {
            DesktopHandDriver.Instance.SetPinnedTarget(hand, gripPoint != null ? gripPoint : transform);
        }
    }

    public override void UpdateManipulation(HandGrabSystem hand, float dt)
    {
        base.UpdateManipulation(hand, dt);
        if (!isManipulated || hand != currentHand) return;

        float targetAngle = angleAtGrab;

        if (trackingMode == TrackingMode.WristTwist)
        {
            // Calculate relative wrist twist around local axis
            Quaternion deltaRot = Quaternion.Inverse(handStartRot) * hand.transform.rotation;
            Vector3 worldAxis = transform.TransformDirection(localRotationAxis).normalized;
            deltaRot.ToAngleAxis(out float twistAngle, out Vector3 twistAxis);

            float projection = Vector3.Dot(twistAxis, worldAxis);
            if (projection < 0f) twistAngle = -twistAngle;

            targetAngle = angleAtGrab + (twistAngle * sensitivity);
        }
        else // HandPositionArc
        {
            Vector3 worldAxis = transform.TransformDirection(localRotationAxis).normalized;
            Vector3 pivot = transform.position;

            Vector3 startVec = transform.TransformPoint(handStartOffset) - pivot;
            Vector3 currentVec = hand.transform.position - pivot;

            Vector3 projStart = Vector3.ProjectOnPlane(startVec, worldAxis).normalized;
            Vector3 projCurrent = Vector3.ProjectOnPlane(currentVec, worldAxis).normalized;

            if (projStart.sqrMagnitude > 0.001f && projCurrent.sqrMagnitude > 0.001f)
            {
                float deltaAngle = Vector3.SignedAngle(projStart, projCurrent, worldAxis);
                targetAngle = angleAtGrab + (deltaAngle * sensitivity);
            }
        }

        SetAngle(targetAngle);
    }

    public override void EndManipulation(HandGrabSystem hand)
    {
        if (hand != currentHand && currentHand != null) return;

        if (currentHand != null)
        {
            if (!isVR && DesktopHandDriver.Instance != null)
            {
                DesktopHandDriver.Instance.ClearPinnedTarget(currentHand);
            }

            var handAnim = currentHand.GetComponentInChildren<HandAnimator>() ?? currentHand.GetComponentInParent<HandAnimator>();
            if (handAnim != null)
            {
                handAnim.ClearGesture();
                handAnim.SetGrip(0f);
            }
        }

        base.EndManipulation(hand);
    }

    public void SetAngle(float angle)
    {
        if (isContinuous)
        {
            float maxTotal = maxRevolutions * 360f;
            totalContinuousAngle = Mathf.Clamp(angle, 0f, maxTotal);
            currentAngle = totalContinuousAngle % 360f;
            normalizedValue = maxTotal > 0f ? (totalContinuousAngle / maxTotal) : 0f;
        }
        else
        {
            float clamped = Mathf.Clamp(angle, minAngle, maxAngle);
            currentAngle = clamped;
            float range = maxAngle - minAngle;
            normalizedValue = range > 0.001f ? Mathf.Clamp01((currentAngle - minAngle) / range) : 0f;
        }

        ApplyRotation(currentAngle);
        CheckSoundStep();

        onValueChanged.Invoke(normalizedValue);

        if (normalizedValue <= 0.001f) onMinLimitReached.Invoke();
        else if (normalizedValue >= 0.999f) onMaxLimitReached.Invoke();
    }

    private void ApplyRotation(float angle)
    {
        transform.localRotation = baseLocalRotation * Quaternion.AngleAxis(angle, localRotationAxis.normalized);
    }

    private void CheckSoundStep()
    {
        if (audioSource == null || rotateSound == null) return;

        if (Mathf.Abs(currentAngle - lastSoundAngle) >= soundAngleStep)
        {
            audioSource.PlayOneShot(rotateSound);
            lastSoundAngle = currentAngle;
        }
    }

    public override void Interact(GameObject interactor)
    {
        // Tap/click: briefly cycle to max angle and spring back
        if (springReturn)
        {
            SetAngle(maxAngle);
        }
        else
        {
            // Toggle between min and max
            float target = (normalizedValue > 0.5f) ? minAngle : maxAngle;
            SetAngle(target);
        }
    }
}
