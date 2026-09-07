using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
public sealed class LingshanEndMenu : MonoBehaviour, ILingshanMenuOptionOwner
{
    private static readonly Color AccentRed = new Color(0.66f, 0.055f, 0.035f, 1f);
    private static readonly Color FineLine = new Color(0.43f, 0.45f, 0.47f, 0.28f);

    private RollerCoasterRide ride;
    private RollerCoasterLookController lookController;
    private CanvasGroup rootGroup;
    private LingshanMenuBackdrop menuBackdrop;
    private CanvasGroup contentGroup;
    private CanvasGroup blackFadeGroup;
    private RectTransform contentRect;
    private RectTransform backgroundRect;
    private Canvas menuCanvas;
    private Vector2 contentRestPosition;
    private Vector2 backgroundRestPosition;
    private LingshanMenuOption againOption;
    private LingshanMenuOption exitOption;
    private EventSystem eventSystem;
    private GameObject ownedEventSystemObject;
    private bool transitioning;
    private bool menuBuilt;
    private readonly LingshanXRMenuInput xrMenuInput = new LingshanXRMenuInput();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureMenuExists()
    {
        if (IsShowing)
        {
            return;
        }

        RollerCoasterRide activeRide = Object.FindAnyObjectByType<RollerCoasterRide>();
        if (activeRide != null)
        {
            WatchForFinish(activeRide);
        }
    }

    public static bool IsShowing => Object.FindAnyObjectByType<LingshanEndMenu>() != null;

    public static void WatchForFinish(RollerCoasterRide activeRide)
    {
        if (activeRide == null || IsShowing)
        {
            return;
        }

        GameObject menuObject = new GameObject("Lingshan End Menu");
        LingshanEndMenu menu = menuObject.AddComponent<LingshanEndMenu>();
        menu.Initialize(activeRide);
    }

    private void Initialize(RollerCoasterRide finishedRide)
    {
        if (ride != null)
        {
            ride.FinishedAtEnd -= HandleRideFinished;
        }

        ride = finishedRide;
        if (ride != null)
        {
            ride.FinishedAtEnd += HandleRideFinished;
            if (ride.IsFinished)
            {
                BeginEndPage();
            }
        }
    }

    private void Awake()
    {
        if (ride == null)
        {
            ride = Object.FindAnyObjectByType<RollerCoasterRide>();
        }

        if (ride == null)
        {
            Destroy(gameObject);
            return;
        }

        lookController = Object.FindAnyObjectByType<RollerCoasterLookController>();
        LingshanXRMenuSupport.AttachToHead(transform);
    }

    private void Update()
    {
        if (!menuBuilt)
        {
            return;
        }

        if (backgroundRect != null && !transitioning)
        {
            Vector2 pointerOffset = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
            if (!LingshanXRMenuSupport.UsesWorldSpaceCanvas &&
                Pointer.current != null && Screen.width > 0 && Screen.height > 0)
            {
                Vector2 normalized = Pointer.current.position.ReadValue();
                normalized.x = normalized.x / Screen.width - 0.5f;
                normalized.y = normalized.y / Screen.height - 0.5f;
                pointerOffset = new Vector2(-normalized.x * 14f, -normalized.y * 8f);
            }
#endif
            Vector2 target = backgroundRestPosition + pointerOffset;
            backgroundRect.anchoredPosition = Vector2.Lerp(
                backgroundRect.anchoredPosition,
                target,
                1f - Mathf.Exp(-Time.unscaledDeltaTime * 2.8f));
        }

#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (!transitioning && keyboard != null && keyboard.escapeKey.wasPressedThisFrame && eventSystem != null)
        {
            eventSystem.SetSelectedGameObject(exitOption.gameObject);
        }
#endif

        if (!transitioning && rootGroup != null && rootGroup.interactable)
        {
            xrMenuInput.Update(eventSystem, againOption.Button, exitOption.Button);
        }
    }

    public void NotifyOptionSelected(LingshanMenuOption selectedOption)
    {
        againOption.SetSelected(selectedOption == againOption);
        exitOption.SetSelected(selectedOption == exitOption);
    }

