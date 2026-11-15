using System.Collections;
using UnityEngine;

/// <summary>
/// Interaction for candles (wall candles, table candles).
/// Interacting with a candle toggles its flame/light,
/// while the player's hand plays a momentary pinching animation for a fraction of a second
/// without grabbing or holding a ghost item.
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

    Outline outline;
    bool isPinching = false;

    public bool CanInteract => !isPinching;

    void Awake()
    {
        outline = GetComponent<Outline>();
        audioSource = GetComponentInChildren<AudioSource>();

        FindFlame();
    }

    void FindFlame()
    {
        if (flameObject == null)
        {
            var flameChild = transform.Find("flame");
            if (flameChild != null)
                flameObject = flameChild.gameObject;
            else
            {
                // Recursive search in children
                var allChildren = GetComponentsInChildren<Transform>(true);
                foreach (var child in allChildren)
                {
                    if (child != transform && child.name.ToLower().Contains("flame"))
                    {
                        flameObject = child.gameObject;
                        break;
                    }
                }
            }
        }

        if (candleLight == null)
            candleLight = GetComponentInChildren<Light>(true);

        if (flameParticles == null)
            flameParticles = GetComponentInChildren<ParticleSystem>(true);

        if (flameObject != null)
            isLit = flameObject.activeSelf;
        else if (candleLight != null)
            isLit = candleLight.enabled;
    }

    public void Interact(GameObject interactor)
    {
        if (isPinching) return;
        ToggleCandle(interactor);
    }

    public void ToggleCandle(GameObject interactor)
    {
        isLit = !isLit;

        if (flameObject != null)
            flameObject.SetActive(isLit);
        if (candleLight != null)
            candleLight.enabled = isLit;
        if (flameParticles != null)
        {
            if (isLit) flameParticles.Play();
            else flameParticles.Stop();
        }

        if (audioSource != null)
            audioSource.Play();

        // Trigger momentary pinch gesture on the interactor's hand for a fraction of a second
        TriggerHandPinch(interactor);
    }

    void TriggerHandPinch(GameObject interactor)
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

    IEnumerator PinchCooldown()
    {
        isPinching = true;
        yield return new WaitForSeconds(0.4f);
        isPinching = false;
    }

    public void SetHighlight(bool show)
    {
        if (outline != null)
            outline.enabled = show;
    }
}
