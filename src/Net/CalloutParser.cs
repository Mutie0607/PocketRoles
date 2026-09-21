using System;
using System.Collections.Generic;
using System.Text;

namespace PocketRoles.Net
{
    /// <summary>
    /// v0.5.3 CalloutWatch's chat reader: which players a meeting line names, and whether the line accuses them.
    /// No game types here (plain strings and colour ids), so it can be tested outside the game (scratchpad\calltest).
    ///
    /// A player is named by a colour word (ja / en / zh, the forms players type: "コラール", "こーらる", "ロズ", "red")
    /// or by the whole name. A line accuses when it has an accusation word that is not negated ("インポ", "怪しい",
    /// "sus", "内鬼"; not "怪しくない", "red isn't sus", "不可疑"), a werewolf verdict ("赤黒", "赤は真っ黒"), a question
    /// ("青じゃない？"), or is nothing but colours / names with a lead-in and a sentence ending ("とりま青", "コラールだ",
    /// "赤 青"). A colour the speaker clears ("緑白", "青は白", "青じゃない", "red is safe") is not a target.
    /// Precision over recall: a word that might be something else ("いったん", "投票しろ", "エンジ", a two-kana name
    /// inside a sentence) is not read as a player. Review 2026-09-21: 25 findings, the cases are in the test harness.
    /// </summary>
    internal static class CalloutParser
    {
        internal struct Cand
        {
            public int Color;      // DefaultOutfit.ColorId
            public string Name;    // display name without tags
        }

        internal sealed class Result
        {
            public bool Accusing;
            public readonly List<int> Targets = new List<int>();   // indexes into the candidates
        }

        private enum Lx { Any, Ja, Zh }
        private struct Word { public string Text; public int Color; public Lx Lang; }

        /// <summary>Colour words after <see cref="Normalize"/> (hiragana → katakana, full-width ASCII → ASCII, lower case).</summary>
        private static readonly Word[] Colors = BuildColors();

        private static Word[] BuildColors()
        {
            var list = new List<Word>();
            void Add(int c, Lx l, params string[] words) { foreach (var w in words) list.Add(new Word { Text = w, Color = c, Lang = l }); }
            Add(0, Lx.Any, "赤", "アカ", "レッド", "red", "红", "紅");
            Add(1, Lx.Any, "アオ", "ブルー", "blue", "蓝", "藍");
            Add(1, Lx.Ja, "青");
            Add(2, Lx.Any, "緑", "ミドリ", "グリーン", "green", "绿");
            Add(3, Lx.Any, "ピンク", "桃", "pink", "粉红", "粉色", "粉");
            Add(4, Lx.Any, "オレンジ", "橙", "orange", "橘");
            Add(5, Lx.Any, "黄色", "黄", "キイロ", "イエロー", "yellow");
            Add(6, Lx.Any, "黒", "クロ", "ブラック", "black", "黑");
            Add(7, Lx.Any, "白", "シロ", "ホワイト", "white");
            Add(8, Lx.Any, "紫", "ムラサキ", "パープル", "purple");
            Add(9, Lx.Any, "茶色", "茶", "チャイロ", "ブラウン", "brown", "棕", "咖啡");
            Add(10, Lx.Any, "水色", "水", "ミズイロ", "シアン", "cyan", "天蓝", "浅蓝");
            Add(10, Lx.Zh, "青", "青色");
            Add(11, Lx.Any, "黄緑", "キミドリ", "ライム", "lime", "黄绿", "浅绿", "柠檬");
            Add(12, Lx.Any, "マルーン", "マルン", "臙脂", "maroon", "栗色", "褐红", "酒红", "暗红", "红褐");
            Add(13, Lx.Any, "ローズ", "ロズ", "薄ピンク", "rose", "玫瑰", "玫红", "淡粉");
            Add(14, Lx.Any, "バナナ", "バナ", "banana", "香蕉", "淡黄");
            Add(15, Lx.Any, "灰色", "灰", "ハイイロ", "グレー", "グレイ", "グレ", "gray", "grey");
            Add(16, Lx.Any, "タン", "ベージュ", "tan", "肤色", "卡其", "棕褐");
            Add(17, Lx.Any, "コーラル", "コラール", "コーラ", "コラル", "サンゴ", "珊瑚", "coral");
            list.Sort((a, b) => b.Text.Length.CompareTo(a.Text.Length));   // longest match first (黄緑 before 黄, バナナ before バナ)
            return list.ToArray();
        }

