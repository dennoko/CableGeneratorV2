using UnityEditor;
using UnityEngine;

namespace CableGeneratorV2Editor
{
    /// <summary>
    /// ツール設定（設計書 §8）。ケーブルやプロファイルの値ではなく、作業の仕方と出力の決めごと。
    /// プロジェクトごと・ユーザーごと（UserSettings/）に保存する。値を変えたら Save() を呼ぶ。
    /// </summary>
    [FilePath("UserSettings/CableGeneratorV2Settings.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class CableToolSettings : ScriptableSingleton<CableToolSettings>
    {
        public const string DefaultProfileFolder = "Assets/CableProfiles";

        // ─── 配線 › 引き方 ─────────────────────────────────────────────
        public int   hitLayerMask        = ~0;     // 2点配線・なぞり描画の「当たる対象」
        public bool  ignoreBackfaces     = true;   // 2点配線・なぞり描画: 面の裏側には当たらない
        public float pickTangentScale    = 1f;     // 2点配線のハンドルの長さ
        public float drawSurfaceOffset   = 0.003f; // なぞり: 面からの浮き (m)
        public float drawSimplifyTol     = 0.01f;  // なぞり: 簡略化の許容誤差 (m)
        public float drawMinPixelSpacing = 4f;     // なぞり: サンプル間隔 (px)
        public int   drawKnotMode        = 0;      // 0 = 自動, 1 = 滑らか

        // ─── 配線 › 制御点を増やす ─────────────────────────────────────
        public int addedKnotMode = 0;              // 0 = 自動, 1 = 滑らか, 2 = 角
        public int divisionCount = 4;

        // ─── 配線 › 面に吸着 ───────────────────────────────────────────
        public int     snapDirectionMode = 0;      // 0 = 下, 1 = 上, 2 = 指定
        public Vector3 snapCustomDirection = Vector3.down;
        public bool    snapDirectionIsLocal = false;
        public float   snapMaxDistance = 10f;
        public float   snapSurfaceOffset = 0.003f;
        public int     snapLayerMask = ~0;
        public bool    snapIgnoreBackfaces = true;

        // ─── 形 ────────────────────────────────────────────────────────
        public bool useGlobalKnotCoords = true;

        // ─── 見た目 ────────────────────────────────────────────────────
        public string profileFolder = DefaultProfileFolder;

        // ─── 出力 ──────────────────────────────────────────────────────
        public string outputFolder = "";           // 空なら CableMeshExporter.DefaultOutputFolder
        public int    exportFormat = 1;            // 0 = メッシュ (.asset), 1 = FBX, 2 = Prefab (LOD 付き)
        public int    fbxLodTier = 0;
        public bool   fbxIncludeBones = true;
        public bool   placeInScene = true;

        public void Save() => Save(true);

        public Vector3 SnapDirection(Transform cable)
        {
            Vector3 dir = snapDirectionMode switch
            {
                0 => Vector3.down,
                1 => Vector3.up,
                _ => snapCustomDirection,
            };
            return snapDirectionMode == 2 && snapDirectionIsLocal && cable != null ? cable.TransformDirection(dir) : dir;
        }

        /// <summary>⋮「設定を初期値に戻す…」。ケーブルとプロファイルの値には触れない。</summary>
        public void ResetToDefaults()
        {
            // ScriptableSingleton は 2 つ目のインスタンスを作るとエラーになるので、既定値を直接入れる
            hitLayerMask = ~0;
            ignoreBackfaces = true;
            pickTangentScale = 1f;
            drawSurfaceOffset = 0.003f;
            drawSimplifyTol = 0.01f;
            drawMinPixelSpacing = 4f;
            drawKnotMode = 0;
            addedKnotMode = 0;
            divisionCount = 4;
            snapDirectionMode = 0;
            snapCustomDirection = Vector3.down;
            snapDirectionIsLocal = false;
            snapMaxDistance = 10f;
            snapSurfaceOffset = 0.003f;
            snapLayerMask = ~0;
            snapIgnoreBackfaces = true;
            useGlobalKnotCoords = true;
            profileFolder = DefaultProfileFolder;
            outputFolder = "";
            exportFormat = 1;
            fbxLodTier = 0;
            fbxIncludeBones = true;
            placeInScene = true;
            Save();
        }
    }
}
