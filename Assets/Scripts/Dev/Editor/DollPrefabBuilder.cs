using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Animations.SpringBones;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Viva.Dolls;

namespace Viva.Dev
{
    public static class DollPrefabBuilder
    {
        public const string DefaultDollPrefabPath = "Assets/Prefabs/Dolls/DefaultDoll.prefab";
        public const string DefaultDollResourcePath = "Assets/Resources/Prefabs/DefaultDoll.prefab";

        private const string ArmaturePath = "Assets/Materials/Dolls/Models/default_armature.fbx";
        private const string BodyLodPath = "Assets/Materials/Dolls/Models/default_body_LOD.fbx";
        private const string BodyFullPath = "Assets/Materials/Dolls/Models/default_body.fbx";
        private const string BodyNeckSeamPath = "Assets/Materials/Dolls/Models/default_body_neckseam.fbx";

        private const string TorsoTexPath = "Assets/Materials/Dolls/Textures/default_torso.psd";
        private const string LimbsTexPath = "Assets/Materials/Dolls/Textures/default_limbs.psd";

        private const string TorsoMatPath = "Assets/Materials/Dolls/Materials/default_torso.mat";
        private const string LimbsMatPath = "Assets/Materials/Dolls/Materials/default_limbs.mat";
        private const string HeadMatPath = "Assets/Materials/Dolls/Materials/default_head.mat";
        private const string PupilMatPath = "Assets/Materials/Dolls/Materials/default_pupil.mat";
        private const string ShirtMatPath = "Assets/Materials/Dolls/Materials/default_shirt.mat";
        private const string SkirtMatPath = "Assets/Materials/Dolls/Materials/default_skirt.mat";
        private const string PantiesMatPath = "Assets/Materials/Dolls/Materials/default_panties.mat";
        private const string PreviewMirrorMatPath = "Assets/Materials/Dolls/Materials/default_preview_mirror.mat";
        private const string PoseCachePath = "Assets/Materials/Dolls/Pose/default_apose.asset";
        private const string DollAvatarPath = "Assets/Materials/Dolls/Pose/DollAvatar.asset";

        [MenuItem("OpenViva/Dolls/1. Configure Doll Assets & Materials", false, 10)]
        public static void ConfigureDollAssetsAndMaterials()
        {
            ConfigureCardDataMasks();
            ConfigureFbxImporters();
            AssetDatabase.Refresh();
            CreateOrUpdateDollMaterials();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[DollPrefabBuilder] Configured Doll assets and materials.");
        }

        [MenuItem("OpenViva/Dolls/2. Build DefaultDoll Prefab", false, 11)]
        public static GameObject BuildDefaultDollPrefab()
        {
            ConfigureDollAssetsAndMaterials();

            GameObject armatureAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ArmaturePath);
            if (armatureAsset == null)
            {
                Debug.LogError($"[DollPrefabBuilder] Missing armature at {ArmaturePath}");
                return null;
            }

            GameObject instance = Object.Instantiate(armatureAsset);
            instance.name = "DefaultDoll";
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

            int interactableLayer = LayerMask.NameToLayer("Interactable");
            if (interactableLayer != -1) instance.layer = interactableLayer;

            instance.SetActive(false);
            NavMeshAgent agent = DollUtilities.EnsureComponent<NavMeshAgent>(instance);
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

            Animator animator = DollUtilities.EnsureComponent<Animator>(instance);
            RuntimeAnimatorController ac = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Resources/Animations/LoliAC.controller");
            if (ac != null) animator.runtimeAnimatorController = ac;

            BoxCollider col = DollUtilities.EnsureComponent<BoxCollider>(instance);
            col.center = new Vector3(0f, 0.65f, -0.03f);
            col.size = new Vector3(0.36f, 1.3f, 0.28f);
            col.isTrigger = true;

            AudioSource audio = DollUtilities.EnsureComponent<AudioSource>(instance);
            audio.spatialBlend = 1.0f;
            audio.minDistance = 1.0f;
            audio.maxDistance = 25.0f;
            audio.playOnAwake = false;

            CharacterInteractable interactable = DollUtilities.EnsureComponent<CharacterInteractable>(instance);
            interactable.characterName = "Doll";

