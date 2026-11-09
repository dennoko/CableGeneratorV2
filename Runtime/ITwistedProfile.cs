namespace CableGeneratorV2Runtime
{
    /// <summary>
    /// 断面をスプライン距離に応じて回転（ねじれ）させるプロファイル用の追加インターフェース。
    /// </summary>
    public interface ITwistedProfile
    {
        /// <summary>1メートルあたりのねじれ回転数（負値で逆方向）</summary>
        float GetTwistTurnsPerMeter();
    }
}