        /// <summary>Words that contain a colour character but are not a colour (scanned past, no mention).</summary>
        private static readonly string[] NotColors =
        {
            "面白", "白状", "告白", "明白", "潔白", "空白", "真ッ白", "真ッ黒", "腹黒", "黒幕", "赤チャン", "青春", "オ茶", "茶番", "無茶", "水曜", "水筒", "划水", "水ヤリ",
            "ボタン", "スタン", "白確", "確白", "黒確", "確黒", "グレー吊", "灰吊", "グレラン", "イッタン", "アカン",
        };

        /// <summary>Scanned past as whole words, so a colour glued after them still counts ("インポコーラル", "とりまあお").</summary>
        private static readonly string[] Boundaries = { "インポスター", "インポ", "キラー", "キル", "ベント", "トリアエズ", "トリマ", "タブン", "ゼッタイ" };

        private struct Accuse { public string Text; public bool Hiragana; }
        /// <summary>
        /// Accusation words (normalized). ASCII words must be whole words ("imp" is not "impossible"). Katakana words
        /// count typed in hiragana only when <see cref="Accuse.Hiragana"/> ("いんぽ", "あやしい"; not "できる" / "べんとう").
        /// </summary>
        private static readonly Accuse[] AccuseWords =
        {
            A("インポ", true), A("アヤシ", true), A("キル", false), A("キラー", false), A("ベント", false),
            A("怪シ", true), A("犯人", true), A("黒幕", true), A("吊", true), A("投票", true), A("殺", true),
            A("内鬼", true), A("狼", true), A("可疑", true), A("凶手", true), A("杀", true), A("刀", true), A("投", true),
            A("imp", true), A("imps", true), A("impo", true), A("impostor", true), A("imposter", true), A("impostors", true),
            A("sus", true), A("sussy", true), A("kill", true), A("killed", true), A("kills", true), A("killer", true),
            A("vent", true), A("vented", true), A("venting", true), A("vote", true),
        };
        private static Accuse A(string t, bool h) => new Accuse { Text = t, Hiragana = h };

        /// <summary>After an accusation word (within a few characters): it is denied ("怪しくない", "インポじゃない", "投票しないで").</summary>
        private static readonly string[] NegAfter = { "ナイ", "ナク", "ナカッ", "ネー", "ネエ", "ジャナ", "デハナ", "マセン", "無イ" };
        /// <summary>Before an accusation word: it is denied ("red isn't sus", "don't vote", "不可疑", "没杀").</summary>
        private static readonly string[] NegBefore = { "not ", "n't ", "isnt ", "never ", "no ", "不", "没", "別ニ" };

        /// <summary>A werewolf-style verdict glued to a colour ("緑白" = green is innocent): consumed with the colour.</summary>
        private static readonly string[] VerdictClear = { "白確", "確白", "白", "シロ" };
        /// <summary>After a colour, past spaces or one particle: the speaker clears that player ("青じゃない", "red is safe").</summary>
        private static readonly string[] ClearAfter = { "ジャナ", "デハナ", "違", "チガ", "無実", "セーフ", "安全", "以外", "好人", "不是", "safe", "clear", "cleared", "innocent", "not", "isnt", "isn't" };
        private static readonly string[] ClearBefore = { "not ", "isnt ", "isn't ", "不是", "非", "除了" };
        private static readonly string[] Particles = { "ハ", "ガ", "モ", "ワ", "is ", "是" };

        /// <summary>A hiragana-typed colour may be followed only by these (particles, endings, honorifics): "あかでしょ", "あおが"; not "あかん".</summary>
        private const string HiraganaNext = "ガハモトヤニデダジッカネヨノサクチワヲ";

