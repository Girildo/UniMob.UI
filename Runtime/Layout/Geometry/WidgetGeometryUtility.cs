using UnityEngine;

namespace UniMob.UI.Layout
{
    /// <summary>
    /// Computes a <see cref="WidgetGeometry"/> from a mounted <see cref="RectTransform"/>. Unity's
    /// <see cref="RectTransform.GetWorldCorners"/> reports corners in <i>screen</i> space, i.e. already
    /// scaled by the root canvas; canvas-space corners are therefore the world corners divided by
    /// <c>rootCanvas.scaleFactor</c>. This mirrors the compensation the presentation layer's legacy
    /// RenderMetrics measurement has always applied.
    /// </summary>
    public static class WidgetGeometryUtility
    {
        // Reused across calls to avoid per-frame allocation while tracking. Unity is single-threaded and
        // GetWorldCorners fills-then-reads within a single call, so a shared buffer is safe.
        private static readonly Vector3[] WorldCornersBuffer = new Vector3[4];

        public static bool TryCompute(
            RectTransform rectTransform,
            Canvas rootCanvas,
            out WidgetGeometry geometry
        )
        {
            if (rectTransform == null || rootCanvas == null)
            {
                geometry = WidgetGeometry.Empty;
                return false;
            }

            rectTransform.GetWorldCorners(WorldCornersBuffer);

            var world = new Quad(
                WorldCornersBuffer[0],
                WorldCornersBuffer[1],
                WorldCornersBuffer[2],
                WorldCornersBuffer[3]
            );

            var scale = rootCanvas.scaleFactor;
            var inverseScale = Mathf.Approximately(scale, 0f) ? 1f : 1f / scale;

            var canvas = new Quad(
                (Vector2)WorldCornersBuffer[0] * inverseScale,
                (Vector2)WorldCornersBuffer[1] * inverseScale,
                (Vector2)WorldCornersBuffer[2] * inverseScale,
                (Vector2)WorldCornersBuffer[3] * inverseScale
            );

            geometry = new WidgetGeometry(canvas, world);
            return true;
        }
    }
}
