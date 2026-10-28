using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MRR.Tools.CodeReference;

// Generates documents/code_reference/*.md: every C# method/constructor and JavaScript function in
// the repo, with its doc comment and the places that call it. Callers are matched by NAME ONLY (a
// regex scan, not a compiler), so a name shared by several types lists callers of all of them --
// the tables say so. Regenerate with:  dotnet run --project MRR.Tools.CodeReference
//
// Usage: MRR.Tools.CodeReference [repoRoot]      (default: current directory)

string root = Path.GetFullPath(args.Length > 0 ? args[0] : Directory.GetCurrentDirectory());
string outDir = Path.Combine(root, "documents", "code_reference");
Directory.CreateDirectory(outDir);

string Rel(string p) => Path.GetRelativePath(root, p).Replace('\\', '/');
bool Skip(string p) => p.Contains("/bin/") || p.Contains("/obj/") || p.Contains("/node_modules/") || p.Contains("/.git/") || p.Contains("/.vs/");

IEnumerable<string> Files(string pattern) =>
    Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories).Where(f => !Skip(f.Replace('\\', '/'))).OrderBy(f => f, StringComparer.Ordinal);

// ── load sources ────────────────────────────────────────────────────────────────────────────
var cs = new List<(string file, string raw, string blank, int[] starts)>();
foreach (var f in Files("*.cs"))
{
    var raw = File.ReadAllText(f);
    var blank = Scanner.Blank(raw, false);
    cs.Add((Rel(f), raw, blank, Scanner.LineStarts(raw)));
}