        /// <summary>
        /// The "a bare list of colours is an accusation" check: what is left around the colours / names may only be a
        /// lead-in, joiners and a sentence ending. "赤がいた" is not bare.
        /// </summary>
        private static readonly string[] LeadIn = { "トリアエズ", "トリマ", "イヤ", "ヤッパ", "タブン", "多分", "絶対", "ゼッタイ", "確定", "投票", "我投", "vote", "its", "it's", "prob", "def" };
        private static readonly string[] Ending =
        {
            "デショウ", "デショ", "ダロウ", "ダロ", "ッポイ", "ポイ", "デス", "ダヨ", "ダネ", "ダナ", "カナ", "カモ", "ヤロ", "ヤン", "ジャネ", "ジャン",
            "ダ", "ネ", "ヨ", "ナ", "w", "草", "吧", "啊", "的", "!", "?",
        };
        private static readonly string[] Joiner = { "ト", "ヤ", "and", "&", "和", "or", "," };

        /// <summary>A kana-less line with one of these is Chinese even without a Chinese-only character: 青 is then ambiguous (cyan in Chinese).</summary>
        private static readonly string[] ZhHints = { "是", "内鬼", "可疑", "投", "和", "杀", "刀", "我", "他", "她", "了", "的" };

        private static readonly string HalfKana = "ｦｧｨｩｪｫｬｭｮｯｰｱｲｳｴｵｶｷｸｹｺｻｼｽｾｿﾀﾁﾂﾃﾄﾅﾆﾇﾈﾉﾊﾋﾌﾍﾎﾏﾐﾑﾒﾓﾔﾕﾖﾗﾘﾙﾚﾛﾜﾝ";
        private static readonly string FullKana = "ヲァィゥェォャュョッーアイウエオカキクケコサシスセソタチツテトナニヌネノハヒフヘホマミムメモヤユヨラリルレロワン";

