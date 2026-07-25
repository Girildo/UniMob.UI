#if UNITY_EDITOR
using System.Collections;
using System.IO;
using UniMob.UI.Widgets;
using UnityEngine;

namespace UniMob.UI.DevTools
{
    /// <summary>
    /// Renders a single widget tree in isolation to a PNG: mounts it under a Canvas + ViewPanel at a
    /// fixed logical size (the same hosting path as the real app, via <see cref="UniMobUI.RunApp"/>) and
    /// reads back an offscreen RenderTexture. Editor/test-only visual-diff infra; the caller supplies the
    /// fully-built root widget and owns its Lifetime / any DI scope.
    /// </summary>
    public static class WidgetCapture
    {
        public static IEnumerator CaptureToPng(
            Lifetime lifetime,
            Widget root,
            string outputPath,
            int width,
            int height,
            Color background,
            int settleFrames = 12
        )
        {
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                name = "WidgetCaptureRT",
            };
            rt.Create();

            var camGo = new GameObject("WidgetCaptureCamera");
            camGo.transform.position = new Vector3(0, 0, -100);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = height / 2f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 1000f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = background;
            cam.targetTexture = rt;

            var canvasGo = new GameObject("WidgetCaptureCanvas", typeof(RectTransform), typeof(Canvas));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 100f;

            // ViewPanel is sized up front so the first (synchronous) render already gets valid constraints.
            var panelGo = new GameObject("ViewPanel", typeof(RectTransform));
            panelGo.transform.SetParent(canvasGo.transform, false);
            var panelRect = (RectTransform)panelGo.transform;
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(width, height);
            panelRect.anchoredPosition = Vector2.zero;
            var panel = panelGo.AddComponent<ViewPanel>();

            UniMobUI.RunApp(lifetime, new StateProvider(), panel, _ => root);

            // Let the Canvas lay out, UniMob's Zone pump run reactions, and TMP build its glyph atlases.
            for (var i = 0; i < settleFrames; i++)
                yield return null;

            Canvas.ForceUpdateCanvases();
            cam.Render();

            var previousActive = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = previousActive;

            var png = tex.EncodeToPNG();
            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllBytes(outputPath, png);
            Debug.Log($"[WidgetCapture] Wrote {outputPath} ({width}x{height}, {png.Length} bytes)");

            cam.targetTexture = null;
            UnityEngine.Object.Destroy(tex);
            UnityEngine.Object.Destroy(canvasGo);
            UnityEngine.Object.Destroy(camGo);
            rt.Release();
            UnityEngine.Object.Destroy(rt);
        }
    }
}
#endif
