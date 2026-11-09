using System;
using System.Collections.Generic;
using UnityEngine;

namespace CableGeneratorV2Runtime
{
    /// <summary>
    /// UnityEngine.Splines 非依存の軽量ベジェ・自動補間スプラインクラス。
    /// V1 の Spline 操作・イベント（KnotInserted / KnotRemoved）と完全互換でありながら、
    /// 区間ごとの直接たわみ（sag）や自動 Catmull-Rom 補間をネイティブにサポートする。
    /// </summary>
    [Serializable]
    public partial class CableSpline
    {
        [SerializeField]
        List<CableKnot> knots = new List<CableKnot>();

        [SerializeField]
        bool closed = false;

        public event Action<CableSpline, int, CableSplineModification> Changed;
        public static event Action<CableSpline, int, CableSplineModification> AnySplineChanged;

        /// <summary>
        /// ReplaceKnots によるノット列の一括差し替え時に、旧インデックス → 新インデックスの対応表を通知する。
        /// （アタッチメントのノット追従用。通知は Changed / AnySplineChanged より先に行われる）
        /// </summary>
        public static event Action<CableSpline, int[]> AnyKnotsRemapped;

        public int Count => knots.Count;

        public int SegmentCount => knots.Count < 2 ? 0 : (closed ? knots.Count : knots.Count - 1);

        public bool Closed
        {
            get => closed;
            set
            {
                if (closed == value) return;
                closed = value;
                NotifyChanged(-1, CableSplineModification.ClosedModified);
            }
        }

        public CableKnot this[int index]
        {
            get => knots[index];
            set => SetKnot(index, value);
        }

        public IReadOnlyList<CableKnot> Knots => knots;

        public CableSpline()
        {
            knots.Add(new CableKnot(new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, -0.5f), new Vector3(0f, 0f, 0.5f), Quaternion.identity, CableTangentMode.AutoSmooth));
            knots.Add(new CableKnot(new Vector3(0f, 0f, 2f), new Vector3(0f, 0f, -0.5f), new Vector3(0f, 0f, 0.5f), Quaternion.identity, CableTangentMode.AutoSmooth));
        }

        public void Clear()
        {
            knots.Clear();
            NotifyChanged(-1, CableSplineModification.Default);
        }

        public void Add(CableKnot knot)
        {
            EnsureValidRotation(ref knot);
            knots.Add(knot);
            NotifyChanged(knots.Count - 1, CableSplineModification.KnotInserted);
        }

        public void Add(CableKnot knot, CableTangentMode mode)
        {
            knot.mode = mode;
            Add(knot);
        }

        public void Insert(int index, CableKnot knot)
        {
            EnsureValidRotation(ref knot);
            index = Mathf.Clamp(index, 0, knots.Count);
            knots.Insert(index, knot);
            NotifyChanged(index, CableSplineModification.KnotInserted);
        }

        public void Insert(int index, CableKnot knot, CableTangentMode mode)
        {
            knot.mode = mode;
            Insert(index, knot);
        }

        public void RemoveAt(int index)
        {
            if (index < 0 || index >= knots.Count) return;
            knots.RemoveAt(index);
            NotifyChanged(index, CableSplineModification.KnotRemoved);
        }

        public void SetKnot(int index, CableKnot knot, bool notify = true)
        {
            if (index < 0 || index >= knots.Count) return;
            EnsureValidRotation(ref knot);
            knots[index] = knot;
            if (notify)
                NotifyChanged(index, CableSplineModification.KnotModified);
        }

        /// <summary>
        /// ノット列を一括で差し替え、変更通知を 1 回だけ発行する。
        /// Clear() + Add() を繰り返すとアタッチメントのインデックスが KnotInserted のたびにずれるため、
        /// 配線のやり直しや細分化などの構造変更はこちらを使う。
        /// </summary>
        /// <param name="oldToNewIndex">旧インデックス → 新インデックス。null の場合は始点・終点を保った比例対応になる。</param>
        public void ReplaceKnots(IList<CableKnot> newKnots, bool isClosed, int[] oldToNewIndex = null)
        {
            if (newKnots == null) throw new ArgumentNullException(nameof(newKnots));

            int oldCount = knots.Count;
            var copy = new List<CableKnot>(newKnots.Count);
            for (int i = 0; i < newKnots.Count; i++)
            {
                var k = newKnots[i];
                EnsureValidRotation(ref k);
                copy.Add(k);
            }

            knots = copy;
            closed = isClosed;

            if (oldCount > 0 && knots.Count > 0)
            {
                var map = oldToNewIndex ?? BuildProportionalRemap(oldCount, knots.Count);
                AnyKnotsRemapped?.Invoke(this, map);
            }

            NotifyChanged(-1, CableSplineModification.Default);
        }

        /// <summary>始点は始点、終点は終点に対応させ、中間は弧の比率で割り当てる対応表を作る。</summary>
        public static int[] BuildProportionalRemap(int oldCount, int newCount)
        {
            var map = new int[Mathf.Max(0, oldCount)];
            for (int i = 0; i < map.Length; i++)
            {
                map[i] = (oldCount <= 1 || newCount <= 1)
                    ? 0
                    : Mathf.Clamp(Mathf.RoundToInt((float)i / (oldCount - 1) * (newCount - 1)), 0, newCount - 1);
            }
            return map;
        }

        public CableTangentMode GetTangentMode(int index)
        {
            if (index < 0 || index >= knots.Count) return CableTangentMode.AutoSmooth;
            return knots[index].mode;
        }

        public void SetTangentMode(int index, CableTangentMode mode, bool notify = true)
        {
            if (index < 0 || index >= knots.Count) return;
            var k = knots[index];
            if (k.mode == mode) return;

            // AutoSmooth から手動モードへ切り替える際は、現在の自動接線を初期値として引き継ぐ
            if (k.mode == CableTangentMode.AutoSmooth && mode != CableTangentMode.AutoSmooth && knots.Count >= 2)
            {
                ComputeAutoWorldTangents(index, out Vector3 wIn, out Vector3 wOut);
                Quaternion invRot = Quaternion.Inverse(k.rotation);
                k.tangentIn  = invRot * wIn;
                k.tangentOut = invRot * wOut;
            }

            k.mode = mode;
            knots[index] = k;
            if (notify)
                NotifyChanged(index, CableSplineModification.KnotModified);
        }

        public float GetSegmentSag(int segmentIndex)
        {
            if (segmentIndex < 0 || segmentIndex >= knots.Count) return 0f;
            return knots[segmentIndex].sag;
        }

        public void SetSegmentSag(int segmentIndex, float sag, bool notify = true)
        {
            if (segmentIndex < 0 || segmentIndex >= knots.Count) return;
            var k = knots[segmentIndex];
            k.sag = Mathf.Max(0f, sag);
            knots[segmentIndex] = k;
            if (notify)
                NotifyChanged(segmentIndex, CableSplineModification.KnotModified);
        }

        public void NotifyChanged(int knotIndex = -1, CableSplineModification mod = CableSplineModification.Default)
        {
            Changed?.Invoke(this, knotIndex, mod);
            AnySplineChanged?.Invoke(this, knotIndex, mod);
        }

        static void EnsureValidRotation(ref CableKnot knot)
        {
            float magSq = knot.rotation.x * knot.rotation.x
                        + knot.rotation.y * knot.rotation.y
                        + knot.rotation.z * knot.rotation.z
                        + knot.rotation.w * knot.rotation.w;
            if (magSq < 1e-8f)
                knot.rotation = Quaternion.identity;
        }
    }
}
