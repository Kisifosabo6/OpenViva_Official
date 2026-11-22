using UnityEngine;

[DefaultExecutionOrder(50)]
public class HandAnimationTester : MonoBehaviour
{
    [Header("Hand Animators")]
    public HandAnimator leftHandAnimator;
    public HandAnimator rightHandAnimator;

    [Header("Wrist Transforms")]
    public Transform leftWrist;
    public Transform rightWrist;

    [Header("Camera & View")]
    public Camera previewCamera;
    public Vector3 leftRestOffset = new Vector3(-0.16f, -0.18f, 0.48f);
    public Vector3 rightRestOffset = new Vector3(0.16f, -0.18f, 0.48f);
    public Vector3 leftRestEuler = new Vector3(7.2f, -106.1f, -55.03f);
    public Vector3 rightRestEuler = new Vector3(355.1f, -254.1f, -314.97f);

    public enum HandTarget
    {
        Both,
        LeftOnly,
        RightOnly
    }

    public enum ViewPreset
    {
        FirstPerson,
        LeftHandCloseUp,
        RightHandCloseUp,
        FrontFacing,
        FreeOrbit
    }

    [Header("State")]
    public HandTarget activeTarget = HandTarget.Both;
    public ViewPreset currentView = ViewPreset.FirstPerson;
    public string activePoseName = "Idle (Open)";
    public float gripAmount = 0f;
    public float triggerAmount = 0f;
    public bool manualAnalogControl = false;

    // Orbit camera controls
    private float _orbitYaw = 0f;
    private float _orbitPitch = 0f;
    private float _orbitDistance = 0.5f;
    private Vector3 _orbitTarget = new Vector3(0f, -0.15f, 0.45f);
    private Vector3 _lastMousePos;

    private void Start()
    {
        if (previewCamera == null)
            previewCamera = Camera.main ?? FindFirstObjectByType<Camera>();

        SetViewPreset(ViewPreset.FirstPerson);
        SetGesture(HandAnimator.Gesture.None, "Idle (Open)");
    }

    private void Update()
    {
        HandleKeyboardInput();
        HandleMouseOrbit();
    }

    private void LateUpdate()
    {
        if (previewCamera == null) return;

        // Position wrists relative to camera in first person or orbit view
        if (leftWrist != null)
        {
            leftWrist.position = previewCamera.transform.TransformPoint(leftRestOffset);
            leftWrist.rotation = previewCamera.transform.rotation * Quaternion.Euler(leftRestEuler);
        }

        if (rightWrist != null)
        {
            rightWrist.position = previewCamera.transform.TransformPoint(rightRestOffset);
            rightWrist.rotation = previewCamera.transform.rotation * Quaternion.Euler(rightRestEuler);
        }
    }

