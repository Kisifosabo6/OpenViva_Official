// OpenViva Fork: Refined PauseMenu page bone alignment, dynamic canvas raycaster registration, and desktop book camera offsets.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Management;

public class PauseMenu : MonoBehaviour
{
    [Header("Book Orientation (Desktop)")]
    [Tooltip("If true, positions book closer to camera at scaled size so hands do not obstruct button raycasts.")]
    [SerializeField] private bool useCloserPlacement = false;
    [SerializeField] private Vector3 closerDesktopCameraOffset = new(0f, -0.30f, 0.442f);
    [SerializeField] private Vector3 closerDesktopCameraEuler = new(0f, 180f, 0f);
    [SerializeField] private float closerDesktopScale = 0.2625f;
    [SerializeField] private Vector3 leftPageLocalOffset = Vector3.zero;
    [SerializeField] private Vector3 leftPageLocalEuler = new(180f, 90f, 90f);

    [Header("Legacy Handle Orientation")]
    [SerializeField] private Vector3 rotationOffset = new(45f, 180f, 0f);
    [SerializeField] private Vector3 desktopPositionOffset = new(0f, -0.85f, 0f);
    [SerializeField] private float desktopScale = 0.75f;
    [SerializeField] private GameObject menuHandleKB;
    [SerializeField] private GameObject menuHandleVR;

    [Header("Pages Settings")]
    [SerializeField] private GameObject rootPage;
    [SerializeField] private GameObject bookMesh;
    [SerializeField] private GameObject LeftPage;
    [SerializeField] private GameObject RightPage;
    [SerializeField] private Transform pageBoneL;
    [SerializeField] private Transform pageBoneR;

    [Header("Animation Settings")]
    [SerializeField] private Animator bookAnimator;
    [Tooltip("Name of the animation state that opens the book")]
    [SerializeField] private string openAnimationStateName = "OpenBook";
    [Tooltip("Name of the animation state that closes the book")]
    [SerializeField] private string closeAnimationStateName = "CloseBook";

    [Header("Animation Clip Timing")]
    [Tooltip("Extra seconds added to the clip length – useful if you have exit-time transitions")]
    [SerializeField] private float extraWaitSeconds = 0.1f;
    [SerializeField] private float _openClipLength = 0.6f;
    [SerializeField] private float _closeClipLength = 1.2f;

    [SerializeField] private List<GameObject> leftPages = new();
    [SerializeField] private List<GameObject> rightPages = new();

    private Player _player;

    [SerializeField] private BookSFXController _audioController;

    private void Start()
    {
        _player = FindFirstObjectByType<Player>();

        if (TryGetComponent(out Animator foundAnimator))
        {
            bookAnimator = foundAnimator;
        }

        AttachPagesToBones();

        rootPage.SetActive(false);
        bookMesh.SetActive(false);
        LeftPage.SetActive(false);
        RightPage.SetActive(false);

        leftPages = GetDirectChildren(LeftPage);
        rightPages = GetDirectChildren(RightPage);

        if (Globals.isDesktopMode)
        {
            EnsureXRDisabled();
        }

        SetupControlsMenuWiring();
        AssignInputs();
        EnsureInputModuleActive();
        ConfigurePageEventCameras();
    }

