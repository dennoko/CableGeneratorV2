using UnityEngine;

namespace CableGeneratorV2Runtime
{
    /// <summary>
    /// 角丸長方形の断面プロファイル（フラットケーブル向け）。
    /// </summary>
    [CreateAssetMenu(fileName = "FlatCableProfile", menuName = "Cable Generator V2/Flat Cable Profile")]
    public class FlatCableProfile : CableProfile
    {
        [Tooltip("幅（X方向の全長）")]
        public float width = 0.1f;

        [Tooltip("厚み（Y方向の全長）")]
        public float thickness = 0.02f;

        [Tooltip("角丸の半径")]
        public float cornerRadius = 0.005f;

        [Tooltip("角丸の分割数")]
        [Range(1, 8)]
        public int cornerSegments = 3;

        public override Vector2[] GetVertices()
        {
            int cSeg = Mathf.Max(1, cornerSegments);
            float r = Mathf.Min(cornerRadius, width * 0.5f, thickness * 0.5f);
            float hw = width * 0.5f - r;
            float hh = thickness * 0.5f - r;

            int vertsPerCorner = cSeg + 1;
            int totalVerts = vertsPerCorner * 4 + 1;
            var verts = new Vector2[totalVerts];

            int idx = 0;

            for (int i = 0; i <= cSeg; i++)
            {
                float angle = (float)i / cSeg * Mathf.PI * 0.5f;
                verts[idx++] = new Vector2(hw + Mathf.Cos(angle) * r, hh + Mathf.Sin(angle) * r);
            }

            for (int i = 0; i <= cSeg; i++)
            {
                float angle = Mathf.PI * 0.5f + (float)i / cSeg * Mathf.PI * 0.5f;
                verts[idx++] = new Vector2(-hw + Mathf.Cos(angle) * r, hh + Mathf.Sin(angle) * r);
            }

            for (int i = 0; i <= cSeg; i++)
            {
                float angle = Mathf.PI + (float)i / cSeg * Mathf.PI * 0.5f;
                verts[idx++] = new Vector2(-hw + Mathf.Cos(angle) * r, -hh + Mathf.Sin(angle) * r);
            }

            for (int i = 0; i <= cSeg; i++)
            {
                float angle = Mathf.PI * 1.5f + (float)i / cSeg * Mathf.PI * 0.5f;
                verts[idx++] = new Vector2(hw + Mathf.Cos(angle) * r, -hh + Mathf.Sin(angle) * r);
            }

            verts[idx] = verts[0];

            return verts;
        }

        public override Vector2[] GetNormals()
        {
            int cSeg = Mathf.Max(1, cornerSegments);
            int vertsPerCorner = cSeg + 1;
            int totalVerts = vertsPerCorner * 4 + 1;
            var norms = new Vector2[totalVerts];

            int idx = 0;

            for (int i = 0; i <= cSeg; i++)
            {
                float angle = (float)i / cSeg * Mathf.PI * 0.5f;
                norms[idx++] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            }

            for (int i = 0; i <= cSeg; i++)
            {
                float angle = Mathf.PI * 0.5f + (float)i / cSeg * Mathf.PI * 0.5f;
                norms[idx++] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            }

            for (int i = 0; i <= cSeg; i++)
            {
                float angle = Mathf.PI + (float)i / cSeg * Mathf.PI * 0.5f;
                norms[idx++] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            }

            for (int i = 0; i <= cSeg; i++)
            {
                float angle = Mathf.PI * 1.5f + (float)i / cSeg * Mathf.PI * 0.5f;
                norms[idx++] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            }

            norms[idx] = norms[0];

            return norms;
        }

        public override float[] GetUCoords()
        {
            var verts = GetVertices();
            var us = new float[verts.Length];

            float totalPerimeter = 0f;
            for (int i = 1; i < verts.Length; i++)
                totalPerimeter += Vector2.Distance(verts[i], verts[i - 1]);

            if (totalPerimeter < 1e-6f) return us;

            us[0] = 0f;
            float cumulative = 0f;
            for (int i = 1; i < verts.Length; i++)
            {
                cumulative += Vector2.Distance(verts[i], verts[i - 1]);
                us[i] = cumulative / totalPerimeter;
            }

            return us;
        }
    }
}

