using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FH.UI
{
    /// <summary>Button colouring driven directly each frame (works while the world is paused): idle, hovered or
    /// keyboard/controller-selected, disabled, and "active" for the current tab or chosen row.</summary>
    [RequireComponent(typeof(Button))]
    public class ButtonVisual : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public bool Accent;
        Button button;
        Image image;
        bool hovered;

        void Awake() { button = GetComponent<Button>(); image = GetComponent<Image>(); }
        public void OnPointerEnter(PointerEventData e) => hovered = true;
        public void OnPointerExit(PointerEventData e) => hovered = false;

        void LateUpdate()
        {
            if (!image) return;
            bool selected = EventSystem.current && EventSystem.current.currentSelectedGameObject == gameObject;
            Color c = !button.interactable ? UiKit.ButtonDisabled : (hovered || selected) ? UiKit.ButtonHot : Accent ? UiKit.ButtonAccent : UiKit.ButtonIdle;
            image.color = c;
        }
    }
}
