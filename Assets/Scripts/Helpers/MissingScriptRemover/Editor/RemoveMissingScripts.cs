using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public class RemoveMissingScripts : MonoBehaviour
{
    [MenuItem("GameObject/Remove Missing Scripts (Recursive)", false, 10)]
    public static void RemoveFromSelected()
    {
        if (Selection.gameObjects.Length == 0)
        {
            Debug.LogWarning("No GameObject selected!");
            return;
        }

        int totalRemoved = 0;
        foreach (GameObject go in Selection.gameObjects)
        {
            totalRemoved += CleanHierarchy(go);
        }

        if (totalRemoved > 0)
        {
            Debug.Log($"Total missing scripts removed from selection: {totalRemoved}");
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }
        else
        {
            Debug.Log("No missing scripts found on the selected object or its children.");
        }
    }

    [MenuItem("Tools/Clean Missing Scripts/Clean Current Scene")]
    public static void CleanCurrentScene()
    {
        var scene = EditorSceneManager.GetActiveScene();
        int totalRemoved = 0;
        foreach (var root in scene.GetRootGameObjects())
        {
            totalRemoved += CleanHierarchy(root);
        }

        if (totalRemoved > 0)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"Cleaned {totalRemoved} missing script(s) from current scene: {scene.name}");
        }
        else
        {
            Debug.Log($"No missing scripts found in scene: {scene.name}");
        }
    }

    [MenuItem("Tools/Clean Missing Scripts/Clean All Prefabs")]
    public static void CleanAllPrefabs()
    {
        string[] prefabGUIDs = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
        int totalPrefabsCleaned = 0;
        int totalRemoved = 0;

        foreach (string guid in prefabGUIDs)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) continue;

            try
            {
                GameObject contents = PrefabUtility.LoadPrefabContents(path);
                if (contents == null) continue;

                int removed = CleanHierarchy(contents);
                if (removed > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(contents, path);
                    totalPrefabsCleaned++;
                    totalRemoved += removed;
                    Debug.Log($"Cleaned {removed} missing script(s) from prefab: {path}");
                }
                PrefabUtility.UnloadPrefabContents(contents);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"Could not process prefab {path}: {ex.Message}");
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"Cleaned {totalRemoved} missing script(s) across {totalPrefabsCleaned} prefab(s).");
    }

    [MenuItem("Tools/Clean Missing Scripts/Clean All Scenes & Prefabs")]
    public static void CleanAllScenesAndPrefabs()
    {
        Debug.Log("=== Starting Clean All Scenes & Prefabs ===");

        // 1. Clean all prefabs
        CleanAllPrefabs();

        // 2. Clean all scenes
        string[] sceneGUIDs = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" });
        int totalSceneScriptsRemoved = 0;

        foreach (string guid in sceneGUIDs)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) continue;

            try
            {
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                int removed = 0;
                foreach (var root in scene.GetRootGameObjects())
                {
                    removed += CleanHierarchy(root);
                }

                if (removed > 0)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    totalSceneScriptsRemoved += removed;
                    Debug.Log($"Cleaned {removed} missing script(s) from scene: {path}");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"Could not process scene {path}: {ex.Message}");
            }
        }

        // Return to MainScene as active scene
        string mainScenePath = "Assets/Scenes/MainScene.unity";
        if (File.Exists(mainScenePath))
        {
            EditorSceneManager.OpenScene(mainScenePath, OpenSceneMode.Single);
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"=== Completed: Total missing scripts removed from scenes: {totalSceneScriptsRemoved} ===");
    }

    private static int CleanHierarchy(GameObject root)
    {
        if (root == null) return 0;
        int count = 0;
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        foreach (Transform t in transforms)
        {
            if (t != null && t.gameObject != null)
            {
                int c = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                if (c > 0)
                {
                    EditorUtility.SetDirty(t.gameObject);
                    count += c;
                }
            }
        }
        return count;
    }
}
