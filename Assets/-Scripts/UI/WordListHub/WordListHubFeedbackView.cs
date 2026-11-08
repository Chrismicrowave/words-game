using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Words.UiMockups.WordListHub
{
    /// <summary>Shows one non-blocking operation message above the Word List Hub.</summary>
    [DisallowMultipleComponent]
    public sealed class WordListHubFeedbackView : MonoBehaviour, IDisposable
    {
        private const float DefaultWidth = 840f;
        private const float ToastHeight = 104f;
        private const float MessageLeft = 24f;
        private const float MessageRight = 72f;

        [SerializeField] private TMP_Text messageText;
        [SerializeField] private Button closeButton;
        [SerializeField] private Image backgroundImage;

        private RectTransform parentRect;
        private RectTransform toastRect;
        private UnityAction closeListener;
        private float remainingSeconds;
        private bool autoDismiss;
        private bool disposed;
        private bool deactivatingOnDisable;
        private bool instanceInitialized;

        public Button CloseButton => closeButton;
        public TMP_Text MessageText => messageText;
        public Image BackgroundImage => backgroundImage;
        public bool IsVisible => this != null && gameObject != null && gameObject.activeInHierarchy;
        public float RemainingSeconds => remainingSeconds;

        /// <summary>Creates at most one feedback view directly under the Hub Canvas root.</summary>
        public static WordListHubFeedbackView Create(
            RectTransform hubCanvasRoot,
            TMP_Text textStyleTemplate,
            Button closeButtonStyle,
            Action<Button> configureCloseButton = null)
        {
            if (hubCanvasRoot == null)
                throw new ArgumentNullException(nameof(hubCanvasRoot));

            for (int index = 0; index < hubCanvasRoot.childCount; index++)
            {
                WordListHubFeedbackView existing =
                    hubCanvasRoot.GetChild(index).GetComponent<WordListHubFeedbackView>();
                if (existing != null)
                    return existing;
            }

            GameObject root = new GameObject(
                "WordListHubFeedback",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            root.SetActive(false);
            root.transform.SetParent(hubCanvasRoot, false);
            root.transform.SetAsLastSibling();

            WordListHubFeedbackView view = root.AddComponent<WordListHubFeedbackView>();
            view.Initialize(hubCanvasRoot, textStyleTemplate, closeButtonStyle, configureCloseButton);
            if (Application.isPlaying)
                root.AddComponent<WordListHubRuntimeInstance>();
            return view;
        }

        /// <summary>Instantiates the editable feedback prefab without changing its authored style or geometry.</summary>
        public static WordListHubFeedbackView CreateFromPrefab(
            WordListHubFeedbackView prefab,
            RectTransform hubCanvasRoot,
            Action<Button> configureCloseButton = null)
        {
            if (prefab == null)
                throw new ArgumentNullException(nameof(prefab));
            if (hubCanvasRoot == null)
                throw new ArgumentNullException(nameof(hubCanvasRoot));

            WordListHubFeedbackView view = Instantiate(prefab, hubCanvasRoot, false);
            view.parentRect = hubCanvasRoot;
            view.toastRect = view.transform as RectTransform;
            if (view.closeButton != null && view.closeListener != null)
                view.closeButton.onClick.RemoveListener(view.closeListener);
            view.closeListener = null;
            view.instanceInitialized = false;
            view.disposed = false;
            view.deactivatingOnDisable = false;
            view.remainingSeconds = 0f;
            view.autoDismiss = false;
            view.gameObject.SetActive(false);
            view.InitializeInstance(configureCloseButton);
            if (Application.isPlaying && view.GetComponent<WordListHubRuntimeInstance>() == null)
                view.gameObject.AddComponent<WordListHubRuntimeInstance>();
            view.Hide();
            view.transform.SetAsLastSibling();
            return view;
        }

        private void Initialize(
            RectTransform hubCanvasRoot,
            TMP_Text textStyleTemplate,
            Button closeButtonStyle,
            Action<Button> configureCloseButton)
        {
            parentRect = hubCanvasRoot;
            toastRect = transform as RectTransform;
            backgroundImage = GetComponent<Image>();
            backgroundImage.sprite = null;
            backgroundImage.type = Image.Type.Simple;
            backgroundImage.raycastTarget = false;
            backgroundImage.color = new Color32(25, 54, 45, 244);

            toastRect.anchorMin = new Vector2(0.5f, 0.75f);
            toastRect.anchorMax = new Vector2(0.5f, 0.75f);
            toastRect.pivot = new Vector2(0.5f, 0.5f);
            toastRect.anchoredPosition = Vector2.zero;
            toastRect.sizeDelta = new Vector2(DefaultWidth, ToastHeight);

            messageText = CreateMessageText(textStyleTemplate);
            closeButton = CreateCloseButton(closeButtonStyle, textStyleTemplate);
            InitializeInstance(configureCloseButton);
        }

        private void InitializeInstance(Action<Button> configureCloseButton)
        {
            if (instanceInitialized)
                return;
            if (messageText == null || closeButton == null)
                throw new InvalidOperationException(
                    "The feedback view prefab must reference its Message TMP and Close Button children.");

            configureCloseButton?.Invoke(closeButton);
            closeListener = Hide;
            closeButton.onClick.AddListener(closeListener);
            instanceInitialized = true;
        }

        private TMP_Text CreateMessageText(TMP_Text styleTemplate)
        {
            GameObject textObject = new GameObject(
                "Message",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            textObject.transform.SetParent(transform, false);

            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(MessageLeft, 12f);
            rect.offsetMax = new Vector2(-MessageRight, -12f);

            TMP_Text text = textObject.GetComponent<TMP_Text>();
            CopyTextStyle(styleTemplate, text);
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.richText = false;
            text.raycastTarget = false;
            text.text = string.Empty;
            return text;
        }

        private Button CreateCloseButton(Button styleTemplate, TMP_Text textStyleTemplate)
        {
            GameObject buttonObject = new GameObject(
                "CloseButton",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            buttonObject.transform.SetParent(transform, false);

            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.sizeDelta = new Vector2(42f, 42f);
            rect.anchoredPosition = new Vector2(-14f, 0f);

            Image image = buttonObject.GetComponent<Image>();
            image.sprite = null;
            image.type = Image.Type.Simple;
            image.color = new Color32(55, 58, 65, 255);
            image.raycastTarget = true;

            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;
            if (styleTemplate != null)
            {
                Graphic sourceGraphic = styleTemplate.targetGraphic;
                Image sourceImage = sourceGraphic as Image;
                if (sourceImage != null)
                {
                    if (sourceImage.sprite != null)
                    {
                        image.sprite = sourceImage.sprite;
                        image.type = sourceImage.type;
                    }
                    image.color = sourceImage.color;
                    image.preserveAspect = sourceImage.preserveAspect;
                }

                button.transition = styleTemplate.transition;
                button.colors = styleTemplate.colors;
                button.spriteState = styleTemplate.spriteState;
                button.animationTriggers = styleTemplate.animationTriggers;
                button.navigation = styleTemplate.navigation;
            }

            TMP_Text sourceLabel = styleTemplate != null
                ? styleTemplate.GetComponentInChildren<TMP_Text>(true)
                : textStyleTemplate;
            GameObject labelObject = new GameObject(
                "Label",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(buttonObject.transform, false);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            TMP_Text label = labelObject.GetComponent<TMP_Text>();
            CopyTextStyle(sourceLabel, label);
            label.text = "×";
            label.fontSize = Mathf.Min(label.fontSize, 28f);
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.raycastTarget = false;

            return button;
        }

        private static void CopyTextStyle(TMP_Text source, TMP_Text target)
        {
            if (target == null)
                return;
            if (source == null)
            {
                target.font = TMP_Settings.defaultFontAsset;
                return;
            }

            target.font = source.font;
            target.fontSharedMaterial = source.fontSharedMaterial;
            target.fontSize = source.fontSize;
            target.fontStyle = source.fontStyle;
            target.color = source.color;
            target.characterSpacing = source.characterSpacing;
            target.wordSpacing = source.wordSpacing;
            target.lineSpacing = source.lineSpacing;
            target.paragraphSpacing = source.paragraphSpacing;
            target.margin = source.margin;
        }

        private void Update()
        {
            TickUnscaled(Time.unscaledDeltaTime);
        }

        /// <summary>Advances automatic dismissal without waiting for Play Mode or wall-clock time.</summary>
        public void TickUnscaled(float unscaledDeltaSeconds)
        {
            if (!IsVisible || !autoDismiss || unscaledDeltaSeconds <= 0f
                || float.IsNaN(unscaledDeltaSeconds))
                return;

            remainingSeconds = Mathf.Max(0f, remainingSeconds - unscaledDeltaSeconds);
            if (remainingSeconds <= 0f)
                Hide();
        }

        /// <summary>Shows a replacement message; non-positive durations remain until closed.</summary>
        public void Show(string message, float durationSeconds, bool isError = false)
        {
            if (disposed || string.IsNullOrWhiteSpace(message))
            {
                Hide();
                return;
            }

            if (parentRect == null || !parentRect.gameObject.activeInHierarchy)
            {
                Hide();
                return;
            }

            messageText.text = message;
            remainingSeconds = durationSeconds > 0f && !float.IsInfinity(durationSeconds)
                && !float.IsNaN(durationSeconds) ? durationSeconds : 0f;
            autoDismiss = remainingSeconds > 0f;
            transform.SetAsLastSibling();
            if (!gameObject.activeSelf)
                gameObject.SetActive(true);
        }

        public void Hide()
        {
            remainingSeconds = 0f;
            autoDismiss = false;
            if (messageText != null)
                messageText.text = string.Empty;
            if (this != null && gameObject != null && gameObject.activeSelf)
                gameObject.SetActive(false);
        }

        private void OnDisable()
        {
            remainingSeconds = 0f;
            autoDismiss = false;
            if (messageText != null)
                messageText.text = string.Empty;
            if (!deactivatingOnDisable && gameObject != null && gameObject.activeSelf)
            {
                deactivatingOnDisable = true;
                gameObject.SetActive(false);
                deactivatingOnDisable = false;
            }
        }

        private void OnDestroy()
        {
            if (closeButton != null && closeListener != null)
                closeButton.onClick.RemoveListener(closeListener);
            closeListener = null;
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            Hide();
            if (gameObject == null)
                return;

            if (Application.isPlaying)
                Destroy(gameObject);
            else
                DestroyImmediate(gameObject);
        }
    }
}
