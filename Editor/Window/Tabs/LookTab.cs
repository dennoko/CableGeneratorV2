using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using CableGeneratorV2Runtime;
using static CableGeneratorV2Editor.CableL10n;

namespace CableGeneratorV2Editor
{
    /// <summary>見た目タブ — 断面プロファイルとメッシュ（設計書 §5.3 / §5.3.1）。</summary>
    internal sealed class LookTab : CableTab
    {
        protected override string UxmlGuid => "a4f805611f414736bf55c90d86c882eb";   // LookTab.uxml

        private ProfilePreviewElement _preview;
        private Label _previewMessage, _previewInfo, _profileInfo, _settingsTitle;
        private VisualElement _grid, _inspectorHost, _meshChips, _angle, _resolution;
        private Button _duplicate;
        private Action<int> _split;
        private SerializedObject _so;

        private List<CableProfile> _profiles;
        private readonly List<(Button tile, ProfilePreviewElement thumb, CableProfile profile)> _tiles = new List<(Button, ProfilePreviewElement, CableProfile)>();
        private bool _profilesDirty = true;
        private CableProfile _inspected;
        private string _meshChipsKey;

        protected override void Initialize()
        {
            var host = Q("preview");
            _preview = new ProfilePreviewElement { LineWidth = 2f, Padding = 14f };
            _preview.style.flexGrow = 1;
            host.Insert(0, _preview);
            _previewMessage = Q<Label>("preview-message");
            _previewInfo = Q<Label>("preview-info");

            _grid = Q("profile-grid");
            _profileInfo = Q<Label>("profile-info");
            _settingsTitle = Q<Label>("profile-settings-title");
            _inspectorHost = Q("profile-inspector");

            var create = Q<Button>("profile-new");
            create.clicked += () => ShowCreateMenu(create);
            _duplicate = OnClick("profile-duplicate", gen =>
            {
                var result = CableCommands.DuplicateProfile(gen);
                Window.ReportSaved(result, gen.profile != null ? AssetDatabase.GetAssetPath(gen.profile) : null);
                _profilesDirty = true;
            });

            _split = Choice("split-mode", i => SetProperty("useAdaptiveSampling", p => p.boolValue = i == 0));
            _angle = Q("prop-angle");
            _resolution = Q("prop-resolution");
            DennokoBlockUI.BindDetails(Q("mesh-details"));
            _meshChips = Q("mesh-chips");
            var reset = DennokoIcons.Apply(Q<Button>("mesh-reset"), DennokoIcons.Refresh, "↺");
            reset.clicked += ResetMesh;

            // プロジェクトのアセットが変わったら（プロファイルの追加・削除・名前変更）一覧を作り直す
            Root.RegisterCallback<AttachToPanelEvent>(_ => EditorApplication.projectChanged += MarkProfilesDirty);
            Root.RegisterCallback<DetachFromPanelEvent>(_ => EditorApplication.projectChanged -= MarkProfilesDirty);
        }

        private void MarkProfilesDirty()
        {
            _profilesDirty = true;
            Window.RefreshAll();
        }

        public override void Bind(CableGenerator gen, SerializedObject so) => _so = so;

