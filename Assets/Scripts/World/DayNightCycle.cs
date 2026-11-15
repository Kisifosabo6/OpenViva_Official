using IngameDebugConsole;
using NaughtyAttributes;
using System;
using System.Collections.Generic;
using System.Linq;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

public enum CycleSpeed 
{ 
    TwelveMinutes, 
    FourtyEightMinutes, 
    TwoHours,
    SixHours,
    TwelveHours,
    OneDay,
    Paused
}

/// <summary>
/// Controls the Day/Night cycle with smooth, continuous transitions for sunlight,
/// ambient lighting, fog, moonlight, stars, and skybox blending.
/// Sunrise is at 06h00, Sunset is at 18h00.
/// Time of day can be chosen between 00h00 and 23h59 via Inspector or code.
/// </summary>
[ExecuteAlways]
public class DayNightCycle : MonoBehaviour
{
    public static DayNightCycle Instance { get; private set; }

    #region Time Settings

    [Header("Time of Day (00h00 - 23h59)")]
    [Tooltip("Time of day in decimal hours (0.00 = 00h00 Midnight, 6.00 = 06h00 Sunrise, 12.00 = 12h00 Noon, 18.00 = 18h00 Sunset, 23.98 = 23h59).")]
    [Range(0f, 24f)]
    [SerializeField] private float timeOfDay = 12.0f;

    [Tooltip("Hour of the day (0 to 23).")]
    [Range(0, 23)]
    [SerializeField] private int hours = 12;

    [Tooltip("Minute of the hour (0 to 59).")]
    [Range(0, 59)]
    [SerializeField] private int minutes = 0;

    [Tooltip("Formatted time of day string (00h00 to 23h59).")]
    [ReadOnly]
    [SerializeField] private string timeString = "12h00";

    public float CurrentTimeOfDay
    {
        get => timeOfDay;
        set => SetTimeOfDay(value);
    }

    public int Hours
    {
        get => hours;
        set => SetTimeOfDay(value, minutes);
    }

    public int Minutes
    {
        get => minutes;
        set => SetTimeOfDay(hours, value);
    }

    public string TimeFormatted => $"{hours:D2}h{minutes:D2}";
    public bool IsDay => (timeOfDay >= 6.0f && timeOfDay < 18.0f);
    public bool IsNight => !IsDay;

    #endregion

    #region Cycle Progression

    [Header("Cycle Progression (Play Mode)")]
    [Tooltip("Whether the cycle automatically progresses during Play Mode.")]
    [SerializeField] private bool runCycleInPlayMode = true;

    [Tooltip("Speed of the day/night cycle in Play Mode.")]
    [SerializeField] private CycleSpeed currentCycleSpeed = CycleSpeed.TwelveMinutes;
    public CycleSpeed CurrentCycleSpeed => currentCycleSpeed;

    private float rotateSpeed;
    private float minutesAccumulated;
    private int days;

    #endregion

    #region Scene References

    [Header("Sun & Celestial References")]
    [Tooltip("Pivot transform that rotates the sun (e.g. Sun_Rotation or Revolution Point).")]
    [SerializeField] private Transform sunPivot;

    [Tooltip("The main directional sunlight source.")]
    [SerializeField] private Light sunLight;

    [Tooltip("Pivot transform for moon orbit (optional).")]
    [SerializeField] private Transform moonPivot;

    [Tooltip("Moon visual GameObject (optional).")]
    [SerializeField] private GameObject moonObject;

    [Tooltip("Secondary moonlight directional light (optional).")]
    [SerializeField] private Light moonLight;

    [Tooltip("Twinkling star particle system (optional).")]
    [SerializeField] private ParticleSystem starsParticleSystem;

    [Header("Skybox & Cubemaps")]
    [Tooltip("The material assigned to RenderSettings.skybox using Custom/Skybox shader for seamless blending.")]
    [SerializeField] private Material blendedSkyboxMaterial;

    [SerializeField] private Cubemap nightCubemap;
    [SerializeField] private Cubemap dawnCubemap;
    [SerializeField] private Cubemap morningCubemap;
    [SerializeField] private Cubemap dayCubemap;
    [SerializeField] private Cubemap afternoonCubemap;
    [SerializeField] private Cubemap duskCubemap;

