using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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

        private readonly HashSet<MockupLibraryGroup> collapsedGroups =
            new HashSet<MockupLibraryGroup>();

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
        private string pendingPlayListId;
        private Coroutine communityScrollRoutine;

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
            BindSceneEvents();
            SetPageActive(currentPage);
            RefreshCurrentPage();
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
            BindButton(library.allLists, () => SetLibraryView(MockupLibraryView.All));
            BindButton(library.favorites, () => SetLibraryView(MockupLibraryView.Favorites));
            BindButton(library.createList, OpenNewListDialog);
            BindInput(library.searchInput, HandleSearchChanged);
            BindDropdown(library.languageFilter, SetLanguageFilterFromDropdown);
            BindDropdown(library.playedFilter, SetPlayedFilterFromDropdown);
            BindDropdown(library.sortFilter, SetSortModeFromDropdown);
            BindDropdown(library.directionFilter, SetSortDirectionFromDropdown);

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
            BindDropdown(community.connectionState, SetMockOnlineFromDropdown);
            BindInput(community.searchInput, HandleCommunitySearchChanged);
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
            newListLanguage = MockupLanguage.English;
            editLanguage = MockupLanguage.English;
            copyEditingMode = false;
            SetPageActive(currentPage);
            RefreshCurrentPage();
        }

        private void ChangePage(Page page)
        {
            currentPage = page;
            lastActionMessage = string.Empty;
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
            lastActionMessage = string.Empty;
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
            SetPageActive(currentPage);
            RefreshLibraryPage();
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
            WordListHubSceneBindings.ConfirmationBinding dialog = sceneBindings.modal.playResult;
            if (!result.Succeeded || result.List == null)
            {
                pendingPlayListId = null;
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
            lastActionMessage = result.Message;
            ShowConfirmation("Mock play · " + result.List.Title,
                "This is a simulated play result only. No game scene will start.\n"
                + "The local list stays in its library section if you cancel.",
                "Complete mock run", "Cancel", dialog);
            BindButton(dialog.confirmButton, () => CompleteMockRun(pendingPlayListId));
            BindButton(dialog.cancelButton, () =>
            {
                pendingPlayListId = null;
                lastActionMessage = "Mock play cancelled. The local list remains in its library section.";
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
                lastActionMessage = "The local mock run could not be recorded.";
                RefreshCurrentPage();
                return;
            }

            lastActionMessage = "Mock run complete: 3 stars · 85.0 seconds. No game was launched.";
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
                lastActionMessage = "Created your editable copy. The downloaded original remains unchanged.";
            }

            OpenEditDialog(item.Id);
        }

        private void PublishList(string mockId)
        {
            MockupActionResult result = store.TryPublish(mockId);
            if (!result.Succeeded || result.List == null)
            {
                lastActionMessage = result.Message + " (mock action; no live service was contacted.)";
                RefreshLibraryPage();
                return;
            }

            selectedListId = result.List.Id;
            ClearFiltersThatHide(result.List);
            lastActionMessage = "Published this list in mock data.";
            RefreshLibraryPage();
        }

        private void Unpublish(string mockId)
        {
            MockupActionResult result = store.TryUnpublish(mockId);
            if (result.Succeeded && result.List != null)
            {
                selectedListId = result.List.Id;
                ClearFiltersThatHide(result.List);
                lastActionMessage = "Returned this list to My Lists in mock data; its previous community identity is no longer attached.";
            }
            else
            {
                lastActionMessage = result.Message + " (mock action; no live service was contacted.)";
            }
            RefreshLibraryPage();
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
                lastActionMessage = "Unpublish this list before deleting its local copy. No remote action was taken.";
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
            if (library.allLists != null)
                SetButtonLabel(library.allLists, "All Lists");
            if (library.favorites != null)
                SetButtonLabel(library.favorites, "★ Favorites");
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
            SetText(binding.title, item.Title);
            SetText(binding.metadata,
                LanguageLabel(item.Language) + "  ·  " + item.WordCount + " words  ·  " + RecordLabel(item));
            string badge = ListBadge(item);
            SetText(binding.badge, badge.ToUpperInvariant());
            if (binding.badgeRoot != null)
                binding.badgeRoot.SetActive(!string.IsNullOrEmpty(badge));
            BindButton(binding.button, () => SelectLibraryItem(item.Id));
        }

        private void UpdateLibraryRow(WordListHubLibraryRowView view, MockupWordList item)
        {
            if (view == null)
                return;
            view.itemId = item.Id;
            SetText(view.favoriteMark, item.IsFavorite ? "★" : "☆");
            SetText(view.title, item.Title);
            SetText(view.metadata,
                LanguageLabel(item.Language) + "  ·  " + item.WordCount + " words  ·  " + RecordLabel(item));
            string badge = ListBadge(item);
            SetText(view.badge, badge.ToUpperInvariant());
            if (view.badgeRoot != null)
                view.badgeRoot.SetActive(!string.IsNullOrEmpty(badge));
            BindButton(view.button, () => SelectLibraryItem(item.Id));
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
                SetText(library.recordNote, string.Empty);
                SetText(library.detailWordsLabel, "WORDS  ·  0");
                SetText(library.detailWords, string.Empty);
                SetText(library.detailStatus, string.Empty);
                SetLibraryAction(library.favorite, string.Empty, false, null);
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
            SetText(library.detailMetadata, "BY " + author + "   ·   " + GroupLabel(item.Group).ToUpperInvariant()
                + "   ·   " + LanguageLabel(item.Language) + "   ·   " + item.WordCount + " WORDS"
                + "   ·   REVISION " + item.ContentRevision);
            int earnedStars = Mathf.Clamp(item.PersonalStars ?? 0, 0, 5);
            SetText(library.recordStars, new string('★', earnedStars) + new string('☆', 5 - earnedStars));
            SetText(library.recordTime, item.BestTimeSeconds.HasValue
                ? item.BestTimeSeconds.Value.ToString("0.0") + " s" : "No run yet");
            SetText(library.recordNote, item.PreviousContentRecords.Count > 0
                ? "Earlier content version records are archived."
                : item.HasBeenPlayed ? "Best record for this content version." : "Finish a run to set your record.");
            SetText(library.detailWordsLabel, "WORDS  ·  " + item.WordCount);
            SetText(library.detailWords, item.Words == null || item.Words.Count == 0
                ? "No words in this list." : string.Join("\n", item.Words));

            bool publicationIsActive = item.IsPubliclyVisible
                || item.PublicationStatus == MockupPublicationStatus.Published;
            SetText(library.detailStatus, DetailFeedbackMessage(item, publicationIsActive));
            SetLibraryAction(library.favorite, item.IsFavorite ? "★ Saved" : "☆ Save", true,
                () => ToggleFavorite(item.Id));
            SetLibraryAction(library.delete, "Delete",
                !publicationIsActive, () => RequestDeleteList(item.Id));
            SetLibraryAction(library.play, "Play", true, () => BeginLibraryMockPlay(item.Id));

            bool hasPublishedBinding = item.Ownership == MockupOwnership.Owned
                && !string.IsNullOrEmpty(item.RemoteItemId);
            SetLibraryAction(library.edit, "Edit list",
                !item.IsReadOnly && !hasPublishedBinding,
                !item.IsReadOnly && !hasPublishedBinding
                    ? (UnityAction)(() => EditOrCopy(item.Id)) : null);
            UnityAction copyEditAction = null;
            if (item.IsReadOnly)
                copyEditAction = () => EditOrCopy(item.Id);
            else if (hasPublishedBinding)
                copyEditAction = () => OpenEditDialog(item.Id, true);
            SetLibraryAction(library.copyEditWords, "Copy & edit words", item.IsReadOnly || hasPublishedBinding,
                copyEditAction);

            bool isPublished = item.PublicationStatus == MockupPublicationStatus.Published;
            string publishLabel = isPublished ? "Published" : "Publish list";
            SetLibraryAction(library.publish, publishLabel, !item.IsReadOnly,
                !item.IsReadOnly && !isPublished
                    ? (UnityAction)(() => PublishList(item.Id)) : null);
            if (library.publish != null && library.publish.button != null)
                library.publish.button.interactable = !isPublished;
            SetLibraryAction(library.unpublish, "Unpublish", !item.IsReadOnly && hasPublishedBinding
                && isPublished, () => Unpublish(item.Id));
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
                community.searchInput.SetTextWithoutNotify(communitySearchText);
            if (community.connectionState != null)
            {
                SetDropdownOptions(community.connectionState,
                    new[] { "Mock network: Online", "Mock network: Offline" }, store.IsOnline ? 0 : 1);
                BindDropdown(community.connectionState, SetMockOnlineFromDropdown);
            }

            IEnumerable<MockupDiscoveryItem> queryResults = store.VisibleDiscoveryItems;
            if (!string.IsNullOrWhiteSpace(communitySearchText))
            {
                string term = communitySearchText.Trim();
                queryResults = queryResults.Where(item => Contains(item.Title, term)
                    || Contains(item.Description, term));
            }
            List<MockupDiscoveryItem> items = queryResults.ToList();
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
                community.emptyNotice.text = "No shared lists match this search.";
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
                SetText(community.detailTitle, "Choose a shared list to see its details.");
                SetText(community.detailAuthor, string.Empty);
                SetText(community.detailSummary, string.Empty);
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
            SetText(community.detailAuthor, "PUBLISHED BY  " + item.RemoteAuthor);
            SetText(community.detailDescription, item.Description);
            SetText(community.detailMetadata, "SAMPLE CONTENT  ·  " + LanguageLabel(item.Language)
                + "  ·  " + item.WordCount + " words");
            SetText(community.detailSummary, item.Words == null || item.Words.Count == 0
                ? string.Empty : string.Join("    ", item.Words.Take(8)));
            SetText(community.detailTrustNote,
                "Download adds a read-only copy to your library. It does not favorite the list.");
            SetCommunityAction(community.like,
                (item.IsLiked ? "♥ Liked" : "♡ Like") + "  ·  " + item.LikeCount,
                true, () => ToggleLike(item.RemoteItemId));
            SetCommunityAction(community.download,
                item.IsDownloaded ? "In Community Downloads" : "Download to Community Downloads",
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
            if (footerText != null)
            {
                string footerMessage = currentPage == Page.Library ? string.Empty : lastActionMessage;
                footerText.text = string.IsNullOrEmpty(footerMessage)
                    ? "LOCAL MOCKUP  ·  SAMPLE DATA ONLY"
                    : "LOCAL MOCKUP  ·  " + footerMessage + "  ·  SAMPLE DATA ONLY";
            }
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
            if (form.languageDropdown != null)
                SetDropdownOptions(form.languageDropdown,
                    new[] { "English", "Chinese", "Mixed" }, (int)newListLanguage);
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
            SetPageActive(currentPage);
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
            bool hasRemoteIdentity = !string.IsNullOrEmpty(item.RemoteItemId);
            if (hasRemoteIdentity && (!copyPublishedContentAsNewList || !hasPublishedBinding))
                return;

            copyEditingMode = copyPublishedContentAsNewList && hasPublishedBinding;
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
            SetText(form.wordsLabel, "Words (one per line)");
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
                    ? "This creates an independent My List. The published original and its scores stay unchanged."
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
            if (editWordsInput != null)
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
            if (contentChanged || copyEditingMode)
            {
                ShowConfirmation(
                    copyEditingMode ? "Create a separate local copy?" : "Confirm content change",
                    copyEditingMode
                        ? "A new My List will be created with an independent ID. The published original and its scores stay unchanged."
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
            bool creatingPublishedVersion = copyEditingMode;
            MockupEditResult result = creatingPublishedVersion
                ? store.SavePublishedContentAsNewList(editingListId, editDraft)
                : store.SaveEdit(editingListId, editDraft);
            selectedListId = result.Item.Id;
            lastActionMessage = creatingPublishedVersion
                ? "Created a separate My List copy; the published original and its scores remain unchanged."
                : result.ShowRecordWarning
                    ? "Saved new words. Current record cleared; the earlier version record was retained."
                    : "Saved list details. Personal record is unchanged.";
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

        private static string ListBadge(MockupWordList item)
        {
            if (item.PublicationStatus == MockupPublicationStatus.Published)
                return "Published";
            if (item.DownloadStatus == MockupDownloadStatus.RecentlyAdded)
                return "Recently added";
            return string.Empty;
        }

        private string DetailFeedbackMessage(MockupWordList item, bool publicationIsActive)
        {
            if (!string.IsNullOrEmpty(lastActionMessage))
                return lastActionMessage;

            var messages = new List<string>();
            string badge = ListBadge(item);
            if (!string.IsNullOrEmpty(badge))
                messages.Add(badge);

            if (item.PreviousContentRecords.Count > 0)
                messages.Add("Previous-version record archived.");
            else if (item.HasBeenPlayed)
                messages.Add("Best saved for this version.");
            else
                messages.Add("No personal best yet.");

            if (publicationIsActive && !item.IsReadOnly)
                messages.Add("Unpublish before deleting.");

            return string.Join("  ·  ", messages);
        }











        private static bool Contains(string value, string term)
        {
            return !string.IsNullOrEmpty(value)
                && value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
        }





    }
}
