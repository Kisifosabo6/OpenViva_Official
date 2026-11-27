using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Viva.Interaction;

namespace Viva.Dolls
{
    /// <summary>
    /// Mirror UI controller for browsing Doll cards, previewing them in the mirror,
    /// spawning new Dolls, or replacing a selected Doll in the world.
    /// </summary>
    [RequireComponent(typeof(Stegosaurus))]
    public class DollMirrorMenu : MonoBehaviour, IInteractable
    {
        [Header("Proximity & Activation")]
        [SerializeField] private float approachRadius = 1.85f;
        [SerializeField] private float exitResetRadius = 2.4f;
        [SerializeField] private Vector3 proximityLocalOffset = new Vector3(-0.31f, -0.30f, 0.96f);

        [Header("UI References")]
        [SerializeField] private Canvas mirrorCanvas;
        [SerializeField] private GameObject modelBrowseTab;
        [SerializeField] private GameObject errorWindow;
        [SerializeField] private Text errorText;
        [SerializeField] private Transform loadingCycle;
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private TextMeshProUGUI dollsSelectedText;
        [SerializeField] private Button spawnOrReplaceButton;
        [SerializeField] private Text spawnOrReplaceButtonText;
        [SerializeField] private Button closeButton;
        [SerializeField] private Button deselectAllButton;
        [SerializeField] private GameObject[] unusedTabObjects;

        [Header("Card Browsers")]
        [SerializeField] private DollCardBrowser dollCardBrowser = new DollCardBrowser();
        [SerializeField] private DollCardBrowser skinCardBrowser = new DollCardBrowser();

        [Header("Doll Prefabs & Assets")]
        [SerializeField] private DollPreviewViewport previewViewport;
        [SerializeField] private GameObject defaultDollPrefab;
        [SerializeField] private GameObject selectorVisualPrefab;
        [SerializeField] private Transform dollSpawnTransform;
        [SerializeField] private Material dollHeadMaterialTemplate;
        [SerializeField] private Material dollPupilMaterialTemplate;
        [SerializeField] private DollPoseCache defaultPoseCache;

        [Header("Preview Stage")]
        [SerializeField] private Vector3 previewStageWorldOffset = new Vector3(0f, -50f, 0f);

        private Stegosaurus stegosaurus;
        private DollInstance previewDollInstance;
        private string selectedCardFileName;
        private Coroutine activeCoroutine;
        private Coroutine loadingCycleCoroutine;
        private bool isMenuOpen = false;
        private bool closedWhileInside = false;

        public bool CanInteract => !isMenuOpen;

        private void Awake()
        {
            stegosaurus = GetComponent<Stegosaurus>();
            if (mirrorCanvas != null) mirrorCanvas.gameObject.SetActive(false);
            if (previewViewport != null) previewViewport.gameObject.SetActive(false);
            if (errorWindow != null) errorWindow.SetActive(false);
            if (loadingCycle != null) loadingCycle.gameObject.SetActive(false);

            WireButtons();
            ApplyDollNamingToUI();
        }

        private void OnEnable()
        {
            if (CharacterSelectionManager.Instance != null)
            {
                CharacterSelectionManager.Instance.OnCharacterSelected += HandleDollSelectionChanged;
                CharacterSelectionManager.Instance.OnCharacterDeselected += HandleDollDeselected;
            }
        }

        private void OnDisable()
        {
            if (CharacterSelectionManager.Instance != null)
            {
                CharacterSelectionManager.Instance.OnCharacterSelected -= HandleDollSelectionChanged;
                CharacterSelectionManager.Instance.OnCharacterDeselected -= HandleDollDeselected;
            }
            if (isMenuOpen) CloseMenu();
        }

        private void WireButtons()
        {
            if (closeButton != null)
            {
                closeButton.onClick.RemoveAllListeners();
                closeButton.onClick.AddListener(CloseMenu);
            }
            if (deselectAllButton != null)
            {
                deselectAllButton.onClick.RemoveAllListeners();
                deselectAllButton.onClick.AddListener(ClickDeselectAllDolls);
            }
            if (spawnOrReplaceButton != null)
            {
                spawnOrReplaceButton.onClick.RemoveAllListeners();
                spawnOrReplaceButton.onClick.AddListener(ClickSpawnOrReplaceDoll);
            }
            if (errorWindow != null)
            {
                var okBtn = errorWindow.GetComponentInChildren<Button>(true);
                if (okBtn != null)
                {
                    okBtn.onClick.RemoveAllListeners();
                    okBtn.onClick.AddListener(ClickCloseErrorWindow);
                }
            }
        }

