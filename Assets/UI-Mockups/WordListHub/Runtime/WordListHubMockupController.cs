using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
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
        private const float ProductionCloseButtonSize = 44f;
        private const float ProductionCloseButtonGap = 8f;
        private const float ProductionCloseSafeMargin = 8f;

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
        [SerializeField, Tooltip("Production controllers receive an IWordListHubBackend from WordListHubGameBridge. Preview mode creates local fixtures.")]
        private bool productionMode;

        [Header("Word Preview")]
        [SerializeField, Min(0.1f), Tooltip(
            "Scale for the glyph and pinyin text in both stacked word previews. "
            + "Changes take effect when the mockup initializes.")]
        private float wordPreviewFontScale = 2f;

        private readonly HashSet<MockupLibraryGroup> collapsedGroups =
            new HashSet<MockupLibraryGroup>();

        private IWordListHubBackend backend;
        private AudioSource buttonClickAudioSource;
        // Kept as the preview-store field name for existing mock scene harnesses/tests.
        private MockupWordListStore store;
        private MockupWordListStore previewStore;
        private bool initialized;
        private bool hubVisible;
        private readonly Dictionary<Button, UnityAction> buttonListeners = new Dictionary<Button, UnityAction>();
        private readonly Dictionary<TMP_Dropdown, UnityAction<int>> dropdownListeners =
            new Dictionary<TMP_Dropdown, UnityAction<int>>();
        private readonly Dictionary<TMP_InputField, UnityAction<string>> inputListeners =
            new Dictionary<TMP_InputField, UnityAction<string>>();
        private MockupLibraryQuery query;
        private Page currentPage = Page.Library;
        private ColorBlock activeNavigationColors;
        private ColorBlock inactiveNavigationColors;
        private Color activeNavigationBackground;
        private Color inactiveNavigationBackground;
        private Color activeNavigationLabel;
        private Color inactiveNavigationLabel;
        private bool navigationColorsInitialized;
        private string libraryNavigationBaseLabel;
        private string communityNavigationBaseLabel;
        private AchievementFilter achievementFilter = AchievementFilter.All;
        private string selectedListId;
        private string selectedChallengeId;
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
        private MockupWordListEditSession editWordSession;
        private string editingListId;
        private TMP_InputField editTitleInput;
        private TMP_InputField editDescriptionInput;
        private TMP_InputField newListTitleInput;
        private TMP_InputField newListDescriptionInput;
        private TMP_InputField newListWordsInput;
        private TMP_InputField newListTagsInput;
        private TMP_InputField editWordsInput;
        private TMP_InputField editTagsInput;
        private MockupWordListEditorView editWordEditorView;
        private TMP_Text dialogValidationText;
        private TMP_Text editSaveHintText;
        private Button editSaveButton;
        private Coroutine libraryScrollRoutine;
        private MockupRubyWordsView libraryRubyWordsView;
        private MockupRubyWordsView communityRubyWordsView;
        private MockupRubyWordsView challengeRubyWordsView;
        private string pendingDeleteListId;
        private int pendingDeleteVisibleIndex = -1;
        private MockupLanguage newListLanguage = MockupLanguage.English;
        private MockupLanguage newListLanguageFallback = MockupLanguage.English;
        private MockupLanguage editLanguage = MockupLanguage.English;
        private MockupLanguage editLanguageFallback = MockupLanguage.English;
        private bool copyEditingMode;
        private string pendingPlayListId;
        private string pendingImportToken;
        private Coroutine communityScrollRoutine;
        private bool filtersMenuOpen;
        private WordListHubSceneBindings.FilterMenuBinding openFilterMenu;
        private float lastLibraryTileViewportWidth = -1f;
        private float lastCommunityTileViewportWidth = -1f;
        private float lastChallengeTileViewportWidth = -1f;
        private Vector3 productionCardBaseScale = Vector3.one;
        private float productionCardScaleFactor = 1f;
        private bool productionCardBaseScaleCaptured;

        private static readonly Color ListTileBackground =
            new Color(0.6117647f, 0.6117647f, 0.6117647f, 1f);
        private static readonly Color ListTileSelected =
            new Color(0.72156864f, 0.46666667f, 0.3529412f, 1f);
        private static readonly Color ListTileText =
            new Color(0.97f, 0.96f, 0.94f, 1f);
        private static readonly Color ListTileFavorite =
            new Color(1f, 0.82f, 0.38f, 1f);

        public event Action<string> PlayRequested;
        public event Action<bool> HubVisibilityChanged;
        public event Action HubCloseRequested;
        public event Action SettingsRequested;
        public event Action ImportRequested;
        public event Action<string> ExportRequested;
        public event Action<WordListHubPage> PageChanged;

        public bool IsProductionMode => productionMode;
        public bool IsHubVisible => hubVisible;

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
            TryInitializeController();
        }

        private void OnEnable()
        {
            TryInitializeController();
        }

        private void OnDestroy()
        {
            RemoveOwnedListeners();
        }

        /// <summary>Supplies the production backend without allowing preview fixtures to be seeded.</summary>
        public void InitializeProductionBackend(IWordListHubBackend productionBackend)
        {
            if (!productionMode)
                throw new InvalidOperationException("Production backend injection requires Production Mode.");
            if (productionBackend == null)
                throw new ArgumentNullException(nameof(productionBackend));

            backend = productionBackend;
            if (isActiveAndEnabled)
            {
                if (!initialized)
                {
                    InitializeController();
                    RefreshCurrentPage();
                }
                else
                    RefreshCurrentPage();
            }
        }

        public void SetButtonClickAudioSource(AudioSource audioSource)
        {
            if (!productionMode)
                throw new InvalidOperationException("Production click audio requires Production Mode.");
            if (audioSource == null)
                throw new ArgumentNullException(nameof(audioSource));

            buttonClickAudioSource = audioSource;
            if (sceneBindings == null)
                sceneBindings = GetComponentInChildren<WordListHubSceneBindings>(true);
            if (sceneBindings != null && sceneBindings.hostRoot != null)
                EnsureButtonSounds(sceneBindings.hostRoot.transform);
        }

        /// <summary>Shows or hides the host root and notifies the game bridge about input/timer gating.</summary>
        public void SetHubVisible(bool visible)
        {
            bool visibilityChanged = hubVisible != visible;
            if (!visibilityChanged && (sceneBindings == null || sceneBindings.hostRoot == null
                || sceneBindings.hostRoot.activeSelf == visible))
                return;

            hubVisible = visible;
            if (visibilityChanged)
                HubVisibilityChanged?.Invoke(visible);
            if (sceneBindings != null && sceneBindings.hostRoot != null
                && sceneBindings.hostRoot.activeSelf != visible)
            {
                sceneBindings.hostRoot.SetActive(visible);
            }
        }

        public void ShowHub()
        {
            SetHubVisible(true);
        }

        public void ShowHub(WordListHubPage page)
        {
            currentPage = FromScenePage(page);
            if (initialized)
            {
                SetFiltersMenuOpen(false);
                SetPageActive(currentPage);
                RefreshCurrentPage();
            }
            PageChanged?.Invoke(ToScenePage(currentPage));
            ShowHub();
        }

        public void HideHub()
        {
            SetHubVisible(false);
        }

        public void RequestCloseHub()
        {
            HubCloseRequested?.Invoke();
            HideHub();
        }

        public void RequestSettings()
        {
            SettingsRequested?.Invoke();
        }

        public void ShowExternalMessage(string message, bool isError = false)
        {
            ShowActionFeedback(message, isError);
        }

        public void ShowCompletedRun(
            string localListId, bool isChallenge, bool recordPersisted,
            int stars, float timeSeconds, string recordError = null)
        {
            if (isChallenge)
            {
                selectedChallengeId = localListId;
                ShowHub(WordListHubPage.Challenges);
            }
            else
            {
                selectedListId = localListId;
                ShowHub(WordListHubPage.Library);
            }

            string message = recordPersisted
                ? "Run saved · " + Mathf.Clamp(stars, 0, 3) + " stars · " + timeSeconds.ToString("0.0") + " s"
                : "Run completed, but its Library record could not be saved."
                    + (string.IsNullOrWhiteSpace(recordError) ? string.Empty : " " + recordError);
            ShowActionFeedback(message, !recordPersisted);
        }

        public void RememberCompletedChallenge(string localListId, bool recordPersisted = true, string recordError = null)
        {
            selectedChallengeId = localListId;
            if (!recordPersisted)
                ShowActionFeedback("Challenge completed, but its progress record could not be saved."
                    + (string.IsNullOrWhiteSpace(recordError) ? string.Empty : " " + recordError), true);
        }

        public void BeginImportReview(string importToken, MockupWordListEdit draft)
        {
            if (string.IsNullOrWhiteSpace(importToken) || draft == null)
            {
                ShowActionFeedback("The import review could not be opened.", true);
                return;
            }

            pendingImportToken = importToken;
            newListLanguage = draft.Language;
            newListLanguageFallback = draft.Language;
            OpenDialog("Review imported list", 680f, 700f);
            DrawNewListDialog();
            if (newListTitleInput != null) newListTitleInput.SetTextWithoutNotify(draft.Title ?? string.Empty);
            if (newListDescriptionInput != null) newListDescriptionInput.SetTextWithoutNotify(draft.Description ?? string.Empty);
            if (newListTagsInput != null) newListTagsInput.SetTextWithoutNotify(draft.Tags ?? string.Empty);
            if (newListWordsInput != null)
                newListWordsInput.SetTextWithoutNotify(string.Join("\n", draft.Words ?? new List<string>()));
            SetText(sceneBindings.modal.newList.validationText,
                draft.Language == MockupLanguage.English
                    ? "Review the imported entries before adding them to My Lists."
                    : "Review each suggested pinyin reading. Replace ‘xxx’ and correct any polyphonic readings before importing.");
            UpdateNewListLanguageDisplay(newListWordsInput != null ? newListWordsInput.text : string.Empty, false);
        }

        private void TryInitializeController()
        {
            if (initialized)
                return;

            if (productionMode)
            {
                if (backend == null)
                    return;
            }
            else if (backend == null)
            {
                previewStore = MockupWordListStore.CreateWithFixtures();
                store = previewStore;
                backend = previewStore;
            }

            InitializeController();
            RefreshCurrentPage();
        }

        private void InitializeController()
        {
            if (backend == null)
                return;
            if (query == null)
                query = new MockupLibraryQuery();
            BindAuthoredScene();
            InitializeRubyWordsViews();
            EnsureButtonSounds(sceneBindings.hostRoot != null
                ? sceneBindings.hostRoot.transform
                : transform);
            CacheNavigationColors();
            BindSceneEvents();
            SetFiltersMenuOpen(false);
            SetPageActive(currentPage);
            initialized = true;
            hubVisible = sceneBindings != null && sceneBindings.hostRoot != null
                ? sceneBindings.hostRoot.activeSelf : !productionMode;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Explicitly synchronizes the saved Scene preview with the same fixture data,
        /// row configuration, and runtime layout methods used by Play Mode.
        /// </summary>
        public void SynchronizeStaticPreview()
        {
            if (Application.isPlaying)
                throw new InvalidOperationException("Static preview synchronization is only available in Edit Mode.");
            if (productionMode)
                throw new InvalidOperationException("Production controllers cannot seed preview fixtures.");

            previewStore = MockupWordListStore.CreateWithFixtures();
            store = previewStore;
            backend = previewStore;
            query = new MockupLibraryQuery();
            communityQuery = new MockupDiscoveryQuery();
            achievementFilter = AchievementFilter.All;
            currentPage = Page.Library;
            selectedListId = null;
            selectedDiscoveryId = null;
            librarySearchText = string.Empty;
            collapsedGroups.Clear();
            ClearActionFeedback();
            InitializeController();
            RefreshCurrentPage();

            RefreshLibraryPage(true);
            currentPage = Page.Community;
            SetPageActive(currentPage);
            RefreshCommunityPage();
            currentPage = Page.Achievements;
            SetPageActive(currentPage);
            RefreshAchievementsPage();
            currentPage = Page.Challenges;
            SetPageActive(currentPage);
            RefreshChallengesPage();

            currentPage = Page.Library;
            SetPageActive(currentPage);
            OpenEditDialog("mock-list-travel-copy");
            CloseDialog();
            MockupWordListEditorView.EnsureAuthoredInputStyle(sceneBindings.modal.editList);
            SetPageActive(Page.Library);
            RefreshLibraryPage(true);
            Canvas.ForceUpdateCanvases();
        }
#endif

        private void Update()
        {
            if (!string.IsNullOrEmpty(lastActionMessage)
                && Time.unscaledTime >= actionFeedbackExpiresAt)
                ClearActionFeedback();

            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                if (openFilterMenu != null)
                    SetFilterMenuOpen(openFilterMenu, false);
                else if (sceneBindings != null && sceneBindings.modal != null
                    && sceneBindings.modal.root != null && sceneBindings.modal.root.activeSelf)
                    CloseDialog();
                else if (productionMode)
                    RequestCloseHub();
            }

            RefreshTilesAfterViewportResize();
        }

        private void RefreshTilesAfterViewportResize()
        {
            if (sceneBindings == null)
                return;

            if (currentPage == Page.Library && sceneBindings.library != null
                && sceneBindings.library.listScroll != null
                && sceneBindings.library.listScroll.viewport != null)
            {
                float width = GetTileViewportWidth(
                    sceneBindings.library.listScroll, sceneBindings.library.listContent);
                if (width > 0f && lastLibraryTileViewportWidth > 0f
                    && Mathf.Abs(width - lastLibraryTileViewportWidth) > 0.5f)
                {
                    RefreshLibraryPage();
                }
                return;
            }

            if (currentPage == Page.Community && sceneBindings.community != null
                && sceneBindings.community.listScroll != null
                && sceneBindings.community.listScroll.viewport != null)
            {
                float width = GetTileViewportWidth(
                    sceneBindings.community.listScroll, sceneBindings.community.listContent);
                if (width > 0f && lastCommunityTileViewportWidth > 0f
                    && Mathf.Abs(width - lastCommunityTileViewportWidth) > 0.5f)
                    RefreshCommunityPage();
            }
            if (currentPage == Page.Challenges && productionMode && sceneBindings.challenges != null
                && sceneBindings.challenges.listScroll != null
                && sceneBindings.challenges.listScroll.viewport != null)
            {
                float width = GetTileViewportWidth(
                    sceneBindings.challenges.listScroll, sceneBindings.challenges.listContent);
                if (width > 0f && lastChallengeTileViewportWidth > 0f
                    && Mathf.Abs(width - lastChallengeTileViewportWidth) > 0.5f)
                    RefreshChallengesPage();
            }
        }

        private void LateUpdate()
        {
            if (libraryRubyWordsView != null)
                libraryRubyWordsView.RefreshLayoutIfNeeded();
            if (communityRubyWordsView != null)
                communityRubyWordsView.RefreshLayoutIfNeeded();
            if (challengeRubyWordsView != null)
                challengeRubyWordsView.RefreshLayoutIfNeeded();
        }

        private void UpdateProductionCloseButtonPosition()
        {
            if (!productionMode || sceneBindings == null || sceneBindings.card == null
                || sceneBindings.closeHub == null || sceneBindings.closeHub.button == null)
                return;
            RectTransform closeRect = sceneBindings.closeHub.button.transform as RectTransform;
            RectTransform cardRect = sceneBindings.card;
            if (closeRect == null || closeRect.parent != cardRect.parent
                || cardRect.rect.width <= 0f || cardRect.rect.height <= 0f)
                return;

            // Set the saved/UI-local position even if the production Canvas is not yet
            // rendered. World corners collapse for an inactive Screen Space Camera Canvas.
            closeRect.localPosition = CalculateProductionCloseButtonLocalPosition(
                cardRect, ProductionCloseButtonGap, ProductionCloseButtonSize);

            Canvas canvas = sceneBindings.hostRoot != null ? sceneBindings.hostRoot.GetComponent<Canvas>() : null;
            Camera camera = canvas != null ? canvas.worldCamera : null;
            if (camera == null || camera.pixelRect.width <= 0f || camera.pixelRect.height <= 0f)
                return;

            if (!productionCardBaseScaleCaptured)
            {
                productionCardBaseScale = cardRect.localScale;
                productionCardBaseScaleCaptured = true;
            }

            var cardCorners = new Vector3[4];
            cardRect.GetWorldCorners(cardCorners);
            var closeCorners = new Vector3[4];
            closeRect.GetWorldCorners(closeCorners);
            float cardWidth = Vector2.Distance(
                RectTransformUtility.WorldToScreenPoint(camera, cardCorners[1]),
                RectTransformUtility.WorldToScreenPoint(camera, cardCorners[2]))
                / Mathf.Max(0.01f, productionCardScaleFactor);
            float cardHeight = Vector2.Distance(
                RectTransformUtility.WorldToScreenPoint(camera, cardCorners[0]),
                RectTransformUtility.WorldToScreenPoint(camera, cardCorners[1]))
                / Mathf.Max(0.01f, productionCardScaleFactor);
            float closeWidth = Vector2.Distance(
                RectTransformUtility.WorldToScreenPoint(camera, closeCorners[1]),
                RectTransformUtility.WorldToScreenPoint(camera, closeCorners[2]));
            Vector2 rightEdgeScreen = RectTransformUtility.WorldToScreenPoint(camera, cardCorners[2]);
            Vector3 gapWorld = closeRect.parent.TransformVector(Vector3.right * ProductionCloseButtonGap);
            float gap = Vector2.Distance(rightEdgeScreen,
                RectTransformUtility.WorldToScreenPoint(camera, cardCorners[2] + gapWorld));
            Vector3 marginWorld = closeRect.parent.TransformVector(Vector3.right * ProductionCloseSafeMargin);
            float margin = Vector2.Distance(rightEdgeScreen,
                RectTransformUtility.WorldToScreenPoint(camera, cardCorners[2] + marginWorld));
            float targetScale = CalculateProductionCardScaleFactor(camera.pixelRect.width,
                camera.pixelRect.height, cardWidth, cardHeight, closeWidth, gap, margin);
            if (Mathf.Abs(targetScale - productionCardScaleFactor) > 0.001f)
            {
                cardRect.localScale = productionCardBaseScale * targetScale;
                productionCardScaleFactor = targetScale;
                cardRect.GetWorldCorners(cardCorners);
            }

            closeRect.localPosition = CalculateProductionCloseButtonLocalPosition(
                cardRect, ProductionCloseButtonGap, ProductionCloseButtonSize);
        }

        /// <summary>
        /// Returns the close-button center in the card parent's local space. This stays
        /// valid before an inactive Screen Space Camera Canvas has generated world corners.
        /// </summary>
        public static Vector3 CalculateProductionCloseButtonLocalPosition(
            RectTransform cardRect,
            float gap,
            float closeButtonSize)
        {
            if (cardRect == null)
                throw new ArgumentNullException(nameof(cardRect));
            Vector3 topRightInParent = cardRect.localPosition + cardRect.localRotation * Vector3.Scale(
                new Vector3(cardRect.rect.xMax, cardRect.rect.yMax, 0f), cardRect.localScale);
            return topRightInParent + new Vector3(gap + closeButtonSize * 0.5f,
                -closeButtonSize * 0.5f, 0f);
        }

        private static float CalculateProductionCardScaleFactor(
            float viewportWidth,
            float viewportHeight,
            float cardWidth,
            float cardHeight,
            float closeWidth,
            float gap,
            float safeMargin)
        {
            if (viewportWidth <= 0f || viewportHeight <= 0f || cardWidth <= 0f || cardHeight <= 0f)
                return 1f;
            float maxCardWidth = viewportWidth - 2f * (closeWidth + gap + safeMargin);
            float maxCardHeight = viewportHeight - 2f * safeMargin;
            return Mathf.Clamp01(Mathf.Min(maxCardWidth / cardWidth, maxCardHeight / cardHeight));
        }

        private void InitializeRubyWordsViews()
        {
            if (sceneBindings.library.detailWords != null && libraryRubyWordsView == null)
                libraryRubyWordsView = MockupRubyWordsView.CreateLibrary(
                    sceneBindings.library.detailWords, wordPreviewFontScale,
                    sceneBindings.libraryRubyWords);
            if (sceneBindings.community.detailSummary != null && communityRubyWordsView == null)
                communityRubyWordsView = MockupRubyWordsView.CreateCommunity(
                    sceneBindings.community.detailSummary,
                    libraryRubyWordsView == null || libraryRubyWordsView.ScrollRect == null
                        ? null
                        : libraryRubyWordsView.ScrollRect.verticalScrollbar,
                    wordPreviewFontScale,
                    sceneBindings.communityRubyWords);
            if (productionMode && sceneBindings.challenges.detailWords != null
                && challengeRubyWordsView == null)
                challengeRubyWordsView = MockupRubyWordsView.CreateLibrary(
                    sceneBindings.challenges.detailWords, wordPreviewFontScale,
                    sceneBindings.challenges.rubyWords);
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
            BindButton(sceneBindings.closeHub, RequestCloseHub);
            if (productionMode)
            {
                if (sceneBindings.openSettings != null && sceneBindings.openSettings.button != null)
                    sceneBindings.openSettings.button.gameObject.SetActive(false);
                BindButton(sceneBindings.openSettings, null);
            }
            else
                BindButton(sceneBindings.openSettings, RequestSettings);
            BindButton(sceneBindings.library.importList,
                productionMode && backend.CanImportLists ? () => ImportRequested?.Invoke() : null);
            if (sceneBindings.library.importList.button != null)
                sceneBindings.library.importList.button.gameObject.SetActive(
                    productionMode && backend.CanImportLists);
            if (productionMode)
            {
                if (sceneBindings.resetMockData != null && sceneBindings.resetMockData.button != null)
                    sceneBindings.resetMockData.button.gameObject.SetActive(false);
                BindButton(sceneBindings.resetMockData, null);
            }
            else
                BindButton(sceneBindings.resetMockData, ResetMockData);

            WordListHubSceneBindings.LibraryPageBinding library = sceneBindings.library;
            BindButton(library.allLists, ToggleLibraryView);
            BindButton(library.favorites, null);
            if (library.favorites != null && library.favorites.button != null)
                library.favorites.button.gameObject.SetActive(false);
            BindButton(library.createList, backend.CanCreateLists ? OpenNewListDialog : null);
            if (library.createList.button != null)
                library.createList.button.gameObject.SetActive(backend.CanCreateLists);
            if (library.exportList.button != null)
                library.exportList.button.gameObject.SetActive(false);
            BindButton(library.exportList, () =>
            {
                if (!string.IsNullOrEmpty(selectedListId))
                    ExportRequested?.Invoke(selectedListId);
            });
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
            if (productionMode)
            {
                SetLibraryAction(sceneBindings.challenges.dailyChallenge, string.Empty, false, null);
                SetLibraryAction(sceneBindings.challenges.weeklyChallenge, string.Empty, false, null);
            }
            else
            {
                BindButton(sceneBindings.challenges.dailyChallenge, () => ChangePage(Page.Library));
                BindButton(sceneBindings.challenges.weeklyChallenge, () => ChangePage(Page.Library));
            }

            WordListHubSceneBindings.ModalBinding modal = sceneBindings.modal;
            if (modal == null)
                return;
            if (modal.newList != null)
            {
                BindInput(modal.newList.wordsInput, HandleNewListWordsChanged);
                BindButton(modal.newList.cancelButton, CloseDialog);
                BindButton(modal.newList.saveButton, CreateNewList);
            }
            if (modal.editList != null)
            {
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
            if (!productionMode && modal.playResult != null)
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
            libraryNavigationBaseLabel = sceneBindings.navLibrary != null
                && sceneBindings.navLibrary.label != null
                ? sceneBindings.navLibrary.label.text : "Library";
            communityNavigationBaseLabel = sceneBindings.navCommunity != null
                && sceneBindings.navCommunity.label != null
                ? sceneBindings.navCommunity.label.text : "Community";
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

        private void RefreshPageAccessIndicators()
        {
            if (backend == null || sceneBindings == null)
                return;

            if (string.IsNullOrWhiteSpace(libraryNavigationBaseLabel))
                libraryNavigationBaseLabel = sceneBindings.navLibrary != null
                    && sceneBindings.navLibrary.label != null
                    && !string.IsNullOrWhiteSpace(sceneBindings.navLibrary.label.text)
                    ? sceneBindings.navLibrary.label.text : "Library";
            if (string.IsNullOrWhiteSpace(communityNavigationBaseLabel))
                communityNavigationBaseLabel = sceneBindings.navCommunity != null
                    && sceneBindings.navCommunity.label != null
                    && !string.IsNullOrWhiteSpace(sceneBindings.navCommunity.label.text)
                    ? sceneBindings.navCommunity.label.text : "Community";

            MockupHubPageAccess libraryAccess = backend.GetPageAccess(WordListHubPage.Library);
            MockupHubPageAccess communityAccess = backend.GetPageAccess(WordListHubPage.Community);
            bool showLockLabels = productionMode;
            SetButtonLabel(sceneBindings.navLibrary,
                showLockLabels && !libraryAccess.IsAvailable
                    ? libraryNavigationBaseLabel + "\n<size=70%>UNAVAILABLE</size>"
                    : showLockLabels && !libraryAccess.IsUnlocked
                    ? libraryNavigationBaseLabel + "\n<size=70%>LOCKED</size>"
                    : libraryNavigationBaseLabel);
            SetButtonLabel(sceneBindings.navCommunity,
                showLockLabels && !communityAccess.IsAvailable
                    ? communityNavigationBaseLabel + "\n<size=70%>UNAVAILABLE</size>"
                    : showLockLabels && !communityAccess.IsUnlocked
                    ? communityNavigationBaseLabel + "\n<size=70%>LOCKED</size>"
                    : communityNavigationBaseLabel);
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

        private static Page FromScenePage(WordListHubPage page)
        {
            switch (page)
            {
                case WordListHubPage.Challenges: return Page.Challenges;
                case WordListHubPage.Community: return Page.Community;
                case WordListHubPage.Achievements: return Page.Achievements;
                default: return Page.Library;
            }
        }

        private void BindButton(WordListHubSceneBindings.ButtonBinding binding, UnityAction action)
        {
            if (binding != null)
                BindButton(binding.button, action);
        }

        private void BindButton(Button button, UnityAction action)
        {
            if (button == null)
                return;
            EnsureButtonSound(button);
            // Remove only the delegate owned by this controller; serialized host listeners remain intact.
            if (buttonListeners.TryGetValue(button, out UnityAction previous))
            {
                button.onClick.RemoveListener(previous);
                buttonListeners.Remove(button);
            }
            if (action != null)
            {
                button.onClick.AddListener(action);
                buttonListeners[button] = action;
            }
        }

        private void EnsureButtonSounds(Transform root)
        {
            if (!productionMode || buttonClickAudioSource == null || root == null)
                return;
            foreach (Button button in root.GetComponentsInChildren<Button>(true))
                EnsureButtonSound(button);
        }

        private void EnsureButtonSound(Button button)
        {
            if (productionMode && buttonClickAudioSource != null && button != null)
                WordListHubButtonSoundRelay.Attach(button, buttonClickAudioSource);
        }

        private void BindDropdown(TMP_Dropdown dropdown, UnityAction<int> action)
        {
            if (dropdown == null)
                return;
            if (dropdownListeners.TryGetValue(dropdown, out UnityAction<int> previous))
            {
                dropdown.onValueChanged.RemoveListener(previous);
                dropdownListeners.Remove(dropdown);
            }
            if (action != null)
            {
                dropdown.onValueChanged.AddListener(action);
                dropdownListeners[dropdown] = action;
            }
        }

        private void BindInput(TMP_InputField input, UnityAction<string> action)
        {
            if (input == null)
                return;
            if (inputListeners.TryGetValue(input, out UnityAction<string> previous))
            {
                input.onValueChanged.RemoveListener(previous);
                inputListeners.Remove(input);
            }
            if (action != null)
            {
                input.onValueChanged.AddListener(action);
                inputListeners[input] = action;
            }
        }

        private void RemoveOwnedListeners()
        {
            foreach (KeyValuePair<Button, UnityAction> binding in buttonListeners)
                if (binding.Key != null) binding.Key.onClick.RemoveListener(binding.Value);
            foreach (KeyValuePair<TMP_Dropdown, UnityAction<int>> binding in dropdownListeners)
                if (binding.Key != null) binding.Key.onValueChanged.RemoveListener(binding.Value);
            foreach (KeyValuePair<TMP_InputField, UnityAction<string>> binding in inputListeners)
                if (binding.Key != null) binding.Key.onValueChanged.RemoveListener(binding.Value);
            buttonListeners.Clear();
            dropdownListeners.Clear();
            inputListeners.Clear();
        }



        private void ResetMockData()
        {
            if (productionMode || previewStore == null)
                return;
            SetFiltersMenuOpen(false);
            CloseDialog();
            previewStore = MockupWordListStore.CreateWithFixtures();
            store = previewStore;
            backend = previewStore;
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
            newListLanguageFallback = MockupLanguage.English;
            editLanguage = MockupLanguage.English;
            editLanguageFallback = MockupLanguage.English;
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
            PageChanged?.Invoke(ToScenePage(page));
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

        private void SelectChallengeItem(string localListId)
        {
            selectedChallengeId = localListId;
            RefreshChallengesPage();
        }

        private void SelectDiscoveryItem(string remoteItemId)
        {
            selectedDiscoveryId = remoteItemId;
            RefreshCommunityPage();
        }

        private void ToggleFavorite(string mockId)
        {
            backend.ToggleFavorite(mockId);
            RefreshLibraryPage();
        }

        private void ToggleLike(string remoteId)
        {
            MockupActionResult result = backend.TryToggleLike(remoteId);
            if (!result.Succeeded)
                ShowActionFeedback(result.Message + " (mock action; no live service was contacted.)", true);
            RefreshCommunityPage();
        }

        private void DownloadDiscoveryItem(string remoteId)
        {
            MockupActionResult result = backend.TryDownload(remoteId);
            if (!result.Succeeded || result.List == null)
            {
                ShowActionFeedback(result.Message + " (mock action; no live service was contacted.)", true);
                RefreshCommunityPage();
                return;
            }

            MockupWordList downloaded = result.List;
            if (productionMode)
            {
                selectedDiscoveryId = remoteId;
                RefreshCommunityPage();
                ShowActionFeedback("Sample download is available only in Community Preview; My Lists and game progress were not changed.");
                return;
            }
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
            MockupActionResult result = backend.TryReportAndHide(remoteId);
            if (!result.Succeeded)
            {
                ShowActionFeedback(result.Message + " (mock action; no live service was contacted.)", true);
                RefreshCommunityPage();
                return;
            }

            selectedDiscoveryId = null;
            RefreshCommunityPage();
            ShowActionFeedback("Hidden from your Community view; a report was simulated locally. No real report was sent.");
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
            ShowMockPlayResult(backend.PlayDiscoveryItem(remoteId));
        }

        private void BeginLibraryMockPlay(string mockId)
        {
            if (productionMode)
            {
                PlayRequested?.Invoke(mockId);
                return;
            }

            if (previewStore != null)
                ShowMockPlayResult(previewStore.PlayLibraryItem(mockId));
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
            ShowConfirmation((productionMode ? "Sample preview · " : "Mock play · ") + result.List.Title,
                "This is a simulated result only. No game scene will start.\n"
                + (productionMode
                    ? "No My Lists content, challenge progress, or real gameplay record will change."
                    : "The local list stays in its library section if you cancel."),
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
            bool completed = backend != null && backend.CompleteRun(mockId, 3, 85f);
            MockupWordList item = backend != null ? backend.FindById(mockId) : null;
            pendingPlayListId = null;
            CloseDialog();
            if (!completed || item == null)
            {
                ShowActionFeedback("The local mock run could not be recorded.", true);
                RefreshCurrentPage();
                return;
            }

            if (productionMode)
            {
                WordListHubSceneBindings.ConfirmationBinding sampleDialog = sceneBindings.modal.playResult;
                ShowConfirmation("Sample preview complete",
                    "3 stars  ·  85.0 seconds\nThis result exists only in Community Preview sample data. No My Lists or challenge record was changed.",
                    "Return to Community Preview", "Close", sampleDialog);
                BindButton(sampleDialog.confirmButton, () =>
                {
                    selectedDiscoveryId = item.RemoteItemId;
                    currentPage = Page.Community;
                    CloseDialog();
                    SetPageActive(currentPage);
                    RefreshCommunityPage();
                });
                BindButton(sampleDialog.cancelButton, () =>
                {
                    CloseDialog();
                    RefreshCurrentPage();
                });
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
            MockupWordList item = backend.FindById(mockId);
            if (item == null)
                return;

            if (item.IsReadOnly)
            {
                item = backend.EditCommunityCopy(item.Id);
                selectedListId = item.Id;
                ShowActionFeedback("Created your editable copy. The downloaded original remains unchanged.");
            }

            OpenEditDialog(item.Id);
        }

        private void PublishList(string mockId)
        {
            MockupActionResult result = backend.TryPublish(mockId);
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
            MockupActionResult result = backend.TryUnpublish(mockId);
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
            MockupWordList item = backend.FindById(mockId);
            if (item == null || item.IsReadOnly)
                return;

            if (item.HasPublishedBinding)
                Unpublish(mockId);
            else
                PublishList(mockId);
        }

        private void RequestDeleteList(string mockId)
        {
            MockupWordList item = backend.FindById(mockId);
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

            List<MockupWordList> visibleItems = backend.QueryLibrary(query).ToList();
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
            MockupActionResult result = backend.TryDeleteList(deletedId);
            pendingDeleteListId = null;
            pendingDeleteVisibleIndex = -1;
            CloseDialog();
            ShowActionFeedback(result.Message, !result.Succeeded);

            if (result.Succeeded && selectedListId == deletedId)
            {
                IReadOnlyList<MockupWordList> remaining = backend.QueryLibrary(query);
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

        private static float GetTileViewportWidth(ScrollRect scroll, Transform content)
        {
            float width = content is RectTransform contentRect
                ? contentRect.rect.width : 0f;
            if (width <= 0f && scroll != null && scroll.viewport != null)
                width = scroll.viewport.rect.width;
            return width > 0f ? width : WordListHubTileGridMetrics.PreferredTileSize;
        }

        private static float TileSizeForWidth(float availableWidth)
        {
            return WordListHubTileGridMetrics.Calculate(
                availableWidth, WordListHubTileGridMetrics.PreferredColumnCount).TileSize;
        }

        private static void PrepareManualTileContent(Transform content)
        {
            if (content == null)
                return;

            VerticalLayoutGroup verticalLayout = content.GetComponent<VerticalLayoutGroup>();
            if (verticalLayout != null)
                verticalLayout.enabled = false;
            ContentSizeFitter contentSizeFitter = content.GetComponent<ContentSizeFitter>();
            if (contentSizeFitter != null)
                contentSizeFitter.enabled = false;

            RectTransform rect = content as RectTransform;
            if (rect == null)
                return;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, rect.anchoredPosition.y);
            rect.sizeDelta = new Vector2(0f, rect.sizeDelta.y);
        }

        private static float HeaderHeight(WordListHubSceneBindings.GroupBinding binding)
        {
            if (binding == null || binding.button == null)
                return 0f;
            LayoutElement layout = binding.button.GetComponent<LayoutElement>();
            if (layout != null && layout.preferredHeight > 0f)
                return layout.preferredHeight;
            RectTransform rect = binding.button.transform as RectTransform;
            if (rect == null)
                return 40f;
            if (rect.rect.height > 0f)
                return rect.rect.height;
            return rect.sizeDelta.y > 0f ? rect.sizeDelta.y : 40f;
        }

        private static void SetTopLeftRect(RectTransform rect, float left, float top, float width, float height)
        {
            if (rect == null)
                return;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(left, -top);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void SetAuthoredHeaderRect(
            RectTransform rect,
            float left,
            float top,
            float width,
            float height)
        {
            if (rect == null)
                return;

            RectTransform parent = rect.parent as RectTransform;
            if (parent == null)
            {
                SetTopLeftRect(rect, left, top, width, height);
                return;
            }

            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);

            Vector2 anchorPoint = new Vector2(
                Mathf.Lerp(rect.anchorMin.x, rect.anchorMax.x, rect.pivot.x),
                Mathf.Lerp(rect.anchorMin.y, rect.anchorMax.y, rect.pivot.y));
            Vector2 anchorLocalPosition = parent.rect.min
                + Vector2.Scale(parent.rect.size, anchorPoint);
            Vector2 pivotLocalPosition = new Vector2(
                parent.rect.xMin + left + rect.rect.width * rect.pivot.x,
                parent.rect.yMax - top - rect.rect.height * (1f - rect.pivot.y));
            rect.anchoredPosition = pivotLocalPosition - anchorLocalPosition;
        }

        private static void SetTileChildRect(RectTransform rect, float top, float width, float height)
        {
            if (rect == null)
                return;
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -top);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void SetTileText(
            TMP_Text text,
            float top,
            float width,
            float height,
            float fontSize,
            TextAlignmentOptions alignment,
            bool wrap)
        {
            if (text == null)
                return;
            RectTransform rect = text.rectTransform;
            SetTileChildRect(rect, top, width, height);
            text.fontSize = fontSize;
            text.color = ListTileText;
            text.alignment = alignment;
            text.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            text.raycastTarget = false;
        }

        private static void ConfigureTileBackground(Button button, GameObject selectedVisual)
        {
            Image background = button != null ? button.targetGraphic as Image : null;
            if (background != null)
            {
                background.color = ListTileBackground;
                ColorBlock colors = button.colors;
                colors.normalColor = ListTileBackground;
                colors.highlightedColor = ListTileBackground;
                colors.pressedColor = ListTileBackground;
                colors.selectedColor = ListTileBackground;
                colors.disabledColor = ListTileBackground;
                colors.colorMultiplier = 1f;
                button.colors = colors;
            }

            if (selectedVisual == null)
                return;
            RectTransform selectedRect = selectedVisual.transform as RectTransform;
            if (selectedRect != null)
            {
                selectedRect.anchorMin = Vector2.zero;
                selectedRect.anchorMax = Vector2.one;
                selectedRect.pivot = new Vector2(0.5f, 0.5f);
                selectedRect.offsetMin = Vector2.zero;
                selectedRect.offsetMax = Vector2.zero;
            }
            Image selectedImage = selectedVisual.GetComponent<Image>();
            if (selectedImage == null)
                selectedImage = selectedVisual.GetComponentInChildren<Image>(true);
            if (selectedImage != null)
            {
                selectedImage.color = ListTileSelected;
                selectedImage.raycastTarget = false;
            }
        }

        private static void ConfigureLibraryTile(
            GameObject root,
            Button button,
            GameObject selectedVisual,
            Button favoriteButton,
            TMP_Text favoriteMark,
            TMP_Text title,
            TMP_Text metadata,
            TMP_Text recordStars,
            TMP_Text bestTime,
            GameObject badgeRoot,
            TMP_Text badge,
            float tileSize)
        {
            ConfigureTileBackground(button, selectedVisual);

            ConfigureListTileRows(title, metadata, recordStars, bestTime, tileSize);

            if (badgeRoot != null && badgeRoot.transform is RectTransform badgeRect)
                SetTopLeftRect(badgeRect, 12f, 8f, 180f, 22f);
            if (badge != null)
            {
                if (badgeRoot != null && badge.rectTransform == badgeRoot.transform)
                    SetTileCornerText(badge, false, 8f, 180f, 22f, 13f);
                else
                    SetTileText(badge, 2f, 168f, 18f, 13f, TextAlignmentOptions.Left, false);
            }

            if (favoriteButton != null)
            {
                RectTransform favoriteRect = favoriteButton.transform as RectTransform;
                if (favoriteRect != null)
                {
                    favoriteRect.anchorMin = new Vector2(1f, 1f);
                    favoriteRect.anchorMax = new Vector2(1f, 1f);
                    favoriteRect.pivot = new Vector2(1f, 1f);
                    favoriteRect.anchoredPosition = new Vector2(-10f, 0f);
                    favoriteRect.sizeDelta = new Vector2(34f, 34f);
                }
            }
            if (favoriteMark != null)
            {
                SetTileText(favoriteMark, 0f, 34f, 34f, 28f,
                    TextAlignmentOptions.Center, false);
                favoriteMark.color = ListTileFavorite;
            }
        }

        private static void ConfigureCommunityTile(
            GameObject root,
            Button button,
            GameObject selectedVisual,
            TMP_Text title,
            TMP_Text metadata,
            TMP_Text recordStars,
            TMP_Text bestTime,
            TMP_Text summary,
            TMP_Text status,
            float tileSize)
        {
            ConfigureTileBackground(button, selectedVisual);
            ConfigureListTileRows(title, metadata, recordStars, bestTime, tileSize);
            SetTileCornerText(status, true, 8f, 70f, 22f, 13f);
            if (summary != null && summary.gameObject.activeSelf)
                summary.gameObject.SetActive(false);
        }

        private static void ConfigureListTileRows(
            TMP_Text title,
            TMP_Text metadata,
            TMP_Text recordStars,
            TMP_Text bestTime,
            float tileSize)
        {
            float verticalScale = tileSize < WordListHubTileGridMetrics.MinimumReadableTileSize
                ? tileSize / WordListHubTileGridMetrics.MinimumReadableTileSize
                : 1f;
            float textWidth = Mathf.Max(0f, tileSize - 40f);
            SetTileText(title, 36f * verticalScale, textWidth, 64f * verticalScale,
                22f, TextAlignmentOptions.Center, true);
            SetTileText(metadata, 116f * verticalScale, textWidth, 28f * verticalScale,
                18f, TextAlignmentOptions.Center, false);
            SetTileText(recordStars, 168f * verticalScale, textWidth, 34f * verticalScale,
                22f, TextAlignmentOptions.Center, false);
            SetTileText(bestTime, 222f * verticalScale, textWidth, 28f * verticalScale,
                18f, TextAlignmentOptions.Center, false);
        }

        private static string ChallengeRewardBannerLabel(string unlockRewardLabel)
        {
            if (string.IsNullOrWhiteSpace(unlockRewardLabel))
                return string.Empty;

            bool unlocksCustom = unlockRewardLabel.IndexOf("Library", StringComparison.OrdinalIgnoreCase) >= 0
                || unlockRewardLabel.IndexOf("Custom List", StringComparison.OrdinalIgnoreCase) >= 0;
            bool unlocksCommunity = unlockRewardLabel.IndexOf("Community", StringComparison.OrdinalIgnoreCase) >= 0;
            if (unlocksCustom && unlocksCommunity)
                return "Unlock Custom List + Community";
            if (unlocksCustom)
                return "Unlock Custom List";
            if (unlocksCommunity)
                return "Unlock Community";
            return unlockRewardLabel;
        }

        private static TMP_Text EnsureChallengeRewardBanner(WordListHubLibraryRowView view)
        {
            if (view == null)
                return null;
            if (view.unlockRewardBanner != null)
                return view.unlockRewardBanner;

            Transform existing = view.transform.Find("UnlockRewardBanner");
            if (existing != null)
                view.unlockRewardBanner = existing.GetComponent<TMP_Text>();
            if (view.unlockRewardBanner == null)
            {
                var bannerObject = new GameObject("UnlockRewardBanner",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                bannerObject.transform.SetParent(view.transform, false);
                view.unlockRewardBanner = bannerObject.GetComponent<TextMeshProUGUI>();
            }

            if (view.unlockRewardBanner != null)
            {
                if (view.title != null && view.title.font != null)
                {
                    view.unlockRewardBanner.font = view.title.font;
                    view.unlockRewardBanner.fontSharedMaterial = view.title.fontSharedMaterial;
                }
                view.unlockRewardBanner.fontSize = 11f;
                view.unlockRewardBanner.fontStyle = FontStyles.Bold;
                view.unlockRewardBanner.color = ListTileText;
                view.unlockRewardBanner.alignment = TextAlignmentOptions.Center;
                view.unlockRewardBanner.textWrappingMode = TextWrappingModes.NoWrap;
                view.unlockRewardBanner.raycastTarget = false;
                view.unlockRewardBanner.gameObject.SetActive(false);
            }
            return view.unlockRewardBanner;
        }

        private static void ConfigureChallengeRewardBanner(WordListHubLibraryRowView view, float tileSize)
        {
            if (view == null || view.unlockRewardBanner == null)
                return;

            RectTransform bannerRect = view.unlockRewardBanner.rectTransform;
            if (bannerRect != null)
            {
                bannerRect.anchorMin = new Vector2(0f, 1f);
                bannerRect.anchorMax = new Vector2(1f, 1f);
                bannerRect.pivot = new Vector2(0.5f, 1f);
                bannerRect.anchoredPosition = Vector2.zero;
                bannerRect.sizeDelta = new Vector2(-24f, 18f);
            }

            if (view.badgeRoot != null && view.badgeRoot.activeSelf
                && view.badgeRoot.transform is RectTransform lockedBadgeRect)
            {
                SetTopLeftRect(lockedBadgeRect, 8f, 18f, 74f, 16f);
                if (view.badge != null)
                {
                    RectTransform lockedTextRect = view.badge.rectTransform;
                    if (lockedTextRect == lockedBadgeRect)
                    {
                        view.badge.fontSize = 11f;
                        view.badge.alignment = TextAlignmentOptions.Center;
                        view.badge.textWrappingMode = TextWrappingModes.NoWrap;
                    }
                    else if (lockedTextRect.parent == lockedBadgeRect)
                    {
                        lockedTextRect.anchorMin = Vector2.zero;
                        lockedTextRect.anchorMax = Vector2.one;
                        lockedTextRect.pivot = new Vector2(0.5f, 0.5f);
                        lockedTextRect.offsetMin = new Vector2(2f, 1f);
                        lockedTextRect.offsetMax = new Vector2(-2f, -1f);
                        view.badge.fontSize = 11f;
                        view.badge.alignment = TextAlignmentOptions.Center;
                        view.badge.textWrappingMode = TextWrappingModes.NoWrap;
                    }
                    else
                    {
                        SetTopLeftRect(lockedTextRect, 8f, 18f, 74f, 16f);
                        view.badge.fontSize = 11f;
                        view.badge.alignment = TextAlignmentOptions.Center;
                        view.badge.textWrappingMode = TextWrappingModes.NoWrap;
                    }
                    view.badge.raycastTarget = false;
                }
                Image badgeImage = view.badgeRoot.GetComponent<Image>();
                if (badgeImage != null)
                    badgeImage.raycastTarget = false;
            }
        }

        private static void SetTileCornerText(
            TMP_Text text,
            bool rightAligned,
            float top,
            float width,
            float height,
            float fontSize)
        {
            if (text == null)
                return;
            RectTransform rect = text.rectTransform;
            if (rightAligned)
                SetTopRightRect(rect, 12f, top, width, height);
            else
                SetTopLeftRect(rect, 12f, top, width, height);
            text.fontSize = fontSize;
            text.color = ListTileText;
            text.alignment = rightAligned ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
        }

        private static void SetTopRightRect(RectTransform rect, float right, float top, float width, float height)
        {
            if (rect == null)
                return;
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-right, -top);
            rect.sizeDelta = new Vector2(width, height);
        }

        private float LayoutTileGrid(
            IReadOnlyList<RectTransform> tiles,
            float availableWidth,
            float topOffset)
        {
            int count = tiles != null ? tiles.Count : 0;
            WordListHubTileGridMetrics metrics = WordListHubTileGridMetrics.Calculate(availableWidth, count);
            if (tiles == null || metrics.RowCount == 0)
                return metrics.ContentHeight;

            for (int i = 0; i < tiles.Count; i++)
            {
                int row = i / metrics.ColumnCount;
                int column = i % metrics.ColumnCount;
                float left = metrics.GetRowStartX(row)
                    + column * (metrics.TileSize + WordListHubTileGridMetrics.Gap);
                float top = topOffset + row * (metrics.RowHeight + WordListHubTileGridMetrics.Gap);
                SetTopLeftRect(tiles[i], left, top, metrics.TileSize, metrics.TileSize);
                ConfigureTileForSize(tiles[i], metrics.TileSize);
                LayoutElement layout = tiles[i].GetComponent<LayoutElement>();
                if (layout != null)
                {
                    layout.minWidth = metrics.TileSize;
                    layout.preferredWidth = metrics.TileSize;
                    layout.minHeight = metrics.TileSize;
                    layout.preferredHeight = metrics.TileSize;
                }
            }
            return metrics.ContentHeight;
        }

        private void ConfigureTileForSize(RectTransform tile, float tileSize)
        {
            if (tile == null)
                return;

            WordListHubSceneBindings.LibraryRowBinding authoredLibraryRow = sceneBindings != null
                && sceneBindings.library != null && sceneBindings.library.authoredRows != null
                ? sceneBindings.library.authoredRows.FirstOrDefault(row => row != null && row.root != null
                    && row.root.transform == tile)
                : null;
            if (authoredLibraryRow != null)
            {
                ConfigureLibraryTile(authoredLibraryRow.root, authoredLibraryRow.button,
                    authoredLibraryRow.selectedVisual, authoredLibraryRow.favoriteButton,
                    authoredLibraryRow.favoriteMark, authoredLibraryRow.title,
                    authoredLibraryRow.metadata, authoredLibraryRow.recordStars,
                    authoredLibraryRow.bestTime, authoredLibraryRow.badgeRoot,
                    authoredLibraryRow.badge, tileSize);
                return;
            }

            WordListHubLibraryRowView libraryView = tile.GetComponent<WordListHubLibraryRowView>();
            if (libraryView != null)
            {
                ConfigureLibraryTile(libraryView.gameObject, libraryView.button, libraryView.selectedVisual,
                    libraryView.favoriteButton, libraryView.favoriteMark, libraryView.title,
                    libraryView.metadata, libraryView.recordStars, libraryView.bestTime,
                    libraryView.badgeRoot, libraryView.badge, tileSize);
                if (!string.IsNullOrWhiteSpace(libraryView.unlockRewardLabel))
                    ConfigureChallengeRewardBanner(libraryView, tileSize);
                return;
            }

            WordListHubSceneBindings.DiscoveryRowBinding authoredCommunityRow = sceneBindings != null
                && sceneBindings.community != null && sceneBindings.community.authoredRows != null
                ? sceneBindings.community.authoredRows.FirstOrDefault(row => row != null && row.root != null
                    && row.root.transform == tile)
                : null;
            if (authoredCommunityRow != null)
            {
                ConfigureCommunityTile(authoredCommunityRow.root, authoredCommunityRow.button,
                    authoredCommunityRow.selectedVisual, authoredCommunityRow.title,
                    authoredCommunityRow.metadata, authoredCommunityRow.recordStars,
                    authoredCommunityRow.bestTime, authoredCommunityRow.summary,
                    authoredCommunityRow.status, tileSize);
                return;
            }

            WordListHubDiscoveryRowView communityView = tile.GetComponent<WordListHubDiscoveryRowView>();
            if (communityView != null)
            {
                ConfigureCommunityTile(communityView.gameObject, communityView.button,
                    communityView.selectedVisual, communityView.title, communityView.metadata,
                    communityView.recordStars, communityView.bestTime, communityView.summary,
                    communityView.status, tileSize);
            }
        }

        private static void SetTileRootSize(RectTransform root, float tileSize)
        {
            if (root == null)
                return;
            root.sizeDelta = new Vector2(tileSize, tileSize);
            LayoutElement layout = root.GetComponent<LayoutElement>();
            if (layout != null)
            {
                layout.minWidth = tileSize;
                layout.preferredWidth = tileSize;
                layout.minHeight = tileSize;
                layout.preferredHeight = tileSize;
            }
        }

        private static void SetContentHeight(Transform content, float height)
        {
            if (content is RectTransform rect)
                rect.sizeDelta = new Vector2(0f, Mathf.Max(0f, height));
        }

        private void RefreshLibraryPage(bool moveScrollToTop = false, bool revealSelectedItem = false)
        {
            if (currentPage != Page.Library)
                return;
            if (sceneBindings == null || libraryListContent == null || libraryDetailPane == null)
                throw new InvalidOperationException("Library Scene bindings are missing.");

            RefreshPageAccessIndicators();
            MockupHubPageAccess access = backend.GetPageAccess(WordListHubPage.Library);
            if (productionMode && !access.IsUnlocked)
            {
                RefreshLockedLibraryPage(access);
                return;
            }

            RestoreLibraryPageControls();
            WordListHubSceneBindings.LibraryPageBinding library = sceneBindings.library;
            Canvas.ForceUpdateCanvases();
            float savedScrollY = libraryScrollRect != null && libraryScrollRect.content != null
                ? libraryScrollRect.content.anchoredPosition.y
                : 0f;
            string revealId = revealSelectedItem ? selectedListId : null;

            IReadOnlyList<MockupWordList> queried = backend.QueryLibrary(query);
            List<MockupWordList> items = queried.ToList();
            PrepareManualTileContent(library.listContent);
            float availableWidth = GetTileViewportWidth(library.listScroll, library.listContent);
            lastLibraryTileViewportWidth = availableWidth;
            if (library.rowTemplate != null)
            {
                WordListHubLibraryRowView template = library.rowTemplate;
                float templateSize = WordListHubTileGridMetrics.Calculate(
                    availableWidth, WordListHubTileGridMetrics.PreferredColumnCount).TileSize;
                SetTileRootSize(template.transform as RectTransform, templateSize);
                ConfigureLibraryTile(template.gameObject, template.button, template.selectedVisual,
                    template.favoriteButton, template.favoriteMark, template.title,
                    template.metadata, template.recordStars, template.bestTime,
                    template.badgeRoot, template.badge, templateSize);
            }
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
                MockupWordList item = backend.FindById(row.listId);
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
            float cursorY = 0f;
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
                        float headerHeight = HeaderHeight(header);
                        if (header.button.transform is RectTransform headerRect)
                            SetAuthoredHeaderRect(headerRect, 0f, cursorY, availableWidth, headerHeight);
                        cursorY += headerHeight + WordListHubTileGridMetrics.Gap;
                    }
                }

                if (collapsedGroups.Contains(group))
                    continue;

                var groupTiles = new List<RectTransform>();
                foreach (MockupWordList item in groupItems)
                {
                    if (authoredById.TryGetValue(item.Id, out WordListHubSceneBindings.LibraryRowBinding authored))
                    {
                        if (authored.root != null)
                        {
                            authored.root.SetActive(true);
                            authored.root.transform.SetSiblingIndex(siblingIndex++);
                            if (authored.root.transform is RectTransform authoredRect)
                                groupTiles.Add(authoredRect);
                        }
                    }
                    else if (dynamicById.TryGetValue(item.Id, out WordListHubLibraryRowView dynamicView))
                    {
                        dynamicView.gameObject.SetActive(true);
                        dynamicView.transform.SetSiblingIndex(siblingIndex++);
                        if (dynamicView.transform is RectTransform dynamicRect)
                            groupTiles.Add(dynamicRect);
                    }
                }

                cursorY += LayoutTileGrid(groupTiles, availableWidth, cursorY);
                if (groupTiles.Count > 0)
                    cursorY += WordListHubTileGridMetrics.Gap;
            }
            SetContentHeight(library.listContent, cursorY);

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

        private static void SetLanguageDropdownValue(TMP_Dropdown dropdown, MockupLanguage language)
        {
            if (dropdown == null)
                return;

            string[] labels = { "English", "Chinese", "Mixed" };
            bool optionsMatch = dropdown.options != null && dropdown.options.Count == labels.Length;
            if (optionsMatch)
            {
                for (int i = 0; i < labels.Length; i++)
                {
                    if (!string.Equals(dropdown.options[i].text, labels[i], StringComparison.Ordinal))
                    {
                        optionsMatch = false;
                        break;
                    }
                }
            }
            if (!optionsMatch)
            {
                dropdown.ClearOptions();
                dropdown.AddOptions(labels.ToList());
            }

            int index = language == MockupLanguage.Chinese ? 1
                : language == MockupLanguage.Mixed ? 2 : 0;
            dropdown.SetValueWithoutNotify(index);
            dropdown.RefreshShownValue();
            dropdown.interactable = false;
        }

        private static LanguageMode ToDetectorLanguage(MockupLanguage language)
        {
            switch (language)
            {
                case MockupLanguage.Chinese: return LanguageMode.Chinese;
                case MockupLanguage.Mixed: return LanguageMode.Mixed;
                default: return LanguageMode.English;
            }
        }

        private static MockupLanguage ToMockupLanguage(LanguageMode language)
        {
            switch (language)
            {
                case LanguageMode.Chinese: return MockupLanguage.Chinese;
                case LanguageMode.Mixed: return MockupLanguage.Mixed;
                default: return MockupLanguage.English;
            }
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
            SetText(binding.metadata, LanguageLabel(item.DetectedLanguage) + "  ·  " + item.WordCount + " words");
            SetText(binding.recordStars, PersonalStarsLabel(item));
            string badge = ListBadge(item);
            SetText(binding.badge, badge.ToUpperInvariant());
            if (binding.badgeRoot != null)
                binding.badgeRoot.SetActive(!string.IsNullOrEmpty(badge));
            BindButton(binding.button, () => SelectLibraryItem(item.Id));
            BindButton(binding.favoriteButton, () => ToggleFavorite(item.Id));
            ConfigureLibraryTile(binding.root, binding.button, binding.selectedVisual,
                binding.favoriteButton, binding.favoriteMark, binding.title,
                binding.metadata, binding.recordStars, binding.bestTime,
                binding.badgeRoot, binding.badge,
                TileSizeForWidth(lastLibraryTileViewportWidth > 0f
                    ? lastLibraryTileViewportWidth : WordListHubTileGridMetrics.PreferredTileSize));
        }

        private void UpdateLibraryRow(WordListHubLibraryRowView view, MockupWordList item)
        {
            if (view == null)
                return;
            view.itemId = item.Id;
            SetText(view.favoriteMark, item.IsFavorite ? "★" : "☆");
            SetText(view.bestTime, BestTimeLabel(item));
            SetText(view.title, item.Title);
            SetText(view.metadata, LanguageLabel(item.DetectedLanguage) + "  ·  " + item.WordCount + " words");
            SetText(view.recordStars, PersonalStarsLabel(item));
            string badge = ListBadge(item);
            SetText(view.badge, badge.ToUpperInvariant());
            if (view.badgeRoot != null)
                view.badgeRoot.SetActive(!string.IsNullOrEmpty(badge));
            BindButton(view.button, () => SelectLibraryItem(item.Id));
            BindButton(view.favoriteButton, () => ToggleFavorite(item.Id));
            ConfigureLibraryTile(view.gameObject, view.button, view.selectedVisual,
                view.favoriteButton, view.favoriteMark, view.title,
                view.metadata, view.recordStars, view.bestTime,
                view.badgeRoot, view.badge,
                TileSizeForWidth(lastLibraryTileViewportWidth > 0f
                    ? lastLibraryTileViewportWidth : WordListHubTileGridMetrics.PreferredTileSize));
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
            MockupWordList item = backend.FindById(selectedListId);
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
                // DetailRecordPanel is retired; leave personal record data in the model and list tiles.
                // SetText(library.recordStars, string.Empty);
                // SetText(library.recordTime, string.Empty);
                SetText(library.detailWordsLabel, "WORDS  ·  0");
                SetText(library.detailWords, string.Empty);
                if (libraryRubyWordsView != null)
                    libraryRubyWordsView.SetWords(new[] { "Choose a list to see its details." });
                SetText(library.detailStatus, lastActionMessage);
                SetLibraryAction(library.delete, productionMode ? null : string.Empty, false, null);
                SetLibraryAction(library.play, productionMode ? null : string.Empty, false, null);
                SetLibraryAction(library.edit, productionMode ? null : string.Empty, false, null);
                SetLibraryAction(library.copyEditWords, productionMode ? null : string.Empty, false, null);
                SetLibraryAction(library.publish, string.Empty, false, null);
                SetLibraryAction(library.unpublish, string.Empty, false, null);
                SetLibraryAction(library.exportList, productionMode ? null : string.Empty, false, null);
                return;
            }

            SetText(library.detailTitle, item.Title);
            SetText(library.detailDescription, string.IsNullOrWhiteSpace(item.Description)
                ? "A word list ready for your next practice session." : item.Description);
            string author = item.IsChallenge
                ? "OFFICIAL CHALLENGE"
                : item.IsReadOnly
                ? (string.IsNullOrEmpty(item.RemoteAuthor) ? "COMMUNITY MEMBER" : item.RemoteAuthor)
                : "YOU";
            SetText(library.detailMetadata, "BY " + author);
            // DetailRecordPanel is retired; keep personal records available to the list tiles and model.
            // int earnedStars = Mathf.Clamp(item.PersonalStars ?? 0, 0, 5);
            // SetText(library.recordStars, new string('★', earnedStars) + new string('☆', 5 - earnedStars));
            // SetText(library.recordTime, item.BestTimeSeconds.HasValue
            //     ? item.BestTimeSeconds.Value.ToString("0.0") + " s" : "No run yet");
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
            SetLibraryAction(library.delete, productionMode ? null : "Delete",
                !item.IsReadOnly && !item.IsChallenge && !publicationIsActive && backend.CanDeleteLists,
                !item.IsReadOnly && !item.IsChallenge && !publicationIsActive && backend.CanDeleteLists
                    ? (UnityAction)(() => RequestDeleteList(item.Id)) : null);
            SetLibraryAction(library.play, productionMode ? null : "Play", true,
                () => BeginLibraryMockPlay(item.Id));
            if (item.IsChallenge && library.play != null && library.play.button != null)
                library.play.button.interactable = item.IsChallengeUnlocked;

            SetLibraryAction(library.edit, productionMode ? null : "Edit List",
                !item.IsReadOnly && !item.IsChallenge && !hasPublishedBinding && backend.CanEditLists,
                !item.IsReadOnly && !item.IsChallenge && !hasPublishedBinding && backend.CanEditLists
                    ? (UnityAction)(() => EditOrCopy(item.Id)) : null);
            UnityAction copyEditAction = null;
            if (item.IsChallenge && item.IsChallengeUnlocked
                && backend.CanCreateLists && backend.CanDuplicateLists)
                copyEditAction = () => OpenEditDialog(item.Id, true);
            else if (item.IsReadOnly && !item.IsChallenge)
                copyEditAction = () => EditOrCopy(item.Id);
            else if (item.Ownership == MockupOwnership.Owned
                && backend.CanCreateLists && backend.CanDuplicateLists)
                copyEditAction = () => OpenEditDialog(item.Id, true);
            SetLibraryAction(library.copyEditWords, productionMode ? null : "Copy", copyEditAction != null,
                copyEditAction);

            bool showPublish = !productionMode && !item.IsReadOnly && !item.IsChallenge;
            SetLibraryAction(library.publish, hasPublishedBinding ? "Unpublish" : "Publish", showPublish,
                showPublish ? (UnityAction)(() => TogglePublication(item.Id)) : null);
            if (library.publish != null && library.publish.button != null)
                library.publish.button.interactable = showPublish;
            SetLibraryAction(library.unpublish, string.Empty, false, null);
            bool canExport = productionMode && backend.CanExportLists
                && item.Ownership == MockupOwnership.Owned && !item.IsChallenge;
            // The Export button caption is authored in the Scene; only its availability
            // and callback depend on the selected list and profile.
            SetLibraryAction(library.exportList, productionMode ? null : "Export entries", canExport,
                canExport ? (UnityAction)(() => ExportRequested?.Invoke(item.Id)) : null);
        }

        private void SetLibraryAction(
            WordListHubSceneBindings.ButtonBinding binding,
            string label,
            bool visible,
            UnityAction action)
        {
            if (binding == null)
                return;
            if (label != null)
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
                && item.DetectedLanguage.ToString() != query.LanguageFilter.ToString())
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
                && item.DetectedLanguage.ToString() != query.LanguageFilter.ToString())
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

            RefreshPageAccessIndicators();
            MockupHubPageAccess access = backend.GetPageAccess(WordListHubPage.Community);
            if (productionMode && !access.IsUnlocked)
            {
                RefreshLockedCommunityPage(access);
                return;
            }

            RestoreCommunityPageControls();
            WordListHubSceneBindings.CommunityPageBinding community = sceneBindings.community;
            Canvas.ForceUpdateCanvases();
            PrepareManualTileContent(community.listContent);
            float availableWidth = GetTileViewportWidth(community.listScroll, community.listContent);
            lastCommunityTileViewportWidth = availableWidth;
            if (community.rowTemplate != null)
            {
                WordListHubDiscoveryRowView template = community.rowTemplate;
                float templateSize = WordListHubTileGridMetrics.Calculate(
                    availableWidth, WordListHubTileGridMetrics.PreferredColumnCount).TileSize;
                SetTileRootSize(template.transform as RectTransform, templateSize);
                ConfigureCommunityTile(template.gameObject, template.button, template.selectedVisual,
                    template.title, template.metadata, template.recordStars, template.bestTime,
                    template.summary, template.status, templateSize);
            }
            Vector2 scrollPosition = community.listScroll != null && community.listScroll.content != null
                ? community.listScroll.content.anchoredPosition
                : Vector2.zero;
            if (community.searchInput != null)
                community.searchInput.SetTextWithoutNotify(communityQuery.SearchText);
            SyncFilterMenu(community.connectionMenu,
                kind => GetCommunityFilterValue(kind, communityQuery), "Filters");

            List<MockupDiscoveryItem> items = backend.QueryDiscoveryWithFilters(communityQuery).ToList();
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
                MockupDiscoveryItem item = backend.FindDiscoveryItem(row.remoteItemId);
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
            var communityTiles = new List<RectTransform>();
            foreach (MockupDiscoveryItem item in items)
            {
                if (authoredById.TryGetValue(item.RemoteItemId,
                    out WordListHubSceneBindings.DiscoveryRowBinding authored))
                {
                    if (authored.root != null)
                    {
                        authored.root.SetActive(true);
                        authored.root.transform.SetSiblingIndex(siblingIndex++);
                        if (authored.root.transform is RectTransform authoredRect)
                            communityTiles.Add(authoredRect);
                    }
                }
                else if (dynamicById.TryGetValue(item.RemoteItemId, out WordListHubDiscoveryRowView dynamicView))
                {
                    dynamicView.gameObject.SetActive(true);
                    dynamicView.transform.SetSiblingIndex(siblingIndex++);
                    if (dynamicView.transform is RectTransform dynamicRect)
                        communityTiles.Add(dynamicRect);
                }
            }
            float contentHeight = LayoutTileGrid(communityTiles, availableWidth, 0f);
            SetContentHeight(community.listContent, contentHeight);

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

        private static string BuildLockedPageMessage(string pageName, MockupHubPageAccess access)
        {
            if (access != null && !access.IsAvailable)
                return pageName + " is unavailable in this version. Select Challenges above for details.";
            if (access == null || access.RequiredChallenges <= 0)
                return pageName + " is unavailable for the active profile. Select Challenges above for details.";

            return "Complete " + access.RequiredChallenges + " "
                + (access.RequiredChallenges == 1 ? "challenge" : "challenges")
                + " to unlock " + pageName + ".\nProgress: "
                + access.CompletedChallenges + "/" + access.RequiredChallenges + " ("
                + access.TrackLabel + ").\nSelect Challenges above to continue.";
        }

        private void RestoreLibraryPageControls()
        {
            WordListHubSceneBindings.LibraryPageBinding library = sceneBindings.library;
            if (library.emptyNotice != null)
            {
                library.emptyNotice.text = string.Empty;
                library.emptyNotice.gameObject.SetActive(false);
            }
            if (library.listScroll != null) library.listScroll.gameObject.SetActive(true);
            if (library.listContent != null) library.listContent.gameObject.SetActive(true);
            if (library.detailPane != null) library.detailPane.gameObject.SetActive(true);
            if (library.searchInput != null) library.searchInput.gameObject.SetActive(true);
            if (library.filtersMenu.toggleButton != null)
                library.filtersMenu.toggleButton.gameObject.SetActive(true);
            if (library.filtersMenu.panel != null) library.filtersMenu.panel.SetActive(false);
            SetDropdownActive(library.languageFilter, true);
            SetDropdownActive(library.playedFilter, true);
            SetDropdownActive(library.sortFilter, true);
            SetDropdownActive(library.directionFilter, true);
            SetButtonActive(library.allLists, true);
            SetButtonActive(library.favorites, false);
            bool canCreate = backend.CanCreateLists;
            SetButtonActive(library.createList, canCreate);
            BindButton(library.createList, canCreate ? (UnityAction)OpenNewListDialog : null);
            bool canImport = productionMode && backend.CanImportLists;
            SetButtonActive(library.importList, canImport);
            BindButton(library.importList, canImport ? (UnityAction)(() => ImportRequested?.Invoke()) : null);
            SetButtonActive(library.exportList, false);
            if (library.detailWordsLabel != null) library.detailWordsLabel.gameObject.SetActive(true);
            if (library.detailWords != null) library.detailWords.gameObject.SetActive(true);
            if (libraryRubyWordsView != null && libraryRubyWordsView.LayoutRoot != null)
                libraryRubyWordsView.LayoutRoot.gameObject.SetActive(true);
        }

        private void RestoreCommunityPageControls()
        {
            WordListHubSceneBindings.CommunityPageBinding community = sceneBindings.community;
            if (community.emptyNotice != null)
            {
                community.emptyNotice.text = string.Empty;
                community.emptyNotice.gameObject.SetActive(false);
            }
            if (community.listScroll != null) community.listScroll.gameObject.SetActive(true);
            if (community.listContent != null) community.listContent.gameObject.SetActive(true);
            if (community.detailPane != null) community.detailPane.gameObject.SetActive(true);
            if (community.searchInput != null) community.searchInput.gameObject.SetActive(true);
            SetDropdownActive(community.connectionState, true);
            if (community.connectionMenu.toggleButton != null)
                community.connectionMenu.toggleButton.gameObject.SetActive(true);
            if (community.connectionMenu.panel != null) community.connectionMenu.panel.SetActive(false);
            if (community.detailAuthor != null) community.detailAuthor.gameObject.SetActive(true);
            if (community.detailSummary != null) community.detailSummary.gameObject.SetActive(true);
            if (community.detailDescription != null) community.detailDescription.gameObject.SetActive(true);
            if (community.detailMetadata != null) community.detailMetadata.gameObject.SetActive(true);
            if (community.detailTrustNote != null) community.detailTrustNote.gameObject.SetActive(true);
            if (communityRubyWordsView != null && communityRubyWordsView.LayoutRoot != null)
                communityRubyWordsView.LayoutRoot.gameObject.SetActive(true);
        }

        private void RefreshLockedLibraryPage(MockupHubPageAccess access)
        {
            WordListHubSceneBindings.LibraryPageBinding library = sceneBindings.library;
            selectedListId = null;
            if (library.listScroll != null) library.listScroll.gameObject.SetActive(true);
            if (library.listContent != null) library.listContent.gameObject.SetActive(false);
            SetText(library.emptyNotice, BuildLockedPageMessage("Library", access));
            if (library.emptyNotice != null) library.emptyNotice.gameObject.SetActive(true);
            if (library.detailPane != null) library.detailPane.gameObject.SetActive(true);
            if (library.searchInput != null) library.searchInput.gameObject.SetActive(false);
            SetButtonActive(library.allLists, false);
            SetButtonActive(library.favorites, false);
            SetButtonActive(library.createList, false);
            SetButtonActive(library.importList, false);
            SetButtonActive(library.exportList, false);
            if (library.filtersMenu.toggleButton != null)
                library.filtersMenu.toggleButton.gameObject.SetActive(false);
            if (library.filtersMenu.panel != null) library.filtersMenu.panel.SetActive(false);
            SetDropdownActive(library.languageFilter, false);
            SetDropdownActive(library.playedFilter, false);
            SetDropdownActive(library.sortFilter, false);
            SetDropdownActive(library.directionFilter, false);
            SetText(library.detailTitle, "LIBRARY LOCKED");
            SetText(library.detailDescription, BuildLockedPageMessage("Library", access));
            SetText(library.detailMetadata, "LOCKED");
            SetText(library.detailWordsLabel, string.Empty);
            SetText(library.detailWords, string.Empty);
            SetText(library.detailStatus, string.Empty);
            if (library.detailWordsLabel != null) library.detailWordsLabel.gameObject.SetActive(false);
            if (library.detailWords != null) library.detailWords.gameObject.SetActive(false);
            if (libraryRubyWordsView != null && libraryRubyWordsView.LayoutRoot != null)
            {
                libraryRubyWordsView.SetWords(Array.Empty<string>());
                libraryRubyWordsView.LayoutRoot.gameObject.SetActive(false);
            }
            SetLibraryAction(library.delete, productionMode ? null : string.Empty, false, null);
            SetLibraryAction(library.play, productionMode ? null : string.Empty, false, null);
            SetLibraryAction(library.edit, productionMode ? null : string.Empty, false, null);
            SetLibraryAction(library.copyEditWords, productionMode ? null : string.Empty, false, null);
            SetLibraryAction(library.publish, string.Empty, false, null);
            SetLibraryAction(library.unpublish, string.Empty, false, null);
            SetLibraryAction(library.exportList, productionMode ? null : string.Empty, false, null);
        }

        private void RefreshLockedCommunityPage(MockupHubPageAccess access)
        {
            WordListHubSceneBindings.CommunityPageBinding community = sceneBindings.community;
            selectedDiscoveryId = null;
            if (community.listScroll != null) community.listScroll.gameObject.SetActive(true);
            if (community.listContent != null) community.listContent.gameObject.SetActive(false);
            SetText(community.emptyNotice, BuildLockedPageMessage("Community", access));
            if (community.emptyNotice != null) community.emptyNotice.gameObject.SetActive(true);
            if (community.detailPane != null) community.detailPane.gameObject.SetActive(true);
            if (community.searchInput != null) community.searchInput.gameObject.SetActive(false);
            SetDropdownActive(community.connectionState, false);
            if (community.connectionMenu.toggleButton != null)
                community.connectionMenu.toggleButton.gameObject.SetActive(false);
            if (community.connectionMenu.panel != null) community.connectionMenu.panel.SetActive(false);
            SetText(community.detailTitle, "COMMUNITY LOCKED");
            SetText(community.detailAuthor, string.Empty);
            SetText(community.detailSummary, string.Empty);
            SetText(community.detailDescription, BuildLockedPageMessage("Community", access));
            SetText(community.detailMetadata, "LOCKED");
            SetText(community.detailTrustNote, "Select Challenges above to continue.");
            if (communityRubyWordsView != null && communityRubyWordsView.LayoutRoot != null)
                communityRubyWordsView.LayoutRoot.gameObject.SetActive(false);
            SetCommunityAction(community.like, string.Empty, false, null);
            SetCommunityAction(community.download, string.Empty, false, null);
            SetCommunityAction(community.play, productionMode ? null : string.Empty, false, null);
            SetCommunityAction(community.report, productionMode ? null : string.Empty, false, null);
        }

        private static void SetButtonActive(WordListHubSceneBindings.ButtonBinding binding, bool active)
        {
            if (binding != null && binding.button != null)
                binding.button.gameObject.SetActive(active);
        }

        private static void SetDropdownActive(TMP_Dropdown dropdown, bool active)
        {
            if (dropdown != null)
                dropdown.gameObject.SetActive(active);
        }

        private void RefreshCurrentPage()
        {
            RefreshPageAccessIndicators();
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
            SetText(row.metadata, LanguageLabel(item.Language) + "  ·  " + item.WordCount + " words");
            MockupWordList downloaded = backend.FindCommunityDownload(item.RemoteItemId);
            SetText(row.recordStars, PersonalStarsLabel(downloaded));
            SetText(row.bestTime, BestTimeLabel(downloaded));
            SetText(row.summary, item.Description);
            SetText(row.status, item.Status == MockupDiscoveryStatus.New ? "NEW" : "UPDATED");
            BindButton(row.button, () => SelectDiscoveryItem(item.RemoteItemId));
            ConfigureCommunityTile(row.root, row.button, row.selectedVisual,
                row.title, row.metadata, row.recordStars, row.bestTime, row.summary, row.status,
                TileSizeForWidth(lastCommunityTileViewportWidth > 0f
                    ? lastCommunityTileViewportWidth : WordListHubTileGridMetrics.PreferredTileSize));
        }

        private void UpdateDiscoveryRow(WordListHubDiscoveryRowView view, MockupDiscoveryItem item)
        {
            if (view == null)
                return;
            SetText(view.title, item.Title);
            SetText(view.metadata, LanguageLabel(item.Language) + "  ·  " + item.WordCount + " words");
            MockupWordList downloaded = backend.FindCommunityDownload(item.RemoteItemId);
            SetText(view.recordStars, PersonalStarsLabel(downloaded));
            SetText(view.bestTime, BestTimeLabel(downloaded));
            SetText(view.summary, item.Description);
            SetText(view.status, item.Status == MockupDiscoveryStatus.New ? "NEW" : "UPDATED");
            BindButton(view.button, () => SelectDiscoveryItem(item.RemoteItemId));
            ConfigureCommunityTile(view.gameObject, view.button, view.selectedVisual,
                view.title, view.metadata, view.recordStars, view.bestTime, view.summary, view.status,
                TileSizeForWidth(lastCommunityTileViewportWidth > 0f
                    ? lastCommunityTileViewportWidth : WordListHubTileGridMetrics.PreferredTileSize));
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
            MockupDiscoveryItem item = backend.FindDiscoveryItem(selectedDiscoveryId);
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
                SetCommunityAction(community.play, productionMode ? null : string.Empty, false, null);
                SetCommunityAction(community.report, productionMode ? null : string.Empty, false, null);
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
            SetText(community.detailTrustNote, CommunityTrustNote());
            SetCommunityAction(community.like,
                (item.IsLiked ? "♥ Liked" : "♡ Like") + "  ·  " + item.LikeCount,
                true, () => ToggleLike(item.RemoteItemId));
            SetCommunityAction(community.download,
                item.IsDownloaded ? "Downloaded" : "Download",
                true, () => DownloadDiscoveryItem(item.RemoteItemId));
            if (community.download != null && community.download.button != null)
                community.download.button.interactable = !item.IsDownloaded;
            SetCommunityAction(community.play, productionMode ? null : "Play", true,
                () => BeginMockPlay(item.RemoteItemId));
            SetCommunityAction(community.report, productionMode ? null : "Report & hide for me", true,
                () => ReportAndHideDiscoveryItem(item.RemoteItemId));
        }

        private void SetCommunityAction(
            WordListHubSceneBindings.ButtonBinding binding,
            string label,
            bool visible,
            UnityAction action)
        {
            if (binding == null)
                return;
            if (label != null)
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
            if (achievements.cards == null)
                achievements.cards = new List<WordListHubSceneBindings.AchievementCardBinding>();

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
            IEnumerable<MockupAchievement> filtered = backend.Achievements;
            if (achievementFilter == AchievementFilter.Unlocked)
                filtered = filtered.Where(item => item.Status == MockupAchievementStatus.Unlocked);
            else if (achievementFilter == AchievementFilter.InProgress)
                filtered = filtered.Where(item => item.Status == MockupAchievementStatus.InProgress);

            var authoredById = achievements.cards
                .Where(card => card != null && !string.IsNullOrEmpty(card.achievementId))
                .ToDictionary(card => card.achievementId, StringComparer.Ordinal);
            foreach (WordListHubSceneBindings.AchievementCardBinding card in achievements.cards)
            {
                GameObject cardRoot = card != null && card.root != null
                    ? card.root : card != null && card.view != null ? card.view.gameObject : null;
                if (cardRoot != null)
                    cardRoot.SetActive(false);
            }

            DestroyOwnedRuntimeChildren(achievements.cardContent);
            foreach (MockupAchievement achievement in filtered)
            {
                if (authoredById.TryGetValue(achievement.Id,
                    out WordListHubSceneBindings.AchievementCardBinding authored))
                {
                    PopulateAchievementCard(authored.view, achievement);
                    if (authored.root == null && authored.view != null)
                        authored.root = authored.view.gameObject;
                    if (authored.root != null)
                        authored.root.SetActive(true);
                }
                else
                {
                    WordListHubAchievementCardView view = Instantiate(
                        achievements.cardTemplate, achievements.cardContent, false);
                    view.name = "AchievementCard_" + achievement.Id;
                    if (view.GetComponent<WordListHubRuntimeInstance>() == null)
                        view.gameObject.AddComponent<WordListHubRuntimeInstance>();
                    PopulateAchievementCard(view, achievement);
                }
            }
            UpdateFooter();
        }

        private void PopulateAchievementCard(
            WordListHubAchievementCardView view,
            MockupAchievement achievement)
        {
            if (view == null || achievement == null)
                return;
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

        private void RefreshChallengesPage()
        {
            if (!productionMode)
            {
                SetText(sceneBindings.challenges.description,
                    "Challenge selection stays in the existing Levels screen.\n"
                    + "This isolated preview focuses on your word library, community lists and achievements.");
                SetButtonLabel(sceneBindings.challenges.dailyChallenge, "Open Word List");
                SetText(sceneBindings.challenges.status, string.Empty);
                UpdateFooter();
                return;
            }

            WordListHubSceneBindings.ChallengesPageBinding challenges = sceneBindings.challenges;
            if (challenges == null || challenges.listContent == null || challenges.listScroll == null
                || challenges.rowTemplate == null)
                throw new InvalidOperationException("Production challenge page bindings are missing.");

            SetText(challenges.description,
                "Official challenges unlock in order. Select one to preview its words, saved stars and best time.");
            List<MockupWordList> items = backend.QueryChallenges().ToList();
            PrepareManualTileContent(challenges.listContent);
            float availableWidth = GetTileViewportWidth(challenges.listScroll, challenges.listContent);
            lastChallengeTileViewportWidth = availableWidth;
            float tileSize = TileSizeForWidth(availableWidth);
            WordListHubLibraryRowView template = challenges.rowTemplate;
            SetTileRootSize(template.transform as RectTransform, tileSize);
            ConfigureLibraryTile(template.gameObject, template.button, template.selectedVisual,
                template.favoriteButton, template.favoriteMark, template.title,
                template.metadata, template.recordStars, template.bestTime,
                template.badgeRoot, template.badge, tileSize);
            DestroyOwnedRuntimeChildren(challenges.listContent);

            var rows = new Dictionary<string, WordListHubLibraryRowView>(StringComparer.Ordinal);
            var tiles = new List<RectTransform>();
            foreach (MockupWordList item in items)
            {
                WordListHubLibraryRowView view = Instantiate(challenges.rowTemplate, challenges.listContent, false);
                view.name = "ProductionChallenge_" + item.Id;
                view.itemId = item.Id;
                view.gameObject.SetActive(true);
                if (view.GetComponent<WordListHubRuntimeInstance>() == null)
                    view.gameObject.AddComponent<WordListHubRuntimeInstance>();
                UpdateChallengeRow(view, item, tileSize);
                rows[item.Id] = view;
                tiles.Add(view.transform as RectTransform);
            }
            float contentHeight = LayoutTileGrid(tiles, availableWidth, 0f);
            SetContentHeight(challenges.listContent, contentHeight);

            bool hasItems = items.Count > 0;
            if (challenges.emptyNotice != null)
            {
                challenges.emptyNotice.text = "No challenges are available for this profile and language.";
                challenges.emptyNotice.gameObject.SetActive(!hasItems);
            }
            if (string.IsNullOrEmpty(selectedChallengeId)
                || !items.Any(item => item.Id == selectedChallengeId))
                selectedChallengeId = items.FirstOrDefault()?.Id;

            foreach (KeyValuePair<string, WordListHubLibraryRowView> row in rows)
                SetSelectedVisual(row.Value.selectedVisual, row.Key == selectedChallengeId);

            MockupWordList selected = items.FirstOrDefault(item => item.Id == selectedChallengeId);
            if (selected == null)
            {
                SetChallengeDetailHeader(challenges, null);
                SetText(challenges.detailWordsLabel, "WORDS  ·  0");
                RefreshChallengeDetailWords(null, string.Empty);
                SetLibraryAction(challenges.play, productionMode ? null : string.Empty, false, null);
                SetLibraryAction(challenges.copyToCustom, productionMode ? null : string.Empty, false, null);
                SetText(challenges.status, lastActionMessage);
            }
            else
            {
                SetChallengeDetailHeader(challenges, selected);
                SetText(challenges.detailWordsLabel, "WORDS  ·  " + selected.WordCount);
                RefreshChallengeDetailWords(selected.Words, "No words in this challenge.");
                bool canCopy = selected.IsChallengeUnlocked
                    && backend.CanCreateLists && backend.CanDuplicateLists;
                SetLibraryAction(challenges.play, productionMode ? null : "Play", true,
                    selected.IsChallengeUnlocked ? (UnityAction)(() => BeginLibraryMockPlay(selected.Id)) : null);
                SetLibraryAction(challenges.copyToCustom,
                    productionMode ? null : "Copy to My Lists", true,
                    canCopy ? (UnityAction)(() => OpenEditDialog(selected.Id, true)) : null);
                if (challenges.play.button != null)
                    challenges.play.button.interactable = selected.IsChallengeUnlocked;
                if (challenges.copyToCustom.button != null)
                    challenges.copyToCustom.button.interactable = canCopy;
                SetText(challenges.status, string.IsNullOrEmpty(lastActionMessage)
                    ? selected.IsChallengeUnlocked ? "Choose Play to start this challenge." : "Complete earlier challenges to unlock Play and Copy."
                    : lastActionMessage);
            }

            if (challenges.detailPane != null)
                challenges.detailPane.gameObject.SetActive(true);
            UpdateFooter();
        }

        private static void SetChallengeDetailHeader(
            WordListHubSceneBindings.ChallengesPageBinding binding, MockupWordList selected)
        {
            if (selected == null)
            {
                SetText(binding.detailTitle, "No challenge selected");
                SetText(binding.detailMetadata, string.Empty);
                SetText(binding.detailRecords, string.Empty);
                return;
            }

            SetText(binding.detailTitle, selected.Title);
            SetText(binding.detailMetadata,
                (selected.IsChallengeUnlocked ? "UNLOCKED" : "LOCKED") + "  ·  "
                + LanguageLabel(selected.DetectedLanguage) + "  ·  " + selected.WordCount + " words");
            SetText(binding.detailRecords,
                PersonalStarsLabel(selected) + "  ·  Best " + BestTimeLabel(selected));
        }

        private void RefreshChallengeDetailWords(IReadOnlyList<string> words, string emptyMessage)
        {
            bool hasWords = words != null && words.Count > 0;
            SetText(sceneBindings.challenges.detailWords, hasWords
                ? string.Join("\n", words.Select(MockupWordContent.FormatForDetail))
                : emptyMessage);
            IReadOnlyList<string> visibleWords = hasWords
                ? words
                : string.IsNullOrWhiteSpace(emptyMessage) ? null : new[] { emptyMessage };
            challengeRubyWordsView?.SetWords(visibleWords);
        }

        private void UpdateChallengeRow(WordListHubLibraryRowView view, MockupWordList item, float tileSize)
        {
            if (view == null || item == null) return;
            view.unlockRewardLabel = ChallengeRewardBannerLabel(item.UnlockRewardLabel);
            SetText(view.favoriteMark, string.Empty);
            SetText(view.bestTime, BestTimeLabel(item));
            SetText(view.title, item.Title);
            string metadata = LanguageLabel(item.DetectedLanguage) + "  ·  " + item.WordCount + " words";
            SetText(view.metadata, metadata);
            SetText(view.recordStars, PersonalStarsLabel(item));
            SetText(view.badge, item.IsChallengeUnlocked ? "" : "LOCKED");
            if (view.badgeRoot != null)
                view.badgeRoot.SetActive(!item.IsChallengeUnlocked);
            if (!string.IsNullOrWhiteSpace(view.unlockRewardLabel))
            {
                TMP_Text rewardBanner = EnsureChallengeRewardBanner(view);
                SetText(rewardBanner, view.unlockRewardLabel);
                if (rewardBanner != null)
                    rewardBanner.gameObject.SetActive(true);
            }
            else if (view.unlockRewardBanner != null)
            {
                SetText(view.unlockRewardBanner, string.Empty);
                view.unlockRewardBanner.gameObject.SetActive(false);
            }
            if (view.favoriteButton != null)
                view.favoriteButton.gameObject.SetActive(false);
            BindButton(view.button, () => SelectChallengeItem(item.Id));
            ConfigureLibraryTile(view.gameObject, view.button, view.selectedVisual,
                view.favoriteButton, view.favoriteMark, view.title, view.metadata,
                view.recordStars, view.bestTime, view.badgeRoot, view.badge, tileSize);
            if (!string.IsNullOrWhiteSpace(view.unlockRewardLabel))
                ConfigureChallengeRewardBanner(view, tileSize);
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
            if (productionMode)
            {
                SetText(footerText, currentPage == Page.Community
                    ? "COMMUNITY PREVIEW  ·  SAMPLE DATA ONLY" : string.Empty);
                if (sceneBindings != null && sceneBindings.resetMockData != null
                    && sceneBindings.resetMockData.button != null)
                    sceneBindings.resetMockData.button.gameObject.SetActive(false);
                return;
            }

            SetText(footerText, "LOCAL MOCKUP  ·  SAMPLE DATA ONLY");
        }

        private void ShowActionFeedback(string message, bool isError = false)
        {
            lastActionMessage = string.IsNullOrWhiteSpace(message) ? string.Empty
                : message.Replace('<', '‹').Replace('>', '›');
            if (string.IsNullOrEmpty(lastActionMessage))
            {
                ClearActionFeedback();
                return;
            }

            actionFeedbackExpiresAt = Time.unscaledTime
                + (isError ? FailedActionFeedbackDuration : SuccessfulActionFeedbackDuration);
            TMP_Text status = null;
            if (sceneBindings != null)
            {
                if (currentPage == Page.Challenges && sceneBindings.challenges != null)
                    status = sceneBindings.challenges.status;
                else if (currentPage == Page.Library && sceneBindings.library != null)
                    status = sceneBindings.library.detailStatus;
                else if (currentPage == Page.Community && sceneBindings.community != null)
                    status = sceneBindings.community.detailTrustNote;
            }
            SetText(status, lastActionMessage);
        }

        private void ClearActionFeedback()
        {
            lastActionMessage = string.Empty;
            actionFeedbackExpiresAt = 0f;
            if (sceneBindings == null) return;
            if (currentPage == Page.Challenges && sceneBindings.challenges != null)
            {
                MockupWordList selected = backend != null ? backend.FindById(selectedChallengeId) : null;
                SetText(sceneBindings.challenges.status, selected == null
                    ? string.Empty
                    : selected.IsChallengeUnlocked
                        ? "Choose Play to start this challenge."
                        : "Complete earlier challenges to unlock Play and Copy.");
            }
            else if (currentPage == Page.Community && sceneBindings.community != null)
                SetText(sceneBindings.community.detailTrustNote, CommunityTrustNote());
            else
                SetText(sceneBindings.library != null ? sceneBindings.library.detailStatus : null, string.Empty);
        }

        private string CommunityTrustNote()
        {
            return productionMode
                ? "SAMPLE DATA ONLY. Download, play, likes and reports stay in Community Preview; none are live or added to My Lists."
                : "Downloads are read-only and appear in Community Downloads. Likes are separate.";
        }

        private void OpenNewListDialog()
        {
            newListLanguage = MockupLanguage.English;
            newListLanguageFallback = MockupLanguage.English;
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
            dialogValidationText = form.validationText;
            SetText(form.validationText, string.Empty);
            if (form.languageDropdown != null)
            {
                SetLanguageDropdownValue(form.languageDropdown, newListLanguage);
                form.languageDropdown.interactable = false;
            }
            UpdateNewListLanguageDisplay(newListWordsInput != null ? newListWordsInput.text : string.Empty, false);
        }

        private void HandleNewListWordsChanged(string value)
        {
            UpdateNewListLanguageDisplay(value, true);
        }

        private bool UpdateNewListLanguageDisplay(string editorText, bool showError)
        {
            if (!WordListLanguageDetector.TryDetectEditorLines(
                ParseWords(editorText), ToDetectorLanguage(newListLanguageFallback),
                out LanguageMode detected, out _, out string error))
            {
                if (showError)
                    SetDialogValidation(error);
                return false;
            }

            newListLanguage = ToMockupLanguage(detected);
            WordListHubSceneBindings.FormBinding form = sceneBindings != null
                && sceneBindings.modal != null ? sceneBindings.modal.newList : null;
            if (form != null && form.languageDropdown != null)
            {
                SetLanguageDropdownValue(form.languageDropdown, newListLanguage);
                form.languageDropdown.interactable = false;
            }
            if (showError && dialogValidationText != null)
                dialogValidationText.text = string.Empty;
            return true;
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
            if (!UpdateNewListLanguageDisplay(wordText, true))
                return;

            string description = newListDescriptionInput != null ? newListDescriptionInput.text.Trim() : string.Empty;
            string tags = newListTagsInput != null ? newListTagsInput.text.Trim() : string.Empty;
            bool created;
            MockupWordList item;
            string error;
            if (!string.IsNullOrEmpty(pendingImportToken))
            {
                var importDraft = new MockupWordListEdit
                {
                    Title = title,
                    Description = description,
                    Tags = tags,
                    Language = newListLanguage,
                    Words = words,
                    WordCount = words.Count
                };
                created = backend.TryCommitImport(pendingImportToken, importDraft,
                    out item, out error);
            }
            else
            {
                created = backend.TryCreateList(title, newListLanguage, words,
                    description, tags, out item, out error);
            }
            if (!created)
            {
                SetDialogValidation(string.IsNullOrWhiteSpace(error) ? "The list could not be created." : error);
                return;
            }
            pendingImportToken = null;
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
            MockupWordList item = backend.FindById(mockId);
            if (item == null)
                return;

            bool copyingUnlockedChallenge = copyAsNewList && item.IsChallenge && item.IsChallengeUnlocked;
            if (item.IsReadOnly && !copyingUnlockedChallenge)
            {
                ShowActionFeedback("This list is read-only. Only an unlocked challenge can be copied to My Lists.", true);
                return;
            }
            if (copyingUnlockedChallenge && (!backend.CanCreateLists || !backend.CanDuplicateLists))
            {
                ShowActionFeedback("Copying challenges to My Lists is disabled by the active profile.", true);
                return;
            }

            editingListId = item.Id;
            bool hasRemoteIdentity = !string.IsNullOrEmpty(item.RemoteItemId);
            if (copyAsNewList && item.Ownership != MockupOwnership.Owned && !copyingUnlockedChallenge)
                return;
            if (hasRemoteIdentity && (!copyAsNewList || !item.HasPublishedBinding))
                return;

            copyEditingMode = copyAsNewList;
            editDraft = backend.CreateEditDraft(item.Id);
            if (editDraft.Words == null)
                editDraft.Words = item.Words == null ? new List<string>() : new List<string>(item.Words);
            editWordSession = new MockupWordListEditSession(editDraft.Words);
            editLanguageFallback = item.Language;
            editLanguage = editLanguageFallback;
            UpdateEditLanguageDisplay(false);
            RenderEditDialog();
        }

        private void RenderEditDialog()
        {
            if (editDraft == null)
                return;
            if (editWordSession == null)
                editWordSession = new MockupWordListEditSession(editDraft.Words);

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
            HideEditTagsControls(form);
            editWordsInput = form.wordsInput;
            editSaveButton = form.saveButton;
            editSaveHintText = form.saveHintText;
            dialogValidationText = form.validationText;

            if (editTitleInput != null)
                editTitleInput.SetTextWithoutNotify(editDraft.Title ?? string.Empty);
            if (editDescriptionInput != null)
                editDescriptionInput.SetTextWithoutNotify(editDraft.Description ?? string.Empty);
            if (editWordsInput != null)
            {
                editWordsInput.SetTextWithoutNotify(editWordSession.SelectedWord);
                editWordsInput.interactable = true;
            }
            if (form.languageDropdown != null)
            {
                SetLanguageDropdownValue(form.languageDropdown, editLanguage);
                form.languageDropdown.interactable = false;
            }
            SetText(form.validationText, string.Empty);
            if (editSaveButton != null)
                SetText(form.saveButtonLabel, copyEditingMode ? "Save editable copy" : "Save changes");
            if (editWordEditorView == null)
            {
                editWordEditorView = new MockupWordListEditorView(
                    form, SelectEditWord, AddEditWord, MoveEditWord, DeleteEditWord, EnsureButtonSound);
            }
            editWordEditorView.Render(editWordSession, SelectedWordPlaceholder(editLanguage), false);
            UpdateEditLanguageDisplay(false);
            UpdateEditSaveButton();
        }

        private void HideEditTagsControls(WordListHubSceneBindings.FormBinding form)
        {
            if (form != null && form.tagsInput != null)
                form.tagsInput.gameObject.SetActive(false);
            Transform tagsLabel = form != null && form.root != null
                ? form.root.transform.Find("EditListFormTagsLabel")
                : null;
            if (tagsLabel != null)
                tagsLabel.gameObject.SetActive(false);

            // A missing edit field means CaptureEditInputs and SaveEditClicked keep
            // the tags already stored in the draft.
            editTagsInput = null;
        }

        private void HandleEditWordsChanged(string value)
        {
            CaptureEditInputs();
            if (editWordEditorView != null)
                editWordEditorView.Render(editWordSession, SelectedWordPlaceholder(editLanguage), false);
            UpdateEditLanguageDisplay(true);
            UpdateEditSaveButton();
        }

        private bool UpdateEditLanguageDisplay(bool showError)
        {
            IEnumerable<string> rows = editWordSession != null
                ? editWordSession.ToList()
                : editDraft != null ? editDraft.Words : Enumerable.Empty<string>();
            if (!WordListLanguageDetector.TryDetectEditorLines(
                rows, ToDetectorLanguage(editLanguageFallback),
                out LanguageMode detected, out _, out string error))
            {
                if (showError)
                    SetDialogValidation(error);
                return false;
            }

            editLanguage = ToMockupLanguage(detected);
            WordListHubSceneBindings.FormBinding form = sceneBindings != null
                && sceneBindings.modal != null ? sceneBindings.modal.editList : null;
            if (form != null && form.languageDropdown != null)
            {
                SetLanguageDropdownValue(form.languageDropdown, editLanguage);
                form.languageDropdown.interactable = false;
            }
            if (showError && dialogValidationText != null)
                dialogValidationText.text = string.Empty;
            return true;
        }

        private void SelectEditWord(int index)
        {
            CaptureEditInputs();
            if (editWordSession == null)
                return;

            editWordSession.Select(index);
            ShowSelectedEditWord(true);
        }

        private void AddEditWord()
        {
            CaptureEditInputs();
            if (editWordSession == null)
                return;

            editWordSession.AddAtFront();
            ShowSelectedEditWord(true);
        }

        private void MoveEditWord(int direction)
        {
            CaptureEditInputs();
            if (editWordSession == null || !editWordSession.MoveSelected(direction))
                return;

            ShowSelectedEditWord(true);
        }

        private void DeleteEditWord()
        {
            CaptureEditInputs();
            if (editWordSession == null || !editWordSession.DeleteSelected())
                return;

            ShowSelectedEditWord(true);
        }

        private void ShowSelectedEditWord(bool activateInput)
        {
            if (editWordEditorView != null)
                editWordEditorView.Render(editWordSession, SelectedWordPlaceholder(editLanguage), false);

            if (editWordsInput != null)
            {
                string selectedWord = editWordSession != null
                    ? editWordSession.SelectedWord : string.Empty;
                editWordsInput.SetTextWithoutNotify(selectedWord);
                editWordsInput.caretPosition = selectedWord.Length;
                if (activateInput && editWordSession != null && editWordSession.HasSelection)
                    editWordsInput.ActivateInputField();
            }

            CaptureEditInputs();
            UpdateEditLanguageDisplay(true);
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
            {
                if (editWordSession == null)
                    editWordSession = new MockupWordListEditSession(editDraft.Words);
                editWordSession.SetSelectedWord(editWordsInput.text);
                editDraft.Words = editWordSession.ToList();
            }
            editDraft.WordCount = editDraft.Words != null ? editDraft.Words.Count : 0;
        }

        private bool EditHasGameplayChange()
        {
            MockupWordList current = backend.FindById(editingListId);
            return current != null
                && !WordsEqualAfterTrim(current.Words, editDraft != null ? editDraft.Words : null);
        }

        private static bool WordsEqualAfterTrim(IEnumerable<string> left, IEnumerable<string> right)
        {
            return (left ?? Enumerable.Empty<string>()).Select(word => (word ?? string.Empty).Trim())
                .SequenceEqual(
                    (right ?? Enumerable.Empty<string>()).Select(word => (word ?? string.Empty).Trim()),
                    StringComparer.Ordinal);
        }

        private void SaveEditClicked()
        {
            if (editDraft == null)
                return;

            CaptureEditInputs();
            editDraft.Title = editTitleInput != null ? editTitleInput.text : editDraft.Title;
            editDraft.Description = editDescriptionInput != null ? editDescriptionInput.text : editDraft.Description;
            editDraft.Tags = editTagsInput != null ? editTagsInput.text.Trim() : editDraft.Tags;
            if (editWordSession == null)
                editWordSession = new MockupWordListEditSession(editDraft.Words);
            editDraft.Words = editWordSession.ToList();
            editDraft.WordCount = editDraft.Words != null ? editDraft.Words.Count : 0;

            if (string.IsNullOrWhiteSpace(editDraft.Title))
            {
                SetDialogValidation("Enter a list name.");
                return;
            }
            if (editDraft.Words == null || editDraft.Words.Count == 0)
            {
                SetDialogValidation("Add at least one word.");
                return;
            }
            int blankWordIndex = editDraft.Words.FindIndex(string.IsNullOrWhiteSpace);
            if (blankWordIndex >= 0)
            {
                SetDialogValidation("Word " + (blankWordIndex + 1)
                    + " is blank. Enter a word or delete that entry.");
                return;
            }
            int multilineWordIndex = editDraft.Words.FindIndex(word =>
                word.IndexOf('\n') >= 0 || word.IndexOf('\r') >= 0);
            if (multilineWordIndex >= 0)
            {
                SetDialogValidation("Word " + (multilineWordIndex + 1)
                    + " must stay on one line.");
                return;
            }
            string wordText = string.Join("\n", editDraft.Words);
            IEnumerable<string> unchangedLegacyWords = null;
            MockupWordList original = backend.FindById(editingListId);
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
                ? backend.SaveOwnedContentAsNewList(editingListId, editDraft)
                : backend.SaveEdit(editingListId, editDraft);
            if (result == null || !result.Succeeded)
            {
                string error = result != null && !string.IsNullOrWhiteSpace(result.Message)
                    ? result.Message : "The list could not be saved.";
                RenderEditDialog();
                SetDialogValidation(error);
                return;
            }
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

        private static string SelectedWordPlaceholder(MockupLanguage language)
        {
            switch (language)
            {
                case MockupLanguage.Chinese:
                    return "苹果 | ping guo";
                case MockupLanguage.Mixed:
                    return "English word or 汉字 | pinyin";
                default:
                    return "Enter one word";
            }
        }

        private static bool WordsEqual(IEnumerable<string> left, IEnumerable<string> right)
        {
            return (left ?? Enumerable.Empty<string>()).SequenceEqual(
                right ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
        }

        private void CloseDialog()
        {
            pendingImportToken = null;
            if (editWordEditorView != null)
            {
                editWordEditorView.Dispose();
                editWordEditorView = null;
            }
            if (sceneBindings != null && sceneBindings.modal != null && sceneBindings.modal.root != null)
            {
                sceneBindings.modal.root.SetActive(false);
                SetModalSection(null);
            }
            dialogContentRoot = null;
            editDraft = null;
            editWordSession = null;
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

        private static string PersonalStarsLabel(MockupWordList item)
        {
            if (item == null || !item.PersonalStars.HasValue || item.PersonalStars.Value <= 0)
                return "☆☆☆";
            return new string('★', Mathf.Clamp(item.PersonalStars.Value, 0, 3));
        }

        private static string BestTimeLabel(MockupWordList item)
        {
            return item != null && item.BestTimeSeconds.HasValue
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

}
