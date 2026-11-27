using System.Collections.Generic;
using Unity.Animations.SpringBones;
using UnityEngine;
using UnityEngine.AI;

namespace Viva.Dolls
{
    /// <summary>
    /// Runtime holder for a Doll's body, head mesh, eye pivots, and dynamically created card bones.
    /// </summary>
    public class DollInstance : MonoBehaviour
    {
        [Header("Rig & Renderers")]
        public Transform bodyArmature;
        public Transform headBone;
        public SkinnedMeshRenderer headSMR;
        public SkinnedMeshRenderer[] bodySMRs;
        public GameObject defaultHairRoot;

        [Header("Eye Pivots")]
        public Transform pupil_r;
        public Transform pupil_l;

        [Header("Pose & Model")]
        public DollPoseCache basePoseCache;
        public string currentCardName;

        public DollModel CurrentModel { get; set; }
        public readonly List<GameObject> createdBones = new List<GameObject>();

        private Mesh baseBodyMeshAsset;
        private Mesh runtimeBodyMesh;

        private static readonly int SkinColorProp = Shader.PropertyToID("_SkinColor");
        private static readonly int NeckColorProp = Shader.PropertyToID("_NeckColor");
        private static readonly int NeckBlendStrengthProp = Shader.PropertyToID("_NeckBlendStrength");

        private Vector3[] capturedRestPositions;
        private Quaternion[] capturedRestRotations;
        private Transform[] capturedRestBones;

        private void Awake()
        {
            AutoResolveReferences();
            CaptureRestPose();
        }

        public void AutoResolveReferences()
        {
            if (bodyArmature == null)
            {
                bodyArmature = transform.Find("CharacterArmature")
                    ?? transform.Find("DollArmature")
                    ?? transform.Find("default_armature")
                    ?? DollUtilities.FindTransformRecursive(transform, "Armature")
                    ?? transform;
            }

            if (headBone == null)
            {
                headBone = DollUtilities.FindTransformRecursive(bodyArmature, "head");
            }

            if (bodySMRs == null || bodySMRs.Length == 0)
            {
                var foundBody = new List<SkinnedMeshRenderer>();
                foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    string n = smr.name.ToLowerInvariant();
                    if (n.Contains("body") || n.Contains("combined"))
                    {
                        foundBody.Add(smr);
                    }
                    else if (headSMR == null && n == "head")
                    {
                        headSMR = smr;
                    }
                }
                if (foundBody.Count > 0)
                {
                    bodySMRs = foundBody.ToArray();
                }
            }

            EnsureEyeTransforms();
        }

        public void EnsureEyeTransforms()
        {
            if (headBone == null) return;

            if (pupil_r == null)
            {
                pupil_r = DollUtilities.FindTransformRecursive(headBone, "pupil_r");
                if (pupil_r == null)
                {
                    var go = new GameObject("pupil_r");
                    go.transform.SetParent(headBone, false);
                    go.transform.localPosition = new Vector3(0.053f, 0.105f, 0.072f);
                    pupil_r = go.transform;
                }
            }

            if (pupil_l == null)
            {
                pupil_l = DollUtilities.FindTransformRecursive(headBone, "pupil_l");
                if (pupil_l == null)
                {
                    var go = new GameObject("pupil_l");
                    go.transform.SetParent(headBone, false);
                    go.transform.localPosition = new Vector3(-0.053f, 0.105f, 0.072f);
                    pupil_l = go.transform;
                }
            }
        }

        private void CaptureRestPose()
        {
            if (bodySMRs == null || bodySMRs.Length == 0 || bodySMRs[0] == null) return;
            capturedRestBones = bodySMRs[0].bones;
            if (capturedRestBones == null || capturedRestBones.Length == 0) return;

            capturedRestPositions = new Vector3[capturedRestBones.Length];
            capturedRestRotations = new Quaternion[capturedRestBones.Length];
            for (int i = 0; i < capturedRestBones.Length; i++)
            {
                if (capturedRestBones[i] != null)
                {
                    capturedRestPositions[i] = capturedRestBones[i].localPosition;
                    capturedRestRotations[i] = capturedRestBones[i].localRotation;
                }
            }
        }

