using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Words.UiMockups.WordListHub
{
    /// <summary>Component references authored on a Library row scene template.</summary>
    public sealed class WordListHubLibraryRowView : MonoBehaviour
    {
        public string itemId;
        public Button button;
        public TMP_Text favoriteMark;
        public TMP_Text title;
        public TMP_Text metadata;
        public GameObject badgeRoot;
        public TMP_Text badge;
    }
}
