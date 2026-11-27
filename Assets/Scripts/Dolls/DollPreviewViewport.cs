using UnityEngine;
using UnityEngine.UI;

namespace Viva.Dolls
{
    [RequireComponent(typeof(Camera))]
    public class DollPreviewViewport : MonoBehaviour
    {
        [SerializeField] private MeshRenderer mirrorMeshRenderer;
        [SerializeField] private Material previewMirrorMaterial;
        [SerializeField] private Button spawnOrReplaceButton;

        public Camera RenderCamera { get; private set; }
        public RenderTexture PreviewRenderTexture { get; private set; }
        public DollInstance PreviewDoll { get; private set; }

        private Material[] cachedMirrorMaterials;
        private Light previewLight;
        private Vector3 currentPivot = Vector3.zero;
        private float currentHeight = 0.0f;
        private float currentRadius = 1.25f;
        private float orbitYaw = 180f;

        private void Awake()
        {
            RenderCamera = GetComponent<Camera>();
            if (mirrorMeshRenderer != null)
            {
                cachedMirrorMaterials = mirrorMeshRenderer.sharedMaterials;
            }
        }

        private void OnEnable()
        {
            if (RenderCamera == null) RenderCamera = GetComponent<Camera>();
            if (mirrorMeshRenderer != null && (cachedMirrorMaterials == null || cachedMirrorMaterials.Length == 0))
            {
                cachedMirrorMaterials = mirrorMeshRenderer.sharedMaterials;
            }

            PreviewRenderTexture = new RenderTexture(512, 1024, 16, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            PreviewRenderTexture.Create();

            int previewLayer = LayerMask.NameToLayer("DollPreview");
            if (previewLayer != -1)
            {
                RenderCamera.cullingMask = 1 << previewLayer;
                EnsurePreviewLight(previewLayer);
            }
            RenderCamera.clearFlags = CameraClearFlags.Skybox;
            RenderCamera.backgroundColor = new Color(0.28f, 0.30f, 0.35f, 1f);
            RenderCamera.targetTexture = PreviewRenderTexture;
            RenderCamera.enabled = true;
            if (previewLight != null) previewLight.enabled = true;

            if (previewMirrorMaterial != null)
            {
                previewMirrorMaterial.mainTexture = PreviewRenderTexture;
                if (previewMirrorMaterial.HasProperty("_BaseMap"))
                {
                    previewMirrorMaterial.SetTexture("_BaseMap", PreviewRenderTexture);
                }

                if (mirrorMeshRenderer != null)
                {
                    Material[] mats = mirrorMeshRenderer.materials;
                    if (mats.Length > 1)
                    {
                        mats[1] = previewMirrorMaterial;
                        mirrorMeshRenderer.materials = mats;
                    }
                }
            }
        }

        private void OnDisable()
        {
            if (RenderCamera != null)
            {
                RenderCamera.targetTexture = null;
            }
            if (previewLight != null)
            {
                previewLight.enabled = false;
            }

            if (PreviewRenderTexture != null)
            {
                PreviewRenderTexture.Release();
                Destroy(PreviewRenderTexture);
                PreviewRenderTexture = null;
            }

            if (mirrorMeshRenderer != null && cachedMirrorMaterials != null)
            {
                mirrorMeshRenderer.materials = cachedMirrorMaterials;
            }
        }

        public void Configure(MeshRenderer mirrorRenderer, Material previewMat, Button actionButton)
        {
            mirrorMeshRenderer = mirrorRenderer;
            previewMirrorMaterial = previewMat;
            spawnOrReplaceButton = actionButton;
            if (mirrorMeshRenderer != null)
            {
                cachedMirrorMaterials = mirrorMeshRenderer.sharedMaterials;
            }
        }

        public void SetPreviewDoll(DollInstance doll)
        {
            if (doll == null)
            {
                gameObject.SetActive(false);
                if (spawnOrReplaceButton != null) spawnOrReplaceButton.gameObject.SetActive(false);
                PreviewDoll = null;
                return;
            }

            if (RenderCamera == null) RenderCamera = GetComponent<Camera>();
            PreviewDoll = doll;
            doll.SetPreviewMode(true);

            int previewLayer = LayerMask.NameToLayer("DollPreview");
            if (previewLayer != -1)
            {
                doll.SetLayerRecursive(previewLayer);
                if (RenderCamera != null) RenderCamera.cullingMask = 1 << previewLayer;
            }

            gameObject.SetActive(true);
            if (spawnOrReplaceButton != null) spawnOrReplaceButton.gameObject.SetActive(true);

            Transform focusBone = doll.headBone != null ? doll.headBone : doll.transform;
            currentPivot = focusBone.position - Vector3.up * 0.25f;
            currentHeight = 0.0f;
            currentRadius = 1.25f;
            orbitYaw = 0f;

            UpdateCameraTransform();
        }

        private void LateUpdate()
        {
            if (PreviewDoll == null) return;

            // Gentle turntable rotation or keyboard A/D inspection could go here; keep front-facing by default
            Transform focusBone = PreviewDoll.headBone != null ? PreviewDoll.headBone : PreviewDoll.transform;
            currentPivot = focusBone.position - Vector3.up * 0.25f;
            UpdateCameraTransform();
        }

        private void UpdateCameraTransform()
        {
            if (PreviewDoll == null) return;
            Vector3 forward = PreviewDoll.transform.forward;
            Quaternion rot = Quaternion.AngleAxis(orbitYaw, Vector3.up);
            Vector3 dir = rot * forward;
            Vector3 pivot = currentPivot + Vector3.up * currentHeight;
            transform.position = pivot + dir * currentRadius;
            transform.rotation = Quaternion.LookRotation((pivot - transform.position).normalized, Vector3.up);
        }

        private void EnsurePreviewLight(int previewLayer)
        {
            if (previewLight == null)
            {
                previewLight = GetComponent<Light>();
                if (previewLight == null)
                {
                    previewLight = gameObject.AddComponent<Light>();
                }
            }
            // Use a short-range Point light at the isolated preview stage (y = -50) instead of a
            // Directional light, because URP ignores Light.cullingMask and a Directional light leaks into the room.
            previewLight.type = LightType.Point;
            previewLight.color = Color.white;
            previewLight.range = 6f;
            previewLight.intensity = 2.2f;
            previewLight.shadows = LightShadows.None;
            previewLight.cullingMask = 1 << previewLayer;
            previewLight.enabled = true;
        }
    }
}
