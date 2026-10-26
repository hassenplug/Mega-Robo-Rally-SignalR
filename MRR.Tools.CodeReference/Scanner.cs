using System.Text;
using System.Text.RegularExpressions;

namespace MRR.Tools.CodeReference;

// Light, regex-and-brace-depth scanning of C# and JavaScript. This is NOT a compiler: it cannot
// tell which type a call like `x.Foo()` targets, so callers are matched by name only.

public record FuncInfo(
    string Kind,          // "method", "ctor", "decl" (interface/abstract), "js"
    string TypeName,      // containing type (C#) or "" (JS)
    string Name,
    string Signature,
    string File,          // repo-relative, forward slashes
    int NameLine,         // line of the name token
    int StartLine,        // first line of the member header (attributes included)
    int EndLine,
    string Doc,
    string Attrs,
    string Mods);

public record TypeInfo(string Name, string File, int StartLine, int EndLine);

public static class Scanner
{
    // Replaces comments and string/char literal contents with spaces, keeping newlines and length,
    // so positions and line numbers still match the original text.
    public static string Blank(string s, bool js)
    {
        var o = s.ToCharArray();
        int n = s.Length, i = 0;
        void Fill(int from, int to) { for (int k = from; k < to && k < n; k++) if (o[k] != '\n' && o[k] != '\r') o[k] = ' '; }
        while (i < n)
        {
            char c = s[i];
            if (c == '/' && i + 1 < n && s[i + 1] == '/')
            {
                int e = s.IndexOf('\n', i); if (e < 0) e = n;
                Fill(i, e); i = e; continue;
            }
            if (c == '/' && i + 1 < n && s[i + 1] == '*')
            {
                int e = s.IndexOf("*/", i + 2, StringComparison.Ordinal); e = e < 0 ? n : e + 2;
                Fill(i, e); i = e; continue;
            }
            if (!js && c == '"' && i + 2 < n && s[i + 1] == '"' && s[i + 2] == '"')
            {
                int e = s.IndexOf("\"\"\"", i + 3, StringComparison.Ordinal); e = e < 0 ? n : e + 3;
                Fill(i + 3, e - 3); i = e; continue;
            }
            if (!js && (c == '@' || c == '$') && i + 1 < n && (s[i + 1] == '"' || ((s[i + 1] == '@' || s[i + 1] == '$') && i + 2 < n && s[i + 2] == '"')))
            {
                int q = s.IndexOf('"', i);
                bool verbatim = s.Substring(i, q - i).Contains('@');
                int j = q + 1;
                while (j < n)
                {
                    if (verbatim) { if (s[j] == '"') { if (j + 1 < n && s[j + 1] == '"') { j += 2; continue; } break; } }
                    else { if (s[j] == '\\') { j += 2; continue; } if (s[j] == '"') break; if (s[j] == '\n') break; }
                    j++;
                }
                Fill(q + 1, j); i = j + 1; continue;
            }
            if (c == '"' || (js && (c == '\'' || c == '`')))
            {
                int j = i + 1;
                while (j < n && s[j] != c) { if (s[j] == '\\') j++; else if (c != '`' && s[j] == '\n') break; j++; }
                Fill(i + 1, j); i = j + 1; continue;
            }
            if (!js && c == '\'')
            {
                int j = i + 1;
                while (j < n && s[j] != '\'' && s[j] != '\n') { if (s[j] == '\\') j++; j++; }
                Fill(i + 1, j); i = j + 1; continue;
            }
            i++;
        }
        var result = new string(o);
        // preprocessor lines (#region, #if, ...) are not code; left in, they leak into member headers
        if (!js) result = Regex.Replace(result, @"^[ \t]*#[^\r\n]*", m => new string(' ', m.Length), RegexOptions.Multiline);
        return result;
    }

    public static int[] LineStarts(string s)
    {
        var l = new List<int> { 0 };
        for (int i = 0; i < s.Length; i++) if (s[i] == '\n') l.Add(i + 1);
        return l.ToArray();
    }

    public static int LineOf(int[] starts, int pos)
    {
        int idx = Array.BinarySearch(starts, pos);
        if (idx < 0) idx = ~idx - 1;
        return idx + 1;
    }

    static readonly Regex TypeRx = new(@"\b(class|struct|interface|enum|record)\s+(?:struct\s+|class\s+)?(\w+)", RegexOptions.Compiled);
    static readonly Regex FirstParenRx = new(@"(\w+)\s*(?:<[^()]*>)?\s*\(", RegexOptions.Compiled);
    static readonly Regex AttrRx = new(@"^(\s*\[[^\]]*\]\s*)+", RegexOptions.Compiled);
    static readonly HashSet<string> Mods = new() { "public", "private", "protected", "internal", "static", "async", "override", "virtual", "abstract", "extern", "partial", "sealed", "new", "unsafe", "readonly" };
    static readonly HashSet<string> NotMethod = new() { "if", "for", "foreach", "while", "switch", "using", "lock", "catch", "fixed", "return", "new", "typeof", "nameof", "when", "where" };

