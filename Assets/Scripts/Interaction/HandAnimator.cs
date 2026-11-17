using UnityEngine;

/// <summary>
/// Controls hand animations via Animator clip playback + code-driven finger bone curling.
/// 
/// Architecture:
/// - Animator plays hold clips (holdBag, holdKnife, etc.) via Play() — no parameters needed.
/// - When NOT holding: finger bones are driven by code in LateUpdate:
///   * VR: grip → thumb/middle/ring/pinky curl, trigger → index curl
///   * Gestures override finger pose entirely (point, wave, come, present, headpat, poke)
/// - When HOLDING: the Animator's hold clip drives finger bones, code does not override.
///
/// Replicates old OpenViva's FingerAnimator + HandAnimationSystem with code-driven poses
/// since the new project has no gesture animation clips.
/// </summary>
public class HandAnimator : MonoBehaviour
{
    public enum Gesture
    {
        None,
        Point,
        Wave,
        Come,
        PresentRight,
        PresentLeft,
        Headpat,
        HeadpatScrub,
        Poke,
        ThumbsUp,
        Fist,
        Pinch
    }

    [Header("References")]
    public Animator handAnimator;
    public bool isLeftHand;

    [Header("Finger Bones")]
    [Tooltip("5 fingers × 3 bones = 15. Order: thumb(3), index(3), middle(3), ring(3), pinky(3)")]
    public Transform[] fingerBones = new Transform[15];
    public Transform wrist;

    [Header("Pose Blending")]
    public float poseBlendSpeed = 10f;
    [Tooltip("Max curl angle per joint in degrees")]
    public float maxCurlAngle = 90f;
    [Tooltip("Thumb max curl (less than fingers)")]
    public float thumbMaxCurl = 60f;

    // Current grip/trigger from VR controller (0-1)
    float currentGrip;
    float currentTrigger;
    float targetGrip;
    float targetTrigger;

    // Hold state
    bool isHolding;
    bool isPinchHold;

    // Gesture state
    Gesture currentGesture = Gesture.None;
    float gestureTime;
    float gestureDuration;

    // Rest pose (captured at startup)
    Quaternion[] restPose = new Quaternion[15];
    bool hasRestPose;

    // Smoothed finger curls (0=rest, 1=fully curled)
    float[] currentFingerCurls = new float[5];
    float[] targetFingerCurls = new float[5];

    [Header("Wall Contact Flattening")]
    [Tooltip("How much fingers flatten against a wall (0=normal, 1=fully flat against surface)")]
    [Range(0f, 1f)]
    public float wallContactAmount = 0f;
    float currentWallContact = 0f;
    public float wallFlattenSpeed = 10f;
    [Tooltip("Thumb 1 pitch uncurl angle in degrees (Blender X -0.15 rad = -8.6 deg)")]
    public float thumb1Pitch = -8.6f;
    [Tooltip("Thumb 1 spread/abduction angle in degrees (Blender Z 0.25 rad = 14.3 deg)")]
    public float thumb1Spread = 14.3f;
    [Tooltip("Thumb 3 tip flatten angle in degrees (Blender X -0.1 rad = -5.7 deg)")]
    public float thumb3Pitch = -5.7f;

    [Header("Idle Life / Organic Breathing")]
    [Tooltip("Enable subtle organic breathing & micro-motion when idle (feels alive without importing incompatible FBX clips)")]
    public bool enableIdleLife = true;
    public float idleBreathSpeed = 1.6f;
    [Tooltip("Subtle curl variation (0.05 = ~4.5 degrees gentle breathing flexion)")]
    public float idleBreathAmount = 0.06f;

    void Awake()
    {
        if (wrist == null)
            wrist = transform;
        if (handAnimator == null)
            handAnimator = GetComponent<Animator>();
        AutoFindFingerBones();
    }

    void Start()
    {
        AutoFindFingerBones();
        SaveRestPose();
    }

