using UnityEngine;

namespace CableGeneratorV2Runtime
{
    /// <summary>
    /// スパイラルチューブ（ケーブル結束用の螺旋巻きチューブ）の断面プロファイル。
    /// </summary>
    [CreateAssetMenu(fileName = "SpiralTubeProfile", menuName = "Cable Generator V2/Spiral Tube Profile")]
    public class SpiralTubeProfile : CableProfile, ITwistedProfile
    {
        [Tooltip("内半径（結束するケーブル束の半径相当）")]
        public float innerRadius = 0.02f;

        [Tooltip("チューブの肉厚（壁の厚み）")]
        public float wallThickness = 0.003f;

        [Tooltip("巻きの隙間角度（度）。大きいほど開いた螺旋になる。0で隙間なしの閉じた筒")]
        [Range(0f, 350f)]
        public float gapAngleDegrees = 90f;

        [Tooltip("円弧（C字）の分割数。大きいほど断面が滑らかになる")]
        [Range(3, 64)]
        public int segments = 24;

        [Tooltip("1メートルあたりの巻き数（螺旋のピッチ。負値で逆巻き）")]
        [Range(-20f, 20f)]
        public float twistTurnsPerMeter = 3f;

        public float GetTwistTurnsPerMeter() => twistTurnsPerMeter;

        public override Vector2[] GetVertices()
        {
            BuildLoop(out var verts, out _);
            return verts;
        }

        public override Vector2[] GetNormals()
        {
            BuildLoop(out _, out var norms);
            return norms;
        }

        public override float[] GetUCoords()
        {
            BuildLoop(out var verts, out _);
            var us = new float[verts.Length];

            float totalPerimeter = 0f;
            for (int i = 1; i < verts.Length; i++)
                totalPerimeter += Vector2.Distance(verts[i], verts[i - 1]);

            if (totalPerimeter < 1e-6f) return us;

            float cumulative = 0f;
            for (int i = 1; i < verts.Length; i++)
            {
                cumulative += Vector2.Distance(verts[i], verts[i - 1]);
                us[i] = cumulative / totalPerimeter;
            }

            return us;
        }

        void BuildLoop(out Vector2[] verts, out Vector2[] norms)
        {
            int n = Mathf.Max(3, segments);
            float gap = Mathf.Clamp(gapAngleDegrees, 0f, 350f) * Mathf.Deg2Rad;
            float rIn = Mathf.Max(1e-4f, innerRadius);
            float rOut = rIn + Mathf.Max(1e-4f, wallThickness);

            float start = gap * 0.5f;
            float end = Mathf.PI * 2f - gap * 0.5f;
            float cover = end - start;

            int count = (n + 1) + 2 + (n + 1) + 2 + 1;
            verts = new Vector2[count];
            norms = new Vector2[count];
            int idx = 0;

            for (int i = 0; i <= n; i++)
            {
                float a = start + cover * (i / (float)n);
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                verts[idx] = new Vector2(c * rOut, s * rOut);
                norms[idx] = new Vector2(c, s);
                idx++;
            }

            {
                float c = Mathf.Cos(end), s = Mathf.Sin(end);
                Vector2 capN = new Vector2(-s, c);
                verts[idx] = new Vector2(c * rOut, s * rOut); norms[idx] = capN; idx++;
                verts[idx] = new Vector2(c * rIn, s * rIn); norms[idx] = capN; idx++;
            }

            for (int i = 0; i <= n; i++)
            {
                float a = end - cover * (i / (float)n);
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                verts[idx] = new Vector2(c * rIn, s * rIn);
                norms[idx] = new Vector2(-c, -s);
                idx++;
            }

            {
                float c = Mathf.Cos(start), s = Mathf.Sin(start);
                Vector2 capN = new Vector2(s, -c);
                verts[idx] = new Vector2(c * rIn, s * rIn); norms[idx] = capN; idx++;
                verts[idx] = new Vector2(c * rOut, s * rOut); norms[idx] = capN; idx++;
            }

            verts[idx] = verts[0];
            norms[idx] = norms[0];
        }
    }
}

