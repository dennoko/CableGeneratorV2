using System;
using System.Collections.Generic;
using UnityEngine;
using CableGeneratorV2Runtime;

namespace CableGeneratorV2Editor
{
    /// <summary>
    /// 制御点の選択（設計書 §6.2）。シーン・配線タブの一覧・形タブ・S1 ツールバーが同じものを読み書きする。
    ///   Indices : 選択中の制御点（シーンの移動矢印を出す対象）。空なら何も選んでいない
    ///   Active  : 最後に選んだ制御点。選択が空でないときは必ず Indices に含まれる
    ///   Owner   : 選択がどのケーブルのものか。別のケーブルに切り替わったら選択を引き継がない
    /// </summary>
    internal static class CableKnotSelection
    {
        /// <summary>選択が変わった。表示はこれで読み直す。</summary>
        public static event Action Changed;

        private static readonly HashSet<int> _indices = new HashSet<int>();

        public static CableGenerator Owner { get; private set; }
        public static int Active { get; private set; }
        public static int Count => _indices.Count;
        public static bool HasSelection => _indices.Count > 0;

        public static bool Contains(int index) => _indices.Contains(index);

        /// <summary>選択中の制御点を昇順で返す（範囲外は除く）。</summary>
        public static List<int> Sorted(int knotCount)
        {
            var list = new List<int>(_indices.Count);
            foreach (int i in _indices)
                if (i >= 0 && i < knotCount) list.Add(i);
            list.Sort();
            return list;
        }

        public static void SelectSingle(CableGenerator owner, int index)
        {
            Owner = owner;
            _indices.Clear();
            _indices.Add(index);
            Active = index;
            Raise();
        }

        public static void Toggle(CableGenerator owner, int index)
        {
            if (Owner != owner) { SelectSingle(owner, index); return; }
            if (_indices.Remove(index))
            {
                if (Active == index && _indices.Count > 0) Active = Min();
            }
            else
            {
                _indices.Add(index);
                Active = index;
            }
            Raise();
        }

        /// <summary>まとめて置き換える（矩形選択・一覧の複数選択）。active は選ぶ点が無ければ無視する。</summary>
        public static void Set(CableGenerator owner, IEnumerable<int> indices, int active = -1)
        {
            Owner = owner;
            _indices.Clear();
            foreach (int i in indices) _indices.Add(i);
            if (_indices.Count > 0)
                Active = _indices.Contains(active) ? active : (_indices.Contains(Active) ? Active : Min());
            Raise();
        }

        public static void Clear()
        {
            if (_indices.Count == 0) return;
            _indices.Clear();
            Raise();
        }

        /// <summary>別のケーブルに切り替わったら選択を外す。Undo などで減った制御点は外す。</summary>
        public static void Validate(CableGenerator gen, int knotCount)
        {
            if (Owner != gen)
            {
                Owner = gen;
                _indices.Clear();
                Active = 0;
                Raise();
                return;
            }

            bool removed = _indices.RemoveWhere(i => i < 0 || i >= knotCount) > 0;
            if (_indices.Count > 0 && !_indices.Contains(Active)) Active = Min();
            if (_indices.Count == 0) Active = Mathf.Clamp(Active, 0, Mathf.Max(0, knotCount - 1));
            if (removed) Raise();
        }

        private static int Min()
        {
            int min = int.MaxValue;
            foreach (int i in _indices) if (i < min) min = i;
            return min == int.MaxValue ? 0 : min;
        }

        public static void Raise()
        {
            Changed?.Invoke();
            CableSceneToolState.Raise();
            UnityEditor.SceneView.RepaintAll();
        }
    }
}
