using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Animations.SpringBones;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace Viva.Dolls
{
    public class DollModelBuildSettings
    {
        public readonly Material headMaterialTemplate;
        public readonly Material pupilMaterialTemplate;

        public string[] requiredMaterials;
        public Texture2D modelTexture;
        public Texture2D rightEyeTexture;
        public Texture2D leftEyeTexture;

        public int boneCount = 0;
        public int vertexCount = 0;
        public int shapeKeyCount = 0;
        public int materialCount = 0;
        public string[] boneNameTable = null;
        public Vector3[] boneHeadTable = null;
        public Vector3[] boneTailTable = null;
        public float[] boneRollTable = null;
        public bool[] IsPartOfBaseSkeletonTable = null;
        public int[] boneHierarchyChildCountTable = null;
        public int[] childBoneIndicesTable = null;
        public string[] materialNameTable = null;
        public Vector3[] vertexTable = null;
        public Vector3[] normalTable = null;
        public Vector2[] uvTable = null;
        public int[] triVertexIndicesTable = null;
        public BoneWeight[] boneWeightsTable = null;
        public int[] submeshTriCountTable = null;
        public string[] shapeKeyNamesTable = null;
        public int[] shapeKeyLengthsTable = null;
        public int[] vertexDeltaIndicesTable = null;
        public Vector3[] vertexDeltaOffsetTable = null;
        public Vector4 hatLocalPosAndPitch = Vector4.zero;
        public Vector4 headpatWorldSphere = Vector4.zero;
        public Vector4 headCollisionWorldSphere = Vector4.zero;

        public DollModelBuildSettings(Material _headMaterialTemplate, Material _pupilMaterialTemplate, string[] _requiredMaterials)
        {
            headMaterialTemplate = _headMaterialTemplate;
            pupilMaterialTemplate = _pupilMaterialTemplate;
            requiredMaterials = _requiredMaterials;
        }
    }

    public class DollModel
    {
        public class CreateDollRequest
        {
            public readonly string sourceCardFilename;
            public readonly DollInstance targetDoll;
            public readonly byte[] data;
            public readonly DollModelBuildSettings modelImportSettings;
            public readonly bool dataIncludesModelSettings;
            public readonly string dollName;
            public string error = null;
            public DollInstance result = null;
            public DollModelBuildSettings mbs = null;
            public DollModel builtModel = null;

            public CreateDollRequest(string _sourceCardFilename, DollInstance _targetDoll, byte[] _buildData, DollModelBuildSettings _modelImportSettings)
            {
                sourceCardFilename = _sourceCardFilename;
                targetDoll = _targetDoll;
                data = _buildData;
                dataIncludesModelSettings = true;
                modelImportSettings = _modelImportSettings;
            }

            public CreateDollRequest(DollInstance _targetDoll, string filePath, DollModelBuildSettings _modelImportSettings)
            {
                targetDoll = _targetDoll;
                dollName = Path.GetFileNameWithoutExtension(filePath);
                try
                {
                    data = File.ReadAllBytes(filePath);
                }
                catch
                {
                    data = null;
                }
                dataIncludesModelSettings = false;
                modelImportSettings = _modelImportSettings;
            }
        }

        public class DynamicBoneInfo
        {
            public readonly string rootBone;
            public float damping = 0.1f;
            public float elasticity = 0.05f;
            public float stiffness = 0.0f;
            public bool disableEndLength = true;
            public bool useGravity = true;
            public bool canRelax = false;
            public bool headCollision = false;
            public bool collisionBack = false;

            public DynamicBoneInfo(string _rootBone)
            {
                rootBone = _rootBone;
            }
        }

        private const float MODEL_IMPORT_VERSION = 1.2f;

        public static readonly DollCardTextureSerializer.CardTextureFormat[] skinFormat = new DollCardTextureSerializer.CardTextureFormat[]
        {
            new DollCardTextureSerializer.CardTextureFormat(1024, true),
            new DollCardTextureSerializer.CardTextureFormat(512, false),
            new DollCardTextureSerializer.CardTextureFormat(512, false),
        };

        public string error { get; private set; }
        public Mesh mesh { get; private set; }
        public Transform[] bonesShared { get; private set; }
        public string sourceCardFilename { get; private set; }
        public string name { get; private set; }

        protected int boneNamesBytesLength;
        protected int boneCount;
        protected int childBoneIndicesTableLength;
        protected int materialNamesBytesLength;
        protected int vertexCount;
        protected int materialCount;
        protected int triIndicesCount;
        protected int shapeKeyCount;
        protected int shapeKeyNamesByteLength;
        protected int shapeKeyVertexElements;

        public Texture2D texture = null;
        public Texture2D rightEyeTexture = null;
        public Texture2D leftEyeTexture = null;
        public float pupilSpanRadius = 0.5f;
        public Vector2 pupilOffset = Vector2.zero;
        public Color skinColor = Color.white;
        public byte voiceIndex;
        public bool fullBodyOverride;
        public Vector4 headpatWorldSphere;
        public Vector4 hatLocalPosAndPitch;
        public Vector4 headCollisionWorldSphere;
        public byte[] modelData = null;
        public List<DynamicBoneInfo> dynamicBoneInfos = new List<DynamicBoneInfo>();

        public DollModel(string _sourceCardFilename, string _name)
        {
            sourceCardFilename = _sourceCardFilename;
            name = _name;
        }

        public static IEnumerator DeserializeDollModel(CreateDollRequest request)
        {
            request.error = null;
            request.result = null;

            if (request.data == null)
            {
                request.error = "[DollModel] Deserialize request rawData is null.";
                yield break;
            }

            DollModel model = new DollModel(request.sourceCardFilename, request.dollName);
            if (request.dataIncludesModelSettings)
            {
                model.DeserializeFromCardData(request.data);
                if (!string.IsNullOrEmpty(model.error))
                {
                    request.error = model.error;
                    yield break;
                }
            }
            else
            {
                model.modelData = request.data;
            }

            DollByteStreamReader bsr = new DollByteStreamReader(model.modelData);
            float version = bsr.ReadFloat();
            if (version < MODEL_IMPORT_VERSION)
            {
                request.error = "Doll model version outdated. Use the latest exporter version.";
                yield break;
            }
            if (version > MODEL_IMPORT_VERSION)
            {
                request.error = "Doll model version is newer than supported.";
                yield break;
            }

            Dictionary<string, int> properties = new Dictionary<string, int>();
            int propertyCount = bsr.ReadUnsigned1ByteInt();
            for (int i = 0; i < propertyCount; i++)
            {
                string propertyName = bsr.ReadUTF8String(bsr.ReadUnsigned1ByteInt());
                properties[propertyName] = bsr.ReadUnsigned2ByteInt();
            }

            model.texture = request.modelImportSettings.modelTexture;
            model.rightEyeTexture = request.modelImportSettings.rightEyeTexture;
            model.leftEyeTexture = request.modelImportSettings.leftEyeTexture;

            model.boneNamesBytesLength = properties["boneNamesBytesLength"];
            model.boneCount = properties["boneCount"];
            model.childBoneIndicesTableLength = properties["childBoneIndicesTableLength"];
            model.materialNamesBytesLength = properties["materialNamesBytesLength"];
            model.vertexCount = properties["vertexCount"];
            model.materialCount = properties["materialCount"];
            model.triIndicesCount = properties["triIndicesCount"] * 3;
            model.shapeKeyCount = properties["shapeKeyCount"];
            model.shapeKeyNamesByteLength = properties["shapeKeyNamesByteLength"];
            model.shapeKeyVertexElements = properties["shapeKeyVertexElements"];

            NativeArray<byte> nativeTargetArray = Stegosaurus.ConvertToNativeArray(model.modelData, Allocator.TempJob);
            var decodeJob = new Stegosaurus.DecodeDollModelJob(
                bsr.index,
                nativeTargetArray,
                model.boneNamesBytesLength,
                model.boneCount,
                model.childBoneIndicesTableLength,
                model.materialNamesBytesLength,
                model.vertexCount,
                model.materialCount,
                model.triIndicesCount,
                model.shapeKeyCount,
                model.shapeKeyNamesByteLength,
                model.shapeKeyVertexElements
            );
            JobHandle jobHandle = decodeJob.Schedule();

            while (!jobHandle.IsCompleted)
            {
                yield return null;
            }
            jobHandle.Complete();
            nativeTargetArray.Dispose();

            var mbs = request.modelImportSettings;
            request.mbs = mbs;

            mbs.boneCount = model.boneCount;
            mbs.vertexCount = model.vertexCount;
            mbs.shapeKeyCount = model.shapeKeyCount;
            mbs.materialCount = model.materialCount;
            mbs.headpatWorldSphere = decodeJob.sphereInfos[0];
            mbs.hatLocalPosAndPitch = decodeJob.sphereInfos[1];
            mbs.headCollisionWorldSphere = decodeJob.sphereInfos[2];

            mbs.boneNameTable = NativeByteArrayToStringArray(decodeJob.boneNameStringTable, mbs.boneCount);
            mbs.boneHeadTable = NativeArrayToVector3Array(decodeJob.boneHeadTable);
            mbs.boneTailTable = NativeArrayToVector3Array(decodeJob.boneTailTable);
            mbs.boneRollTable = NativeArrayToFloatArray(decodeJob.boneRollTable);
            mbs.boneHierarchyChildCountTable = NativeArrayToIntArray(decodeJob.boneHierarchyChildCountTable);
            mbs.IsPartOfBaseSkeletonTable = NativeArrayToBoolArray(decodeJob.IsPartOfBaseSkeletonTable);
            mbs.childBoneIndicesTable = NativeArrayToIntArray(decodeJob.childBoneIndicesTable);
            mbs.materialNameTable = NativeByteArrayToStringArray(decodeJob.materialNamesTable, mbs.materialCount);
            mbs.vertexTable = NativeArrayToVector3Array(decodeJob.vertexTable);
            mbs.normalTable = NativeArrayToVector3Array(decodeJob.normalTable);
            mbs.uvTable = NativeArrayToVector2Array(decodeJob.uvTable);
            mbs.triVertexIndicesTable = NativeArrayToIntArray(decodeJob.triVertexIndicesTable);
            mbs.boneWeightsTable = NativeArrayToBoneWeightsArray(decodeJob.boneWeightsTable);
            mbs.submeshTriCountTable = NativeArrayToIntArray(decodeJob.submeshTriCountTable);
            mbs.shapeKeyNamesTable = NativeByteArrayToStringArray(decodeJob.shapeKeyNamesTable, mbs.shapeKeyCount);
            mbs.shapeKeyLengthsTable = NativeArrayToIntArray(decodeJob.shapeKeyLengthsTable);
            mbs.vertexDeltaIndicesTable = NativeArrayToIntArray(decodeJob.vertexDeltaIndicesTable);
            mbs.vertexDeltaOffsetTable = NativeArrayToVector3Array(decodeJob.vertexDeltaOffsetTable);

            decodeJob.sphereInfos.Dispose();
            decodeJob.boneNameStringTable.Dispose();
            decodeJob.boneHeadTable.Dispose();
            decodeJob.boneTailTable.Dispose();
            decodeJob.boneRollTable.Dispose();
            decodeJob.boneHierarchyChildCountTable.Dispose();
            decodeJob.IsPartOfBaseSkeletonTable.Dispose();
            decodeJob.childBoneIndicesTable.Dispose();
            decodeJob.materialNamesTable.Dispose();
            decodeJob.vertexTable.Dispose();
            decodeJob.normalTable.Dispose();
            decodeJob.uvTable.Dispose();
            decodeJob.triVertexIndicesTable.Dispose();
            decodeJob.boneWeightsTable.Dispose();
            decodeJob.submeshTriCountTable.Dispose();
            decodeJob.shapeKeyNamesTable.Dispose();
            decodeJob.shapeKeyLengthsTable.Dispose();
            decodeJob.vertexDeltaIndicesTable.Dispose();
            decodeJob.vertexDeltaOffsetTable.Dispose();

            model.Build(request.targetDoll, mbs);
            request.builtModel = model;
            request.result = request.targetDoll;
        }

        private void DeserializeFromCardData(byte[] cardData)
        {
            error = null;
            var bsr = new DollByteStreamReader(cardData);
            byte version = bsr.ReadUnsigned1ByteInt();
            if (version == 1)
            {
                error = "Outdated doll card version.";
                return;
            }
            byte cardType = bsr.ReadUnsigned1ByteInt();
            if (cardType != (byte)Stegosaurus.UnpackedCard.CardType.CUSTOM_DOLL_MODEL)
            {
                error = "Selected PNG is not a valid Doll card.";
                return;
            }

            pupilSpanRadius = bsr.Read1ByteNormalFloat();
            pupilOffset.x = bsr.Read1ByteNormalFloat();
            pupilOffset.y = bsr.Read1ByteNormalFloat();
            skinColor.r = bsr.ReadUnsigned1ByteNormalFloat();
            skinColor.g = bsr.ReadUnsigned1ByteNormalFloat();
            skinColor.b = bsr.ReadUnsigned1ByteNormalFloat();
            skinColor.a = 1f;
            voiceIndex = bsr.ReadUnsigned1ByteInt();
            fullBodyOverride = Convert.ToBoolean(bsr.ReadUnsigned1ByteInt());
            headpatWorldSphere = new Vector4(bsr.ReadFloat(), bsr.ReadFloat(), bsr.ReadFloat(), bsr.ReadFloat());
            hatLocalPosAndPitch = new Vector4(bsr.ReadFloat(), bsr.ReadFloat(), bsr.ReadFloat(), bsr.ReadFloat());
            headCollisionWorldSphere = new Vector4(bsr.ReadFloat(), bsr.ReadFloat(), bsr.ReadFloat(), bsr.ReadFloat());
            name = bsr.ReadUTF8String(bsr.ReadUnsigned1ByteInt());
            modelData = bsr.ReadBytes(bsr.ReadSigned4ByteInt());

            int dynamicBoneInfoCount = bsr.ReadUnsigned1ByteInt();
            dynamicBoneInfos = new List<DynamicBoneInfo>(dynamicBoneInfoCount);
            for (int i = 0; i < dynamicBoneInfoCount; i++)
            {
                var info = new DynamicBoneInfo(bsr.ReadUTF8String(bsr.ReadUnsigned1ByteInt()))
                {
                    damping = bsr.ReadUnsigned1ByteNormalFloat(),
                    elasticity = bsr.ReadUnsigned1ByteNormalFloat(),
                    stiffness = bsr.ReadUnsigned1ByteNormalFloat()
                };

                if (version > 2)
                {
                    byte packedBooleans = bsr.ReadUnsigned1ByteInt();
                    info.disableEndLength = (packedBooleans & (1 << 0)) != 0;
                    info.useGravity = (packedBooleans & (1 << 1)) != 0;
                    info.canRelax = (packedBooleans & (1 << 2)) != 0;
                    info.headCollision = (packedBooleans & (1 << 3)) != 0;
                    info.collisionBack = (packedBooleans & (1 << 4)) != 0;
                }
                else
                {
                    info.disableEndLength = Convert.ToBoolean(bsr.ReadUnsigned1ByteInt());
                    info.useGravity = Convert.ToBoolean(bsr.ReadUnsigned1ByteInt());
                    info.canRelax = Convert.ToBoolean(bsr.ReadUnsigned1ByteInt());
                    info.headCollision = Convert.ToBoolean(bsr.ReadUnsigned1ByteInt());
                    info.collisionBack = Convert.ToBoolean(bsr.ReadUnsigned1ByteInt());
                }
                dynamicBoneInfos.Add(info);
            }
        }

        public void Build(DollInstance targetDoll, DollModelBuildSettings mbs)
        {
            error = null;
            if (targetDoll == null || mbs == null)
            {
                Debug.LogError("[DollModel] Target Doll or build settings cannot be null!");
                return;
            }

            targetDoll.AutoResolveReferences();
            if (targetDoll.bodySMRs == null || targetDoll.bodySMRs.Length == 0 || targetDoll.headSMR == null)
            {
                Debug.LogError("[DollModel] Target Doll is missing bodySMRs or headSMR!");
                return;
            }

            // Hide default hair when applying a custom Doll card head (card mesh includes its own hair)
            if (targetDoll.defaultHairRoot != null)
            {
                targetDoll.defaultHairRoot.SetActive(false);
            }

            // Clean up any previous dynamically created bones/meshes
            targetDoll.DestroyAllCreatedBones();
            if (targetDoll.CurrentModel != null && targetDoll.CurrentModel != this)
            {
                targetDoll.CurrentModel.DestroyRuntimeAssets(targetDoll.headSMR);
            }

            targetDoll.CurrentModel = this;
            targetDoll.currentCardName = sourceCardFilename;
            if (!string.IsNullOrEmpty(name))
            {
                targetDoll.gameObject.name = $"Doll_{name}";
            }

            Transform bodyArmature = targetDoll.bodyArmature;
            Transform[] baseBones = targetDoll.bodySMRs[0].bones;
            SkinnedMeshRenderer destSMR = targetDoll.headSMR;

            // Temporarily reset root & bones to bind pose so bindposes match Blender export space
            Vector3 cachedArmaturePos = bodyArmature.position;
            Quaternion cachedArmatureRot = bodyArmature.rotation;
            bodyArmature.position = Vector3.zero;
            bodyArmature.rotation = Quaternion.identity;

            targetDoll.ResetToBindPose();

            GenerateBonesShared(targetDoll, baseBones, mbs);
            GenerateBoneHierarchy(targetDoll, mbs);

            Material[] materials = GenerateMaterials(mbs);
            destSMR.materials = materials;
            foreach (Material mat in destSMR.materials)
            {
                mat.name = mat.name.Replace(" (Instance)", "");
            }

            mesh = GenerateMesh(destSMR, mbs);
            GenerateShapeKeys(mesh, mbs);
            ApplyHeadModelTextures(destSMR);

            hatLocalPosAndPitch = mbs.hatLocalPosAndPitch;
            headpatWorldSphere = mbs.headpatWorldSphere;
            headCollisionWorldSphere = mbs.headCollisionWorldSphere;

            mesh.name = string.IsNullOrEmpty(name) ? "DollHead" : name;
            destSMR.bones = bonesShared;
            destSMR.sharedMesh = mesh;
            destSMR.updateWhenOffscreen = true;
            destSMR.localBounds = mesh.bounds;
            if (targetDoll.headBone != null)
            {
                destSMR.rootBone = targetDoll.headBone;
            }

            bodyArmature.position = cachedArmaturePos;
            bodyArmature.rotation = cachedArmatureRot;

            // Re-bind animator and refresh CompanionAI + SpringBone physics
            if (targetDoll.TryGetComponent<Animator>(out var animator))
            {
                animator.Rebind();
            }

            SetupDynamicSpringBones(targetDoll);
            targetDoll.ApplySkinColor(skinColor);
            Color neckMatchColor = SampleHeadNeckColor(mbs, skinColor);
            targetDoll.ApplyNeckColor(neckMatchColor, 1f);

            if (targetDoll.TryGetComponent<CharacterInteractable>(out var interactable))
            {
                interactable.characterName = string.IsNullOrEmpty(name) ? "Doll" : name;
            }

            if (targetDoll.TryGetComponent<CompanionAI>(out var ai))
            {
                ai.headSMR = destSMR;
                ai.RefreshFaceSetup();
                ai.InitializeLocomotionAnimation();
            }
        }

        private void SetupDynamicSpringBones(DollInstance targetDoll)
        {
            if (targetDoll.bodyArmature == null) return;

            SpringSphereCollider headCol = null;
            if (targetDoll.headBone != null)
            {
                Transform existingCol = targetDoll.headBone.Find("Col_Head");
                if (existingCol == null)
                {
                    existingCol = new GameObject("Col_Head").transform;
                    existingCol.SetParent(targetDoll.headBone, false);
                    existingCol.localPosition = new Vector3(0f, 0.08f, 0f);
                }
                headCol = DollUtilities.EnsureComponent<SpringSphereCollider>(existingCol.gameObject);
                headCol.radius = 0.125f;
            }

            var headColliders = headCol != null ? new[] { headCol } : Array.Empty<SpringSphereCollider>();

            foreach (var info in dynamicBoneInfos)
            {
                Transform chainRoot = DollUtilities.FindTransformRecursive(targetDoll.bodyArmature, info.rootBone);
                if (chainRoot == null) continue;

                foreach (Transform bone in chainRoot.GetComponentsInChildren<Transform>(true))
                {
                    if (bone.childCount == 0) continue;

                    SpringBone sb = DollUtilities.EnsureComponent<SpringBone>(bone.gameObject);
                    sb.stiffnessForce = Mathf.Lerp(120f, 360f, info.stiffness);
                    sb.dragForce = Mathf.Clamp(info.damping * 2.5f, 0.2f, 0.8f);
                    sb.springForce = info.useGravity ? new Vector3(0f, -0.0005f, 0f) : Vector3.zero;
                    sb.radius = 0.02f;
                    sb.sphereColliders = info.headCollision ? headColliders : Array.Empty<SpringSphereCollider>();
                    sb.capsuleColliders = Array.Empty<SpringCapsuleCollider>();
                    sb.panelColliders = Array.Empty<SpringPanelCollider>();
                }
            }

            SpringManager sm = DollUtilities.EnsureComponent<SpringManager>(targetDoll.gameObject);
            sm.automaticUpdates = true;
            sm.simulationFrameRate = 60;
            sm.dynamicRatio = 1.0f;
            sm.gravity = new Vector3(0f, -9.81f, 0f);
            sm.bounce = 0.05f;
            sm.friction = 0.8f;
            sm.collideWithGround = false;
            sm.enableCollision = true;
            sm.FindSpringBones(true);
            if (sm.springBones != null)
            {
                foreach (var bone in sm.springBones)
                {
                    if (bone != null)
                    {
                        bone.Initialize(sm);
                    }
                }
                sm.UpdateBoneIsAnimatedStates(Array.Empty<string>());
            }
        }

        private Color SampleHeadNeckColor(DollModelBuildSettings mbs, Color fallback)
        {
            if (mbs?.modelTexture == null || mbs.uvTable == null || mbs.boneWeightsTable == null || bonesShared == null)
            {
                return fallback;
            }

            int neckIdx = FindBoneIndexByName(bonesShared, "neck");
            if (neckIdx < 0)
            {
                return fallback;
            }

            // Find the vertex index span for the head "skin" submesh (where neck vertices live)
            int skinStart = 0;
            int skinEnd = mbs.triVertexIndicesTable != null ? mbs.triVertexIndicesTable.Length : 0;
            if (mbs.materialNameTable != null && mbs.submeshTriCountTable != null && mbs.triVertexIndicesTable != null)
            {
                int cursor = 0;
                for (int i = 0; i < mbs.materialNameTable.Length && i < mbs.submeshTriCountTable.Length; i++)
                {
                    int len = mbs.submeshTriCountTable[i];
                    if (mbs.materialNameTable[i].StartsWith("skin"))
                    {
                        skinStart = cursor;
                        skinEnd = cursor + len;
                        break;
                    }
                    cursor += len;
                }
            }

            var neckUvs = new List<Vector2>();
            if (mbs.triVertexIndicesTable != null && skinEnd > skinStart)
            {
                // Collect unique neck-weighted vertices from the skin submesh and take lowest vertical band (closest to neck seam)
                var seen = new HashSet<int>();
                float minY = float.MaxValue;
                var candidates = new List<int>();
                for (int i = skinStart; i < skinEnd && i < mbs.triVertexIndicesTable.Length; i++)
                {
                    int vi = mbs.triVertexIndicesTable[i];
                    if (!seen.Add(vi) || vi < 0 || vi >= mbs.boneWeightsTable.Length) continue;
                    BoneWeight bw = mbs.boneWeightsTable[vi];
                    bool hasNeckWeight =
                        (bw.boneIndex0 == neckIdx && bw.weight0 > 0.25f) ||
                        (bw.boneIndex1 == neckIdx && bw.weight1 > 0.25f);
                    if (hasNeckWeight && mbs.vertexTable != null && vi < mbs.vertexTable.Length)
                    {
                        float y = mbs.vertexTable[vi].z; // Blender export space before bone transform: Z is vertical
                        if (y < minY) minY = y;
                        candidates.Add(vi);
                    }
                }

                float bandThreshold = minY + 0.03f;
                foreach (int vi in candidates)
                {
                    if (mbs.vertexTable[vi].z <= bandThreshold && vi < mbs.uvTable.Length)
                    {
                        neckUvs.Add(mbs.uvTable[vi]);
                    }
                }
            }

            if (neckUvs.Count == 0)
            {
                return fallback;
            }

            RenderTexture rt = RenderTexture.GetTemporary(256, 256, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(mbs.modelTexture, rt);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D readable = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
            readable.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            Vector3 sum = Vector3.zero;
            int count = 0;
            foreach (Vector2 uv in neckUvs)
            {
                Color c = readable.GetPixelBilinear(uv.x, uv.y);
                sum += new Vector3(c.r, c.g, c.b);
                count++;
            }
            UnityEngine.Object.Destroy(readable);

            if (count == 0) return fallback;
            Vector3 avg = sum / count;
            return new Color(avg.x, avg.y, avg.z, 1f);
        }

        private static int FindBoneIndexByName(Transform[] bones, string boneName)
        {
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] != null && bones[i].name == boneName)
                {
                    return i;
                }
            }
            return -1;
        }

        private void GenerateBonesShared(DollInstance targetDoll, Transform[] baseBones, DollModelBuildSettings mbs)
        {
            bonesShared = new Transform[mbs.boneCount];
            for (int i = 0; i < bonesShared.Length; i++)
            {
                string boneName = mbs.boneNameTable[i];
                Vector3 head = mbs.boneHeadTable[i];
                Vector3 tail = mbs.boneTailTable[i];
                float roll = mbs.boneRollTable[i];

                int boneIndex = FindBoneIndexByName(baseBones, boneName);
                Transform bone;
                if (boneName.StartsWith("pupil_"))
                {
                    bone = boneName.EndsWith("r") ? targetDoll.pupil_r : targetDoll.pupil_l;
                    if (bone == null)
                    {
                        bone = new GameObject(boneName).transform;
                        targetDoll.createdBones.Add(bone.gameObject);
                    }
                    bone.position = head;
                    bone.eulerAngles = new Vector3(0.0f, 0.0f, 180.0f);
                }
                else if (boneIndex == -1)
                {
                    bone = new GameObject(boneName).transform;
                    targetDoll.createdBones.Add(bone.gameObject);
                    bone.position = head;
                    Vector3 dir = tail - head;
                    if (dir.sqrMagnitude > 0.000001f)
                    {
                        bone.rotation = Quaternion.AngleAxis(roll * Mathf.Rad2Deg, dir.normalized);
                    }
                }
                else
                {
                    bone = baseBones[boneIndex];
                }

                bonesShared[i] = bone;
            }
        }

        private void GenerateBoneHierarchy(DollInstance targetDoll, DollModelBuildSettings mbs)
        {
            List<Transform> potentialDynamicBoneRoots = new List<Transform>();
            int childBoneIndicesTableIndex = 0;

            for (int i = 0; i < mbs.boneCount; i++)
            {
                Transform bone = bonesShared[i];
                int childCount = mbs.boneHierarchyChildCountTable[i];
                bool boneIsFromBase = mbs.IsPartOfBaseSkeletonTable[i];

                for (int j = 0; j < childCount; j++)
                {
                    int childIndex = mbs.childBoneIndicesTable[childBoneIndicesTableIndex++];
                    Transform child = bonesShared[childIndex];
                    bool childIsFromBase = mbs.IsPartOfBaseSkeletonTable[childIndex];
                    if (childIsFromBase)
                    {
                        continue;
                    }
                    if (boneIsFromBase && !childIsFromBase)
                    {
                        potentialDynamicBoneRoots.Add(child);
                    }
                    child.SetParent(bone, true);
                }
            }

            if (dynamicBoneInfos.Count == 0)
            {
                foreach (Transform bone in potentialDynamicBoneRoots)
                {
                    if (bone.childCount > 0)
                    {
                        dynamicBoneInfos.Add(new DynamicBoneInfo(bone.name));
                    }
                }
            }
        }

        private static Material CreateModelMaterial(DollModelBuildSettings mbs, string materialName)
        {
            Material template = materialName.StartsWith("pupil_") && mbs.pupilMaterialTemplate != null
                ? mbs.pupilMaterialTemplate
                : mbs.headMaterialTemplate;

            Material newMaterial = template != null
                ? new Material(template)
                : new Material(Shader.Find("Anime/DollHead") ?? Shader.Find("Anime/AnimeHead") ?? Shader.Find("Universal Render Pipeline/Lit"));

            newMaterial.name = materialName;
            return newMaterial;
        }

        private Material[] GenerateMaterials(DollModelBuildSettings mbs)
        {
            Material[] materialTable = new Material[mbs.materialCount];
            for (int i = 0; i < materialTable.Length; i++)
            {
                materialTable[i] = CreateModelMaterial(mbs, mbs.materialNameTable[i]);
            }

            if (materialTable.Length == 0)
            {
                materialTable = new Material[] { CreateModelMaterial(mbs, "skin") };
            }
            return materialTable;
        }

        private Mesh GenerateMesh(SkinnedMeshRenderer targetSMR, DollModelBuildSettings mbs)
        {
            Mesh builtMesh = new Mesh
            {
                vertices = mbs.vertexTable,
                normals = mbs.normalTable,
                uv = mbs.uvTable,
                boneWeights = mbs.boneWeightsTable,
                subMeshCount = targetSMR.materials.Length
            };

            int submeshTriCountTableIndex = 0;
            for (int i = 0; i < targetSMR.materials.Length; i++)
            {
                int[] submeshIndices = new int[mbs.submeshTriCountTable[i]];
                Array.Copy(mbs.triVertexIndicesTable, submeshTriCountTableIndex, submeshIndices, 0, submeshIndices.Length);
                builtMesh.SetIndices(submeshIndices, MeshTopology.Triangles, i, false);
                submeshTriCountTableIndex += submeshIndices.Length;
            }
            builtMesh.bindposes = CreateBindPoses(bonesShared);
            builtMesh.RecalculateBounds();
            return builtMesh;
        }

        private Matrix4x4[] CreateBindPoses(Transform[] boneTable)
        {
            Matrix4x4[] poseTable = new Matrix4x4[boneTable.Length];
            for (int i = 0; i < boneTable.Length; i++)
            {
                poseTable[i] = boneTable[i].worldToLocalMatrix;
            }
            return poseTable;
        }

        private void GenerateShapeKeys(Mesh targetMesh, DollModelBuildSettings mbs)
        {
            int vertexDeltaIndicesIndex = 0;
            for (int i = 0; i < mbs.shapeKeyCount; i++)
            {
                string shapeKeyName = mbs.shapeKeyNamesTable[i];
                Vector3[] vertexDeltas = new Vector3[mbs.vertexCount];
                int deltas = mbs.shapeKeyLengthsTable[i];
                for (int j = 0; j < deltas; j++)
                {
                    int index = mbs.vertexDeltaIndicesTable[vertexDeltaIndicesIndex];
                    vertexDeltas[index] = mbs.vertexDeltaOffsetTable[vertexDeltaIndicesIndex];
                    vertexDeltaIndicesIndex++;
                }
                targetMesh.AddBlendShapeFrame(shapeKeyName, 100.0f, vertexDeltas, null, null);
            }

            if (mbs.shapeKeyNamesTable == null || !mbs.shapeKeyNamesTable.Contains("pupilShrink"))
            {
                targetMesh.AddBlendShapeFrame("pupilShrink", 100.0f, new Vector3[mbs.vertexCount], null, null);
            }
        }

        public void ApplyHeadModelTextures(SkinnedMeshRenderer destSMR)
        {
            foreach (Material material in destSMR.materials)
            {
                if (material == null) continue;
                if (material.name.StartsWith("skin"))
                {
                    material.mainTexture = texture;
                }
                else if (material.name.StartsWith("pupil_r"))
                {
                    material.mainTexture = rightEyeTexture;
                    if (material.HasProperty("_SideMultiplier")) material.SetFloat("_SideMultiplier", -1f);
                }
                else if (material.name.StartsWith("pupil_l"))
                {
                    material.mainTexture = leftEyeTexture;
                    if (material.HasProperty("_SideMultiplier")) material.SetFloat("_SideMultiplier", 1f);
                }
            }
        }

        public void DestroyRuntimeAssets(SkinnedMeshRenderer targetSMR)
        {
            if (targetSMR != null)
            {
                foreach (Material mat in targetSMR.materials)
                {
                    if (mat != null) UnityEngine.Object.Destroy(mat);
                }
            }
            if (mesh != null)
            {
                UnityEngine.Object.Destroy(mesh);
                mesh = null;
            }
            if (texture != null)
            {
                UnityEngine.Object.Destroy(texture);
                texture = null;
            }
            if (rightEyeTexture != null)
            {
                UnityEngine.Object.Destroy(rightEyeTexture);
                rightEyeTexture = null;
            }
            if (leftEyeTexture != null)
            {
                UnityEngine.Object.Destroy(leftEyeTexture);
                leftEyeTexture = null;
            }
        }

        private static bool[] NativeArrayToBoolArray(NativeArray<bool> array)
        {
            var result = new bool[array.Length];
            array.CopyTo(result);
            return result;
        }
        private static float[] NativeArrayToFloatArray(NativeArray<float> array)
        {
            var result = new float[array.Length];
            array.CopyTo(result);
            return result;
        }
        private static int[] NativeArrayToIntArray(NativeArray<int> array)
        {
            var result = new int[array.Length];
            array.CopyTo(result);
            return result;
        }
        private static Vector2[] NativeArrayToVector2Array(NativeArray<Vector2> array)
        {
            var result = new Vector2[array.Length];
            array.CopyTo(result);
            return result;
        }
        private static Vector3[] NativeArrayToVector3Array(NativeArray<Vector3> array)
        {
            var result = new Vector3[array.Length];
            array.CopyTo(result);
            return result;
        }
        private static BoneWeight[] NativeArrayToBoneWeightsArray(NativeArray<BoneWeight> array)
        {
            var result = new BoneWeight[array.Length];
            array.CopyTo(result);
            return result;
        }

        private static string[] NativeByteArrayToStringArray(NativeArray<byte> bytes, int stringCount)
        {
            var rawBytes = new byte[bytes.Length];
            for (int i = 0; i < bytes.Length; i++)
            {
                rawBytes[i] = bytes[i];
            }
            var bsr = new DollByteStreamReader(rawBytes);
            var strings = new string[stringCount];
            for (int i = 0; i < stringCount; i++)
            {
                strings[i] = bsr.ReadUTF8String(bsr.ReadUnsigned1ByteInt());
            }
            return strings;
        }
    }
}