    sealed class Frame
    {
        public string Kind = "other";   // ns, type, method, other
        public string Name = "";
        public int Start;
        public FuncInfo? Func;
        public int FuncIdx = -1;
        public int TypeIdx = -1;
        public int Paren;
        public bool Keep;   // braces inside an expression (lambda, `new { }`); the member header must survive them
    }

    // Finds the C# methods, constructors and type ranges in one file.
    public static (List<FuncInfo> funcs, List<TypeInfo> types) ParseCSharp(string file, string raw, string blank)
    {
        var funcs = new List<FuncInfo>();
        var types = new List<TypeInfo>();
        var starts = LineStarts(raw);
        var rawLines = raw.Split('\n');
        var stack = new Stack<Frame>();
        var header = new StringBuilder();
        int headerStart = -1;            // char position of the first char in the header buffer
        int paren = 0;
        string TypePath() => string.Join(".", stack.Where(f => f.Kind == "type").Reverse().Select(f => f.Name));

        void ResetHeader() { header.Clear(); headerStart = -1; }

        string DocAbove(int firstLine)
        {
            var parts = new List<string>();
            for (int l = firstLine - 2; l >= 0 && l < rawLines.Length; l--)
            {
                var t = rawLines[l].Trim();
                if (t.StartsWith("///")) parts.Insert(0, t[3..].Trim());
                else if (t.StartsWith("[") ) continue;
                else break;
            }
            if (parts.Count == 0) return "";
            var doc = string.Join(" ", parts);
            var m = Regex.Match(doc, @"<summary>(.*?)</summary>", RegexOptions.Singleline);
            if (m.Success) doc = m.Groups[1].Value;
            doc = Regex.Replace(doc, @"<see\s+(?:cref|langword)=""([^""]*)""\s*/>", "`$1`");
            doc = Regex.Replace(doc, @"<paramref\s+name=""([^""]*)""\s*/>", "`$1`");
            doc = Regex.Replace(doc, @"<[^>]+>", "");
            return Regex.Replace(doc, @"\s+", " ").Trim();
        }

        // Tries to read the header as a method/ctor. Returns null if it isn't one.
        FuncInfo? AsMethod(string h, int hStartPos, int endLine, string parentKind, string typeName, bool exprBodied)
        {
            var attrM = AttrRx.Match(h);
            string attrs = attrM.Success ? Regex.Replace(attrM.Value, @"\s+", " ").Trim() : "";
            string sig = attrM.Success ? h[attrM.Length..] : h;
            int p = sig.IndexOf('(');
            if (p < 0) return null;
            string pre = sig[..p];
            if (pre.Contains('=') || pre.Contains("=>")) return null;
            // the first identifier-before-'(' that is not a modifier: skips tuple return types like `static (int a, int b) Name(...)`
            Match? m = null;
            foreach (Match cand in FirstParenRx.Matches(sig))
                if (!Mods.Contains(cand.Groups[1].Value)) { m = cand; break; }
            if (m == null) return null;
            string name = m.Groups[1].Value;
            if (NotMethod.Contains(name) || name == "operator") return null;
            if (!exprBodied && !Regex.IsMatch(sig.TrimEnd(), @"\)\s*(where\b[^{]*)?$|\)\s*:\s*(base|this)\s*\(.*\)$", RegexOptions.Singleline)) return null;
            string flat = Regex.Replace(sig, @"\s+", " ").Trim();
            if (exprBodied) { int a = flat.IndexOf("=>", StringComparison.Ordinal); if (a > 0) flat = flat[..a].Trim(); }
            string beforeName = sig[..m.Groups[1].Index];
            bool hasReturnType = Regex.Matches(beforeName, @"[\w<>\[\],?.]+").Any(t => !Mods.Contains(t.Value));
            string kind = name == typeName.Split('.').Last() && !hasReturnType ? "ctor" : "method";
            int nameIdx = hStartPos + (h.Length - sig.Length) + m.Groups[1].Index;
            int nameLine = LineOf(starts, Math.Min(nameIdx, raw.Length - 1));
            int firstNonWs = hStartPos; while (firstNonWs < raw.Length && char.IsWhiteSpace(blank[firstNonWs])) firstNonWs++;
            int startLine = LineOf(starts, Math.Min(firstNonWs, raw.Length - 1));
            string mods = string.Join(" ", Regex.Matches(pre, @"\b(public|private|protected|internal|static|async|override|virtual|abstract|extern|partial)\b").Select(x => x.Value));
            return new FuncInfo(kind, typeName, name, flat.Length > 170 ? flat[..167] + "..." : flat, file, nameLine, startLine, endLine, DocAbove(startLine), attrs, mods);
        }

        int n = blank.Length;
        for (int i = 0; i < n; i++)
        {
            char c = blank[i];
            if (c == '(' || c == '[') paren++;
            else if (c == ')' || c == ']') paren = Math.Max(0, paren - 1);

            if (c == '{')
            {
                var parent = stack.Count > 0 ? stack.Peek() : null;
                var f = new Frame { Start = LineOf(starts, i) };
                string h = header.ToString();
                bool keep = (parent != null && parent.Keep) || paren > 0 || (parent != null && parent.Kind == "type" && h.Contains("=>"));
                if (paren == 0 && !keep)
                {
                    var tm = TypeRx.Match(h);
                    bool inMember = parent != null && (parent.Kind == "method" || parent.Kind == "other");
                    if (!inMember && tm.Success && !h.Contains("=>") && !Regex.IsMatch(h, @"\bnew\b"))
                    {
                        f.Kind = "type"; f.Name = tm.Groups[2].Value;
                        f.TypeIdx = types.Count;
                        types.Add(new TypeInfo(string.Join(".", stack.Where(x => x.Kind == "type").Reverse().Select(x => x.Name).Append(f.Name)), file, f.Start, f.Start));
                    }
                    else if (!inMember && Regex.IsMatch(h, @"^\s*namespace\b")) f.Kind = "ns";
                    else if (parent != null && parent.Kind == "type")
                    {
                        var fi = AsMethod(h, headerStart < 0 ? i : headerStart, f.Start, "type", TypePath(), false);
                        if (fi != null) { f.Kind = "method"; f.Func = fi; f.FuncIdx = funcs.Count; funcs.Add(fi); }
                    }
                }
                f.Keep = keep;
                f.Paren = paren;
                paren = 0;
                stack.Push(f);
                if (!keep) ResetHeader();
                continue;
            }
            if (c == '}')
            {
                if (stack.Count > 0)
                {
                    var f = stack.Pop();
                    int endLine = LineOf(starts, i);
                    if (f.Kind == "method" && f.FuncIdx >= 0) funcs[f.FuncIdx] = f.Func! with { EndLine = endLine };
                    if (f.Kind == "type" && f.TypeIdx >= 0) types[f.TypeIdx] = types[f.TypeIdx] with { EndLine = endLine };
                    paren = f.Paren;
                    if (f.Keep) continue;
                }
                ResetHeader();
                continue;
            }
            if (c == ';' && paren == 0)
            {
                var parent = stack.Count > 0 ? stack.Peek() : null;
                string h = header.ToString();
                if (parent != null && parent.Kind == "type")
                {
                    bool expr = h.Contains("=>");
                    var fi = AsMethod(h, headerStart < 0 ? i : headerStart, LineOf(starts, i), "type", TypePath(), expr);
                    if (fi != null && (expr || fi.Kind == "method"))
                    {
                        bool inIface = false;
                        // abstract/interface declarations (no body) are kept but marked "decl"
                        if (!expr) { inIface = true; fi = fi with { Kind = "decl" }; }
                        if (expr || inIface) funcs.Add(fi);
                    }
                }
                ResetHeader();
                continue;
            }
            if (stack.Count > 0 && stack.Peek().Keep) continue;
            if (headerStart < 0) headerStart = i;
            header.Append(c);
        }
        return (funcs, types);
    }

