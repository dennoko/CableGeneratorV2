using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using CableGeneratorV2Runtime;
using static CableGeneratorV2Editor.CableL10n;

namespace CableGeneratorV2Editor
{
    /// <summary>
    /// Cable Generator のツールウィンドウ（設計書 §3〜§5）。ケーブルの作業はこことシーンだけで完結させる。
    ///   .cs        : 開き方・寿命・枠（ヘッダー・タブ）・表示の読み直し
    ///   .Target.cs : 対象の決め方（選択への追従・固定・候補・バインド）
    ///   .Footer.cs : 作業中バナー・出力ドック・フッター・ステータス
    /// 値はコンポーネント（CableGenerator / CableProfile）とツール設定にだけ持ち、UI は SerializedObject にバインドする。
    /// </summary>
    internal sealed partial class CableGeneratorWindow : EditorWindow
    {
        private const string UXML_GUID = "7e58d0351f3041488620d0d7e4314e7c";   // CableGeneratorWindow.uxml
        private const string USS_GUID  = "2b5462839a0c40ce8c1a8c29b3e5ff1c";   // CableGeneratorWindow.uss
        private const string THEME_USS_GUID  = "51d8cead3e6147d294b612e96f8079d5";
        private const string BLOCKS_USS_GUID = "60a6f3265e9d4f779690c64f2d77bd3b";
        private const long PollIntervalMs = 500;   // メッシュの再生成（非同期）後のポリゴン数などを拾う

        public const int TabWire = 0, TabShape = 1, TabLook = 2, TabOutput = 3;

        [SerializeField] private int _tab;
        [SerializeField] private bool _pinned;
        [SerializeField] private CableGenerator _target;

        private CableTab[] _tabs;
        private Button[] _tabButtons;
        private VisualElement _tabContent;
        private SerializedObject _so;
        private VisualElement _tracker;
        private ObjectField _targetField;
        private Button _pinButton;
        private VisualElement _candidates;
        private Label _targetEmpty;
        private bool _built;

        public CableGenerator Target => _target;

        // ─── 開き方（§3.1） ─────────────────────────────────────────────

        [MenuItem("Tools/dennokoworks/Cable Generator", false, 100)]
        private static void ShowWindow() => Open(null);

        [MenuItem("GameObject/Cable Generator V2/Open in Window", false, 11)]
        private static void OpenFromHierarchy()
        {
            var go = Selection.activeGameObject;
            Open(go != null ? go.GetComponentInChildren<CableGenerator>(true) : null);
        }

        public static CableGeneratorWindow Open(CableGenerator target, int tab = -1)
        {
            var window = GetWindow<CableGeneratorWindow>();
            window.titleContent = new GUIContent(CableHeader.ToolName);
            window.minSize = new Vector2(340f, 460f);
            if (target != null) window.SetTarget(target, true);
            if (tab >= 0) window.SelectTab(tab);
            window.Show();
            window.Focus();
            return window;
        }

        // ─── 寿命 ───────────────────────────────────────────────────────

        private void OnEnable()
        {
            Selection.selectionChanged += OnSelectionChanged;
            CableKnotSelection.Changed += RefreshAll;
            CableSceneToolState.Changed += RefreshAll;
            Undo.undoRedoPerformed += RefreshAll;
            CableProfile.AnyProfileChanged += OnProfileChanged;
            CableL10n.Changed += OnLanguageChanged;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
            CableKnotSelection.Changed -= RefreshAll;
            CableSceneToolState.Changed -= RefreshAll;
            Undo.undoRedoPerformed -= RefreshAll;
            CableProfile.AnyProfileChanged -= OnProfileChanged;
            CableL10n.Changed -= OnLanguageChanged;
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
        }

        // ウィンドウを閉じたら作業を終える（セッション規約の自動終了）
        private void OnDestroy() => CableSceneHandles.EndWork();

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.AddToClassList("dennoko-root");
            root.style.backgroundColor = (Color)new Color32(0x12, 0x12, 0x12, 0xFF);   // USS が読めなくても明るくしない
            root.style.flexGrow = 1;
            DennokoUIFont.Apply(root);
            AddStyle(root, THEME_USS_GUID);
            AddStyle(root, BLOCKS_USS_GUID);
            AddStyle(root, USS_GUID);

