using UnityEngine;

namespace CableGeneratorV2Runtime
{
    /// <summary>
    /// 断面プロファイルの抽象基底クラス（V1 完全互換＋LOD減衰・最大半径取得拡張）。
    /// </summary>
    public abstract class CableProfile : ScriptableObject
    {
        public event System.Action Changed;

        /// <summary>
        /// 任意のプロファイルが変更されたときに通知される静的イベント。
        /// 購読側は「自分が参照しているプロファイルか」を比較するだけで済み、購読先の追跡状態を持たなくてよい。
        /// </summary>
        public static event System.Action<CableProfile> AnyProfileChanged;

        protected virtual void OnValidate()
        {
            Changed?.Invoke();
            AnyProfileChanged?.Invoke(this);
        }

        /// <summary>断面の2D頂点座標（ローカル XY 平面）</summary>
        public abstract Vector2[] GetVertices();

        /// <summary>断面の2D法線（各頂点に対応）</summary>
        public abstract Vector2[] GetNormals();

        /// <summary>断面のU座標（各頂点に対応、0〜1）</summary>
        public abstract float[] GetUCoords();

        /// <summary>1ループ（1本のワイヤー）あたりの頂点数を返す</summary>
        public virtual int GetVerticesPerLoop() => GetVertices().Length;

        /// <summary>各ワイヤー（ループ）の周長を返す（UV1パッキング用）</summary>
        public virtual float[] GetPerimeters()
        {
            var verts = GetVertices();
            int vpl = Mathf.Max(1, GetVerticesPerLoop());
            int loopCount = verts.Length / vpl;
            var perimeters = new float[loopCount];

            for (int loop = 0; loop < loopCount; loop++)
            {
                int baseIdx = loop * vpl;
                float p = 0f;
                for (int i = 1; i < vpl; i++)
                    p += Vector2.Distance(verts[baseIdx + i], verts[baseIdx + i - 1]);
                perimeters[loop] = p;
            }

            return perimeters;
        }

        /// <summary>断面の外接円半径（コライダー接地時のめり込み防止オフセット用）</summary>
        public virtual float GetMaxRadius()
        {
            var verts = GetVertices();
            float maxR2 = 0f;
            for (int i = 0; i < verts.Length; i++)
            {
                float r2 = verts[i].sqrMagnitude;
                if (r2 > maxR2) maxR2 = r2;
            }
            return Mathf.Sqrt(maxR2);
        }
    }
}

