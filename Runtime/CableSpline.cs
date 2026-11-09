using System;
using System.Collections.Generic;
using UnityEngine;

namespace CableGeneratorV2Runtime
{
    public enum CableTangentMode
    {
        AutoSmooth = 0,
        Mirrored   = 1,
        Continuous = 2,
        Broken     = 3,
    }

    public enum CableSplineModification
    {
        Default        = 0,
        KnotModified   = 1,
        KnotInserted   = 2,
        KnotRemoved    = 3,
        ClosedModified = 4,
    }

    [Serializable]
    public struct CableKnot
    {
        public Vector3          position;
        public Vector3          tangentIn;
        public Vector3          tangentOut;
        public Quaternion       rotation;
        public CableTangentMode mode;
        public float            sag;

        public CableKnot(Vector3 position)
        {
            this.position   = position;
            this.tangentIn  = new Vector3(0f, 0f, -0.5f);
            this.tangentOut = new Vector3(0f, 0f,  0.5f);
            this.rotation   = Quaternion.identity;
            this.mode       = CableTangentMode.AutoSmooth;
            this.sag        = 0f;
        }

        public CableKnot(Vector3 position, Vector3 tangentIn, Vector3 tangentOut, Quaternion rotation, CableTangentMode mode = CableTangentMode.Mirrored, float sag = 0f)
        {
            this.position   = position;
            this.tangentIn  = tangentIn;
            this.tangentOut = tangentOut;
            this.rotation   = NormalizeSafe(rotation);
            this.mode       = mode;
            this.sag        = sag;
        }

        static Quaternion NormalizeSafe(Quaternion q)
        {
            float magSq = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            if (magSq < 1e-8f) return Quaternion.identity;
            float inv = 1f / Mathf.Sqrt(magSq);
            return new Quaternion(q.x * inv, q.y * inv, q.z * inv, q.w * inv);
        }
    }

    /// <summary>
    /// UnityEngine.Splines 非依存の軽量ベジェ・自動補間スプラインクラス。
    /// V1 の Spline 操作・イベント（KnotInserted / KnotRemoved）と完全互換でありながら、
    /// 区間ごとの直接たわみ（sag）や自動 Catmull-Rom 補間をネイティブにサポートする。
    /// </summary>
    [Serializable]
    public class CableSpline
    {
        const float kEpsilonSqr = 1e-12f;

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

        // ================================================================
        //  Curve Evaluation
        // ================================================================

        /// <summary>
        /// 指定ノットの自動接線ベクトル（スプラインローカル空間）を Catmull-Rom 式で計算する。
        /// </summary>
        public void ComputeAutoWorldTangents(int index, out Vector3 tangentInLocal, out Vector3 tangentOutLocal)
        {
            int n = knots.Count;
            if (n < 2)
            {
                tangentInLocal  = new Vector3(0f, 0f, -0.3f);
                tangentOutLocal = new Vector3(0f, 0f,  0.3f);
                return;
            }

            Vector3 pCurr = knots[index].position;
            Vector3 pPrev;
            Vector3 pNext;

            if (closed)
            {
                pPrev = knots[(index - 1 + n) % n].position;
                pNext = knots[(index + 1) % n].position;
            }
            else
            {
                pPrev = index > 0     ? knots[index - 1].position : pCurr - (knots[1].position - pCurr);
                pNext = index < n - 1 ? knots[index + 1].position : pCurr + (pCurr - knots[n - 2].position);
            }

            // Centripetal / Uniform ハイブリッド Catmull-Rom 接線（オーバーシュート防止）
            Vector3 dPrev = pCurr - pPrev;
            Vector3 dNext = pNext - pCurr;
            float lenPrev = dPrev.magnitude;
            float lenNext = dNext.magnitude;

            if (lenPrev < 1e-6f && lenNext < 1e-6f)
            {
                tangentInLocal  = Vector3.zero;
                tangentOutLocal = Vector3.zero;
                return;
            }

            Vector3 dirPrev = lenPrev > 1e-6f ? dPrev / lenPrev : dNext.normalized;
            Vector3 dirNext = lenNext > 1e-6f ? dNext / lenNext : dPrev.normalized;
            Vector3 bisector = (dirPrev + dirNext);
            if (bisector.sqrMagnitude < 1e-6f)
                bisector = dirNext;
            else
                bisector.Normalize();

            tangentInLocal  = -bisector * (lenPrev / 3f);
            tangentOutLocal =  bisector * (lenNext / 3f);
        }

        /// <summary>
        /// セグメント i -> (i+1) の 3次ベジェ制御点 (p0, p1, p2, p3) と直接たわみ量を取得する。
        /// </summary>
        public void GetSegmentControlPoints(
            int segmentIndex,
            out Vector3 p0, out Vector3 p1, out Vector3 p2, out Vector3 p3,
            out float sagAmount)
        {
            int n = knots.Count;
            int idxA = Mathf.Clamp(segmentIndex, 0, n - 1);
            int idxB = closed ? (idxA + 1) % n : Mathf.Min(idxA + 1, n - 1);

            CableKnot kA = knots[idxA];
            CableKnot kB = knots[idxB];

            p0 = kA.position;
            p3 = kB.position;
            sagAmount = kA.sag;

            Vector3 outVecA;
            if (kA.mode == CableTangentMode.AutoSmooth)
            {
                ComputeAutoWorldTangents(idxA, out _, out outVecA);
            }
            else
            {
                outVecA = kA.rotation * kA.tangentOut;
            }

            Vector3 inVecB;
            if (kB.mode == CableTangentMode.AutoSmooth)
            {
                ComputeAutoWorldTangents(idxB, out inVecB, out _);
            }
            else
            {
                inVecB = kB.rotation * kB.tangentIn;
            }

            p1 = p0 + outVecA;
            p2 = p3 + inVecB;
        }

