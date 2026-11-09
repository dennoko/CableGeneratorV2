using UnityEngine;

namespace CableGeneratorV2Runtime
{
    /// <summary>
    /// 太丸断面プロファイル（オプションで凹凸付き）。
    /// </summary>
    [CreateAssetMenu(fileName = "HeavyDutyCableProfile", menuName = "Cable Generator V2/Heavy Duty Cable Profile")]
    public class HeavyDutyCableProfile : CableProfile
    {
        [Tooltip("ケーブルの半径")]
        public float radius = 0.05f;

        [Tooltip("円周の分割数")]
        [Range(6, 64)]
        public int segments = 16;

        [Tooltip("凹凸のリブの数（0で滑らかな円）")]
        [Range(0, 24)]
        public int ribCount = 0;

        [Tooltip("リブの深さ（半径に対する割合）")]
        [Range(0f, 0.3f)]
        public float ribDepth = 0.1f;

        public override Vector2[] GetVertices()
        {
            int seg = Mathf.Max(3, segments);
            int count = seg + 1;
            var verts = new Vector2[count];

            for (int i = 0; i <= seg; i++)
            {
                float angle = (float)i / seg * Mathf.PI * 2f;
                float r = radius;

                if (ribCount > 0)
                {
                    float ribPhase = Mathf.Sin(angle * ribCount);
                    r -= ribDepth * radius * (ribPhase * 0.5f + 0.5f);
                }

                verts[i] = new Vector2(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r);
            }

            return verts;
        }

        public override Vector2[] GetNormals()
        {
            int seg = Mathf.Max(3, segments);
            int count = seg + 1;
            var norms = new Vector2[count];

            if (ribCount > 0)
            {
                var verts = GetVertices();
                for (int i = 0; i < count; i++)
                    norms[i] = verts[i].sqrMagnitude > 1e-12f ? verts[i].normalized : Vector2.right;
            }
            else
            {
                for (int i = 0; i <= seg; i++)
                {
                    float angle = (float)i / seg * Mathf.PI * 2f;
                    norms[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                }
            }

            return norms;
        }

        public override int GetVerticesPerLoop() => Mathf.Max(3, segments) + 1;

        public override float[] GetUCoords()
        {
            int seg = Mathf.Max(3, segments);
            int count = seg + 1;
            var us = new float[count];

            for (int i = 0; i <= seg; i++)
                us[i] = (float)i / seg;

            return us;
        }
    }
}

