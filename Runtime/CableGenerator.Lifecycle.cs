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
            // ここではメッシュを破棄せず、MeshFilter の参照も外さない。
            // コンポーネントだけが外された場合（手動での削除・ビルド時のスクリプト除去など）に、
            // シーンに保存されたケーブルの見た目が消えないようにするため。
            var mf0 = GetComponent<MeshFilter>();
            var mf1 = FindLodFilter(1);
            var mf2 = FindLodFilter(2);
            Mesh m0 = FindOwnedMesh(mf0, 0, claim: false);
            Mesh m1 = FindOwnedMesh(mf1, 1, claim: false);
            Mesh m2 = FindOwnedMesh(mf2, 2, claim: false);
            ReleaseMeshOwnership(m0);
            ReleaseMeshOwnership(m1);
            ReleaseMeshOwnership(m2);

#if UNITY_EDITOR
            // GameObject ごと破棄されたかどうかは OnDestroy の時点では判別できないため、次のエディタ更新で確認する。
            // （ランタイムでは何もしない。参照を失ったメッシュはシーンのアンロード時に回収される）
            Transform tf = transform;
            UnityEditor.EditorApplication.delayCall += () =>
            {
                // MeshFilter ごと消えていれば、メッシュはどこからも参照されないので解放する
                DestroyIfOrphaned(mf0, m0);
                DestroyIfOrphaned(mf1, m1);
                DestroyIfOrphaned(mf2, m2);

                // コンポーネントだけが外された場合は、残った LOD 階層を手で編集できるようにする
                if (tf == null || tf.GetComponent<CableGenerator>() != null) return;
                UnlockLodStructure(tf);
            };
#endif
        }

#if UNITY_EDITOR
        static void DestroyIfOrphaned(MeshFilter mf, Mesh m)
        {
            if (m == null || mf != null) return;
            CableObjectUtility.Destroy(m);
        }
#endif

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

