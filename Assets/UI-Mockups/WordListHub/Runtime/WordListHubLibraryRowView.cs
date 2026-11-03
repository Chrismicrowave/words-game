using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Words.UiMockups.WordListHub
{
    /// <summary>Component references authored on a Library row scene template.</summary>
    public sealed class WordListHubLibraryRowView : MonoBehaviour
    {
        public string itemId;
        public GameObject selectedVisual;
        public Button button;
        public Button favoriteButton;
        public TMP_Text favoriteMark;
        public TMP_Text bestTime;
        public TMP_Text title;
        public TMP_Text metadata;
        public TMP_Text recordStars;
        public GameObject badgeRoot;
        public TMP_Text badge;
    }
}
