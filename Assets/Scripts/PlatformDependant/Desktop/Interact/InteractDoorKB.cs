using System.Collections;
using UnityEngine;

public class InteractDoorKB : MonoBehaviour, IInteractable
{
    protected Player _player;

    [SerializeField] protected Collider _playerLeftHand;
    [SerializeField] protected Collider _playerRightHand;

    private bool _playerInRange = false;
    private bool _isOpen = false;
    private bool _isMoving = false;

    [SerializeField] Animator _doorAnimator;
    protected Outline _outline;

    [SerializeField] protected HintManager _canvas;
    private bool _doOnce = true;

    private AudioSource _audioSource;

    public bool CanInteract => !_isMoving;

    public void Interact(GameObject interactor)
    {
        _playerInRange = true;
        interactDoor();
    }

    protected virtual void Start()
    {
        _player = FindFirstObjectByType<Player>();
        _outline = GetComponent<Outline>();
        _audioSource = GetComponentInChildren<AudioSource>();

        if (_canvas == null)
            _canvas = FindFirstObjectByType<HintManager>();

        FindPlayerHands();
        AssignInputs();
    }

    protected void FindPlayerHands()
    {
        if (_playerLeftHand == null)
        {
            var hL = GameObject.Find("hand_l") ?? GameObject.Find("PhysicsHand_L");
            if (hL != null) _playerLeftHand = hL.GetComponentInChildren<Collider>();
        }
        if (_playerRightHand == null)
        {
            var hR = GameObject.Find("hand_r") ?? GameObject.Find("PhysicsHand_R");
            if (hR != null) _playerRightHand = hR.GetComponentInChildren<Collider>();
        }
    }

    protected virtual void AssignInputs()
    {
        if (_player != null && _player.Controls != null)
        {
            _player.Controls.Viva.DesktopLeftGrab.performed += context => interactDoor();
            _player.Controls.Viva.DesktopRightGrab.performed += context => interactDoor();
        }
    }

    protected void interactDoor()
    {
        if (_playerInRange && !_isMoving)
        {
            _isMoving = true;
            if (_doorAnimator != null)
            {
                if (!_isOpen)
                {
                    _doorAnimator.Play("Opening");
                }
                else
                {
                    _doorAnimator.Play("Closing");
                }
            }

            StartCoroutine(SetIsMoving());
            if (_audioSource != null && !_audioSource.isPlaying) { _audioSource.Play(); }
            _isOpen = !_isOpen;
        }
    }

    private IEnumerator SetIsMoving()
    {
        yield return new WaitForSeconds(1f);
        _isMoving = false;
    }

    protected bool IsPlayer(Collider collider)
    {
        if (collider == null) return false;
        if (collider == _playerLeftHand || collider == _playerRightHand) return true;
        if (collider.CompareTag(TagConstants.Player)) return true;
        string n = collider.name.ToLower();
        if (n.StartsWith("hand_") || n.StartsWith("physicshand_") || n.StartsWith("wrist_")) return true;
        if (collider.GetComponentInParent<CharacterController>() != null) return true;
        return false;
    }

    protected virtual void OnTriggerEnter(Collider collider)
    {
        if (IsPlayer(collider))
        {
            _playerInRange = true;
            if (_outline != null) _outline.enabled = true;
            if (_doOnce && _canvas != null)
            {
                _canvas.CreateHint(HintConstants.InteractHint);
                _doOnce = false;
            }
        }
    }

    protected virtual void OnTriggerExit(Collider collider)
    {
        if (IsPlayer(collider))
        {
            _playerInRange = false;
            if (_outline != null) _outline.enabled = false;
            if (_canvas != null) _canvas.ClearHint(HintConstants.InteractHint);
            _doOnce = true;
        }
    }
}
