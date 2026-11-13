using UnityEngine;

namespace CableGeneratorV2Runtime
{
    /// <summary>
    /// 生成物（メッシュ・子オブジェクト・コンポーネント）の保存フラグ管理と、
    /// 再生中／エディタ／Prefab インスタンスの状況に応じた安全な破棄を行うためのユーティリティ。
    /// </summary>
    public static class CableObjectUtility
    {
        /// <summary>
        /// 生成メッシュに付与する HideFlags。DontSave 系を付けないことで、MeshFilter から参照されたメッシュが
        /// シーンに直接シリアライズされ、ワールドのビルドにもそのまま含まれる（ベイク不要）。
        /// </summary>
        public const HideFlags GeneratedAssetFlags = HideFlags.None;

        /// <summary>
        /// 生成した子オブジェクト（LOD 階層）に付与する HideFlags。シーン・ビルドには保存するが、手編集はさせない。
        /// </summary>
        public const HideFlags GeneratedObjectFlags = HideFlags.NotEditable;

        const HideFlags kDontSaveMask = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;

        /// <summary>シーンとビルドの両方に保存されるオブジェクトか（DontSave 系フラグが立っていないか）。</summary>
        public static bool IsSavedToSceneAndBuild(Object obj)
            => obj != null && (obj.hideFlags & kDontSaveMask) == 0;

        /// <summary>
        /// HideFlags を指定値に揃える。旧バージョンが「保存しない」フラグ付きで作った生成物を保存対象へ移行するのにも使う。
        /// </summary>
        /// <returns>値を変更したら true</returns>
        public static bool SetHideFlags(Object obj, HideFlags flags)
        {
            if (obj == null || obj.hideFlags == flags) return false;
            obj.hideFlags = flags;
            return true;
        }

        /// <summary>
        /// Undo を経由せずスクリプトから書き換えた生成物（メッシュ参照・LOD 階層）を、シーン保存の対象にする。
        /// これを呼ばないと「開いただけで再生成されたメッシュ」が保存されず、古い内容のままビルドされてしまう。
        /// 再生中・ビルド中・ランタイムでは何もしない。
        /// </summary>
        public static void MarkSceneDataChanged(Component component)
        {
#if UNITY_EDITOR
            if (component == null) return;
            if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (UnityEditor.BuildPipeline.isBuildingPlayer) return;

            // Prefab インスタンスは、オーバーライドとして記録しないと保存時に変更が捨てられる
            if (UnityEditor.PrefabUtility.IsPartOfPrefabInstance(component))
                UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(component);

            var scene = component.gameObject.scene;
            if (scene.IsValid() && scene.isLoaded)
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
#endif
        }

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