    #endregion

    #region Lighting Curves & Gradients (Smooth Transitions)

    [Header("Smooth Lighting Transitions (00h00 - 24h00)")]
    [Tooltip("Color of the sunlight throughout the 24-hour cycle.")]
    [SerializeField] private Gradient sunColorGradient;

    [Tooltip("Intensity of the sunlight throughout the 24-hour cycle (peaks at 12h00 noon, rises at 06h00, sets at 18h00).")]
    [SerializeField] private AnimationCurve sunIntensityCurve;

    [Tooltip("Ambient lighting intensity throughout the 24-hour cycle.")]
    [SerializeField] private AnimationCurve ambientIntensityCurve;

    [Tooltip("Atmospheric fog color throughout the 24-hour cycle.")]
    [SerializeField] private Gradient fogColorGradient;

    [Tooltip("Intensity of moonlight throughout the 24-hour cycle (active only at night).")]
    [SerializeField] private AnimationCurve moonIntensityCurve;

    [Tooltip("Color of moonlight throughout the 24-hour cycle.")]
    [SerializeField] private Color moonColor = new Color(0.68f, 0.82f, 0.98f);

    [Tooltip("Stars particle system emission rate throughout the 24-hour cycle.")]
    [SerializeField] private AnimationCurve starEmissionCurve;

    private List<DaylightSensor> _daylightSensors = new List<DaylightSensor>();
    private bool? _lastNightState = null;
    private int _lastProcessedHour = -1;
    private int _lastProcessedMinute = -1;

    #endregion

    #region Unity Callbacks

    private void Awake()
    {
        if (Instance == null) Instance = this;
        AutoFindReferences();
        InitializeDefaultCurves(false);
    }

    private void OnEnable()
    {
        if (Instance == null) Instance = this;
        AutoFindReferences();
        InitializeDefaultCurves(false);
        ApplyTimeVisuals();
    }

    private void Start()
    {
        AutoFindReferences();
        InitializeDefaultCurves(false);
        _daylightSensors = FindObjectsByType<DaylightSensor>(FindObjectsSortMode.None).ToList();

        UpdateSpeed();
        ApplyTimeVisuals();

        if (Application.isPlaying)
        {
            DebugLogConsole.AddCommandInstance("SetTime", "Change time of day between 0 and 1 (0 = 00h00, 0.25 = 06h00, 0.5 = 12h00, 0.75 = 18h00)", nameof(SetTime), this);
            DebugLogConsole.AddCommandInstance("SetTimeHours", "Change time of day in hours (0 to 24)", nameof(SetTimeHoursConsole), this);
        }
    }

    private void Update()
    {
        if (Application.isPlaying && runCycleInPlayMode && currentCycleSpeed != CycleSpeed.Paused)
        {
            ProgressCycle(Time.deltaTime);
        }
    }

    private void OnValidate()
    {
        // Keep hours, minutes, and timeOfDay synchronized
        if (timeOfDay >= 24.0f) timeOfDay = 0f;
        if (timeOfDay < 0f) timeOfDay = 23.99f;

        int totalMins = Mathf.Clamp(Mathf.RoundToInt(timeOfDay * 60f), 0, 1439);
        hours = totalMins / 60;
        minutes = totalMins % 60;
        timeString = $"{hours:D2}h{minutes:D2}";

        UpdateSpeed();
        AutoFindReferences();
        InitializeDefaultCurves(false);
        ApplyTimeVisuals();
    }

    #endregion

    #region Curve & Gradient Initialization

