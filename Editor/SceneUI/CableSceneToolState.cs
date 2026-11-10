using System;
using UnityEditor;
using CableGeneratorV2Runtime;

namespace CableGeneratorV2Editor
{
    /// <summary>
    /// シーン操作 UI（S1 ツールバー）が見る対象と変更通知。
    /// 値そのもの（モード・ノット選択・トグル）は CableGeneratorInspector / CableGenerator が持ち、
    /// ここは「どのケーブルを操作しているか」と「表示を読み直す合図 (Changed)」だけを持つ。
    /// </summary>
    [InitializeOnLoad]
    internal static class CableSceneToolState
    {
        /// <summary>モード・選択・トグルが変わった。ツールバーはこれで表示を読み直す。</summary>
        public static event Action Changed;

        /// <summary>今 Inspector で選ばれているケーブル。null ならツールバーを隠す。</summary>
        public static CableGenerator Target { get; private set; }

        public static bool Active => Target != null;

        static CableSceneToolState()
        {
            Undo.undoRedoPerformed += Raise;
        }

        public static void SetTarget(CableGenerator gen)
        {
            if (Target == gen) return;
            Target = gen;
            Raise();
        }

        /// <summary>gen が今の対象のときだけ外す（Inspector の作り直しで新しい対象を消さないため）。</summary>
        public static void ClearTarget(CableGenerator gen)
        {
            if (Target != gen && Target != null) return;
            Target = null;
            Raise();
        }

        public static void Raise() => Changed?.Invoke();
    }
}
