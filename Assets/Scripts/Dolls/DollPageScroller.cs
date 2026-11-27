using UnityEngine;
using UnityEngine.UI;

namespace Viva.Dolls
{
    [System.Serializable]
    public class DollPageScroller
    {
        public delegate int OnMaxPageManifest();
        public delegate void OnPageUpdate(int page);

        [SerializeField] private RectTransform pageContent;
        [SerializeField] private Button rightPageButton;
        [SerializeField] private Button leftPageButton;
        [SerializeField] private Text pageText;

        private int m_page = 0;
        public int Page => m_page;
        private OnMaxPageManifest onMaxPageManifest;
        private OnPageUpdate onPageUpdate;
        private bool pageChangeEnabled = true;

        public void Initialize(OnMaxPageManifest maxPageManifest, OnPageUpdate pageUpdate)
        {
            onMaxPageManifest = maxPageManifest;
            onPageUpdate = pageUpdate;

            if (rightPageButton != null)
            {
                rightPageButton.onClick.RemoveAllListeners();
                rightPageButton.onClick.AddListener(() => FlipPage(1));
            }
            if (leftPageButton != null)
            {
                leftPageButton.onClick.RemoveAllListeners();
                leftPageButton.onClick.AddListener(() => FlipPage(-1));
            }
        }

        public void FlipPage(int delta)
        {
            if (!pageChangeEnabled) return;

            int maxPages = onMaxPageManifest != null ? Mathf.Max(0, onMaxPageManifest()) : 0;
            int newPage = Mathf.Clamp(m_page + delta, 0, maxPages);

            if (rightPageButton != null) rightPageButton.gameObject.SetActive(newPage < maxPages);
            if (leftPageButton != null) leftPageButton.gameObject.SetActive(newPage > 0);

            if (newPage == m_page && delta != 0) return;

            m_page = newPage;
            if (pageText != null)
            {
                pageText.text = $"Page {m_page + 1}/{maxPages + 1}";
            }
            onPageUpdate?.Invoke(m_page);
        }

        public void SetEnablePageChange(bool enable)
        {
            pageChangeEnabled = enable;
        }

        public RectTransform GetPageContent() => pageContent;
    }
}
