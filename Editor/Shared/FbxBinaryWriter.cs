using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CableGeneratorV2Editor
{
    /// <summary>
    /// FBX ノードツリー（バイナリ FBX 7.4 形式で書き出すための最小モデル）。
    /// </summary>
    public sealed class FbxNode
    {
        public readonly string Name;
        public readonly List<object> Properties = new List<object>();
        public readonly List<FbxNode> Children = new List<FbxNode>();

        public FbxNode(string name, params object[] properties)
        {
            Name = name;
            if (properties != null) Properties.AddRange(properties);
        }

        public FbxNode Add(string name, params object[] properties)
        {
            var child = new FbxNode(name, properties);
            Children.Add(child);
            return child;
        }

        public FbxNode Add(FbxNode child)
        {
            Children.Add(child);
            return child;
        }

        /// <summary>Properties70 内の P レコードを追加する。</summary>
        public FbxNode P(string propName, string type, string label, string flags, params object[] values)
        {
            var args = new object[4 + (values?.Length ?? 0)];
            args[0] = propName; args[1] = type; args[2] = label; args[3] = flags;
            if (values != null) Array.Copy(values, 0, args, 4, values.Length);
            return Add("P", args);
        }
    }

    /// <summary>
    /// 外部パッケージ（Unity FBX Exporter / Autodesk FBX SDK）に依存しないバイナリ FBX 7.4 ライター。
    /// Unity の ModelImporter と Blender の FBX インポーターの双方で読める構造で書き出す。
    /// </summary>
    public static class FbxBinaryWriter
    {
        public const int Version = 7400;

        static readonly byte[] kHeaderMagic = Encoding.ASCII.GetBytes("Kaydara FBX Binary  \0\x1a\0");
        static readonly byte[] kFooterId = { 0xfa, 0xbc, 0xab, 0x09, 0xd0, 0xc8, 0xd4, 0x66, 0xb1, 0x76, 0xfb, 0x83, 0x1c, 0xf7, 0x26, 0x7e };
        static readonly byte[] kFooterMagic = { 0xf8, 0x5a, 0x8c, 0x6a, 0xde, 0xf5, 0xd9, 0x7e, 0xec, 0xe9, 0x0c, 0xe3, 0x75, 0x8f, 0x29, 0x0b };
        const int kNullRecordLength = 13;

        /// <summary>"Name\0\1Class" 形式のオブジェクト名を作る（バイナリ FBX の命名規則）。</summary>
        public static string ObjectName(string name, string cls) => (name ?? string.Empty) + "\0\u0001" + cls;

        public static byte[] Write(IList<FbxNode> topLevelNodes)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms, Encoding.UTF8))
            {
                w.Write(kHeaderMagic);
                w.Write((uint)Version);

                foreach (var node in topLevelNodes)
                    WriteNode(w, node);
                w.Write(new byte[kNullRecordLength]);

                // フッター（FBX SDK / Blender が書き出す固定パターン）
                w.Write(kFooterId);
                w.Write(new byte[4]);
                long ofs = ms.Position;
                int pad = (int)(((ofs + 15) & ~15L) - ofs);
                if (pad == 0) pad = 16;
                w.Write(new byte[pad]);
                w.Write((uint)Version);
                w.Write(new byte[120]);
                w.Write(kFooterMagic);

                w.Flush();
                return ms.ToArray();
            }
        }

        static void WriteNode(BinaryWriter w, FbxNode node)
        {
            Stream s = w.BaseStream;
            long start = s.Position;

            w.Write(0u); // EndOffset（後で書き戻す）
            w.Write((uint)node.Properties.Count);
            w.Write(0u); // PropertyListLen（後で書き戻す）

            byte[] nameBytes = Encoding.ASCII.GetBytes(node.Name);
            if (nameBytes.Length > 255) throw new ArgumentException("FBX node name too long: " + node.Name);
            w.Write((byte)nameBytes.Length);
            w.Write(nameBytes);

            long propStart = s.Position;
            foreach (var prop in node.Properties)
                WriteProperty(w, prop);
            long propEnd = s.Position;

            // 子を持つノード、およびプロパティも子も持たないノードは終端の NULL レコードを持つ
            if (node.Children.Count > 0 || node.Properties.Count == 0)
            {
                foreach (var child in node.Children)
                    WriteNode(w, child);
                w.Write(new byte[kNullRecordLength]);
            }

            long end = s.Position;
            if (end > uint.MaxValue) throw new IOException("FBX 7.4 binary is limited to 4GB.");

            s.Position = start;
            w.Write((uint)end);
            s.Position = start + 8;
            w.Write((uint)(propEnd - propStart));
            s.Position = end;
        }

        static void WriteProperty(BinaryWriter w, object value)
        {
            switch (value)
            {
                case bool b:
                    w.Write((byte)'C'); w.Write((byte)(b ? 1 : 0)); break;
                case short sh:
                    w.Write((byte)'Y'); w.Write(sh); break;
                case int i:
                    w.Write((byte)'I'); w.Write(i); break;
                case long l:
                    w.Write((byte)'L'); w.Write(l); break;
                case float f:
                    w.Write((byte)'F'); w.Write(f); break;
                case double d:
                    w.Write((byte)'D'); w.Write(d); break;
                case string str:
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(str);
                    w.Write((byte)'S'); w.Write((uint)bytes.Length); w.Write(bytes);
                    break;
                }
                case byte[] raw:
                    w.Write((byte)'R'); w.Write((uint)raw.Length); w.Write(raw); break;
                case int[] ia:
                    WriteArrayHeader(w, 'i', ia.Length, 4);
                    foreach (var x in ia) w.Write(x);
                    break;
                case long[] la:
                    WriteArrayHeader(w, 'l', la.Length, 8);
                    foreach (var x in la) w.Write(x);
                    break;
                case float[] fa:
                    WriteArrayHeader(w, 'f', fa.Length, 4);
                    foreach (var x in fa) w.Write(x);
                    break;
                case double[] da:
                    WriteArrayHeader(w, 'd', da.Length, 8);
                    foreach (var x in da) w.Write(x);
                    break;
                default:
                    throw new ArgumentException("Unsupported FBX property type: " + (value?.GetType().Name ?? "null"));
            }
        }

        static void WriteArrayHeader(BinaryWriter w, char type, int length, int elemSize)
        {
            w.Write((byte)type);
            w.Write((uint)length);
            w.Write(0u);                           // Encoding: 0 = 非圧縮
            w.Write((uint)(length * elemSize));    // CompressedLength (= バイト長)
        }
    }
}