    /// <summary>
    /// Initializes smooth, handcrafted curves and gradients for all lighting parameters.
    /// </summary>
    public void InitializeDefaultCurves(bool force)
    {
        // 1. Sun Intensity Curve (0h - 24h normalized 0.0 - 1.0)
        // Sunrise: 06h00 (0.25), Sunset: 18h00 (0.75), Noon: 12h00 (0.50)
        if (sunIntensityCurve == null || sunIntensityCurve.length == 0 || force)
        {
            sunIntensityCurve = new AnimationCurve();
            sunIntensityCurve.AddKey(new Keyframe(0.000f, 0.0f));  // 00h00 (Midnight)
            sunIntensityCurve.AddKey(new Keyframe(0.200f, 0.0f));  // 04h48 (Pre-dawn)
            sunIntensityCurve.AddKey(new Keyframe(0.229f, 0.10f)); // 05h30 (First dawn light)
            sunIntensityCurve.AddKey(new Keyframe(0.250f, 0.40f)); // 06h00 (Sunrise)
            sunIntensityCurve.AddKey(new Keyframe(0.292f, 0.85f)); // 07h00 (Morning)
            sunIntensityCurve.AddKey(new Keyframe(0.375f, 1.15f)); // 09h00
            sunIntensityCurve.AddKey(new Keyframe(0.500f, 1.35f)); // 12h00 (Noon)
            sunIntensityCurve.AddKey(new Keyframe(0.625f, 1.25f)); // 15h00 (Afternoon)
            sunIntensityCurve.AddKey(new Keyframe(0.708f, 1.00f)); // 17h00 (Golden hour)
            sunIntensityCurve.AddKey(new Keyframe(0.729f, 0.75f)); // 17h30 (Sunset approaches)
            sunIntensityCurve.AddKey(new Keyframe(0.750f, 0.40f)); // 18h00 (Sunset)
            sunIntensityCurve.AddKey(new Keyframe(0.771f, 0.10f)); // 18h30 (Afterglow)
            sunIntensityCurve.AddKey(new Keyframe(0.800f, 0.0f));  // 19h12 (Night begins)
            sunIntensityCurve.AddKey(new Keyframe(1.000f, 0.0f));  // 24h00

            for (int i = 0; i < sunIntensityCurve.length; i++)
            {
                sunIntensityCurve.SmoothTangents(i, 0f);
            }
        }

        // 2. Sun Color Gradient
        if (sunColorGradient == null || sunColorGradient.colorKeys.Length <= 1 || force)
        {
            sunColorGradient = new Gradient();
            sunColorGradient.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(new Color(0.25f, 0.35f, 0.55f), 0.000f), // 00h00 Midnight indigo
                    new GradientColorKey(new Color(0.95f, 0.48f, 0.35f), 0.220f), // 05h17 Dawn rose
                    new GradientColorKey(new Color(1.00f, 0.72f, 0.42f), 0.250f), // 06h00 Sunrise golden peach
                    new GradientColorKey(new Color(1.00f, 0.92f, 0.78f), 0.300f), // 07h12 Warm morning
                    new GradientColorKey(new Color(1.00f, 0.98f, 0.94f), 0.500f), // 12h00 Clean midday sun
                    new GradientColorKey(new Color(1.00f, 0.82f, 0.55f), 0.700f), // 16h48 Golden hour
                    new GradientColorKey(new Color(1.00f, 0.52f, 0.22f), 0.750f), // 18h00 Fiery amber sunset
                    new GradientColorKey(new Color(0.85f, 0.32f, 0.28f), 0.790f)  // 18h57 Dusk crimson
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(1.0f, 0.0f),
                    new GradientAlphaKey(1.0f, 1.0f)
                }
            );
        }

        // 3. Fog Color Gradient
        if (fogColorGradient == null || fogColorGradient.colorKeys.Length <= 1 || force)
        {
            fogColorGradient = new Gradient();
            fogColorGradient.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(new Color(0.04f, 0.06f, 0.12f), 0.000f), // 00h00 Midnight navy
                    new GradientColorKey(new Color(0.38f, 0.28f, 0.42f), 0.220f), // 05h17 Dawn violet
                    new GradientColorKey(new Color(0.78f, 0.60f, 0.55f), 0.250f), // 06h00 Sunrise peach haze
                    new GradientColorKey(new Color(0.72f, 0.80f, 0.80f), 0.300f), // 07h12 Soft morning haze
                    new GradientColorKey(new Color(0.78f, 0.88f, 0.85f), 0.500f), // 12h00 Daytime mint/cyan (#C7E0D8)
                    new GradientColorKey(new Color(0.82f, 0.70f, 0.58f), 0.700f), // 16h48 Warm afternoon haze
                    new GradientColorKey(new Color(0.78f, 0.48f, 0.30f), 0.750f), // 18h00 Sunset orange fog
                    new GradientColorKey(new Color(0.20f, 0.15f, 0.25f), 0.800f)  // 19h12 Twilight purple
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(1.0f, 0.0f),
                    new GradientAlphaKey(1.0f, 1.0f)
                }
            );
        }

        // 4. Ambient Intensity Curve
        if (ambientIntensityCurve == null || ambientIntensityCurve.length == 0 || force)
        {
            ambientIntensityCurve = new AnimationCurve();
            ambientIntensityCurve.AddKey(new Keyframe(0.000f, 0.35f)); // Midnight
            ambientIntensityCurve.AddKey(new Keyframe(0.200f, 0.35f)); // Pre-dawn
            ambientIntensityCurve.AddKey(new Keyframe(0.250f, 0.55f)); // Sunrise (06h00)
            ambientIntensityCurve.AddKey(new Keyframe(0.350f, 0.75f)); // Morning
            ambientIntensityCurve.AddKey(new Keyframe(0.500f, 0.90f)); // Noon (12h00)
            ambientIntensityCurve.AddKey(new Keyframe(0.650f, 0.80f)); // Afternoon
            ambientIntensityCurve.AddKey(new Keyframe(0.750f, 0.55f)); // Sunset (18h00)
            ambientIntensityCurve.AddKey(new Keyframe(0.820f, 0.35f)); // Night
            ambientIntensityCurve.AddKey(new Keyframe(1.000f, 0.35f));

            for (int i = 0; i < ambientIntensityCurve.length; i++)
            {
                ambientIntensityCurve.SmoothTangents(i, 0f);
            }
        }

        // 5. Moon Intensity Curve
        if (moonIntensityCurve == null || moonIntensityCurve.length == 0 || force)
        {
            moonIntensityCurve = new AnimationCurve();
            moonIntensityCurve.AddKey(new Keyframe(0.000f, 0.18f)); // Midnight peak
            moonIntensityCurve.AddKey(new Keyframe(0.200f, 0.15f)); // Late night
            moonIntensityCurve.AddKey(new Keyframe(0.230f, 0.08f)); // Dawn begins
            moonIntensityCurve.AddKey(new Keyframe(0.250f, 0.00f)); // Sunrise (06h00)
            moonIntensityCurve.AddKey(new Keyframe(0.730f, 0.00f)); // Day
            moonIntensityCurve.AddKey(new Keyframe(0.750f, 0.03f)); // Sunset (18h00)
            moonIntensityCurve.AddKey(new Keyframe(0.780f, 0.09f)); // Dusk
            moonIntensityCurve.AddKey(new Keyframe(0.850f, 0.16f)); // Night
            moonIntensityCurve.AddKey(new Keyframe(1.000f, 0.18f));

            for (int i = 0; i < moonIntensityCurve.length; i++)
            {
                moonIntensityCurve.SmoothTangents(i, 0f);
            }
        }

        // 6. Star Emission Curve
        if (starEmissionCurve == null || starEmissionCurve.length == 0 || force)
        {
            starEmissionCurve = new AnimationCurve();
            starEmissionCurve.AddKey(new Keyframe(0.000f, 900f)); // Midnight
            starEmissionCurve.AddKey(new Keyframe(0.200f, 900f)); // Night
            starEmissionCurve.AddKey(new Keyframe(0.230f, 350f)); // Dawn
            starEmissionCurve.AddKey(new Keyframe(0.260f, 0f));   // Sunrise (06h00+)
            starEmissionCurve.AddKey(new Keyframe(0.730f, 0f));   // Day
            starEmissionCurve.AddKey(new Keyframe(0.760f, 200f)); // Sunset (18h00+)
            starEmissionCurve.AddKey(new Keyframe(0.820f, 750f)); // Dusk
            starEmissionCurve.AddKey(new Keyframe(0.860f, 900f)); // Night
            starEmissionCurve.AddKey(new Keyframe(1.000f, 900f));

            for (int i = 0; i < starEmissionCurve.length; i++)
            {
                starEmissionCurve.SmoothTangents(i, 0f);
            }
        }
    }

    #endregion

    #region Auto Finding References

    public void AutoFindReferences()
    {
        // 1. Sun Pivot
        if (sunPivot == null)
        {
            var sr = GameObject.Find("Day-Night-Seasons_Controller/Sun_Rotation");
            if (sr != null) sunPivot = sr.transform;
            else
            {
                var rev = GameObject.Find("Revolution Point");
                if (rev != null) sunPivot = rev.transform;
                else sunPivot = transform;
            }
        }

        // 2. Sun Light
        if (sunLight == null)
        {
            if (sunPivot != null)
            {
                sunLight = sunPivot.GetComponentInChildren<Light>(true);
            }
            if (sunLight == null && RenderSettings.sun != null)
            {
                sunLight = RenderSettings.sun;
            }
        }

        // 3. Moon Pivot & Moon
        if (moonPivot == null)
        {
            var mp = GameObject.Find("Day-Night-Seasons_Controller/CelestialBodies/Moon_Pivot");
            if (mp != null) moonPivot = mp.transform;
        }

        if (moonObject == null && moonPivot != null)
        {
            var m = moonPivot.Find("Moon");
            if (m != null) moonObject = m.gameObject;
        }

        // 4. Moon Light
        if (moonLight == null)
        {
            var mlGo = GameObject.Find("MoonLight");
            if (mlGo != null) moonLight = mlGo.GetComponent<Light>();
        }

        // 5. Stars Particle System
        if (starsParticleSystem == null)
        {
            var starsGo = GameObject.Find("Day-Night-Seasons_Controller/CelestialBodies/Stars");
            if (starsGo != null) starsParticleSystem = starsGo.GetComponent<ParticleSystem>();
        }

        // 6. Skybox Cubemaps & Blended Material
#if UNITY_EDITOR
        if (nightCubemap == null) nightCubemap = AssetDatabase.LoadAssetAtPath<Cubemap>("Assets/Scenes/MainScene/Skybox/Night.png");
        if (dawnCubemap == null) dawnCubemap = AssetDatabase.LoadAssetAtPath<Cubemap>("Assets/Scenes/MainScene/Skybox/Dawn.png");
        if (morningCubemap == null) morningCubemap = AssetDatabase.LoadAssetAtPath<Cubemap>("Assets/Scenes/MainScene/Skybox/Morning.png");
        if (dayCubemap == null) dayCubemap = AssetDatabase.LoadAssetAtPath<Cubemap>("Assets/Scenes/MainScene/Skybox/Day.png");
        if (afternoonCubemap == null) afternoonCubemap = AssetDatabase.LoadAssetAtPath<Cubemap>("Assets/Scenes/MainScene/Skybox/Afternoon.png");
        if (duskCubemap == null) duskCubemap = AssetDatabase.LoadAssetAtPath<Cubemap>("Assets/Scenes/MainScene/Skybox/Dusk.png");

        if (blendedSkyboxMaterial == null)
        {
            blendedSkyboxMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Scenes/MainScene/Skybox/BlendedSkybox.mat");
            if (blendedSkyboxMaterial == null)
            {
                var s = Shader.Find("Custom/Skybox");
                if (s != null)
                {
                    blendedSkyboxMaterial = new Material(s);
                    AssetDatabase.CreateAsset(blendedSkyboxMaterial, "Assets/Scenes/MainScene/Skybox/BlendedSkybox.mat");
                }
            }
        }
#endif
    }

    #endregion

    #region Cycle Progression Logic

    private void ProgressCycle(float deltaTime)
    {
        // 1440 minutes in a day. 
        // rotateSpeed is in degrees per second (360 degrees = 1440 minutes).
        float minutesPerSecond = (rotateSpeed / 360f) * 1440f;
        float deltaMinutes = minutesPerSecond * deltaTime;

        minutesAccumulated += deltaMinutes;
        float newTimeOfDay = timeOfDay + (deltaMinutes / 60f);

        if (newTimeOfDay >= 24f)
        {
            newTimeOfDay -= 24f;
            days++;
        }

        timeOfDay = newTimeOfDay;
        int totalMinutes = Mathf.Clamp(Mathf.FloorToInt(timeOfDay * 60f), 0, 1439);
        int currentHour = totalMinutes / 60;
        int currentMinute = totalMinutes % 60;

        hours = currentHour;
        minutes = currentMinute;
        timeString = $"{hours:D2}h{minutes:D2}";

        ApplyTimeVisuals();

        if (currentMinute != _lastProcessedMinute)
        {
            _lastProcessedMinute = currentMinute;
            OnMinutesChange(totalMinutes);
        }

        if (currentHour != _lastProcessedHour)
        {
            _lastProcessedHour = currentHour;
            OnHourChange(currentHour);
        }
    }

    private void UpdateSpeed()
    {
        rotateSpeed = currentCycleSpeed switch
        {
            CycleSpeed.TwelveMinutes => 360f / (12 * 60),
            CycleSpeed.FourtyEightMinutes => 360f / (48 * 60),
            CycleSpeed.TwoHours => 360f / (2 * 60 * 60),
            CycleSpeed.SixHours => 360f / (6 * 60 * 60),
            CycleSpeed.TwelveHours => 360f / (12 * 60 * 60),
            CycleSpeed.OneDay => 360f / (24 * 60 * 60),
            CycleSpeed.Paused => 0f,
            _ => 0f
        };
    }

    public void SetCycleSpeed(CycleSpeed cycleSpeed)
    {
        currentCycleSpeed = cycleSpeed;
        UpdateSpeed();
    }

    #endregion

    #region Visual Application (Sun, Moon, Stars, Skybox, Lights)

    /// <summary>
    /// Applies the celestial positioning, lighting colors/intensities, stars, and skybox blending
    /// with silky-smooth continuous transitions for the current timeOfDay. Works in both Edit Mode and Play Mode.
    /// </summary>
    public void ApplyTimeVisuals()
    {
        float normalizedTime = timeOfDay / 24f; // 0.0 to 1.0

        // 1. Sun Rotation
        // Radian angle: 0 at 06h00 (Sunrise), PI/2 at 12h00 (Noon), PI at 18h00 (Sunset), 3PI/2 at 00h00 (Midnight)
        float rad = (timeOfDay - 6f) / 24f * Mathf.PI * 2f;
        float elevation = Mathf.Sin(rad) * 65f;
        float azimuth = (timeOfDay >= 6f && timeOfDay <= 18f)
            ? Mathf.Lerp(40f, 220f, (timeOfDay - 6f) / 12f)
            : Mathf.Lerp(220f, 400f, (timeOfDay > 18f ? (timeOfDay - 18f) / 12f : (timeOfDay + 6f) / 12f));

        if (sunPivot != null)
        {
            sunPivot.rotation = Quaternion.Euler(elevation, azimuth, 0f);
        }

        // 2. Sunlight Color & Intensity via Continuous Curves
        if (sunLight != null)
        {
            float intensity = sunIntensityCurve != null ? Mathf.Max(0f, sunIntensityCurve.Evaluate(normalizedTime)) : 1.0f;
            Color color = sunColorGradient != null ? sunColorGradient.Evaluate(normalizedTime) : Color.white;

            sunLight.intensity = intensity;
            sunLight.color = color;
            sunLight.enabled = (intensity > 0.001f);
        }

        // 3. Ambient Intensity & Fog Color (Smooth Environment Transitions)
        if (ambientIntensityCurve != null)
        {
            RenderSettings.ambientIntensity = ambientIntensityCurve.Evaluate(normalizedTime);
        }

        if (RenderSettings.fog && fogColorGradient != null)
        {
            RenderSettings.fogColor = fogColorGradient.Evaluate(normalizedTime);
        }

        // 4. Moon & Moonlight
        float moonIntensity = moonIntensityCurve != null ? Mathf.Max(0f, moonIntensityCurve.Evaluate(normalizedTime)) : 0f;
        if (moonLight != null)
        {
            moonLight.intensity = moonIntensity;
            moonLight.color = moonColor;
            moonLight.enabled = (moonIntensity > 0.001f);
        }

        if (moonPivot != null)
        {
            float moonRad = (timeOfDay - 18f) / 24f * Mathf.PI * 2f;
            float moonElev = Mathf.Sin(moonRad) * 55f;
            float moonAzimuth = Mathf.Lerp(220f, 40f, (timeOfDay >= 18f) ? (timeOfDay - 18f) / 12f : (timeOfDay + 6f) / 12f);
            moonPivot.rotation = Quaternion.Euler(moonElev, moonAzimuth, 0f);
        }

        if (moonObject != null)
        {
            // Moon object smoothly active when in view (from pre-sunset to dawn)
            moonObject.SetActive(timeOfDay > 17.0f || timeOfDay < 6.8f);
        }

        // 5. Stars (Twinkling particles fade in gradually during dusk, fade out at dawn)
        if (starsParticleSystem != null)
        {
            float targetEmission = starEmissionCurve != null ? Mathf.Max(0f, starEmissionCurve.Evaluate(normalizedTime)) : 0f;
            var emission = starsParticleSystem.emission;
            emission.rateOverTime = targetEmission;
        }

        // 6. Skybox Continuous S-Curve Blending (No material swaps, 100% continuous)
        UpdateSkyboxSmooth(timeOfDay);

        // 7. Daylight Sensors (Streetlights & Lamps)
        bool isNightTime = (timeOfDay < 5.75f || timeOfDay >= 17.75f);
        UpdateDaylightSensors(isNightTime);

        // 8. Update Environment reflections/GI
        DynamicGI.UpdateEnvironment();
    }

    private void UpdateSkyboxSmooth(float t)
    {
        if (blendedSkyboxMaterial == null) return;

        Cubemap tex1 = null;
        Cubemap tex2 = null;
        float blend = 0f;

        // Transitions:
        // 00:00 - 04:30: Deep Night (Night -> Night, blend 0)
        // 04:30 - 06:00: Night -> Dawn (Sunrise at 06h00 reaches full Dawn)
        // 06:00 - 08:30: Dawn -> Morning
        // 08:30 - 13:30: Morning -> Day
        // 13:30 - 17:00: Day -> Afternoon
        // 17:00 - 18:30: Afternoon -> Dusk (Sunset at 18h00 blends deeply into Dusk)
        // 18:30 - 21:00: Dusk -> Night
        // 21:00 - 24:00: Deep Night
        if (t < 4.5f)
        {
            tex1 = nightCubemap;
            tex2 = dawnCubemap;
            blend = 0.0f; // 100% Night
        }
        else if (t < 6.0f)
        {
            tex1 = nightCubemap;
            tex2 = dawnCubemap;
            blend = Mathf.SmoothStep(0f, 1f, (t - 4.5f) / 1.5f);
        }
        else if (t < 8.5f)
        {
            tex1 = dawnCubemap;
            tex2 = morningCubemap;
            blend = Mathf.SmoothStep(0f, 1f, (t - 6.0f) / 2.5f);
        }
        else if (t < 13.5f)
        {
            tex1 = morningCubemap;
            tex2 = dayCubemap;
            blend = Mathf.SmoothStep(0f, 1f, (t - 8.5f) / 5.0f);
        }
        else if (t < 17.0f)
        {
            tex1 = dayCubemap;
            tex2 = afternoonCubemap;
            blend = Mathf.SmoothStep(0f, 1f, (t - 13.5f) / 3.5f);
        }
        else if (t < 18.5f)
        {
            tex1 = afternoonCubemap;
            tex2 = duskCubemap;
            blend = Mathf.SmoothStep(0f, 1f, (t - 17.0f) / 1.5f);
        }
        else if (t < 21.0f)
        {
            tex1 = duskCubemap;
            tex2 = nightCubemap;
            blend = Mathf.SmoothStep(0f, 1f, (t - 18.5f) / 2.5f);
        }
        else
        {
            tex1 = nightCubemap;
            tex2 = dawnCubemap;
            blend = 0.0f; // 100% Night
        }

        if (tex1 != null) blendedSkyboxMaterial.SetTexture("_Tex1", tex1);
        if (tex2 != null) blendedSkyboxMaterial.SetTexture("_Tex2", tex2);
        blendedSkyboxMaterial.SetFloat("_Blend", blend);

        if (RenderSettings.skybox != blendedSkyboxMaterial)
        {
            RenderSettings.skybox = blendedSkyboxMaterial;
        }
    }

    private void UpdateDaylightSensors(bool isNight)
    {
        if (_lastNightState == isNight) return;
        _lastNightState = isNight;

        if (_daylightSensors == null || _daylightSensors.Count == 0)
        {
            _daylightSensors = FindObjectsByType<DaylightSensor>(FindObjectsSortMode.None).ToList();
        }

        foreach (var sensor in _daylightSensors)
        {
            if (sensor == null) continue;
            try
            {
                if (isNight) sensor.TurnLightsOn();
                else sensor.TurnLightsOff();
            }
            catch
            {
                // In edit mode or scene load, sensors may be uninitialized
            }
        }
    }

    #endregion

    #region Event Handlers

    private void OnMinutesChange(int totalMinutes)
    {
        // Periodic updates if needed
    }

    private void OnHourChange(int currentHour)
    {
        if (Application.isPlaying)
        {
            if (ChurchTimer.Instance != null)
            {
                StartCoroutine(ChurchTimer.Instance.TryRingBells(currentHour));
            }

            if (StaticAmbianceManager.Instance != null)
            {
                if (currentHour >= 5 && currentHour < 9)
                {
                    StaticAmbianceManager.Instance.CurrentTimeOfDay = StaticAmbianceManager.TimeOfDay.Morning;
                }
                else if (currentHour >= 9 && currentHour < 18)
                {
                    StaticAmbianceManager.Instance.CurrentTimeOfDay = StaticAmbianceManager.TimeOfDay.Day;
                }
                else
                {
                    StaticAmbianceManager.Instance.CurrentTimeOfDay = StaticAmbianceManager.TimeOfDay.Night;
                }
            }

            if (MusicManager.Instance != null)
            {
                MusicManager.Instance.IsDay = (currentHour >= 6 && currentHour < 18);
            }
        }
    }

    #endregion

    #region Public Control Methods

    /// <summary>
    /// Sets the time of day in decimal hours between 0.0f (00h00) and 24.0f (24h00).
    /// </summary>
    public void SetTimeOfDay(float hoursFloat)
    {
        hoursFloat = Mathf.Repeat(hoursFloat, 24f);
        timeOfDay = hoursFloat;

        int totalMins = Mathf.Clamp(Mathf.RoundToInt(timeOfDay * 60f), 0, 1439);
        hours = totalMins / 60;
        minutes = totalMins % 60;
        timeString = $"{hours:D2}h{minutes:D2}";

        ApplyTimeVisuals();

        if (Application.isPlaying)
        {
            OnHourChange(hours);
            OnMinutesChange(totalMins);
        }
    }

    /// <summary>
    /// Sets the time of day by specific hour (0-23) and minute (0-59).
    /// </summary>
    public void SetTimeOfDay(int targetHour, int targetMinute)
    {
        targetHour = Mathf.Clamp(targetHour, 0, 23);
        targetMinute = Mathf.Clamp(targetMinute, 0, 59);

        hours = targetHour;
        minutes = targetMinute;
        timeOfDay = targetHour + (targetMinute / 60f);
        timeString = $"{hours:D2}h{minutes:D2}";

        ApplyTimeVisuals();

        if (Application.isPlaying)
        {
            OnHourChange(hours);
            OnMinutesChange(hours * 60 + minutes);
        }
    }

    /// <summary>
    /// Sets normalized time of day between 0.0 (00h00) and 1.0 (24h00).
    /// Used by console commands and external systems.
    /// </summary>
    public void SetTime(float normalizedTime)
    {
        SetTimeOfDay(Mathf.Clamp01(normalizedTime) * 24f);
        Debug.Log($"Changed time of day to {timeString} (normalized: {normalizedTime:F2})");
    }

    public void SetTimeHoursConsole(float hoursFloat)
    {
        SetTimeOfDay(hoursFloat);
        Debug.Log($"Changed time of day to {timeString} ({hoursFloat:F2}h)");
    }

    [Button("Sunrise (06h00)")]
    public void SetSunrise() => SetTimeOfDay(6, 0);

    [Button("Noon (12h00)")]
    public void SetNoon() => SetTimeOfDay(12, 0);

    [Button("Sunset (18h00)")]
    public void SetSunset() => SetTimeOfDay(18, 0);

    [Button("Midnight (00h00)")]
    public void SetMidnight() => SetTimeOfDay(0, 0);

    [Button("Set Day (12h00)")]
    public void SetDay() => SetNoon();

    [Button("Set Night (00h00)")]
    public void SetNight() => SetMidnight();

    public int GetMinutes() => hours * 60 + minutes;
    public CycleSpeed GetSpeed() => currentCycleSpeed;

    #endregion
}
