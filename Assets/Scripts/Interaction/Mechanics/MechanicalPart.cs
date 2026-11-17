using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Abstract base class for modular physical mechanical parts (handles, knobs, sliders, levers, valves).
/// Provides a universal interface for both VR hand manipulation (HandGrabSystem) and Desktop (KB/M).
/// </summary>
public abstract class MechanicalPart : MonoBehaviour, IInteractable
{
    [Header("Interaction & Physical Reach")]
    [Tooltip("Max reach distance before handle slips from hand")]
    public float breakDistance = 1.2f;

    [Tooltip("Optional grip transform where the hand attaches (defaults to this transform)")]
    public Transform gripPoint;

    [Header("Highlighting")]
    public Outline outline;

    [Header("Events")]
    [Tooltip("Normalized value of this part (0.0 to 1.0)")]
    public UnityEvent<float> onValueChanged = new UnityEvent<float>();
    public UnityEvent onMinLimitReached = new UnityEvent();
    public UnityEvent onMaxLimitReached = new UnityEvent();

    // State
    protected bool isManipulated = false;
    protected HandGrabSystem currentHand = null;
    protected bool isVR = false;
    protected float normalizedValue = 0f;

    public bool IsManipulated => isManipulated;
    public HandGrabSystem CurrentHand => currentHand;
    public float NormalizedValue => normalizedValue;
    public virtual bool CanInteract => true;

    protected virtual void Awake()
    {
        if (gripPoint == null) gripPoint = transform;
        if (outline == null) outline = GetComponent<Outline>() ?? GetComponentInParent<Outline>();

        if (outline != null)
        {
            outline.OutlineMode = Outline.Mode.OutlineAll;
            outline.OutlineColor = new Color(1f, 0.85f, 0.2f, 1f); // Warm gold highlight
            outline.OutlineWidth = 3.0f;
            outline.enabled = false;
        }
    }

    public virtual void SetHighlight(bool highlighted)
    {
        if (outline != null)
        {
            outline.enabled = highlighted;
        }
    }

    public virtual void BeginManipulation(HandGrabSystem hand, bool vrMode)
    {
        isManipulated = true;
        currentHand = hand;
        isVR = vrMode;
        SetHighlight(true);
    }

    public virtual void UpdateManipulation(HandGrabSystem hand, float dt)
    {
        if (!isManipulated || hand != currentHand) return;

        // Auto-release if player walks too far
        Vector3 refPos = gripPoint != null ? gripPoint.position : transform.position;
        if (Vector3.Distance(refPos, hand.transform.position) > breakDistance)
        {
            EndManipulation(hand);
        }
    }

    public virtual void EndManipulation(HandGrabSystem hand)
    {
        if (hand != currentHand && currentHand != null) return;

        isManipulated = false;
        currentHand = null;
        SetHighlight(false);
    }

    public abstract void Interact(GameObject interactor);
}