            CompanionAI companionAI = DollUtilities.EnsureComponent<CompanionAI>(instance);
            companionAI.stoppingDistance = 1.8f;
            companionAI.walkSpeed = 1.5f;
            companionAI.runSpeed = 3.2f;

            Dictionary<string, Transform> boneMap = new Dictionary<string, Transform>();
            Transform rootBone = null;
            Transform headBone = null;
            Transform neckBone = null;
            Transform armatureRoot = null;

            foreach (Transform t in instance.GetComponentsInChildren<Transform>(true))
            {
                string lower = t.name.ToLowerInvariant();
                if (armatureRoot == null && lower.Contains("armature"))
                {
                    armatureRoot = t;
                    t.name = "DollArmature";
                }
                if (!boneMap.ContainsKey(t.name)) boneMap.Add(t.name, t);
                if (lower == "spine1" || lower == "hips") rootBone = t;
                if (lower == "head" && headBone == null) headBone = t;
                if (lower == "neck" && neckBone == null) neckBone = t;
            }

            companionAI.headBone = headBone;
            companionAI.neckBone = neckBone;

            Material matTorso = AssetDatabase.LoadAssetAtPath<Material>(TorsoMatPath);
            Material matLimbs = AssetDatabase.LoadAssetAtPath<Material>(LimbsMatPath);
            Material matHeadDefault = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Shinobu/Shinobu_Head.mat");
            Material matEyesDefault = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Shinobu/Shinobu_Eyes.mat");
            Material matCheeksDefault = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Shinobu/Shinobu_Cheeks.mat");
            Material matHairDefault = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Shinobu/Shinobu_Hair.mat");
            Material matShirt = AssetDatabase.LoadAssetAtPath<Material>(ShirtMatPath) ?? AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Shinobu/Shinobu_Shirt.mat");
            Material matSkirt = AssetDatabase.LoadAssetAtPath<Material>(SkirtMatPath) ?? AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Shinobu/Shinobu_Skirt.mat");
            Material matPanties = AssetDatabase.LoadAssetAtPath<Material>(PantiesMatPath) ?? AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Shinobu/Shinobu_Panties.mat");

            // Build Humanoid DollAvatar BEFORE attaching head/hair/cloth meshes (which add duplicate 'head'/'spine3' names)
            Avatar dollAvatar = CreateOrUpdateDollAvatar(instance);
            if (dollAvatar != null) animator.avatar = dollAvatar;

            // 1. Body model & textures (use neck-seam vertex-color body if present; note [uvTest/limbs, torso] submesh order)
            string bodyAssetPath = File.Exists(BodyNeckSeamPath) ? BodyNeckSeamPath : BodyLodPath;
            Material[] bodyMaterials = bodyAssetPath == BodyNeckSeamPath
                ? new[] { matLimbs, matTorso }
                : new[] { matTorso, matLimbs };
            SkinnedMeshRenderer bodySMR = AttachSubMesh(
                instance,
                bodyAssetPath,
                "default_body",
                bodyMaterials,
                boneMap,
                rootBone
            );
            DollPoseCache poseCache = CreateOrUpdatePoseCache(bodySMR);

            // 2. Fallback default head (replaced dynamically when loading a card via Stegosaurus)
            SkinnedMeshRenderer headSMR = AttachSubMesh(
                instance,
                "Assets/Resources/Models/loli/head.fbx",
                "head",
                new[] { matHeadDefault, matHeadDefault, matEyesDefault, matCheeksDefault != null ? matCheeksDefault : matHeadDefault, matEyesDefault },
                boneMap,
                headBone
            );
            if (headSMR == null)
            {
                GameObject headGo = new GameObject("head");
                headGo.transform.SetParent(instance.transform, false);
                headSMR = headGo.AddComponent<SkinnedMeshRenderer>();
                headSMR.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(HeadMatPath);
                headSMR.rootBone = headBone;
            }
            companionAI.headSMR = headSMR;

            // 3. Fallback default hair (hidden automatically when a card head is loaded)
            GameObject hairObj = AttachDefaultHair(instance, matHairDefault, headBone);

            // 4. Default clothing (shirt, panties, skirt)
            AttachSubMesh(instance, "Assets/Models/clothes/prefabs/shirt 1.prefab", "shirt 1", new[] { matShirt }, boneMap, rootBone);
            AttachSubMesh(instance, "Assets/Models/clothes/prefabs/panty 1.prefab", "panty 1", new[] { matPanties }, boneMap, rootBone);
            AttachSkirt(instance, matSkirt, boneMap);