    void Update()
    {
        // Smooth blend grip/trigger values
        currentGrip = Mathf.MoveTowards(currentGrip, targetGrip, poseBlendSpeed * Time.deltaTime);
        currentTrigger = Mathf.MoveTowards(currentTrigger, targetTrigger, poseBlendSpeed * Time.deltaTime);

        // Advance gesture timer
        if (currentGesture != Gesture.None)
        {
            gestureTime += Time.deltaTime;
            // Auto-expire timed gestures (wave, come)
            if (gestureDuration > 0f && gestureTime >= gestureDuration)
            {
                currentGesture = Gesture.None;
            }
        }

        // Determine target curls for fingers
        if (currentGesture != Gesture.None)
            EvaluateGestureTargets();
        else
            EvaluateGripTriggerTargets();

        // Smoothly interpolate current curls towards targets
        for (int i = 0; i < 5; i++)
        {
            currentFingerCurls[i] = Mathf.MoveTowards(currentFingerCurls[i], targetFingerCurls[i], poseBlendSpeed * Time.deltaTime);
        }

        // Smooth blend wall contact flattening
        currentWallContact = Mathf.MoveTowards(currentWallContact, wallContactAmount, wallFlattenSpeed * Time.deltaTime);
    }

    void LateUpdate()
    {
        if (!hasRestPose || fingerBones == null || fingerBones.Length < 15) return;
        if (fingerBones[0] == null) return;

        // When holding a pinch item, code-drives the delicate pinch pose so fingers are not fully closed
        if (isHolding)
        {
            if (isPinchHold)
            {
                ApplyFingerCurl(0, 0.45f); // thumb
                ApplyFingerCurl(1, 0.50f); // index
                ApplyFingerCurl(2, 0.25f); // middle (relaxed, not closed)
                ApplyFingerCurl(3, 0.20f); // ring (relaxed, not closed)
                ApplyFingerCurl(4, 0.15f); // pinky (relaxed, not closed)
            }
            return;
        }

        bool hasGesture = currentGesture != Gesture.None;
        bool hasVrInput = currentGrip > 0.01f || currentTrigger > 0.01f;
        bool hasWallContact = currentWallContact > 0.005f;

        // Apply programmatic finger curls (VR inputs, gestures, or organic idle breathing)
        if (hasGesture || hasVrInput || enableIdleLife)
        {
            for (int f = 0; f < 5; f++)
            {
                ApplyFingerCurl(f, currentFingerCurls[f]);
            }
        }

        // When contacting a wall, extend & flatten fingers against the wall plane
        if (hasWallContact)
        {
            ApplyWallFlatten(currentWallContact);
        }
    }

    #region Public API

    /// <summary>Set the grip amount (0=open, 1=fist). VR controller input.</summary>
    public void SetGrip(float value)
    {
        targetGrip = Mathf.Clamp01(value);
    }

    /// <summary>Set the trigger/index finger amount (0=extended, 1=curled). VR controller input.</summary>
    public void SetTrigger(float value)
    {
        targetTrigger = Mathf.Clamp01(value);
    }

    /// <summary>Set wall contact amount (0=normal, 1=fully flat against wall).</summary>
    public void SetWallContact(float amount)
    {
        wallContactAmount = Mathf.Clamp01(amount);
    }

    /// <summary>Play the appropriate hold animation for a grabbed item.</summary>
    public void SetHoldPose(GrabbableItem item)
    {
        if (item == null) { ClearHoldPose(); return; }

        isHolding = true;
        isPinchHold = item.IsPinchItem;

        if (handAnimator != null)
        {
            string clipName = GetHoldClipName(item.holdPose);
            if (!string.IsNullOrEmpty(clipName))
                handAnimator.Play(clipName, 0, 0f);
        }
    }

    /// <summary>Return to idle hand pose.</summary>
    public void ClearHoldPose()
    {
        isHolding = false;
        isPinchHold = false;
        currentGesture = Gesture.None;

        if (handAnimator != null)
            handAnimator.Play("handIdle", 0, 0f);
    }

    /// <summary>Start a gesture. Duration=0 means hold until ClearGesture/other gesture.</summary>
    public void PlayGesture(Gesture gesture, float duration = 0f)
    {
        if (isHolding) return; // Can't gesture while holding
        currentGesture = gesture;
        gestureTime = 0f;
        gestureDuration = duration;

        // Play idle animation as base — fingers will be overridden in LateUpdate
        if (handAnimator != null)
            handAnimator.Play("handIdle", 0, 0f);
    }

    /// <summary>Stop current gesture, return to grip/trigger-driven pose.</summary>
    public void ClearGesture()
    {
        currentGesture = Gesture.None;
    }

