using UnityEngine;

namespace CableGeneratorV2Runtime
{
    /// <summary>
    /// 生成物（メッシュ・子オブジェクト・コンポーネント）の破棄を、
    /// 再生中／エディタ／Prefab インスタンスの状況に応じて安全に行うためのユーティリティ。
    /// </summary>
    public static class CableObjectUtility
    {
        /// <summary>
        /// 一時生成物（シーン・Prefab に保存しない）に付与する HideFlags。
        /// DontUnloadUnusedAsset を含めないことで、万一参照を失っても UnloadUnusedAssets で回収される。
        /// </summary>
        public const HideFlags TransientAssetFlags  = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        public const HideFlags TransientObjectFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild | HideFlags.NotEditable;

        public static bool IsTransient(Object obj)
            => obj != null && (obj.hideFlags & HideFlags.DontSaveInEditor) != 0;

        /// <summary>
        /// Prefab インスタンスの一部（Prefab アセット由来）のオブジェクトはスクリプトから破棄できないため事前に判定する。
        /// </summary>
        public static bool CanDestroy(Object obj)
        {
            if (obj == null) return false;
#if UNITY_EDITOR
            if (Application.isPlaying) return true;
            if (UnityEditor.EditorUtility.IsPersistent(obj)) return false;

            if (obj is Component component)
            {
                if (!UnityEditor.PrefabUtility.IsPartOfPrefabInstance(component)) return true;
                return UnityEditor.PrefabUtility.IsAddedComponentOverride(component);
            }

            if (obj is GameObject go)
            {
                if (!UnityEditor.PrefabUtility.IsPartOfPrefabInstance(go)) return true;
                // 自身が最外の Prefab インスタンスのルートなら丸ごと破棄できる
                if (UnityEditor.PrefabUtility.GetOutermostPrefabInstanceRoot(go) == go) return true;
                return UnityEditor.PrefabUtility.IsAddedGameObjectOverride(go);
            }
#endif
            return true;
        }

        public static void Destroy(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying)
                Object.Destroy(obj);
            else
                Object.DestroyImmediate(obj);
        }
    }
}
