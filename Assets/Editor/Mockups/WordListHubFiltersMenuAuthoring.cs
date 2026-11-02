#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Words.UiMockups.WordListHub;

namespace Words.UiMockups.Editor
{
    /// <summary>
    /// Authors only the Library filters popup, leaving the rest of the existing scene hierarchy untouched.
    /// </summary>
    public static class WordListHubFiltersMenuAuthoring
    {
        private const string ScenePath = "Assets/Scenes/UI-Mockups/WordListHubMockup.unity";
        private const string PopupName = "LibraryFiltersPopup";
        private const string ToggleName = "FiltersToggle";
        private const string ViewportName = "Viewport";
        private const string ContentName = "Content";
        private const string FilterLanguageName = "Filter_Language";
        private const string FilterPlayedName = "Filter_Played";
        private const string FilterSortName = "Filter_Sort";
        private const string FilterDirectionName = "Filter_Direction";
        private const string CommunityToggleName = "MockConnectionStateToggle";
        private const string CommunityPopupName = "MockConnectionStatePopup";
        private const string AchievementsToggleName = "AchievementFilterToggle";
        private const string AchievementsPopupName = "AchievementFilterPopup";

        private static readonly Color Ink = new Color(0.17f, 0.17f, 0.15f, 1f);
        private static readonly Color MutedInk = new Color(0.39f, 0.39f, 0.35f, 1f);
        private static readonly Color Accent = new Color(0.722f, 0.467f, 0.353f, 1f);
        private static readonly Color Paper = new Color(0.963f, 0.953f, 0.889f, 1f);
        private static readonly Color PaperAlt = new Color(0.925f, 0.925f, 0.846f, 1f);

        private sealed class SectionSpec
        {
            public readonly WordListHubSceneBindings.LibraryFilterKind kind;
            public readonly string title;
            public readonly string[] options;

            public SectionSpec(
                WordListHubSceneBindings.LibraryFilterKind kind,
                string title,
                params string[] options)
            {
                this.kind = kind;
                this.title = title;
                this.options = options;
            }
        }

        private static readonly SectionSpec[] Sections =
        {
            new SectionSpec(WordListHubSceneBindings.LibraryFilterKind.Order, "Order",
                "Ascending", "Descending"),
            new SectionSpec(WordListHubSceneBindings.LibraryFilterKind.Language, "Language",
                "All", "English", "Chinese", "Mixed"),
            new SectionSpec(WordListHubSceneBindings.LibraryFilterKind.Played, "Played",
                "All", "Yes", "No"),
            new SectionSpec(WordListHubSceneBindings.LibraryFilterKind.Sort, "Sort",
                "Recently played", "Name", "Date added", "Date modified", "Word count", "Personal stars")
        };

        [MenuItem("Tools/Mockups/Author Word List Hub Filters Menu")]
        public static void AuthorFiltersMenuOnly()
        {
            if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play mode before authoring the Word List Hub filters menu.");
            if (EditorApplication.isCompiling)
                throw new InvalidOperationException("Wait for script compilation before authoring the filters menu.");

            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException("Open " + ScenePath + " before authoring its filters menu.");
            if (scene.isDirty)
                throw new InvalidOperationException("The Word List Hub scene has unsaved edits; save them before authoring the filters menu.");

            WordListHubMockupController controller = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<WordListHubMockupController>(true))
                .SingleOrDefault();
            if (controller == null)
                throw new InvalidOperationException("The open scene does not contain one WordListHubMockupController.");

            WordListHubSceneBindings bindings = controller.GetComponent<WordListHubSceneBindings>();
            if (bindings == null || bindings.library == null || bindings.library.root == null)
                throw new InvalidOperationException("The controller has no serialized Library page binding.");

            Transform libraryRoot = bindings.library.root.transform;
            Undo.RegisterFullObjectHierarchyUndo(libraryRoot.gameObject, "Author Word List Hub filters menu");
            EnsureForMigration(libraryRoot, controller);
            EditorUtility.SetDirty(bindings);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException("Unity could not save the authored filters menu.");
            Debug.Log("[WordListHubFiltersMenuAuthoring] Authored the Library Filters popup in " + ScenePath + ".");
        }

