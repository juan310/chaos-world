using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace FH.UI
{
    /// <summary>Raises one callback when a control is hovered by the mouse or selected by keyboard/controller,
    /// so detail panels (tooltips) follow whichever device the player uses.</summary>
    public class UiHover : MonoBehaviour, IPointerEnterHandler, ISelectHandler
    {
        public Action OnFocus;
        public void OnPointerEnter(PointerEventData e) => OnFocus?.Invoke();
        public void OnSelect(BaseEventData e) => OnFocus?.Invoke();

        public static void Attach(UiButton b, Action onFocus)
        {
            var h = b.Button.gameObject.AddComponent<UiHover>();
            h.OnFocus = onFocus;
        }
    }
}
