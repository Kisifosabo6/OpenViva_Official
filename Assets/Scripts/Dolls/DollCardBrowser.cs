using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Viva.Dolls
{
    [System.Serializable]
    public class DollCardBrowser
    {
        public delegate void ExecuteLoadCard(string fileName, Button sourceCard);

        public class CardEntry
        {
            public readonly Button button;
            public readonly Image image;
            public readonly Text label;

            private string fileName;
            private Texture2D thumbnail;
            private Sprite thumbnailSprite;

            public CardEntry(GameObject container)
            {
                button = container.GetComponent<Button>();
                image = container.GetComponent<Image>();
                label = container.GetComponentInChildren<Text>(true);
            }

            public void Initialize(MonoBehaviour host, DollCardBrowser browser, string _fileName, ExecuteLoadCard onSelect)
            {
                fileName = _fileName;
                SetThumbnail(null);
                if (label != null)
                {
                    label.text = Path.GetFileNameWithoutExtension(fileName);
                }

                if (button != null)
                {
                    button.onClick.RemoveAllListeners();
                    button.onClick.AddListener(() => onSelect?.Invoke(fileName, button));
                }

                if (browser.LoadThumbnails && host != null)
                {
                    string thumbPath = Path.Combine(Stegosaurus.EnsureFolderExistence(browser.CardFolder + "/.thumbs"), fileName);
                    if (!File.Exists(thumbPath))
                    {
                        host.StartCoroutine(GenerateThenLoadThumbnail(browser.CardFolder));
                    }
                    else
                    {
                        host.StartCoroutine(LoadThumbnail(thumbPath));
                    }
                }
            }

            private IEnumerator GenerateThenLoadThumbnail(string folder)
            {
                string fullPath = Path.Combine(Stegosaurus.EnsureFolderExistence(folder), fileName);
                byte[] rawFileData = null;

                Task readTask = Task.Run(() =>
                {
                    try { rawFileData = File.ReadAllBytes(fullPath); }
                    catch (System.Exception e) { Debug.LogError($"[DollCardBrowser] Failed reading {fullPath}: {e.Message}"); }
                });
                while (!readTask.IsCompleted) yield return null;

                if (rawFileData == null || rawFileData.Length == 0) yield break;

                Texture2D cardTexture = new Texture2D(Stegosaurus.PACK_SIZE, Stegosaurus.CARD_HEIGHT, TextureFormat.RGB24, false, false);
                if (ImageConversion.LoadImage(cardTexture, rawFileData, false))
                {
                    cardTexture.Apply();
                    cardTexture.name = Path.GetFileNameWithoutExtension(fileName);

                    Texture2D newThumb = Stegosaurus.AttemptSaveCardThumbnail(cardTexture, folder + "/.thumbs");
                    if (newThumb != null)
                    {
                        SetThumbnail(newThumb);
                    }
                }
                Object.Destroy(cardTexture);
            }

            private IEnumerator LoadThumbnail(string thumbPath)
            {
                byte[] thumbnailData = null;
                Task readTask = Task.Run(() =>
                {
                    try { thumbnailData = File.ReadAllBytes(thumbPath); }
                    catch { /* fallback handled below */ }
                });
                while (!readTask.IsCompleted) yield return null;

                if (thumbnailData == null || thumbnailData.Length == 0) yield break;

                int w = Stegosaurus.PACK_SIZE / Stegosaurus.THUMBNAIL_SCALE;
                int h = Stegosaurus.CARD_HEIGHT / Stegosaurus.THUMBNAIL_SCALE;
                Texture2D newThumb = new Texture2D(w, h, TextureFormat.RGB24, false, false);
                if (ImageConversion.LoadImage(newThumb, thumbnailData, false))
                {
                    newThumb.Apply();
                    SetThumbnail(newThumb);
                }
                else
                {
                    Object.Destroy(newThumb);
                }
            }

            public void SetThumbnail(Texture2D newThumb)
            {
                if (thumbnail != null)
                {
                    Object.Destroy(thumbnail);
                    thumbnail = null;
                }
                if (thumbnailSprite != null)
                {
                    Object.Destroy(thumbnailSprite);
                    thumbnailSprite = null;
                }
                thumbnail = newThumb;
                if (newThumb == null || image == null) return;

                thumbnailSprite = Sprite.Create(thumbnail, new Rect(0, 0, thumbnail.width, thumbnail.height), new Vector2(0.5f, 0.5f));
                image.sprite = thumbnailSprite;
            }
        }

        public class LoadCardTextureRequest
        {
            public readonly string name;
            public Texture2D result;
            public string error;

            public LoadCardTextureRequest(string _name)
            {
                name = _name;
            }
        }

        [SerializeField] private string m_cardFolder;
        public string CardFolder => m_cardFolder;

        [SerializeField] private DollPageScroller cardScroller = new DollPageScroller();
        [SerializeField] private RectTransform cardEntriesContainer;

        private MonoBehaviour hostRunner;
        private Coroutine loadCardsCoroutine = null;
        private CardEntry[] cardEntries;
        private string[] cardManifest = null;
        private string[] filteredCards = null;
        private ExecuteLoadCard executeLoadCard;
        public bool LoadThumbnails { get; private set; }

        public void Configure(string folder, RectTransform entriesContainer, DollPageScroller scroller)
        {
            m_cardFolder = folder;
            cardEntriesContainer = entriesContainer;
            cardScroller = scroller;
        }

        public void Initialize(MonoBehaviour host, bool loadThumbnails, ExecuteLoadCard onSelectCard)
        {
            hostRunner = host;
            LoadThumbnails = loadThumbnails;
            executeLoadCard = onSelectCard;
            cardScroller?.Initialize(OnLoadCardMaxPages, OnUpdateLoadCardPage);
            InitializeCardEntries();
            RefreshCards();
        }

        private void InitializeCardEntries()
        {
            if (cardEntries != null || cardEntriesContainer == null) return;
            cardEntries = new CardEntry[cardEntriesContainer.childCount];
            for (int i = 0; i < cardEntries.Length; i++)
            {
                cardEntries[i] = new CardEntry(cardEntriesContainer.GetChild(i).gameObject);
            }
        }

        public IEnumerator LoadCardTexture(LoadCardTextureRequest request)
        {
            request.result = null;
            request.error = null;

            string deckDir = Stegosaurus.EnsureFolderExistence(m_cardFolder);
            string path = FindMatchingFileCaseInsensitive(deckDir, request.name);
            if (string.IsNullOrEmpty(path))
            {
                request.error = $"[DollCardBrowser] Card file not found: {Path.Combine(deckDir, request.name)}";
                yield break;
            }

            byte[] data = null;
            Task readTask = Task.Run(() =>
            {
                try { data = File.ReadAllBytes(path); }
                catch (System.Exception e) { request.error = $"{e.Message}\nCould not open file {path}"; }
            });

            while (!readTask.IsCompleted) yield return null;

            if (data == null || data.Length == 0)
            {
                request.error += $" Could not read file {path}";
                yield break;
            }

            Texture2D texture = new Texture2D(Stegosaurus.PACK_SIZE, Stegosaurus.CARD_HEIGHT, TextureFormat.RGB24, false, false);
            if (ImageConversion.LoadImage(texture, data, false))
            {
                texture.name = Path.GetFileName(path);
                if (texture.width == 8 && texture.height == 8)
                {
                    Object.Destroy(texture);
                    request.error = "[DollCardBrowser] Invalid card image dimensions (8x8 fallback).";
                }
                else
                {
                    request.result = texture;
                }
            }
            else
            {
                Object.Destroy(texture);
                request.error = $"[DollCardBrowser] Could not decode PNG texture at {path}";
            }
        }

        private static string FindMatchingFileCaseInsensitive(string directory, string targetFileName)
        {
            string exact = Path.Combine(directory, targetFileName);
            if (File.Exists(exact)) return exact;

            if (!Directory.Exists(directory)) return null;
            string lowerTarget = targetFileName.ToLowerInvariant();
            foreach (string file in Directory.GetFiles(directory))
            {
                if (Path.GetFileName(file).ToLowerInvariant() == lowerTarget)
                {
                    return file;
                }
            }
            return null;
        }

        public string[] FindAllExistingCardsInFolders()
        {
            var thumbsPath = Stegosaurus.EnsureFolderExistence(m_cardFolder + "/.thumbs");
            HashSet<string> cardsInFolderDict = new HashSet<string>();

            FileInfo[] thumbFiles = new DirectoryInfo(thumbsPath).GetFiles();
            AddFilesIfValidCardPath(thumbFiles, Stegosaurus.EnsureFolderExistence(m_cardFolder), cardsInFolderDict);

            var deckPath = Stegosaurus.EnsureFolderExistence(m_cardFolder);
            FileInfo[] deckFiles = new DirectoryInfo(deckPath).GetFiles();
            AddFilesIfValidCardPath(deckFiles, deckPath, cardsInFolderDict);

            return cardsInFolderDict.OrderBy(n => n).ToArray();
        }

        public void RefreshCards()
        {
            cardManifest = FindAllExistingCardsInFolders();
            FilterCards("");
            cardScroller?.FlipPage(0);
        }

        public void SetAllCardsInteractable(bool interactable)
        {
            if (cardEntries == null) return;
            foreach (var card in cardEntries)
            {
                if (card?.button != null) card.button.interactable = interactable;
            }
        }

        public void FilterCards(string filter)
        {
            if (cardManifest == null)
            {
                filteredCards = System.Array.Empty<string>();
                return;
            }
            if (string.IsNullOrEmpty(filter))
            {
                filteredCards = cardManifest;
                return;
            }

            string lowerFilter = filter.ToLowerInvariant();
            filteredCards = cardManifest.Where(c => c.ToLowerInvariant().Contains(lowerFilter)).ToArray();
        }

        private void AddFilesIfValidCardPath(FileInfo[] files, string deckDirectory, HashSet<string> cardsInFolderDict)
        {
            foreach (var file in files)
            {
                string cardName = file.Name;
                if (!cardName.ToLowerInvariant().EndsWith(".png")) continue;

                string deckFile = Path.Combine(deckDirectory, cardName);
                if (!File.Exists(deckFile))
                {
                    string orphanThumb = Path.Combine(deckDirectory, ".thumbs", cardName);
                    if (File.Exists(orphanThumb)) File.Delete(orphanThumb);
                    continue;
                }

                string entryName = Path.GetFileNameWithoutExtension(cardName);
                cardsInFolderDict.Add(entryName);
            }
        }

        private int OnLoadCardMaxPages()
        {
            if (filteredCards == null || filteredCards.Length == 0 || cardEntries == null || cardEntries.Length == 0)
                return 0;
            return Mathf.Max(0, Mathf.CeilToInt((float)filteredCards.Length / cardEntries.Length) - 1);
        }

        private void OnUpdateLoadCardPage(int page)
        {
            if (hostRunner == null) return;
            if (loadCardsCoroutine != null)
            {
                hostRunner.StopCoroutine(loadCardsCoroutine);
            }
            loadCardsCoroutine = hostRunner.StartCoroutine(LoadCardPage(page));
        }

        private IEnumerator LoadCardPage(int page)
        {
            if (cardEntries == null || filteredCards == null) yield break;

            cardScroller?.SetEnablePageChange(false);

            int cardIndex = page * cardEntries.Length;
            int i = 0;
            for (; i < cardEntries.Length; i++)
            {
                if (cardIndex >= filteredCards.Length) break;
                CardEntry card = cardEntries[i];
                card.Initialize(hostRunner, this, filteredCards[cardIndex++] + ".png", executeLoadCard);
                card.button.gameObject.SetActive(true);
            }
            for (; i < cardEntries.Length; i++)
            {
                cardEntries[i].button.gameObject.SetActive(false);
            }

            yield return new WaitForSecondsRealtime(0.15f);
            cardScroller?.SetEnablePageChange(true);
        }
    }
}
