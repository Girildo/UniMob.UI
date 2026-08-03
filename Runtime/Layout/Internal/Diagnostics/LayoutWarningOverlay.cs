#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace UniMob.UI.Layout.Internal.Diagnostics
{
    /// <summary>
    ///     The hazard stripe painted over a box the layout system is unhappy about, pooled per view.
    /// </summary>
    /// <remarks>
    ///     <b>Level-triggered</b>, and that is the point: the console says a fault started, this says it
    ///     is happening. A zero-sized widget is otherwise indistinguishable from an intentionally empty
    ///     one, which is what makes the stripe half of the repair policy rather than decoration on it.
    ///     <para>
    ///         Private to the package's own painting, unlike <c>UniMob.UI.Diagnostics</c>, which anyone
    ///         may call: this is driven from inside a view's Render and has no caller outside.
    ///     </para>
    /// </remarks>
    internal sealed class LayoutWarningOverlay
    {
        private static Sprite _stripeSprite;

        private readonly List<UnityEngine.UI.Image> _pool = new List<UnityEngine.UI.Image>();
        private int _used;

        private static Sprite StripeSprite
        {
            get
            {
                if (_stripeSprite != null)
                {
                    return _stripeSprite;
                }

                const int size = 16;
                var tex = new Texture2D(size, size)
                {
                    wrapMode = TextureWrapMode.Repeat,
                    filterMode = FilterMode.Point,
                    hideFlags = HideFlags.DontSave,
                };

                var pixels = new Color32[size * size];
                for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    pixels[y * size + x] =
                        (x + y) % size < size / 2
                            ? new Color32(255, 220, 0, 220)
                            : new Color32(20, 20, 20, 220);
                }

                tex.SetPixels32(pixels);
                tex.Apply();

                _stripeSprite = Sprite.Create(
                    tex,
                    new Rect(0, 0, size, size),
                    Vector2.zero,
                    size,
                    0,
                    SpriteMeshType.FullRect
                );
                _stripeSprite.hideFlags = HideFlags.DontSave;
                return _stripeSprite;
            }
        }

        /// <summary>Covers <paramref name="target"/> with a stripe, reusing one from the pool.</summary>
        public void Paint(RectTransform target)
        {
            // Qualified: UniMob.UI.Layout.Image is a widget, and this file sits under that
            // namespace, so it wins over UnityEngine.UI.Image on an unqualified name.
            UnityEngine.UI.Image overlay;
            if (_used < _pool.Count)
            {
                overlay = _pool[_used];
                overlay.gameObject.SetActive(true);
            }
            else
            {
                var go = new GameObject("LayoutWarning", typeof(RectTransform), typeof(UnityEngine.UI.Image))
                {
                    hideFlags = HideFlags.DontSave,
                };

                overlay = go.GetComponent<UnityEngine.UI.Image>();
                overlay.sprite = StripeSprite;
                overlay.type = UnityEngine.UI.Image.Type.Tiled;
                overlay.raycastTarget = false;
                _pool.Add(overlay);
            }

            _used++;

            var rt = (RectTransform)overlay.transform;
            rt.SetParent(target.parent, false); // sibling of the target, same coordinate space
            rt.SetAsLastSibling(); // paint on top
            rt.anchorMin = target.anchorMin;
            rt.anchorMax = target.anchorMax;
            rt.pivot = target.pivot;
            rt.anchoredPosition = target.anchoredPosition;

            // Never smaller than this, even though it means the stripe is not the box. The commonest
            // fault reaching here is a non-finite axis, and the repair for that is to zero the axis --
            // so drawing the marker at the box's own size draws nothing at all, in precisely the case
            // it exists for. A zeroed widget is indistinguishable from an intentionally empty one, and
            // that ambiguity is the whole reason the clamp is paired with a marker; a marker that
            // disappears with the box does not resolve it.
            const float minimumVisibleExtent = 16f;
            rt.sizeDelta = new Vector2(
                Mathf.Max(target.sizeDelta.x, minimumVisibleExtent),
                Mathf.Max(target.sizeDelta.y, minimumVisibleExtent)
            );
        }

        /// <summary>
        ///     Ends a paint pass: anything not painted this time is hidden, so a fault that stopped
        ///     stops being drawn.
        /// </summary>
        public void HideUnused()
        {
            for (var i = _used; i < _pool.Count; i++)
            {
                _pool[i].gameObject.SetActive(false);
            }

            _used = 0;
        }
    }
}
#endif