            // 5. Wire DollInstance & SpringManager
            DollInstance doll = DollUtilities.EnsureComponent<DollInstance>(instance);
            doll.bodyArmature = armatureRoot != null ? armatureRoot : instance.transform;
            doll.headBone = headBone;
            doll.headSMR = headSMR;
            doll.bodySMRs = bodySMR != null ? new[] { bodySMR } : System.Array.Empty<SkinnedMeshRenderer>();
            doll.defaultHairRoot = hairObj;
            doll.basePoseCache = poseCache;
            doll.EnsureEyeTransforms();

            SpringManager springManager = DollUtilities.EnsureComponent<SpringManager>(instance);
            springManager.automaticUpdates = true;
            springManager.simulationFrameRate = 60;
            springManager.dynamicRatio = 1.0f;
            springManager.gravity = new Vector3(0f, -9.81f, 0f);
            springManager.bounce = 0.05f;
            springManager.friction = 0.8f;
            springManager.collideWithGround = false;
            springManager.enableCollision = true;

            // 6. Physical Reaction & Ragdoll
            CompanionPhysicalReaction physReaction = DollUtilities.EnsureComponent<CompanionPhysicalReaction>(instance);
            physReaction.EnsureRagdollBones();

            // Save Prefab
            Directory.CreateDirectory(Path.GetDirectoryName(DefaultDollPrefabPath));
            Directory.CreateDirectory(Path.GetDirectoryName(DefaultDollResourcePath));

            GameObject prefabAsset = PrefabUtility.SaveAsPrefabAssetAndConnect(instance, DefaultDollPrefabPath, InteractionMode.AutomatedAction);
            PrefabUtility.SaveAsPrefabAsset(instance, DefaultDollResourcePath);
            Object.DestroyImmediate(instance);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[DollPrefabBuilder] Built DefaultDoll prefab at {DefaultDollPrefabPath}");
            return prefabAsset;
        }

        private static void ConfigureCardDataMasks()
        {
            string[] maskFiles =
            {
                "Assets/Textures/CardData/characterPackingMask.psd",
                "Assets/Textures/CardData/characterCardBorderAlpha.psd",
                "Assets/Textures/CardData/characterCardBorderGrey.psd",
                "Assets/Textures/CardData/skinPackingMask.psd",
                "Assets/Textures/CardData/skinCardBorderAlpha.psd",
                "Assets/Textures/CardData/skinCardBorderGrey.psd"
            };

            foreach (string path in maskFiles)
            {
                if (AssetImporter.GetAtPath(path) is TextureImporter ti)
                {
                    ti.textureType = TextureImporterType.SingleChannel;
                    ti.sRGBTexture = false;
                    ti.mipmapEnabled = false;
                    ti.isReadable = true;
                    ti.npotScale = TextureImporterNPOTScale.None;
                    ti.textureCompression = TextureImporterCompression.Uncompressed;
                    ti.filterMode = FilterMode.Point;
                    ti.alphaSource = TextureImporterAlphaSource.FromGrayScale;
                    ti.alphaIsTransparency = true;

                    var settings = new TextureImporterSettings();
                    ti.ReadTextureSettings(settings);
                    settings.singleChannelComponent = TextureImporterSingleChannelComponent.Alpha;
                    settings.alphaSource = TextureImporterAlphaSource.FromGrayScale;
                    settings.alphaIsTransparency = true;
                    settings.readable = true;
                    settings.mipmapEnabled = false;
                    settings.sRGBTexture = false;
                    ti.SetTextureSettings(settings);

                    var defaultPlatform = ti.GetDefaultPlatformTextureSettings();
                    defaultPlatform.format = TextureImporterFormat.Alpha8;
                    defaultPlatform.textureCompression = TextureImporterCompression.Uncompressed;
                    ti.SetPlatformTextureSettings(defaultPlatform);

                    ti.SaveAndReimport();
                }
            }
        }

