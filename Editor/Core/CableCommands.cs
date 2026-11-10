using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CableGeneratorV2Runtime;
using static CableGeneratorV2Editor.CableL10n;

namespace CableGeneratorV2Editor
{
    /// <summary>操作の結果。Message は画面に出す文言（翻訳済み）。</summary>
    internal readonly struct CableResult
    {
        public readonly bool Ok;
        public readonly string Message;

        public CableResult(bool ok, string message)
        {
            Ok = ok;
            Message = message ?? string.Empty;
        }

        public static CableResult Success(string message) => new CableResult(true, message);
        public static CableResult Fail(string message) => new CableResult(false, message);
    }

    /// <summary>
    /// ケーブルへの操作（設計書 §8）。ウィンドウ・Inspector・S1 ツールバー・シーンはどれもここを呼ぶ。
    /// Undo に載せ、結果の文言を CableResult で返す。表示（ステータス / トースト）は呼び出し側が決める。
    /// </summary>
    internal static partial class CableCommands
    {
        internal const float kVectorEpsilonSqr = 1e-12f;
        internal const float kLinearTangentDivisor = 3f;

        // ─── 情報 ───────────────────────────────────────────────────────

        /// <summary>三角形数（mesh.triangles は配列を確保するため使わない）。</summary>
        public static int TriangleCount(Mesh mesh)
        {
            if (mesh == null || mesh.subMeshCount == 0) return 0;
            return (int)(mesh.GetIndexCount(0) / 3);
        }

        public static bool HasMesh(CableGenerator gen)
        {
            Mesh mesh = gen != null ? gen.GeneratedMesh : null;
            return mesh != null && mesh.vertexCount > 0;
        }

        /// <summary>ポリゴン数の表示（LOD 有効時は「LOD0→LOD2」）。</summary>
        public static string TriangleText(CableGenerator gen)
        {
            if (gen == null) return string.Empty;
            int tri = TriangleCount(gen.GeneratedMesh);
            Mesh lod2 = gen.enableLod ? gen.GeneratedLod2Mesh : null;
            return lod2 != null ? $"{tri:N0}→{TriangleCount(lod2):N0}△" : $"{tri:N0}△";
        }

        public static float Length(CableGenerator gen)
        {
            if (gen == null || gen.Spline == null) return 0f;
            var tf = gen.transform;
            return gen.Spline.CalculateLength(tf.localToWorldMatrix, tf.InverseTransformVector(Vector3.down));
        }

        /// <summary>フッター・Inspector の情報行（制御点 6 · 全長 2.40 m · 1,240△）。</summary>
        public static string SummaryText(CableGenerator gen)
        {
            if (gen == null || gen.Spline == null) return string.Empty;
            return Tr("info.summary", gen.Spline.Count, Length(gen).ToString("F2"), TriangleText(gen));
        }

        // ─── 接線モード ─────────────────────────────────────────────────

        public static int ToSimpleIndex(CableTangentMode mode) => mode switch
        {
            CableTangentMode.Mirrored   => 1,
            CableTangentMode.Continuous => 1,
            CableTangentMode.Broken     => 2,
            _                           => 0,
        };

        public static CableTangentMode FromSimpleIndex(int idx) => idx switch
        {
            1 => CableTangentMode.Mirrored,
            2 => CableTangentMode.Broken,
            _ => CableTangentMode.AutoSmooth,
        };

        public static void SetTangentMode(CableGenerator gen, IList<int> indices, int simpleIndex)
        {
            if (gen == null || indices == null || indices.Count == 0) return;
            Undo.RecordObject(gen, Tr("undo.tangentMode"));
            for (int n = 0; n < indices.Count; n++)
                gen.Spline.SetTangentMode(indices[n], FromSimpleIndex(simpleIndex), n == indices.Count - 1);
            EditorUtility.SetDirty(gen);
        }

