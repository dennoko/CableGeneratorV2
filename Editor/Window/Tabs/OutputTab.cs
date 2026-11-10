using System;
using UnityEngine;
using UnityEngine.UIElements;
using CableGeneratorV2Runtime;
using static CableGeneratorV2Editor.CableL10n;

namespace CableGeneratorV2Editor
{
    /// <summary>出力タブ — 軽くして書き出す（設計書 §5.4）。書き出しはフッターの「出力」。</summary>
    internal sealed class OutputTab : CableTab
    {
        protected override string UxmlGuid => "e86d2f1f6d994c0dbf09edd0718e3936";   // OutputTab.uxml

        private VisualElement _lodContent, _legacyBones, _fbxOptions, _boneCount;
        private Label _lodInfo, _bonesHint, _meshHint, _prefabHint;
        private Toggle _bones, _place;
        private Action<int> _format, _fbxLod;

        protected override void Initialize()
        {
            var s = Settings;

            _lodContent = Q("lod-content");
            BindToggleSection(Q<Toggle>("lod-toggle"), _lodContent);
            _lodInfo = Q<Label>("lod-info");
            _legacyBones = Q("legacy-bones");
            OnClick("legacy-bones-remove", gen => Report(CableCommands.RemoveLegacyBones(gen)));

            _format = Choice("format", i => { s.exportFormat = i; s.Save(); });
            _fbxOptions = Q("fbx-options");
            _fbxLod = Choice("fbx-lod", i => { s.fbxLodTier = i; s.Save(); });
            _bones = Q<Toggle>("fbx-bones");
            BindSetting(_bones, () => s.fbxIncludeBones, v => { s.fbxIncludeBones = v; Window.RefreshAll(); });
            _boneCount = Q("bone-count");
            _bonesHint = Q<Label>("bones-hint");
            _place = Q<Toggle>("place");
            BindSetting(_place, () => s.placeInScene, v => { s.placeInScene = v; Window.RefreshAll(); });
            _meshHint = Q<Label>("mesh-hint");
            _prefabHint = Q<Label>("prefab-hint");
        }

        public override void Refresh()
        {
            var gen = Gen;
            var s = Settings;
            Root.SetEnabled(gen != null);

            if (gen != null)
            {
                _lodContent.parent.EnableInClassList("dennoko-toggle-section--collapsed", !gen.enableLod);
                _lodInfo.text = Tr("output.lod.info",
                    CableCommands.TriangleCount(gen.GeneratedMesh).ToString("N0"),
                    CableCommands.TriangleCount(gen.GeneratedLod1Mesh).ToString("N0"),
                    CableCommands.TriangleCount(gen.GeneratedLod2Mesh).ToString("N0"));
                Show(_legacyBones, CableCommands.HasLegacyBones(gen));
            }

            bool mesh = s.exportFormat == 0, fbx = s.exportFormat == 1, prefab = s.exportFormat == 2;
            _format(s.exportFormat);
            Show(_fbxOptions, fbx);
            Show(_place, !mesh);           // メッシュ (.asset) は常にシーンに置く
            Show(_meshHint, mesh);
            Show(_prefabHint, prefab);
            _fbxLod(s.fbxLodTier);
            SetValueIfChanged(_bones, s.fbxIncludeBones);
            SetValueIfChanged(_place, s.placeInScene);
            Show(_boneCount, s.fbxIncludeBones);
            Show(_bonesHint, s.fbxIncludeBones);
        }
    }
}