        /// <summary>Creates the menu before the safe full migration captures its existing Library signature.</summary>
        public static void EnsureForMigration(Transform libraryRoot, WordListHubMockupController controller)
        {
            if (libraryRoot == null)
                throw new ArgumentNullException(nameof(libraryRoot));
            if (controller == null)
                throw new ArgumentNullException(nameof(controller));

            WordListHubSceneBindings bindings = controller.GetComponent<WordListHubSceneBindings>();
            if (bindings == null)
                bindings = Undo.AddComponent<WordListHubSceneBindings>(controller.gameObject);
            if (bindings.library == null)
                bindings.library = new WordListHubSceneBindings.LibraryPageBinding();
            bindings.library.root = libraryRoot.gameObject;

            Button buttonTemplate = RequireChild<Button>(libraryRoot, "View_All");
            TMP_Text textTemplate = buttonTemplate.GetComponentInChildren<TMP_Text>(true);
            if (textTemplate == null)
                throw new InvalidOperationException("The authored View_All button has no TMP label to use as a style template.");

            HideLegacyFilter(libraryRoot, FilterLanguageName);
            HideLegacyFilter(libraryRoot, FilterPlayedName);
            HideLegacyFilter(libraryRoot, FilterSortName);
            HideLegacyFilter(libraryRoot, FilterDirectionName);

            Button toggle = EnsureButtonClone(libraryRoot, ToggleName, buttonTemplate);
            TMP_Text toggleLabel = toggle.GetComponentInChildren<TMP_Text>(true);
            if (toggleLabel == null)
                throw new InvalidOperationException("The authored Filters trigger has no TMP label.");
            ConfigureToggle(toggle, toggleLabel, new Vector2(96f, 267f), new Vector2(200f, 40f), "Filters");

            GameObject popup = EnsurePopup(libraryRoot, PopupName, buttonTemplate.GetComponent<Image>());
            RectTransform popupRect = popup.GetComponent<RectTransform>();
            ConfigurePopupRect(popupRect, new Vector2(-4f, 240f), new Vector2(380f, 400f));
            ScrollRect scroll = EnsureComponent<ScrollRect>(popup);
            Image popupImage = EnsureComponent<Image>(popup);
            ConfigurePopupImage(popupImage, buttonTemplate.GetComponent<Image>());

            Transform viewport = EnsureViewport(popup.transform, buttonTemplate.GetComponent<Image>());
            Transform content = EnsureContent(viewport, textTemplate);
            scroll.content = content as RectTransform;
            scroll.viewport = viewport as RectTransform;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = true;
            scroll.scrollSensitivity = 30f;

            var queryDefaults = new MockupLibraryQuery();
            var sectionBindings = new List<WordListHubSceneBindings.FilterSectionBinding>(Sections.Length);
            foreach (SectionSpec spec in Sections)
            {
                TMP_Text heading = EnsureHeading(content, spec, textTemplate);
                var optionBindings = new List<WordListHubSceneBindings.FilterOptionBinding>(spec.options.Length);
                for (int value = 0; value < spec.options.Length; value++)
                {
                    Button optionButton = EnsureOptionButton(content, spec, value, spec.options[value], buttonTemplate);
                    TMP_Text optionLabel = optionButton.GetComponentInChildren<TMP_Text>(true);
                    GameObject selectedVisual = EnsureSelectedVisual(optionButton, buttonTemplate.GetComponent<Image>());
                    bool selected = IsSelected(spec.kind, value, queryDefaults);
                    selectedVisual.SetActive(selected);
                    optionLabel.text = (selected ? "✓  " : "    ") + spec.options[value];
                    optionBindings.Add(new WordListHubSceneBindings.FilterOptionBinding
                    {
                        value = value,
                        optionLabel = spec.options[value],
                        button = optionButton,
                        label = optionLabel,
                        selectedVisual = selectedVisual
                    });
                }

                sectionBindings.Add(new WordListHubSceneBindings.FilterSectionBinding
                {
                    kind = spec.kind,
                    heading = heading,
                    options = optionBindings
                });
            }

            // Keep the popup above all existing Library controls and rows; append only new roots.
            popup.transform.SetAsLastSibling();
            toggle.transform.SetSiblingIndex(popup.transform.GetSiblingIndex());
            popup.transform.SetAsLastSibling();
            popup.SetActive(false);

            bindings.library.filtersMenu = new WordListHubSceneBindings.FilterMenuBinding
            {
                toggleButton = toggle,
                toggleLabel = toggleLabel,
                toggleBaseLabel = "Filters",
                panel = popup,
                scrollRect = scroll,
                content = content,
                sections = sectionBindings
            };
            EditorUtility.SetDirty(bindings);
        }

