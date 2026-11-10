using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using CableGeneratorV2Runtime;
using static CableGeneratorV2Editor.CableL10n;

namespace CableGeneratorV2Editor
{
    /// <summary>
    /// シーン上の直接操作（設計書 §2・§6）。Editor.OnSceneGUI ではなく SceneView.duringSceneGui で動かすので、
    /// Inspector でコンポーネントを畳んでいても球・矢印・矩形選択が使える（§7.2）。
    ///   通常       : 制御点の球（常に表示）・選択中の移動矢印・接線・区間の ＋ / ▼・矩形選択   … .Edit / .RectSelect
    ///   2点配線    : 表面を 2 回クリック                                                   … .Picking
    ///   なぞり描画 : 表面をドラッグ                                                         … .SurfaceDraw
    /// 対象は「作業中のケーブル」→ なければ「Hierarchy で選んでいるケーブル」。
    /// </summary>
    [InitializeOnLoad]
    internal static partial class CableSceneHandles
    {
        /// <summary>2点配線・なぞり描画を始めた / 終えた。</summary>
        public static event Action WorkChanged;

        static CableSceneHandles()
        {
            SceneView.duringSceneGui += OnSceneGui;

            // セッション規約の自動終了: Play モード・シーン切替・再コンパイルの前
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode) EndWork();
            };
            EditorSceneManager.activeSceneChangedInEditMode += (_, __) => EndWork();
            AssemblyReloadEvents.beforeAssemblyReload += EndWork;
        }

        // ─── 作業（2点配線・なぞり描画） ────────────────────────────────

        public static bool IsPicking(CableGenerator gen) => gen != null && s_pickingTarget == gen;
        public static bool IsDrawing(CableGenerator gen) => gen != null && s_drawTarget == gen;
        public static bool IsWorking(CableGenerator gen) => IsPicking(gen) || IsDrawing(gen);
        public static bool AnyWork => s_pickingTarget != null || s_drawTarget != null;

        public static void TogglePicking(CableGenerator gen)
        {
            if (IsPicking(gen)) EndWork();
            else StartPicking(gen);
        }

        public static void ToggleDrawing(CableGenerator gen)
        {
            if (IsDrawing(gen)) EndWork();
            else StartDrawing(gen);
        }

        /// <summary>どちらの作業も終える（Esc・バナーの「終了」・自動終了）。</summary>
        public static void EndWork()
        {
            bool was = AnyWork;
            CancelPicking();
            CancelDrawing();
            if (was) RaiseWorkChanged();
        }

        /// <summary>今の作業の次の操作（作業中バナー・カーソルヒント）。作業していなければ空。</summary>
        public static string GuideText(CableGenerator gen)
        {
            if (IsPicking(gen)) return Tr(s_pickCount == 0 ? "guide.pickStart" : "guide.pickEnd");
            if (IsDrawing(gen)) return Tr(s_isStroking ? "guide.drawRelease" : "guide.drawStart");
            return string.Empty;
        }

        static void RaiseWorkChanged()
        {
            WorkChanged?.Invoke();
            CableSceneToolState.Raise();
            SceneView.RepaintAll();
        }

        // ─── 入口 ───────────────────────────────────────────────────────

        /// <summary>作業中のケーブル（2点配線 / なぞり描画）。作業していなければ null。</summary>
        public static CableGenerator WorkingCable => s_pickingTarget != null ? s_pickingTarget : s_drawTarget;

        static CableGenerator ActiveCable() => CableSceneToolState.Current;

        static void OnSceneGui(SceneView view)
        {
            // 作業中に対象が消えたら終える（== null は破棄済みも含む）
            if ((!ReferenceEquals(s_pickingTarget, null) && s_pickingTarget == null) ||
                (!ReferenceEquals(s_drawTarget, null) && s_drawTarget == null))
            {
                EndWork();
            }

            var gen = ActiveCable();
            if (gen == null || gen.Spline == null || !gen.gameObject.activeInHierarchy) return;

            EnsureStyles();

            if (s_drawTarget == gen) { HandleSurfaceDraw(gen); return; }
            if (s_pickingTarget == gen) { HandlePicking(gen); return; }
            HandleEdit(gen);
        }

        // ─── 描画スタイル ───────────────────────────────────────────────

        static GUIStyle s_indexLabelStyle;
        static GUIStyle s_indexLabelSelectedStyle;
        static GUIStyle s_midpointButtonStyle;

        static void EnsureStyles()
        {
            s_indexLabelStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                fontSize  = 11,
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = new Color(1f, 1f, 1f, 0.85f) },
            };
            s_indexLabelSelectedStyle ??= new GUIStyle(s_indexLabelStyle)
            {
                fontSize = 13,
                normal   = { textColor = DennokoSceneColors.SelectedLine },
            };
            s_midpointButtonStyle ??= new GUIStyle(GUI.skin.button)
            {
                fontSize  = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                padding   = new RectOffset(2, 2, 1, 1),
            };
        }

        static Color KnotColor(CableTangentMode mode) => mode switch
        {
            CableTangentMode.Mirrored   => Color.green,
            CableTangentMode.Continuous => Color.yellow,
            CableTangentMode.Broken     => Color.red,
            CableTangentMode.AutoSmooth => Color.cyan,
            _                           => Color.white,
        };
    }
}
