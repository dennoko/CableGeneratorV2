using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using CableGeneratorV2Runtime;

namespace CableGeneratorV2Editor
{
    /// <summary>
    /// ウィンドウのタブ 1 枚（UXML + 小さいコントローラー。設計書 §12）。
    ///   Initialize : UXML を読み込んだ後に 1 回。ボタンなどを接続する
    ///   Bind       : 対象が変わったとき。PropertyField はウィンドウがまとめて Bind するので、ここではバインドしない値を接続する
    ///   Refresh    : 値・選択・作業・言語が変わったとき（見えているタブだけ）。要素を作り直さず、値と表示だけ更新する
    /// </summary>
    internal abstract class CableTab
    {
        protected VisualElement Root { get; private set; }
        protected CableGeneratorWindow Window { get; private set; }
        protected CableGenerator Gen => Window != null ? Window.Target : null;
        protected static CableToolSettings Settings => CableToolSettings.instance;

        protected abstract string UxmlGuid { get; }

        public void Build(VisualElement container, CableGeneratorWindow window)
        {
            Window = window;
            Root = new VisualElement();
            Root.AddToClassList("cable-tab");
            string path = AssetDatabase.GUIDToAssetPath(UxmlGuid);
            var tree = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
            if (tree == null)
            {
                Root.Add(new Label($"UXML not found (GUID {UxmlGuid})."));
                container.Add(Root);
                return;
            }
            tree.CloneTree(Root);
            container.Add(Root);
            Initialize();
        }

        public void SetVisible(bool visible) => DennokoBlockUI.Show(Root, visible);

        protected abstract void Initialize();
        public virtual void Bind(CableGenerator gen, SerializedObject so) { }
        public abstract void Refresh();

        protected void Report(CableResult result) => Window.Report(result);

        // ─── 接続の小道具 ───────────────────────────────────────────────

        protected T Q<T>(string name) where T : VisualElement => Root.Q<T>(name);
        protected VisualElement Q(string name) => Root.Q(name);

        /// <summary>ボタンを押したら action(対象) を呼び、表示を読み直す。対象が無ければ何もしない。</summary>
        protected Button OnClick(string name, Action<CableGenerator> action)
        {
            var b = Root.Q<Button>(name);
            if (b != null)
            {
                b.clicked += () =>
                {
                    var gen = Gen;
                    if (gen == null) return;
                    action(gen);
                    CableCommands.RepaintAll();
                    Window.RefreshAll();
                };
            }
            return b;
        }

        /// <summary>選択肢グループ（.dennoko-choice-group）。戻り値で外から選択を合わせる。</summary>
        protected Action<int> Choice(string name, Action<int> onSelect) =>
            DennokoBlockUI.BindChoice(Root.Q(name), -1, i =>
            {
                onSelect(i);
                Window.RefreshAll();
            });

        /// <summary>ツール設定の値（バインドしない）を欄につなぐ。変えたら保存する。</summary>
        protected void BindSetting<TValue>(BaseField<TValue> field, Func<TValue> get, Action<TValue> set)
        {
            if (field == null) return;
            field.SetValueWithoutNotify(get());
            field.RegisterValueChangedCallback(e =>
            {
                set(e.newValue);
                Settings.Save();
            });
        }

        protected static void SetValueIfChanged<TValue>(BaseField<TValue> field, TValue value)
        {
            if (field != null && !Equals(field.value, value)) field.SetValueWithoutNotify(value);
        }

        protected static void Show(VisualElement e, bool visible) => DennokoBlockUI.Show(e, visible);

        /// <summary>トグル付きセクション（見出しのトグル OFF で中身を折りたたむ。スキル BindToggleSection と同じ）。</summary>
        protected static void BindToggleSection(Toggle toggle, VisualElement content)
        {
            if (toggle == null || content == null) return;
            var section = content.parent;
            toggle.RegisterValueChangedCallback(e => section.EnableInClassList("dennoko-toggle-section--collapsed", !e.newValue));
            section.EnableInClassList("dennoko-toggle-section--collapsed", !toggle.value);
        }

        /// <summary>LayerMaskField（int のマスク）をツール設定につなぐ。</summary>
        protected void BindLayerMask(LayerMaskField field, Func<int> get, Action<int> set)
        {
            if (field == null) return;
            field.SetValueWithoutNotify(get());
            field.RegisterValueChangedCallback(e =>
            {
                set(e.newValue);
                Settings.Save();
            });
        }

        protected static Vector3 Round(Vector3 v, int digits = 4) =>
            new Vector3((float)Math.Round(v.x, digits), (float)Math.Round(v.y, digits), (float)Math.Round(v.z, digits));
    }
}
