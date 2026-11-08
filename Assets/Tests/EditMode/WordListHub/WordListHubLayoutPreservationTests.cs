using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Words.UiMockups.WordListHub.Tests
{
    /// <summary>
    /// Scene-level regression coverage for the authored Word List Hub UI. The tests
    /// clone the scene hierarchy into an unsaved temporary scene so controller work
    /// cannot modify the user's open or saved mockup scene.
    /// </summary>
    [TestFixture]
    public sealed class WordListHubLayoutPreservationTests
    {
        private const string MockupScenePath = "Assets/Scenes/WordListHubMockup.unity";

        private SceneHarness harness;

        [SetUp]
        public void SetUp()
        {
            harness = SceneHarness.Open();
        }

        [TearDown]
        public void TearDown()
        {
            if (harness != null)
            {
                harness.Dispose();
                harness = null;
            }
        }

        [Test]
        public void SerializedBindings_ReferenceSceneTemplatesAndConfiguredLayoutGroups()
        {
            WordListHubSceneBindings bindings = harness.Bindings;
            Assert.That(bindings.card, Is.Not.Null);
            Assert.That(bindings.hubRoot, Is.Not.Null);
            Assert.That(bindings.footer, Is.Not.Null);
            Assert.That(bindings.library.detailStatus.text, Is.Empty,
                "The serialized action prompt should be blank before the controller runs.");

            WordListHubPage[] expectedPages = (WordListHubPage[])Enum.GetValues(typeof(WordListHubPage));
            Assert.That(bindings.pages, Is.Not.Null);
            Assert.That(bindings.pages.Select(page => page.page).Distinct().Count(), Is.EqualTo(expectedPages.Length));
            foreach (WordListHubPage page in expectedPages)
            {
                Assert.That(bindings.pages.Count(binding => binding.page == page && binding.root != null),
                    Is.EqualTo(1), "Exactly one authored root must be assigned for " + page + ".");
            }

            Type challengeBindingType = typeof(WordListHubSceneBindings.ChallengesPageBinding);
            Assert.That(challengeBindingType.GetField("trackLabel", BindingFlags.Instance | BindingFlags.Public),
                Is.Null, "Challenge language/track context belongs in the existing detail metadata, not a separate label binding.");
            Assert.That(challengeBindingType.GetField("detailMetadata", BindingFlags.Instance | BindingFlags.Public),
                Is.Not.Null, "The Challenge detail page retains the Library detail metadata field.");

            AssertLayoutGroup(bindings.library.listContent, "Library list content");
            AssertLayoutGroup(bindings.community.listContent, "Community list content");
            AssertLayoutGroup(bindings.achievements.cardContent, "Achievement card content");

            AssertLibraryTemplate(bindings.library.rowTemplate);
            AssertCommunityTemplate(bindings.community.rowTemplate);
            AssertCommunityVisualAlignment(bindings);
            AssertAchievementTemplate(bindings.achievements.cardTemplate);
            AssertAuthoredLibraryRows(bindings.library);
            AssertAuthoredCommunityRows(bindings.community);

            AssertDropdownTemplate(bindings.library.languageFilter, "Library language filter");
            AssertDropdownTemplate(bindings.library.playedFilter, "Library played filter");
            AssertDropdownTemplate(bindings.library.sortFilter, "Library sort filter");
            AssertDropdownTemplate(bindings.library.directionFilter, "Library direction filter");
            AssertDropdownTemplate(bindings.community.connectionState, "Community connection state");
            AssertDropdownTemplate(bindings.achievements.filter, "Achievement filter");
            AssertDropdownTemplate(bindings.modal.newList.languageDropdown, "Create form language");
            AssertDropdownTemplate(bindings.modal.editList.languageDropdown, "Edit form language");

            AssertFormBindings(bindings.modal.newList, "Create form", true);
            AssertFormBindings(bindings.modal.editList, "Edit form", true);
            AssertConfirmationBindings(bindings.modal.contentChange, "Content change confirmation");
            AssertConfirmationBindings(bindings.modal.deleteList, "Delete confirmation");
            AssertConfirmationBindings(bindings.modal.playResult, "Play result confirmation");
        }

        [Test]
        public void LockedLibraryAndCommunityHideTheirScrollbarsAndUseCustomNoticeColor()
        {
            WordListHubMockupController controller = harness.Controller;
            WordListHubSceneBindings bindings = harness.Bindings;
            InvokeAwake(controller);

            Transform customSurface = bindings.library.detailPane.Find("DetailWordsPanel");
            Transform communitySurface = bindings.community.detailPane.Find("DiscoverySample");
            Assert.That(customSurface, Is.Not.Null);
            Assert.That(communitySurface, Is.Not.Null);
            Image customSurfaceImage = customSurface.GetComponent<Image>();
            Image communitySurfaceImage = communitySurface.GetComponent<Image>();
            Assert.That(customSurfaceImage, Is.Not.Null);
            Assert.That(communitySurfaceImage, Is.Not.Null);
            Color originalCustomSurfaceColor = customSurfaceImage.color;
            Color originalCommunitySurfaceColor = communitySurfaceImage.color;
            Color expectedLockedSurfaceColor = new Color(0.29f, 0.74f, 0.51f, 0.81f);
            customSurfaceImage.color = expectedLockedSurfaceColor;

            Scrollbar libraryScrollbar = bindings.library.listScroll.verticalScrollbar;
            Scrollbar communityScrollbar = bindings.community.listScroll.verticalScrollbar;
            Assert.That(libraryScrollbar, Is.Not.Null);
            Assert.That(communityScrollbar, Is.Not.Null);
            bool libraryScrollbarInitiallyActive = libraryScrollbar.gameObject.activeSelf;
            bool communityScrollbarInitiallyActive = communityScrollbar.gameObject.activeSelf;
            Color originalCustomNoticeColor = bindings.library.emptyNotice.color;
            Color originalCommunityColor = bindings.community.emptyNotice.color;
            Color customCanonicalColor = new Color(0.17f, 0.63f, 0.82f, 0.73f);
            Assert.That(originalCustomNoticeColor, Is.Not.EqualTo(customCanonicalColor));
            bindings.library.emptyNotice.color = customCanonicalColor;

            InvokePrivate(controller, "RefreshLockedLibraryPage",
                new MockupHubPageAccess(false, 0, 5, "English"));
            Assert.That(libraryScrollbar.gameObject.activeSelf, Is.False);
            Assert.That(bindings.library.emptyNotice.color, Is.EqualTo(customCanonicalColor),
                "The locked Library notice preserves its authored Custom color.");

            InvokePrivate(controller, "RefreshLockedCommunityPage",
                new MockupHubPageAccess(false, 0, 10, "English"));
            Assert.That(communityScrollbar.gameObject.activeSelf, Is.False);
            Assert.That(bindings.community.emptyNotice.color, Is.EqualTo(bindings.library.emptyNotice.color),
                "The locked Community notice uses the existing Custom notice color.");
            Assert.That(communitySurfaceImage.color, Is.EqualTo(expectedLockedSurfaceColor),
                "The locked Community preview surface uses the Custom detail panel surface color.");

            InvokePrivate(controller, "RestoreLibraryPageControls");
            InvokePrivate(controller, "RestoreCommunityPageControls");
            Assert.That(libraryScrollbar.gameObject.activeSelf, Is.EqualTo(libraryScrollbarInitiallyActive));
            Assert.That(communityScrollbar.gameObject.activeSelf, Is.EqualTo(communityScrollbarInitiallyActive));
            Assert.That(bindings.community.emptyNotice.color, Is.EqualTo(originalCommunityColor),
                "Unlocking Community restores its authored notice color.");
            Assert.That(communitySurfaceImage.color, Is.EqualTo(originalCommunitySurfaceColor),
                "Unlocking Community restores its authored detail surface color.");
            Assert.That(bindings.library.emptyNotice.color, Is.EqualTo(customCanonicalColor),
                "Community restoration does not change the Custom notice color.");
            bindings.library.emptyNotice.color = originalCustomNoticeColor;
            customSurfaceImage.color = originalCustomSurfaceColor;
        }

        [Test]
        public void LibraryAndCommunityTilesRenderFourRowsAndKeepLikesInDetails()
        {
            WordListHubMockupController controller = harness.Controller;
            WordListHubSceneBindings bindings = harness.Bindings;
            InvokeAwake(controller);
            MockupWordListStore store = GetStore(controller);

            MockupWordList libraryItem = store.FindById("mock-list-empty");
            store.CompleteRun(libraryItem.Id, 2, 12.5f);
            InvokeRefreshLibraryPage(controller);
            WordListHubSceneBindings.LibraryRowBinding libraryRow = bindings.library.authoredRows
                .Single(row => row != null && row.listId == libraryItem.Id);
            Assert.That(libraryRow.metadata.text, Does.EndWith("0 words"));
            Assert.That(libraryRow.metadata.text, Does.Not.Contain("★"));
            Assert.That(libraryRow.recordStars.text, Is.EqualTo("★★"));
            Assert.That(libraryRow.bestTime.text, Is.EqualTo("12.5 s"));
            AssertTileInformationRows(libraryRow.title, libraryRow.metadata,
                libraryRow.recordStars, libraryRow.bestTime, "Library tile");

            const string remoteItemId = "mock-ugc-weekend-garden";
            MockupDiscoveryItem discoveryItem = store.FindDiscoveryItem(remoteItemId);
            InvokeChangePage(controller, "Community");
            WordListHubSceneBindings.DiscoveryRowBinding communityRow = bindings.community.authoredRows
                .Single(row => row != null && row.remoteItemId == remoteItemId);
            Assert.That(communityRow.metadata.text, Does.EndWith(discoveryItem.WordCount + " words"));
            Assert.That(communityRow.metadata.text, Does.Not.Contain("likes"));
            Assert.That(communityRow.recordStars.text, Is.EqualTo("☆☆☆"));
            Assert.That(communityRow.bestTime.text, Is.EqualTo("—"));
            AssertTileInformationRows(communityRow.title, communityRow.metadata,
                communityRow.recordStars, communityRow.bestTime, "Community tile");

            Press(communityRow.button);
            Press(bindings.community.like.button);
            Assert.That(bindings.community.like.label.text, Does.Contain(discoveryItem.LikeCount.ToString()));

            MockupWordList downloaded = store.Download(remoteItemId);
            store.CompleteRun(downloaded.Id, 3, 7.4f);
            InvokeChangePage(controller, "Community");
            Assert.That(communityRow.recordStars.text, Is.EqualTo("★★★"));
            Assert.That(communityRow.bestTime.text, Is.EqualTo("7.4 s"));
            Assert.That(bindings.community.like.label.text, Does.Contain(discoveryItem.LikeCount.ToString()));
        }

        [Test]
        public void OfficialChallengeCopyOpensEditableDraft_whileInPlaceEditIsRejected()
        {
            WordListHubMockupController controller = harness.Controller;
            InvokeAwake(controller);
            MockupWordListStore store = GetStore(controller);
            MockupWordList challenge = store.FindById("mock-list-travel-copy");
            Assert.That(challenge, Is.Not.Null);
            typeof(MockupWordList).GetProperty("IsChallenge").SetValue(challenge, true);
            typeof(MockupWordList).GetProperty("IsChallengeUnlocked").SetValue(challenge, true);

            MethodInfo openEdit = typeof(WordListHubMockupController).GetMethod(
                "OpenEditDialog", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(openEdit, Is.Not.Null);
            openEdit.Invoke(controller, new object[] { challenge.Id, true });

            Assert.That(harness.Bindings.modal.root.activeSelf, Is.True);
            Assert.That(harness.Bindings.modal.editList.root.activeSelf, Is.True);
            Assert.That(typeof(WordListHubMockupController).GetField(
                "copyEditingMode", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller), Is.True);

            typeof(WordListHubMockupController).GetMethod(
                "CloseDialog", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
            openEdit.Invoke(controller, new object[] { challenge.Id, false });

            Assert.That(harness.Bindings.modal.root.activeSelf, Is.False,
                "An official challenge can only open as a separate copy, never as an in-place edit.");
            Assert.That(harness.Bindings.library.detailStatus.text, Does.Contain("read-only"));
        }

        [Test]
        public void ButtonSoundRelayRebindsAfterRemoveAllListenersAndSurvivesCloseDeactivation()
        {
            GameObject buttonObject = new GameObject("Sound relay test button");
            GameObject audioObject = new GameObject("Sound relay test source");
            buttonObject.SetActive(false);
            AudioClip clip = AudioClip.Create("silent click", 4410, 1, 44100, false);
            try
            {
                Button button = buttonObject.AddComponent<Button>();
                AudioSource source = audioObject.AddComponent<AudioSource>();
                source.clip = clip;
                source.mute = true;
                WordListHubButtonSoundRelay relay = WordListHubButtonSoundRelay.Attach(button, source);
                Assert.That(relay, Is.Not.Null);
                Assert.That(relay.IsListening, Is.True,
                    "An inactive authored Button still needs its runtime listener before the Hub is shown.");

                int runtimeCalls = GetRuntimeUnityCallCount(button.onClick);
                WordListHubButtonSoundRelay.Attach(button, source);
                WordListHubButtonSoundRelay.Attach(button, source);
                Assert.That(GetRuntimeUnityCallCount(button.onClick), Is.EqualTo(runtimeCalls),
                    "Repeated attachment must retain one owned sound callback.");

                button.onClick.RemoveAllListeners();
                Assert.That(GetRuntimeUnityCallCount(button.onClick), Is.Zero);
                WordListHubButtonSoundRelay.Attach(button, source);
                Assert.That(GetRuntimeUnityCallCount(button.onClick), Is.EqualTo(1),
                    "The next binding pass must restore the listener cleared by RemoveAllListeners.");

                buttonObject.SetActive(true);
                bool closeRanAfterSound = false;
                button.onClick.AddListener(() =>
                {
                    closeRanAfterSound = relay.IsListening && source.isPlaying;
                    buttonObject.SetActive(false);
                });
                button.onClick.Invoke();
                Assert.That(closeRanAfterSound, Is.True,
                    "The close action must observe the sound listener after it has started the shared source.");
                Assert.That(relay.IsListening, Is.True,
                    "Deactivating the Hub during its click must not remove the relay before the event finishes.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(buttonObject);
                UnityEngine.Object.DestroyImmediate(audioObject);
                UnityEngine.Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void ButtonSoundRelaySkipsOnlyEnabledPersistentMenuPlayEvents()
        {
            GameObject buttonObject = new GameObject("Persistent sound test button");
            GameObject audioObject = new GameObject("Persistent sound test source");
            try
            {
                Button button = buttonObject.AddComponent<Button>();
                AudioSource source = audioObject.AddComponent<AudioSource>();
                UnityEventTools.AddPersistentListener(button.onClick, source.Play);

                WordListHubButtonSoundRelay relay = WordListHubButtonSoundRelay.Attach(button, source);
                Assert.That(button.onClick.GetPersistentListenerState(0), Is.Not.EqualTo(UnityEventCallState.Off));
                Assert.That(relay.IsListening, Is.False,
                    "An enabled persistent Menu.Play call already provides the click sound.");

                SerializedObject serializedButton = new SerializedObject(button);
                SerializedProperty persistentCall = serializedButton
                    .FindProperty("m_OnClick.m_PersistentCalls.m_Calls")
                    .GetArrayElementAtIndex(0);
                persistentCall.FindPropertyRelative("m_CallState").intValue = (int)UnityEventCallState.Off;
                serializedButton.ApplyModifiedPropertiesWithoutUndo();
                WordListHubButtonSoundRelay.Attach(button, source);
                Assert.That(relay.IsListening, Is.True,
                    "A disabled persistent call does not provide sound and must not suppress the relay.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(buttonObject);
                UnityEngine.Object.DestroyImmediate(audioObject);
            }
        }

        private static int GetRuntimeUnityCallCount(UnityEventBase unityEvent)
        {
            FieldInfo callsField = typeof(UnityEventBase).GetField("m_Calls",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(callsField, Is.Not.Null);
            object calls = callsField.GetValue(unityEvent);
            FieldInfo runtimeCallsField = calls.GetType().GetField("m_RuntimeCalls",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(runtimeCallsField, Is.Not.Null);
            return ((System.Collections.ICollection)runtimeCallsField.GetValue(calls)).Count;
        }

        [Test]
        public void RuntimeTilesAndEditWordControlsPreserveGroupsAndUncommittedPinyin()
        {
            WordListHubMockupController controller = harness.Controller;
            WordListHubSceneBindings bindings = harness.Bindings;
            InvokeAwake(controller);

            MockupWordListStore store = GetStore(controller);
            for (int index = 0; index < 8; index++)
                store.CreateList("Grid fixture " + index, MockupLanguage.English, new[] { "apple" });
            InvokeRefreshLibraryPage(controller);
            Canvas.ForceUpdateCanvases();

            WordListHubSceneBindings.LibraryPageBinding library = bindings.library;
            RectTransform libraryContent = library.listContent as RectTransform;
            Assert.That(library.listContent.GetComponent<VerticalLayoutGroup>().enabled, Is.False);
            Assert.That(library.listContent.GetComponent<ContentSizeFitter>().enabled, Is.False);
            Assert.That(libraryContent.rect.height, Is.GreaterThan(library.listScroll.viewport.rect.height));
            Assert.That(GetVisibleLibraryRowIds(library).Count,
                Is.EqualTo(store.QueryLibrary(new MockupLibraryQuery()).Count));
            AssertTileSetMatchesGrid(library.listContent.GetComponentsInChildren<WordListHubLibraryRowView>(true)
                .Where(view => view != null && view.gameObject.activeInHierarchy)
                .Select(view => view.transform as RectTransform), libraryContent.rect.width,
                "Runtime Library tiles");

            WordListHubSceneBindings.GroupBinding myListsHeader = library.groups
                .Single(group => group.groupKey == MockupLibraryGroup.MyLists.ToString());
            int myListsCount = store.QueryLibrary(new MockupLibraryQuery())
                .Count(item => item.Group == MockupLibraryGroup.MyLists);
            int expandedRows = GetVisibleLibraryRowIds(library).Count;
            float expandedHeight = libraryContent.rect.height;
            Press(myListsHeader.button);
            Assert.That(myListsHeader.label.text, Does.Contain(myListsCount.ToString()));
            Assert.That(myListsHeader.label.text, Does.Contain(">"));
            Assert.That(GetVisibleLibraryRowIds(library).Count, Is.LessThan(expandedRows));
            Assert.That(libraryContent.rect.height, Is.LessThan(expandedHeight));
            Press(myListsHeader.button);
            Assert.That(libraryContent.rect.height, Is.EqualTo(expandedHeight).Within(1f));

            Press(bindings.navCommunity.button);
            Canvas.ForceUpdateCanvases();
            Assert.That(bindings.community.searchInput, Is.Not.Null);
            Assert.That(bindings.community.connectionMenu, Is.Not.Null);
            RectTransform communityContent = bindings.community.listContent as RectTransform;
            List<RectTransform> visibleCommunityTiles = Enumerable.Range(0, communityContent.childCount)
                .Select(index => communityContent.GetChild(index))
                .Where(child => child.gameObject.activeSelf && child.GetComponent<Button>() != null)
                .Select(child => child as RectTransform)
                .ToList();
            Assert.That(visibleCommunityTiles, Is.Not.Empty);
            AssertTileSetMatchesGrid(visibleCommunityTiles, communityContent.rect.width,
                "Runtime Community tiles");
            Assert.That(bindings.community.authoredRows.Where(row => row != null && row.root.activeInHierarchy)
                .All(row => row.summary == null || !row.summary.gameObject.activeSelf), Is.True);

            Press(bindings.navLibrary.button);
            MockupWordList editable = store.FindById("mock-list-travel-copy");
            string[] originalWords = editable.Words.ToArray();
            string originalTitle = editable.Title;
            Press(FindLibraryButton(library, editable.Id));
            Press(library.edit.button);
            WordListHubSceneBindings.FormBinding edit = bindings.modal.editList;
            Assert.That(edit.wordsInput.lineType, Is.EqualTo(TMP_InputField.LineType.SingleLine));
            Transform editorRoot = edit.wordEditorLayout.root.transform;
            Assert.That(editorRoot, Is.Not.Null, "The serialized editor layout should be reused in Play Mode.");
            Transform rowsContent = editorRoot.Find("WordRowsViewport/WordRowsContent");
            Button[] wordRows = rowsContent.GetComponentsInChildren<Button>(true)
                .Where(button => button.name == "WordRow").ToArray();
            Assert.That(wordRows.Length, Is.EqualTo(originalWords.Length));
            Button moveUp = editorRoot.Find("MoveWordUp").GetComponent<Button>();
            Button moveDown = editorRoot.Find("MoveWordDown").GetComponent<Button>();
            Button addWord = editorRoot.Find("AddWord").GetComponent<Button>();
            Button deleteWord = editorRoot.Find("DeleteWord").GetComponent<Button>();
            FieldInfo sessionField = typeof(WordListHubMockupController).GetField(
                "editWordSession", BindingFlags.Instance | BindingFlags.NonPublic);
            MockupWordListEditSession session = (MockupWordListEditSession)sessionField.GetValue(controller);

            Press(wordRows[1]);
            Assert.That(edit.wordsInput.text, Is.EqualTo(originalWords[1]));
            Assert.That(originalWords[1], Does.Contain("|"), "The selected fixture includes canonical pinyin.");
            Press(moveUp);
            Assert.That(session.Words[0], Is.EqualTo(originalWords[1]));
            Press(moveDown);
            CollectionAssert.AreEqual(originalWords, session.Words);

            SetInputValue(edit.wordsInput, "unsaved edit");
            Assert.That(edit.wordsInput.text, Is.EqualTo("unsaved edit"), "The field should hold the typed draft.");
            Assert.That(session.SelectedWord, Is.EqualTo("unsaved edit"),
                "Typing should update the selected draft entry before another action.");
            string[] beforeAdd = session.ToList().ToArray();
            Press(addWord);
            Assert.That(session.Words[0], Is.Empty);
            CollectionAssert.AreEqual(beforeAdd, session.Words.Skip(1),
                "Adding a row should first capture the pending edit and preserve the remaining order.");
            Press(deleteWord);
            CollectionAssert.AreEqual(beforeAdd, session.Words,
                "Deleting the new row should restore the edited draft order.");
            Assert.That(editable.Words, Is.EqualTo(originalWords),
                "The stored words should remain unchanged until Save.");

            SetInputValueWithoutNotify(edit.titleInput, "Unsaved title draft");
            Press(edit.cancelButton);
            Assert.That(editable.Words, Is.EqualTo(originalWords));
            Assert.That(editable.Title, Is.EqualTo(originalTitle));
            Assert.That(edit.wordsInput.lineType, Is.EqualTo(TMP_InputField.LineType.SingleLine));
        }

        [Test]
        public void StaticPreviewSynchronizationIsIdempotentAndMatchesControllerInitialization()
        {
            WordListHubSceneBindings bindings = harness.Bindings;
            WordListHubMockupController controller = harness.Controller;
            MockupWordListStore fixtures = MockupWordListStore.CreateWithFixtures();
            AddAuthoredCommunityPreviewRows(bindings.community, fixtures);
            AddAuthoredAchievementPreviewCards(bindings.achievements, fixtures);
            MockupWordList editExample = fixtures.FindById("mock-list-travel-copy");
            MockupWordListEditorView.EnsureAuthoredLayout(bindings.modal.editList, editExample.Words);

            controller.SynchronizeStaticPreview();
            Canvas.ForceUpdateCanvases();
            int firstObjectCount = CountHierarchyObjects(harness.Root.transform);
            int firstLibraryChildCount = bindings.library.listContent.childCount;
            int firstCommunityChildCount = bindings.community.listContent.childCount;
            int firstAchievementChildCount = bindings.achievements.cardContent.childCount;
            Assert.That(bindings.community.authoredRows.Count, Is.EqualTo(fixtures.VisibleDiscoveryItems.Count));
            Assert.That(bindings.achievements.cards.Count, Is.EqualTo(fixtures.Achievements.Count));
            Assert.That(bindings.library.authoredRows.Where(row => row != null && row.root != null)
                .All(row => IsSquareTile(row.root.transform as RectTransform)), Is.True);
            Assert.That(bindings.community.authoredRows.Where(row => row != null && row.root != null)
                .All(row => IsSquareTile(row.root.transform as RectTransform)), Is.True);
            AssertTileSetMatchesGrid(bindings.library.authoredRows.Where(row => row != null && row.root != null)
                .Select(row => row.root.transform as RectTransform),
                ((RectTransform)bindings.library.listContent).rect.width, "Static Library tiles");
            AssertTileSetMatchesGrid(bindings.community.authoredRows.Where(row => row != null && row.root != null)
                .Select(row => row.root.transform as RectTransform),
                ((RectTransform)bindings.community.listContent).rect.width, "Static Community tiles");

            WordListHubSceneBindings.WordEditorLayoutBinding editor = bindings.modal.editList.wordEditorLayout;
            Assert.That(editor.root, Is.Not.Null);
            Assert.That(editor.scrollRect, Is.Not.Null);
            Assert.That(editor.authoredRows.Count, Is.EqualTo(editExample.Words.Count));
            Assert.That(bindings.modal.editList.wordsInput.lineType, Is.EqualTo(TMP_InputField.LineType.SingleLine));
            float configuredPreviewFontScale = new SerializedObject(controller)
                .FindProperty("wordPreviewFontScale").floatValue;
            Assert.That(bindings.libraryRubyWords.root, Is.Not.Null);
            Assert.That(bindings.libraryRubyWords.content, Is.Not.Null);
            Assert.That(bindings.libraryRubyWords.previewFontScale,
                Is.EqualTo(configuredPreviewFontScale).Within(0.001f));
            Assert.That(bindings.communityRubyWords.viewport, Is.Not.Null);
            Assert.That(bindings.communityRubyWords.scrollbar, Is.Not.Null);

            controller.SynchronizeStaticPreview();
            Canvas.ForceUpdateCanvases();
            Assert.That(CountHierarchyObjects(harness.Root.transform), Is.EqualTo(firstObjectCount),
                "A second static synchronization should reuse authored tile, card, ruby and editor views.");
            Assert.That(bindings.library.listContent.childCount, Is.EqualTo(firstLibraryChildCount));
            Assert.That(bindings.community.listContent.childCount, Is.EqualTo(firstCommunityChildCount));
            Assert.That(bindings.achievements.cardContent.childCount, Is.EqualTo(firstAchievementChildCount));

            TMP_Text communityDetailTitle = bindings.community.detailTitle;
            TMP_Text libraryDetailTitle = bindings.library.detailTitle;
            string communityDetailTitleText = communityDetailTitle.text;
            string libraryDetailTitleText = libraryDetailTitle.text;
            float communityDetailTitleSize = communityDetailTitle.fontSize;
            float libraryDetailTitleSize = libraryDetailTitle.fontSize;
            var communityDetailTitleStyle = new TextStyleState(communityDetailTitle, false);
            var libraryDetailTitleStyle = new TextStyleState(libraryDetailTitle, false);
            InvokeAwake(controller);
            Canvas.ForceUpdateCanvases();
            Assert.That(CountHierarchyObjects(harness.Root.transform), Is.EqualTo(firstObjectCount),
                "Controller initialization should bind the baked UI instead of generating duplicates.");
            Assert.That(bindings.library.authoredRows.Where(row => row != null && row.root != null)
                .All(row => IsSquareTile(row.root.transform as RectTransform)), Is.True);
            Assert.That(bindings.community.authoredRows.Where(row => row != null && row.root != null)
                .All(row => IsSquareTile(row.root.transform as RectTransform)), Is.True);
            AssertTileSetMatchesGrid(bindings.library.authoredRows.Where(row => row != null && row.root != null)
                .Select(row => row.root.transform as RectTransform),
                ((RectTransform)bindings.library.listContent).rect.width, "Initialized Library tiles");
            AssertTileSetMatchesGrid(bindings.community.authoredRows.Where(row => row != null && row.root != null)
                .Select(row => row.root.transform as RectTransform),
                ((RectTransform)bindings.community.listContent).rect.width, "Initialized Community tiles");
            Assert.That(bindings.modal.editList.wordEditorLayout.root, Is.SameAs(editor.root));
            communityDetailTitleStyle.AssertUnchanged("controller initialization Community detail title");
            libraryDetailTitleStyle.AssertUnchanged("controller initialization Library detail title");
            if (communityDetailTitle.text == communityDetailTitleText)
                Assert.That(communityDetailTitle.fontSize, Is.EqualTo(communityDetailTitleSize).Within(0.001f));
            if (libraryDetailTitle.text == libraryDetailTitleText)
                Assert.That(libraryDetailTitle.fontSize, Is.EqualTo(libraryDetailTitleSize).Within(0.001f));
        }
        [Test]
        public void CommunityRowsShowFourInformationRowsAndKeepStatusInCorner()
        {
            WordListHubSceneBindings.CommunityPageBinding community = harness.Bindings.community;
            WordListHubDiscoveryRowView template = community.rowTemplate;

            Assert.That(template.summary.gameObject.activeSelf, Is.False,
                "The selectable Community row should not show a description line.");
            AssertTileInformationRows(template.title, template.metadata, template.recordStars,
                template.bestTime, "Community template");
            Assert.That(template.status.gameObject.activeSelf, Is.True);
            AssertVector2(template.status.rectTransform.anchoredPosition, new Vector2(-12f, -8f),
                "Keep the Community status in the top-right corner.");
            AssertNoRectOverlap(template.title.rectTransform, template.status.rectTransform,
                "Community title and status");

            WordListHubDiscoveryRowView dynamicClone = UnityEngine.Object.Instantiate(
                template, community.listContent, false);
            try
            {
                Assert.That(dynamicClone.summary.gameObject.activeSelf, Is.False,
                    "Runtime Community rows should inherit the hidden description line.");
                AssertTileInformationRows(dynamicClone.title, dynamicClone.metadata,
                    dynamicClone.recordStars, dynamicClone.bestTime, "Community clone");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(dynamicClone.gameObject);
            }
        }

        [Test]
        public void StackedWordViewsAlignRubyCellsWrapEnglishAndKeepDetailRegionsFixed()
        {
            SerializedProperty previewScaleProperty = new SerializedObject(harness.Controller)
                .FindProperty("wordPreviewFontScale");
            Assert.That(previewScaleProperty, Is.Not.Null,
                "The preview scale must be editable on the existing controller component.");
            Assert.That(previewScaleProperty.floatValue, Is.EqualTo(1.5f).Within(0.001f));
            const float previewFontScale = 2f;

            TMP_Text libraryText = CreateIsolatedRubyTestText(
                harness.Bindings.library.detailWords, "LibraryRubyGeometryTest");
            libraryText.text = "legacy detail text remains available";
            MockupRubyWordsView libraryView = MockupRubyWordsView.CreateLibrary(
                libraryText, previewFontScale);
            libraryView.SetWords(new[]
            {
                "apple — 苹果 | ping guo",
                "before 你，好! after 苹果 | ni hao ping guo",
                "苹果",
                new string('a', 80)
            }.Concat(Enumerable.Repeat("apple — 苹果 | ping guo", 8)).ToArray());

            Canvas.ForceUpdateCanvases();
            libraryView.RefreshLayoutIfNeeded();
            Canvas.ForceUpdateCanvases();

            Assert.That(libraryText.enabled, Is.False);
            Assert.That(libraryText.text, Is.EqualTo("legacy detail text remains available"));
            Assert.That(libraryText.GetComponent<LayoutElement>().ignoreLayout, Is.True);
            Assert.That(libraryView.LayoutRoot.parent, Is.EqualTo(libraryText.transform.parent));

            TMP_Text[] firstEntryText = libraryView.LayoutRoot.GetChild(0).GetComponentsInChildren<TMP_Text>(true);
            TMP_Text englishGlyph = firstEntryText.Single(text => text.text == "apple — ");
            TMP_Text pinyin = firstEntryText.Single(text => text.text == "ping");
            TMP_Text hanGlyph = firstEntryText.Single(text => text.text == "苹");
            Assert.That(englishGlyph.fontSize, Is.EqualTo(36f).Within(0.001f));
            Assert.That(hanGlyph.fontSize, Is.EqualTo(36f).Within(0.001f));
            Assert.That(pinyin.fontSize, Is.EqualTo(27.04f).Within(0.001f));
            Assert.That(pinyin.preferredHeight,
                Is.LessThanOrEqualTo(pinyin.rectTransform.rect.height + 0.1f));
            Assert.That(hanGlyph.preferredHeight,
                Is.LessThanOrEqualTo(hanGlyph.rectTransform.rect.height + 0.1f));
            Assert.That(((RectTransform)pinyin.transform.parent).rect.height,
                Is.GreaterThanOrEqualTo(pinyin.rectTransform.rect.height + hanGlyph.rectTransform.rect.height),
                "Ruby cells must grow to fit the larger body and pinyin fonts.");
            TMP_Text[] legacyEntryText = libraryView.LayoutRoot.GetChild(2).GetComponentsInChildren<TMP_Text>(true);
            TMP_Text[] legacyUnknown = legacyEntryText.Where(text => text.text == "?").ToArray();
            Assert.That(legacyUnknown, Has.Length.EqualTo(2),
                "Each unannotated Han character should get an explicit unknown reading marker.");
            Assert.That(legacyUnknown.All(text =>
                    text.transform.parent.GetComponent<RectTransform>().name == "HanCell"), Is.True);

            RectTransform pinyinRect = pinyin.rectTransform;
            RectTransform hanRect = hanGlyph.rectTransform;
            Vector3 pinyinCenter = pinyinRect.TransformPoint(pinyinRect.rect.center);
            Vector3 hanCenter = hanRect.TransformPoint(hanRect.rect.center);
            Assert.That(pinyinCenter.y, Is.GreaterThan(hanCenter.y));
            Assert.That(pinyinCenter.x, Is.EqualTo(hanCenter.x).Within(0.1f));
            Assert.That(englishGlyph.rectTransform.TransformPoint(englishGlyph.rectTransform.rect.center).y,
                Is.EqualTo(hanCenter.y).Within(0.1f));
            float measuredCellWidth = Mathf.Max(
                pinyin.GetPreferredValues(pinyin.text, 10000f, 10000f).x,
                hanGlyph.GetPreferredValues(hanGlyph.text, 10000f, 10000f).x);
            Assert.That(pinyin.transform.parent.GetComponent<RectTransform>().rect.width,
                Is.GreaterThanOrEqualTo(measuredCellWidth + 5f));

            RectTransform longEnglishEntry = libraryView.LayoutRoot.GetChild(3) as RectTransform;
            Assert.That(longEnglishEntry.childCount, Is.GreaterThan(30),
                "A long English token should split at text-element boundaries instead of overflowing.");
            RectTransform firstEnglishCell = longEnglishEntry.GetChild(0) as RectTransform;
            RectTransform secondEnglishCell = longEnglishEntry.GetChild(1) as RectTransform;
            Assert.That(secondEnglishCell.anchoredPosition.x - firstEnglishCell.rect.width,
                Is.EqualTo(firstEnglishCell.anchoredPosition.x).Within(0.1f),
                "A wrapped English word should keep normal letter spacing without per-letter cell gaps.");
            float firstRowY = longEnglishEntry.GetChild(0).GetComponent<RectTransform>().anchoredPosition.y;
            float lastRowY = longEnglishEntry.GetChild(longEnglishEntry.childCount - 1)
                .GetComponent<RectTransform>().anchoredPosition.y;
            Assert.That(lastRowY, Is.LessThan(firstRowY));
            VerticalLayoutGroup libraryLayout = libraryView.LayoutRoot.GetComponent<VerticalLayoutGroup>();
            RectTransform[] activeLibraryEntries = Enumerable.Range(0, libraryView.LayoutRoot.childCount)
                .Select(index => libraryView.LayoutRoot.GetChild(index) as RectTransform)
                .Where(entry => entry.gameObject.activeSelf)
                .ToArray();
            Assert.That(activeLibraryEntries, Has.Length.EqualTo(12));
            float expectedLibraryHeight = activeLibraryEntries
                .Sum(entry => entry.rect.height)
                + Mathf.Max(0, activeLibraryEntries.Length - 1) * libraryLayout.spacing
                + libraryLayout.padding.vertical;
            Assert.That(libraryView.LayoutRoot.rect.width, Is.GreaterThan(1f));
            Assert.That(libraryView.LayoutRoot.rect.height, Is.EqualTo(expectedLibraryHeight).Within(0.1f),
                "The Library runtime root needs an explicit height because its parent does not control child height.");
            Assert.That(libraryView.ScrollRect.content.rect.height,
                Is.GreaterThan(libraryView.ScrollRect.viewport.rect.height),
                "The Library detail content must grow enough to scroll through all word entries.");

            TMP_Text communityText = CreateIsolatedRubyTestText(
                harness.Bindings.community.detailSummary, "CommunityRubyGeometryTest");
            RectTransform communityRect = communityText.rectTransform;
            Vector2 communitySize = communityRect.sizeDelta;
            Vector2 communityPosition = communityRect.anchoredPosition;
            Scrollbar libraryScrollbar = libraryView.ScrollRect.verticalScrollbar;
            Assert.That(libraryScrollbar, Is.Not.Null,
                "The authored Library detail ScrollRect must provide the shared scrollbar style.");
            harness.Bindings.pages.Single(binding => binding.page == WordListHubPage.Community)
                .root.SetActive(true);
            MockupRubyWordsView communityView = MockupRubyWordsView.CreateCommunity(
                communityText, libraryScrollbar, previewFontScale);
            communityView.SetWords(Enumerable.Repeat("苹果 | ping guo", 8).ToArray());
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(communityView.LayoutRoot);
            Canvas.ForceUpdateCanvases();
            TMP_Text[] communityEntryText = communityView.LayoutRoot.GetChild(0).GetComponentsInChildren<TMP_Text>(true);
            TMP_Text communityGlyph = communityEntryText.Single(text => text.text == "苹");
            TMP_Text communityPinyin = communityEntryText.Single(text => text.text == "ping");
            Assert.That(communityGlyph.fontSize, Is.EqualTo(36f).Within(0.001f));
            Assert.That(communityPinyin.fontSize, Is.EqualTo(27.04f).Within(0.001f));
            Assert.That(communityGlyph.preferredHeight,
                Is.LessThanOrEqualTo(communityGlyph.rectTransform.rect.height + 0.1f));
            Assert.That(communityPinyin.preferredHeight,
                Is.LessThanOrEqualTo(communityPinyin.rectTransform.rect.height + 0.1f));
            Assert.That(((RectTransform)communityGlyph.transform.parent).rect.height,
                Is.GreaterThanOrEqualTo(communityGlyph.rectTransform.rect.height
                    + communityPinyin.rectTransform.rect.height),
                "Community ruby cells must grow to fit the doubled glyph and pinyin fonts.");

            Assert.That(communityView.ScrollRect, Is.Not.Null);
            Scrollbar communityScrollbar = communityView.ScrollRect.verticalScrollbar;
            Assert.That(communityScrollbar, Is.Not.Null,
                "The Community detail preview must have a visible vertical scrollbar.");
            Assert.That(communityScrollbar, Is.Not.SameAs(libraryScrollbar));
            Assert.That(communityScrollbar.transform.parent, Is.EqualTo(communityRect));
            Assert.That(communityScrollbar.handleRect, Is.Not.Null);
            Assert.That(communityScrollbar.handleRect.IsChildOf(communityScrollbar.transform), Is.True);
            Assert.That(communityScrollbar.targetGraphic, Is.Not.Null);
            Assert.That(communityScrollbar.targetGraphic.transform.IsChildOf(communityScrollbar.transform), Is.True);
            Assert.That(communityScrollbar.gameObject.activeInHierarchy, Is.True,
                string.Format(
                    "Scrollbar visibility: activeSelf={0}, regionActive={1}, contentHeight={2}, "
                    + "viewportHeight={3}, entries={4}, scale={5}.",
                    communityScrollbar.gameObject.activeSelf,
                    communityRect.gameObject.activeInHierarchy,
                    communityView.ScrollRect.content.rect.height,
                    communityView.ScrollRect.viewport.rect.height,
                    communityView.LayoutRoot.childCount,
                    previewFontScale));
            RectTransform scrollbarRect = communityScrollbar.transform as RectTransform;
            RectTransform sourceScrollbarRect = libraryScrollbar.transform as RectTransform;
            Assert.That(scrollbarRect.anchorMin.x, Is.EqualTo(1f).Within(0.001f));
            Assert.That(scrollbarRect.anchorMax.x, Is.EqualTo(1f).Within(0.001f));
            Assert.That(scrollbarRect.sizeDelta.x, Is.EqualTo(sourceScrollbarRect.sizeDelta.x).Within(0.1f));
            Assert.That(communityView.ScrollRect.verticalScrollbarVisibility,
                Is.EqualTo(ScrollRect.ScrollbarVisibility.AutoHide));
            Assert.That(communityView.ScrollRect.viewport.parent, Is.EqualTo(communityRect));
            Assert.That(communityView.ScrollRect.viewport.rect.width,
                Is.EqualTo(communityRect.rect.width).Within(0.1f));
            Assert.That(communityView.ScrollRect.viewport.rect.height,
                Is.EqualTo(communityRect.rect.height).Within(0.1f));
            Assert.That(communityView.ScrollRect.content, Is.EqualTo(communityView.LayoutRoot));
            Assert.That(communityView.LayoutRoot.rect.width,
                Is.EqualTo(communityView.ScrollRect.viewport.rect.width
                    - scrollbarRect.rect.width - 2f).Within(0.1f),
                "The Community content width must reserve room for the scrollbar.");
            Assert.That(communityView.LayoutRoot.rect.height, Is.GreaterThan(communityRect.rect.height));

            float topPosition = communityView.ScrollRect.verticalNormalizedPosition;
            communityScrollbar.value = 1f;
            communityScrollbar.value = 0f;
            Canvas.ForceUpdateCanvases();
            Assert.That(communityView.ScrollRect.verticalNormalizedPosition,
                Is.LessThan(topPosition),
                "Dragging the Community scrollbar must move the stacked preview content.");
            Assert.That(communityRect.sizeDelta, Is.EqualTo(communitySize));
            Assert.That(communityRect.anchoredPosition, Is.EqualTo(communityPosition));

            const float customPreviewScale = 1.5f;
            MockupRubyWordsView customLibraryView = MockupRubyWordsView.CreateLibrary(
                libraryText, customPreviewScale);
            customLibraryView.SetWords(new[] { "苹果 | ping guo" });
            Canvas.ForceUpdateCanvases();
            TMP_Text[] customLibraryText = customLibraryView.LayoutRoot.GetChild(0)
                .GetComponentsInChildren<TMP_Text>(true);
            Assert.That(customLibraryText.Single(text => text.text == "苹").fontSize,
                Is.EqualTo(27f).Within(0.001f));
            Assert.That(customLibraryText.Single(text => text.text == "ping").fontSize,
                Is.EqualTo(20.28f).Within(0.001f));

            MockupRubyWordsView customCommunityView = MockupRubyWordsView.CreateCommunity(
                communityText, libraryScrollbar, customPreviewScale);
            customCommunityView.SetWords(new[] { "苹果 | ping guo" });
            Canvas.ForceUpdateCanvases();
            TMP_Text[] customCommunityText = customCommunityView.LayoutRoot.GetChild(0)
                .GetComponentsInChildren<TMP_Text>(true);
            Assert.That(customCommunityText.Single(text => text.text == "苹").fontSize,
                Is.EqualTo(27f).Within(0.001f));
            Assert.That(customCommunityText.Single(text => text.text == "ping").fontSize,
                Is.EqualTo(20.28f).Within(0.001f));
        }

        [Test]
        public void ProductionChallengeDetailUsesRubyProjectionWhenLegacyTmpIsDisabled()
        {
            WordListHubMockupController controller = harness.Controller;
            InvokeAwake(controller);
            RectTransform challengeDetailPane = harness.Bindings.challenges.detailPane as RectTransform;
            if (challengeDetailPane == null)
            {
                GameObject paneObject = new GameObject("ChallengeRubyProjectionDetailPane", typeof(RectTransform));
                paneObject.transform.SetParent(harness.Bindings.challenges.root.transform, false);
                challengeDetailPane = paneObject.GetComponent<RectTransform>();
                challengeDetailPane.anchorMin = Vector2.zero;
                challengeDetailPane.anchorMax = Vector2.one;
                challengeDetailPane.offsetMin = Vector2.zero;
                challengeDetailPane.offsetMax = Vector2.zero;
                harness.Bindings.challenges.detailPane = challengeDetailPane;
            }
            TMP_Text challengeStyle = UnityEngine.Object.Instantiate(
                harness.Bindings.library.detailWords, challengeDetailPane, false);
            challengeStyle.gameObject.name = "ChallengeLegacyWordsStyle";
            TMP_Text legacyText = CreateIsolatedRubyTestText(
                challengeStyle, "ChallengeDetailRubyProjectionTest");
            UnityEngine.Object.DestroyImmediate(challengeStyle.gameObject);
            legacyText.text = "legacy text must not be the visible renderer";
            harness.Bindings.challenges.detailWords = legacyText;
            harness.Bindings.challenges.rubyWords = new WordListHubSceneBindings.RubyPreviewBinding();
            typeof(WordListHubMockupController).GetField(
                "productionMode", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, true);

            MethodInfo initializeRubyViews = typeof(WordListHubMockupController).GetMethod(
                "InitializeRubyWordsViews", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(initializeRubyViews, Is.Not.Null);
            initializeRubyViews.Invoke(controller, null);
            harness.Bindings.challenges.root.SetActive(true);

            MockupWordList challenge = GetStore(controller).CreateList("Projected challenge",
                MockupLanguage.Mixed, new[] { "apple", "苹果 | ping guo", "ice 冰 | bing" });
            MethodInfo renderWords = typeof(WordListHubMockupController).GetMethod(
                "RefreshChallengeDetailWords", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(renderWords, Is.Not.Null);
            renderWords.Invoke(controller, new object[] { challenge.Words, "No words in this challenge." });
            Canvas.ForceUpdateCanvases();

            Assert.That(legacyText.enabled, Is.False,
                "The saved legacy TMP projection is disabled; challenge words must use the ruby view.");
            FieldInfo challengeViewField = typeof(WordListHubMockupController).GetField(
                "challengeRubyWordsView", BindingFlags.Instance | BindingFlags.NonPublic);
            MockupRubyWordsView challengeView = (MockupRubyWordsView)challengeViewField.GetValue(controller);
            Assert.That(challengeView, Is.Not.Null);
            Assert.That(challengeView.LayoutRoot.IsChildOf(challengeDetailPane), Is.True,
                "Challenge Ruby rows must render under the right-side Challenge details pane.");
            Assert.That(challengeView.LayoutRoot.IsChildOf(harness.Bindings.library.detailPane), Is.False,
                "Challenge words must not mutate or render in the separate Library detail pane.");
            string[] visibleRubyText = challengeView.LayoutRoot.GetComponentsInChildren<TMP_Text>(true)
                .Where(text => text.gameObject.activeInHierarchy)
                .Select(text => text.text).ToArray();
            Assert.That(visibleRubyText, Does.Contain("apple"));
            Assert.That(visibleRubyText, Does.Contain("苹"));
            Assert.That(visibleRubyText, Does.Contain("果"));
            Assert.That(visibleRubyText, Does.Contain("ping"));
            Assert.That(visibleRubyText, Does.Contain("guo"));
            Assert.That(visibleRubyText, Does.Contain("ice "));
            Assert.That(visibleRubyText, Does.Contain("冰"));
            Assert.That(visibleRubyText, Does.Contain("bing"));

            renderWords.Invoke(controller, new object[] { new[] { "banana", "北京 | bei jing" }, string.Empty });
            Canvas.ForceUpdateCanvases();
            visibleRubyText = challengeView.LayoutRoot.GetComponentsInChildren<TMP_Text>(true)
                .Where(text => text.gameObject.activeInHierarchy)
                .Select(text => text.text).ToArray();
            Assert.That(visibleRubyText, Does.Contain("banana"));
            Assert.That(visibleRubyText, Does.Contain("bei"));
            Assert.That(visibleRubyText, Does.Not.Contain("apple"));

            renderWords.Invoke(controller, new object[] { System.Array.Empty<string>(), "No words in this challenge." });
            Canvas.ForceUpdateCanvases();
            visibleRubyText = challengeView.LayoutRoot.GetComponentsInChildren<TMP_Text>(true)
                .Where(text => text.gameObject.activeInHierarchy)
                .Select(text => text.text).ToArray();
            Assert.That(visibleRubyText, Does.Contain("No words in this challenge."),
                "An empty selected challenge must show its fallback through the visible ruby view.");

            renderWords.Invoke(controller, new object[] { null, string.Empty });
            Canvas.ForceUpdateCanvases();
            Assert.That(challengeView.LayoutRoot.GetComponentsInChildren<TMP_Text>(true)
                .Any(text => text.gameObject.activeInHierarchy), Is.False,
                "Clearing the selected challenge must not leave the prior challenge's words visible.");
        }

        [Test]
        public void NavigationDetailsAndFilters_PreserveFixedLayoutAndDispatchFavoriteOnce()
        {
            WordListHubMockupController controller = harness.Controller;
            WordListHubSceneBindings bindings = harness.Bindings;
            UiSceneSnapshot snapshot = UiSceneSnapshot.Capture(
                harness.Root, NavigationVisualComponents(bindings));
            NavigationStyleSnapshot activeNavigationStyle =
                NavigationStyleSnapshot.Capture(bindings.navLibrary);
            NavigationStyleSnapshot inactiveNavigationStyle =
                NavigationStyleSnapshot.Capture(bindings.navChallenges);
            InvokeAwake(controller);
            snapshot.AssertUnchanged("initial scene binding and refresh");
            Assert.That(bindings.library.detailStatus.text, Is.Empty,
                "The action feedback prompt should start empty.");

            AssertPageActive(bindings, WordListHubPage.Library);
            AssertNavigationVisuals(bindings, WordListHubPage.Library,
                activeNavigationStyle, inactiveNavigationStyle);
            Press(bindings.navCommunity.button);
            AssertPageActive(bindings, WordListHubPage.Community);
            AssertNavigationVisuals(bindings, WordListHubPage.Community,
                activeNavigationStyle, inactiveNavigationStyle);
            Assert.That(harness.TestEventSystem, Is.Not.Null);
            harness.TestEventSystem.SetSelectedGameObject(bindings.navAchievements.button.gameObject);
            AssertNavigationVisuals(bindings, WordListHubPage.Community,
                activeNavigationStyle, inactiveNavigationStyle);
            Assert.That(bindings.community.rowTemplate.gameObject.activeInHierarchy, Is.False,
                "The Community source template must stay hidden when its page is active.");

            RowAction discoveryRow = FindFirstCommunityRow(bindings.community);
            Assert.That(discoveryRow, Is.Not.Null, "The Community page should show at least one row.");
            Press(discoveryRow.button);
            Assert.That(bindings.community.detailTitle.text, Is.EqualTo(discoveryRow.title));
            AssertExactlyOneSelectedCommunityRow(bindings.community, discoveryRow.title);

            string likeLabelBefore = bindings.community.like.label.text;
            Press(bindings.community.like.button);
            Assert.That(bindings.community.like.label.text, Is.Not.EqualTo(likeLabelBefore),
                "One click should toggle the selected community item's like state once.");
            AssertExactlyOneSelectedCommunityRow(bindings.community, discoveryRow.title);

            SetInputValue(bindings.community.searchInput, "no-matching-word-list");
            Assert.That(bindings.community.emptyNotice.gameObject.activeSelf, Is.True);
            Assert.That(CountRuntimeChildren(bindings.community.listContent), Is.EqualTo(0));
            AssertNoSelectedCommunityRows(bindings.community);
            SetInputValue(bindings.community.searchInput, "no-matching-word-list");
            Assert.That(CountRuntimeChildren(bindings.community.listContent), Is.EqualTo(0),
                "Repeated filtering must not leave stale runtime rows behind.");
            SetInputValue(bindings.community.searchInput, string.Empty);
            int restoredCommunityRows = CountRuntimeChildren(bindings.community.listContent);
            SetInputValue(bindings.community.searchInput, string.Empty);
            Assert.That(CountRuntimeChildren(bindings.community.listContent), Is.EqualTo(restoredCommunityRows));
            AssertExactlyOneSelectedCommunityRow(bindings.community,
                bindings.community.detailTitle.text);
            AssertRuntimeCommunityClonesMatchTemplate(bindings.community);

            Press(bindings.navAchievements.button);
            AssertPageActive(bindings, WordListHubPage.Achievements);
            AssertNavigationVisuals(bindings, WordListHubPage.Achievements,
                activeNavigationStyle, inactiveNavigationStyle);
            Assert.That(bindings.achievements.cardTemplate.gameObject.activeInHierarchy, Is.False,
                "The Achievement source template must stay hidden when its page is active.");
            MockupWordListStore fixtureStore = MockupWordListStore.CreateWithFixtures();
            int unlockedCount = fixtureStore.Achievements.Count(
                item => item.Status == MockupAchievementStatus.Unlocked);
            SetDropdownValue(bindings.achievements.filter, 1);
            Assert.That(CountVisibleAchievementCards(bindings.achievements), Is.EqualTo(unlockedCount));
            int filteredCardCount = CountRuntimeChildren(bindings.achievements.cardContent);
            SetDropdownValue(bindings.achievements.filter, 1);
            Assert.That(CountRuntimeChildren(bindings.achievements.cardContent), Is.EqualTo(filteredCardCount),
                "Refreshing the same achievement filter must not duplicate cards.");
            AssertRuntimeAchievementClonesMatchTemplate(bindings.achievements);

            Press(bindings.navChallenges.button);
            AssertPageActive(bindings, WordListHubPage.Challenges);
            AssertNavigationVisuals(bindings, WordListHubPage.Challenges,
                activeNavigationStyle, inactiveNavigationStyle);
            Press(bindings.navCommunity.button);
            AssertPageActive(bindings, WordListHubPage.Community);
            AssertNavigationVisuals(bindings, WordListHubPage.Community,
                activeNavigationStyle, inactiveNavigationStyle);
            AssertExactlyOneSelectedCommunityRow(bindings.community,
                bindings.community.detailTitle.text);
            Press(bindings.navLibrary.button);
            AssertPageActive(bindings, WordListHubPage.Library);
            AssertNavigationVisuals(bindings, WordListHubPage.Library,
                activeNavigationStyle, inactiveNavigationStyle);
            Assert.That(bindings.library.rowTemplate.gameObject.activeInHierarchy, Is.False,
                "The Library source template must stay hidden when its page is active.");

            RowAction libraryRow = FindFirstLibraryRow(bindings.library);
            Assert.That(libraryRow, Is.Not.Null, "The Library page should show at least one row.");
            Press(libraryRow.button);
            Assert.That(bindings.library.detailTitle.text, Is.EqualTo(libraryRow.title),
                "Selecting a row should refresh the authored detail title.");
            Assert.That(bindings.library.detailStatus.text, Is.Empty,
                "Selecting a row should not create routine status text.");
            AssertExactlyOneSelectedLibraryRow(bindings.library, libraryRow.title);

            WordListHubSceneBindings.LibraryRowBinding favoriteRow =
                bindings.library.authoredRows.First(row => row != null);
            string favoriteLabelBefore = favoriteRow.favoriteMark.text;
            Press(favoriteRow.favoriteButton);
            Assert.That(favoriteRow.favoriteMark.text, Is.Not.EqualTo(favoriteLabelBefore),
                "A single row-star click must toggle Favorite once; duplicate listeners would toggle it back.");
            Assert.That(bindings.library.detailStatus.text, Is.Empty,
                "Favorite toggles should not create a redundant action prompt.");
            AssertExactlyOneSelectedLibraryRow(bindings.library, libraryRow.title);

            SetDropdownValue(bindings.library.languageFilter, 1);
            Assert.That(bindings.library.detailStatus.text, Is.Empty,
                "Passive filters should not create action feedback.");
            MockupLibraryQuery englishQuery = new MockupLibraryQuery
            {
                LanguageFilter = MockupLanguageFilter.English
            };
            AssertLibraryRowsMatchQuery(fixtureStore, englishQuery, bindings.library);
            int filteredLibraryRows = CountRuntimeChildren(bindings.library.listContent);
            SetDropdownValue(bindings.library.languageFilter, 1);
            Assert.That(CountRuntimeChildren(bindings.library.listContent), Is.EqualTo(filteredLibraryRows),
                "Repeated filtering must replace owned clones without accumulating duplicates.");
            AssertExactlyOneSelectedLibraryRow(bindings.library, bindings.library.detailTitle.text);
            AssertRuntimeLibraryClonesMatchTemplate(bindings.library);

            Press(bindings.navCommunity.button);
            Press(bindings.navLibrary.button);
            AssertExactlyOneSelectedLibraryRow(bindings.library, bindings.library.detailTitle.text);
            Assert.That(bindings.library.detailStatus.text, Is.Empty,
                "Passive navigation and search should leave the action prompt empty.");

            snapshot.AssertUnchanged("navigation, row selection, filtering, and favorite click");
            snapshot.AssertOriginalObjectsStillExist("navigation, row selection, filtering, and favorite click");
        }

        [Test]
        public void LibraryViewToggle_CombinesAllAndFavoritesAndPreservesQueryState()
        {
            WordListHubMockupController controller = harness.Controller;
            WordListHubSceneBindings.LibraryPageBinding library = harness.Bindings.library;
            InvokeAwake(controller);

            Assert.That(library.favorites.button.gameObject.activeSelf, Is.False,
                "The separate Favorites button should be hidden after combining the views.");
            Assert.That(library.allLists.label.text, Is.EqualTo("Favourite"));
            library.favorites.button.onClick.Invoke();
            FieldInfo queryField = typeof(WordListHubMockupController).GetField(
                "query", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(queryField, Is.Not.Null);
            MockupLibraryQuery query = (MockupLibraryQuery)queryField.GetValue(controller);
            Assert.That(query.View, Is.EqualTo(MockupLibraryView.All),
                "The hidden legacy button must no longer have a click listener.");

            SetInputValue(library.searchInput, "word");
            Press(library.filtersMenu.toggleButton);
            Press(library.filtersMenu.sections[0].options[0].button);
            Press(library.filtersMenu.sections[1].options[2].button);
            Press(library.filtersMenu.sections[2].options[1].button);
            Press(library.filtersMenu.sections[3].options[1].button);
            Press(library.filtersMenu.toggleButton);

            Press(library.allLists.button);
            Assert.That(query.View, Is.EqualTo(MockupLibraryView.Favorites));
            Assert.That(library.allLists.label.text, Is.EqualTo("Show All"));
            AssertLibraryTogglePreservesQuery(query, library, "favorites view");
            library.favorites.button.onClick.Invoke();
            Assert.That(query.View, Is.EqualTo(MockupLibraryView.Favorites),
                "The hidden legacy button must not switch the view.");

            Press(library.allLists.button);
            Assert.That(query.View, Is.EqualTo(MockupLibraryView.All));
            Assert.That(library.allLists.label.text, Is.EqualTo("Favourite"));
            AssertLibraryTogglePreservesQuery(query, library, "all view");
        }

        private static void AssertLibraryTogglePreservesQuery(
            MockupLibraryQuery query,
            WordListHubSceneBindings.LibraryPageBinding library,
            string phase)
        {
            Assert.That(library.searchInput.text, Is.EqualTo("word"), phase + " search text");
            Assert.That(query.SearchText, Is.EqualTo("word"), phase + " query search text");
            Assert.That(query.LanguageFilter, Is.EqualTo(MockupLanguageFilter.Chinese), phase + " language filter");
            Assert.That(query.PlayedFilter, Is.EqualTo(MockupPlayedFilter.Played), phase + " played filter");
            Assert.That(query.SortMode, Is.EqualTo(MockupSortMode.Name), phase + " sort mode");
            Assert.That(query.SortDirection, Is.EqualTo(MockupSortDirection.Ascending), phase + " sort direction");
        }

        [Test]
        public void LibraryFiltersMenu_CombinesAllFiltersAndKeepsSelectionVisibleUntilClosed()
        {
            WordListHubMockupController controller = harness.Controller;
            WordListHubSceneBindings.LibraryPageBinding library = harness.Bindings.library;
            WordListHubSceneBindings.FilterMenuBinding menu = library.filtersMenu;
            Assert.That(menu, Is.Not.Null);
            Assert.That(menu.panel, Is.Not.Null);
            Assert.That(menu.scrollRect, Is.Not.Null);
            Assert.That(menu.sections, Has.Count.EqualTo(4));
            CollectionAssert.AreEqual(
                new[]
                {
                    WordListHubSceneBindings.LibraryFilterKind.Order,
                    WordListHubSceneBindings.LibraryFilterKind.Language,
                    WordListHubSceneBindings.LibraryFilterKind.Played,
                    WordListHubSceneBindings.LibraryFilterKind.Sort
                },
                menu.sections.Select(section => section.kind));
            CollectionAssert.AreEqual(new[] { 2, 4, 3, 6 },
                menu.sections.Select(section => section.options.Count));
            Assert.That(menu.scrollRect.horizontal, Is.False);
            Assert.That(menu.scrollRect.vertical, Is.True);
            Assert.That(menu.scrollRect.viewport.GetComponent<Mask>(), Is.Not.Null);
            Assert.That(menu.panel.GetComponent<RectTransform>().rect.height, Is.EqualTo(400f).Within(0.01f));

            InvokeAwake(controller);
            Assert.That(menu.panel.activeSelf, Is.False, "The unified menu must start collapsed.");
            Assert.That(library.languageFilter.gameObject.activeSelf, Is.False);
            Assert.That(library.playedFilter.gameObject.activeSelf, Is.False);
            Assert.That(library.sortFilter.gameObject.activeSelf, Is.False);
            Assert.That(library.directionFilter.gameObject.activeSelf, Is.False);

            Press(menu.toggleButton);
            Assert.That(menu.panel.activeSelf, Is.True);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(menu.content as RectTransform);
            Assert.That(((RectTransform)menu.content).rect.height,
                Is.GreaterThan(menu.scrollRect.viewport.rect.height),
                "All four sections should extend past the viewport and remain reachable by scrolling.");

            WordListHubSceneBindings.FilterOptionBinding ascending = menu.sections[0].options[0];
            WordListHubSceneBindings.FilterOptionBinding chinese = menu.sections[1].options[2];
            WordListHubSceneBindings.FilterOptionBinding played = menu.sections[2].options[1];
            WordListHubSceneBindings.FilterOptionBinding byName = menu.sections[3].options[1];
            Press(ascending.button);
            Press(chinese.button);
            Press(played.button);
            Press(byName.button);

            FieldInfo queryField = typeof(WordListHubMockupController).GetField(
                "query", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(queryField, Is.Not.Null);
            MockupLibraryQuery query = (MockupLibraryQuery)queryField.GetValue(controller);
            Assert.That(query.SortDirection, Is.EqualTo(MockupSortDirection.Ascending));
            Assert.That(query.LanguageFilter, Is.EqualTo(MockupLanguageFilter.Chinese));
            Assert.That(query.PlayedFilter, Is.EqualTo(MockupPlayedFilter.Played));
            Assert.That(query.SortMode, Is.EqualTo(MockupSortMode.Name));
            Assert.That(ascending.selectedVisual.activeSelf, Is.True);
            Assert.That(chinese.selectedVisual.activeSelf, Is.True);
            Assert.That(played.selectedVisual.activeSelf, Is.True);
            Assert.That(byName.selectedVisual.activeSelf, Is.True);
            Assert.That(chinese.label.text, Does.Contain("✓"));
            AssertLibraryRowsMatchQuery(GetStore(controller), query, library);
            Assert.That(menu.panel.activeSelf, Is.True,
                "Selecting a filter should leave the menu open so other sections can be adjusted.");

            Press(menu.toggleButton);
            Assert.That(menu.panel.activeSelf, Is.False);
            Press(menu.toggleButton);
            Press(harness.Bindings.navCommunity.button);
            Assert.That(menu.panel.activeSelf, Is.False,
                "Changing pages should close the unified menu.");
            AssertPageActive(harness.Bindings, WordListHubPage.Community);
        }

        [Test]
        public void CommunityAndAchievementMenus_KeepExistingActionsAndSelectedStateInSync()
        {
            WordListHubMockupController controller = harness.Controller;
            WordListHubSceneBindings bindings = harness.Bindings;
            InvokeAwake(controller);

            WordListHubSceneBindings.FilterMenuBinding communityMenu = bindings.community.connectionMenu;
            Assert.That(communityMenu.sections.Count, Is.EqualTo(4));
            Assert.That(communityMenu.sections[0].options.Count, Is.EqualTo(2));
            Assert.That(communityMenu.sections[1].options.Count, Is.EqualTo(4));
            Assert.That(communityMenu.sections[2].options.Count, Is.EqualTo(3));
            Assert.That(communityMenu.sections[3].options.Count, Is.EqualTo(4));
            Assert.That(bindings.community.connectionState.gameObject.activeSelf, Is.False,
                "The legacy Community dropdown should remain hidden.");
            Assert.That(bindings.community.connectionState.onValueChanged.GetPersistentEventCount(), Is.EqualTo(0),
                "The mock network state control should no longer be part of the Community UI.");
            Press(bindings.navCommunity.button);
            Assert.That(communityMenu.sections[0].options[0].selectedVisual.activeSelf, Is.True);
            Press(communityMenu.toggleButton);
            Assert.That(communityMenu.panel.activeSelf, Is.True);

            MockupWordListStore store = GetStore(controller);
            Assert.That(store.IsOnline, Is.True);
            Press(communityMenu.sections[0].options[1].button);
            Assert.That(communityMenu.sections[0].options[1].selectedVisual.activeSelf, Is.True);
            Assert.That(communityMenu.panel.activeSelf, Is.True,
                "Choosing a filter should leave the menu open so other sections can be adjusted.");
            Assert.That(store.IsOnline, Is.True,
                "Community filters must not change the mock network state.");

            Press(communityMenu.sections[1].options[2].button);
            Assert.That(communityMenu.sections[1].options[2].selectedVisual.activeSelf, Is.True);
            Assert.That(store.QueryDiscoveryWithFilters(GetCommunityQuery(controller))
                .Select(item => item.RemoteItemId), Is.EquivalentTo(new[] { "mock-ugc-night-market" }));

            Press(communityMenu.sections[1].options[0].button);
            Press(communityMenu.sections[2].options[1].button);
            Assert.That(communityMenu.sections[2].options[1].selectedVisual.activeSelf, Is.True);
            Assert.That(store.QueryDiscoveryWithFilters(GetCommunityQuery(controller)).Count, Is.EqualTo(2));

            Press(communityMenu.sections[2].options[0].button);
            Press(communityMenu.sections[0].options[1].button);
            Press(communityMenu.sections[3].options[3].button);
            Assert.That(communityMenu.sections[3].options[3].selectedVisual.activeSelf, Is.True);
            Assert.That(store.QueryDiscoveryWithFilters(GetCommunityQuery(controller))[0].RemoteItemId,
                Is.EqualTo("mock-ugc-night-market"),
                "Word count sorting should use the selected direction.");

            Press(communityMenu.sections[3].options[0].button);
            Assert.That(store.QueryDiscoveryWithFilters(GetCommunityQuery(controller))[0].RemoteItemId,
                Is.EqualTo("mock-ugc-airport-mix"),
                "Default descending order should reverse the original discovery order.");
            Assert.That(communityMenu.toggleLabel.text, Does.Contain("Filters"));
            bindings.community.connectionState.value = 1;
            Assert.That(store.IsOnline, Is.True,
                "Changing the hidden legacy dropdown must not toggle online/offline behavior.");
            Press(communityMenu.toggleButton);
            Assert.That(communityMenu.panel.activeSelf, Is.False);

            WordListHubSceneBindings.FilterMenuBinding achievementMenu = bindings.achievements.filterMenu;
            AssertCompactMenu(achievementMenu, 3, "Show");
            Assert.That(bindings.achievements.filter.gameObject.activeSelf, Is.False,
                "The legacy Achievement dropdown should stay bound but hidden.");
            Press(bindings.navAchievements.button);
            Press(achievementMenu.toggleButton);

            MockupWordListStore fixtureStore = MockupWordListStore.CreateWithFixtures();
            int unlockedCount = fixtureStore.Achievements.Count(
                item => item.Status == MockupAchievementStatus.Unlocked);
            Press(achievementMenu.sections[0].options[1].button);
            Assert.That(bindings.achievements.filter.value, Is.EqualTo(1));
            Assert.That(CountVisibleAchievementCards(bindings.achievements), Is.EqualTo(unlockedCount));
            Assert.That(achievementMenu.sections[0].options[1].selectedVisual.activeSelf, Is.True);
            Assert.That(achievementMenu.panel.activeSelf, Is.True,
                "Choosing an achievement filter should leave the compact menu open.");

            int inProgressCount = fixtureStore.Achievements.Count(
                item => item.Status == MockupAchievementStatus.InProgress);
            Press(achievementMenu.sections[0].options[2].button);
            Assert.That(bindings.achievements.filter.value, Is.EqualTo(2));
            Assert.That(CountVisibleAchievementCards(bindings.achievements), Is.EqualTo(inProgressCount));
            Assert.That(achievementMenu.sections[0].options[2].selectedVisual.activeSelf, Is.True);
            Assert.That(achievementMenu.toggleLabel.text, Does.Contain("In progress"));

            Press(achievementMenu.toggleButton);
            Assert.That(achievementMenu.panel.activeSelf, Is.False);
            Press(achievementMenu.toggleButton);
            Press(bindings.navLibrary.button);
            Assert.That(achievementMenu.panel.activeSelf, Is.False,
                "Navigating away from Achievements should close its menu.");
            Press(bindings.library.filtersMenu.toggleButton);
            Assert.That(bindings.library.filtersMenu.panel.activeSelf, Is.True);
            Press(bindings.library.createList.button);
            Assert.That(bindings.library.filtersMenu.panel.activeSelf, Is.False,
                "Opening a modal should close the currently open menu.");
            Assert.That(bindings.modal.newList.root.activeInHierarchy, Is.True);
        }

        [Test]
        public void AchievementCarousel_ComputesOverflowForMoreThanThreeCards()
        {
            WordListHubMockupController controller = harness.Controller;
            WordListHubSceneBindings.AchievementsPageBinding achievements = harness.Bindings.achievements;
            InvokeAwake(controller);
            Press(harness.Bindings.navAchievements.button);

            Transform scrollRoot = achievements.root.transform.Find("AchievementsScroll");
            Assert.That(scrollRoot, Is.Not.Null);
            ScrollRect scroll = scrollRoot.GetComponent<ScrollRect>();
            Assert.That(scroll, Is.Not.Null);
            Assert.That(scroll.horizontal, Is.True);
            Assert.That(scroll.vertical, Is.False);
            Assert.That(scroll.movementType, Is.EqualTo(ScrollRect.MovementType.Clamped));
            Assert.That(scroll.scrollSensitivity, Is.EqualTo(40f));
            Assert.That(scroll.content, Is.EqualTo(achievements.cardContent));
            Assert.That(scroll.viewport, Is.Not.Null);

            while (CountVisibleAchievementCards(achievements) < 4)
            {
                GameObject fixtureCard = UnityEngine.Object.Instantiate(
                    achievements.cardTemplate.gameObject, achievements.cardContent, false);
                fixtureCard.name = "LocalOverflowFixture";
                fixtureCard.SetActive(true);
            }

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(achievements.cardContent as RectTransform);
            RectTransform contentRect = achievements.cardContent as RectTransform;
            Assert.That(contentRect.rect.width, Is.GreaterThan(scroll.viewport.rect.width),
                "Four 460-pixel cards with their authored gaps should create a horizontal scroll range.");

            scroll.horizontalNormalizedPosition = 0f;
            float leftPosition = contentRect.anchoredPosition.x;
            scroll.horizontalNormalizedPosition = 1f;
            Canvas.ForceUpdateCanvases();
            float rightPosition = contentRect.anchoredPosition.x;
            Assert.That(Mathf.Abs(rightPosition - leftPosition), Is.GreaterThan(100f),
                "The horizontal ScrollRect should be able to move across the overflow range.");

            scroll.horizontalNormalizedPosition = 0.5f;
            Canvas.ForceUpdateCanvases();
            float beforeWheel = contentRect.anchoredPosition.x;
            scroll.OnScroll(new PointerEventData(harness.TestEventSystem)
            {
                scrollDelta = new Vector2(0f, 1f)
            });
            float afterWheelUp = contentRect.anchoredPosition.x;
            Assert.That(afterWheelUp, Is.LessThan(beforeWheel),
                "An upward wheel event should move the horizontal-only carousel via ScrollRect.OnScroll.");
            Assert.That(Mathf.Abs(afterWheelUp - beforeWheel), Is.EqualTo(40f).Within(1f),
                "One wheel unit should use the authored 40-pixel sensitivity.");

            scroll.OnScroll(new PointerEventData(harness.TestEventSystem)
            {
                scrollDelta = new Vector2(0f, -1f)
            });
            float afterWheelDown = contentRect.anchoredPosition.x;
            Assert.That(afterWheelDown, Is.GreaterThan(afterWheelUp),
                "A downward wheel event should move the carousel in the opposite direction.");
            Assert.That(Mathf.Abs(afterWheelDown - afterWheelUp), Is.EqualTo(40f).Within(1f));
        }

        [Test]
        public void AchievementCards_AreVerticallyContainedByViewportWithZeroHeightContent()
        {
            WordListHubMockupController controller = harness.Controller;
            WordListHubSceneBindings.AchievementsPageBinding achievements = harness.Bindings.achievements;
            InvokeAwake(controller);
            Press(harness.Bindings.navAchievements.button);

            ScrollRect scroll = achievements.root.transform.Find("AchievementsScroll").GetComponent<ScrollRect>();
            RectTransform content = achievements.cardContent as RectTransform;
            HorizontalLayoutGroup layout = content.GetComponent<HorizontalLayoutGroup>();
            Assert.That(scroll.viewport, Is.Not.Null);
            Assert.That(content.rect.height, Is.EqualTo(0f).Within(0.01f),
                "This regression must cover the authored zero-height carousel content.");
            Assert.That(layout.childAlignment, Is.EqualTo(TextAnchor.UpperLeft),
                "Cards should align their top edges with the viewport instead of centering on its top edge.");

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            Assert.That(CountVisibleAchievementCards(achievements), Is.GreaterThan(0));
            foreach (WordListHubAchievementCardView card in achievements.cardContent
                .GetComponentsInChildren<WordListHubAchievementCardView>(true))
            {
                if (card.GetComponent<WordListHubRuntimeInstance>() == null)
                    continue;

                AssertVerticallyContained(card.transform as RectTransform, scroll.viewport, card.name);
                AssertVerticallyContained(card.icon.rectTransform, scroll.viewport, card.name + ".icon");
                AssertVerticallyContained(card.state.rectTransform, scroll.viewport, card.name + ".state");
                AssertVerticallyContained(card.title.rectTransform, scroll.viewport, card.name + ".title");
                AssertVerticallyContained(card.description.rectTransform, scroll.viewport, card.name + ".description");
                AssertVerticallyContained(card.progress.rectTransform, scroll.viewport, card.name + ".progress");
            }
        }

        [Test]
        public void ActionFeedback_IsOnlyShownForActionsAndExpiresWithoutBlockingNavigation()
        {
            WordListHubMockupController controller = harness.Controller;
            WordListHubSceneBindings bindings = harness.Bindings;
            InvokeAwake(controller);

            MethodInfo showFeedback = typeof(WordListHubMockupController).GetMethod(
                "ShowActionFeedback", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(showFeedback, Is.Not.Null);

            showFeedback.Invoke(controller, new object[] { "Published this list.", false });
            WordListHubFeedbackView feedbackView = bindings.hostRoot.GetComponentInChildren<WordListHubFeedbackView>(true);
            Assert.That(feedbackView, Is.Not.Null);
            Assert.That(feedbackView.IsVisible, Is.True);
            Assert.That(feedbackView.MessageText.text, Is.EqualTo("Published this list."));
            Assert.That(bindings.footer.text, Is.EqualTo("LOCAL MOCKUP  ·  SAMPLE DATA ONLY"),
                "The footer should keep its independent sample-data notice.");
            Assert.That(feedbackView.RemainingSeconds, Is.EqualTo(3f).Within(0.1f),
                "Action feedback should use the configured three-second lifetime.");

            Press(bindings.navCommunity.button);
            Press(bindings.navLibrary.button);
            SetDropdownValue(bindings.library.languageFilter, 1);
            SetInputValue(bindings.library.searchInput, "Blank Canvas");
            Assert.That(feedbackView.IsVisible, Is.True);
            Assert.That(feedbackView.MessageText.text, Is.EqualTo("Published this list."),
                "Passive navigation, filters, and search should not clear a live action toast.");

            feedbackView.TickUnscaled(3.1f);
            Assert.That(feedbackView.IsVisible, Is.False,
                "The action toast should clear after its unscaled deadline.");

            showFeedback.Invoke(controller, new object[] { "That action failed.", true });
            Assert.That(feedbackView.IsVisible, Is.True);
            Assert.That(feedbackView.MessageText.text, Is.EqualTo("That action failed."));
            Assert.That(feedbackView.RemainingSeconds, Is.EqualTo(3f).Within(0.1f),
                "Success and error toasts share the configured duration.");
        }

        [Test]
        public void DeletingLastVisibleList_LeavesSuccessFeedbackWhenNoListIsSelected()
        {
            WordListHubMockupController controller = harness.Controller;
            WordListHubSceneBindings bindings = harness.Bindings;
            InvokeAwake(controller);

            SetInputValue(bindings.library.searchInput, "Blank Canvas");
            Assert.That(GetStore(controller).QueryLibrary(new MockupLibraryQuery
            {
                SearchText = "Blank Canvas"
            }), Has.Count.EqualTo(1));
            Assert.That(bindings.library.detailTitle.text, Is.EqualTo("Blank Canvas"));

            Press(bindings.library.delete.button);
            Press(bindings.modal.deleteList.confirmButton);

            Assert.That(GetStore(controller).FindById("mock-list-empty"), Is.Null);
            Assert.That(bindings.library.detailTitle.text, Is.EqualTo("Choose a list to see its details."));
            Assert.That(bindings.library.detailStatus.text,
                Is.EqualTo("Local list removed. No remote item was changed."),
                "Deleting the final visible list should still report success after selection becomes empty.");
            FieldInfo selectedListId = typeof(WordListHubMockupController).GetField(
                "selectedListId", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(selectedListId.GetValue(controller), Is.Null);
        }

        private static void AssertCompactMenu(
            WordListHubSceneBindings.FilterMenuBinding menu,
            int expectedOptionCount,
            string expectedHeading)
        {
            Assert.That(menu, Is.Not.Null);
            Assert.That(menu.panel, Is.Not.Null);
            Assert.That(menu.scrollRect, Is.Not.Null);
            Assert.That(menu.sections, Has.Count.EqualTo(1));
            Assert.That(menu.sections[0].heading.text, Is.EqualTo(expectedHeading));
            Assert.That(menu.sections[0].options, Has.Count.EqualTo(expectedOptionCount));
            Assert.That(menu.scrollRect.horizontal, Is.False);
            Assert.That(menu.scrollRect.vertical, Is.True);
            Assert.That(menu.scrollRect.viewport.GetComponent<Mask>(), Is.Not.Null);
            Assert.That(menu.panel.GetComponent<RectTransform>().rect.height, Is.LessThan(220f),
                "Compact menus should size to their two or three choices.");
        }

        [Test]
        public void AuthoredCreateAndDeleteDialogs_UseBoundControlsAndKeepSceneObjects()
        {
            WordListHubMockupController controller = harness.Controller;
            WordListHubSceneBindings bindings = harness.Bindings;
            UiSceneSnapshot snapshot = UiSceneSnapshot.Capture(harness.Root);
            InvokeAwake(controller);

            string uniqueTitle = "Layout preservation create once";
            Press(bindings.library.createList.button);
            Assert.That(bindings.modal.newList.root.activeInHierarchy, Is.True,
                "Opening Create must display the serialized form root.");
            Assert.That(bindings.modal.editList.root.activeInHierarchy, Is.False);
            SetInputValueWithoutNotify(bindings.modal.newList.titleInput, uniqueTitle);
            SetInputValueWithoutNotify(bindings.modal.newList.wordsInput, "single-word");
            snapshot.AssertUnchanged("Create form opened");
            Press(bindings.modal.newList.saveButton);

            Assert.That(bindings.modal.root.activeInHierarchy, Is.False,
                "Saving must close the authored modal root.");
            Assert.That(CountLibraryRowsByTitle(bindings.library, uniqueTitle), Is.EqualTo(1),
                "One Save click must create exactly one list row.");
            Assert.That(bindings.library.detailTitle.text, Is.EqualTo(uniqueTitle));
            Assert.That(bindings.library.delete.label.text, Is.EqualTo("Delete"));

            Press(bindings.library.edit.button);
            Assert.That(bindings.modal.editList.root.activeInHierarchy, Is.True,
                "Opening Edit must display the serialized edit form.");
            Assert.That(bindings.modal.newList.root.activeInHierarchy, Is.False);
            snapshot.AssertUnchanged("Edit form opened");
            Press(bindings.modal.editList.cancelButton);
            Assert.That(bindings.modal.root.activeInHierarchy, Is.False,
                "Cancel must close the authored modal root.");

            Press(bindings.library.delete.button);
            Assert.That(bindings.modal.deleteList.root.activeInHierarchy, Is.True,
                "Delete must display the serialized confirmation root.");
            snapshot.AssertUnchanged("Delete confirmation opened");
            Press(bindings.modal.deleteList.confirmButton);
            Assert.That(CountLibraryRowsByTitle(bindings.library, uniqueTitle), Is.EqualTo(0),
                "Confirm must remove the newly created local list.");
            snapshot.AssertUnchanged("Create, Edit cancel, and Delete confirmation");
            snapshot.AssertOriginalObjectsStillExist("Create, Edit cancel, and Delete confirmation");
        }

        [Test]
        public void CommunityDownload_SelectsOneLibraryCopyWithoutReplacingAuthoredLayout()
        {
            WordListHubMockupController controller = harness.Controller;
            WordListHubSceneBindings bindings = harness.Bindings;
            UiSceneSnapshot snapshot = UiSceneSnapshot.Capture(harness.Root);
            InvokeAwake(controller);
            Press(bindings.navCommunity.button);

            MockupWordListStore fixtureStore = MockupWordListStore.CreateWithFixtures();
            MockupDiscoveryItem candidate = fixtureStore.VisibleDiscoveryItems
                .FirstOrDefault(item => !item.IsDownloaded);
            Assert.That(candidate, Is.Not.Null, "The fixture should include a downloadable Community item.");
            RowAction row = FindCommunityRow(bindings.community, candidate.RemoteItemId);
            Assert.That(row, Is.Not.Null, "The selected Community item should have a visible row.");
            Press(row.button);
            Assert.That(bindings.community.detailTitle.text, Is.EqualTo(candidate.Title));

            Press(bindings.community.download.button);

            AssertPageActive(bindings, WordListHubPage.Library);
            Assert.That(bindings.library.detailTitle.text, Is.EqualTo(candidate.Title),
                "After download the new local copy should be selected in the Library.");
            Assert.That(CountLibraryRowsByTitle(bindings.library, candidate.Title), Is.EqualTo(1));
            Assert.That(bindings.library.copyEditWords.button.gameObject.activeInHierarchy, Is.True);
            Assert.That(bindings.library.copyEditWords.label.text, Is.EqualTo("Copy"));
            Assert.That(bindings.library.edit.button.gameObject.activeInHierarchy, Is.False,
                "A community download should use the shared copy action, not a separate edit entry.");
            Assert.That(bindings.library.delete.label.text, Is.EqualTo("Delete"));
            AssertExactlyOneSelectedLibraryRow(bindings.library, candidate.Title);

            Button sharedCopyAction = bindings.library.copyEditWords.button;
            MockupWordList published = GetStore(controller).QueryLibrary(new MockupLibraryQuery
                { View = MockupLibraryView.All })
                .First(item => item.Ownership == MockupOwnership.Owned
                    && !string.IsNullOrEmpty(item.RemoteItemId));
            Button publishedRow = FindLibraryButton(bindings.library, published.Id);
            Press(publishedRow);
            Assert.That(bindings.library.copyEditWords.button, Is.SameAs(sharedCopyAction),
                "All copy entry states must reuse the same authored Scene button.");
            Assert.That(sharedCopyAction.gameObject.activeInHierarchy, Is.True);
            Assert.That(bindings.library.copyEditWords.label.text, Is.EqualTo("Copy"));
            Assert.That(bindings.library.edit.button.gameObject.activeInHierarchy, Is.False,
                "The published original is immutable; use the shared Copy action.");
            AssertExactlyOneSelectedLibraryRow(bindings.library, published.Title);
            snapshot.AssertUnchanged("Community download");
            snapshot.AssertOriginalObjectsStillExist("Community download");
        }

        [Test]
        public void MyListsShowSeparateEditAndCopy_withOneBindingDrivenPublicationToggle()
        {
            WordListHubMockupController controller = harness.Controller;
            WordListHubSceneBindings bindings = harness.Bindings;
            UiSceneSnapshot snapshot = UiSceneSnapshot.Capture(harness.Root);
            InvokeAwake(controller);

            MockupWordListStore store = GetStore(controller);
            MockupWordList source = store.FindById("mock-list-travel-copy");
            Assert.That(source, Is.Not.Null);
            Press(FindLibraryButton(bindings.library, source.Id));

            Assert.That(bindings.library.edit.button.gameObject.activeInHierarchy, Is.True);
            Assert.That(bindings.library.copyEditWords.button.gameObject.activeInHierarchy, Is.True);
            Assert.That(bindings.library.copyEditWords.label.text, Is.EqualTo("Copy"));
            Assert.That(bindings.library.publish.button.gameObject.activeInHierarchy, Is.True);
            Assert.That(bindings.library.publish.label.text, Is.EqualTo("Publish"));
            Assert.That(bindings.library.unpublish.button.gameObject.activeSelf, Is.False);

            Press(bindings.library.copyEditWords.button);
            Assert.That(bindings.modal.editList.root.activeInHierarchy, Is.True);
            Assert.That(bindings.modal.editList.saveButtonLabel.text, Is.EqualTo("Save editable copy"));
            SetInputValueWithoutNotify(bindings.modal.editList.titleInput, "Travel Phrases Copy");
            Press(bindings.modal.editList.saveButton);
            Assert.That(bindings.modal.contentChange.root.activeInHierarchy, Is.True);
            Press(bindings.modal.contentChange.confirmButton);

            MockupWordList copy = store.QueryLibrary(new MockupLibraryQuery
                { View = MockupLibraryView.MyLists })
                .Single(item => item.Title == "Travel Phrases Copy");
            Assert.That(copy.Id, Is.Not.EqualTo(source.Id));
            Assert.That(copy.Group, Is.EqualTo(MockupLibraryGroup.MyLists));
            Assert.That(copy.HasPublishedBinding, Is.False);
            Assert.That(copy.RemoteItemId, Is.Null);
            Assert.That(source.Title, Is.EqualTo("Travel Phrases"));
            Assert.That(bindings.library.detailTitle.text, Is.EqualTo(copy.Title));

            Press(bindings.library.publish.button);
            Assert.That(copy.HasPublishedBinding, Is.True);
            Assert.That(bindings.library.publish.label.text, Is.EqualTo("Unpublish"));
            Assert.That(bindings.library.publish.button.gameObject.activeInHierarchy, Is.True);
            Assert.That(bindings.library.unpublish.button.gameObject.activeSelf, Is.False);
            Assert.That(bindings.library.edit.button.gameObject.activeInHierarchy, Is.False);

            // A bound remote identity drives the toggle even if the visible status is stale.
            typeof(MockupWordList).GetProperty(nameof(MockupWordList.PublicationStatus))
                .GetSetMethod(true).Invoke(copy, new object[] { MockupPublicationStatus.NotPublished });
            InvokeRefreshLibraryPage(controller);
            Assert.That(bindings.library.publish.label.text, Is.EqualTo("Unpublish"));
            Assert.That(bindings.library.publish.button.interactable, Is.True);

            Press(bindings.library.publish.button);
            Assert.That(copy.HasPublishedBinding, Is.False);
            Assert.That(copy.Group, Is.EqualTo(MockupLibraryGroup.MyLists));
            Assert.That(bindings.library.publish.label.text, Is.EqualTo("Publish"));
            Assert.That(bindings.library.edit.button.gameObject.activeInHierarchy, Is.True);
            Assert.That(bindings.library.copyEditWords.button.gameObject.activeInHierarchy, Is.True);
            Assert.That(bindings.library.unpublish.button.gameObject.activeSelf, Is.False);
            snapshot.AssertUnchanged("Edit, Copy and publication toggle");
            snapshot.AssertOriginalObjectsStillExist("Edit, Copy and publication toggle");
        }

        private static void AssertLibraryRowsMatchQuery(
            MockupWordListStore fixtureStore,
            MockupLibraryQuery expectedQuery,
            WordListHubSceneBindings.LibraryPageBinding library)
        {
            HashSet<string> expected = new HashSet<string>(
                fixtureStore.QueryLibrary(expectedQuery).Select(item => item.Id));
            HashSet<string> actual = GetVisibleLibraryRowIds(library);
            CollectionAssert.AreEquivalent(expected, actual,
                "The visible authored and cloned rows should match the store query.");
        }

        private static void AssertExactlyOneSelectedLibraryRow(
            WordListHubSceneBindings.LibraryPageBinding library,
            string expectedTitle)
        {
            CollectionAssert.AreEqual(new[] { expectedTitle }, GetSelectedLibraryRowTitles(library),
                "Exactly the selected Library row should show its Scene-authored visual.");
        }

        private static IEnumerable<string> GetSelectedLibraryRowTitles(
            WordListHubSceneBindings.LibraryPageBinding library)
        {
            IEnumerable<GameObject> selectedRoots = library.authoredRows
                .Where(row => row != null && row.root != null && row.root.activeInHierarchy
                    && row.selectedVisual != null && row.selectedVisual.activeInHierarchy)
                .Select(row => row.root)
                .Concat(library.listContent.GetComponentsInChildren<WordListHubLibraryRowView>(true)
                    .Where(row => row.gameObject.activeInHierarchy
                        && row.selectedVisual != null && row.selectedVisual.activeInHierarchy)
                    .Select(row => row.gameObject))
                .Distinct();

            return selectedRoots.Select(root =>
            {
                WordListHubSceneBindings.LibraryRowBinding binding = library.authoredRows
                    .FirstOrDefault(row => row != null && row.root == root);
                WordListHubLibraryRowView view = root.GetComponent<WordListHubLibraryRowView>();
                TMP_Text title = binding != null && binding.title != null
                    ? binding.title : view != null ? view.title : null;
                return title != null ? title.text : string.Empty;
            });
        }

        private static void AssertExactlyOneSelectedCommunityRow(
            WordListHubSceneBindings.CommunityPageBinding community,
            string expectedTitle)
        {
            CollectionAssert.AreEqual(new[] { expectedTitle }, GetSelectedCommunityRowTitles(community),
                "Exactly the selected Community row should show its Scene-authored visual.");
        }

        private static IEnumerable<string> GetSelectedCommunityRowTitles(
            WordListHubSceneBindings.CommunityPageBinding community)
        {
            IEnumerable<GameObject> selectedRoots = community.authoredRows
                .Where(row => row != null && row.root != null && row.root.activeInHierarchy
                    && row.selectedVisual != null && row.selectedVisual.activeInHierarchy)
                .Select(row => row.root)
                .Concat(community.listContent.GetComponentsInChildren<WordListHubDiscoveryRowView>(true)
                    .Where(row => row.gameObject.activeInHierarchy
                        && row.selectedVisual != null && row.selectedVisual.activeInHierarchy)
                    .Select(row => row.gameObject))
                .Distinct();

            return selectedRoots.Select(root =>
            {
                WordListHubSceneBindings.DiscoveryRowBinding binding = community.authoredRows
                    .FirstOrDefault(row => row != null && row.root == root);
                WordListHubDiscoveryRowView view = root.GetComponent<WordListHubDiscoveryRowView>();
                TMP_Text title = binding != null && binding.title != null
                    ? binding.title : view != null ? view.title : null;
                return title != null ? title.text : string.Empty;
            });
        }

        private static void AssertNoSelectedCommunityRows(
            WordListHubSceneBindings.CommunityPageBinding community)
        {
            Assert.That(GetSelectedCommunityRowTitles(community).Count(), Is.Zero,
                "Filtering out every Community item must hide all selected visuals.");
        }

        private static HashSet<string> GetVisibleLibraryRowIds(
            WordListHubSceneBindings.LibraryPageBinding library)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (WordListHubSceneBindings.LibraryRowBinding row in library.authoredRows)
            {
                if (row != null && row.root != null && row.root.activeInHierarchy)
                    ids.Add(row.listId);
            }
            foreach (WordListHubLibraryRowView row in library.listContent
                .GetComponentsInChildren<WordListHubLibraryRowView>(true))
            {
                if (row.gameObject.activeInHierarchy)
                    ids.Add(row.itemId);
            }
            return ids;
        }

        private static Button FindLibraryButton(
            WordListHubSceneBindings.LibraryPageBinding library,
            string listId)
        {
            WordListHubSceneBindings.LibraryRowBinding authored = library.authoredRows.FirstOrDefault(row =>
                row != null && row.listId == listId && row.root != null && row.root.activeInHierarchy);
            if (authored != null)
                return authored.button;

            WordListHubLibraryRowView dynamic = library.listContent
                .GetComponentsInChildren<WordListHubLibraryRowView>(true)
                .FirstOrDefault(row => row.itemId == listId && row.gameObject.activeInHierarchy);
            return dynamic != null ? dynamic.button : null;
        }

        private static MockupWordListStore GetStore(WordListHubMockupController controller)
        {
            FieldInfo storeField = typeof(WordListHubMockupController).GetField(
                "store", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(storeField, Is.Not.Null, "The runtime store is required for the Scene binding test.");
            return (MockupWordListStore)storeField.GetValue(controller);
        }

        private static MockupDiscoveryQuery GetCommunityQuery(WordListHubMockupController controller)
        {
            FieldInfo queryField = typeof(WordListHubMockupController).GetField(
                "communityQuery", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(queryField, Is.Not.Null, "The Community filter query is required for the Scene binding test.");
            return (MockupDiscoveryQuery)queryField.GetValue(controller);
        }

        private static RowAction FindFirstLibraryRow(WordListHubSceneBindings.LibraryPageBinding library)
        {
            WordListHubSceneBindings.LibraryRowBinding authored = library.authoredRows.FirstOrDefault(row =>
                row != null && row.root != null && row.root.activeInHierarchy && row.button != null);
            if (authored != null)
                return new RowAction(authored.button, authored.title != null ? authored.title.text : string.Empty);

            WordListHubLibraryRowView dynamic = library.listContent
                .GetComponentsInChildren<WordListHubLibraryRowView>(true)
                .FirstOrDefault(row => row.gameObject.activeInHierarchy && row.button != null);
            return dynamic != null ? new RowAction(dynamic.button, dynamic.title.text) : null;
        }

        private static RowAction FindFirstCommunityRow(WordListHubSceneBindings.CommunityPageBinding community)
        {
            WordListHubSceneBindings.DiscoveryRowBinding authored = community.authoredRows.FirstOrDefault(row =>
                row != null && row.root != null && row.root.activeInHierarchy && row.button != null);
            if (authored != null)
                return new RowAction(authored.button, authored.title != null ? authored.title.text : string.Empty);

            WordListHubDiscoveryRowView dynamic = community.listContent
                .GetComponentsInChildren<WordListHubDiscoveryRowView>(true)
                .FirstOrDefault(row => row.gameObject.activeInHierarchy && row.button != null);
            return dynamic != null ? new RowAction(dynamic.button, dynamic.title.text) : null;
        }

        private static RowAction FindCommunityRow(
            WordListHubSceneBindings.CommunityPageBinding community,
            string remoteId)
        {
            WordListHubSceneBindings.DiscoveryRowBinding authored = community.authoredRows.FirstOrDefault(row =>
                row != null && row.remoteItemId == remoteId && row.root != null
                && row.root.activeInHierarchy && row.button != null);
            if (authored != null)
                return new RowAction(authored.button, authored.title != null ? authored.title.text : string.Empty);

            WordListHubDiscoveryRowView dynamic = community.listContent
                .GetComponentsInChildren<WordListHubDiscoveryRowView>(true)
                .FirstOrDefault(row => row.gameObject.activeInHierarchy && row.button != null
                    && string.Equals(row.name, "DiscoveryItem_" + remoteId, StringComparison.Ordinal));
            return dynamic != null ? new RowAction(dynamic.button, dynamic.title.text) : null;
        }

        private static int CountLibraryRowsByTitle(
            WordListHubSceneBindings.LibraryPageBinding library,
            string title)
        {
            int count = 0;
            foreach (WordListHubLibraryRowView row in library.listContent
                .GetComponentsInChildren<WordListHubLibraryRowView>(true))
            {
                if (row.gameObject.activeInHierarchy && row.title != null && row.title.text == title)
                    count++;
            }
            foreach (WordListHubSceneBindings.LibraryRowBinding row in library.authoredRows)
            {
                if (row != null && row.root != null && row.root.activeInHierarchy
                    && row.title != null && row.title.text == title)
                    count++;
            }
            return count;
        }

        private static int CountVisibleAchievementCards(
            WordListHubSceneBindings.AchievementsPageBinding achievements)
        {
            return achievements.cardContent.GetComponentsInChildren<WordListHubAchievementCardView>(true)
                .Count(card => card.gameObject.activeInHierarchy && card != achievements.cardTemplate);
        }

        private static void AssertAuthoredLibraryRows(
            WordListHubSceneBindings.LibraryPageBinding library)
        {
            foreach (WordListHubSceneBindings.LibraryRowBinding row in library.authoredRows)
            {
                if (row == null)
                    continue;
                Assert.That(row.root, Is.Not.Null);
                AssertSelectedVisualStructure(row.selectedVisual, row.root.transform,
                    "Authored Library row " + row.listId);
                Assert.That(row.button, Is.Not.Null);
                Assert.That(row.favoriteButton, Is.Not.Null);
                Assert.That(row.favoriteButton.targetGraphic, Is.EqualTo(row.favoriteMark));
                Assert.That(row.favoriteMark, Is.Not.Null);
                Assert.That(row.bestTime, Is.Not.Null);
                Assert.That(row.bestTime.raycastTarget, Is.False);
                Assert.That(row.bestTime.rectTransform.rect.height, Is.EqualTo(28f).Within(0.001f));
                Assert.That(row.bestTime.textWrappingMode, Is.EqualTo(TextWrappingModes.NoWrap));
                AssertTextRendersWithin(row.bestTime, "Authored Library row " + row.listId + ".bestTime");
                Assert.That(row.title, Is.Not.Null);
                Assert.That(row.metadata, Is.Not.Null);
                Assert.That(row.recordStars, Is.Not.Null);
                AssertTileInformationRows(row.title, row.metadata, row.recordStars, row.bestTime,
                    "Authored Library row " + row.listId);
                AssertTextRendersWithin(row.recordStars, "Authored Library row " + row.listId + ".recordStars");
            }

            string expectedSelectedId = MockupWordListStore.CreateWithFixtures()
                .QueryLibrary(new MockupLibraryQuery()).First().Id;
            CollectionAssert.AreEqual(new[] { expectedSelectedId }, library.authoredRows
                .Where(row => row != null && row.selectedVisual != null && row.selectedVisual.activeSelf)
                .Select(row => row.listId),
                "The saved Library preview should highlight the same first result selected during Play initialization.");
        }

        private static void AssertAuthoredCommunityRows(
            WordListHubSceneBindings.CommunityPageBinding community)
        {
            foreach (WordListHubSceneBindings.DiscoveryRowBinding row in community.authoredRows)
            {
                if (row == null)
                    continue;
                Assert.That(row.root, Is.Not.Null);
                AssertSelectedVisualStructure(row.selectedVisual, row.root.transform,
                    "Authored Community row " + row.remoteItemId);
                Assert.That(row.button, Is.Not.Null);
                Assert.That(row.title, Is.Not.Null);
                Assert.That(row.metadata, Is.Not.Null);
                Assert.That(row.recordStars, Is.Not.Null);
                Assert.That(row.bestTime, Is.Not.Null);
                Assert.That(row.summary, Is.Not.Null);
                Assert.That(row.summary.gameObject.activeSelf, Is.False,
                    "Authored Community selection rows should hide the description line.");
                Assert.That(row.status, Is.Not.Null);
                AssertTileInformationRows(row.title, row.metadata, row.recordStars, row.bestTime,
                    "Authored Community row " + row.remoteItemId);
            }

            string expectedSelectedId = MockupWordListStore.CreateWithFixtures()
                .QueryDiscoveryWithFilters(new MockupDiscoveryQuery()).First().RemoteItemId;
            CollectionAssert.AreEqual(new[] { expectedSelectedId }, community.authoredRows
                .Where(row => row != null && row.selectedVisual != null && row.selectedVisual.activeSelf)
                .Select(row => row.remoteItemId),
                "The saved Community preview should highlight the same first result selected during Play initialization.");
        }

        private static void AssertLayoutGroup(Transform content, string description)
        {
            Assert.That(content, Is.Not.Null, description + " must be assigned.");
            Assert.That(content.GetComponent<LayoutGroup>(), Is.Not.Null,
                description + " must use the LayoutGroup configured in the Scene.");
        }

        private static void AssertSelectedVisual(GameObject selectedVisual, Transform rowRoot, string description)
        {
            AssertSelectedVisualStructure(selectedVisual, rowRoot, description);
            Assert.That(selectedVisual.activeSelf, Is.False,
                description + " SelectedState should be hidden on the reusable template.");
        }

        private static void AssertSelectedVisualStructure(
            GameObject selectedVisual,
            Transform rowRoot,
            string description)
        {
            Assert.That(selectedVisual, Is.Not.Null, description + " needs an authored SelectedState object.");
            Assert.That(selectedVisual.transform.parent, Is.EqualTo(rowRoot),
                description + " SelectedState must remain under its Scene-authored row.");
            Image image = selectedVisual.GetComponent<Image>();
            Assert.That(image, Is.Not.Null, description + " SelectedState needs an Image.");
            Assert.That(image.type, Is.EqualTo(Image.Type.Sliced), description + " SelectedState Image type");
            Assert.That(image.raycastTarget, Is.False, description + " SelectedState must not block row clicks.");
            LayoutElement layout = selectedVisual.GetComponent<LayoutElement>();
            Assert.That(layout, Is.Not.Null, description + " SelectedState needs a LayoutElement.");
            Assert.That(layout.ignoreLayout, Is.True, description + " SelectedState must not affect row layout.");
        }

        private static void AssertLibraryTemplate(WordListHubLibraryRowView template)
        {
            Assert.That(template, Is.Not.Null, "A Scene-authored Library row template is required.");
            AssertSelectedVisual(template.selectedVisual, template.transform, "Library row template");
            Assert.That(template.button, Is.Not.Null);
            Assert.That(template.favoriteButton, Is.Not.Null);
            Assert.That(template.favoriteButton.targetGraphic, Is.EqualTo(template.favoriteMark));
            Assert.That(template.favoriteMark, Is.Not.Null);
            Assert.That(template.bestTime, Is.Not.Null);
            Assert.That(template.bestTime.raycastTarget, Is.False);
            Assert.That(template.title, Is.Not.Null);
            Assert.That(template.metadata, Is.Not.Null);
            Assert.That(template.recordStars, Is.Not.Null);
            AssertReferenceWithin(template.transform, template.button.transform, "Library row button");
            AssertReferenceWithin(template.transform, template.favoriteButton.transform, "Library favorite button");
            AssertReferenceWithin(template.transform, template.favoriteMark.transform, "Library favorite text");
            AssertReferenceWithin(template.transform, template.bestTime.transform, "Library best time text");
            AssertReferenceWithin(template.transform, template.title.transform, "Library title text");
            AssertReferenceWithin(template.transform, template.metadata.transform, "Library metadata text");
            AssertReferenceWithin(template.transform, template.recordStars.transform, "Library record stars text");
            AssertTileInformationRows(template.title, template.metadata, template.recordStars,
                template.bestTime, "Library template");
            if (template.badge != null)
                AssertReferenceWithin(template.transform, template.badge.transform, "Library badge text");
        }

        private static void AssertCommunityTemplate(WordListHubDiscoveryRowView template)
        {
            Assert.That(template, Is.Not.Null, "A Scene-authored Community row template is required.");
            AssertSelectedVisual(template.selectedVisual, template.transform, "Community row template");
            Assert.That(template.button, Is.Not.Null);
            Assert.That(template.title, Is.Not.Null);
            Assert.That(template.metadata, Is.Not.Null);
            Assert.That(template.recordStars, Is.Not.Null);
            Assert.That(template.bestTime, Is.Not.Null);
            Assert.That(template.summary, Is.Not.Null);
            Assert.That(template.summary.gameObject.activeSelf, Is.False,
                "Community selection rows should hide the description line.");
            Assert.That(template.status, Is.Not.Null);
            AssertReferenceWithin(template.transform, template.button.transform, "Community row button");
            AssertReferenceWithin(template.transform, template.title.transform, "Community title text");
            AssertReferenceWithin(template.transform, template.metadata.transform, "Community metadata text");
            AssertReferenceWithin(template.transform, template.recordStars.transform, "Community record stars text");
            AssertReferenceWithin(template.transform, template.bestTime.transform, "Community best time text");
            AssertTileInformationRows(template.title, template.metadata, template.recordStars,
                template.bestTime, "Community template");
            AssertReferenceWithin(template.transform, template.summary.transform, "Community summary text");
            AssertReferenceWithin(template.transform, template.status.transform, "Community status text");
        }

        private static void AssertAchievementTemplate(WordListHubAchievementCardView template)
        {
            Assert.That(template, Is.Not.Null, "A Scene-authored achievement card template is required.");
            Assert.That(template.icon, Is.Not.Null);
            Assert.That(template.state, Is.Not.Null);
            Assert.That(template.title, Is.Not.Null);
            Assert.That(template.description, Is.Not.Null);
            Assert.That(template.progress, Is.Not.Null);
            AssertReferenceWithin(template.transform, template.icon.transform, "Achievement icon");
            AssertReferenceWithin(template.transform, template.state.transform, "Achievement state text");
            AssertReferenceWithin(template.transform, template.title.transform, "Achievement title text");
            AssertReferenceWithin(template.transform, template.description.transform, "Achievement description text");
            AssertReferenceWithin(template.transform, template.progress.transform, "Achievement progress text");
        }

        private static void AssertDropdownTemplate(TMP_Dropdown dropdown, string description)
        {
            Assert.That(dropdown, Is.Not.Null, description + " must be assigned.");
            Assert.That(dropdown.template, Is.Not.Null, description + " must retain its Scene option template.");
            Assert.That(dropdown.template.gameObject.activeSelf, Is.False,
                description + " option template should be hidden outside its popup.");
        }

        private static void AssertFormBindings(
            WordListHubSceneBindings.FormBinding form,
            string description,
            bool requireWords)
        {
            Assert.That(form, Is.Not.Null, description + " binding must exist.");
            Assert.That(form.root, Is.Not.Null);
            Assert.That(form.titleInput, Is.Not.Null);
            Assert.That(form.descriptionInput, Is.Not.Null);
            Assert.That(form.tagsInput, Is.Not.Null);
            Assert.That(form.languageDropdown, Is.Not.Null);
            Assert.That(form.validationText, Is.Not.Null);
            Assert.That(form.saveButton, Is.Not.Null);
            Assert.That(form.cancelButton, Is.Not.Null);
            if (requireWords)
                Assert.That(form.wordsInput, Is.Not.Null);
        }

        private static void AssertConfirmationBindings(
            WordListHubSceneBindings.ConfirmationBinding confirmation,
            string description)
        {
            Assert.That(confirmation, Is.Not.Null, description + " binding must exist.");
            Assert.That(confirmation.root, Is.Not.Null);
            Assert.That(confirmation.message, Is.Not.Null);
            Assert.That(confirmation.confirmButton, Is.Not.Null);
            Assert.That(confirmation.cancelButton, Is.Not.Null);
        }

        private static void AssertReferenceWithin(Transform root, Transform reference, string description)
        {
            Assert.That(reference == root || reference.IsChildOf(root), Is.True,
                description + " must point into its Scene template, not an unrelated object.");
        }

        private static void AssertRuntimeLibraryClonesMatchTemplate(
            WordListHubSceneBindings.LibraryPageBinding library)
        {
            foreach (WordListHubLibraryRowView row in library.listContent
                .GetComponentsInChildren<WordListHubLibraryRowView>(true))
            {
                if (row.GetComponent<WordListHubRuntimeInstance>() == null)
                    continue;
                AssertRectShapeMatches(library.rowTemplate.transform as RectTransform,
                    row.transform as RectTransform, row.name);
                AssertReferenceWithin(row.transform, row.button.transform, row.name + ".button");
                AssertReferenceWithin(row.transform, row.favoriteButton.transform, row.name + ".favoriteButton");
                AssertReferenceWithin(row.transform, row.favoriteMark.transform, row.name + ".favorite");
                AssertReferenceWithin(row.transform, row.bestTime.transform, row.name + ".bestTime");
                AssertReferenceWithin(row.transform, row.title.transform, row.name + ".title");
                AssertReferenceWithin(row.transform, row.metadata.transform, row.name + ".metadata");
                AssertReferenceWithin(row.transform, row.recordStars.transform, row.name + ".recordStars");
                if (row.badge != null)
                    AssertReferenceWithin(row.transform, row.badge.transform, row.name + ".badge");
                AssertTextStyleMatches(library.rowTemplate.favoriteMark, row.favoriteMark, row.name + ".favorite");
                AssertTextStyleMatches(library.rowTemplate.bestTime, row.bestTime, row.name + ".bestTime");
                AssertTextRendersWithin(row.bestTime, row.name + ".bestTime");
                AssertTextStyleMatches(library.rowTemplate.title, row.title, row.name + ".title");
                AssertTextStyleMatches(library.rowTemplate.metadata, row.metadata, row.name + ".metadata");
                AssertTextStyleMatches(library.rowTemplate.recordStars, row.recordStars, row.name + ".recordStars");
                AssertTileInformationRows(row.title, row.metadata, row.recordStars, row.bestTime,
                    row.name + " tile");
                AssertTextRendersWithin(row.recordStars, row.name + ".recordStars");
                if (library.rowTemplate.badge != null)
                    AssertTextStyleMatches(library.rowTemplate.badge, row.badge, row.name + ".badge");
            }
        }

        private static void AssertRuntimeCommunityClonesMatchTemplate(
            WordListHubSceneBindings.CommunityPageBinding community)
        {
            foreach (WordListHubDiscoveryRowView row in community.listContent
                .GetComponentsInChildren<WordListHubDiscoveryRowView>(true))
            {
                if (row.GetComponent<WordListHubRuntimeInstance>() == null)
                    continue;
                AssertRectShapeMatches(community.rowTemplate.transform as RectTransform,
                    row.transform as RectTransform, row.name);
                AssertReferenceWithin(row.transform, row.button.transform, row.name + ".button");
                AssertReferenceWithin(row.transform, row.title.transform, row.name + ".title");
                AssertReferenceWithin(row.transform, row.metadata.transform, row.name + ".metadata");
                AssertReferenceWithin(row.transform, row.recordStars.transform, row.name + ".recordStars");
                AssertReferenceWithin(row.transform, row.bestTime.transform, row.name + ".bestTime");
                AssertReferenceWithin(row.transform, row.summary.transform, row.name + ".summary");
                AssertReferenceWithin(row.transform, row.status.transform, row.name + ".status");
                AssertTextStyleMatches(community.rowTemplate.title, row.title, row.name + ".title");
                AssertTextStyleMatches(community.rowTemplate.metadata, row.metadata, row.name + ".metadata");
                AssertTextStyleMatches(community.rowTemplate.recordStars, row.recordStars, row.name + ".recordStars");
                AssertTextStyleMatches(community.rowTemplate.bestTime, row.bestTime, row.name + ".bestTime");
                AssertTextStyleMatches(community.rowTemplate.summary, row.summary, row.name + ".summary");
                AssertTextStyleMatches(community.rowTemplate.status, row.status, row.name + ".status");
                Assert.That(row.summary.gameObject.activeSelf, Is.False,
                    row.name + " must not display the Community description line.");

                RectTransform rowRect = row.transform as RectTransform;
                AssertRectContained(row.title.rectTransform, rowRect, row.name + ".title");
                AssertRectContained(row.metadata.rectTransform, rowRect, row.name + ".metadata");
                AssertRectContained(row.recordStars.rectTransform, rowRect, row.name + ".recordStars");
                AssertRectContained(row.bestTime.rectTransform, rowRect, row.name + ".bestTime");
                AssertRectContained(row.summary.rectTransform, rowRect, row.name + ".summary");
                AssertRectContained(row.status.rectTransform, rowRect, row.name + ".status");
                AssertTextRendersWithin(row.title, row.name + ".title");
                AssertTextRendersWithin(row.metadata, row.name + ".metadata");
                AssertTextRendersWithin(row.recordStars, row.name + ".recordStars");
                AssertTextRendersWithin(row.bestTime, row.name + ".bestTime");
                AssertTextRendersWithin(row.status, row.name + ".status");
                AssertTileInformationRows(row.title, row.metadata, row.recordStars, row.bestTime,
                    row.name + " tile");
                RectTransform[] informationRows =
                {
                    row.title.rectTransform,
                    row.metadata.rectTransform,
                    row.recordStars.rectTransform,
                    row.bestTime.rectTransform
                };
                for (int i = 0; i < informationRows.Length; i++)
                {
                    for (int j = i + 1; j < informationRows.Length; j++)
                        AssertNoRectOverlap(informationRows[i], informationRows[j],
                            row.name + " information row " + i + " and " + j);
                    AssertNoRectOverlap(informationRows[i], row.status.rectTransform,
                        row.name + " information row " + i + " and status");
                }
            }
        }

        private static void AssertRuntimeAchievementClonesMatchTemplate(
            WordListHubSceneBindings.AchievementsPageBinding achievements)
        {
            foreach (WordListHubAchievementCardView card in achievements.cardContent
                .GetComponentsInChildren<WordListHubAchievementCardView>(true))
            {
                if (card.GetComponent<WordListHubRuntimeInstance>() == null)
                    continue;
                AssertRectShapeMatches(achievements.cardTemplate.transform as RectTransform,
                    card.transform as RectTransform, card.name);
                AssertReferenceWithin(card.transform, card.icon.transform, card.name + ".icon");
                AssertImageStyleMatches(achievements.cardTemplate.icon, card.icon, card.name + ".icon");
                AssertReferenceWithin(card.transform, card.state.transform, card.name + ".state");
                AssertReferenceWithin(card.transform, card.title.transform, card.name + ".title");
                AssertReferenceWithin(card.transform, card.description.transform, card.name + ".description");
                AssertReferenceWithin(card.transform, card.progress.transform, card.name + ".progress");
                AssertTextStyleMatches(achievements.cardTemplate.state, card.state, card.name + ".state");
                AssertTextStyleMatches(achievements.cardTemplate.title, card.title, card.name + ".title");
                AssertTextStyleMatches(achievements.cardTemplate.description, card.description,
                    card.name + ".description");
                AssertTextStyleMatches(achievements.cardTemplate.progress, card.progress, card.name + ".progress");
            }
        }

        private static void AssertRectShapeMatches(RectTransform source, RectTransform clone, string description)
        {
            AssertVector2(clone.pivot, source.pivot, description + " pivot");
            DrivenTransformProperties driven = UiSceneSnapshot.GetConfiguredDrivenProperties(clone);
            if ((driven & DrivenTransformProperties.AnchorMinX) == 0)
                AssertFloat(clone.anchorMin.x, source.anchorMin.x, description + " anchorMin.x");
            if ((driven & DrivenTransformProperties.AnchorMinY) == 0)
                AssertFloat(clone.anchorMin.y, source.anchorMin.y, description + " anchorMin.y");
            if ((driven & DrivenTransformProperties.AnchorMaxX) == 0)
                AssertFloat(clone.anchorMax.x, source.anchorMax.x, description + " anchorMax.x");
            if ((driven & DrivenTransformProperties.AnchorMaxY) == 0)
                AssertFloat(clone.anchorMax.y, source.anchorMax.y, description + " anchorMax.y");
            if ((driven & DrivenTransformProperties.SizeDeltaX) == 0)
                AssertFloat(clone.sizeDelta.x, source.sizeDelta.x, description + " sizeDelta.x");
            if ((driven & DrivenTransformProperties.SizeDeltaY) == 0)
                AssertFloat(clone.sizeDelta.y, source.sizeDelta.y, description + " sizeDelta.y");
            AssertVector3(clone.localScale, source.localScale, description + " localScale");
            AssertQuaternion(clone.localRotation, source.localRotation, description + " localRotation");
        }

        private static void AssertVerticallyContained(
            RectTransform subject,
            RectTransform viewport,
            string description)
        {
            Vector3[] subjectCorners = new Vector3[4];
            Vector3[] viewportCorners = new Vector3[4];
            subject.GetWorldCorners(subjectCorners);
            viewport.GetWorldCorners(viewportCorners);
            float subjectMinY = subjectCorners.Min(corner => corner.y);
            float subjectMaxY = subjectCorners.Max(corner => corner.y);
            float viewportMinY = viewportCorners.Min(corner => corner.y);
            float viewportMaxY = viewportCorners.Max(corner => corner.y);

            Assert.That(subjectMinY, Is.GreaterThanOrEqualTo(viewportMinY - 0.01f),
                description + " bottom edge must remain inside the viewport.");
            Assert.That(subjectMaxY, Is.LessThanOrEqualTo(viewportMaxY + 0.01f),
                description + " top edge must remain inside the viewport.");
        }

        private static void AssertCommunityLabelLayout(
            TMP_Text text,
            Vector2 position,
            Vector2 size,
            string description,
            bool wraps = false,
            TextOverflowModes overflow = TextOverflowModes.Ellipsis)
        {
            AssertVector2(text.rectTransform.anchoredPosition, position, description + " position");
            AssertVector2(text.rectTransform.sizeDelta, size, description + " size");
            Assert.That(text.textWrappingMode,
                Is.EqualTo(wraps ? TextWrappingModes.Normal : TextWrappingModes.NoWrap), description + " wrapping");
            Assert.That(text.overflowMode, Is.EqualTo(overflow), description + " overflow");
        }

        private static void AssertCommunityLabelMatches(
            TMP_Text source,
            TMP_Text target,
            string description,
            bool wraps = false,
            TextOverflowModes overflow = TextOverflowModes.Ellipsis)
        {
            Rect sourceBounds = GetWorldRect(source.rectTransform);
            Rect targetBounds = GetWorldRect(target.rectTransform);
            Assert.That(targetBounds.xMin, Is.EqualTo(sourceBounds.xMin).Within(0.001f), description + " left edge");
            Assert.That(targetBounds.xMax, Is.EqualTo(sourceBounds.xMax).Within(0.001f), description + " right edge");
            Assert.That(targetBounds.yMin, Is.EqualTo(sourceBounds.yMin).Within(0.001f), description + " bottom edge");
            Assert.That(targetBounds.yMax, Is.EqualTo(sourceBounds.yMax).Within(0.001f), description + " top edge");
            Assert.That(target.textWrappingMode,
                Is.EqualTo(wraps ? TextWrappingModes.Normal : TextWrappingModes.NoWrap), description + " wrapping");
            Assert.That(target.overflowMode, Is.EqualTo(overflow), description + " overflow");
        }

        private static void AssertRectContained(
            RectTransform subject,
            RectTransform parent,
            string description)
        {
            Vector3[] subjectCorners = new Vector3[4];
            Vector3[] parentCorners = new Vector3[4];
            subject.GetWorldCorners(subjectCorners);
            parent.GetWorldCorners(parentCorners);
            float subjectMinX = subjectCorners.Min(corner => corner.x);
            float subjectMaxX = subjectCorners.Max(corner => corner.x);
            float subjectMinY = subjectCorners.Min(corner => corner.y);
            float subjectMaxY = subjectCorners.Max(corner => corner.y);
            float parentMinX = parentCorners.Min(corner => corner.x);
            float parentMaxX = parentCorners.Max(corner => corner.x);
            float parentMinY = parentCorners.Min(corner => corner.y);
            float parentMaxY = parentCorners.Max(corner => corner.y);

            Assert.That(subjectMinX, Is.GreaterThanOrEqualTo(parentMinX - 0.01f), description + " left edge");
            Assert.That(subjectMaxX, Is.LessThanOrEqualTo(parentMaxX + 0.01f), description + " right edge");
            Assert.That(subjectMinY, Is.GreaterThanOrEqualTo(parentMinY - 0.01f), description + " bottom edge");
            Assert.That(subjectMaxY, Is.LessThanOrEqualTo(parentMaxY + 0.01f), description + " top edge");
        }

        private static void AssertTextRendersWithin(TMP_Text text, string description)
        {
            if (text == null || string.IsNullOrWhiteSpace(text.text))
                return;

            text.ForceMeshUpdate(true, true);
            Assert.That(text.textInfo.characterCount, Is.GreaterThan(0),
                description + " should produce at least one visible character.");
            Assert.That(text.preferredHeight,
                Is.LessThanOrEqualTo(text.rectTransform.rect.height + 0.1f),
                description + " should fit its authored text box.");
        }

        private static void AssertTextStyleMatches(TMP_Text source, TMP_Text clone, string description)
        {
            Assert.That(clone.font, Is.SameAs(source.font), description + " font");
            Assert.That(clone.fontSharedMaterial, Is.SameAs(source.fontSharedMaterial), description + " material");
            Assert.That(clone.fontSize, Is.EqualTo(source.fontSize).Within(0.001f), description + " fontSize");
            Assert.That(clone.fontSizeMin, Is.EqualTo(source.fontSizeMin).Within(0.001f), description + " fontSizeMin");
            Assert.That(clone.fontSizeMax, Is.EqualTo(source.fontSizeMax).Within(0.001f), description + " fontSizeMax");
            Assert.That(clone.enableAutoSizing, Is.EqualTo(source.enableAutoSizing), description + " auto sizing");
            Assert.That(clone.fontStyle, Is.EqualTo(source.fontStyle), description + " fontStyle");
            Assert.That(clone.alignment, Is.EqualTo(source.alignment), description + " alignment");
            Assert.That(clone.textWrappingMode, Is.EqualTo(source.textWrappingMode), description + " wrapping");
            Assert.That(clone.overflowMode, Is.EqualTo(source.overflowMode), description + " overflow");
            Assert.That(clone.color, Is.EqualTo(source.color), description + " color");
            Assert.That(clone.margin, Is.EqualTo(source.margin), description + " margin");
            Assert.That(clone.raycastTarget, Is.EqualTo(source.raycastTarget), description + " raycast target");
        }

        private static void AssertTextBaseStyleMatches(TMP_Text source, TMP_Text clone, string description)
        {
            Assert.That(clone.font, Is.SameAs(source.font), description + " font");
            Assert.That(clone.fontSharedMaterial, Is.SameAs(source.fontSharedMaterial), description + " material");
            Assert.That(GetBaseFontSize(clone), Is.EqualTo(GetBaseFontSize(source)).Within(0.001f),
                description + " base font size");
            Assert.That(clone.fontSizeMin, Is.EqualTo(source.fontSizeMin).Within(0.001f),
                description + " fontSizeMin");
            Assert.That(clone.fontSizeMax, Is.EqualTo(source.fontSizeMax).Within(0.001f),
                description + " fontSizeMax");
            Assert.That(clone.enableAutoSizing, Is.EqualTo(source.enableAutoSizing), description + " auto sizing");
            Assert.That(clone.fontStyle, Is.EqualTo(source.fontStyle), description + " fontStyle");
            Assert.That(clone.alignment, Is.EqualTo(source.alignment), description + " alignment");
            Assert.That(clone.textWrappingMode, Is.EqualTo(source.textWrappingMode), description + " wrapping");
            Assert.That(clone.overflowMode, Is.EqualTo(source.overflowMode), description + " overflow");
            Assert.That(clone.color, Is.EqualTo(source.color), description + " color");
        }

        private static float GetBaseFontSize(TMP_Text text)
        {
            var serialized = new SerializedObject(text);
            serialized.Update();
            SerializedProperty baseFontSize = serialized.FindProperty("m_fontSizeBase");
            Assert.That(baseFontSize, Is.Not.Null, text.name + " should expose its authored TMP base font size.");
            return baseFontSize.floatValue;
        }

        private static void AssertImageStyleMatches(Image source, Image clone, string description)
        {
            Assert.That(clone.type, Is.EqualTo(source.type), description + " type");
            Assert.That(clone.color, Is.EqualTo(source.color), description + " color");
            Assert.That(clone.preserveAspect, Is.EqualTo(source.preserveAspect), description + " preserve aspect");
            Assert.That(clone.fillCenter, Is.EqualTo(source.fillCenter), description + " fill center");
            Assert.That(clone.fillMethod, Is.EqualTo(source.fillMethod), description + " fill method");
            Assert.That(clone.fillOrigin, Is.EqualTo(source.fillOrigin), description + " fill origin");
            Assert.That(clone.fillClockwise, Is.EqualTo(source.fillClockwise), description + " fill clockwise");
            Assert.That(clone.pixelsPerUnitMultiplier,
                Is.EqualTo(source.pixelsPerUnitMultiplier).Within(0.001f), description + " pixels per unit");
            Assert.That(clone.raycastTarget, Is.EqualTo(source.raycastTarget), description + " raycast target");
        }

        private static void AssertPageActive(WordListHubSceneBindings bindings, WordListHubPage activePage)
        {
            Assert.That(bindings.pages.Count(page => page.root != null && page.root.activeSelf), Is.EqualTo(1),
                "Exactly one authored page root should be active.");
            Assert.That(bindings.pages.Single(page => page.page == activePage).root.activeSelf, Is.True);
        }

        private static HashSet<Component> NavigationVisualComponents(WordListHubSceneBindings bindings)
        {
            var components = new HashSet<Component>();
            components.Add(bindings.community.detailTitle);
            components.Add(bindings.library.detailTitle);
            foreach (WordListHubSceneBindings.ButtonBinding binding in new[]
            {
                bindings.navChallenges,
                bindings.navLibrary,
                bindings.navCommunity,
                bindings.navAchievements
            })
            {
                if (binding == null)
                    continue;
                if (binding.button != null)
                {
                    components.Add(binding.button);
                    if (binding.button.targetGraphic != null)
                        components.Add(binding.button.targetGraphic);
                }
                if (binding.label != null)
                    components.Add(binding.label);
            }
            return components;
        }

        private static void AssertCommunityVisualAlignment(WordListHubSceneBindings bindings)
        {
            Canvas.ForceUpdateCanvases();
            RectTransform libraryListPane = bindings.library.listScroll.transform.parent as RectTransform;
            RectTransform communityListPane = bindings.community.listScroll.transform.parent as RectTransform;
            RectTransform libraryDetailPane = bindings.library.detailPane as RectTransform;
            RectTransform communityDetailPane = bindings.community.detailPane as RectTransform;
            RectTransform libraryScrollRoot = bindings.library.listScroll.GetComponent<RectTransform>();
            RectTransform communityScrollRoot = bindings.community.listScroll.GetComponent<RectTransform>();

            AssertVector2(communityListPane.anchoredPosition, libraryListPane.anchoredPosition,
                "Community list pane position should match Library");
            AssertVector2(communityListPane.rect.size, libraryListPane.rect.size,
                "Community list pane size should match Library");
            AssertVector2(communityListPane.anchorMin, libraryListPane.anchorMin,
                "Community list pane anchorMin should match Library");
            AssertVector2(communityListPane.anchorMax, libraryListPane.anchorMax,
                "Community list pane anchorMax should match Library");
            AssertVector2(communityListPane.pivot, libraryListPane.pivot,
                "Community list pane pivot should match Library");
            AssertVector2(communityDetailPane.anchoredPosition, libraryDetailPane.anchoredPosition,
                "Community detail pane position should match Library");
            AssertVector2(communityDetailPane.rect.size, libraryDetailPane.rect.size,
                "Community detail pane size should match Library");
            AssertVector2(communityScrollRoot.rect.size, libraryScrollRoot.rect.size,
                "Community list scroll root size should match Library");
            AssertVector2(bindings.community.listScroll.viewport.rect.size,
                bindings.library.listScroll.viewport.rect.size,
                "Community list viewport size should match Library");
            Assert.That(((RectTransform)bindings.community.listContent).rect.width,
                Is.EqualTo(((RectTransform)bindings.library.listContent).rect.width).Within(0.001f),
                "Community list content should use the same available width as Library.");

            RectTransform communityRow = bindings.community.rowTemplate.GetComponent<RectTransform>();
            LayoutElement communityRowLayout = bindings.community.rowTemplate.GetComponent<LayoutElement>();
            float expectedTemplateSize = WordListHubTileGridMetrics.Calculate(
                ((RectTransform)bindings.community.listContent).rect.width,
                WordListHubTileGridMetrics.PreferredColumnCount).TileSize;
            Assert.That(communityRow.rect.width, Is.EqualTo(expectedTemplateSize).Within(0.001f));
            Assert.That(communityRow.rect.height, Is.EqualTo(expectedTemplateSize).Within(0.001f));
            Assert.That(communityRowLayout.preferredWidth, Is.EqualTo(expectedTemplateSize).Within(0.001f));
            Assert.That(communityRowLayout.preferredHeight, Is.EqualTo(expectedTemplateSize).Within(0.001f));
            Assert.That(communityRowLayout.preferredWidth, Is.EqualTo(communityRow.rect.width).Within(0.001f));
            Assert.That(communityRow.rect.width,
                Is.LessThan(bindings.community.listScroll.viewport.rect.width));
            Assert.That(bindings.community.rowTemplate.title.fontSize,
                Is.EqualTo(bindings.library.rowTemplate.title.fontSize).Within(0.001f));
            Assert.That(bindings.community.rowTemplate.metadata.fontSize,
                Is.EqualTo(bindings.library.rowTemplate.metadata.fontSize).Within(0.001f));
            Assert.That(bindings.community.rowTemplate.title.fontSize, Is.EqualTo(22f).Within(0.001f));
            Assert.That(bindings.community.rowTemplate.metadata.fontSize, Is.EqualTo(18f).Within(0.001f));
            Assert.That(bindings.community.rowTemplate.summary.fontSize, Is.EqualTo(15f).Within(0.001f));
            Assert.That(bindings.community.rowTemplate.status.fontSize, Is.EqualTo(13f).Within(0.001f));
            Assert.That(bindings.community.rowTemplate.summary.textWrappingMode,
                Is.EqualTo(TextWrappingModes.NoWrap));
            Assert.That(bindings.community.rowTemplate.summary.overflowMode,
                Is.EqualTo(TextOverflowModes.Ellipsis));
            AssertCommunityLabelLayout(bindings.community.rowTemplate.title,
                new Vector2(0f, -36f), new Vector2(expectedTemplateSize - 40f, 64f), "Community row title", true);
            AssertCommunityLabelLayout(bindings.community.rowTemplate.metadata,
                new Vector2(0f, -116f), new Vector2(expectedTemplateSize - 40f, 28f), "Community row metadata");
            AssertCommunityLabelLayout(bindings.community.rowTemplate.recordStars,
                new Vector2(0f, -168f), new Vector2(expectedTemplateSize - 40f, 34f), "Community row PersonalStars");
            AssertCommunityLabelLayout(bindings.community.rowTemplate.bestTime,
                new Vector2(0f, -222f), new Vector2(expectedTemplateSize - 40f, 28f), "Community row best time");
            AssertCommunityLabelLayout(bindings.community.rowTemplate.status,
                new Vector2(-12f, -8f), new Vector2(70f, 22f), "Community row status");
            Assert.That(bindings.community.rowTemplate.status.alignment,
                Is.EqualTo(TextAlignmentOptions.MidlineRight));

            RectTransform communitySearch = bindings.community.searchInput.GetComponent<RectTransform>();
            RectTransform librarySearch = bindings.library.searchInput.GetComponent<RectTransform>();
            AssertVector2(communitySearch.anchoredPosition, librarySearch.anchoredPosition,
                "Community search position should match Library");
            AssertVector2(communitySearch.rect.size, librarySearch.rect.size,
                "Community search size should match Library");
            RectTransform communityFilters = bindings.community.connectionMenu.toggleButton.GetComponent<RectTransform>();
            RectTransform libraryFilters = bindings.library.filtersMenu.toggleButton.GetComponent<RectTransform>();
            AssertVector2(communityFilters.anchoredPosition, libraryFilters.anchoredPosition,
                "Community filters position should match Library");
            AssertVector2(communityFilters.rect.size, libraryFilters.rect.size,
                "Community filters size should match Library");
            RectTransform communityPopup = bindings.community.connectionMenu.panel.GetComponent<RectTransform>();
            Assert.That(communityPopup.anchoredPosition.x + (1f - communityPopup.pivot.x) * communityPopup.rect.width,
                Is.EqualTo(communityFilters.anchoredPosition.x
                    + (1f - communityFilters.pivot.x) * communityFilters.rect.width).Within(0.001f),
                "Community filter popup right edge should align with its toggle.");

            Transform communityListTitle = bindings.community.root.transform
                .Find("CommunityListPane/CommunityListTitle");
            Transform libraryListTitle = bindings.library.root.transform
                .Find("LibraryListPane/ListPaneTitle");
            AssertVector2(communityListTitle.GetComponent<RectTransform>().anchoredPosition,
                libraryListTitle.GetComponent<RectTransform>().anchoredPosition,
                "Community list heading position should match Library");
            AssertVector2(communityListTitle.GetComponent<RectTransform>().rect.size,
                libraryListTitle.GetComponent<RectTransform>().rect.size,
                "Community list heading size should match Library");
            AssertTextBaseStyleMatches(libraryListTitle.GetComponent<TMP_Text>(),
                communityListTitle.GetComponent<TMP_Text>(), "Community list heading style");

            TMP_Text communityDetailTitle = bindings.community.detailTitle;
            TMP_Text libraryDetailTitle = bindings.library.detailTitle;
            AssertCommunityLabelMatches(libraryDetailTitle, communityDetailTitle,
                "Community detail title", false, TextOverflowModes.Truncate);
            AssertCommunityLabelMatches(bindings.library.detailMetadata, bindings.community.detailAuthor,
                "Community detail author", true, TextOverflowModes.Truncate);
            AssertCommunityLabelMatches(bindings.library.detailDescription, bindings.community.detailDescription,
                "Community detail description", true);
            AssertTextBaseStyleMatches(libraryDetailTitle, communityDetailTitle, "Community detail title style");
            AssertTextBaseStyleMatches(bindings.library.detailMetadata, bindings.community.detailAuthor,
                "Community detail author style");
            AssertTextBaseStyleMatches(bindings.library.detailDescription, bindings.community.detailDescription,
                "Community detail description style");
            AssertTextBaseStyleMatches(bindings.library.detailWordsLabel, bindings.community.detailMetadata,
                "Community sample label style");
            AssertTextBaseStyleMatches(bindings.library.detailWords, bindings.community.detailSummary,
                "Community sample words style");

            RectTransform sampleRoot = communityDetailPane.Find("DiscoverySample").GetComponent<RectTransform>();
            AssertVector2(sampleRoot.anchoredPosition, new Vector2(0f, -25f),
                "Community sample wrapper position");
            AssertVector2(sampleRoot.rect.size, new Vector2(490f, 390f),
                "Community sample wrapper size");
            AssertCommunityLabelLayout(bindings.community.detailMetadata, new Vector2(0f, 180f),
                new Vector2(450f, 22f), "Community sample label", true, TextOverflowModes.Truncate);
            AssertCommunityLabelLayout(bindings.community.detailSummary, new Vector2(0f, -12f),
                new Vector2(470f, 350f), "Community sample words", true, TextOverflowModes.Overflow);
            AssertRectContained(bindings.community.detailMetadata.rectTransform, sampleRoot,
                "Community sample label");
            AssertRectContained(bindings.community.detailSummary.rectTransform, sampleRoot,
                "Community sample words");
            AssertNoRectOverlap(bindings.community.detailMetadata.rectTransform,
                bindings.community.detailSummary.rectTransform, "Community sample label and words");

            RectTransform[] communityActions =
            {
                bindings.community.like.button.GetComponent<RectTransform>(),
                bindings.community.download.button.GetComponent<RectTransform>(),
                bindings.community.play.button.GetComponent<RectTransform>(),
                bindings.community.report.button.GetComponent<RectTransform>()
            };
            float[] communityActionX = { -183f, -61f, 61f, 183f };
            for (int i = 0; i < communityActions.Length; i++)
            {
                AssertVector2(communityActions[i].anchoredPosition,
                    new Vector2(communityActionX[i], -259f), "Community action " + i + " position");
                AssertVector2(communityActions[i].rect.size, new Vector2(110f, 60f),
                    "Community action " + i + " size");
                Assert.That(communityActions[i].anchoredPosition.x - communityActions[i].rect.width * 0.5f,
                    Is.GreaterThanOrEqualTo(communityDetailPane.rect.xMin),
                    "Community action " + i + " left edge should fit inside the pane.");
                Assert.That(communityActions[i].anchoredPosition.x + communityActions[i].rect.width * 0.5f,
                    Is.LessThanOrEqualTo(communityDetailPane.rect.xMax),
                    "Community action " + i + " right edge should fit inside the pane.");
                if (i > 0)
                    AssertNoRectOverlap(communityActions[i - 1], communityActions[i],
                        "Community action " + (i - 1) + " / " + i);
            }
            Assert.That(communityDetailPane.Find("DiscoveryEyebrow").gameObject.activeSelf, Is.False,
                "The inactive discovery eyebrow should remain hidden.");
            Assert.That(bindings.community.detailTrustNote.gameObject.activeSelf, Is.False,
                "The inactive community trust note should remain hidden.");

            AssertNoRectOverlap(
                bindings.community.rowTemplate.title.rectTransform,
                bindings.community.rowTemplate.metadata.rectTransform,
                "Community row title and metadata");
            AssertNoRectOverlap(
                bindings.community.rowTemplate.title.rectTransform,
                bindings.community.rowTemplate.status.rectTransform,
                "Community row title and status");
            AssertNoRectOverlap(
                bindings.community.rowTemplate.summary.rectTransform,
                bindings.community.rowTemplate.status.rectTransform,
                "Community row summary and status");

            RectTransform detailPane = bindings.community.detailPane as RectTransform;
            var detailFlow = new List<RectTransform>
            {
                detailPane.Find("DiscoveryEyebrow").GetComponent<RectTransform>(),
                bindings.community.detailTitle.rectTransform,
                bindings.community.detailAuthor.rectTransform,
                bindings.community.detailDescription.rectTransform,
                detailPane.Find("DiscoverySample").GetComponent<RectTransform>(),
                bindings.community.detailTrustNote.rectTransform,
                bindings.community.like.button.GetComponent<RectTransform>(),
                bindings.community.report.button.GetComponent<RectTransform>()
            };
            for (int i = 0; i < detailFlow.Count; i++)
                AssertVerticallyContained(detailFlow[i], detailPane, "Community detail " + detailFlow[i].name);
            List<RectTransform> visibleDetailFlow = detailFlow
                .Where(rect => rect != null && rect.gameObject.activeSelf)
                .ToList();
            for (int i = 0; i < visibleDetailFlow.Count - 1; i++)
            {
                AssertNoRectOverlap(visibleDetailFlow[i], visibleDetailFlow[i + 1],
                    "Community detail flow " + visibleDetailFlow[i].name + " / " + visibleDetailFlow[i + 1].name);
            }
            AssertNoRectOverlap(
                bindings.community.like.button.GetComponent<RectTransform>(),
                bindings.community.download.button.GetComponent<RectTransform>(),
                "Community Like and Download actions");
            AssertNoRectOverlap(
                bindings.community.download.button.GetComponent<RectTransform>(),
                bindings.community.play.button.GetComponent<RectTransform>(),
                "Community Download and Play actions");
        }

        private static void AssertNoRectOverlap(RectTransform first, RectTransform second, string description)
        {
            Rect firstBounds = GetWorldRect(first);
            Rect secondBounds = GetWorldRect(second);
            Assert.That(firstBounds.Overlaps(secondBounds), Is.False, description + " must not overlap.");
        }

        private static Rect GetWorldRect(RectTransform rect)
        {
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            float minX = corners.Min(corner => corner.x);
            float maxX = corners.Max(corner => corner.x);
            float minY = corners.Min(corner => corner.y);
            float maxY = corners.Max(corner => corner.y);
            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        private sealed class NavigationStyleSnapshot
        {
            public ColorBlock Colors { get; }
            public Color Background { get; }
            public Color Label { get; }

            private NavigationStyleSnapshot(WordListHubSceneBindings.ButtonBinding binding)
            {
                Colors = binding.button.colors;
                Background = binding.button.targetGraphic.color;
                Label = binding.label.color;
            }

            public static NavigationStyleSnapshot Capture(WordListHubSceneBindings.ButtonBinding binding)
            {
                Assert.That(binding, Is.Not.Null);
                Assert.That(binding.button, Is.Not.Null);
                Assert.That(binding.button.targetGraphic, Is.Not.Null);
                Assert.That(binding.label, Is.Not.Null);
                return new NavigationStyleSnapshot(binding);
            }
        }

        private static void AssertNavigationVisuals(
            WordListHubSceneBindings bindings,
            WordListHubPage activePage,
            NavigationStyleSnapshot activeStyle,
            NavigationStyleSnapshot inactiveStyle)
        {
            AssertNavigationVisual(bindings.navChallenges,
                activePage == WordListHubPage.Challenges, activeStyle, inactiveStyle);
            AssertNavigationVisual(bindings.navLibrary,
                activePage == WordListHubPage.Library, activeStyle, inactiveStyle);
            AssertNavigationVisual(bindings.navCommunity,
                activePage == WordListHubPage.Community, activeStyle, inactiveStyle);
            AssertNavigationVisual(bindings.navAchievements,
                activePage == WordListHubPage.Achievements, activeStyle, inactiveStyle);
        }

        private static void AssertNavigationVisual(
            WordListHubSceneBindings.ButtonBinding binding,
            bool active,
            NavigationStyleSnapshot activeStyle,
            NavigationStyleSnapshot inactiveStyle)
        {
            NavigationStyleSnapshot expected = active ? activeStyle : inactiveStyle;
            Assert.That(binding.button.interactable, Is.True, binding.button.name + " remains clickable.");
            Assert.That(binding.button.colors.normalColor, Is.EqualTo(expected.Colors.normalColor));
            Assert.That(binding.button.colors.selectedColor, Is.EqualTo(expected.Colors.selectedColor));
            Assert.That(binding.button.targetGraphic.color, Is.EqualTo(expected.Background));
            Assert.That(binding.label.color, Is.EqualTo(expected.Label));
        }

        private static void Press(Button button)
        {
            Assert.That(button, Is.Not.Null, "Expected a serialized or cloned UI button reference.");
            Assert.That(button.gameObject.activeInHierarchy, Is.True, button.name + " must be visible.");
            Assert.That(button.interactable, Is.True, button.name + " must be interactable.");
            button.onClick.Invoke();
        }

        private static void SetDropdownValue(TMP_Dropdown dropdown, int value)
        {
            Assert.That(dropdown, Is.Not.Null);
            Assert.That(value, Is.InRange(0, dropdown.options.Count - 1));
            dropdown.SetValueWithoutNotify(value);
            dropdown.onValueChanged.Invoke(value);
        }

        private static void SetInputValue(TMP_InputField input, string value)
        {
            Assert.That(input, Is.Not.Null);
            input.SetTextWithoutNotify(value);
            input.onValueChanged.Invoke(value);
        }

        private static void SetInputValueWithoutNotify(TMP_InputField input, string value)
        {
            Assert.That(input, Is.Not.Null);
            input.SetTextWithoutNotify(value);
        }

        private static TMP_Text CreateIsolatedRubyTestText(TMP_Text template, string name)
        {
            Assert.That(template, Is.Not.Null);
            RectTransform templateRect = template.rectTransform;
            GameObject regionObject = new GameObject(name + "Region", typeof(RectTransform));
            regionObject.transform.SetParent(templateRect.parent, false);
            RectTransform region = regionObject.GetComponent<RectTransform>();
            region.anchorMin = templateRect.anchorMin;
            region.anchorMax = templateRect.anchorMax;
            region.pivot = templateRect.pivot;
            region.anchoredPosition = templateRect.anchoredPosition;
            region.sizeDelta = templateRect.sizeDelta;
            region.localScale = templateRect.localScale;
            LayoutElement regionLayout = regionObject.AddComponent<LayoutElement>();
            regionLayout.ignoreLayout = true;

            GameObject textObject = UnityEngine.Object.Instantiate(template.gameObject, region, false);
            textObject.name = name;
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.pivot = new Vector2(0.5f, 0.5f);
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            TMP_Text testText = textObject.GetComponent<TMP_Text>();
            LayoutElement textLayout = textObject.GetComponent<LayoutElement>();
            if (textLayout == null)
                textLayout = textObject.AddComponent<LayoutElement>();
            textLayout.ignoreLayout = true;
            testText.enabled = true;
            return testText;
        }

        private static int CountRuntimeChildren(Transform content)
        {
            return content.GetComponentsInChildren<WordListHubRuntimeInstance>(true)
                .Count(marker => marker != null && marker.transform.parent == content);
        }

        private static int CountHierarchyObjects(Transform root)
        {
            return root.GetComponentsInChildren<Transform>(true).Length;
        }

        private static bool IsSquareTile(RectTransform rect)
        {
            return rect != null
                && rect.rect.width > 0f
                && Mathf.Abs(rect.rect.width - rect.rect.height) < 0.1f;
        }

        private static void AssertTileSetMatchesGrid(
            IEnumerable<RectTransform> tileRects,
            float availableWidth,
            string description)
        {
            List<RectTransform> tiles = tileRects.Where(tile => tile != null).ToList();
            Assert.That(tiles, Is.Not.Empty, description + " should contain tiles.");
            float expectedSize = WordListHubTileGridMetrics.Calculate(availableWidth, tiles.Count).TileSize;
            foreach (RectTransform tile in tiles)
            {
                Assert.That(tile.rect.width, Is.EqualTo(expectedSize).Within(0.1f), description + ".width");
                Assert.That(tile.rect.height, Is.EqualTo(expectedSize).Within(0.1f), description + ".height");
            }
        }

        private static void AddAuthoredCommunityPreviewRows(
            WordListHubSceneBindings.CommunityPageBinding community,
            MockupWordListStore fixtures)
        {
            if (community.authoredRows == null)
                community.authoredRows = new List<WordListHubSceneBindings.DiscoveryRowBinding>();
            foreach (MockupDiscoveryItem item in fixtures.QueryDiscoveryWithFilters(new MockupDiscoveryQuery()))
            {
                WordListHubSceneBindings.DiscoveryRowBinding binding = community.authoredRows
                    .FirstOrDefault(row => row != null && row.remoteItemId == item.RemoteItemId);
                if (binding != null && binding.root != null)
                    continue;

                WordListHubDiscoveryRowView view = UnityEngine.Object.Instantiate(
                    community.rowTemplate, community.listContent, false);
                view.name = "CommunityPreview_" + item.RemoteItemId;
                if (binding == null)
                {
                    binding = new WordListHubSceneBindings.DiscoveryRowBinding();
                    community.authoredRows.Add(binding);
                }
                binding.remoteItemId = item.RemoteItemId;
                binding.root = view.gameObject;
                binding.selectedVisual = view.selectedVisual;
                binding.button = view.button;
                binding.title = view.title;
                binding.metadata = view.metadata;
                binding.summary = view.summary;
                binding.status = view.status;
            }
        }

        private static void AddAuthoredAchievementPreviewCards(
            WordListHubSceneBindings.AchievementsPageBinding achievements,
            MockupWordListStore fixtures)
        {
            if (achievements.cards == null)
                achievements.cards = new List<WordListHubSceneBindings.AchievementCardBinding>();
            foreach (MockupAchievement item in fixtures.Achievements)
            {
                WordListHubSceneBindings.AchievementCardBinding binding = achievements.cards
                    .FirstOrDefault(card => card != null && card.achievementId == item.Id);
                if (binding != null && binding.root != null && binding.view != null)
                    continue;

                WordListHubAchievementCardView view = UnityEngine.Object.Instantiate(
                    achievements.cardTemplate, achievements.cardContent, false);
                view.name = "AchievementPreview_" + item.Id;
                if (binding == null)
                {
                    binding = new WordListHubSceneBindings.AchievementCardBinding();
                    achievements.cards.Add(binding);
                }
                binding.achievementId = item.Id;
                binding.root = view.gameObject;
                binding.view = view;
            }
        }

        private static void InvokeAwake(WordListHubMockupController controller)
        {
            MethodInfo awake = typeof(WordListHubMockupController).GetMethod(
                "Awake", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(awake, Is.Not.Null, "The test needs a controller initialization entry point.");
            awake.Invoke(controller, null);
        }

        private static void InvokePrivate(object target, string methodName, params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, methodName + " should exist.");
            method.Invoke(target, arguments);
        }

        private static void InvokeRefreshLibraryPage(WordListHubMockupController controller)
        {
            MethodInfo refresh = typeof(WordListHubMockupController).GetMethod(
                "RefreshLibraryPage", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(refresh, Is.Not.Null);
            refresh.Invoke(controller, new object[] { false, false });
        }

        private static void InvokeChangePage(WordListHubMockupController controller, string pageName)
        {
            MethodInfo changePage = typeof(WordListHubMockupController).GetMethod(
                "ChangePage", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(changePage, Is.Not.Null);
            Type pageType = changePage.GetParameters()[0].ParameterType;
            changePage.Invoke(controller, new[] { Enum.Parse(pageType, pageName) });
        }

        private static void AssertTileInformationRows(
            TMP_Text title,
            TMP_Text metadata,
            TMP_Text recordStars,
            TMP_Text bestTime,
            string description)
        {
            AssertTileTextRect(title, 36f, 64f, 22f, description + ".name");
            AssertTileTextRect(metadata, 116f, 28f, 18f, description + ".languageAndCount");
            AssertTileTextRect(recordStars, 168f, 34f, 22f, description + ".stars");
            AssertTileTextRect(bestTime, 222f, 28f, 18f, description + ".time");
        }

        private static void AssertTileTextRect(TMP_Text text, float top, float height, float fontSize, string description)
        {
            Assert.That(text, Is.Not.Null, description + " must be assigned.");
            RectTransform tile = text.rectTransform.parent as RectTransform;
            Assert.That(tile, Is.Not.Null, description + " must be parented to a tile.");
            float expectedWidth = Mathf.Max(0f, tile.rect.width - 40f);
            AssertVector2(text.rectTransform.anchoredPosition, new Vector2(0f, -top), description + " position");
            AssertVector2(text.rectTransform.sizeDelta, new Vector2(expectedWidth, height), description + " size");
            Assert.That(text.fontSize, Is.EqualTo(fontSize).Within(0.01f), description + " font size");
        }

        private static void InvokeControllerUpdate(WordListHubMockupController controller)
        {
            MethodInfo update = typeof(WordListHubMockupController).GetMethod(
                "Update", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(update, Is.Not.Null);
            update.Invoke(controller, null);
        }

        private static void AssertVector2(Vector2 actual, Vector2 expected, string description)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.001f), description + ".x");
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.001f), description + ".y");
        }

        private static void AssertFloat(float actual, float expected, string description)
        {
            Assert.That(actual, Is.EqualTo(expected).Within(0.001f), description);
        }

        private static void AssertVector3(Vector3 actual, Vector3 expected, string description)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.001f), description + ".x");
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.001f), description + ".y");
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(0.001f), description + ".z");
        }

        private static void AssertQuaternion(Quaternion actual, Quaternion expected, string description)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.001f), description + ".x");
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.001f), description + ".y");
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(0.001f), description + ".z");
            Assert.That(actual.w, Is.EqualTo(expected.w).Within(0.001f), description + ".w");
        }

        private sealed class RowAction
        {
            public readonly Button button;
            public readonly string title;

            public RowAction(Button button, string title)
            {
                this.button = button;
                this.title = title;
            }
        }

        private sealed class SceneHarness : IDisposable
        {
            private readonly Scene previousActiveScene;
            private readonly Scene sourceScene;
            private readonly bool openedSourceScene;
            private readonly Scene isolatedScene;
            private readonly bool createdIsolatedScene;
            private readonly GameObject testEventSystemRoot;

            public readonly GameObject Root;
            public readonly WordListHubMockupController Controller;
            public readonly WordListHubSceneBindings Bindings;
            public readonly EventSystem TestEventSystem;

            private SceneHarness(
                Scene previousActiveScene,
                Scene sourceScene,
                bool openedSourceScene,
                Scene isolatedScene,
                bool createdIsolatedScene,
                GameObject testEventSystemRoot,
                GameObject root,
                WordListHubMockupController controller,
                WordListHubSceneBindings bindings)
            {
                this.previousActiveScene = previousActiveScene;
                this.sourceScene = sourceScene;
                this.openedSourceScene = openedSourceScene;
                this.isolatedScene = isolatedScene;
                this.createdIsolatedScene = createdIsolatedScene;
                this.testEventSystemRoot = testEventSystemRoot;
                Root = root;
                Controller = controller;
                Bindings = bindings;
                TestEventSystem = testEventSystemRoot != null
                    ? testEventSystemRoot.GetComponentInChildren<EventSystem>(true)
                    : null;
            }

            public static SceneHarness Open()
            {
                Scene previousActiveScene = SceneManager.GetActiveScene();
                Scene sourceScene = SceneManager.GetSceneByPath(MockupScenePath);
                bool openedSourceScene = !sourceScene.IsValid() || !sourceScene.isLoaded;
                if (openedSourceScene)
                    sourceScene = EditorSceneManager.OpenScene(MockupScenePath, OpenSceneMode.Additive);

                Scene isolatedScene = default;
                for (int index = 0; index < SceneManager.sceneCount; index++)
                {
                    Scene candidate = SceneManager.GetSceneAt(index);
                    if (candidate.IsValid() && candidate.isLoaded && string.IsNullOrEmpty(candidate.path))
                    {
                        isolatedScene = candidate;
                        break;
                    }
                }

                bool createdIsolatedScene = !isolatedScene.IsValid();
                if (createdIsolatedScene)
                    isolatedScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                GameObject root = null;
                GameObject testEventSystemRoot = null;
                try
                {
                    WordListHubMockupController sourceController = FindController(sourceScene);
                    Assert.That(sourceController, Is.Not.Null,
                        "The saved mockup scene must contain WordListHubMockupController.");

                    root = UnityEngine.Object.Instantiate(sourceController.transform.root.gameObject);
                    SceneManager.MoveGameObjectToScene(root, isolatedScene);
                    GameObject sourceEventSystemRoot = sourceScene.GetRootGameObjects()
                        .FirstOrDefault(sceneRoot =>
                            sceneRoot.GetComponentInChildren<EventSystem>(true) != null);
                    testEventSystemRoot = sourceEventSystemRoot != null
                        ? UnityEngine.Object.Instantiate(sourceEventSystemRoot)
                        : new GameObject("WordListHubTestEventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                    SceneManager.MoveGameObjectToScene(testEventSystemRoot, isolatedScene);
                    WordListHubMockupController controller = root
                        .GetComponentsInChildren<WordListHubMockupController>(true).SingleOrDefault();
                    WordListHubSceneBindings bindings = root
                        .GetComponentsInChildren<WordListHubSceneBindings>(true).SingleOrDefault();

                    Assert.That(controller, Is.Not.Null);
                    Assert.That(bindings, Is.Not.Null,
                        "The migrated scene must include WordListHubSceneBindings.");
                    if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                        EditorSceneManager.SetActiveScene(previousActiveScene);

                    return new SceneHarness(previousActiveScene, sourceScene, openedSourceScene,
                        isolatedScene, createdIsolatedScene, testEventSystemRoot, root, controller, bindings);
                }
                catch
                {
                    if (testEventSystemRoot != null)
                        UnityEngine.Object.DestroyImmediate(testEventSystemRoot);
                    if (root != null)
                        UnityEngine.Object.DestroyImmediate(root);
                    if (createdIsolatedScene && isolatedScene.IsValid() && isolatedScene.isLoaded)
                        EditorSceneManager.CloseScene(isolatedScene, true);
                    if (openedSourceScene && sourceScene.IsValid() && sourceScene.isLoaded)
                        EditorSceneManager.CloseScene(sourceScene, true);
                    if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                        EditorSceneManager.SetActiveScene(previousActiveScene);
                    throw;
                }
            }

            public void Dispose()
            {
                if (testEventSystemRoot != null)
                    UnityEngine.Object.DestroyImmediate(testEventSystemRoot);
                if (Root != null)
                    UnityEngine.Object.DestroyImmediate(Root);
                if (createdIsolatedScene && isolatedScene.IsValid() && isolatedScene.isLoaded)
                    EditorSceneManager.CloseScene(isolatedScene, true);
                if (openedSourceScene && sourceScene.IsValid() && sourceScene.isLoaded)
                    EditorSceneManager.CloseScene(sourceScene, true);
                if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                    EditorSceneManager.SetActiveScene(previousActiveScene);
            }

            private static WordListHubMockupController FindController(Scene scene)
            {
                return scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<WordListHubMockupController>(true))
                    .FirstOrDefault();
            }
        }

        private sealed class UiSceneSnapshot
        {
            private readonly List<GameObjectIdentity> objects = new List<GameObjectIdentity>();
            private readonly List<ComponentIdentity> components = new List<ComponentIdentity>();
            private readonly List<RectState> rects = new List<RectState>();
            private readonly List<TextStyleState> texts = new List<TextStyleState>();
            private readonly List<ImageStyleState> images = new List<ImageStyleState>();
            private readonly List<SelectableStyleState> selectables = new List<SelectableStyleState>();
            private readonly List<SerializedComponentState> layoutComponents =
                new List<SerializedComponentState>();

            public static UiSceneSnapshot Capture(
                GameObject root,
                ISet<Component> intentionallyDynamicVisuals = null)
            {
                var snapshot = new UiSceneSnapshot();
                WordListHubSceneBindings bindings = root.GetComponentInChildren<WordListHubSceneBindings>(true);
                var dataDrivenRubyContent = new List<Transform>();
                if (bindings != null)
                {
                    if (bindings.libraryRubyWords != null && bindings.libraryRubyWords.content != null)
                        dataDrivenRubyContent.Add(bindings.libraryRubyWords.content);
                    if (bindings.communityRubyWords != null && bindings.communityRubyWords.content != null)
                        dataDrivenRubyContent.Add(bindings.communityRubyWords.content);
                }

                bool IsDataDrivenRubyTransform(Transform target)
                {
                    return dataDrivenRubyContent.Any(content =>
                        target == content || target.IsChildOf(content));
                }

                foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                    snapshot.objects.Add(new GameObjectIdentity(transform.gameObject));
                foreach (Component component in root.GetComponentsInChildren<Component>(true))
                {
                    if (component != null)
                        snapshot.components.Add(new ComponentIdentity(component));
                }

                foreach (RectTransform rect in root.GetComponentsInChildren<RectTransform>(true))
                {
                    if (IsDataDrivenRubyTransform(rect))
                        continue;
                    DrivenTransformProperties drivenProperties = GetConfiguredDrivenProperties(rect);
                    foreach (ScrollRect scroll in root.GetComponentsInChildren<ScrollRect>(true))
                    {
                        if (scroll.content != rect)
                            continue;
                        if (scroll.horizontal)
                            drivenProperties |= DrivenTransformProperties.AnchoredPositionX;
                        if (scroll.vertical)
                            drivenProperties |= DrivenTransformProperties.AnchoredPositionY;
                    }
                    snapshot.rects.Add(new RectState(rect, drivenProperties));
                }

                Selectable[] sceneSelectables = root.GetComponentsInChildren<Selectable>(true);
                var selectableGraphics = new HashSet<Graphic>(sceneSelectables
                    .Where(selectable => selectable.targetGraphic != null)
                    .Select(selectable => selectable.targetGraphic));
                foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (IsDataDrivenRubyTransform(text.transform))
                        continue;
                    bool skipColor = selectableGraphics.Contains(text)
                        || intentionallyDynamicVisuals != null && intentionallyDynamicVisuals.Contains(text);
                    snapshot.texts.Add(new TextStyleState(text, skipColor));
                }
                foreach (Image image in root.GetComponentsInChildren<Image>(true))
                {
                    if (IsDataDrivenRubyTransform(image.transform))
                        continue;
                    bool skipColor = selectableGraphics.Contains(image)
                        || intentionallyDynamicVisuals != null && intentionallyDynamicVisuals.Contains(image);
                    snapshot.images.Add(new ImageStyleState(image, skipColor));
                }
                foreach (Selectable selectable in sceneSelectables)
                {
                    if (IsDataDrivenRubyTransform(selectable.transform))
                        continue;
                    if (intentionallyDynamicVisuals == null || !intentionallyDynamicVisuals.Contains(selectable))
                        snapshot.selectables.Add(new SelectableStyleState(selectable));
                }

                foreach (Component component in root.GetComponentsInChildren<Component>(true))
                {
                    if (IsDataDrivenRubyTransform(component.transform))
                        continue;
                    if (component is LayoutGroup || component is LayoutElement
                        || component is ContentSizeFitter || component is AspectRatioFitter
                        || component is ScrollRect)
                    {
                        snapshot.layoutComponents.Add(new SerializedComponentState(component));
                    }
                }
                return snapshot;
            }

            internal static DrivenTransformProperties GetConfiguredDrivenProperties(RectTransform rect)
            {
                DrivenTransformProperties driven = DrivenTransformProperties.None;
                LayoutElement layoutElement = rect.GetComponent<LayoutElement>();
                LayoutGroup parentLayout = rect.parent != null
                    ? rect.parent.GetComponent<LayoutGroup>() : null;
                bool participatesInParentLayout = parentLayout != null
                    && (layoutElement == null || !layoutElement.ignoreLayout);

                if (participatesInParentLayout)
                {
                    if (parentLayout is GridLayoutGroup)
                    {
                        driven |= DrivenTransformProperties.Anchors
                            | DrivenTransformProperties.AnchoredPosition
                            | DrivenTransformProperties.SizeDelta;
                    }
                    else if (parentLayout is HorizontalOrVerticalLayoutGroup linearLayout)
                    {
                        // Linear groups position children on both axes and always drive
                        // their anchors. Child size is driven only on enabled axes.
                        driven |= DrivenTransformProperties.Anchors
                            | DrivenTransformProperties.AnchoredPositionX
                            | DrivenTransformProperties.AnchoredPositionY;
                        if (linearLayout.childControlWidth)
                            driven |= DrivenTransformProperties.SizeDeltaX;
                        if (linearLayout.childControlHeight)
                            driven |= DrivenTransformProperties.SizeDeltaY;
                    }
                }

                ContentSizeFitter contentSizeFitter = rect.GetComponent<ContentSizeFitter>();
                if (contentSizeFitter != null)
                {
                    if (contentSizeFitter.horizontalFit != ContentSizeFitter.FitMode.Unconstrained)
                        driven |= DrivenTransformProperties.SizeDeltaX;
                    if (contentSizeFitter.verticalFit != ContentSizeFitter.FitMode.Unconstrained)
                        driven |= DrivenTransformProperties.SizeDeltaY;
                }

                AspectRatioFitter aspectRatioFitter = rect.GetComponent<AspectRatioFitter>();
                if (aspectRatioFitter != null)
                {
                    switch (aspectRatioFitter.aspectMode)
                    {
                        case AspectRatioFitter.AspectMode.HeightControlsWidth:
                            driven |= DrivenTransformProperties.SizeDeltaX;
                            break;
                        case AspectRatioFitter.AspectMode.WidthControlsHeight:
                            driven |= DrivenTransformProperties.SizeDeltaY;
                            break;
                        case AspectRatioFitter.AspectMode.FitInParent:
                        case AspectRatioFitter.AspectMode.EnvelopeParent:
                            driven |= DrivenTransformProperties.Anchors
                                | DrivenTransformProperties.AnchoredPosition
                                | DrivenTransformProperties.SizeDeltaX
                                | DrivenTransformProperties.SizeDeltaY;
                            break;
                    }
                }

                Scrollbar scrollbar = rect.GetComponentInParent<Scrollbar>(true);
                if (scrollbar != null && scrollbar.handleRect == rect)
                {
                    // Scrollbar.UpdateVisuals writes the handle's full anchor rect,
                    // including the cross-axis anchors, when the control is enabled.
                    driven |= DrivenTransformProperties.Anchors;
                }

                return driven;
            }

            public void AssertOriginalObjectsStillExist(string phase)
            {
                foreach (GameObjectIdentity identity in objects)
                {
                    Assert.That(identity.gameObject, Is.Not.Null,
                        phase + " destroyed authored object " + identity.name + ".");
                    Assert.That(identity.gameObject.GetInstanceID(), Is.EqualTo(identity.instanceId),
                        phase + " replaced authored object " + identity.name + ".");
                }
                foreach (ComponentIdentity identity in components)
                {
                    Assert.That(identity.component, Is.Not.Null,
                        phase + " destroyed authored component " + identity.description + ".");
                    Assert.That(identity.component.GetInstanceID(), Is.EqualTo(identity.instanceId),
                        phase + " replaced authored component " + identity.description + ".");
                }
            }

            public void AssertUnchanged(string phase)
            {
                AssertOriginalObjectsStillExist(phase);
                foreach (RectState rect in rects)
                    rect.AssertUnchanged(phase);
                foreach (TextStyleState text in texts)
                    text.AssertUnchanged(phase);
                foreach (ImageStyleState image in images)
                    image.AssertUnchanged(phase);
                foreach (SelectableStyleState selectable in selectables)
                    selectable.AssertUnchanged(phase);
                foreach (SerializedComponentState component in layoutComponents)
                    component.AssertUnchanged(phase);
            }
        }

        private sealed class GameObjectIdentity
        {
            public readonly GameObject gameObject;
            public readonly int instanceId;
            public readonly string name;

            public GameObjectIdentity(GameObject gameObject)
            {
                this.gameObject = gameObject;
                instanceId = gameObject.GetInstanceID();
                name = gameObject.name;
            }
        }

        private sealed class ComponentIdentity
        {
            public readonly Component component;
            public readonly int instanceId;
            public readonly string description;

            public ComponentIdentity(Component component)
            {
                this.component = component;
                instanceId = component.GetInstanceID();
                description = component.GetType().Name + " on " + component.gameObject.name;
            }
        }

        private sealed class RectState
        {
            private readonly RectTransform rect;
            private readonly Vector2 anchorMin;
            private readonly Vector2 anchorMax;
            private readonly Vector2 pivot;
            private readonly Vector3 localScale;
            private readonly Quaternion localRotation;
            private readonly Vector2 anchoredPosition;
            private readonly Vector2 sizeDelta;
            private readonly DrivenTransformProperties drivenProperties;

            public RectState(RectTransform rect, DrivenTransformProperties drivenProperties)
            {
                this.rect = rect;
                anchorMin = rect.anchorMin;
                anchorMax = rect.anchorMax;
                pivot = rect.pivot;
                localScale = rect.localScale;
                localRotation = rect.localRotation;
                anchoredPosition = rect.anchoredPosition;
                sizeDelta = rect.sizeDelta;
                this.drivenProperties = drivenProperties;
            }

            public void AssertUnchanged(string phase)
            {
                Assert.That(rect, Is.Not.Null, phase + " destroyed a fixed RectTransform.");
                string description = phase + " changed RectTransform " + rect.name + ".";
                if (!IsDriven(DrivenTransformProperties.AnchorMinX))
                    AssertFloat(rect.anchorMin.x, anchorMin.x, description + " anchorMin.x");
                if (!IsDriven(DrivenTransformProperties.AnchorMinY))
                    AssertFloat(rect.anchorMin.y, anchorMin.y, description + " anchorMin.y");
                if (!IsDriven(DrivenTransformProperties.AnchorMaxX))
                    AssertFloat(rect.anchorMax.x, anchorMax.x, description + " anchorMax.x");
                if (!IsDriven(DrivenTransformProperties.AnchorMaxY))
                    AssertFloat(rect.anchorMax.y, anchorMax.y, description + " anchorMax.y");
                AssertVector2(rect.pivot, pivot, description + " pivot");
                AssertVector3(rect.localScale, localScale, description + " localScale");
                AssertQuaternion(rect.localRotation, localRotation, description + " localRotation");
                if (!IsDriven(DrivenTransformProperties.AnchoredPositionX))
                    AssertFloat(rect.anchoredPosition.x, anchoredPosition.x, description + " position.x");
                if (!IsDriven(DrivenTransformProperties.AnchoredPositionY))
                    AssertFloat(rect.anchoredPosition.y, anchoredPosition.y, description + " position.y");
                if (!IsDriven(DrivenTransformProperties.SizeDeltaX))
                    AssertFloat(rect.sizeDelta.x, sizeDelta.x, description + " sizeDelta.x");
                if (!IsDriven(DrivenTransformProperties.SizeDeltaY))
                    AssertFloat(rect.sizeDelta.y, sizeDelta.y, description + " sizeDelta.y");
            }

            private bool IsDriven(DrivenTransformProperties property)
            {
                return (drivenProperties & property) != 0;
            }
        }

        private sealed class TextStyleState
        {
            private readonly TMP_Text text;
            private readonly TMP_FontAsset font;
            private readonly Material material;
            private readonly float fontSize;
            private readonly float fontSizeMin;
            private readonly float fontSizeMax;
            private readonly bool enableAutoSizing;
            private readonly FontStyles fontStyle;
            private readonly TextAlignmentOptions alignment;
            private readonly TextWrappingModes wrapping;
            private readonly TextOverflowModes overflow;
            private readonly Color color;
            private readonly Vector4 margin;
            private readonly bool raycastTarget;
            private readonly bool richText;
            private readonly float characterSpacing;
            private readonly float wordSpacing;
            private readonly float lineSpacing;
            private readonly float paragraphSpacing;
            private readonly bool skipStateColor;

            public TextStyleState(TMP_Text text, bool skipStateColor)
            {
                this.text = text;
                font = text.font;
                material = text.fontSharedMaterial;
                fontSize = text.fontSize;
                fontSizeMin = text.fontSizeMin;
                fontSizeMax = text.fontSizeMax;
                enableAutoSizing = text.enableAutoSizing;
                fontStyle = text.fontStyle;
                alignment = text.alignment;
                wrapping = text.textWrappingMode;
                overflow = text.overflowMode;
                color = text.color;
                margin = text.margin;
                raycastTarget = text.raycastTarget;
                richText = text.richText;
                characterSpacing = text.characterSpacing;
                wordSpacing = text.wordSpacing;
                lineSpacing = text.lineSpacing;
                paragraphSpacing = text.paragraphSpacing;
                this.skipStateColor = skipStateColor;
            }

            public void AssertUnchanged(string phase)
            {
                Assert.That(text, Is.Not.Null, phase + " destroyed authored TMP text.");
                string description = phase + " changed TMP style on " + text.name + ".";
                Assert.That(text.font, Is.SameAs(font), description + " font");
                Assert.That(text.fontSharedMaterial, Is.SameAs(material), description + " material");
                if (!enableAutoSizing)
                    Assert.That(text.fontSize, Is.EqualTo(fontSize).Within(0.001f), description + " font size");
                else
                    Assert.That(text.fontSize, Is.InRange(fontSizeMin, fontSizeMax),
                        description + " auto-sized font size must stay within its authored range");
                Assert.That(text.fontSizeMin, Is.EqualTo(fontSizeMin).Within(0.001f), description + " minimum font size");
                Assert.That(text.fontSizeMax, Is.EqualTo(fontSizeMax).Within(0.001f), description + " maximum font size");
                Assert.That(text.enableAutoSizing, Is.EqualTo(enableAutoSizing), description + " auto sizing");
                Assert.That(text.fontStyle, Is.EqualTo(fontStyle), description + " font style");
                Assert.That(text.alignment, Is.EqualTo(alignment), description + " alignment");
                Assert.That(text.textWrappingMode, Is.EqualTo(wrapping), description + " wrapping");
                Assert.That(text.overflowMode, Is.EqualTo(overflow), description + " overflow");
                if (!skipStateColor)
                    Assert.That(text.color, Is.EqualTo(color), description + " color");
                Assert.That(text.margin, Is.EqualTo(margin), description + " margin");
                Assert.That(text.raycastTarget, Is.EqualTo(raycastTarget), description + " raycast target");
                Assert.That(text.richText, Is.EqualTo(richText), description + " rich text");
                Assert.That(text.characterSpacing, Is.EqualTo(characterSpacing).Within(0.001f),
                    description + " character spacing");
                Assert.That(text.wordSpacing, Is.EqualTo(wordSpacing).Within(0.001f),
                    description + " word spacing");
                Assert.That(text.lineSpacing, Is.EqualTo(lineSpacing).Within(0.001f),
                    description + " line spacing");
                Assert.That(text.paragraphSpacing, Is.EqualTo(paragraphSpacing).Within(0.001f),
                    description + " paragraph spacing");
            }
        }

        private sealed class ImageStyleState
        {
            private readonly Image image;
            private readonly Image.Type type;
            private readonly Color color;
            private readonly bool preserveAspect;
            private readonly bool fillCenter;
            private readonly Image.FillMethod fillMethod;
            private readonly int fillOrigin;
            private readonly bool fillClockwise;
            private readonly float pixelsPerUnitMultiplier;
            private readonly bool raycastTarget;
            private readonly bool skipStateColor;

            public ImageStyleState(Image image, bool skipStateColor)
            {
                this.image = image;
                type = image.type;
                color = image.color;
                preserveAspect = image.preserveAspect;
                fillCenter = image.fillCenter;
                fillMethod = image.fillMethod;
                fillOrigin = image.fillOrigin;
                fillClockwise = image.fillClockwise;
                pixelsPerUnitMultiplier = image.pixelsPerUnitMultiplier;
                raycastTarget = image.raycastTarget;
                this.skipStateColor = skipStateColor;
            }

            public void AssertUnchanged(string phase)
            {
                Assert.That(image, Is.Not.Null, phase + " destroyed authored Image.");
                string description = phase + " changed Image style on " + image.name + ".";
                Assert.That(image.type, Is.EqualTo(type), description + " type");
                if (!skipStateColor)
                    Assert.That(image.color, Is.EqualTo(color), description + " color");
                Assert.That(image.preserveAspect, Is.EqualTo(preserveAspect), description + " preserve aspect");
                Assert.That(image.fillCenter, Is.EqualTo(fillCenter), description + " fill center");
                Assert.That(image.fillMethod, Is.EqualTo(fillMethod), description + " fill method");
                Assert.That(image.fillOrigin, Is.EqualTo(fillOrigin), description + " fill origin");
                Assert.That(image.fillClockwise, Is.EqualTo(fillClockwise), description + " fill clockwise");
                Assert.That(image.pixelsPerUnitMultiplier,
                    Is.EqualTo(pixelsPerUnitMultiplier).Within(0.001f), description + " pixels per unit");
                Assert.That(image.raycastTarget, Is.EqualTo(raycastTarget), description + " raycast target");
            }
        }

        private sealed class SelectableStyleState
        {
            private readonly Selectable selectable;
            private readonly Selectable.Transition transition;
            private readonly ColorBlock colors;

            public SelectableStyleState(Selectable selectable)
            {
                this.selectable = selectable;
                transition = selectable.transition;
                colors = selectable.colors;
            }

            public void AssertUnchanged(string phase)
            {
                Assert.That(selectable, Is.Not.Null, phase + " destroyed an authored Selectable.");
                string description = phase + " changed Selectable colors on " + selectable.name + ".";
                Assert.That(selectable.transition, Is.EqualTo(transition), description + " transition");
                ColorBlock actual = selectable.colors;
                Assert.That(actual.normalColor, Is.EqualTo(colors.normalColor), description + " normal color");
                Assert.That(actual.highlightedColor, Is.EqualTo(colors.highlightedColor),
                    description + " highlighted color");
                Assert.That(actual.pressedColor, Is.EqualTo(colors.pressedColor), description + " pressed color");
                Assert.That(actual.selectedColor, Is.EqualTo(colors.selectedColor), description + " selected color");
                Assert.That(actual.disabledColor, Is.EqualTo(colors.disabledColor), description + " disabled color");
                Assert.That(actual.colorMultiplier, Is.EqualTo(colors.colorMultiplier).Within(0.001f),
                    description + " color multiplier");
                Assert.That(actual.fadeDuration, Is.EqualTo(colors.fadeDuration).Within(0.001f),
                    description + " fade duration");
            }
        }

        private sealed class SerializedComponentState
        {
            private readonly Component component;
            private readonly string serializedState;

            public SerializedComponentState(Component component)
            {
                this.component = component;
                serializedState = EditorJsonUtility.ToJson(component);
            }

            public void AssertUnchanged(string phase)
            {
                Assert.That(component, Is.Not.Null, phase + " destroyed a layout component.");
                Assert.That(EditorJsonUtility.ToJson(component), Is.EqualTo(serializedState),
                    phase + " changed serialized layout settings on " + component.GetType().Name + ".");
            }
        }
    }

    [TestFixture]
    public sealed class WordListHubPageAccessTests
    {
        private readonly List<GameObject> ownedObjects = new List<GameObject>();
        private string temporaryLibraryRoot;

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject root in ownedObjects.Where(item => item != null).ToArray())
                UnityEngine.Object.DestroyImmediate(root);
            ownedObjects.Clear();
            if (!string.IsNullOrEmpty(temporaryLibraryRoot)
                && System.IO.Directory.Exists(temporaryLibraryRoot))
                System.IO.Directory.Delete(temporaryLibraryRoot, true);
        }

        [Test]
        public void ProductionLateUpdatePreservesSceneAuthoredCloseAndCardLayout()
        {
            GameObject root = NewObject("Static Hub layout test root", null);
            root.SetActive(false);
            GameObject cardObject = NewObject("Card", root.transform, typeof(RectTransform));
            RectTransform card = cardObject.GetComponent<RectTransform>();
            card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
            card.anchoredPosition = new Vector2(23f, -11f);
            card.sizeDelta = new Vector2(1580f, 780f);
            card.localScale = new Vector3(0.91f, 0.93f, 1f);

            GameObject closeObject = NewObject("ProductionCloseHubButton", root.transform,
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            RectTransform close = closeObject.GetComponent<RectTransform>();
            close.anchorMin = close.anchorMax = new Vector2(0.5f, 0.5f);
            close.pivot = new Vector2(0.25f, 0.75f);
            close.anchoredPosition = new Vector2(747f, 427f);
            close.sizeDelta = new Vector2(52f, 38f);
            close.localScale = new Vector3(0.9f, 1.1f, 1f);

            WordListHubSceneBindings bindings = NewObject("Hub bindings", root.transform,
                typeof(WordListHubSceneBindings)).GetComponent<WordListHubSceneBindings>();
            bindings.card = card;
            bindings.hostRoot = root;
            bindings.closeHub = new WordListHubSceneBindings.ButtonBinding
            {
                button = closeObject.GetComponent<Button>()
            };
            WordListHubMockupController controller = NewObject("Hub controller", root.transform)
                .AddComponent<WordListHubMockupController>();
            SetPrivateField(controller, "productionMode", true);
            SetPrivateField(controller, "sceneBindings", bindings);

            Vector2 closePosition = close.anchoredPosition;
            Vector2 closeSize = close.sizeDelta;
            Vector2 closeAnchorMin = close.anchorMin;
            Vector2 closeAnchorMax = close.anchorMax;
            Vector2 closePivot = close.pivot;
            Vector3 closeScale = close.localScale;
            Vector2 cardPosition = card.anchoredPosition;
            Vector2 cardSize = card.sizeDelta;
            Vector3 cardScale = card.localScale;

            InvokePrivate(controller, "LateUpdate");

            Assert.That(close.anchoredPosition, Is.EqualTo(closePosition));
            Assert.That(close.sizeDelta, Is.EqualTo(closeSize));
            Assert.That(close.anchorMin, Is.EqualTo(closeAnchorMin));
            Assert.That(close.anchorMax, Is.EqualTo(closeAnchorMax));
            Assert.That(close.pivot, Is.EqualTo(closePivot));
            Assert.That(close.localScale, Is.EqualTo(closeScale));
            Assert.That(card.anchoredPosition, Is.EqualTo(cardPosition));
            Assert.That(card.sizeDelta, Is.EqualTo(cardSize));
            Assert.That(card.localScale, Is.EqualTo(cardScale));
        }

        [Test]
        public void EditFormHidesTagsWithoutClearingDraftAndKeepsAuthoredWordsInputRect()
        {
            GameObject formRoot = NewObject("Edit form", null);
            GameObject tagsInputObject = NewObject("Edit tags input", formRoot.transform,
                typeof(RectTransform), typeof(CanvasRenderer), typeof(TMP_InputField));
            TMP_InputField tagsInput = tagsInputObject.GetComponent<TMP_InputField>();
            GameObject tagsLabel = NewObject("EditListFormTagsLabel", formRoot.transform);
            GameObject wordsInputObject = NewObject("Edit words input", formRoot.transform,
                typeof(RectTransform), typeof(CanvasRenderer), typeof(TMP_InputField));
            TMP_InputField wordsInput = wordsInputObject.GetComponent<TMP_InputField>();
            wordsInput.textComponent = NewText("Edit words text", wordsInputObject.transform);
            RectTransform wordsRect = wordsInput.GetComponent<RectTransform>();
            wordsRect.anchorMin = new Vector2(0.25f, 0.75f);
            wordsRect.anchorMax = new Vector2(0.75f, 0.75f);
            wordsRect.pivot = new Vector2(0.2f, 0.8f);
            wordsRect.anchoredPosition = new Vector2(13f, -29f);
            wordsRect.sizeDelta = new Vector2(417f, 51f);
            Vector2 expectedPosition = wordsRect.anchoredPosition;
            Vector2 expectedSize = wordsRect.sizeDelta;
            GameObject cancelObject = NewObject("Edit cancel button", formRoot.transform,
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            Button cancelButton = cancelObject.GetComponent<Button>();
            NewText("Edit cancel label", cancelObject.transform, "Cancel");
            WordListHubSceneBindings.FormBinding form = new WordListHubSceneBindings.FormBinding
            {
                root = formRoot,
                tagsInput = tagsInput,
                wordsInput = wordsInput,
                cancelButton = cancelButton
            };

            GameObject controllerObject = NewObject("Edit form controller", null);
            controllerObject.SetActive(false);
            WordListHubMockupController controller = controllerObject.AddComponent<WordListHubMockupController>();
            SetPrivateField(controller, "editTagsInput", tagsInput);
            SetPrivateField(controller, "editDraft", new MockupWordListEdit
            {
                Tags = "kept, tags",
                Words = new List<string> { "apple" }
            });

            InvokePrivate(controller, "HideEditTagsControls", form);
            InvokePrivate(controller, "CaptureEditInputs");

            Assert.That(tagsInputObject.activeSelf, Is.False);
            Assert.That(tagsLabel.activeSelf, Is.False);
            Assert.That(GetPrivateField(controller, "editTagsInput"), Is.Null);
            Assert.That(((MockupWordListEdit)GetPrivateField(controller, "editDraft")).Tags,
                Is.EqualTo("kept, tags"));

            using (var view = new MockupWordListEditorView(form, null, null, null, null))
            {
                Assert.That(wordsRect.anchoredPosition, Is.EqualTo(expectedPosition));
                Assert.That(wordsRect.sizeDelta, Is.EqualTo(expectedSize));
                view.Render(new MockupWordListEditSession(new[] { "apple", "pear" }), "Enter a word", false);
                Assert.That(wordsRect.anchoredPosition, Is.EqualTo(expectedPosition));
                Assert.That(wordsRect.sizeDelta, Is.EqualTo(expectedSize));
            }

            Assert.That(wordsRect.anchoredPosition, Is.EqualTo(expectedPosition));
            Assert.That(wordsRect.sizeDelta, Is.EqualTo(expectedSize));
        }

        [Test]
        public void NewListHidesTagsAndEditWordAddButtonReadsNewWord()
        {
            GameObject newFormRoot = NewObject("New list form", null, typeof(RectTransform));
            GameObject newTagsInputObject = NewObject("New list tags input", newFormRoot.transform,
                typeof(RectTransform), typeof(CanvasRenderer), typeof(TMP_InputField));
            GameObject newTagsLabel = NewObject("NewListFormTagsLabel", newFormRoot.transform);
            var newForm = new WordListHubSceneBindings.FormBinding
            {
                root = newFormRoot,
                tagsInput = newTagsInputObject.GetComponent<TMP_InputField>()
            };
            InvokeStaticPrivate(typeof(WordListHubMockupController), "HideNewListTagsControls", newForm);
            Assert.That(newTagsInputObject.activeSelf, Is.False);
            Assert.That(newTagsLabel.activeSelf, Is.False);
            Assert.That(typeof(WordListHubMockupController).GetField(
                "newListTagsInput", BindingFlags.Instance | BindingFlags.NonPublic), Is.Null,
                "The New List form should no longer read its retired Tags input.");

            GameObject editRoot = NewObject("Edit word form", null, typeof(RectTransform));
            GameObject inputObject = NewObject("Edit words input", editRoot.transform,
                typeof(RectTransform), typeof(CanvasRenderer), typeof(TMP_InputField));
            TMP_InputField input = inputObject.GetComponent<TMP_InputField>();
            input.textComponent = NewText("Edit words text", inputObject.transform);
            TMP_FontAsset highwayFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                AssetDatabase.GUIDToAssetPath("dc36b3fdc14f47ebb36fd484a67e268a"));
            TMP_FontAsset wordFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                AssetDatabase.GUIDToAssetPath("790db52db469a9641a494c319808aed3"));
            TMP_FontAsset symbolFallbackFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                AssetDatabase.GUIDToAssetPath("401fd8374cb8f0747b71fbd498c78d4e"));
            Assert.That(highwayFont, Is.Not.Null, "The EHS action font should be available to the fixture.");
            Assert.That(wordFont, Is.Not.Null, "The CJK data font should be available to the fixture.");
            Assert.That(symbolFallbackFont, Is.Not.Null, "The Noto Primary fallback font should be available to the fixture.");
            CollectionAssert.Contains(highwayFont.fallbackFontAssetTable, symbolFallbackFont,
                "The EHS action font needs the Noto fallback for arrow button labels.");
            input.textComponent.font = wordFont;
            input.textComponent.fontSharedMaterial = wordFont.material;
            GameObject cancelObject = NewObject("Edit cancel", editRoot.transform,
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            Button cancelButton = cancelObject.GetComponent<Button>();
            cancelButton.targetGraphic = cancelObject.GetComponent<Image>();
            TMP_Text cancelLabel = NewText("Edit cancel label", cancelObject.transform, "Cancel");
            cancelLabel.font = highwayFont;
            cancelLabel.fontSharedMaterial = highwayFont.material;
            var editForm = new WordListHubSceneBindings.FormBinding
            {
                root = editRoot,
                wordsInput = input,
                cancelButton = cancelButton
            };

            using (var view = new MockupWordListEditorView(editForm, null, null, null, null))
            {
                TMP_Text addLabel = editForm.wordEditorLayout.addButton.GetComponentInChildren<TMP_Text>(true);
                Assert.That(addLabel, Is.Not.Null);
                Assert.That(addLabel.text, Is.EqualTo("New Word"));
                Assert.That(addLabel.font, Is.SameAs(highwayFont));
                Assert.That(addLabel.fontSharedMaterial, Is.SameAs(highwayFont.material));

                view.Render(new MockupWordListEditSession(new[] { "测试" }), "Enter a word", false);

                foreach (string actionName in new[] { "MoveWordUp", "MoveWordDown", "DeleteWord", "AddWord" })
                {
                    TMP_Text actionLabel = editForm.wordEditorLayout.root.transform
                        .Find(actionName).GetComponentInChildren<TMP_Text>(true);
                    Assert.That(actionLabel, Is.Not.Null, actionName + " should have a label.");
                    Assert.That(actionLabel.font, Is.SameAs(highwayFont), actionName + " should use the action font.");
                    Assert.That(actionLabel.fontSharedMaterial, Is.SameAs(highwayFont.material),
                        actionName + " should use the action material.");
                }

                TMP_Text generatedWordLabel = editForm.wordEditorLayout.content
                    .GetComponentInChildren<TMP_Text>(true);
                Assert.That(generatedWordLabel.text, Is.EqualTo("测试"));
                Assert.That(generatedWordLabel.font, Is.SameAs(wordFont),
                    "A generated fallback row must keep the user-word font instead of inheriting the action font.");
                Assert.That(generatedWordLabel.fontSharedMaterial, Is.SameAs(wordFont.material));
                Assert.That(editForm.wordEditorLayout.emptyNotice.font, Is.SameAs(highwayFont),
                    "The short empty-state notice should use the UI chrome font.");
                Assert.That(editForm.wordEditorLayout.emptyNotice.fontSharedMaterial, Is.SameAs(highwayFont.material));
                Assert.That(input.textComponent.font, Is.SameAs(wordFont),
                    "The input field must retain its authored data font.");
                Assert.That(input.textComponent.fontSharedMaterial, Is.SameAs(wordFont.material));
            }

            using (var authoredRowsView = new MockupWordListEditorView(editForm, null, null, null, null))
            {
                authoredRowsView.Render(
                    new MockupWordListEditSession(new[] { "测试", "词语" }), "Enter a word", false);

                TMP_Text[] authoredRowLabels = editForm.wordEditorLayout.content
                    .GetComponentsInChildren<TMP_Text>(true)
                    .Where(text => text.gameObject.name == "WordLabel")
                    .ToArray();
                Assert.That(authoredRowLabels, Has.Length.EqualTo(2));
                foreach (TMP_Text rowLabel in authoredRowLabels)
                {
                    Assert.That(rowLabel.font, Is.SameAs(wordFont),
                        "Authored/cloned word rows should preserve their data font when action labels use EHS.");
                    Assert.That(rowLabel.fontSharedMaterial, Is.SameAs(wordFont.material));
                }
            }
        }

        [Test]
        public void ActionFeedbackUsesNonBlockingToastAndConfiguredDuration()
        {
            GameObject hostRoot = NewObject("Feedback host", null, typeof(RectTransform), typeof(Canvas));
            WordListHubSceneBindings bindings = NewObject("Feedback bindings", hostRoot.transform,
                typeof(WordListHubSceneBindings)).GetComponent<WordListHubSceneBindings>();
            bindings.hostRoot = hostRoot;
            bindings.library = new WordListHubSceneBindings.LibraryPageBinding
            {
                detailStatus = NewText("Toast style source", hostRoot.transform)
            };
            bindings.closeHub = CreateButtonBinding("Toast close style", hostRoot.transform, "Close");

            GameObject controllerObject = NewObject("Toast controller", hostRoot.transform);
            controllerObject.SetActive(false);
            WordListHubMockupController controller = controllerObject.AddComponent<WordListHubMockupController>();
            SetPrivateField(controller, "sceneBindings", bindings);
            SetPrivateField(controller, "operationFeedbackDurationSeconds", 3f);

            InvokePrivate(controller, "ShowActionFeedback", "Saved the list.", false);
            WordListHubFeedbackView view = hostRoot.GetComponentInChildren<WordListHubFeedbackView>(true);
            Assert.That(view, Is.Not.Null);
            Assert.That(view.IsVisible, Is.True);
            Assert.That(view.MessageText.text, Is.EqualTo("Saved the list."));
            Assert.That(view.RemainingSeconds, Is.EqualTo(3f).Within(0.01f));
            Assert.That(bindings.library.detailStatus.text, Is.Empty,
                "Action feedback should not be written into the static detail status field.");

            view.TickUnscaled(2.9f);
            Assert.That(view.IsVisible, Is.True);
            view.TickUnscaled(0.2f);
            Assert.That(view.IsVisible, Is.False);

            InvokePrivate(controller, "ShowActionFeedback", "Saved the list.", true);
            Assert.That(view.IsVisible, Is.True);
            view.CloseButton.onClick.Invoke();
            Assert.That(view.IsVisible, Is.False,
                "The toast's close button should dismiss it without a modal overlay.");
        }

        [Test]
        public void LanguageFieldsAreReadOnlyAndDetectLettersIgnoringPinyinDigitsAndPunctuation()
        {
            GameObject root = NewObject("Auto language UI test", null);
            root.SetActive(false);
            WordListHubSceneBindings bindings = NewObject("Bindings", root.transform,
                typeof(WordListHubSceneBindings)).GetComponent<WordListHubSceneBindings>();
            bindings.modal = new WordListHubSceneBindings.ModalBinding();

            GameObject newFormRoot = NewObject("New list form", root.transform);
            TMP_Dropdown newLanguage = NewObject("New language", newFormRoot.transform,
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(TMP_Dropdown))
                .GetComponent<TMP_Dropdown>();
            TMP_Text newValidation = NewText("New validation", newFormRoot.transform);
            bindings.modal.newList.root = newFormRoot;
            bindings.modal.newList.languageDropdown = newLanguage;
            bindings.modal.newList.validationText = newValidation;

            GameObject editFormRoot = NewObject("Edit list form", root.transform);
            TMP_Dropdown editLanguage = NewObject("Edit language", editFormRoot.transform,
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(TMP_Dropdown))
                .GetComponent<TMP_Dropdown>();
            bindings.modal.editList.root = editFormRoot;
            bindings.modal.editList.languageDropdown = editLanguage;

            GameObject controllerObject = NewObject("Controller", root.transform);
            WordListHubMockupController controller = controllerObject.AddComponent<WordListHubMockupController>();
            SetPrivateField(controller, "sceneBindings", bindings);
            SetPrivateField(controller, "dialogValidationText", newValidation);
            SetPrivateField(controller, "newListLanguageFallback", MockupLanguage.English);

            InvokePrivate(controller, "HandleNewListWordsChanged", "123 !!!");
            Assert.That(newLanguage.interactable, Is.False);
            Assert.That(newLanguage.value, Is.EqualTo(0), "No letters use the New List English fallback.");

            InvokePrivate(controller, "HandleNewListWordsChanged", "苹果 | ping guo");
            Assert.That(newLanguage.value, Is.EqualTo(1), "Pinyin annotation Latin letters must not force Mixed.");
            InvokePrivate(controller, "HandleNewListWordsChanged", "tea 123 苹果 | ping guo!");
            Assert.That(newLanguage.value, Is.EqualTo(2));
            InvokePrivate(controller, "HandleNewListWordsChanged", "こんにちは");
            Assert.That(newValidation.text, Does.Contain("Only English (Latin) and Chinese (Han)"));

            var editDraft = new MockupWordListEdit
            {
                Language = MockupLanguage.Mixed,
                Words = new List<string> { "12.! | a b" }
            };
            SetPrivateField(controller, "editDraft", editDraft);
            SetPrivateField(controller, "editLanguageFallback", MockupLanguage.Mixed);
            SetPrivateField(controller, "editLanguage", MockupLanguage.Mixed);
            SetPrivateField(controller, "editWordSession", new MockupWordListEditSession(editDraft.Words));
            Assert.That(InvokePrivate(controller, "UpdateEditLanguageDisplay", false), Is.True);
            Assert.That(editLanguage.interactable, Is.False);
            Assert.That(editLanguage.value, Is.EqualTo(2), "Neutral-only edit rows keep the stored fallback.");

            SetPrivateField(controller, "editWordSession",
                new MockupWordListEditSession(new[] { "你 | ni" }));
            Assert.That(InvokePrivate(controller, "UpdateEditLanguageDisplay", false), Is.True);
            Assert.That(editLanguage.value, Is.EqualTo(1));
            SetPrivateField(controller, "editWordSession",
                new MockupWordListEditSession(new[] { "tea 你 | ni" }));
            Assert.That(InvokePrivate(controller, "UpdateEditLanguageDisplay", false), Is.True);
            Assert.That(editLanguage.value, Is.EqualTo(2));
        }

        [Test]
        public void ProductionBridgeAwakeHidesActiveHostsWithoutBlockingLaterShow()
        {
            Type bridgeType = GetLoadedType("WordListHubGameBridge");

            GameObject activePanel = NewObject("MainPanel active", null);
            activePanel.SetActive(false);
            GameObject activeHost = NewObject("Renamed Hub host", activePanel.transform);
            activeHost.SetActive(true);
            WordListHubSceneBindings activeBindings = NewObject("Active bindings", activePanel.transform,
                typeof(WordListHubSceneBindings)).GetComponent<WordListHubSceneBindings>();
            activeBindings.hostRoot = activeHost;
            WordListHubMockupController activeController = activePanel.AddComponent<WordListHubMockupController>();
            SetPrivateField(activeController, "productionMode", true);
            SetPrivateField(activeController, "sceneBindings", activeBindings);
            int visibilityEvents = 0;
            activeController.HubVisibilityChanged += _ => visibilityEvents++;

            activePanel.SetActive(true);
            Assert.That(activeHost.activeSelf, Is.True,
                "The fixture starts with the scene-authored host enabled, as an editor-active scene can.");

            GameObject bridgeHost = NewObject("Independent bridge host", null);
            bridgeHost.SetActive(false);
            Component bridge = bridgeHost.AddComponent(bridgeType);
            SetPrivateField(bridge, "hubController", activeController);
            bridgeHost.SetActive(true);
            // Plain runtime MonoBehaviours do not receive Awake from an EditMode fixture.
            InvokePrivate(bridge, "Awake");

            Assert.That(activeHost.activeSelf, Is.False,
                "Bridge Awake must hide the serialized host reference even when it began active.");
            Assert.That(visibilityEvents, Is.Zero,
                "Normalizing an already logically hidden Hub must not raise an input/timer gate event.");

            activeController.ShowHub();
            Assert.That(activeHost.activeSelf, Is.True);
            Assert.That(visibilityEvents, Is.EqualTo(1),
                "The first explicit show should still raise exactly one visibility event.");

            GameObject inactivePanel = NewObject("MainPanel inactive", null);
            inactivePanel.SetActive(false);
            GameObject inactiveHost = NewObject("Inactive renamed host", inactivePanel.transform);
            inactiveHost.SetActive(true);
            WordListHubSceneBindings inactiveBindings = NewObject("Inactive bindings", inactivePanel.transform,
                typeof(WordListHubSceneBindings)).GetComponent<WordListHubSceneBindings>();
            inactiveBindings.hostRoot = inactiveHost;
            WordListHubMockupController inactiveController = inactivePanel.AddComponent<WordListHubMockupController>();
            SetPrivateField(inactiveController, "productionMode", true);
            SetPrivateField(inactiveController, "sceneBindings", inactiveBindings);
            int inactiveVisibilityEvents = 0;
            inactiveController.HubVisibilityChanged += _ => inactiveVisibilityEvents++;

            GameObject inactiveBridgeHost = NewObject("Second independent bridge host", null);
            inactiveBridgeHost.SetActive(false);
            Component inactiveBridge = inactiveBridgeHost.AddComponent(bridgeType);
            SetPrivateField(inactiveBridge, "hubController", inactiveController);
            inactiveBridgeHost.SetActive(true);
            InvokePrivate(inactiveBridge, "Awake");

            Assert.That(inactiveHost.activeSelf, Is.False,
                "An inactive controller can still hide its assigned host before its own Awake runs.");
            Assert.That(inactiveVisibilityEvents, Is.Zero);
            inactiveController.ShowHub();
            Assert.That(inactiveHost.activeSelf, Is.True);
            Assert.That(inactiveVisibilityEvents, Is.EqualTo(1));
        }

        [Test]
        public void ProductionDetailRefreshPreservesFixedSceneCaptionsAndUpdatesDynamicLabels()
        {
            GameObject root = NewObject("Static button label test root", null);
            root.SetActive(false);
            var bindings = NewObject("Hub bindings", root.transform,
                typeof(WordListHubSceneBindings)).GetComponent<WordListHubSceneBindings>();
            bindings.library = new WordListHubSceneBindings.LibraryPageBinding
            {
                delete = CreateButtonBinding("Delete", root.transform, "Scene Delete"),
                play = CreateButtonBinding("Play", root.transform, "Scene Play"),
                edit = CreateButtonBinding("Edit", root.transform, "Scene Edit"),
                copyEditWords = CreateButtonBinding("Copy", root.transform, "Scene Copy"),
                publish = CreateButtonBinding("Publish", root.transform, "Scene Publish"),
                exportList = CreateButtonBinding("Export", root.transform, "Export my words")
            };
            bindings.community = new WordListHubSceneBindings.CommunityPageBinding
            {
                like = CreateButtonBinding("Like", root.transform, "Scene Like"),
                download = CreateButtonBinding("Download", root.transform, "Scene Download"),
                play = CreateButtonBinding("Community play", root.transform, "Community Play"),
                report = CreateButtonBinding("Community report", root.transform, "Community Report")
            };

            WordListHubMockupController controller = NewObject("Hub controller", root.transform)
                .AddComponent<WordListHubMockupController>();
            MockupWordListStore backend = MockupWordListStore.CreateWithFixtures();
            SetPrivateField(controller, "sceneBindings", bindings);
            SetPrivateField(controller, "backend", backend);
            SetPrivateField(controller, "productionMode", true);
            SetPrivateField(controller, "selectedListId", "mock-list-shop-signs");
            SetPrivateField(controller, "selectedDiscoveryId", "mock-ugc-weekend-garden");

            MockupWordList localItem = backend.FindById("mock-list-shop-signs");
            InvokePrivate(controller, "UpdateLibraryDetail", new List<MockupWordList> { localItem });
            Assert.That(bindings.library.delete.label.text, Is.EqualTo("Scene Delete"));
            Assert.That(bindings.library.play.label.text, Is.EqualTo("Scene Play"));
            Assert.That(bindings.library.edit.label.text, Is.EqualTo("Scene Edit"));
            Assert.That(bindings.library.copyEditWords.label.text, Is.EqualTo("Scene Copy"));
            Assert.That(bindings.library.exportList.label.text, Is.EqualTo("Export my words"));
            Assert.That(bindings.library.publish.label.text, Is.EqualTo("Publish"),
                "An unpublished list shows the current publication action.");

            MockupDiscoveryItem communityItem = backend.FindDiscoveryItem("mock-ugc-weekend-garden");
            InvokePrivate(controller, "UpdateCommunityDetail",
                new List<MockupDiscoveryItem> { communityItem });
            Assert.That(bindings.community.play.label.text, Is.EqualTo("Community Play"));
            Assert.That(bindings.community.report.label.text, Is.EqualTo("Community Report"));
            Assert.That(bindings.community.like.label.text, Does.Contain("Like"));
            Assert.That(bindings.community.download.label.text, Is.EqualTo("Download"));

            SetPrivateField(controller, "productionMode", false);
            InvokePrivate(controller, "UpdateLibraryDetail", new List<MockupWordList> { localItem });
            Assert.That(bindings.library.play.label.text, Is.EqualTo("Play"));
            Assert.That(bindings.library.exportList.label.text, Is.EqualTo("Export entries"));
            InvokePrivate(controller, "UpdateCommunityDetail",
                new List<MockupDiscoveryItem> { communityItem });
            Assert.That(bindings.community.play.label.text, Is.EqualTo("Play"));
            Assert.That(bindings.community.report.label.text, Is.EqualTo("Report & hide for me"));
        }

        [Test]
        public void CustomRunCompletionDoesNotOpenHubOrEmitVisibilityChange()
        {
            GameObject hubRoot = NewObject("Custom completion Hub", null);
            hubRoot.SetActive(false);
            GameObject hostRoot = NewObject("Hub host", hubRoot.transform);
            hostRoot.SetActive(false);
            WordListHubSceneBindings bindings = NewObject("Hub bindings", hubRoot.transform,
                typeof(WordListHubSceneBindings)).GetComponent<WordListHubSceneBindings>();
            bindings.hostRoot = hostRoot;
            WordListHubMockupController controller = hubRoot.AddComponent<WordListHubMockupController>();
            SetPrivateField(controller, "sceneBindings", bindings);
            int visibilityEvents = 0;
            controller.HubVisibilityChanged += _ => visibilityEvents++;

            GameObject bridgeRoot = NewObject("Game bridge", null);
            bridgeRoot.SetActive(false);
            Type bridgeType = GetLoadedType("WordListHubGameBridge");
            Component bridge = bridgeRoot.AddComponent(bridgeType);
            SetPrivateField(bridge, "hubController", controller);

            Type snapshotType = GetLoadedType("GameRunCompletionSnapshot");
            ConstructorInfo snapshotConstructor = snapshotType.GetConstructors()
                .Single(candidate => candidate.GetParameters().Length == 15);
            ParameterInfo[] parameters = snapshotConstructor.GetParameters();
            object[] arguments = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                Type parameterType = parameters[i].ParameterType;
                if (parameterType == typeof(string))
                    arguments[i] = string.Empty;
                else if (parameterType.IsEnum)
                    arguments[i] = Enum.ToObject(parameterType, 0);
                else if (parameterType.IsValueType)
                    arguments[i] = Activator.CreateInstance(parameterType);
            }
            arguments[0] = "custom-list-id";
            object snapshot = snapshotConstructor.Invoke(arguments);

            InvokePrivate(bridge, "HandleRunCompleted", snapshot);

            Assert.That(hostRoot.activeSelf, Is.False);
            Assert.That(visibilityEvents, Is.Zero,
                "Custom completion must leave the existing result-panel flow in place without opening the Hub.");
        }

        [Test]
        public void ConsolidatedLibraryFiltersKeepLegacyDropdownsHidden()
        {
            GameObject root = NewObject("Consolidated filter test root", null);
            root.SetActive(false);
            WordListHubSceneBindings bindings = NewObject("Consolidated filter bindings", root.transform,
                typeof(WordListHubSceneBindings)).GetComponent<WordListHubSceneBindings>();
            ConfigurePageBindings(root.transform, bindings);

            WordListHubMockupController controller = NewObject("Consolidated filter controller", root.transform)
                .AddComponent<WordListHubMockupController>();
            SetPrivateField(controller, "sceneBindings", bindings);
            SetPrivateField(controller, "productionMode", true);
            SetPrivateField(controller, "backend", new PageAccessStore { LibraryUnlocked = true });
            SetPrivateField(controller, "query", new MockupLibraryQuery());

            InvokePrivate(controller, "RestoreLibraryPageControls");
            InvokePrivate(controller, "SyncLibraryControlValues", bindings.library);

            Assert.That(bindings.library.filtersMenu.toggleButton.gameObject.activeSelf, Is.True);
            Assert.That(bindings.library.languageFilter.gameObject.activeSelf, Is.False);
            Assert.That(bindings.library.playedFilter.gameObject.activeSelf, Is.False);
            Assert.That(bindings.library.sortFilter.gameObject.activeSelf, Is.False);
            Assert.That(bindings.library.directionFilter.gameObject.activeSelf, Is.False);
            Assert.That(bindings.library.languageFilter.options, Is.Empty,
                "The consolidated menu owns the filter state; legacy dropdowns are not repopulated or rebound.");

            GameObject previewRoot = NewObject("Legacy preview root", null);
            previewRoot.SetActive(false);
            WordListHubSceneBindings previewBindings = NewObject("Legacy preview bindings", previewRoot.transform,
                typeof(WordListHubSceneBindings)).GetComponent<WordListHubSceneBindings>();
            previewBindings.library = new WordListHubSceneBindings.LibraryPageBinding
            {
                languageFilter = NewObject("Preview language filter", previewRoot.transform,
                    typeof(TMP_Dropdown)).GetComponent<TMP_Dropdown>()
            };
            WordListHubMockupController previewController = NewObject("Legacy preview controller",
                previewRoot.transform).AddComponent<WordListHubMockupController>();
            SetPrivateField(previewController, "sceneBindings", previewBindings);
            SetPrivateField(previewController, "backend", new PageAccessStore { LibraryUnlocked = true });
            InvokePrivate(previewController, "RestoreLibraryPageControls");
            Assert.That(previewBindings.library.languageFilter.gameObject.activeSelf, Is.True,
                "Legacy preview scenes without a consolidated menu keep their fallback dropdown.");
        }

        [Test]
        public void PublishAndAgreementActionsRequireCommunityUnlockAcrossPublicationStates()
        {
            GameObject root = NewObject("Publication access test", null,
                typeof(RectTransform), typeof(Canvas));
            root.SetActive(false);
            GameObject toastHost = NewObject("Publication toast host", null,
                typeof(RectTransform), typeof(Canvas));
            WordListHubSceneBindings bindings = NewObject("Hub bindings", root.transform,
                typeof(WordListHubSceneBindings)).GetComponent<WordListHubSceneBindings>();
            bindings.hostRoot = toastHost;
            bindings.library = new WordListHubSceneBindings.LibraryPageBinding
            {
                detailTitle = NewText("Detail title", root.transform),
                detailDescription = NewText("Detail description", root.transform),
                detailMetadata = NewText("Detail metadata", root.transform),
                detailWordsLabel = NewText("Detail words label", root.transform),
                detailWords = NewText("Detail words", root.transform),
                detailStatus = NewText("Detail status", root.transform),
                publish = CreateButtonBinding("Publish", root.transform, "Publish"),
                unpublish = CreateButtonBinding("Open agreement", root.transform, "Open agreement")
            };

            PageAccessStore backend = new PageAccessStore { LibraryUnlocked = true };
            FakeCommunityAsync community = new FakeCommunityAsync { IsRemoteAuthenticated = true };
            MockupWordList item = backend.FindById("mock-list-shop-signs");
            Assert.That(item, Is.Not.Null);
            Assert.That(item.IsReadOnly, Is.False);
            Assert.That(item.Ownership, Is.EqualTo(MockupOwnership.Owned));

            WordListHubMockupController controller = NewObject("Hub controller", root.transform)
                .AddComponent<WordListHubMockupController>();
            SetPrivateField(controller, "sceneBindings", bindings);
            SetPrivateField(controller, "productionMode", true);
            SetPrivateField(controller, "backend", backend);
            SetPrivateField(controller, "communityAsync", community);
            SetPrivateField(controller, "selectedListId", item.Id);

            MockupCommunityPublicationState[] states =
            {
                MockupCommunityPublicationState.Unpublished,
                MockupCommunityPublicationState.Published,
                MockupCommunityPublicationState.Submitting,
                MockupCommunityPublicationState.NeedsAgreement,
                MockupCommunityPublicationState.Failed,
                MockupCommunityPublicationState.RecoveryRequired
            };
            foreach (MockupCommunityPublicationState state in states)
            {
                bool published = state == MockupCommunityPublicationState.Published;
                SetProperty(item, nameof(MockupWordList.IsPubliclyVisible), published);
                SetProperty(item, nameof(MockupWordList.PublicationStatus), published
                    ? MockupPublicationStatus.Published : MockupPublicationStatus.NotPublished);
                bool needsAgreement = state == MockupCommunityPublicationState.NeedsAgreement;
                community.Publications = new[]
                {
                    new MockupCommunityPublicationInfo(item.Id, item.Title,
                        state == MockupCommunityPublicationState.Unpublished ? string.Empty : "remote-item",
                        "hash", state, needsAgreement, string.Empty)
                };

                backend.CommunityUnlocked = false;
                InvokePrivate(controller, "UpdateLibraryDetail", new List<MockupWordList> { item });
                Assert.That(bindings.library.publish.button.gameObject.activeSelf, Is.False,
                    "Publish, unpublish, and status-refresh actions must stay hidden while Community is locked: " + state);
                Assert.That(bindings.library.unpublish.button.gameObject.activeSelf, Is.False,
                    "The agreement action must stay hidden while Community is locked: " + state);

                backend.CommunityUnlocked = true;
                InvokePrivate(controller, "UpdateLibraryDetail", new List<MockupWordList> { item });
                Assert.That(bindings.library.publish.button.gameObject.activeSelf, Is.True,
                    "Unlocking Community should restore the valid publication action: " + state);
                Assert.That(bindings.library.publish.button.interactable, Is.True);
                Assert.That(bindings.library.publish.label.text,
                    Is.EqualTo(published ? "Unpublish"
                        : state == MockupCommunityPublicationState.Unpublished ? "Publish" : "Refresh status"));
                Assert.That(bindings.library.unpublish.button.gameObject.activeSelf, Is.EqualTo(needsAgreement),
                    "Only an agreement-needed publication should expose the Workshop agreement action.");
            }

            community.IsRemoteAuthenticated = false;
            SetProperty(item, nameof(MockupWordList.IsPubliclyVisible), false);
            SetProperty(item, nameof(MockupWordList.PublicationStatus), MockupPublicationStatus.NotPublished);
            community.Publications = new[]
            {
                new MockupCommunityPublicationInfo(item.Id, item.Title, string.Empty,
                    "hash", MockupCommunityPublicationState.Unpublished, false, string.Empty)
            };
            InvokePrivate(controller, "UpdateLibraryDetail", new List<MockupWordList> { item });
            Assert.That(bindings.library.publish.button.gameObject.activeSelf, Is.True,
                "An unlocked owned list keeps its Publish action visible while Steam is signed out.");
            bindings.library.publish.button.onClick.Invoke();
            Assert.That(community.PublishCalls, Is.Zero,
                "An unauthenticated Publish click reports locally without making a remote call.");

            SetProperty(item, nameof(MockupWordList.IsPubliclyVisible), true);
            SetProperty(item, nameof(MockupWordList.PublicationStatus), MockupPublicationStatus.Published);
            community.Publications = new[]
            {
                new MockupCommunityPublicationInfo(item.Id, item.Title, "remote-item",
                    "hash", MockupCommunityPublicationState.Published, false, string.Empty)
            };
            InvokePrivate(controller, "UpdateLibraryDetail", new List<MockupWordList> { item });
            Assert.That(bindings.library.publish.button.gameObject.activeSelf, Is.True,
                "An unlocked published list keeps its Unpublish action visible while Steam is signed out.");
            bindings.library.publish.button.onClick.Invoke();
            Assert.That(community.UnpublishCalls, Is.Zero,
                "An unauthenticated Unpublish click reports locally without making a remote call.");

            community.Publications = new[]
            {
                new MockupCommunityPublicationInfo(item.Id, item.Title, "remote-item",
                    "hash", MockupCommunityPublicationState.NeedsAgreement, true, string.Empty)
            };
            InvokePrivate(controller, "UpdateLibraryDetail", new List<MockupWordList> { item });
            Assert.That(bindings.library.unpublish.button.gameObject.activeSelf, Is.True,
                "The agreement action is visible when needed, even before Steam sign-in.");
            bindings.library.unpublish.button.onClick.Invoke();
            Assert.That(community.ItemPageCalls, Is.Zero,
                "An unauthenticated agreement-page click reports locally without making a remote call.");

            community.LocalPublicationLocked = true;
            SetProperty(item, nameof(MockupWordList.IsPubliclyVisible), false);
            SetProperty(item, nameof(MockupWordList.PublicationStatus), MockupPublicationStatus.NotPublished);
            community.Publications = new[]
            {
                new MockupCommunityPublicationInfo(item.Id, item.Title, string.Empty,
                    "hash", MockupCommunityPublicationState.RecoveryRequired, false, string.Empty)
            };
            InvokePrivate(controller, "UpdateLibraryDetail", new List<MockupWordList> { item });
            Assert.That(bindings.library.publish.button.gameObject.activeSelf, Is.True,
                "A locked local publication with no remote ID still exposes the Publish action for recovery feedback.");
            Assert.That(bindings.library.publish.label.text, Is.EqualTo("Publish"));
            bindings.library.publish.button.onClick.Invoke();
            Assert.That(community.PublishCalls, Is.Zero,
                "A locally locked item must not issue a duplicate Publish request.");
            WordListHubFeedbackView feedback = GetPrivateField(controller, "operationFeedbackView")
                as WordListHubFeedbackView;
            Assert.That(feedback, Is.Not.Null);
            Assert.That(feedback.MessageText.text, Does.Contain("manual recovery"));
        }

        [Test]
        public void LockedPagesHideContentAndRestoreControlsWhenUnlocked()
        {
            GameObject root = NewObject("Hub page access test", null, typeof(RectTransform), typeof(Canvas));
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            root.GetComponent<RectTransform>().sizeDelta = new Vector2(1280f, 720f);
            WordListHubMockupController controller = NewObject("Hub controller", root.transform)
                .AddComponent<WordListHubMockupController>();
            WordListHubSceneBindings bindings = NewObject("Hub bindings", root.transform)
                .AddComponent<WordListHubSceneBindings>();
            PageAccessStore backend = new PageAccessStore();
            backend.LibraryUnlocked = false;
            backend.CommunityUnlocked = false;
            ConfigurePageBindings(root.transform, bindings);
            bindings.library.detailStatus = NewText("Library detail status", bindings.library.detailPane);
            List<ScrollRect> communityScrollRects = ConfigureCommunityScrollbarRegressionFixture(
                root.transform, bindings.community);
            ScrollRect communityListScroll = bindings.community.listScroll;
            Scrollbar originalListHorizontalScrollbar = communityListScroll.horizontalScrollbar;
            Scrollbar originalListVerticalScrollbar = communityListScroll.verticalScrollbar;
            ScrollRect detailScroll = communityScrollRects.Single(scrollRect => scrollRect != communityListScroll);
            Scrollbar originalDetailHorizontalScrollbar = detailScroll.horizontalScrollbar;
            Scrollbar originalDetailVerticalScrollbar = detailScroll.verticalScrollbar;
            Scrollbar[] communityScrollbars = bindings.community.root.GetComponentsInChildren<Scrollbar>(true);
            bool[] originalScrollbarActiveStates = communityScrollbars
                .Select(scrollbar => scrollbar.gameObject.activeSelf).ToArray();
            ScrollRect.ScrollbarVisibility originalHorizontalVisibility =
                communityListScroll.horizontalScrollbarVisibility;
            ScrollRect.ScrollbarVisibility originalVerticalVisibility =
                communityListScroll.verticalScrollbarVisibility;
            Assert.That(communityScrollbars.Length, Is.EqualTo(5),
                "The fixture includes both lists' bars and an unbound bar nested in the detail pane.");
            Assert.That(communityScrollbars.All(scrollbar => scrollbar.gameObject.activeSelf), Is.True,
                "All authored bars begin visible before the page is locked.");
            RebuildCommunityScrollLayoutAndVisibility(bindings.community.root, communityScrollRects);
            Assert.That(communityScrollbars.All(scrollbar => scrollbar.gameObject.activeSelf), Is.True,
                "AutoHide bars are active before locking while their scroll regions have content.");
            SetPrivateField(controller, "sceneBindings", bindings);
            SetPrivateField(controller, "productionMode", true);
            SetPrivateField(controller, "backend", backend);
            InvokePrivate(controller, "CacheNavigationColors");
            InvokePrivate(controller, "RefreshPageAccessIndicators");

            MockupRubyWordsView libraryRubyWords = MockupRubyWordsView.CreateLibrary(
                bindings.library.detailWords, 1f);
            libraryRubyWords.SetWords(new[] { "preserved word" });
            SetPrivateField(controller, "libraryRubyWordsView", libraryRubyWords);
            Assert.That(libraryRubyWords.LayoutRoot.gameObject.activeSelf, Is.True);

            SetPrivateField(controller, "selectedListId", "retained-list-id");
            InvokePrivate(controller, "RefreshLockedLibraryPage",
                backend.GetPageAccess(WordListHubPage.Library));
            Assert.That(bindings.navLibrary.label.text, Does.Not.Contain("LOCKED"),
                "The left lock notice is the only lock copy; the navigation label stays unchanged.");
            Assert.That(bindings.library.emptyNotice.text, Is.EqualTo("Locked"),
                "The left Library list preview should show one short lock label.");
            Assert.That(bindings.library.detailDescription.text, Is.Empty,
                "Page access details belong in the left lock notice, not the right preview.");
            Assert.That(bindings.library.detailMetadata.text, Is.Empty);
            Assert.That(bindings.library.detailStatus.text, Is.Empty);
            Assert.That(bindings.library.listContent.gameObject.activeSelf, Is.False);
            Assert.That(bindings.library.createList.button.gameObject.activeSelf, Is.False);
            Assert.That(bindings.library.searchInput.gameObject.activeSelf, Is.False);
            Assert.That(bindings.library.play.button.gameObject.activeSelf, Is.False);
            Assert.That(libraryRubyWords.LayoutRoot.gameObject.activeSelf, Is.False,
                "The generated Ruby layout is a sibling of the legacy TMP field and must be hidden too.");
            Assert.That(((List<string>)GetPrivateField(libraryRubyWords, "words")), Is.Empty,
                "Locking Library must clear the generated Ruby projection.");
            Assert.That(GetPrivateField(controller, "selectedListId"), Is.Null);

            backend.LibraryUnlocked = true;
            InvokePrivate(controller, "RefreshPageAccessIndicators");
            InvokePrivate(controller, "RestoreLibraryPageControls");
            Assert.That(bindings.navLibrary.label.text, Does.Not.Contain("LOCKED"));
            Assert.That(bindings.library.emptyNotice.gameObject.activeSelf, Is.False);
            Assert.That(bindings.library.listContent.gameObject.activeSelf, Is.True);
            Assert.That(bindings.library.createList.button.gameObject.activeSelf, Is.True);
            Assert.That(bindings.library.searchInput.gameObject.activeSelf, Is.True);
            Assert.That(libraryRubyWords.LayoutRoot.gameObject.activeSelf, Is.True,
                "Unlocking must restore the generated Ruby detail region.");
            Assert.That(backend.QueryLibrary(new MockupLibraryQuery()), Is.Not.Empty,
                "Unlocking must reveal the existing local lists rather than replace their data.");

            SetPrivateField(controller, "selectedDiscoveryId", "remote-item-id");
            InvokePrivate(controller, "RefreshLockedCommunityPage",
                backend.GetPageAccess(WordListHubPage.Community));
            RebuildCommunityScrollLayoutAndVisibility(bindings.community.root, communityScrollRects);
            AssertCommunityScrollbarsLocked(bindings.community.root, communityScrollRects);
            Assert.That(bindings.navCommunity.label.text, Does.Not.Contain("LOCKED"));
            Assert.That(bindings.community.emptyNotice.text, Is.EqualTo("Locked"),
                "The left Community list preview should show one short lock label.");
            Assert.That(bindings.community.detailDescription.text, Is.Empty);
            Assert.That(bindings.community.detailMetadata.text, Is.Empty);
            Assert.That(bindings.community.detailTrustNote.text, Is.Empty);
            Assert.That(bindings.community.listContent.gameObject.activeSelf, Is.False);
            Assert.That(bindings.community.searchInput.gameObject.activeSelf, Is.False);
            Assert.That(bindings.community.download.button.gameObject.activeSelf, Is.False);
            Assert.That(GetPrivateField(controller, "selectedDiscoveryId"), Is.Null);

            backend.CommunityAvailable = false;
            MockupHubPageAccess disabledCommunity = backend.GetPageAccess(WordListHubPage.Community);
            Assert.That(disabledCommunity.IsAvailable, Is.False);
            Assert.That(disabledCommunity.IsUnlocked, Is.False);
            InvokePrivate(controller, "RefreshPageAccessIndicators");
            InvokePrivate(controller, "RefreshLockedCommunityPage", disabledCommunity);
            RebuildCommunityScrollLayoutAndVisibility(bindings.community.root, communityScrollRects);
            AssertCommunityScrollbarsLocked(bindings.community.root, communityScrollRects);
            Assert.That(bindings.navCommunity.label.text, Does.Contain("UNAVAILABLE"));
            Assert.That(bindings.navCommunity.label.text, Does.Not.Contain("LOCKED"));
            Assert.That(bindings.community.emptyNotice.text, Is.EqualTo("Locked"));
            Assert.That(bindings.community.emptyNotice.text, Does.Not.Contain("Complete"));
            Assert.That(bindings.community.download.button.gameObject.activeSelf, Is.False);

            backend.CommunityAvailable = true;
            InvokePrivate(controller, "RefreshPageAccessIndicators");
            InvokePrivate(controller, "RefreshLockedCommunityPage",
                backend.GetPageAccess(WordListHubPage.Community));
            RebuildCommunityScrollLayoutAndVisibility(bindings.community.root, communityScrollRects);
            AssertCommunityScrollbarsLocked(bindings.community.root, communityScrollRects);
            Assert.That(bindings.navCommunity.label.text, Does.Not.Contain("LOCKED"));
            Assert.That(bindings.community.emptyNotice.text, Is.EqualTo("Locked"));

            backend.CommunityUnlocked = true;
            InvokePrivate(controller, "RefreshPageAccessIndicators");
            InvokePrivate(controller, "RestoreCommunityPageControls");
            RebuildCommunityScrollLayoutAndVisibility(bindings.community.root, communityScrollRects);
            Assert.That(communityListScroll.enabled, Is.True,
                "Unlocking restores the list ScrollRect's enabled state.");
            Assert.That(communityListScroll.horizontalScrollbar, Is.SameAs(originalListHorizontalScrollbar));
            Assert.That(communityListScroll.verticalScrollbar, Is.SameAs(originalListVerticalScrollbar));
            Assert.That(detailScroll.enabled, Is.True,
                "Unlocking restores nested detail ScrollRects as well as the primary list.");
            Assert.That(detailScroll.horizontalScrollbar, Is.SameAs(originalDetailHorizontalScrollbar));
            Assert.That(detailScroll.verticalScrollbar, Is.SameAs(originalDetailVerticalScrollbar));
            Assert.That(communityListScroll.horizontalScrollbarVisibility, Is.EqualTo(originalHorizontalVisibility));
            Assert.That(communityListScroll.verticalScrollbarVisibility, Is.EqualTo(originalVerticalVisibility));
            Assert.That(communityScrollbars.Select(scrollbar => scrollbar.gameObject.activeSelf),
                Is.EqualTo(originalScrollbarActiveStates),
                "Unlocking restores every bar's original active state, including unbound nested bars.");
            List<MockupDiscoveryItem> discovery = backend.QueryDiscoveryWithFilters(
                new MockupDiscoveryQuery()).ToList();
            Assert.That(discovery, Is.Not.Empty);
            SetPrivateField(controller, "selectedDiscoveryId", discovery[0].RemoteItemId);
            InvokePrivate(controller, "UpdateCommunityDetail", discovery);
            Assert.That(bindings.navCommunity.label.text, Does.Not.Contain("LOCKED"));
            Assert.That(bindings.community.emptyNotice.gameObject.activeSelf, Is.False);
            Assert.That(bindings.community.listContent.gameObject.activeSelf, Is.True);
            Assert.That(bindings.community.searchInput.gameObject.activeSelf, Is.True);
            Assert.That(bindings.community.download.button.gameObject.activeSelf, Is.True);
        }

        private List<ScrollRect> ConfigureCommunityScrollbarRegressionFixture(
            Transform testRoot, WordListHubSceneBindings.CommunityPageBinding community)
        {
            GameObject pageRoot = NewObject("Community page root", testRoot,
                typeof(RectTransform));
            RectTransform pageRect = pageRoot.GetComponent<RectTransform>();
            pageRect.sizeDelta = new Vector2(900f, 600f);
            community.root = pageRoot;

            GameObject[] communityRoots =
            {
                community.listScroll.gameObject,
                community.detailPane.gameObject,
                community.searchInput.gameObject,
                community.connectionState.gameObject,
                community.connectionMenu.toggleButton.gameObject,
                community.connectionMenu.panel,
                community.like.button.gameObject,
                community.download.button.gameObject,
                community.play.button.gameObject,
                community.report.button.gameObject
            };
            foreach (GameObject communityObject in communityRoots)
                communityObject.transform.SetParent(pageRoot.transform, false);

            ScrollRect listScroll = community.listScroll;
            ConfigureScrollRectViewport(listScroll, community.listContent, "Community list viewport",
                260f, 180f, 620f, 620f);
            listScroll.horizontal = true;
            listScroll.vertical = true;
            listScroll.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            listScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            listScroll.horizontalScrollbar = CreateTestScrollbar("Community list horizontal bar", listScroll.transform);
            listScroll.verticalScrollbar = CreateTestScrollbar("Community list vertical bar", listScroll.transform);

            GameObject detailScrollObject = NewObject("Community detail scroll", community.detailPane,
                typeof(RectTransform), typeof(ScrollRect));
            ScrollRect detailScroll = detailScrollObject.GetComponent<ScrollRect>();
            ConfigureScrollRectViewport(detailScroll, null, "Community detail viewport",
                280f, 210f, 540f, 560f);
            detailScroll.horizontal = true;
            detailScroll.vertical = true;
            detailScroll.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            detailScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            detailScroll.horizontalScrollbar = CreateTestScrollbar("Community detail horizontal bar", detailScroll.transform);
            detailScroll.verticalScrollbar = CreateTestScrollbar("Community detail vertical bar", detailScroll.transform);

            CreateTestScrollbar("Unbound detail scrollbar", community.detailPane);
            return new List<ScrollRect> { listScroll, detailScroll };
        }

        private void ConfigureScrollRectViewport(
            ScrollRect scrollRect, Transform existingContent, string viewportName,
            float viewportWidth, float viewportHeight, float contentWidth, float contentHeight)
        {
            RectTransform scrollRectTransform = scrollRect.GetComponent<RectTransform>();
            scrollRectTransform.sizeDelta = new Vector2(viewportWidth, viewportHeight);
            GameObject viewportObject = NewObject(viewportName, scrollRect.transform, typeof(RectTransform));
            RectTransform viewport = viewportObject.GetComponent<RectTransform>();
            viewport.sizeDelta = new Vector2(viewportWidth, viewportHeight);
            if (existingContent != null)
                existingContent.SetParent(viewport, false);

            RectTransform content = NewObject(viewportName + " content", viewport, typeof(RectTransform))
                .GetComponent<RectTransform>();
            content.sizeDelta = new Vector2(contentWidth, contentHeight);
            scrollRect.viewport = viewport;
            scrollRect.content = content;
        }

        private Scrollbar CreateTestScrollbar(string name, Transform parent)
        {
            GameObject scrollbarObject = NewObject(name, parent,
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Scrollbar));
            scrollbarObject.GetComponent<RectTransform>().sizeDelta = new Vector2(14f, 100f);
            return scrollbarObject.GetComponent<Scrollbar>();
        }

        private static void RebuildCommunityScrollLayoutAndVisibility(
            GameObject pageRoot, IEnumerable<ScrollRect> scrollRects)
        {
            RectTransform pageRect = pageRoot.GetComponent<RectTransform>();
            if (pageRect != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(pageRect);
            Canvas.ForceUpdateCanvases();
            foreach (ScrollRect scrollRect in scrollRects)
            {
                if (scrollRect.viewport != null)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(scrollRect.viewport);
                if (scrollRect.content != null)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(scrollRect.content);
                scrollRect.Rebuild(CanvasUpdate.Prelayout);
                scrollRect.Rebuild(CanvasUpdate.PostLayout);
            }

            Canvas.ForceUpdateCanvases();
            foreach (ScrollRect scrollRect in scrollRects)
                InvokePrivate(scrollRect, "LateUpdate");
            Canvas.ForceUpdateCanvases();
        }

        private static void AssertCommunityScrollbarsLocked(
            GameObject pageRoot, IEnumerable<ScrollRect> scrollRects)
        {
            Assert.That(pageRoot.GetComponentsInChildren<Scrollbar>(true)
                    .All(scrollbar => !scrollbar.gameObject.activeInHierarchy),
                Is.True, "No bound or unbound scrollbar anywhere under the locked Community page may be active.");
            foreach (ScrollRect scrollRect in scrollRects)
            {
                Assert.That(scrollRect.enabled, Is.False,
                    "Locked pages disable every ScrollRect so auto-hide updates cannot reactivate bars.");
                Assert.That(scrollRect.horizontalScrollbar, Is.Null);
                Assert.That(scrollRect.verticalScrollbar, Is.Null);
            }
        }

        [Test]
        public void ProductionCommunityQueriesAndWritesStayGated()
        {
            temporaryLibraryRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "WordListHubPageGate_" + Guid.NewGuid().ToString("N"));
            Type serviceType = GetLoadedType("WordListLibraryService");
            Type backendType = GetLoadedType("WordListHubProductionBackend");
            object isolatedLibrary = Activator.CreateInstance(serviceType, new object[]
            {
                System.IO.Path.Combine(temporaryLibraryRoot, "Custom"),
                System.IO.Path.Combine(temporaryLibraryRoot, "state.json"),
                null
            });
            ConstructorInfo backendConstructor = backendType.GetConstructors()
                .Single(candidate => candidate.GetParameters().Length == 3);
            object backend = backendConstructor.Invoke(new[] { isolatedLibrary, null, null });

            MockupHubPageAccess libraryAccess = (MockupHubPageAccess)InvokeBackend(
                backend, "GetPageAccess", WordListHubPage.Library);
            MockupHubPageAccess communityAccess = (MockupHubPageAccess)InvokeBackend(
                backend, "GetPageAccess", WordListHubPage.Community);
            Assert.That(libraryAccess.IsUnlocked, Is.False);
            Assert.That(libraryAccess.IsAvailable, Is.False);
            Assert.That(libraryAccess.RequiredChallenges, Is.EqualTo(5));
            Assert.That(communityAccess.IsUnlocked, Is.False);
            Assert.That(communityAccess.IsAvailable, Is.False);
            Assert.That(communityAccess.RequiredChallenges, Is.EqualTo(10));
            Assert.That((IReadOnlyList<MockupDiscoveryItem>)InvokeBackend(
                backend, "QueryDiscoveryWithFilters", new MockupDiscoveryQuery()), Is.Empty);
            Assert.That(InvokeBackend(backend, "FindDiscoveryItem", "mock-ugc-weekend-garden"), Is.Null);
            AssertBlockedForCommunity((MockupActionResult)InvokeBackend(
                backend, "TryToggleLike", "mock-ugc-weekend-garden"));
            AssertBlockedForCommunity((MockupActionResult)InvokeBackend(
                backend, "TryDownload", "mock-ugc-weekend-garden"));
            AssertBlockedForCommunity((MockupActionResult)InvokeBackend(
                backend, "TryReportAndHide", "mock-ugc-weekend-garden"));
            MockupActionResult publishResult = (MockupActionResult)InvokeBackend(
                backend, "TryPublish", "some-local-id");
            Assert.That(publishResult, Is.Not.Null);
            Assert.That(publishResult.Succeeded, Is.False);
            Assert.That(publishResult.Message, Does.Contain("Library"));
            Assert.That(publishResult.Message, Does.Contain("unavailable"));
            MockupActionResult unpublishResult = (MockupActionResult)InvokeBackend(
                backend, "TryUnpublish", "some-local-id");
            Assert.That(unpublishResult, Is.Not.Null);
            Assert.That(unpublishResult.Succeeded, Is.False);
            Assert.That(unpublishResult.Message, Does.Contain("no active Workshop publication"));
        }

        [Test]
        public void ProductionMetadataOnlyEditKeepsLegacyMixedModeRevisionPinyinAndTags()
        {
            temporaryLibraryRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "WordListHubAutoLanguageMetadata_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(temporaryLibraryRoot);

            Type serviceType = GetLoadedType("WordListLibraryService");
            Type contentType = GetLoadedType("WordListLibraryContent");
            Type languageType = GetLoadedType("LanguageMode");
            Type codecType = GetLoadedType("WordListLibraryContentCodec");
            Type backendType = GetLoadedType("WordListHubProductionBackend");
            Type profileType = GetLoadedType("GameConfigProfile");
            Type configType = GetLoadedType("GameConfig");
            Type levelPanelType = GetLoadedType("LevelPanelController");

            string customDirectory = System.IO.Path.Combine(temporaryLibraryRoot, "Custom");
            object service = Activator.CreateInstance(serviceType, new object[]
            {
                customDirectory,
                System.IO.Path.Combine(temporaryLibraryRoot, "state.json"),
                null
            });

            ScriptableObject profile = ScriptableObject.CreateInstance(profileType);
            ScriptableObject config = ScriptableObject.CreateInstance(configType);
            GameObject levelPanelObject = NewObject("Metadata test LevelPanel", null);
            levelPanelObject.SetActive(false);
            Component levelPanel = levelPanelObject.AddComponent(levelPanelType);
            try
            {
                profileType.GetField("allowChineseAndMixed").SetValue(profile, true);
                profileType.GetField("ignoreProgressionLocks").SetValue(profile, true);
                profileType.GetField("unlockAllFeatures").SetValue(profile, true);
                profileType.GetField("maxCustomLists").SetValue(profile, 0);
                profileType.GetField("maxWordsPerList").SetValue(profile, 0);
                configType.GetField("activeProfile").SetValue(config, profile);
                SetPrivateField(levelPanel, "config", config);

                MethodInfo parse = codecType.GetMethod("TryParseEditorLines",
                    BindingFlags.Public | BindingFlags.Static);
                Assert.That(parse, Is.Not.Null);
                object[] parseArgs =
                {
                    new List<string> { "你1，好! | ni hao" },
                    Enum.Parse(languageType, "Mixed"),
                    null,
                    null,
                    null
                };
                Assert.That((bool)parse.Invoke(null, parseArgs), Is.True,
                    parseArgs[4] as string);
                object typedContent = parseArgs[3];

                MethodInfo create = serviceType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .Single(method => method.Name == "TryCreateList"
                        && method.GetParameters().Length == 4
                        && method.GetParameters()[1].ParameterType == contentType);
                object[] createArgs = { "Legacy mixed Han", typedContent, null, null };
                Assert.That((bool)create.Invoke(service, createArgs), Is.True, createArgs[3] as string);
                object canonical = createArgs[2];
                string localListId = (string)canonical.GetType().GetProperty("LocalListId").GetValue(canonical);

                MethodInfo updateMetadata = serviceType.GetMethod("TryUpdateMetadata",
                    BindingFlags.Instance | BindingFlags.Public);
                object[] metadataArgs =
                {
                    localListId, "Legacy mixed Han", "", new List<string> { "keep-tag" }, false, null, null
                };
                Assert.That((bool)updateMetadata.Invoke(service, metadataArgs), Is.True,
                    metadataArgs[6] as string);
                canonical = metadataArgs[5];
                int originalRevision = (int)canonical.GetType().GetProperty("ContentRevision").GetValue(canonical);
                Assert.That(Enum.GetName(languageType,
                    canonical.GetType().GetProperty("LanguageMode").GetValue(canonical)), Is.EqualTo("Mixed"));

                ConstructorInfo backendConstructor = backendType.GetConstructors()
                    .Single(candidate => candidate.GetParameters().Length == 3);
                object backend = backendConstructor.Invoke(new[] { service, levelPanel, null });
                MockupWordList draftSource = (MockupWordList)InvokeBackend(backend, "FindById", localListId);
                Assert.That(draftSource.Language, Is.EqualTo(MockupLanguage.Mixed));
                Assert.That(draftSource.DetectedLanguage, Is.EqualTo(MockupLanguage.Chinese));
                Assert.That(draftSource.Words.Single(), Is.EqualTo("你1，好! | ni hao"));

                MockupWordListEdit draft = (MockupWordListEdit)InvokeBackend(backend, "CreateEditDraft", localListId);
                draft.Title = "Metadata changed only";
                draft.Language = MockupLanguage.Chinese;
                draft.Tags = null;
                object result = InvokeBackend(backend, "SaveEdit", localListId, draft);
                Assert.That((bool)result.GetType().GetProperty("Succeeded").GetValue(result), Is.True,
                    result.GetType().GetProperty("Message").GetValue(result) as string);
                Assert.That((bool)result.GetType().GetProperty("ContentChanged").GetValue(result), Is.False);

                object saved = serviceType.GetMethod("FindById").Invoke(service, new object[] { localListId });
                Assert.That((int)saved.GetType().GetProperty("ContentRevision").GetValue(saved),
                    Is.EqualTo(originalRevision));
                Assert.That(Enum.GetName(languageType,
                    saved.GetType().GetProperty("LanguageMode").GetValue(saved)), Is.EqualTo("Mixed"));
                CollectionAssert.AreEqual(new[] { "keep-tag" },
                    (IEnumerable<string>)saved.GetType().GetProperty("Tags").GetValue(saved));
                MockupWordList projected = (MockupWordList)InvokeBackend(backend, "FindById", localListId);
                Assert.That(projected.DetectedLanguage, Is.EqualTo(MockupLanguage.Chinese));
                Assert.That(projected.Words.Single(), Is.EqualTo("你1，好! | ni hao"));

                MethodInfo createThroughBackend = backendType.GetMethod("TryCreateList",
                    BindingFlags.Instance | BindingFlags.Public);
                object[] createRowsArgs =
                {
                    "Empty row filtering", MockupLanguage.English,
                    new List<string> { "apple", "", "banana", "  | ni", "pear" },
                    "", "", null, null
                };
                Assert.That((bool)createThroughBackend.Invoke(backend, createRowsArgs), Is.True,
                    createRowsArgs[6] as string);
                MockupWordList createdWithRows = createRowsArgs[5] as MockupWordList;
                Assert.That(createdWithRows, Is.Not.Null);
                CollectionAssert.AreEqual(new[] { "apple", "banana", "pear" }, createdWithRows.Words);

                object canonicalBeforeEdit = serviceType.GetMethod("FindById")
                    .Invoke(service, new object[] { createdWithRows.Id });
                int editRevision = (int)canonicalBeforeEdit.GetType()
                    .GetProperty("ContentRevision").GetValue(canonicalBeforeEdit);
                MockupWordListEdit contentEdit = (MockupWordListEdit)InvokeBackend(
                    backend, "CreateEditDraft", createdWithRows.Id);
                contentEdit.Words = new List<string> { "apple", "", "banana", " | ni" };
                object contentResult = InvokeBackend(backend, "SaveEdit", createdWithRows.Id, contentEdit);
                Assert.That((bool)contentResult.GetType().GetProperty("Succeeded").GetValue(contentResult), Is.True,
                    contentResult.GetType().GetProperty("Message").GetValue(contentResult) as string);
                Assert.That((bool)contentResult.GetType().GetProperty("ContentChanged").GetValue(contentResult), Is.True);
                object canonicalAfterContentEdit = serviceType.GetMethod("FindById")
                    .Invoke(service, new object[] { createdWithRows.Id });
                Assert.That((int)canonicalAfterContentEdit.GetType().GetProperty("ContentRevision")
                    .GetValue(canonicalAfterContentEdit), Is.EqualTo(editRevision + 1));
                MockupWordList contentProjection = (MockupWordList)InvokeBackend(
                    backend, "FindById", createdWithRows.Id);
                CollectionAssert.AreEqual(new[] { "apple", "banana" }, contentProjection.Words);

                MockupWordListEdit metadataWithPlaceholders = (MockupWordListEdit)InvokeBackend(
                    backend, "CreateEditDraft", createdWithRows.Id);
                metadataWithPlaceholders.Title = "Metadata after filtering";
                metadataWithPlaceholders.Words = new List<string> { "apple", " ", "banana", "| ni" };
                object metadataResult = InvokeBackend(
                    backend, "SaveEdit", createdWithRows.Id, metadataWithPlaceholders);
                Assert.That((bool)metadataResult.GetType().GetProperty("Succeeded").GetValue(metadataResult), Is.True,
                    metadataResult.GetType().GetProperty("Message").GetValue(metadataResult) as string);
                Assert.That((bool)metadataResult.GetType().GetProperty("ContentChanged").GetValue(metadataResult), Is.False);
                object canonicalAfterMetadata = serviceType.GetMethod("FindById")
                    .Invoke(service, new object[] { createdWithRows.Id });
                Assert.That((int)canonicalAfterMetadata.GetType().GetProperty("ContentRevision")
                    .GetValue(canonicalAfterMetadata), Is.EqualTo(editRevision + 1));

                MockupWordListEdit allEmptyEdit = (MockupWordListEdit)InvokeBackend(
                    backend, "CreateEditDraft", createdWithRows.Id);
                allEmptyEdit.Title = "Must not be written";
                allEmptyEdit.Words = new List<string> { "", "   ", "| ni" };
                object allEmptyResult = InvokeBackend(backend, "SaveEdit", createdWithRows.Id, allEmptyEdit);
                Assert.That((bool)allEmptyResult.GetType().GetProperty("Succeeded").GetValue(allEmptyResult), Is.False);
                Assert.That(allEmptyResult.GetType().GetProperty("Message").GetValue(allEmptyResult) as string,
                    Does.Contain("at least one word"));
                object canonicalAfterRejectedEmpty = serviceType.GetMethod("FindById")
                    .Invoke(service, new object[] { createdWithRows.Id });
                Assert.That((int)canonicalAfterRejectedEmpty.GetType().GetProperty("ContentRevision")
                    .GetValue(canonicalAfterRejectedEmpty), Is.EqualTo(editRevision + 1));
                Assert.That(canonicalAfterRejectedEmpty.GetType().GetProperty("Title")
                    .GetValue(canonicalAfterRejectedEmpty), Is.EqualTo("Metadata after filtering"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(levelPanelObject);
                UnityEngine.Object.DestroyImmediate(config);
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void CustomEditCapAllowsSameCountAndShrinkButRejectsGrowth()
        {
            Type profileType = GetLoadedType("GameConfigProfile");
            Type configType = GetLoadedType("GameConfig");
            Type levelPanelType = GetLoadedType("LevelPanelController");
            UnityEngine.Object profile = ScriptableObject.CreateInstance(profileType);
            UnityEngine.Object config = ScriptableObject.CreateInstance(configType);
            try
            {
                profileType.GetField("ignoreProgressionLocks").SetValue(profile, false);
                profileType.GetField("communityEnabled").SetValue(profile, false);
                profileType.GetField("unlockAllFeatures").SetValue(profile, true);
                profileType.GetField("communityUnlockLevels").SetValue(profile, int.MaxValue);
                profileType.GetField("maxWordsPerList").SetValue(profile, 2);
                configType.GetField("activeProfile").SetValue(config, profile);
                GameObject levelPanelObject = NewObject("Isolated LevelPanel", null);
                levelPanelObject.SetActive(false);
                Component levelPanel = levelPanelObject.AddComponent(levelPanelType);
                SetPrivateField(levelPanel, "config", config);
                MethodInfo canEdit = levelPanelType.GetMethod("CanEditListFromHub", BindingFlags.Instance | BindingFlags.Public);
                Assert.That(canEdit, Is.Not.Null);

                Assert.That((bool)profileType.GetField("communityEnabled").GetValue(profile), Is.False);
                Assert.That((bool)profileType.GetField("ignoreProgressionLocks").GetValue(profile), Is.False);
                Assert.That((bool)levelPanelType.GetProperty("IsCommunityEnabledFromHub").GetValue(levelPanel), Is.True,
                    "The developer master enables Community without changing its raw switch.");
                Assert.That((bool)levelPanelType.GetProperty("CanOpenCommunityFromHub").GetValue(levelPanel), Is.True,
                    "The developer master bypasses Community progression but does not change the raw switch.");

                object[] sameCount = { 3, true, null, 3 };
                Assert.That((bool)canEdit.Invoke(levelPanel, sameCount), Is.True,
                    "An existing over-cap list can keep its current count.");
                object[] shrink = { 2, true, null, 3 };
                Assert.That((bool)canEdit.Invoke(levelPanel, shrink), Is.True,
                    "An existing over-cap list can be reduced.");
                object[] growth = { 4, true, null, 3 };
                Assert.That((bool)canEdit.Invoke(levelPanel, growth), Is.False,
                    "Progression bypass must not permit growing beyond the numeric cap.");
                Assert.That((string)growth[2], Does.Contain("word limit"));
                object[] metadataOnly = { 3, false, null, 3 };
                Assert.That((bool)canEdit.Invoke(levelPanel, metadataOnly), Is.True,
                    "Metadata-only edits do not fail because an existing list is over cap.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void ChallengeDetailUsesSeparateStarsAndTimeLineWithoutSelectedDescription()
        {
            Assert.That(typeof(WordListHubSceneBindings.ChallengesPageBinding).GetField(
                "detailDescription", BindingFlags.Instance | BindingFlags.Public), Is.Null);
            Assert.That(typeof(WordListHubSceneBindings.ChallengesPageBinding).GetField(
                "detailRecords", BindingFlags.Instance | BindingFlags.Public), Is.Not.Null);

            GameObject owner = NewObject("Challenge details test", null);
            var binding = new WordListHubSceneBindings.ChallengesPageBinding
            {
                detailTitle = NewText("Challenge title", owner.transform),
                detailMetadata = NewText("Challenge metadata", owner.transform),
                detailRecords = NewText("Challenge records", owner.transform),
                detailWordsLabel = NewText("Challenge words label", owner.transform),
                detailWords = NewText("Challenge legacy words", owner.transform),
                status = NewText("Challenge status", owner.transform)
            };
            DateTime now = DateTime.UtcNow;
            MockupWordList selected = MockupWordListProjection.CreateWordList(
                "challenge-test", "Test challenge", "PRIVATE_DESCRIPTION_MUST_NOT_RENDER", string.Empty,
                MockupLanguage.English, new[] { "apple" }, false, now, now, now, now,
                3, 12.5f, 1, 1, isChallenge: true, isChallengeUnlocked: true);

            InvokeStaticPrivate(typeof(WordListHubMockupController), "SetChallengeDetailHeader",
                binding, selected);

            Assert.That(binding.detailMetadata.text, Does.Contain("UNLOCKED"));
            Assert.That(binding.detailMetadata.text, Does.Contain("English"));
            Assert.That(binding.detailMetadata.text, Does.Contain("1 words"));
            Assert.That(binding.detailMetadata.text, Does.Not.Contain("Best"));
            Assert.That(binding.detailRecords.text, Does.Contain("★★★"));
            Assert.That(binding.detailRecords.text, Does.Contain("12.5 s"));
            Assert.That(binding.detailRecords.text, Does.Not.Contain("PRIVATE_DESCRIPTION_MUST_NOT_RENDER"));

            GameObject rowObject = NewObject("Challenge reward tile", owner.transform, typeof(RectTransform));
            RectTransform tile = rowObject.GetComponent<RectTransform>();
            tile.sizeDelta = new Vector2(270f, 270f);
            WordListHubLibraryRowView row = rowObject.AddComponent<WordListHubLibraryRowView>();
            row.title = NewText("Reward tile title", rowObject.transform);
            row.metadata = NewText("Reward metadata", rowObject.transform);
            row.recordStars = NewText("Reward stars", rowObject.transform);
            row.bestTime = NewText("Reward tile time", rowObject.transform);
            row.badgeRoot = NewObject("Existing locked badge", rowObject.transform,
                typeof(RectTransform), typeof(Image));
            row.badge = NewText("Existing locked badge label", row.badgeRoot.transform, "LOCKED");
            GameObject controllerObject = NewObject("Challenge layout controller", owner.transform);
            controllerObject.SetActive(false);
            WordListHubMockupController controller = controllerObject.AddComponent<WordListHubMockupController>();
            SetPrivateField(controller, "productionMode", true);
            WordListHubSceneBindings controllerBindings = NewObject("Challenge bindings", owner.transform,
                typeof(WordListHubSceneBindings)).GetComponent<WordListHubSceneBindings>();
            controllerBindings.hostRoot = owner;
            controllerBindings.challenges = binding;
            SetPrivateField(controller, "sceneBindings", controllerBindings);
            SetPrivateField(controller, "challengeRubyWordsView",
                MockupRubyWordsView.CreateLibrary(binding.detailWords, 1f));
            binding.play = CreateButtonBinding("Challenge play", owner.transform, "Play");
            binding.copyToCustom = CreateButtonBinding("Challenge copy", owner.transform, "Copy");

            MockupWordList unlockedButUnplayed = MockupWordListProjection.CreateWordList(
                "challenge-unplayed", "PRIVATE_UNPLAYED_TITLE", "PRIVATE_UNPLAYED_DESCRIPTION", string.Empty,
                MockupLanguage.English, new[] { "PRIVATE_UNPLAYED_WORD" }, false,
                now, now, now, null, null, null, 1, 0,
                isChallenge: true, isChallengeUnlocked: true, challengeNumber: 2);
            Assert.That(unlockedButUnplayed.HasBeenPlayed, Is.False);
            Assert.That(unlockedButUnplayed.HasCompletedChallenge, Is.False,
                "An effective profile unlock does not count as a completed challenge.");
            Assert.That(unlockedButUnplayed.CanRevealChallengeDetails, Is.False,
                "An unlocked challenge stays private until it is entered or the master override is enabled.");
            InvokePrivate(controller, "RenderChallengeDetail", binding, unlockedButUnplayed, 2);
            Assert.That(binding.detailTitle.text, Is.EqualTo("Level 2"));
            Assert.That(binding.detailTitle.text, Does.Not.Contain("PRIVATE_UNPLAYED_TITLE"));
            Assert.That(binding.detailTitle.text, Does.Not.Contain("PRIVATE_UNPLAYED_DESCRIPTION"));
            Assert.That(binding.detailMetadata.gameObject.activeSelf, Is.False);
            Assert.That(binding.detailMetadata.text, Is.Empty);
            Assert.That(binding.detailRecords.gameObject.activeSelf, Is.False);
            Assert.That(binding.detailRecords.text, Is.Empty);
            Assert.That(binding.detailWordsLabel.gameObject.activeSelf, Is.False);
            Assert.That(binding.detailWords.gameObject.activeSelf, Is.False);
            Assert.That(binding.detailWords.text, Is.Empty);
            MockupRubyWordsView challengeRuby = (MockupRubyWordsView)GetPrivateField(controller, "challengeRubyWordsView");
            Assert.That(challengeRuby.LayoutRoot.gameObject.activeSelf, Is.False);
            Assert.That((List<string>)GetPrivateField(challengeRuby, "words"), Is.Empty);
            Assert.That(binding.play.button.gameObject.activeSelf, Is.True,
                "An unlocked challenge remains playable before its details are revealed.");
            Assert.That(binding.copyToCustom.button.gameObject.activeSelf, Is.False,
                "Production hides Copy when no unlocked Custom backend is assigned.");
            Assert.That(binding.status.text, Does.Contain("Play this challenge once"));
            Assert.That(binding.status.text, Does.Not.Contain("Complete earlier challenges"));
            InvokePrivateExact(controller, "UpdateChallengeRow",
                new[] { typeof(WordListHubLibraryRowView), typeof(MockupWordList), typeof(float), typeof(int) },
                row, unlockedButUnplayed, 270f, 2);
            Assert.That(row.title.text, Is.EqualTo("Level 2"));
            Assert.That(row.metadata.text, Is.Empty);
            Assert.That(row.recordStars.text, Is.Empty);
            Assert.That(row.bestTime.text, Is.Empty);
            Assert.That(row.badgeRoot.activeSelf, Is.False,
                "An unlocked but unplayed challenge stays distinct from a locked one.");

            MockupWordList masterOverrideUnplayed = MockupWordListProjection.CreateWordList(
                "challenge-master-override", "MASTER_OVERRIDE_TITLE", string.Empty, string.Empty,
                MockupLanguage.English, new[] { "MASTER_OVERRIDE_WORD" }, false,
                now, now, now, null, null, null, 1, 0,
                isChallenge: true, isChallengeUnlocked: true, challengeNumber: 2,
                revealChallengeDetails: true);
            Assert.That(masterOverrideUnplayed.LastPlayedAt, Is.Null);
            Assert.That(masterOverrideUnplayed.PlayCount, Is.Zero);
            Assert.That(masterOverrideUnplayed.HasBeenPlayed, Is.False);
            Assert.That(masterOverrideUnplayed.HasCompletedChallenge, Is.False);
            Assert.That(masterOverrideUnplayed.CanRevealChallengeDetails, Is.True,
                "The explicit master override reveals contents without recording a play.");
            InvokePrivate(controller, "RenderChallengeDetail", binding, masterOverrideUnplayed, 2);
            Assert.That(binding.detailTitle.text, Is.EqualTo("MASTER_OVERRIDE_TITLE"));
            Assert.That(binding.detailWords.text, Does.Contain("MASTER_OVERRIDE_WORD"));
            Assert.That(binding.detailRecords.gameObject.activeSelf, Is.False,
                "The master override does not fabricate completion records.");

            MockupWordList enteredButUncompleted = MockupWordListProjection.CreateWordList(
                "challenge-entered", "ENTERED_CHALLENGE_TITLE", string.Empty, string.Empty,
                MockupLanguage.English, new[] { "ENTERED_CHALLENGE_WORD" }, false,
                now, now, now, now, null, null, 1, 0,
                isChallenge: true, isChallengeUnlocked: true, challengeNumber: 2);
            Assert.That(enteredButUncompleted.HasBeenPlayed, Is.True);
            Assert.That(enteredButUncompleted.PlayCount, Is.Zero);
            Assert.That(enteredButUncompleted.HasCompletedChallenge, Is.False,
                "Entering Play is not counted as a completed challenge.");
            Assert.That(enteredButUncompleted.CanRevealChallengeDetails, Is.True);
            InvokePrivate(controller, "RenderChallengeDetail", binding, enteredButUncompleted, 2);
            Assert.That(binding.detailTitle.text, Is.EqualTo("ENTERED_CHALLENGE_TITLE"));
            Assert.That(binding.detailMetadata.gameObject.activeSelf, Is.True);
            Assert.That(binding.detailWords.gameObject.activeSelf, Is.True);
            Assert.That(binding.detailWords.text, Does.Contain("ENTERED_CHALLENGE_WORD"));
            Assert.That(binding.detailRecords.gameObject.activeSelf, Is.False);
            Assert.That(binding.detailRecords.text, Is.Empty,
                "An entered but uncompleted challenge has no fabricated record line.");
            Assert.That(binding.status.text, Does.Contain("Choose Play"));
            InvokePrivateExact(controller, "UpdateChallengeRow",
                new[] { typeof(WordListHubLibraryRowView), typeof(MockupWordList), typeof(float), typeof(int) },
                row, enteredButUncompleted, 270f, 2);
            Assert.That(row.title.text, Is.EqualTo("ENTERED_CHALLENGE_TITLE"));
            Assert.That(row.metadata.text, Does.Contain("English"));
            Assert.That(row.recordStars.text, Is.Empty);
            Assert.That(row.bestTime.text, Is.Empty);
            Assert.That(row.badgeRoot.activeSelf, Is.False);

            MockupWordList completedByPersistedRecord = MockupWordListProjection.CreateWordList(
                "challenge-record-completed", "Record-completed title", string.Empty, string.Empty,
                MockupLanguage.English, new[] { "record-word" }, false,
                now, now, now, null, null, null, 1, 0,
                isChallenge: true, isChallengeUnlocked: true, challengeNumber: 7,
                challengeCompleted: true);
            Assert.That(completedByPersistedRecord.PlayCount, Is.Zero);
            Assert.That(completedByPersistedRecord.HasCompletedChallenge, Is.True,
                "The backend can report completion from the per-level saved marker even with zero stars.");
            Assert.That(completedByPersistedRecord.CanRevealChallengeDetails, Is.True);
            InvokePrivate(controller, "RenderChallengeDetail", binding, completedByPersistedRecord, 7);
            Assert.That(binding.detailTitle.text, Is.EqualTo("Record-completed title"));
            Assert.That(binding.detailWords.gameObject.activeSelf, Is.True);

            MockupWordList lockedChallenge = MockupWordListProjection.CreateWordList(
                "challenge-private", "PRIVATE_CHALLENGE_TITLE", "PRIVATE_CHALLENGE_DESCRIPTION", string.Empty,
                MockupLanguage.English, new[] { "SECRET_WORD", "SECRET_ANNOTATION | secret" }, false,
                now, now, now, null, null, null, 1, 0, isChallenge: true,
                isChallengeUnlocked: false, unlockRewardLabel: "Unlock Library + Community",
                challengeNumber: 7);
            Assert.That(lockedChallenge.CanRevealChallengeDetails, Is.False,
                "A future locked challenge must keep its details private.");
            InvokePrivate(controller, "RenderChallengeDetail", binding, lockedChallenge, 7);
            Assert.That(binding.detailTitle.text, Is.EqualTo("Level 7"));
            Assert.That(binding.detailTitle.text, Does.Not.Contain("PRIVATE_CHALLENGE_TITLE"));
            Assert.That(binding.detailMetadata.text, Is.Empty);
            Assert.That(binding.detailMetadata.gameObject.activeSelf, Is.False);
            Assert.That(binding.detailRecords.text, Is.Empty);
            Assert.That(binding.detailRecords.gameObject.activeSelf, Is.False);
            Assert.That(binding.detailWordsLabel.text, Is.Empty);
            Assert.That(binding.detailWordsLabel.gameObject.activeSelf, Is.False);
            Assert.That(binding.detailWords.text, Is.Empty);
            Assert.That(binding.detailWords.gameObject.activeSelf, Is.False);
            Assert.That(challengeRuby.LayoutRoot.gameObject.activeSelf, Is.False,
                "Switching from an unlocked challenge clears the previous word preview.");
            Assert.That((List<string>)GetPrivateField(challengeRuby, "words"), Is.Empty);
            Assert.That(binding.play.button.gameObject.activeSelf, Is.False);
            Assert.That(binding.copyToCustom.button.gameObject.activeSelf, Is.False);
            Assert.That(binding.status.text, Does.Contain("Complete earlier challenges"));
            Assert.That(binding.status.text, Does.Contain("Unlock Custom List + Community"));

            InvokePrivate(controller, "RenderChallengeDetail", binding, enteredButUncompleted, 2);
            Assert.That(binding.detailTitle.text, Is.EqualTo("ENTERED_CHALLENGE_TITLE"),
                "Switching back to an entered challenge restores its title.");
            Assert.That(binding.detailWords.gameObject.activeSelf, Is.True);
            Assert.That(binding.detailWords.text, Does.Contain("ENTERED_CHALLENGE_WORD"));
            Assert.That(binding.detailRecords.gameObject.activeSelf, Is.False);
            Assert.That((List<string>)GetPrivateField(challengeRuby, "words"), Does.Contain("ENTERED_CHALLENGE_WORD"));

            InvokePrivate(controller, "RenderChallengeDetail", binding, selected, 7);
            Assert.That(binding.detailTitle.gameObject.activeSelf, Is.True);
            Assert.That(binding.detailTitle.text, Is.EqualTo("Test challenge"));
            Assert.That(binding.detailMetadata.gameObject.activeSelf, Is.True);
            Assert.That(binding.detailRecords.gameObject.activeSelf, Is.True);
            Assert.That(binding.detailWordsLabel.gameObject.activeSelf, Is.True);
            Assert.That(binding.detailWordsLabel.text, Does.Contain("1"));
            Assert.That(challengeRuby.LayoutRoot.gameObject.activeSelf, Is.True);
            Assert.That((List<string>)GetPrivateField(challengeRuby, "words"), Does.Contain("apple"));
            Assert.That(binding.play.button.gameObject.activeSelf, Is.True);
            Assert.That(binding.play.button.interactable, Is.True);

            MockupWordList bothRewards = MockupWordListProjection.CreateWordList(
                "challenge-both-rewards", "Both rewards", string.Empty, string.Empty,
                MockupLanguage.English, new[] { "apple" }, false, now, now, now, null,
                null, null, 1, 0, isChallenge: true, isChallengeUnlocked: false,
                unlockRewardLabel: "Unlock Library + Community", challengeNumber: 7);
            InvokePrivateExact(controller, "UpdateChallengeRow",
                new[] { typeof(WordListHubLibraryRowView), typeof(MockupWordList), typeof(float), typeof(int) },
                row, bothRewards, 270f, 7);

            Assert.That(row.title.text, Is.EqualTo("Level 7"));
            Assert.That(row.title.text, Does.Not.Contain("Both rewards"));
            Assert.That(row.metadata.text, Is.Empty);
            Assert.That(row.metadata.text, Does.Not.Contain("Unlock"));
            Assert.That(row.recordStars.text, Is.Empty);
            Assert.That(row.bestTime.text, Is.Empty);
            Assert.That(row.badgeRoot.activeSelf, Is.True);
            Assert.That(row.badge.text, Is.EqualTo("LOCKED"));
            Assert.That(row.badgeRoot.GetComponent<RectTransform>().anchoredPosition.y, Is.EqualTo(-18f));
            Assert.That(row.badgeRoot.GetComponent<RectTransform>().sizeDelta.y, Is.EqualTo(16f));
            Assert.That(row.unlockRewardBanner, Is.Not.Null);
            Assert.That(row.unlockRewardBanner.gameObject.name, Is.EqualTo("UnlockRewardBanner"));
            Assert.That(row.unlockRewardBanner.transform.parent, Is.EqualTo(rowObject.transform));
            Assert.That(row.unlockRewardBanner.gameObject.activeSelf, Is.True);
            Assert.That(row.unlockRewardBanner.text, Is.EqualTo("Unlock Custom List + Community"));
            Assert.That(row.unlockRewardBanner.rectTransform.anchorMin, Is.EqualTo(new Vector2(0f, 1f)));
            Assert.That(row.unlockRewardBanner.rectTransform.anchorMax, Is.EqualTo(new Vector2(1f, 1f)));
            Assert.That(row.unlockRewardBanner.rectTransform.anchoredPosition, Is.EqualTo(Vector2.zero));
            Assert.That(row.unlockRewardBanner.rectTransform.sizeDelta, Is.EqualTo(new Vector2(-24f, 18f)));

            MockupWordList customReward = MockupWordListProjection.CreateWordList(
                "challenge-custom-reward", "Custom reward", string.Empty, string.Empty,
                MockupLanguage.English, new[] { "apple" }, false, now, now, now, now,
                null, null, 1, 0, isChallenge: true, isChallengeUnlocked: true,
                unlockRewardLabel: "Unlock Library", challengeNumber: 7);
            InvokePrivateExact(controller, "UpdateChallengeRow",
                new[] { typeof(WordListHubLibraryRowView), typeof(MockupWordList), typeof(float) },
                row, customReward, 270f);
            Assert.That(row.title.text, Is.EqualTo("Custom reward"));
            Assert.That(row.metadata.text, Does.Contain("English"));
            Assert.That(row.metadata.text, Does.Contain("1 words"));
            Assert.That(row.recordStars.text, Is.Empty);
            Assert.That(row.bestTime.text, Is.Empty);
            Assert.That(row.badgeRoot.activeSelf, Is.False,
                "An unlocked but uncompleted tile shows its details without looking locked.");
            Assert.That(row.unlockRewardBanner.text, Is.EqualTo("Unlock Custom List"));

            MockupWordList completedByRunRecord = MockupWordListProjection.CreateWordList(
                "challenge-played", "Completed challenge", string.Empty, string.Empty,
                MockupLanguage.English, new[] { "apple" }, false, now, now, now, null,
                null, null, 1, 1, isChallenge: true, isChallengeUnlocked: true,
                unlockRewardLabel: "Unlock Library", challengeNumber: 7);
            Assert.That(completedByRunRecord.HasCompletedChallenge, Is.True,
                "A canonical completed-run count reveals the saved challenge details.");
            InvokePrivateExact(controller, "UpdateChallengeRow",
                new[] { typeof(WordListHubLibraryRowView), typeof(MockupWordList), typeof(float) },
                row, completedByRunRecord, 270f);
            Assert.That(row.title.text, Is.EqualTo("Completed challenge"));
            Assert.That(row.metadata.text, Does.Contain("English"));

            tile.sizeDelta = new Vector2(240f, 240f);
            InvokePrivate(controller, "ConfigureTileForSize", tile, 240f);
            Assert.That(row.unlockRewardBanner.gameObject.activeSelf, Is.True,
                "A reward banner remains visible when the tile is resized.");
            Assert.That(row.unlockRewardBanner.rectTransform.anchorMin, Is.EqualTo(new Vector2(0f, 1f)));
            Assert.That(row.unlockRewardBanner.rectTransform.anchorMax, Is.EqualTo(new Vector2(1f, 1f)));
            Assert.That(row.unlockRewardBanner.rectTransform.anchoredPosition, Is.EqualTo(Vector2.zero));
            Assert.That(row.unlockRewardBanner.rectTransform.sizeDelta, Is.EqualTo(new Vector2(-24f, 18f)));

            MockupWordList noReward = MockupWordListProjection.CreateWordList(
                "challenge-no-reward", "No reward", string.Empty, string.Empty,
                MockupLanguage.English, new[] { "apple" }, false, now, now, now, null,
                null, null, 1, 0, isChallenge: true, isChallengeUnlocked: false);
            InvokePrivateExact(controller, "UpdateChallengeRow",
                new[] { typeof(WordListHubLibraryRowView), typeof(MockupWordList), typeof(float) },
                row, noReward, 240f);
            Assert.That(row.unlockRewardBanner.gameObject.activeSelf, Is.False);
            Assert.That(row.unlockRewardBanner.text, Is.Empty);
            Assert.That(row.badgeRoot.activeSelf, Is.True);
            Assert.That(row.badge.text, Is.EqualTo("LOCKED"));
            Assert.That(rowObject.transform.Find("UnlockRewardBanner"), Is.EqualTo(row.unlockRewardBanner.transform),
                "Repeated row updates reuse the single generated banner child.");

            InvokePrivate(controller, "ConfigureTileForSize", tile, 240f);
            Assert.That(row.unlockRewardBanner.gameObject.activeSelf, Is.False,
                "Resizing a no-reward challenge does not show the cached reward banner.");
            float resizedTileScale = 240f < WordListHubTileGridMetrics.MinimumReadableTileSize
                ? 240f / WordListHubTileGridMetrics.MinimumReadableTileSize : 1f;
            Assert.That(row.metadata.rectTransform.anchoredPosition.y, Is.EqualTo(-116f * resizedTileScale));
            Assert.That(row.metadata.rectTransform.sizeDelta.y, Is.EqualTo(28f * resizedTileScale));
            Assert.That(row.metadata.textWrappingMode, Is.EqualTo(TextWrappingModes.NoWrap));
            Assert.That(row.recordStars.rectTransform.anchoredPosition.y, Is.EqualTo(-168f * resizedTileScale));
            Assert.That(row.recordStars.rectTransform.sizeDelta.y, Is.EqualTo(34f * resizedTileScale));
        }

        private void ConfigurePageBindings(Transform parent, WordListHubSceneBindings bindings)
        {
            bindings.navLibrary = CreateButtonBinding("Library", parent, "Library");
            bindings.navCommunity = CreateButtonBinding("Community", parent, "Community");
            bindings.navChallenges = CreateButtonBinding("Challenges", parent, "Challenges");

            WordListHubSceneBindings.LibraryPageBinding library = new WordListHubSceneBindings.LibraryPageBinding();
            GameObject libraryScroll = NewObject("Library scroll", parent, typeof(ScrollRect));
            library.listScroll = libraryScroll.GetComponent<ScrollRect>();
            library.listContent = NewObject("Library content", libraryScroll.transform).transform;
            library.emptyNotice = NewText("Library notice", libraryScroll.transform);
            library.detailPane = NewObject("Library details", parent).transform;
            library.detailTitle = NewText("Library detail title", library.detailPane);
            library.detailDescription = NewText("Library detail description", library.detailPane);
            library.detailMetadata = NewText("Library detail metadata", library.detailPane);
            library.detailWordsLabel = NewText("Library detail words label", library.detailPane);
            library.detailWords = NewText("Library detail words", library.detailPane);
            library.searchInput = NewObject("Library search", parent, typeof(TMP_InputField)).GetComponent<TMP_InputField>();
            library.languageFilter = NewObject("Library language filter", parent, typeof(TMP_Dropdown)).GetComponent<TMP_Dropdown>();
            library.playedFilter = NewObject("Library played filter", parent, typeof(TMP_Dropdown)).GetComponent<TMP_Dropdown>();
            library.sortFilter = NewObject("Library sort filter", parent, typeof(TMP_Dropdown)).GetComponent<TMP_Dropdown>();
            library.directionFilter = NewObject("Library direction filter", parent, typeof(TMP_Dropdown)).GetComponent<TMP_Dropdown>();
            library.filtersMenu.toggleButton = CreateButtonBinding("Library filters", parent, "Filters").button;
            library.filtersMenu.panel = NewObject("Library filter panel", parent);
            library.allLists = CreateButtonBinding("All lists", parent, "All lists");
            library.favorites = CreateButtonBinding("Favorites", parent, "Favorites");
            library.createList = CreateButtonBinding("Create list", parent, "Create");
            library.importList = CreateButtonBinding("Import list", parent, "Import");
            library.exportList = CreateButtonBinding("Export list", parent, "Export");
            library.delete = CreateButtonBinding("Delete list", parent, "Delete");
            library.play = CreateButtonBinding("Play list", parent, "Play");
            library.edit = CreateButtonBinding("Edit list", parent, "Edit");
            library.copyEditWords = CreateButtonBinding("Copy list", parent, "Copy");
            library.publish = CreateButtonBinding("Publish list", parent, "Publish");
            library.unpublish = CreateButtonBinding("Unpublish list", parent, "Unpublish");
            bindings.library = library;

            WordListHubSceneBindings.CommunityPageBinding community = new WordListHubSceneBindings.CommunityPageBinding();
            GameObject communityScroll = NewObject("Community scroll", parent, typeof(ScrollRect));
            community.listScroll = communityScroll.GetComponent<ScrollRect>();
            community.listContent = NewObject("Community content", communityScroll.transform).transform;
            community.emptyNotice = NewText("Community notice", communityScroll.transform);
            community.detailPane = NewObject("Community details", parent).transform;
            community.detailTitle = NewText("Community detail title", community.detailPane);
            community.detailAuthor = NewText("Community detail author", community.detailPane);
            community.detailSummary = NewText("Community detail summary", community.detailPane);
            community.detailDescription = NewText("Community detail description", community.detailPane);
            community.detailMetadata = NewText("Community detail metadata", community.detailPane);
            community.detailTrustNote = NewText("Community detail note", community.detailPane);
            community.searchInput = NewObject("Community search", parent, typeof(TMP_InputField)).GetComponent<TMP_InputField>();
            community.connectionState = NewObject("Community connection", parent, typeof(TMP_Dropdown)).GetComponent<TMP_Dropdown>();
            community.connectionMenu.toggleButton = CreateButtonBinding("Community filters", parent, "Filters").button;
            community.connectionMenu.panel = NewObject("Community filter panel", parent);
            community.like = CreateButtonBinding("Like", parent, "Like");
            community.download = CreateButtonBinding("Download", parent, "Download");
            community.play = CreateButtonBinding("Community play", parent, "Play");
            community.report = CreateButtonBinding("Report", parent, "Report");
            bindings.community = community;
        }

        private WordListHubSceneBindings.ButtonBinding CreateButtonBinding(
            string name, Transform parent, string labelText)
        {
            GameObject buttonObject = NewObject(name, parent,
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = buttonObject.GetComponent<Image>();
            return new WordListHubSceneBindings.ButtonBinding
            {
                button = button,
                label = NewText(name + " label", buttonObject.transform, labelText)
            };
        }

        private TMP_Text NewText(string name, Transform parent, string text = "")
        {
            GameObject textObject = NewObject(name, parent,
                typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            TMP_Text result = textObject.GetComponent<TextMeshProUGUI>();
            result.text = text;
            return result;
        }

        private GameObject NewObject(string name, Transform parent, params Type[] components)
        {
            GameObject result = components == null || components.Length == 0
                ? new GameObject(name)
                : new GameObject(name, components);
            if (parent != null)
                result.transform.SetParent(parent, false);
            if (parent == null || !ownedObjects.Any(item => item != null
                && (parent == item.transform || parent.IsChildOf(item.transform))))
                ownedObjects.Add(result);
            return result;
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName + " should be a private controller field.");
            field.SetValue(target, value);
        }

        private static object GetPrivateField(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName + " should be a private controller field.");
            return field.GetValue(target);
        }

        private static void SetProperty(object target, string propertyName, object value)
        {
            PropertyInfo property = target.GetType().GetProperty(propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(property, Is.Not.Null, propertyName + " should exist.");
            MethodInfo setter = property.GetSetMethod(true);
            Assert.That(setter, Is.Not.Null, propertyName + " should have a setter.");
            setter.Invoke(target, new[] { value });
        }

        private static object InvokePrivate(object target, string methodName, params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, methodName + " should exist.");
            return method.Invoke(target, arguments);
        }

        private static object InvokePrivateExact(
            object target, string methodName, Type[] parameterTypes, params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(methodName,
                BindingFlags.Instance | BindingFlags.NonPublic, null, parameterTypes, null);
            Assert.That(method, Is.Not.Null, methodName + " should exist with the requested signature.");
            return method.Invoke(target, arguments);
        }

        private static void InvokeStaticPrivate(Type type, string methodName, params object[] arguments)
        {
            MethodInfo method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, methodName + " should exist.");
            method.Invoke(null, arguments);
        }

        private static void AssertBlockedForCommunity(MockupActionResult result)
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Message, Does.Contain("Community"));
            Assert.That(result.Message, Does.Contain("unavailable"));
            Assert.That(result.Message, Does.Not.Contain("Complete"));
        }

        private static Type GetLoadedType(string typeName)
        {
            Type result = AppDomain.CurrentDomain.GetAssemblies()
                .Select(candidate => candidate.GetType(typeName, false))
                .FirstOrDefault(candidate => candidate != null);
            Assert.That(result, Is.Not.Null, typeName + " should exist in a loaded runtime assembly.");
            return result;
        }

        private static object InvokeBackend(object backend, string methodName, params object[] arguments)
        {
            MethodInfo method = backend.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
            Assert.That(method, Is.Not.Null, methodName + " should be public on the production backend.");
            return method.Invoke(backend, arguments);
        }

        private sealed class FakeCommunityAsync : IWordListHubCommunityAsync
        {
            public event Action Changed { add { } remove { } }
            public event Action BecameAvailable { add { } remove { } }
            public event Action<MockupCommunityProgress> ProgressChanged { add { } remove { } }
            public IReadOnlyList<MockupDiscoveryItem> Snapshot => Array.Empty<MockupDiscoveryItem>();
            public IReadOnlyList<MockupWordList> DownloadedItems => Array.Empty<MockupWordList>();
            public IReadOnlyList<MockupCommunityPublicationInfo> Publications { get; set; }
                = Array.Empty<MockupCommunityPublicationInfo>();
            public int PublishCalls { get; private set; }
            public int UnpublishCalls { get; private set; }
            public int ReconcileCalls { get; private set; }
            public int ItemPageCalls { get; private set; }
            public bool LocalPublicationLocked { get; set; }
            public string ConnectionMessage => string.Empty;
            public string ContinuationToken => string.Empty;
            public bool HasMore => false;
            public bool IsRemoteAuthenticated { get; set; }
            public bool HasHiddenItems => false;
            public MockupCommunityProgress CurrentProgress => null;

            public bool IsLocalListPublicationLocked(string localListId) => LocalPublicationLocked;

            public bool TryGetDownloadedContentForCopy(string communityProjectionId,
                out WordListLibraryContent content, out string title, out string error)
            {
                content = null;
                title = string.Empty;
                error = string.Empty;
                return false;
            }

            public Task<MockupCommunityQueryResult> QueryAsync(
                MockupDiscoveryQuery query, string continuationToken, CancellationToken cancellationToken)
                => throw new NotSupportedException();
            public Task<MockupActionResult> LikeAsync(string remoteItemId, CancellationToken cancellationToken)
                => throw new NotSupportedException();
            public Task<MockupActionResult> DownloadAsync(string remoteItemId, CancellationToken cancellationToken)
                => throw new NotSupportedException();
            public Task<MockupActionResult> HideLocallyAsync(string remoteItemId, CancellationToken cancellationToken)
                => throw new NotSupportedException();
            public Task<MockupActionResult> RestoreHiddenItemsAsync(CancellationToken cancellationToken)
                => throw new NotSupportedException();
            public Task<MockupCommunityPublicationResult> PublishAsync(
                string localListId, CancellationToken cancellationToken)
            {
                PublishCalls++;
                throw new NotSupportedException();
            }
            public Task<MockupCommunityPublicationResult> UnpublishAsync(
                string localListId, CancellationToken cancellationToken)
            {
                UnpublishCalls++;
                throw new NotSupportedException();
            }
            public Task<MockupCommunityPublicationResult> ReconcilePublicationAsync(
                string localListId, CancellationToken cancellationToken)
            {
                ReconcileCalls++;
                throw new NotSupportedException();
            }
            public Task<MockupCommunityPageResult> GetItemPageAsync(
                string remoteItemId, CancellationToken cancellationToken)
            {
                ItemPageCalls++;
                throw new NotSupportedException();
            }
        }

        private sealed class PageAccessStore : MockupWordListStore
        {
            public bool LibraryUnlocked;
            public bool CommunityUnlocked;
            public bool CommunityAvailable = true;

            public override MockupHubPageAccess GetPageAccess(WordListHubPage page)
            {
                if (page == WordListHubPage.Library)
                    return new MockupHubPageAccess(LibraryUnlocked, 2, 5, "English");
                if (page == WordListHubPage.Community)
                    return new MockupHubPageAccess(CommunityUnlocked, 2, 10, "English", CommunityAvailable);
                return MockupHubPageAccess.OpenPreview;
            }
        }
    }

    [TestFixture]
    public sealed class WordListHubSceneSerializationTests
    {
        private const string MockupScenePath = "Assets/Scenes/WordListHubMockup.unity";

        [Test]
        public void SceneFile_PreservesUnityYamlPreambleAndEndsWithSceneRoots()
        {
            string sceneText = System.IO.File.ReadAllText(MockupScenePath).Replace("\r\n", "\n");
            const string expectedPreamble = "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n";
            Assert.That(sceneText.StartsWith(expectedPreamble, StringComparison.Ordinal), Is.True,
                "Unity's !u! document tags require the YAML tag directive at the start of the scene.");

            int lastDocumentStart = sceneText.LastIndexOf("\n--- !u!", StringComparison.Ordinal);
            Assert.That(lastDocumentStart, Is.GreaterThanOrEqualTo(0),
                "The scene should contain a final Unity YAML document.");
            string lastDocument = sceneText.Substring(lastDocumentStart + 1).TrimEnd();
            string[] lastDocumentLines = lastDocument.Split('\n');
            Assert.That(lastDocumentLines[0], Does.StartWith("--- !u!1660057539 &"));
            Assert.That(lastDocumentLines.Length, Is.GreaterThan(1));
            Assert.That(lastDocumentLines[1], Is.EqualTo("SceneRoots:"),
                "SceneRoots must be the final serialized document in a Unity scene.");
        }
    }
}
