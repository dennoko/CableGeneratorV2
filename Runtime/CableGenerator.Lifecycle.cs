using UnityEngine;

namespace CableGeneratorV2Runtime
{
    public partial class CableGenerator
    {
        // ================================================================
        //  Lifecycle
        // ================================================================

        void OnEnable()
        {
            if (spline == null)
                spline = new CableSpline();

            CableSpline.AnySplineChanged     += OnAnySplineChanged;
            CableProfile.AnyProfileChanged   += OnAnyProfileChanged;

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                // 構造変更を伴うので OnEnable 中には行わず次のエディタ更新でまとめて実行
                QueueRefresh();
                return;
            }
#endif
            Refresh();
        }

        void OnDisable()
        {
            CableSpline.AnySplineChanged   -= OnAnySplineChanged;
            CableProfile.AnyProfileChanged -= OnAnyProfileChanged;
        }

        void OnDestroy()
        {
            DestroyOwnedMesh(GetComponent<MeshFilter>(), 0);
            DestroyOwnedMesh(FindLodFilter(1), 1);
            DestroyOwnedMesh(FindLodFilter(2), 2);

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                // コンポーネントだけが外された場合は、残った一時 LOD 階層と LODGroup を片付ける
                // （GameObject ごと破棄される場合は tf が null になるので何もしない）
                Transform tf = transform;
                UnityEditor.EditorApplication.delayCall += () =>
                {
                    if (tf == null || tf.GetComponent<CableGenerator>() != null) return;
                    RemoveLodStructure(tf);
                };
            }
#endif
        }

        void OnValidate()
        {
            if (spline == null)
                spline = new CableSpline();

#if UNITY_EDITOR
            if (!CanGenerate()) return;
            QueueRefresh();
#endif
        }

        void OnAnySplineChanged(CableSpline changedSpline, int knotIndex, CableSplineModification modification)
        {
            if (changedSpline != spline || !CanGenerate()) return;
            BuildMeshes();
        }

        void OnAnyProfileChanged(CableProfile changedProfile)
        {
            if (changedProfile != profile || this == null) return;
#if UNITY_EDITOR
            // プロファイルの OnValidate 中に構造変更しないよう遅延
            QueueRefresh();
#else
            Refresh();
#endif
        }

        /// <summary>
        /// 生成対象として扱えるか（Prefab アセットやインポート中のアセットには生成物を作らない）。
        /// </summary>
        bool CanGenerate()
        {
#if UNITY_EDITOR
            if (UnityEditor.EditorUtility.IsPersistent(this)) return false;
#endif
            return true;
        }

        // ================================================================
        //  Editor Delayed Execution Queues
        // ================================================================

#if UNITY_EDITOR
        void QueueRefresh()
        {
            if (refreshQueued) return;
            refreshQueued = true;
            UnityEditor.EditorApplication.delayCall += DeferredRefresh;
        }

        void DeferredRefresh()
        {
            refreshQueued = false;
            if (this == null || !isActiveAndEnabled || !CanGenerate()) return;
            Refresh();
        }

        void QueueLodPass()
        {
            if (lodPassQueued) return;
            lodPassQueued = true;
            UnityEditor.EditorApplication.delayCall += DeferredLodPass;
        }

        void DeferredLodPass()
        {
            lodPassQueued = false;
            if (this == null || !isActiveAndEnabled || !CanGenerate()) return;

            // ハンドル／スライダーのドラッグ中は遠景用 LOD の更新を見送り、離した時点で 1 回だけ作る
            if (GUIUtility.hotControl != 0)
            {
                QueueLodPass();
                return;
            }
            BuildLodTiers();
        }
#endif
    }
}
