using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(DayNightCycle))]
public class DayNightCycleEditor : Editor
{
    private SerializedProperty timeOfDayProp;
    private SerializedProperty hoursProp;
    private SerializedProperty minutesProp;
    private SerializedProperty timeStringProp;
    private SerializedProperty runCycleInPlayModeProp;
    private SerializedProperty currentCycleSpeedProp;

    private SerializedProperty sunPivotProp;
    private SerializedProperty dummySunVisualProp;
    private SerializedProperty sunLightProp;
    private SerializedProperty skyFillLightProp;
    private SerializedProperty moonPivotProp;
    private SerializedProperty moonObjectProp;
    private SerializedProperty moonLightProp;
    private SerializedProperty starsParticleSystemProp;

    private SerializedProperty useStabilizedShadowsProp;
    private SerializedProperty shadowStepIntervalProp;
    private SerializedProperty shadowGlideDurationProp;
    private SerializedProperty minSunElevationProp;
    private SerializedProperty maxSunElevationProp;

    private SerializedProperty blendedSkyboxMaterialProp;
    private SerializedProperty nightCubemapProp;
    private SerializedProperty dawnCubemapProp;
    private SerializedProperty morningCubemapProp;
    private SerializedProperty dayCubemapProp;
    private SerializedProperty afternoonCubemapProp;
    private SerializedProperty duskCubemapProp;

    private SerializedProperty sunColorGradientProp;
    private SerializedProperty sunIntensityCurveProp;
    private SerializedProperty ambientIntensityCurveProp;
    private SerializedProperty fogColorGradientProp;
    private SerializedProperty moonIntensityCurveProp;
    private SerializedProperty moonColorProp;
    private SerializedProperty starEmissionCurveProp;

    private bool showReferences = false;
    private bool showCurves = false;

