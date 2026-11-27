using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

public class CharacterAssembler : MonoBehaviour
{
    #region Fields
    [SerializeField] private List<CharacterModel> loadedCharacters = new();
    private bool isLoading = false;

    private string charactersFolder = null;
    #endregion

    private void Awake()
    {
        GetCharactersFolder();
    }

    private void Start()
    {
        if (string.IsNullOrEmpty(charactersFolder))
        {
            GetCharactersFolder();
        }
    }

    private void GetCharactersFolder()
    {
#if UNITY_EDITOR
        if (Application.isEditor)
        {
            charactersFolder = Directory.GetParent(Application.dataPath).FullName;
            charactersFolder = Path.Combine(charactersFolder, "Characters");
            Debug.Log($"[Chara Loader] Found Characters path at {charactersFolder}");
        }
#else
        if (Application.platform == RuntimePlatform.WindowsPlayer)
        {
            string buildFolder = Directory.GetParent(Application.dataPath).FullName;
            charactersFolder = Path.Combine(buildFolder, "Characters");
        }
        else if (Application.platform == RuntimePlatform.Android)
        {
            charactersFolder = Path.Combine(Application.persistentDataPath, "Characters");
        }
#endif
    }

    #region Public Access
    /// <summary>
    /// Returns all loaded character models.
    /// </summary>
    public List<CharacterModel> GetAllModels() => loadedCharacters;

    /// <summary>
    /// Returns a character model by its name.
    /// </summary>
    public CharacterModel GetModelByName(string name) => loadedCharacters.Find(m => m.bundleName == name);

    /// <summary>
    /// Returns the first loaded character model (if any exists).
    /// </summary>
    public CharacterModel GetFirstModel() => loadedCharacters.Count > 0 ? loadedCharacters[0] : null;

    /// <summary>
    /// As the name suggests.
    /// </summary>
    public IEnumerator ReloadAllCharacters() => ReadAllCharacters();
    #endregion

    private IEnumerator ReadAllCharacters()
    {
        if (isLoading)
        {
            Debug.LogError("[Chara Loader] Already reading characters. Please wait.");
            yield break;
        }

        isLoading = true;

        if (string.IsNullOrEmpty(charactersFolder))
        {
            GetCharactersFolder();
        }

        if (!Directory.Exists(charactersFolder))
        {
            Debug.LogError("[Chara Loader] Characters folder not found: " + charactersFolder);
            isLoading = false;
            yield break;
        }

        string[] vivaFiles = Directory.GetFiles(charactersFolder, "*.viva");

        if (vivaFiles.Length == 0)
        {
            Debug.LogError("[Chara Loader] No .viva files have been found in Characters folder!");
            isLoading = false;
            yield break;
        }

        Debug.Log($"[Chara Loader] Found {vivaFiles.Length} characters to read.");

        // Clear the loaded characters before reading again
        loadedCharacters.Clear();

        yield return StartCoroutine(ReadCharactersAsync(vivaFiles));

        isLoading = false;
    }

