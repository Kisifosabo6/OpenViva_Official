using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Viva.Dev
{
    public static class HierarchySanitizer
    {
        [MenuItem("OpenViva/Sanitize Companion Hierarchy", false, 5)]
        public static void Sanitize()
        {
            GameObject companion = GameObject.Find("ShinobuCompanion");
            if (companion == null)
            {
                Debug.LogError("[HierarchySanitizer] ShinobuCompanion not found in scene!");
                return;
            }

            Undo.RegisterFullObjectHierarchyUndo(companion, "Sanitize Hierarchy");

            // 1. Build bone map from ShinobuArmature
            Transform armature = companion.transform.Find("ShinobuArmature");
            if (armature == null)
            {
                Debug.LogError("[HierarchySanitizer] ShinobuArmature not found!");
                return;
            }

            var boneMap = new Dictionary<string, Transform>(System.StringComparer.OrdinalIgnoreCase);
            foreach (Transform t in armature.GetComponentsInChildren<Transform>(true))
            {
                if (!boneMap.ContainsKey(t.name)) boneMap.Add(t.name, t);
            }

            Transform spine1 = null;
            boneMap.TryGetValue("spine1", out spine1);

            // 2. Remove all mesh renderers / rogue objects from inside bones in ShinobuArmature
            // Specifically remove any skirt, hair, or meshes attached inside bones
            for (int i = armature.childCount - 1; i >= 0; i--)
            {
                CleanBonesRecursive(armature.GetChild(i));
            }

            // 3. Remove existing hair and skirt from companion root to cleanly re-create them
            for (int i = companion.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = companion.transform.GetChild(i);
                string lower = child.name.ToLowerInvariant();
                if (lower.Contains("hair") || lower.Contains("skirt"))
                {
                    Undo.DestroyObjectImmediate(child.gameObject);
                }
            }

            Material matHair = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Shinobu/Shinobu_Hair.mat");
            Material matSkirt = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Shinobu/Shinobu_Skirt.mat");

            // 4. Attach Hair as a DIRECT child of ShinobuCompanion
            GameObject hairAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Models/loli/hair.fbx");
            if (hairAsset != null)
            {
                GameObject hairInstance = (GameObject)PrefabUtility.InstantiatePrefab(hairAsset);
                hairInstance.name = "hair";
                hairInstance.transform.SetParent(companion.transform, false);
                hairInstance.transform.localPosition = Vector3.zero;
                hairInstance.transform.localRotation = Quaternion.identity;
                hairInstance.transform.localScale = Vector3.one;

                foreach (var smr in hairInstance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (matHair != null) smr.sharedMaterial = matHair;
                }
                Undo.RegisterCreatedObjectUndo(hairInstance, "Create Sanitized Hair");
                Debug.Log("[HierarchySanitizer] Attached clean Hair directly under ShinobuCompanion");
            }

            // 5. Attach Skirt with clean MeshFilter + MeshRenderer attached to spine1
            string fbxPath = "Assets/Models/clothes/skirt 1.fbx";
            GameObject fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (fbxAsset != null)
            {
                MeshFilter srcMF = fbxAsset.GetComponentInChildren<MeshFilter>(true);
                if (srcMF != null && srcMF.sharedMesh != null)
                {
                    Transform parent = spine1 != null ? spine1 : companion.transform;
                    GameObject skirtObj = new GameObject("skirt 1");
                    skirtObj.transform.position = companion.transform.position;
                    skirtObj.transform.rotation = companion.transform.rotation;
                    skirtObj.transform.localScale = Vector3.one;
                    skirtObj.transform.SetParent(parent, true);

                    MeshFilter mf = skirtObj.AddComponent<MeshFilter>();
                    mf.sharedMesh = srcMF.sharedMesh;

                    MeshRenderer mr = skirtObj.AddComponent<MeshRenderer>();
                    if (matSkirt != null) mr.sharedMaterial = matSkirt;

                    Undo.RegisterCreatedObjectUndo(skirtObj, "Create Sanitized Skirt");
                    Debug.Log("[HierarchySanitizer] Attached clean Skirt with MeshRenderer");
                }
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[HierarchySanitizer] Hierarchy sanitization complete!");
        }

        private static void CleanBonesRecursive(Transform bone)
        {
            for (int i = bone.childCount - 1; i >= 0; i--)
            {
                Transform child = bone.GetChild(i);
                string lower = child.name.ToLowerInvariant();
                // If it's a mesh or clothing piece placed in a bone, remove it
                if (lower.Contains("skirt") || lower.Contains("hair") || lower.Contains("shoe") || lower.Contains("dress"))
                {
                    Debug.Log($"[HierarchySanitizer] Removing rogue mesh object '{child.name}' from bone '{bone.name}'");
                    Undo.DestroyObjectImmediate(child.gameObject);
                }
                else
                {
                    CleanBonesRecursive(child);
                }
            }
        }
    }
}
