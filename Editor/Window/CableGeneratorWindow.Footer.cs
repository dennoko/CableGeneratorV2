using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using static CableGeneratorV2Editor.CableL10n;

namespace CableGeneratorV2Editor
{
    internal sealed partial class CableGeneratorWindow
    {
        // ================================================================
        //  作業中バナー（B7）・出力ドック（B5）・フッター・ステータス
        // ================================================================

        private VisualElement _banner;
        private Label _bannerState;
        private Label _bannerTarget;
        private Label _bannerGuide;
        private TextField _outputPath;
        private VisualElement _outputChips;
        private Label _footerInfo;
        private Button _primary;
        private DennokoStatus _status;
        private Label _statusLabel;
        private string _lastSavedPath;
        private string _chipsKey;

        private void InitializeFooter(VisualElement root)
        {
            _banner = root.Q("session-banner");
            _bannerState = root.Q<Label>("session-state");
            _bannerTarget = root.Q<Label>("session-target");
            _bannerGuide = root.Q<Label>("session-guide");
            _bannerTarget.RegisterCallback<ClickEvent>(_ => FrameTarget());
            root.Q<Button>("session-end").clicked += CableSceneHandles.EndWork;

            _outputPath = root.Q<TextField>("output-path");
            _outputChips = root.Q("output-chips");
            root.Q<Button>("output-browse").clicked += BrowseOutputFolder;

            _footerInfo = root.Q<Label>("footer-info");
            _primary = root.Q<Button>("primary-button");
            _primary.clicked += ExportTarget;

            _statusLabel = root.Q<Label>("status-label");
            _status = new DennokoStatus(_statusLabel);
            // 「保存: …」はクリックで Project ビューのファイルを Ping する（§4）
            _statusLabel.RegisterCallback<ClickEvent>(_ =>
            {
                if (string.IsNullOrEmpty(_lastSavedPath)) return;
                var asset = AssetDatabase.LoadMainAssetAtPath(_lastSavedPath);
                if (asset != null) EditorGUIUtility.PingObject(asset);
            });
        }

        /// <summary>ウィンドウから実行した操作の結果をステータスバーに出す（シーンから実行したものはトースト）。</summary>
        public void Report(CableResult result)
        {
            if (_status == null || string.IsNullOrEmpty(result.Message)) return;
            _lastSavedPath = null;
            _status.Set(result.Message, result.Ok ? DennokoStatusType.Success : DennokoStatusType.Warning);
            RefreshAll();
        }

        /// <summary>保存したアセットを Ping できる結果（「保存: …」「複製しました: …」）。</summary>
        public void ReportSaved(CableResult result, string assetPath)
        {
            Report(result);
            if (result.Ok) _lastSavedPath = assetPath;
        }

        private void ExportTarget()
        {
            if (_target == null) return;
            var result = CableCommands.Export(_target);
            ReportSaved(result, CableCommands.LastExportPath);
        }

        private void BrowseOutputFolder()
        {
            var s = CableToolSettings.instance;
            string start = string.IsNullOrEmpty(s.outputFolder) ? "Assets" : s.outputFolder;
            string picked = EditorUtility.OpenFolderPanel(Tr("dock.folder"), start, string.Empty);
            if (string.IsNullOrEmpty(picked)) return;

            string dataPath = Application.dataPath.Replace('\\', '/');
            picked = picked.Replace('\\', '/');
            if (!picked.StartsWith(dataPath))
            {
                _status.Set(Tr("msg.folderInAssets"), DennokoStatusType.Warning);
                return;
            }
            s.outputFolder = "Assets" + picked.Substring(dataPath.Length);
            s.Save();
            RefreshFooter();
        }

        // ─── 表示 ───────────────────────────────────────────────────────

        private void RefreshBanner()
        {
            bool working = CableSceneHandles.IsWorking(_target);
            DennokoBlockUI.Show(_banner, working);
            if (!working) return;
            _bannerState.text = Tr(CableSceneHandles.IsPicking(_target) ? "banner.picking" : "banner.drawing");
            _bannerTarget.text = _target.name;
            _bannerGuide.text = CableSceneHandles.GuideText(_target);
        }

        private void RefreshFooter()
        {
            if (!_built) return;
            var s = CableToolSettings.instance;

            string folder = string.IsNullOrEmpty(s.outputFolder) ? CableMeshExporter.DefaultOutputFolder : s.outputFolder;
            if (_outputPath.value != folder) _outputPath.SetValueWithoutNotify(folder);
            RefreshChips(s);

            string info = _target != null ? CableCommands.SummaryText(_target) : string.Empty;
            if (_footerInfo.text != info) _footerInfo.text = info;

            bool hasMesh = CableCommands.HasMesh(_target);
            _primary.SetEnabled(hasMesh);
            _primary.tooltip = _target == null ? Tr("footer.export.noTarget")
                : hasMesh ? Tr(s.exportFormat switch
                    {
                        0 => "footer.export.mesh.tip",
                        2 => "footer.export.prefab.tip",
                        _ => "footer.export.fbx.tip",
                    })
                : Tr("msg.noMesh");
        }

        /// <summary>出力形式の要約チップ（FBX · LOD0 · 🦴12 · 配置）。クリックで出力タブへ。</summary>
        private void RefreshChips(CableToolSettings s)
        {
            bool fbx = s.exportFormat == 1;
            int bones = _target != null ? _target.boneCount : 0;
            string key = $"{CableL10n.English}|{s.exportFormat}|{s.fbxLodTier}|{s.fbxIncludeBones}|{bones}|{s.placeInScene}";
            if (key == _chipsKey) return;
            _chipsKey = key;

            _outputChips.Clear();
            void Add(string text, string tip) =>
                _outputChips.Add(DennokoBlockUI.Chip(null, text, tip, () => SelectTab(TabOutput)));

            bool mesh = s.exportFormat == 0;
            Add(Tr(s.exportFormat switch
            {
                0 => "output.format.mesh",
                2 => "output.format.prefab",
                _ => "output.format.fbx",
            }), Tr("dock.chip.format.tip"));
            if (fbx)
            {
                Add("LOD" + s.fbxLodTier, Tr("dock.chip.lod.tip"));
                if (s.fbxIncludeBones) Add(Tr("dock.chip.bones", bones), Tr("dock.chip.bones.tip"));
            }
            if (mesh || s.placeInScene) Add(Tr("dock.chip.place"), Tr("dock.chip.place.tip"));
        }
    }
}
