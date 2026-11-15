using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

public class PauseMenu : MonoBehaviour
{
    [Header("Book Orientation (Desktop)")]
    [Tooltip("If true, positions book closer to camera at scaled size so hands do not obstruct button raycasts.")]
    [SerializeField] private bool useCloserPlacement = true;
    [SerializeField] private Vector3 closerDesktopCameraOffset = new(0f, -0.353f, 0.442f);
    [SerializeField] private Vector3 closerDesktopCameraEuler = new(18f, 180f, 0f);
    [SerializeField] private float closerDesktopScale = 0.2625f;

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

        AssignInputs();
        EnsureInputModuleActive();
        ConfigurePageEventCameras();
    }

    private void AssignInputs()
    {
        _player.Controls.Viva.Pause.performed += ctx => TogglePauseMenu();
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
        if (rootPage != null && rootPage.transform.parent != transform) ApplyToCanvases(rootPage);
        if (LeftPage != null && LeftPage.transform.parent != transform) ApplyToCanvases(LeftPage);
        if (RightPage != null && RightPage.transform.parent != transform) ApplyToCanvases(RightPage);
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
            LeftPage.transform.localPosition = Vector3.zero;
            LeftPage.transform.localEulerAngles = new Vector3(180f, 90f, 90f);
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
}
