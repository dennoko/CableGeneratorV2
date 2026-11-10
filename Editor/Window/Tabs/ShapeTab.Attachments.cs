using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using CableGeneratorV2Runtime;
using static CableGeneratorV2Editor.CableL10n;

namespace CableGeneratorV2Editor
{
    /// <summary>
    /// 制御点タブの「アタッチメント」カード。選択中の制御点に付いているアタッチメントを 1 件ずつ編集する。
    /// 項目は Inspector（CableKnotAttachmentInspector）と同じ: 制御点番号・Prefab / FBX・位置／回転オフセット・スケール・更新。
    /// 制御点を選んでいないときは、ケーブルに付いている全アタッチメントをチップで出し、クリックでその制御点を選ぶ。
    /// </summary>
    internal sealed partial class ShapeTab
    {
        private const string AttachmentItemUxmlGuid = "11274f08d6fc4cf688005626f8d28d2e";   // AttachmentItem.uxml

        private VisualTreeAsset _attachmentItem;
        private Button _attachAdd;
        private Label _attachHint;
        private VisualElement _attachOthers, _attachList;
        private string _attachKey;
        private readonly List<AttachmentRow> _attachRows = new List<AttachmentRow>();

        private sealed class AttachmentRow
        {
            public CableKnotAttachment attachment;
            public Label name;
            public IntegerField knot;
            public ObjectField prefab;
            public Vector3Field position, rotation, scale;
        }

        private void InitializeAttachments()
        {
            string path = AssetDatabase.GUIDToAssetPath(AttachmentItemUxmlGuid);
            _attachmentItem = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);

            _attachHint = Q<Label>("attach-hint");
            _attachOthers = Q("attach-others");
            _attachList = Q("attach-list");
            _attachAdd = OnClick("attach-add", gen =>
            {
                if (SingleIndex >= 0) CableCommands.AddAttachment(gen, SingleIndex);
            });
        }

        private void RefreshAttachments(CableGenerator gen, List<int> selected)
        {
            int index = selected.Count == 1 ? selected[0] : -1;
            var all = CableCommands.AttachmentsOf(gen);
            var mine = new List<CableKnotAttachment>();
            if (index >= 0)
                foreach (var a in all)
                    if (a.knotIndex == index) mine.Add(a);

            _attachAdd.SetEnabled(index >= 0);

            string hint = index < 0 ? Tr(all.Count == 0 ? "attach.hint.select" : "attach.hint.pick")
                        : mine.Count == 0 ? Tr("attach.hint.none")
                        : string.Empty;
            _attachHint.text = hint;
            Show(_attachHint, hint.Length > 0);

            // 件数・並び・番号が変わったときだけ作り直す（値は下で毎回合わせる）
            var key = new StringBuilder().Append(CableL10n.English).Append('|').Append(index);
            foreach (var a in index < 0 ? all : mine)
                key.Append('|').Append(a.GetInstanceID()).Append(':').Append(a.knotIndex).Append(':').Append(a.prefab != null ? a.prefab.name : "-");
            string k = key.ToString();
            if (k != _attachKey)
            {
                _attachKey = k;
                RebuildAttachmentChips(gen, index < 0 ? all : null);
                RebuildAttachmentRows(gen, mine);
            }

            foreach (var row in _attachRows)
            {
                var a = row.attachment;
                if (a == null) continue;
                row.name.text = AttachmentName(a);
                if (!IsEditing(row.knot)) SetValueIfChanged(row.knot, a.knotIndex);
                SetValueIfChanged<Object>(row.prefab, a.prefab);
                if (!IsEditing(row.position)) SetValueIfChanged(row.position, a.positionOffset);
                if (!IsEditing(row.rotation)) SetValueIfChanged(row.rotation, a.rotationOffset);
                if (!IsEditing(row.scale)) SetValueIfChanged(row.scale, a.scale);
            }
        }

