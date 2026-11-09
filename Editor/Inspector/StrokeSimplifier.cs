using System.Collections.Generic;
using UnityEngine;

namespace CableGeneratorV2Editor
{
    public static class StrokeSimplifier
    {
        const float kVectorEpsilonSqr = 1e-10f;

        /// <summary>
        /// 3D Ramer–Douglas–Peucker アルゴリズムによるストローク頂点の間引き。
        /// </summary>
        public static List<int> SimplifyStroke(List<Vector3> points, float tolerance)
        {
            var kept = new List<int>();
            if (points == null || points.Count == 0) return kept;

            float minSpacing = Mathf.Max(tolerance * 0.25f, 0.001f);
            float minSpacingSqr = minSpacing * minSpacing;

            var thinned = new List<int> { 0 };
            for (int i = 1; i < points.Count; i++)
                if ((points[i] - points[thinned[thinned.Count - 1]]).sqrMagnitude >= minSpacingSqr)
                    thinned.Add(i);

            int lastIndex = points.Count - 1;
            if (thinned[thinned.Count - 1] != lastIndex)
            {
                bool lastTooClose =
                    (points[lastIndex] - points[thinned[thinned.Count - 1]]).sqrMagnitude < minSpacingSqr;
                if (lastTooClose && thinned.Count > 1)
                    thinned[thinned.Count - 1] = lastIndex;
                else
                    thinned.Add(lastIndex);
            }

            if (thinned.Count <= 2)
                return thinned;

            var keep = new bool[thinned.Count];
            keep[0] = keep[thinned.Count - 1] = true;

            float tolSqr = tolerance * tolerance;
            var ranges = new Stack<(int first, int last)>();
            ranges.Push((0, thinned.Count - 1));

            while (ranges.Count > 0)
            {
                var (first, last) = ranges.Pop();
                if (last - first < 2) continue;

                Vector3 a = points[thinned[first]];
                Vector3 b = points[thinned[last]];

                float maxDistSqr = -1f;
                int   maxIndex   = -1;
                for (int i = first + 1; i < last; i++)
                {
                    float d = DistanceToSegmentSqr(points[thinned[i]], a, b);
                    if (d > maxDistSqr)
                    {
                        maxDistSqr = d;
                        maxIndex   = i;
                    }
                }

                if (maxDistSqr > tolSqr)
                {
                    keep[maxIndex] = true;
                    ranges.Push((first, maxIndex));
                    ranges.Push((maxIndex, last));
                }
            }

            for (int i = 0; i < thinned.Count; i++)
                if (keep[i])
                    kept.Add(thinned[i]);
            return kept;
        }

        static float DistanceToSegmentSqr(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float lenSqr = ab.sqrMagnitude;
            if (lenSqr < kVectorEpsilonSqr) return (p - a).sqrMagnitude;

            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / lenSqr);
            return (p - (a + ab * t)).sqrMagnitude;
        }
    }
}