        private static void ConfigureFbxImporters()
        {
            if (AssetImporter.GetAtPath(BodyFullPath) is ModelImporter bodyImporter)
            {
                bodyImporter.globalScale = 3.75f;
                bodyImporter.useFileUnits = true;
                bodyImporter.isReadable = true;
                bodyImporter.SaveAndReimport();
            }

            if (AssetImporter.GetAtPath(BodyLodPath) is ModelImporter lodImporter)
            {
                lodImporter.globalScale = 3.75f;
                lodImporter.useFileUnits = true;
                lodImporter.isReadable = true;
                lodImporter.SaveAndReimport();
            }

            if (AssetImporter.GetAtPath(BodyNeckSeamPath) is ModelImporter neckImporter)
            {
                neckImporter.globalScale = 3.75f;
                neckImporter.useFileUnits = true;
                neckImporter.isReadable = true;
                neckImporter.SaveAndReimport();
            }

            if (AssetImporter.GetAtPath(ArmaturePath) is ModelImporter armImporter)
            {
                armImporter.globalScale = 3.75f;
                armImporter.useFileUnits = true;
                armImporter.isReadable = true;
                armImporter.animationType = ModelImporterAnimationType.Human;
                armImporter.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                armImporter.SaveAndReimport();
            }
        }

        private static void CreateOrUpdateDollMaterials()
        {
            Directory.CreateDirectory("Assets/Materials/Dolls/Materials");

            Shader bodyShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/Dolls/AnimeBody.shader")
                ?? Shader.Find("Anime/DollBody")
                ?? Shader.Find("Universal Render Pipeline/Lit");
            Shader headShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/Dolls/AnimeHead.shader")
                ?? Shader.Find("Anime/DollHead")
                ?? Shader.Find("Universal Render Pipeline/Lit");
            Shader pupilShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/Dolls/AnimePupil.shader")
                ?? Shader.Find("Anime/DollPupil")
                ?? Shader.Find("Universal Render Pipeline/Lit");
            Shader mirrorShader = Shader.Find("Universal Render Pipeline/Unlit");

            Texture2D torsoTex = AssetDatabase.LoadAssetAtPath<Texture2D>(TorsoTexPath);
            Texture2D limbsTex = AssetDatabase.LoadAssetAtPath<Texture2D>(LimbsTexPath);

            Color defaultSkin = new Color(1f, 0.90f, 0.87f, 1f);
            Color defaultShade = new Color(0.95f, 0.80f, 0.77f, 1f);

            Material torsoMat = LoadOrCreateMaterial(TorsoMatPath, bodyShader);
            torsoMat.SetTexture("_MainTex", torsoTex);
            if (torsoMat.HasProperty("_SkinColor")) torsoMat.SetColor("_SkinColor", defaultSkin);
            if (torsoMat.HasProperty("_SkinShadeColor")) torsoMat.SetColor("_SkinShadeColor", defaultShade);
            if (torsoMat.HasProperty("_NeckColor")) torsoMat.SetColor("_NeckColor", defaultSkin);
            if (torsoMat.HasProperty("_NeckBlendStrength")) torsoMat.SetFloat("_NeckBlendStrength", 1.0f);
            EditorUtility.SetDirty(torsoMat);

            Material limbsMat = LoadOrCreateMaterial(LimbsMatPath, bodyShader);
            limbsMat.SetTexture("_MainTex", limbsTex);
            if (limbsMat.HasProperty("_SkinColor")) limbsMat.SetColor("_SkinColor", defaultSkin);
            if (limbsMat.HasProperty("_SkinShadeColor")) limbsMat.SetColor("_SkinShadeColor", defaultShade);
            EditorUtility.SetDirty(limbsMat);

            Material headMat = LoadOrCreateMaterial(HeadMatPath, headShader);
            EditorUtility.SetDirty(headMat);

            Material pupilMat = LoadOrCreateMaterial(PupilMatPath, pupilShader);
            EditorUtility.SetDirty(pupilMat);

            Material previewMirrorMat = LoadOrCreateMaterial(PreviewMirrorMatPath, mirrorShader);
            if (previewMirrorMat.HasProperty("_BaseColor")) previewMirrorMat.SetColor("_BaseColor", Color.white);
            EditorUtility.SetDirty(previewMirrorMat);

            CopyMaterialIfPresent("Assets/Materials/Shinobu/Shinobu_Shirt.mat", ShirtMatPath);
            CopyMaterialIfPresent("Assets/Materials/Shinobu/Shinobu_Skirt.mat", SkirtMatPath);
            CopyMaterialIfPresent("Assets/Materials/Shinobu/Shinobu_Panties.mat", PantiesMatPath);
        }

