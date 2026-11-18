#if UNITY_EDITOR || UNITY_ANDROID || UNITY_STANDALONE_WIN

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Gravity;

/// <summary>
/// VR locomotion controller that supplements the XRI locomotion providers.
/// Handles smooth locomotion toggle, snap/smooth turn toggle, movement speed,
/// and OpenViva 0.2.1-parity body collision anchoring.
/// The actual locomotion is handled by XRI's ContinuousMoveProvider and SnapTurnProvider
/// on the XR Origin. This script provides runtime control and settings integration.
/// </summary>
public class PlayerVR_Locomotion : MonoBehaviour
{
    [Header("Locomotion Settings")]
    [Tooltip("Movement speed multiplier applied to the ContinuousMoveProvider")]
    public float moveSpeed = 3.5f;
    [Tooltip("Run speed multiplier (applied when grip + joystick)")]
    public float runMultiplier = 2f;
    [Tooltip("Whether smooth turning is enabled (vs snap turn)")]
    public bool useSmoothTurn = false;
    [Tooltip("Smooth turn speed in degrees/sec")]
    public float smoothTurnSpeed = 60f;
    [Tooltip("Snap turn angle in degrees")]
    public float snapTurnAngle = 45f;

    [Header("VR Body Collision & Leaning (OpenViva 0.2.1 Parity)")]
    [Tooltip("Slim radius for the player CharacterController (matching openviva021 0.09m-0.12m)")]
    public float characterRadius = 0.12f;
    [Tooltip("Prevent CharacterController from dynamically following head into counters and furniture")]
    public bool anchorBodyToOrigin = true;

    [Header("Comfort")]
    [Tooltip("Vignette intensity during movement (0 = off, 1 = full)")]
    [Range(0f, 1f)]
    public float vignetteIntensity = 0f;

    [Header("Input — Optional override for sprint")]
    [SerializeField] InputActionReference leftGripAction;

    [Header("References — auto-found if not set")]
    [Tooltip("Assign the XR Origin's ContinuousMoveProvider if not auto-found")]
    public MonoBehaviour moveProvider;    // ContinuousMoveProvider or DynamicMoveProvider
    [Tooltip("Assign the XR Origin's SnapTurnProvider if not auto-found")]
    public MonoBehaviour snapTurnProvider;
    [Tooltip("Assign the XR Origin's ContinuousTurnProvider if not auto-found")]
    public MonoBehaviour continuousTurnProvider;

    bool isSprinting;

    void Start()
    {
        ApplyBodyCollisionSettings();
    }

    void OnEnable()
    {
        ApplySettings();
        ApplyBodyCollisionSettings();
    }

    void Update()
    {
        // Sprint detection: hold grip while moving
        if (leftGripAction != null && leftGripAction.action != null)
        {
            bool gripHeld = leftGripAction.action.ReadValue<float>() > 0.5f;
            if (gripHeld != isSprinting)
            {
                isSprinting = gripHeld;
                ApplyMoveSpeed();
            }
        }
    }

    /// <summary>
    /// Configure CharacterController, GravityProvider, and XRBodyTransformer to match
    /// OpenViva 0.2.1 behavior (preventing leaning over counters from pushing the player back).
    /// </summary>
    public void ApplyBodyCollisionSettings()
    {
        var cc = GetComponentInParent<CharacterController>() ?? GetComponent<CharacterController>();
        if (cc == null)
        {
            var pvr = GameObject.Find("PlayerVR") ?? GameObject.Find("Player")?.transform.Find("PlayerVR")?.gameObject;
            if (pvr != null) cc = pvr.GetComponent<CharacterController>();
        }

        if (cc != null)
        {
            cc.radius = characterRadius;
            if (anchorBodyToOrigin)
            {
                cc.center = new Vector3(0f, cc.center.y, 0f);
            }
        }

        var grav = GetComponentInChildren<GravityProvider>(true);
        if (grav == null)
        {
            var pvr = GameObject.Find("PlayerVR") ?? GameObject.Find("Player")?.transform.Find("PlayerVR")?.gameObject;
            if (pvr != null) grav = pvr.GetComponentInChildren<GravityProvider>(true);
        }
        if (grav != null && anchorBodyToOrigin)
        {
            grav.updateCharacterControllerCenterEachFrame = false;
        }

        var transformer = GetComponentInChildren<XRBodyTransformer>(true);
        if (transformer == null)
        {
            var pvr = GameObject.Find("PlayerVR") ?? GameObject.Find("Player")?.transform.Find("PlayerVR")?.gameObject;
            if (pvr != null) transformer = pvr.GetComponentInChildren<XRBodyTransformer>(true);
        }
        if (transformer != null && anchorBodyToOrigin)
        {
            transformer.bodyPositionEvaluator = VRFixedBodyPositionEvaluator.SharedInstance;
        }
    }

    /// <summary>
    /// Apply current settings to the XRI locomotion providers.
    /// Call this after changing any settings at runtime.
    /// </summary>
    public void ApplySettings()
    {
        ApplyMoveSpeed();
        ApplyTurnMode();
        ApplyBodyCollisionSettings();
    }

    void ApplyMoveSpeed()
    {
        if (moveProvider == null) return;

        float speed = isSprinting ? moveSpeed * runMultiplier : moveSpeed;

        // Use reflection-free approach: ContinuousMoveProvider has a 'moveSpeed' property
        var prop = moveProvider.GetType().GetProperty("moveSpeed");
        if (prop != null)
            prop.SetValue(moveProvider, speed);
    }

    void ApplyTurnMode()
    {
        if (snapTurnProvider != null)
            snapTurnProvider.enabled = !useSmoothTurn;
        if (continuousTurnProvider != null)
            continuousTurnProvider.enabled = useSmoothTurn;
    }

    /// <summary>
    /// Toggle between smooth and snap turning.
    /// </summary>
    public void ToggleTurnMode()
    {
        useSmoothTurn = !useSmoothTurn;
        ApplyTurnMode();
    }

    /// <summary>
    /// Set movement speed at runtime.
    /// </summary>
    public void SetMoveSpeed(float speed)
    {
        moveSpeed = speed;
        ApplyMoveSpeed();
    }
}

#endif
