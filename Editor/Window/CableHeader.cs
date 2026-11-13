using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine.UIElements;
using static CableGeneratorV2Editor.CableL10n;

namespace CableGeneratorV2Editor
{
    /// <summary>
    /// 標準ヘッダー（設計書 §4.1〜§4.4）の接続。ウィンドウと Inspector の細いヘッダーで共有する。
    ///   バージョン + ↻ : CableGeneratorVersion（6 時間に 1 回まで。↻ は即時）。更新ありでもリンクは開かない
    ///   言語          : CableL10n.SetEnglish。開いている UI はすべて CableL10n.Changed で読み直す
    ///   マニュアル ?   : 同梱の Docs/user_manual_ja.pdf / user_manual_en.pdf（今の言語 → 無ければもう一方）
    ///   設定 ⋮        : 上段 = toolMenu、下段 = 「設定を初期値に戻す…」「更新を確認」
    /// </summary>
    internal sealed class CableHeader
    {
        public const string ToolName = "Cable Generator";

        private static readonly Dictionary<string, string> BuiltInKeys = new Dictionary<string, string>
        {
            { "更新を確認", "appbar.checkUpdate" },
            { "マニュアルを開く", "appbar.manual.tip" },
            { "設定", "appbar.settings" },
            { "設定を初期値に戻す…", "appbar.reset" },
            { "すべての設定を初期値に戻しますか？", "appbar.reset.confirm" },
            { "戻す", "appbar.reset.ok" },
            { "キャンセル", "common.cancel" },
        };

        private readonly DennokoAppBar _bar;
        private readonly Label _version;
        private readonly Button _manual;

        public CableHeader(VisualElement root, Action<GenericMenu> toolMenu, Action resetAll)
        {
            _bar = new DennokoAppBar(root, ToolName, English)
            {
                Translate = s => BuiltInKeys.TryGetValue(s, out var key) ? Tr(key) : s,
                AddToolMenuItems = toolMenu,
                ResetAll = resetAll,
                ReleaseUrl = null,   // 外部リンクは置かない（§4.2）
            };
            _bar.CheckUpdateRequested += () =>
            {
                CableGeneratorVersion.ForceRecheck();
                ApplyVersion();
            };
            _bar.LanguageChanged += SetEnglish;

            _version = root.Q<Label>("version-label");
            _manual = root.Q<Button>("appbar-manual");

            // パネルに付いている間だけ購読する（Inspector は作り直されるので、外れたまま残ると増えていく）
            root.RegisterCallback<AttachToPanelEvent>(_ => Subscribe(true));
            root.RegisterCallback<DetachFromPanelEvent>(_ => Subscribe(false));
            if (root.panel != null) Subscribe(true);

            ApplyLanguage();
            CableGeneratorVersion.StartCheckBackgroundTask();
        }

        private void Subscribe(bool on)
        {
            CableGeneratorVersion.Changed -= ApplyVersion;
            CableL10n.Changed -= ApplyLanguage;
            if (!on) return;
            CableGeneratorVersion.Changed += ApplyVersion;
            CableL10n.Changed += ApplyLanguage;
        }

        private void ApplyLanguage()
        {
            _bar.SetEnglish(English);
            _bar.RefreshTexts();
            ApplyManual();
            ApplyVersion();
        }

        // ─── マニュアル ─────────────────────────────────────────────────

        /// <summary>今の言語のマニュアル（無ければもう一方の言語）の絶対パス。どちらも無ければ null。</summary>
        public static string ManualPath()
        {
            string root = CableGeneratorVersion.RootFolder();
            if (string.IsNullOrEmpty(root)) return null;
            string firstPdf = English ? "user_manual_en.pdf" : "user_manual_ja.pdf";
            string secondPdf = English ? "user_manual_ja.pdf" : "user_manual_en.pdf";
            string firstMd = English ? "user_manual_en.md" : "user_manual_ja.md";
            string secondMd = English ? "user_manual_ja.md" : "user_manual_en.md";
            foreach (string name in new[] { firstPdf, secondPdf, firstMd, secondMd })
            {
                string path = Path.Combine(root, "Docs", name);
                if (File.Exists(path)) return path;
            }
            return null;
        }

        private void ApplyManual()
        {
            string path = ManualPath();
            // DennokoAppBar は ManualUrl を Application.OpenURL で開く。file:// で同梱の PDF を OS の既定アプリに渡す
            _bar.ManualUrl = path != null ? new Uri(path).AbsoluteUri : null;
            if (_manual == null) return;
            _manual.SetEnabled(path != null);
            _manual.tooltip = Tr(path != null ? "appbar.manual.tip" : "appbar.manual.missing");
        }

        // ─── バージョン ─────────────────────────────────────────────────

        private void ApplyVersion()
        {
            if (_version == null) return;
            var r = CableGeneratorVersion.LoadResult();
            string v = r.LocalVersion ?? "";
            string text = (v.StartsWith("v", System.StringComparison.OrdinalIgnoreCase) ? "" : "v") + v;
            bool update = false, error = false;
            string tooltip = null;
            switch (r.State)
            {
                case DennokoVersionChecker.State.UpdateAvailable:
                    text += "  " + Tr("version.update", r.LatestVersion);
                    tooltip = Tr("version.update.tip", r.LatestVersion) + (string.IsNullOrEmpty(r.Message) ? "" : "\n" + r.Message);
                    update = true;
                    break;
                case DennokoVersionChecker.State.Error:
                    text += "  " + Tr("version.error");
                    error = true;
                    break;
                case DennokoVersionChecker.State.Checking:
                    text += "  " + Tr("version.checking");
                    break;
            }
            _version.text = text;
            _version.EnableInClassList("dennoko-version-label--update", update);
            _version.EnableInClassList("dennoko-version-label--error", error);
            _bar.SetVersionClickable(false, tooltip);   // 更新ありでもリンクは開かない
        }
    }
}