        public static void BindExistingMenu(
            Transform libraryRoot,
            WordListHubSceneBindings.LibraryPageBinding library)
        {
            if (libraryRoot == null || library == null)
                return;

            Transform toggleRoot = libraryRoot.Find(ToggleName);
            Transform popupRoot = libraryRoot.Find(PopupName);
            if (toggleRoot == null || popupRoot == null)
                throw new InvalidOperationException("The authored Filters trigger or popup is missing.");

            Button toggle = RequireComponent<Button>(toggleRoot.gameObject);
            TMP_Text toggleLabel = toggle.GetComponentInChildren<TMP_Text>(true);
            ScrollRect scroll = RequireComponent<ScrollRect>(popupRoot.gameObject);
            Transform viewport = popupRoot.Find(ViewportName);
            Transform content = viewport != null ? viewport.Find(ContentName) : null;
            if (viewport == null || content == null)
                throw new InvalidOperationException("The authored Filters popup is missing its Viewport or Content.");

            var sectionBindings = new List<WordListHubSceneBindings.FilterSectionBinding>(Sections.Length);
            foreach (SectionSpec spec in Sections)
            {
                TMP_Text heading = FindText(content, "SectionHeading_" + spec.kind);
                var options = new List<WordListHubSceneBindings.FilterOptionBinding>(spec.options.Length);
                for (int value = 0; value < spec.options.Length; value++)
                {
                    Transform optionRoot = content.Find(OptionName(spec, value));
                    if (optionRoot == null)
                        throw new InvalidOperationException("The authored filter option is missing: " + OptionName(spec, value));
                    Button button = RequireComponent<Button>(optionRoot.gameObject);
                    TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
                    Transform selectedVisual = optionRoot.Find("SelectedVisual");
                    options.Add(new WordListHubSceneBindings.FilterOptionBinding
                    {
                        value = value,
                        optionLabel = spec.options[value],
                        button = button,
                        label = label,
                        selectedVisual = selectedVisual != null ? selectedVisual.gameObject : null
                    });
                }

                sectionBindings.Add(new WordListHubSceneBindings.FilterSectionBinding
                {
                    kind = spec.kind,
                    heading = heading,
                    options = options
                });
            }

            library.filtersMenu = new WordListHubSceneBindings.FilterMenuBinding
            {
                toggleButton = toggle,
                toggleLabel = toggleLabel,
                toggleBaseLabel = "Filters",
                panel = popupRoot.gameObject,
                scrollRect = scroll,
                content = content,
                sections = sectionBindings
            };
        }

        public static void EnsureCommunityMenuForMigration(Transform pageRoot, Button buttonTemplate)
        {
            EnsureCompactMenuForMigration(
                pageRoot,
                buttonTemplate,
                "MockConnectionState",
                CommunityToggleName,
                CommunityPopupName,
                new[]
                {
                    new SectionSpec(WordListHubSceneBindings.LibraryFilterKind.CommunityOrder,
                        "Order", "Ascending", "Descending"),
                    new SectionSpec(WordListHubSceneBindings.LibraryFilterKind.CommunityLanguage,
                        "Language", "All", "English", "Chinese", "Mixed"),
                    new SectionSpec(WordListHubSceneBindings.LibraryFilterKind.CommunityDownloadState,
                        "Download", "All", "Downloaded", "Not downloaded"),
                    new SectionSpec(WordListHubSceneBindings.LibraryFilterKind.CommunitySort,
                        "Sort", "Default", "Name", "Likes", "Word count")
                },
                new Vector2(0f, 267f),
                new Vector2(200f, 40f),
                0,
                preserveExistingGeometry: true);
        }

