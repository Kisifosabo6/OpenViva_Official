using System.Collections;
using UnityEngine;

/// <summary>
/// Interaction for candles (wall candles, table candles).
/// Interacting with a candle toggles its flame/light,
/// while the player's hand plays a momentary pinching animation for a fraction of a second.
/// Includes automatic distance culling for lights and flame visuals to maintain high performance.
/// </summary>
public class InteractCandle : MonoBehaviour, IInteractable
{
    [Header("Candle Components")]
    [Tooltip("The flame GameObject to toggle active/inactive")]
    public GameObject flameObject;
    [Tooltip("Light source for the candle (optional, auto-found if child)")]
    public Light candleLight;
    [Tooltip("Audio played when lighting/snuffing")]
    public AudioSource audioSource;
    [Tooltip("Particle system for flame")]
    public ParticleSystem flameParticles;

    [Header("State")]
    public bool isLit = true;

    [Header("Lighting Tuning")]
    [Tooltip("Target intensity when candle is lit.")]
    [SerializeField] private float targetIntensity = 1.8f;
    [Tooltip("Target range in meters when candle is lit.")]
    [SerializeField] private float targetRange = 5.5f;

    [Header("Performance & Distance Culling")]
    [Tooltip("Whether to automatically cull light and flame visuals based on camera distance.")]
    [SerializeField] private bool enableDistanceCulling = true;
    [Tooltip("Maximum distance in meters to render the candle's light component.")]
    [SerializeField] private float lightCullDistance = 18.0f;
    [Tooltip("Maximum distance in meters to render the flame visual and particles.")]
    [SerializeField] private float flameCullDistance = 35.0f;

    private Outline outline;
    private bool isPinching = false;
    private Transform mainCameraTransform;
    private float cullingTimer = 0f;
    private float cullingInterval = 0.25f;

    public bool CanInteract => !isPinching;

    private void Awake()
    {
        // Guard against being erroneously placed on an empty parent group/folder container holding candles
        if (GetComponent<Collider>() == null && GetComponent<Renderer>() == null)
        {
            var childCandles = GetComponentsInChildren<InteractCandle>(true);
            if (childCandles.Length > 1) // includes self and children
            {
                Debug.LogWarning($"[InteractCandle] Disabling and removing invalid InteractCandle component from parent group container '{gameObject.name}'.");
                enabled = false;
                Destroy(this);
                return;
            }
        }

        outline = GetComponent<Outline>();
        audioSource = GetComponentInChildren<AudioSource>();

        FindFlame();
        ConfigureLight();
        EnsureInteractionReach();

        // Stagger initial culling timer so all candles don't evaluate on the exact same frame
        cullingTimer = Random.Range(0f, cullingInterval);
    }

    private void Start()
    {
        CacheMainCamera();
        ApplyState(isLit, force: true);
    }

    private void Update()
    {
        if (!enableDistanceCulling) return;

        cullingTimer -= Time.deltaTime;
        if (cullingTimer <= 0f)
        {
            cullingTimer = cullingInterval;
            UpdateDistanceCulling();
        }
    }

    private void CacheMainCamera()
    {
        if (mainCameraTransform == null && Camera.main != null)
        {
            mainCameraTransform = Camera.main.transform;
        }
    }

    private void FindFlame()
    {
        if (flameObject == null)
        {
            var flameChild = transform.Find("flame");
            if (flameChild != null)
            {
                flameObject = flameChild.gameObject;
            }
            else
            {
                var allChildren = GetComponentsInChildren<Transform>(true);
                foreach (var child in allChildren)
                {
                    if (child != transform && child.name.ToLower().Contains("flame"))
                    {
                        var parentCandle = child.GetComponentInParent<InteractCandle>();
                        if (parentCandle == this)
                        {
                            flameObject = child.gameObject;
                            break;
                        }
                    }
                }
            }
        }

        if (candleLight == null)
        {
            var lights = GetComponentsInChildren<Light>(true);
            foreach (var l in lights)
            {
                var parentCandle = l.GetComponentInParent<InteractCandle>();
                if (parentCandle == this)
                {
                    candleLight = l;
                    break;
                }
            }
        }

        if (flameParticles == null)
        {
            var particles = GetComponentsInChildren<ParticleSystem>(true);
            foreach (var p in particles)
            {
                var parentCandle = p.GetComponentInParent<InteractCandle>();
                if (parentCandle == this)
                {
                    flameParticles = p;
                    break;
                }
            }
        }

        // Restore quad mesh and unmark static on flare to fix billboarding batching bugs
        RepairFlareVisuals();

        if (flameObject != null)
            isLit = flameObject.activeSelf;
        else if (candleLight != null)
            isLit = candleLight.enabled;
    }

