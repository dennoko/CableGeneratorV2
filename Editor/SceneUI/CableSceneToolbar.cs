using System;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine.UIElements;
using CableGeneratorV2Runtime;
using static CableGeneratorV2Editor.CableL10n;

namespace CableGeneratorV2Editor
{
    /// <summary>
    /// S1 シーンツールバー (カプセル)。ケーブルを選んでいる間・作業中だけ表示する (ITransientOverlay)。
    /// ウィンドウを閉じていても出す（設計書 §14 決定事項）。既定は SceneView の左の列の下端。
    /// 状態は持たず、CableSceneToolState.Changed（と Inspector / ウィンドウ側の変更を拾う定期更新）で読み直す。
    /// </summary>
    [Overlay(typeof(SceneView), "CableGeneratorV2.SceneToolbar", "Cable Generator", true,
        defaultDockZone = DockZone.LeftColumn, defaultDockPosition = DockPosition.Bottom, defaultLayout = Layout.Panel)]
    internal sealed class CableSceneToolbar : Overlay, ITransientOverlay
    {
        private const string UXML_GUID = "5602fb84c67a47218a66e1cbea4a2777";   // CableSceneToolbar.uxml
        private const long PollIntervalMs = 300;   // ウィンドウ / Inspector から変えたトグル・ポリゴン数を拾う間隔

        public bool visible => CableSceneToolState.Active;

        // ドック位置の変更や SceneView の作り直しのたびに呼ばれるので、毎回作り直せる形にする
        public override VisualElement CreatePanelContent()
        {
            if (DennokoSceneUI.CreateOverlayRoot(UXML_GUID, out var root)) InitializeUI(root);
            return root;
        }

        private static void InitializeUI(VisualElement root)
        {
            const string Active = DennokoSceneUI.ActiveClass;

            Button B(string name, string icon, string fallback, Action<CableGenerator> click)
            {
                var b = DennokoIcons.Apply(root.Q<Button>(name), icon, fallback);
                if (b != null)
                {
                    b.clicked += () =>
                    {
                        var gen = CableSceneToolState.Current;
                        if (gen == null) return;
                        click(gen);
                        CableCommands.RepaintAll();
                    };
                }
                return b;
            }

            static void Report(CableResult r) => CableSceneGuide.Toast(r.Message, r.Ok);

            var pick = B("tool-pick", "d_Linked|Linked", "2", CableSceneHandles.TogglePicking);
            var draw = B("tool-draw", "d_Grid.PaintTool|Grid.PaintTool", "✎", CableSceneHandles.ToggleDrawing);

            var modes = new[] { root.Q<Button>("mode-auto"), root.Q<Button>("mode-smooth"), root.Q<Button>("mode-corner") };
            for (int i = 0; i < modes.Length; i++)
            {
                int value = i;
                modes[i].clicked += () =>
                {
                    var gen = CableSceneToolState.Current;
                    if (gen == null) return;
                    CableCommands.SetTangentMode(gen, CableKnotSelection.Sorted(gen.Spline.Count), value);
                    CableCommands.RepaintAll();
                };
            }

            var snap = B("knot-snap", "d_SceneViewSnap|SceneViewSnap", "⤓",
                gen => Report(CableCommands.SnapKnots(gen, CableKnotSelection.Sorted(gen.Spline.Count))));
            var attach = B("knot-attach", "d_FixedJoint Icon|FixedJoint Icon", "+",
                gen =>
                {
                    // 付けたらモデルの指定と調整はウィンドウの制御点タブで続ける
                    CableCommands.AddAttachment(gen, CableKnotSelection.Active);
                    CableGeneratorWindow.Open(gen, CableGeneratorWindow.TabShape);
                });
            var remove = B("knot-delete", "d_TreeEditor.Trash|TreeEditor.Trash", "✕",
                gen => Report(CableCommands.DeleteKnots(gen, CableKnotSelection.Sorted(gen.Spline.Count))));

            var collision = B("toggle-collision", "d_MeshCollider Icon|MeshCollider Icon", "床", CableCommands.ToggleSurfaceCollision);
            var lod = B("toggle-lod", "d_LODGroup Icon|LODGroup Icon", "L", CableCommands.ToggleLod);
            var open = B("open-window", "d_winbtn_win_max|winbtn_win_max", "⧉", gen => CableGeneratorWindow.Open(gen));

            var count = root.Q<Label>("count");
            var capsule = root.Q("capsule");
            bool english = !CableL10n.English;   // 初回に必ず文言を入れる

            // 状態 → 表示。選択中・ON は selection 色の枠 (.dennoko-button-active)
            void Refresh()
            {
                if (english != CableL10n.English)
                {
                    english = CableL10n.English;
                    CableL10n.Localize(root);
                    snap.tooltip = Tr("s1.snap.tip");
                    attach.tooltip = Tr("s1.attach.tip");
                    remove.tooltip = Tr("s1.delete.tip");
                    collision.tooltip = Tr("s1.collision.tip");
                    lod.tooltip = Tr("s1.lod.tip");
                    open.tooltip = Tr("s1.open.tip");
                    count.tooltip = Tr("s1.count.tip");
                }

                var gen = CableSceneToolState.Current;
                if (gen == null || gen.Spline == null) return;

                bool picking = CableSceneHandles.IsPicking(gen);
                bool drawing = CableSceneHandles.IsDrawing(gen);
                bool busy = picking || drawing;

                pick.EnableInClassList(Active, picking);
                pick.tooltip = Tr(picking ? "s1.pick.end.tip" : "s1.pick.tip");
                draw.EnableInClassList(Active, drawing);
                draw.tooltip = Tr(drawing ? "s1.draw.end.tip" : "s1.draw.tip");

                var selected = CableKnotSelection.Owner == gen ? CableKnotSelection.Sorted(gen.Spline.Count) : new System.Collections.Generic.List<int>();
                bool hasSelection = !busy && selected.Count > 0;
                int mode = hasSelection ? CableCommands.CommonSimpleMode(gen, selected) : -1;
                for (int i = 0; i < modes.Length; i++)
                {
                    modes[i].SetEnabled(hasSelection);
                    modes[i].EnableInClassList(Active, mode == i);
                }
                snap.SetEnabled(hasSelection);
                attach.SetEnabled(hasSelection && selected.Count == 1);
                remove.SetEnabled(!busy && CableCommands.CanDelete(gen, selected.Count));

                collision.EnableInClassList(Active, gen.enableSurfaceCollision);
                lod.EnableInClassList(Active, gen.enableLod);

                string tri = CableCommands.TriangleText(gen);
                if (count.text != tri) count.text = tri;
            }

            DennokoSceneUI.RefreshWhileAttached(capsule, Refresh,
                h => CableSceneToolState.Changed += h, h => CableSceneToolState.Changed -= h);
            capsule.schedule.Execute(Refresh).Every(PollIntervalMs);
        }
    }
}
