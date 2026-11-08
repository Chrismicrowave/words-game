using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Words.UiMockups.WordListHub
{
    /// <summary>
    /// Stacked word view for the mockup's existing detail text regions. It binds to
    /// serialized rows when available and pools rows/cells for later content updates.
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
        private readonly WordListHubSceneBindings.RubyPreviewBinding binding;
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
            float previewFontScale,
            WordListHubSceneBindings.RubyPreviewBinding binding)
        {
            sourceText = text;
            layoutRoot = root;
            scrollRect = scroll;
            verticalLayout = layout;
            this.binding = binding;
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
            if (binding != null && binding.previewWords != null)
            {
                for (int i = 0; i < binding.previewWords.Count; i++)
                    words.Add(binding.previewWords[i] ?? string.Empty);
            }
        }

        public static MockupRubyWordsView CreateLibrary(
            TMP_Text legacyText,
            float previewFontScale,
            WordListHubSceneBindings.RubyPreviewBinding binding = null)
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
            RectTransform root = binding != null ? binding.root : null;
            if (root == null)
                root = contentParent.Find(LibraryRootName) as RectTransform;
            bool createdRoot = root == null;
            if (root == null)
                root = CreateRectTransform(LibraryRootName, contentParent);
            if (createdRoot)
                ConfigureTopStretch(root);

            VerticalLayoutGroup layout = root.GetComponent<VerticalLayoutGroup>();
            bool createdLayout = layout == null;
            if (layout == null)
                layout = root.gameObject.AddComponent<VerticalLayoutGroup>();
            if (createdRoot || createdLayout)
                ConfigureVerticalLayout(layout, EntryGap);

            LayoutElement rootElement = root.GetComponent<LayoutElement>();
            bool createdRootElement = rootElement == null;
            if (rootElement == null)
                rootElement = root.gameObject.AddComponent<LayoutElement>();
            if (createdRoot || createdRootElement)
            {
                rootElement.minWidth = 0f;
                rootElement.flexibleWidth = 1f;
            }

            ScrollRect existingScroll = binding != null && binding.scrollRect != null
                ? binding.scrollRect : legacyText.GetComponentInParent<ScrollRect>(true);
            if (binding != null)
            {
                binding.root = root;
                binding.content = root;
                binding.scrollRect = existingScroll;
                binding.viewport = existingScroll != null ? existingScroll.viewport : null;
                binding.scrollbar = existingScroll != null ? existingScroll.verticalScrollbar : null;
            }

            var view = new MockupRubyWordsView(legacyText, root, existingScroll, layout,
                previewFontScale, binding);
            view.RefreshLayoutIfNeeded();
            if (binding != null)
                binding.previewFontScale = previewFontScale;
            return view;
        }

        public static MockupRubyWordsView CreateCommunity(
            TMP_Text legacyText,
            Scrollbar scrollbarTemplate,
            float previewFontScale,
            WordListHubSceneBindings.RubyPreviewBinding binding = null)
        {
            if (legacyText == null)
                return null;
            if (scrollbarTemplate == null && (binding == null || binding.scrollbar == null))
                throw new InvalidOperationException(
                    "Community stacked word previews require the Library detail scrollbar template.");

            RectTransform viewportRegion = legacyText.rectTransform;
            RectTransform viewport = binding != null ? binding.viewport : null;
            if (viewport == null)
                viewport = viewportRegion.Find(CommunityViewportName) as RectTransform;
            bool createdViewport = viewport == null;
            if (viewport == null)
                viewport = CreateRectTransform(CommunityViewportName, viewportRegion);
            if (createdViewport)
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

            ScrollRect scroll = binding != null && binding.scrollRect != null
                ? binding.scrollRect : viewport.GetComponent<ScrollRect>();
            bool createdScroll = scroll == null;
            if (scroll == null)
                scroll = viewport.gameObject.AddComponent<ScrollRect>();

            RectTransform content = binding != null ? binding.content : null;
            if (content == null)
                content = viewport.Find(CommunityContentName) as RectTransform;
            bool createdContent = content == null;
            if (content == null)
                content = CreateRectTransform(CommunityContentName, viewport);
            if (createdContent)
                ConfigureTopStretch(content);

            VerticalLayoutGroup layout = content.GetComponent<VerticalLayoutGroup>();
            bool createdLayout = layout == null;
            if (layout == null)
                layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            if (createdContent || createdLayout)
                ConfigureVerticalLayout(layout, EntryGap);

            ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
            bool createdFitter = fitter == null;
            if (fitter == null)
                fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            if (createdContent || createdFitter)
            {
                fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }

            Transform existingScrollbarRoot = viewportRegion.Find(CommunityScrollbarName);
            bool createdScrollbar = (binding == null || binding.scrollbar == null)
                && (existingScrollbarRoot == null
                    || existingScrollbarRoot.GetComponent<Scrollbar>() == null);
            Scrollbar communityScrollbar = binding != null && binding.scrollbar != null
                ? binding.scrollbar
                : CreateCommunityScrollbar(viewportRegion, scrollbarTemplate);
            RectTransform scrollbarRect = communityScrollbar.transform as RectTransform;
            RectTransform templateScrollbarRect = scrollbarTemplate != null
                ? scrollbarTemplate.transform as RectTransform : scrollbarRect;
            float scrollbarWidth = Mathf.Max(templateScrollbarRect.rect.width, templateScrollbarRect.sizeDelta.x);
            if (createdViewport || createdContent || createdScrollbar)
            {
                scrollbarRect.anchorMin = new Vector2(1f, 0f);
                scrollbarRect.anchorMax = Vector2.one;
                scrollbarRect.pivot = new Vector2(1f, 1f);
                scrollbarRect.anchoredPosition = Vector2.zero;
                scrollbarRect.sizeDelta = new Vector2(scrollbarWidth, 0f);
                content.sizeDelta = new Vector2(-(scrollbarWidth + CommunityScrollbarGap), content.sizeDelta.y);
            }

            LayoutElement contentElement = content.GetComponent<LayoutElement>();
            bool createdContentElement = contentElement == null;
            if (contentElement == null)
                contentElement = content.gameObject.AddComponent<LayoutElement>();
            if (createdContent || createdContentElement)
            {
                contentElement.minWidth = 0f;
                contentElement.flexibleWidth = 1f;
            }

            if (createdViewport || createdContent || createdScrollbar || createdScroll)
            {
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
            }
            else
            {
                if (scroll.viewport == null)
                    scroll.viewport = viewport;
                if (scroll.content == null)
                    scroll.content = content;
                if (scroll.verticalScrollbar == null)
                    scroll.verticalScrollbar = communityScrollbar;
            }

            legacyText.enabled = false;
            if (binding != null)
            {
                binding.root = viewportRegion;
                binding.viewport = viewport;
                binding.content = content;
                binding.scrollRect = scroll;
                binding.scrollbar = communityScrollbar;
            }

            var view = new MockupRubyWordsView(legacyText, content, scroll, layout,
                previewFontScale, binding);
            view.RefreshLayoutIfNeeded();
            if (binding != null)
                binding.previewFontScale = previewFontScale;
            return view;
        }

        private static Scrollbar CreateCommunityScrollbar(RectTransform communityRegion, Scrollbar template)
        {
            Transform existing = communityRegion.Find(CommunityScrollbarName);
            Scrollbar scrollbar = existing == null ? null : existing.GetComponent<Scrollbar>();
            if (scrollbar != null)
                return scrollbar;
            if (template == null)
                throw new InvalidOperationException("Community scrollbar binding and Library template are missing.");

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

            if (binding != null)
            {
                if (binding.previewWords == null)
                    binding.previewWords = new List<string>();
                binding.previewWords.Clear();
                binding.previewWords.AddRange(words);
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
            List<RectTransform> entries = GetEntryPool();
            float entriesHeight = 0f;
            int entryCount = 0;
            int entryIndex = 0;
            for (int wordIndex = 0; wordIndex < words.Count; wordIndex++)
            {
                if (words[wordIndex].Length == 0)
                    continue;

                RectTransform entryRoot = GetOrCreateEntry(entries, entryIndex++);
                entryRoot.gameObject.SetActive(true);
                LayoutElement entryElement = entryRoot.GetComponent<LayoutElement>();
                if (entryElement == null)
                    entryElement = entryRoot.gameObject.AddComponent<LayoutElement>();
                entryElement.minHeight = cellHeight;
                entryElement.flexibleHeight = 0f;
                List<RectTransform> cellPool = GetCellPool(entryRoot);

                List<TokenData> tokens = BuildTokens(words[wordIndex]);
                var cells = new List<CellView>();
                foreach (TokenData token in tokens)
                {
                    if (!token.IsHan && MeasureWidth(token.Text) > width)
                    {
                        foreach (string textElement in SplitTextElements(token.Text))
                            cells.Add(CreateCell(entryRoot, cellPool, cells.Count,
                                new TokenData(textElement, string.Empty, false, true)));
                    }
                    else
                    {
                        cells.Add(CreateCell(entryRoot, cellPool, cells.Count, token));
                    }
                }
                for (int cellIndex = cells.Count; cellIndex < cellPool.Count; cellIndex++)
                    cellPool[cellIndex].gameObject.SetActive(false);

                entriesHeight += LayoutEntry(entryRoot, entryElement, cells, width);
                entryCount++;
            }

            for (int i = entryIndex; i < entries.Count; i++)
                entries[i].gameObject.SetActive(false);

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

        private List<RectTransform> GetEntryPool()
        {
            var entries = new List<RectTransform>();
            var seen = new HashSet<GameObject>();
            if (binding != null)
            {
                if (binding.authoredEntries == null)
                    binding.authoredEntries = new List<GameObject>();
                foreach (GameObject authoredEntry in binding.authoredEntries)
                {
                    RectTransform rect = authoredEntry != null
                        ? authoredEntry.GetComponent<RectTransform>() : null;
                    if (rect != null && rect.parent == layoutRoot && seen.Add(authoredEntry))
                        entries.Add(rect);
                }
            }

            for (int i = 0; i < layoutRoot.childCount; i++)
            {
                Transform child = layoutRoot.GetChild(i);
                if (!(child is RectTransform rect) || !seen.Add(child.gameObject))
                    continue;
                entries.Add(rect);
                if (binding != null)
                    binding.authoredEntries.Add(child.gameObject);
            }
            return entries;
        }

        private RectTransform GetOrCreateEntry(List<RectTransform> entries, int index)
        {
            while (entries.Count <= index)
            {
                RectTransform created = CreateRectTransform("WordEntry", layoutRoot);
                created.anchorMin = new Vector2(0f, 1f);
                created.anchorMax = new Vector2(1f, 1f);
                created.pivot = new Vector2(0f, 1f);
                created.gameObject.AddComponent<LayoutElement>();
                entries.Add(created);
                if (binding != null)
                {
                    if (binding.authoredEntries == null)
                        binding.authoredEntries = new List<GameObject>();
                    binding.authoredEntries.Add(created.gameObject);
                }
            }
            return entries[index];
        }

        private static List<RectTransform> GetCellPool(RectTransform entryRoot)
        {
            var cells = new List<RectTransform>();
            for (int i = 0; i < entryRoot.childCount; i++)
            {
                if (entryRoot.GetChild(i) is RectTransform cell)
                    cells.Add(cell);
            }
            return cells;
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

        private CellView CreateCell(
            RectTransform parent,
            List<RectTransform> cellPool,
            int index,
            TokenData token)
        {
            RectTransform cellRoot = index < cellPool.Count
                ? cellPool[index]
                : CreateRectTransform(token.IsHan ? "HanCell" : "TextCell", parent);
            cellRoot.gameObject.SetActive(true);
            cellRoot.name = token.IsHan ? "HanCell" : "TextCell";
            cellRoot.anchorMin = new Vector2(0f, 1f);
            cellRoot.anchorMax = new Vector2(0f, 1f);
            cellRoot.pivot = new Vector2(0f, 1f);

            Transform pinyinTransform = cellRoot.Find("Pinyin");
            TMP_Text pinyinLabel = pinyinTransform != null
                ? pinyinTransform.GetComponent<TMP_Text>() : null;
            if (pinyinLabel == null)
                pinyinLabel = CreateLabel(cellRoot, "Pinyin", true);
            ConfigureLabel(pinyinLabel);
            pinyinLabel.text = token.IsHan ? token.Pinyin : string.Empty;
            pinyinLabel.fontSize = pinyinFontSize;
            pinyinLabel.fontStyle = FontStyles.Bold;

            Transform glyphTransform = cellRoot.Find("Glyph");
            TMP_Text glyphLabel = glyphTransform != null
                ? glyphTransform.GetComponent<TMP_Text>() : null;
            if (glyphLabel == null)
                glyphLabel = CreateLabel(cellRoot, "Glyph", false);
            ConfigureLabel(glyphLabel);
            glyphLabel.text = token.Text;
            glyphLabel.fontSize = bodyFontSize;
            glyphLabel.fontStyle = FontStyles.Normal;

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
            ConfigureLabel(label);
            if (!pinyin)
                label.fontStyle = FontStyles.Normal;
            return label;
        }

        private void ConfigureLabel(TMP_Text label)
        {
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
