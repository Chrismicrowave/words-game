using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Words.UiMockups.WordListHub
{
    internal sealed class MockupSlicedDropdown : TMP_Dropdown
    {
        protected override GameObject CreateBlocker(Canvas rootCanvas)
        {
            GameObject blocker = base.CreateBlocker(rootCanvas);
            if (blocker != null)
            {
                foreach (Image image in blocker.GetComponentsInChildren<Image>(true))
                    image.type = Image.Type.Sliced;
            }
            return blocker;
        }
    }

    /// <summary>
    /// Local-only interaction layer for the isolated Word List Hub mockup scene.
    /// All state lives in a fresh MockupWordListStore and is discarded when the scene
    /// is opened again. This controller never talks to gameplay, Steam, files or prefs.
    /// </summary>
    public sealed class WordListHubMockupController : MonoBehaviour
    {
        private enum Page
        {
            Challenges,
            Library,
            Community,
            Achievements
        }

        private enum AchievementFilter
        {
            All,
            Unlocked,
            InProgress
        }

        private static readonly Color Ink = new Color(0.17f, 0.17f, 0.15f, 1f);
        private static readonly Color MutedInk = new Color(0.39f, 0.39f, 0.35f, 1f);
        private static readonly Color Accent = new Color(0.722f, 0.467f, 0.353f, 1f);
        private static readonly Color AccentDark = new Color(0.49f, 0.27f, 0.20f, 1f);
        private static readonly Color Paper = new Color(0.963f, 0.953f, 0.889f, 1f);
        private static readonly Color PaperAlt = new Color(0.925f, 0.925f, 0.846f, 1f);
        private static readonly Color White = new Color(1f, 0.995f, 0.96f, 1f);
        private static readonly Color Sage = new Color(0.34f, 0.48f, 0.38f, 1f);
        private const float DropdownItemHeight = 20f;

        [SerializeField] private TMP_FontAsset primaryFont;
        [SerializeField] private TMP_FontAsset cjkFont;
        [SerializeField] private Sprite standardSprite;
        [SerializeField] private Sprite backgroundSprite;
        [SerializeField] private Sprite inputFieldSprite;
        [SerializeField] private Sprite maskSprite;
        [SerializeField] private Sprite firstWordUnlockedIcon;
        [SerializeField] private Sprite firstWordLockedIcon;
        [SerializeField] private Sprite twoWordsUnlockedIcon;
        [SerializeField] private Sprite twoWordsLockedIcon;
        [SerializeField] private Sprite wordWithDUnlockedIcon;
        [SerializeField] private Sprite wordWithDLockedIcon;

        private readonly HashSet<MockupLibraryGroup> collapsedGroups =
            new HashSet<MockupLibraryGroup>();

        private DefaultControls.Resources uiResources;
        private MockupWordListStore store;
        private MockupLibraryQuery query;
        private Page currentPage = Page.Library;
        private AchievementFilter achievementFilter = AchievementFilter.All;
        private string selectedListId;
        private string selectedDiscoveryId;
        private string librarySearchText = string.Empty;
        private string communitySearchText = string.Empty;
        private string lastActionMessage = string.Empty;
        private Transform card;
        private Transform generatedRoot;
        private Transform dialogContentRoot;
        private Transform pageRoot;
        private Transform libraryListContent;
        private Transform libraryDetailPane;
        private ScrollRect libraryScrollRect;
        private Transform communityListContent;
        private Transform communityDetailPane;
        private TMP_Text footerText;
        private MockupWordListEdit editDraft;
        private string editingListId;
        private TMP_InputField editTitleInput;
        private TMP_InputField editDescriptionInput;
        private TMP_InputField newListTitleInput;
        private TMP_InputField newListDescriptionInput;
        private TMP_InputField newListWordsInput;
        private TMP_InputField newListTagsInput;
        private TMP_InputField editWordsInput;
        private TMP_InputField editTagsInput;
        private TMP_Text dialogValidationText;
        private TMP_Text editSaveHintText;
        private Button editSaveButton;
        private Coroutine libraryScrollRoutine;
        private string pendingDeleteListId;
        private int pendingDeleteVisibleIndex = -1;
        private MockupLanguage newListLanguage = MockupLanguage.English;
        private MockupLanguage editLanguage = MockupLanguage.English;
        private bool copyEditingMode;
        private bool editWordsReadOnly;

        /// <summary>
        /// Called once by the Editor scene builder so the saved mockup scene contains
        /// visible preview UI. Play mode rebuilds the same view and wires local events.
        /// </summary>
        public void BuildPreviewInEditor()
        {
            EnsureStore();
            BuildView(false);
        }

        private void Awake()
        {
            EnsureStore();
            BuildView(true);
        }

        private void EnsureStore()
        {
            if (store == null)
                store = MockupWordListStore.CreateWithFixtures();
            if (query == null)
                query = new MockupLibraryQuery();

            uiResources = new DefaultControls.Resources
            {
                standard = standardSprite,
                background = backgroundSprite,
                inputField = inputFieldSprite,
                knob = standardSprite,
                checkmark = standardSprite,
                dropdown = standardSprite,
                mask = maskSprite
            };
        }

        private void BuildView(bool wireEvents)
        {
            StopLibraryScrollRoutine();
            card = transform.Find("Card");
            if (card == null)
                throw new InvalidOperationException("The mockup clone is missing its Card visual root.");

            VerticalLayoutGroup oldLayout = card.GetComponent<VerticalLayoutGroup>();
            if (oldLayout != null)
                oldLayout.enabled = false;

            TMP_Text title = card.Find("Title") != null
                ? card.Find("Title").GetComponent<TMP_Text>()
                : null;
            if (title == null)
                throw new InvalidOperationException("The mockup clone is missing the original TMP title.");

            title.text = "WORD LIST HUB";
            title.font = primaryFont != null ? primaryFont : title.font;
            title.fontSize = 29f;
            title.fontStyle = FontStyles.Bold;
            title.color = Ink;
            title.alignment = TextAlignmentOptions.MidlineLeft;
            title.raycastTarget = false;
            SetRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(34f, -19f), new Vector2(430f, 54f));

            Transform prior = card.Find("HubGenerated");
            if (prior != null)
            {
                if (Application.isPlaying)
                    Destroy(prior.gameObject);
                else
                    DestroyImmediate(prior.gameObject);
            }

            GameObject root = new GameObject("HubGenerated", typeof(RectTransform));
            generatedRoot = root.transform;
            generatedRoot.SetParent(card, false);
            SetStretch(generatedRoot as RectTransform, 28f, 24f, 28f, 82f);

            BuildGlobalNavigation(wireEvents);

            GameObject page = new GameObject("PageContent", typeof(RectTransform));
            pageRoot = page.transform;
            pageRoot.SetParent(generatedRoot, false);
            SetStretch(pageRoot as RectTransform, 0f, 0f, 0f, 70f);
            libraryListContent = null;
            libraryDetailPane = null;
            libraryScrollRect = null;
            communityListContent = null;
            communityDetailPane = null;

            GameObject notice = new GameObject("SampleDataWatermark", typeof(RectTransform));
            notice.transform.SetParent(generatedRoot, false);
            RectTransform noticeRect = notice.GetComponent<RectTransform>();
            noticeRect.anchorMin = Vector2.zero;
            noticeRect.anchorMax = new Vector2(1f, 0f);
            noticeRect.pivot = new Vector2(0.5f, 0f);
            noticeRect.sizeDelta = new Vector2(-24f, 32f);
            noticeRect.anchoredPosition = new Vector2(0f, -20f);
            footerText = CreateText(notice.transform, "NoticeText",
                "LOCAL MOCKUP  ·  SAMPLE DATA ONLY", 16f, AccentDark,
                TextAlignmentOptions.MidlineLeft, true);
            SetStretch(footerText.rectTransform, 0f, 0f, 0f, 0f);
            footerText.overflowMode = TextOverflowModes.Overflow;
            footerText.textWrappingMode = TextWrappingModes.NoWrap;
            footerText.enableAutoSizing = true;
            footerText.fontSizeMin = 10f;
            footerText.fontSizeMax = 16f;

            RenderPage(wireEvents);
            ApplySlicedImages(transform);
        }

        private void BuildGlobalNavigation(bool wireEvents)
        {
            GameObject nav = new GameObject("GlobalNavigation", typeof(RectTransform));
            nav.transform.SetParent(generatedRoot, false);
            SetRect(nav.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(1f, 1f), new Vector2(-4f, -16f), new Vector2(860f, 50f));

            HorizontalLayoutGroup layout = nav.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.MiddleRight;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            CreateButton(nav.transform, "Nav_Challenges", "Challenges", 142f, 42f,
                currentPage == Page.Challenges ? Accent : PaperAlt,
                wireEvents ? () => ChangePage(Page.Challenges) : null, 18f, currentPage == Page.Challenges);
            CreateButton(nav.transform, "Nav_WordLibrary", "Word List", 154f, 42f,
                currentPage == Page.Library ? Accent : PaperAlt,
                wireEvents ? () => ChangePage(Page.Library) : null, 18f, currentPage == Page.Library);
            CreateButton(nav.transform, "Nav_Community", "Community", 142f, 42f,
                currentPage == Page.Community ? Accent : PaperAlt,
                wireEvents ? () => ChangePage(Page.Community) : null, 18f, currentPage == Page.Community);
            CreateButton(nav.transform, "Nav_Achievements", "Achievements", 160f, 42f,
                currentPage == Page.Achievements ? Accent : PaperAlt,
                wireEvents ? () => ChangePage(Page.Achievements) : null, 18f, currentPage == Page.Achievements);
            CreateButton(nav.transform, "ResetMockData", "Reset mock data", 150f, 42f,
                PaperAlt, wireEvents ? ResetMockData : null, 15f, false);
        }

        private void ResetMockData()
        {
            CloseDialog();
            store = MockupWordListStore.CreateWithFixtures();
            query = new MockupLibraryQuery();
            currentPage = Page.Library;
            achievementFilter = AchievementFilter.All;
            selectedListId = null;
            selectedDiscoveryId = null;
            librarySearchText = string.Empty;
            communitySearchText = string.Empty;
            lastActionMessage = "Mock data reset. No game data was changed.";
            collapsedGroups.Clear();
            editDraft = null;
            editingListId = null;
            editTitleInput = null;
            editDescriptionInput = null;
            newListTitleInput = null;
            newListDescriptionInput = null;
            newListWordsInput = null;
            newListTagsInput = null;
            editWordsInput = null;
            editTagsInput = null;
            dialogValidationText = null;
            newListLanguage = MockupLanguage.English;
            editLanguage = MockupLanguage.English;
            copyEditingMode = false;
            editWordsReadOnly = false;
            BuildView(true);
        }

        private void ChangePage(Page page)
        {
            currentPage = page;
            lastActionMessage = string.Empty;
            BuildView(true);
        }

        private void RenderPage(bool wireEvents)
        {
            ClearChildren(pageRoot);
            UpdateFooter();

            switch (currentPage)
            {
                case Page.Challenges:
                    BuildChallengesPage(wireEvents);
                    break;
                case Page.Community:
                    BuildCommunityPage(wireEvents);
                    break;
                case Page.Achievements:
                    BuildAchievementsPage(wireEvents);
                    break;
                default:
                    BuildLibraryPage(wireEvents);
                    break;
            }
        }

        private void BuildLibraryPage(bool wireEvents)
        {
            CreateButton(pageRoot, "View_All", "All Lists", 112f, 40f,
                query.View == MockupLibraryView.All ? Accent : PaperAlt,
                wireEvents ? () => SetLibraryView(MockupLibraryView.All) : null,
                18f, query.View == MockupLibraryView.All, new Vector2(-642f, 267f));
            CreateButton(pageRoot, "View_Favorites", "★ Favorites", 134f, 40f,
                query.View == MockupLibraryView.Favorites ? Accent : PaperAlt,
                wireEvents ? () => SetLibraryView(MockupLibraryView.Favorites) : null,
                18f, query.View == MockupLibraryView.Favorites, new Vector2(-510f, 267f));

            TMP_InputField search = CreateInputField(pageRoot, "LibrarySearch", "Search your lists…",
                330f, 42f, new Vector2(-267f, 267f), wireEvents ? HandleSearchChanged : null);
            search.SetTextWithoutNotify(librarySearchText);

            CreateDropdown(pageRoot, "Filter_Language",
                new[] { "Language: All", "Language: English", "Language: Chinese", "Language: Mixed" },
                (int)query.LanguageFilter, 148f, 40f, new Vector2(10f, 267f),
                wireEvents ? SetLanguageFilterFromDropdown : null);
            CreateDropdown(pageRoot, "Filter_Played",
                new[] { "Played: All", "Played: Yes", "Played: No" },
                (int)query.PlayedFilter, 142f, 40f, new Vector2(165f, 267f),
                wireEvents ? SetPlayedFilterFromDropdown : null);
            CreateDropdown(pageRoot, "Filter_Sort",
                new[] { "Sort: Recently played", "Sort: Name", "Sort: Date added", "Sort: Date modified", "Sort: Word count", "Sort: Personal stars" },
                (int)query.SortMode, 188f, 40f, new Vector2(345f, 267f),
                wireEvents ? SetSortModeFromDropdown : null);
            CreateDropdown(pageRoot, "Filter_Direction",
                new[] { "Ascending", "Descending" },
                (int)query.SortDirection, 120f, 40f, new Vector2(503f, 267f),
                wireEvents ? SetSortDirectionFromDropdown : null);
            CreateButton(pageRoot, "CreateList", "+ New List", 142f, 40f,
                AccentDark, wireEvents ? OpenNewListDialog : null, 17f, true,
                new Vector2(645f, 267f));

            GameObject listPane = CreatePanel("LibraryListPane", pageRoot, White);
            SetRect(listPane.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(-380f, -30f), new Vector2(740f, 510f));
            CreateText(listPane.transform, "ListPaneTitle", "YOUR COLLECTION", 18f, MutedInk,
                TextAlignmentOptions.MidlineLeft, true, new Vector2(0f, 226f), new Vector2(690f, 34f));

            Transform listContent = CreateScrollContent(listPane.transform, "LibraryScroll", 710f, 445f,
                new Vector2(0f, -22f));
            libraryListContent = listContent;
            libraryScrollRect = GetScrollRectForContent(listContent);
            RenderLibraryGroups(listContent, wireEvents);

            GameObject detailPane = CreatePanel("LibraryDetailPane", pageRoot, Paper);
            SetRect(detailPane.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(380f, -30f), new Vector2(740f, 510f));
            libraryDetailPane = detailPane.transform;
            RenderLibraryDetail(detailPane.transform, wireEvents);
        }

        private void RenderLibraryGroups(Transform content, bool wireEvents)
        {
            IReadOnlyList<MockupWordList> items = store.QueryLibrary(query);
            if (!items.Any())
            {
                CreateText(content, "EmptyLibrary", "No lists match these filters.", 18f, MutedInk,
                    TextAlignmentOptions.Center, false, Vector2.zero, new Vector2(650f, 70f));
                return;
            }

            foreach (MockupLibraryGroup group in Enum.GetValues(typeof(MockupLibraryGroup)))
            {
                List<MockupWordList> groupItems = items.Where(item => item.Group == group).ToList();
                bool collapsed = collapsedGroups.Contains(group);
                Button header = CreateButton(content, "Group_" + group, GroupLabel(group) +
                        "  ·  " + groupItems.Count + (collapsed ? "    >" : "    v"),
                    680f, 38f, PaperAlt,
                    wireEvents ? () => ToggleGroup(group) : null, 17f, true);
                LayoutElement headerLayout = header.GetComponent<LayoutElement>();
                headerLayout.preferredHeight = 38f;
                headerLayout.minHeight = 38f;

                if (collapsed)
                    continue;

                foreach (MockupWordList item in groupItems)
                {
                    CreateLibraryItemRow(content, item, item.Id == selectedListId, wireEvents);
                }
            }
        }

        private void CreateLibraryItemRow(Transform parent, MockupWordList item, bool selected, bool wireEvents)
        {
            Color fill = selected ? new Color(0.97f, 0.90f, 0.82f, 1f) : White;
            Button button = CreateButton(parent, "LibraryItem_" + item.Id, "", 680f, 76f, fill,
                wireEvents ? () => SelectLibraryItem(item.Id) : null, 14f, false);
            LayoutElement layout = button.GetComponent<LayoutElement>();
            layout.preferredHeight = 76f;
            layout.minHeight = 76f;

            string favorite = item.IsFavorite ? "★" : "☆";
            CreateText(button.transform, "FavoriteMark", favorite, 21f, AccentDark,
                TextAlignmentOptions.Center, false, new Vector2(-310f, 0f), new Vector2(34f, 34f));
            CreateText(button.transform, "ListTitle", item.Title, 20f, Ink,
                TextAlignmentOptions.MidlineLeft, true, new Vector2(-35f, 15f), new Vector2(450f, 30f));
            CreateText(button.transform, "ListMetadata",
                LanguageLabel(item.Language) + "  ·  " + item.WordCount + " words  ·  " + RecordLabel(item),
                16f, MutedInk, TextAlignmentOptions.MidlineLeft, false,
                new Vector2(-35f, -16f), new Vector2(450f, 24f));
            string badge = ListBadge(item);
            if (!string.IsNullOrEmpty(badge))
                CreateText(button.transform, "ListBadge", badge.ToUpperInvariant(), 13f, AccentDark,
                    TextAlignmentOptions.MidlineRight, true, new Vector2(264f, -16f), new Vector2(132f, 22f));
        }

        private void RenderLibraryDetail(Transform pane, bool wireEvents)
        {
            ClearChildren(pane);

            IReadOnlyList<MockupWordList> visibleItems = store.QueryLibrary(query);
            MockupWordList item = store.FindById(selectedListId);
            if (item == null || !visibleItems.Any(candidate => candidate.Id == selectedListId))
            {
                item = visibleItems.FirstOrDefault();
                selectedListId = item != null ? item.Id : null;
            }

            if (item == null)
            {
                CreateText(pane, "NoSelection", "Choose a list to see its details.", 18f, MutedInk,
                    TextAlignmentOptions.Center, false, Vector2.zero, new Vector2(620f, 68f));
                return;
            }

            bool useCjk = item.Language == MockupLanguage.Chinese || item.Language == MockupLanguage.Mixed;
            Transform header = CreatePanel("DetailHeaderPanel", pane, PaperAlt).transform;
            SetRect(header.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0f, 160f), new Vector2(680f, 124f));

            CreateText(header, "DetailEyebrow", "LIST DETAILS", 13f, MutedInk,
                TextAlignmentOptions.MidlineLeft, true, new Vector2(0f, 47f), new Vector2(616f, 22f));
            TextMeshProUGUI detailTitle = CreateText(header, "DetailTitle", item.Title, 27f, Ink,
                TextAlignmentOptions.MidlineLeft, true, new Vector2(0f, 20f), new Vector2(616f, 38f),
                useCjk: useCjk);
            detailTitle.enableAutoSizing = true;
            detailTitle.fontSizeMin = 21f;
            detailTitle.fontSizeMax = 27f;
            detailTitle.textWrappingMode = TextWrappingModes.NoWrap;
            detailTitle.overflowMode = TextOverflowModes.Ellipsis;

            TextMeshProUGUI detailDescription = CreateText(header, "DetailDescription",
                string.IsNullOrWhiteSpace(item.Description)
                    ? "A word list ready for your next practice session."
                    : item.Description,
                16f, MutedInk, TextAlignmentOptions.TopLeft, false,
                new Vector2(0f, -7f), new Vector2(616f, 36f), useCjk: useCjk);
            detailDescription.textWrappingMode = TextWrappingModes.Normal;
            detailDescription.overflowMode = TextOverflowModes.Ellipsis;

            string author = item.IsReadOnly
                ? (string.IsNullOrEmpty(item.RemoteAuthor) ? "COMMUNITY MEMBER" : item.RemoteAuthor)
                : "YOU";
            string metadata = "BY " + author
                + "   ·   " + GroupLabel(item.Group).ToUpperInvariant()
                + "   ·   " + LanguageLabel(item.Language)
                + "   ·   " + item.WordCount + " WORDS"
                + "   ·   REVISION " + item.ContentRevision;
            CreateText(header, "DetailMetadata", metadata, 13f, AccentDark,
                TextAlignmentOptions.MidlineLeft, true, new Vector2(0f, -44f), new Vector2(616f, 22f));

            Transform recordPanel = CreatePanel("DetailRecordPanel", pane, new Color(0.90f, 0.91f, 0.82f, 1f)).transform;
            SetRect(recordPanel.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(-170f, -38f), new Vector2(320f, 220f));
            CreateText(recordPanel, "RecordHeader", "YOUR BEST RUN", 14f, MutedInk,
                TextAlignmentOptions.MidlineLeft, true, new Vector2(0f, 75f), new Vector2(280f, 22f));
            CreateText(recordPanel, "RecordStarsLabel", "STARS", 12f, MutedInk,
                TextAlignmentOptions.MidlineLeft, true, new Vector2(0f, 48f), new Vector2(280f, 18f));
            int earnedStars = Mathf.Clamp(item.PersonalStars ?? 0, 0, 5);
            string stars = new string('★', earnedStars) + new string('☆', 5 - earnedStars);
            CreateText(recordPanel, "RecordStars", stars, 29f, AccentDark,
                TextAlignmentOptions.MidlineLeft, true, new Vector2(0f, 17f), new Vector2(280f, 48f));
            CreateText(recordPanel, "RecordTimeLabel", "BEST TIME", 12f, MutedInk,
                TextAlignmentOptions.MidlineLeft, true, new Vector2(0f, -18f), new Vector2(280f, 18f));
            string time = item.BestTimeSeconds.HasValue
                ? item.BestTimeSeconds.Value.ToString("0.0") + " s"
                : "No run yet";
            CreateText(recordPanel, "RecordTime", time, 24f, Ink,
                TextAlignmentOptions.MidlineLeft, true, new Vector2(0f, -48f), new Vector2(280f, 40f));
            CreateText(recordPanel, "RecordNote",
                item.PreviousContentRecords.Count > 0
                    ? "Earlier content version records are archived."
                    : item.HasBeenPlayed ? "Best record for this content version." : "Finish a run to set your record.",
                12f, MutedInk, TextAlignmentOptions.BottomLeft, false,
                new Vector2(0f, -87f), new Vector2(280f, 22f));

            Transform wordsPanel = CreatePanel("DetailWordsPanel", pane, White).transform;
            SetRect(wordsPanel.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(170f, -38f), new Vector2(320f, 220f));
            CreateText(wordsPanel, "DetailWordsLabel", "WORDS  ·  " + item.WordCount, 14f, MutedInk,
                TextAlignmentOptions.MidlineLeft, true, new Vector2(0f, 75f), new Vector2(280f, 22f));
            Transform wordsContent = CreateScrollContent(wordsPanel, "DetailWordsScroll", 294f, 156f,
                new Vector2(0f, -18f));
            string words = item.Words == null || item.Words.Count == 0
                ? "No words in this list."
                : string.Join("\n", item.Words);
            TextMeshProUGUI wordsText = CreateText(wordsContent, "DetailWords", words, 16f, Ink,
                TextAlignmentOptions.TopLeft, false,
                useCjk: useCjk);
            wordsText.overflowMode = TextOverflowModes.Overflow;
            wordsText.textWrappingMode = TextWrappingModes.Normal;
            ContentSizeFitter wordsTextFitter = wordsText.gameObject.AddComponent<ContentSizeFitter>();
            wordsTextFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            wordsTextFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            string detailStatus = ListBadge(item);
            string versionNote = item.PreviousContentRecords.Count > 0
                ? "Current words are a new version; earlier records are kept separately."
                : item.HasBeenPlayed ? "Personal best for the current content version." : "Finish a run to add a personal record.";
            detailStatus = string.IsNullOrEmpty(detailStatus)
                ? versionNote
                : detailStatus + "  ·  " + versionNote;
            CreateText(pane, "DetailStatus", detailStatus, 12f, Sage,
                TextAlignmentOptions.MidlineLeft, false, new Vector2(0f, -159f), new Vector2(650f, 18f));

            CreateButton(pane, "Action_Favorite", item.IsFavorite ? "★ Saved" : "☆ Save",
                126f, 40f, PaperAlt, wireEvents ? () => ToggleFavorite(item.Id) : null,
                15f, item.IsFavorite, new Vector2(-248f, -187f));
            bool publicationIsActive = item.IsPubliclyVisible
                || item.PublicationStatus == MockupPublicationStatus.Published
                || item.PublicationStatus == MockupPublicationStatus.UpdateNeeded;
            if (!publicationIsActive)
            {
                CreateButton(pane, "Action_DeleteLocal",
                    item.IsReadOnly ? "Remove download" : "Delete local",
                    126f, 40f, PaperAlt,
                    wireEvents ? () => RequestDeleteList(item.Id) : null,
                    13f, false, new Vector2(-110f, -187f));
            }
            else
            {
                CreateText(pane, "DeleteLocalHint", "Unpublish before deleting",
                    12f, MutedInk, TextAlignmentOptions.Center, false,
                    new Vector2(-110f, -187f), new Vector2(150f, 28f));
            }
            CreateButton(pane, "Action_PlayLibrary", "Play", 172f, 38f, AccentDark,
                wireEvents ? () => BeginLibraryMockPlay(item.Id) : null,
                15f, true, new Vector2(-185f, -231f));

            bool hasPublishedBinding = item.Ownership == MockupOwnership.Owned
                && !string.IsNullOrEmpty(item.RemoteItemId);
            string editLabel = item.IsReadOnly ? "Make an editable copy" :
                hasPublishedBinding ? "Edit details" : "Edit list";
            CreateButton(pane, "Action_Edit", editLabel, item.IsReadOnly ? 205f : 132f, 40f,
                Accent, wireEvents ? () => EditOrCopy(item.Id) : null, 14f, true,
                item.IsReadOnly ? new Vector2(70f, -187f) : new Vector2(35f, -187f));

            if (!item.IsReadOnly)
            {
                bool needsUpdate = item.PublicationStatus == MockupPublicationStatus.UpdateNeeded;
                bool isPublished = item.PublicationStatus == MockupPublicationStatus.Published;
                bool isUnpublished = item.PublicationStatus == MockupPublicationStatus.Unpublished;
                string publishLabel = needsUpdate ? "Update publication" :
                    isPublished ? "Published" : isUnpublished ? "Publish again" : "Publish list";
                Button publishButton = CreateButton(pane, "Action_Publish", publishLabel, 170f, 40f,
                    needsUpdate ? AccentDark : PaperAlt,
                    wireEvents && !isPublished
                        ? () => PublishOrUpdate(item.Id, item.PublicationStatus)
                        : null,
                    13f, needsUpdate, new Vector2(260f, -187f));
                publishButton.interactable = !isPublished || needsUpdate;

                if (hasPublishedBinding)
                {
                    CreateButton(pane, "Action_CopyEditWords", "Copy & edit words", 205f, 38f,
                        Accent, wireEvents ? () => OpenEditDialog(item.Id, true) : null,
                        13f, true, new Vector2(35f, -231f));
                    if (isPublished || needsUpdate)
                    {
                        CreateButton(pane, "Action_Unpublish", "Unpublish", 170f, 38f,
                            PaperAlt, wireEvents ? () => Unpublish(item.Id) : null,
                            13f, false, new Vector2(260f, -231f));
                    }
                }
            }
        }

        private void BuildCommunityPage(bool wireEvents)
        {
            CreateText(pageRoot, "CommunityHeading", "DISCOVER COMMUNITY LISTS", 18f, MutedInk,
                TextAlignmentOptions.MidlineLeft, true, new Vector2(-540f, 267f), new Vector2(420f, 36f));
            CreateDropdown(pageRoot, "MockConnectionState",
                new[] { "Mock network: Online", "Mock network: Offline" },
                store.IsOnline ? 0 : 1, 210f, 40f, new Vector2(0f, 267f),
                wireEvents ? SetMockOnlineFromDropdown : null);
            TMP_InputField search = CreateInputField(pageRoot, "CommunitySearch", "Search shared lists…",
                360f, 42f, new Vector2(480f, 267f), wireEvents ? HandleCommunitySearchChanged : null);
            search.SetTextWithoutNotify(communitySearchText);

            GameObject listPane = CreatePanel("CommunityListPane", pageRoot, White);
            SetRect(listPane.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(-460f, -30f), new Vector2(580f, 510f));
            CreateText(listPane.transform, "CommunityListTitle", "POPULAR THIS WEEK", 17f, MutedInk,
                TextAlignmentOptions.MidlineLeft, true, new Vector2(0f, 226f), new Vector2(520f, 34f));
            Transform content = CreateScrollContent(listPane.transform, "CommunityScroll", 550f, 445f,
                new Vector2(0f, -22f));
            communityListContent = content;
            RenderDiscoveryRows(content, wireEvents);

            GameObject detailPane = CreatePanel("CommunityDetailPane", pageRoot, Paper);
            SetRect(detailPane.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(320f, -30f), new Vector2(900f, 510f));
            communityDetailPane = detailPane.transform;
            RenderDiscoveryDetail(detailPane.transform, wireEvents);
        }

        private void RenderDiscoveryRows(Transform content, bool wireEvents)
        {
            IEnumerable<MockupDiscoveryItem> items = store.VisibleDiscoveryItems;
            if (!string.IsNullOrWhiteSpace(communitySearchText))
            {
                string term = communitySearchText.Trim();
                items = items.Where(item => Contains(item.Title, term) || Contains(item.Description, term));
            }

            List<MockupDiscoveryItem> results = items.ToList();
            if (string.IsNullOrEmpty(selectedDiscoveryId) || !results.Any(item => item.RemoteItemId == selectedDiscoveryId))
                selectedDiscoveryId = results.FirstOrDefault() != null ? results[0].RemoteItemId : null;

            if (results.Count == 0)
            {
                CreateText(content, "EmptyCommunity", "No shared lists match this search.", 17f, MutedInk,
                    TextAlignmentOptions.Center, false, Vector2.zero, new Vector2(500f, 70f));
                return;
            }

            foreach (MockupDiscoveryItem item in results)
            {
                bool selected = item.RemoteItemId == selectedDiscoveryId;
                Button row = CreateButton(content, "DiscoveryItem_" + item.RemoteItemId,
                    "", 520f, 92f, selected ? new Color(0.97f, 0.90f, 0.82f, 1f) : White,
                    wireEvents ? () => SelectDiscoveryItem(item.RemoteItemId) : null, 14f, false);
                LayoutElement layout = row.GetComponent<LayoutElement>();
                layout.preferredHeight = 92f;
                layout.minHeight = 92f;
                CreateText(row.transform, "DiscoveryTitle", item.Title, 19f, Ink,
                    TextAlignmentOptions.MidlineLeft, true, new Vector2(-35f, 20f), new Vector2(430f, 28f));
                CreateText(row.transform, "DiscoveryMeta",
                    LanguageLabel(item.Language) + "  ·  " + item.WordCount + " words  ·  " + item.LikeCount + " likes",
                    16f, MutedInk, TextAlignmentOptions.MidlineLeft, false,
                    new Vector2(-35f, -14f), new Vector2(430f, 24f));
                string status = item.Status == MockupDiscoveryStatus.New ? "NEW" : "UPDATED";
                CreateText(row.transform, "DiscoveryStatus", status, 13f, AccentDark,
                    TextAlignmentOptions.MidlineRight, true, new Vector2(204f, -34f), new Vector2(130f, 18f));
            }
        }

        private void RenderDiscoveryDetail(Transform pane, bool wireEvents)
        {
            ClearChildren(pane);
            CreateText(pane, "DiscoveryEyebrow", "COMMUNITY WORD LIST", 15f, MutedInk,
                TextAlignmentOptions.MidlineLeft, true, new Vector2(0f, 222f), new Vector2(800f, 24f));
            MockupDiscoveryItem item = store.FindDiscoveryItem(selectedDiscoveryId);
            if (item == null)
            {
                CreateText(pane, "NoDiscoverySelection", "Choose a shared list to see its details.",
                    18f, MutedInk, TextAlignmentOptions.Center, false, Vector2.zero, new Vector2(700f, 70f));
                return;
            }

            CreateText(pane, "DiscoveryDetailTitle", item.Title, 30f, Ink,
                TextAlignmentOptions.MidlineLeft, true, new Vector2(0f, 162f), new Vector2(760f, 52f));
            CreateText(pane, "DiscoveryDetailDesc", item.Description, 18f, MutedInk,
                TextAlignmentOptions.TopLeft, false, new Vector2(0f, 106f), new Vector2(760f, 62f));
            CreateText(pane, "DiscoveryAuthor", "PUBLISHED BY  " + item.RemoteAuthor, 15f, AccentDark,
                TextAlignmentOptions.MidlineLeft, true, new Vector2(0f, 51f), new Vector2(760f, 24f));

            CreatePanel("DiscoverySample", pane, new Color(0.90f, 0.91f, 0.82f, 1f));
            Transform sample = pane.Find("DiscoverySample");
            SetRect(sample.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0f, -30f), new Vector2(760f, 138f));
            CreateText(sample, "SampleLabel", "SAMPLE CONTENT  ·  " + LanguageLabel(item.Language) +
                    "  ·  " + item.WordCount + " words", 14f, MutedInk,
                TextAlignmentOptions.MidlineLeft, true, new Vector2(0f, 42f), new Vector2(700f, 24f));
            string sampleWords = item.Words == null || item.Words.Count == 0
                ? string.Empty
                : string.Join("    ", item.Words.Take(8));
            CreateText(sample, "SampleWords", sampleWords,
                22f, Ink, TextAlignmentOptions.MidlineLeft, true,
                new Vector2(0f, 0f), new Vector2(700f, 48f),
                item.Language == MockupLanguage.Chinese || item.Language == MockupLanguage.Mixed);

            CreateText(pane, "CommunityTrustNote", "Download adds a read-only copy to your library. It does not favorite the list.",
                16f, MutedInk, TextAlignmentOptions.MidlineLeft, false,
                new Vector2(0f, -122f), new Vector2(760f, 28f));

            CreateButton(pane, "Action_Like", (item.IsLiked ? "♥ Liked" : "♡ Like") + "  ·  " + item.LikeCount,
                172f, 44f, item.IsLiked ? Accent : PaperAlt,
                wireEvents ? () => ToggleLike(item.RemoteItemId) : null, 15f, item.IsLiked,
                new Vector2(-252f, -172f));
            Button download = CreateButton(pane, "Action_Download",
                item.IsDownloaded ? "In Community Downloads" : "Download to Community Downloads",
                250f, 44f, item.IsDownloaded ? Sage : AccentDark,
                wireEvents ? () => DownloadDiscoveryItem(item.RemoteItemId) : null,
                16f, true, new Vector2(30f, -172f));
            download.interactable = !item.IsDownloaded;
            CreateButton(pane, "Action_Play", "Play", 172f, 44f, AccentDark,
                wireEvents ? () => BeginMockPlay(item.RemoteItemId) : null,
                16f, true, new Vector2(260f, -172f));
            CreateButton(pane, "Action_Report", "Report & hide for me", 190f, 40f, PaperAlt,
                wireEvents ? () => ReportAndHideDiscoveryItem(item.RemoteItemId) : null,
                14f, false, new Vector2(0f, -222f));
        }

        private void BuildAchievementsPage(bool wireEvents)
        {
            CreateText(pageRoot, "AchievementsHeading", "ACHIEVEMENTS", 18f, MutedInk,
                TextAlignmentOptions.MidlineLeft, true, new Vector2(-600f, 267f), new Vector2(320f, 38f));
            CreateText(pageRoot, "AchievementsCaption", "SAMPLE PROGRESS  ·  LOCAL PREVIEW", 15f, MutedInk,
                TextAlignmentOptions.MidlineRight, false, new Vector2(480f, 267f), new Vector2(340f, 38f));

            CreateDropdown(pageRoot, "AchievementFilter",
                new[] { "Show: All", "Show: Unlocked", "Show: In progress" },
                (int)achievementFilter, 210f, 38f, new Vector2(0f, 216f),
                wireEvents ? SetAchievementFilterFromDropdown : null);

            IEnumerable<MockupAchievement> filtered = store.Achievements;
            if (achievementFilter == AchievementFilter.Unlocked)
                filtered = filtered.Where(item => item.Status == MockupAchievementStatus.Unlocked);
            else if (achievementFilter == AchievementFilter.InProgress)
                filtered = filtered.Where(item => item.Status == MockupAchievementStatus.InProgress);

            List<MockupAchievement> achievements = filtered.ToList();
            float cardWidth = achievements.Count == 1 ? 440f : 460f;
            float gap = 18f;
            float total = achievements.Count * cardWidth + Mathf.Max(0, achievements.Count - 1) * gap;
            float startX = -total * 0.5f + cardWidth * 0.5f;
            for (int index = 0; index < achievements.Count; index++)
            {
                float cardX = startX + index * (cardWidth + gap);
                BuildAchievementCard(achievements[index], cardX, cardWidth);
            }
        }

        private void BuildAchievementCard(MockupAchievement achievement, float x, float width)
        {
            Color fill = achievement.Status == MockupAchievementStatus.Unlocked ?
                new Color(0.93f, 0.93f, 0.84f, 1f) : White;
            GameObject cardView = CreatePanel("AchievementCard_" + achievement.Id, pageRoot, fill);
            SetRect(cardView.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(x, -26f), new Vector2(width, 340f));
            Image iconImage = CreateImage(cardView.transform, "AchievementIcon", AchievementIcon(achievement),
                new Vector2(-width * 0.5f + 72f, 16f), new Vector2(88f, 88f));
            iconImage.color = achievement.Status == MockupAchievementStatus.Unlocked
                ? Color.white
                : achievement.Status == MockupAchievementStatus.Locked
                    ? new Color(0.72f, 0.72f, 0.72f, 1f)
                    : new Color(0.90f, 0.84f, 0.67f, 1f);
            CreateText(cardView.transform, "AchievementState",
                achievement.Status.ToString().Replace("InProgress", "In Progress").ToUpperInvariant(),
                12f, achievement.Status == MockupAchievementStatus.Unlocked ? Sage : AccentDark,
                TextAlignmentOptions.MidlineLeft, true, new Vector2(62f, 115f),
                new Vector2(width - 164f, 24f));
            CreateText(cardView.transform, "AchievementTitle", achievement.Title, 20f, Ink,
                TextAlignmentOptions.TopLeft, true, new Vector2(64f, 68f),
                new Vector2(width - 164f, 54f));
            CreateText(cardView.transform, "AchievementDescription", achievement.Description, 16f, MutedInk,
                TextAlignmentOptions.TopLeft, false, new Vector2(64f, -8f),
                new Vector2(width - 164f, 88f));
            CreateText(cardView.transform, "AchievementProgress", achievement.ProgressText, 18f,
                achievement.Status == MockupAchievementStatus.Unlocked ? Sage : AccentDark,
                TextAlignmentOptions.MidlineLeft, true, new Vector2(64f, -126f),
                new Vector2(width - 164f, 32f));
        }

        private Sprite AchievementIcon(MockupAchievement achievement)
        {
            bool unlocked = achievement.Status == MockupAchievementStatus.Unlocked;
            switch (achievement.Id)
            {
                case "COMPLETE_FIRST_WORD": return unlocked ? firstWordUnlockedIcon : firstWordLockedIcon;
                case "COMPLETE_TWO_WORDS": return unlocked ? twoWordsUnlockedIcon : twoWordsLockedIcon;
                case "COMPLETE_WORD_WITH_D": return unlocked ? wordWithDUnlockedIcon : wordWithDLockedIcon;
                default: return standardSprite;
            }
        }

        private void BuildChallengesPage(bool wireEvents)
        {
            GameObject panel = CreatePanel("ChallengesMockupPage", pageRoot, Paper);
            SetRect(panel.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900f, 400f));
            CreateText(panel.transform, "ChallengesMockupTitle", "CHALLENGE LEVELS", 30f, Ink,
                TextAlignmentOptions.Center, true, new Vector2(0f, 95f), new Vector2(780f, 54f));
            CreateText(panel.transform, "ChallengesMockupNote",
                "Challenge selection stays in the existing Levels screen.\nThis isolated preview focuses on your word library, community lists and achievements.",
                18f, MutedInk, TextAlignmentOptions.Center, false,
                new Vector2(0f, 5f), new Vector2(780f, 100f));
            CreateButton(panel.transform, "ChallengesBackToLibrary", "Open Word List",
                220f, 48f, AccentDark,
                wireEvents ? () => ChangePage(Page.Library) : null,
                16f, true, new Vector2(0f, -116f));
        }

        private void SetLibraryView(MockupLibraryView view)
        {
            query.View = view;
            RefreshLibraryPage(true);
        }

        private void HandleSearchChanged(string value)
        {
            librarySearchText = value ?? string.Empty;
            query.SearchText = librarySearchText;
            RefreshLibraryPage(true);
        }

        private void HandleCommunitySearchChanged(string value)
        {
            communitySearchText = value ?? string.Empty;
            RefreshCommunityPage();
        }

        private void SetLanguageFilterFromDropdown(int value)
        {
            query.LanguageFilter = (MockupLanguageFilter)Mathf.Clamp(value, 0,
                Enum.GetValues(typeof(MockupLanguageFilter)).Length - 1);
            lastActionMessage = "Language filter: " + FilterLabel(query.LanguageFilter);
            RefreshLibraryPage(true);
        }

        private void SetPlayedFilterFromDropdown(int value)
        {
            query.PlayedFilter = (MockupPlayedFilter)Mathf.Clamp(value, 0,
                Enum.GetValues(typeof(MockupPlayedFilter)).Length - 1);
            lastActionMessage = "Played filter: " + FilterLabel(query.PlayedFilter);
            RefreshLibraryPage(true);
        }

        private void SetSortModeFromDropdown(int value)
        {
            query.SortMode = (MockupSortMode)Mathf.Clamp(value, 0,
                Enum.GetValues(typeof(MockupSortMode)).Length - 1);
            lastActionMessage = "Sort by: " + SortLabel(query.SortMode);
            RefreshLibraryPage();
        }

        private void SetSortDirectionFromDropdown(int value)
        {
            query.SortDirection = (MockupSortDirection)Mathf.Clamp(value, 0,
                Enum.GetValues(typeof(MockupSortDirection)).Length - 1);
            lastActionMessage = "Sort order: " + query.SortDirection;
            RefreshLibraryPage();
        }

        private void ToggleGroup(MockupLibraryGroup group)
        {
            if (!collapsedGroups.Add(group))
                collapsedGroups.Remove(group);
            RefreshLibraryPage();
        }

        private void SelectLibraryItem(string mockId)
        {
            selectedListId = mockId;
            RefreshLibraryPage();
        }

        private void SelectDiscoveryItem(string remoteItemId)
        {
            selectedDiscoveryId = remoteItemId;
            RefreshCommunityPage();
        }

        private void ToggleFavorite(string mockId)
        {
            bool isFavorite = store.ToggleFavorite(mockId);
            lastActionMessage = isFavorite ? "Added to Favorites" : "Removed from Favorites";
            RefreshLibraryPage();
        }

        private void ToggleLike(string remoteId)
        {
            MockupActionResult result = store.TryToggleLike(remoteId);
            lastActionMessage = result.Succeeded
                ? (result.DiscoveryItem != null && result.DiscoveryItem.IsLiked
                    ? "Liked in local mock data. No live vote was sent."
                    : "Like removed in local mock data.")
                : result.Message + " (mock action; no live service was contacted.)";
            RefreshCommunityPage();
        }

        private void DownloadDiscoveryItem(string remoteId)
        {
            MockupActionResult result = store.TryDownload(remoteId);
            if (!result.Succeeded || result.List == null)
            {
                lastActionMessage = result.Message + " (mock action; no live service was contacted.)";
                RefreshCommunityPage();
                return;
            }

            MockupWordList downloaded = result.List;
            selectedListId = downloaded.Id;
            ClearFiltersThatHideDownloadedItem(downloaded);
            collapsedGroups.Remove(downloaded.Group);
            currentPage = Page.Library;
            lastActionMessage = "Added “" + downloaded.Title + "” to Community Downloads and cached it for offline use.";
            BuildView(true);
            ScheduleLibraryScrollRestore(0f, false, downloaded.Id);
        }

        private void ReportAndHideDiscoveryItem(string remoteId)
        {
            MockupActionResult result = store.TryReportAndHide(remoteId);
            if (!result.Succeeded)
            {
                lastActionMessage = result.Message + " (mock action; no live service was contacted.)";
                RefreshCommunityPage();
                return;
            }

            selectedDiscoveryId = null;
            lastActionMessage = "Hidden from your Community view; a report was simulated locally. No real report was sent.";
            RefreshCommunityPage();
        }

        private void SetMockOnlineFromDropdown(int value)
        {
            bool online = value == 0;
            store.SetOnline(online);
            lastActionMessage = online
                ? "Mock network is online. No live service is connected."
                : "Mock network is offline. Cached Play remains available; network actions will fail visibly.";
            RefreshCommunityPage();
        }

        private void BeginMockPlay(string remoteId)
        {
            ShowMockPlayResult(store.PlayDiscoveryItem(remoteId));
        }

        private void BeginLibraryMockPlay(string mockId)
        {
            ShowMockPlayResult(store.PlayLibraryItem(mockId));
        }

        private void ShowMockPlayResult(MockupPlayResult result)
        {
            if (!result.Succeeded || result.List == null)
            {
                OpenDialog("Mock play unavailable", 640f, 300f);
                CreateText(dialogContentRoot, "MockPlayUnavailableMessage",
                    result.Message + "\n\nThis is a local mockup. No live service or game was contacted.",
                    16f, MutedInk, TextAlignmentOptions.Center, false,
                    new Vector2(0f, 8f), new Vector2(540f, 112f));
                CreateButton(dialogContentRoot, "MockPlayUnavailableClose", "Close", 150f, 42f,
                    AccentDark, () =>
                    {
                        CloseDialog();
                        RefreshCurrentPage();
                    }, 15f, true, new Vector2(0f, -88f));
                return;
            }

            selectedListId = result.List.Id;
            lastActionMessage = result.Message;
            OpenDialog("Mock play · " + result.List.Title, 660f, 350f);
            CreateText(dialogContentRoot, "MockPlayExplanation",
                "This is a simulated play result only. No game scene will start.\n" +
                "The local list stays in its library section if you cancel.",
                16f, MutedInk, TextAlignmentOptions.Center, false,
                new Vector2(0f, 24f), new Vector2(560f, 104f));
            CreateButton(dialogContentRoot, "MockPlayComplete", "Complete mock run", 205f, 44f,
                AccentDark, () => CompleteMockRun(result.List.Id), 15f, true,
                new Vector2(125f, -92f));
            CreateButton(dialogContentRoot, "MockPlayCancel", "Cancel", 150f, 44f,
                PaperAlt, () =>
                {
                    lastActionMessage = "Mock play cancelled. The local list remains in its library section.";
                    CloseDialog();
                    RefreshCurrentPage();
                }, 15f, false, new Vector2(-125f, -92f));
        }

        private void CompleteMockRun(string mockId)
        {
            bool completed = store.CompleteRun(mockId, 3, 85f);
            MockupWordList item = store.FindById(mockId);
            CloseDialog();
            if (!completed || item == null)
            {
                lastActionMessage = "The local mock run could not be recorded.";
                RefreshCurrentPage();
                return;
            }

            lastActionMessage = "Mock run complete: 3 stars · 85.0 seconds. No game was launched.";
            OpenDialog("Mock run complete", 620f, 310f);
            CreateText(dialogContentRoot, "MockPlayResultMessage",
                "3 stars  ·  85.0 seconds\nThis result was recorded in mock data only.",
                17f, MutedInk, TextAlignmentOptions.Center, false,
                new Vector2(0f, 22f), new Vector2(520f, 88f));
            string viewListLabel = item.Group == MockupLibraryGroup.CommunityDownloads
                ? "View in Community Downloads"
                : "View in My Lists";
            CreateButton(dialogContentRoot, "MockPlayViewList", viewListLabel, 220f, 42f,
                AccentDark, () =>
                {
                    currentPage = Page.Library;
                    selectedListId = item.Id;
                    ClearFiltersThatHide(item);
                    CloseDialog();
                    RefreshLibraryPage(false, true);
                }, 15f, true, new Vector2(110f, -78f));
            CreateButton(dialogContentRoot, "MockPlayResultClose", "Close", 140f, 42f,
                PaperAlt, () =>
                {
                    CloseDialog();
                    RefreshCurrentPage();
                }, 15f, false, new Vector2(-110f, -78f));
        }

        private void EditOrCopy(string mockId)
        {
            MockupWordList item = store.FindById(mockId);
            if (item == null)
                return;

            if (item.IsReadOnly)
            {
                item = store.EditCommunityCopy(item.Id);
                selectedListId = item.Id;
                lastActionMessage = "Created your editable copy. The downloaded original remains unchanged.";
            }

            OpenEditDialog(item.Id);
        }

        private void PublishOrUpdate(string mockId, MockupPublicationStatus status)
        {
            MockupActionResult result = status == MockupPublicationStatus.UpdateNeeded
                ? store.TryUpdatePublication(mockId)
                : store.TryPublish(mockId);
            if (!result.Succeeded || result.List == null)
            {
                lastActionMessage = result.Message + " (mock action; no live service was contacted.)";
                RefreshLibraryPage();
                return;
            }

            selectedListId = result.List.Id;
            lastActionMessage = status == MockupPublicationStatus.UpdateNeeded
                ? "Updated the existing publication in mock data."
                : status == MockupPublicationStatus.Unpublished
                    ? "Published this list again in mock data."
                    : "Published this list in mock data.";
            RefreshLibraryPage();
        }

        private void Unpublish(string mockId)
        {
            MockupActionResult result = store.TryUnpublish(mockId);
            lastActionMessage = result.Succeeded
                ? "Removed this list from public discovery in mock data. Your local list remains in My Published."
                : result.Message + " (mock action; no live service was contacted.)";
            RefreshLibraryPage();
        }

        private void RequestDeleteList(string mockId)
        {
            MockupWordList item = store.FindById(mockId);
            if (item == null)
                return;

            bool publicationIsActive = item.IsPubliclyVisible
                || item.PublicationStatus == MockupPublicationStatus.Published
                || item.PublicationStatus == MockupPublicationStatus.UpdateNeeded;
            if (publicationIsActive)
            {
                lastActionMessage = "Unpublish this list before deleting its local copy. No remote action was taken.";
                RefreshLibraryPage();
                return;
            }

            List<MockupWordList> visibleItems = store.QueryLibrary(query).ToList();
            pendingDeleteVisibleIndex = visibleItems.FindIndex(candidate => candidate.Id == mockId);
            pendingDeleteListId = mockId;
            OpenDialog(item.IsReadOnly ? "Remove downloaded list?" : "Delete local list?", 620f, 300f);
            CreateText(dialogContentRoot, "DeleteLocalExplanation",
                item.IsReadOnly
                    ? "This removes the cached copy from Community Downloads. The community item remains available and can be downloaded again."
                    : "This removes the local list only. No published item will be deleted or changed.",
                18f, Ink, TextAlignmentOptions.Center, false,
                new Vector2(0f, 22f), new Vector2(540f, 110f));
            CreateButton(dialogContentRoot, "DeleteLocalCancel", "Cancel", 150f, 42f, PaperAlt,
                CancelDeleteList, 15f, false, new Vector2(-105f, -82f));
            CreateButton(dialogContentRoot, "DeleteLocalConfirm",
                item.IsReadOnly ? "Remove local copy" : "Delete local list",
                190f, 42f, AccentDark, ConfirmDeleteList, 14f, true,
                new Vector2(105f, -82f));
        }

        private void CancelDeleteList()
        {
            pendingDeleteListId = null;
            pendingDeleteVisibleIndex = -1;
            CloseDialog();
            UpdateFooter();
        }

        private void ConfirmDeleteList()
        {
            if (string.IsNullOrEmpty(pendingDeleteListId))
                return;

            string deletedId = pendingDeleteListId;
            int previousIndex = pendingDeleteVisibleIndex;
            MockupActionResult result = store.TryDeleteList(deletedId);
            pendingDeleteListId = null;
            pendingDeleteVisibleIndex = -1;
            CloseDialog();
            lastActionMessage = result.Message;

            if (result.Succeeded && selectedListId == deletedId)
            {
                IReadOnlyList<MockupWordList> remaining = store.QueryLibrary(query);
                if (remaining.Count == 0)
                {
                    selectedListId = null;
                }
                else
                {
                    int adjacentIndex = previousIndex < 0
                        ? 0
                        : Mathf.Min(previousIndex, remaining.Count - 1);
                    selectedListId = remaining[adjacentIndex].Id;
                }
            }

            RefreshLibraryPage();
        }

        private void SetAchievementFilter(AchievementFilter filter)
        {
            achievementFilter = filter;
            RenderPage(true);
        }

        private void SetAchievementFilterFromDropdown(int value)
        {
            SetAchievementFilter((AchievementFilter)Mathf.Clamp(value, 0,
                Enum.GetValues(typeof(AchievementFilter)).Length - 1));
        }

        private void RefreshLibraryPage(bool moveScrollToTop = false, bool revealSelectedItem = false)
        {
            if (currentPage != Page.Library)
                return;
            if (libraryListContent == null || libraryDetailPane == null)
            {
                BuildView(true);
                ScheduleLibraryScrollRestore(0f, true,
                    revealSelectedItem ? selectedListId : null);
                return;
            }
            float savedScrollY = libraryScrollRect != null && libraryScrollRect.content != null
                ? libraryScrollRect.content.anchoredPosition.y
                : 0f;
            string revealId = revealSelectedItem ? selectedListId : null;
            ClearChildren(libraryListContent);
            RenderLibraryGroups(libraryListContent, true);
            RenderLibraryDetail(libraryDetailPane, true);
            UpdateFooter();
            ScheduleLibraryScrollRestore(savedScrollY, moveScrollToTop, revealId);
        }

        private void ScheduleLibraryScrollRestore(float savedScrollY, bool moveToTop, string revealListId)
        {
            StopLibraryScrollRoutine();
            if (!Application.isPlaying)
            {
                RestoreLibraryScroll(savedScrollY, moveToTop, revealListId);
                return;
            }

            libraryScrollRoutine = StartCoroutine(RestoreLibraryScrollAfterLayout(
                savedScrollY, moveToTop, revealListId));
        }

        private IEnumerator RestoreLibraryScrollAfterLayout(
            float savedScrollY,
            bool moveToTop,
            string revealListId)
        {
            // ClearChildren uses deferred Destroy in Play Mode, so wait until those
            // rows are gone before measuring the new layout and restoring the offset.
            yield return null;
            RestoreLibraryScroll(savedScrollY, moveToTop, revealListId);
            libraryScrollRoutine = null;
        }

        private void RestoreLibraryScroll(float savedScrollY, bool moveToTop, string revealListId)
        {
            if (libraryListContent == null || libraryScrollRect == null
                || libraryScrollRect.content == null || libraryScrollRect.viewport == null)
                return;

            RectTransform contentRect = libraryScrollRect.content;
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);

            if (string.IsNullOrEmpty(revealListId))
            {
                Vector2 position = contentRect.anchoredPosition;
                position.y = moveToTop ? 0f : savedScrollY;
                contentRect.anchoredPosition = position;
            }
            else
            {
                ScrollLibraryItemIntoView(revealListId);
            }

            ClampLibraryScroll();
            libraryScrollRect.velocity = Vector2.zero;
        }

        private void ScrollLibraryItemIntoView(string listId)
        {
            if (libraryListContent == null || libraryScrollRect == null
                || libraryScrollRect.content == null || libraryScrollRect.viewport == null)
                return;

            Transform row = libraryListContent.Find("LibraryItem_" + listId);
            if (row == null)
                return;

            Bounds rowBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(
                libraryScrollRect.viewport, row);
            Rect viewportRect = libraryScrollRect.viewport.rect;
            Vector2 position = libraryScrollRect.content.anchoredPosition;
            if (rowBounds.max.y > viewportRect.yMax)
                position.y -= rowBounds.max.y - viewportRect.yMax;
            else if (rowBounds.min.y < viewportRect.yMin)
                position.y += viewportRect.yMin - rowBounds.min.y;
            libraryScrollRect.content.anchoredPosition = position;
        }

        private void ClampLibraryScroll()
        {
            if (libraryScrollRect == null || libraryScrollRect.content == null
                || libraryScrollRect.viewport == null)
                return;

            RectTransform content = libraryScrollRect.content;
            float maxScrollY = Mathf.Max(0f, content.rect.height - libraryScrollRect.viewport.rect.height);
            Vector2 position = content.anchoredPosition;
            position.y = Mathf.Clamp(position.y, 0f, maxScrollY);
            content.anchoredPosition = position;
        }

        private void StopLibraryScrollRoutine()
        {
            if (libraryScrollRoutine != null)
            {
                StopCoroutine(libraryScrollRoutine);
                libraryScrollRoutine = null;
            }
        }

        private void ClearFiltersThatHide(MockupWordList item)
        {
            if (item == null)
                return;

            bool viewHidesItem = query.View == MockupLibraryView.Favorites && !item.IsFavorite
                || query.View == MockupLibraryView.MyLists && item.Group != MockupLibraryGroup.MyLists
                || query.View == MockupLibraryView.CommunityDownloads && item.Group != MockupLibraryGroup.CommunityDownloads
                || query.View == MockupLibraryView.MyPublished && item.Group != MockupLibraryGroup.MyPublished;
            if (viewHidesItem)
                query.View = MockupLibraryView.All;

            if (!string.IsNullOrWhiteSpace(query.SearchText)
                && !Contains(item.Title, query.SearchText.Trim())
                && !Contains(item.Description, query.SearchText.Trim()))
                query.SearchText = string.Empty;
            librarySearchText = query.SearchText;

            if (query.LanguageFilter != MockupLanguageFilter.All
                && item.Language.ToString() != query.LanguageFilter.ToString())
                query.LanguageFilter = MockupLanguageFilter.All;

            if (query.PlayedFilter == MockupPlayedFilter.Played && !item.HasBeenPlayed
                || query.PlayedFilter == MockupPlayedFilter.Unplayed && item.HasBeenPlayed)
                query.PlayedFilter = MockupPlayedFilter.All;

            collapsedGroups.Remove(item.Group);
        }

        private void ClearFiltersThatHideDownloadedItem(MockupWordList item)
        {
            if (item == null)
                return;

            if (query.View == MockupLibraryView.Favorites && !item.IsFavorite)
                query.View = MockupLibraryView.All;

            if (query.LanguageFilter != MockupLanguageFilter.All
                && item.Language.ToString() != query.LanguageFilter.ToString())
                query.LanguageFilter = MockupLanguageFilter.All;

            if (query.PlayedFilter == MockupPlayedFilter.Played && !item.HasBeenPlayed
                || query.PlayedFilter == MockupPlayedFilter.Unplayed && item.HasBeenPlayed)
                query.PlayedFilter = MockupPlayedFilter.All;

            if (!string.IsNullOrWhiteSpace(query.SearchText)
                && !Contains(item.Title, query.SearchText.Trim())
                && !Contains(item.Description, query.SearchText.Trim()))
                query.SearchText = string.Empty;
            librarySearchText = query.SearchText;
            collapsedGroups.Remove(MockupLibraryGroup.CommunityDownloads);
        }

        private void RefreshCommunityPage()
        {
            if (currentPage != Page.Community)
                return;
            if (communityListContent == null || communityDetailPane == null)
            {
                BuildView(true);
                return;
            }
            ClearChildren(communityListContent);
            RenderDiscoveryRows(communityListContent, true);
            RenderDiscoveryDetail(communityDetailPane, true);
            UpdateFooter();
        }

        private void RefreshCurrentPage()
        {
            if (currentPage == Page.Library)
                RefreshLibraryPage();
            else if (currentPage == Page.Community)
                RefreshCommunityPage();
        }

        private void UpdateFooter()
        {
            if (footerText != null)
                footerText.text = string.IsNullOrEmpty(lastActionMessage)
                    ? "LOCAL MOCKUP  ·  SAMPLE DATA ONLY"
                    : "LOCAL MOCKUP  ·  " + lastActionMessage + "  ·  SAMPLE DATA ONLY";
        }

        private void OpenNewListDialog()
        {
            newListLanguage = MockupLanguage.English;
            OpenDialog("Create a word list", 680f, 700f);
            DrawNewListDialog();
        }

        private void DrawNewListDialog()
        {
            CreateText(dialogContentRoot, "NewListLabel", "List name", 15f, MutedInk,
                TextAlignmentOptions.MidlineLeft, true, new Vector2(0f, 263f), new Vector2(600f, 24f));
            newListTitleInput = CreateInputField(dialogContentRoot, "NewListTitleInput", "New word list",
                600f, 42f, new Vector2(0f, 221f), null);
            CreateText(dialogContentRoot, "NewListDescriptionLabel", "Description (optional)", 15f, MutedInk,
                TextAlignmentOptions.MidlineLeft, true, new Vector2(0f, 183f), new Vector2(600f, 24f));
            newListDescriptionInput = CreateInputField(dialogContentRoot, "NewListDescriptionInput",
                "Add a short description…", 600f, 48f, new Vector2(0f, 140f), null, true);
            CreateText(dialogContentRoot, "NewListTagsLabel", "Tags, comma-separated (optional)", 15f, MutedInk,
                TextAlignmentOptions.MidlineLeft, true, new Vector2(0f, 98f), new Vector2(600f, 24f));
            newListTagsInput = CreateInputField(dialogContentRoot, "NewListTagsInput", "travel, beginner, daily",
                600f, 40f, new Vector2(0f, 62f), null);
            CreateText(dialogContentRoot, "NewListLanguageLabel", "Language", 15f, MutedInk,
                TextAlignmentOptions.MidlineLeft, true, new Vector2(-180f, 19f), new Vector2(240f, 22f));
            CreateDropdown(dialogContentRoot, "NewListLanguage",
                new[] { "English", "Chinese", "Mixed" }, (int)newListLanguage,
                220f, 38f, new Vector2(190f, 19f), SetNewListLanguageFromDropdown);
            CreateText(dialogContentRoot, "NewListWordsLabel", "Words (one per line; at least one required)",
                15f, MutedInk, TextAlignmentOptions.MidlineLeft, true,
                new Vector2(0f, -25f), new Vector2(600f, 24f));
            newListWordsInput = CreateInputField(dialogContentRoot, "NewListWordsInput",
                "Type or paste one word per line…", 600f, 180f,
                new Vector2(0f, -134f), null, true);
            dialogValidationText = CreateText(dialogContentRoot, "NewListValidation", string.Empty, 14f,
                AccentDark, TextAlignmentOptions.MidlineLeft, false,
                new Vector2(0f, -246f), new Vector2(600f, 24f));
            CreateButton(dialogContentRoot, "NewListCancel", "Cancel", 140f, 42f, PaperAlt,
                CloseDialog, 15f, false, new Vector2(-105f, -306f));
            CreateButton(dialogContentRoot, "NewListCreate", "Create list", 170f, 42f, AccentDark,
                CreateNewList, 15f, true, new Vector2(105f, -306f));
        }

        private void SetNewListLanguageFromDropdown(int value)
        {
            newListLanguage = (MockupLanguage)Mathf.Clamp(value, 0,
                Enum.GetValues(typeof(MockupLanguage)).Length - 1);
        }

        private void CreateNewList()
        {
            string title = newListTitleInput != null ? newListTitleInput.text.Trim() : string.Empty;
            List<string> words = ParseWords(newListWordsInput != null ? newListWordsInput.text : string.Empty);
            if (string.IsNullOrWhiteSpace(title))
            {
                SetDialogValidation("Enter a list name.");
                return;
            }
            if (words.Count == 0)
            {
                SetDialogValidation("Enter at least one word. Blank lines are ignored.");
                return;
            }

            MockupWordList item = store.CreateList(title, newListLanguage, words);
            MockupWordListEdit details = store.CreateEditDraft(item.Id);
            details.Description = newListDescriptionInput != null ? newListDescriptionInput.text.Trim() : string.Empty;
            details.Tags = newListTagsInput != null ? newListTagsInput.text.Trim() : string.Empty;
            store.SaveEdit(item.Id, details);
            selectedListId = item.Id;
            ClearFiltersThatHide(item);
            communitySearchText = string.Empty;
            lastActionMessage = "Created “" + item.Title + "” with " + item.Words.Count + " words in My Lists.";
            CloseDialog();
            currentPage = Page.Library;
            RefreshLibraryPage(false, true);
        }

        private void OpenEditDialog(string mockId, bool copyPublishedContentAsNewList = false)
        {
            MockupWordList item = store.FindById(mockId);
            if (item == null || item.IsReadOnly)
                return;

            editingListId = item.Id;
            bool hasPublishedBinding = item.Ownership == MockupOwnership.Owned
                && !string.IsNullOrEmpty(item.RemoteItemId);
            copyEditingMode = copyPublishedContentAsNewList && hasPublishedBinding;
            editWordsReadOnly = hasPublishedBinding && !copyEditingMode;
            editDraft = store.CreateEditDraft(item.Id);
            if (editDraft.Words == null)
                editDraft.Words = item.Words == null ? new List<string>() : new List<string>(item.Words);
            editLanguage = item.Language;
            RenderEditDialog();
        }

        private void RenderEditDialog()
        {
            if (editDraft == null)
                return;
            editSaveButton = null;
            editSaveHintText = null;
            MockupWordList item = store.FindById(editingListId);
            string title = copyEditingMode
                ? "Create a new version from your published list"
                : item != null && !string.IsNullOrEmpty(item.RemoteItemId)
                    ? "Edit published list details"
                    : "Edit your word list";
            OpenDialog(title, 760f, 700f);
            CreateText(dialogContentRoot, "EditTitleLabel", "List name", 15f, MutedInk,
                TextAlignmentOptions.MidlineLeft, true, new Vector2(0f, 263f), new Vector2(640f, 24f));
            editTitleInput = CreateInputField(dialogContentRoot, "EditTitleInput", editDraft.Title,
                640f, 42f, new Vector2(0f, 221f), null);
            editTitleInput.text = editDraft.Title;

            CreateText(dialogContentRoot, "EditDescriptionLabel", "Description", 15f, MutedInk,
                TextAlignmentOptions.MidlineLeft, true, new Vector2(0f, 183f), new Vector2(640f, 24f));
            editDescriptionInput = CreateInputField(dialogContentRoot, "EditDescriptionInput", "Add a short description…",
                640f, 48f, new Vector2(0f, 140f), null, true);
            editDescriptionInput.text = editDraft.Description;

            CreateText(dialogContentRoot, "EditTagsLabel", "Tags, comma-separated (optional)", 15f, MutedInk,
                TextAlignmentOptions.MidlineLeft, true, new Vector2(0f, 98f), new Vector2(640f, 24f));
            editTagsInput = CreateInputField(dialogContentRoot, "EditTagsInput", "travel, beginner, daily",
                640f, 40f, new Vector2(0f, 62f), null);
            editTagsInput.text = editDraft.Tags ?? string.Empty;

            CreateText(dialogContentRoot, "EditLanguageLabel", "Language", 15f, MutedInk,
                TextAlignmentOptions.MidlineLeft, true, new Vector2(-190f, 19f), new Vector2(230f, 22f));
            TMP_Dropdown languageDropdown = CreateDropdown(dialogContentRoot, "EditLanguage",
                new[] { "English", "Chinese", "Mixed" }, (int)editLanguage,
                220f, 38f, new Vector2(190f, 19f), SetEditLanguageFromDropdown);
            languageDropdown.interactable = !editWordsReadOnly;

            CreateText(dialogContentRoot, "EditWordsLabel",
                editWordsReadOnly ? "Words (published content is fixed)" : "Words (one per line)",
                15f, MutedInk, TextAlignmentOptions.MidlineLeft, true,
                new Vector2(0f, -25f), new Vector2(640f, 24f));
            editWordsInput = CreateInputField(dialogContentRoot, "EditWordsInput",
                "One word per line…", 640f, 180f, new Vector2(0f, -134f), HandleEditWordsChanged, true);
            editWordsInput.text = WordsAsText(editDraft.Words);
            editWordsInput.interactable = !editWordsReadOnly;
            if (editWordsReadOnly)
            {
                CreateText(dialogContentRoot, "PublishedWordsHint",
                    "Use “Copy & edit words” to make a separate My List version. The published original stays unchanged.",
                    13f, MutedInk, TextAlignmentOptions.Center, false,
                    new Vector2(0f, -237f), new Vector2(640f, 18f));
            }
            else if (copyEditingMode)
            {
                editSaveHintText = CreateText(dialogContentRoot, "NewVersionHint",
                    "Change at least one word or the language to enable creating a separate version.",
                    12f, MutedInk, TextAlignmentOptions.MidlineLeft, false,
                    new Vector2(0f, -237f), new Vector2(640f, 20f));
            }
            dialogValidationText = CreateText(dialogContentRoot, "EditValidation", string.Empty, 14f,
                AccentDark, TextAlignmentOptions.MidlineLeft, false,
                new Vector2(0f, -264f), new Vector2(640f, 20f));
            CreateButton(dialogContentRoot, "EditCancel", "Cancel", 140f, 42f, PaperAlt,
                CloseDialog, 15f, false, new Vector2(-105f, -306f));
            editSaveButton = CreateButton(dialogContentRoot, "EditSave",
                copyEditingMode ? "Create new version" : "Save changes",
                190f, 42f, AccentDark, SaveEditClicked, 15f, true, new Vector2(105f, -306f));
            UpdateEditSaveButton();
        }

        private void SetEditLanguageFromDropdown(int value)
        {
            CaptureEditInputs();
            editLanguage = (MockupLanguage)Mathf.Clamp(value, 0,
                Enum.GetValues(typeof(MockupLanguage)).Length - 1);
            if (editDraft != null)
                editDraft.Language = editLanguage;
            RenderEditDialog();
        }

        private void HandleEditWordsChanged(string value)
        {
            CaptureEditInputs();
            if (dialogValidationText != null)
                dialogValidationText.text = string.Empty;
            UpdateEditSaveButton();
        }

        private void UpdateEditSaveButton()
        {
            if (editSaveButton == null)
                return;

            bool hasGameplayChange = EditHasGameplayChange();
            editSaveButton.interactable = !copyEditingMode || hasGameplayChange;
            if (editSaveHintText != null)
            {
                editSaveHintText.text = hasGameplayChange
                    ? "This creates an independent My List. The published original and its scores stay unchanged."
                    : "Change at least one word or the language to enable creating a separate version.";
            }
        }

        private void CaptureEditInputs()
        {
            if (editDraft == null)
                return;
            if (editTitleInput != null)
                editDraft.Title = editTitleInput.text;
            if (editDescriptionInput != null)
                editDraft.Description = editDescriptionInput.text;
            if (editTagsInput != null)
                editDraft.Tags = editTagsInput.text;
            if (!editWordsReadOnly && editWordsInput != null)
                editDraft.Words = ParseWords(editWordsInput.text);
            editDraft.Language = editLanguage;
            editDraft.WordCount = editDraft.Words != null ? editDraft.Words.Count : 0;
        }

        private bool EditHasGameplayChange()
        {
            MockupWordList current = store.FindById(editingListId);
            return current != null && (current.Language != editLanguage
                || !WordsEqual(current.Words, editDraft != null ? editDraft.Words : null));
        }

        private void SaveEditClicked()
        {
            if (editDraft == null)
                return;

            CaptureEditInputs();
            editDraft.Title = editTitleInput != null ? editTitleInput.text : editDraft.Title;
            editDraft.Description = editDescriptionInput != null ? editDescriptionInput.text : editDraft.Description;
            editDraft.Tags = editTagsInput != null ? editTagsInput.text.Trim() : editDraft.Tags;
            editDraft.Language = editLanguage;
            if (!editWordsReadOnly && editWordsInput != null)
                editDraft.Words = ParseWords(editWordsInput.text);
            editDraft.WordCount = editDraft.Words != null ? editDraft.Words.Count : 0;

            if (string.IsNullOrWhiteSpace(editDraft.Title))
            {
                SetDialogValidation("Enter a list name.");
                return;
            }
            if (editDraft.Words == null || editDraft.Words.Count == 0)
            {
                SetDialogValidation("Enter at least one word. Blank lines are ignored.");
                return;
            }

            bool contentChanged = EditHasGameplayChange();
            if (copyEditingMode && !contentChanged)
            {
                SetDialogValidation("Change at least one word or the language to create a separate version.");
                return;
            }
            if (contentChanged || copyEditingMode)
            {
                OpenDialog(copyEditingMode ? "Create a separate list version?" : "Confirm content change",
                    660f, 350f);
                CreateText(dialogContentRoot, "ContentChangeWarning",
                    copyEditingMode
                        ? "A new My List will be created with an independent ID. The published original and its scores stay unchanged."
                        : "This changes the list's playable words. Current stars and best time will be cleared for this version; the previous record stays in its history.",
                    18f, Ink, TextAlignmentOptions.Center, false,
                    new Vector2(0f, 40f), new Vector2(540f, 130f));
                CreateButton(dialogContentRoot, "ContentChangeCancel", "Keep editing", 170f, 42f,
                    PaperAlt, RenderEditDialog, 15f, false, new Vector2(-100f, -96f));
                CreateButton(dialogContentRoot, "ContentChangeConfirm",
                    copyEditingMode ? "Create new list" : "Save changed words", 205f, 42f,
                    AccentDark, ApplyEdit, 15f, true, new Vector2(115f, -96f));
                return;
            }

            ApplyEdit();
        }

        private void ApplyEdit()
        {
            if (editDraft == null)
                return;
            bool creatingPublishedVersion = copyEditingMode;
            MockupEditResult result = creatingPublishedVersion
                ? store.SavePublishedContentAsNewList(editingListId, editDraft)
                : store.SaveEdit(editingListId, editDraft);
            selectedListId = result.Item.Id;
            lastActionMessage = creatingPublishedVersion
                ? "Created a separate My List version; the published original and its scores remain unchanged."
                : result.ShowRecordWarning
                    ? "Saved new words. Current record cleared; the earlier version record was retained."
                    : "Saved list details. Personal record is unchanged.";
            editDraft = null;
            copyEditingMode = false;
            editWordsReadOnly = false;
            CloseDialog();
            currentPage = Page.Library;
            ClearFiltersThatHide(result.Item);
            RefreshLibraryPage(false, true);
        }

        private void OpenDialog(string title, float width, float height)
        {
            Transform existing = card != null ? card.Find("ModalLayer") : null;
            if (existing != null)
            {
                existing.gameObject.SetActive(false);
                if (Application.isPlaying)
                    Destroy(existing.gameObject);
                else
                    DestroyImmediate(existing.gameObject);
            }

            GameObject layer = CreatePanel("ModalLayer", card, new Color(0.10f, 0.10f, 0.08f, 0.56f));
            SetStretch(layer.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);
            layer.GetComponent<Image>().raycastTarget = true;
            GameObject modal = CreatePanel("ModalCard", layer.transform, Paper);
            SetRect(modal.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(width, height));
            dialogContentRoot = modal.transform;
            CreateText(dialogContentRoot, "ModalTitle", title, 23f, Ink,
                TextAlignmentOptions.MidlineLeft, true, new Vector2(0f, height * 0.5f - 42f),
                new Vector2(width - 56f, 42f));
        }

        private void SetDialogValidation(string message)
        {
            if (dialogValidationText != null)
                dialogValidationText.text = message;
        }

        private static List<string> ParseWords(string text)
        {
            if (string.IsNullOrEmpty(text))
                return new List<string>();
            return text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(word => word.Trim())
                .Where(word => word.Length > 0)
                .ToList();
        }

        private static string WordsAsText(IEnumerable<string> words)
        {
            return string.Join("\n", (words ?? Enumerable.Empty<string>())
                .Where(word => !string.IsNullOrWhiteSpace(word)));
        }

        private static bool WordsEqual(IEnumerable<string> left, IEnumerable<string> right)
        {
            return (left ?? Enumerable.Empty<string>()).SequenceEqual(
                right ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
        }

        private void CloseDialog()
        {
            if (card == null)
                return;
            Transform layer = card.Find("ModalLayer");
            if (layer != null)
            {
                layer.gameObject.SetActive(false);
                if (Application.isPlaying)
                    Destroy(layer.gameObject);
                else
                    DestroyImmediate(layer.gameObject);
            }
            dialogContentRoot = null;
            editDraft = null;
        }

        private void RefreshLibraryAfterDialog()
        {
            CloseDialog();
            BuildView(true);
        }

        private static string LanguageLabel(MockupLanguage language)
        {
            switch (language)
            {
                case MockupLanguage.Chinese: return "中文";
                case MockupLanguage.Mixed: return "EN + 中文";
                default: return "English";
            }
        }

        private static string FilterLabel(MockupLanguageFilter filter)
        {
            switch (filter)
            {
                case MockupLanguageFilter.English: return "English";
                case MockupLanguageFilter.Chinese: return "中文";
                case MockupLanguageFilter.Mixed: return "Mixed";
                default: return "All";
            }
        }

        private static string FilterLabel(MockupPlayedFilter filter)
        {
            switch (filter)
            {
                case MockupPlayedFilter.Played: return "Played";
                case MockupPlayedFilter.Unplayed: return "Unplayed";
                default: return "All";
            }
        }

        private static string SortLabel(MockupSortMode mode)
        {
            switch (mode)
            {
                case MockupSortMode.Name: return "Name";
                case MockupSortMode.Added: return "Date added";
                case MockupSortMode.Modified: return "Modified";
                case MockupSortMode.WordCount: return "Word count";
                case MockupSortMode.PersonalStars: return "Personal stars";
                default: return "Recently played";
            }
        }

        private static string GroupLabel(MockupLibraryGroup group)
        {
            switch (group)
            {
                case MockupLibraryGroup.CommunityDownloads: return "Community Downloads";
                case MockupLibraryGroup.MyPublished: return "My Published";
                default: return "My Lists";
            }
        }

        private static string RecordLabel(MockupWordList item)
        {
            if (!item.PersonalStars.HasValue)
                return item.HasBeenPlayed ? "played" : "not played";
            return new string('★', Mathf.Clamp(item.PersonalStars.Value, 0, 5));
        }

        private static string ListBadge(MockupWordList item)
        {
            if (item.PublicationStatus == MockupPublicationStatus.UpdateNeeded)
                return "Update needed";
            if (item.PublicationStatus == MockupPublicationStatus.Published)
                return "Published";
            if (item.DownloadStatus == MockupDownloadStatus.RecentlyAdded)
                return "Recently added";
            return string.Empty;
        }

        private TMP_Dropdown CreateDropdown(
            Transform parent,
            string name,
            IReadOnlyList<string> options,
            int selectedIndex,
            float width,
            float height,
            Vector2 position,
            Action<int> onValueChanged)
        {
            if (options == null || options.Count == 0)
                throw new ArgumentException("A mockup dropdown needs at least one option.", nameof(options));
            int safeSelectedIndex = Mathf.Clamp(selectedIndex, 0, options.Count - 1);

            GameObject root = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            root.SetActive(false);
            root.transform.SetParent(parent, false);
            SetRect(root.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), position, new Vector2(width, height));

            Image background = root.AddComponent<Image>();
            background.sprite = uiResources.standard;
            background.type = Image.Type.Sliced;
            background.color = PaperAlt;
            background.raycastTarget = true;

            TMP_Dropdown dropdown = Application.isPlaying
                ? (TMP_Dropdown)root.AddComponent<MockupSlicedDropdown>()
                : root.AddComponent<TMP_Dropdown>();
            dropdown.targetGraphic = background;
            dropdown.navigation = new Navigation { mode = Navigation.Mode.None };
            dropdown.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = dropdown.colors;
            colors.normalColor = PaperAlt;
            colors.highlightedColor = Color.Lerp(PaperAlt, Accent, 0.12f);
            colors.pressedColor = Color.Lerp(PaperAlt, AccentDark, 0.2f);
            colors.selectedColor = PaperAlt;
            colors.disabledColor = new Color(PaperAlt.r, PaperAlt.g, PaperAlt.b, 0.6f);
            colors.colorMultiplier = 1f;
            dropdown.colors = colors;

            TextMeshProUGUI caption = CreateText(root.transform, "Caption", options[safeSelectedIndex],
                15f, Ink, TextAlignmentOptions.MidlineLeft, false);
            SetStretch(caption.rectTransform, 12f, 30f, 2f, 2f);
            dropdown.captionText = caption;

            TextMeshProUGUI arrow = CreateText(root.transform, "Arrow", "▾", 17f, AccentDark,
                TextAlignmentOptions.Center, true);
            SetRect(arrow.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f), new Vector2(-8f, 0f), new Vector2(22f, 24f));

            GameObject template = DefaultControls.CreatePanel(uiResources);
            template.name = "Template";
            template.transform.SetParent(root.transform, false);
            RectTransform templateRect = template.GetComponent<RectTransform>();
            SetRect(templateRect, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0.5f, 1f), new Vector2(0f, 2f),
                new Vector2(0f, Mathf.Min(240f, options.Count * DropdownItemHeight + 4f)));
            Image templateImage = template.GetComponent<Image>();
            templateImage.sprite = uiResources.background;
            templateImage.type = Image.Type.Sliced;
            templateImage.color = White;
            templateImage.raycastTarget = false;

            ScrollRect scroll = template.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.inertia = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;

            GameObject viewportObject = new GameObject("Viewport",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask));
            viewportObject.transform.SetParent(template.transform, false);
            SetStretch(viewportObject.GetComponent<RectTransform>(), 2f, 2f, 2f, 2f);
            Image viewportImage = viewportObject.GetComponent<Image>();
            viewportImage.sprite = uiResources.mask;
            viewportImage.type = Image.Type.Sliced;
            viewportImage.color = Color.white;
            viewportImage.raycastTarget = true;
            viewportObject.GetComponent<Mask>().showMaskGraphic = false;

            GameObject contentObject = new GameObject("Content", typeof(RectTransform));
            contentObject.transform.SetParent(viewportObject.transform, false);
            RectTransform contentRect = contentObject.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = Vector2.zero;

            VerticalLayoutGroup contentLayout = contentObject.AddComponent<VerticalLayoutGroup>();
            contentLayout.padding = new RectOffset(2, 2, 2, 2);
            contentLayout.spacing = 0f;
            contentLayout.childAlignment = TextAnchor.UpperCenter;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = false;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;
            ContentSizeFitter contentFitter = contentObject.AddComponent<ContentSizeFitter>();
            contentFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewportObject.GetComponent<RectTransform>();
            scroll.content = contentRect;

            GameObject item = new GameObject("Item",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Toggle), typeof(LayoutElement));
            item.transform.SetParent(contentObject.transform, false);
            RectTransform itemRect = item.GetComponent<RectTransform>();
            itemRect.anchorMin = new Vector2(0f, 1f);
            itemRect.anchorMax = new Vector2(1f, 1f);
            itemRect.pivot = new Vector2(0.5f, 1f);
            itemRect.sizeDelta = new Vector2(0f, DropdownItemHeight);
            Image itemBackground = item.GetComponent<Image>();
            itemBackground.sprite = uiResources.standard;
            itemBackground.type = Image.Type.Sliced;
            itemBackground.color = White;
            itemBackground.raycastTarget = true;

            GameObject checkmarkObject = new GameObject("Item Checkmark",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            checkmarkObject.transform.SetParent(item.transform, false);
            SetRect(checkmarkObject.GetComponent<RectTransform>(), new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(10f, 0f),
                new Vector2(18f, 18f));
            Image checkmark = checkmarkObject.GetComponent<Image>();
            checkmark.sprite = uiResources.checkmark;
            checkmark.type = Image.Type.Sliced;
            checkmark.color = AccentDark;
            checkmark.raycastTarget = false;

            TextMeshProUGUI optionLabel = CreateText(item.transform, "Item Label", string.Empty,
                14f, Ink, TextAlignmentOptions.MidlineLeft, false);
            SetStretch(optionLabel.rectTransform, 34f, 8f, 1f, 1f);
            optionLabel.enableAutoSizing = true;
            optionLabel.fontSizeMin = 10f;
            optionLabel.fontSizeMax = 14f;
            optionLabel.textWrappingMode = TextWrappingModes.NoWrap;
            optionLabel.overflowMode = TextOverflowModes.Overflow;

            Toggle toggle = item.GetComponent<Toggle>();
            toggle.targetGraphic = itemBackground;
            toggle.graphic = checkmark;
            toggle.navigation = new Navigation { mode = Navigation.Mode.None };
            toggle.transition = Selectable.Transition.ColorTint;
            ColorBlock toggleColors = toggle.colors;
            toggleColors.normalColor = White;
            toggleColors.highlightedColor = Color.Lerp(White, Accent, 0.12f);
            toggleColors.pressedColor = Color.Lerp(White, AccentDark, 0.16f);
            toggleColors.selectedColor = White;
            toggle.colors = toggleColors;

            LayoutElement itemLayout = item.GetComponent<LayoutElement>();
            itemLayout.preferredHeight = DropdownItemHeight;
            itemLayout.minHeight = DropdownItemHeight;

            template.SetActive(false);
            dropdown.template = templateRect;
            dropdown.itemText = optionLabel;
            dropdown.ClearOptions();
            dropdown.AddOptions(options.ToList());
            dropdown.value = safeSelectedIndex;
            if (onValueChanged != null && Application.isPlaying)
                dropdown.onValueChanged.AddListener(onValueChanged.Invoke);

            root.SetActive(true);
            return dropdown;
        }

        private Image CreateImage(Transform parent, string name, Sprite sprite, Vector2 position, Vector2 size)
        {
            GameObject root = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            root.transform.SetParent(parent, false);
            SetRect(root.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), position, size);
            Image image = root.GetComponent<Image>();
            image.sprite = sprite != null ? sprite : uiResources.standard;
            image.type = Image.Type.Sliced;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        private TMP_InputField CreateInputField(
            Transform parent,
            string name,
            string placeholder,
            float width,
            float height,
            Vector2 position,
            Action<string> onChanged,
            bool multiline = false)
        {
            // Do not start with DefaultControls.CreateInputField here: that factory
            // adds the legacy InputField (a Selectable), and TMP_InputField is also
            // a Selectable. Unity's DisallowMultipleComponent on Selectable rejects
            // the second component when both are placed on this GameObject.
            GameObject field = new GameObject(name, typeof(RectTransform));
            field.SetActive(false);
            field.transform.SetParent(parent, false);
            field.AddComponent<CanvasRenderer>();
            Image background = field.AddComponent<Image>();
            TMP_InputField input = field.AddComponent<TMP_InputField>();
            SetRect(field.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), position, new Vector2(width, height));

            background.sprite = uiResources.inputField;
            background.color = White;
            background.type = Image.Type.Sliced;
            background.raycastTarget = true;

            GameObject textArea = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            textArea.transform.SetParent(field.transform, false);
            RectTransform viewport = textArea.GetComponent<RectTransform>();
            SetStretch(viewport, 10f, 10f, 6f, 6f);

            TextMeshProUGUI value = CreateText(textArea.transform, "Text", string.Empty, 18f, Ink,
                TextAlignmentOptions.MidlineLeft, false);
            SetStretch(value.rectTransform, 0f, 0f, 0f, 0f);
            value.textWrappingMode = multiline ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            value.overflowMode = TextOverflowModes.Overflow;

            TextMeshProUGUI placeholderText = CreateText(textArea.transform, "Placeholder", placeholder,
                16f, MutedInk, TextAlignmentOptions.MidlineLeft, false);
            SetStretch(placeholderText.rectTransform, 0f, 0f, 0f, 0f);
            placeholderText.fontStyle = FontStyles.Italic;
            placeholderText.textWrappingMode = multiline ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            placeholderText.overflowMode = TextOverflowModes.Overflow;

            input.textViewport = viewport;
            input.textComponent = value;
            input.placeholder = placeholderText;
            input.targetGraphic = background;
            input.lineType = multiline ? TMP_InputField.LineType.MultiLineNewline : TMP_InputField.LineType.SingleLine;
            input.caretColor = AccentDark;
            input.selectionColor = new Color(0.72f, 0.47f, 0.35f, 0.25f);
            input.navigation = new Navigation { mode = Navigation.Mode.None };
            if (onChanged != null)
                input.onValueChanged.AddListener(onChanged.Invoke);
            // Activate only after TMP_InputField has all of its required references.
            field.SetActive(true);
            return input;
        }

        private Button CreateButton(
            Transform parent,
            string name,
            string label,
            float width,
            float height,
            Color background,
            Action onClick,
            float fontSize,
            bool bold,
            Vector2? position = null)
        {
            GameObject root = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image), typeof(Button), typeof(LayoutElement));
            root.transform.SetParent(parent, false);
            if (position.HasValue)
                SetRect(root.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), position.Value, new Vector2(width, height));
            else
                root.GetComponent<RectTransform>().sizeDelta = new Vector2(width, height);

            Image image = root.GetComponent<Image>();
            image.sprite = uiResources.standard;
            image.type = Image.Type.Sliced;
            image.color = background;
            image.raycastTarget = true;

            Button button = root.GetComponent<Button>();
            button.targetGraphic = image;
            if (button.targetGraphic is Image targetImage)
                targetImage.type = Image.Type.Sliced;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = button.colors;
            colors.normalColor = background;
            colors.highlightedColor = Color.Lerp(background, Accent, 0.12f);
            colors.pressedColor = Color.Lerp(background, AccentDark, 0.2f);
            colors.selectedColor = background;
            colors.disabledColor = new Color(background.r, background.g, background.b, 0.6f);
            colors.colorMultiplier = 1f;
            button.colors = colors;

            TextMeshProUGUI labelText = CreateText(root.transform, "Label", label, fontSize,
                IsLightText(background) ? White : Ink, TextAlignmentOptions.Center, bold);
            labelText.rectTransform.anchorMin = Vector2.zero;
            labelText.rectTransform.anchorMax = Vector2.one;
            labelText.rectTransform.offsetMin = new Vector2(9f, 2f);
            labelText.rectTransform.offsetMax = new Vector2(-9f, -2f);

            LayoutElement layout = root.GetComponent<LayoutElement>();
            layout.preferredWidth = width;
            layout.preferredHeight = height;
            layout.minWidth = width;
            layout.minHeight = height;
            if (onClick != null && Application.isPlaying)
            {
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(onClick.Invoke);
            }
            return button;
        }

        private GameObject CreatePanel(string name, Transform parent, Color color)
        {
            GameObject panel = DefaultControls.CreatePanel(uiResources);
            panel.name = name;
            panel.transform.SetParent(parent, false);
            Image image = panel.GetComponent<Image>();
            image.sprite = uiResources.background;
            image.type = Image.Type.Sliced;
            image.color = color;
            image.raycastTarget = false;
            return panel;
        }

        private TextMeshProUGUI CreateText(
            Transform parent,
            string name,
            string value,
            float size,
            Color color,
            TextAlignmentOptions alignment,
            bool bold,
            Vector2? position = null,
            Vector2? sizeDelta = null,
            bool useCjk = false)
        {
            GameObject root = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            root.transform.SetParent(parent, false);
            TextMeshProUGUI text = root.GetComponent<TextMeshProUGUI>();
            text.text = value;
            text.font = primaryFont != null ? primaryFont : TMP_Settings.defaultFontAsset;
            text.fontSize = size;
            text.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            text.color = color;
            text.margin = Vector4.zero;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.Normal;
            if (position.HasValue && sizeDelta.HasValue)
                SetRect(text.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), position.Value, sizeDelta.Value);
            if (useCjk && cjkFont != null)
                text.font = cjkFont;
            return text;
        }

        private static ScrollRect GetScrollRectForContent(Transform content)
        {
            if (content == null || content.parent == null || content.parent.parent == null)
                return null;
            return content.parent.parent.GetComponent<ScrollRect>();
        }

        private Transform CreateScrollContent(Transform parent, string name, float width, float height, Vector2 position)
        {
            GameObject scrollView = DefaultControls.CreateScrollView(uiResources);
            scrollView.name = name;
            scrollView.transform.SetParent(parent, false);
            SetRect(scrollView.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), position, new Vector2(width, height));

            Image rootImage = scrollView.GetComponent<Image>();
            rootImage.color = new Color(1f, 1f, 1f, 0f);
            rootImage.raycastTarget = false;
            ScrollRect scroll = scrollView.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
            scroll.horizontalScrollbar = null;
            scroll.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            Transform viewport = scrollView.transform.Find("Viewport");
            Transform content = viewport != null ? viewport.Find("Content") : null;
            if (viewport == null || content == null)
                throw new InvalidOperationException("Default Scroll View is missing its Viewport/Content hierarchy.");

            Image viewportImage = viewport.GetComponent<Image>();
            viewportImage.sprite = maskSprite != null ? maskSprite : standardSprite;
            viewportImage.type = Image.Type.Sliced;
            // Mask's stencil writer enables UI alpha clipping; keep this Image opaque
            // so it writes the stencil. showMaskGraphic=false keeps the viewport invisible.
            viewportImage.color = Color.white;
            viewportImage.raycastTarget = true;
            Mask mask = viewport.GetComponent<Mask>();
            mask.showMaskGraphic = false;

            RectTransform contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(0f, 0f);

            VerticalLayoutGroup layout = content.GetComponent<VerticalLayoutGroup>();
            if (layout == null)
                layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(6, 6, 6, 6);
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
            if (fitter == null)
                fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = contentRect;
            scroll.viewport = viewport.GetComponent<RectTransform>();

            Transform horizontalBar = scrollView.transform.Find("Scrollbar Horizontal");
            if (horizontalBar != null)
            {
                if (Application.isPlaying)
                    Destroy(horizontalBar.gameObject);
                else
                    DestroyImmediate(horizontalBar.gameObject);
            }
            Transform verticalBar = scrollView.transform.Find("Scrollbar Vertical");
            if (verticalBar != null)
            {
                RectTransform barRect = verticalBar.GetComponent<RectTransform>();
                barRect.sizeDelta = new Vector2(10f, 0f);
                scroll.verticalScrollbar = verticalBar.GetComponent<Scrollbar>();
                scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            }
            return content;
        }

        private void OpenNewListDialogInternal()
        {
            OpenNewListDialog();
        }

        private static bool IsLightText(Color background)
        {
            return background.r < 0.80f && background.g < 0.66f && background.b < 0.62f;
        }

        private static bool Contains(string value, string term)
        {
            return !string.IsNullOrEmpty(value)
                && value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ApplySlicedImages(Transform root)
        {
            if (root == null)
                return;

            Sprite fallbackSprite = backgroundSprite != null ? backgroundSprite : standardSprite;
            foreach (Image image in root.GetComponentsInChildren<Image>(true))
            {
                if (image.sprite == null && fallbackSprite != null)
                    image.sprite = fallbackSprite;
                image.type = Image.Type.Sliced;
            }
        }

        private static void SetRect(
            RectTransform rect,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 position,
            Vector2 size)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void SetStretch(RectTransform rect, float left, float right, float bottom, float top)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private void ClearChildren(Transform parent)
        {
            if (parent == null)
                return;
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                GameObject child = parent.GetChild(i).gameObject;
                if (Application.isPlaying)
                    Destroy(child);
                else
                    DestroyImmediate(child);
            }
        }
    }
}
