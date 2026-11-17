using UnityEngine;

/// <summary>
/// Modular 1-DOF linear interactable for VR & Desktop.
/// Ideal for cabinet vertical pins, door bolts, sliding latches, drawers, and plungers.
/// Emits normalized values (0.0 to 1.0) and fires events at limits.
/// </summary>
public class MechanicalSlider : MechanicalPart
{
    [Header("Slide Setup")]
    [Tooltip("Local axis along which this part slides")]
    public Vector3 localSlideAxis = Vector3.up;

    [Tooltip("Minimum travel distance in meters (usually 0)")]
    public float minDistance = 0f;

    [Tooltip("Maximum travel distance in meters (e.g. 0.04 for a 4cm pin, 0.4 for a drawer)")]
    public float maxDistance = 0.04f;

    [Tooltip("Resting position when released")]
    public float neutralDistance = 0f;

    [Header("Spring Return")]
    [Tooltip("If true, snaps back to neutralDistance when released. If false, stays in place via friction.")]
    public bool springReturn = false;

    [Tooltip("Speed of spring return in meters per second")]
    public float returnSpeed = 0.2f;

    [Header("Sensitivity")]
    public float sensitivity = 1.0f;

    [Header("Audio")]
    public AudioSource audioSource;
    public AudioClip slideSound;
    public AudioClip limitSound;

    // Internal State
    private Vector3 baseLocalPosition;
    private float currentDistance = 0f;
    private Vector3 handStartPos;
    private float distAtGrab = 0f;

    public float CurrentDistance => currentDistance;

    protected override void Awake()
    {
        base.Awake();
        baseLocalPosition = transform.localPosition;
        currentDistance = neutralDistance;
        ApplyPosition(currentDistance);
    }

    private void Update()
    {
        if (!isManipulated && springReturn)
        {
            if (!Mathf.Approximately(currentDistance, neutralDistance))
            {
                float newDist = Mathf.MoveTowards(currentDistance, neutralDistance, returnSpeed * Time.deltaTime);
                SetDistance(newDist);
            }
        }
    }

    public override void BeginManipulation(HandGrabSystem hand, bool vrMode)
    {
        base.BeginManipulation(hand, vrMode);

        distAtGrab = currentDistance;
        handStartPos = hand.transform.position;

        var handAnim = hand.GetComponentInChildren<HandAnimator>() ?? hand.GetComponentInParent<HandAnimator>();
        if (handAnim != null)
        {
            handAnim.PlayGesture(HandAnimator.Gesture.Pinch);
            handAnim.SetGrip(1f);
        }

        if (!isVR && DesktopHandDriver.Instance != null)
        {
            DesktopHandDriver.Instance.SetPinnedTarget(hand, gripPoint != null ? gripPoint : transform);
        }
    }

    public override void UpdateManipulation(HandGrabSystem hand, float dt)
    {
        base.UpdateManipulation(hand, dt);
        if (!isManipulated || hand != currentHand) return;

        Vector3 worldAxis = transform.TransformDirection(localSlideAxis).normalized;
        Vector3 handDelta = hand.transform.position - handStartPos;

        float pullDist = Vector3.Dot(handDelta, worldAxis) * sensitivity;
        SetDistance(distAtGrab + pullDist);
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

    public void SetDistance(float dist)
    {
        float clamped = Mathf.Clamp(dist, minDistance, maxDistance);
        currentDistance = clamped;

        float range = maxDistance - minDistance;
        normalizedValue = range > 0.0001f ? Mathf.Clamp01((currentDistance - minDistance) / range) : 0f;

        ApplyPosition(currentDistance);

        onValueChanged.Invoke(normalizedValue);

        if (normalizedValue <= 0.001f) onMinLimitReached.Invoke();
        else if (normalizedValue >= 0.999f) onMaxLimitReached.Invoke();
    }

    private void ApplyPosition(float dist)
    {
        transform.localPosition = baseLocalPosition + (localSlideAxis.normalized * dist);
    }

    public override void Interact(GameObject interactor)
    {
        // Toggle between min and max on click
        float target = (normalizedValue > 0.5f) ? minDistance : maxDistance;
        SetDistance(target);

        if (audioSource != null && limitSound != null)
        {
            audioSource.PlayOneShot(limitSound);
        }
    }
}
