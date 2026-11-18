using UnityEngine;
using UnityEngine.InputSystem;

public class LanternVR : MonoBehaviour
{
    [SerializeField] private InputActionReference _XRIButtonDownY;
    [SerializeField] private InputActionReference _XRIButtonDownB;

    [SerializeField] private HintManager _controlHintsL;
    [SerializeField] private HintManager _controlHintsR;

    LanternKB _lanternKB;
    private PlayerVR_GrabObject _grabScript;
    private int _isGrabbed = 0;
    private bool _doOnce = true;

    private bool _isOn;
    private GameObject _diode;

    private void Start()
    {
        _lanternKB = GetComponent<LanternKB>();
        _grabScript = GetComponent<PlayerVR_GrabObject>();

        if (_lanternKB != null)
        {
            _isOn = _lanternKB._isOn;
            _diode = _lanternKB._diode;
        }

        if (_XRIButtonDownY?.action != null) _XRIButtonDownY.action.Enable();
        if (_XRIButtonDownB?.action != null) _XRIButtonDownB.action.Enable();
    }

    private void Update()
    {
        if (_grabScript == null) return;
        _isGrabbed = _grabScript.IsGrabbed;

        if (_isGrabbed != 0)
        {
            if (!_doOnce) return;

            if (_isGrabbed == 1)
            {
                _controlHintsL?.CreateHint(HintConstants.LeftLanternHintVR);
                if (_XRIButtonDownB?.action != null) _XRIButtonDownB.action.performed -= ToggleLantern;
                if (_XRIButtonDownY?.action != null) _XRIButtonDownY.action.performed += ToggleLantern;
            }
            else if (_isGrabbed == 2)
            {
                _controlHintsR?.CreateHint(HintConstants.RightLanternHintVR);
                if (_XRIButtonDownY?.action != null) _XRIButtonDownY.action.performed -= ToggleLantern;
                if (_XRIButtonDownB?.action != null) _XRIButtonDownB.action.performed += ToggleLantern;  
            }

            _doOnce = false;
        }
        else
        {
            _controlHintsL?.ClearHint(HintConstants.LeftLanternHintVR);
            _controlHintsR?.ClearHint(HintConstants.RightLanternHintVR);
            _doOnce = true;
        }
    }

    private void ToggleLantern(InputAction.CallbackContext context)
    {
        if (_isGrabbed == 0) return;

        if (_diode != null) _diode.SetActive(!_isOn);
        _isOn = !_isOn;
    }
}
