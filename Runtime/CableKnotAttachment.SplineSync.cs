using UnityEngine;

namespace CableGeneratorV2Runtime
{
    public partial class CableKnotAttachment
    {
        // ================================================================
        //  Spline Change Tracking
        // ================================================================

        void OnSplineChanged(CableSpline spline, int knotIdx, CableSplineModification modification)
        {
            var gen = Generator;
            if (gen == null || gen.Spline != spline) return;

            HandleSplineModification(spline, knotIdx, modification);
        }

        void OnKnotsRemapped(CableSpline spline, int[] oldToNew)
        {
            if (oldToNew == null || oldToNew.Length == 0) return;
            var gen = Generator;
            if (gen == null || gen.Spline != spline) return;

            int src = Mathf.Clamp(knotIndex, 0, oldToNew.Length - 1);
            int newIndex = Mathf.Clamp(oldToNew[src], 0, Mathf.Max(0, spline.Count - 1));
            SetKnotIndexWithUndo(newIndex);
            // 位置更新は直後の Changed 通知（HandleSplineModification）で行われる
        }

        public void HandleSplineModification(CableSpline spline, int knotIdx, CableSplineModification modification)
        {
            if (knotIdx < 0)
            {
                UpdateAttachment();
                return;
            }

            if (modification == CableSplineModification.KnotInserted)
            {
                if (knotIdx <= knotIndex)
                    SetKnotIndexWithUndo(knotIndex + 1);
            }
            else if (modification == CableSplineModification.KnotRemoved)
            {
                if (knotIdx == knotIndex)
                {
                    int newIndex = Mathf.Max(0, knotIndex - 1);
                    bool conflict = HasOtherAttachmentAt(newIndex);
                    if (knotIndex == 0)
                        conflict |= HasOtherAttachmentAt(1);

                    if (conflict && TryDestroySelf())
                        return;

                    SetKnotIndexWithUndo(newIndex);
                }
                else if (knotIdx < knotIndex)
                {
                    SetKnotIndexWithUndo(knotIndex - 1);
                }
            }

            UpdateAttachment();
        }

        void SetKnotIndexWithUndo(int newIndex)
        {
            if (newIndex == knotIndex) return;
#if UNITY_EDITOR
            if (!Application.isPlaying)
                UnityEditor.Undo.RecordObject(this, "Update Knot Attachment Index");
#endif
            knotIndex = newIndex;
        }

        bool HasOtherAttachmentAt(int index)
        {
            var gen = Generator;
            if (gen == null) return false;

            var siblings = gen.GetComponentsInChildren<CableKnotAttachment>(true);
            for (int i = 0; i < siblings.Length; i++)
            {
                if (siblings[i] != this && siblings[i].knotIndex == index && siblings[i].Generator == gen)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 重複したアタッチメントを削除する。ノット削除操作と同じ Undo グループで即時に削除するため、
        /// Ctrl+Z 1 回でノットとアタッチメントが同時に元に戻る。
        /// </summary>
        bool TryDestroySelf()
        {
            if (!CableObjectUtility.CanDestroy(gameObject)) return false;

            if (Application.isPlaying)
            {
                Destroy(gameObject);
                return true;
            }
#if UNITY_EDITOR
            UnityEditor.Undo.DestroyObjectImmediate(gameObject);
            return true;
#else
            return false;
#endif
        }
    }
}

