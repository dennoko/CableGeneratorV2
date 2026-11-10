using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CableGeneratorV2Editor
{
    /// <summary>
    /// SceneView 上の UI (Overlay の中身・HUD・ラジアルメニュー) の共通処理。そのままコピーし、GUID だけ設定する。
    ///   SetupRoot          : ルートにテーマを当てる (テーマ → ブロック → シーンの順に USS を足す。背景色は付けない)
    ///   CloneTree          : UXML を GUID で読み込む
    ///   BindUndoSlider     : スライダーの 1 ドラッグを Undo 1 段にまとめる
    ///   BindImpulseSlider  : 1 回適用スライダー (ドラッグした量だけかけ、離すと確定して 0 に戻る)
    ///   RefreshWhileAttached : パネルに付いている間だけ状態の Changed を購読する
    /// </summary>
    internal static class DennokoSceneUI
    {
        // 配置した USS の .meta に書いてある GUID を設定する
        private const string THEME_USS_GUID  = "51d8cead3e6147d294b612e96f8079d5";
        private const string BLOCKS_USS_GUID = "60a6f3265e9d4f779690c64f2d77bd3b";
        private const string SCENE_USS_GUID  = "4103be721b334a8798f7e71b868feaff";

        public const string ActiveClass = "dennoko-button-active";

        public static void SetupRoot(VisualElement root)
        {
            root.AddToClassList("dennoko-root");
            root.AddToClassList("dennoko-scene");
            AddStyle(root, THEME_USS_GUID);
            AddStyle(root, BLOCKS_USS_GUID);
            AddStyle(root, SCENE_USS_GUID);   // 同じ詳細度のルールを後勝ちで上書きするので最後に足す
            DennokoUIFont.Apply(root);
        }

        /// <summary>Overlay の中身のルートを作る (CreatePanelContent から呼ぶ)。UXML が読めなければ案内の Label を入れて false。</summary>
        public static bool CreateOverlayRoot(string uxmlGuid, out VisualElement root)
        {
            root = new VisualElement();
            SetupRoot(root);
            root.AddToClassList("dennoko-overlay-root");
            return CloneTree(uxmlGuid, root);
        }

        public static bool CloneTree(string uxmlGuid, VisualElement into)
        {
            string path = AssetDatabase.GUIDToAssetPath(uxmlGuid);
            var tree = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
            if (tree == null)
            {
                into.Add(new Label($"UXML が見つかりません (GUID {uxmlGuid})。"));
                return false;
            }
            tree.CloneTree(into);
            return true;
        }

        public static void Show(VisualElement e, bool visible)
        {
            if (e != null) e.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>
        /// スライダーの 1 ドラッグを Undo 1 段にまとめる。apply の中で Undo.RecordObject してから値を変える。
        /// UI Toolkit の Slider はドラッグ中も ChangeEvent を出し続けるので、最初の変更で Undo グループを開き、
        /// ポインタを離したら (PointerUp / PointerCaptureOut、TrickleDown で先に受ける) CollapseUndoOperations する。
        /// </summary>
        public static void BindUndoSlider(BaseSlider<float> slider, string undoName, Action<float> apply)
        {
            if (slider == null) return;
            int group = -1;
            slider.RegisterValueChangedCallback(e =>
            {
                if (group < 0)
                {
                    Undo.IncrementCurrentGroup();
                    Undo.SetCurrentGroupName(undoName);
                    group = Undo.GetCurrentGroup();
                }
                apply(e.newValue);
            });
            void EndDrag()
            {
                if (group < 0) return;
                Undo.CollapseUndoOperations(group);
                group = -1;
            }
            slider.RegisterCallback<PointerUpEvent>(_ => EndDrag(), TrickleDown.TrickleDown);
            slider.RegisterCallback<PointerCaptureOutEvent>(_ => EndDrag(), TrickleDown.TrickleDown);
        }

        /// <summary>
        /// 1 回適用スライダー。ドラッグ中は percent に「40%」を出し、離したら commit(量) を呼んで 0 に戻る。
        /// 数値欄は出さない。選択が無いなど使えないときは slider.SetEnabled(false) + ツールチップで理由を出す。
        /// </summary>
        public static void BindImpulseSlider(BaseSlider<float> slider, Label percent, Action<float> commit)
        {
            if (slider == null) return;
            if (percent != null) percent.text = string.Empty;
            slider.RegisterValueChangedCallback(e =>
            {
                if (percent != null) percent.text = e.newValue > 0f ? $"{Mathf.RoundToInt(e.newValue * 100)}%" : string.Empty;
            });
            void Commit()
            {
                float amount = slider.value;
                if (amount <= 0f) return;
                slider.SetValueWithoutNotify(0f);
                if (percent != null) percent.text = string.Empty;
                commit(amount);
            }
            slider.RegisterCallback<PointerUpEvent>(_ => Commit(), TrickleDown.TrickleDown);
            slider.RegisterCallback<PointerCaptureOutEvent>(_ => Commit(), TrickleDown.TrickleDown);
        }

        /// <summary>
        /// element がパネルに付いている間だけ refresh を購読する。
        /// Overlay の中身はドック移動や SceneView の作り直しのたびに作り直されるので、
        /// 外れたまま購読が残ると作り直すたびに増えていく。
        /// 例: RefreshWhileAttached(panel, Refresh, h => YourSceneToolState.Changed += h, h => YourSceneToolState.Changed -= h);
        /// </summary>
        public static void RefreshWhileAttached(VisualElement element, Action refresh, Action<Action> subscribe, Action<Action> unsubscribe)
        {
            element.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                unsubscribe(refresh);
                subscribe(refresh);
                refresh();
            });
            element.RegisterCallback<DetachFromPanelEvent>(_ => unsubscribe(refresh));
            refresh();
        }

        /// <summary>実行の成功を 2 秒だけ緑の枠で知らせる (アイコンは差し替えない)。</summary>
        public static void FlashSuccess(VisualElement button)
        {
            button.AddToClassList("dennoko-success-frame");
            button.schedule.Execute(() => button.RemoveFromClassList("dennoko-success-frame")).StartingIn(2000);
        }

        private static void AddStyle(VisualElement root, string guid)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var sheet = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
            if (sheet != null) root.styleSheets.Add(sheet);
            else Debug.LogWarning($"[{nameof(DennokoSceneUI)}] USS が見つかりません。GUID を確認してください: {guid}");
        }
    }
}
