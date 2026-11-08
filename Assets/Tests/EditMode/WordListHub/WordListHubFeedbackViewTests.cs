using NUnit.Framework;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Words.UiMockups.WordListHub.Tests
{
    public sealed class WordListHubFeedbackViewTests
    {
        private GameObject canvasObject;
        private RectTransform canvasRoot;
        private TMP_Text textStyleTemplate;
        private Button closeButtonStyle;

        [SetUp]
        public void SetUp()
        {
            canvasObject = new GameObject("HubCanvas", typeof(RectTransform), typeof(Canvas));
            canvasRoot = canvasObject.GetComponent<RectTransform>();
            canvasRoot.sizeDelta = new Vector2(1280f, 720f);

            GameObject textObject = new GameObject(
                "TextStyle",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            textObject.transform.SetParent(canvasRoot, false);
            textStyleTemplate = textObject.GetComponent<TMP_Text>();
            TMP_FontAsset font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            if (font == null)
                font = TMP_Settings.defaultFontAsset;
            textStyleTemplate.font = font;
            textStyleTemplate.fontSize = 22f;

            GameObject buttonObject = new GameObject(
                "CloseStyle",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            buttonObject.transform.SetParent(canvasRoot, false);
            Image image = buttonObject.GetComponent<Image>();
            closeButtonStyle = buttonObject.GetComponent<Button>();
            closeButtonStyle.targetGraphic = image;
        }

        [TearDown]
        public void TearDown()
        {
            if (canvasObject != null)
                Object.DestroyImmediate(canvasObject);
        }

        [Test]
        public void CreateUsesQuarterUpAnchorAndOnlyCloseButtonBlocksRaycasts()
        {
            bool closeConfigured = false;
            WordListHubFeedbackView view = WordListHubFeedbackView.Create(
                canvasRoot,
                textStyleTemplate,
                closeButtonStyle,
                _ => closeConfigured = true);

            RectTransform toast = view.transform as RectTransform;
            Assert.That(toast.anchorMin, Is.EqualTo(new Vector2(0.5f, 0.75f)));
            Assert.That(toast.anchorMax, Is.EqualTo(new Vector2(0.5f, 0.75f)));
            Assert.That(toast.parent, Is.SameAs(canvasRoot));
            Assert.That(view.GetComponent<Image>().raycastTarget, Is.False);
            Assert.That(view.MessageText.raycastTarget, Is.False);
            Assert.That(view.CloseButton.targetGraphic.raycastTarget, Is.True);
            Assert.That(closeConfigured, Is.True);

            int childCount = canvasRoot.childCount;
            WordListHubFeedbackView repeated = WordListHubFeedbackView.Create(
                canvasRoot, textStyleTemplate, closeButtonStyle);
            Assert.That(repeated, Is.SameAs(view));
            Assert.That(canvasRoot.childCount, Is.EqualTo(childCount));
        }

        [Test]
        public void ShowReplacesMessageAndResetsUnscaledLifetime()
        {
            WordListHubFeedbackView view = CreateView();

            view.Show("First result", 3f);
            view.TickUnscaled(2f);
            view.Show("Replacement result", 3f, true);

            Assert.That(view.IsVisible, Is.True);
            Assert.That(view.MessageText.text, Is.EqualTo("Replacement result"));
            Assert.That(view.RemainingSeconds, Is.EqualTo(3f));
            view.TickUnscaled(2.8f);
            Assert.That(view.IsVisible, Is.True);
            view.TickUnscaled(0.21f);
            Assert.That(view.IsVisible, Is.False);
            Assert.That(view.RemainingSeconds, Is.EqualTo(0f));
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        public void NonPositiveDurationRemainsUntilClosed(float durationSeconds)
        {
            WordListHubFeedbackView view = CreateView();

            view.Show("Manual close", durationSeconds);
            view.TickUnscaled(100f);

            Assert.That(view.IsVisible, Is.True);
            Assert.That(view.RemainingSeconds, Is.EqualTo(0f));
            view.CloseButton.onClick.Invoke();
            Assert.That(view.IsVisible, Is.False);
        }

        [Test]
        public void CreateFromPrefabPreservesAuthoredGeometryAndTextStyle()
        {
            WordListHubFeedbackView prefabTemplate = CreateView();
            RectTransform authoredRect = prefabTemplate.transform as RectTransform;
            authoredRect.anchorMin = new Vector2(0.25f, 0.6f);
            authoredRect.anchorMax = new Vector2(0.75f, 0.8f);
            authoredRect.pivot = new Vector2(0.3f, 0.65f);
            authoredRect.anchoredPosition = new Vector2(17f, -23f);
            authoredRect.sizeDelta = new Vector2(612f, 86f);
            authoredRect.localScale = new Vector3(1.15f, 0.92f, 1f);
            prefabTemplate.MessageText.fontSize = 31f;
            prefabTemplate.MessageText.color = new Color32(245, 220, 180, 255);
            prefabTemplate.BackgroundImage.color = new Color32(60, 44, 80, 230);

            GameObject runtimeParentObject = new GameObject("RuntimeHubCanvas", typeof(RectTransform));
            runtimeParentObject.transform.SetParent(canvasRoot, false);
            RectTransform runtimeParent = runtimeParentObject.GetComponent<RectTransform>();
            runtimeParent.sizeDelta = new Vector2(960f, 540f);

            bool closeConfigured = false;
            WordListHubFeedbackView view = WordListHubFeedbackView.CreateFromPrefab(
                prefabTemplate, runtimeParent, _ => closeConfigured = true);
            RectTransform instanceRect = view.transform as RectTransform;

            Assert.That(closeConfigured, Is.True);
            Assert.That(instanceRect.anchorMin, Is.EqualTo(authoredRect.anchorMin));
            Assert.That(instanceRect.anchorMax, Is.EqualTo(authoredRect.anchorMax));
            Assert.That(instanceRect.pivot, Is.EqualTo(authoredRect.pivot));
            Assert.That(instanceRect.anchoredPosition, Is.EqualTo(authoredRect.anchoredPosition));
            Assert.That(instanceRect.sizeDelta, Is.EqualTo(authoredRect.sizeDelta));
            Assert.That(instanceRect.localScale, Is.EqualTo(authoredRect.localScale));
            Assert.That(view.MessageText.fontSize, Is.EqualTo(31f));
            Assert.That((Color32)view.MessageText.color, Is.EqualTo(new Color32(245, 220, 180, 255)));
            Assert.That((Color32)view.BackgroundImage.color, Is.EqualTo(new Color32(60, 44, 80, 230)));

            view.Show("Editable prefab style", 3f);
            runtimeParent.sizeDelta = new Vector2(800f, 450f);
            view.TickUnscaled(1f);

            Assert.That(instanceRect.anchorMin, Is.EqualTo(authoredRect.anchorMin));
            Assert.That(instanceRect.anchorMax, Is.EqualTo(authoredRect.anchorMax));
            Assert.That(instanceRect.sizeDelta, Is.EqualTo(authoredRect.sizeDelta));
            Assert.That(instanceRect.localScale, Is.EqualTo(authoredRect.localScale));
            Assert.That(view.MessageText.fontSize, Is.EqualTo(31f));
            Assert.That((Color32)view.MessageText.color, Is.EqualTo(new Color32(245, 220, 180, 255)));
            Assert.That((Color32)view.BackgroundImage.color, Is.EqualTo(new Color32(60, 44, 80, 230)));
            view.CloseButton.onClick.Invoke();
            Assert.That(view.IsVisible, Is.False);
        }

        [Test]
        public void ParentDisableClearsAndHidesPopup()
        {
            WordListHubFeedbackView view = CreateView();
            view.Show("Hub operation complete", 3f);

            canvasObject.SetActive(false);
            InvokeOnDisableForEditMode(view);

            Assert.That(view.IsVisible, Is.False);
            Assert.That(view.RemainingSeconds, Is.EqualTo(0f));
            canvasObject.SetActive(true);
            Assert.That(view.IsVisible, Is.False);
        }

        private WordListHubFeedbackView CreateView()
        {
            return WordListHubFeedbackView.Create(canvasRoot, textStyleTemplate, closeButtonStyle);
        }

        private static void InvokeOnDisableForEditMode(WordListHubFeedbackView view)
        {
            MethodInfo onDisable = typeof(WordListHubFeedbackView).GetMethod(
                "OnDisable", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(onDisable, Is.Not.Null);
            onDisable.Invoke(view, null);
        }
    }
}
