using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
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
        private const string MockupScenePath = "Assets/Scenes/UI-Mockups/WordListHubMockup.unity";

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
            foreach (WordListHubSceneBindings.LibraryRowBinding row in library.authoredRows
                .Where(row => row != null && row.root != null && row.root.activeInHierarchy))
            {
                RectTransform tile = row.root.transform as RectTransform;
                Assert.That(tile.rect.width, Is.EqualTo(270f).Within(0.1f));
                Assert.That(tile.rect.height, Is.EqualTo(270f).Within(0.1f));
            }

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
            Assert.That(visibleCommunityTiles.All(tile =>
                Mathf.Abs(tile.rect.width - 270f) < 0.1f && Mathf.Abs(tile.rect.height - 270f) < 0.1f), Is.True);
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
            Assert.That(bindings.modal.editList.wordEditorLayout.root, Is.SameAs(editor.root));
            communityDetailTitleStyle.AssertUnchanged("controller initialization Community detail title");
            libraryDetailTitleStyle.AssertUnchanged("controller initialization Library detail title");
            if (communityDetailTitle.text == communityDetailTitleText)
                Assert.That(communityDetailTitle.fontSize, Is.EqualTo(communityDetailTitleSize).Within(0.001f));
            if (libraryDetailTitle.text == libraryDetailTitleText)
                Assert.That(libraryDetailTitle.fontSize, Is.EqualTo(libraryDetailTitleSize).Within(0.001f));
        }
        [Test]
        public void CommunityRowsShowOnlyCenteredTitleAndMetadata()
        {
            WordListHubSceneBindings.CommunityPageBinding community = harness.Bindings.community;
            WordListHubDiscoveryRowView template = community.rowTemplate;
            RectTransform titleRect = template.title.rectTransform;
            RectTransform metadataRect = template.metadata.rectTransform;

            Assert.That(template.summary.gameObject.activeSelf, Is.False,
                "The selectable Community row should not show a description line.");
            AssertVector2(titleRect.anchoredPosition, new Vector2(0f, -72f),
                "Community title position");
            AssertVector2(metadataRect.anchoredPosition, new Vector2(0f, -144f),
                "Community metadata position");
            Assert.That(titleRect.sizeDelta, Is.EqualTo(new Vector2(230f, 64f)));
            Assert.That(metadataRect.sizeDelta, Is.EqualTo(new Vector2(230f, 54f)));

            float titleTop = 72f;
            float metadataBottom = 144f + metadataRect.rect.height;
            Assert.That((titleTop + metadataBottom) * 0.5f,
                Is.EqualTo(template.transform.GetComponent<RectTransform>().rect.height * 0.5f).Within(0.001f),
                "The title and metadata block should be vertically centered in its tile.");
            Assert.That(template.status.gameObject.activeSelf, Is.True);
            Assert.That(template.status.rectTransform.anchoredPosition.y,
                Is.EqualTo(-230f).Within(0.001f), "Keep the status in the lower tile band.");

            WordListHubDiscoveryRowView dynamicClone = UnityEngine.Object.Instantiate(
                template, community.listContent, false);
            try
            {
                Assert.That(dynamicClone.summary.gameObject.activeSelf, Is.False,
                    "Runtime Community rows should inherit the hidden description line.");
                Assert.That(dynamicClone.title.rectTransform.anchoredPosition.y,
                    Is.EqualTo(-72f).Within(0.001f));
                Assert.That(dynamicClone.metadata.rectTransform.anchoredPosition.y,
                    Is.EqualTo(-144f).Within(0.001f));
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
            FieldInfo expiryField = typeof(WordListHubMockupController).GetField(
                "actionFeedbackExpiresAt", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(showFeedback, Is.Not.Null);
            Assert.That(expiryField, Is.Not.Null);

            showFeedback.Invoke(controller, new object[] { "Published this list.", false });
            Assert.That(bindings.library.detailStatus.text, Is.EqualTo("Published this list."));
            Assert.That(bindings.footer.text, Is.EqualTo("LOCAL MOCKUP  ·  SAMPLE DATA ONLY"),
                "The footer should keep its independent sample-data notice.");
            float successExpiry = (float)expiryField.GetValue(controller);
            Assert.That(successExpiry - Time.unscaledTime,
                Is.EqualTo(3f).Within(0.1f), "Successful action feedback should last three unscaled seconds.");

            Press(bindings.navCommunity.button);
            Press(bindings.navLibrary.button);
            SetDropdownValue(bindings.library.languageFilter, 1);
            SetInputValue(bindings.library.searchInput, "Blank Canvas");
            Assert.That(bindings.library.detailStatus.text, Is.EqualTo("Published this list."),
                "Passive navigation, filters, and search should not clear a live action message.");

            expiryField.SetValue(controller, Time.unscaledTime - 0.1f);
            InvokeControllerUpdate(controller);
            Assert.That(bindings.library.detailStatus.text, Is.Empty,
                "The action prompt should clear after its unscaled deadline.");

            showFeedback.Invoke(controller, new object[] { "That action failed.", true });
            Assert.That(bindings.library.detailStatus.text, Is.EqualTo("That action failed."));
            float failureExpiry = (float)expiryField.GetValue(controller);
            Assert.That(failureExpiry - Time.unscaledTime,
                Is.EqualTo(4f).Within(0.1f), "Failure feedback should remain visible for four unscaled seconds.");
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
                Assert.That(row.summary, Is.Not.Null);
                Assert.That(row.summary.gameObject.activeSelf, Is.False,
                    "Authored Community selection rows should hide the description line.");
                Assert.That(row.status, Is.Not.Null);
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
            AssertReferenceWithin(template.transform, template.button.transform, "Library row button");
            AssertReferenceWithin(template.transform, template.favoriteButton.transform, "Library favorite button");
            AssertReferenceWithin(template.transform, template.favoriteMark.transform, "Library favorite text");
            AssertReferenceWithin(template.transform, template.bestTime.transform, "Library best time text");
            AssertReferenceWithin(template.transform, template.title.transform, "Library title text");
            AssertReferenceWithin(template.transform, template.metadata.transform, "Library metadata text");
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
            Assert.That(template.summary, Is.Not.Null);
            Assert.That(template.summary.gameObject.activeSelf, Is.False,
                "Community selection rows should hide the description line.");
            Assert.That(template.status, Is.Not.Null);
            AssertReferenceWithin(template.transform, template.button.transform, "Community row button");
            AssertReferenceWithin(template.transform, template.title.transform, "Community title text");
            AssertReferenceWithin(template.transform, template.metadata.transform, "Community metadata text");
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
                if (row.badge != null)
                    AssertReferenceWithin(row.transform, row.badge.transform, row.name + ".badge");
                AssertTextStyleMatches(library.rowTemplate.favoriteMark, row.favoriteMark, row.name + ".favorite");
                AssertTextStyleMatches(library.rowTemplate.bestTime, row.bestTime, row.name + ".bestTime");
                AssertTextRendersWithin(row.bestTime, row.name + ".bestTime");
                AssertTextStyleMatches(library.rowTemplate.title, row.title, row.name + ".title");
                AssertTextStyleMatches(library.rowTemplate.metadata, row.metadata, row.name + ".metadata");
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
                AssertReferenceWithin(row.transform, row.summary.transform, row.name + ".summary");
                AssertReferenceWithin(row.transform, row.status.transform, row.name + ".status");
                AssertTextStyleMatches(community.rowTemplate.title, row.title, row.name + ".title");
                AssertTextStyleMatches(community.rowTemplate.metadata, row.metadata, row.name + ".metadata");
                AssertTextStyleMatches(community.rowTemplate.summary, row.summary, row.name + ".summary");
                AssertTextStyleMatches(community.rowTemplate.status, row.status, row.name + ".status");
                Assert.That(row.summary.gameObject.activeSelf, Is.False,
                    row.name + " must not display the Community description line.");

                RectTransform rowRect = row.transform as RectTransform;
                AssertRectContained(row.title.rectTransform, rowRect, row.name + ".title");
                AssertRectContained(row.metadata.rectTransform, rowRect, row.name + ".metadata");
                AssertRectContained(row.summary.rectTransform, rowRect, row.name + ".summary");
                AssertRectContained(row.status.rectTransform, rowRect, row.name + ".status");
                AssertTextRendersWithin(row.title, row.name + ".title");
                AssertTextRendersWithin(row.metadata, row.name + ".metadata");
                AssertTextRendersWithin(row.status, row.name + ".status");
                AssertNoRectOverlap(row.title.rectTransform, row.metadata.rectTransform,
                    row.name + " title and metadata");
                AssertNoRectOverlap(row.title.rectTransform, row.status.rectTransform,
                    row.name + " title and status");
                AssertNoRectOverlap(row.metadata.rectTransform, row.status.rectTransform,
                    row.name + " metadata and status");
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
            bool wraps = false)
        {
            AssertVector2(text.rectTransform.anchoredPosition, position, description + " position");
            AssertVector2(text.rectTransform.sizeDelta, size, description + " size");
            Assert.That(text.textWrappingMode,
                Is.EqualTo(wraps ? TextWrappingModes.Normal : TextWrappingModes.NoWrap), description + " wrapping");
            Assert.That(text.overflowMode, Is.EqualTo(TextOverflowModes.Ellipsis), description + " overflow");
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
            RectTransform communityRow = bindings.community.rowTemplate.GetComponent<RectTransform>();
            LayoutElement communityRowLayout = bindings.community.rowTemplate.GetComponent<LayoutElement>();
            Assert.That(communityRow.rect.width, Is.EqualTo(270f).Within(0.001f));
            Assert.That(communityRow.rect.height, Is.EqualTo(270f).Within(0.001f));
            Assert.That(communityRowLayout.preferredWidth, Is.EqualTo(270f).Within(0.001f));
            Assert.That(communityRowLayout.preferredHeight, Is.EqualTo(270f).Within(0.001f));
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
            Assert.That(bindings.community.rowTemplate.status.fontSize, Is.EqualTo(15f).Within(0.001f));
            Assert.That(bindings.community.rowTemplate.summary.textWrappingMode,
                Is.EqualTo(TextWrappingModes.NoWrap));
            Assert.That(bindings.community.rowTemplate.summary.overflowMode,
                Is.EqualTo(TextOverflowModes.Ellipsis));
            AssertCommunityLabelLayout(bindings.community.rowTemplate.title,
                new Vector2(0f, -72f), new Vector2(230f, 64f), "Community row title", true);
            AssertCommunityLabelLayout(bindings.community.rowTemplate.metadata,
                new Vector2(0f, -144f), new Vector2(230f, 54f), "Community row metadata", true);
            AssertCommunityLabelLayout(bindings.community.rowTemplate.status,
                new Vector2(0f, -230f), new Vector2(205f, 24f), "Community row status");
            Assert.That(bindings.community.rowTemplate.status.alignment,
                Is.EqualTo(TextAlignmentOptions.Center));

            RectTransform communitySearch = bindings.community.searchInput.GetComponent<RectTransform>();
            Assert.That(communitySearch.rect.size, Is.EqualTo(new Vector2(450f, 42f)),
                "Preserve the authored Community search width from the saved mockup scene.");
            RectTransform communityConnection = bindings.community.connectionState.GetComponent<RectTransform>();
            RectTransform libraryLanguage = bindings.library.languageFilter.GetComponent<RectTransform>();
            Assert.That(communityConnection.rect.size, Is.EqualTo(libraryLanguage.rect.size));

            Transform communityListTitle = bindings.community.root.transform
                .Find("CommunityListPane/CommunityListTitle");
            Transform libraryListTitle = bindings.library.root.transform
                .Find("LibraryListPane/ListPaneTitle");
            Assert.That(communityListTitle.GetComponent<TMP_Text>().fontSize,
                Is.EqualTo(libraryListTitle.GetComponent<TMP_Text>().fontSize).Within(0.001f));
            TMP_Text communityDetailTitle = bindings.community.detailTitle;
            TMP_Text libraryDetailTitle = bindings.library.detailTitle;
            Assert.That(communityDetailTitle.font, Is.SameAs(libraryDetailTitle.font));
            Assert.That(communityDetailTitle.enableAutoSizing, Is.True);
            Assert.That(libraryDetailTitle.enableAutoSizing, Is.True);
            Assert.That(communityDetailTitle.fontSizeMin, Is.EqualTo(23f).Within(0.001f));
            Assert.That(communityDetailTitle.fontSizeMax, Is.EqualTo(29f).Within(0.001f));
            Assert.That(libraryDetailTitle.fontSizeMin, Is.EqualTo(23f).Within(0.001f));
            Assert.That(libraryDetailTitle.fontSizeMax, Is.EqualTo(29f).Within(0.001f));
            Assert.That(communityDetailTitle.fontSize,
                Is.InRange(communityDetailTitle.fontSizeMin, communityDetailTitle.fontSizeMax));
            Assert.That(libraryDetailTitle.fontSize,
                Is.InRange(libraryDetailTitle.fontSizeMin, libraryDetailTitle.fontSizeMax));
            Assert.That(bindings.community.detailAuthor.fontSize,
                Is.EqualTo(bindings.library.detailMetadata.fontSize).Within(0.001f));
            Assert.That(bindings.community.detailDescription.fontSize,
                Is.EqualTo(bindings.library.detailDescription.fontSize).Within(0.001f));
            Assert.That(bindings.community.detailSummary.fontSize,
                Is.EqualTo(bindings.library.detailWords.fontSize).Within(0.001f));
            Assert.That(bindings.community.like.button.GetComponent<RectTransform>().rect.height,
                Is.EqualTo(bindings.library.play.button.GetComponent<RectTransform>().rect.height).Within(0.001f));
            Assert.That(bindings.community.download.button.GetComponent<RectTransform>().rect.height,
                Is.EqualTo(bindings.library.play.button.GetComponent<RectTransform>().rect.height).Within(0.001f));
            Assert.That(bindings.community.play.button.GetComponent<RectTransform>().rect.height,
                Is.EqualTo(bindings.library.play.button.GetComponent<RectTransform>().rect.height).Within(0.001f));

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
            Rect firstBounds = new Rect(first.anchoredPosition - first.rect.size * 0.5f, first.rect.size);
            Rect secondBounds = new Rect(second.anchoredPosition - second.rect.size * 0.5f, second.rect.size);
            Assert.That(firstBounds.Overlaps(secondBounds), Is.False, description + " must not overlap.");
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
                && Mathf.Abs(rect.rect.width - 270f) < 0.1f
                && Mathf.Abs(rect.rect.height - 270f) < 0.1f;
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

        private static void InvokeRefreshLibraryPage(WordListHubMockupController controller)
        {
            MethodInfo refresh = typeof(WordListHubMockupController).GetMethod(
                "RefreshLibraryPage", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(refresh, Is.Not.Null);
            refresh.Invoke(controller, new object[] { false, false });
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
    public sealed class WordListHubSceneSerializationTests
    {
        private const string MockupScenePath = "Assets/Scenes/UI-Mockups/WordListHubMockup.unity";

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