        private void ApplyDollNamingToUI()
        {
            if (titleText != null)
            {
                titleText.text = "Doll Customizer";
            }
            if (unusedTabObjects != null)
            {
                foreach (var obj in unusedTabObjects)
                {
                    if (obj != null) obj.SetActive(false);
                }
            }
            RefreshSelectedDollUI();
        }

        public void Interact(GameObject interactor)
        {
            if (!isMenuOpen)
            {
                closedWhileInside = false;
                OpenMenu();
            }
        }

        private void Update()
        {
            Vector3 playerPos = GetPlayerWorldPosition();
            Vector3 mirrorCenter = transform.TransformPoint(proximityLocalOffset);
            float dist = Vector3.Distance(playerPos, mirrorCenter);

            if (!isMenuOpen)
            {
                if (closedWhileInside)
                {
                    if (dist > exitResetRadius)
                    {
                        closedWhileInside = false;
                    }
                }
                else if (dist <= approachRadius && !Globals.isMenuOpen)
                {
                    OpenMenu();
                }
            }
            else
            {
                if (!Globals.isDesktopMode && dist > exitResetRadius)
                {
                    CloseMenu();
                    return;
                }

                if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                {
                    CloseMenu();
                }
            }
        }

        private Vector3 GetPlayerWorldPosition()
        {
            Camera cam = GetActiveEventCamera();
            if (cam != null) return cam.transform.position;

            var player = FindFirstObjectByType<Player>();
            return player != null ? player.transform.position : Vector3.positiveInfinity;
        }

        public void OpenMenu()
        {
            if (isMenuOpen || Globals.isMenuOpen) return;

            isMenuOpen = true;
            Globals.isMenuOpen = true;
            Globals.allowMenuOpen = false;

            if (mirrorCanvas != null)
            {
                mirrorCanvas.gameObject.SetActive(true);
                ConfigureCanvasForInput();
            }
            if (modelBrowseTab != null) modelBrowseTab.SetActive(true);
            if (errorWindow != null) errorWindow.SetActive(false);

            ApplyDollNamingToUI();
            RefreshSelectedDollUI();

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (!Globals.isDesktopMode && PlayerManager.Instance != null)
            {
                if (PlayerManager.Instance.VRLeftInteractor != null) PlayerManager.Instance.VRLeftInteractor.enableFarCasting = true;
                if (PlayerManager.Instance.VRRightInteractor != null) PlayerManager.Instance.VRRightInteractor.enableFarCasting = true;
            }

            dollCardBrowser.Initialize(this, true, OnSelectDollCard);
            skinCardBrowser.Initialize(this, false, null);

            if (spawnOrReplaceButton != null)
            {
                spawnOrReplaceButton.gameObject.SetActive(!string.IsNullOrEmpty(selectedCardFileName) && previewDollInstance != null);
            }
        }

        public void CloseMenu()
        {
            bool wasOpen = isMenuOpen;
            isMenuOpen = false;
            if (wasOpen)
            {
                closedWhileInside = true;
                Globals.isMenuOpen = false;
                Globals.allowMenuOpen = true;
            }

            if (activeCoroutine != null)
            {
                StopCoroutine(activeCoroutine);
                activeCoroutine = null;
            }
            StopLoadingCycle();

            if (previewViewport != null)
            {
                previewViewport.SetPreviewDoll(null);
                previewViewport.gameObject.SetActive(false);
            }
            if (previewDollInstance != null)
            {
                previewDollInstance.gameObject.SetActive(false);
            }
            if (mirrorCanvas != null)
            {
                mirrorCanvas.gameObject.SetActive(false);
            }

            if (!wasOpen) return;

            if (Globals.isDesktopMode)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            else if (PlayerManager.Instance != null)
            {
                if (PlayerManager.Instance.VRLeftInteractor != null) PlayerManager.Instance.VRLeftInteractor.enableFarCasting = false;
                if (PlayerManager.Instance.VRRightInteractor != null) PlayerManager.Instance.VRRightInteractor.enableFarCasting = false;
            }
        }

