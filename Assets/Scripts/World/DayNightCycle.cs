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
    [Tooltip("Pivot transform that rotates the sun (e.g. Sun_Rotation or Revolution Point). Arcs continuously across the sky.")]
    [SerializeField] private Transform sunPivot;

    [Tooltip("Optional dummy visual transform for the sun (mesh, billboard, flare). Arcs continuously every frame.")]
    [SerializeField] private Transform dummySunVisual;

    [Tooltip("The main directional sunlight source.")]
    [SerializeField] private Light sunLight;

    [Tooltip("Optional secondary directional sky fill light (no shadows) for warm/cool color depth.")]
    [SerializeField] private Light skyFillLight;

    [Tooltip("Pivot transform for moon orbit (optional).")]
    [SerializeField] private Transform moonPivot;

    [Tooltip("Moon visual GameObject (optional).")]
    [SerializeField] private GameObject moonObject;

    [Tooltip("Secondary moonlight directional light (optional).")]
    [SerializeField] private Light moonLight;

    [Tooltip("Twinkling star particle system (optional).")]
    [SerializeField] private ParticleSystem starsParticleSystem;

    [Header("Shadow Motion & Stabilization")]
    [Tooltip("Whether to use stepped stealth-gliding for the shadow-casting light to eliminate shadow wobbling/crawling.")]
    [SerializeField] private bool useStabilizedShadows = true;

    [Tooltip("Interval (in real-time seconds) between shadow light orientation updates. Recommended: 5 to 10 seconds.")]
    [Range(2f, 20f)]
    [SerializeField] private float shadowStepInterval = 7.0f;

    [Tooltip("Duration (in seconds) of the smooth Slerp transition when catching up to the sun. Prevents 1-frame snapping.")]
    [Range(0.2f, 2.5f)]
    [SerializeField] private float shadowGlideDuration = 0.8f;

    [Tooltip("Minimum elevation angle (degrees) for the shadow-casting sun light. Clamping prevents grazing angles from creating stretched flickering shadows.")]
    [Range(10f, 30f)]
    [SerializeField] private float minSunElevation = 15.0f;

    [Tooltip("Maximum elevation angle (degrees) reached at midday noon.")]
    [Range(45f, 85f)]
    [SerializeField] private float maxSunElevation = 65.0f;

    private float _timeSinceLastShadowStep = 0f;
    private Quaternion _currentLightRotation;
    private Quaternion _startLightRotation;
    private Quaternion _targetLightRotation;
    private bool _isGliding = false;
    private float _glideTimer = 0f;

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

    [Header("Environment & GI Update Settings")]
    [Tooltip("Whether to update DynamicGI environment probe during play mode.")]
    [SerializeField] private bool updateEnvironmentGI = true;

    [Tooltip("How frequently (in real-time seconds) DynamicGI.UpdateEnvironment is called. Set to 0 to disable periodic probe updates. Recommended: 3 to 10 seconds to avoid ambient probe thrashing.")]
    [SerializeField] private float environmentUpdateInterval = 5.0f;
    private float _lastEnvironmentUpdateTime = -999f;

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
        ResetShadowStabilization();
    }

    private void OnEnable()
    {
        if (Instance == null) Instance = this;
        AutoFindReferences();
        InitializeDefaultCurves(false);
        ResetShadowStabilization();
        ApplyTimeVisuals(0f);
    }

    private void Start()
    {
        AutoFindReferences();
        InitializeDefaultCurves(false);
        _daylightSensors = FindObjectsByType<DaylightSensor>(FindObjectsSortMode.None).ToList();

        UpdateSpeed();
        ResetShadowStabilization();
        ApplyTimeVisuals(0f);

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
        else if (Application.isPlaying && _isGliding)
        {
            // If the cycle is paused while a glide is in progress, finish the glide cleanly
            UpdateShadowStabilization(Time.deltaTime, CalculateSunRotation(timeOfDay, clampElevation: true));
            if (sunLight != null)
            {
                sunLight.transform.rotation = _currentLightRotation;
            }
        }
    }

    private void LateUpdate()
    {
        // Enforce world rotation freeze on sunLight so parent transforms cannot alter it
        if (Application.isPlaying && useStabilizedShadows && sunLight != null)
        {
            sunLight.transform.rotation = _currentLightRotation;
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
        ResetShadowStabilization();
        if (Application.isPlaying)
        {
            ApplyTimeVisuals(0f);
        }
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
                    new GradientColorKey(new Color(0.45f, 0.65f, 0.98f), 0.220f), // 05h17 Dawn blue
                    new GradientColorKey(new Color(0.68f, 0.80f, 1.00f), 0.250f), // 06h00 Sunrise cool blue
                    new GradientColorKey(new Color(1.00f, 0.92f, 0.78f), 0.300f), // 07h12 Warm morning
                    new GradientColorKey(new Color(1.00f, 0.98f, 0.94f), 0.500f), // 12h00 Clean midday sun
                    new GradientColorKey(new Color(1.00f, 0.82f, 0.55f), 0.700f), // 16h48 Golden hour
                    new GradientColorKey(new Color(1.00f, 0.48f, 0.20f), 0.750f), // 18h00 Fiery orange-red sunset
                    new GradientColorKey(new Color(0.85f, 0.28f, 0.22f), 0.790f)  // 18h57 Dusk reddish
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
            moonIntensityCurve.AddKey(new Keyframe(0.230f, 0.10f)); // Dawn begins
            moonIntensityCurve.AddKey(new Keyframe(0.250f, 0.06f)); // Sunrise overlap (06h00)
            moonIntensityCurve.AddKey(new Keyframe(0.270f, 0.00f)); // Post-sunrise fade out (06h29)
            moonIntensityCurve.AddKey(new Keyframe(0.720f, 0.00f)); // Day
            moonIntensityCurve.AddKey(new Keyframe(0.750f, 0.06f)); // Sunset overlap (18h00)
            moonIntensityCurve.AddKey(new Keyframe(0.780f, 0.10f)); // Dusk
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
        // 1. Sun Pivot (The Celestial Orbit / Dummy Visual)
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

        // 1b. Dummy Sun Visual (Optional dedicated visual transform in sky)
        if (dummySunVisual == null && sunPivot != null)
        {
            var visual = sunPivot.Find("SunVisual") ?? sunPivot.Find("Visual");
            if (visual != null) dummySunVisual = visual;
        }

        // 2. Sun Light (The Shadow-Casting Directional Light)
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

        // 2b. Sky Fill Light (Optional secondary directional fill)
        if (skyFillLight == null)
        {
            var fillGo = GameObject.Find("SkyFillLight") ?? GameObject.Find("Day-Night-Seasons_Controller/SkyFillLight");
            if (fillGo != null) skyFillLight = fillGo.GetComponent<Light>();
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

        ApplyTimeVisuals(deltaTime);

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
    /// Applies celestial positioning, stabilized lighting colors/intensities, stars, and skybox blending.
    /// Works in both Edit Mode and Play Mode.
    /// </summary>
    public void ApplyTimeVisuals(float deltaTime = 0f)
    {
        float normalizedTime = timeOfDay / 24f; // 0.0 to 1.0

        // 1. Visual Sun Celestial Orbit (The Dummy Sun)
        // Rotates smoothly every frame following true celestial astronomical coordinates
        Quaternion continuousSunRotation = CalculateSunRotation(timeOfDay, clampElevation: false);

        if (sunPivot != null)
        {
            sunPivot.rotation = continuousSunRotation;
        }

        if (dummySunVisual != null && dummySunVisual != sunPivot)
        {
            dummySunVisual.rotation = continuousSunRotation;
        }

        // 2. Shadow-Casting Directional Light (The Stabilized Shadow Driver)
        Quaternion targetShadowRotation = CalculateSunRotation(timeOfDay, clampElevation: true);

        if (!Application.isPlaying || !useStabilizedShadows || deltaTime <= 0f)
        {
            // In Edit Mode, time scrubbing, or stabilization disabled: snap immediately
            _currentLightRotation = targetShadowRotation;
            _isGliding = false;
            _timeSinceLastShadowStep = 0f;
        }
        else
        {
            // In Play Mode with stabilization enabled: stealth glide every shadowStepInterval seconds
            UpdateShadowStabilization(deltaTime, targetShadowRotation);
        }

        if (sunLight != null)
        {
            sunLight.transform.rotation = _currentLightRotation;
        }

        // 3. Sunlight Color & Intensity (Continuous per-frame daylight formula & curve)
        if (sunLight != null)
        {
            float baseIntensity = sunIntensityCurve != null ? Mathf.Max(0f, sunIntensityCurve.Evaluate(normalizedTime)) : CalculateDaylightIntensity(timeOfDay);
            // Smoothly fade out intensity as sun approaches the horizon to prevent grazing acne
            float elevFade = CalculateElevationFade(timeOfDay);
            float finalIntensity = baseIntensity * elevFade;

            Color color = sunColorGradient != null ? sunColorGradient.Evaluate(normalizedTime) : CalculateSunColor(timeOfDay);

            sunLight.intensity = finalIntensity;
            sunLight.color = color;
            sunLight.enabled = (finalIntensity > 0.001f);
        }

        // 4. Optional Secondary Sky Fill Light (Ambient depth without shadow overhead)
        if (skyFillLight != null)
        {
            float fillIntensity = (sunLight != null && sunLight.enabled) ? sunLight.intensity * 0.22f : 0f;
            skyFillLight.intensity = fillIntensity;
            skyFillLight.color = CalculateSkyFillColor(timeOfDay);
            skyFillLight.shadows = LightShadows.None; // Never cast shadows to avoid extra shadow passes
            skyFillLight.enabled = (fillIntensity > 0.001f);

            // Orient downward from the sky hemisphere opposite the sun
            float azimuth = (timeOfDay >= 6f && timeOfDay <= 18f)
                ? Mathf.Lerp(40f, 220f, (timeOfDay - 6f) / 12f)
                : 220f;
            skyFillLight.transform.rotation = Quaternion.Euler(60f, azimuth + 180f, 0f);
        }

        // 5. Ambient Intensity, Ambient Tint & Fog Color (Smooth Environment Transitions)
        if (ambientIntensityCurve != null)
        {
            RenderSettings.ambientIntensity = ambientIntensityCurve.Evaluate(normalizedTime);
        }
        RenderSettings.ambientLight = CalculateAmbientColor(timeOfDay);

        if (RenderSettings.fog && fogColorGradient != null)
        {
            RenderSettings.fogColor = fogColorGradient.Evaluate(normalizedTime);
        }

        // 6. Moon & Moonlight (with dawn/dusk overlap floor so sun/moon handoff never goes pitch black)
        float moonIntensity = moonIntensityCurve != null ? Mathf.Max(0f, moonIntensityCurve.Evaluate(normalizedTime)) : 0f;
        if (timeOfDay >= 5.25f && timeOfDay <= 6.5f)
        {
            float dawnBridge = Mathf.InverseLerp(6.5f, 5.5f, timeOfDay) * 0.08f;
            moonIntensity = Mathf.Max(moonIntensity, dawnBridge);
        }
        else if (timeOfDay >= 17.5f && timeOfDay <= 18.75f)
        {
            float duskBridge = Mathf.InverseLerp(17.5f, 18.5f, timeOfDay) * 0.08f;
            moonIntensity = Mathf.Max(moonIntensity, duskBridge);
        }
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

        // 8. Update Environment reflections/GI (throttled in Play Mode, immediate on scrub in Edit Mode)
        if (!Application.isPlaying)
        {
            DynamicGI.UpdateEnvironment();
        }
        else if (updateEnvironmentGI && environmentUpdateInterval > 0f)
        {
            if (Time.time - _lastEnvironmentUpdateTime >= environmentUpdateInterval)
            {
                _lastEnvironmentUpdateTime = Time.time;
                DynamicGI.UpdateEnvironment();
            }
        }
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

        ResetShadowStabilization();
        ApplyTimeVisuals(0f);

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

        ResetShadowStabilization();
        ApplyTimeVisuals(0f);

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

    #region Mathematical Solar Formulas & Stabilization

    /// <summary>
    /// Resets shadow stabilization timers and snaps the shadow light immediately to its target angle.
    /// Useful when scrubbing time in editor or teleporting time via code.
    /// </summary>
    public void ResetShadowStabilization()
    {
        _isGliding = false;
        _timeSinceLastShadowStep = 0f;
        _currentLightRotation = CalculateSunRotation(timeOfDay, clampElevation: true);
        if (sunLight != null)
        {
            sunLight.transform.rotation = _currentLightRotation;
        }
    }

    /// <summary>
    /// Updates the stealth glide state machine for the shadow-casting directional light.
    /// Freezes the light for shadowStepInterval seconds, then gently slerps over shadowGlideDuration.
    /// </summary>
    private void UpdateShadowStabilization(float deltaTime, Quaternion targetRotation)
    {
        if (!_isGliding)
        {
            _timeSinceLastShadowStep += deltaTime;
            if (_timeSinceLastShadowStep >= shadowStepInterval)
            {
                _timeSinceLastShadowStep = 0f;
                _startLightRotation = _currentLightRotation;
                _targetLightRotation = targetRotation;
                _isGliding = true;
                _glideTimer = 0f;
            }
        }

        if (_isGliding)
        {
            _glideTimer += deltaTime;
            float progress = Mathf.Clamp01(_glideTimer / Mathf.Max(0.01f, shadowGlideDuration));
            float smoothT = Mathf.SmoothStep(0f, 1f, progress);

            _currentLightRotation = Quaternion.Slerp(_startLightRotation, _targetLightRotation, smoothT);

            if (progress >= 1.0f)
            {
                _isGliding = false;
                _currentLightRotation = _targetLightRotation;
            }
        }
    }

    /// <summary>
    /// Calculates celestial orientation (elevation and azimuth) for a given time of day.
    /// When clampElevation is true, elevation is clamped to minSunElevation to prevent extreme grazing shadows.
    /// </summary>
    public Quaternion CalculateSunRotation(float t, bool clampElevation = false)
    {
        float rad = (t - 6f) / 24f * Mathf.PI * 2f;
        float elevation = Mathf.Sin(rad) * maxSunElevation;

        if (clampElevation && (t >= 5.5f && t <= 18.5f))
        {
            elevation = Mathf.Max(elevation, minSunElevation);
        }

        float azimuth = (t >= 6f && t <= 18f)
            ? Mathf.Lerp(40f, 220f, (t - 6f) / 12f)
            : Mathf.Lerp(220f, 400f, (t > 18f ? (t - 18f) / 12f : (t + 6f) / 12f));

        return Quaternion.Euler(elevation, azimuth, 0f);
    }

    /// <summary>
    /// Smoothly fades out direct sunlight intensity as the true sun approaches the horizon (< minSunElevation),
    /// completely preventing elongated grazing shadows or shadow acne at dawn and dusk.
    /// </summary>
    public float CalculateElevationFade(float t)
    {
        if (t < 5.5f || t > 18.5f) return 0f;
        float rad = (t - 6f) / 24f * Mathf.PI * 2f;
        float trueElevation = Mathf.Sin(rad) * maxSunElevation;
        return Mathf.Clamp01((trueElevation + 2f) / Mathf.Max(0.1f, minSunElevation + 2f));
    }

    /// <summary>
    /// Smoothly tints ambient light across the day: blue at break of dawn, neutral/warm by day,
    /// orange/reddish at turn of night, and cool slate-blue at night.
    /// </summary>
    public static Color CalculateAmbientColor(float t)
    {
        Color nightBlue = new Color(0.14f, 0.20f, 0.34f);
        Color dawnBlue = new Color(0.32f, 0.48f, 0.78f);
        Color morningNeutral = new Color(0.76f, 0.75f, 0.74f);
        Color dayNeutral = new Color(0.78f, 0.77f, 0.74f);
        Color duskOrangeRed = new Color(0.82f, 0.42f, 0.24f);
        Color twilightRed = new Color(0.45f, 0.20f, 0.22f);

        if (t < 4.5f) return nightBlue;
        if (t < 6.0f) return Color.Lerp(nightBlue, dawnBlue, (t - 4.5f) / 1.5f);
        if (t < 7.5f) return Color.Lerp(dawnBlue, morningNeutral, (t - 6.0f) / 1.5f);
        if (t < 16.5f) return dayNeutral;
        if (t < 18.0f) return Color.Lerp(dayNeutral, duskOrangeRed, (t - 16.5f) / 1.5f);
        if (t < 19.25f) return Color.Lerp(duskOrangeRed, twilightRed, (t - 18.0f) / 1.25f);
        if (t < 20.5f) return Color.Lerp(twilightRed, nightBlue, (t - 19.25f) / 1.25f);
        return nightBlue;
    }

    /// <summary>
    /// Calculates theoretical daylight intensity from a smooth bell curve peaking at noon (1.35).
    /// </summary>
    public static float CalculateDaylightIntensity(float t)
    {
        if (t <= 5.5f || t >= 18.5f) return 0f;
        float dayProgress = (t - 6f) / 12f;
        float sine = Mathf.Clamp01(Mathf.Sin(dayProgress * Mathf.PI));
        return Mathf.Pow(sine, 0.75f) * 1.35f;
    }

    /// <summary>
    /// Calculates natural blackbody sun color temperature throughout the day.
    /// </summary>
    public static Color CalculateSunColor(float t)
    {
        if (t < 5.2f || t > 19.0f) return new Color(0.23f, 0.31f, 0.48f); // Night indigo
        if (t < 6.0f) return Color.Lerp(new Color(1.0f, 0.58f, 0.45f), new Color(1.0f, 0.72f, 0.44f), (t - 5.2f) / 0.8f); // Dawn peach
        if (t < 9.0f) return Color.Lerp(new Color(1.0f, 0.72f, 0.44f), new Color(1.0f, 0.94f, 0.82f), (t - 6.0f) / 3.0f); // Morning gold
        if (t < 15.5f) return new Color(1.0f, 0.98f, 0.95f); // Midday crisp white
        if (t < 17.5f) return Color.Lerp(new Color(1.0f, 0.98f, 0.95f), new Color(1.0f, 0.73f, 0.35f), (t - 15.5f) / 2.0f); // Golden hour
        if (t < 18.3f) return Color.Lerp(new Color(1.0f, 0.73f, 0.35f), new Color(1.0f, 0.35f, 0.15f), (t - 17.5f) / 0.8f); // Sunset fiery amber
        return Color.Lerp(new Color(1.0f, 0.35f, 0.15f), new Color(0.40f, 0.25f, 0.45f), (t - 18.3f) / 0.7f); // Dusk twilight
    }

    /// <summary>
    /// Calculates complementary sky fill color (cool sky blue during the day, dusk violet at sunset).
    /// </summary>
    public static Color CalculateSkyFillColor(float t)
    {
        if (t < 6.0f || t > 18.5f) return new Color(0.10f, 0.14f, 0.25f); // Dark navy
        if (t < 8.0f) return new Color(0.65f, 0.72f, 0.88f); // Soft morning blue
        if (t < 16.0f) return new Color(0.55f, 0.75f, 0.92f); // Clear midday sky blue
        if (t < 18.0f) return new Color(0.62f, 0.48f, 0.68f); // Sunset lavender/violet
        return new Color(0.25f, 0.20f, 0.38f); // Dusk purple
    }

    #endregion
}