        public void ResetToBindPose()
        {
            if (bodySMRs == null || bodySMRs.Length == 0 || bodySMRs[0] == null) return;
            Transform[] baseBones = bodySMRs[0].bones;
            if (baseBones == null) return;

            if (basePoseCache != null &&
                basePoseCache.positions != null &&
                basePoseCache.quaternions != null &&
                basePoseCache.positions.Length == baseBones.Length &&
                basePoseCache.quaternions.Length == baseBones.Length)
            {
                for (int i = 0; i < baseBones.Length; i++)
                {
                    if (baseBones[i] != null)
                    {
                        baseBones[i].localPosition = basePoseCache.positions[i];
                        baseBones[i].localRotation = basePoseCache.quaternions[i];
                    }
                }
            }
            else if (capturedRestBones != null && capturedRestBones.Length == baseBones.Length)
            {
                for (int i = 0; i < baseBones.Length; i++)
                {
                    if (baseBones[i] != null)
                    {
                        baseBones[i].localPosition = capturedRestPositions[i];
                        baseBones[i].localRotation = capturedRestRotations[i];
                    }
                }
            }
        }

        public Mesh GetBaseBodyMesh()
        {
            if (bodySMRs == null || bodySMRs.Length == 0 || bodySMRs[0] == null) return null;
            if (baseBodyMeshAsset == null)
            {
                baseBodyMeshAsset = bodySMRs[0].sharedMesh;
            }
            return baseBodyMeshAsset;
        }

        public Mesh GetOrCreateRuntimeBodyMesh()
        {
            if (bodySMRs == null || bodySMRs.Length == 0 || bodySMRs[0] == null) return null;
            if (baseBodyMeshAsset == null)
            {
                baseBodyMeshAsset = bodySMRs[0].sharedMesh;
            }
            if (baseBodyMeshAsset == null) return null;

            if (runtimeBodyMesh != null)
            {
                Destroy(runtimeBodyMesh);
            }
            runtimeBodyMesh = Instantiate(baseBodyMeshAsset);
            runtimeBodyMesh.name = $"{baseBodyMeshAsset.name}_Runtime";
            bodySMRs[0].sharedMesh = runtimeBodyMesh;
            return runtimeBodyMesh;
        }

        public void DestroyAllCreatedBones()
        {
            foreach (var boneGo in createdBones)
            {
                if (boneGo != null)
                {
                    DestroyImmediate(boneGo);
                }
            }
            createdBones.Clear();
        }

        public void ApplySkinColor(Color skinColor)
        {
            if (skinColor.a <= 0.01f) skinColor.a = 1f;

            if (bodySMRs != null)
            {
                foreach (var smr in bodySMRs)
                {
                    if (smr == null) continue;
                    foreach (var mat in smr.materials)
                    {
                        if (mat != null && mat.HasProperty(SkinColorProp))
                        {
                            mat.SetColor(SkinColorProp, skinColor);
                        }
                    }
                }
            }

            if (headSMR != null)
            {
                foreach (var mat in headSMR.materials)
                {
                    if (mat != null && mat.HasProperty(SkinColorProp))
                    {
                        mat.SetColor(SkinColorProp, skinColor);
                    }
                }
            }
        }

        public void ApplyNeckColor(Color neckColor, float blendStrength = 1f)
        {
            if (neckColor.a <= 0.01f) neckColor.a = 1f;
            float clampedStrength = Mathf.Clamp01(blendStrength);

            if (bodySMRs != null)
            {
                foreach (var smr in bodySMRs)
                {
                    if (smr == null) continue;
                    foreach (var mat in smr.materials)
                    {
                        if (mat == null) continue;
                        if (mat.HasProperty(NeckColorProp))
                        {
                            mat.SetColor(NeckColorProp, neckColor);
                        }
                        if (mat.HasProperty(NeckBlendStrengthProp))
                        {
                            mat.SetFloat(NeckBlendStrengthProp, clampedStrength);
                        }
                    }
                }
            }
        }

        public void SetLayerRecursive(int layer)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.layer = layer;
            }
        }

        public void SetPreviewMode(bool preview)
        {
            if (TryGetComponent<CharacterAI>(out var ai))
            {
                ai.isPreviewMode = preview;
                if (preview)
                {
                    ai.isFollowing = false;
                }
            }

            if (TryGetComponent<NavMeshAgent>(out var agent))
            {
                agent.enabled = !preview;
            }

            if (TryGetComponent<CharacterPhysicalReaction>(out var phys))
            {
                phys.enabled = !preview;
            }

            if (TryGetComponent<BoxCollider>(out var col))
            {
                col.enabled = !preview;
            }

            if (TryGetComponent<AudioSource>(out var audio))
            {
                audio.mute = preview;
            }

            if (TryGetComponent<CharacterInteractable>(out var interactable))
            {
                interactable.enabled = !preview;
            }

            foreach (var rb in GetComponentsInChildren<Rigidbody>(true))
            {
                if (preview) rb.isKinematic = true;
            }

            if (preview && TryGetComponent<SpringManager>(out var sm))
            {
                sm.FindSpringBones(true);
            }
        }
    }
}
