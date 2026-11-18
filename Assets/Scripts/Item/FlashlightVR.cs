using UnityEngine;
using UnityEngine.InputSystem;

public class FlashlightVR : MonoBehaviour
{
    [SerializeField] private InputActionReference _XRIButtonDownY;
    [SerializeField] private InputActionReference _XRIButtonDownB;

    [SerializeField] private HintManager _controlHintsL;
    [SerializeField] private HintManager _controlHintsR;

    FlashlightKB _flashlightKB;
    private PlayerVR_GrabObject _grabScript;
    private int _isGrabbed = 0;
    private bool _doOnce = true;

    private bool _isOn;
    private GameObject _diode;
    private GameObject _vfx;

    private void Start()
    {
        _flashlightKB = GetComponent<FlashlightKB>();
        _grabScript = GetComponent<PlayerVR_GrabObject>();

        if (_flashlightKB != null)
        {
            _isOn = _flashlightKB._isOn;
            _diode = _flashlightKB._diode;
            _vfx = _flashlightKB._vfx;
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
                _controlHintsL?.CreateHint(HintConstants.LeftFlashlightHintVR);
                if (_XRIButtonDownB?.action != null) _XRIButtonDownB.action.performed -= ToggleFlashlight;
                if (_XRIButtonDownY?.action != null) _XRIButtonDownY.action.performed += ToggleFlashlight;
            }
            else if (_isGrabbed == 2)
            {
                _controlHintsR?.CreateHint(HintConstants.RightFlashlightHintVR);
                if (_XRIButtonDownY?.action != null) _XRIButtonDownY.action.performed -= ToggleFlashlight;
                if (_XRIButtonDownB?.action != null) _XRIButtonDownB.action.performed += ToggleFlashlight;
            }

            _doOnce = false;
        }
        else
        {
            _controlHintsL?.ClearHint(HintConstants.LeftFlashlightHintVR);
            _controlHintsR?.ClearHint(HintConstants.RightFlashlightHintVR);
            _doOnce = true;
        }
    }



    private void ToggleFlashlight(InputAction.CallbackContext context)
    {
        if (_isGrabbed == 0) return;

        if (_diode != null) _diode.SetActive(!_isOn);
        if (_vfx != null)
        {
            _vfx.SetActive(!_isOn);
        }
        _isOn = !_isOn;
    }
}