    /// <summary>Get current gesture.</summary>
    public Gesture CurrentGesture => currentGesture;

    /// <summary>Is the hand currently holding an item?</summary>
    public bool IsHolding => isHolding;

    /// <summary>
    /// Play a momentary pinch pose for a fraction of a second (e.g. for lighting/snuffing a candle),
    /// then auto-release smoothly back to resting pose.
    /// </summary>
    public void PlayPinchMomentary(float duration = 0.35f)
    {
        if (isHolding) return;
        PlayGesture(Gesture.Pinch, duration);
    }

    #endregion

    #region Finger Curling

    /// <summary>Set target finger curls based on VR grip/trigger or organic idle breathing.</summary>
    void EvaluateGripTriggerTargets()
    {
        if (currentGrip > 0.01f || currentTrigger > 0.01f)
        {
            targetFingerCurls[0] = currentGrip * 0.7f;
            targetFingerCurls[1] = currentTrigger;
            targetFingerCurls[2] = currentGrip;
            targetFingerCurls[3] = currentGrip;
            targetFingerCurls[4] = currentGrip;
        }
        else if (enableIdleLife)
        {
            // Organic alive breathing & micro-motion for idle hands
            float t = Time.time * idleBreathSpeed + (isLeftHand ? 0.7f : 0f);
            float[] phaseOffsets = { 0.0f, 0.25f, 0.45f, 0.65f, 0.85f };

            for (int f = 0; f < 5; f++)
            {
                float wave = Mathf.Sin(t + phaseOffsets[f]);
                targetFingerCurls[f] = Mathf.Clamp01(0.02f + (wave * 0.5f + 0.5f) * idleBreathAmount);
            }
        }
        else
        {
            for (int f = 0; f < 5; f++)
            {
                targetFingerCurls[f] = 0f;
            }
        }
    }

    /// <summary>Set target finger curls based on active gesture.</summary>
    void EvaluateGestureTargets()
    {
        switch (currentGesture)
        {
            case Gesture.Point:
            case Gesture.Poke:
                // Index extended (0), others curled (1)
                targetFingerCurls[0] = 0.8f;
                targetFingerCurls[1] = 0f;
                targetFingerCurls[2] = 1f;
                targetFingerCurls[3] = 1f;
                targetFingerCurls[4] = 1f;
                break;

            case Gesture.Wave:
                float wave = Mathf.Sin(gestureTime * 6f) * 0.15f;
                targetFingerCurls[0] = Mathf.Clamp01(0.1f + wave);
                targetFingerCurls[1] = Mathf.Clamp01(0.05f + wave * 0.8f);
                targetFingerCurls[2] = Mathf.Clamp01(0.05f + wave);
                targetFingerCurls[3] = Mathf.Clamp01(0.05f - wave * 0.5f);
                targetFingerCurls[4] = Mathf.Clamp01(0.1f - wave);
                break;

            case Gesture.Come:
                float beck = (Mathf.Sin(gestureTime * 5f) + 1f) * 0.5f;
                targetFingerCurls[0] = 0.3f;
                targetFingerCurls[1] = beck;
                targetFingerCurls[2] = beck * 0.8f;
                targetFingerCurls[3] = 0.6f;
                targetFingerCurls[4] = 0.7f;
                break;

            case Gesture.PresentRight:
            case Gesture.PresentLeft:
                targetFingerCurls[0] = 0.15f;
                targetFingerCurls[1] = 0f;
                targetFingerCurls[2] = 0f;
                targetFingerCurls[3] = 0f;
                targetFingerCurls[4] = 0.05f;
                break;

            case Gesture.Headpat:
                targetFingerCurls[0] = 0.2f;
                targetFingerCurls[1] = 0.25f;
                targetFingerCurls[2] = 0.3f;
                targetFingerCurls[3] = 0.3f;
                targetFingerCurls[4] = 0.35f;
                break;

            case Gesture.HeadpatScrub:
                float scrub = Mathf.Sin(gestureTime * 8f) * 0.1f;
                targetFingerCurls[0] = Mathf.Clamp01(0.25f + scrub);
                targetFingerCurls[1] = Mathf.Clamp01(0.3f + scrub);
                targetFingerCurls[2] = Mathf.Clamp01(0.35f + scrub);
                targetFingerCurls[3] = Mathf.Clamp01(0.35f - scrub);
                targetFingerCurls[4] = Mathf.Clamp01(0.4f - scrub);
                break;

            case Gesture.ThumbsUp:
                targetFingerCurls[0] = 0f;
                targetFingerCurls[1] = 1f;
                targetFingerCurls[2] = 1f;
                targetFingerCurls[3] = 1f;
                targetFingerCurls[4] = 1f;
                break;

            case Gesture.Fist:
                targetFingerCurls[0] = 1f;
                targetFingerCurls[1] = 1f;
                targetFingerCurls[2] = 1f;
                targetFingerCurls[3] = 1f;
                targetFingerCurls[4] = 1f;
                break;

            case Gesture.Pinch:
                // Delicate pinch: thumb & index meet, middle/ring/pinky relaxed open
                targetFingerCurls[0] = 0.45f;
                targetFingerCurls[1] = 0.50f;
                targetFingerCurls[2] = 0.25f;
                targetFingerCurls[3] = 0.20f;
                targetFingerCurls[4] = 0.15f;
                break;
        }
    }

