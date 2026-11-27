using UnityEngine;
using Viva.Interaction;

public class CharacterInteractable : MonoBehaviour, IItemInteractable, IInteractable
{
    [Header("Selection Visuals")]
    public GameObject selectionArrow;

    [HideInInspector]
    public string characterName;

    // Pointer always interacts with dolls (never grabs them)
    public InteractionType CurrentType => InteractionType.Interact;

    public bool CanInteract => true;

    public void Interact(GameObject interactor)
    {
        if (CharacterSelectionManager.Instance != null)
        {
            CharacterSelectionManager.Instance.SelectCharacter(this);
        }
    }

    public bool ExecuteAction(GameObject interactor, HandSide hand, Transform handTransform)
    {
        if (CharacterSelectionManager.Instance != null)
        {
            CharacterSelectionManager.Instance.SelectCharacter(this);
            return true;
        }
        return false;
    }

    public void OnDropped(HandSide hand)
    {

    }

    public void SetSelectedVisual(bool isSelected)
    {
        if (selectionArrow != null)
        {
            selectionArrow.SetActive(isSelected);
        }
    }

    public void ReceiveCommand(string commandID)
    {
        Debug.Log($"{gameObject.name} received command: {commandID}");

        if (TryGetComponent<CharacterAI>(out var CharacterAI))
        {
            CharacterAI.ReceiveCommand(commandID);
            return;
        }

        // TODO: Route these commands to the character manager
        switch (commandID)
        {
            case "Follow":
                // navAgent.SetDestination(player.transform.position);
                break;
            case "Wave":
                // animator.SetTrigger("Wave");
                break;
        }
    }
}
