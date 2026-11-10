using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using CableGeneratorV2Runtime;

namespace CableGeneratorV2Editor
{
    internal sealed partial class CableGeneratorWindow
    {
        // ─── 対象の決め方（§3.2） ───────────────────────────────────────

        public void SetTarget(CableGenerator gen, bool force = false)
        {
            if (gen != null && EditorUtility.IsPersistent(gen)) gen = null;   // Prefab アセットは編集しない
            if (gen == _target && !force) return;
            if (gen != _target && CableSceneHandles.IsWorking(_target)) CableSceneHandles.EndWork();
            _target = gen;
            if (_built) Rebind();
        }

        private void OnSelectionChanged()
        {
            if (_pinned || CableSceneHandles.AnyWork) { RefreshTarget(); return; }   // 作業中は選択を変えても対象を変えない
            var go = Selection.activeGameObject;
            ClearCandidates();
            if (go == null || EditorUtility.IsPersistent(go)) { RefreshTarget(); return; }

            var gen = go.GetComponent<CableGenerator>();
            if (gen != null) { SetTarget(gen); return; }

            // 子孫にケーブルがあれば候補を出す（対象は変えない）
            foreach (var child in go.GetComponentsInChildren<CableGenerator>(true))
            {
                var candidate = child;
                var button = new Button(() => { SetTarget(candidate, true); ClearCandidates(); }) { text = candidate.name };
                button.AddToClassList("dennoko-candidate");
                _candidates?.Add(button);
            }
            RefreshTarget();
        }

        private void ClearCandidates() => _candidates?.Clear();

        private void OnHierarchyChanged()
        {
            // 対象が削除されたら空の状態にする（== null は破棄済みも含む）
            if (!ReferenceEquals(_target, null) && _target == null) SetTarget(null, true);
            else RefreshAll();
        }

        private void OnProfileChanged(CableProfile profile)
        {
            if (_target != null && _target.profile == profile) RefreshAll();
        }

        private void OnLanguageChanged()
        {
            if (!_built) return;
            CableL10n.Localize(rootVisualElement);
            RefreshAll();
        }

        private void Rebind()
        {
            _tracker?.RemoveFromHierarchy();
            _so = _target != null ? new SerializedObject(_target) : null;

            _tabContent.Unbind();
            if (_so != null) _tabContent.Bind(_so);
            foreach (var tab in _tabs) tab.Bind(_target, _so);

            if (_so != null)
            {
                // ケーブルの値が変わったら（シーン・Inspector・Undo からでも）表示を読み直す
                _tracker = new VisualElement { name = "cable-tracker" };
                _tracker.style.display = DisplayStyle.None;
                rootVisualElement.Add(_tracker);
                _tracker.TrackSerializedObjectValue(_so, _ => RefreshAll());
            }

            // バインドで作られた PropertyField のラベル（#キー）も置き換える。項目は次のフレームに作られることがあるので後でもう一度
            CableL10n.Localize(rootVisualElement);
            rootVisualElement.schedule.Execute(() => CableL10n.Localize(rootVisualElement));
            RefreshAll();
        }

        private void RefreshTarget()
        {
            if (_targetField == null) return;
            _targetField.SetValueWithoutNotify(_target);
            _pinButton.EnableInClassList(DennokoBlockUI.ActiveClass, _pinned);
            DennokoBlockUI.Show(_targetEmpty, _target == null && _candidates.childCount == 0);
            DennokoBlockUI.Show(_candidates, _candidates.childCount > 0);
        }
    }
}