        private void ConfigureCanvasForInput()
        {
            Camera cam = GetActiveEventCamera();
            if (cam != null)
            {
                mirrorCanvas.worldCamera = cam;
            }

            foreach (var raycaster in mirrorCanvas.GetComponentsInChildren<GraphicRaycaster>(true))
            {
                raycaster.ignoreReversedGraphics = false;
                raycaster.blockingObjects = GraphicRaycaster.BlockingObjects.None;
            }

            var es = EventSystem.current != null ? EventSystem.current : FindFirstObjectByType<EventSystem>();
            if (es != null && Globals.isDesktopMode)
            {
                var ism = es.GetComponent<InputSystemUIInputModule>();
                if (ism == null) ism = es.gameObject.AddComponent<InputSystemUIInputModule>();
                ism.enabled = true;
            }
        }

        private Camera GetActiveEventCamera()
        {
            if (Camera.main != null && Camera.main.isActiveAndEnabled) return Camera.main;
            foreach (var c in Camera.allCameras)
            {
                if (c.isActiveAndEnabled && c.CompareTag("MainCamera")) return c;
            }
            return Camera.main;
        }

        private void HandleDollSelectionChanged(CharacterInteractable selected) => RefreshSelectedDollUI();
        private void HandleDollDeselected() => RefreshSelectedDollUI();

        private void RefreshSelectedDollUI()
        {
            bool hasSelection = CharacterSelectionManager.Instance != null &&
                                CharacterSelectionManager.Instance.SelectedCharacter != null;

            if (dollsSelectedText != null)
            {
                dollsSelectedText.text = hasSelection ? "1 Doll Selected" : "0 Dolls Selected";
            }
            if (spawnOrReplaceButtonText != null)
            {
                spawnOrReplaceButtonText.text = hasSelection ? "Replace Doll" : "Spawn Doll";
            }
        }

        public void ClickDeselectAllDolls()
        {
            if (CharacterSelectionManager.Instance != null)
            {
                CharacterSelectionManager.Instance.DeselectCharacter();
            }
            RefreshSelectedDollUI();
        }

        public void ClickCloseErrorWindow()
        {
            if (errorWindow != null) errorWindow.SetActive(false);
            if (modelBrowseTab != null) modelBrowseTab.SetActive(true);
            dollCardBrowser.SetAllCardsInteractable(true);
            activeCoroutine = null;
            StopLoadingCycle();
        }

        private void DisplayErrorWindow(string message)
        {
            Debug.LogError("[DollMirrorMenu] " + message);
            if (modelBrowseTab != null) modelBrowseTab.SetActive(false);
            if (errorWindow != null) errorWindow.SetActive(true);
            if (errorText != null) errorText.text = message;
            StopLoadingCycle();
            activeCoroutine = null;
        }

        private void OnSelectDollCard(string cardFileName, Button sourceButton)
        {
            if (activeCoroutine != null) return;

            selectedCardFileName = cardFileName;
            StartLoadingCycle();
            dollCardBrowser.SetAllCardsInteractable(false);
            activeCoroutine = StartCoroutine(PreviewDollCardRoutine(cardFileName));
        }

        private IEnumerator PreviewDollCardRoutine(string cardFileName)
        {
            DollInstance previewDoll = EnsurePreviewDollInstance();
            if (previewDoll == null)
            {
                DisplayErrorWindow("DefaultDoll prefab is not assigned on the mirror.");
                yield break;
            }

            previewDoll.gameObject.SetActive(true);
            string error = null;
            yield return StartCoroutine(LoadCardOntoDoll(cardFileName, previewDoll, err => error = err));

            if (!string.IsNullOrEmpty(error))
            {
                DisplayErrorWindow(error);
                yield break;
            }

            if (previewViewport != null)
            {
                previewViewport.SetPreviewDoll(previewDoll);
            }

            if (spawnOrReplaceButton != null) spawnOrReplaceButton.gameObject.SetActive(true);
            dollCardBrowser.SetAllCardsInteractable(true);
            StopLoadingCycle();
            activeCoroutine = null;
            RefreshSelectedDollUI();
        }

        public void ClickSpawnOrReplaceDoll()
        {
            if (activeCoroutine != null || string.IsNullOrEmpty(selectedCardFileName)) return;

            StartLoadingCycle();
            dollCardBrowser.SetAllCardsInteractable(false);
            if (spawnOrReplaceButton != null) spawnOrReplaceButton.interactable = false;

            activeCoroutine = StartCoroutine(SpawnOrReplaceDollRoutine(selectedCardFileName));
        }

