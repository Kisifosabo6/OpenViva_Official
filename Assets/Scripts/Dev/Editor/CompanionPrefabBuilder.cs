using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Viva.Interaction;

namespace Viva.Dev
{
    [InitializeOnLoad]
    public static class CompanionPrefabBuilder
    {
        private const string PrefabSavePath = "Assets/Resources/Prefabs/ShinobuCompanion.prefab";
        private const string PrefabCompanionPath = "Assets/Prefabs/Companion/ShinobuCompanion.prefab";

        static CompanionPrefabBuilder()
        {
            // Auto-check on editor compile
            EditorApplication.delayCall += CheckAndBuildOnLoad;
        }

        private static void CheckAndBuildOnLoad()
        {
            // Always ensure the prefab is generated and up to date
            BuildShinobuPrefab();
        }

        [MenuItem("OpenViva/Place Shinobu in MainScene", false, 2)]
        public static void PlaceShinobuInMainScene()
        {
            // Spawn location inside the starting house on the floor
            PlaceShinobuInScenePath("Assets/Scenes/MainScene.unity", new Vector3(-299.5f, 100.5f, 57.07f));
        }

        [MenuItem("OpenViva/Place Shinobu in Landscape_Main", false, 3)]
        public static void PlaceShinobuInLandscapeMain()
        {
            PlaceShinobuInScenePath("Assets/Scenes/Landscape_Main.unity", new Vector3(735.95f, 86.75f, 1117.58f));
        }

