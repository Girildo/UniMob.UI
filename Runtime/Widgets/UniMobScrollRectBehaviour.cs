using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UniMob.UI.Widgets
{
    /// <summary>
    /// A <see cref="ScrollRect"/> that forwards drags along its inactive axis to the parent
    /// hierarchy, so a vertical list nested in a horizontal pager scrolls both ways.
    /// </summary>
    internal class UniMobScrollRectBehaviour : ScrollRect
    {
        private bool _routeToParent;

        public override void OnInitializePotentialDrag(PointerEventData eventData)
        {
            ExecuteEvents.ExecuteHierarchy(transform.parent.gameObject, eventData,
                ExecuteEvents.initializePotentialDrag);

            base.OnInitializePotentialDrag(eventData);
        }

        public override void OnBeginDrag(PointerEventData eventData)
        {
            _routeToParent =
                vertical && Mathf.Abs(eventData.delta.x) > Mathf.Abs(eventData.delta.y) ||
                horizontal && Mathf.Abs(eventData.delta.x) < Mathf.Abs(eventData.delta.y);

            if (_routeToParent)
            {
                ExecuteEvents.ExecuteHierarchy(transform.parent.gameObject, eventData, ExecuteEvents.beginDragHandler);
                return;
            }

            base.OnBeginDrag(eventData);
        }

        public override void OnDrag(PointerEventData eventData)
        {
            if (_routeToParent)
            {
                ExecuteEvents.ExecuteHierarchy(transform.parent.gameObject, eventData, ExecuteEvents.dragHandler);
                return;
            }

            base.OnDrag(eventData);
        }

        public override void OnEndDrag(PointerEventData eventData)
        {
            if (_routeToParent)
            {
                ExecuteEvents.ExecuteHierarchy(transform.parent.gameObject, eventData, ExecuteEvents.endDragHandler);
                return;
            }

            base.OnEndDrag(eventData);
        }
    }
}
