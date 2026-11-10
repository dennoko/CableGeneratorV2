using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using CableGeneratorV2Runtime;

namespace CableGeneratorV2Editor
{
    /// <summary>
    /// Cable Generator の Inspector（設計書 §7）。ふだんは畳んでおき、開いたときだけ情報を見て少し調整する役割。
    ///   細いヘッダー（バージョン・言語・マニュアル・設定）→ 情報行 → プロファイル・太さ → 「Cable Generator を開く」→ ▶ すべての項目
    /// 配線・投影・出力などの操作は置かない（ツールウィンドウで行う）。
    /// シーンのハンドルは CableSceneHandles（duringSceneGui）が描くので、ここに OnSceneGUI は無い（畳んでいても動くように）。
    /// </summary>
    [CustomEditor(typeof(CableGenerator))]
    public class CableGeneratorInspector : Editor
    {
        private const string UXML_GUID = "227265c4d1a1482588ecd226035c35dd";   // CableGeneratorInspector.uxml
        private const string THEME_USS_GUID  = "51d8cead3e6147d294b612e96f8079d5";
        private const string BLOCKS_USS_GUID = "60a6f3265e9d4f779690c64f2d77bd3b";
        private const string WINDOW_USS_GUID = "2b5462839a0c40ce8c1a8c29b3e5ff1c";

        private Label _info;

        private void OnEnable()
        {
            // プロファイル未設定ならデフォルトを割り当てる。
            // アセット生成を伴うので Editor.OnEnable 中には行わず遅延し、Prefab アセットには適用しない。
            var gen = target as CableGenerator;
            if (gen != null && gen.profile == null && !EditorUtility.IsPersistent(gen))
            {
                EditorApplication.delayCall += () =>
                {
                    if (gen == null || gen.profile != null) return;
                    var defaultProfile = CableGeneratorCreationMenu.FindOrCreateDefaultProfile();
                    if (defaultProfile == null) return;
                    gen.profile = defaultProfile;
                    EditorUtility.SetDirty(gen);
                    gen.RequestRefresh();
                };
            }
        }

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            root.AddToClassList("dennoko-root");
            root.AddToClassList("dennoko-inspector-root");
            AddStyle(root, THEME_USS_GUID);
            AddStyle(root, BLOCKS_USS_GUID);
            AddStyle(root, WINDOW_USS_GUID);
            DennokoUIFont.Apply(root);

            string path = AssetDatabase.GUIDToAssetPath(UXML_GUID);
            var tree = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
            if (tree == null)
            {
                root.Add(new Label($"UXML not found (GUID {UXML_GUID})."));
                return root;
            }
            tree.CloneTree(root);

            var gen = (CableGenerator)target;
            new CableHeader(root, menu =>
            {
                menu.AddItem(new GUIContent(CableL10n.Tr("menu.rebuild")), false, () => gen.RebuildMesh());
            }, () => CableToolSettings.instance.ResetToDefaults());

            root.Q<Button>("open-window").clicked += () => CableGeneratorWindow.Open(gen);
            DennokoBlockUI.BindDetails(root.Q("all-details"));
            InspectorElement.FillDefaultInspector(root.Q("all-fields"), serializedObject, this);

            _info = root.Q<Label>("info");
            root.TrackSerializedObjectValue(serializedObject, _ => RefreshInfo());
            root.schedule.Execute(RefreshInfo).Every(500);   // メッシュの再生成（非同期）後のポリゴン数

            void Relocalize()
            {
                CableL10n.Localize(root);
                RefreshInfo();
            }
            root.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                CableL10n.Changed -= Relocalize;
                CableL10n.Changed += Relocalize;
                root.schedule.Execute(() => CableL10n.Localize(root));   // パネルに付いてから作られる PropertyField のラベル
            });
            root.RegisterCallback<DetachFromPanelEvent>(_ => CableL10n.Changed -= Relocalize);

            root.Bind(serializedObject);
            CableL10n.Localize(root);
            RefreshInfo();
            return root;
        }

        private void RefreshInfo()
        {
            var gen = target as CableGenerator;
            if (_info == null || gen == null) return;
            string lod = gen.enableLod ? " · LOD ON" : string.Empty;
            string text = CableCommands.SummaryText(gen) + lod;
            if (_info.text != text) _info.text = text;
        }

        private static void AddStyle(VisualElement root, string guid)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var sheet = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
            if (sheet != null) root.styleSheets.Add(sheet);
        }
    }
}
