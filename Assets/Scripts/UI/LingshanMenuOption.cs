using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Shared owner contract keeps the animated option reusable on both menu pages.
public interface ILingshanMenuOptionOwner
{
    void NotifyOptionSelected(LingshanMenuOption selectedOption);
}

[DisallowMultipleComponent]
public sealed class LingshanMenuOption : MonoBehaviour, ISelectHandler, IPointerEnterHandler
{
    private const float HighlightWidth = 560f;

    private ILingshanMenuOptionOwner owner;
    private RectTransform highlightRect;
    private CanvasGroup highlightGroup;
    private CanvasGroup diamondGroup;
    private Text label;
    private float selectionAmount;
    private bool selected;

    public Button Button { get; private set; }

    public void Initialize(
        ILingshanMenuOptionOwner menuOwner,
        Button optionButton,
        RectTransform optionHighlight,
        CanvasGroup optionHighlightGroup,
        CanvasGroup optionDiamondGroup,
        Text optionLabel)
    {
        owner = menuOwner;
        Button = optionButton;
        highlightRect = optionHighlight;
        highlightGroup = optionHighlightGroup;
        diamondGroup = optionDiamondGroup;
        label = optionLabel;
        ApplyVisuals();
    }

    public void SetSelected(bool isSelected, bool immediate = false)
    {
        selected = isSelected;
        if (immediate)
        {
            selectionAmount = selected ? 1f : 0f;
            ApplyVisuals();
        }
    }

    private void Update()
    {
        float target = selected ? 1f : 0f;
        selectionAmount = Mathf.MoveTowards(
            selectionAmount,
            target,
            Time.unscaledDeltaTime * 6.8f);
        ApplyVisuals();
    }

    public void OnSelect(BaseEventData eventData)
    {
        owner.NotifyOptionSelected(this);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(gameObject);
        }
    }

    private void ApplyVisuals()
    {
        float eased = 1f - Mathf.Pow(1f - selectionAmount, 3f);
        if (highlightGroup != null)
        {
            highlightGroup.alpha = eased;
        }

        if (diamondGroup != null)
        {
            diamondGroup.alpha = eased;
        }

        if (highlightRect != null)
        {
            highlightRect.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Horizontal,
                Mathf.Lerp(12f, HighlightWidth, eased));
        }

        if (label != null)
        {
            label.color = Color.Lerp(
                new Color(0.55f, 0.57f, 0.59f, 0.9f),
                Color.white,
                eased);
            label.rectTransform.anchoredPosition = new Vector2(
                Mathf.Lerp(40f, 52f, eased),
                0f);
        }
    }
}