        public static void EnsureAchievementsMenuForMigration(Transform pageRoot, Button buttonTemplate)
        {
            EnsureCompactMenuForMigration(
                pageRoot,
                buttonTemplate,
                "AchievementFilter",
                AchievementsToggleName,
                AchievementsPopupName,
                new[]
                {
                    new SectionSpec(WordListHubSceneBindings.LibraryFilterKind.AchievementStatus,
                        "Show", "All", "Unlocked", "In progress")
                },
                new Vector2(0f, 216f),
                new Vector2(210f, 38f),
                0);
        }

        public static void BindExistingCommunityMenu(
            Transform pageRoot,
            WordListHubSceneBindings.CommunityPageBinding community)
        {
            if (community == null)
                return;
            community.connectionMenu = BindExistingCompactMenu(
                pageRoot,
                "MockConnectionState",
                CommunityToggleName,
                CommunityPopupName,
                new[]
                {
                    new SectionSpec(WordListHubSceneBindings.LibraryFilterKind.CommunityOrder,
                        "Order", "Ascending", "Descending"),
                    new SectionSpec(WordListHubSceneBindings.LibraryFilterKind.CommunityLanguage,
                        "Language", "All", "English", "Chinese", "Mixed"),
                    new SectionSpec(WordListHubSceneBindings.LibraryFilterKind.CommunityDownloadState,
                        "Download", "All", "Downloaded", "Not downloaded"),
                    new SectionSpec(WordListHubSceneBindings.LibraryFilterKind.CommunitySort,
                        "Sort", "Default", "Name", "Likes", "Word count")
                });
        }

        public static void BindExistingAchievementsMenu(
            Transform pageRoot,
            WordListHubSceneBindings.AchievementsPageBinding achievements)
        {
            if (achievements == null)
                return;
            achievements.filterMenu = BindExistingCompactMenu(
                pageRoot,
                "AchievementFilter",
                AchievementsToggleName,
                AchievementsPopupName,
                new[]
                {
                    new SectionSpec(WordListHubSceneBindings.LibraryFilterKind.AchievementStatus,
                        "Show", "All", "Unlocked", "In progress")
                });
        }

