using UnityEngine;

namespace CableGeneratorV2Runtime
{
    /// <summary>
    /// 複数のワイヤーをリング状に配置し、スプラインに沿ってねじれさせる断面プロファイル。
    /// </summary>
    [CreateAssetMenu(fileName = "TwistedCableProfile", menuName = "Cable Generator V2/Twisted Cable Profile")]
    public class TwistedCableProfile : CableProfile, ITwistedProfile
    {
        [Tooltip("ワイヤーの本数")]
        [Range(2, 16)]
        public int wireCount = 2;

        [Tooltip("各ワイヤーの半径")]
        public float wireRadius = 0.008f;

        [Tooltip("隣接ワイヤー表面間の間隔（0で密着）")]
        public float spacing = 0f;

        [Tooltip("各ワイヤーの円周分割数")]
        [Range(4, 32)]
        public int segments = 8;

        [Tooltip("1メートルあたりのねじれ回転数（負値で逆方向）")]
        [Range(-20f, 20f)]
        public float twistTurnsPerMeter = 2f;

        public float GetTwistTurnsPerMeter() => twistTurnsPerMeter;

        public override int GetVerticesPerLoop() => Mathf.Max(3, segments) + 1;

        float ComputeRingRadius(int count)
        {
            if (count < 2) return 0f;
            float chord = wireRadius * 2f + Mathf.Max(0f, spacing);
            return chord / (2f * Mathf.Sin(Mathf.PI / count));
        }

        Vector2 WireCenter(int wireIndex, int count, float ringRadius)
        {
            float angle = (float)wireIndex / count * Mathf.PI * 2f;
            return new Vector2(Mathf.Cos(angle) * ringRadius, Mathf.Sin(angle) * ringRadius);
        }

        public override Vector2[] GetVertices()
        {
            int count = Mathf.Max(2, wireCount);
            int seg = Mathf.Max(3, segments);
            int vertsPerWire = seg + 1;
            var verts = new Vector2[vertsPerWire * count];
            float ringRadius = ComputeRingRadius(count);

            for (int w = 0; w < count; w++)
            {
                Vector2 center = WireCenter(w, count, ringRadius);
                int baseIdx = w * vertsPerWire;
                for (int i = 0; i <= seg; i++)
                {
                    float angle = (float)i / seg * Mathf.PI * 2f;
                    verts[baseIdx + i] = new Vector2(
                        center.x + Mathf.Cos(angle) * wireRadius,
                        center.y + Mathf.Sin(angle) * wireRadius
                    );
                }
            }

            return verts;
        }

        public override Vector2[] GetNormals()
        {
            int count = Mathf.Max(2, wireCount);
            int seg = Mathf.Max(3, segments);
            int vertsPerWire = seg + 1;
            var norms = new Vector2[vertsPerWire * count];

            for (int w = 0; w < count; w++)
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

        public override float[] GetUCoords()
        {
            int count = Mathf.Max(2, wireCount);
            int seg = Mathf.Max(3, segments);
            int vertsPerWire = seg + 1;
            var us = new float[vertsPerWire * count];

            float uPerWire = 1f / count;

            for (int w = 0; w < count; w++)
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