        private IEnumerator SpawnOrReplaceDollRoutine(string cardFileName)
        {
            if (defaultDollPrefab == null)
            {
                DisplayErrorWindow("DefaultDoll prefab is not assigned on the mirror.");
                if (spawnOrReplaceButton != null) spawnOrReplaceButton.interactable = true;
                yield break;
            }

            CharacterInteractable selectedExisting = CharacterSelectionManager.Instance != null
                ? CharacterSelectionManager.Instance.SelectedCharacter
                : null;

            bool isReplacing = selectedExisting != null;
            Vector3 spawnPos;
            Quaternion spawnRot;
            bool preserveFollow = true;
            Transform preserveTarget = null;

            if (isReplacing)
            {
                spawnPos = selectedExisting.transform.position;
                spawnRot = selectedExisting.transform.rotation;
                if (selectedExisting.TryGetComponent<CompanionAI>(out var oldAi))
                {
                    preserveFollow = oldAi.isFollowing;
                    preserveTarget = oldAi.target;
                }
            }
            else
            {
                spawnPos = dollSpawnTransform != null
                    ? dollSpawnTransform.position
                    : transform.position + transform.forward * 1.5f;
                spawnRot = dollSpawnTransform != null
                    ? dollSpawnTransform.rotation
                    : Quaternion.LookRotation(-transform.forward, Vector3.up);

                if (NavMesh.SamplePosition(spawnPos, out NavMeshHit navHit, 5.0f, NavMesh.AllAreas))
                {
                    spawnPos = navHit.position;
                }
            }

            GameObject newDollGo = Instantiate(defaultDollPrefab, spawnPos, spawnRot);
            DollInstance newDoll = DollUtilities.EnsureComponent<DollInstance>(newDollGo);
            if (newDoll.basePoseCache == null) newDoll.basePoseCache = defaultPoseCache;
            newDoll.SetPreviewMode(false);

            string error = null;
            yield return StartCoroutine(LoadCardOntoDoll(cardFileName, newDoll, err => error = err));

            if (!string.IsNullOrEmpty(error))
            {
                Destroy(newDollGo);
                DisplayErrorWindow(error);
                if (spawnOrReplaceButton != null) spawnOrReplaceButton.interactable = true;
                yield break;
            }

            if (newDollGo.TryGetComponent<CompanionAI>(out var ai))
            {
                ai.isFollowing = preserveFollow;
                ai.target = preserveTarget != null ? preserveTarget : (Camera.main != null ? Camera.main.transform : null);
                ai.TrySnapToNavMesh();
            }

            EnsureSelectionVisuals(newDollGo);

            if (isReplacing)
            {
                CharacterSelectionManager.Instance.DeselectCharacter();
                Destroy(selectedExisting.gameObject);
                if (newDollGo.TryGetComponent<CharacterInteractable>(out var newInteractable))
                {
                    CharacterSelectionManager.Instance.SelectCharacter(newInteractable);
                }
                Debug.Log($"[DollMirrorMenu] Replaced Doll with '{newDollGo.name}' at {spawnPos}");
            }
            else
            {
                Debug.Log($"[DollMirrorMenu] Spawned Doll '{newDollGo.name}' at {spawnPos}");
            }

            if (spawnOrReplaceButton != null) spawnOrReplaceButton.interactable = true;
            dollCardBrowser.SetAllCardsInteractable(true);
            StopLoadingCycle();
            activeCoroutine = null;

            CloseMenu();
        }

        private void EnsureSelectionVisuals(GameObject dollGo)
        {
            if (!dollGo.TryGetComponent<CharacterInteractable>(out var interactable)) return;
            if (interactable.selectionArrow != null || selectorVisualPrefab == null) return;

            var visuals = DollUtilities.EnsureComponent<SpawnSelectorVisuals>(dollGo);
            visuals.SpawnPrefab = selectorVisualPrefab;
            visuals.SpawnOffset = new Vector3(0f, 0.55f, 0f);
            visuals.SpawnSelectorVisual(dollGo.transform);
            interactable.selectionArrow = visuals.SpawnedSelector;
            interactable.SetSelectedVisual(false);
        }

