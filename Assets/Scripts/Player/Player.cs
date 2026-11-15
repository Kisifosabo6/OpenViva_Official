using UnityEngine;

public class Player : MonoBehaviour
{
    private PlayerControls _controls;
    public PlayerControls Controls
    {
        get
        {
            if (_controls == null)
            {
                _controls = new PlayerControls();
                _controls.Enable();
            }
            return _controls;
        }
        private set => _controls = value;
    }

    private void Awake()
    {
        if (_controls == null)
        {
            _controls = new PlayerControls();
        }
    }

    private void OnEnable()
    {
        Controls.Enable();
    }

    private void OnDisable()
    {
        _controls?.Disable();
    }
}