        private static void EnsureCompactMenuForMigration(
            Transform pageRoot,
            Button buttonTemplate,
            string legacyDropdownName,
            string toggleName,
            string popupName,
            SectionSpec[] sections,
            Vector2 togglePosition,
            Vector2 toggleSize,
            int selectedValue,
            bool preserveExistingGeometry = false)
        {
            if (pageRoot == null)
                throw new ArgumentNullException(nameof(pageRoot));
            if (buttonTemplate == null)
                throw new ArgumentNullException(nameof(buttonTemplate));
            if (sections == null || sections.Length == 0)
                throw new ArgumentException("At least one filter section is required.", nameof(sections));

            HideLegacyFilter(pageRoot, legacyDropdownName);
            Image templateImage = buttonTemplate.GetComponent<Image>();
            TMP_Text textTemplate = buttonTemplate.GetComponentInChildren<TMP_Text>(true);
            if (textTemplate == null)
                throw new InvalidOperationException("The View_All style button has no TMP label.");

            bool hadToggle = pageRoot.Find(toggleName) != null;
            bool hadPopup = pageRoot.Find(popupName) != null;
            Button toggle = EnsureButtonClone(pageRoot, toggleName, buttonTemplate);
            TMP_Text toggleLabel = toggle.GetComponentInChildren<TMP_Text>(true);
            if (toggleLabel == null)
                throw new InvalidOperationException("The filter trigger has no TMP label: " + toggleName);
            string toggleBaseLabel = preserveExistingGeometry ? "Filters" : sections[0].title;
            if (preserveExistingGeometry && hadToggle)
                ConfigureToggleAppearance(toggle, toggleLabel, toggleBaseLabel);
            else
                ConfigureToggle(toggle, toggleLabel, togglePosition, toggleSize, toggleBaseLabel);

            GameObject popup = EnsurePopup(pageRoot, popupName, templateImage);
            RectTransform popupRect = popup.GetComponent<RectTransform>();
            float contentHeight = 20f + sections.Length * 26f
                + sections.Sum(section => section.options.Length * 36f)
                + Mathf.Max(0, sections.Length - 1) * 2f;
            float popupHeight = preserveExistingGeometry ? 400f : contentHeight + 8f;
            if (preserveExistingGeometry && hadPopup)
            {
                if (popupRect.sizeDelta.y < popupHeight)
                    popupRect.sizeDelta = new Vector2(popupRect.sizeDelta.x, popupHeight);
            }
            else
            {
                ConfigurePopupRect(popupRect, new Vector2(togglePosition.x, togglePosition.y - 27f),
                    new Vector2(380f, popupHeight));
            }

            ScrollRect scroll = EnsureComponent<ScrollRect>(popup);
            ConfigurePopupImage(EnsureComponent<Image>(popup), templateImage);
            Transform viewport = EnsureViewport(popup.transform, templateImage);
            Transform content = EnsureContent(viewport, textTemplate);
            scroll.content = content as RectTransform;
            scroll.viewport = viewport as RectTransform;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = true;
            scroll.scrollSensitivity = 30f;

            if (preserveExistingGeometry)
                ReuseLegacyCommunitySection(content);

            var sectionBindings = new List<WordListHubSceneBindings.FilterSectionBinding>(sections.Length);
            foreach (SectionSpec section in sections)
            {
                EnsureHeading(content, section, textTemplate);
                var options = new List<WordListHubSceneBindings.FilterOptionBinding>(section.options.Length);
                for (int value = 0; value < section.options.Length; value++)
                {
                    Button optionButton = EnsureOptionButton(
                        content, section, value, section.options[value], buttonTemplate);
                    TMP_Text optionLabel = optionButton.GetComponentInChildren<TMP_Text>(true);
                    GameObject selectedVisual = EnsureSelectedVisual(optionButton, templateImage);
                    bool selected = value == selectedValue;
                    selectedVisual.SetActive(selected);
                    optionLabel.text = (selected ? "✓  " : "    ") + section.options[value];
                    options.Add(new WordListHubSceneBindings.FilterOptionBinding
                    {
                        value = value,
                        optionLabel = section.options[value],
                        button = optionButton,
                        label = optionLabel,
                        selectedVisual = selectedVisual
                    });
                }

                sectionBindings.Add(new WordListHubSceneBindings.FilterSectionBinding
                {
                    kind = section.kind,
                    heading = FindText(content, "SectionHeading_" + section.kind),
                    options = options
                });
            }

            if (!preserveExistingGeometry)
            {
                popup.transform.SetAsLastSibling();
                toggle.transform.SetSiblingIndex(popup.transform.GetSiblingIndex());
                popup.transform.SetAsLastSibling();
            }
            popup.SetActive(false);
        }

        private static void ReuseLegacyCommunitySection(Transform content)
        {
            Transform heading = content.Find("SectionHeading_MockConnectionState");
            if (heading != null && content.Find("SectionHeading_CommunityOrder") == null)
                heading.name = "SectionHeading_CommunityOrder";

            for (int value = 0; value < 2; value++)
            {
                Transform option = content.Find("FilterOption_MockConnectionState_" + value);
                if (option != null && content.Find("FilterOption_CommunityOrder_" + value) == null)
                    option.name = "FilterOption_CommunityOrder_" + value;
            }
        }