    private IEnumerator ReadCharactersAsync(string[] vivaFiles)
    {
        foreach (string vivaFile in vivaFiles)
        {
            string bundleName = Path.GetFileNameWithoutExtension(vivaFile);

            // 1. Offload file reading and JSON parsing to a background thread
            Task<ParsedFileData> parseTask = Task.Run(() => ReadFileOnBackgroundThread(vivaFile));
            while (!parseTask.IsCompleted)
            {
                yield return null; // Wait for thread without blocking main frame
            }

            ParsedFileData fileData = parseTask.Result;
            if (fileData.HasError)
            {
                Debug.LogError($"[Chara Loader] {fileData.ErrorMessage}");
                continue;
            }

            // 2. Load AssetBundle asynchronously on the Main Thread
            AssetBundleCreateRequest bundleRequest = AssetBundle.LoadFromMemoryAsync(fileData.BundleData);
            yield return bundleRequest;

            AssetBundle assetBundle = bundleRequest.assetBundle;
            if (assetBundle == null)
            {
                Debug.LogError("[Chara Loader] Failed to load AssetBundle from memory.");
                continue;
            }

            // 3. Load Prefab asynchronously on the Main Thread
            GameObject prefab = null;
            if (!string.IsNullOrEmpty(fileData.CharData.PrefabName))
            {
                AssetBundleRequest assetRequest = assetBundle.LoadAssetAsync<GameObject>(fileData.CharData.PrefabName);
                yield return assetRequest;
                prefab = assetRequest.asset as GameObject;
            }

            // Load the first asset if prefab is empty for some reason
            if (prefab == null)
            {
                AssetBundleRequest allAssetsRequest = assetBundle.LoadAllAssetsAsync<GameObject>();
                yield return allAssetsRequest;

                if (allAssetsRequest.allAssets.Length > 0)
                {
                    prefab = allAssetsRequest.allAssets[0] as GameObject;
                    Debug.LogWarning("[Chara Loader] Prefab name not found, using first asset.");
                }
            }

            if (prefab == null)
            {
                Debug.LogError("[Chara Loader] Failed to load prefab from AssetBundle.");
                assetBundle.Unload(false);
                continue;
            }

            // 4. Final setup on Main Thread
            UpgradeIncompatibleMaterialsToURP(prefab);

            VivaCharacter newData = prefab.AddComponent<VivaCharacter>();
            newData.characterData = fileData.CharData;

            GameObject headObj = !string.IsNullOrEmpty(fileData.CharData?.Info?.HeadBonePath)
                ? VivaUtilities.FindGameObjectByPath(prefab.gameObject, fileData.CharData.Info.HeadBonePath)
                : null;

            CharacterModel newCharacterModel = new()
            {
                bundleName = bundleName,
                prefab = prefab,
                headBone = headObj != null ? headObj.transform : null,
                vivaCharacterData = fileData.CharData
            };

            loadedCharacters.Add(newCharacterModel);

            // Cleanup AssetBundle
            assetBundle.Unload(false);
            Debug.Log($"[Chara Loader] Character loaded with prefab name: {prefab.name}");
        }

        Debug.Log($"[Chara Loader] Loaded {loadedCharacters.Count} characters.");
        isLoading = false;
    }

