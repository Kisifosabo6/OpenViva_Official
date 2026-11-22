using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class HandAnimationPreviewBuilder
{
    private const string SCENE_PATH = "Assets/Scenes/HandAnimation_Preview.unity";
    private const string SCENE_VERSION_KEY = "OpenViva_HandAnimationPreview_Version";
    private const int CURRENT_VERSION = 3;

    static HandAnimationPreviewBuilder()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorPrefs.GetInt(SCENE_VERSION_KEY, 0) < CURRENT_VERSION || !System.IO.File.Exists(SCENE_PATH))
            {
                EditorPrefs.SetInt(SCENE_VERSION_KEY, CURRENT_VERSION);
                CreateScene();
            }
        };
    }

    [MenuItem("OpenViva/Dev/Create Hand Animation Preview Scene")]
    public static void CreateScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // 1. Camera
        GameObject camGo = new GameObject("Main Camera");
        Camera cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.12f, 0.13f, 0.16f);
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = 50f;
        cam.fieldOfView = 58f;
        camGo.transform.position = Vector3.zero;
        camGo.transform.rotation = Quaternion.identity;
        camGo.tag = "MainCamera";

        // 2. Studio Lighting
        GameObject keyLight = new GameObject("Key Light");
        Light l1 = keyLight.AddComponent<Light>();
        l1.type = LightType.Directional;
        l1.color = new Color(1f, 0.96f, 0.90f);
        l1.intensity = 1.3f;
        keyLight.transform.rotation = Quaternion.Euler(45f, -30f, 0f);

        GameObject fillLight = new GameObject("Fill Light");
        Light l2 = fillLight.AddComponent<Light>();
        l2.type = LightType.Directional;
        l2.color = new Color(0.65f, 0.78f, 1f);
        l2.intensity = 0.7f;
        fillLight.transform.rotation = Quaternion.Euler(-20f, 145f, 0f);

        GameObject rimLight = new GameObject("Rim Light");
        Light l3 = rimLight.AddComponent<Light>();
        l3.type = LightType.Directional;
        l3.color = new Color(0.85f, 0.9f, 1f);
        l3.intensity = 0.4f;
        rimLight.transform.rotation = Quaternion.Euler(-45f, -40f, 0f);

        // 3. Instantiate the ACTUAL MainScene Player Hand Model (Player_Prefab.fbx)
        GameObject fbxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Scenes/MainScene/Prefabs/Player/Player_Prefab.fbx");
        if (fbxPrefab == null)
        {
            Debug.LogError("[HandAnimationPreview] Could not find Player_Prefab.fbx!");
            return;
        }

        GameObject handsInstance = (GameObject)PrefabUtility.InstantiatePrefab(fbxPrefab);
        handsInstance.name = "PlayerHands_Rig";
        handsInstance.transform.position = Vector3.zero;
        handsInstance.transform.rotation = Quaternion.identity;

        // If root has an Animator from FBX import, remove it so child wrist animators have full control
        Animator rootAnim = handsInstance.GetComponent<Animator>();
        if (rootAnim != null)
        {
            Object.DestroyImmediate(rootAnim);
        }

        // Configure SkinnedMeshRenderer & Material
        Material handMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Scenes/MainScene/Materials/Player/Player_Material.mat");
        SkinnedMeshRenderer smr = handsInstance.GetComponentInChildren<SkinnedMeshRenderer>();
        if (smr != null)
        {
            if (handMat != null) smr.sharedMaterial = handMat;
            smr.updateWhenOffscreen = true;
        }

        // Find wrist bones
        Transform wristL = FindChildRecursive(handsInstance.transform, "wrist_l");
        Transform wristR = FindChildRecursive(handsInstance.transform, "wrist_r");

        // Load Controllers
        RuntimeAnimatorController leftAC = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
            "Assets/Scenes/MainScene/Animations/Player/Keyboard/LeftHandAC.controller");
        RuntimeAnimatorController rightAC = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
            "Assets/Scenes/MainScene/Animations/Player/Keyboard/RightHandAC.controller");

        HandAnimator handAnimL = null;
        HandAnimator handAnimR = null;

        // Setup Left Hand
        if (wristL != null)
        {
            wristL.gameObject.SetActive(true);
            wristL.position = new Vector3(-0.16f, -0.18f, 0.48f);
            wristL.rotation = Quaternion.Euler(7.2f, -106.1f, -55.03f);

            Animator animL = wristL.GetComponent<Animator>();
            if (animL == null) animL = wristL.gameObject.AddComponent<Animator>();
            animL.runtimeAnimatorController = leftAC;

            handAnimL = wristL.GetComponent<HandAnimator>();
            if (handAnimL == null) handAnimL = wristL.gameObject.AddComponent<HandAnimator>();
            handAnimL.isLeftHand = true;
            handAnimL.wrist = wristL;
            handAnimL.handAnimator = animL;
            handAnimL.AutoFindFingerBones();
        }

        // Setup Right Hand
        if (wristR != null)
        {
            wristR.gameObject.SetActive(true);
            wristR.position = new Vector3(0.16f, -0.18f, 0.48f);
            wristR.rotation = Quaternion.Euler(355.1f, -254.1f, -314.97f);

            Animator animR = wristR.GetComponent<Animator>();
            if (animR == null) animR = wristR.gameObject.AddComponent<Animator>();
            animR.runtimeAnimatorController = rightAC;

            handAnimR = wristR.GetComponent<HandAnimator>();
            if (handAnimR == null) handAnimR = wristR.gameObject.AddComponent<HandAnimator>();
            handAnimR.isLeftHand = false;
            handAnimR.wrist = wristR;
            handAnimR.handAnimator = animR;
            handAnimR.AutoFindFingerBones();
        }

        // 4. Tester Component
        GameObject testerGo = new GameObject("HandAnimationTester");
        HandAnimationTester tester = testerGo.AddComponent<HandAnimationTester>();
        tester.leftHandAnimator = handAnimL;
        tester.rightHandAnimator = handAnimR;
        tester.leftWrist = wristL;
        tester.rightWrist = wristR;
        tester.previewCamera = cam;

        // 5. Save Scene
        if (!System.IO.Directory.Exists("Assets/Scenes"))
        {
            System.IO.Directory.CreateDirectory("Assets/Scenes");
        }

        EditorSceneManager.SaveScene(scene, SCENE_PATH);
        AssetDatabase.Refresh();
        Debug.Log($"[OpenViva] MainScene KB Hand Animation Preview Scene generated successfully at: {SCENE_PATH}");
    }

    private static Transform FindChildRecursive(Transform parent, string name)
    {
        if (parent == null) return null;
        if (parent.name.Equals(name, System.StringComparison.OrdinalIgnoreCase))
            return parent;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform found = FindChildRecursive(parent.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }
}
