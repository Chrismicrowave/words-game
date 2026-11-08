using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Words.UiMockups.WordListHub
{
    /// <summary>
    /// Word picker placed inside the existing Edit List words area. It binds to
    /// serialized layout objects when available and keeps a runtime-only fallback
    /// for older scenes.
    /// </summary>
    public sealed class MockupWordListEditorView : IDisposable
    {
        private const float RegionWidth = 640f;
        private const float RegionHeight = 180f;
        private const float RowHeight = 30f;
        private const float RowGap = 2f;
        private const float ViewportHeight = 90f;
        private const float InputHeight = 42f;
        private const float ButtonHeight = 30f;

        private static readonly Color SelectedRowColor =
            new Color(0.96855f, 0.75201f, 0.6853f, 1f);

        private sealed class WordRow
        {
            public GameObject root;
            public Button button;
            public Image background;
            public TMP_Text label;
            public UnityAction onClick;
            public bool authored;
        }

        private readonly WordListHubSceneBindings.FormBinding form;
        private readonly TMP_InputField input;
        private readonly RectTransform inputRect;
        private readonly Vector2 originalInputPosition;
        private readonly Vector2 originalInputSize;
        private readonly TMP_InputField.LineType originalLineType;
        private readonly bool originalInteractable;
        private readonly TextWrappingModes originalWrapping;
        private readonly TextOverflowModes originalOverflow;
        private readonly string originalPlaceholder;
        private readonly string originalWordsLabel;
        private readonly WordListHubSceneBindings.WordEditorLayoutBinding layoutBinding;
        private readonly Action<int> selectWord;
        private readonly Action addWord;
        private readonly Action<int> moveWord;
        private readonly Action deleteWord;
        private readonly Action<Button> ensureButtonSound;
        private readonly Button buttonTemplate;
        private readonly TMP_Text textTemplate;
        private GameObject runtimeRoot;
        private RectTransform content;
        private ScrollRect scrollRect;
        private TMP_Text emptyNotice;
        private readonly List<WordRow> rows = new List<WordRow>();
        private Button moveUpButton;
        private Button moveDownButton;
        private Button deleteButton;
        private Button addButton;
        private UnityAction moveUpAction;
        private UnityAction moveDownAction;
        private UnityAction deleteAction;
        private UnityAction addAction;
        private bool disposed;

        public MockupWordListEditorView(
            WordListHubSceneBindings.FormBinding form,
            Action<int> selectWord,
            Action addWord,
            Action<int> moveWord,
            Action deleteWord,
            Action<Button> ensureButtonSound = null)
        {
            this.form = form ?? throw new ArgumentNullException(nameof(form));
            layoutBinding = form.wordEditorLayout ?? (form.wordEditorLayout =
                new WordListHubSceneBindings.WordEditorLayoutBinding());
            input = form.wordsInput;
            if (form.root == null || input == null)
                throw new InvalidOperationException("The Edit List word input is missing from its scene binding.");

            this.selectWord = selectWord;
            this.addWord = addWord;
            this.moveWord = moveWord;
            this.deleteWord = deleteWord;
            this.ensureButtonSound = ensureButtonSound;
            buttonTemplate = form.cancelButton;
            textTemplate = buttonTemplate != null
                ? buttonTemplate.GetComponentInChildren<TMP_Text>(true)
                : input.textComponent;
            if (buttonTemplate == null || textTemplate == null)
                throw new InvalidOperationException("The Edit List form has no button/text style source.");

            inputRect = input.GetComponent<RectTransform>();
            if (inputRect == null)
                throw new InvalidOperationException("The Edit List word input has no RectTransform.");
            originalInputPosition = inputRect.anchoredPosition;
            originalInputSize = inputRect.sizeDelta;
            originalLineType = input.lineType;
            originalInteractable = input.interactable;
            originalWrapping = input.textComponent.textWrappingMode;
            originalOverflow = input.textComponent.overflowMode;
            TMP_Text placeholderText = input.placeholder as TMP_Text;
            originalPlaceholder = placeholderText != null ? placeholderText.text : null;
            originalWordsLabel = form.wordsLabel != null ? form.wordsLabel.text : null;

            RectTransform rootRect = ResolveRoot();
            scrollRect = ResolveScroll(rootRect);
            content = scrollRect.content;
            emptyNotice = ResolveEmptyNotice(scrollRect.viewport);
            moveUpButton = ResolveButton(rootRect, "MoveWordUp", layoutBinding.moveUpButton,
                "↑", 45f, new Vector2(-137.5f, -17f), false);
            moveDownButton = ResolveButton(rootRect, "MoveWordDown", layoutBinding.moveDownButton,
                "↓", 45f, new Vector2(-87.5f, -17f), false);
            deleteButton = ResolveButton(rootRect, "DeleteWord", layoutBinding.deleteButton,
                "Delete", 95f, new Vector2(-12.5f, -17f), false);
            addButton = ResolveButton(rootRect, "AddWord", layoutBinding.addButton,
                "New Word", 120f, new Vector2(100f, -17f), true);
            TMP_Text addButtonLabel = addButton != null ? addButton.GetComponentInChildren<TMP_Text>(true) : null;
            if (addButtonLabel != null)
                addButtonLabel.text = "New Word";
            layoutBinding.scrollRect = scrollRect;
            layoutBinding.viewport = scrollRect.viewport;
            layoutBinding.content = content;
            layoutBinding.emptyNotice = emptyNotice;
            layoutBinding.moveUpButton = moveUpButton;
            layoutBinding.moveDownButton = moveDownButton;
            layoutBinding.deleteButton = deleteButton;
            layoutBinding.addButton = addButton;
            BindAuthoredRows();

            ensureButtonSound?.Invoke(moveUpButton);
            ensureButtonSound?.Invoke(moveDownButton);
            ensureButtonSound?.Invoke(deleteButton);
            ensureButtonSound?.Invoke(addButton);

            moveUpAction = () => this.moveWord?.Invoke(-1);
            moveDownAction = () => this.moveWord?.Invoke(1);
            deleteAction = () => this.deleteWord?.Invoke();
            addAction = () => this.addWord?.Invoke();
            moveUpButton.onClick.AddListener(moveUpAction);
            moveDownButton.onClick.AddListener(moveDownAction);
            deleteButton.onClick.AddListener(deleteAction);
            addButton.onClick.AddListener(addAction);

            ConfigureInputForPicker(form);
        }

        /// <summary>
        /// Creates and serializes the same layout used at runtime, then bakes a
        /// sample edit session into it. Call only from an explicit Editor authoring
        /// command; this is intentionally not an OnValidate path.
        /// </summary>
        public static void EnsureAuthoredLayout(
            WordListHubSceneBindings.FormBinding form,
            IReadOnlyList<string> previewWords)
        {
            var view = new MockupWordListEditorView(form, null, null, null, null);
            view.Render(new MockupWordListEditSession(previewWords), "Enter one word", false);
            view.DisposeForAuthoring();
        }

        public static void EnsureAuthoredInputStyle(WordListHubSceneBindings.FormBinding form)
        {
            if (form == null || form.wordsInput == null || form.wordsInput.textComponent == null)
                return;
            form.wordsInput.lineType = TMP_InputField.LineType.SingleLine;
            form.wordsInput.textComponent.textWrappingMode = TextWrappingModes.PreserveWhitespaceNoWrap;
            form.wordsInput.textComponent.overflowMode = TextOverflowModes.Ellipsis;
        }

        public void Render(MockupWordListEditSession session, string placeholder, bool focusInput)
        {
            if (disposed)
                return;

            if (form.wordsLabel != null)
                form.wordsLabel.text = "Words · select one to edit";
            int wordCount = session != null ? session.Words.Count : 0;
            while (rows.Count < wordCount)
                rows.Add(CreateRow());

            for (int i = 0; i < rows.Count; i++)
            {
                int index = i;
                WordRow row = rows[i];
                row.root.SetActive(i < wordCount);
                if (i >= wordCount)
                {
                    RemoveRowListener(row);
                    continue;
                }
                string word = session.Words[i] ?? string.Empty;
                row.label.text = string.IsNullOrWhiteSpace(word) ? "(empty)" : word;
                bool selected = session.HasSelection && session.SelectedIndex == i;
                row.background.color = selected ? SelectedRowColor : Color.clear;
                RemoveRowListener(row);
                ensureButtonSound?.Invoke(row.button);
                row.onClick = () => selectWord?.Invoke(index);
                row.button.onClick.AddListener(row.onClick);
            }

            if (emptyNotice != null)
                emptyNotice.gameObject.SetActive(wordCount == 0);
            moveUpButton.interactable = session != null && session.CanMoveUp;
            moveDownButton.interactable = session != null && session.CanMoveDown;
            deleteButton.interactable = session != null && session.HasSelection;
            input.interactable = session != null && session.HasSelection;
            TMP_Text placeholderText = input.placeholder as TMP_Text;
            if (placeholderText != null)
                placeholderText.text = session != null && !session.HasSelection
                    ? "Add a word to begin" : placeholder ?? "Enter one word";

            string selectedWord = session != null ? session.SelectedWord : string.Empty;
            if (input.text != selectedWord)
                input.SetTextWithoutNotify(selectedWord);
            if (focusInput)
            {
                input.ActivateInputField();
                input.caretPosition = selectedWord.Length;
            }

            Canvas.ForceUpdateCanvases();
            if (content != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            if (focusInput)
                ScrollSelectedRowIntoView(session);
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            UnbindListeners();
            if (Application.isPlaying && runtimeRoot == null)
            {
                foreach (WordRow row in rows)
                {
                    if (row != null && !row.authored && row.root != null)
                        DestroyObject(row.root);
                }
            }
            input.lineType = originalLineType;
            input.interactable = originalInteractable;
            input.textComponent.textWrappingMode = originalWrapping;
            input.textComponent.overflowMode = originalOverflow;
            TMP_Text placeholderText = input.placeholder as TMP_Text;
            if (placeholderText != null && originalPlaceholder != null)
                placeholderText.text = originalPlaceholder;
            if (form.wordsLabel != null && originalWordsLabel != null)
                form.wordsLabel.text = originalWordsLabel;
            inputRect.anchoredPosition = originalInputPosition;
            inputRect.sizeDelta = originalInputSize;
            if (runtimeRoot != null)
                DestroyObject(runtimeRoot);
        }

        private void DisposeForAuthoring()
        {
            if (disposed)
                return;
            disposed = true;
            UnbindListeners();
        }

        private void UnbindListeners()
        {
            if (moveUpButton != null && moveUpAction != null)
                moveUpButton.onClick.RemoveListener(moveUpAction);
            if (moveDownButton != null && moveDownAction != null)
                moveDownButton.onClick.RemoveListener(moveDownAction);
            if (deleteButton != null && deleteAction != null)
                deleteButton.onClick.RemoveListener(deleteAction);
            if (addButton != null && addAction != null)
                addButton.onClick.RemoveListener(addAction);
            foreach (WordRow row in rows)
                RemoveRowListener(row);
        }

        private static void RemoveRowListener(WordRow row)
        {
            if (row != null && row.button != null && row.onClick != null)
            {
                row.button.onClick.RemoveListener(row.onClick);
                row.onClick = null;
            }
        }

        private static void ConfigureInputForPicker(WordListHubSceneBindings.FormBinding form)
        {
            EnsureAuthoredInputStyle(form);
        }

        private RectTransform ResolveRoot()
        {
            if (layoutBinding.root != null)
            {
                RectTransform authoredRoot = layoutBinding.root.GetComponent<RectTransform>();
                if (authoredRoot == null)
                    throw new InvalidOperationException("The authored word editor root must have a RectTransform.");
                return authoredRoot;
            }

            GameObject root = new GameObject("EditWordEditor", typeof(RectTransform));
            if (Application.isPlaying)
                root.AddComponent<WordListHubRuntimeInstance>();
            root.transform.SetParent(form.root.transform, false);
            RectTransform rect = root.GetComponent<RectTransform>();
            SetRect(rect, new Vector2(0f, -134f), new Vector2(RegionWidth, RegionHeight));
            layoutBinding.root = root;
            runtimeRoot = Application.isPlaying ? root : null;
            return rect;
        }

        private ScrollRect ResolveScroll(RectTransform rootRect)
        {
            ScrollRect scroll = layoutBinding.scrollRect;
            if (scroll == null)
                scroll = rootRect.GetComponentInChildren<ScrollRect>(true);
            if (scroll == null)
            {
                scroll = CreateWordScroll(rootRect, out RectTransform createdContent, out TMP_Text createdNotice);
                layoutBinding.viewport = scroll.viewport;
                layoutBinding.content = createdContent;
                layoutBinding.emptyNotice = createdNotice;
            }

            if (scroll.viewport == null)
                scroll.viewport = layoutBinding.viewport;
            if (scroll.content == null)
                scroll.content = layoutBinding.content;
            if (scroll.viewport == null || scroll.content == null)
                throw new InvalidOperationException("The serialized word editor ScrollRect needs a viewport and content.");
            return scroll;
        }

        private TMP_Text ResolveEmptyNotice(RectTransform viewport)
        {
            TMP_Text notice = layoutBinding.emptyNotice;
            if (notice == null)
            {
                Transform noticeRoot = viewport.Find("NoWordsNotice");
                if (noticeRoot != null)
                    notice = noticeRoot.GetComponent<TMP_Text>();
            }
            if (notice != null)
                return notice;

            GameObject emptyObject = new GameObject("NoWordsNotice", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            emptyObject.transform.SetParent(viewport, false);
            RectTransform emptyRect = emptyObject.GetComponent<RectTransform>();
            SetRect(emptyRect, Vector2.zero, new Vector2(RegionWidth - 16f, 34f));
            notice = emptyObject.GetComponent<TMP_Text>();
            ConfigureText(notice, "No words yet. Choose Add.", textTemplate, 18f,
                TextAlignmentOptions.Center, textTemplate.color);
            emptyObject.SetActive(false);
            return notice;
        }

        private Button ResolveButton(
            RectTransform rootRect,
            string name,
            Button existing,
            string label,
            float width,
            Vector2 position,
            bool primary)
        {
            if (existing == null)
                existing = rootRect.Find(name)?.GetComponent<Button>();
            return existing != null
                ? existing
                : CreateButton(rootRect, name, label, width, position, primary);
        }

        private void BindAuthoredRows()
        {
            if (layoutBinding.authoredRows == null)
                layoutBinding.authoredRows = new List<WordListHubSceneBindings.WordEditorRowBinding>();

            var boundRoots = new HashSet<GameObject>();
            foreach (WordListHubSceneBindings.WordEditorRowBinding binding in layoutBinding.authoredRows)
            {
                WordRow row = BindRow(binding);
                if (row != null && boundRoots.Add(row.root))
                    rows.Add(row);
            }

            for (int i = 0; i < content.childCount; i++)
            {
                GameObject rowRoot = content.GetChild(i).gameObject;
                if (!boundRoots.Add(rowRoot))
                    continue;
                WordRow row = BindRow(new WordListHubSceneBindings.WordEditorRowBinding
                {
                    root = rowRoot,
                    button = rowRoot.GetComponent<Button>(),
                    background = rowRoot.GetComponent<Image>(),
                    label = rowRoot.GetComponentInChildren<TMP_Text>(true)
                });
                if (row == null)
                    continue;
                rows.Add(row);
                AddRowBinding(row);
            }

            if (rows.Count > 0 && layoutBinding.rowTemplate == null)
                layoutBinding.rowTemplate = rows[0].root;
        }

        private static WordRow BindRow(WordListHubSceneBindings.WordEditorRowBinding binding)
        {
            if (binding == null || binding.root == null)
                return null;
            Button button = binding.button != null ? binding.button : binding.root.GetComponent<Button>();
            Image background = binding.background != null ? binding.background : binding.root.GetComponent<Image>();
            TMP_Text label = binding.label != null
                ? binding.label : binding.root.GetComponentInChildren<TMP_Text>(true);
            if (button == null || background == null || label == null)
                return null;
            binding.button = button;
            binding.background = background;
            binding.label = label;
            return new WordRow
            {
                root = binding.root,
                button = button,
                background = background,
                label = label,
                authored = true
            };
        }

        private void AddRowBinding(WordRow row)
        {
            foreach (WordListHubSceneBindings.WordEditorRowBinding binding in layoutBinding.authoredRows)
            {
                if (binding != null && binding.root == row.root)
                    return;
            }
            layoutBinding.authoredRows.Add(new WordListHubSceneBindings.WordEditorRowBinding
            {
                root = row.root,
                button = row.button,
                background = row.background,
                label = row.label
            });
        }

        private ScrollRect CreateWordScroll(
            RectTransform parent,
            out RectTransform wordContent,
            out TMP_Text notice)
        {
            GameObject viewportObject = new GameObject("WordRowsViewport", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image), typeof(Mask), typeof(ScrollRect));
            viewportObject.transform.SetParent(parent, false);
            RectTransform viewport = viewportObject.GetComponent<RectTransform>();
            SetRect(viewport, new Vector2(0f, 45f), new Vector2(RegionWidth, ViewportHeight));
            Image viewportImage = viewportObject.GetComponent<Image>();
            viewportImage.color = Color.white;
            Mask mask = viewportObject.GetComponent<Mask>();
            mask.showMaskGraphic = false;

            GameObject contentObject = new GameObject("WordRowsContent", typeof(RectTransform),
                typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentObject.transform.SetParent(viewport, false);
            wordContent = contentObject.GetComponent<RectTransform>();
            wordContent.anchorMin = new Vector2(0f, 1f);
            wordContent.anchorMax = new Vector2(1f, 1f);
            wordContent.pivot = new Vector2(0.5f, 1f);
            wordContent.anchoredPosition = Vector2.zero;
            wordContent.sizeDelta = Vector2.zero;

            VerticalLayoutGroup layout = contentObject.GetComponent<VerticalLayoutGroup>();
            layout.spacing = RowGap;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            ContentSizeFitter fitter = contentObject.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            GameObject emptyObject = new GameObject("NoWordsNotice", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            emptyObject.transform.SetParent(viewport, false);
            RectTransform emptyRect = emptyObject.GetComponent<RectTransform>();
            SetRect(emptyRect, Vector2.zero, new Vector2(RegionWidth - 16f, 34f));
            notice = emptyObject.GetComponent<TMP_Text>();
            ConfigureText(notice, "No words yet. Choose Add.", textTemplate, 18f,
                TextAlignmentOptions.Center, textTemplate.color);
            emptyObject.SetActive(false);

            ScrollRect scroll = viewportObject.GetComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = wordContent;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = false;
            scroll.scrollSensitivity = 30f;
            return scroll;
        }

        private WordRow CreateRow()
        {
            GameObject rowObject;
            bool clonedTemplate = layoutBinding.rowTemplate != null;
            if (clonedTemplate)
            {
                rowObject = UnityEngine.Object.Instantiate(layoutBinding.rowTemplate, content, false);
                rowObject.name = "WordRow";
            }
            else
            {
                rowObject = new GameObject("WordRow", typeof(RectTransform),
                    typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
                rowObject.transform.SetParent(content, false);
            }

            if (Application.isPlaying && rowObject.GetComponent<WordListHubRuntimeInstance>() == null)
                rowObject.AddComponent<WordListHubRuntimeInstance>();
            RectTransform rowRect = rowObject.GetComponent<RectTransform>();
            if (rowRect == null)
                throw new InvalidOperationException("The Word List row template needs a RectTransform.");

            if (!clonedTemplate)
            {
                rowRect.anchorMin = new Vector2(0f, 1f);
                rowRect.anchorMax = new Vector2(1f, 1f);
                rowRect.pivot = new Vector2(0.5f, 1f);
                rowRect.sizeDelta = new Vector2(0f, RowHeight);
                LayoutElement layout = rowObject.GetComponent<LayoutElement>();
                layout.minHeight = RowHeight;
                layout.preferredHeight = RowHeight;

                Image backgroundImage = rowObject.GetComponent<Image>();
                backgroundImage.color = Color.clear;
                Button rowButton = rowObject.GetComponent<Button>();
                rowButton.targetGraphic = backgroundImage;
                rowButton.navigation = new Navigation { mode = Navigation.Mode.None };
                rowButton.transition = Selectable.Transition.None;

                GameObject labelObject = new GameObject("WordLabel", typeof(RectTransform),
                    typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                labelObject.transform.SetParent(rowObject.transform, false);
                RectTransform labelRect = labelObject.GetComponent<RectTransform>();
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = new Vector2(8f, 0f);
                labelRect.offsetMax = new Vector2(-8f, 0f);
                TMP_Text labelText = labelObject.GetComponent<TMP_Text>();
                ConfigureText(labelText, string.Empty, textTemplate, 18f,
                    TextAlignmentOptions.MidlineLeft, input.textComponent.color);
                labelText.fontStyle = FontStyles.Bold;
            }

            WordRow row = BindRow(new WordListHubSceneBindings.WordEditorRowBinding
            {
                root = rowObject,
                button = rowObject.GetComponent<Button>(),
                background = rowObject.GetComponent<Image>(),
                label = rowObject.GetComponentInChildren<TMP_Text>(true)
            });
            if (row == null)
                throw new InvalidOperationException("The Word List row template needs Button, Image and TMP_Text components.");
            row.authored = !Application.isPlaying;
            if (!Application.isPlaying)
                AddRowBinding(row);
            if (layoutBinding.rowTemplate == null)
                layoutBinding.rowTemplate = rowObject;
            return row;
        }

        private Button CreateButton(
            RectTransform parent,
            string name,
            string label,
            float width,
            Vector2 position,
            bool primary)
        {
            GameObject buttonObject = new GameObject(name, typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            SetRect(rect, position, new Vector2(width, ButtonHeight));

            Button sourceButton = primary && form.saveButton != null
                ? form.saveButton : buttonTemplate;
            Image image = buttonObject.GetComponent<Image>();
            Image sourceImage = sourceButton.targetGraphic as Image;
            if (sourceImage != null)
            {
                image.sprite = sourceImage.sprite;
                image.type = sourceImage.type;
                image.color = sourceImage.color;
            }
            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;
            button.colors = sourceButton.colors;
            button.navigation = new Navigation { mode = Navigation.Mode.None };

            GameObject textObject = new GameObject("Label", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(buttonObject.transform, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(2f, 0f);
            textRect.offsetMax = new Vector2(-2f, 0f);
            ConfigureText(textObject.GetComponent<TMP_Text>(), label, textTemplate,
                18f, TextAlignmentOptions.Center, textTemplate.color);
            return button;
        }

        private void ScrollSelectedRowIntoView(MockupWordListEditSession session)
        {
            if (session == null || !session.HasSelection || session.SelectedIndex >= rows.Count)
                return;
            RectTransform selected = rows[session.SelectedIndex].button.transform as RectTransform;
            Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(
                scrollRect.viewport, selected);
            Rect viewport = scrollRect.viewport.rect;
            Vector2 position = content.anchoredPosition;
            if (bounds.max.y > viewport.yMax)
                position.y -= bounds.max.y - viewport.yMax;
            else if (bounds.min.y < viewport.yMin)
                position.y += viewport.yMin - bounds.min.y;
            content.anchoredPosition = position;
        }

        private static void ConfigureText(
            TMP_Text target,
            string value,
            TMP_Text template,
            float fontSize,
            TextAlignmentOptions alignment,
            Color color)
        {
            target.font = template.font;
            target.fontSharedMaterial = template.fontSharedMaterial;
            target.fontSize = fontSize;
            target.fontStyle = template.fontStyle;
            target.color = color;
            target.alignment = alignment;
            target.textWrappingMode = TextWrappingModes.NoWrap;
            target.overflowMode = TextOverflowModes.Ellipsis;
            target.raycastTarget = false;
            target.text = value;
        }

        private static void SetRect(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;
        }

        private static void DestroyObject(GameObject target)
        {
            if (target == null)
                return;
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(target);
            else
                UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
