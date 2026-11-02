using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Words.UiMockups.WordListHub
{
    /// <summary>
    /// Local-only interaction layer for the isolated Word List Hub mockup scene.
    /// All state lives in a fresh MockupWordListStore and is discarded when the scene
    /// is opened again. This controller never talks to gameplay, Steam, files or prefs.
    /// </summary>
    public sealed class WordListHubMockupController : MonoBehaviour
    {
        private const float SuccessfulActionFeedbackDuration = 3f;
        private const float FailedActionFeedbackDuration = 4f;

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

        [SerializeField] private TMP_FontAsset primaryFont;
        [SerializeField] private TMP_FontAsset cjkFont;
        [SerializeField] private Sprite standardSprite;
        [SerializeField] private Sprite backgroundSprite;
        [SerializeField] private Sprite inputFieldSprite;
        [SerializeField] private Sprite maskSprite;
        [SerializeField] private WordListHubSceneBindings sceneBindings;
        [SerializeField] private Sprite firstWordUnlockedIcon;
        [SerializeField] private Sprite firstWordLockedIcon;
        [SerializeField] private Sprite twoWordsUnlockedIcon;
        [SerializeField] private Sprite twoWordsLockedIcon;
        [SerializeField] private Sprite wordWithDUnlockedIcon;
        [SerializeField] private Sprite wordWithDLockedIcon;

        [Header("Word Preview")]
        [SerializeField, Min(0.1f), Tooltip(
            "Scale for the glyph and pinyin text in both stacked word previews. "
            + "Changes take effect when the mockup initializes.")]
        private float wordPreviewFontScale = 2f;

        private readonly HashSet<MockupLibraryGroup> collapsedGroups =
            new HashSet<MockupLibraryGroup>();

        private MockupWordListStore store;
        private MockupLibraryQuery query;
        private Page currentPage = Page.Library;
        private ColorBlock activeNavigationColors;
        private ColorBlock inactiveNavigationColors;
        private Color activeNavigationBackground;
        private Color inactiveNavigationBackground;
        private Color activeNavigationLabel;
        private Color inactiveNavigationLabel;
        private bool navigationColorsInitialized;
        private AchievementFilter achievementFilter = AchievementFilter.All;
        private string selectedListId;
        private string selectedDiscoveryId;
        private string librarySearchText = string.Empty;
        private MockupDiscoveryQuery communityQuery = new MockupDiscoveryQuery();
        private string lastActionMessage = string.Empty;
        private float actionFeedbackExpiresAt;
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
        private MockupRubyWordsView libraryRubyWordsView;
        private MockupRubyWordsView communityRubyWordsView;
        private string pendingDeleteListId;
        private int pendingDeleteVisibleIndex = -1;
        private MockupLanguage newListLanguage = MockupLanguage.English;
        private MockupLanguage editLanguage = MockupLanguage.English;
        private bool copyEditingMode;
        private string pendingPlayListId;
        private Coroutine communityScrollRoutine;
        private bool filtersMenuOpen;
        private WordListHubSceneBindings.FilterMenuBinding openFilterMenu;

        /// <summary>
        /// Retained for source compatibility with any stale Editor menu. Rebuilding
        /// authored UI from this entry point is intentionally disabled.
        /// </summary>
        [Obsolete("Use the explicit scene migration tool; this entry point cannot rebuild the UI.")]
        public void BuildPreviewInEditor()
        {
            throw new InvalidOperationException(
                "The Word List Hub is Scene-authored. Use the safe one-time Editor migration tool.");
        }

        private void Awake()
        {
            EnsureStore();
            BindAuthoredScene();
            InitializeRubyWordsViews();
            CacheNavigationColors();
            BindSceneEvents();
            SetFiltersMenuOpen(false);
            SetPageActive(currentPage);
            RefreshCurrentPage();
        }

        private void Update()
        {
            if (!string.IsNullOrEmpty(lastActionMessage)
                && Time.unscaledTime >= actionFeedbackExpiresAt)
                ClearActionFeedback();

            if (openFilterMenu != null && Input.GetKeyDown(KeyCode.Escape))
                SetFilterMenuOpen(openFilterMenu, false);
        }

        private void LateUpdate()
        {
            if (libraryRubyWordsView != null)
                libraryRubyWordsView.RefreshLayoutIfNeeded();
            if (communityRubyWordsView != null)
                communityRubyWordsView.RefreshLayoutIfNeeded();
        }

        private void InitializeRubyWordsViews()
        {
            if (sceneBindings.library.detailWords != null && libraryRubyWordsView == null)
                libraryRubyWordsView = MockupRubyWordsView.CreateLibrary(
                    sceneBindings.library.detailWords, wordPreviewFontScale);
            if (sceneBindings.community.detailSummary != null && communityRubyWordsView == null)
                communityRubyWordsView = MockupRubyWordsView.CreateCommunity(
                    sceneBindings.community.detailSummary,
                    libraryRubyWordsView == null || libraryRubyWordsView.ScrollRect == null
                        ? null
                        : libraryRubyWordsView.ScrollRect.verticalScrollbar,
                    wordPreviewFontScale);
        }

        private void BindAuthoredScene()
        {
            if (sceneBindings == null)
                sceneBindings = GetComponent<WordListHubSceneBindings>();
            if (sceneBindings == null)
                throw new InvalidOperationException("WordListHubSceneBindings is missing from the mockup scene.");

            card = sceneBindings.card;
            generatedRoot = sceneBindings.hubRoot;
            footerText = sceneBindings.footer;
            libraryListContent = sceneBindings.library.listContent;
            libraryScrollRect = sceneBindings.library.listScroll;
            libraryDetailPane = sceneBindings.library.detailPane;
            communityListContent = sceneBindings.community.listContent;
            communityDetailPane = sceneBindings.community.detailPane;

            if (sceneBindings.pages == null)
                throw new InvalidOperationException("No authored page references are assigned.");
            foreach (WordListHubSceneBindings.PageBinding binding in sceneBindings.pages)
            {
                if (binding == null || binding.root == null)
                    continue;
                if ((Page)binding.page == currentPage)
                    pageRoot = binding.root.transform;
            }

            if (card == null || generatedRoot == null || pageRoot == null
                || libraryListContent == null || libraryDetailPane == null)
            {
                throw new InvalidOperationException(
                    "The Word List Hub scene bindings are incomplete. Run the safe one-time migration tool.");
            }

            BindDialogReferences();
        }

        private void BindDialogReferences()
        {
            WordListHubSceneBindings.ModalBinding modal = sceneBindings.modal;
            if (modal == null)
                return;

            dialogContentRoot = modal.root != null ? modal.root.transform : null;
            WordListHubSceneBindings.FormBinding create = modal.newList;
            if (create != null)
            {
                newListTitleInput = create.titleInput;
                newListDescriptionInput = create.descriptionInput;
                newListTagsInput = create.tagsInput;
                newListWordsInput = create.wordsInput;
                dialogValidationText = create.validationText;
            }

            WordListHubSceneBindings.FormBinding edit = modal.editList;
            if (edit != null)
            {
                editTitleInput = edit.titleInput;
                editDescriptionInput = edit.descriptionInput;
                editTagsInput = edit.tagsInput;
                editWordsInput = edit.wordsInput;
                editSaveHintText = edit.saveHintText;
                editSaveButton = edit.saveButton;
            }
        }

        private void BindSceneEvents()
        {
            BindButton(sceneBindings.navChallenges, () => ChangePage(Page.Challenges));
            BindButton(sceneBindings.navLibrary, () => ChangePage(Page.Library));
            BindButton(sceneBindings.navCommunity, () => ChangePage(Page.Community));
            BindButton(sceneBindings.navAchievements, () => ChangePage(Page.Achievements));
            BindButton(sceneBindings.resetMockData, ResetMockData);

            WordListHubSceneBindings.LibraryPageBinding library = sceneBindings.library;
            BindButton(library.allLists, ToggleLibraryView);
            BindButton(library.favorites, null);
            if (library.favorites != null && library.favorites.button != null)
                library.favorites.button.gameObject.SetActive(false);
            BindButton(library.createList, OpenNewListDialog);
            BindInput(library.searchInput, HandleSearchChanged);
            BindDropdown(library.languageFilter, SetLanguageFilterFromDropdown);
            BindDropdown(library.playedFilter, SetPlayedFilterFromDropdown);
            BindDropdown(library.sortFilter, SetSortModeFromDropdown);
            BindDropdown(library.directionFilter, SetSortDirectionFromDropdown);
            BindFilterMenu(library.filtersMenu);

            if (library.groups != null)
            {
                foreach (WordListHubSceneBindings.GroupBinding groupBinding in library.groups)
                {
                    if (groupBinding == null
                        || !Enum.TryParse(groupBinding.groupKey, true, out MockupLibraryGroup group))
                    {
                        continue;
                    }
                    BindButton(groupBinding.button, () => ToggleGroup(group));
                }
            }

            WordListHubSceneBindings.CommunityPageBinding community = sceneBindings.community;
            BindFilterMenu(community.connectionMenu);
            if (community.connectionState != null)
                community.connectionState.gameObject.SetActive(false);
            BindInput(community.searchInput, HandleCommunitySearchChanged);
            BindFilterMenu(sceneBindings.achievements.filterMenu);
            BindDropdown(sceneBindings.achievements.filter, SetAchievementFilterFromDropdown);
            BindButton(sceneBindings.challenges.dailyChallenge, () => ChangePage(Page.Library));
            BindButton(sceneBindings.challenges.weeklyChallenge, () => ChangePage(Page.Library));

            WordListHubSceneBindings.ModalBinding modal = sceneBindings.modal;
            if (modal == null)
                return;
            if (modal.newList != null)
            {
                BindDropdown(modal.newList.languageDropdown, SetNewListLanguageFromDropdown);
                BindButton(modal.newList.cancelButton, CloseDialog);
                BindButton(modal.newList.saveButton, CreateNewList);
            }
            if (modal.editList != null)
            {
                BindDropdown(modal.editList.languageDropdown, SetEditLanguageFromDropdown);
                BindInput(modal.editList.wordsInput, HandleEditWordsChanged);
                BindButton(modal.editList.cancelButton, CloseDialog);
                BindButton(modal.editList.saveButton, SaveEditClicked);
            }
            if (modal.contentChange != null)
            {
                BindButton(modal.contentChange.confirmButton, ApplyEdit);
                BindButton(modal.contentChange.cancelButton, RenderEditDialog);
            }
            if (modal.deleteList != null)
            {
                BindButton(modal.deleteList.confirmButton, ConfirmDeleteList);
                BindButton(modal.deleteList.cancelButton, CancelDeleteList);
            }
            if (modal.playResult != null)
                BindButton(modal.playResult.confirmButton, () => CompleteMockRun(pendingPlayListId));
        }

        private void SetPageActive(Page page)
        {
            WordListHubPage requested = ToScenePage(page);
            pageRoot = null;
            foreach (WordListHubSceneBindings.PageBinding binding in sceneBindings.pages)
            {
                if (binding == null || binding.root == null)
                    continue;

                bool active = binding.page == requested;
                binding.root.SetActive(active);
                if (active)
                    pageRoot = binding.root.transform;
            }

            if (pageRoot == null)
                throw new InvalidOperationException("The requested page is not assigned in WordListHubSceneBindings: " + requested);

            UpdateNavigationVisuals(page);
        }

        private void CacheNavigationColors()
        {
            WordListHubSceneBindings.ButtonBinding active = sceneBindings.navLibrary;
            WordListHubSceneBindings.ButtonBinding inactive = sceneBindings.navChallenges;
            if (active == null || active.button == null || inactive == null || inactive.button == null)
                return;

            activeNavigationColors = active.button.colors;
            inactiveNavigationColors = inactive.button.colors;
            activeNavigationBackground = active.button.targetGraphic != null
                ? active.button.targetGraphic.color : Color.white;
            inactiveNavigationBackground = inactive.button.targetGraphic != null
                ? inactive.button.targetGraphic.color : Color.white;
            activeNavigationLabel = active.label != null ? active.label.color : Color.white;
            inactiveNavigationLabel = inactive.label != null ? inactive.label.color : Color.white;
            navigationColorsInitialized = true;
        }

        private void UpdateNavigationVisuals(Page activePage)
        {
            if (!navigationColorsInitialized)
                return;

            ApplyNavigationVisual(sceneBindings.navChallenges, activePage == Page.Challenges);
            ApplyNavigationVisual(sceneBindings.navLibrary, activePage == Page.Library);
            ApplyNavigationVisual(sceneBindings.navCommunity, activePage == Page.Community);
            ApplyNavigationVisual(sceneBindings.navAchievements, activePage == Page.Achievements);
        }

        private void ApplyNavigationVisual(WordListHubSceneBindings.ButtonBinding binding, bool active)
        {
            if (binding == null || binding.button == null)
                return;

            binding.button.colors = active ? activeNavigationColors : inactiveNavigationColors;
            if (binding.button.targetGraphic != null)
                binding.button.targetGraphic.color = active
                    ? activeNavigationBackground : inactiveNavigationBackground;
            if (binding.label != null)
                binding.label.color = active ? activeNavigationLabel : inactiveNavigationLabel;
        }

        private static WordListHubPage ToScenePage(Page page)
        {
            switch (page)
            {
                case Page.Challenges: return WordListHubPage.Challenges;
                case Page.Community: return WordListHubPage.Community;
                case Page.Achievements: return WordListHubPage.Achievements;
                default: return WordListHubPage.Library;
            }
        }

        private static void BindButton(WordListHubSceneBindings.ButtonBinding binding, UnityAction action)
        {
            if (binding != null)
                BindButton(binding.button, action);
        }

        private static void BindButton(Button button, UnityAction action)
        {
            if (button == null)
                return;
            button.onClick.RemoveAllListeners();
            if (action != null)
                button.onClick.AddListener(action);
        }

        private static void BindDropdown(TMP_Dropdown dropdown, UnityAction<int> action)
        {
            if (dropdown == null)
                return;
            dropdown.onValueChanged.RemoveAllListeners();
            if (action != null)
                dropdown.onValueChanged.AddListener(action);
        }

        private static void BindInput(TMP_InputField input, UnityAction<string> action)
        {
            if (input == null)
                return;
            input.onValueChanged.RemoveAllListeners();
            if (action != null)
                input.onValueChanged.AddListener(action);
        }

        private void EnsureStore()
        {
            if (store == null)
                store = MockupWordListStore.CreateWithFixtures();
            if (query == null)
                query = new MockupLibraryQuery();
        }



        private void ResetMockData()
        {
            SetFiltersMenuOpen(false);
            CloseDialog();
            store = MockupWordListStore.CreateWithFixtures();
            query = new MockupLibraryQuery();
            currentPage = Page.Library;
            achievementFilter = AchievementFilter.All;
            selectedListId = null;
            selectedDiscoveryId = null;
            librarySearchText = string.Empty;
            communityQuery = new MockupDiscoveryQuery();
            ClearActionFeedback();
            collapsedGroups.Clear();
            editDraft = null;
            editingListId = null;
            newListLanguage = MockupLanguage.English;
            editLanguage = MockupLanguage.English;
            copyEditingMode = false;
            SetPageActive(currentPage);
            RefreshCurrentPage();
        }

        private void ChangePage(Page page)
        {
            SetFiltersMenuOpen(false);
            currentPage = page;
            SetPageActive(page);
            RefreshCurrentPage();
        }

        private void RenderPage(bool wireEvents)
        {
            SetPageActive(currentPage);
            RefreshCurrentPage();
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


        private void SetLibraryView(MockupLibraryView view)
        {
            query.View = view;
            RefreshLibraryPage(true);
        }

        private void ToggleLibraryView()
        {
            SetLibraryView(query.View == MockupLibraryView.Favorites
                ? MockupLibraryView.All
                : MockupLibraryView.Favorites);
        }

        private void HandleSearchChanged(string value)
        {
            librarySearchText = value ?? string.Empty;
            query.SearchText = librarySearchText;
            RefreshLibraryPage(true);
        }

        private void HandleCommunitySearchChanged(string value)
        {
            communityQuery.SearchText = value ?? string.Empty;
            RefreshCommunityPage();
        }

        private void SetLanguageFilterFromDropdown(int value)
        {
            query.LanguageFilter = (MockupLanguageFilter)Mathf.Clamp(value, 0,
                Enum.GetValues(typeof(MockupLanguageFilter)).Length - 1);
            RefreshLibraryPage(true);
        }

        private void SetPlayedFilterFromDropdown(int value)
        {
            query.PlayedFilter = (MockupPlayedFilter)Mathf.Clamp(value, 0,
                Enum.GetValues(typeof(MockupPlayedFilter)).Length - 1);
            RefreshLibraryPage(true);
        }

        private void SetSortModeFromDropdown(int value)
        {
            query.SortMode = (MockupSortMode)Mathf.Clamp(value, 0,
                Enum.GetValues(typeof(MockupSortMode)).Length - 1);
            RefreshLibraryPage();
        }

        private void SetSortDirectionFromDropdown(int value)
        {
            query.SortDirection = (MockupSortDirection)Mathf.Clamp(value, 0,
                Enum.GetValues(typeof(MockupSortDirection)).Length - 1);
            RefreshLibraryPage();
        }

        private void BindFilterMenu(WordListHubSceneBindings.FilterMenuBinding menu)
        {
            if (menu == null)
                return;

            BindButton(menu.toggleButton, () => SetFilterMenuOpen(menu, menu.panel == null || !menu.panel.activeSelf));
            if (menu.sections == null)
                return;

            foreach (WordListHubSceneBindings.FilterSectionBinding section in menu.sections)
            {
                if (section == null || section.options == null)
                    continue;

                foreach (WordListHubSceneBindings.FilterOptionBinding option in section.options)
                {
                    if (option == null || option.button == null)
                        continue;

                    int value = option.value;
                    switch (section.kind)
                    {
                        case WordListHubSceneBindings.LibraryFilterKind.Order:
                            BindButton(option.button, () => SetSortDirectionFromDropdown(value));
                            break;
                        case WordListHubSceneBindings.LibraryFilterKind.Language:
                            BindButton(option.button, () => SetLanguageFilterFromDropdown(value));
                            break;
                        case WordListHubSceneBindings.LibraryFilterKind.Played:
                            BindButton(option.button, () => SetPlayedFilterFromDropdown(value));
                            break;
                        case WordListHubSceneBindings.LibraryFilterKind.Sort:
                            BindButton(option.button, () => SetSortModeFromDropdown(value));
                            break;
                        case WordListHubSceneBindings.LibraryFilterKind.CommunityOrder:
                            BindButton(option.button, () => SetCommunitySortDirection(value));
                            break;
                        case WordListHubSceneBindings.LibraryFilterKind.CommunityLanguage:
                            BindButton(option.button, () => SetCommunityLanguageFilter(value));
                            break;
                        case WordListHubSceneBindings.LibraryFilterKind.CommunityDownloadState:
                            BindButton(option.button, () => SetCommunityDownloadFilter(value));
                            break;
                        case WordListHubSceneBindings.LibraryFilterKind.CommunitySort:
                            BindButton(option.button, () => SetCommunitySortMode(value));
                            break;
                        case WordListHubSceneBindings.LibraryFilterKind.AchievementStatus:
                            BindButton(option.button, () => SetAchievementFilterFromDropdown(value));
                            break;
                    }
                }
            }
        }

        private void SetFiltersMenuOpen(bool open)
        {
            WordListHubSceneBindings.FilterMenuBinding menu = sceneBindings != null
                && sceneBindings.library != null ? sceneBindings.library.filtersMenu : null;
            if (open)
                SetFilterMenuOpen(menu, true);
            else
                CloseAllFilterMenus();
        }

        private void SetFilterMenuOpen(WordListHubSceneBindings.FilterMenuBinding menu, bool open)
        {
            if (open)
            {
                foreach (WordListHubSceneBindings.FilterMenuBinding candidate in BoundFilterMenus())
                {
                    if (candidate == null || candidate == menu || candidate.panel == null)
                        continue;
                    candidate.panel.SetActive(false);
                    SetFilterMenuToggleLabel(candidate, false);
                }
                openFilterMenu = menu;
            }
            else if (openFilterMenu == menu)
            {
                openFilterMenu = null;
            }

            if (menu != null && menu.panel != null)
                menu.panel.SetActive(open);
            if (open && menu != null && menu.scrollRect != null)
                menu.scrollRect.verticalNormalizedPosition = 1f;
            if (menu != null)
                SetFilterMenuToggleLabel(menu, open);

            filtersMenuOpen = sceneBindings != null
                && sceneBindings.library != null
                && openFilterMenu == sceneBindings.library.filtersMenu;
        }

        private void CloseAllFilterMenus()
        {
            foreach (WordListHubSceneBindings.FilterMenuBinding menu in BoundFilterMenus())
            {
                if (menu == null)
                    continue;
                if (menu.panel != null)
                    menu.panel.SetActive(false);
                SetFilterMenuToggleLabel(menu, false);
            }
            openFilterMenu = null;
            filtersMenuOpen = false;
        }

        private IEnumerable<WordListHubSceneBindings.FilterMenuBinding> BoundFilterMenus()
        {
            if (sceneBindings == null)
                yield break;
            if (sceneBindings.library != null)
                yield return sceneBindings.library.filtersMenu;
            if (sceneBindings.community != null)
                yield return sceneBindings.community.connectionMenu;
            if (sceneBindings.achievements != null)
                yield return sceneBindings.achievements.filterMenu;
        }

        private static void SetFilterMenuToggleLabel(
            WordListHubSceneBindings.FilterMenuBinding menu,
            bool open)
        {
            if (menu != null && menu.toggleLabel != null)
                menu.toggleLabel.text = (menu.toggleBaseLabel ?? "Filters") + (open ? "  ▲" : "  ▼");
        }

        private static void SyncFilterMenu(
            WordListHubSceneBindings.FilterMenuBinding menu,
            MockupLibraryQuery currentQuery)
        {
            if (currentQuery == null)
                return;

            SyncFilterMenu(menu, kind => GetLibraryFilterValue(kind, currentQuery), "Filters");
        }

        private static int GetLibraryFilterValue(
            WordListHubSceneBindings.LibraryFilterKind kind,
            MockupLibraryQuery currentQuery)
        {
            switch (kind)
            {
                case WordListHubSceneBindings.LibraryFilterKind.Order: return (int)currentQuery.SortDirection;
                case WordListHubSceneBindings.LibraryFilterKind.Language: return (int)currentQuery.LanguageFilter;
                case WordListHubSceneBindings.LibraryFilterKind.Played: return (int)currentQuery.PlayedFilter;
                case WordListHubSceneBindings.LibraryFilterKind.Sort: return (int)currentQuery.SortMode;
                default: return -1;
            }
        }

        private static int GetCommunityFilterValue(
            WordListHubSceneBindings.LibraryFilterKind kind,
            MockupDiscoveryQuery currentQuery)
        {
            if (currentQuery == null)
                return -1;

            switch (kind)
            {
                case WordListHubSceneBindings.LibraryFilterKind.CommunityOrder:
                    return (int)currentQuery.SortDirection;
                case WordListHubSceneBindings.LibraryFilterKind.CommunityLanguage:
                    return (int)currentQuery.LanguageFilter;
                case WordListHubSceneBindings.LibraryFilterKind.CommunityDownloadState:
                    return (int)currentQuery.DownloadFilter;
                case WordListHubSceneBindings.LibraryFilterKind.CommunitySort:
                    return (int)currentQuery.SortMode;
                default:
                    return -1;
            }
        }

        private static void SyncFilterMenu(
            WordListHubSceneBindings.FilterMenuBinding menu,
            Func<WordListHubSceneBindings.LibraryFilterKind, int> getSelectedValue,
            string toggleBaseLabel)
        {
            if (menu == null)
                return;
            menu.toggleBaseLabel = toggleBaseLabel;
            if (menu.sections != null)
            {
                foreach (WordListHubSceneBindings.FilterSectionBinding section in menu.sections)
                {
                    if (section == null || section.options == null)
                        continue;

                    int selectedValue = getSelectedValue != null ? getSelectedValue(section.kind) : -1;
                    if (selectedValue < 0)
                        continue;

                    foreach (WordListHubSceneBindings.FilterOptionBinding option in section.options)
                    {
                        if (option == null)
                            continue;

                        bool selected = option.value == selectedValue;
                        if (option.selectedVisual != null)
                            option.selectedVisual.SetActive(selected);
                        if (option.label != null)
                            option.label.text = (selected ? "✓  " : "    ") + option.optionLabel;
                    }
                }
            }

            SetFilterMenuToggleLabel(menu, menu.panel != null && menu.panel.activeSelf);
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
            store.ToggleFavorite(mockId);
            RefreshLibraryPage();
        }

        private void ToggleLike(string remoteId)
        {
            MockupActionResult result = store.TryToggleLike(remoteId);
            if (!result.Succeeded)
                ShowActionFeedback(result.Message + " (mock action; no live service was contacted.)", true);
            RefreshCommunityPage();
        }

        private void DownloadDiscoveryItem(string remoteId)
        {
            MockupActionResult result = store.TryDownload(remoteId);
            if (!result.Succeeded || result.List == null)
            {
                ShowActionFeedback(result.Message + " (mock action; no live service was contacted.)", true);
                RefreshCommunityPage();
                return;
            }

            MockupWordList downloaded = result.List;
            selectedListId = downloaded.Id;
            ClearFiltersThatHideDownloadedItem(downloaded);
            collapsedGroups.Remove(downloaded.Group);
            currentPage = Page.Library;
            ShowActionFeedback("Added “" + downloaded.Title + "” to Community Downloads and cached it for offline use.");
            SetPageActive(currentPage);
            RefreshLibraryPage();
            ScheduleLibraryScrollRestore(0f, false, downloaded.Id);
        }

        private void ReportAndHideDiscoveryItem(string remoteId)
        {
            MockupActionResult result = store.TryReportAndHide(remoteId);
            if (!result.Succeeded)
            {
                ShowActionFeedback(result.Message + " (mock action; no live service was contacted.)", true);
                RefreshCommunityPage();
                return;
            }

            selectedDiscoveryId = null;
            ShowActionFeedback("Hidden from your Community view; a report was simulated locally. No real report was sent.");
            RefreshCommunityPage();
        }

        private void SetCommunitySortDirection(int value)
        {
            communityQuery.SortDirection = (MockupSortDirection)Mathf.Clamp(
                value, 0, Enum.GetValues(typeof(MockupSortDirection)).Length - 1);
            RefreshCommunityPage();
        }

        private void SetCommunityLanguageFilter(int value)
        {
            communityQuery.LanguageFilter = (MockupLanguageFilter)Mathf.Clamp(
                value, 0, Enum.GetValues(typeof(MockupLanguageFilter)).Length - 1);
            RefreshCommunityPage();
        }

        private void SetCommunityDownloadFilter(int value)
        {
            communityQuery.DownloadFilter = (MockupDiscoveryDownloadFilter)Mathf.Clamp(
                value, 0, Enum.GetValues(typeof(MockupDiscoveryDownloadFilter)).Length - 1);
            RefreshCommunityPage();
        }

        private void SetCommunitySortMode(int value)
        {
            communityQuery.SortMode = (MockupDiscoverySortMode)Mathf.Clamp(
                value, 0, Enum.GetValues(typeof(MockupDiscoverySortMode)).Length - 1);
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
            WordListHubSceneBindings.ConfirmationBinding dialog = sceneBindings.modal.playResult;
            if (!result.Succeeded || result.List == null)
            {
                pendingPlayListId = null;
                ShowActionFeedback(result.Message, true);
                ShowConfirmation("Mock play unavailable",
                    result.Message + "\n\nThis is a local mockup. No live service or game was contacted.",
                    "Close", string.Empty, dialog);
                BindButton(dialog.confirmButton, () =>
                {
                    CloseDialog();
                    RefreshCurrentPage();
                });
                return;
            }

            selectedListId = result.List.Id;
            pendingPlayListId = result.List.Id;
            ShowConfirmation("Mock play · " + result.List.Title,
                "This is a simulated play result only. No game scene will start.\n"
                + "The local list stays in its library section if you cancel.",
                "Complete mock run", "Cancel", dialog);
            BindButton(dialog.confirmButton, () => CompleteMockRun(pendingPlayListId));
            BindButton(dialog.cancelButton, () =>
            {
                pendingPlayListId = null;
                CloseDialog();
                RefreshCurrentPage();
            });
        }
        private void CompleteMockRun(string mockId)
        {
            bool completed = store.CompleteRun(mockId, 3, 85f);
            MockupWordList item = store.FindById(mockId);
            pendingPlayListId = null;
            CloseDialog();
            if (!completed || item == null)
            {
                ShowActionFeedback("The local mock run could not be recorded.", true);
                RefreshCurrentPage();
                return;
            }

            string viewListLabel = item.Group == MockupLibraryGroup.CommunityDownloads
                ? "View in Community Downloads" : "View in My Lists";
            WordListHubSceneBindings.ConfirmationBinding dialog = sceneBindings.modal.playResult;
            ShowConfirmation("Mock run complete",
                "3 stars  ·  85.0 seconds\nThis result was recorded in mock data only.",
                viewListLabel, "Close", dialog);
            BindButton(dialog.confirmButton, () =>
            {
                currentPage = Page.Library;
                selectedListId = item.Id;
                ClearFiltersThatHide(item);
                CloseDialog();
                SetPageActive(currentPage);
                RefreshLibraryPage(false, true);
            });
            BindButton(dialog.cancelButton, () =>
            {
                CloseDialog();
                RefreshCurrentPage();
            });
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
                ShowActionFeedback("Created your editable copy. The downloaded original remains unchanged.");
            }

            OpenEditDialog(item.Id);
        }

        private void PublishList(string mockId)
        {
            MockupActionResult result = store.TryPublish(mockId);
            if (!result.Succeeded || result.List == null)
            {
                ShowActionFeedback(result.Message + " (mock action; no live service was contacted.)", true);
                RefreshLibraryPage();
                return;
            }

            selectedListId = result.List.Id;
            ClearFiltersThatHide(result.List);
            ShowActionFeedback("Published this list in mock data.");
            RefreshLibraryPage();
        }

        private void Unpublish(string mockId)
        {
            MockupActionResult result = store.TryUnpublish(mockId);
            if (result.Succeeded && result.List != null)
            {
                selectedListId = result.List.Id;
                ClearFiltersThatHide(result.List);
                ShowActionFeedback("Returned this list to My Lists in mock data; its previous community identity is no longer attached.");
            }
            else
            {
                ShowActionFeedback(result.Message + " (mock action; no live service was contacted.)", true);
            }
            RefreshLibraryPage();
        }

        private void TogglePublication(string mockId)
        {
            MockupWordList item = store.FindById(mockId);
            if (item == null || item.IsReadOnly)
                return;

            if (item.HasPublishedBinding)
                Unpublish(mockId);
            else
                PublishList(mockId);
        }

        private void RequestDeleteList(string mockId)
        {
            MockupWordList item = store.FindById(mockId);
            if (item == null)
                return;

            bool publicationIsActive = item.IsPubliclyVisible
                || item.PublicationStatus == MockupPublicationStatus.Published;
            if (publicationIsActive)
            {
                ShowActionFeedback("Unpublish this list before deleting its local copy. No remote action was taken.", true);
                RefreshLibraryPage();
                return;
            }

            List<MockupWordList> visibleItems = store.QueryLibrary(query).ToList();
            pendingDeleteVisibleIndex = visibleItems.FindIndex(candidate => candidate.Id == mockId);
            pendingDeleteListId = mockId;
            ShowConfirmation(
                item.IsReadOnly ? "Remove downloaded list?" : "Delete local list?",
                item.IsReadOnly
                    ? "This removes the cached copy from Community Downloads. The community item remains available and can be downloaded again."
                    : "This removes the local list only. No published item will be deleted or changed.",
                item.IsReadOnly ? "Remove local copy" : "Delete local list",
                "Cancel", sceneBindings.modal.deleteList);
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
            ShowActionFeedback(result.Message, !result.Succeeded);

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
            if (sceneBindings == null || libraryListContent == null || libraryDetailPane == null)
                throw new InvalidOperationException("Library Scene bindings are missing.");

            float savedScrollY = libraryScrollRect != null && libraryScrollRect.content != null
                ? libraryScrollRect.content.anchoredPosition.y
                : 0f;
            string revealId = revealSelectedItem ? selectedListId : null;

            IReadOnlyList<MockupWordList> queried = store.QueryLibrary(query);
            List<MockupWordList> items = queried.ToList();
            WordListHubSceneBindings.LibraryPageBinding library = sceneBindings.library;
            SyncLibraryControlValues(library);
            DestroyOwnedRuntimeChildren(library.listContent);

            var authoredById = library.authoredRows
                .Where(row => row != null && !string.IsNullOrEmpty(row.listId))
                .ToDictionary(row => row.listId, StringComparer.Ordinal);
            var dynamicById = new Dictionary<string, WordListHubLibraryRowView>(StringComparer.Ordinal);

            foreach (WordListHubSceneBindings.LibraryRowBinding row in library.authoredRows)
            {
                if (row == null)
                    continue;
                MockupWordList item = store.FindById(row.listId);
                if (item == null)
                {
                    if (row.root != null)
                        row.root.SetActive(false);
                    continue;
                }
                UpdateLibraryRow(row, item);
                if (row.root != null)
                    row.root.SetActive(false);
            }

            foreach (MockupWordList item in items)
            {
                if (authoredById.ContainsKey(item.Id))
                    continue;
                WordListHubLibraryRowView view = InstantiateLibraryRow(library, item.Id);
                if (view != null)
                {
                    dynamicById[item.Id] = view;
                    UpdateLibraryRow(view, item);
                    view.gameObject.SetActive(false);
                }
            }

            int siblingIndex = 0;
            foreach (MockupLibraryGroup group in Enum.GetValues(typeof(MockupLibraryGroup)))
            {
                List<MockupWordList> groupItems = items.Where(item => item.Group == group).ToList();
                WordListHubSceneBindings.GroupBinding header = FindGroupBinding(library, group);
                if (header != null)
                {
                    string arrow = collapsedGroups.Contains(group) ? "    >" : "    v";
                    SetText(header.label, GroupLabel(group) + "  ·  " + groupItems.Count + arrow);
                    if (header.button != null)
                    {
                        header.button.gameObject.SetActive(true);
                        BindButton(header.button, () => ToggleGroup(group));
                        header.button.transform.SetSiblingIndex(siblingIndex++);
                    }
                }

                if (collapsedGroups.Contains(group))
                    continue;

                foreach (MockupWordList item in groupItems)
                {
                    if (authoredById.TryGetValue(item.Id, out WordListHubSceneBindings.LibraryRowBinding authored))
                    {
                        if (authored.root != null)
                        {
                            authored.root.SetActive(true);
                            authored.root.transform.SetSiblingIndex(siblingIndex++);
                        }
                    }
                    else if (dynamicById.TryGetValue(item.Id, out WordListHubLibraryRowView dynamicView))
                    {
                        dynamicView.gameObject.SetActive(true);
                        dynamicView.transform.SetSiblingIndex(siblingIndex++);
                    }
                }
            }

            if (library.emptyNotice != null)
            {
                library.emptyNotice.text = "No lists match these filters.";
                library.emptyNotice.gameObject.SetActive(items.Count == 0);
            }

            UpdateLibraryDetail(items);
            UpdateLibrarySelectionVisuals(library, dynamicById);
            UpdateFooter();
            ScheduleLibraryScrollRestore(savedScrollY, moveScrollToTop, revealId);
        }

        private void SyncLibraryControlValues(WordListHubSceneBindings.LibraryPageBinding library)
        {
            if (library.searchInput != null)
                library.searchInput.SetTextWithoutNotify(librarySearchText);
            if (library.languageFilter != null)
            {
                SetDropdownOptions(library.languageFilter,
                    new[] { "Language: All", "Language: English", "Language: Chinese", "Language: Mixed" },
                    (int)query.LanguageFilter);
                BindDropdown(library.languageFilter, SetLanguageFilterFromDropdown);
            }
            if (library.playedFilter != null)
            {
                SetDropdownOptions(library.playedFilter,
                    new[] { "Played: All", "Played: Yes", "Played: No" }, (int)query.PlayedFilter);
                BindDropdown(library.playedFilter, SetPlayedFilterFromDropdown);
            }
            if (library.sortFilter != null)
            {
                SetDropdownOptions(library.sortFilter,
                    new[] { "Sort: Recently played", "Sort: Name", "Sort: Date added", "Sort: Date modified", "Sort: Word count", "Sort: Personal stars" },
                    (int)query.SortMode);
                BindDropdown(library.sortFilter, SetSortModeFromDropdown);
            }
            if (library.directionFilter != null)
            {
                SetDropdownOptions(library.directionFilter, new[] { "Ascending", "Descending" },
                    (int)query.SortDirection);
                BindDropdown(library.directionFilter, SetSortDirectionFromDropdown);
            }
            SyncFilterMenu(library.filtersMenu, query);
            if (library.allLists != null)
                SetButtonLabel(library.allLists,
                    query.View == MockupLibraryView.Favorites ? "Show All" : "Favourite");
        }

        private static void SetDropdownOptions(TMP_Dropdown dropdown, IEnumerable<string> options, int value)
        {
            if (dropdown == null)
                return;
            dropdown.ClearOptions();
            dropdown.AddOptions(options.ToList());
            dropdown.SetValueWithoutNotify(Mathf.Clamp(value, 0, Mathf.Max(0, dropdown.options.Count - 1)));
            dropdown.RefreshShownValue();
        }

        private WordListHubSceneBindings.GroupBinding FindGroupBinding(
            WordListHubSceneBindings.LibraryPageBinding library,
            MockupLibraryGroup group)
        {
            return library.groups?.FirstOrDefault(binding => binding != null
                && string.Equals(binding.groupKey, group.ToString(), StringComparison.OrdinalIgnoreCase));
        }

        private WordListHubLibraryRowView InstantiateLibraryRow(
            WordListHubSceneBindings.LibraryPageBinding library,
            string listId)
        {
            if (library.rowTemplate == null)
            {
                Debug.LogError("The Library row template is not assigned in WordListHubSceneBindings.", this);
                return null;
            }

            WordListHubLibraryRowView view = Instantiate(library.rowTemplate, library.listContent, false);
            view.name = "LibraryItem_" + listId;
            view.itemId = listId;
            if (view.GetComponent<WordListHubRuntimeInstance>() == null)
                view.gameObject.AddComponent<WordListHubRuntimeInstance>();
            return view;
        }

        private void UpdateLibraryRow(WordListHubSceneBindings.LibraryRowBinding binding, MockupWordList item)
        {
            SetText(binding.favoriteMark, item.IsFavorite ? "★" : "☆");
            SetText(binding.bestTime, BestTimeLabel(item));
            SetText(binding.title, item.Title);
            SetText(binding.metadata,
                LanguageLabel(item.Language) + "  ·  " + item.WordCount + " words  ·  " + RecordLabel(item));
            string badge = ListBadge(item);
            SetText(binding.badge, badge.ToUpperInvariant());
            if (binding.badgeRoot != null)
                binding.badgeRoot.SetActive(!string.IsNullOrEmpty(badge));
            BindButton(binding.button, () => SelectLibraryItem(item.Id));
            BindButton(binding.favoriteButton, () => ToggleFavorite(item.Id));
        }

        private void UpdateLibraryRow(WordListHubLibraryRowView view, MockupWordList item)
        {
            if (view == null)
                return;
            view.itemId = item.Id;
            SetText(view.favoriteMark, item.IsFavorite ? "★" : "☆");
            SetText(view.bestTime, BestTimeLabel(item));
            SetText(view.title, item.Title);
            SetText(view.metadata,
                LanguageLabel(item.Language) + "  ·  " + item.WordCount + " words  ·  " + RecordLabel(item));
            string badge = ListBadge(item);
            SetText(view.badge, badge.ToUpperInvariant());
            if (view.badgeRoot != null)
                view.badgeRoot.SetActive(!string.IsNullOrEmpty(badge));
            BindButton(view.button, () => SelectLibraryItem(item.Id));
            BindButton(view.favoriteButton, () => ToggleFavorite(item.Id));
        }

        private void UpdateLibrarySelectionVisuals(
            WordListHubSceneBindings.LibraryPageBinding library,
            IReadOnlyDictionary<string, WordListHubLibraryRowView> dynamicRows)
        {
            if (library.authoredRows != null)
            {
                foreach (WordListHubSceneBindings.LibraryRowBinding row in library.authoredRows)
                {
                    if (row != null)
                        SetSelectedVisual(row.selectedVisual, row.listId == selectedListId);
                }
            }

            if (dynamicRows == null)
                return;
            foreach (KeyValuePair<string, WordListHubLibraryRowView> pair in dynamicRows)
            {
                if (pair.Value != null)
                    SetSelectedVisual(pair.Value.selectedVisual, pair.Key == selectedListId);
            }
        }

        private void UpdateLibraryDetail(IReadOnlyList<MockupWordList> visibleItems)
        {
            WordListHubSceneBindings.LibraryPageBinding library = sceneBindings.library;
            MockupWordList item = store.FindById(selectedListId);
            if (item == null || !visibleItems.Any(candidate => candidate.Id == selectedListId))
            {
                item = visibleItems.FirstOrDefault();
                selectedListId = item != null ? item.Id : null;
            }

            if (item == null)
            {
                SetText(library.detailTitle, "Choose a list to see its details.");
                SetText(library.detailDescription, string.Empty);
                SetText(library.detailMetadata, string.Empty);
                SetText(library.recordStars, string.Empty);
                SetText(library.recordTime, string.Empty);
                SetText(library.detailWordsLabel, "WORDS  ·  0");
                SetText(library.detailWords, string.Empty);
                if (libraryRubyWordsView != null)
                    libraryRubyWordsView.SetWords(new[] { "Choose a list to see its details." });
                SetText(library.detailStatus, lastActionMessage);
                SetLibraryAction(library.delete, string.Empty, false, null);
                SetLibraryAction(library.play, string.Empty, false, null);
                SetLibraryAction(library.edit, string.Empty, false, null);
                SetLibraryAction(library.copyEditWords, string.Empty, false, null);
                SetLibraryAction(library.publish, string.Empty, false, null);
                SetLibraryAction(library.unpublish, string.Empty, false, null);
                return;
            }

            SetText(library.detailTitle, item.Title);
            SetText(library.detailDescription, string.IsNullOrWhiteSpace(item.Description)
                ? "A word list ready for your next practice session." : item.Description);
            string author = item.IsReadOnly
                ? (string.IsNullOrEmpty(item.RemoteAuthor) ? "COMMUNITY MEMBER" : item.RemoteAuthor)
                : "YOU";
            SetText(library.detailMetadata, "BY " + author);
            int earnedStars = Mathf.Clamp(item.PersonalStars ?? 0, 0, 5);
            SetText(library.recordStars, new string('★', earnedStars) + new string('☆', 5 - earnedStars));
            SetText(library.recordTime, item.BestTimeSeconds.HasValue
                ? item.BestTimeSeconds.Value.ToString("0.0") + " s" : "No run yet");
            SetText(library.detailWordsLabel, "WORDS  ·  " + item.WordCount);
            SetText(library.detailWords, item.Words == null || item.Words.Count == 0
                ? "No words in this list." : string.Join("\n", item.Words.Select(MockupWordContent.FormatForDetail)));
            if (libraryRubyWordsView != null)
                libraryRubyWordsView.SetWords(item.Words == null || item.Words.Count == 0
                    ? new[] { "No words in this list." } : item.Words);

            bool hasPublishedBinding = item.HasPublishedBinding;
            bool publicationIsActive = item.IsPubliclyVisible
                || item.PublicationStatus == MockupPublicationStatus.Published;
            SetText(library.detailStatus, lastActionMessage);
            SetLibraryAction(library.delete, "Delete",
                !publicationIsActive, () => RequestDeleteList(item.Id));
            SetLibraryAction(library.play, "Play", true, () => BeginLibraryMockPlay(item.Id));

            SetLibraryAction(library.edit, "Edit List",
                !item.IsReadOnly && !hasPublishedBinding,
                !item.IsReadOnly && !hasPublishedBinding
                    ? (UnityAction)(() => EditOrCopy(item.Id)) : null);
            UnityAction copyEditAction = null;
            if (item.IsReadOnly)
                copyEditAction = () => EditOrCopy(item.Id);
            else if (item.Ownership == MockupOwnership.Owned)
                copyEditAction = () => OpenEditDialog(item.Id, true);
            SetLibraryAction(library.copyEditWords, "Copy", copyEditAction != null,
                copyEditAction);

            SetLibraryAction(library.publish, hasPublishedBinding ? "Unpublish" : "Publish", !item.IsReadOnly,
                !item.IsReadOnly ? (UnityAction)(() => TogglePublication(item.Id)) : null);
            if (library.publish != null && library.publish.button != null)
                library.publish.button.interactable = !item.IsReadOnly;
            SetLibraryAction(library.unpublish, string.Empty, false, null);
        }

        private static void SetLibraryAction(
            WordListHubSceneBindings.ButtonBinding binding,
            string label,
            bool visible,
            UnityAction action)
        {
            if (binding == null)
                return;
            SetButtonLabel(binding, label);
            if (binding.button != null)
                binding.button.gameObject.SetActive(visible);
            BindButton(binding.button, visible ? action : null);
        }

        private static void SetButtonLabel(WordListHubSceneBindings.ButtonBinding binding, string label)
        {
            if (binding != null)
                SetText(binding.label, label);
        }

        private static void SetText(TMP_Text text, string value)
        {
            if (text != null)
                text.text = value ?? string.Empty;
        }

        private static void DestroyOwnedRuntimeChildren(Transform parent)
        {
            if (parent == null)
                return;
            foreach (WordListHubRuntimeInstance marker in parent.GetComponentsInChildren<WordListHubRuntimeInstance>(true))
            {
                if (marker == null || marker.transform.parent != parent)
                    continue;
                marker.gameObject.SetActive(false);
                if (Application.isPlaying)
                    Destroy(marker.gameObject);
                else
                    DestroyImmediate(marker.gameObject);
            }
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
            // Runtime row clones are destroyed at the end of the frame; wait until
            // their marked instances are gone before restoring the authored scroll offset.
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

            Transform row = sceneBindings.library.authoredRows
                .FirstOrDefault(binding => binding != null && binding.listId == listId)?.root?.transform;
            if (row == null)
            {
                row = libraryListContent.GetComponentsInChildren<WordListHubLibraryRowView>(true)
                    .FirstOrDefault(view => view != null && view.itemId == listId)?.transform;
            }
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
            if (sceneBindings == null || communityListContent == null || communityDetailPane == null)
                throw new InvalidOperationException("Community Scene bindings are missing.");

            WordListHubSceneBindings.CommunityPageBinding community = sceneBindings.community;
            Vector2 scrollPosition = community.listScroll != null && community.listScroll.content != null
                ? community.listScroll.content.anchoredPosition
                : Vector2.zero;
            if (community.searchInput != null)
                community.searchInput.SetTextWithoutNotify(communityQuery.SearchText);
            SyncFilterMenu(community.connectionMenu,
                kind => GetCommunityFilterValue(kind, communityQuery), "Filters");

            List<MockupDiscoveryItem> items = store.QueryDiscoveryWithFilters(communityQuery).ToList();
            if (string.IsNullOrEmpty(selectedDiscoveryId)
                || !items.Any(item => item.RemoteItemId == selectedDiscoveryId))
            {
                selectedDiscoveryId = items.FirstOrDefault() != null
                    ? items[0].RemoteItemId : null;
            }

            DestroyOwnedRuntimeChildren(community.listContent);
            var authoredById = community.authoredRows
                .Where(row => row != null && !string.IsNullOrEmpty(row.remoteItemId))
                .ToDictionary(row => row.remoteItemId, StringComparer.Ordinal);
            var dynamicById = new Dictionary<string, WordListHubDiscoveryRowView>(StringComparer.Ordinal);

            foreach (WordListHubSceneBindings.DiscoveryRowBinding row in community.authoredRows)
            {
                if (row == null)
                    continue;
                MockupDiscoveryItem item = store.FindDiscoveryItem(row.remoteItemId);
                if (item == null)
                {
                    if (row.root != null)
                        row.root.SetActive(false);
                    continue;
                }
                UpdateDiscoveryRow(row, item);
                if (row.root != null)
                    row.root.SetActive(false);
            }

            foreach (MockupDiscoveryItem item in items)
            {
                if (authoredById.ContainsKey(item.RemoteItemId))
                    continue;
                WordListHubDiscoveryRowView view = InstantiateDiscoveryRow(community, item.RemoteItemId);
                if (view != null)
                {
                    dynamicById[item.RemoteItemId] = view;
                    UpdateDiscoveryRow(view, item);
                    view.gameObject.SetActive(false);
                }
            }

            int siblingIndex = 0;
            foreach (MockupDiscoveryItem item in items)
            {
                if (authoredById.TryGetValue(item.RemoteItemId,
                    out WordListHubSceneBindings.DiscoveryRowBinding authored))
                {
                    if (authored.root != null)
                    {
                        authored.root.SetActive(true);
                        authored.root.transform.SetSiblingIndex(siblingIndex++);
                    }
                }
                else if (dynamicById.TryGetValue(item.RemoteItemId, out WordListHubDiscoveryRowView dynamicView))
                {
                    dynamicView.gameObject.SetActive(true);
                    dynamicView.transform.SetSiblingIndex(siblingIndex++);
                }
            }

            if (community.emptyNotice != null)
            {
                community.emptyNotice.text = "No community lists match these filters.";
                community.emptyNotice.gameObject.SetActive(items.Count == 0);
            }

            UpdateCommunityDetail(items);
            UpdateCommunitySelectionVisuals(community, dynamicById);
            UpdateFooter();
            RestoreCommunityScrollAfterLayout(community.listScroll, scrollPosition);
        }

        private void RefreshCurrentPage()
        {
            SetPageActive(currentPage);
            switch (currentPage)
            {
                case Page.Community:
                    RefreshCommunityPage();
                    break;
                case Page.Achievements:
                    RefreshAchievementsPage();
                    break;
                case Page.Challenges:
                    RefreshChallengesPage();
                    break;
                default:
                    RefreshLibraryPage();
                    break;
            }
        }

        private WordListHubDiscoveryRowView InstantiateDiscoveryRow(
            WordListHubSceneBindings.CommunityPageBinding community,
            string remoteItemId)
        {
            if (community.rowTemplate == null)
            {
                Debug.LogError("The Community row template is not assigned in WordListHubSceneBindings.", this);
                return null;
            }

            WordListHubDiscoveryRowView view = Instantiate(community.rowTemplate, community.listContent, false);
            view.name = "DiscoveryItem_" + remoteItemId;
            if (view.GetComponent<WordListHubRuntimeInstance>() == null)
                view.gameObject.AddComponent<WordListHubRuntimeInstance>();
            return view;
        }

        private void UpdateDiscoveryRow(WordListHubSceneBindings.DiscoveryRowBinding row, MockupDiscoveryItem item)
        {
            SetText(row.title, item.Title);
            SetText(row.metadata, LanguageLabel(item.Language) + "  ·  " + item.WordCount
                + " words  ·  " + item.LikeCount + " likes");
            SetText(row.summary, item.Description);
            SetText(row.status, item.Status == MockupDiscoveryStatus.New ? "NEW" : "UPDATED");
            BindButton(row.button, () => SelectDiscoveryItem(item.RemoteItemId));
        }

        private void UpdateDiscoveryRow(WordListHubDiscoveryRowView view, MockupDiscoveryItem item)
        {
            if (view == null)
                return;
            SetText(view.title, item.Title);
            SetText(view.metadata, LanguageLabel(item.Language) + "  ·  " + item.WordCount
                + " words  ·  " + item.LikeCount + " likes");
            SetText(view.summary, item.Description);
            SetText(view.status, item.Status == MockupDiscoveryStatus.New ? "NEW" : "UPDATED");
            BindButton(view.button, () => SelectDiscoveryItem(item.RemoteItemId));
        }

        private void UpdateCommunitySelectionVisuals(
            WordListHubSceneBindings.CommunityPageBinding community,
            IReadOnlyDictionary<string, WordListHubDiscoveryRowView> dynamicRows)
        {
            if (community.authoredRows != null)
            {
                foreach (WordListHubSceneBindings.DiscoveryRowBinding row in community.authoredRows)
                {
                    if (row != null)
                        SetSelectedVisual(row.selectedVisual, row.remoteItemId == selectedDiscoveryId);
                }
            }

            if (dynamicRows == null)
                return;
            foreach (KeyValuePair<string, WordListHubDiscoveryRowView> pair in dynamicRows)
            {
                if (pair.Value != null)
                    SetSelectedVisual(pair.Value.selectedVisual, pair.Key == selectedDiscoveryId);
            }
        }

        private static void SetSelectedVisual(GameObject selectedVisual, bool selected)
        {
            if (selectedVisual != null && selectedVisual.activeSelf != selected)
                selectedVisual.SetActive(selected);
        }

        private void UpdateCommunityDetail(IReadOnlyList<MockupDiscoveryItem> visibleItems)
        {
            WordListHubSceneBindings.CommunityPageBinding community = sceneBindings.community;
            MockupDiscoveryItem item = store.FindDiscoveryItem(selectedDiscoveryId);
            if (item == null || !visibleItems.Any(candidate => candidate.RemoteItemId == selectedDiscoveryId))
            {
                SetText(community.detailTitle, "Choose a list to see its details.");
                SetText(community.detailAuthor, string.Empty);
                SetText(community.detailSummary, string.Empty);
                if (communityRubyWordsView != null)
                    communityRubyWordsView.SetWords(Array.Empty<string>());
                SetText(community.detailDescription, string.Empty);
                SetText(community.detailMetadata, string.Empty);
                SetText(community.detailTrustNote, string.Empty);
                SetCommunityAction(community.like, string.Empty, false, null);
                SetCommunityAction(community.download, string.Empty, false, null);
                SetCommunityAction(community.play, string.Empty, false, null);
                SetCommunityAction(community.report, string.Empty, false, null);
                return;
            }

            SetText(community.detailTitle, item.Title);
            SetText(community.detailAuthor, "BY  " + item.RemoteAuthor);
            SetText(community.detailDescription, item.Description);
            SetText(community.detailMetadata, "SAMPLE CONTENT  ·  " + LanguageLabel(item.Language)
                + "  ·  " + item.WordCount + " words");
            List<string> previewWords = item.Words == null
                ? new List<string>() : item.Words.Take(8).ToList();
            SetText(community.detailSummary, previewWords.Count == 0
                ? string.Empty : string.Join("    ", previewWords.Select(MockupWordContent.FormatForDetail)));
            if (communityRubyWordsView != null)
                communityRubyWordsView.SetWords(previewWords);
            SetText(community.detailTrustNote,
                "Downloads are read-only and appear in Community Downloads. Likes are separate.");
            SetCommunityAction(community.like,
                (item.IsLiked ? "♥ Liked" : "♡ Like") + "  ·  " + item.LikeCount,
                true, () => ToggleLike(item.RemoteItemId));
            SetCommunityAction(community.download,
                item.IsDownloaded ? "Downloaded" : "Download",
                true, () => DownloadDiscoveryItem(item.RemoteItemId));
            if (community.download != null && community.download.button != null)
                community.download.button.interactable = !item.IsDownloaded;
            SetCommunityAction(community.play, "Play", true,
                () => BeginMockPlay(item.RemoteItemId));
            SetCommunityAction(community.report, "Report & hide for me", true,
                () => ReportAndHideDiscoveryItem(item.RemoteItemId));
        }

        private static void SetCommunityAction(
            WordListHubSceneBindings.ButtonBinding binding,
            string label,
            bool visible,
            UnityAction action)
        {
            if (binding == null)
                return;
            SetButtonLabel(binding, label);
            if (binding.button != null)
                binding.button.gameObject.SetActive(visible);
            BindButton(binding.button, visible ? action : null);
        }

        private void RefreshAchievementsPage()
        {
            WordListHubSceneBindings.AchievementsPageBinding achievements = sceneBindings.achievements;
            if (achievements == null || achievements.cardContent == null || achievements.cardTemplate == null)
                throw new InvalidOperationException("Achievement Scene template bindings are missing.");

            SetDropdownOptions(achievements.filter,
                new[] { "Show: All", "Show: Unlocked", "Show: In progress" }, (int)achievementFilter);
            BindDropdown(achievements.filter, SetAchievementFilterFromDropdown);
            string achievementFilterLabel = achievementFilter == AchievementFilter.All ? "All"
                : achievementFilter == AchievementFilter.Unlocked ? "Unlocked" : "In progress";
            SyncFilterMenu(
                achievements.filterMenu,
                kind => kind == WordListHubSceneBindings.LibraryFilterKind.AchievementStatus
                    ? (int)achievementFilter : -1,
                "Show: " + achievementFilterLabel);
            DestroyOwnedRuntimeChildren(achievements.cardContent);

            IEnumerable<MockupAchievement> filtered = store.Achievements;
            if (achievementFilter == AchievementFilter.Unlocked)
                filtered = filtered.Where(item => item.Status == MockupAchievementStatus.Unlocked);
            else if (achievementFilter == AchievementFilter.InProgress)
                filtered = filtered.Where(item => item.Status == MockupAchievementStatus.InProgress);

            foreach (MockupAchievement achievement in filtered)
            {
                WordListHubAchievementCardView view = Instantiate(
                    achievements.cardTemplate, achievements.cardContent, false);
                view.name = "AchievementCard_" + achievement.Id;
                if (view.GetComponent<WordListHubRuntimeInstance>() == null)
                    view.gameObject.AddComponent<WordListHubRuntimeInstance>();
                Sprite icon = AchievementIcon(achievement);
                if (view.icon != null && icon != null)
                    view.icon.sprite = icon;
                SetText(view.state, achievement.Status.ToString().Replace("InProgress", "In Progress")
                    .ToUpperInvariant());
                SetText(view.title, achievement.Title);
                SetText(view.description, achievement.Description);
                SetText(view.progress, achievement.ProgressText);
                view.gameObject.SetActive(true);
            }
            UpdateFooter();
        }

        private void RefreshChallengesPage()
        {
            SetText(sceneBindings.challenges.description,
                "Challenge selection stays in the existing Levels screen.\n"
                + "This isolated preview focuses on your word library, community lists and achievements.");
            SetButtonLabel(sceneBindings.challenges.dailyChallenge, "Open Word List");
            SetText(sceneBindings.challenges.status, string.Empty);
            UpdateFooter();
        }

        private void RestoreCommunityScrollAfterLayout(ScrollRect scroll, Vector2 anchoredPosition)
        {
            if (scroll == null || scroll.content == null)
                return;
            if (communityScrollRoutine != null)
                StopCoroutine(communityScrollRoutine);
            if (Application.isPlaying)
                communityScrollRoutine = StartCoroutine(RestoreCommunityScrollAfterLayoutRoutine(scroll, anchoredPosition));
            else
                RestoreCommunityScroll(scroll, anchoredPosition);
        }

        private IEnumerator RestoreCommunityScrollAfterLayoutRoutine(ScrollRect scroll, Vector2 anchoredPosition)
        {
            yield return null;
            RestoreCommunityScroll(scroll, anchoredPosition);
            communityScrollRoutine = null;
        }

        private static void RestoreCommunityScroll(ScrollRect scroll, Vector2 anchoredPosition)
        {
            if (scroll == null || scroll.content == null)
                return;
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
            scroll.content.anchoredPosition = anchoredPosition;
            scroll.velocity = Vector2.zero;
        }

        private void UpdateFooter()
        {
            SetText(footerText, "LOCAL MOCKUP  ·  SAMPLE DATA ONLY");
        }

        private void ShowActionFeedback(string message, bool isError = false)
        {
            lastActionMessage = string.IsNullOrWhiteSpace(message) ? string.Empty : message;
            if (string.IsNullOrEmpty(lastActionMessage))
            {
                ClearActionFeedback();
                return;
            }

            actionFeedbackExpiresAt = Time.unscaledTime
                + (isError ? FailedActionFeedbackDuration : SuccessfulActionFeedbackDuration);
            SetText(sceneBindings != null && sceneBindings.library != null
                ? sceneBindings.library.detailStatus : null, lastActionMessage);
        }

        private void ClearActionFeedback()
        {
            lastActionMessage = string.Empty;
            actionFeedbackExpiresAt = 0f;
            SetText(sceneBindings != null && sceneBindings.library != null
                ? sceneBindings.library.detailStatus : null, string.Empty);
        }

        private void OpenNewListDialog()
        {
            newListLanguage = MockupLanguage.English;
            OpenDialog("Create a word list", 680f, 700f);
            DrawNewListDialog();
        }

        private void DrawNewListDialog()
        {
            WordListHubSceneBindings.FormBinding form = sceneBindings.modal.newList;
            if (form == null || form.root == null)
                throw new InvalidOperationException("The New List form is missing from the scene bindings.");

            form.root.SetActive(true);
            if (newListTitleInput != null)
                newListTitleInput.SetTextWithoutNotify(string.Empty);
            if (newListDescriptionInput != null)
                newListDescriptionInput.SetTextWithoutNotify(string.Empty);
            if (newListTagsInput != null)
                newListTagsInput.SetTextWithoutNotify(string.Empty);
            if (newListWordsInput != null)
                newListWordsInput.SetTextWithoutNotify(string.Empty);
            SetText(form.validationText, string.Empty);
            SetText(form.wordsLabel, WordsInputHint(newListLanguage));
            if (form.languageDropdown != null)
                SetDropdownOptions(form.languageDropdown,
                    new[] { "English", "Chinese", "Mixed" }, (int)newListLanguage);
        }

        private void SetNewListLanguageFromDropdown(int value)
        {
            newListLanguage = (MockupLanguage)Mathf.Clamp(value, 0,
                Enum.GetValues(typeof(MockupLanguage)).Length - 1);
            if (sceneBindings != null && sceneBindings.modal != null && sceneBindings.modal.newList != null)
                SetText(sceneBindings.modal.newList.wordsLabel, WordsInputHint(newListLanguage));
        }

        private void CreateNewList()
        {
            string title = newListTitleInput != null ? newListTitleInput.text.Trim() : string.Empty;
            string wordText = newListWordsInput != null ? newListWordsInput.text : string.Empty;
            List<string> words = ParseWords(wordText);
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
            if (!MockupWordContent.TryValidateWordText(wordText, null, out string pinyinError))
            {
                SetDialogValidation(pinyinError);
                return;
            }

            MockupWordList item = store.CreateList(title, newListLanguage, words);
            MockupWordListEdit details = store.CreateEditDraft(item.Id);
            details.Description = newListDescriptionInput != null ? newListDescriptionInput.text.Trim() : string.Empty;
            details.Tags = newListTagsInput != null ? newListTagsInput.text.Trim() : string.Empty;
            store.SaveEdit(item.Id, details);
            selectedListId = item.Id;
            ClearFiltersThatHide(item);
            communityQuery.SearchText = string.Empty;
            ShowActionFeedback("Created “" + item.Title + "” with " + item.Words.Count + " words in My Lists.");
            CloseDialog();
            currentPage = Page.Library;
            SetPageActive(currentPage);
            RefreshLibraryPage(false, true);
        }

        private void OpenEditDialog(string mockId, bool copyAsNewList = false)
        {
            MockupWordList item = store.FindById(mockId);
            if (item == null || item.IsReadOnly)
                return;

            editingListId = item.Id;
            bool hasRemoteIdentity = !string.IsNullOrEmpty(item.RemoteItemId);
            if (copyAsNewList && item.Ownership != MockupOwnership.Owned)
                return;
            if (hasRemoteIdentity && (!copyAsNewList || !item.HasPublishedBinding))
                return;

            copyEditingMode = copyAsNewList;
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

            WordListHubSceneBindings.FormBinding form = sceneBindings.modal.editList;
            if (form == null || form.root == null)
                throw new InvalidOperationException("The Edit List form is missing from the scene bindings.");

            string title = copyEditingMode
                ? "Create an editable copy"
                : "Edit your word list";
            OpenDialog(title, 0f, 0f);
            form.root.SetActive(true);
            editTitleInput = form.titleInput;
            editDescriptionInput = form.descriptionInput;
            editTagsInput = form.tagsInput;
            editWordsInput = form.wordsInput;
            editSaveButton = form.saveButton;
            editSaveHintText = form.saveHintText;
            dialogValidationText = form.validationText;

            if (editTitleInput != null)
                editTitleInput.SetTextWithoutNotify(editDraft.Title ?? string.Empty);
            if (editDescriptionInput != null)
                editDescriptionInput.SetTextWithoutNotify(editDraft.Description ?? string.Empty);
            if (editTagsInput != null)
                editTagsInput.SetTextWithoutNotify(editDraft.Tags ?? string.Empty);
            if (editWordsInput != null)
            {
                editWordsInput.SetTextWithoutNotify(WordsAsText(editDraft.Words));
                editWordsInput.interactable = true;
            }
            if (form.languageDropdown != null)
            {
                SetDropdownOptions(form.languageDropdown, new[] { "English", "Chinese", "Mixed" }, (int)editLanguage);
                form.languageDropdown.interactable = true;
            }
            SetText(form.wordsLabel, WordsInputHint(editLanguage));
            SetText(form.validationText, string.Empty);
            if (editSaveButton != null)
                SetText(form.saveButtonLabel, copyEditingMode ? "Save editable copy" : "Save changes");
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
            editSaveButton.interactable = true;
            if (editSaveHintText != null)
            {
                editSaveHintText.text = copyEditingMode
                    ? "This creates an independent My List. The original and its scores stay unchanged."
                    : hasGameplayChange
                        ? "Saving changed words clears the current record; the previous record stays in history."
                        : string.Empty;
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
            if (editWordsInput != null)
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
            string wordText = editWordsInput != null ? editWordsInput.text : string.Empty;
            if (editWordsInput != null)
                editDraft.Words = ParseWords(wordText);
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
            IEnumerable<string> unchangedLegacyWords = null;
            MockupWordList original = store.FindById(editingListId);
            unchangedLegacyWords = original != null ? original.Words : null;
            if (!MockupWordContent.TryValidateWordText(wordText, unchangedLegacyWords, out string pinyinError))
            {
                SetDialogValidation(pinyinError);
                return;
            }

            bool contentChanged = EditHasGameplayChange();
            if (contentChanged || copyEditingMode)
            {
                ShowConfirmation(
                    copyEditingMode ? "Create a separate local copy?" : "Confirm content change",
                    copyEditingMode
                        ? "A new My List will be created with an independent ID. The original and its scores stay unchanged."
                        : "This changes the list's playable words. Current stars and best time will be cleared for this version; the previous record stays in its history.",
                    copyEditingMode ? "Create copy" : "Save changed words",
                    "Keep editing", sceneBindings.modal.contentChange);
                return;
            }

            ApplyEdit();
        }

        private void ApplyEdit()
        {
            if (editDraft == null)
                return;
            bool creatingCopy = copyEditingMode;
            MockupEditResult result = creatingCopy
                ? store.SaveOwnedContentAsNewList(editingListId, editDraft)
                : store.SaveEdit(editingListId, editDraft);
            selectedListId = result.Item.Id;
            ShowActionFeedback(creatingCopy
                ? "Created a separate My List copy; the original and its scores remain unchanged."
                : result.ShowRecordWarning
                    ? "Saved new words. Current record cleared; the earlier version record was retained."
                    : "Saved list details. Personal record is unchanged.");
            editDraft = null;
            copyEditingMode = false;
            CloseDialog();
            currentPage = Page.Library;
            ClearFiltersThatHide(result.Item);
            SetPageActive(currentPage);
            RefreshLibraryPage(false, true);
        }

        private void OpenDialog(string title, float width, float height)
        {
            SetFiltersMenuOpen(false);
            WordListHubSceneBindings.ModalBinding modal = sceneBindings.modal;
            if (modal == null || modal.root == null)
                throw new InvalidOperationException("The authored modal layer is missing from the scene bindings.");

            dialogContentRoot = modal.root.transform;
            modal.root.SetActive(true);
            SetText(modal.title, title);
            SetModalSection(null);
        }

        private void SetModalSection(GameObject section)
        {
            WordListHubSceneBindings.ModalBinding modal = sceneBindings.modal;
            if (modal.newList != null) modal.newList.root?.SetActive(false);
            if (modal.editList != null) modal.editList.root?.SetActive(false);
            if (modal.contentChange != null) modal.contentChange.root?.SetActive(false);
            if (modal.deleteList != null) modal.deleteList.root?.SetActive(false);
            if (modal.playResult != null) modal.playResult.root?.SetActive(false);
            if (section != null)
                section.SetActive(true);
        }

        private void ShowConfirmation(
            string title,
            string message,
            string confirmLabel,
            string cancelLabel,
            WordListHubSceneBindings.ConfirmationBinding confirmation)
        {
            if (confirmation == null || confirmation.root == null)
                throw new InvalidOperationException("The requested confirmation panel is missing from the scene bindings.");

            OpenDialog(title, 0f, 0f);
            confirmation.root.SetActive(true);
            SetText(confirmation.message, message);
            SetText(confirmation.confirmLabel, confirmLabel);
            SetText(confirmation.cancelLabel, cancelLabel);
            if (confirmation.confirmButton != null)
                confirmation.confirmButton.gameObject.SetActive(true);
            if (confirmation.cancelButton != null)
                confirmation.cancelButton.gameObject.SetActive(!string.IsNullOrEmpty(cancelLabel));
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

        private static string WordsInputHint(MockupLanguage language)
        {
            switch (language)
            {
                case MockupLanguage.Chinese:
                    return "Words per line · e.g. 苹果 | ping guo";
                case MockupLanguage.Mixed:
                    return "Words per line · add pinyin after |";
                default:
                    return "Words (one per line)";
            }
        }

        private static bool WordsEqual(IEnumerable<string> left, IEnumerable<string> right)
        {
            return (left ?? Enumerable.Empty<string>()).SequenceEqual(
                right ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
        }

        private void CloseDialog()
        {
            if (sceneBindings != null && sceneBindings.modal != null && sceneBindings.modal.root != null)
            {
                sceneBindings.modal.root.SetActive(false);
                SetModalSection(null);
            }
            dialogContentRoot = null;
            editDraft = null;
        }
        private void RefreshLibraryAfterDialog()
        {
            CloseDialog();
            SetPageActive(Page.Library);
            RefreshLibraryPage();
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

        private static string BestTimeLabel(MockupWordList item)
        {
            return item.BestTimeSeconds.HasValue
                ? item.BestTimeSeconds.Value.ToString("0.0") + " s"
                : "—";
        }

        private static string ListBadge(MockupWordList item)
        {
            if (item.PublicationStatus == MockupPublicationStatus.Published)
                return "Published";
            if (item.DownloadStatus == MockupDownloadStatus.RecentlyAdded)
                return "Recently added";
            return string.Empty;
        }

        private static bool Contains(string value, string term)
        {
            return !string.IsNullOrEmpty(value)
                && value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    /// <summary>
    /// Runtime-only stacked word view for the mockup's existing detail text regions.
    /// It keeps the authored TMP components as data/accessibility fallbacks and creates
    /// per-character UI cells without depending on production gameplay assemblies.
    /// </summary>
    public sealed class MockupRubyWordsView
    {
        private const string LibraryRootName = "RuntimeRubyWords";
        private const string CommunityViewportName = "RuntimeRubyWordsViewport";
        private const string CommunityContentName = "RuntimeRubyWordsContent";
        private const string CommunityScrollbarName = "RuntimeRubyWordsScrollbar";
        private const float HorizontalInset = 2f;
        private const float CellGap = 3f;
        private const float WrapGap = 2f;
        private const float EntryGap = 7f;
        private const float ContentFontSizeIncrease = 2f;
        private const float PinyinFontRatio = 0.72f;
        private const float TextBandHeightRatio = 1.5f;
        private const float CommunityScrollbarGap = 2f;

        private readonly TMP_Text sourceText;
        private readonly RectTransform layoutRoot;
        private readonly ScrollRect scrollRect;
        private readonly VerticalLayoutGroup verticalLayout;
        private readonly List<string> words = new List<string>();
        private float bodyFontSize;
        private float pinyinFontSize;
        private float pinyinBandHeight;
        private float glyphBandHeight;
        private float cellHeight;
        private float lastWidth = -1f;

        public RectTransform LayoutRoot => layoutRoot;
        public ScrollRect ScrollRect => scrollRect;
        public TMP_Text LegacyText => sourceText;

        private MockupRubyWordsView(
            TMP_Text text,
            RectTransform root,
            ScrollRect scroll,
            VerticalLayoutGroup layout,
            float previewFontScale)
        {
            sourceText = text;
            layoutRoot = root;
            scrollRect = scroll;
            verticalLayout = layout;
            float baseBodyFontSize = Mathf.Max(12f, text.fontSize);
            float basePinyinFontSize = Mathf.Clamp(
                (baseBodyFontSize - ContentFontSizeIncrease) * PinyinFontRatio, 11f, 12f)
                + ContentFontSizeIncrease;
            float safePreviewFontScale = Mathf.Max(0.1f, previewFontScale);
            bodyFontSize = baseBodyFontSize * safePreviewFontScale;
            pinyinFontSize = basePinyinFontSize * safePreviewFontScale;
            pinyinBandHeight = Mathf.Ceil(Mathf.Max(15f, pinyinFontSize * TextBandHeightRatio)) + 2f;
            glyphBandHeight = Mathf.Ceil(Mathf.Max(18f, bodyFontSize * TextBandHeightRatio)) + 2f;
            cellHeight = pinyinBandHeight + glyphBandHeight;
        }

        public static MockupRubyWordsView CreateLibrary(TMP_Text legacyText, float previewFontScale)
        {
            if (legacyText == null || legacyText.transform.parent == null)
                return null;

            LayoutElement ignoredTextLayout = legacyText.GetComponent<LayoutElement>();
            if (ignoredTextLayout == null)
                ignoredTextLayout = legacyText.gameObject.AddComponent<LayoutElement>();
            ignoredTextLayout.ignoreLayout = true;
            ContentSizeFitter legacyFitter = legacyText.GetComponent<ContentSizeFitter>();
            if (legacyFitter != null)
                legacyFitter.enabled = false;
            legacyText.enabled = false;

            Transform contentParent = legacyText.transform.parent;
            RectTransform root = contentParent.Find(LibraryRootName) as RectTransform;
            if (root == null)
                root = CreateRectTransform(LibraryRootName, contentParent);
            ConfigureTopStretch(root);

            VerticalLayoutGroup layout = root.GetComponent<VerticalLayoutGroup>();
            if (layout == null)
                layout = root.gameObject.AddComponent<VerticalLayoutGroup>();
            ConfigureVerticalLayout(layout, EntryGap);

            LayoutElement rootElement = root.GetComponent<LayoutElement>();
            if (rootElement == null)
                rootElement = root.gameObject.AddComponent<LayoutElement>();
            rootElement.minWidth = 0f;
            rootElement.flexibleWidth = 1f;

            ScrollRect existingScroll = legacyText.GetComponentInParent<ScrollRect>(true);
            return new MockupRubyWordsView(legacyText, root, existingScroll, layout, previewFontScale);
        }

        public static MockupRubyWordsView CreateCommunity(
            TMP_Text legacyText,
            Scrollbar scrollbarTemplate,
            float previewFontScale)
        {
            if (legacyText == null)
                return null;
            if (scrollbarTemplate == null)
                throw new InvalidOperationException(
                    "Community stacked word previews require the Library detail scrollbar template.");

            RectTransform viewportRegion = legacyText.rectTransform;
            RectTransform viewport = viewportRegion.Find(CommunityViewportName) as RectTransform;
            if (viewport == null)
                viewport = CreateRectTransform(CommunityViewportName, viewportRegion);
            ConfigureFullStretch(viewport);

            Image inputGraphic = viewport.GetComponent<Image>();
            if (inputGraphic == null)
            {
                inputGraphic = viewport.gameObject.AddComponent<Image>();
                inputGraphic.color = new Color(1f, 1f, 1f, 0f);
            }
            inputGraphic.raycastTarget = true;

            RectMask2D mask = viewport.GetComponent<RectMask2D>();
            if (mask == null)
                viewport.gameObject.AddComponent<RectMask2D>();

            ScrollRect scroll = viewport.GetComponent<ScrollRect>();
            if (scroll == null)
                scroll = viewport.gameObject.AddComponent<ScrollRect>();

            RectTransform content = viewport.Find(CommunityContentName) as RectTransform;
            if (content == null)
                content = CreateRectTransform(CommunityContentName, viewport);
            ConfigureTopStretch(content);

            VerticalLayoutGroup layout = content.GetComponent<VerticalLayoutGroup>();
            if (layout == null)
                layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            ConfigureVerticalLayout(layout, EntryGap);

            ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
            if (fitter == null)
                fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            Scrollbar communityScrollbar = CreateCommunityScrollbar(viewportRegion, scrollbarTemplate);
            RectTransform scrollbarRect = communityScrollbar.transform as RectTransform;
            RectTransform templateScrollbarRect = scrollbarTemplate.transform as RectTransform;
            float scrollbarWidth = Mathf.Max(templateScrollbarRect.rect.width, templateScrollbarRect.sizeDelta.x);
            scrollbarRect.anchorMin = new Vector2(1f, 0f);
            scrollbarRect.anchorMax = Vector2.one;
            scrollbarRect.pivot = new Vector2(1f, 1f);
            scrollbarRect.anchoredPosition = Vector2.zero;
            scrollbarRect.sizeDelta = new Vector2(scrollbarWidth, 0f);
            content.sizeDelta = new Vector2(-(scrollbarWidth + CommunityScrollbarGap), content.sizeDelta.y);

            LayoutElement contentElement = content.GetComponent<LayoutElement>();
            if (contentElement == null)
                contentElement = content.gameObject.AddComponent<LayoutElement>();
            contentElement.minWidth = 0f;
            contentElement.flexibleWidth = 1f;

            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = false;
            scroll.scrollSensitivity = 40f;
            scroll.horizontalScrollbar = null;
            scroll.verticalScrollbar = communityScrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            scroll.verticalScrollbarSpacing = 0f;

            legacyText.enabled = false;
            return new MockupRubyWordsView(legacyText, content, scroll, layout, previewFontScale);
        }

        private static Scrollbar CreateCommunityScrollbar(RectTransform communityRegion, Scrollbar template)
        {
            Transform existing = communityRegion.Find(CommunityScrollbarName);
            Scrollbar scrollbar = existing == null ? null : existing.GetComponent<Scrollbar>();
            if (scrollbar != null)
                return scrollbar;

            GameObject clone = UnityEngine.Object.Instantiate(template.gameObject, communityRegion, false);
            clone.name = CommunityScrollbarName;
            clone.SetActive(true);
            scrollbar = clone.GetComponent<Scrollbar>();
            if (scrollbar == null)
                throw new InvalidOperationException("The Library detail scrollbar template has no Scrollbar component.");

            scrollbar.onValueChanged.RemoveAllListeners();
            scrollbar.direction = template.direction;
            return scrollbar;
        }

        public void SetWords(IReadOnlyList<string> nextWords)
        {
            if (WordsEqual(words, nextWords))
            {
                RefreshLayoutIfNeeded();
                return;
            }

            words.Clear();
            if (nextWords != null)
            {
                for (int i = 0; i < nextWords.Count; i++)
                    words.Add(nextWords[i] ?? string.Empty);
            }

            RebuildEntries();
        }

        public void RefreshLayoutIfNeeded()
        {
            float width = AvailableWidth();
            if (Mathf.Abs(width - lastWidth) > 0.5f)
                RebuildEntries();
        }

        private void RebuildEntries()
        {
            float width = AvailableWidth();
            ClearChildren(layoutRoot);
            float entriesHeight = 0f;
            int entryCount = 0;
            for (int wordIndex = 0; wordIndex < words.Count; wordIndex++)
            {
                if (words[wordIndex].Length == 0)
                    continue;

                RectTransform entryRoot = CreateRectTransform("WordEntry", layoutRoot);
                entryRoot.anchorMin = new Vector2(0f, 1f);
                entryRoot.anchorMax = new Vector2(1f, 1f);
                entryRoot.pivot = new Vector2(0f, 1f);
                LayoutElement entryElement = entryRoot.gameObject.AddComponent<LayoutElement>();
                entryElement.minHeight = cellHeight;
                entryElement.flexibleHeight = 0f;

                List<TokenData> tokens = BuildTokens(words[wordIndex]);
                var cells = new List<CellView>();
                foreach (TokenData token in tokens)
                {
                    if (!token.IsHan && MeasureWidth(token.Text) > width)
                    {
                        foreach (string textElement in SplitTextElements(token.Text))
                            cells.Add(CreateCell(entryRoot, new TokenData(textElement, string.Empty, false, true)));
                    }
                    else
                    {
                        cells.Add(CreateCell(entryRoot, token));
                    }
                }

                entriesHeight += LayoutEntry(entryRoot, entryElement, cells, width);
                entryCount++;
            }

            float rootHeight = entriesHeight
                + Mathf.Max(0, entryCount - 1) * verticalLayout.spacing
                + verticalLayout.padding.vertical;
            LayoutElement rootElement = layoutRoot.GetComponent<LayoutElement>();
            if (rootElement != null)
                rootElement.preferredHeight = rootHeight;
            if (scrollRect == null || scrollRect.content != layoutRoot)
                layoutRoot.sizeDelta = new Vector2(layoutRoot.sizeDelta.x, rootHeight);

            lastWidth = width;
            LayoutRebuilder.MarkLayoutForRebuild(layoutRoot);
            if (layoutRoot.gameObject.activeInHierarchy)
                LayoutRebuilder.ForceRebuildLayoutImmediate(layoutRoot);
            RectTransform layoutParent = layoutRoot.parent as RectTransform;
            if (layoutParent != null && layoutParent.gameObject.activeInHierarchy)
                LayoutRebuilder.ForceRebuildLayoutImmediate(layoutParent);
            if (scrollRect != null)
            {
                if (scrollRect.content != null && scrollRect.content.gameObject.activeInHierarchy)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(scrollRect.content);
                scrollRect.verticalNormalizedPosition = 1f;
            }
        }

        private float LayoutEntry(
            RectTransform entryRoot,
            LayoutElement entryElement,
            List<CellView> cells,
            float availableWidth)
        {
            float cursorX = 0f;
            float cursorY = 0f;
            int rowCount = 1;
            foreach (CellView cell in cells)
            {
                float cellWidth = Mathf.Min(cell.Width, availableWidth);
                if (cursorX > 0f && cursorX + cellWidth > availableWidth)
                {
                    cursorX = 0f;
                    cursorY += cellHeight + WrapGap;
                    rowCount++;
                }

                cell.Root.sizeDelta = new Vector2(cellWidth, cellHeight);
                cell.Root.anchoredPosition = new Vector2(cursorX, -cursorY);
                cursorX += cellWidth + cell.GapAfter;
            }

            float height = rowCount * cellHeight + (rowCount - 1) * WrapGap;
            entryRoot.sizeDelta = new Vector2(0f, height);
            entryElement.preferredHeight = height;
            return height;
        }

        private CellView CreateCell(RectTransform parent, TokenData token)
        {
            RectTransform cellRoot = CreateRectTransform(token.IsHan ? "HanCell" : "TextCell", parent);
            cellRoot.anchorMin = new Vector2(0f, 1f);
            cellRoot.anchorMax = new Vector2(0f, 1f);
            cellRoot.pivot = new Vector2(0f, 1f);

            TMP_Text pinyinLabel = CreateLabel(cellRoot, "Pinyin", true);
            pinyinLabel.text = token.IsHan ? token.Pinyin : string.Empty;
            pinyinLabel.fontSize = pinyinFontSize;
            pinyinLabel.fontStyle = FontStyles.Bold;

            TMP_Text glyphLabel = CreateLabel(cellRoot, "Glyph", false);
            glyphLabel.text = token.Text;
            glyphLabel.fontSize = bodyFontSize;

            RectTransform pinyinRect = pinyinLabel.rectTransform;
            pinyinRect.anchorMin = new Vector2(0f, 1f);
            pinyinRect.anchorMax = new Vector2(1f, 1f);
            pinyinRect.pivot = new Vector2(0.5f, 1f);
            pinyinRect.sizeDelta = new Vector2(0f, pinyinBandHeight);
            pinyinRect.anchoredPosition = Vector2.zero;

            RectTransform glyphRect = glyphLabel.rectTransform;
            glyphRect.anchorMin = new Vector2(0f, 0f);
            glyphRect.anchorMax = new Vector2(1f, 0f);
            glyphRect.pivot = new Vector2(0.5f, 0f);
            glyphRect.sizeDelta = new Vector2(0f, glyphBandHeight);
            glyphRect.anchoredPosition = Vector2.zero;

            float glyphWidth = token.IsHan
                ? MeasureLabelWidth(glyphLabel, token.Text)
                : MeasureTextRunWidth(glyphLabel, token.Text);
            float pinyinWidth = token.IsHan ? MeasureLabelWidth(pinyinLabel, token.Pinyin) : 0f;
            float width = Mathf.Max(glyphWidth, pinyinWidth);
            width += token.IsHan ? 6f : token.IsBreakCluster ? 0f : 2f;
            if (!token.IsHan && string.IsNullOrWhiteSpace(token.Text))
                width = Mathf.Max(width, bodyFontSize * 0.3f);

            return new CellView(cellRoot, width, token.IsHan ? CellGap : 0f);
        }

        private TMP_Text CreateLabel(RectTransform parent, string name, bool pinyin)
        {
            var textObject = new GameObject(name,
                typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);
            TMP_Text label = textObject.GetComponent<TMP_Text>();
            label.font = sourceText.font;
            if (sourceText.fontSharedMaterial != null)
                label.fontSharedMaterial = sourceText.fontSharedMaterial;
            label.color = sourceText.color;
            label.alignment = TextAlignmentOptions.Center;
            label.richText = false;
            label.enableAutoSizing = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            label.raycastTarget = false;
            label.margin = Vector4.zero;
            if (!pinyin)
                label.fontStyle = FontStyles.Normal;
            return label;
        }

        private List<TokenData> BuildTokens(string word)
        {
            MockupWordContent.GetPresentationParts(
                word, out string displayText, out string[] pinyinSyllables, out bool hasUnannotatedHan);
            var tokens = new List<TokenData>();
            var nonHanRun = new StringBuilder();
            int pinyinIndex = 0;

            Action flushRun = () =>
            {
                if (nonHanRun.Length == 0)
                    return;
                tokens.Add(new TokenData(nonHanRun.ToString(), string.Empty, false));
                nonHanRun.Length = 0;
            };

            foreach (char character in displayText)
            {
                if (!MockupWordContent.IsHanCharacter(character))
                {
                    nonHanRun.Append(character);
                    continue;
                }

                flushRun();
                string pinyin = pinyinIndex < pinyinSyllables.Length
                    ? pinyinSyllables[pinyinIndex]
                    : hasUnannotatedHan ? "?" : string.Empty;
                tokens.Add(new TokenData(character.ToString(), pinyin, true));
                pinyinIndex++;
            }

            flushRun();
            return tokens;
        }

        private float AvailableWidth()
        {
            float width = layoutRoot.rect.width;
            if (width <= 1f)
                width = sourceText.rectTransform.rect.width;
            if (width <= 1f && layoutRoot.parent is RectTransform parentRect)
                width = parentRect.rect.width;
            return Mathf.Max(1f, width - verticalLayout.padding.horizontal - HorizontalInset * 2f);
        }

        private float MeasureWidth(string value)
        {
            if (string.IsNullOrEmpty(value))
                return 0f;
            return MeasureTextRunWidth(sourceText, value);
        }

        private float MeasureTextRunWidth(TMP_Text text, string value)
        {
            int trailingSpaces = 0;
            for (int i = value.Length - 1; i >= 0 && char.IsWhiteSpace(value[i]); i--)
                trailingSpaces++;

            string visibleText = trailingSpaces == 0 ? value : value.Substring(0, value.Length - trailingSpaces);
            float width = string.IsNullOrEmpty(visibleText)
                ? 0f : text.GetPreferredValues(visibleText, 100000f, 100000f).x;
            if (trailingSpaces > 0)
            {
                float measuredSpace = text.GetPreferredValues(" ", 100000f, 100000f).x;
                float spaceAdvance = Mathf.Max(measuredSpace, bodyFontSize * 0.28f);
                width += trailingSpaces * spaceAdvance;
            }
            return width;
        }

        private static float MeasureLabelWidth(TMP_Text label, string value)
        {
            if (string.IsNullOrEmpty(value))
                return 0f;
            return label.GetPreferredValues(value, 100000f, 100000f).x;
        }

        private static List<string> SplitTextElements(string value)
        {
            var elements = new List<string>();
            TextElementEnumerator enumerator = StringInfo.GetTextElementEnumerator(value);
            while (enumerator.MoveNext())
                elements.Add(enumerator.GetTextElement());
            return elements;
        }

        private static RectTransform CreateRectTransform(string name, Transform parent)
        {
            var gameObject = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)gameObject.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static void ConfigureTopStretch(RectTransform rect)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(0f, 0f);
        }

        private static void ConfigureFullStretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
        }

        private static void ConfigureVerticalLayout(VerticalLayoutGroup layout, float spacing)
        {
            layout.padding = new RectOffset(2, 2, 2, 2);
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
        }

        private static bool WordsEqual(IReadOnlyList<string> left, IReadOnlyList<string> right)
        {
            int rightCount = right == null ? 0 : right.Count;
            if (left.Count != rightCount)
                return false;
            for (int i = 0; i < left.Count; i++)
            {
                string rightWord = right[i] ?? string.Empty;
                if (!string.Equals(left[i], rightWord, StringComparison.Ordinal))
                    return false;
            }
            return true;
        }

        private static void ClearChildren(RectTransform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                GameObject child = parent.GetChild(i).gameObject;
                child.SetActive(false);
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(child);
                else
                    UnityEngine.Object.DestroyImmediate(child);
            }
        }

        private sealed class TokenData
        {
            public readonly string Text;
            public readonly string Pinyin;
            public readonly bool IsHan;
            public readonly bool IsBreakCluster;

            public TokenData(string text, string pinyin, bool isHan, bool isBreakCluster = false)
            {
                Text = text;
                Pinyin = pinyin;
                IsHan = isHan;
                IsBreakCluster = isBreakCluster;
            }
        }

        private sealed class CellView
        {
            public readonly RectTransform Root;
            public readonly float Width;
            public readonly float GapAfter;

            public CellView(RectTransform root, float width, float gapAfter)
            {
                Root = root;
                Width = width;
                GapAfter = gapAfter;
            }
        }
    }
}
