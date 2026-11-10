using System;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine.UIElements;
using CableGeneratorV2Runtime;

namespace CableGeneratorV2Editor
{
    /// <summary>
    /// S1 シーンツールバー (カプセル)。CableGenerator を選んでいる間だけ表示する (ITransientOverlay)。
    /// 既定は SceneView の左の列の下端。ユーザーが動かした位置は Unity が覚える。
    /// 状態は持たず、CableSceneToolState.Changed（と Inspector 側の変更を拾う定期更新）で読み直す。
    /// ボタンはアイコンのみ。名前とキーはツールチップ。旧 IMGUI の上部バーと制御点のフローティングバーを統合した。
    /// </summary>
    [Overlay(typeof(SceneView), "CableGeneratorV2.SceneToolbar", "Cable Generator", true,
        defaultDockZone = DockZone.LeftColumn, defaultDockPosition = DockPosition.Bottom, defaultLayout = Layout.Panel)]
    internal sealed class CableSceneToolbar : Overlay, ITransientOverlay
    {
        private const string UXML_GUID = "5602fb84c67a47218a66e1cbea4a2777";   // CableSceneToolbar.uxml
        private const long PollIntervalMs = 300;   // Inspector から変えたトグル・ポリゴン数を拾う間隔

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

            Button B(string name, string icon, string fallback, string tooltip, Action<CableGenerator> click)
            {
                var b = DennokoIcons.Apply(root.Q<Button>(name), icon, fallback, tooltip);
                if (b != null)
                {
                    b.clicked += () =>
                    {
                        var gen = CableSceneToolState.Target;
                        if (gen != null) click(gen);
                    };
                }
                return b;
            }

            // 配線モード
            var pick = B("tool-pick", "d_Linked|Linked", "2", null, CableGeneratorInspector.TogglePickingMode);
            var draw = B("tool-draw", "d_Grid.PaintTool|Grid.PaintTool", "✎", null, CableGeneratorInspector.ToggleDrawMode);

            // 選択中の制御点（文字の選択肢 .dennoko-choice は 3 字まで）
            var modes = new[] { root.Q<Button>("mode-auto"), root.Q<Button>("mode-smooth"), root.Q<Button>("mode-corner") };
            string[] modeTips = { "自動 (接線を自動で滑らかに)", "滑らか (接線を左右対称に手動調整)", "角 (接線を左右別々に手動調整)" };
            for (int i = 0; i < modes.Length; i++)
            {
                int value = i;
                modes[i].tooltip = modeTips[i];
                modes[i].clicked += () =>
                {
                    var gen = CableSceneToolState.Target;
                    if (gen != null) CableGeneratorInspector.SetSelectedKnotsMode(gen, value);
                };
            }
            var snap   = B("knot-snap", "d_SceneViewSnap|SceneViewSnap", "⤓",
                "面投影 (選択中の制御点を下方向のメッシュ表面へ)", CableGeneratorInspector.SnapSelectedKnots);
            var attach = B("knot-attach", "d_FixedJoint Icon|FixedJoint Icon", "+",
                "アタッチメントを追加 (制御点を 1 つ選んでいるとき)", CableGeneratorInspector.AddAttachmentToSelection);
            var remove = B("knot-delete", "d_TreeEditor.Trash|TreeEditor.Trash", "✕",
                "選択中の制御点を削除 (2 点は残す)", CableGeneratorInspector.DeleteSelectedKnots);

            // ケーブル全体のトグル（ON は selection 色の枠。ON/OFF を文字で書かない）
            var collision = B("toggle-collision", "d_MeshCollider Icon|MeshCollider Icon", "床",
                "床判定 (メッシュ表面に沿わせる)", CableGeneratorInspector.ToggleSurfaceCollision);
            var lod = B("toggle-lod", "d_LODGroup Icon|LODGroup Icon", "L",
                "LOD (軽量化メッシュを生成)", CableGeneratorInspector.ToggleLod);

            // 出力
            var bake = B("export-bake", DennokoIcons.Save, "↓",
                "メッシュを保存 (Bake)", CableGeneratorInspector.BakeFromToolbar);
            var fbx  = B("export-fbx", "d_PrefabModel Icon|PrefabModel Icon", "F",
                null, CableGeneratorInspector.ExportFbxFromToolbar);
            var count = root.Q<Label>("count");
            count.tooltip = "ポリゴン数 (LOD 有効時は LOD0→LOD2)";

            var capsule = root.Q("capsule");

            // 状態 → 表示。選択中・ON は selection 色の枠 (.dennoko-button-active)
            void Refresh()
            {
                var gen = CableSceneToolState.Target;
                if (gen == null || gen.Spline == null) return;

                bool picking = CableGeneratorInspector.IsPicking(gen);
                bool drawing = CableGeneratorInspector.IsDrawing(gen);
                bool busy    = picking || drawing;

                pick.EnableInClassList(Active, picking);
                pick.tooltip = picking ? "2点配線を終了  Esc" : "2点配線 (始点と終点の表面をクリック)";
                draw.EnableInClassList(Active, drawing);
                draw.tooltip = drawing ? "なぞり描画を終了  Esc" : "なぞり描画 (表面をドラッグして配線)";

                bool hasSelection = !busy && CableGeneratorInspector.HasKnotSelection;
                int mode = hasSelection ? CableGeneratorInspector.SelectedKnotsSimpleMode(gen) : -1;
                for (int i = 0; i < modes.Length; i++)
                {
                    modes[i].SetEnabled(hasSelection);
                    modes[i].EnableInClassList(Active, mode == i);
                }
                snap.SetEnabled(hasSelection);
                attach.SetEnabled(hasSelection && CableGeneratorInspector.CanAddAttachmentToSelection(gen));
                remove.SetEnabled(!busy && CableGeneratorInspector.CanDeleteSelectedKnots(gen));

                collision.EnableInClassList(Active, gen.enableSurfaceCollision);
                lod.EnableInClassList(Active, gen.enableLod);

                bool hasMesh = CableGeneratorInspector.HasGeneratedMesh(gen);
                bake.SetEnabled(hasMesh);
                fbx.SetEnabled(hasMesh);
                fbx.tooltip = $"ボーン付き FBX を書き出す ({gen.boneCount} 本、今の形状で固定)";

                string tri = CableGeneratorInspector.TriangleCountText(gen);
                if (count.text != tri) count.text = tri;
            }

            DennokoSceneUI.RefreshWhileAttached(capsule, Refresh,
                h => CableSceneToolState.Changed += h, h => CableSceneToolState.Changed -= h);
            capsule.schedule.Execute(Refresh).Every(PollIntervalMs);
        }
    }
}