        private void RebuildAttachmentChips(CableGenerator gen, List<CableKnotAttachment> attachments)
        {
            _attachOthers.Clear();
            Show(_attachOthers, attachments != null && attachments.Count > 0);
            if (attachments == null) return;
            foreach (var a in attachments)
            {
                int knot = a.knotIndex;
                _attachOthers.Add(DennokoBlockUI.Chip(null, $"[{knot}] {AttachmentName(a)}", Tr("attach.chip.tip"),
                    () => CableKnotSelection.SelectSingle(gen, knot)));
            }
        }

        private void RebuildAttachmentRows(CableGenerator gen, List<CableKnotAttachment> attachments)
        {
            _attachList.Clear();
            _attachRows.Clear();
            if (_attachmentItem == null)
            {
                if (attachments.Count > 0) _attachList.Add(new Label($"UXML not found (GUID {AttachmentItemUxmlGuid})."));
                return;
            }

            for (int i = 0; i < attachments.Count; i++)
                _attachList.Add(CreateAttachmentRow(gen, attachments[i], i > 0));
            CableL10n.Localize(_attachList);
        }

        private VisualElement CreateAttachmentRow(CableGenerator gen, CableKnotAttachment attachment, bool divided)
        {
            var item = _attachmentItem.Instantiate();
            item.Q(className: "cable-attachment")?.EnableInClassList("cable-attachment--divided", divided);

            var row = new AttachmentRow
            {
                attachment = attachment,
                name = item.Q<Label>("att-name"),
                knot = item.Q<IntegerField>("att-knot"),
                prefab = item.Q<ObjectField>("att-prefab"),
                position = item.Q<Vector3Field>("att-position"),
                rotation = item.Q<Vector3Field>("att-rotation"),
                scale = item.Q<Vector3Field>("att-scale"),
            };
            _attachRows.Add(row);

            row.prefab.objectType = typeof(GameObject);
            row.prefab.allowSceneObjects = false;
            row.knot.isDelayed = true;   // 「12」と打つ途中の「1」で点が移らないように

            row.knot.RegisterValueChangedCallback(e =>
            {
                if (attachment == null) return;
                CableCommands.SetAttachmentKnot(attachment, e.newValue);
                CableKnotSelection.SelectSingle(gen, attachment.knotIndex);   // 付け替えた先の点を選んで編集を続けられるように
                AfterAttachmentEdit();
            });
            row.prefab.RegisterValueChangedCallback(e =>
            {
                if (attachment == null) return;
                CableCommands.SetAttachmentPrefab(attachment, e.newValue as GameObject);
                AfterAttachmentEdit();
            });
            EventCallback<ChangeEvent<Vector3>> onOffset = _ =>
            {
                if (attachment == null) return;
                CableCommands.SetAttachmentOffset(attachment, row.position.value, row.rotation.value, row.scale.value);
                AfterAttachmentEdit();
            };
            row.position.RegisterValueChangedCallback(onOffset);
            row.rotation.RegisterValueChangedCallback(onOffset);
            row.scale.RegisterValueChangedCallback(onOffset);

            var update = item.Q<Button>("att-update");
            if (update != null)
                update.clicked += () =>
                {
                    if (attachment == null) return;
                    CableCommands.RefreshAttachment(attachment);
                    AfterAttachmentEdit();
                };

            var delete = DennokoIcons.Apply(item.Q<Button>("att-delete"), "d_TreeEditor.Trash|TreeEditor.Trash", "✕");
            if (delete != null)
                delete.clicked += () =>
                {
                    if (attachment == null) return;
                    CableCommands.RemoveAttachment(attachment);
                    AfterAttachmentEdit();
                };

            // Inspector や Undo から変わったときも表示を読み直す
            item.TrackSerializedObjectValue(new SerializedObject(attachment), _ => Window.RefreshAll());
            return item;
        }

        private void AfterAttachmentEdit()
        {
            CableCommands.RepaintAll();
            Window.RefreshAll();
        }

        private static string AttachmentName(CableKnotAttachment a) =>
            a.prefab != null ? a.prefab.name : Tr("attach.noModel");
    }
}
