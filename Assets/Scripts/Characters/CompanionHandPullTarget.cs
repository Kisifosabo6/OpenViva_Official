using UnityEngine;

/// <summary>
/// Attached to companion hands to allow player hands (or debug controls)
/// to hold and lead the companion by the hand.
/// </summary>
public class CompanionHandPullTarget : MonoBehaviour
{
    [SerializeField] private CompanionPhysicalReaction reaction;
    [SerializeField] private bool isLeftHand;

    private Transform currentPullerTransform = null;

    public void Initialize(CompanionPhysicalReaction physicalReaction, bool isLeft)
    {
        reaction = physicalReaction;
        isLeftHand = isLeft;
    }

    /// <summary>
    /// Starts pulling this hand towards a follower/puller transform.
    /// </summary>
    public void StartPull(Transform puller)
    {
        currentPullerTransform = puller;
        if (reaction != null && currentPullerTransform != null)
        {
            reaction.BeginPull(isLeftHand, currentPullerTransform.position);
        }
    }

    /// <summary>
    /// Stops pulling this hand.
    /// </summary>
    public void StopPull()
    {
        currentPullerTransform = null;
        if (reaction != null)
        {
            reaction.EndPull();
        }
    }

    private void Update()
    {
        if (currentPullerTransform != null && reaction != null)
        {
            reaction.UpdatePull(currentPullerTransform.position);
        }
    }
}
