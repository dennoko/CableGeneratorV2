using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using CableGeneratorV2Editor;

namespace CableGeneratorV2Tests
{
    /// <summary>翻訳表（ja.json / en.json）とフォントのウォームアップの整合（設計書 §4.4）。</summary>
    public class CableGeneratorV2L10nTests
    {
        private static Dictionary<string, string> LoadTable(string fileName)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:TextAsset", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith("/Editor/Localization/" + fileName)) continue;
                return CableL10n.ParseFlatJson(AssetDatabase.LoadAssetAtPath<TextAsset>(path).text);
            }
            Assert.Fail(fileName + " not found");
            return null;
        }

        [Test]
        public void JaAndEn_HaveSameKeys_AndNoEmptyValues()
        {
            var ja = LoadTable("ja.json");
            var en = LoadTable("en.json");
            CollectionAssert.AreEquivalent(ja.Keys, en.Keys);
            foreach (var kv in ja) Assert.IsNotEmpty(kv.Value, "ja: " + kv.Key);
            foreach (var kv in en) Assert.IsNotEmpty(kv.Value, "en: " + kv.Key);
        }

        [Test]
        public void JaPlaceholders_MatchEn()
        {
            var ja = LoadTable("ja.json");
            var en = LoadTable("en.json");
            foreach (var kv in ja)
            {
                for (int i = 0; i < 4; i++)
                {
                    string ph = "{" + i + "}";
                    Assert.AreEqual(kv.Value.Contains(ph), en[kv.Key].Contains(ph), $"{kv.Key} {ph}");
                }
            }
        }

        [Test]
        public void FontWarmup_ContainsAllJapaneseCharacters()
        {
            var field = typeof(DennokoUIFont).GetField("WarmupJapanese", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(field);
            string warmup = (string)field.GetValue(null);
            var missing = new HashSet<char>();
            foreach (string text in LoadTable("ja.json").Values)
                foreach (char c in text)
                    if (c > 0x7e && c != '\n' && warmup.IndexOf(c) < 0) missing.Add(c);
            Assert.IsEmpty(missing, "WarmupJapanese に無い文字: " + string.Concat(missing));
        }

        [Test]
        public void ParseFlatJson_HandlesEscapes()
        {
            var t = CableL10n.ParseFlatJson("{ \"a\": \"x\\\"y\\n\\u3042\", \"b\": \"\" }");
            Assert.AreEqual("x\"y\nあ", t["a"]);
            Assert.AreEqual("", t["b"]);
        }
    }
}
