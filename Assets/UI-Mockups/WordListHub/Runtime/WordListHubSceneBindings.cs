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
            public Button favoriteButton;
            public TMP_Text favoriteMark;
            public TMP_Text bestTime;
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

        public enum LibraryFilterKind
        {
            Order,
            Language,
            Played,
            Sort,
            MockConnectionState,
            AchievementStatus,
            CommunityOrder,
            CommunityLanguage,
            CommunityDownloadState,
            CommunitySort
        }

        [Serializable]
        public sealed class FilterOptionBinding
        {
            public int value;
            public string optionLabel;
            public Button button;
            public TMP_Text label;
            public GameObject selectedVisual;
        }

        [Serializable]
        public sealed class FilterSectionBinding
        {
            public LibraryFilterKind kind;
            public TMP_Text heading;
            public List<FilterOptionBinding> options = new List<FilterOptionBinding>();
        }

        [Serializable]
        public sealed class FilterMenuBinding
        {
            public Button toggleButton;
            public TMP_Text toggleLabel;
            public string toggleBaseLabel = "Filters";
            public GameObject panel;
            public ScrollRect scrollRect;
            public Transform content;
            public List<FilterSectionBinding> sections = new List<FilterSectionBinding>();
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
            public WordEditorLayoutBinding wordEditorLayout = new WordEditorLayoutBinding();
        }

        [Serializable]
        public sealed class WordEditorRowBinding
        {
            public GameObject root;
            public Button button;
            public Image background;
            public TMP_Text label;
        }

        [Serializable]
        public sealed class WordEditorLayoutBinding
        {
            public GameObject root;
            public ScrollRect scrollRect;
            public RectTransform viewport;
            public RectTransform content;
            public TMP_Text emptyNotice;
            public GameObject rowTemplate;
            public Button moveUpButton;
            public Button moveDownButton;
            public Button deleteButton;
            public Button addButton;
            public List<WordEditorRowBinding> authoredRows = new List<WordEditorRowBinding>();
        }

        [Serializable]
        public sealed class RubyPreviewBinding
        {
            public RectTransform root;
            public RectTransform viewport;
            public RectTransform content;
            public ScrollRect scrollRect;
            public Scrollbar scrollbar;
            public float previewFontScale = -1f;
            public List<GameObject> authoredEntries = new List<GameObject>();
            public List<string> previewWords = new List<string>();
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
            public FilterMenuBinding filtersMenu = new FilterMenuBinding();
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
            // DetailRecordPanel is retired; personal records remain in MockupWordList and list tiles.
            // public TMP_Text recordStars;
            // public TMP_Text recordTime;
            public TMP_Text detailWordsLabel;
            public TMP_Text detailWords;
            public TMP_Text detailStatus;
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
            public FilterMenuBinding connectionMenu = new FilterMenuBinding();
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
            public FilterMenuBinding filterMenu = new FilterMenuBinding();
            public Transform cardContent;
            public WordListHubAchievementCardView cardTemplate;
            public List<AchievementCardBinding> cards = new List<AchievementCardBinding>();
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
        public RubyPreviewBinding libraryRubyWords = new RubyPreviewBinding();
        public RubyPreviewBinding communityRubyWords = new RubyPreviewBinding();
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