        private DollInstance EnsurePreviewDollInstance()
        {
            if (previewDollInstance != null) return previewDollInstance;
            if (defaultDollPrefab == null) return null;

            Vector3 stagePos = transform.position + previewStageWorldOffset;
            GameObject previewGo = Instantiate(defaultDollPrefab, stagePos, Quaternion.identity);
            previewGo.name = "DollPreviewStage";
            previewDollInstance = DollUtilities.EnsureComponent<DollInstance>(previewGo);
            if (previewDollInstance.basePoseCache == null) previewDollInstance.basePoseCache = defaultPoseCache;
            previewDollInstance.SetPreviewMode(true);
            return previewDollInstance;
        }

        private IEnumerator LoadCardOntoDoll(string cardFileName, DollInstance targetDoll, System.Action<string> onError)
        {
            if (stegosaurus == null)
            {
                onError?.Invoke("[DollMirrorMenu] Stegosaurus component is missing on the mirror!");
                yield break;
            }

            var modelCardReq = new DollCardBrowser.LoadCardTextureRequest(cardFileName);
            yield return StartCoroutine(dollCardBrowser.LoadCardTexture(modelCardReq));
            if (modelCardReq.result == null)
            {
                onError?.Invoke(modelCardReq.error ?? $"Failed to load doll card '{cardFileName}'.");
                yield break;
            }

            var unpackModelReq = new Stegosaurus.UnpackLosslessDataRequest(modelCardReq.result, true);
            yield return StartCoroutine(stegosaurus.ExecuteUnpackDoll(unpackModelReq));
            string loadedCardName = modelCardReq.result.name;
            Destroy(modelCardReq.result);

            if (unpackModelReq.result == null)
            {
                onError?.Invoke(unpackModelReq.error ?? $"Failed to unpack doll card '{cardFileName}'.");
                yield break;
            }

            var skinCardReq = new DollCardBrowser.LoadCardTextureRequest(loadedCardName);
            yield return StartCoroutine(skinCardBrowser.LoadCardTexture(skinCardReq));
            if (skinCardReq.result == null)
            {
                onError?.Invoke(skinCardReq.error ?? $"Missing matching skin card for '{loadedCardName}' in Cards/Skins.");
                yield break;
            }

            var unpackSkinReq = new Stegosaurus.UnpackLosslessDataRequest(skinCardReq.result, false);
            yield return StartCoroutine(stegosaurus.ExecuteUnpackDoll(unpackSkinReq));
            Destroy(skinCardReq.result);

            if (unpackSkinReq.result == null)
            {
                onError?.Invoke(unpackSkinReq.error ?? $"Failed to unpack skin card for '{loadedCardName}'.");
                yield break;
            }

            var serializer = new DollCardTextureSerializer(DollModel.skinFormat);
            Texture2D[] modelTextures = serializer.Deserialize(unpackSkinReq.result);
            if (modelTextures == null)
            {
                onError?.Invoke(serializer.error ?? "Failed to deserialize doll skin textures.");
                yield break;
            }

            var mbs = new DollModelBuildSettings(
                dollHeadMaterialTemplate,
                dollPupilMaterialTemplate,
                new[] { "skin", "pupil_r", "pupil_l" }
            )
            {
                modelTexture = modelTextures[0],
                rightEyeTexture = modelTextures[1],
                leftEyeTexture = modelTextures[2]
            };

            var createReq = new DollModel.CreateDollRequest(cardFileName, targetDoll, unpackModelReq.result, mbs);
            yield return StartCoroutine(DollModel.DeserializeDollModel(createReq));
            if (!string.IsNullOrEmpty(createReq.error))
            {
                onError?.Invoke(createReq.error);
                yield break;
            }
        }

        private void StartLoadingCycle()
        {
            if (loadingCycle == null || loadingCycleCoroutine != null) return;
            loadingCycle.gameObject.SetActive(true);
            loadingCycleCoroutine = StartCoroutine(LoadingCycleAnimation());
        }

        private void StopLoadingCycle()
        {
            if (loadingCycleCoroutine == null) return;
            StopCoroutine(loadingCycleCoroutine);
            loadingCycleCoroutine = null;
            if (loadingCycle != null) loadingCycle.gameObject.SetActive(false);
        }

        private IEnumerator LoadingCycleAnimation()
        {
            while (true)
            {
                if (loadingCycle != null)
                {
                    loadingCycle.localEulerAngles = new Vector3(0f, 0f, Mathf.Floor(Time.unscaledTime * 8.0f) * 45.0f);
                }
                yield return null;
            }
        }
    }
}