        /// <summary>indices の接線モード。混在または空なら -1。</summary>
        public static int CommonSimpleMode(CableGenerator gen, IList<int> indices)
        {
            int mode = -1;
            foreach (int i in indices)
            {
                int m = ToSimpleIndex(gen.Spline.GetTangentMode(i));
                if (mode >= 0 && m != mode) return -1;
                mode = m;
            }
            return mode;
        }

        // ─── 制御点の追加・削除 ─────────────────────────────────────────

        public static void AddKnot(CableGenerator gen) => SplineKnotOperations.AddKnotAfterSelected(gen);
        public static void InsertKnotAfter(CableGenerator gen, int index) => SplineKnotOperations.InsertKnotAfter(gen, index);
        public static void Flip(CableGenerator gen) => SplineKnotOperations.FlipSplineDirection(gen);
        public static void RedistributeEvenly(CableGenerator gen) => SplineKnotOperations.RedistributeKnotsEvenly(gen);

        public static CableResult Subdivide(CableGenerator gen, CableTangentMode mode)
        {
            bool ok = SplineKnotOperations.SubdivideSplineKnots(gen, mode);
            return new CableResult(ok, SplineKnotOperations.LastMessage);
        }

        public static CableResult DivideEndpoints(CableGenerator gen, int divisions, CableTangentMode mode)
        {
            bool ok = SplineKnotOperations.RedistributeKnotsBetweenEndpoints(gen, divisions, mode);
            return new CableResult(ok, SplineKnotOperations.LastMessage);
        }

        /// <summary>制御点は 2 つ以上残す。</summary>
        public static bool CanDelete(CableGenerator gen, int deleteCount) =>
            gen != null && deleteCount > 0 && gen.Spline.Count - deleteCount >= 2;

        public static CableResult DeleteKnots(CableGenerator gen, IList<int> indices)
        {
            if (indices == null || !CanDelete(gen, indices.Count)) return CableResult.Fail(Tr("msg.keepTwoKnots"));
            var sorted = new List<int>(indices);
            sorted.Sort();
            var spline = gen.Spline;

            Undo.RecordObject(gen, Tr(sorted.Count > 1 ? "undo.deleteKnots" : "undo.deleteKnot"));
            for (int n = sorted.Count - 1; n >= 0; n--)   // 後ろから消して番号をずらさない
                spline.RemoveAt(sorted[n]);
            EditorUtility.SetDirty(gen);

            CableKnotSelection.Clear();
            return CableResult.Success(Tr("msg.deletedKnots", sorted.Count));
        }

        public static void SetClosed(CableGenerator gen, bool closed)
        {
            if (gen == null || gen.Spline.Closed == closed) return;
            Undo.RecordObject(gen, Tr("undo.loop"));
            gen.Spline.Closed = closed;
            EditorUtility.SetDirty(gen);
        }

        // ─── ケーブル全体のトグル ───────────────────────────────────────

        public static void ToggleSurfaceCollision(CableGenerator gen)
        {
            Undo.RecordObject(gen, Tr("undo.collision"));
            gen.enableSurfaceCollision = !gen.enableSurfaceCollision;
            EditorUtility.SetDirty(gen);
            gen.RequestRefresh();
        }

        public static void ToggleLod(CableGenerator gen)
        {
            Undo.RecordObject(gen, Tr("undo.lod"));
            gen.enableLod = !gen.enableLod;
            EditorUtility.SetDirty(gen);
            gen.RequestRefresh();
        }

        // ─── アタッチメント ─────────────────────────────────────────────

        public static CableKnotAttachment AddAttachment(CableGenerator generator, int knotIndex)
        {
            var child = new GameObject($"Attachment_Knot{knotIndex}");
            Undo.RegisterCreatedObjectUndo(child, Tr("undo.addAttachment"));
            child.transform.SetParent(generator.transform, false);
            child.layer = generator.gameObject.layer;
            var attachment = child.AddComponent<CableKnotAttachment>();
            attachment.knotIndex = knotIndex;
            attachment.UpdateAttachment();
            Selection.activeGameObject = child;
            return attachment;
        }