        private static void PlaceShinobuInScenePath(string scenePath, Vector3 spawnPos)
        {
            var activeScene = EditorSceneManager.GetActiveScene();

            if (activeScene.path != scenePath)
            {
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            }

            // Snap spawn position to NavMesh (Humanoid type 0)
            if (NavMesh.SamplePosition(spawnPos, out NavMeshHit hit, 10f, NavMesh.AllAreas))
            {
                spawnPos = hit.position;
            }

            // Remove existing companion in scene to guarantee fresh setup
            GameObject existing = GameObject.Find("ShinobuCompanion");
            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing);
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabSavePath);
            if (prefab == null)
            {
                prefab = BuildShinobuPrefab();
            }
            if (prefab == null) return;

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.name = "ShinobuCompanion";
            instance.transform.position = spawnPos;
            instance.transform.rotation = Quaternion.Euler(0, 180, 0);

            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[CompanionPrefabBuilder] Successfully placed fresh ShinobuCompanion in {scenePath} at {spawnPos}");
        }

        [MenuItem("OpenViva/Build Shinobu Companion Prefab", false, 1)]
        public static GameObject BuildShinobuPrefab()
        {
            string fbxPath = "Assets/Resources/Models/shinobu.fbx";
            GameObject fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (fbxAsset == null)
            {
                Debug.LogError($"[CompanionPrefabBuilder] Could not find {fbxPath}");
                return null;
            }

            // Create base instance in memory
            GameObject instance = Object.Instantiate(fbxAsset);
            instance.name = "ShinobuCompanion";
            instance.transform.position = Vector3.zero;
            instance.transform.rotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            // Remove debug dummy mesh body_FORANIM
            for (int i = instance.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = instance.transform.GetChild(i);
                if (child.name.ToLowerInvariant().Contains("foranim"))
                {
                    Object.DestroyImmediate(child.gameObject);
                }
            }

            // 1. Layer
            int interactableLayer = LayerMask.NameToLayer("Interactable");
            if (interactableLayer != -1) instance.layer = interactableLayer;

            // 2. NavMeshAgent: Humanoid agent type (0), disabled initially until position is snapped
            // Deactivate instance temporarily to prevent AddComponent<NavMeshAgent> from querying NavMesh at (0,0,0)
            instance.SetActive(false);
            NavMeshAgent agent = instance.GetComponent<NavMeshAgent>();
            if (agent == null) agent = instance.AddComponent<NavMeshAgent>();
            agent.agentTypeID = 0;
            agent.enabled = false;
            agent.speed = 1.5f;
            agent.stoppingDistance = 1.8f;
            agent.radius = 0.3f;
            agent.height = 1.2f;
            agent.acceleration = 10f;
            agent.angularSpeed = 360f;
            agent.updateRotation = false;
            agent.autoBraking = true;
            instance.SetActive(true);

            // 3. Animator
            Animator animator = instance.GetComponent<Animator>();
            if (animator == null) animator = instance.AddComponent<Animator>();
            RuntimeAnimatorController ac = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Resources/Animations/LoliAC.controller");
            if (ac != null) animator.runtimeAnimatorController = ac;

            // 4. BoxCollider for interaction
            BoxCollider col = instance.GetComponent<BoxCollider>();
            if (col == null) col = instance.AddComponent<BoxCollider>();
            col.center = new Vector3(0, 0.65f, 0);
            col.size = new Vector3(0.5f, 1.3f, 0.5f);
            col.isTrigger = true;

            // 5. AudioSource
            AudioSource audio = instance.GetComponent<AudioSource>();
            if (audio == null) audio = instance.AddComponent<AudioSource>();
            audio.spatialBlend = 1.0f;
            audio.minDistance = 1.0f;
            audio.maxDistance = 25.0f;
            audio.playOnAwake = false;

            // 6. CharacterInteractable
            CharacterInteractable charInteractable = instance.GetComponent<CharacterInteractable>();
            if (charInteractable == null) charInteractable = instance.AddComponent<CharacterInteractable>();
            charInteractable.characterName = "Shinobu";

            // 7. CompanionAI
            CompanionAI companionAI = instance.GetComponent<CompanionAI>();
            if (companionAI == null) companionAI = instance.AddComponent<CompanionAI>();
            companionAI.stoppingDistance = 1.8f;
            companionAI.walkSpeed = 1.5f;
            companionAI.runSpeed = 3.2f;

            // Build bone dictionary from master armature
            Dictionary<string, Transform> boneMap = new Dictionary<string, Transform>();
            Transform rootBone = null;
            Transform headBone = null;
            Transform neckBone = null;

            foreach (Transform t in instance.GetComponentsInChildren<Transform>(true))
            {
                if (!boneMap.ContainsKey(t.name))
                {
                    boneMap.Add(t.name, t);
                }
                string lower = t.name.ToLowerInvariant();
                if (lower == "spine1" || lower == "hips") rootBone = t;
                if (lower == "head" && headBone == null) headBone = t;
                if (lower == "neck" && neckBone == null) neckBone = t;
            }

            companionAI.headBone = headBone;
            companionAI.neckBone = neckBone;

            // Materials
            Material matHead = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Shinobu/Shinobu_Head.mat");
            Material matBody = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Shinobu/Shinobu_Body.mat");
            Material matLimbs = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Shinobu/Shinobu_Limbs.mat");
            Material matEyes = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Shinobu/Shinobu_Eyes.mat");
            Material matHair = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Shinobu/Shinobu_Hair.mat");
            Material matShirt = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Shinobu/Shinobu_Shirt.mat");
            Material matSkirt = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Shinobu/Shinobu_Skirt.mat");
            Material matPanties = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Shinobu/Shinobu_Panties.mat");
            Material matShoes = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Shinobu/Shinobu_Shoes.mat");

            // 1. Attach Body: Torso (slot 0) & Limbs/Hands/Feet (slot 1)
            SkinnedMeshRenderer bodySMR = AttachSubMesh(
                instance,
                "Assets/Resources/Models/loli/combined_LOD.fbx",
                new Material[] { matBody, matLimbs },
                boneMap,
                rootBone
            );

            // 2. Attach Head with blendshapes (Elements: 0:Head, 1:Head, 2:Eyes, 3:Head, 4:Eyes)
            AttachSubMesh(
                instance,
                "Assets/Resources/Models/loli/head.fbx",
                new Material[] { matHead, matHead, matEyes, matHead, matEyes },
                boneMap,
                headBone,
                (smr) => { companionAI.headSMR = smr; }
            );

            // 3. Attach Hair directly under companion root
            AttachHair(instance, matHair);

            // 4. Attach Shirt
            AttachSubMesh(
                instance,
                "Assets/Models/clothes/prefabs/shirt 1.prefab",
                new Material[] { matShirt },
                boneMap,
                rootBone
            );

            // 5. Attach Panties
            AttachSubMesh(
                instance,
                "Assets/Models/clothes/prefabs/panty 1.prefab",
                new Material[] { matPanties },
                boneMap,
                rootBone
            );

            // 6. Attach Skirt directly under companion root
            AttachSkirt(
                instance,
                matSkirt,
                boneMap,
                rootBone
            );

            // Save Prefabs
            Directory.CreateDirectory(Path.GetDirectoryName(PrefabSavePath));
            Directory.CreateDirectory(Path.GetDirectoryName(PrefabCompanionPath));

            GameObject prefabAsset = PrefabUtility.SaveAsPrefabAssetAndConnect(instance, PrefabSavePath, InteractionMode.AutomatedAction);
            PrefabUtility.SaveAsPrefabAsset(instance, PrefabCompanionPath);

            Debug.Log($"[CompanionPrefabBuilder] Successfully created Shinobu Companion prefab at {PrefabSavePath} and {PrefabCompanionPath}");

            // Clean up temporary scene object
            Object.DestroyImmediate(instance);

            // Update RagdollSpawner reference if it exists
            UpdateRagdollSpawnerReference(prefabAsset);

            // If ShinobuCompanion exists in the open active scene, replace it with fresh instance
            GameObject existingInScene = GameObject.Find("ShinobuCompanion");
            if (existingInScene != null && PrefabUtility.IsPartOfAnyPrefab(existingInScene))
            {
                Vector3 curPos = existingInScene.transform.position;
                if (curPos.sqrMagnitude < 1f)
                {
                    var sceneName = EditorSceneManager.GetActiveScene().name;
                    curPos = sceneName.Contains("Landscape") ? new Vector3(735.95f, 86.75f, 1117.58f) : new Vector3(-299.5f, 100.5f, 57.07f);
                }

                if (NavMesh.SamplePosition(curPos, out NavMeshHit hit, 15f, NavMesh.AllAreas))
                {
                    curPos = hit.position;
                }

                Quaternion curRot = existingInScene.transform.rotation;
                Transform curParent = existingInScene.transform.parent;
                Undo.DestroyObjectImmediate(existingInScene);
                GameObject freshInstance = (GameObject)PrefabUtility.InstantiatePrefab(prefabAsset);
                freshInstance.name = "ShinobuCompanion";
                freshInstance.transform.SetParent(curParent, false);
                freshInstance.transform.position = curPos;
                freshInstance.transform.rotation = curRot;
                var activeScene = EditorSceneManager.GetActiveScene();
                EditorSceneManager.MarkSceneDirty(activeScene);
                EditorSceneManager.SaveScene(activeScene);
                Debug.Log($"[CompanionPrefabBuilder] Updated ShinobuCompanion in active scene '{activeScene.name}' at {curPos}");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return prefabAsset;
        }

        private static SkinnedMeshRenderer AttachSubMesh(
            GameObject root,
            string assetPath,
            Material[] materials,
            Dictionary<string, Transform> boneMap,
            Transform fallbackRootBone,
            System.Action<SkinnedMeshRenderer> onCreated = null)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (asset == null)
            {
                Debug.LogWarning($"[CompanionPrefabBuilder] Could not find asset at {assetPath}");
                return null;
            }

            GameObject subObj = Object.Instantiate(asset);
            SkinnedMeshRenderer srcSMR = subObj.GetComponentInChildren<SkinnedMeshRenderer>(true);
            SkinnedMeshRenderer newSMR = null;

            if (srcSMR != null && srcSMR.sharedMesh != null)
            {
                GameObject container = new GameObject(Path.GetFileNameWithoutExtension(assetPath));
                container.transform.SetParent(root.transform, false);

                container.transform.localPosition = srcSMR.transform.localPosition;
                container.transform.localRotation = srcSMR.transform.localRotation;
                container.transform.localScale = srcSMR.transform.localScale;

                newSMR = container.AddComponent<SkinnedMeshRenderer>();
                newSMR.sharedMesh = srcSMR.sharedMesh;

                int submeshCount = srcSMR.sharedMesh.subMeshCount;
                Material[] assignedMats = new Material[submeshCount];
                for (int m = 0; m < submeshCount; m++)
                {
                    if (materials != null && m < materials.Length && materials[m] != null)
                    {
                        assignedMats[m] = materials[m];
                    }
                    else if (materials != null && materials.Length > 0 && materials[0] != null)
                    {
                        assignedMats[m] = materials[0];
                    }
                }
                newSMR.sharedMaterials = assignedMats;

                // Remap bones to the root armature
                Transform[] oldBones = srcSMR.bones;
                Transform[] remappedBones = new Transform[oldBones.Length];
                for (int b = 0; b < oldBones.Length; b++)
                {
                    if (oldBones[b] != null && boneMap.TryGetValue(oldBones[b].name, out Transform mapped))
                    {
                        remappedBones[b] = mapped;
                    }
                    else
                    {
                        remappedBones[b] = fallbackRootBone != null ? fallbackRootBone : root.transform;
                    }
                }
                newSMR.bones = remappedBones;

                Transform targetRoot = fallbackRootBone;
                if (srcSMR.rootBone != null && boneMap.TryGetValue(srcSMR.rootBone.name, out Transform mappedRoot))
                {
                    targetRoot = mappedRoot;
                }
                newSMR.rootBone = targetRoot != null ? targetRoot : root.transform;

                onCreated?.Invoke(newSMR);
            }

            Object.DestroyImmediate(subObj);
            return newSMR;
        }

        private static void AttachHair(GameObject root, Material matHair)
        {
            string hairPath = "Assets/Resources/Models/loli/hair.fbx";
            GameObject hairAsset = AssetDatabase.LoadAssetAtPath<GameObject>(hairPath);
            if (hairAsset == null)
            {
                Debug.LogWarning($"[CompanionPrefabBuilder] Could not find hair asset at {hairPath}");
                return;
            }

            GameObject hairInstance = Object.Instantiate(hairAsset);
            hairInstance.name = "hair";

            // Direct child of root, preserving origin coordinate space
            hairInstance.transform.SetParent(root.transform, false);
            hairInstance.transform.localPosition = Vector3.zero;
            hairInstance.transform.localRotation = Quaternion.identity;
            hairInstance.transform.localScale = Vector3.one;

            // Assign material to all renderers in hair
            foreach (var smr in hairInstance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (matHair != null) smr.sharedMaterial = matHair;
            }
        }

        private static void AttachSkirt(
            GameObject root,
            Material matSkirt,
            Dictionary<string, Transform> boneMap,
            Transform rootBone)
        {
            string fbxPath = "Assets/Models/clothes/skirt 1.fbx";
            GameObject fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (fbxAsset == null) return;

            MeshFilter srcMF = fbxAsset.GetComponentInChildren<MeshFilter>(true);
            if (srcMF == null || srcMF.sharedMesh == null) return;

            Transform spine1 = null;
            boneMap.TryGetValue("spine1", out spine1);
            Transform parent = spine1 != null ? spine1 : root.transform;

            GameObject skirtObj = new GameObject("skirt 1");
            skirtObj.transform.position = root.transform.position;
            skirtObj.transform.rotation = root.transform.rotation;
            skirtObj.transform.localScale = Vector3.one;
            skirtObj.transform.SetParent(parent, true);

            MeshFilter mf = skirtObj.AddComponent<MeshFilter>();
            mf.sharedMesh = srcMF.sharedMesh;

            MeshRenderer mr = skirtObj.AddComponent<MeshRenderer>();
            if (matSkirt != null) mr.sharedMaterial = matSkirt;
        }

        private static void AttachShoes(
            GameObject root,
            string prefabPath,
            Material matShoes,
            Dictionary<string, Transform> boneMap)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (asset == null)
            {
                Debug.LogWarning($"[CompanionPrefabBuilder] Could not find shoes asset at {prefabPath}");
                return;
            }

            GameObject container = Object.Instantiate(asset);

            Transform footR = null;
            Transform footL = null;
            boneMap.TryGetValue("foot_r", out footR);
            boneMap.TryGetValue("foot_l", out footL);

            Transform shoeR = container.transform.Find("shoe1_r");
            Transform shoeL = container.transform.Find("shoe1_l");

            if (shoeR != null && footR != null)
            {
                shoeR.SetParent(footR, false);
                shoeR.localPosition = Vector3.zero;
                shoeR.localRotation = Quaternion.identity;
                shoeR.localScale = Vector3.one;
                MeshRenderer mr = shoeR.GetComponent<MeshRenderer>();
                if (mr != null && matShoes != null) mr.sharedMaterial = matShoes;
            }

            if (shoeL != null && footL != null)
            {
                shoeL.SetParent(footL, false);
                shoeL.localPosition = Vector3.zero;
                shoeL.localRotation = Quaternion.identity;
                shoeL.localScale = Vector3.one;
                MeshRenderer mr = shoeL.GetComponent<MeshRenderer>();
                if (mr != null && matShoes != null) mr.sharedMaterial = matShoes;
            }

            Object.DestroyImmediate(container);
        }

        private static void UpdateRagdollSpawnerReference(GameObject companionPrefab)
        {
            string spawnerPrefabPath = "Assets/Prefabs/Dev/RagdollSpawner.prefab";
            GameObject spawnerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(spawnerPrefabPath);
            if (spawnerPrefab != null)
            {
                RagdollSpawner spawner = spawnerPrefab.GetComponent<RagdollSpawner>();
                if (spawner != null)
                {
                    SerializedObject so = new SerializedObject(spawner);
                    SerializedProperty prop = so.FindProperty("characterPrefab");
                    if (prop != null)
                    {
                        prop.objectReferenceValue = companionPrefab;
                        so.ApplyModifiedProperties();
                        EditorUtility.SetDirty(spawnerPrefab);
                        Debug.Log("[CompanionPrefabBuilder] Assigned ShinobuCompanion to RagdollSpawner.characterPrefab");
                    }
                }
            }
        }

        [MenuItem("OpenViva/Spawn Shinobu in Active Scene", false, 2)]
        public static void SpawnShinobuInActiveScene()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabSavePath);
            if (prefab == null)
            {
                prefab = BuildShinobuPrefab();
            }

            if (prefab == null)
            {
                Debug.LogError("[CompanionPrefabBuilder] Failed to get or build Shinobu Companion prefab!");
                return;
            }

            Vector3 spawnPos = new Vector3(735.95f, 86.75f, 1117.58f);

            Camera mainCam = Camera.main;
            if (mainCam != null)
            {
                Vector3 camPos = mainCam.transform.position;
                Vector3 forward = mainCam.transform.forward;
                forward.y = 0;
                if (forward.sqrMagnitude > 0.01f)
                {
                    spawnPos = camPos + forward.normalized * 2.5f;
                    if (NavMesh.SamplePosition(spawnPos, out NavMeshHit hit, 5f, NavMesh.AllAreas))
                    {
                        spawnPos = hit.position;
                    }
                }
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.position = spawnPos;
            if (mainCam != null)
            {
                Vector3 look = (mainCam.transform.position - spawnPos);
                look.y = 0;
                if (look.sqrMagnitude > 0.01f) instance.transform.rotation = Quaternion.LookRotation(look);
            }

            Undo.RegisterCreatedObjectUndo(instance, "Spawn Shinobu Companion");
            Selection.activeGameObject = instance;
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"[CompanionPrefabBuilder] Spawned Shinobu Companion at {spawnPos}");
        }
    }
}