        private static WordListHubSceneBindings.FilterMenuBinding BindExistingCompactMenu(
            Transform pageRoot,
            string legacyDropdownName,
            string toggleName,
            string popupName,
            SectionSpec[] sections)
        {
            if (pageRoot == null)
                throw new InvalidOperationException("The page root for " + popupName + " is missing.");
            HideLegacyFilter(pageRoot, legacyDropdownName);

            Transform toggleRoot = pageRoot.Find(toggleName);
            Transform popupRoot = pageRoot.Find(popupName);
            if (toggleRoot == null || popupRoot == null)
                throw new InvalidOperationException("The filter trigger or popup is missing: " + toggleName);

            Button toggle = RequireComponent<Button>(toggleRoot.gameObject);
            TMP_Text toggleLabel = toggle.GetComponentInChildren<TMP_Text>(true);
            ScrollRect scroll = RequireComponent<ScrollRect>(popupRoot.gameObject);
            Transform viewport = popupRoot.Find(ViewportName);
            Transform content = viewport != null ? viewport.Find(ContentName) : null;
            if (viewport == null || content == null)
                throw new InvalidOperationException("The filter popup is missing its Viewport or Content: " + popupName);

            var sectionBindings = new List<WordListHubSceneBindings.FilterSectionBinding>(sections.Length);
            foreach (SectionSpec section in sections)
            {
                TMP_Text heading = FindText(content, "SectionHeading_" + section.kind);
                if (heading == null)
                    throw new InvalidOperationException("The filter section heading is missing: " + section.kind);

                var options = new List<WordListHubSceneBindings.FilterOptionBinding>(section.options.Length);
                for (int value = 0; value < section.options.Length; value++)
                {
                    Transform optionRoot = content.Find(OptionName(section, value));
                    if (optionRoot == null)
                        throw new InvalidOperationException("The filter option is missing: " + OptionName(section, value));
                    Button button = RequireComponent<Button>(optionRoot.gameObject);
                    TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
                    Transform selectedVisual = optionRoot.Find("SelectedVisual");
                    options.Add(new WordListHubSceneBindings.FilterOptionBinding
                    {
                        value = value,
                        optionLabel = section.options[value],
                        button = button,
                        label = label,
                        selectedVisual = selectedVisual != null ? selectedVisual.gameObject : null
                    });
                }

                sectionBindings.Add(new WordListHubSceneBindings.FilterSectionBinding
                {
                    kind = section.kind,
                    heading = heading,
                    options = options
                });
            }

            return new WordListHubSceneBindings.FilterMenuBinding
            {
                toggleButton = toggle,
                toggleLabel = toggleLabel,
                toggleBaseLabel = sections.Length == 1 ? sections[0].title : "Filters",
                panel = popupRoot.gameObject,
                scrollRect = scroll,
                content = content,
                sections = sectionBindings
            };
        }