            string path = AssetDatabase.GUIDToAssetPath(UXML_GUID);
            var tree = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
            if (tree == null)
            {
                root.Add(new Label($"UXML not found (GUID {UXML_GUID})."));
                return;
            }
            tree.CloneTree(root);

            InitializeFrame(root);
            InitializeFooter(root);

            _tabContent = root.Q("tab-content");
            _tabs = new CableTab[] { new WireTab(), new ShapeTab(), new LookTab(), new OutputTab() };
            foreach (var tab in _tabs) tab.Build(_tabContent, this);

            _built = true;
            if (_target == null) OnSelectionChanged();
            Rebind();
            SelectTab(_tab);
            CableL10n.Localize(root);
            root.schedule.Execute(RefreshFooter).Every(PollIntervalMs);
        }

        private static void AddStyle(VisualElement root, string guid)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var sheet = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
            if (sheet != null) root.styleSheets.Add(sheet);
            else Debug.LogWarning($"[{nameof(CableGeneratorWindow)}] USS not found: {guid}");
        }

        // ─── 枠（ヘッダー・対象・タブ） ─────────────────────────────────

        private void InitializeFrame(VisualElement root)
        {
            new CableHeader(root, AddToolMenuItems, () =>
            {
                CableToolSettings.instance.ResetToDefaults();
                Rebind();
                Report(CableResult.Success(Tr("msg.settingsReset")));
            });

            _targetField = root.Q<ObjectField>("target-field");
            _targetField.objectType = typeof(CableGenerator);
            _targetField.allowSceneObjects = true;
            _targetField.RegisterValueChangedCallback(e => SetTarget(e.newValue as CableGenerator, true));

            _pinButton = DennokoIcons.Apply(root.Q<Button>("target-pin"), "d_IN_LockButton on|IN_LockButton on", "📌");
            _pinButton.clicked += () =>
            {
                _pinned = !_pinned;
                RefreshTarget();
                if (!_pinned) OnSelectionChanged();
            };

            root.Q<Button>("target-new").clicked += () =>
            {
                var parent = Selection.activeGameObject;
                if (parent != null && (EditorUtility.IsPersistent(parent) || parent.GetComponent<CableGenerator>() != null)) parent = null;
                var go = CableGeneratorCreationMenu.CreateCable(parent);
                SetTarget(go.GetComponent<CableGenerator>(), true);
                SelectTab(TabWire);
            };

            _candidates = root.Q("target-candidates");
            _targetEmpty = root.Q<Label>("target-empty");

            _tabButtons = new[] { root.Q<Button>("tab-wire"), root.Q<Button>("tab-shape"), root.Q<Button>("tab-look"), root.Q<Button>("tab-output") };
            for (int i = 0; i < _tabButtons.Length; i++)
            {
                int index = i;
                _tabButtons[i].clicked += () => SelectTab(index);
            }
        }

        private void AddToolMenuItems(GenericMenu menu)
        {
            if (_target == null)
            {
                menu.AddDisabledItem(new GUIContent(Tr("menu.rebuild")));
                menu.AddDisabledItem(new GUIContent(Tr("menu.frame")));
                return;
            }
            menu.AddItem(new GUIContent(Tr("menu.rebuild")), false, () => _target.RebuildMesh());
            menu.AddItem(new GUIContent(Tr("menu.frame")), false, FrameTarget);
        }

        public void SelectTab(int index)
        {
            _tab = Mathf.Clamp(index, 0, 3);
            if (!_built) return;
            for (int i = 0; i < _tabs.Length; i++)
            {
                _tabs[i].SetVisible(i == _tab);
                _tabButtons[i].EnableInClassList(DennokoBlockUI.ActiveClass, i == _tab);
            }
            RefreshAll();
        }

        /// <summary>表示をすべて読み直す（値・選択・作業・言語が変わったとき）。</summary>
        public void RefreshAll()
        {
            if (!_built) return;
            if (_target != null && _target.Spline != null) CableKnotSelection.Validate(_target, _target.Spline.Count);
            RefreshTarget();
            RefreshBanner();
            RefreshFooter();
            _tabs[_tab].Refresh();
        }

        private void FrameTarget()
        {
            if (_target == null) return;
            var view = SceneView.lastActiveSceneView;
            var renderer = _target.GetComponent<Renderer>();
            if (view == null) return;
            view.Frame(renderer != null ? renderer.bounds : new Bounds(_target.transform.position, Vector3.one), false);
        }
    }
}
