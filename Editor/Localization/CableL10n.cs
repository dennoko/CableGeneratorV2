using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CableGeneratorV2Editor
{
    /// <summary>
    /// 日本語 / English の切り替え（設計書 §4.4）。
    ///   Tr(key)            : 今の言語の文言。英語に無ければ日本語、どちらにも無ければキーそのもの
    ///   Localize(root)     : UXML の text / tooltip / ラベルに書いた「#キー」を置き換える。言語を変えたらもう一度呼ぶ
    ///   SetEnglish(bool)   : 切り替えて Changed を出す。選んだ言語は EditorPrefs に保存（プロジェクトをまたいで共有）
    /// 翻訳表は Editor/Localization/ja.json / en.json（キー → 文言のフラットな JSON）。
    /// </summary>
    [InitializeOnLoad]
    internal static class CableL10n
    {
        private const string PrefKey = "CableGeneratorV2_Language";
        private const string JaJsonGuid = "ad28064c9d394bf8a121a9be817a0574";
        private const string EnJsonGuid = "869228d39b3746a494d8733e4a3f6084";
        private const char KeyPrefix = '#';

        /// <summary>言語が変わった。開いている UI はこれで文言を読み直す。</summary>
        public static event Action Changed;

        public static bool English { get; private set; }

        private static Dictionary<string, string> _ja;
        private static Dictionary<string, string> _en;

        private sealed class Keys
        {
            public string Text;
            public string Tooltip;
        }

        private static readonly ConditionalWeakTable<VisualElement, Keys> ElementKeys = new ConditionalWeakTable<VisualElement, Keys>();

        static CableL10n()
        {
            English = EditorPrefs.HasKey(PrefKey)
                ? EditorPrefs.GetString(PrefKey) == "en"
                : Application.systemLanguage != SystemLanguage.Japanese;
        }

        public static void SetEnglish(bool english)
        {
            if (English == english) return;
            English = english;
            EditorPrefs.SetString(PrefKey, english ? "en" : "ja");
            Changed?.Invoke();
        }

        // ─── 文言 ───────────────────────────────────────────────────────

        public static string Tr(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            if (English && TryGet(EnTable, key, out var en)) return en;
            if (TryGet(JaTable, key, out var ja)) return ja;
            return key;   // 抜けが画面で分かるように、キーをそのまま出す
        }

        public static string Tr(string key, params object[] args)
        {
            string format = Tr(key);
            try { return string.Format(format, args); }
            catch (FormatException) { return format; }
        }

        private static bool TryGet(Dictionary<string, string> table, string key, out string value)
        {
            value = null;
            return table != null && table.TryGetValue(key, out value) && !string.IsNullOrEmpty(value);
        }

        private static Dictionary<string, string> JaTable => _ja ??= Load(JaJsonGuid);
        private static Dictionary<string, string> EnTable => _en ??= Load(EnJsonGuid);

        /// <summary>翻訳表を読み直す（json を編集したとき・テスト用）。</summary>
        public static void Reload()
        {
            _ja = null;
            _en = null;
        }

        /// <summary>日本語の翻訳表のすべての文言（フォントのウォームアップ確認のテスト用）。</summary>
        internal static IEnumerable<string> AllJapaneseTexts() => JaTable?.Values ?? (IEnumerable<string>)Array.Empty<string>();

        private static Dictionary<string, string> Load(string guid)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) return null;   // ドメインリロード直後など。キャッシュせず次回また試す
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (asset == null) return null;
            try
            {
                return ParseFlatJson(asset.text);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CableGenerator V2] 翻訳表を読めません ({path}): {e.Message}");
                return new Dictionary<string, string>();
            }
        }

        // ─── UXML の #キー ───────────────────────────────────────────────

        /// <summary>
        /// root 以下の「#キー」を今の言語の文言にする。キーは覚えておくので、言語を変えたら同じ root でもう一度呼ぶ。
        /// C# で動的に書き換える文言（件数・状態など）には #キー を書かない。
        /// </summary>
        public static void Localize(VisualElement root)
        {
            if (root == null) return;
            root.Query<VisualElement>().ForEach(LocalizeElement);
        }

        private static void LocalizeElement(VisualElement e)
        {
            ElementKeys.TryGetValue(e, out var keys);

            if (e is TextElement te)
            {
                if (IsKey(te.text))
                {
                    keys ??= GetOrCreate(e);
                    keys.Text = te.text.Substring(1);
                }
                if (keys?.Text != null) te.text = Tr(keys.Text);
            }

            if (IsKey(e.tooltip))
            {
                keys ??= GetOrCreate(e);
                keys.Tooltip = e.tooltip.Substring(1);
            }
            if (keys?.Tooltip != null) e.tooltip = Tr(keys.Tooltip);
        }

        private static Keys GetOrCreate(VisualElement e)
        {
            if (!ElementKeys.TryGetValue(e, out var keys))
            {
                keys = new Keys();
                ElementKeys.Add(e, keys);
            }
            return keys;
        }

        private static bool IsKey(string s) => !string.IsNullOrEmpty(s) && s.Length > 1 && s[0] == KeyPrefix && s[1] != ' ';

        // ─── フラットな JSON（{ "key": "value", ... }）の読み込み ──────────

        internal static Dictionary<string, string> ParseFlatJson(string json)
        {
            var result = new Dictionary<string, string>();
            int i = 0;
            SkipWhite(json, ref i);
            Expect(json, ref i, '{');
            while (true)
            {
                SkipWhite(json, ref i);
                if (i < json.Length && json[i] == '}') break;
                string key = ReadString(json, ref i);
                SkipWhite(json, ref i);
                Expect(json, ref i, ':');
                SkipWhite(json, ref i);
                result[key] = ReadString(json, ref i);
                SkipWhite(json, ref i);
                if (i < json.Length && json[i] == ',') { i++; continue; }
                SkipWhite(json, ref i);
                Expect(json, ref i, '}');
                break;
            }
            return result;
        }

        private static void SkipWhite(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        private static void Expect(string s, ref int i, char c)
        {
            if (i >= s.Length || s[i] != c) throw new FormatException($"'{c}' expected at {i}");
            i++;
        }

        private static string ReadString(string s, ref int i)
        {
            Expect(s, ref i, '"');
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
                char esc = s[i++];
                switch (esc)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'u':
                        sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
                        i += 4;
                        break;
                    default: sb.Append(esc); break;   // \" \\ \/
                }
            }
            throw new FormatException("unterminated string");
        }
    }
}
