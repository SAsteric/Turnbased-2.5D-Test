using UnityEngine;
using UnityEngine.EventSystems;

// Attached at runtime to command/skill/item buttons.
// When the pointer enters the button, it reports its description text
// to a callback the UI controller provides (Octopath-style hover text).
public class HoverDescription : MonoBehaviour, IPointerEnterHandler
{
    private string text;
    private System.Action<string> onHover;

    public void Bind(string description, System.Action<string> hoverCallback)
    {
        text = description;
        onHover = hoverCallback;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (onHover != null) onHover(text);
    }
}