        /// <summary>このケーブルのアタッチメントのうち、knotIndex に付いているもの。</summary>
        public static List<CableKnotAttachment> AttachmentsOf(CableGenerator gen, int knotIndex)
        {
            var list = new List<CableKnotAttachment>();
            if (gen == null) return list;
            foreach (var a in gen.GetComponentsInChildren<CableKnotAttachment>(true))
                if (a.knotIndex == knotIndex) list.Add(a);
            return list;
        }

        // ─── 旧バージョンのボーン ───────────────────────────────────────

        public static bool HasLegacyBones(CableGenerator gen) =>
            gen != null && gen.transform.Find(CableGenerator.kLegacyBonesRootName) != null;

        public static CableResult RemoveLegacyBones(CableGenerator gen)
        {
            Transform legacy = gen != null ? gen.transform.Find(CableGenerator.kLegacyBonesRootName) : null;
            if (legacy == null) return CableResult.Success(string.Empty);
            if (!CableObjectUtility.CanDestroy(legacy.gameObject)) return CableResult.Fail(Tr("msg.legacyBonesPrefab"));

            Undo.DestroyObjectImmediate(legacy.gameObject);
            var smr = gen.GetComponent<SkinnedMeshRenderer>();
            if (smr != null && CableObjectUtility.CanDestroy(smr)) Undo.DestroyObjectImmediate(smr);
            var mr = gen.GetComponent<MeshRenderer>();
            if (mr != null && !mr.enabled)
            {
                Undo.RecordObject(mr, Tr("undo.enableRenderer"));
                mr.enabled = true;
            }
            return CableResult.Success(Tr("msg.legacyBonesRemoved"));
        }

        // ─── 接線の長さ（2点配線のハンドルの長さ） ──────────────────────

        /// <summary>手動モードの制御点の接線を、隣との距離 / 3 × scale にそろえる。</summary>
        public static void ApplyTangentScale(CableGenerator cableGen, float scale)
        {
            if (cableGen == null || cableGen.Spline == null) return;
            var spline = cableGen.Spline;
            int count = spline.Count;
            if (count == 0) return;

            bool closed = spline.Closed;
            var positions = new Vector3[count];
            for (int i = 0; i < count; i++) positions[i] = spline[i].position;

            Undo.RecordObject(cableGen, Tr("undo.tangentScale"));
            for (int i = 0; i < count; i++)
            {
                if (spline.GetTangentMode(i) == CableTangentMode.AutoSmooth) continue;

                float dPrev = 0f, dNext = 0f, avg;
                if (closed)
                {
                    dPrev = Vector3.Distance(positions[(i - 1 + count) % count], positions[i]);
                    dNext = Vector3.Distance(positions[i], positions[(i + 1) % count]);
                    avg = (dPrev + dNext) * 0.5f;
                }
                else
                {
                    if (i > 0)         dPrev = Vector3.Distance(positions[i - 1], positions[i]);
                    if (i < count - 1) dNext = Vector3.Distance(positions[i], positions[i + 1]);
                    avg = (i > 0 && i < count - 1) ? (dPrev + dNext) * 0.5f : (dPrev > 0f ? dPrev : dNext);
                }

                float len = avg / kLinearTangentDivisor * scale;
                var k = spline[i];
                Vector3 dirIn  = k.tangentIn.sqrMagnitude  > 1e-6f ? k.tangentIn.normalized  : Vector3.back;
                Vector3 dirOut = k.tangentOut.sqrMagnitude > 1e-6f ? k.tangentOut.normalized : Vector3.forward;
                k.tangentIn  = dirIn * len;
                k.tangentOut = dirOut * len;
                spline.SetKnot(i, k, false);
            }
            spline.NotifyChanged(-1, CableSplineModification.KnotModified);
            EditorUtility.SetDirty(cableGen);
        }

        // ─── 表示の更新 ─────────────────────────────────────────────────

        /// <summary>Inspector・ウィンドウ・シーンを描き直す（操作の後に 1 回だけ呼ぶ。ドラッグ中は呼ばない）。</summary>
        public static void RepaintAll()
        {
            SceneView.RepaintAll();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            CableSceneToolState.Raise();
        }
    }
}