    private void OnEnable()
    {
        timeOfDayProp = serializedObject.FindProperty("timeOfDay");
        hoursProp = serializedObject.FindProperty("hours");
        minutesProp = serializedObject.FindProperty("minutes");
        timeStringProp = serializedObject.FindProperty("timeString");
        runCycleInPlayModeProp = serializedObject.FindProperty("runCycleInPlayMode");
        currentCycleSpeedProp = serializedObject.FindProperty("currentCycleSpeed");

        sunPivotProp = serializedObject.FindProperty("sunPivot");
        dummySunVisualProp = serializedObject.FindProperty("dummySunVisual");
        sunLightProp = serializedObject.FindProperty("sunLight");
        skyFillLightProp = serializedObject.FindProperty("skyFillLight");
        moonPivotProp = serializedObject.FindProperty("moonPivot");
        moonObjectProp = serializedObject.FindProperty("moonObject");
        moonLightProp = serializedObject.FindProperty("moonLight");
        starsParticleSystemProp = serializedObject.FindProperty("starsParticleSystem");

        useStabilizedShadowsProp = serializedObject.FindProperty("useStabilizedShadows");
        shadowStepIntervalProp = serializedObject.FindProperty("shadowStepInterval");
        shadowGlideDurationProp = serializedObject.FindProperty("shadowGlideDuration");
        minSunElevationProp = serializedObject.FindProperty("minSunElevation");
        maxSunElevationProp = serializedObject.FindProperty("maxSunElevation");

        blendedSkyboxMaterialProp = serializedObject.FindProperty("blendedSkyboxMaterial");
        nightCubemapProp = serializedObject.FindProperty("nightCubemap");
        dawnCubemapProp = serializedObject.FindProperty("dawnCubemap");
        morningCubemapProp = serializedObject.FindProperty("morningCubemap");
        dayCubemapProp = serializedObject.FindProperty("dayCubemap");
        afternoonCubemapProp = serializedObject.FindProperty("afternoonCubemap");
        duskCubemapProp = serializedObject.FindProperty("duskCubemap");

        sunColorGradientProp = serializedObject.FindProperty("sunColorGradient");
        sunIntensityCurveProp = serializedObject.FindProperty("sunIntensityCurve");
        ambientIntensityCurveProp = serializedObject.FindProperty("ambientIntensityCurve");
        fogColorGradientProp = serializedObject.FindProperty("fogColorGradient");
        moonIntensityCurveProp = serializedObject.FindProperty("moonIntensityCurve");
        moonColorProp = serializedObject.FindProperty("moonColor");
        starEmissionCurveProp = serializedObject.FindProperty("starEmissionCurve");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        var cycle = (DayNightCycle)target;

        // Header Badge
        DrawHeaderBadge(cycle);

        EditorGUILayout.Space(8);

        // Time of Day Slider & Controls
        EditorGUILayout.LabelField("Time of Day Selection (00h00 - 23h59)", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();

        // 24-Hour Slider
        float newTime = EditorGUILayout.Slider(new GUIContent("Time (0h - 24h)", "Scrub between 00h00 and 23h59"), timeOfDayProp.floatValue, 0f, 23.99f);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(cycle, "Change Time of Day");
            cycle.SetTimeOfDay(newTime);
            EditorUtility.SetDirty(cycle);
            SceneView.RepaintAll();
        }

        // Hour and Minute fields
        EditorGUILayout.BeginHorizontal();
        EditorGUI.BeginChangeCheck();
        int newHour = EditorGUILayout.IntSlider("Hour", hoursProp.intValue, 0, 23);
        int newMinute = EditorGUILayout.IntSlider("Minute", minutesProp.intValue, 0, 59);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(cycle, "Change Hour/Minute");
            cycle.SetTimeOfDay(newHour, newMinute);
            EditorUtility.SetDirty(cycle);
            SceneView.RepaintAll();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(6);

        // Quick Preset Buttons
        EditorGUILayout.LabelField("Quick Presets", EditorStyles.miniBoldLabel);
        EditorGUILayout.BeginHorizontal();

        GUI.backgroundColor = new Color(1.0f, 0.85f, 0.5f);
        if (GUILayout.Button("Sunrise (06h00)", GUILayout.Height(26)))
        {
            Undo.RecordObject(cycle, "Set Sunrise");
            cycle.SetSunrise();
            EditorUtility.SetDirty(cycle);
            SceneView.RepaintAll();
        }

        GUI.backgroundColor = new Color(1.0f, 0.98f, 0.7f);
        if (GUILayout.Button("Noon (12h00)", GUILayout.Height(26)))
        {
            Undo.RecordObject(cycle, "Set Noon");
            cycle.SetNoon();
            EditorUtility.SetDirty(cycle);
            SceneView.RepaintAll();
        }

        GUI.backgroundColor = new Color(1.0f, 0.65f, 0.4f);
        if (GUILayout.Button("Sunset (18h00)", GUILayout.Height(26)))
        {
            Undo.RecordObject(cycle, "Set Sunset");
            cycle.SetSunset();
            EditorUtility.SetDirty(cycle);
            SceneView.RepaintAll();
        }

        GUI.backgroundColor = new Color(0.6f, 0.75f, 1.0f);
        if (GUILayout.Button("Midnight (00h00)", GUILayout.Height(26)))
        {
            Undo.RecordObject(cycle, "Set Midnight");
            cycle.SetMidnight();
            EditorUtility.SetDirty(cycle);
            SceneView.RepaintAll();
        }

        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(12);

        // Cycle Progression (Play mode)
        EditorGUILayout.LabelField("Cycle Progression (Play Mode)", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(runCycleInPlayModeProp, new GUIContent("Run in Play Mode"));
        EditorGUILayout.PropertyField(currentCycleSpeedProp, new GUIContent("Cycle Speed"));

        EditorGUILayout.Space(10);

        // Shadow Motion & Anti-Wobble Stabilization
        EditorGUILayout.LabelField("Shadow Motion & Anti-Wobble", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(useStabilizedShadowsProp, new GUIContent("Stabilize Shadows", "Decouples shadow casting from continuous per-frame light rotation to eliminate shadow crawling and jitter."));
        if (useStabilizedShadowsProp.boolValue)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(shadowStepIntervalProp, new GUIContent("Step Interval (sec)", "Seconds between shadow light updates (e.g. 5-10s). Shadows stay completely frozen between steps."));
            EditorGUILayout.PropertyField(shadowGlideDurationProp, new GUIContent("Glide Duration (sec)", "Duration of the smooth Slerp transition when catching up. Eliminates 1-frame popping."));
            EditorGUILayout.PropertyField(minSunElevationProp, new GUIContent("Min Shadow Elevation", "Prevents grazing low-angle sun rays from creating stretched flickering shadows near horizon."));
            EditorGUILayout.PropertyField(maxSunElevationProp, new GUIContent("Midday Max Elevation", "Peak sun elevation at midday noon."));
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space(10);

        // Lighting Curves & Gradients Foldout
        showCurves = EditorGUILayout.Foldout(showCurves, "Smooth Transition Curves & Gradients", true, EditorStyles.foldoutHeader);
        if (showCurves)
        {
            EditorGUI.indentLevel++;
            if (sunIntensityCurveProp != null) EditorGUILayout.PropertyField(sunIntensityCurveProp);
            if (sunColorGradientProp != null) EditorGUILayout.PropertyField(sunColorGradientProp);
            if (ambientIntensityCurveProp != null) EditorGUILayout.PropertyField(ambientIntensityCurveProp);
            if (fogColorGradientProp != null) EditorGUILayout.PropertyField(fogColorGradientProp);
            if (moonIntensityCurveProp != null) EditorGUILayout.PropertyField(moonIntensityCurveProp);
            if (moonColorProp != null) EditorGUILayout.PropertyField(moonColorProp);
            if (starEmissionCurveProp != null) EditorGUILayout.PropertyField(starEmissionCurveProp);

            EditorGUILayout.Space(4);
            if (GUILayout.Button("Reset Curves to Smooth Defaults", GUILayout.Height(24)))
            {
                Undo.RecordObject(cycle, "Reset Curves");
                cycle.InitializeDefaultCurves(true);
                cycle.ApplyTimeVisuals();
                EditorUtility.SetDirty(cycle);
                SceneView.RepaintAll();
            }
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space(10);

        // Scene References Foldout
        showReferences = EditorGUILayout.Foldout(showReferences, "Scene & Skybox References", true, EditorStyles.foldoutHeader);
        if (showReferences)
        {
            EditorGUI.indentLevel++;
            if (sunPivotProp != null) EditorGUILayout.PropertyField(sunPivotProp, new GUIContent("Sun Pivot (Visual Celestial)"));
            if (dummySunVisualProp != null) EditorGUILayout.PropertyField(dummySunVisualProp, new GUIContent("Dummy Sun Visual"));
            if (sunLightProp != null) EditorGUILayout.PropertyField(sunLightProp, new GUIContent("Sun Light (Shadow Caster)"));
            if (skyFillLightProp != null) EditorGUILayout.PropertyField(skyFillLightProp, new GUIContent("Sky Fill Light (Optional)"));
            if (moonPivotProp != null) EditorGUILayout.PropertyField(moonPivotProp);
            if (moonObjectProp != null) EditorGUILayout.PropertyField(moonObjectProp);
            if (moonLightProp != null) EditorGUILayout.PropertyField(moonLightProp);
            if (starsParticleSystemProp != null) EditorGUILayout.PropertyField(starsParticleSystemProp);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Skybox Assets", EditorStyles.miniBoldLabel);
            if (blendedSkyboxMaterialProp != null) EditorGUILayout.PropertyField(blendedSkyboxMaterialProp);
            if (nightCubemapProp != null) EditorGUILayout.PropertyField(nightCubemapProp);
            if (dawnCubemapProp != null) EditorGUILayout.PropertyField(dawnCubemapProp);
            if (morningCubemapProp != null) EditorGUILayout.PropertyField(morningCubemapProp);
            if (dayCubemapProp != null) EditorGUILayout.PropertyField(dayCubemapProp);
            if (afternoonCubemapProp != null) EditorGUILayout.PropertyField(afternoonCubemapProp);
            if (duskCubemapProp != null) EditorGUILayout.PropertyField(duskCubemapProp);

            EditorGUILayout.Space(4);
            if (GUILayout.Button("Auto-Find Scene References", GUILayout.Height(24)))
            {
                Undo.RecordObject(cycle, "Auto-Find References");
                cycle.AutoFindReferences();
                EditorUtility.SetDirty(cycle);
                SceneView.RepaintAll();
            }
            EditorGUI.indentLevel--;
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawHeaderBadge(DayNightCycle cycle)
    {
        string badgeText;
        Color badgeColor;

        if (cycle.Hours == 6 && cycle.Minutes == 0)
        {
            badgeText = $"TIME: {cycle.TimeFormatted} [SUNRISE]";
            badgeColor = new Color(1.0f, 0.75f, 0.35f);
        }
        else if (cycle.Hours == 18 && cycle.Minutes == 0)
        {
            badgeText = $"TIME: {cycle.TimeFormatted} [SUNSET]";
            badgeColor = new Color(1.0f, 0.55f, 0.25f);
        }
        else if (cycle.Hours == 12 && cycle.Minutes == 0)
        {
            badgeText = $"TIME: {cycle.TimeFormatted} [NOON]";
            badgeColor = new Color(1.0f, 0.95f, 0.4f);
        }
        else if (cycle.Hours == 0 && cycle.Minutes == 0)
        {
            badgeText = $"TIME: {cycle.TimeFormatted} [MIDNIGHT]";
            badgeColor = new Color(0.45f, 0.7f, 1.0f);
        }
        else if (cycle.IsDay)
        {
            badgeText = $"TIME: {cycle.TimeFormatted} [DAY]";
            badgeColor = new Color(0.85f, 1.0f, 0.5f);
        }
        else
        {
            badgeText = $"TIME: {cycle.TimeFormatted} [NIGHT]";
            badgeColor = new Color(0.6f, 0.8f, 1.0f);
        }

        Rect boxRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(36));
        EditorGUI.DrawRect(boxRect, new Color(0.12f, 0.12f, 0.14f, 1f));

        GUIStyle style = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 15,
            normal = { textColor = badgeColor }
        };

        GUI.Label(boxRect, badgeText, style);
    }
}
