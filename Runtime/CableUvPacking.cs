using System.Collections.Generic;
using UnityEngine;

namespace CableGeneratorV2Runtime
{
    public static class CableUvPacking
    {
        public struct StripInfo
        {
            public int loopIndex;
            public int splitIndex;
            public int splitCount;
            public float realWidth;
            public float realHeight;
            public float packU;
            public float packV;
            public float packW;
            public float packH;
        }

        public struct UV1Layout
        {
            public StripInfo[] strips;
            public int[] loopStripStart;
            public int[] loopSplitCount;
        }

        const float Padding = 0.01f;

        public static UV1Layout CalculateUV1Layout(float[] perimeters, int loopCount, float splineLength)
        {
            var layout = new UV1Layout
            {
                loopStripStart = new int[loopCount],
                loopSplitCount = new int[loopCount]
            };

            var stripList = new List<StripInfo>();

            for (int loop = 0; loop < loopCount; loop++)
            {
                float perimeter = (loop < perimeters.Length) ? perimeters[loop] : 0.01f;
                if (perimeter < 0.0001f) perimeter = 0.0001f;

                int splits = 1;
                layout.loopStripStart[loop] = stripList.Count;
                layout.loopSplitCount[loop] = splits;

                float stripHeight = splineLength / splits;

                for (int s = 0; s < splits; s++)
                {
                    stripList.Add(new StripInfo
                    {
                        loopIndex = loop,
                        splitIndex = s,
                        splitCount = splits,
                        realWidth = perimeter,
                        realHeight = stripHeight,
                    });
                }
            }

            var strips = stripList.ToArray();
            if (strips.Length == 0)
            {
                layout.strips = strips;
                return layout;
            }

            var sortedIndices = new int[strips.Length];
            for (int i = 0; i < sortedIndices.Length; i++) sortedIndices[i] = i;
            System.Array.Sort(sortedIndices, (a, b) =>
                strips[b].realHeight.CompareTo(strips[a].realHeight));

            float totalArea = 0f;
            for (int i = 0; i < strips.Length; i++)
                totalArea += strips[i].realWidth * strips[i].realHeight;

            float scaleFactor = totalArea > 1e-8f ? 1f / Mathf.Sqrt(totalArea) : 1f;
            float bestScale = scaleFactor;

            for (int attempt = 0; attempt < 16; attempt++)
            {
                if (TryShelfPack(strips, sortedIndices, bestScale, Padding, out _, out _))
                    break;
                bestScale *= 0.85f;
            }

            TryShelfPack(strips, sortedIndices, bestScale, Padding, out float usedW, out float usedH);

            float fillScale = 1f;
            if (usedW > 0.0001f && usedH > 0.0001f)
                fillScale = Mathf.Min((1f - Padding) / usedW, (1f - Padding) / usedH);

            for (int i = 0; i < strips.Length; i++)
            {
                var s = strips[i];
                s.packU = s.packU * fillScale + Padding * 0.5f;
                s.packV = s.packV * fillScale + Padding * 0.5f;
                s.packW *= fillScale;
                s.packH *= fillScale;
                strips[i] = s;
            }

            layout.strips = strips;
            return layout;
        }

        static bool TryShelfPack(StripInfo[] strips, int[] sortedIndices, float scale, float pad,
            out float usedWidth, out float usedHeight)
        {
            float cursorU = pad;
            float cursorV = pad;
            float shelfHeight = 0f;
            float maxU = 0f;

            for (int si = 0; si < sortedIndices.Length; si++)
            {
                int idx = sortedIndices[si];
                float w = strips[idx].realWidth * scale;
                float h = strips[idx].realHeight * scale;

                if (cursorU + w + pad > 1f)
                {
                    cursorU = pad;
                    cursorV += shelfHeight + pad;
                    shelfHeight = 0f;
                }

                var s = strips[idx];
                s.packU = cursorU;
                s.packV = cursorV;
                s.packW = w;
                s.packH = h;
                strips[idx] = s;

                cursorU += w + pad;
                if (h > shelfHeight) shelfHeight = h;
                if (cursorU > maxU) maxU = cursorU;
            }

            float maxV = cursorV + shelfHeight + pad;
            usedWidth = maxU;
            usedHeight = maxV;

            return maxV <= 1f;
        }

        public static Vector2 CalculateUV1(UV1Layout layout, int loopIdx, float localU, float t)
        {
            if (layout.strips == null || layout.strips.Length == 0)
                return new Vector2(localU, t);

            int splitCount = layout.loopSplitCount[loopIdx];
            int stripStart = layout.loopStripStart[loopIdx];

            int splitIdx = Mathf.FloorToInt(t * splitCount);
            if (splitIdx >= splitCount) splitIdx = splitCount - 1;

            int stripIdx = stripStart + splitIdx;
            var strip = layout.strips[stripIdx];
            float splitT = t * splitCount - splitIdx;

            return new Vector2(
                strip.packU + localU * strip.packW,
                strip.packV + splitT * strip.packH
            );
        }
    }
}
