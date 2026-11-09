using UnityEngine;

namespace CableGeneratorV2Runtime
{
    /// <summary>
    /// スプラインの制御点（ノット）に任意の Prefab または FBX をアタッチして追従させるコンポーネント。
    /// UnityEngine.Splines 非依存版（V1 CableKnotAttachment 完全互換）。
    /// 親の CableGenerator は参照をキャッシュせず毎回階層から解決する（付け替え・ドメインリロードに強くするため）。
    /// </summary>
    [ExecuteAlways]
    public partial class CableKnotAttachment : MonoBehaviour
    {
        [Tooltip("アタッチ先のノットインデックス")]
        public int knotIndex = 0;

        [Tooltip("配置するPrefabまたはFBXモデル")]
        public GameObject prefab;

        [Tooltip("ノット位置からのオフセット（ローカル）")]
        public Vector3 positionOffset = Vector3.zero;

        [Tooltip("追加回転（オイラー角）")]
        public Vector3 rotationOffset = Vector3.zero;

        [Tooltip("スケール")]
        public Vector3 scale = Vector3.one;

        [SerializeField, HideInInspector]
        GameObject spawnedInstance;
        [SerializeField, HideInInspector]
        GameObject lastPrefab;

        public GameObject SpawnedInstance => spawnedInstance;
        public CableGenerator Generator => GetComponentInParent<CableGenerator>(true);

#if UNITY_EDITOR
        [System.NonSerialized] bool refreshQueued;
#endif

        // ================================================================
        //  Lifecycle
        // ================================================================

        void OnEnable()
        {
            CableSpline.AnySplineChanged += OnSplineChanged;
            CableSpline.AnyKnotsRemapped += OnKnotsRemapped;
            RequestRefresh();
        }

        void OnDisable()
        {
            CableSpline.AnySplineChanged -= OnSplineChanged;
            CableSpline.AnyKnotsRemapped -= OnKnotsRemapped;
        }

        void OnValidate()
        {
            // インスタンス生成やトランスフォーム変更は OnValidate 中に行わず遅延させる
            RequestRefresh();
        }

        bool CanModify()
        {
#if UNITY_EDITOR
            if (UnityEditor.EditorUtility.IsPersistent(this)) return false;
#endif
            return true;
        }

        void RequestRefresh()
        {
            if (!CanModify()) return;
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                if (refreshQueued) return;
                refreshQueued = true;
                UnityEditor.EditorApplication.delayCall += DeferredRefresh;
                return;
            }
#endif
            Refresh();
        }

#if UNITY_EDITOR
        void DeferredRefresh()
        {
            refreshQueued = false;
            if (this == null || !CanModify()) return;
            Refresh();
        }
#endif

        void Refresh()
        {
            if (spawnedInstance == null || prefab != lastPrefab)
                RebuildInstance();
            UpdateAttachment();
        }

        // ================================================================
        //  Instance & Transform
        // ================================================================

        public void RebuildInstance()
        {
            if (prefab == lastPrefab && spawnedInstance != null) return;

            if (spawnedInstance != null)
            {
                if (!CableObjectUtility.CanDestroy(spawnedInstance))
                {
                    Debug.LogWarning($"[CableGenerator V2] '{name}' のアタッチメントは Prefab の一部のため差し替えできません。Prefab を展開するか Prefab モードで編集してください。", this);
                    return;
                }
                CableObjectUtility.Destroy(spawnedInstance);
                spawnedInstance = null;
            }

            lastPrefab = prefab;
            if (prefab != null)
            {
#if UNITY_EDITOR
                var assetType = UnityEditor.PrefabUtility.GetPrefabAssetType(prefab);
                bool isRegularPrefab = assetType == UnityEditor.PrefabAssetType.Regular
                                    || assetType == UnityEditor.PrefabAssetType.Variant;
                if (isRegularPrefab)
                    spawnedInstance = UnityEditor.PrefabUtility.InstantiatePrefab(prefab, transform) as GameObject;
                else
                    spawnedInstance = Instantiate(prefab, transform);
#else
                spawnedInstance = Instantiate(prefab, transform);
#endif
                if (spawnedInstance != null)
                    spawnedInstance.name = prefab.name + " (Attachment)";
            }

#if UNITY_EDITOR
            // SerializedObject を経由しない変更なので、保存対象として明示する
            if (!Application.isPlaying)
                UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        public void UpdateAttachment()
        {
            var gen = Generator;
            if (gen == null || gen.Spline == null) return;

            var spline = gen.Spline;
            if (spline.Count == 0) return;

            int safeIndex = Mathf.Clamp(knotIndex, 0, spline.Count - 1);
            var knot = spline[safeIndex];

            Transform splineTransform = gen.transform;
            Vector3 worldPos = splineTransform.TransformPoint(knot.position);
            Quaternion worldRot = CalculateKnotRotation(spline, safeIndex, splineTransform);

            Quaternion finalRot = worldRot * Quaternion.Euler(rotationOffset);
            Vector3 finalPos = worldPos + finalRot * positionOffset;

            // 変化がなければ書き込まない（Transform 変更通知・再描画を抑える）
            if ((transform.position - finalPos).sqrMagnitude > 1e-12f)
                transform.position = finalPos;
            if (Quaternion.Angle(transform.rotation, finalRot) > 1e-4f)
                transform.rotation = finalRot;
            if (transform.localScale != scale)
                transform.localScale = scale;
        }

        public static Quaternion CalculateKnotRotation(CableSpline spline, int index, Transform splineTransform)
        {
            int count = spline.Count;
            if (count == 0) return splineTransform.rotation;

            var knot = spline[index];
            Vector3 tangentLocal;

            if (knot.mode == CableTangentMode.AutoSmooth && count >= 2)
            {
                spline.ComputeAutoWorldTangents(index, out Vector3 autoIn, out Vector3 autoOut);
                tangentLocal = (index < count - 1) ? autoOut : -autoIn;
            }
            else
            {
                if (index < count - 1)
                    tangentLocal = knot.rotation * knot.tangentOut;
                else
                    tangentLocal = -(knot.rotation * knot.tangentIn);
            }

            Vector3 forward = splineTransform.TransformDirection(tangentLocal.normalized);
            if (forward.sqrMagnitude < 0.0001f)
                forward = splineTransform.forward;

            // ノット上の Up はノット回転そのもの（曲線評価は不要。閉じたスプラインでも t の換算ずれが起きない）
            Vector3 up = splineTransform.TransformDirection(knot.rotation * Vector3.up);
            if (up.sqrMagnitude < 0.0001f)
                up = Vector3.up;

            return CableSpline.SafeLookRotation(forward, up);
        }
    }
}