    private void EnsureEventSystem()
    {
        eventSystem = EventSystem.current;
        if (eventSystem == null)
        {
            ownedEventSystemObject = new GameObject("End Menu EventSystem");
            eventSystem = ownedEventSystemObject.AddComponent<EventSystem>();
            InputSystemUIInputModule inputModule =
                ownedEventSystemObject.AddComponent<InputSystemUIInputModule>();
            inputModule.AssignDefaultActions();
        }

        LingshanXRMenuSupport.ConfigureEventSystem(eventSystem, eventSystem.gameObject);
    }

    private void BuildInterface()
    {
        Font titleFont = Resources.Load<Font>("StartMenu/Fonts/LoveYaLikeASister");
        Font menuFont = Resources.Load<Font>("StartMenu/Fonts/RobotoMono");
        Font fallbackFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        titleFont = titleFont != null ? titleFont : fallbackFont;
        menuFont = menuFont != null ? menuFont : fallbackFont;

        GameObject canvasObject = CreateObject("End Menu Canvas", transform);
        menuCanvas = canvasObject.AddComponent<Canvas>();
        menuCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        menuCanvas.sortingOrder = 1250;
        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        LingshanXRMenuSupport.ConfigureCanvas(menuCanvas, scaler);
        canvasObject.AddComponent<GraphicRaycaster>();

        rootGroup = canvasObject.AddComponent<CanvasGroup>();
        rootGroup.alpha = 0f;
        rootGroup.interactable = false;
        rootGroup.blocksRaycasts = true;

        CreateBackground(canvasObject.transform);
        CreateShade(canvasObject.transform);
        CreateContent(canvasObject.transform, titleFont, menuFont);
        CreateBlackFade(canvasObject.transform);
        menuBackdrop = LingshanMenuBackdrop.Show(menuCanvas);

        Navigation againNavigation = againOption.Button.navigation;
        againNavigation.mode = Navigation.Mode.Explicit;
        againNavigation.selectOnDown = exitOption.Button;
        againNavigation.selectOnUp = exitOption.Button;
        againOption.Button.navigation = againNavigation;

        Navigation exitNavigation = exitOption.Button.navigation;
        exitNavigation.mode = Navigation.Mode.Explicit;
        exitNavigation.selectOnDown = againOption.Button;
        exitNavigation.selectOnUp = againOption.Button;
        exitOption.Button.navigation = exitNavigation;

        againOption.SetSelected(true, true);
        exitOption.SetSelected(false, true);
        eventSystem.SetSelectedGameObject(againOption.gameObject);
    }

