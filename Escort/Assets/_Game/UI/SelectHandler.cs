using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace HS.UI
{
    /// <summary>Calls back when a UI element is selected (a pad or the keys moving onto it): the picker shows the skill.</summary>
    public sealed class SelectHandler : MonoBehaviour, ISelectHandler
    {
        public Action OnSelected;
        public void OnSelect(BaseEventData e) => OnSelected?.Invoke();
    }
}
