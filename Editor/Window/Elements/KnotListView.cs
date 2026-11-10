using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using CableGeneratorV2Runtime;
using static CableGeneratorV2Editor.CableL10n;

namespace CableGeneratorV2Editor
{
    /// <summary>
    /// 配線タブの制御点の一覧（設計書 §5.1）。行のクリックはシーンの球のクリックと同じ
    /// （単体選択 / Shift・Ctrl で足す・外す）、ダブルクリックでシーンをその点に合わせる。
    /// 制御点はふつう数十個までなので仮想化せず行を並べる（ScrollView の中に ListView を入れると二重スクロールになるため）。
    /// 行の数が変わったときだけ作り直し、それ以外は表示だけ更新する。
    /// </summary>
    internal sealed class KnotListView
    {
        private sealed class Row
        {
            public VisualElement Element;
            public Label Name;
            public Button[] Modes;
            public Button Delete;
        }

        private readonly VisualElement _container;
        private readonly List<Row> _rows = new List<Row>();
        private readonly Func<CableGenerator> _gen;
        private readonly Action<int> _frame;
        private bool _english;

        public KnotListView(VisualElement container, Func<CableGenerator> gen, Action<int> frame)
        {
            _container = container;
            _gen = gen;
            _frame = frame;
        }

        public void Refresh()
        {
            var gen = _gen();
            int count = gen != null && gen.Spline != null ? gen.Spline.Count : 0;
            if (_rows.Count != count || _english != English) Rebuild(count);

            for (int i = 0; i < count; i++)
            {
                var row = _rows[i];
                bool selected = CableKnotSelection.Owner == gen && CableKnotSelection.Contains(i);
                row.Element.EnableInClassList("cable-knot-row--selected", selected);

                bool first = i == 0, last = !gen.Spline.Closed && i == count - 1;
                string name = first ? Tr("wire.knot.start", i) : last ? Tr("wire.knot.end", i) : $"[{i}]";
                if (row.Name.text != name) row.Name.text = name;

                int mode = CableCommands.ToSimpleIndex(gen.Spline.GetTangentMode(i));
                for (int m = 0; m < row.Modes.Length; m++) row.Modes[m].EnableInClassList(DennokoBlockUI.ActiveClass, m == mode);
                row.Delete.SetEnabled(CableCommands.CanDelete(gen, 1));
            }
        }

        private void Rebuild(int count)
        {
            _english = English;
            _container.Clear();
            _rows.Clear();
            for (int i = 0; i < count; i++) _rows.Add(CreateRow(i));
        }

        private Row CreateRow(int index)
        {
            var row = new Row { Element = new VisualElement() };
            row.Element.AddToClassList("cable-knot-row");
            row.Element.RegisterCallback<ClickEvent>(e =>
            {
                var gen = _gen();
                if (gen == null) return;
                if (e.clickCount >= 2) { _frame(index); return; }
                if (e.shiftKey || e.ctrlKey || e.commandKey) CableKnotSelection.Toggle(gen, index);
                else CableKnotSelection.SelectSingle(gen, index);
            });

            row.Name = new Label { pickingMode = PickingMode.Ignore };
            row.Name.AddToClassList("cable-knot-row__name");
            row.Element.Add(row.Name);

            var modes = new VisualElement();
            modes.AddToClassList("dennoko-segmented");
            modes.AddToClassList("dennoko-choice-group");
            modes.AddToClassList("cable-knot-row__modes");
            string[] keys = { "mode.auto", "mode.smooth", "mode.corner" };
            row.Modes = new Button[keys.Length];
            for (int m = 0; m < keys.Length; m++)
            {
                int mode = m;
                var b = new Button(() =>
                {
                    var gen = _gen();
                    if (gen == null) return;
                    CableCommands.SetTangentMode(gen, new[] { index }, mode);
                    CableCommands.RepaintAll();
                }) { text = Tr(keys[m]), tooltip = Tr(keys[m] + ".tip") };
                b.RegisterCallback<ClickEvent>(e => e.StopPropagation());   // 行の選択と重ねない
                row.Modes[m] = b;
                modes.Add(b);
            }
            row.Element.Add(modes);

            row.Delete = DennokoIcons.Apply(new Button(() =>
            {
                var gen = _gen();
                if (gen == null) return;
                CableCommands.DeleteKnots(gen, new[] { index });
                CableCommands.RepaintAll();
            }), "d_TreeEditor.Trash|TreeEditor.Trash", "✕", Tr("wire.knotDelete.tip"));
            row.Delete.AddToClassList("dennoko-icon-button");
            row.Delete.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            row.Element.Add(row.Delete);
            return row;
        }
    }
}