        private static GameObject EnsurePopup(Transform root, string popupName, Image templateImage)
        {
            Transform existing = root.Find(popupName);
            if (existing != null)
                return existing.gameObject;

            GameObject popup = new GameObject(popupName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ScrollRect));
            Undo.RegisterCreatedObjectUndo(popup, "Create " + popupName);
            popup.transform.SetParent(root, false);
            Image image = popup.GetComponent<Image>();
            ConfigurePopupImage(image, templateImage);
            return popup;
        }

        private static Transform EnsureViewport(Transform popup, Image templateImage)
        {
            Transform viewport = popup.Find(ViewportName);
            if (viewport == null)
            {
                GameObject viewportObject = new GameObject(ViewportName,
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask));
                Undo.RegisterCreatedObjectUndo(viewportObject, "Create Library Filters viewport");
                viewportObject.transform.SetParent(popup, false);
                viewport = viewportObject.transform;
            }

            RectTransform rect = viewport as RectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(4f, 4f);
            rect.offsetMax = new Vector2(-4f, -4f);
            Image image = EnsureComponent<Image>(viewport.gameObject);
            image.sprite = templateImage != null ? templateImage.sprite : null;
            image.type = templateImage != null ? templateImage.type : Image.Type.Sliced;
            image.color = Paper;
            image.raycastTarget = true;
            EnsureComponent<Mask>(viewport.gameObject).showMaskGraphic = false;
            return viewport;
        }

        private static Transform EnsureContent(Transform viewport, TMP_Text textTemplate)
        {
            Transform content = viewport.Find(ContentName);
            if (content == null)
            {
                GameObject contentObject = new GameObject(ContentName,
                    typeof(RectTransform), typeof(ContentSizeFitter), typeof(VerticalLayoutGroup));
                Undo.RegisterCreatedObjectUndo(contentObject, "Create Library Filters content");
                contentObject.transform.SetParent(viewport, false);
                content = contentObject.transform;
            }

            RectTransform rect = content as RectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;

            ContentSizeFitter fitter = EnsureComponent<ContentSizeFitter>(content.gameObject);
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            VerticalLayoutGroup layout = EnsureComponent<VerticalLayoutGroup>(content.gameObject);
            layout.padding = new RectOffset(10, 10, 10, 10);
            layout.spacing = 2f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childScaleWidth = false;
            layout.childScaleHeight = false;
            return content;
        }

        private static TMP_Text EnsureHeading(Transform content, SectionSpec spec, TMP_Text template)
        {
            string name = "SectionHeading_" + spec.kind;
            TMP_Text heading = FindText(content, name);
            if (heading == null)
            {
                heading = UnityEngine.Object.Instantiate(template, content, false);
                Undo.RegisterCreatedObjectUndo(heading.gameObject, "Create " + spec.title + " filter heading");
                heading.gameObject.name = name;
            }

            heading.text = spec.title;
            heading.color = MutedInk;
            heading.fontStyle = FontStyles.Bold;
            heading.fontSize = 18f;
            heading.enableAutoSizing = false;
            heading.alignment = TextAlignmentOptions.MidlineLeft;
            heading.raycastTarget = false;
            RectTransform rect = heading.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(0f, 26f);
            LayoutElement element = EnsureComponent<LayoutElement>(heading.gameObject);
            element.minHeight = 26f;
            element.preferredHeight = 26f;
            element.flexibleHeight = 0f;
            return heading;
        }

        private static Button EnsureOptionButton(
            Transform content,
            SectionSpec spec,
            int value,
            string label,
            Button buttonTemplate)
        {
            string name = OptionName(spec, value);
            Transform existing = content.Find(name);
            Button button;
            if (existing != null)
            {
                button = RequireComponent<Button>(existing.gameObject);
            }
            else
            {
                GameObject clone = UnityEngine.Object.Instantiate(buttonTemplate.gameObject, content, false);
                Undo.RegisterCreatedObjectUndo(clone, "Create " + spec.title + " filter option");
                clone.name = name;
                button = RequireComponent<Button>(clone);
            }

            RectTransform rect = button.transform as RectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(0f, 34f);

            LayoutElement element = EnsureComponent<LayoutElement>(button.gameObject);
            element.minWidth = 0f;
            element.preferredWidth = -1f;
            element.flexibleWidth = 0f;
            element.minHeight = 34f;
            element.preferredHeight = 34f;
            element.flexibleHeight = 0f;

            Image image = EnsureComponent<Image>(button.gameObject);
            image.color = PaperAlt;
            image.raycastTarget = true;
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = button.colors;
            colors.normalColor = PaperAlt;
            colors.highlightedColor = Paper;
            colors.pressedColor = new Color(0.88f, 0.86f, 0.79f, 1f);
            colors.selectedColor = Paper;
            colors.disabledColor = PaperAlt;
            button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.RemoveAllListeners();

            TMP_Text text = button.GetComponentInChildren<TMP_Text>(true);
            if (text == null)
                throw new InvalidOperationException("Filter option button has no TMP label: " + name);
            text.text = label;
            text.color = Ink;
            text.raycastTarget = false;
            return button;
        }

        private static GameObject EnsureSelectedVisual(Button button, Image templateImage)
        {
            Transform existing = button.transform.Find("SelectedVisual");
            GameObject selected;
            if (existing != null)
            {
                selected = existing.gameObject;
            }
            else
            {
                selected = new GameObject("SelectedVisual", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                Undo.RegisterCreatedObjectUndo(selected, "Create selected filter option background");
                selected.transform.SetParent(button.transform, false);
            }

            selected.transform.SetAsFirstSibling();
            RectTransform rect = selected.transform as RectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            Image image = EnsureComponent<Image>(selected);
            image.sprite = templateImage != null ? templateImage.sprite : null;
            image.type = templateImage != null ? templateImage.type : Image.Type.Sliced;
            image.color = Accent;
            image.raycastTarget = false;
            selected.SetActive(false);
            return selected;
        }

        private static void ConfigureToggle(
            Button toggle,
            TMP_Text label,
            Vector2 position,
            Vector2 size,
            string baseLabel)
        {
            RectTransform rect = toggle.transform as RectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            ConfigureToggleAppearance(toggle, label, baseLabel);
        }

        private static void ConfigureToggleAppearance(Button toggle, TMP_Text label, string baseLabel)
        {
            Image image = EnsureComponent<Image>(toggle.gameObject);
            image.color = Accent;
            image.raycastTarget = true;
            toggle.targetGraphic = image;
            toggle.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = toggle.colors;
            colors.normalColor = Accent;
            colors.highlightedColor = new Color(0.76f, 0.51f, 0.40f, 1f);
            colors.pressedColor = new Color(0.6756f, 0.4276f, 0.3224f, 1f);
            colors.selectedColor = Accent;
            colors.disabledColor = Accent;
            toggle.colors = colors;
            toggle.navigation = new Navigation { mode = Navigation.Mode.None };
            toggle.onClick.RemoveAllListeners();
            label.text = baseLabel + "  ▼";
            label.color = Color.white;
            label.alignment = TextAlignmentOptions.Midline;
            label.raycastTarget = false;
        }

        private static void ConfigurePopupRect(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void ConfigurePopupImage(Image image, Image templateImage)
        {
            image.sprite = templateImage != null ? templateImage.sprite : null;
            image.type = templateImage != null ? templateImage.type : Image.Type.Sliced;
            image.color = Paper;
            image.raycastTarget = false;
        }

        private static Button EnsureButtonClone(Transform parent, string name, Button template)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
                return RequireComponent<Button>(existing.gameObject);

            GameObject clone = UnityEngine.Object.Instantiate(template.gameObject, parent, false);
            Undo.RegisterCreatedObjectUndo(clone, "Create Library Filters trigger");
            clone.name = name;
            return RequireComponent<Button>(clone);
        }

        private static void HideLegacyFilter(Transform root, string childName)
        {
            Transform child = root.Find(childName);
            if (child == null || child.GetComponent<TMP_Dropdown>() == null)
                throw new InvalidOperationException("Expected the existing TMP filter dropdown " + childName + ".");
            child.gameObject.SetActive(false);
        }

        private static string OptionName(SectionSpec spec, int value)
        {
            return "FilterOption_" + spec.kind + "_" + value;
        }

        private static TMP_Text FindText(Transform root, string childName)
        {
            Transform child = root != null ? root.Find(childName) : null;
            return child != null ? child.GetComponent<TMP_Text>() : null;
        }

        private static bool IsSelected(
            WordListHubSceneBindings.LibraryFilterKind kind,
            int value,
            MockupLibraryQuery query)
        {
            switch (kind)
            {
                case WordListHubSceneBindings.LibraryFilterKind.Order: return value == (int)query.SortDirection;
                case WordListHubSceneBindings.LibraryFilterKind.Language: return value == (int)query.LanguageFilter;
                case WordListHubSceneBindings.LibraryFilterKind.Played: return value == (int)query.PlayedFilter;
                case WordListHubSceneBindings.LibraryFilterKind.Sort: return value == (int)query.SortMode;
                default: return false;
            }
        }

        private static T EnsureComponent<T>(GameObject gameObject) where T : Component
        {
            T component = gameObject.GetComponent<T>();
            return component != null ? component : Undo.AddComponent<T>(gameObject);
        }

        private static T RequireComponent<T>(GameObject gameObject) where T : Component
        {
            T component = gameObject != null ? gameObject.GetComponent<T>() : null;
            if (component == null)
                throw new InvalidOperationException(
                    "Expected " + typeof(T).Name + " on " + (gameObject != null ? gameObject.name : "a missing GameObject") + ".");
            return component;
        }

        private static T RequireChild<T>(Transform parent, string childName) where T : Component
        {
            Transform child = parent.Find(childName);
            if (child == null)
                throw new InvalidOperationException("The existing Library child " + childName + " is missing.");
            T component = child.GetComponent<T>();
            if (component == null)
                throw new InvalidOperationException("The existing Library child " + childName + " has no " + typeof(T).Name + ".");
            return component;
        }
    }
}
#endif
