using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Words.UiMockups.WordListHub
{
    /// <summary>
    /// Serialized references to the authored Word List Hub scene. Runtime code must
    /// bind these references instead of searching the hierarchy or rebuilding pages.
    /// </summary>
    public sealed class WordListHubSceneBindings : MonoBehaviour
    {
        [Serializable]
        public sealed class ButtonBinding
        {
            public Button button;
            public TMP_Text label;
        }

        [Serializable]
        public sealed class PageBinding
        {
            public WordListHubPage page;
            public GameObject root;
        }

        [Serializable]
        public sealed class LibraryRowBinding
        {
            public string listId;
            public GameObject root;
            public GameObject selectedVisual;
            public Button button;
            public TMP_Text favoriteMark;
            public TMP_Text title;
            public TMP_Text metadata;
            public GameObject badgeRoot;
            public TMP_Text badge;
        }

        [Serializable]
        public sealed class GroupBinding
        {
            public string groupKey;
            public Button button;
            public TMP_Text label;
        }

        [Serializable]
        public sealed class DiscoveryRowBinding
        {
            public string remoteItemId;
            public GameObject root;
            public GameObject selectedVisual;
            public Button button;
            public TMP_Text title;
            public TMP_Text metadata;
            public TMP_Text summary;
            public TMP_Text status;
        }

        [Serializable]
        public sealed class AchievementCardBinding
        {
            public string achievementId;
            public GameObject root;
            public WordListHubAchievementCardView view;
        }

        [Serializable]
        public sealed class FormBinding
        {
            public GameObject root;
            public TMP_InputField titleInput;
            public TMP_InputField descriptionInput;
            public TMP_InputField tagsInput;
            public TMP_InputField wordsInput;
            public TMP_Dropdown languageDropdown;
            public TMP_Text wordsLabel;
            public TMP_Text validationText;
            public TMP_Text saveHintText;
            public Button saveButton;
            public TMP_Text saveButtonLabel;
            public Button cancelButton;
        }

        [Serializable]
        public sealed class ConfirmationBinding
        {
            public GameObject root;
            public TMP_Text message;
            public Button confirmButton;
            public TMP_Text confirmLabel;
            public Button cancelButton;
            public TMP_Text cancelLabel;
        }

        [Serializable]
        public sealed class LibraryPageBinding
        {
            public GameObject root;
            public ButtonBinding allLists = new ButtonBinding();
            public ButtonBinding favorites = new ButtonBinding();
            public TMP_InputField searchInput;
            public TMP_Dropdown languageFilter;
            public TMP_Dropdown playedFilter;
            public TMP_Dropdown sortFilter;
            public TMP_Dropdown directionFilter;
            public ButtonBinding createList = new ButtonBinding();
            public ScrollRect listScroll;
            public Transform listContent;
            public Transform detailPane;
            public TMP_Text emptyNotice;
            public List<GroupBinding> groups = new List<GroupBinding>();
            public List<LibraryRowBinding> authoredRows = new List<LibraryRowBinding>();
            public WordListHubLibraryRowView rowTemplate;

            public TMP_Text detailTitle;
            public TMP_Text detailDescription;
            public TMP_Text detailMetadata;
            public TMP_Text recordStars;
            public TMP_Text recordTime;
            public TMP_Text recordNote;
            public TMP_Text detailWordsLabel;
            public TMP_Text detailWords;
            public TMP_Text detailStatus;
            public ButtonBinding favorite = new ButtonBinding();
            public ButtonBinding delete = new ButtonBinding();
            public ButtonBinding play = new ButtonBinding();
            public ButtonBinding edit = new ButtonBinding();
            public ButtonBinding copyEditWords = new ButtonBinding();
            public ButtonBinding publish = new ButtonBinding();
            public ButtonBinding unpublish = new ButtonBinding();
        }

        [Serializable]
        public sealed class CommunityPageBinding
        {
            public GameObject root;
            public TMP_Dropdown connectionState;
            public TMP_InputField searchInput;
            public ScrollRect listScroll;
            public Transform listContent;
            public Transform detailPane;
            public TMP_Text emptyNotice;
            public List<DiscoveryRowBinding> authoredRows = new List<DiscoveryRowBinding>();
            public WordListHubDiscoveryRowView rowTemplate;
            public TMP_Text detailTitle;
            public TMP_Text detailAuthor;
            public TMP_Text detailSummary;
            public TMP_Text detailDescription;
            public TMP_Text detailMetadata;
            public TMP_Text detailTrustNote;
            public ButtonBinding like = new ButtonBinding();
            public ButtonBinding download = new ButtonBinding();
            public ButtonBinding play = new ButtonBinding();
            public ButtonBinding report = new ButtonBinding();
        }

        [Serializable]
        public sealed class AchievementsPageBinding
        {
            public GameObject root;
            public TMP_Dropdown filter;
            public Transform cardContent;
            public WordListHubAchievementCardView cardTemplate;
        }

        [Serializable]
        public sealed class ChallengesPageBinding
        {
            public GameObject root;
            public ButtonBinding dailyChallenge = new ButtonBinding();
            public ButtonBinding weeklyChallenge = new ButtonBinding();
            public TMP_Text description;
            public TMP_Text status;
        }

        [Serializable]
        public sealed class ModalBinding
        {
            public GameObject root;
            public TMP_Text title;
            public FormBinding newList = new FormBinding();
            public FormBinding editList = new FormBinding();
            public ConfirmationBinding contentChange = new ConfirmationBinding();
            public ConfirmationBinding deleteList = new ConfirmationBinding();
            public ConfirmationBinding playResult = new ConfirmationBinding();
        }

        public RectTransform card;
        public Transform hubRoot;
        public Transform navigationRoot;
        public ButtonBinding navChallenges = new ButtonBinding();
        public ButtonBinding navLibrary = new ButtonBinding();
        public ButtonBinding navCommunity = new ButtonBinding();
        public ButtonBinding navAchievements = new ButtonBinding();
        public ButtonBinding resetMockData = new ButtonBinding();
        public TMP_Text footer;
        public List<PageBinding> pages = new List<PageBinding>();
        public LibraryPageBinding library = new LibraryPageBinding();
        public CommunityPageBinding community = new CommunityPageBinding();
        public AchievementsPageBinding achievements = new AchievementsPageBinding();
        public ChallengesPageBinding challenges = new ChallengesPageBinding();
        public ModalBinding modal = new ModalBinding();
    }

    public enum WordListHubPage
    {
        Challenges,
        Library,
        Community,
        Achievements
    }
}
