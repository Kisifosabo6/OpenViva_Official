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

            Color calibratedSkin = skinColor;
            Material[] materials;
            if (TryBuildUnifiedSeamlessDoll(targetDoll, mbs, out Mesh unifiedMesh, out materials, ref calibratedSkin))
            {
                mesh = unifiedMesh;
                destSMR.materials = materials;
                foreach (Material mat in destSMR.materials)
                {
                    mat.name = mat.name.Replace(" (Instance)", "");
                }
                ApplyHeadModelTextures(destSMR);
                targetDoll.bodySMRs[0].enabled = false;
            }
            else
            {
                materials = GenerateMaterials(mbs);
                destSMR.materials = materials;
                foreach (Material mat in destSMR.materials)
                {
                    mat.name = mat.name.Replace(" (Instance)", "");
                }
                mesh = GenerateMesh(destSMR, mbs);
                GenerateShapeKeys(mesh, mbs);
                ApplyHeadModelTextures(destSMR);
            }

            hatLocalPosAndPitch = mbs.hatLocalPosAndPitch;
            headpatWorldSphere = mbs.headpatWorldSphere;
            headCollisionWorldSphere = mbs.headCollisionWorldSphere;

            mesh.name = string.IsNullOrEmpty(name) ? "DollSeamless" : name;
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

            // Re-bind animator and refresh CharacterAI + SpringBone physics
            if (targetDoll.TryGetComponent<Animator>(out var animator))
            {
                animator.Rebind();
            }

            SetupDynamicSpringBones(targetDoll);
            targetDoll.ApplySkinColor(calibratedSkin);
            targetDoll.ApplyNeckColor(calibratedSkin, 1f);

            if (targetDoll.TryGetComponent<CharacterInteractable>(out var interactable))
            {
                interactable.characterName = string.IsNullOrEmpty(name) ? "Doll" : name;
            }

            if (targetDoll.TryGetComponent<CharacterAI>(out var ai))
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

        /// <summary>
        /// Builds a single unified, welded, seamless Mesh combining the base body and card head:
        /// - Cuts the bottom neck ring (replaceRows = 1) from the head to remove pinched rim geometry
        /// - Orders & aligns the collarbone loop and head cut loop (zero twist/fold)
        /// - Builds a smooth interpolated quad bridge (bridgeCuts = 2) with unified normals, weights, and shape keys
        /// - Computes NeckBlend vertex color gradient + UV_Body (uv2) for seamless torso-to-head shader transition
        /// - Calibrates Linear-space _SkinColor from the clean head cut-loop UVs and torso texture R channel
        /// </summary>
        private bool TryBuildUnifiedSeamlessDoll(
            DollInstance targetDoll,
            DollModelBuildSettings mbs,
            out Mesh unifiedMesh,
            out Material[] unifiedMaterials,
            ref Color calibratedSkinColor)
        {
            unifiedMesh = null;
            unifiedMaterials = null;

            if (targetDoll?.bodySMRs == null || targetDoll.bodySMRs.Length == 0 || targetDoll.bodySMRs[0] == null)
                return false;

            SkinnedMeshRenderer bodySMR = targetDoll.bodySMRs[0];
            Mesh bodyMesh = targetDoll.GetBaseBodyMesh();
            if (bodyMesh == null || mbs.vertexTable == null || mbs.triVertexIndicesTable == null)
                return false;

            // 1. Find & order the body's collarbone boundary loop
            List<int> bodyLoop = FindOrderedBoundaryLoop(bodyMesh.vertices, GetAllTriangles(bodyMesh), Vector3.zero, false);
            if (bodyLoop.Count < 8) return false;

            Vector3 bodyCenter = Vector3.zero;
            Vector3[] bodyVerts = bodyMesh.vertices;
            foreach (int bi in bodyLoop) bodyCenter += bodyVerts[bi];
            bodyCenter /= bodyLoop.Count;

            // Identify body submeshes (limbs vs torso)
            Material[] bodyMats = bodySMR.sharedMaterials;
            int limbsSubmesh = -1;
            int torsoSubmesh = -1;
            for (int s = 0; s < bodyMesh.subMeshCount; s++)
            {
                string mName = (bodyMats != null && s < bodyMats.Length && bodyMats[s] != null)
                    ? bodyMats[s].name.ToLowerInvariant()
                    : string.Empty;
                if (mName.Contains("torso"))
                    torsoSubmesh = s;
                else if (limbsSubmesh < 0)
                    limbsSubmesh = s;
            }
            if (torsoSubmesh < 0) torsoSubmesh = bodyMesh.subMeshCount > 1 ? 1 : 0;

            // Identify head "skin" submesh
            int headSkinSubmesh = 0;
            if (mbs.materialNameTable != null)
            {
                for (int i = 0; i < mbs.materialNameTable.Length; i++)
                {
                    if (mbs.materialNameTable[i] != null && mbs.materialNameTable[i].ToLowerInvariant().Contains("skin"))
                    {
                        headSkinSubmesh = i;
                        break;
                    }
                }
            }

            // 2. Find the head's bottom neck rim loop near bodyCenter, and remove 1 bottom band of triangles (replace_rows = 1)
            int[] origHeadSkinTris = GetHeadSubmeshTriangles(mbs, headSkinSubmesh);
            List<int> origHeadRim = FindOrderedBoundaryLoop(mbs.vertexTable, origHeadSkinTris, bodyCenter, true);
            if (origHeadRim.Count < 8) return false;

            var origRimSet = new HashSet<int>(origHeadRim);
            // Expand rimSet to include any coincident UV-split vertices at the rim
            ExpandCoincidentVertices(mbs.vertexTable, origRimSet);

            // Filter out triangles touching the bottom rim (replace_rows = 1)
            var keptHeadSkinTris = new List<int>(origHeadSkinTris.Length);
            for (int t = 0; t + 2 < origHeadSkinTris.Length; t += 3)
            {
                int a = origHeadSkinTris[t];
                int b = origHeadSkinTris[t + 1];
                int c = origHeadSkinTris[t + 2];
                if (origRimSet.Contains(a) || origRimSet.Contains(b) || origRimSet.Contains(c))
                    continue;
                keptHeadSkinTris.Add(a);
                keptHeadSkinTris.Add(b);
                keptHeadSkinTris.Add(c);
            }
            if (keptHeadSkinTris.Count == 0) return false;

            // Find the newly exposed cut loop on the head neck
            List<int> headCutLoop = FindOrderedBoundaryLoop(mbs.vertexTable, keptHeadSkinTris.ToArray(), bodyCenter, true);
            if (headCutLoop.Count < 8) return false;

            // Align headCutLoop to bodyLoop (reverse + cyclic offset) to eliminate twist/pinch
            AlignOrderedLoop(bodyVerts, bodyLoop, mbs.vertexTable, headCutLoop);

            // 3. Ensure all body bones exist in bonesShared
            Vector3[] bodyNorms = bodyMesh.normals;
            Vector2[] bodyUvs = bodyMesh.uv;
            BoneWeight[] bodyWeights = bodyMesh.boneWeights;
            if (bodyNorms == null || bodyNorms.Length != bodyVerts.Length) bodyNorms = new Vector3[bodyVerts.Length];

            var sharedBoneList = new List<Transform>(bonesShared);
            Transform[] bodyBones = bodySMR.bones;
            int[] bodyToSharedBone = new int[bodyBones != null ? bodyBones.Length : 0];
            for (int i = 0; i < bodyToSharedBone.Length; i++)
            {
                Transform b = bodyBones[i];
                if (b == null) { bodyToSharedBone[i] = 0; continue; }
                int idx = FindBoneIndexByName(sharedBoneList.ToArray(), b.name);
                if (idx < 0)
                {
                    idx = sharedBoneList.Count;
                    sharedBoneList.Add(b);
                }
                bodyToSharedBone[i] = idx;
            }
            bonesShared = sharedBoneList.ToArray();

            // 4. Linear-space _SkinColor calibration from clean headCutLoop UVs + torso R channel
            Texture torsoTex = (bodyMats != null && torsoSubmesh < bodyMats.Length && bodyMats[torsoSubmesh] != null)
                ? bodyMats[torsoSubmesh].mainTexture
                : null;
            Texture2D readableHead = mbs.modelTexture != null ? BlitToReadableTexture(mbs.modelTexture) : null;
            Texture2D readableTorso = torsoTex != null ? BlitToReadableTexture(torsoTex) : null;
            try
            {
                if (readableHead != null && mbs.uvTable != null)
                {
                    int nSamples = Mathf.Min(bodyLoop.Count, headCutLoop.Count);
                    var neckR = new List<float>(nSamples);
                    var neckG = new List<float>(nSamples);
                    var neckB = new List<float>(nSamples);
                    var torsoR = new List<float>(nSamples);

                    for (int i = 0; i < headCutLoop.Count; i++)
                    {
                        int hi = headCutLoop[i];
                        int bi = bodyLoop[(i * bodyLoop.Count) / headCutLoop.Count];
                        if (hi < mbs.uvTable.Length)
                        {
                            Color hc = readableHead.GetPixelBilinear(mbs.uvTable[hi].x, mbs.uvTable[hi].y);
                            neckR.Add(Mathf.GammaToLinearSpace(hc.r));
                            neckG.Add(Mathf.GammaToLinearSpace(hc.g));
                            neckB.Add(Mathf.GammaToLinearSpace(hc.b));
                        }
                        if (readableTorso != null && bodyUvs != null && bi < bodyUvs.Length)
                        {
                            Color tc = readableTorso.GetPixelBilinear(bodyUvs[bi].x, bodyUvs[bi].y);
                            torsoR.Add(Mathf.GammaToLinearSpace(tc.r));
                        }
                    }

                    if (neckR.Count > 0)
                    {
                        float medR = Median(neckR);
                        float medG = Median(neckG);
                        float medB = Median(neckB);
                        float medTorsoR = torsoR.Count > 0 ? Mathf.Max(0.05f, Median(torsoR)) : 0.92f;
                        calibratedSkinColor = new Color(
                            Mathf.LinearToGammaSpace(Mathf.Clamp01(medR)),
                            Mathf.LinearToGammaSpace(Mathf.Clamp01(medG / medTorsoR)),
                            Mathf.LinearToGammaSpace(Mathf.Clamp01(medB / medTorsoR)),
                            1f);
                        Debug.Log($"[DollModel] Calibrated _SkinColor={calibratedSkinColor} (card skinColor={skinColor}, medTorsoR={medTorsoR:F3})");
                    }
                }
            }
            finally
            {
                if (readableHead != null) UnityEngine.Object.Destroy(readableHead);
                if (readableTorso != null) UnityEngine.Object.Destroy(readableTorso);
            }

            // 5. Build unified vertex buffers: [0..bodyCount-1] = body, followed by head verts
            int bodyCount = bodyVerts.Length;
            var unifiedVerts = new List<Vector3>(bodyCount + mbs.vertexCount + 128);
            var unifiedNorms = new List<Vector3>(bodyCount + mbs.vertexCount + 128);
            var unifiedUv0 = new List<Vector2>(bodyCount + mbs.vertexCount + 128);
            var unifiedUv1 = new List<Vector2>(bodyCount + mbs.vertexCount + 128);
            var unifiedWeights = new List<BoneWeight>(bodyCount + mbs.vertexCount + 128);
            var unifiedColors = new List<Color>(bodyCount + mbs.vertexCount + 128);

            for (int i = 0; i < bodyCount; i++)
            {
                unifiedVerts.Add(bodyVerts[i]);
                unifiedNorms.Add(bodyNorms[i]);
                Vector2 buv = (bodyUvs != null && i < bodyUvs.Length) ? bodyUvs[i] : Vector2.zero;
                unifiedUv0.Add(buv);
                unifiedUv1.Add(buv);
                unifiedWeights.Add(bodyWeights != null && i < bodyWeights.Length ? RemapBoneWeight(bodyWeights[i], bodyToSharedBone) : default);
                unifiedColors.Add(new Color(0f, 0f, 0f, 1f)); // 0 = 100% body shader
            }

            var headToUnified = new int[mbs.vertexCount];
            for (int hi = 0; hi < mbs.vertexCount; hi++)
            {
                headToUnified[hi] = unifiedVerts.Count;
                unifiedVerts.Add(mbs.vertexTable[hi]);
                unifiedNorms.Add(mbs.normalTable != null && hi < mbs.normalTable.Length ? mbs.normalTable[hi] : Vector3.up);
                unifiedUv0.Add(mbs.uvTable != null && hi < mbs.uvTable.Length ? mbs.uvTable[hi] : Vector2.zero);
                unifiedUv1.Add(new Vector2(0.5f, 0.5f));
                unifiedWeights.Add(mbs.boneWeightsTable != null && hi < mbs.boneWeightsTable.Length ? mbs.boneWeightsTable[hi] : default);
                unifiedColors.Add(new Color(1f, 1f, 1f, 1f)); // 1 = 100% head texture
            }

            // 6. Build smooth linear quad bridge (bridgeCuts = 2) between bodyLoop (t=0) and headCutLoop (t=1)
            const int bridgeCuts = 2;
            int nBridge = bodyLoop.Count;
            var rings = new List<int[]>(bridgeCuts + 2);
            rings.Add(bodyLoop.ToArray());

            // Pre-map each bodyLoop[i] to its aligned headCutLoop index and clean head-rim UV0
            var alignedHeadUnified = new int[nBridge];
            var bodyUvAtIdx = new Vector2[nBridge];
            var headBotUvAtIdx = new Vector2[nBridge];
            for (int i = 0; i < nBridge; i++)
            {
                int hIdx = (i * headCutLoop.Count) / nBridge;
                int hi = headCutLoop[hIdx];
                alignedHeadUnified[i] = headToUnified[hi];
                int bi = bodyLoop[i];
                bodyUvAtIdx[i] = (bodyUvs != null && bi < bodyUvs.Length) ? bodyUvs[bi] : new Vector2(0.5f, 0.5f);
                unifiedUv1[alignedHeadUnified[i]] = bodyUvAtIdx[i];

                // Match nearest origHeadRim vertex UV (same island check as viva_seamless_poc.py)
                Vector2 topUv = unifiedUv0[alignedHeadUnified[i]];
                Vector2 botUv = topUv;
                float bestRimDistSq = float.MaxValue;
                Vector3 bPos = unifiedVerts[bi];
                foreach (int rhi in origHeadRim)
                {
                    float dSq = (mbs.vertexTable[rhi] - bPos).sqrMagnitude;
                    if (dSq < bestRimDistSq && mbs.uvTable != null && rhi < mbs.uvTable.Length)
                    {
                        bestRimDistSq = dSq;
                        botUv = mbs.uvTable[rhi];
                    }
                }
                if ((botUv - topUv).sqrMagnitude > 0.04f) botUv = topUv;
                headBotUvAtIdx[i] = botUv;
                unifiedUv0[bi] = botUv;
            }

            for (int k = 1; k <= bridgeCuts; k++)
            {
                float t = k / (float)(bridgeCuts + 1);
                float smoothT = t * t * (3f - 2f * t);
                var ring = new int[nBridge];
                for (int i = 0; i < nBridge; i++)
                {
                    int bi = bodyLoop[i];
                    int hui = alignedHeadUnified[i];
                    ring[i] = unifiedVerts.Count;
                    unifiedVerts.Add(Vector3.Lerp(unifiedVerts[bi], unifiedVerts[hui], t));
                    unifiedNorms.Add(Vector3.Slerp(unifiedNorms[bi], unifiedNorms[hui], t).normalized);
                    unifiedUv0.Add(Vector2.Lerp(headBotUvAtIdx[i], unifiedUv0[hui], t));
                    unifiedUv1.Add(bodyUvAtIdx[i]);
                    unifiedWeights.Add(LerpBoneWeight(unifiedWeights[bi], unifiedWeights[hui], t));
                    unifiedColors.Add(new Color(smoothT, smoothT, smoothT, 1f));
                }
                rings.Add(ring);
            }
            rings.Add(alignedHeadUnified);

            Vector3 bridgeAxisCenter = Vector3.zero;
            foreach (int bi in bodyLoop) bridgeAxisCenter += unifiedVerts[bi];
            foreach (int hui in alignedHeadUnified) bridgeAxisCenter += unifiedVerts[hui];
            bridgeAxisCenter /= Mathf.Max(1, bodyLoop.Count + alignedHeadUnified.Length);

            var bridgeTris = new List<int>(nBridge * (bridgeCuts + 1) * 6);
            for (int k = 0; k <= bridgeCuts; k++)
            {
                int[] r0 = rings[k];
                int[] r1 = rings[k + 1];
                for (int i = 0; i < nBridge; i++)
                {
                    int nxt = (i + 1) % nBridge;
                    int v00 = r0[i];
                    int v01 = r0[nxt];
                    int v11 = r1[nxt];
                    int v10 = r1[i];

                    Vector3 quadCenter = 0.25f * (unifiedVerts[v00] + unifiedVerts[v01] + unifiedVerts[v11] + unifiedVerts[v10]);
                    Vector3 triCross = Vector3.Cross(unifiedVerts[v01] - unifiedVerts[v00], unifiedVerts[v11] - unifiedVerts[v00]);
                    Vector3 outward = quadCenter - new Vector3(bridgeAxisCenter.x, bridgeAxisCenter.y, quadCenter.z);
                    if (Vector3.Dot(triCross, outward) >= 0f)
                    {
                        bridgeTris.Add(v00); bridgeTris.Add(v01); bridgeTris.Add(v11);
                        bridgeTris.Add(v00); bridgeTris.Add(v11); bridgeTris.Add(v10);
                    }
                    else
                    {
                        bridgeTris.Add(v00); bridgeTris.Add(v11); bridgeTris.Add(v01);
                        bridgeTris.Add(v00); bridgeTris.Add(v10); bridgeTris.Add(v11);
                    }
                }
            }

            // 7. Assemble submeshes: [0] limbs, [1] unified torso+bridge+headSkin, [2..] other head materials
            var submeshTris = new List<int[]>();
            var matList = new List<Material>();

            if (limbsSubmesh >= 0)
            {
                submeshTris.Add(bodyMesh.GetTriangles(limbsSubmesh));
                Material limbsTemplate = bodyMats != null && limbsSubmesh < bodyMats.Length ? bodyMats[limbsSubmesh] : null;
                Material limbsMat = limbsTemplate != null ? new Material(limbsTemplate) : CreateModelMaterial(mbs, "limbs");
                limbsMat.name = "limbs";
                matList.Add(limbsMat);
            }

            int[] torsoTris = bodyMesh.GetTriangles(torsoSubmesh);
            var unifiedSkinTris = new List<int>(torsoTris.Length + bridgeTris.Count + keptHeadSkinTris.Count);
            unifiedSkinTris.AddRange(torsoTris);
            unifiedSkinTris.AddRange(bridgeTris);
            for (int i = 0; i < keptHeadSkinTris.Count; i++)
            {
                unifiedSkinTris.Add(headToUnified[keptHeadSkinTris[i]]);
            }
            // Smooth vertex normals across bodyLoop, bridge rings, and headCutLoop from actual triangles
            var seamNormalVerts = new HashSet<int>();
            foreach (var r in rings)
            {
                foreach (int vi in r) seamNormalVerts.Add(vi);
            }
            SmoothNormalsForVertexSet(unifiedVerts, unifiedSkinTris, seamNormalVerts, unifiedNorms);

            submeshTris.Add(unifiedSkinTris.ToArray());

            Material unifiedSkinMat = CreateModelMaterial(mbs, "skin");
            if (torsoTex != null && unifiedSkinMat.HasProperty("_BodyTex"))
                unifiedSkinMat.SetTexture("_BodyTex", torsoTex);
            if (unifiedSkinMat.HasProperty("_UseNeckBlend"))
                unifiedSkinMat.SetFloat("_UseNeckBlend", 1f);
            if (unifiedSkinMat.HasProperty("_SkinColor"))
                unifiedSkinMat.SetColor("_SkinColor", calibratedSkinColor);
            matList.Add(unifiedSkinMat);

            int matCount = Mathf.Max(1, mbs.materialCount);
            int cursor = 0;
            for (int s = 0; s < matCount; s++)
            {
                int len = (mbs.submeshTriCountTable != null && s < mbs.submeshTriCountTable.Length) ? mbs.submeshTriCountTable[s] : 0;
                if (s != headSkinSubmesh && len > 0)
                {
                    var tris = new int[len];
                    for (int t = 0; t < len; t++)
                    {
                        tris[t] = headToUnified[mbs.triVertexIndicesTable[cursor + t]];
                    }
                    submeshTris.Add(tris);
                    string mName = (mbs.materialNameTable != null && s < mbs.materialNameTable.Length) ? mbs.materialNameTable[s] : $"mat_{s}";
                    matList.Add(CreateModelMaterial(mbs, mName));
                }
                cursor += len;
            }

            unifiedMesh = new Mesh
            {
                indexFormat = unifiedVerts.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16,
                vertices = unifiedVerts.ToArray(),
                normals = unifiedNorms.ToArray(),
                uv = unifiedUv0.ToArray(),
                uv2 = unifiedUv1.ToArray(),
                colors = unifiedColors.ToArray(),
                boneWeights = unifiedWeights.ToArray(),
                subMeshCount = submeshTris.Count
            };
            for (int s = 0; s < submeshTris.Count; s++)
            {
                unifiedMesh.SetTriangles(submeshTris[s], s, false);
            }
            unifiedMesh.bindposes = CreateBindPoses(bonesShared);
            unifiedMesh.RecalculateBounds();

            GenerateShapeKeysUnified(unifiedMesh, mbs, headToUnified, unifiedVerts.Count);

            unifiedMaterials = matList.ToArray();
            return true;
        }

        private static void SmoothNormalsForVertexSet(
            List<Vector3> verts,
            List<int> tris,
            HashSet<int> targetVerts,
            List<Vector3> normals)
        {
            var accum = new Dictionary<int, Vector3>(targetVerts.Count);
            foreach (int vi in targetVerts) accum[vi] = Vector3.zero;

            for (int t = 0; t + 2 < tris.Count; t += 3)
            {
                int i0 = tris[t];
                int i1 = tris[t + 1];
                int i2 = tris[t + 2];
                bool h0 = targetVerts.Contains(i0);
                bool h1 = targetVerts.Contains(i1);
                bool h2 = targetVerts.Contains(i2);
                if (!h0 && !h1 && !h2) continue;

                Vector3 faceCross = Vector3.Cross(verts[i1] - verts[i0], verts[i2] - verts[i0]);
                if (h0) accum[i0] += faceCross;
                if (h1) accum[i1] += faceCross;
                if (h2) accum[i2] += faceCross;
            }

            foreach (var kv in accum)
            {
                if (kv.Value.sqrMagnitude > 1e-10f)
                {
                    normals[kv.Key] = kv.Value.normalized;
                }
            }
        }

        private static int[] GetAllTriangles(Mesh mesh)
        {
            var all = new List<int>();
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                all.AddRange(mesh.GetTriangles(s));
            }
            return all.ToArray();
        }

        private static void ExpandCoincidentVertices(Vector3[] verts, HashSet<int> set)
        {
            var keys = new HashSet<(int, int, int)>();
            foreach (int i in set)
            {
                Vector3 v = verts[i];
                keys.Add((Mathf.RoundToInt(v.x * 10000f), Mathf.RoundToInt(v.y * 10000f), Mathf.RoundToInt(v.z * 10000f)));
            }
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 v = verts[i];
                if (keys.Contains((Mathf.RoundToInt(v.x * 10000f), Mathf.RoundToInt(v.y * 10000f), Mathf.RoundToInt(v.z * 10000f))))
                {
                    set.Add(i);
                }
            }
        }

        private static List<int> FindOrderedBoundaryLoop(Vector3[] verts, int[] tris, Vector3 targetCenter, bool pickClosestToTarget)
        {
            if (verts == null || tris == null || verts.Length == 0 || tris.Length == 0)
                return new List<int>();

            var posToCanonical = new Dictionary<(int, int, int), int>(verts.Length);
            var canonicalOf = new int[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 v = verts[i];
                var key = (Mathf.RoundToInt(v.x * 10000f), Mathf.RoundToInt(v.y * 10000f), Mathf.RoundToInt(v.z * 10000f));
                if (!posToCanonical.TryGetValue(key, out int canon))
                {
                    canon = i;
                    posToCanonical[key] = canon;
                }
                canonicalOf[i] = canon;
            }

            var edgeCounts = new Dictionary<(int, int), int>();
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                int a = canonicalOf[tris[t]];
                int b = canonicalOf[tris[t + 1]];
                int c = canonicalOf[tris[t + 2]];
                CountEdge(edgeCounts, a, b);
                CountEdge(edgeCounts, b, c);
                CountEdge(edgeCounts, c, a);
            }

            var adj = new Dictionary<int, List<int>>();
            foreach (var kv in edgeCounts)
            {
                if (kv.Value == 1)
                {
                    AddAdj(adj, kv.Key.Item1, kv.Key.Item2);
                }
            }

            var visited = new HashSet<int>();
            var loops = new List<List<int>>();
            foreach (int start in adj.Keys)
            {
                if (visited.Contains(start)) continue;
                var loop = new List<int> { start };
                visited.Add(start);
                int curr = start;
                int prev = -1;
                while (true)
                {
                    int next = -1;
                    foreach (int nb in adj[curr])
                    {
                        if (nb != prev && !visited.Contains(nb))
                        {
                            next = nb;
                            break;
                        }
                    }
                    if (next < 0) break;
                    loop.Add(next);
                    visited.Add(next);
                    prev = curr;
                    curr = next;
                }
                if (loop.Count >= 8) loops.Add(loop);
            }

            if (loops.Count == 0) return new List<int>();
            if (!pickClosestToTarget)
            {
                loops.Sort((a, b) => b.Count.CompareTo(a.Count));
                return loops[0];
            }

            List<int> best = loops[0];
            float bestDistSq = float.MaxValue;
            foreach (var loop in loops)
            {
                Vector3 c = Vector3.zero;
                foreach (int vi in loop) c += verts[vi];
                c /= loop.Count;
                float dSq = (c - targetCenter).sqrMagnitude;
                if (dSq < bestDistSq)
                {
                    bestDistSq = dSq;
                    best = loop;
                }
            }
            return best;
        }

        private static void AlignOrderedLoop(Vector3[] bodyVerts, List<int> bodyLoop, Vector3[] headVerts, List<int> headLoop)
        {
            int na = bodyLoop.Count;
            int nb = headLoop.Count;
            if (na == 0 || nb == 0) return;

            bool bestRev = false;
            int bestOffset = 0;
            float bestDistSq = float.MaxValue;

            for (int r = 0; r < 2; r++)
            {
                bool rev = r == 1;
                for (int offset = 0; offset < nb; offset++)
                {
                    float sumSq = 0f;
                    for (int i = 0; i < na; i++)
                    {
                        int hiIdx = (i * nb) / na;
                        int actualIdx = rev ? (nb - 1 - ((hiIdx + offset) % nb)) : ((hiIdx + offset) % nb);
                        sumSq += (bodyVerts[bodyLoop[i]] - headVerts[headLoop[actualIdx]]).sqrMagnitude;
                    }
                    if (sumSq < bestDistSq)
                    {
                        bestDistSq = sumSq;
                        bestRev = rev;
                        bestOffset = offset;
                    }
                }
            }

            if (bestRev) headLoop.Reverse();
            if (bestOffset > 0 && !bestRev)
            {
                var shifted = new List<int>(nb);
                for (int i = 0; i < nb; i++) shifted.Add(headLoop[(i + bestOffset) % nb]);
                headLoop.Clear();
                headLoop.AddRange(shifted);
            }
            else if (bestRev)
            {
                // Re-align offset after reverse
                int bestShift = 0;
                float bestShiftSq = float.MaxValue;
                for (int s = 0; s < nb; s++)
                {
                    float sumSq = 0f;
                    for (int i = 0; i < na; i++)
                    {
                        int hiIdx = ((i * nb) / na + s) % nb;
                        sumSq += (bodyVerts[bodyLoop[i]] - headVerts[headLoop[hiIdx]]).sqrMagnitude;
                    }
                    if (sumSq < bestShiftSq)
                    {
                        bestShiftSq = sumSq;
                        bestShift = s;
                    }
                }
                if (bestShift > 0)
                {
                    var shifted = new List<int>(nb);
                    for (int i = 0; i < nb; i++) shifted.Add(headLoop[(i + bestShift) % nb]);
                    headLoop.Clear();
                    headLoop.AddRange(shifted);
                }
            }
        }

        private static BoneWeight LerpBoneWeight(BoneWeight a, BoneWeight b, float t)
        {
            var weights = new Dictionary<int, float>(8);
            AccumulateWeight(weights, a.boneIndex0, a.weight0 * (1f - t));
            AccumulateWeight(weights, a.boneIndex1, a.weight1 * (1f - t));
            AccumulateWeight(weights, a.boneIndex2, a.weight2 * (1f - t));
            AccumulateWeight(weights, a.boneIndex3, a.weight3 * (1f - t));
            AccumulateWeight(weights, b.boneIndex0, b.weight0 * t);
            AccumulateWeight(weights, b.boneIndex1, b.weight1 * t);
            AccumulateWeight(weights, b.boneIndex2, b.weight2 * t);
            AccumulateWeight(weights, b.boneIndex3, b.weight3 * t);

            var sorted = weights.Where(kv => kv.Value > 1e-5f).OrderByDescending(kv => kv.Value).Take(4).ToList();
            float total = sorted.Sum(kv => kv.Value);
            if (total <= 1e-6f) return a;

            BoneWeight res = default;
            if (sorted.Count > 0) { res.boneIndex0 = sorted[0].Key; res.weight0 = sorted[0].Value / total; }
            if (sorted.Count > 1) { res.boneIndex1 = sorted[1].Key; res.weight1 = sorted[1].Value / total; }
            if (sorted.Count > 2) { res.boneIndex2 = sorted[2].Key; res.weight2 = sorted[2].Value / total; }
            if (sorted.Count > 3) { res.boneIndex3 = sorted[3].Key; res.weight3 = sorted[3].Value / total; }
            return res;
        }

        private static void AccumulateWeight(Dictionary<int, float> dict, int boneIdx, float w)
        {
            if (w <= 1e-6f) return;
            dict[boneIdx] = dict.TryGetValue(boneIdx, out float cur) ? cur + w : w;
        }

        private static int[] GetHeadSubmeshTriangles(DollModelBuildSettings mbs, int targetSubmesh)
        {
            if (mbs.submeshTriCountTable == null || mbs.triVertexIndicesTable == null)
                return Array.Empty<int>();
            int cursor = 0;
            for (int s = 0; s < mbs.submeshTriCountTable.Length; s++)
            {
                int len = mbs.submeshTriCountTable[s];
                if (s == targetSubmesh)
                {
                    var tris = new int[len];
                    Array.Copy(mbs.triVertexIndicesTable, cursor, tris, 0, len);
                    return tris;
                }
                cursor += len;
            }
            return Array.Empty<int>();
        }

        private static void ComputeNeckBlendAndBodyUvs(
            int[] headSkinTris,
            int[] headToUnified,
            HashSet<int> seamUnifiedSet,
            List<Vector3> unifiedVerts,
            List<Vector2> unifiedUv1,
            List<Color> unifiedColors)
        {
            var adj = new Dictionary<int, List<int>>();
            for (int i = 0; i + 2 < headSkinTris.Length; i += 3)
            {
                int a = headToUnified[headSkinTris[i]];
                int b = headToUnified[headSkinTris[i + 1]];
                int c = headToUnified[headSkinTris[i + 2]];
                AddAdj(adj, a, b);
                AddAdj(adj, b, c);
                AddAdj(adj, c, a);
            }

            var ringDist = new Dictionary<int, int>();
            var frontier = new HashSet<int>();
            foreach (int sv in seamUnifiedSet)
            {
                ringDist[sv] = 0;
                frontier.Add(sv);
            }

            for (int step = 1; step <= 4; step++)
            {
                var next = new HashSet<int>();
                foreach (int u in frontier)
                {
                    if (!adj.TryGetValue(u, out var nbrs)) continue;
                    foreach (int v in nbrs)
                    {
                        if (!ringDist.ContainsKey(v))
                        {
                            ringDist[v] = step;
                            next.Add(v);
                        }
                    }
                }
                frontier = next;
            }

            float[] blendCurve = { 0.0f, 0.25f, 0.55f, 0.85f, 1.0f };
            var seamVerts = new List<int>(seamUnifiedSet);
            foreach (var kv in ringDist)
            {
                int ui = kv.Key;
                int dist = kv.Value;
                float val = dist < blendCurve.Length ? blendCurve[dist] : 1.0f;
                unifiedColors[ui] = new Color(val, val, val, 1f);

                if (dist > 0 && seamVerts.Count > 0)
                {
                    Vector3 p = unifiedVerts[ui];
                    int bestSeam = seamVerts[0];
                    float bestD = float.MaxValue;
                    foreach (int sv in seamVerts)
                    {
                        float d = (unifiedVerts[sv] - p).sqrMagnitude;
                        if (d < bestD) { bestD = d; bestSeam = sv; }
                    }
                    unifiedUv1[ui] = unifiedUv1[bestSeam];
                }
            }
        }

        private static void AddAdj(Dictionary<int, List<int>> adj, int a, int b)
        {
            if (a == b) return;
            if (!adj.TryGetValue(a, out var la)) { la = new List<int>(); adj[a] = la; }
            if (!la.Contains(b)) la.Add(b);
            if (!adj.TryGetValue(b, out var lb)) { lb = new List<int>(); adj[b] = lb; }
            if (!lb.Contains(a)) lb.Add(a);
        }

        private void GenerateShapeKeysUnified(Mesh targetMesh, DollModelBuildSettings mbs, int[] headToUnified, int unifiedVertCount)
        {
            int deltaIdx = 0;
            for (int i = 0; i < mbs.shapeKeyCount; i++)
            {
                string shapeKeyName = mbs.shapeKeyNamesTable[i];
                Vector3[] vertexDeltas = new Vector3[unifiedVertCount];
                int deltas = mbs.shapeKeyLengthsTable[i];
                for (int j = 0; j < deltas; j++)
                {
                    int origIdx = mbs.vertexDeltaIndicesTable[deltaIdx];
                    int mappedIdx = (origIdx >= 0 && origIdx < headToUnified.Length) ? headToUnified[origIdx] : -1;
                    if (mappedIdx >= 0 && mappedIdx < unifiedVertCount)
                    {
                        vertexDeltas[mappedIdx] = mbs.vertexDeltaOffsetTable[deltaIdx];
                    }
                    deltaIdx++;
                }
                targetMesh.AddBlendShapeFrame(shapeKeyName, 100.0f, vertexDeltas, null, null);
            }

            if (mbs.shapeKeyNamesTable == null || !mbs.shapeKeyNamesTable.Contains("pupilShrink"))
            {
                targetMesh.AddBlendShapeFrame("pupilShrink", 100.0f, new Vector3[unifiedVertCount], null, null);
            }
        }

        private static int[] FindBoundaryVerticesFromTris(Vector3[] verts, int[] tris)
        {
            if (verts == null || tris == null || verts.Length == 0 || tris.Length == 0)
                return Array.Empty<int>();

            var posToCanonical = new Dictionary<(int, int, int), int>(verts.Length);
            var canonicalOf = new int[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 v = verts[i];
                var key = (Mathf.RoundToInt(v.x * 10000f), Mathf.RoundToInt(v.y * 10000f), Mathf.RoundToInt(v.z * 10000f));
                if (!posToCanonical.TryGetValue(key, out int canon))
                {
                    canon = i;
                    posToCanonical[key] = canon;
                }
                canonicalOf[i] = canon;
            }

            var edgeCounts = new Dictionary<(int, int), int>();
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                int a = canonicalOf[tris[t]];
                int b = canonicalOf[tris[t + 1]];
                int c = canonicalOf[tris[t + 2]];
                CountEdge(edgeCounts, a, b);
                CountEdge(edgeCounts, b, c);
                CountEdge(edgeCounts, c, a);
            }

            var boundaryCanons = new HashSet<int>();
            foreach (var kv in edgeCounts)
            {
                if (kv.Value == 1)
                {
                    boundaryCanons.Add(kv.Key.Item1);
                    boundaryCanons.Add(kv.Key.Item2);
                }
            }

            if (boundaryCanons.Count == 0) return Array.Empty<int>();
            var result = new List<int>();
            for (int i = 0; i < verts.Length; i++)
            {
                if (boundaryCanons.Contains(canonicalOf[i])) result.Add(i);
            }
            return result.ToArray();
        }

        private static int[] FindBoundaryVertices(Mesh mesh, int submeshIndex)
        {
            Vector3[] verts = mesh.vertices;
            if (verts == null || verts.Length == 0) return Array.Empty<int>();

            // Map each vertex index to a canonical position key so UV-split seams don't look like boundaries
            var posToCanonical = new Dictionary<(int, int, int), int>(verts.Length);
            var canonicalOf = new int[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 v = verts[i];
                var key = (Mathf.RoundToInt(v.x * 10000f), Mathf.RoundToInt(v.y * 10000f), Mathf.RoundToInt(v.z * 10000f));
                if (!posToCanonical.TryGetValue(key, out int canon))
                {
                    canon = i;
                    posToCanonical[key] = canon;
                }
                canonicalOf[i] = canon;
            }

            var edgeCounts = new Dictionary<(int, int), int>();
            int startSub = submeshIndex >= 0 && submeshIndex < mesh.subMeshCount ? submeshIndex : 0;
            int endSub = submeshIndex >= 0 && submeshIndex < mesh.subMeshCount ? submeshIndex + 1 : mesh.subMeshCount;
            for (int s = startSub; s < endSub; s++)
            {
                int[] tris = mesh.GetTriangles(s);
                for (int t = 0; t + 2 < tris.Length; t += 3)
                {
                    int a = canonicalOf[tris[t]];
                    int b = canonicalOf[tris[t + 1]];
                    int c = canonicalOf[tris[t + 2]];
                    CountEdge(edgeCounts, a, b);
                    CountEdge(edgeCounts, b, c);
                    CountEdge(edgeCounts, c, a);
                }
            }

            var boundaryCanons = new HashSet<int>();
            foreach (var kv in edgeCounts)
            {
                if (kv.Value == 1)
                {
                    boundaryCanons.Add(kv.Key.Item1);
                    boundaryCanons.Add(kv.Key.Item2);
                }
            }

            if (boundaryCanons.Count == 0) return Array.Empty<int>();

            var result = new List<int>();
            for (int i = 0; i < verts.Length; i++)
            {
                if (boundaryCanons.Contains(canonicalOf[i]))
                {
                    result.Add(i);
                }
            }
            return result.ToArray();
        }

        private static void CountEdge(Dictionary<(int, int), int> counts, int a, int b)
        {
            if (a == b) return;
            var key = a < b ? (a, b) : (b, a);
            counts[key] = counts.TryGetValue(key, out int c) ? c + 1 : 1;
        }

        private static BoneWeight RemapBoneWeight(BoneWeight src, int[] map)
        {
            BoneWeight dst = default;
            dst.boneIndex0 = src.boneIndex0 >= 0 && src.boneIndex0 < map.Length && map[src.boneIndex0] >= 0 ? map[src.boneIndex0] : 0;
            dst.weight0 = src.weight0;
            dst.boneIndex1 = src.boneIndex1 >= 0 && src.boneIndex1 < map.Length && map[src.boneIndex1] >= 0 ? map[src.boneIndex1] : 0;
            dst.weight1 = src.weight1;
            dst.boneIndex2 = src.boneIndex2 >= 0 && src.boneIndex2 < map.Length && map[src.boneIndex2] >= 0 ? map[src.boneIndex2] : 0;
            dst.weight2 = src.weight2;
            dst.boneIndex3 = src.boneIndex3 >= 0 && src.boneIndex3 < map.Length && map[src.boneIndex3] >= 0 ? map[src.boneIndex3] : 0;
            dst.weight3 = src.weight3;
            return dst;
        }

        private static Texture2D BlitToReadableTexture(Texture source)
        {
            RenderTexture rt = RenderTexture.GetTemporary(256, 256, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(source, rt);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D readable = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
            readable.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return readable;
        }

        private static float Median(List<float> values)
        {
            values.Sort();
            int mid = values.Count / 2;
            return (values.Count % 2 == 1) ? values[mid] : 0.5f * (values[mid - 1] + values[mid]);
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
            if (texture != null) texture.wrapMode = TextureWrapMode.Clamp;
            if (rightEyeTexture != null) rightEyeTexture.wrapMode = TextureWrapMode.Clamp;
            if (leftEyeTexture != null) leftEyeTexture.wrapMode = TextureWrapMode.Clamp;

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
