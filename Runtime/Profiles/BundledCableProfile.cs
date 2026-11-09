using UnityEngine;

namespace CableGeneratorV2Runtime
{
    /// <summary>
    /// 任意の本数の丸ケーブルを横一列に並べた断面プロファイル。
    /// </summary>
    [CreateAssetMenu(fileName = "BundledCableProfile", menuName = "Cable Generator V2/Bundled Cable Profile")]
    public class BundledCableProfile : CableProfile
    {
        [Tooltip("ケーブルの本数")]
        [Range(1, 24)]
        public int wireCount = 5;

        [Tooltip("各ケーブルの半径")]
        public float wireRadius = 0.01f;

        [Tooltip("隣接するケーブル間の隙間")]
        public float gap = 0.002f;

        [Tooltip("各ケーブルの円周分割数")]
        [Range(4, 32)]
        public int segments = 8;

        float WirePitch => wireRadius * 2f + gap;
        float BundleHalfWidth => WirePitch * (Mathf.Max(1, wireCount) - 1) * 0.5f;

        public override Vector2[] GetVertices()
        {
            int wCount = Mathf.Max(1, wireCount);
            int seg = Mathf.Max(3, segments);
            int vertsPerWire = seg + 1;
            int totalVerts = vertsPerWire * wCount;
            var verts = new Vector2[totalVerts];

            float halfWidth = BundleHalfWidth;
            float pitch = WirePitch;

            for (int w = 0; w < wCount; w++)
            {
                float cx = -halfWidth + pitch * w;
                int baseIdx = w * vertsPerWire;

                for (int i = 0; i <= seg; i++)
                {
                    float angle = (float)i / seg * Mathf.PI * 2f;
                    verts[baseIdx + i] = new Vector2(
                        cx + Mathf.Cos(angle) * wireRadius,
                        Mathf.Sin(angle) * wireRadius
                    );
                }
            }

            return verts;
        }

        public override Vector2[] GetNormals()
        {
            int wCount = Mathf.Max(1, wireCount);
            int seg = Mathf.Max(3, segments);
            int vertsPerWire = seg + 1;
            int totalVerts = vertsPerWire * wCount;
            var norms = new Vector2[totalVerts];

            for (int w = 0; w < wCount; w++)
            {
                int baseIdx = w * vertsPerWire;
                for (int i = 0; i <= seg; i++)
                {
                    float angle = (float)i / seg * Mathf.PI * 2f;
                    norms[baseIdx + i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                }
            }

            return norms;
        }

        public override int GetVerticesPerLoop() => Mathf.Max(3, segments) + 1;

        public override float[] GetUCoords()
        {
            int wCount = Mathf.Max(1, wireCount);
            int seg = Mathf.Max(3, segments);
            int vertsPerWire = seg + 1;
            int totalVerts = vertsPerWire * wCount;
            var us = new float[totalVerts];

            float uPerWire = 1f / wCount;

            for (int w = 0; w < wCount; w++)
            {
                int baseIdx = w * vertsPerWire;
                float uStart = uPerWire * w;
                for (int i = 0; i <= seg; i++)
                    us[baseIdx + i] = uStart + (float)i / seg * uPerWire;
            }

            return us;
        }
    }
}