    private void SetupControlsMenuWiring()
    {
        Transform buttons = null;
        foreach (var b in GetComponentsInChildren<Transform>(true))
        {
            if (b.name == "buttons" && b.parent != null && b.parent.name == "root")
            {
                buttons = b;
                break;
            }
        }

        if (buttons != null)
        {
            var controlsBtnTrans = buttons.Find("Controls");
            if (controlsBtnTrans != null)
            {
                controlsBtnTrans.gameObject.SetActive(true);
                var btn = controlsBtnTrans.GetComponent<Button>();
                if (btn != null)
                {
                    btn.onClick.RemoveAllListeners();
                    btn.onClick.AddListener(OpenControlsMenu);
                }
            }
        }

        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            if ((t.name == "kbControls" || t.name == "vrControls") && t.parent != null && t.parent.name == "UI_Main")
            {
                var backBtnTrans = t.Find("Back");
                if (backBtnTrans != null)
                {
                    var btn = backBtnTrans.GetComponent<Button>();
                    if (btn != null)
                    {
                        btn.onClick.RemoveAllListeners();
                        btn.onClick.AddListener(CloseControlsMenu);
                    }
                }
            }

            if (t.name == "kbControls" && t.parent != null && t.parent.name == "UI_Main")
            {
                var switchBtnTrans = t.Find("Switch to VR");
                if (switchBtnTrans != null)
                {
                    var btn = switchBtnTrans.GetComponent<Button>();
                    if (btn != null)
                    {
                        btn.onClick.RemoveAllListeners();
                        btn.onClick.AddListener(clickSwitchControlScheme);
                    }
                }
            }

            if (t.name == "vrControls" && t.parent != null && t.parent.name == "UI_Main")
            {
                var switchBtnTrans = t.Find("Switch to Keyboard");
                if (switchBtnTrans != null)
                {
                    var btn = switchBtnTrans.GetComponent<Button>();
                    if (btn != null)
                    {
                        btn.onClick.RemoveAllListeners();
                        btn.onClick.AddListener(clickSwitchControlScheme);
                    }
                }
            }
        }
    }

    private void AssignInputs()
    {
        if (_player != null && _player.Controls != null)
        {
            _player.Controls.Viva.Pause.performed += ctx => TogglePauseMenu();
            _player.Controls.Viva.ChangeInputType.performed += ctx => clickSwitchControlScheme();
        }
    }

    private void OnDestroy()
    {
        if (_player != null && _player.Controls != null)
        {
            _player.Controls.Viva.Pause.performed -= ctx => TogglePauseMenu();
            _player.Controls.Viva.ChangeInputType.performed -= ctx => clickSwitchControlScheme();
        }
    }

    public void TogglePauseMenu()
    {
        if (!Globals.allowMenuOpen)
        {
            return;
        }

        if (!Globals.isMenuOpen)
        {
            OnBeginPauseInput();
        }
        else
        {
            OnExitPauseInput();
        }
    }

    private void OnBeginPauseInput()
    {
        StartOpenSequence();
        OrientPauseMenuToPlayer();
        Globals.isMenuOpen = true;
        EnsureInputModuleActive();
        ConfigurePageEventCameras();

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (!Globals.isDesktopMode)
        {
            PlayerManager.Instance.VRLeftInteractor.enableFarCasting = true;
            PlayerManager.Instance.VRRightInteractor.enableFarCasting = true;
        }
    }

    private Camera GetActiveEventCamera()
    {
        if (Globals.isDesktopMode)
        {
            if (menuHandleKB != null)
            {
                var cam = menuHandleKB.GetComponentInParent<Camera>();
                if (cam != null) return cam;
            }

            var desktopCamGo = GameObject.Find("Main Camera KB");
            if (desktopCamGo != null)
            {
                var cam = desktopCamGo.GetComponent<Camera>();
                if (cam != null) return cam;
            }
        }
        else
        {
            if (menuHandleVR != null)
            {
                var cam = menuHandleVR.GetComponentInParent<Camera>();
                if (cam != null && cam.isActiveAndEnabled) return cam;
            }

            var vrCamGo = GameObject.Find("Player_Objects/Player/PlayerVR/Camera Offset/Main Camera");
            if (vrCamGo != null)
            {
                var cam = vrCamGo.GetComponent<Camera>();
                if (cam != null && cam.isActiveAndEnabled) return cam;
            }
        }

        if (_player != null)
        {
            var cams = _player.GetComponentsInChildren<Camera>(false);
            foreach (var cam in cams)
            {
                if (cam.isActiveAndEnabled && cam.name != "NoPostCamVR" && cam.name != "NoPostCamDesktop")
                {
                    return cam;
                }
            }
        }

        foreach (var c in Camera.allCameras)
        {
            if (c.isActiveAndEnabled && c.CompareTag("MainCamera"))
            {
                return c;
            }
        }

        return Camera.main;
    }

    private void ConfigurePageEventCameras()
    {
        Camera cam = GetActiveEventCamera();
        if (cam == null) return;

        void ApplyToCanvases(GameObject go)
        {
            if (go == null) return;
            foreach (var canvas in go.GetComponentsInChildren<Canvas>(true))
            {
                if (canvas.renderMode == RenderMode.WorldSpace)
                {
                    canvas.worldCamera = cam;
                }
            }
            foreach (var raycaster in go.GetComponentsInChildren<GraphicRaycaster>(true))
            {
                raycaster.ignoreReversedGraphics = false;
                raycaster.blockingObjects = GraphicRaycaster.BlockingObjects.None;
            }
        }

        ApplyToCanvases(gameObject);
        if (rootPage != null) ApplyToCanvases(rootPage);
        if (LeftPage != null) ApplyToCanvases(LeftPage);
        if (RightPage != null) ApplyToCanvases(RightPage);
    }

    private void EnsureInputModuleActive()
    {
        var es = EventSystem.current != null ? EventSystem.current : FindFirstObjectByType<EventSystem>();
        if (es == null) return;
        EventSystem.current = es;

        if (Globals.isDesktopMode)
        {
            var ism = es.GetComponent<InputSystemUIInputModule>();
            if (ism == null)
            {
                ism = es.gameObject.AddComponent<InputSystemUIInputModule>();
            }
            if (!ism.enabled)
            {
                ism.enabled = true;
            }

            var sim = es.GetComponent<StandaloneInputModule>();
            if (sim != null && sim.enabled)
            {
                sim.enabled = false;
            }
        }
    }

    private void Update()
    {
        if (UnityEngine.InputSystem.Keyboard.current != null &&
            UnityEngine.InputSystem.Keyboard.current.f1Key.wasPressedThisFrame)
        {
            clickSwitchControlScheme();
        }

        if (Globals.isMenuOpen && Globals.isDesktopMode)
        {
            EnsureInputModuleActive();
            if (LeftPage != null && LeftPage.activeSelf)
            {
                var c = LeftPage.GetComponent<Canvas>();
                if (c != null && c.worldCamera == null)
                {
                    ConfigurePageEventCameras();
                }
            }
        }
    }

    private void OnExitPauseInput()
    {
        StartCloseSequence();
        Globals.isMenuOpen = false;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        if (!Globals.isDesktopMode)
        {
            PlayerManager.Instance.VRLeftInteractor.enableFarCasting = false;
            PlayerManager.Instance.VRRightInteractor.enableFarCasting = false;
        }
    }

    public void StartOpenSequence()
    {
        StopAllCoroutines();
        StartCoroutine(OpenSequenceRoutine());
    }

    public void StartCloseSequence()
    {
        StopAllCoroutines();
        StartCoroutine(CloseSequenceRoutine());
    }

    private IEnumerator OpenSequenceRoutine()
    {
        if (rootPage != null && LeftPage != null && RightPage != null)
        {
            StartCoroutine(leftPages.DeactivateListAsync(batchSize: 3));
            StartCoroutine(rightPages.DeactivateListAsync(batchSize: 3));
            LeftPage.SetActive(false);
            RightPage.SetActive(false);
        }

        if (bookMesh != null)
        {
            bookMesh.SetActive(true);
        }

        PlayBookAnimation(openAnimationStateName);
        _audioController.BookOpened();

        yield return new WaitForSeconds(_openClipLength + extraWaitSeconds);

        if (rootPage != null && LeftPage != null && RightPage != null)
        {
            LeftPage.SetActive(true);
            RightPage.SetActive(true);
            rootPage.SetActive(true);
            ConfigurePageEventCameras();
        }

        Globals.RaiseBookOpened();
    }

    private IEnumerator CloseSequenceRoutine()
    {
        if (rootPage != null && LeftPage != null && RightPage != null)
        {
            StartCoroutine(leftPages.DeactivateListAsync(batchSize: 3));
            StartCoroutine(rightPages.DeactivateListAsync(batchSize: 3));
            LeftPage.SetActive(false);
            RightPage.SetActive(false);
        }

        PlayBookAnimation(closeAnimationStateName);
        _audioController.BookClosed();

        yield return new WaitForSeconds(_closeClipLength + extraWaitSeconds);

        if (bookMesh != null)
        {
            bookMesh.SetActive(false);
        }
    }

    private void AttachPagesToBones()
    {
        if (pageBoneL == null)
        {
            pageBoneL = transform.Find("pauseMenuBook/bookArmature/root/local/book_r/UI_l");
        }
        if (pageBoneR == null)
        {
            pageBoneR = transform.Find("pauseMenuBook/bookArmature/root/local/book_l/UI_r");
        }

        if (LeftPage != null && pageBoneL != null && LeftPage.transform.parent != pageBoneL)
        {
            LeftPage.transform.SetParent(pageBoneL, false);
            LeftPage.transform.localPosition = leftPageLocalOffset;
            LeftPage.transform.localEulerAngles = leftPageLocalEuler;
            LeftPage.transform.localScale = Vector3.one * 0.001f;
        }

        if (RightPage != null && pageBoneR != null && RightPage.transform.parent != pageBoneR)
        {
            RightPage.transform.SetParent(pageBoneR, false);
            RightPage.transform.localPosition = Vector3.zero;
            RightPage.transform.localEulerAngles = new Vector3(0f, 90f, 90f);
            RightPage.transform.localScale = Vector3.one * 0.001f;
        }
    }

    private void OrientPauseMenuToPlayer()
    {
        AttachPagesToBones();

        Vector3 targetPosition;
        Quaternion targetRotation;

        if (Globals.isDesktopMode)
        {
            Camera cam = GetActiveEventCamera();
            if (useCloserPlacement && cam != null)
            {
                targetPosition = cam.transform.TransformPoint(closerDesktopCameraOffset);
                targetRotation = cam.transform.rotation * Quaternion.Euler(closerDesktopCameraEuler);
                transform.localScale = Vector3.one * closerDesktopScale;
            }
            else if (menuHandleKB != null && menuHandleKB.activeSelf)
            {
                menuHandleKB.transform.GetPositionAndRotation(out targetPosition, out targetRotation);
                targetRotation *= Quaternion.Euler(rotationOffset);
                targetPosition += targetRotation * desktopPositionOffset;
                transform.localScale = Vector3.one * desktopScale;
            }
            else
            {
                transform.GetPositionAndRotation(out targetPosition, out targetRotation);
                transform.localScale = Vector3.one * desktopScale;
            }
        }
        else
        {
            if (menuHandleVR != null)
            {
                menuHandleVR.transform.GetPositionAndRotation(out targetPosition, out targetRotation);
            }
            else
            {
                transform.GetPositionAndRotation(out targetPosition, out targetRotation);
            }
            transform.localScale = Vector3.one;
        }

        transform.SetPositionAndRotation(targetPosition, targetRotation);
    }

    private void PlayBookAnimation(string name)
    {
        if (bookAnimator != null && !string.IsNullOrEmpty(name))
        {
            bookAnimator.CrossFade(name, 0f);
        }
    }

    private List<GameObject> GetDirectChildren(GameObject parent)
    {
        List<GameObject> children = new();

        if (parent == null)
        {
            return children;
        }

        for (int i = 0; i < parent.transform.childCount; i++)
        {
            children.Add(parent.transform.GetChild(i).gameObject);
        }

        return children;
    }

    public void OnQuitGame()
    {
        Application.Quit();
    }

    public void OpenControlsMenu()
    {
        if (rootPage != null) rootPage.SetActive(false);

        Transform uiMain = LeftPage != null ? (LeftPage.name == "UI_Main" ? LeftPage.transform : (LeftPage.transform.Find("UI_Main") ?? LeftPage.transform)) : null;
        Transform uiDesc = RightPage != null ? (RightPage.name == "UI_Descriptions" ? RightPage.transform : (RightPage.transform.Find("UI_Descriptions") ?? RightPage.transform)) : null;

        if (Globals.isDesktopMode)
        {
            if (uiMain != null)
            {
                var kb = uiMain.Find("kbControls");
                if (kb != null) kb.gameObject.SetActive(true);
                var vr = uiMain.Find("vrControls");
                if (vr != null) vr.gameObject.SetActive(false);
            }
            if (uiDesc != null)
            {
                var kb = uiDesc.Find("kbControls");
                if (kb != null) kb.gameObject.SetActive(true);
                var vr = uiDesc.Find("vrControls");
                if (vr != null) vr.gameObject.SetActive(false);
            }
        }
        else
        {
            if (uiMain != null)
            {
                var vr = uiMain.Find("vrControls");
                if (vr != null) vr.gameObject.SetActive(true);
                var kb = uiMain.Find("kbControls");
                if (kb != null) kb.gameObject.SetActive(false);
            }
            if (uiDesc != null)
            {
                var vr = uiDesc.Find("vrControls");
                if (vr != null) vr.gameObject.SetActive(true);
                var kb = uiDesc.Find("kbControls");
                if (kb != null) kb.gameObject.SetActive(false);
            }
        }

        ConfigurePageEventCameras();
    }

    public void CloseControlsMenu()
    {
        Transform uiMain = LeftPage != null ? (LeftPage.name == "UI_Main" ? LeftPage.transform : (LeftPage.transform.Find("UI_Main") ?? LeftPage.transform)) : null;
        Transform uiDesc = RightPage != null ? (RightPage.name == "UI_Descriptions" ? RightPage.transform : (RightPage.transform.Find("UI_Descriptions") ?? RightPage.transform)) : null;

        if (uiMain != null)
        {
            var kb = uiMain.Find("kbControls");
            if (kb != null) kb.gameObject.SetActive(false);
            var vr = uiMain.Find("vrControls");
            if (vr != null) vr.gameObject.SetActive(false);
        }
        if (uiDesc != null)
        {
            var kb = uiDesc.Find("kbControls");
            if (kb != null) kb.gameObject.SetActive(false);
            var vr = uiDesc.Find("vrControls");
            if (vr != null) vr.gameObject.SetActive(false);
        }

        if (rootPage != null) rootPage.SetActive(true);
        ConfigurePageEventCameras();
    }

    public void clickSwitchControlScheme()
    {
        if (Globals.isDesktopMode)
        {
            StartCoroutine(SwitchToVRRoutine());
        }
        else
        {
            StartCoroutine(SwitchToDesktopRoutine());
        }
    }

    private IEnumerator SwitchToVRRoutine()
    {
        var manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
        if (manager == null)
        {
            Debug.LogError("[PauseMenu] No XRManagerSettings found.");
            yield break;
        }

        if (!manager.isInitializationComplete)
        {
            yield return manager.InitializeLoader();
        }

        if (manager.activeLoader == null)
        {
            Debug.LogError("[PauseMenu] Failed to initialize OpenXR. Ensure a VR headset is connected and active.");
            yield break;
        }

        manager.StartSubsystems();
#pragma warning disable CS0618
        XRSettings.enabled = true;
#pragma warning restore CS0618

        yield return null;

        var player = _player != null ? _player : FindFirstObjectByType<Player>();
        if (player != null)
        {
            var kb = player.transform.Find("PlayerKB")?.gameObject;
            var vr = player.transform.Find("PlayerVR")?.gameObject;
            if (kb != null && vr != null)
            {
                vr.transform.position = kb.transform.position;
                vr.transform.rotation = Quaternion.Euler(0, kb.transform.eulerAngles.y, 0);

                var kbCam = kb.GetComponentInChildren<Camera>(true);
                if (kbCam != null)
                {
                    kbCam.enabled = false;
                    kbCam.tag = "Untagged";
                    kbCam.stereoTargetEye = StereoTargetEyeMask.None;
                }

                var vrCam = vr.GetComponentInChildren<Camera>(true);
                if (vrCam != null)
                {
                    vrCam.enabled = true;
                    vrCam.tag = "MainCamera";
                    vrCam.stereoTargetEye = StereoTargetEyeMask.Both;
                }

                kb.SetActive(false);
                vr.SetActive(true);
            }
        }

        Globals.isDesktopMode = false;

        if (PlayerManager.Instance != null)
        {
            if (PlayerManager.Instance.VRLeftInteractor != null) PlayerManager.Instance.VRLeftInteractor.enableFarCasting = true;
            if (PlayerManager.Instance.VRRightInteractor != null) PlayerManager.Instance.VRRightInteractor.enableFarCasting = true;
        }

        if (Globals.isMenuOpen)
        {
            OpenControlsMenu();
            OrientPauseMenuToPlayer();
            ConfigurePageEventCameras();
        }
    }

    private IEnumerator SwitchToDesktopRoutine()
    {
        // 1. Stop OpenXR cleanly while VR camera is still alive
        EnsureXRDisabled();

        // 2. Wait a frame for Unity's rendering pipeline to release the XR display mirror
        yield return null;

        var player = _player != null ? _player : FindFirstObjectByType<Player>();
        if (player != null)
        {
            var kb = player.transform.Find("PlayerKB")?.gameObject;
            var vr = player.transform.Find("PlayerVR")?.gameObject;
            if (kb != null && vr != null)
            {
                kb.transform.position = vr.transform.position;
                kb.transform.rotation = Quaternion.Euler(0, vr.transform.eulerAngles.y, 0);

                var vrCam = vr.GetComponentInChildren<Camera>(true);
                if (vrCam != null)
                {
                    vrCam.enabled = false;
                    vrCam.tag = "Untagged";
                    vrCam.stereoTargetEye = StereoTargetEyeMask.None;
                }

                var kbCam = kb.GetComponentInChildren<Camera>(true);
                if (kbCam != null)
                {
                    kbCam.enabled = true;
                    kbCam.tag = "MainCamera";
                    kbCam.stereoTargetEye = StereoTargetEyeMask.None;
                }

                vr.SetActive(false);
                kb.SetActive(true);
            }
        }

        Globals.isDesktopMode = true;
        EnsureInputModuleActive();
        Globals.handleMovement = true;
        Globals.handleKBLook = true;

        if (PlayerManager.Instance != null)
        {
            if (PlayerManager.Instance.VRLeftInteractor != null) PlayerManager.Instance.VRLeftInteractor.enableFarCasting = false;
            if (PlayerManager.Instance.VRRightInteractor != null) PlayerManager.Instance.VRRightInteractor.enableFarCasting = false;
        }

        if (Globals.isMenuOpen)
        {
            OpenControlsMenu();
            OrientPauseMenuToPlayer();
            ConfigurePageEventCameras();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        yield return null;
    }

    public static void EnsureXRDisabled()
    {
#pragma warning disable CS0618
        if (XRSettings.enabled)
        {
            XRSettings.enabled = false;
        }
#pragma warning restore CS0618
        var manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
        if (manager != null && manager.isInitializationComplete)
        {
            manager.StopSubsystems();
            manager.DeinitializeLoader();
        }
    }
}
