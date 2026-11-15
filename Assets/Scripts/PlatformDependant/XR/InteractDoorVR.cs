using UnityEngine;
using UnityEngine.InputSystem;

public class InteractDoorVR : InteractDoorKB
{
    [SerializeField] private InputActionReference _XRILeftSelect;
    [SerializeField] private InputActionReference _XRIRightSelect;

    protected override void Start()
    {
        base.Start();
    }

    protected override void AssignInputs()
    {
        _XRILeftSelect.action.performed += context => interactDoor();
        _XRIRightSelect.action.performed += context => interactDoor();
    }

    protected override void OnTriggerEnter(Collider collider)
    {
        if (!IsPlayer(collider)) { return; }

        base.OnTriggerEnter(collider);
        if (FloatingCanvas.Instance != null)
            FloatingCanvas.Instance.WarpToObject(gameObject.GetComponent<Collider>().bounds.center);
    }

    protected override void OnTriggerExit(Collider collider)
    {
        if (!IsPlayer(collider)) { return; }

        base.OnTriggerExit(collider);
        if (FloatingCanvas.Instance != null)
            FloatingCanvas.Instance.WarpToOrigin();
    }
}