    private void RepairFlareVisuals()
    {
        if (flameObject == null) return;

        var allRenderers = flameObject.GetComponentsInChildren<MeshRenderer>(true);
        foreach (var mr in allRenderers)
        {
            if (mr.gameObject.isStatic)
            {
                mr.gameObject.isStatic = false;
            }

            var mf = mr.GetComponent<MeshFilter>();
            if (mf != null && (mf.sharedMesh == null || mf.sharedMesh.name.Contains("Combined Mesh")))
            {
                // Restore fresh Quad mesh primitive
                var tempQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                mf.sharedMesh = tempQuad.GetComponent<MeshFilter>().sharedMesh;
                Destroy(tempQuad);
            }
        }
    }

    private void ConfigureLight()
    {
        if (candleLight == null) return;

        // Eliminate point-light shadow maps to prevent GPU overhead
        candleLight.shadows = LightShadows.None;

        if (candleLight.intensity < 0.6f)
            candleLight.intensity = targetIntensity;

        if (candleLight.range < 3.0f)
            candleLight.range = targetRange;
    }

    private void EnsureInteractionReach()
    {
        // If the candle has a BoxCollider that is flush with or embedded inside a wall,
        // expand its bounds forward slightly so hand rays and overlap spheres detect it easily.
        var box = GetComponent<BoxCollider>();
        if (box != null)
        {
            Vector3 size = box.size;
            Vector3 center = box.center;

            if (size.x < 0.22f) size.x = 0.22f;
            if (size.y < 0.25f) size.y = 0.25f;
            if (size.z < 0.22f) size.z = 0.22f;

            box.size = size;
            box.center = center;
        }
    }

    public void Interact(GameObject interactor)
    {
        if (isPinching) return;
        ToggleCandle(interactor);
    }

    public void ToggleCandle(GameObject interactor)
    {
        isLit = !isLit;
        ApplyState(isLit, force: false);

        if (audioSource != null)
        {
            audioSource.Play();
        }

        TriggerHandPinch(interactor);
    }

    private void ApplyState(bool lit, bool force)
    {
        if (!lit)
        {
            if (flameObject != null) flameObject.SetActive(false);
            if (candleLight != null) candleLight.enabled = false;
            if (flameParticles != null) flameParticles.Stop();
        }
        else
        {
            if (flameObject != null) flameObject.SetActive(true);
            if (flameParticles != null) flameParticles.Play();

            // Distance culling decides if the light and flame are active right now
            if (enableDistanceCulling)
            {
                UpdateDistanceCulling();
            }
            else
            {
                if (candleLight != null) candleLight.enabled = true;
            }
        }
    }

    private void UpdateDistanceCulling()
    {
        if (!isLit) return;

        CacheMainCamera();
        if (mainCameraTransform == null)
        {
            if (candleLight != null) candleLight.enabled = true;
            if (flameObject != null) flameObject.SetActive(true);
            return;
        }

        float sqrDist = (mainCameraTransform.position - transform.position).sqrMagnitude;
        float lightSqr = lightCullDistance * lightCullDistance;
        float flameSqr = flameCullDistance * flameCullDistance;

        if (candleLight != null)
        {
            bool shouldLight = (sqrDist <= lightSqr);
            if (candleLight.enabled != shouldLight)
            {
                candleLight.enabled = shouldLight;
            }
        }

        if (flameObject != null)
        {
            bool shouldFlame = (sqrDist <= flameSqr);
            if (flameObject.activeSelf != shouldFlame)
            {
                flameObject.SetActive(shouldFlame);
            }
        }
    }

    private void TriggerHandPinch(GameObject interactor)
    {
        if (interactor == null) return;

        var animators = interactor.GetComponentsInChildren<HandAnimator>();
        if (animators == null || animators.Length == 0)
        {
            var player = Object.FindFirstObjectByType<Player>();
            if (player != null) animators = player.GetComponentsInChildren<HandAnimator>();
        }

        if (animators != null && animators.Length > 0)
        {
            HandAnimator best = null;
            float bestDist = float.MaxValue;
            foreach (var ha in animators)
            {
                if (ha == null || ha.wrist == null) continue;
                float d = Vector3.Distance(ha.wrist.position, transform.position);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = ha;
                }
            }

            if (best != null)
            {
                best.PlayPinchMomentary(0.35f);
            }
        }

        StartCoroutine(PinchCooldown());
    }

    private IEnumerator PinchCooldown()
    {
        isPinching = true;
        yield return new WaitForSeconds(0.4f);
        isPinching = false;
    }

    public void SetHighlight(bool show)
    {
        if (outline != null)
        {
            outline.enabled = show;
        }
    }
}