        private static void CopyMaterialIfPresent(string srcPath, string dstPath)
        {
            if (AssetDatabase.LoadAssetAtPath<Material>(srcPath) == null) return;
            if (AssetDatabase.LoadAssetAtPath<Material>(dstPath) != null) AssetDatabase.DeleteAsset(dstPath);
            AssetDatabase.CopyAsset(srcPath, dstPath);
        }

        private static Material LoadOrCreateMaterial(string path, Shader shader)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (shader != null && mat.shader != shader)
            {
                mat.shader = shader;
            }
            return mat;
        }

        private static DollPoseCache CreateOrUpdatePoseCache(SkinnedMeshRenderer bodySMR)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PoseCachePath));
            DollPoseCache cache = AssetDatabase.LoadAssetAtPath<DollPoseCache>(PoseCachePath);
            if (cache == null)
            {
                cache = ScriptableObject.CreateInstance<DollPoseCache>();
                AssetDatabase.CreateAsset(cache, PoseCachePath);
            }

            if (bodySMR != null && bodySMR.bones != null)
            {
                Transform[] bones = bodySMR.bones;
                cache.positions = new Vector3[bones.Length];
                cache.quaternions = new Quaternion[bones.Length];
                for (int i = 0; i < bones.Length; i++)
                {
                    if (bones[i] != null)
                    {
                        cache.positions[i] = bones[i].localPosition;
                        cache.quaternions[i] = bones[i].localRotation;
                    }
                }
                EditorUtility.SetDirty(cache);
            }
            return cache;
        }

        private static Avatar CreateOrUpdateDollAvatar(GameObject root)
        {
            Avatar srcAvatar = AssetDatabase.LoadAllAssetsAtPath(ArmaturePath).OfType<Avatar>().FirstOrDefault();
            if (srcAvatar == null)
            {
                Debug.LogWarning($"[DollPrefabBuilder] Could not load source Avatar from {ArmaturePath}");
                return null;
            }

            HumanDescription desc = srcAvatar.humanDescription;

            // Pose a temporary clone into the source avatar's calibrated T-pose (A-pose FBX transforms
            // otherwise bake a ~47 deg arm offset as 0-muscle T-pose and pull arms behind the back).
            GameObject tPoseRoot = Object.Instantiate(root);
            tPoseRoot.name = root.name;
            try
            {
                var srcBones = new Dictionary<string, SkeletonBone>();
                SkeletonBone[] srcSkel = desc.skeleton;
                for (int i = 0; i < srcSkel.Length; i++)
                {
                    string key = i == 0 ? root.name : (srcSkel[i].name == "ShinobuArmature" ? "DollArmature" : srcSkel[i].name);
                    if (!srcBones.ContainsKey(key)) srcBones[key] = srcSkel[i];
                }

                Transform[] tPoseTransforms = tPoseRoot.GetComponentsInChildren<Transform>(true);
                SkeletonBone[] skeleton = new SkeletonBone[tPoseTransforms.Length];
                for (int i = 0; i < tPoseTransforms.Length; i++)
                {
                    Transform t = tPoseTransforms[i];
                    if (srcBones.TryGetValue(t.name, out SkeletonBone srcBone))
                    {
                        t.localPosition = srcBone.position;
                        t.localRotation = srcBone.rotation;
                        t.localScale = srcBone.scale;
                    }
                    skeleton[i] = new SkeletonBone
                    {
                        name = t.name,
                        position = t.localPosition,
                        rotation = t.localRotation,
                        scale = t.localScale
                    };
                }
                desc.skeleton = skeleton;

                Avatar newAvatar = AvatarBuilder.BuildHumanAvatar(tPoseRoot, desc);
                newAvatar.name = "DollAvatar";
                if (!newAvatar.isValid || !newAvatar.isHuman)
                {
                    Debug.LogError("[DollPrefabBuilder] Failed to build valid Humanoid DollAvatar!");
                    Object.DestroyImmediate(newAvatar);
                    return srcAvatar;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(DollAvatarPath));
                if (AssetDatabase.LoadAssetAtPath<Avatar>(DollAvatarPath) != null)
                {
                    AssetDatabase.DeleteAsset(DollAvatarPath);
                }
                AssetDatabase.CreateAsset(newAvatar, DollAvatarPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Avatar saved = AssetDatabase.LoadAssetAtPath<Avatar>(DollAvatarPath);
                Debug.Log($"[DollPrefabBuilder] Built DollAvatar valid={saved != null && saved.isValid} human={saved != null && saved.isHuman}");
                return saved;
            }
            finally
            {
                Object.DestroyImmediate(tPoseRoot);
            }
        }

        private static SkinnedMeshRenderer AttachSubMesh(
            GameObject root,
            string assetPath,
            string containerName,
            Material[] materials,
            Dictionary<string, Transform> boneMap,
            Transform fallbackRootBone)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (asset == null)
            {
                Debug.LogWarning($"[DollPrefabBuilder] Asset not found at {assetPath}");
                return null;
            }

            GameObject subObj = Object.Instantiate(asset);
            SkinnedMeshRenderer srcSMR = subObj.GetComponentInChildren<SkinnedMeshRenderer>(true);
            SkinnedMeshRenderer newSMR = null;

            if (srcSMR != null && srcSMR.sharedMesh != null)
            {
                GameObject container = new GameObject(containerName);
                container.transform.SetParent(root.transform, false);
                container.transform.localPosition = srcSMR.transform.localPosition;
                container.transform.localRotation = srcSMR.transform.localRotation;
                container.transform.localScale = srcSMR.transform.localScale;

                newSMR = container.AddComponent<SkinnedMeshRenderer>();
                newSMR.sharedMesh = srcSMR.sharedMesh;
                newSMR.updateWhenOffscreen = true;

                int submeshCount = srcSMR.sharedMesh.subMeshCount;
                Material[] assignedMats = new Material[submeshCount];
                for (int m = 0; m < submeshCount; m++)
                {
                    if (materials != null && m < materials.Length && materials[m] != null)
                        assignedMats[m] = materials[m];
                    else if (materials != null && materials.Length > 0 && materials[0] != null)
                        assignedMats[m] = materials[0];
                }
                newSMR.sharedMaterials = assignedMats;

                Transform[] oldBones = srcSMR.bones;
                Transform[] remappedBones = new Transform[oldBones.Length];
                for (int b = 0; b < oldBones.Length; b++)
                {
                    if (oldBones[b] != null && boneMap.TryGetValue(oldBones[b].name, out Transform mapped))
                        remappedBones[b] = mapped;
                    else
                        remappedBones[b] = fallbackRootBone != null ? fallbackRootBone : root.transform;
                }
                newSMR.bones = remappedBones;

                Transform targetRoot = fallbackRootBone;
                if (srcSMR.rootBone != null && boneMap.TryGetValue(srcSMR.rootBone.name, out Transform mappedRoot))
                {
                    targetRoot = mappedRoot;
                }
                newSMR.rootBone = targetRoot != null ? targetRoot : root.transform;
                newSMR.localBounds = srcSMR.sharedMesh.bounds;
            }

            Object.DestroyImmediate(subObj);
            return newSMR;
        }

        private static GameObject AttachDefaultHair(GameObject root, Material matHair, Transform headBone)
        {
            GameObject hairAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Models/loli/hair.fbx");
            if (hairAsset == null) return null;

            GameObject hairInstance = Object.Instantiate(hairAsset);
            hairInstance.name = "hair";
            hairInstance.transform.SetParent(root.transform, false);

            Transform hairArmature = hairInstance.transform.Find("hairArmature");
            if (hairArmature != null && headBone != null)
            {
                hairArmature.SetParent(headBone, true);
            }

            foreach (var smr in hairInstance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (matHair != null) smr.sharedMaterial = matHair;
                if (headBone != null) smr.rootBone = headBone;
                smr.updateWhenOffscreen = true;
                if (smr.sharedMesh != null) smr.localBounds = smr.sharedMesh.bounds;
            }

            return hairInstance;
        }

        private static void AttachSkirt(GameObject root, Material matSkirt, Dictionary<string, Transform> boneMap)
        {
            GameObject fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/clothes/skirt 1.fbx");
            if (fbxAsset == null) return;

            MeshFilter srcMF = fbxAsset.GetComponentInChildren<MeshFilter>(true);
            if (srcMF == null || srcMF.sharedMesh == null) return;

            boneMap.TryGetValue("spine1", out Transform spine1);
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
    }
}