// JS: .js files and inline <script> blocks of .html under wwwroot, third-party libraries excluded.
var js = new List<(string file, string raw, string blank, int[] starts, string markup)>();
bool ThirdParty(string n) => Regex.IsMatch(n, @"(\.min\.js$|jquery|signalr|w3\.js)", RegexOptions.IgnoreCase);
foreach (var f in Files("*.js").Where(f => f.Replace('\\', '/').Contains("/wwwroot/") && !ThirdParty(Path.GetFileName(f))))
{
    var raw = File.ReadAllText(f);
    js.Add((Rel(f), raw, Scanner.Blank(raw, true), Scanner.LineStarts(raw), ""));
}
foreach (var f in Files("*.html").Where(f => f.Replace('\\', '/').Contains("/wwwroot/")))
{
    var raw = File.ReadAllText(f);
    var script = new char[raw.Length];
    var markup = raw.ToCharArray();
    for (int i = 0; i < raw.Length; i++) script[i] = raw[i] == '\n' || raw[i] == '\r' ? raw[i] : ' ';
    foreach (Match m in Regex.Matches(raw, @"<script(?![^>]*\bsrc=)[^>]*>(.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
    {
        var g = m.Groups[1];
        for (int i = g.Index; i < g.Index + g.Length; i++) { script[i] = raw[i]; if (raw[i] != '\n' && raw[i] != '\r') markup[i] = ' '; }
    }
    var sraw = new string(script);
    js.Add((Rel(f), sraw, Scanner.Blank(sraw, true), Scanner.LineStarts(raw), new string(markup)));
}

// ── parse ───────────────────────────────────────────────────────────────────────────────────
var funcs = new List<FuncInfo>();
var types = new List<TypeInfo>();
foreach (var c in cs)
{
    var (fn, ty) = Scanner.ParseCSharp(c.file, c.raw, c.blank);
    funcs.AddRange(fn); types.AddRange(ty);
}
foreach (var j in js) funcs.AddRange(Scanner.ParseJs(j.file, j.raw, j.blank));

var declPos = new HashSet<(string, int, string)>(funcs.Select(f => (f.File, f.NameLine, f.Name)));
var csFiles = cs.ToDictionary(c => c.file);
var jsFiles = js.ToDictionary(c => c.file);

// ── endpoints (minimal APIs) ─────────────────────────────────────────────────────────────────
// Each Map* call's span is tracked so calls inside the handler lambda are attributed to the route.
var endpoints = new List<(string verb, string route, string file, int line, int endLine)>();
foreach (var c in cs)
{
    foreach (Match m in Regex.Matches(c.raw, @"\.Map(Get|Post|Put|Delete|Patch)\(\s*""([^""]+)""|\.MapHub<(\w+)>\(\s*""([^""]+)"""))
    {
        int line = Scanner.LineOf(c.starts, m.Index);
        int start = m.Index + m.Value.IndexOf('(');
        int depth = 0, endPos = c.blank.Length - 1;
        for (int k = start; k < c.blank.Length; k++)
        {
            if (c.blank[k] == '(') depth++;
            else if (c.blank[k] == ')' && --depth == 0) { endPos = k; break; }
        }
        if (m.Groups[3].Success) endpoints.Add(("HUB", m.Groups[4].Value, c.file, line, line));
        else endpoints.Add((m.Groups[1].Value.ToUpperInvariant(), m.Groups[2].Value, c.file, line, Scanner.LineOf(c.starts, endPos)));
    }
}

// ── callers ─────────────────────────────────────────────────────────────────────────────────
string Where(string file, int line)
{
    if (csFiles.ContainsKey(file))
    {
        var ep = endpoints.FirstOrDefault(e => e.file == file && line >= e.line && line <= e.endLine);
        if (ep.route != null && ep.verb != "HUB") return $"endpoint {ep.verb} {ep.route}";
        var f = funcs.FirstOrDefault(x => x.File == file && x.Kind != "js" && line >= x.StartLine && line <= x.EndLine);
        if (f != null) return string.IsNullOrEmpty(f.TypeName) ? f.Name : $"{f.TypeName}.{f.Name}";
        var t = types.Where(x => x.File == file && line >= x.StartLine && line <= x.EndLine).OrderBy(x => x.EndLine - x.StartLine).FirstOrDefault();
        return t != null ? $"{t.Name} (property or field)" : $"{Path.GetFileName(file)} (top-level)";
    }
    var jf = funcs.Where(x => x.File == file && x.Kind == "js" && line >= x.StartLine && line <= x.EndLine).OrderBy(x => x.EndLine - x.StartLine).FirstOrDefault();
    return jf != null ? jf.Name : $"{Path.GetFileName(file)} (top-level script)";
}

bool IsTestFile(string f) => f.StartsWith("MRR.Tests/");
var callers = new Dictionary<FuncInfo, List<(string who, string file, int line)>>();
foreach (var f in funcs) callers[f] = new();

var csNames = funcs.Where(f => f.Kind is "method" or "decl").Select(f => f.Name).Distinct().ToList();
var jsNames = funcs.Where(f => f.Kind == "js").Select(f => f.Name).Distinct().ToList();
var csByName = funcs.Where(f => f.Kind is "method" or "decl").GroupBy(f => f.Name).ToDictionary(g => g.Key, g => g.ToList());
var ctorByType = funcs.Where(f => f.Kind == "ctor").GroupBy(f => f.TypeName.Split('.').Last()).ToDictionary(g => g.Key, g => g.ToList());
var jsByName = funcs.Where(f => f.Kind == "js").GroupBy(f => f.Name).ToDictionary(g => g.Key, g => g.ToList());

var csCall = new Regex(@"\b(" + string.Join("|", csNames.Select(Regex.Escape)) + @")\s*(?:<[^<>()]*>)?\s*\(", RegexOptions.Compiled);
var ctorCall = ctorByType.Count == 0 ? null : new Regex(@"\bnew\s+(?:[\w.]+\.)?(" + string.Join("|", ctorByType.Keys.Select(Regex.Escape)) + @")\b\s*(?:<[^<>()]*>)?\s*[\(\{]", RegexOptions.Compiled);
var jsCall = new Regex(@"\b(" + string.Join("|", jsNames.Select(Regex.Escape)) + @")\s*\(", RegexOptions.Compiled);

foreach (var c in cs)
{
    foreach (Match m in csCall.Matches(c.blank))
    {
        string name = m.Groups[1].Value;
        int line = Scanner.LineOf(c.starts, m.Index);
        if (declPos.Contains((c.file, line, name))) continue;
        if (Regex.IsMatch(c.blank.AsSpan(Math.Max(0, m.Index - 6), Math.Min(6, m.Index)).ToString(), @"\bnew\s*$")) continue;
        var who = Where(c.file, line);
        foreach (var f in csByName[name]) callers[f].Add((who, c.file, line));
    }
    if (ctorCall != null)
        foreach (Match m in ctorCall.Matches(c.blank))
        {
            int line = Scanner.LineOf(c.starts, m.Index);
            var who = Where(c.file, line);
            foreach (var f in ctorByType[m.Groups[1].Value]) callers[f].Add((who, c.file, line));
        }
}
foreach (var j in js)
{
    foreach (Match m in jsCall.Matches(j.blank))
    {
        string name = m.Groups[1].Value;
        int line = Scanner.LineOf(j.starts, m.Index);
        if (declPos.Contains((j.file, line, name))) continue;
        if (Regex.IsMatch(j.blank.AsSpan(Math.Max(0, m.Index - 9), Math.Min(9, m.Index)).ToString(), @"function\s*$")) continue;
        var who = Where(j.file, line);
        foreach (var f in jsByName[name]) callers[f].Add((who, j.file, line));
    }
    // inline handlers in markup: onclick="name(...)"
    if (j.markup.Length > 0)
        foreach (Match m in Regex.Matches(j.markup, @"\bon\w+\s*=\s*""([^""]*)"""))
            foreach (Match cm in jsCall.Matches(m.Groups[1].Value))
            {
                int line = Scanner.LineOf(j.starts, m.Groups[1].Index);
                foreach (var f in jsByName[cm.Groups[1].Value]) callers[f].Add(($"{Path.GetFileName(j.file)} (markup handler)", j.file, line));
            }
}
// SignalR hub methods are called from JS by string: connection.invoke("Name", ...)
var hubFile = cs.FirstOrDefault(c => Regex.IsMatch(c.raw, @"class\s+\w+\s*:\s*Hub\b"));
var hubTypes = cs.Where(c => Regex.IsMatch(c.raw, @"class\s+(\w+)\s*:\s*Hub\b")).SelectMany(c => Regex.Matches(c.raw, @"class\s+(\w+)\s*:\s*Hub\b").Select(m => m.Groups[1].Value)).ToHashSet();
foreach (var j in js)
    foreach (Match m in Regex.Matches(j.raw, @"\.(?:invoke|send)\(\s*['""](\w+)['""]"))
        foreach (var f in funcs.Where(f => f.Kind == "method" && hubTypes.Contains(f.TypeName) && f.Name == m.Groups[1].Value))
            callers[f].Add(($"JS connection.invoke (hub)", j.file, Scanner.LineOf(j.starts, m.Index)));

// ── markdown helpers ────────────────────────────────────────────────────────────────────────
string Esc(string s) => s.Replace("|", "\\|").Replace("\n", " ");
string LinkFile(string file, int line, string text) => $"[{Esc(text)}](../../{file.Replace(" ", "%20")}#L{line})";
string CallerCell(FuncInfo f)
{
    var list = callers[f];
    if (list.Count == 0) return "_none found_";
    var groups = list.GroupBy(x => x.who).Select(g => (who: g.Key, first: g.First(), n: g.Count())).OrderBy(g => IsTestFile(g.first.file)).ThenBy(g => g.first.file, StringComparer.Ordinal).ThenBy(g => g.first.line).ToList();
    var shown = groups.Take(8).Select(g => LinkFile(g.first.file, g.first.line, g.who) + (g.n > 1 ? $" ×{g.n}" : "") + (IsTestFile(g.first.file) ? " (test)" : ""));
    var s = string.Join("; ", shown);
    if (groups.Count > 8) s += $"; +{groups.Count - 8} more";
    return s;
}

var common = new HashSet<string> { "Add", "Remove", "Clear", "Get", "Set", "Contains", "Parse", "ToString", "Equals", "Dispose", "Send", "Start", "Stop", "Run", "Write", "Read", "Close", "Open", "Update", "Delete", "Insert", "Find", "Load", "Save", "Reset", "Init", "Create", "Count", "Print", "Connect", "Disconnect", "Next", "Select", "Invoke", "Execute", "Process", "Log", "Handle", "Show" };
string NameNote(FuncInfo f)
{
    var dict = f.Kind == "js" ? jsByName : csByName;
    if (f.Kind == "ctor") return "";
    var notes = new List<string>();
    int n = dict[f.Name].Count;
    if (n > 1) notes.Add($"name shared by {n} members");
    if (f.Kind != "js" && common.Contains(f.Name)) notes.Add("common name; framework calls may match");
    return notes.Count > 0 ? " ⚠ " + string.Join(", ", notes) : "";
}

// ── per-project files ───────────────────────────────────────────────────────────────────────
string Project(string file) => file.Contains('/') ? file[..file.IndexOf('/')] : "(root)";
var csFuncs = funcs.Where(f => f.Kind != "js").ToList();
var projects = csFuncs.Select(f => Project(f.File)).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
var summary = new List<(string name, int files, int funcs, int unref)>();

foreach (var proj in projects)
{
    var sb = new StringBuilder();
    var pf = csFuncs.Where(f => Project(f.File) == proj).ToList();
    sb.AppendLine($"# {proj} — functions");
    sb.AppendLine();
    sb.AppendLine("_Generated by `MRR.Tools.CodeReference`; do not edit by hand. Callers are matched by name only — see [README](README.md)._");
    sb.AppendLine();
    foreach (var g in pf.GroupBy(f => f.File).OrderBy(g => g.Key, StringComparer.Ordinal))
    {
        sb.AppendLine($"## {g.Key}");
        sb.AppendLine();
        sb.AppendLine("| Function | Signature | What it does | Called from |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var f in g.OrderBy(f => f.NameLine))
        {
            string label = (string.IsNullOrEmpty(f.TypeName) ? "" : f.TypeName + ".") + (f.Kind == "ctor" ? "(constructor)" : f.Name);
            string doc = f.Doc.Length == 0 ? "—" : (f.Doc.Length > 300 ? f.Doc[..297] + "..." : f.Doc);
            string sig = f.Attrs.Length > 0 && f.Attrs.Contains("Fact") || f.Attrs.Contains("Theory") ? "[test] " + f.Signature : f.Signature;
            if (f.Kind == "decl") sig = "[interface/abstract] " + sig;
            sb.AppendLine($"| {LinkFile(f.File, f.NameLine, label)} | `{Esc(sig)}` | {Esc(doc)} | {CallerCell(f)}{NameNote(f)} |");
        }
        sb.AppendLine();
    }
    File.WriteAllText(Path.Combine(outDir, proj + ".md"), sb.ToString().Replace("\r\n", "\n"), new UTF8Encoding(false));
}

// ── web (JS) ────────────────────────────────────────────────────────────────────────────────
{
    var sb = new StringBuilder();
    sb.AppendLine("# Web UI — JavaScript functions (wwwroot)");
    sb.AppendLine();
    sb.AppendLine("_Generated by `MRR.Tools.CodeReference`; do not edit by hand. Includes inline `<script>` blocks in the HTML pages. Third-party libraries (jQuery, SignalR, w3) are skipped. Callers are matched by name, including `onclick=\"...\"` handlers._");
    sb.AppendLine();
    foreach (var g in funcs.Where(f => f.Kind == "js").GroupBy(f => f.File).OrderBy(g => g.Key, StringComparer.Ordinal))
    {
        sb.AppendLine($"## {g.Key}");
        sb.AppendLine();
        sb.AppendLine("| Function | Signature | What it does | Called from |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var f in g.OrderBy(f => f.NameLine))
            sb.AppendLine($"| {LinkFile(f.File, f.NameLine, f.Name)} | `{Esc(f.Signature)}` | {(f.Doc.Length == 0 ? "—" : Esc(f.Doc.Length > 300 ? f.Doc[..297] + "..." : f.Doc))} | {CallerCell(f)}{NameNote(f)} |");
        sb.AppendLine();
    }
    File.WriteAllText(Path.Combine(outDir, "web.md"), sb.ToString().Replace("\r\n", "\n"), new UTF8Encoding(false));
}

// ── api: REST endpoints, SignalR ────────────────────────────────────────────────────────────
{
    var sb = new StringBuilder();
    sb.AppendLine("# HTTP endpoints and SignalR");
    sb.AppendLine();
    sb.AppendLine("_Generated by `MRR.Tools.CodeReference`; do not edit by hand. Only minimal-API `app.Map*` registrations are found; routes built another way are missed. \"Calls\" are the named C# methods invoked inside the handler. \"Browser callers\" are places in wwwroot that mention the route's static prefix._");
    sb.AppendLine();
    sb.AppendLine("| Verb | Route | Registered | Calls | Browser callers |");
    sb.AppendLine("|---|---|---|---|---|");
    foreach (var e in endpoints.OrderBy(e => e.route, StringComparer.Ordinal))
    {
        var inside = funcs.Where(f => f.Kind == "method" && callers[f].Any(c => c.file == e.file && c.line >= e.line && c.line <= e.endLine))
            .GroupBy(f => f.Name).Select(g => g.Count() == 1 ? $"{g.First().TypeName}.{g.Key}" : $"{g.Key} ⚠ (name on {g.Count()} types)").Take(8).ToList();
        string prefix = e.route.Contains('{') ? e.route[..e.route.IndexOf('{')] : e.route;
        var browser = new List<string>();
        if (e.verb != "HUB")
            foreach (var j in js)
                foreach (Match m in Regex.Matches(j.raw.Replace("\r", ""), Regex.Escape(prefix.TrimEnd('/')) + @"(?=['""/?`$])"))
                    browser.Add(LinkFile(j.file, Scanner.LineOf(j.starts, m.Index), $"{Path.GetFileName(j.file)}:{Scanner.LineOf(j.starts, m.Index)}"));
        // also non-inline html/js strings that sit in markup attributes
        sb.AppendLine($"| {e.verb} | `{Esc(e.route)}` | {LinkFile(e.file, e.line, Path.GetFileName(e.file) + ":" + e.line)} | {(inside.Count == 0 ? "—" : Esc(string.Join(", ", inside)))} | {(browser.Count == 0 ? "_none found_" : string.Join(" ", browser.Distinct().Take(6)))} |");
    }
    sb.AppendLine();
    sb.AppendLine("## SignalR events");
    sb.AppendLine();
    sb.AppendLine("| Event | Sent by (C#) | Listened for (JS) |");
    sb.AppendLine("|---|---|---|");
    var sent = new Dictionary<string, List<string>>();
    foreach (var c in cs)
        foreach (Match m in Regex.Matches(c.raw, @"SendAsync\(\s*""(\w+)"""))
        {
            int line = Scanner.LineOf(c.starts, m.Index);
            if (!sent.TryGetValue(m.Groups[1].Value, out var l)) sent[m.Groups[1].Value] = l = new();
            l.Add(LinkFile(c.file, line, Where(c.file, line)));
        }
    var heard = new Dictionary<string, List<string>>();
    foreach (var j in js)
        foreach (Match m in Regex.Matches(j.raw, @"\.on\(\s*['""](\w+)['""]"))
        {
            int line = Scanner.LineOf(j.starts, m.Index);
            if (!heard.TryGetValue(m.Groups[1].Value, out var l)) heard[m.Groups[1].Value] = l = new();
            l.Add(LinkFile(j.file, line, $"{Path.GetFileName(j.file)}:{line}"));
        }
    foreach (var ev in sent.Keys.Union(heard.Keys).OrderBy(x => x, StringComparer.Ordinal))
        sb.AppendLine($"| `{ev}` | {(sent.TryGetValue(ev, out var a) ? string.Join("; ", a.Distinct().Take(6)) : "_none found_")} | {(heard.TryGetValue(ev, out var b) ? string.Join("; ", b.Distinct().Take(6)) : "_none found_")} |");
    sb.AppendLine();
    sb.AppendLine("## Hub methods callable from the browser");
    sb.AppendLine();
    sb.AppendLine("| Method | Called from |");
    sb.AppendLine("|---|---|");
    var hubMethods = funcs.Where(f => f.Kind == "method" && hubTypes.Contains(f.TypeName) && f.Mods.Contains("public")).ToList();
    foreach (var f in hubMethods)
        sb.AppendLine($"| {LinkFile(f.File, f.NameLine, f.TypeName + "." + f.Name)} | {CallerCell(f)} |");
    if (hubMethods.Count == 0) sb.AppendLine("| _none_ — the hub is broadcast-only (`DataHub` declares no methods); clients only listen. | — |");
    File.WriteAllText(Path.Combine(outDir, "api.md"), sb.ToString().Replace("\r\n", "\n"), new UTF8Encoding(false));
}

// ── data: DB tables, robot commands, config keys ────────────────────────────────────────────
{
    var sb = new StringBuilder();
    sb.AppendLine("# Data and configuration touch points");
    sb.AppendLine();
    sb.AppendLine("_Generated by `MRR.Tools.CodeReference`; do not edit by hand. Text matches only — a table name that is also an ordinary word or class name (e.g. `Options`) can over-match._");
    sb.AppendLine();
    sb.AppendLine("## Database tables (from `install/MRRDatabase.sql`) and the C# that mentions them");
    sb.AppendLine();
    sb.AppendLine("| Table | Files | Members that mention it |");
    sb.AppendLine("|---|---|---|");
    string sqlPath = Path.Combine(root, "install", "MRRDatabase.sql");
    var tables = File.Exists(sqlPath)
        ? Regex.Matches(File.ReadAllText(sqlPath), @"CREATE TABLE `(\w+)`").Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList()
        : new List<string>();
    foreach (var t in tables)
    {
        var rx = new Regex(@"\b" + Regex.Escape(t) + @"\b");
        var hits = new List<(string file, int line)>();
        foreach (var c in cs.Where(c => !IsTestFile(c.file)))
        {
            var lines = c.raw.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var tl = lines[i].TrimStart();
                if (tl.StartsWith("//")) continue;
                if (rx.IsMatch(lines[i])) hits.Add((c.file, i + 1));
            }
        }
        var files = hits.GroupBy(h => h.file).Select(g => $"{Path.GetFileName(g.Key)} ×{g.Count()}").Take(6);
        var who = hits.Select(h => Where(h.file, h.line)).Distinct().Take(8);
        sb.AppendLine($"| `{t}` | {(hits.Count == 0 ? "_none found_" : Esc(string.Join(", ", files)))} | {(hits.Count == 0 ? "—" : Esc(string.Join(", ", who)))} |");
    }
    sb.AppendLine();
    sb.AppendLine("## Robot WebSocket commands (`cmd_id`)");
    sb.AppendLine();
    sb.AppendLine("| cmd_id | Uses | Where |");
    sb.AppendLine("|---|---|---|");
    var cmds = new Dictionary<string, List<(string file, int line)>>();
    foreach (var c in cs)
        foreach (Match m in Regex.Matches(c.raw, @"cmd_id\s*=\s*""(\w+)"""))
        {
            if (!cmds.TryGetValue(m.Groups[1].Value, out var l)) cmds[m.Groups[1].Value] = l = new();
            l.Add((c.file, Scanner.LineOf(c.starts, m.Index)));
        }
    foreach (var kv in cmds.OrderBy(k => k.Key, StringComparer.Ordinal))
        sb.AppendLine($"| `{kv.Key}` | {kv.Value.Count} | {string.Join("; ", kv.Value.Select(v => LinkFile(v.file, v.line, Where(v.file, v.line))).Distinct().Take(6))} |");
    sb.AppendLine();
    sb.AppendLine("## Configuration keys (`MRR/appsettings.json`) — names only, values are deliberately not listed");
    sb.AppendLine();
    sb.AppendLine("| Key | Referenced in |");
    sb.AppendLine("|---|---|");
    string cfg = Path.Combine(root, "MRR", "appsettings.json");
    if (File.Exists(cfg))
    {
        var keys = new List<string>();
        void Walk(JsonElement e, string prefix)
        {
            if (e.ValueKind == JsonValueKind.Object)
                foreach (var p in e.EnumerateObject()) Walk(p.Value, prefix.Length == 0 ? p.Name : prefix + ":" + p.Name);
            else keys.Add(prefix);
        }
        using (var doc = JsonDocument.Parse(File.ReadAllText(cfg), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }))
            Walk(doc.RootElement, "");
        foreach (var k in keys.Where(k => !k.EndsWith("_comment")))
        {
            string last = k.Split(':').Last();
            var hits = new List<string>();
            foreach (var c in cs)
                foreach (Match m in Regex.Matches(c.raw, "\"(" + Regex.Escape(k) + "|" + Regex.Escape(last) + ")\""))
                {
                    int line = Scanner.LineOf(c.starts, m.Index);
                    hits.Add(LinkFile(c.file, line, Where(c.file, line)));
                }
            sb.AppendLine($"| `{k}` | {(hits.Count == 0 ? "_no string match in C# (the framework may read it, e.g. `Urls`, `Logging`)_" : string.Join("; ", hits.Distinct().Take(6)))} |");
        }
    }
    File.WriteAllText(Path.Combine(outDir, "data.md"), sb.ToString().Replace("\r\n", "\n"), new UTF8Encoding(false));
}

// ── possibly unreferenced ───────────────────────────────────────────────────────────────────
var unref = funcs.Where(f => f.Kind is "method" or "js" && callers[f].Count == 0).ToList();
// A name used as a value (handler, method group: `Task.Run(Name)`, `+= Name`, `addEventListener('x', Name)`) is a
// reference even with no call.
var valueRefs = new Dictionary<FuncInfo, List<(string file, int line)>>();
foreach (var f in unref)
{
    var rx = new Regex(@"(?<![\w.])" + Regex.Escape(f.Name) + @"\b(?!\s*\()");
    var hits = new List<(string, int)>();
    if (f.Kind == "js")
        foreach (var j in js) foreach (Match m in rx.Matches(j.blank)) { int ln = Scanner.LineOf(j.starts, m.Index); if (!(j.file == f.File && ln == f.NameLine)) hits.Add((j.file, ln)); }
    else
        foreach (var c in cs) foreach (Match m in rx.Matches(c.blank)) { int ln = Scanner.LineOf(c.starts, m.Index); if (!(c.file == f.File && ln >= f.StartLine && ln <= f.NameLine)) hits.Add((c.file, ln)); }
    valueRefs[f] = hits;
}
bool Entry(FuncInfo f) =>
    f.Name == "Main" || f.Mods.Contains("override") || f.Attrs.Contains("Fact") || f.Attrs.Contains("Theory") || f.Attrs.Contains("HttpGet") ||
    hubTypes.Contains(f.TypeName) || f.Name is "ExecuteAsync" or "StartAsync" or "StopAsync" or "OnConnectedAsync" or "OnDisconnectedAsync";
{
    var sb = new StringBuilder();
    sb.AppendLine("# Functions with no call sites found");
    sb.AppendLine();
    sb.AppendLine("_Generated by `MRR.Tools.CodeReference`; do not edit by hand._");
    sb.AppendLine();
    sb.AppendLine("**Do not delete from this list without checking.** A function lands here when no `Name(` call was found anywhere in the C# or web code. That is not proof it is dead: it may be called through a method group (`Task.Run(Method)`, `+= Handler`), reflection, a framework convention, or a test, which this scan cannot see. Framework entry points (overrides, `Main`, hub methods, `[Fact]` tests, `ExecuteAsync`, ...) are listed separately.");
    sb.AppendLine();
    sb.AppendLine("Still-open features are expected here: Haywire and Option card effects are unfinished by design (see `install/todo.md`), so the matching code is **not** to be removed.");
    sb.AppendLine();
    foreach (var (title, items, withRefs) in new[] {
        ("Possibly unused (name never appears outside its declaration)", unref.Where(f => !Entry(f) && valueRefs[f].Count == 0).ToList(), false),
        ("Referenced but never called (probably an event handler or method group)", unref.Where(f => !Entry(f) && valueRefs[f].Count > 0).ToList(), true),
        ("Framework / convention entry points (expected to have no explicit callers)", unref.Where(Entry).ToList(), false) })
    {
        sb.AppendLine($"## {title} ({items.Count})");
        sb.AppendLine();
        sb.AppendLine(withRefs ? "| Function | Signature | Referenced at |" : "| Function | Signature |");
        sb.AppendLine(withRefs ? "|---|---|---|" : "|---|---|");
        foreach (var f in items.OrderBy(f => f.File, StringComparer.Ordinal).ThenBy(f => f.NameLine))
            sb.AppendLine($"| {LinkFile(f.File, f.NameLine, (string.IsNullOrEmpty(f.TypeName) ? "" : f.TypeName + ".") + f.Name)} | `{Esc(f.Signature)}` |" + (withRefs ? " " + string.Join("; ", valueRefs[f].Take(3).Select(h => LinkFile(h.file, h.line, $"{Path.GetFileName(h.file)}:{h.line}"))) + " |" : ""));
        sb.AppendLine();
    }
    File.WriteAllText(Path.Combine(outDir, "unreferenced.md"), sb.ToString().Replace("\r\n", "\n"), new UTF8Encoding(false));
}

// ── README ──────────────────────────────────────────────────────────────────────────────────
{
    var sb = new StringBuilder();
    sb.AppendLine("# Code reference");
    sb.AppendLine();
    sb.AppendLine("Generated index of the functions in this repository: what each one is, and where it is called. **Regenerate** (do not hand-edit):");
    sb.AppendLine();
    sb.AppendLine("```");
    sb.AppendLine("dotnet run --project MRR.Tools.CodeReference");
    sb.AppendLine("```");
    sb.AppendLine();
    sb.AppendLine("## How reliable is it?");
    sb.AppendLine();
    sb.AppendLine("This is a regex and brace-depth scan, **not** a compiler. Treat it as a map to search from, not as proof.");
    sb.AppendLine();
    sb.AppendLine("- **Callers are matched by name only.** If a name is declared on several types (or is a common framework name like `Add`), the table flags it with ⚠ and the caller list may include calls to other types' methods. Most names here are unique.");
    sb.AppendLine("- **Missed calls:** method-group references (`Task.Run(Method)`, `+= Handler`), reflection, calls through string names (other than SignalR `invoke`), local functions, and anything inside an unparsed construct.");
    sb.AppendLine("- **\"What it does\"** is the member's `///` summary (C#) or the `//` comment block right above it (JS). `—` means there is none; the text was not invented. Adding a doc comment in the source fills it in on the next run.");
    sb.AppendLine("- **Not covered:** the ESP32 firmware (`esp32-player-station/`, C++), shell scripts in `install/`, SQL, and properties/fields (only methods and constructors).");
    sb.AppendLine();
    sb.AppendLine("## Contents");
    sb.AppendLine();
    sb.AppendLine("| File | Covers | Members |");
    sb.AppendLine("|---|---|---|");
    foreach (var proj in projects)
        sb.AppendLine($"| [{proj}.md]({proj}.md) | C# methods and constructors in `{proj}/` | {csFuncs.Count(f => Project(f.File) == proj)} |");
    sb.AppendLine($"| [web.md](web.md) | JavaScript in `MRR/wwwroot` (including inline page scripts) | {funcs.Count(f => f.Kind == "js")} |");
    sb.AppendLine($"| [api.md](api.md) | {endpoints.Count(e => e.verb != "HUB")} HTTP endpoints, SignalR events, hub methods | — |");
    sb.AppendLine("| [data.md](data.md) | Database tables, robot `cmd_id` commands, `appsettings.json` keys | — |");
    sb.AppendLine($"| [unreferenced.md](unreferenced.md) | Functions with no call sites found | {unref.Count(f => !Entry(f) && valueRefs[f].Count == 0)} |");
    sb.AppendLine();
    sb.AppendLine("Related hand-written docs: [PROJECT_STATUS.md](../../PROJECT_STATUS.md) (how to run it), [CLAUDE.md](../../CLAUDE.md) (architecture and state machine), [API_DECOMPOSITION_DESIGN.md](../API_DECOMPOSITION_DESIGN.md).");
    File.WriteAllText(Path.Combine(outDir, "README.md"), sb.ToString().Replace("\r\n", "\n"), new UTF8Encoding(false));
}

Console.WriteLine($"{csFuncs.Count} C# members, {funcs.Count(f => f.Kind == "js")} JS functions, {endpoints.Count} endpoints, {unref.Count} with no callers -> {Rel(outDir)}");