    /// <summary>Apply smoothed curl to one finger (3 joints).</summary>
    void ApplyFingerCurl(int fingerIndex, float curl)
    {
        curl = Mathf.Clamp01(curl);
        float maxAngle = (fingerIndex == 0) ? thumbMaxCurl : maxCurlAngle;

        for (int j = 0; j < 3; j++)
        {
            int boneIndex = fingerIndex * 3 + j;
            if (boneIndex >= fingerBones.Length || fingerBones[boneIndex] == null) continue;

            Quaternion rest = restPose[boneIndex];
            float jointScale = (j == 0) ? 0.7f : 1f;
            Quaternion curled = rest * Quaternion.Euler(maxAngle * jointScale, 0f, 0f);

            fingerBones[boneIndex].localRotation = Quaternion.Slerp(rest, curled, curl);
        }
    }

    /// <summary>Flatten and extend fingers against a wall surface to prevent clipping (matching Blender reference pose).</summary>
    void ApplyWallFlatten(float weight)
    {
        // 1. Index, Middle, Ring, Pinky (fingers 1 to 4):
        // In Blender, 0,0,0 rotation corresponds to the flat rest pose.
        // Slerp from current curl towards the flat rest pose!
        for (int f = 1; f < 5; f++)
        {
            for (int j = 0; j < 3; j++)
            {
                int boneIndex = f * 3 + j;
                if (boneIndex >= fingerBones.Length || fingerBones[boneIndex] == null) continue;

                fingerBones[boneIndex].localRotation = Quaternion.Slerp(fingerBones[boneIndex].localRotation, restPose[boneIndex], weight);
            }
        }

        // 2. Thumb (finger 0) with user's exact Blender reference pose:
        // thumb1_l: X -0.15 rad (-8.6 deg), Z +0.25 rad (+14.3 deg)
        // thumb1_r: X -0.15 rad (-8.6 deg), Z -0.25 rad (-14.3 deg)
        // thumb2: 0, 0, 0
        // thumb3: X -0.1 rad (-5.7 deg)
        if (fingerBones.Length >= 3 && fingerBones[0] != null && fingerBones[1] != null && fingerBones[2] != null)
        {
            float thumbZ = isLeftHand ? thumb1Spread : -thumb1Spread;
            Quaternion flatThumb1 = restPose[0] * Quaternion.Euler(thumb1Pitch, 0f, thumbZ);
            fingerBones[0].localRotation = Quaternion.Slerp(fingerBones[0].localRotation, flatThumb1, weight);

            fingerBones[1].localRotation = Quaternion.Slerp(fingerBones[1].localRotation, restPose[1], weight);

            Quaternion flatThumb3 = restPose[2] * Quaternion.Euler(thumb3Pitch, 0f, 0f);
            fingerBones[2].localRotation = Quaternion.Slerp(fingerBones[2].localRotation, flatThumb3, weight);
        }
    }

    #endregion

    #region Utility