        public override void Refresh()
        {
            var gen = Gen;
            Root.SetEnabled(gen != null);
            var profile = gen != null ? gen.profile : null;

            // プレビュー
            _preview.Profile = profile;
            _preview.MarkDirtyRepaint();
            Show(_previewMessage, profile == null);
            _previewInfo.text = profile == null ? string.Empty
                : Tr("look.preview.info", profile.GetVertices().Length, (profile.GetMaxRadius() * 2f * gen.radiusScale * 1000f).ToString("0.#"), CableCommands.ProfileTypeName(profile));

            RefreshGrid(profile);
            _duplicate.SetEnabled(profile != null);
            _profileInfo.text = profile == null ? Tr("look.profile.none")
                : Tr("look.profile.info", profile.name, CableCommands.CountProfileUsers(profile));

            // 選んだプロファイルの値（種類ごとの項目は InspectorElement が並べる）
            if (_inspected != profile)
            {
                _inspected = profile;
                _inspectorHost.Clear();
                if (profile != null)
                {
                    var inspector = new InspectorElement(profile);
                    _inspectorHost.Add(inspector);
                    // スクリプト欄は要らない（InspectorElement は付けてから項目を作るので 1 フレーム後に外す）
                    inspector.schedule.Execute(() => inspector.Q<PropertyField>("PropertyField:m_Script")?.RemoveFromHierarchy());
                }
            }
            _settingsTitle.text = profile == null ? string.Empty : Tr("look.profile.settings", CableCommands.ProfileTypeName(profile));

            // 太さと細かさ
            if (gen != null)
            {
                _split(gen.useAdaptiveSampling ? 0 : 1);
                Show(_angle, gen.useAdaptiveSampling);
                Show(_resolution, !gen.useAdaptiveSampling);
                string key = $"{English}|{gen.uvTiling}";
                if (key != _meshChipsKey)
                {
                    _meshChipsKey = key;
                    _meshChips.Clear();
                    if (!Mathf.Approximately(gen.uvTiling, 1f)) _meshChips.Add(DennokoBlockUI.Chip(null, $"UV ×{gen.uvTiling:0.##}", Tr("look.uv")));
                }
            }
        }

        private void RefreshGrid(CableProfile current)
        {
            if (_profilesDirty || _profiles == null)
            {
                _profilesDirty = false;
                _profiles = CableCommands.FindAllProfiles();
                _grid.Clear();
                _tiles.Clear();
                foreach (var p in _profiles)
                {
                    var profile = p;
                    var tile = new Button(() =>
                    {
                        if (Gen == null) return;
                        CableCommands.AssignProfile(Gen, profile);
                        Window.RefreshAll();
                    }) { tooltip = $"{profile.name}\n{CableCommands.ProfileTypeName(profile)}" };
                    tile.AddToClassList("cable-profile-tile");
                    var thumb = new ProfilePreviewElement { Profile = profile, LineWidth = 1f, Padding = 4f };
                    thumb.AddToClassList("cable-profile-tile__thumb");
                    tile.Add(thumb);
                    var name = new Label(profile.name) { pickingMode = PickingMode.Ignore };
                    name.AddToClassList("cable-profile-tile__name");
                    tile.Add(name);
                    _grid.Add(tile);
                    _tiles.Add((tile, thumb, profile));
                }
            }

            foreach (var (tile, thumb, profile) in _tiles)
            {
                if (profile == null) { _profilesDirty = true; continue; }   // 削除されたアセット
                tile.EnableInClassList(DennokoBlockUI.ActiveClass, profile == current);
                thumb.MarkDirtyRepaint();
            }
        }

        private void ShowCreateMenu(VisualElement anchor)
        {
            var menu = new GenericMenu();
            foreach (var (key, type) in CableCommands.ProfileTypes)
            {
                var t = type;
                string name = Tr(key);
                menu.AddItem(new GUIContent(name), false, () =>
                {
                    if (Gen == null) return;
                    var result = CableCommands.CreateProfile(Gen, t, name);
                    _profilesDirty = true;
                    Window.ReportSaved(result, Gen.profile != null ? AssetDatabase.GetAssetPath(Gen.profile) : null);
                });
            }
            menu.DropDown(anchor.worldBound);
        }

        private void SetProperty(string path, Action<SerializedProperty> set)
        {
            if (_so == null) return;
            _so.Update();
            var prop = _so.FindProperty(path);
            if (prop == null) return;
            set(prop);
            _so.ApplyModifiedProperties();
        }

        /// <summary>太さと細かさのカードだけを初期値に戻す（Undo で戻せるので確認は出さない）。</summary>
        private void ResetMesh()
        {
            if (_so == null) return;
            _so.Update();
            _so.FindProperty("radiusScale").floatValue = 1f;
            _so.FindProperty("useAdaptiveSampling").boolValue = true;
            _so.FindProperty("adaptiveAngleError").floatValue = 6f;
            _so.FindProperty("resolution").intValue = 32;
            _so.FindProperty("uvTiling").floatValue = 1f;
            _so.ApplyModifiedProperties();
            Window.RefreshAll();
        }
    }
}
