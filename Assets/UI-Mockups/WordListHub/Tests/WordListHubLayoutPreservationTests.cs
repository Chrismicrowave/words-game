using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
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
        public void NavigationDetailsAndFilters_PreserveFixedLayoutAndDispatchFavoriteOnce()
        {
            WordListHubMockupController controller = harness.Controller;
            WordListHubSceneBindings bindings = harness.Bindings;
            UiSceneSnapshot snapshot = UiSceneSnapshot.Capture(harness.Root);
            InvokeAwake(controller);
            snapshot.AssertUnchanged("initial scene binding and refresh");

            AssertPageActive(bindings, WordListHubPage.Library);
            Press(bindings.navCommunity.button);
            AssertPageActive(bindings, WordListHubPage.Community);
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
            Press(bindings.navCommunity.button);
            AssertPageActive(bindings, WordListHubPage.Community);
            AssertExactlyOneSelectedCommunityRow(bindings.community,
                bindings.community.detailTitle.text);
            Press(bindings.navLibrary.button);
            AssertPageActive(bindings, WordListHubPage.Library);
            Assert.That(bindings.library.rowTemplate.gameObject.activeInHierarchy, Is.False,
                "The Library source template must stay hidden when its page is active.");

            RowAction libraryRow = FindFirstLibraryRow(bindings.library);
            Assert.That(libraryRow, Is.Not.Null, "The Library page should show at least one row.");
            Press(libraryRow.button);
            Assert.That(bindings.library.detailTitle.text, Is.EqualTo(libraryRow.title),
                "Selecting a row should refresh the authored detail title.");
            AssertExactlyOneSelectedLibraryRow(bindings.library, libraryRow.title);

            string favoriteLabelBefore = bindings.library.favorite.label.text;
            Press(bindings.library.favorite.button);
            Assert.That(bindings.library.favorite.label.text, Is.Not.EqualTo(favoriteLabelBefore),
                "A single click must toggle Favorite once; duplicate listeners would toggle it back.");
            AssertExactlyOneSelectedLibraryRow(bindings.library, libraryRow.title);

            SetDropdownValue(bindings.library.languageFilter, 1);
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

            snapshot.AssertUnchanged("navigation, row selection, filtering, and favorite click");
            snapshot.AssertOriginalObjectsStillExist("navigation, row selection, filtering, and favorite click");
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
            Assert.That(bindings.library.copyEditWords.label.text, Is.EqualTo("Copy & edit words"));
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
            Assert.That(bindings.library.copyEditWords.label.text, Is.EqualTo("Copy & edit words"));
            Assert.That(bindings.library.edit.button.gameObject.activeInHierarchy, Is.False,
                "The published original is immutable; use the shared Copy & edit words action.");
            AssertExactlyOneSelectedLibraryRow(bindings.library, published.Title);
            snapshot.AssertUnchanged("Community download");
            snapshot.AssertOriginalObjectsStillExist("Community download");
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
            return library.authoredRows
                .Where(row => row != null && row.root != null && row.root.activeInHierarchy
                    && row.selectedVisual != null && row.selectedVisual.activeSelf)
                .Select(row => row.title != null ? row.title.text : string.Empty)
                .Concat(library.listContent.GetComponentsInChildren<WordListHubLibraryRowView>(true)
                    .Where(row => row.gameObject.activeInHierarchy
                        && row.selectedVisual != null && row.selectedVisual.activeSelf)
                    .Select(row => row.title != null ? row.title.text : string.Empty));
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
            return community.authoredRows
                .Where(row => row != null && row.root != null && row.root.activeInHierarchy
                    && row.selectedVisual != null && row.selectedVisual.activeSelf)
                .Select(row => row.title != null ? row.title.text : string.Empty)
                .Concat(community.listContent.GetComponentsInChildren<WordListHubDiscoveryRowView>(true)
                    .Where(row => row.gameObject.activeInHierarchy
                        && row.selectedVisual != null && row.selectedVisual.activeSelf)
                    .Select(row => row.title != null ? row.title.text : string.Empty));
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
                AssertSelectedVisual(row.selectedVisual, row.root.transform,
                    "Authored Library row " + row.listId);
                Assert.That(row.button, Is.Not.Null);
                Assert.That(row.favoriteMark, Is.Not.Null);
                Assert.That(row.title, Is.Not.Null);
                Assert.That(row.metadata, Is.Not.Null);
            }
        }

        private static void AssertAuthoredCommunityRows(
            WordListHubSceneBindings.CommunityPageBinding community)
        {
            foreach (WordListHubSceneBindings.DiscoveryRowBinding row in community.authoredRows)
            {
                if (row == null)
                    continue;
                Assert.That(row.root, Is.Not.Null);
                AssertSelectedVisual(row.selectedVisual, row.root.transform,
                    "Authored Community row " + row.remoteItemId);
                Assert.That(row.button, Is.Not.Null);
                Assert.That(row.title, Is.Not.Null);
                Assert.That(row.metadata, Is.Not.Null);
                Assert.That(row.summary, Is.Not.Null);
                Assert.That(row.status, Is.Not.Null);
            }
        }

        private static void AssertLayoutGroup(Transform content, string description)
        {
            Assert.That(content, Is.Not.Null, description + " must be assigned.");
            Assert.That(content.GetComponent<LayoutGroup>(), Is.Not.Null,
                description + " must use the LayoutGroup configured in the Scene.");
        }

        private static void AssertSelectedVisual(GameObject selectedVisual, Transform rowRoot, string description)
        {
            Assert.That(selectedVisual, Is.Not.Null, description + " needs an authored SelectedState object.");
            Assert.That(selectedVisual.transform.parent, Is.EqualTo(rowRoot),
                description + " SelectedState must remain under its Scene-authored row.");
            Assert.That(selectedVisual.activeSelf, Is.False,
                description + " SelectedState must be hidden in the saved scene.");
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
            Assert.That(template.favoriteMark, Is.Not.Null);
            Assert.That(template.title, Is.Not.Null);
            Assert.That(template.metadata, Is.Not.Null);
            AssertReferenceWithin(template.transform, template.button.transform, "Library row button");
            AssertReferenceWithin(template.transform, template.favoriteMark.transform, "Library favorite text");
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
                AssertReferenceWithin(row.transform, row.favoriteMark.transform, row.name + ".favorite");
                AssertReferenceWithin(row.transform, row.title.transform, row.name + ".title");
                AssertReferenceWithin(row.transform, row.metadata.transform, row.name + ".metadata");
                if (row.badge != null)
                    AssertReferenceWithin(row.transform, row.badge.transform, row.name + ".badge");
                AssertTextStyleMatches(library.rowTemplate.favoriteMark, row.favoriteMark, row.name + ".favorite");
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

        private static int CountRuntimeChildren(Transform content)
        {
            return content.GetComponentsInChildren<WordListHubRuntimeInstance>(true)
                .Count(marker => marker != null && marker.transform.parent == content);
        }

        private static void InvokeAwake(WordListHubMockupController controller)
        {
            MethodInfo awake = typeof(WordListHubMockupController).GetMethod(
                "Awake", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(awake, Is.Not.Null, "The test needs a controller initialization entry point.");
            awake.Invoke(controller, null);
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

            public readonly GameObject Root;
            public readonly WordListHubMockupController Controller;
            public readonly WordListHubSceneBindings Bindings;

            private SceneHarness(
                Scene previousActiveScene,
                Scene sourceScene,
                bool openedSourceScene,
                Scene isolatedScene,
                bool createdIsolatedScene,
                GameObject root,
                WordListHubMockupController controller,
                WordListHubSceneBindings bindings)
            {
                this.previousActiveScene = previousActiveScene;
                this.sourceScene = sourceScene;
                this.openedSourceScene = openedSourceScene;
                this.isolatedScene = isolatedScene;
                this.createdIsolatedScene = createdIsolatedScene;
                Root = root;
                Controller = controller;
                Bindings = bindings;
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
                try
                {
                    WordListHubMockupController sourceController = FindController(sourceScene);
                    Assert.That(sourceController, Is.Not.Null,
                        "The saved mockup scene must contain WordListHubMockupController.");

                    root = UnityEngine.Object.Instantiate(sourceController.transform.root.gameObject);
                    SceneManager.MoveGameObjectToScene(root, isolatedScene);
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
                        isolatedScene, createdIsolatedScene, root, controller, bindings);
                }
                catch
                {
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

            public static UiSceneSnapshot Capture(GameObject root)
            {
                var snapshot = new UiSceneSnapshot();
                foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                    snapshot.objects.Add(new GameObjectIdentity(transform.gameObject));
                foreach (Component component in root.GetComponentsInChildren<Component>(true))
                {
                    if (component != null)
                        snapshot.components.Add(new ComponentIdentity(component));
                }

                foreach (RectTransform rect in root.GetComponentsInChildren<RectTransform>(true))
                {
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
                    snapshot.texts.Add(new TextStyleState(text, selectableGraphics.Contains(text)));
                foreach (Image image in root.GetComponentsInChildren<Image>(true))
                    snapshot.images.Add(new ImageStyleState(image, selectableGraphics.Contains(image)));
                foreach (Selectable selectable in sceneSelectables)
                    snapshot.selectables.Add(new SelectableStyleState(selectable));

                foreach (Component component in root.GetComponentsInChildren<Component>(true))
                {
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
}