    private void HandleKeyboardInput()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
            SetGesture(HandAnimator.Gesture.None, "Idle (Open)");
        else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
            SetGesture(HandAnimator.Gesture.Point, "Pointing");
        else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3))
            SetGesture(HandAnimator.Gesture.ThumbsUp, "Thumbs Up");
        else if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4))
            SetGesture(HandAnimator.Gesture.Fist, "Fist");
        else if (Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.Keypad5))
            SetGesture(HandAnimator.Gesture.Pinch, "Pinch");
        else if (Input.GetKeyDown(KeyCode.Alpha6) || Input.GetKeyDown(KeyCode.Keypad6))
            SetGesture(HandAnimator.Gesture.Wave, "Wave");
        else if (Input.GetKeyDown(KeyCode.Alpha7) || Input.GetKeyDown(KeyCode.Keypad7))
            SetGesture(HandAnimator.Gesture.Come, "Come / Beckon");
        else if (Input.GetKeyDown(KeyCode.Alpha8) || Input.GetKeyDown(KeyCode.Keypad8))
            SetGesture(HandAnimator.Gesture.Headpat, "Headpat");
        else if (Input.GetKeyDown(KeyCode.Space))
            SetViewPreset(ViewPreset.FirstPerson);
    }

    private void HandleMouseOrbit()
    {
        // Mouse wheel zoom
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.001f)
        {
            currentView = ViewPreset.FreeOrbit;
            _orbitDistance = Mathf.Clamp(_orbitDistance - scroll * 0.4f, 0.15f, 1.2f);
            ApplyOrbitCamera();
        }

        // Mouse drag rotate / orbit
        if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
        {
            _lastMousePos = Input.mousePosition;
        }
        else if (Input.GetMouseButton(0) || Input.GetMouseButton(1))
        {
            Vector3 delta = Input.mousePosition - _lastMousePos;
            _lastMousePos = Input.mousePosition;

            // Only orbit if outside the GUI windows (left window width = 310, right window width = 230)
            if (Input.mousePosition.x > 320 && Input.mousePosition.x < Screen.width - 240)
            {
                currentView = ViewPreset.FreeOrbit;
                _orbitYaw += delta.x * 0.4f;
                _orbitPitch = Mathf.Clamp(_orbitPitch - delta.y * 0.4f, -70f, 70f);
                ApplyOrbitCamera();
            }
        }
    }

    public void SetViewPreset(ViewPreset preset)
    {
        currentView = preset;
        switch (preset)
        {
            case ViewPreset.FirstPerson:
                _orbitYaw = 0f;
                _orbitPitch = 0f;
                _orbitDistance = 0.45f;
                _orbitTarget = new Vector3(0f, -0.15f, 0.45f);
                if (previewCamera != null)
                {
                    previewCamera.transform.position = Vector3.zero;
                    previewCamera.transform.rotation = Quaternion.identity;
                }
                break;

            case ViewPreset.LeftHandCloseUp:
                _orbitYaw = 25f;
                _orbitPitch = 15f;
                _orbitDistance = 0.28f;
                _orbitTarget = leftRestOffset;
                ApplyOrbitCamera();
                break;

            case ViewPreset.RightHandCloseUp:
                _orbitYaw = -25f;
                _orbitPitch = 15f;
                _orbitDistance = 0.28f;
                _orbitTarget = rightRestOffset;
                ApplyOrbitCamera();
                break;

            case ViewPreset.FrontFacing:
                _orbitYaw = 180f;
                _orbitPitch = 0f;
                _orbitDistance = 0.55f;
                _orbitTarget = new Vector3(0f, -0.15f, 0.45f);
                ApplyOrbitCamera();
                break;
        }
    }

    private void ApplyOrbitCamera()
    {
        if (previewCamera == null) return;
        Quaternion rot = Quaternion.Euler(_orbitPitch, _orbitYaw, 0f);
        Vector3 pos = _orbitTarget - rot * Vector3.forward * _orbitDistance;
        previewCamera.transform.position = pos;
        previewCamera.transform.LookAt(_orbitTarget);
    }

    public void SetGesture(HandAnimator.Gesture gesture, string name)
    {
        manualAnalogControl = false;
        activePoseName = name;

        if (activeTarget == HandTarget.Both || activeTarget == HandTarget.LeftOnly)
        {
            if (leftHandAnimator != null)
            {
                leftHandAnimator.ClearHoldPose();
                if (gesture != HandAnimator.Gesture.None)
                    leftHandAnimator.PlayGesture(gesture);
            }
        }

        if (activeTarget == HandTarget.Both || activeTarget == HandTarget.RightOnly)
        {
            if (rightHandAnimator != null)
            {
                rightHandAnimator.ClearHoldPose();
                if (gesture != HandAnimator.Gesture.None)
                    rightHandAnimator.PlayGesture(gesture);
            }
        }
    }

    public void SetHoldPose(string clipName, string displayName)
    {
        manualAnalogControl = false;
        activePoseName = $"Hold: {displayName}";

        if (activeTarget == HandTarget.Both || activeTarget == HandTarget.LeftOnly)
        {
            if (leftHandAnimator != null)
                leftHandAnimator.PlayHoldClip(clipName);
        }

        if (activeTarget == HandTarget.Both || activeTarget == HandTarget.RightOnly)
        {
            if (rightHandAnimator != null)
                rightHandAnimator.PlayHoldClip(clipName);
        }
    }

    public void ApplyAnalogCurl()
    {
        manualAnalogControl = true;
        activePoseName = $"Analog (Grip: {gripAmount:F2}, Trig: {triggerAmount:F2})";

        if (activeTarget == HandTarget.Both || activeTarget == HandTarget.LeftOnly)
        {
            if (leftHandAnimator != null)
            {
                leftHandAnimator.ClearGesture();
                leftHandAnimator.SetGrip(gripAmount);
                leftHandAnimator.SetTrigger(triggerAmount);
            }
        }

        if (activeTarget == HandTarget.Both || activeTarget == HandTarget.RightOnly)
        {
            if (rightHandAnimator != null)
            {
                rightHandAnimator.ClearGesture();
                rightHandAnimator.SetGrip(gripAmount);
                rightHandAnimator.SetTrigger(triggerAmount);
            }
        }
    }

    private void OnGUI()
    {
        GUI.skin.box.fontSize = 11;
        GUI.skin.button.fontSize = 12;

        // --- Left Panel: Gesture & Hold Controls ---
        GUILayout.BeginArea(new Rect(15, 15, 290, 560), "Hand Animation Tester", GUI.skin.window);
        GUILayout.Space(4);

        // Hand Target Selector
        GUILayout.BeginHorizontal();
        GUILayout.Label("Target:", GUILayout.Width(45));
        if (GUILayout.Toggle(activeTarget == HandTarget.Both, "Both", GUI.skin.button)) activeTarget = HandTarget.Both;
        if (GUILayout.Toggle(activeTarget == HandTarget.LeftOnly, "Left", GUI.skin.button)) activeTarget = HandTarget.LeftOnly;
        if (GUILayout.Toggle(activeTarget == HandTarget.RightOnly, "Right", GUI.skin.button)) activeTarget = HandTarget.RightOnly;
        GUILayout.EndHorizontal();

        GUILayout.Space(6);
        GUILayout.Label($"<b>Active Pose:</b> <color=yellow>{activePoseName}</color>");
        GUILayout.Space(6);

        Color origColor = GUI.backgroundColor;

        GUILayout.Label("<b>Core VR Gestures:</b>");
        DrawGestureBtn("[1] Idle / Relaxed", HandAnimator.Gesture.None, "Idle (Open)");
        DrawGestureBtn("[2] Point (Index Extended)", HandAnimator.Gesture.Point, "Pointing");
        DrawGestureBtn("[3] Thumbs Up", HandAnimator.Gesture.ThumbsUp, "Thumbs Up");
        DrawGestureBtn("[4] Fist", HandAnimator.Gesture.Fist, "Fist");

        GUILayout.Space(4);
        GUILayout.Label("<b>Additional Gestures:</b>");
        DrawGestureBtn("[5] Delicate Pinch", HandAnimator.Gesture.Pinch, "Pinch");
        DrawGestureBtn("[6] Hand Wave", HandAnimator.Gesture.Wave, "Wave");
        DrawGestureBtn("[7] Come / Beckon", HandAnimator.Gesture.Come, "Come / Beckon");
        DrawGestureBtn("[8] Headpat Palm", HandAnimator.Gesture.Headpat, "Headpat");

        GUILayout.Space(6);
        GUILayout.Label("<b>Item Hold Animations:</b>");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Knife", GUILayout.Height(26))) SetHoldPose("holdKnife", "Knife");
        if (GUILayout.Button("Bag", GUILayout.Height(26))) SetHoldPose("holdBag", "Bag");
        if (GUILayout.Button("Lantern", GUILayout.Height(26))) SetHoldPose("holdLantern", "Lantern");
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Flashlight", GUILayout.Height(26))) SetHoldPose("holdFlashlight", "Flashlight");
        if (GUILayout.Button("Flour Jar", GUILayout.Height(26))) SetHoldPose("holdFlourJar", "Flour Jar");
        if (GUILayout.Button("Egg", GUILayout.Height(26))) SetHoldPose("holdEgg", "Egg");
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Peach", GUILayout.Height(26))) SetHoldPose("holdPeach", "Peach");
        if (GUILayout.Button("Bread", GUILayout.Height(26))) SetHoldPose("holdBread", "Bread");
        if (GUILayout.Button("Pot", GUILayout.Height(26))) SetHoldPose("holdPot", "Pot");
        GUILayout.EndHorizontal();

        GUILayout.Space(6);
        GUILayout.Label("<b>Analog VR Controller Simulation:</b>");
        GUILayout.BeginHorizontal();
        GUILayout.Label($"Grip: {gripAmount:F2}", GUILayout.Width(75));
        float newGrip = GUILayout.HorizontalSlider(gripAmount, 0f, 1f);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label($"Trigger: {triggerAmount:F2}", GUILayout.Width(75));
        float newTrigger = GUILayout.HorizontalSlider(triggerAmount, 0f, 1f);
        GUILayout.EndHorizontal();

        if (Mathf.Abs(newGrip - gripAmount) > 0.005f || Mathf.Abs(newTrigger - triggerAmount) > 0.005f)
        {
            gripAmount = newGrip;
            triggerAmount = newTrigger;
            ApplyAnalogCurl();
        }

        GUILayout.EndArea();

        // --- Right Panel: View & Inspection Controls ---
        GUILayout.BeginArea(new Rect(Screen.width - 230, 15, 215, 270), "Camera & View", GUI.skin.window);
        GUILayout.Space(4);

        if (GUILayout.Button("First-Person (Default)", GUILayout.Height(30))) SetViewPreset(ViewPreset.FirstPerson);
        if (GUILayout.Button("Left Hand Close-Up", GUILayout.Height(28))) SetViewPreset(ViewPreset.LeftHandCloseUp);
        if (GUILayout.Button("Right Hand Close-Up", GUILayout.Height(28))) SetViewPreset(ViewPreset.RightHandCloseUp);
        if (GUILayout.Button("Front View (Palms)", GUILayout.Height(28))) SetViewPreset(ViewPreset.FrontFacing);

        GUILayout.Space(8);
        GUILayout.Label("<b>Controls:</b>");
        GUILayout.Label("• Drag mouse: Orbit camera\n• Scroll wheel: Zoom\n• Spacebar: Reset view\n• 1-8 keys: Switch pose");

        GUILayout.EndArea();
    }

    private void DrawGestureBtn(string label, HandAnimator.Gesture gesture, string name)
    {
        Color orig = GUI.backgroundColor;
        if (activePoseName == name && !manualAnalogControl)
            GUI.backgroundColor = new Color(0.4f, 0.9f, 0.4f);

        if (GUILayout.Button(label, GUILayout.Height(26)))
            SetGesture(gesture, name);

        GUI.backgroundColor = orig;
    }
}
