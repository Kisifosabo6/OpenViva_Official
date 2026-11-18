#if UNITY_EDITOR || UNITY_ANDROID || UNITY_STANDALONE_WIN

using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;

/// <summary>
/// Evaluates the user's body position grounded at the XR Origin's floor anchor (Vector3.zero local),
/// matching OpenViva 0.2.1's rooted player capsule behavior.
/// 
/// In standard room-scale VR, when a player leans their head forward to look into a drawer or reach
/// over a countertop, their feet remain on the floor. The default UnderCameraBodyPositionEvaluator
/// projects the head straight down, driving the entire 1.5m-tall CharacterController capsule into
/// waist-height furniture, which causes PhysX to forcefully push the camera and character backwards.
/// 
/// This evaluator keeps the CharacterController anchored at the player's base so leaning and reaching
/// never push the camera or character backwards.
/// </summary>
public class VRFixedBodyPositionEvaluator : ScriptableObject, IXRBodyPositionEvaluator
{
    private static VRFixedBodyPositionEvaluator _sharedInstance;
    public static VRFixedBodyPositionEvaluator SharedInstance
    {
        get
        {
            if (_sharedInstance == null)
            {
                _sharedInstance = ScriptableObject.CreateInstance<VRFixedBodyPositionEvaluator>();
                _sharedInstance.name = "VRFixedBodyPositionEvaluator_Shared";
            }
            return _sharedInstance;
        }
    }

    /// <inheritdoc/>
    public Vector3 GetBodyGroundLocalPosition(XROrigin xrOrigin)
    {
        // Ground position is fixed at (0, 0, 0) relative to XR Origin
        return Vector3.zero;
    }

    /// <inheritdoc/>
    public Vector3 GetBodyGroundWorldPosition(XROrigin xrOrigin)
    {
        return xrOrigin != null && xrOrigin.Origin != null ? xrOrigin.Origin.transform.position : Vector3.zero;
    }
}

#endif
