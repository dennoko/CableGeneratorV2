using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CableGeneratorV2Editor
{
    /// <summary>
    /// シーン操作中の案内（dennokoworks デザイン S4「カーソルヒント」）。
    /// 2点配線・なぞり描画の間、カーソルの右下に次の操作を 1 行だけ出す。
    ///
    /// 各 SceneView の rootVisualElement に透明な全面のルート (.dennoko-hud、pickingMode = Ignore) を重ね、
    /// その上に .dennoko-cursor-hint の Label を 1 つ置く。見た目は DennokoSceneUI.SetupRoot（テーマ → ブロック → シーンの USS）。
    ///
    ///   Show(text) : モード中の OnSceneGUI から毎イベント呼ぶ（カーソル移動で追従、文字は変わったときだけ更新）
    ///   Hide()     : モード終了時に呼ぶ
    /// </summary>
    [InitializeOnLoad]
    internal static class CableSceneGuide
    {
        private const string RootName = "CableGeneratorV2-SceneGuide";

        private static readonly Vector2 CursorOffset = new Vector2(14f, 16f);   // カーソルの右下
        private const float EdgeMargin = 4f;

        private sealed class Hud
        {
            public VisualElement Root;
            public Label Hint;
            public bool Visible;
            public Vector2 Position = new Vector2(float.NaN, float.NaN);
        }

        private static readonly Dictionary<SceneView, Hud> Huds = new Dictionary<SceneView, Hud>();

        static CableSceneGuide()
        {
            // ドメインリロード前に出していた HUD が SceneView に残っていたら外す
            EditorApplication.delayCall += RemoveStale;
        }

        // ─── 公開 API ───────────────────────────────────────────────────

        public static void Show(string text)
        {
            var view = SceneView.currentDrawingSceneView;
            var e = Event.current;
            if (view == null || e == null) return;

            var hud = EnsureHud(view);
            if (hud == null) return;

            if (hud.Hint.text != text) hud.Hint.text = text;

            switch (e.type)
            {
                case EventType.MouseMove:
                case EventType.MouseDrag:
                case EventType.MouseDown:
                case EventType.MouseUp:
                case EventType.MouseEnterWindow:
                    // カーソルがあるビューにだけ出す
                    foreach (var kv in Huds)
                        if (kv.Value != hud) SetVisible(kv.Value, false);
                    Place(view, hud, e.mousePosition);
                    SetVisible(hud, true);
                    break;

                case EventType.MouseLeaveWindow:
                    SetVisible(hud, false);
                    break;

                case EventType.Repaint:
                    // MouseLeaveWindow を取りこぼしたときの保険
                    if (hud.Visible && EditorWindow.mouseOverWindow != view) SetVisible(hud, false);
                    break;
            }
        }

        public static void Hide()
        {
            foreach (var hud in Huds.Values) SetVisible(hud, false);
        }

        // ─── 内部 ───────────────────────────────────────────────────────

        private static Hud EnsureHud(SceneView view)
        {
            if (Huds.TryGetValue(view, out var existing) && existing.Root.panel != null) return existing;

            var parent = view.rootVisualElement;
            parent.Q(RootName)?.RemoveFromHierarchy();   // 二重にしない

            var hud = new Hud
            {
                Root = new VisualElement { name = RootName, pickingMode = PickingMode.Ignore },
                Hint = new Label { pickingMode = PickingMode.Ignore },
            };

            DennokoSceneUI.SetupRoot(hud.Root);   // テーマ → ブロック → シーンの USS とフォント
            hud.Root.AddToClassList("dennoko-hud");

            hud.Hint.AddToClassList("dennoko-cursor-hint");
            hud.Root.Add(hud.Hint);
            parent.Add(hud.Root);

            SetVisible(hud, false, force: true);
            Huds[view] = hud;
            return hud;
        }

        /// <summary>カーソル右下に置く。SceneView の端からはみ出すときはカーソルの反対側へ回す。</summary>
        private static void Place(SceneView view, Hud hud, Vector2 gui)
        {
            Vector2 cursor = ToRoot(view, gui);
            Vector2 p = cursor + CursorOffset;

            float w = hud.Hint.layout.width;
            float h = hud.Hint.layout.height;
            float rootW = hud.Root.layout.width;
            float rootH = hud.Root.layout.height;
            if (!float.IsNaN(w) && !float.IsNaN(rootW) && p.x + w > rootW - EdgeMargin)
                p.x = cursor.x - CursorOffset.x - w;
            if (!float.IsNaN(h) && !float.IsNaN(rootH) && p.y + h > rootH - EdgeMargin)
                p.y = cursor.y - CursorOffset.y - h;

            // 同じ値の再代入でレイアウトを汚さない（Repaint の連鎖を防ぐ）
            if (p == hud.Position) return;
            hud.Position = p;
            hud.Hint.style.left = p.x;
            hud.Hint.style.top = p.y;
        }

        /// <summary>
        /// SceneView の GUI 座標 → rootVisualElement の座標。
        /// rootVisualElement はシーン上部のツールバーの分だけ背が高いので、その差だけ下へずらす。
        /// </summary>
        private static Vector2 ToRoot(SceneView view, Vector2 gui)
        {
            float camHeight = view.camera != null
                ? view.camera.pixelHeight / EditorGUIUtility.pixelsPerPoint
                : view.position.height;
            float offset = Mathf.Max(0f, view.rootVisualElement.layout.height - camHeight);
            return gui + new Vector2(0f, offset);
        }

        private static void SetVisible(Hud hud, bool visible, bool force = false)
        {
            if (!force && hud.Visible == visible) return;
            hud.Visible = visible;
            hud.Hint.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static void RemoveStale()
        {
            foreach (SceneView view in SceneView.sceneViews)
            {
                if (view == null || (Huds.TryGetValue(view, out var hud) && hud.Root.panel != null)) continue;
                view.rootVisualElement.Q(RootName)?.RemoveFromHierarchy();
            }
        }
    }
}
