using System;
using UnityEditor;
using CableGeneratorV2Runtime;

namespace CableGeneratorV2Editor
{
    /// <summary>
    /// シーンで操作しているケーブルと、表示を読み直す合図（Changed）。
    ///   Target  : Hierarchy で選んでいるケーブル（Inspector が畳まれていても、ウィンドウが閉じていても追従する）
    ///   Current : 作業中（2点配線・なぞり描画）のケーブル → なければ Target。S1 ツールバーとシーンの操作はこれを見る
    /// 値そのもの（モード・選択・トグル）は CableSceneHandles / CableKnotSelection / CableGenerator が持つ。
    /// </summary>
    [InitializeOnLoad]
    internal static class CableSceneToolState
    {
        /// <summary>対象・作業・選択・トグル・言語が変わった。</summary>
        public static event Action Changed;

        public static CableGenerator Target { get; private set; }

        public static CableGenerator Current => CableSceneHandles.WorkingCable != null ? CableSceneHandles.WorkingCable : Target;

        /// <summary>S1 ツールバーを出すか（ケーブルを選んでいる・作業中）。</summary>
        public static bool Active => Current != null;

        static CableSceneToolState()
        {
            Selection.selectionChanged += RefreshTarget;
            EditorApplication.hierarchyChanged += RefreshTarget;
            Undo.undoRedoPerformed += Raise;
            CableL10n.Changed += Raise;
            EditorApplication.delayCall += RefreshTarget;
        }

        static void RefreshTarget()
        {
            var go = Selection.activeGameObject;
            var gen = go != null && !EditorUtility.IsPersistent(go) ? go.GetComponent<CableGenerator>() : null;
            if (gen == Target) return;
            Target = gen;
            Raise();
        }

        public static void Raise() => Changed?.Invoke();
    }
}
