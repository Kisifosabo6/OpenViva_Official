#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenViva.Dev
{
    [InitializeOnLoad]
    public static class CameraLightLevelIndicator
    {
        private const string PrefKey = "OpenViva_CameraLightLevelIndicator_Enabled";
        private const string MenuPath = "Tools/OpenViva/Camera Light Level Indicator";

        public static bool IsEnabled
        {
            get => EditorPrefs.GetBool(PrefKey, true);
            set
            {
                EditorPrefs.SetBool(PrefKey, value);
                Menu.SetChecked(MenuPath, value);
                SceneView.RepaintAll();
            }
        }

        static CameraLightLevelIndicator()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            EditorApplication.delayCall += () =>
            {
                Menu.SetChecked(MenuPath, IsEnabled);
            };
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        [MenuItem(MenuPath, false, 100)]
        public static void ToggleIndicator()
        {
            IsEnabled = !IsEnabled;
        }

        [MenuItem(MenuPath, true)]
        public static bool ToggleIndicatorValidate()
        {
            Menu.SetChecked(MenuPath, IsEnabled);
            return true;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && IsEnabled)
            {
                EnsureRuntimeOverlayExists();
            }
        }

        public static void EnsureRuntimeOverlayExists()
        {
            if (!Application.isPlaying) return;
            if (Object.FindFirstObjectByType<CameraLightLevelRuntimeOverlay>() == null)
            {
                GameObject go = new GameObject("CameraLightLevelRuntimeOverlay")
                {
                    hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild
                };
                go.AddComponent<CameraLightLevelRuntimeOverlay>();
            }
        }

        private static void OnSceneGUI(SceneView sceneView)
        {
            if (!IsEnabled) return;
            if (sceneView == null || sceneView.camera == null) return;

            Camera cam = sceneView.camera;
            LightSampleData data = EvaluateLightAt(cam.transform.position, cam.transform.forward);

            Handles.BeginGUI();
            DrawGUIOverlay(sceneView.position.width, sceneView.position.height, data, "SCENE VIEW CAMERA (EDIT MODE)");
            Handles.EndGUI();
        }

        public struct LightSampleData
        {
            public float Total;
            public float Sun;
            public float Ambient;
            public float LocalLights;
            public int LocalLightCount;
            public Vector3 Position;
        }

        public static LightSampleData EvaluateLightAt(Vector3 position, Vector3 forwardDir)
        {
            LightSampleData data = new LightSampleData
            {
                Position = position
            };

            // 1. Ambient / Spherical Harmonics
            SphericalHarmonicsL2 probe;
            LightProbes.GetInterpolatedProbe(position, null, out probe);
            Vector3[] directions = new Vector3[]
            {
                Vector3.up, Vector3.down, forwardDir, -forwardDir, Vector3.right, Vector3.left
            };
            Color[] shColors = new Color[6];
            probe.Evaluate(directions, shColors);

            Color avgAmbient = Color.black;
            for (int i = 0; i < 6; i++) avgAmbient += shColors[i];
            avgAmbient /= 6f;
            data.Ambient = 0.2126f * avgAmbient.r + 0.7152f * avgAmbient.g + 0.0722f * avgAmbient.b;

            // 2. Directional & Local Lights
            Light[] lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (Light l in lights)
            {
                if (l == null || !l.enabled || !l.gameObject.activeInHierarchy) continue;

                float luma = 0.2126f * l.color.r + 0.7152f * l.color.g + 0.0722f * l.color.b;

                if (l.type == LightType.Directional)
                {
                    data.Sun += l.intensity * luma;
                }
                else if (l.type == LightType.Point || l.type == LightType.Spot)
                {
                    float dist = Vector3.Distance(l.transform.position, position);
                    if (dist <= l.range && dist > 0.001f)
                    {
                        float atten = Mathf.Clamp01(1f - (dist / l.range));
                        atten *= atten; // Smooth quadratic falloff

                        if (l.type == LightType.Spot)
                        {
                            Vector3 dirToCam = (position - l.transform.position).normalized;
                            float angle = Vector3.Angle(l.transform.forward, dirToCam);
                            float outer = l.spotAngle * 0.5f;
                            float inner = l.innerSpotAngle * 0.5f;

                            if (angle > outer) atten = 0f;
                            else if (angle > inner) atten *= Mathf.Clamp01((outer - angle) / Mathf.Max(0.001f, outer - inner));
                        }

                        if (atten > 0.001f)
                        {
                            data.LocalLights += l.intensity * atten * luma;
                            data.LocalLightCount++;
                        }
                    }
                }
            }

            data.Total = data.Ambient + data.Sun + data.LocalLights;
            return data;
        }

        public static void DrawGUIOverlay(float viewWidth, float viewHeight, LightSampleData data, string headerTag)
        {
            float width = 270f;
            float height = 115f;
            float margin = 12f;

            // Anchor to Bottom-Right
            Rect rect = new Rect(viewWidth - width - margin, viewHeight - height - margin - 20f, width, height);

            GUI.color = new Color(0.08f, 0.08f, 0.08f, 0.88f);
            GUI.Box(rect, GUIContent.none);
            GUI.color = Color.white;

            GUILayout.BeginArea(new Rect(rect.x + 8f, rect.y + 6f, width - 16f, height - 12f));

            // Header
            GUIStyle headerStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                fontSize = 10,
                normal = { textColor = new Color(0.7f, 0.7f, 0.7f) }
            };
            GUILayout.Label(headerTag, headerStyle);

            // Total Light Level + Status Badge
            Color statusColor;
            string statusText;
            if (data.Total < 0.35f)
            {
                statusColor = new Color(0.5f, 0.7f, 1f);
                statusText = "[DIM / NIGHT]";
            }
            else if (data.Total <= 0.90f)
            {
                statusColor = new Color(0.4f, 1f, 0.4f);
                statusText = "[BALANCED]";
            }
            else if (data.Total <= 1.8f)
            {
                statusColor = new Color(1f, 0.75f, 0.2f);
                statusText = "[BLOOM TRIGGER (>0.9)]";
            }
            else
            {
                statusColor = new Color(1f, 0.25f, 0.25f);
                statusText = "[OVEREXPOSED / BLOWN OUT]";
            }

            GUIStyle totalStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                normal = { textColor = statusColor }
            };
            GUILayout.Label($"Light Level: {data.Total:F2}  {statusText}", totalStyle);

            // Breakdown
            GUIStyle bodyStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11,
                normal = { textColor = Color.white }
            };

            GUILayout.Label($"  Sun/Directional: {data.Sun:F2}", bodyStyle);
            GUILayout.Label($"  Ambient (SH):    {data.Ambient:F2}", bodyStyle);
            GUILayout.Label($"  Local Lights:    {data.LocalLights:F2} ({data.LocalLightCount} in range)", bodyStyle);

            GUILayout.EndArea();
        }
    }

    public class CameraLightLevelRuntimeOverlay : MonoBehaviour
    {
        private void Awake()
        {
            if (!CameraLightLevelIndicator.IsEnabled)
            {
                Destroy(gameObject);
            }
        }

        private void OnGUI()
        {
            if (!CameraLightLevelIndicator.IsEnabled) return;

            Camera cam = Camera.main;
            if (cam == null) cam = Camera.current;
            if (cam == null) return;

            CameraLightLevelIndicator.LightSampleData data =
                CameraLightLevelIndicator.EvaluateLightAt(cam.transform.position, cam.transform.forward);

            CameraLightLevelIndicator.DrawGUIOverlay(Screen.width, Screen.height, data, "MAIN CAMERA (PLAY MODE)");
        }
    }
}
#endif
