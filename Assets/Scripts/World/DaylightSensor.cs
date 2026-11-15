using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class DaylightSensor : MonoBehaviour
{
    [Tooltip("Maximum distance from the camera/player for a light to be enabled at night.")]
    [SerializeField] private float _cullingDistance = 40f;

    [Tooltip("How many frames between distance checks to optimize performance.")]
    [SerializeField] private int _checkIntervalFrames = 15;

    private List<Light> _lights;
    private bool _isNight;
    private Transform _targetTransform;

    private void Awake()
    {
        _lights = GetComponentsInChildren<Light>(true).ToList();
    }

    public void TurnLightsOff()
    {
        _isNight = false;
        if (_lights == null) _lights = GetComponentsInChildren<Light>(true).ToList();
        foreach (Light light in _lights)
        {
            if (light != null)
            {
                light.enabled = false;
            }
        }
    }

    public void TurnLightsOn()
    {
        _isNight = true;
        if (_lights == null) _lights = GetComponentsInChildren<Light>(true).ToList();
        UpdateLightCulling();
    }

    private void Update()
    {
        if (!_isNight) return;

        if (Time.frameCount % _checkIntervalFrames == 0)
        {
            UpdateLightCulling();
        }
    }

    private void UpdateLightCulling()
    {
        Transform target = GetTargetTransform();
        if (target == null)
        {
            // Fallback: if no target camera found yet, enable lights
            foreach (Light light in _lights)
            {
                if (light != null) light.enabled = true;
            }
            return;
        }

        Vector3 targetPos = target.position;
        float maxDistSqr = _cullingDistance * _cullingDistance;

        foreach (Light light in _lights)
        {
            if (light == null) continue;
            float sqrDist = (light.transform.position - targetPos).sqrMagnitude;
            light.enabled = sqrDist <= maxDistSqr;
        }
    }

    private Transform GetTargetTransform()
    {
        if (_targetTransform != null) return _targetTransform;

        if (Camera.main != null)
        {
            _targetTransform = Camera.main.transform;
            return _targetTransform;
        }

        GameObject player = GameObject.FindGameObjectWithTag(TagConstants.MainPlayer);
        if (player != null)
        {
            _targetTransform = player.transform;
            return _targetTransform;
        }

        return null;
    }
}
