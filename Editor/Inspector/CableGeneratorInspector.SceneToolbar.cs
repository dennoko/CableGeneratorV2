using UnityEditor;
using UnityEngine;
using CableGeneratorV2Runtime;

namespace CableGeneratorV2Editor
{
    public partial class CableGeneratorInspector
    {
        // ================================================================
        //  Scene Toolbar Actions — S1 Overlay (CableSceneToolbar) から呼ぶ操作
        //  表示は CableSceneToolbar.uxml / .cs。ここは状態の読み書きだけを持つ
        // ================================================================

        internal static bool IsPicking(CableGenerator gen) => gen != null && s_pickingTarget == gen;
        internal static bool IsDrawing(CableGenerator gen) => gen != null && s_drawTarget == gen;

        internal static void TogglePickingMode(CableGenerator gen)
        {
            if (IsPicking(gen)) CancelPickingMode();
            else                StartPickingMode(gen);
            CableSceneToolState.Raise();
        }

        internal static void ToggleDrawMode(CableGenerator gen)
        {
            if (IsDrawing(gen)) CancelDrawMode();
            else                StartDrawMode(gen);
            CableSceneToolState.Raise();
        }

        internal static void ToggleSurfaceCollision(CableGenerator gen)
        {
            Undo.RecordObject(gen, "Toggle Cable Surface Collision");
            gen.enableSurfaceCollision = !gen.enableSurfaceCollision;
            EditorUtility.SetDirty(gen);
            gen.RequestRefresh();
            AfterToolbarAction();
        }

        internal static void ToggleLod(CableGenerator gen)
        {
            Undo.RecordObject(gen, "Toggle Cable LOD");
            gen.enableLod = !gen.enableLod;
            EditorUtility.SetDirty(gen);
            gen.RequestRefresh();
            AfterToolbarAction();
        }

        internal static bool HasGeneratedMesh(CableGenerator gen)
        {
            Mesh mesh = gen.GeneratedMesh;
            return mesh != null && mesh.vertexCount > 0;
        }

        internal static void BakeFromToolbar(CableGenerator gen)
        {
            BakeCableToSceneAndAssets(gen, EditorPrefs.GetString(kPrefKeyBakeFolder, ""));
            AfterToolbarAction();
        }

        internal static void ExportFbxFromToolbar(CableGenerator gen)
        {
            ExportCableFbx(gen, EditorPrefs.GetString(kPrefKeyBakeFolder, ""), s_fbxLodTier, true, true);
            AfterToolbarAction();
        }

        /// <summary>右端に出すポリゴン数（LOD 有効時は「LOD0→LOD2」）。</summary>
        internal static string TriangleCountText(CableGenerator gen)
        {
            int tri = TriangleCount(gen.GeneratedMesh);
            Mesh lod2 = gen.enableLod ? gen.GeneratedLod2Mesh : null;
            return lod2 != null ? $"{tri}→{TriangleCount(lod2)}△" : $"{tri}△";
        }

        // ---- 選択中の制御点 ----

        /// <summary>選択中の制御点の接線モード（自動 0 / スムーズ 1 / コーナー 2）。混在または未選択なら -1。</summary>
        internal static int SelectedKnotsSimpleMode(CableGenerator gen)
        {
            var selected = GetSelectedKnots(gen.Spline.Count);
            int mode = -1;
            foreach (int i in selected)
            {
                int m = ToSimpleIndex(gen.Spline.GetTangentMode(i));
                if (mode >= 0 && m != mode) return -1;
                mode = m;
            }
            return mode;
        }

        internal static void SetSelectedKnotsMode(CableGenerator gen, int simpleIndex)
        {
            var spline   = gen.Spline;
            var selected = GetSelectedKnots(spline.Count);
            if (selected.Count == 0) return;

            Undo.RecordObject(gen, "接線モードを変更");
            for (int n = 0; n < selected.Count; n++)
                spline.SetTangentMode(selected[n], FromSimpleIndex(simpleIndex), n == selected.Count - 1);
            EditorUtility.SetDirty(gen);
            AfterToolbarAction();
        }

        internal static void SnapSelectedKnots(CableGenerator gen)
        {
            SnapKnotInDirection(gen);
            AfterToolbarAction();
        }

        internal static bool CanAddAttachmentToSelection(CableGenerator gen) =>
            GetSelectedKnots(gen.Spline.Count).Count == 1;

        internal static void AddAttachmentToSelection(CableGenerator gen)
        {
            var selected = GetSelectedKnots(gen.Spline.Count);
            if (selected.Count != 1) return;
            AddAttachment(gen, selected[0]);
            AfterToolbarAction();
        }

        /// <summary>制御点は 2 つ以上残す。</summary>
        internal static bool CanDeleteSelectedKnots(CableGenerator gen)
        {
            int selected = GetSelectedKnots(gen.Spline.Count).Count;
            return selected > 0 && gen.Spline.Count - selected >= 2;
        }

        internal static void DeleteSelectedKnots(CableGenerator gen)
        {
            if (!CanDeleteSelectedKnots(gen)) return;
            var spline   = gen.Spline;
            var selected = GetSelectedKnots(spline.Count);

            Undo.RecordObject(gen, selected.Count > 1 ? "制御点をまとめて削除" : "制御点を削除");
            for (int n = selected.Count - 1; n >= 0; n--)   // 後ろから消して番号をずらさない
                spline.RemoveAt(selected[n]);

            s_snapKnotIndex = Mathf.Clamp(selected[0], 0, Mathf.Max(0, spline.Count - 1));
            s_selectedKnotIndices.Clear();
            EditorUtility.SetDirty(gen);
            OnKnotSelectionChanged();
        }

        static void AfterToolbarAction()
        {
            SceneView.RepaintAll();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            CableSceneToolState.Raise();
        }
    }
}