    private void BeginEndPage()
    {
        if (menuBuilt || ride == null)
        {
            return;
        }

        menuBuilt = true;
        transitioning = true;
        ride.HoldAtFinishedEnd();
        LingshanXRMenuSupport.AttachToHead(transform);
        if (lookController != null)
        {
            lookController.enabled = false;
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        EnsureEventSystem();
        BuildInterface();
        StartCoroutine(PlayIntro());
        Debug.Log("Lingshan end menu ready.", this);
    }

    private void CreateBackground(Transform parent)
    {
        GameObject backgroundObject = CreateObject("Figma Background", parent);
        backgroundRect = backgroundObject.GetComponent<RectTransform>();
        SetAnchors(backgroundRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        backgroundRect.sizeDelta = new Vector2(1920f, 1080f);
        backgroundRestPosition = Vector2.zero;

        RawImage background = backgroundObject.AddComponent<RawImage>();
        background.texture = Resources.Load<Texture2D>("StartMenu/LingshanMenuBackground");
        background.color = background.texture == null
            ? new Color(0.035f, 0.04f, 0.045f, 1f)
            : Color.white;
        background.raycastTarget = false;

        AspectRatioFitter fitter = backgroundObject.AddComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        fitter.aspectRatio = background.texture == null
            ? 16f / 9f
            : (float)background.texture.width / background.texture.height;
    }

    private static void CreateShade(Transform parent)
    {
        GameObject shadeObject = CreateObject("Cinematic Shade", parent);
        RectTransform shadeRect = shadeObject.GetComponent<RectTransform>();
        Stretch(shadeRect);
        Image shade = shadeObject.AddComponent<Image>();
        shade.color = new Color(0f, 0.01f, 0.015f, 0.13f);
        shade.raycastTarget = false;
    }

    private void CreateContent(Transform parent, Font titleFont, Font menuFont)
    {
        GameObject contentObject = CreateObject("Menu Content", parent);
        contentRect = contentObject.GetComponent<RectTransform>();
        SetAnchors(contentRect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
        contentRect.pivot = new Vector2(0f, 0.5f);
        contentRect.sizeDelta = new Vector2(820f, 740f);
        contentRestPosition = new Vector2(168f, 26f);
        contentRect.anchoredPosition = contentRestPosition;
        contentGroup = contentObject.AddComponent<CanvasGroup>();

        CreateText(
            "Title Line 1",
            contentObject.transform,
            "LINGSHAN",
            titleFont,
            108,
            new Vector2(0f, -18f),
            new Vector2(800f, 126f),
            Color.white,
            true);

        CreateText(
            "Title Line 2",
            contentObject.transform,
            "<color=#9D1713>S</color>WIFT RIDE",
            titleFont,
            108,
            new Vector2(0f, -132f),
            new Vector2(810f, 126f),
            Color.white,
            true);

        CreateText(
            "Tagline",
            contentObject.transform,
            "JOURNEY COMPLETE",
            menuFont,
            24,
            new Vector2(4f, -272f),
            new Vector2(720f, 42f),
            new Color(0.73f, 0.75f, 0.77f, 0.92f),
            false);

        CreateChallengeResults(contentObject.transform, menuFont);

        GameObject spineObject = CreateObject("Menu Spine", contentObject.transform);
        RectTransform spineRect = spineObject.GetComponent<RectTransform>();
        SetTopLeft(spineRect, new Vector2(4f, -473f), new Vector2(1.5f, 166f));
        Image spine = spineObject.AddComponent<Image>();
        spine.color = FineLine;
        spine.raycastTarget = false;

        againOption = CreateOption(
            contentObject.transform,
            "Again",
            "A G A I N",
            -468f,
            menuFont,
            PlayAgain);
        exitOption = CreateOption(
            contentObject.transform,
            "Exit",
            "E X I T",
            -556f,
            menuFont,
            ExitGame);

        GameObject ruleObject = CreateObject("Lower Rule", contentObject.transform);
        RectTransform ruleRect = ruleObject.GetComponent<RectTransform>();
        SetTopLeft(ruleRect, new Vector2(4f, -644f), new Vector2(560f, 1f));
        Image rule = ruleObject.AddComponent<Image>();
        rule.color = FineLine;
        rule.raycastTarget = false;
    }

    private static void CreateChallengeResults(Transform parent, Font menuFont)
    {
        LingshanCollectChallenge challenge = LingshanCollectChallenge.Instance;
        int targetCount = challenge != null ? challenge.TargetCount : 12;
        int score = challenge != null ? challenge.Score : 0;
        int collected = challenge != null ? challenge.CollectedCount : 0;
        // The end page and challenge can receive FinishedAtEnd in either order.
        // Include this completed run even if persistence happens later that frame.
        int bestScore = challenge != null ? Mathf.Max(score, challenge.BestScore) : 0;

        CreateText(
            "Run Score", parent, $"SCORE  {score} / {targetCount * 100}", menuFont, 32,
            new Vector2(4f, -328f), new Vector2(720f, 44f), Color.white, false);
        CreateText(
            "Collected Charms", parent, $"CHARMS  {collected} / {targetCount}", menuFont, 24,
            new Vector2(4f, -377f), new Vector2(720f, 34f),
            new Color(0.87f, 0.88f, 0.89f, 1f), false);
        CreateText(
            "Best Score", parent, $"PERSONAL BEST  {bestScore}", menuFont, 24,
            new Vector2(4f, -414f), new Vector2(720f, 34f),
            new Color(0.87f, 0.88f, 0.89f, 1f), false);
    }

    private LingshanMenuOption CreateOption(
        Transform parent,
        string name,
        string labelValue,
        float y,
        Font menuFont,
        UnityEngine.Events.UnityAction action)
    {
        GameObject optionObject = CreateObject(name + " Option", parent);
        RectTransform optionRect = optionObject.GetComponent<RectTransform>();
        SetTopLeft(optionRect, new Vector2(4f, y), new Vector2(620f, 72f));

        Image hitArea = optionObject.AddComponent<Image>();
        hitArea.color = Color.clear;
        Button button = optionObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = hitArea;
        button.onClick.AddListener(action);

        GameObject highlightObject = CreateObject("Selection", optionObject.transform);
        RectTransform highlightRect = highlightObject.GetComponent<RectTransform>();
        highlightRect.anchorMin = new Vector2(0f, 0f);
        highlightRect.anchorMax = new Vector2(0f, 1f);
        highlightRect.pivot = new Vector2(0f, 0.5f);
        highlightRect.anchoredPosition = Vector2.zero;
        highlightRect.sizeDelta = new Vector2(560f, 0f);
        HorizontalGradientGraphic gradient = highlightObject.AddComponent<HorizontalGradientGraphic>();
        gradient.raycastTarget = false;
        CanvasGroup highlightGroup = highlightObject.AddComponent<CanvasGroup>();

        GameObject accentLineObject = CreateObject("Accent Line", highlightObject.transform);
        RectTransform accentLineRect = accentLineObject.GetComponent<RectTransform>();
        accentLineRect.anchorMin = new Vector2(0f, 0f);
        accentLineRect.anchorMax = new Vector2(0f, 1f);
        accentLineRect.pivot = new Vector2(0.5f, 0.5f);
        accentLineRect.anchoredPosition = new Vector2(1f, 0f);
        accentLineRect.sizeDelta = new Vector2(2f, 0f);
        Image accentLine = accentLineObject.AddComponent<Image>();
        accentLine.color = AccentRed;
        accentLine.raycastTarget = false;

        GameObject diamondObject = CreateObject("Selector Diamond", optionObject.transform);
        RectTransform diamondRect = diamondObject.GetComponent<RectTransform>();
        diamondRect.anchorMin = new Vector2(0f, 0.5f);
        diamondRect.anchorMax = new Vector2(0f, 0.5f);
        diamondRect.pivot = new Vector2(0.5f, 0.5f);
        diamondRect.anchoredPosition = new Vector2(1f, 0f);
        diamondRect.sizeDelta = new Vector2(18f, 18f);
        diamondRect.localRotation = Quaternion.Euler(0f, 0f, 45f);
        Image diamondOuter = diamondObject.AddComponent<Image>();
        diamondOuter.color = AccentRed;
        diamondOuter.raycastTarget = false;
        CanvasGroup diamondGroup = diamondObject.AddComponent<CanvasGroup>();

        GameObject diamondInnerObject = CreateObject("Inner", diamondObject.transform);
        RectTransform diamondInnerRect = diamondInnerObject.GetComponent<RectTransform>();
        SetAnchors(diamondInnerRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        diamondInnerRect.sizeDelta = new Vector2(12f, 12f);
        Image diamondInner = diamondInnerObject.AddComponent<Image>();
        diamondInner.color = new Color(0.07f, 0.035f, 0.03f, 1f);
        diamondInner.raycastTarget = false;

        GameObject diamondCoreObject = CreateObject("Core", diamondInnerObject.transform);
        RectTransform diamondCoreRect = diamondCoreObject.GetComponent<RectTransform>();
        SetAnchors(diamondCoreRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        diamondCoreRect.sizeDelta = new Vector2(4f, 4f);
        Image diamondCore = diamondCoreObject.AddComponent<Image>();
        diamondCore.color = AccentRed;
        diamondCore.raycastTarget = false;

        Text label = CreateText(
            "Label",
            optionObject.transform,
            labelValue,
            menuFont,
            22,
            Vector2.zero,
            new Vector2(580f, 72f),
            Color.white,
            false);
        label.rectTransform.anchorMin = new Vector2(0f, 0f);
        label.rectTransform.anchorMax = new Vector2(0f, 1f);
        label.rectTransform.pivot = new Vector2(0f, 0.5f);
        label.rectTransform.anchoredPosition = new Vector2(52f, 0f);
        label.rectTransform.sizeDelta = new Vector2(540f, 0f);

        LingshanMenuOption option = optionObject.AddComponent<LingshanMenuOption>();
        option.Initialize(this, button, highlightRect, highlightGroup, diamondGroup, label);
        return option;
    }

    private void CreateBlackFade(Transform parent)
    {
        GameObject fadeObject = CreateObject("Black Fade", parent);
        RectTransform fadeRect = fadeObject.GetComponent<RectTransform>();
        Stretch(fadeRect);
        Image fadeImage = fadeObject.AddComponent<Image>();
        fadeImage.color = Color.black;
        fadeImage.raycastTarget = false;
        blackFadeGroup = fadeObject.AddComponent<CanvasGroup>();
        blackFadeGroup.alpha = 0f;
    }

    private IEnumerator PlayIntro()
    {
        contentGroup.alpha = 0f;
        contentRect.anchoredPosition = contentRestPosition + new Vector2(-58f, -8f);
        backgroundRect.localScale = Vector3.one * 1.045f;

        const float duration = 0.82f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            rootGroup.alpha = Mathf.Clamp01(t * 1.45f);
            contentGroup.alpha = Mathf.Clamp01((t - 0.16f) / 0.66f);
            contentRect.anchoredPosition = Vector2.Lerp(
                contentRestPosition + new Vector2(-58f, -8f),
                contentRestPosition,
                eased);
            backgroundRect.localScale = Vector3.Lerp(
                Vector3.one * 1.045f,
                Vector3.one,
                eased);
            yield return null;
        }

        rootGroup.alpha = 1f;
        contentGroup.alpha = 1f;
        contentRect.anchoredPosition = contentRestPosition;
        backgroundRect.localScale = Vector3.one;
        rootGroup.interactable = true;
        eventSystem.SetSelectedGameObject(againOption.gameObject);
        transitioning = false;
    }

    private void HandleRideFinished(RollerCoasterRide finishedRide)
    {
        if (finishedRide == ride)
        {
            BeginEndPage();
        }
    }

    private void PlayAgain()
    {
        if (transitioning) return;
        transitioning = true;
        StopAllCoroutines();
        menuBackdrop?.Restore();
        ride.StartRideFromMenu();
        Debug.Log("Lingshan end menu: AGAIN confirmed.", this);
        if (lookController != null)
        {
            lookController.enabled = true;
        }

        eventSystem.SetSelectedGameObject(null);
        rootGroup.alpha = 0f;
        rootGroup.interactable = false;
        menuCanvas.enabled = false;
        Destroy(menuCanvas.gameObject);
        menuCanvas = null;
        menuBuilt = false;
        transitioning = false;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void ExitGame()
    {
        if (!transitioning)
        {
            StartCoroutine(ExitGameRoutine());
        }
    }

    private IEnumerator ExitGameRoutine()
    {
        transitioning = true;
        rootGroup.interactable = false;
        eventSystem.SetSelectedGameObject(exitOption.gameObject);

        const float duration = 0.55f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            contentGroup.alpha = 1f - t;
            blackFadeGroup.alpha = t * t;
            yield return null;
        }

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void OnDestroy()
    {
        if (ride != null)
        {
            ride.FinishedAtEnd -= HandleRideFinished;
        }

        if (ownedEventSystemObject != null)
        {
            Destroy(ownedEventSystemObject);
        }
    }

    private static Text CreateText(
        string name,
        Transform parent,
        string value,
        Font font,
        int size,
        Vector2 position,
        Vector2 dimensions,
        Color color,
        bool richText)
    {
        GameObject textObject = CreateObject(name, parent);
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        SetTopLeft(textRect, position, dimensions);
        Text text = textObject.AddComponent<Text>();
        text.text = value;
        text.font = font;
        text.fontSize = size;
        text.fontStyle = FontStyle.Normal;
        text.alignment = TextAnchor.MiddleLeft;
        text.color = color;
        text.supportRichText = richText;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = name.StartsWith("Title Line")
            ? VerticalWrapMode.Overflow : VerticalWrapMode.Truncate;
        text.raycastTarget = false;
        return text;
    }

    private static GameObject CreateObject(string name, Transform parent)
    {
        GameObject result = new GameObject(name, typeof(RectTransform));
        result.transform.SetParent(parent, false);
        return result;
    }

    private static void SetTopLeft(RectTransform rect, Vector2 position, Vector2 dimensions)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = dimensions;
    }

    private static void SetAnchors(RectTransform rect, Vector2 minimum, Vector2 maximum)
    {
        rect.anchorMin = minimum;
        rect.anchorMax = maximum;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
    }
}