        /// <summary>
        /// セグメント i 上の局所パラメータ u ∈ [0, 1] を評価する。
        /// </summary>
        public void EvaluateSegment(
            int segmentIndex, float u, Vector3 localGravityDir,
            out Vector3 pos, out Vector3 tangent, out Vector3 up)
        {
            int n = knots.Count;
            if (n == 0)
            {
                pos     = Vector3.zero;
                tangent = Vector3.forward;
                up      = Vector3.up;
                return;
            }
            if (n == 1)
            {
                pos     = knots[0].position;
                tangent = knots[0].rotation * Vector3.forward;
                up      = knots[0].rotation * Vector3.up;
                return;
            }

            u = Mathf.Clamp01(u);
            GetSegmentControlPoints(segmentIndex, out Vector3 p0, out Vector3 p1, out Vector3 p2, out Vector3 p3, out float sagAmount);

            float omu  = 1f - u;
            float omu2 = omu * omu;
            float u2   = u * u;

            pos = omu2 * omu * p0
                + 3f * omu2 * u * p1
                + 3f * omu * u2 * p2
                + u2 * u * p3;

            tangent = 3f * omu2 * (p1 - p0)
                    + 6f * omu * u * (p2 - p1)
                    + 3f * u2 * (p3 - p2);

            // 直接たわみ（sag）の放物線重畳: Δp(u) = 4u(1-u) * sag * g
            if (sagAmount > 1e-5f && localGravityDir.sqrMagnitude > kEpsilonSqr)
            {
                Vector3 g = localGravityDir.normalized;
                pos     += (4f * u * omu * sagAmount) * g;
                tangent += (4f * (1f - 2f * u) * sagAmount) * g;
            }

            if (tangent.sqrMagnitude < kEpsilonSqr)
            {
                Vector3 chord = p3 - p0;
                tangent = chord.sqrMagnitude > kEpsilonSqr ? chord.normalized : Vector3.forward;
            }

            int idxA = Mathf.Clamp(segmentIndex, 0, n - 1);
            int idxB = closed ? (idxA + 1) % n : Mathf.Min(idxA + 1, n - 1);
            Quaternion midRot = Quaternion.Slerp(knots[idxA].rotation, knots[idxB].rotation, u);
            up = midRot * Vector3.up;
            if (up.sqrMagnitude < kEpsilonSqr)
                up = Vector3.up;
        }

        /// <summary>
        /// スプライン全体の正規化パラメータ t ∈ [0, 1] を評価する。
        /// </summary>
        public void Evaluate(float t, out Vector3 pos, out Vector3 tangent, out Vector3 up)
        {
            Evaluate(t, Vector3.down, out pos, out tangent, out up);
        }

        public void Evaluate(float t, Vector3 localGravityDir, out Vector3 pos, out Vector3 tangent, out Vector3 up)
        {
            int segCount = SegmentCount;
            if (segCount <= 0)
            {
                EvaluateSegment(0, 0f, localGravityDir, out pos, out tangent, out up);
                return;
            }

            t = Mathf.Clamp01(t);
            float scaled = t * segCount;
            int segIdx = Mathf.FloorToInt(scaled);
            if (segIdx >= segCount)
            {
                segIdx = segCount - 1;
                scaled = segCount;
            }
            float u = scaled - segIdx;
            EvaluateSegment(segIdx, u, localGravityDir, out pos, out tangent, out up);
        }

        /// <summary>
        /// スプライン全体のワールド弧長を計算する。
        /// </summary>
        public float CalculateLength(Matrix4x4 localToWorld, Vector3 localGravityDir, int samplesPerSegment = 16)
        {
            int segCount = SegmentCount;
            if (segCount <= 0) return 0f;

            samplesPerSegment = Mathf.Max(4, samplesPerSegment);
            float totalLength = 0f;

            for (int seg = 0; seg < segCount; seg++)
            {
                EvaluateSegment(seg, 0f, localGravityDir, out Vector3 prevLocal, out _, out _);
                Vector3 prevWorld = localToWorld.MultiplyPoint3x4(prevLocal);

                for (int s = 1; s <= samplesPerSegment; s++)
                {
                    float u = (float)s / samplesPerSegment;
                    EvaluateSegment(seg, u, localGravityDir, out Vector3 currLocal, out _, out _);
                    Vector3 currWorld = localToWorld.MultiplyPoint3x4(currLocal);
                    totalLength += Vector3.Distance(prevWorld, currWorld);
                    prevWorld = currWorld;
                }
            }

            return totalLength;
        }

        public float CalculateLength(Matrix4x4 localToWorld)
        {
            return CalculateLength(localToWorld, Vector3.down, 16);
        }

        // ================================================================
        //  Static Math Helpers
        // ================================================================

        public static Quaternion SafeLookRotation(Vector3 forward)
        {
            if (forward.sqrMagnitude < 1e-8f) forward = Vector3.forward;
            forward.Normalize();
            Vector3 up = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
            return Quaternion.LookRotation(forward, up);
        }

        public static Quaternion SafeLookRotation(Vector3 forward, Vector3 upHint)
        {
            if (forward.sqrMagnitude < 1e-8f) forward = Vector3.forward;
            forward.Normalize();

            Vector3 up = upHint.sqrMagnitude < 1e-8f ? Vector3.up : upHint.normalized;
            if (Mathf.Abs(Vector3.Dot(forward, up)) > 0.99f)
                up = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > 0.99f ? Vector3.right : Vector3.up;

            return Quaternion.LookRotation(forward, up);
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