    private static void UpgradeIncompatibleMaterialsToURP(GameObject root)
    {
        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
        Shader urpUnlit = Shader.Find("Universal Render Pipeline/Unlit");
        if (urpLit == null && urpUnlit == null) return;

        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = renderer.sharedMaterials;
            if (mats == null) continue;

            for (int i = 0; i < mats.Length; i++)
            {
                Material mat = mats[i];
                if (mat == null) continue;

                string shaderName = mat.shader != null ? mat.shader.name : string.Empty;
                bool isBuiltInOnly =
                    shaderName == "Hidden/InternalErrorShader" ||
                    shaderName == "Standard" ||
                    shaderName == "Standard (Specular setup)" ||
                    shaderName.StartsWith("Legacy Shaders/", StringComparison.Ordinal) ||
                    shaderName.StartsWith("Mobile/", StringComparison.Ordinal) ||
                    shaderName.StartsWith("Unlit/", StringComparison.Ordinal);

                // If the project has a URP-compiled version of this shader (e.g. lilToon),
                // rebind to the local project shader instead of the AssetBundle's Built-in RP shader.
                if (!string.IsNullOrEmpty(shaderName) && !isBuiltInOnly)
                {
                    Shader localShader = Shader.Find(shaderName);
                    if (localShader != null && localShader.isSupported)
                    {
                        if (mat.shader != localShader)
                        {
                            mat.shader = localShader;
                            Debug.Log($"[Chara Loader] Rebound AssetBundle shader '{shaderName}' on '{mat.name}' to project URP shader.");
                        }
                        continue;
                    }
                }

                bool isBrokenOrBuiltIn =
                    mat.shader == null ||
                    !mat.shader.isSupported ||
                    isBuiltInOnly ||
                    Shader.Find(shaderName) == null;

                if (!isBrokenOrBuiltIn) continue;

                Texture mainTex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : mat.mainTexture;
                Color baseColor = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
                Texture bumpMap = mat.HasProperty("_BumpMap") ? mat.GetTexture("_BumpMap") : null;
                float cutoff = mat.HasProperty("_Cutoff") ? mat.GetFloat("_Cutoff") : 0.5f;
                float mode = mat.HasProperty("_Mode") ? mat.GetFloat("_Mode") : 0f;
                int prevRenderQueue = mat.renderQueue;

                bool useUnlit = shaderName.StartsWith("Unlit/", StringComparison.Ordinal) && urpUnlit != null;
                mat.shader = useUnlit ? urpUnlit : (urpLit ?? urpUnlit);

                if (mainTex != null && mat.HasProperty("_BaseMap"))
                {
                    mat.SetTexture("_BaseMap", mainTex);
                }
                if (mat.HasProperty("_BaseColor"))
                {
                    mat.SetColor("_BaseColor", baseColor);
                }
                if (bumpMap != null && mat.HasProperty("_BumpMap"))
                {
                    mat.SetTexture("_BumpMap", bumpMap);
                    mat.EnableKeyword("_NORMALMAP");
                }

                // Preserve Cutout (mode == 1) or Transparent (mode == 2/3)
                if (mode >= 2f || prevRenderQueue >= 3000)
                {
                    if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
                    mat.SetOverrideTag("RenderType", "Transparent");
                    mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                }
                else if (Mathf.Approximately(mode, 1f) || (prevRenderQueue >= 2450 && prevRenderQueue < 3000))
                {
                    if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 1f);
                    if (mat.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", cutoff);
                    mat.EnableKeyword("_ALPHATEST_ON");
                    mat.SetOverrideTag("RenderType", "TransparentCutout");
                    mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                }

                Debug.Log($"[Chara Loader] Remapped shader '{shaderName}' on '{mat.name}' to '{mat.shader.name}'.");
            }
        }
    }

    /// <summary>
    /// Thread-safe helper method to read binary and JSON off the main thread.
    /// </summary>
    private ParsedFileData ReadFileOnBackgroundThread(string characterPath)
    {
        if (!File.Exists(characterPath))
        {
            return new ParsedFileData { ErrorMessage = $"Package not found: {characterPath}" };
        }

        try
        {
            using FileStream fs = new(characterPath, FileMode.Open, FileAccess.Read);
            using BinaryReader reader = new(fs);
            var header = VivaFormat.ReadHeader(reader);

            if (header.VivaKey != VivaFormat.VivaBytes)
            {
                return new ParsedFileData { ErrorMessage = $"Invalid package file format: {characterPath} (Key: {header.VivaKey})" };
            }

            if (header.Version > VivaFormat.CurrentVersion)
            {
                return new ParsedFileData { ErrorMessage = $"Unsupported version: {header.Version}. Max supported: {VivaFormat.CurrentVersion}." };
            }

            byte[] characterDataBytes = reader.ReadBytes(header.CharacterDataSize);
            byte[] bundleData = reader.ReadBytes(header.BundleSize);

            VivaCharacterData charData = JsonUtility.FromJson<VivaCharacterData>(
                System.Text.Encoding.UTF8.GetString(characterDataBytes));

            charData.Version = VivaFormat.CurrentVersion;

            return new ParsedFileData
            {
                CharData = charData,
                BundleData = bundleData
            };
        }
        catch (Exception ex)
        {
            return new ParsedFileData { ErrorMessage = $"Failed to load character: {ex.Message}" };
        }
    }

    #region Helper Classes
    private class ParsedFileData
    {
        public VivaCharacterData CharData;
        public byte[] BundleData;
        public string ErrorMessage;
        public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    }

    public class CharacterModel
    {
        public string bundleName;
        public GameObject prefab;
        public Transform headBone;
        public VivaCharacterData vivaCharacterData;
    }
    #endregion
}