    // JavaScript: top-level and nested named functions, found by pattern; end line by brace matching.
    public static List<FuncInfo> ParseJs(string file, string raw, string blank)
    {
        var res = new List<FuncInfo>();
        var starts = LineStarts(raw);
        var rawLines = raw.Split('\n');
        var rx = new Regex(@"\bfunction\s+(\w+)\s*\(|\b(?:var|let|const)\s+(\w+)\s*=\s*(?:async\s*)?(?:function\b|\([^)]*\)\s*=>|\w+\s*=>)|\b(\w+)\s*:\s*(?:async\s*)?function\b", RegexOptions.Compiled);
        foreach (Match m in rx.Matches(blank))
        {
            string name = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
            if (name is "null" or "true" or "false" or "undefined") continue;   // `cond ? null : function...`
            int line = LineOf(starts, m.Index);
            int end = line;
            int b = blank.IndexOf('{', m.Index + m.Length - 1);
            int semi = blank.IndexOf(';', m.Index);
            // arrow functions with an expression body have no brace before the statement ends
            if (b >= 0 && (semi < 0 || b < semi || m.Value.Contains("function")))
            {
                int depth = 0;
                for (int k = b; k < blank.Length; k++)
                {
                    if (blank[k] == '{') depth++;
                    else if (blank[k] == '}' && --depth == 0) { end = LineOf(starts, k); break; }
                }
            }
            string sig = Regex.Replace(m.Value, @"\s+", " ").Trim();
            int pe = blank.IndexOf(')', m.Index + m.Length - 1);
            if (m.Groups[1].Success && pe > 0) sig = Regex.Replace(blank.Substring(m.Index, pe - m.Index + 1), @"\s+", " ");
            string doc = "";
            for (int l = line - 2; l >= 0 && l < rawLines.Length; l--)
            {
                var t = rawLines[l].Trim();
                if (t.StartsWith("//")) doc = t.TrimStart('/').Trim() + (doc.Length > 0 ? " " + doc : "");
                else break;
            }
            res.Add(new FuncInfo("js", "", name, sig, file, line, line, end, doc, "", ""));
        }
        return res;
    }
}