    public void AutoFindFingerBones()
    {
        if (fingerBones == null || fingerBones.Length < 15)
            fingerBones = new Transform[15];

        bool missingAny = false;
        for (int i = 0; i < 15; i++)
        {
            if (fingerBones[i] == null) { missingAny = true; break; }
        }

        if (missingAny)
        {
            Transform searchRoot = wrist != null ? wrist : transform;

            // 1. Try standard character armature convention (e.g. thumb1_l, index2_l)
            string suffix = isLeftHand ? "_l" : "_r";
            string[] fingers = new string[] { "thumb", "index", "middle", "ring", "pinky" };
            int idx = 0;
            for (int f = 0; f < fingers.Length; f++)
            {
                for (int b = 1; b <= 3; b++)
                {
                    string bName = $"{fingers[f]}{b}{suffix}";
                    Transform bone = FindRecursive(searchRoot, bName);
                    if (bone != null)
                        fingerBones[idx] = bone;
                    idx++;
                }
            }

            // 2. Fallback to Unity XR / OpenXR controller armature convention (e.g. L_IndexProximal, L_ThumbDistal)
            bool stillMissing = false;
            for (int i = 0; i < 15; i++)
            {
                if (fingerBones[i] == null) { stillMissing = true; break; }
            }

            if (stillMissing)
            {
                string pfx = isLeftHand ? "L_" : "R_";
                string[] xriFingers = new string[] { "Thumb", "Index", "Middle", "Ring", "Little" };
                string[] stages = new string[] { "Proximal", "Intermediate", "Distal" };

                int xriIdx = 0;
                for (int f = 0; f < xriFingers.Length; f++)
                {
                    for (int s = 0; s < stages.Length; s++)
                    {
                        string bName = $"{pfx}{xriFingers[f]}{stages[s]}";
                        if (xriFingers[f] == "Thumb")
                        {
                            if (s == 1) bName = $"{pfx}ThumbDistal";
                            else if (s == 2) bName = $"{pfx}ThumbTip";
                        }
                        Transform bone = FindRecursive(searchRoot, bName);
                        if (bone != null)
                            fingerBones[xriIdx] = bone;
                        xriIdx++;
                    }
                }
            }
        }
    }

    Transform FindRecursive(Transform root, string targetName)
    {
        if (root.name.Equals(targetName, System.StringComparison.OrdinalIgnoreCase))
            return root;
        foreach (Transform child in root)
        {
            Transform found = FindRecursive(child, targetName);
            if (found != null) return found;
        }
        return null;
    }

    void SaveRestPose()
    {
        if (fingerBones == null || fingerBones.Length < 15) return;
        for (int i = 0; i < Mathf.Min(fingerBones.Length, restPose.Length); i++)
        {
            if (fingerBones[i] != null)
                restPose[i] = fingerBones[i].localRotation;
        }
        hasRestPose = true;
    }

    string GetHoldClipName(GrabbableItem.HoldPose pose)
    {
        return pose switch
        {
            GrabbableItem.HoldPose.Bag => "holdBag",
            GrabbableItem.HoldPose.RubberDucky => "holdRubberDucky",
            GrabbableItem.HoldPose.Peach => "holdPeach",
            GrabbableItem.HoldPose.Strawberry => "holdStrawberry",
            GrabbableItem.HoldPose.Cantaloupe => "holdCantaloupe",
            GrabbableItem.HoldPose.Blueberry => "holdBlueberry",
            GrabbableItem.HoldPose.Wheat => "holdWheat",
            GrabbableItem.HoldPose.Flashlight => "holdFlashlight",
            GrabbableItem.HoldPose.Egg => "holdEgg",
            GrabbableItem.HoldPose.FlourJar => "holdFlourJar",
            GrabbableItem.HoldPose.Knife => "holdKnife",
            GrabbableItem.HoldPose.Lantern => "holdLantern",
            GrabbableItem.HoldPose.MilkCanister => "holdMilkCanister",
            GrabbableItem.HoldPose.MixingBowl => "holdMixingBowl",
            GrabbableItem.HoldPose.MixingSpoon => "holdMixingSpoon",
            GrabbableItem.HoldPose.Mortar => "holdMortar",
            GrabbableItem.HoldPose.Pestle => "holdPestle",
            GrabbableItem.HoldPose.Pot => "holdPot",
            GrabbableItem.HoldPose.Soap => "holdSoap",
            GrabbableItem.HoldPose.Towel => "holdTowel",
            _ => "holdFlashlight"
        };
    }

    #endregion
}