        /// <summary>Half-width katakana → full-width (ﾞ / ﾟ folded into the letter), before anything else; changes the length.</summary>
        internal static string FoldHalfWidth(string s)
        {
            bool any = false;
            foreach (char c in s) if (c >= 'ｦ' && c <= 'ﾟ') { any = true; break; }
            if (!any) return s;
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                int k = HalfKana.IndexOf(c);
                if (k >= 0) { sb.Append(FullKana[k]); continue; }
                if ((c == 'ﾞ' || c == 'ﾟ') && sb.Length > 0)
                {
                    char p = sb[sb.Length - 1];
                    bool dakuten = c == 'ﾞ';
                    if (dakuten && p == 'ウ') { sb[sb.Length - 1] = 'ヴ'; continue; }
                    if ("カキクケコサシスセソタチツテトハヒフヘホ".IndexOf(p) >= 0 && dakuten) { sb[sb.Length - 1] = (char)(p + 1); continue; }
                    if ("ハヒフヘホ".IndexOf(p) >= 0 && !dakuten) { sb[sb.Length - 1] = (char)(p + 2); continue; }
                    continue;
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>One character in, one character out (the index maps back to the raw line).</summary>
        internal static string Normalize(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c0 in s)
            {
                char c = c0;
                if (c >= '！' && c <= '～') c = (char)(c - 0xFEE0);          // full-width ASCII
                else if (c == '　') c = ' ';
                else if (c == '’' || c == '‘') c = '\'';
                else if (c >= 'ぁ' && c <= 'ゖ') c = (char)(c + 0x60);       // hiragana → katakana
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        private static bool IsHiragana(char c) => c >= 'ぁ' && c <= 'ゖ';
        private static bool IsKatakana(char c) => (c >= 'ァ' && c <= 'ヺ') || c == 'ー';
        private static bool IsKana(char c) => IsHiragana(c) || IsKatakana(c);
        private static bool IsKanji(char c) => c >= '一' && c <= '鿿';
        private static bool IsAsciiLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        private static bool IsSeparator(char c) => char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsSymbol(c);

        private static bool At(string s, int i, string w) => i >= 0 && i + w.Length <= s.Length && string.CompareOrdinal(s, i, w, 0, w.Length) == 0;

        private static bool AsciiBounded(string s, int i, int len)
        {
            bool before = i > 0 && IsAsciiLetter(s[i - 1]);
            bool after = i + len < s.Length && IsAsciiLetter(s[i + len]);
            return !before && !after;
        }

        private static bool ContainsAny(string s, string[] words)
        {
            foreach (var w in words) if (s.IndexOf(w, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        /// <param name="zh">The line is Chinese (青 = cyan there, blue in Japanese).</param>
        internal static Result Parse(string raw, IList<Cand> cands, bool zh)
        {
            var result = new Result();
            if (string.IsNullOrEmpty(raw)) return result;
            raw = FoldHalfWidth(raw);
            if (raw.Length > 120) raw = raw.Substring(0, char.IsHighSurrogate(raw[119]) ? 119 : 120);
            string s = Normalize(raw);

            bool hasKana = false;
            foreach (char c in raw) if (IsKana(c) && c != 'ー') { hasKana = true; break; }
            bool aoUnclear = !zh && !hasKana && ContainsAny(s, ZhHints);   // "青是内鬼" without a Chinese-only character

            // candidates by colour, and by whole name (normalized, 2+ characters, 3+ when plain ASCII)
            var byColor = new Dictionary<int, int>();
            var names = new List<KeyValuePair<string, int>>();
            for (int n = 0; n < cands.Count; n++)
            {
                if (!byColor.ContainsKey(cands[n].Color)) byColor[cands[n].Color] = n;
                string name = Normalize(FoldHalfWidth((cands[n].Name ?? "").Trim()));
                bool ascii = true;
                foreach (char c in name) if (c > 0x7f) { ascii = false; break; }
                if (name.Length >= (ascii ? 3 : 2)) names.Add(new KeyValuePair<string, int>(name, n));
            }
            names.Sort((a, b) => b.Key.Length.CompareTo(a.Key.Length));

            var consumed = new bool[s.Length];
            int lastEnd = -1;
            bool verdictAccuse = false;
            for (int i = 0; i < s.Length;)
            {
                // whole player names first (a name may contain a colour word: 【红褐】慕铁0607)
                int named = -1, len = 0;
                foreach (var kv in names)
                {
                    if (!At(s, i, kv.Key)) continue;
                    int e = i + kv.Key.Length;
                    if (IsAsciiLetter(kv.Key[0]) && !AsciiBounded(s, i, kv.Key.Length)) continue;
                    if (ShortKanaName(kv.Key) && !((i == 0 || IsSeparator(raw[i - 1])) && (e >= raw.Length || IsSeparator(raw[e])))) continue;   // "やや怪しい"
                    named = kv.Value; len = kv.Key.Length;
                    break;
                }
                int color = -1;
                if (named < 0)
                {
                    string skip = null;
                    foreach (var w in NotColors) if (At(s, i, w)) { skip = w; break; }
                    if (skip == null) foreach (var w in Boundaries) if (At(s, i, w)) { skip = w; break; }
                    if (skip != null) { i += skip.Length; lastEnd = i; continue; }   // not marked: "赤がボタン押した" is not a bare list
                    foreach (var w in Colors)
                    {
                        if (w.Lang == Lx.Ja && zh) continue;
                        if (w.Lang == Lx.Zh && !zh) continue;
                        if (w.Text == "青" && aoUnclear) continue;
                        if (!At(s, i, w.Text)) continue;
                        if (IsAsciiLetter(w.Text[0]) && !AsciiBounded(s, i, w.Text.Length)) continue;
                        if (IsKatakana(w.Text[0]) && !KanaColorFits(raw, s, i, w.Text, lastEnd)) continue;
                        color = w.Color; len = w.Text.Length;
                        break;
                    }
                }
                if (named < 0 && color < 0) { i++; continue; }
                int target = named;
                if (target < 0 && !byColor.TryGetValue(color, out target)) target = -1;
                Mark(consumed, i, len);
                int end = i + len;

                // verdict / clearing words: glued, past spaces, or past one particle; a negation right before
                int jSpace = end;
                while (jSpace < s.Length && s[jSpace] == ' ') jSpace++;
                int jPart = -1;
                foreach (var pw in Particles) if (At(s, jSpace, pw)) { jPart = jSpace + pw.Length; break; }
                if (jPart >= 0) while (jPart < s.Length && s[jPart] == ' ') jPart++;
                bool cleared = false;
                string blackGlued = color == 6 ? null : At(s, end, "黒確") ? "黒確" : At(s, end, "黒") ? "黒" : At(s, end, "クロ") ? "クロ" : null;
                if (blackGlued != null) { Mark(consumed, end, blackGlued.Length); end += blackGlued.Length; verdictAccuse = true; }
                else if (jPart >= 0 && VerdictAt(s, jPart, BlackVerdicts) is string bv)
                {
                    // "赤は真っ黒" / "赤は黒": an accusation, and the verdict is no Black mention
                    verdictAccuse = true;
                    Mark(consumed, jPart, bv.Length); end = jPart + bv.Length;
                }
                else
                {
                    string glued = null;
                    if (color != 7) foreach (var v in VerdictClear) if (At(s, end, v)) { glued = v; break; }
                    if (glued != null) { cleared = true; Mark(consumed, end, glued.Length); end += glued.Length; }
                    else if (jPart >= 0 && VerdictAt(s, jPart, WhiteVerdicts) is string wv)
                    {
                        // "青は白": the verdict is no White mention
                        cleared = true;
                        Mark(consumed, jPart, wv.Length); end = jPart + wv.Length;
                    }
                    else
                    {
                        foreach (var cw in ClearAfter)
                        {
                            int at = At(s, end, cw) ? end : At(s, jSpace, cw) ? jSpace : (jPart >= 0 && At(s, jPart, cw)) ? jPart : -1;
                            if (at < 0) continue;
                            if (IsAsciiLetter(cw[0]) && !AsciiBounded(s, at, cw.Length)) continue;   // "clearly"
                            if ((cw == "ジャナ" || cw == "デハナ") && QuestionWithin(s, at + cw.Length, 4)) { verdictAccuse = true; break; }   // "青じゃない？"
                            cleared = true;
                            break;
                        }
                    }
                    if (!cleared)
                        foreach (var cb in ClearBefore)
                            if (i - cb.Length >= 0 && At(s, i - cb.Length, cb)) { cleared = true; break; }
                }
                if (!cleared && target >= 0 && !result.Targets.Contains(target)) result.Targets.Add(target);
                i = end;
                lastEnd = end;
            }

            bool keyword = verdictAccuse || HasAccusation(raw, s);
            if (!keyword && result.Targets.Count > 0) keyword = IsBareList(s, consumed);
            result.Accusing = keyword;
            return result;
        }

        /// <summary>An all-kana name of up to 3 characters ("やや", "かな") is an ordinary word inside a sentence.</summary>
        private static bool ShortKanaName(string name)
        {
            if (name.Length > 3) return false;
            foreach (char c in name) if (!IsKatakana(c)) return false;   // normalized: hiragana is katakana here
            return true;
        }

        /// <summary>
        /// A katakana-lexicon colour at s[i..]: typed in katakana it may not sit inside a longer katakana word ("ボタン",
        /// "エンジン"; "ブルーー" and a glued known word are fine); typed in hiragana it may not sit inside a hiragana word
        /// ("いったん", "うしろ", "あかん") and "しろ" after a kanji is the imperative ("投票しろ").
        /// </summary>
        private static bool KanaColorFits(string raw, string s, int i, string word, int lastEnd)
        {
            int e = i + word.Length;
            bool hira = false;
            for (int k = i; k < e && k < raw.Length; k++) if (IsHiragana(raw[k])) { hira = true; break; }
            char prev = i > 0 ? raw[i - 1] : ' ';
            char next = e < raw.Length ? raw[e] : ' ';
            if (!hira)
            {
                if (i > 0 && IsKatakana(prev) && i != lastEnd) return false;
                if (e < raw.Length && IsKatakana(next) && next != 'ー' && !GluedWord(s, e)) return false;
                return true;
            }
            if (i > 0 && IsHiragana(prev) && i != lastEnd) return false;
            if (word == "シロ" && i > 0 && IsKanji(prev)) return false;
            if (e < raw.Length && IsHiragana(next) && HiraganaNext.IndexOf(s[e]) < 0) return false;
            if (e < raw.Length && IsKatakana(next) && next != 'ー' && !GluedWord(s, e)) return false;
            return true;
        }

        private static bool GluedWord(string s, int at)
        {
            foreach (var w in Boundaries) if (At(s, at, w)) return true;
            foreach (var w in VerdictClear) if (At(s, at, w)) return true;
            if (At(s, at, "クロ")) return true;
            foreach (var w in Colors) if (IsKatakana(w.Text[0]) && At(s, at, w.Text)) return true;
            return false;
        }

        private static readonly string[] BlackVerdicts = { "真ッ黒", "黒確", "黒", "クロ" };
        private static readonly string[] WhiteVerdicts = { "真ッ白", "白確", "潔白", "白", "シロ" };

        private static string VerdictAt(string s, int at, string[] words)
        {
            foreach (var w in words) if (At(s, at, w)) return w;
            return null;
        }

        private static bool QuestionWithin(string s, int from, int n)
        {
            for (int k = from; k < s.Length && k < from + n; k++) if (s[k] == '?') return true;
            return false;
        }

        /// <summary>An accusation word that is not denied ("怪しくない" is not; "怪しくない？" is a question, so it is).</summary>
        private static bool HasAccusation(string raw, string s)
        {
            foreach (var w in AccuseWords)
            {
                for (int at = s.IndexOf(w.Text, StringComparison.Ordinal); at >= 0; at = s.IndexOf(w.Text, at + 1, StringComparison.Ordinal))
                {
                    int e = at + w.Text.Length;
                    if (IsAsciiLetter(w.Text[0]) && !AsciiBounded(s, at, w.Text.Length)) continue;
                    if (IsKatakana(w.Text[0]))
                    {
                        bool hira = false;
                        for (int k = at; k < e && k < raw.Length; k++) if (IsHiragana(raw[k])) { hira = true; break; }
                        if (hira && !w.Hiragana) continue;                                                       // "できる", "べんとう"
                        if (!hira && w.Text != "インポ" && at > 0 && IsKatakana(raw[at - 1])) continue;          // "スキル"
                    }
                    if (Denied(s, at, e)) continue;
                    return true;
                }
            }
            return false;
        }

        private static bool Denied(string s, int at, int e)
        {
            foreach (var nb in NegBefore)
            {
                if (!IsAsciiLetter(nb[0]))
                {
                    // CJK: right before the word ("不可疑", "不是内鬼", "没杀", "別に怪しくない" is caught after)
                    if (At(s, at - nb.Length, nb) || (nb == "不" && At(s, at - 2, "不是"))) return true;
                    continue;
                }
                int from = Math.Max(0, at - nb.Length - 6);   // "not the imp", "don't vote"
                int k = s.IndexOf(nb, from, StringComparison.Ordinal);
                if (k >= 0 && k + nb.Length <= at) return true;
            }
            int limit = Math.Min(s.Length, e + 7);
            foreach (var na in NegAfter)
            {
                int k = s.IndexOf(na, e, StringComparison.Ordinal);
                if (k < 0 || k + na.Length > limit) continue;
                return !QuestionWithin(s, k + na.Length, 3);   // "怪しくない？" asks, it does not deny
            }
            return false;
        }

        /// <summary>Only colours / names with a lead-in, joiners and a sentence ending around them.</summary>
        private static bool IsBareList(string s, bool[] consumed)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (consumed[i]) { if (sb.Length == 0 || sb[sb.Length - 1] != '#') sb.Append('#'); continue; }
                char c = s[i];
                if (char.IsWhiteSpace(c) || c == '、' || c == '。' || c == '・' || c == '…' || c == '~' || c == '.' || c == '〜' || c == 'ー') continue;
                sb.Append(c);
            }
            string r = sb.ToString();
            for (bool again = true; again;)
            {
                again = false;
                foreach (var w in LeadIn) if (r.StartsWith(w, StringComparison.Ordinal)) { r = r.Substring(w.Length); again = true; }
                foreach (var w in Ending) if (r.Length > w.Length && r.EndsWith(w, StringComparison.Ordinal)) { r = r.Substring(0, r.Length - w.Length); again = true; }
                foreach (var w in Joiner)
                {
                    string between = "#" + w + "#";
                    if (r.Contains(between)) { r = r.Replace(between, "#"); again = true; }
                }
            }
            foreach (char c in r) if (c != '#') return false;
            return r.Length > 0;
        }

        private static void Mark(bool[] consumed, int from, int len)
        {
            for (int k = from; k < from + len && k < consumed.Length; k++) consumed[k] = true;
        }
    }
}
