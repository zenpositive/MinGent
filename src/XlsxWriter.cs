using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MinGent
{
    class XlsxCell
    {
        public const int Empty = 0, Text = 1, Number = 2, Bool = 3, Formula = 4;
        public int Kind;
        public string Value = "";
        public double Num;
        public string Format;
        public string Link;
        public bool Bold;
    }

    class XlsxSheet
    {
        public string Name = "";
        public readonly List<List<XlsxCell>> Rows = new List<List<XlsxCell>>();
        public bool Header;
    }

    static class XlsxWriter
    {
        const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        const int MaxRows = 1048576, MaxCols = 16384, MaxText = 32767;
        const long MaxCells = 4000000;

        static void TooMany(string sheet, int rows, List<string> problems)
        {
            if (problems != null) problems.Add("the sheet " + sheet + " was cut after " + rows.ToString(CultureInfo.InvariantCulture) + " rows, because a workbook made by MinGent holds at most 4 million cells");
        }

        static readonly HashSet<string> SafeFunctions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ABS", "ACOS", "ACOSH", "AND", "ASIN", "ASINH", "ATAN", "ATAN2", "ATANH", "AVERAGE", "AVERAGEA", "AVERAGEIF", "AVERAGEIFS",
            "CEILING", "CEILING.MATH", "CHAR", "CHOOSE", "CLEAN", "CODE", "COLUMN", "COLUMNS", "CONCAT", "CONCATENATE", "CORREL", "COS", "COSH",
            "COUNT", "COUNTA", "COUNTBLANK", "COUNTIF", "COUNTIFS", "COVAR", "DATE", "DATEDIF", "DATEVALUE", "DAY", "DAYS", "DEGREES", "DOLLAR",
            "EDATE", "EOMONTH", "EVEN", "EXACT", "EXP", "FACT", "FALSE", "FIND", "FIXED", "FLOOR", "FLOOR.MATH", "FORECAST", "FV", "GROWTH",
            "HLOOKUP", "HOUR", "IF", "IFERROR", "IFNA", "IFS", "INDEX", "INT", "IPMT", "IRR", "ISBLANK", "ISERR", "ISERROR", "ISEVEN", "ISLOGICAL",
            "ISNA", "ISNONTEXT", "ISNUMBER", "ISODD", "ISOWEEKNUM", "ISTEXT", "LARGE", "LEFT", "LEN", "LN", "LOG", "LOG10", "LOOKUP", "LOWER",
            "MATCH", "MAX", "MAXA", "MAXIFS", "MEDIAN", "MID", "MIN", "MINA", "MINIFS", "MINUTE", "MOD", "MODE", "MODE.SNGL", "MONTH", "MROUND",
            "N", "NA", "NETWORKDAYS", "NOT", "NOW", "NPER", "NPV", "ODD", "OR", "PERCENTILE", "PERCENTILE.INC", "PERCENTRANK", "PI", "PMT",
            "POWER", "PPMT", "PRODUCT", "PROPER", "PV", "QUARTILE", "QUARTILE.INC", "QUOTIENT", "RADIANS", "RAND", "RANDBETWEEN", "RANK",
            "RANK.AVG", "RANK.EQ", "RATE", "REPLACE", "REPT", "RIGHT", "ROUND", "ROUNDDOWN", "ROUNDUP", "ROW", "ROWS", "SEARCH", "SECOND",
            "SIGN", "SIN", "SINH", "SLOPE", "SMALL", "SQRT", "STDEV", "STDEV.P", "STDEV.S", "STDEVA", "STDEVP", "SUBSTITUTE", "SUBTOTAL", "SUM",
            "SUMIF", "SUMIFS", "SUMPRODUCT", "SUMSQ", "SWITCH", "T", "TAN", "TANH", "TEXT", "TEXTJOIN", "TIME", "TIMEVALUE", "TODAY", "TREND",
            "TRIM", "TRUE", "TRUNC", "UPPER", "VALUE", "VAR", "VAR.P", "VAR.S", "VARP", "VLOOKUP", "WEEKDAY", "WEEKNUM", "WORKDAY", "XLOOKUP",
            "XMATCH", "YEAR", "YEARFRAC"
        };

        static readonly HashSet<string> Future = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CEILING.MATH", "CONCAT", "DAYS", "FLOOR.MATH", "IFNA", "IFS", "ISOWEEKNUM", "MAXIFS", "MINIFS", "MODE.SNGL", "PERCENTILE.INC",
            "QUARTILE.INC", "RANK.AVG", "RANK.EQ", "STDEV.P", "STDEV.S", "SWITCH", "TEXTJOIN", "VAR.P", "VAR.S", "XLOOKUP", "XMATCH"
        };

        static readonly Regex StringLit = new Regex("\"(?:[^\"]|\"\")*\"", RegexOptions.CultureInvariant);
        static readonly Regex FuncName = new Regex(@"(?<![A-Za-z0-9_.$])([A-Za-z_][A-Za-z0-9_.]*)\s*\(", RegexOptions.CultureInvariant);
        static readonly Regex PlainNumber = new Regex(@"^[-+]?(\d+(\.\d+)?|\.\d+)([eE][-+]?\d{1,3})?$", RegexOptions.CultureInvariant);
        static readonly Regex Grouped = new Regex(@"^[-+]?\d{1,3}(,\d{3})+(\.\d+)?$", RegexOptions.CultureInvariant);
        static readonly Regex GroupedEu = new Regex(@"^[-+]?\d{1,3}(\.\d{3})+(,\d+)?$", RegexOptions.CultureInvariant);
        static readonly Regex CommaDecimal = new Regex(@"^[-+]?\d+,\d+$", RegexOptions.CultureInvariant);
        static readonly Regex IsoDate = new Regex(@"^(\d{4})-(\d{2})-(\d{2})(?:[ T](\d{2}):(\d{2})(?::(\d{2}))?)?$", RegexOptions.CultureInvariant);

        public static bool SafeFormula(string f, out string bad)
        {
            bad = null;
            string s = StringLit.Replace(f, "\"\"");
            if (s.IndexOf('[') >= 0 || s.IndexOf('|') >= 0 || s.IndexOf("://", StringComparison.Ordinal) >= 0 || s.IndexOf(":\\", StringComparison.Ordinal) >= 0 || s.IndexOf("\\\\", StringComparison.Ordinal) >= 0)
            {
                bad = "a link to another file or program";
                return false;
            }
            foreach (Match m in FuncName.Matches(s))
            {
                string n = m.Groups[1].Value;
                if (n.StartsWith("_xlfn.", StringComparison.OrdinalIgnoreCase)) n = n.Substring(6);
                if (n.StartsWith("_xlws.", StringComparison.OrdinalIgnoreCase)) n = n.Substring(6);
                if (!SafeFunctions.Contains(n)) { bad = n.ToUpperInvariant(); return false; }
            }
            return true;
        }

        static string Stored(string f)
        {
            List<string> saved = new List<string>();
            string s = StringLit.Replace(f, delegate(Match m) { saved.Add(m.Value); return "\u0001" + (saved.Count - 1).ToString(CultureInfo.InvariantCulture) + "\u0001"; });
            s = FuncName.Replace(s, delegate(Match m)
            {
                string n = m.Groups[1].Value;
                if (n.StartsWith("_xlfn.", StringComparison.OrdinalIgnoreCase)) return m.Value;
                return Future.Contains(n) ? "_xlfn." + n.ToUpperInvariant() + m.Value.Substring(n.Length) : m.Value;
            });
            s = Regex.Replace(s, "\u0001(\\d+)\u0001", delegate(Match m) { return saved[int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)]; });
            return s;
        }

        public static XlsxCell Typed(string raw, bool decimalComma, string where, List<string> problems)
        {
            XlsxCell c = new XlsxCell();
            string t = DocModel.Clean(raw ?? "");
            string s = t.Trim();
            if (s.Length == 0) return c;
            if (s.Length > 1 && s[0] == '=')
            {
                string f = s.Substring(1);
                string bad;
                if (SafeFormula(f, out bad))
                {
                    c.Kind = XlsxCell.Formula;
                    c.Value = Stored(f);
                    return c;
                }
                string note = (where != null ? where + ": " : "") + "the formula uses " + bad + ", which MinGent does not put in spreadsheets, so it was saved as text";
                if (problems != null && problems.Count < 20 && !problems.Contains(note)) problems.Add(note);
                c.Kind = XlsxCell.Text;
                c.Value = t;
                return c;
            }
            if (s[0] == '\'' && s.Length > 1) { c.Kind = XlsxCell.Text; c.Value = s.Substring(1); return c; }
            double v;
            string body = s;
            string fmt = null;
            bool pct = false;
            string cur = null;
            bool curAfter = false;
            if (body.EndsWith("%")) { pct = true; body = body.Substring(0, body.Length - 1).TrimEnd(); }
            else
            {
                string sign = "";
                if (body.StartsWith("-") || body.StartsWith("+")) { sign = body.Substring(0, 1); body = body.Substring(1); }
                if (body.Length > 1 && "$\u20AC\u00A3\u00A5".IndexOf(body[0]) >= 0) { cur = body.Substring(0, 1); body = body.Substring(1).TrimStart(); }
                else if (body.Length > 1 && "\u20AC\u00A3".IndexOf(body[body.Length - 1]) >= 0) { cur = body.Substring(body.Length - 1); body = body.Substring(0, body.Length - 1).TrimEnd(); curAfter = true; }
                body = sign + body;
            }
            string digits = body.TrimStart('-', '+');
            bool leadingZero = digits.Length > 1 && digits[0] == '0' && digits[1] != '.' && digits[1] != ',';
            if (!leadingZero && Number(body, decimalComma, out v, out fmt) && digits.Replace(",", "").Replace(".", "").Length <= 15)
            {
                c.Kind = XlsxCell.Number;
                if (pct)
                {
                    int dec = Decimals(body, decimalComma);
                    c.Num = v / 100.0;
                    c.Format = dec == 0 ? "0%" : "0." + new string('0', Math.Min(6, dec)) + "%";
                    return c;
                }
                c.Num = v;
                if (cur != null)
                {
                    string pattern = cur == "\u00A5" && Decimals(body, decimalComma) == 0 ? "#,##0" : "#,##0.00";
                    c.Format = curAfter ? pattern + " \"" + cur + "\"" : "\"" + cur + "\"" + pattern;
                    return c;
                }
                if (fmt == null)
                {
                    int dec = Decimals(body, decimalComma);
                    if (dec > 0 && dec <= 10 && body.IndexOf('e') < 0 && body.IndexOf('E') < 0 && body[body.Length - 1] == '0') fmt = "0." + new string('0', dec);
                }
                c.Format = fmt;
                return c;
            }
            Match d = IsoDate.Match(s);
            if (d.Success)
            {
                int y = int.Parse(d.Groups[1].Value, CultureInfo.InvariantCulture), mo = int.Parse(d.Groups[2].Value, CultureInfo.InvariantCulture), da = int.Parse(d.Groups[3].Value, CultureInfo.InvariantCulture);
                if (y >= 1900 && y <= 9999 && mo >= 1 && mo <= 12 && da >= 1 && da <= DateTime.DaysInMonth(y, mo))
                {
                    DateTime when = new DateTime(y, mo, da);
                    bool time = d.Groups[4].Success;
                    if (time)
                    {
                        int hh = int.Parse(d.Groups[4].Value, CultureInfo.InvariantCulture), mm = int.Parse(d.Groups[5].Value, CultureInfo.InvariantCulture);
                        int ss = d.Groups[6].Success ? int.Parse(d.Groups[6].Value, CultureInfo.InvariantCulture) : 0;
                        if (hh > 23 || mm > 59 || ss > 59) { c.Kind = XlsxCell.Text; c.Value = t; return c; }
                        when = when.AddHours(hh).AddMinutes(mm).AddSeconds(ss);
                    }
                    c.Kind = XlsxCell.Number;
                    c.Num = (when - new DateTime(1899, 12, 30)).TotalDays;
                    c.Format = time ? (d.Groups[6].Success ? "yyyy-mm-dd hh:mm:ss" : "yyyy-mm-dd hh:mm") : "yyyy-mm-dd";
                    return c;
                }
            }
            string low = s.ToLowerInvariant();
            if (low == "true" || low == "false") { c.Kind = XlsxCell.Bool; c.Num = low == "true" ? 1 : 0; return c; }
            c.Kind = XlsxCell.Text;
            c.Value = t;
            return c;
        }

        static int Decimals(string body, bool decimalComma)
        {
            int at = body.LastIndexOf(decimalComma ? ',' : '.');
            if (at < 0) return 0;
            int n = 0;
            for (int i = at + 1; i < body.Length && char.IsDigit(body[i]); i++) n++;
            return n;
        }

        static bool Number(string s, bool decimalComma, out double v, out string fmt)
        {
            fmt = null;
            v = 0;
            if (s.Length == 0 || s.Length > 40) return false;
            if (PlainNumber.IsMatch(s))
            {
                if (decimalComma && s.IndexOf('.') >= 0 && s.IndexOf('e') < 0 && s.IndexOf('E') < 0) return false;
                return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && !double.IsInfinity(v);
            }
            if (!decimalComma && Grouped.IsMatch(s))
            {
                if (!double.TryParse(s.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return false;
                int dec = Decimals(s, false);
                fmt = dec == 0 ? "#,##0" : "#,##0." + new string('0', Math.Min(6, dec));
                return true;
            }
            if (decimalComma && (GroupedEu.IsMatch(s) || CommaDecimal.IsMatch(s)))
            {
                if (!double.TryParse(s.Replace(".", "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return false;
                int dec = Decimals(s, true);
                fmt = s.IndexOf('.') >= 0 ? (dec == 0 ? "#,##0" : "#,##0." + new string('0', Math.Min(6, dec))) : (dec == 0 ? null : "0." + new string('0', Math.Min(6, dec)));
                return true;
            }
            return false;
        }

        public static string SheetName(string want, List<XlsxSheet> sheets)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char ch in DocModel.Clean(want ?? "")) if ("[]:*?/\\\n\t".IndexOf(ch) < 0) sb.Append(ch);
            string n = Regex.Replace(sb.ToString(), @"\s+", " ").Trim().Trim('\'').Trim();
            if (n.Length > 31) n = n.Substring(0, 31).TrimEnd();
            if (n.Length == 0 || string.Equals(n, "History", StringComparison.OrdinalIgnoreCase)) n = "Sheet" + (sheets.Count + 1).ToString(CultureInfo.InvariantCulture);
            string baseName = n;
            int k = 2;
            while (true)
            {
                bool clash = false;
                foreach (XlsxSheet s in sheets) if (string.Equals(s.Name, n, StringComparison.OrdinalIgnoreCase)) { clash = true; break; }
                if (!clash) return n;
                string suffix = " (" + (k++).ToString(CultureInfo.InvariantCulture) + ")";
                n = (baseName.Length + suffix.Length > 31 ? baseName.Substring(0, 31 - suffix.Length) : baseName) + suffix;
            }
        }

        public static string Col(int i)
        {
            StringBuilder sb = new StringBuilder();
            i++;
            while (i > 0) { int r = (i - 1) % 26; sb.Insert(0, (char)('A' + r)); i = (i - 1) / 26; }
            return sb.ToString();
        }

        public static List<XlsxSheet> Parse(string content, List<string> problems, out string error)
        {
            error = null;
            List<XlsxSheet> sheets = new List<XlsxSheet>();
            string text = (content ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
            string trimmed = text.Trim();
            if (trimmed.Length == 0) { error = "the content is empty. Write the rows as CSV, a Markdown table or JSON."; return null; }
            if (trimmed[0] == '[' || trimmed[0] == '{')
            {
                object json = Json.TryParse(trimmed);
                if (json != null)
                {
                    FromJson(json, sheets, problems);
                    if (sheets.Count == 0) { error = "the JSON has no rows. Use an array of rows (arrays or objects), or {\"sheets\": [{\"name\": \"...\", \"rows\": [...]}]}."; return null; }
                    return sheets;
                }
                if (trimmed[0] == '{') { error = "the content starts like JSON but is not valid JSON."; return null; }
            }
            if (FromMarkdown(text, sheets, problems)) return sheets;
            XlsxSheet csv = FromDelimited(text, problems);
            if (csv != null) { csv.Name = SheetName("Sheet1", sheets); sheets.Add(csv); return sheets; }
            error = "the content is not a table. For a spreadsheet, write the rows as CSV (one row per line), as a Markdown table, or as JSON rows.";
            return null;
        }

        static void FromJson(object json, List<XlsxSheet> sheets, List<string> problems)
        {
            List<object> arr = json as List<object>;
            if (arr != null) { AddJsonSheet(null, arr, sheets, problems, null); return; }
            Dictionary<string, object> obj = json as Dictionary<string, object>;
            if (obj == null) return;
            List<object> list = Json.List(obj, "sheets");
            if (list != null)
            {
                foreach (object o in list)
                {
                    Dictionary<string, object> sh = o as Dictionary<string, object>;
                    if (sh == null) continue;
                    List<object> rows = Json.List(sh, "rows") ?? Json.List(sh, "data");
                    if (rows == null) continue;
                    object hv;
                    bool? header = sh.TryGetValue("header", out hv) && hv is bool ? (bool?)(bool)hv : null;
                    AddJsonSheet(Json.Str(sh, "name") ?? Json.Str(sh, "title"), rows, sheets, problems, header);
                }
                return;
            }
            List<object> single = Json.List(obj, "rows") ?? Json.List(obj, "data");
            if (single != null) { AddJsonSheet(Json.Str(obj, "name") ?? Json.Str(obj, "title"), single, sheets, problems, null); return; }
            foreach (KeyValuePair<string, object> kv in obj)
            {
                List<object> rows = kv.Value as List<object>;
                if (rows != null) AddJsonSheet(kv.Key, rows, sheets, problems, null);
            }
        }

        static void AddJsonSheet(string name, List<object> rows, List<XlsxSheet> sheets, List<string> problems, bool? header)
        {
            XlsxSheet sh = new XlsxSheet();
            sh.Name = SheetName(name ?? "Sheet" + (sheets.Count + 1).ToString(CultureInfo.InvariantCulture), sheets);
            bool objects = false;
            List<string> keys = new List<string>();
            HashSet<string> known = new HashSet<string>(StringComparer.Ordinal);
            foreach (object r in rows)
            {
                Dictionary<string, object> o = r as Dictionary<string, object>;
                if (o == null) continue;
                objects = true;
                foreach (string k in o.Keys) if (keys.Count < MaxCols && known.Add(k)) keys.Add(k);
            }
            if (objects)
            {
                List<XlsxCell> head = new List<XlsxCell>();
                foreach (string k in keys) head.Add(new XlsxCell { Kind = XlsxCell.Text, Value = DocModel.Clean(k) });
                sh.Rows.Add(head);
                long limit = Math.Min(MaxRows, Math.Max(2, MaxCells / Math.Max(1, keys.Count)));
                foreach (object r in rows)
                {
                    Dictionary<string, object> o = r as Dictionary<string, object>;
                    if (o == null) continue;
                    if (sh.Rows.Count >= limit) { if (limit < MaxRows) TooMany(sh.Name, sh.Rows.Count, problems); break; }
                    List<XlsxCell> row = new List<XlsxCell>();
                    foreach (string k in keys)
                    {
                        object v;
                        row.Add(o.TryGetValue(k, out v) ? JsonCell(v, sh, sh.Rows.Count, row.Count, problems) : new XlsxCell());
                    }
                    sh.Rows.Add(row);
                }
                sh.Header = header ?? true;
            }
            else
            {
                long count = 0;
                foreach (object r in rows)
                {
                    List<object> cells = r as List<object>;
                    int width = cells != null ? Math.Min(cells.Count, MaxCols) : 1;
                    if (count + width > MaxCells) { TooMany(sh.Name, sh.Rows.Count, problems); break; }
                    count += width;
                    List<XlsxCell> row = new List<XlsxCell>();
                    if (cells != null) { foreach (object v in cells) { if (row.Count >= MaxCols) break; row.Add(JsonCell(v, sh, sh.Rows.Count, row.Count, problems)); } }
                    else row.Add(JsonCell(r, sh, sh.Rows.Count, 0, problems));
                    sh.Rows.Add(row);
                    if (sh.Rows.Count >= MaxRows) break;
                }
                sh.Header = header ?? LooksLikeHeader(sh);
            }
            if (sh.Rows.Count > 0) sheets.Add(sh);
        }

        static XlsxCell JsonCell(object v, XlsxSheet sh, int row, int col, List<string> problems)
        {
            if (v == null) return new XlsxCell();
            if (v is double) { double d = (double)v; return double.IsNaN(d) || double.IsInfinity(d) ? new XlsxCell() : new XlsxCell { Kind = XlsxCell.Number, Num = d }; }
            if (v is bool) return new XlsxCell { Kind = XlsxCell.Bool, Num = (bool)v ? 1 : 0 };
            string s = v as string;
            if (s != null) return Typed(s, false, sh.Name + "!" + Col(col) + (row + 1).ToString(CultureInfo.InvariantCulture), problems);
            return new XlsxCell { Kind = XlsxCell.Text, Value = DocModel.Clean(Json.Write(v)) };
        }

        static bool LooksLikeHeader(XlsxSheet sh)
        {
            if (sh.Rows.Count < 2) return false;
            List<XlsxCell> first = sh.Rows[0];
            int text = 0, filled = 0;
            foreach (XlsxCell c in first)
            {
                if (c.Kind == XlsxCell.Empty) continue;
                filled++;
                if (c.Kind == XlsxCell.Text) text++;
            }
            return filled > 0 && text == filled;
        }

        static bool FromMarkdown(string text, List<XlsxSheet> sheets, List<string> problems)
        {
            if (text.IndexOf('|') < 0) return false;
            List<MdBlock> blocks = Markdown.Parse(text);
            bool any = false;
            foreach (MdBlock b in blocks) if (b.Kind == MdKind.Table) { any = true; break; }
            if (!any) return false;
            string heading = null;
            bool skipped = false;
            int tables = 0;
            foreach (MdBlock b in blocks)
            {
                if (b.Kind == MdKind.Heading) { heading = Markdown.PlainText(b.Runs).Trim(); continue; }
                if (b.Kind != MdKind.Table) { if (Markdown.PlainText(b.Runs).Trim().Length > 0 || (b.Code ?? "").Trim().Length > 0) skipped = true; continue; }
                tables++;
                XlsxSheet sh = new XlsxSheet();
                sh.Name = SheetName(heading ?? "Sheet" + (sheets.Count + 1).ToString(CultureInfo.InvariantCulture), sheets);
                heading = null;
                long tableCells = 0;
                for (int r = 0; r < b.Rows.Count && r < MaxRows; r++)
                {
                    int width = Math.Min(b.Rows[r].Count, MaxCols);
                    if (tableCells + width > MaxCells) { TooMany(sh.Name, sh.Rows.Count, problems); break; }
                    tableCells += width;
                    List<XlsxCell> row = new List<XlsxCell>();
                    for (int ci = 0; ci < b.Rows[r].Count && ci < MaxCols; ci++)
                    {
                        List<MdRun> runs = Markdown.Inline(b.Rows[r][ci]);
                        string plain = Regex.Replace(Markdown.PlainText(runs), @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
                        string link = null;
                        bool bold = runs.Count > 0;
                        foreach (MdRun run in runs)
                        {
                            if (run.Link != null && link == null) link = DocModel.SafeLink(run.Link);
                            if (run.Text.Trim().Length > 0 && (run.Style & MdRun.Bold) == 0) bold = false;
                        }
                        XlsxCell cell = Typed(plain, false, sh.Name + "!" + Col(ci) + (r + 1).ToString(CultureInfo.InvariantCulture), problems);
                        if (link != null && cell.Kind == XlsxCell.Text) cell.Link = link;
                        if (bold && r > 0 && cell.Kind != XlsxCell.Empty) cell.Bold = true;
                        row.Add(cell);
                    }
                    sh.Rows.Add(row);
                }
                sh.Header = true;
                sheets.Add(sh);
            }
            if (skipped && problems != null) problems.Add("only the tables were put in the workbook; the text around them was left out");
            return tables > 0;
        }

        static XlsxSheet FromDelimited(string text, List<string> problems)
        {
            string[] lines = text.Split('\n');
            int tabs = 0, commas = 0, semis = 0, nonEmpty = 0;
            foreach (string l in lines)
            {
                if (l.Trim().Length == 0) continue;
                nonEmpty++;
                if (l.IndexOf('\t') >= 0) tabs++;
                if (l.IndexOf(',') >= 0) commas++;
                if (l.IndexOf(';') >= 0) semis++;
            }
            if (nonEmpty == 0) return null;
            char delim = tabs >= nonEmpty * 0.6 ? '\t' : semis >= nonEmpty * 0.6 && semis > commas ? ';' : ',';
            List<List<string>> rows = Csv(text, delim);
            int multi = 0, filled = 0;
            foreach (List<string> r in rows)
            {
                bool empty = true;
                foreach (string c in r) if (c.Trim().Length > 0) { empty = false; break; }
                if (empty) continue;
                filled++;
                if (r.Count >= 2) multi++;
            }
            if (filled == 0 || multi < Math.Max(1, filled * 0.6)) return null;
            while (rows.Count > 0 && rows[rows.Count - 1].Count == 1 && rows[rows.Count - 1][0].Trim().Length == 0) rows.RemoveAt(rows.Count - 1);
            XlsxSheet sh = new XlsxSheet();
            sh.Name = "Sheet1";
            bool comma = delim == ';';
            long csvCells = 0;
            for (int r = 0; r < rows.Count && r < MaxRows; r++)
            {
                int width = Math.Min(rows[r].Count, MaxCols);
                if (csvCells + width > MaxCells) { TooMany(sh.Name, sh.Rows.Count, problems); break; }
                csvCells += width;
                List<XlsxCell> row = new List<XlsxCell>();
                for (int ci = 0; ci < rows[r].Count && ci < MaxCols; ci++) row.Add(Typed(rows[r][ci], comma, Col(ci) + (r + 1).ToString(CultureInfo.InvariantCulture), problems));
                sh.Rows.Add(row);
            }
            if (rows.Count > MaxRows && problems != null) problems.Add("only the first " + MaxRows + " rows fit in a worksheet");
            sh.Header = LooksLikeHeader(sh);
            return sh;
        }

        public static List<List<string>> Csv(string text, char delim)
        {
            List<List<string>> rows = new List<List<string>>();
            List<string> row = new List<string>();
            StringBuilder cell = new StringBuilder();
            bool quoted = false, wasQuoted = false;
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i += 2; continue; }
                        quoted = false;
                        i++;
                        continue;
                    }
                    cell.Append(c);
                    i++;
                    continue;
                }
                if (c == '"' && cell.ToString().Trim().Length == 0 && !wasQuoted) { cell.Length = 0; quoted = true; wasQuoted = true; i++; continue; }
                if (c == delim) { row.Add(wasQuoted ? cell.ToString() : cell.ToString().Trim()); cell.Length = 0; wasQuoted = false; i++; continue; }
                if (c == '\n')
                {
                    row.Add(wasQuoted ? cell.ToString() : cell.ToString().Trim());
                    rows.Add(row);
                    row = new List<string>();
                    cell.Length = 0;
                    wasQuoted = false;
                    i++;
                    continue;
                }
                cell.Append(c);
                i++;
            }
            if (cell.Length > 0 || row.Count > 0 || wasQuoted)
            {
                row.Add(wasQuoted ? cell.ToString() : cell.ToString().Trim());
                rows.Add(row);
            }
            return rows;
        }

        class Styles
        {
            readonly Dictionary<string, int> fmts = new Dictionary<string, int>(StringComparer.Ordinal);
            readonly List<string> fmtXml = new List<string>();
            readonly Dictionary<string, int> xfs = new Dictionary<string, int>(StringComparer.Ordinal);
            readonly List<string> xfXml = new List<string>();

            public Styles() { Xf(0, 0, 0, 0, false, null); }

            int Fmt(string code)
            {
                if (string.IsNullOrEmpty(code)) return 0;
                int id;
                if (fmts.TryGetValue(code, out id)) return id;
                id = 164 + fmts.Count;
                fmts[code] = id;
                fmtXml.Add("<numFmt numFmtId=\"" + id.ToString(CultureInfo.InvariantCulture) + "\" formatCode=\"" + DocXml.Esc(code) + "\"/>");
                return id;
            }

            int Xf(int numFmt, int font, int fill, int border, bool wrap, string vertical)
            {
                return Xf(numFmt, font, fill, border, wrap, vertical, null);
            }

            int Xf(int numFmt, int font, int fill, int border, bool wrap, string vertical, string horizontal)
            {
                return Xf(numFmt, font, fill, border, wrap, vertical, horizontal, false);
            }

            int Xf(int numFmt, int font, int fill, int border, bool wrap, string vertical, string horizontal, bool quote)
            {
                string key = numFmt + "|" + font + "|" + fill + "|" + border + "|" + wrap + "|" + vertical + "|" + horizontal + "|" + quote;
                int id;
                if (xfs.TryGetValue(key, out id)) return id;
                id = xfXml.Count;
                xfs[key] = id;
                StringBuilder sb = new StringBuilder();
                sb.Append("<xf numFmtId=\"").Append(numFmt).Append("\" fontId=\"").Append(font).Append("\" fillId=\"").Append(fill).Append("\" borderId=\"").Append(border).Append("\" xfId=\"0\"");
                if (quote) sb.Append(" quotePrefix=\"1\"");
                if (numFmt != 0) sb.Append(" applyNumberFormat=\"1\"");
                if (font != 0) sb.Append(" applyFont=\"1\"");
                if (fill != 0) sb.Append(" applyFill=\"1\"");
                if (border != 0) sb.Append(" applyBorder=\"1\"");
                if (wrap || vertical != null || horizontal != null)
                {
                    sb.Append(" applyAlignment=\"1\"><alignment");
                    if (horizontal != null) sb.Append(" horizontal=\"").Append(horizontal).Append("\"");
                    if (vertical != null) sb.Append(" vertical=\"").Append(vertical).Append("\"");
                    if (wrap) sb.Append(" wrapText=\"1\"");
                    sb.Append("/></xf>");
                }
                else sb.Append("/>");
                xfXml.Add(sb.ToString());
                return id;
            }

            public int For(XlsxCell c, bool header, bool wrap, bool right)
            {
                int font = header || c.Bold ? 1 : c.Link != null ? 2 : 0;
                bool quote = c.Kind == XlsxCell.Text && !string.IsNullOrEmpty(c.Value) && "=+-@".IndexOf(c.Value[0]) >= 0;
                return Xf(Fmt(c.Format), font, header ? 2 : 0, header ? 1 : 0, wrap, wrap || header ? "top" : null, header && right ? "right" : null, quote);
            }

            public string Xml()
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<styleSheet xmlns=\"").Append(Main).Append("\">");
                if (fmtXml.Count > 0)
                {
                    sb.Append("<numFmts count=\"").Append(fmtXml.Count).Append("\">");
                    foreach (string f in fmtXml) sb.Append(f);
                    sb.Append("</numFmts>");
                }
                sb.Append("<fonts count=\"3\">");
                sb.Append("<font><sz val=\"11\"/><color rgb=\"FF1F2328\"/><name val=\"Calibri\"/><family val=\"2\"/></font>");
                sb.Append("<font><b/><sz val=\"11\"/><color rgb=\"FF1F2328\"/><name val=\"Calibri\"/><family val=\"2\"/></font>");
                sb.Append("<font><u/><sz val=\"11\"/><color rgb=\"FF0969DA\"/><name val=\"Calibri\"/><family val=\"2\"/></font>");
                sb.Append("</fonts><fills count=\"3\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill>");
                sb.Append("<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFF2F4F7\"/><bgColor indexed=\"64\"/></patternFill></fill></fills>");
                sb.Append("<borders count=\"2\"><border><left/><right/><top/><bottom/><diagonal/></border>");
                sb.Append("<border><left/><right/><top/><bottom style=\"thin\"><color rgb=\"FFB8C0CC\"/></bottom><diagonal/></border></borders>");
                sb.Append("<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>");
                sb.Append("<cellXfs count=\"").Append(xfXml.Count).Append("\">");
                foreach (string x in xfXml) sb.Append(x);
                sb.Append("</cellXfs><cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>");
                sb.Append("<dxfs count=\"0\"/><tableStyles count=\"0\" defaultTableStyle=\"TableStyleMedium2\" defaultPivotStyle=\"PivotStyleLight16\"/></styleSheet>");
                return sb.ToString();
            }
        }

        static double Units(string s)
        {
            double n = 0;
            foreach (string line in s.Split('\n'))
            {
                double w = 0;
                foreach (char ch in line) w += ch >= 0x1100 && (ch <= 0x115F || (ch >= 0x2E80 && ch <= 0xA4CF) || (ch >= 0xAC00 && ch <= 0xD7A3) || (ch >= 0xF900 && ch <= 0xFAFF) || (ch >= 0xFF00 && ch <= 0xFF60)) ? 2 : 1;
                n = Math.Max(n, w);
            }
            return n;
        }

        static string Shown(XlsxCell c)
        {
            switch (c.Kind)
            {
                case XlsxCell.Text: return c.Value;
                case XlsxCell.Bool: return c.Num != 0 ? "TRUE" : "FALSE";
                case XlsxCell.Formula: return "0000000000";
                case XlsxCell.Number:
                    if (c.Format != null && c.Format.StartsWith("yyyy")) return c.Format;
                    string s = c.Num.ToString(c.Format != null && c.Format.IndexOf(',') >= 0 ? "#,##0.##" : "0.##########", CultureInfo.InvariantCulture);
                    return s + (c.Format != null && c.Format.IndexOf('"') >= 0 ? "  " : "") + (c.Format != null && c.Format.EndsWith("%") ? "%" : "");
            }
            return "";
        }

        public static byte[] Build(List<XlsxSheet> sheets, string title)
        {
            Styles st = new Styles();
            List<string> shared = new List<string>();
            Dictionary<string, int> sharedIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            int sharedRefs = 0;
            DocPackage pkg = new DocPackage();
            StringBuilder types = new StringBuilder();
            types.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
            types.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>");
            types.Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
            types.Append("<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
            types.Append("<Override PartName=\"/xl/sharedStrings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml\"/>");
            types.Append("<Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>");
            StringBuilder wbRels = new StringBuilder();
            StringBuilder sheetList = new StringBuilder();
            StringBuilder names = new StringBuilder();
            for (int si = 0; si < sheets.Count; si++)
            {
                XlsxSheet sh = sheets[si];
                int cols = 0;
                foreach (List<XlsxCell> r in sh.Rows) cols = Math.Max(cols, r.Count);
                cols = Math.Max(1, cols);
                double[] width = new double[cols];
                foreach (List<XlsxCell> r in sh.Rows)
                    for (int ci = 0; ci < r.Count; ci++)
                        width[ci] = Math.Max(width[ci], Units(Shown(r[ci])) * (sh.Header && r == sh.Rows[0] ? 1.1 : 1.0));
                for (int ci = 0; ci < cols; ci++) width[ci] = Math.Max(8.43, Math.Min(60, width[ci] + 2));
                bool[] numeric = new bool[cols];
                string[] colFormat = new string[cols];
                for (int ci = 0; ci < cols; ci++)
                {
                    int nums = 0, texts = 0;
                    Dictionary<string, int> fmtCount = new Dictionary<string, int>(StringComparer.Ordinal);
                    for (int ri = sh.Header ? 1 : 0; ri < sh.Rows.Count; ri++)
                    {
                        if (ci >= sh.Rows[ri].Count) continue;
                        XlsxCell c = sh.Rows[ri][ci];
                        if (c.Kind == XlsxCell.Number || c.Kind == XlsxCell.Formula) nums++;
                        else if (c.Kind == XlsxCell.Text) texts++;
                        if (c.Kind == XlsxCell.Number && c.Format != null && !c.Format.StartsWith("yyyy"))
                        {
                            int n;
                            fmtCount.TryGetValue(c.Format, out n);
                            fmtCount[c.Format] = n + 1;
                        }
                    }
                    numeric[ci] = nums > 0 && nums >= texts * 2;
                    int best = 0;
                    foreach (KeyValuePair<string, int> kv in fmtCount) if (kv.Value > best) { best = kv.Value; colFormat[ci] = kv.Key; }
                }
                double totalWidth = 0;
                for (int ci = 0; ci < cols; ci++) totalWidth += width[ci];
                StringBuilder data = new StringBuilder();
                StringBuilder links = new StringBuilder();
                StringBuilder sheetRels = new StringBuilder();
                int nextRel = 1;
                for (int ri = 0; ri < sh.Rows.Count; ri++)
                {
                    List<XlsxCell> r = sh.Rows[ri];
                    bool head = sh.Header && ri == 0;
                    int lines = 1;
                    StringBuilder cells = new StringBuilder();
                    for (int ci = 0; ci < (head ? cols : r.Count); ci++)
                    {
                        XlsxCell c = ci < r.Count ? r[ci] : new XlsxCell();
                        if (c.Kind == XlsxCell.Empty && !head) continue;
                        string text = c.Value ?? "";
                        if (c.Kind == XlsxCell.Text && text.Length > MaxText) text = text.Substring(0, MaxText);
                        bool wrap = c.Kind == XlsxCell.Text && (text.IndexOf('\n') >= 0 || Units(text) > width[ci] + 1);
                        if (wrap)
                        {
                            int n = 0;
                            foreach (string part in text.Split('\n')) n += Math.Max(1, (int)Math.Ceiling(Units(part) / Math.Max(1, width[ci] - 1)));
                            lines = Math.Max(lines, Math.Min(n, 30));
                        }
                        string cref = Col(ci) + (ri + 1).ToString(CultureInfo.InvariantCulture);
                        if (c.Kind == XlsxCell.Formula && c.Format == null && colFormat[ci] != null) c.Format = colFormat[ci];
                        int s = st.For(c, head, wrap, numeric[ci]);
                        cells.Append("<c r=\"").Append(cref).Append("\"");
                        if (s != 0) cells.Append(" s=\"").Append(s).Append("\"");
                        switch (c.Kind)
                        {
                            case XlsxCell.Empty:
                                cells.Append("/>");
                                break;
                            case XlsxCell.Text:
                                {
                                    int idx;
                                    if (!sharedIndex.TryGetValue(text, out idx)) { idx = shared.Count; shared.Add(text); sharedIndex[text] = idx; }
                                    sharedRefs++;
                                    cells.Append(" t=\"s\"><v>").Append(idx).Append("</v></c>");
                                    if (c.Link != null)
                                    {
                                        string rid = "rId" + (nextRel++).ToString(CultureInfo.InvariantCulture);
                                        links.Append("<hyperlink ref=\"").Append(cref).Append("\" r:id=\"").Append(rid).Append("\"/>");
                                        sheetRels.Append("<Relationship Id=\"").Append(rid).Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink\" Target=\"").Append(DocXml.Esc(c.Link)).Append("\" TargetMode=\"External\"/>");
                                    }
                                    break;
                                }
                            case XlsxCell.Number:
                                cells.Append("><v>").Append(c.Num.ToString("R", CultureInfo.InvariantCulture)).Append("</v></c>");
                                break;
                            case XlsxCell.Bool:
                                cells.Append(" t=\"b\"><v>").Append(c.Num != 0 ? "1" : "0").Append("</v></c>");
                                break;
                            case XlsxCell.Formula:
                                cells.Append("><f>").Append(DocXml.Esc(c.Value)).Append("</f></c>");
                                break;
                        }
                    }
                    data.Append("<row r=\"").Append(ri + 1).Append("\"");
                    if (lines > 1) data.Append(" ht=\"").Append((lines * 15).ToString(CultureInfo.InvariantCulture)).Append("\" customHeight=\"1\"");
                    data.Append(">").Append(cells).Append("</row>");
                }
                int rowsN = Math.Max(1, sh.Rows.Count);
                string range = "A1:" + Col(cols - 1) + rowsN.ToString(CultureInfo.InvariantCulture);
                StringBuilder ws = new StringBuilder();
                bool wide = totalWidth > 95;
                ws.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<worksheet xmlns=\"").Append(Main).Append("\" xmlns:r=\"").Append(Rel).Append("\">");
                if (wide) ws.Append("<sheetPr><pageSetUpPr fitToPage=\"1\"/></sheetPr>");
                ws.Append("<dimension ref=\"").Append(range).Append("\"/>");
                ws.Append("<sheetViews><sheetView workbookViewId=\"0\"");
                if (si == 0) ws.Append(" tabSelected=\"1\"");
                if (sh.Header && sh.Rows.Count > 1) ws.Append("><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/><selection pane=\"bottomLeft\" activeCell=\"A2\" sqref=\"A2\"/></sheetView>");
                else ws.Append("/>");
                ws.Append("</sheetViews><sheetFormatPr defaultRowHeight=\"15\"/><cols>");
                for (int ci = 0; ci < cols; ci++)
                    ws.Append("<col min=\"").Append(ci + 1).Append("\" max=\"").Append(ci + 1).Append("\" width=\"").Append(Math.Round(width[ci], 2).ToString(CultureInfo.InvariantCulture)).Append("\" customWidth=\"1\"/>");
                ws.Append("</cols><sheetData>").Append(data).Append("</sheetData>");
                bool filter = sh.Header && sh.Rows.Count > 1 && cols > 0;
                if (filter)
                {
                    ws.Append("<autoFilter ref=\"").Append(range).Append("\"/>");
                    string quoted = "'" + sh.Name.Replace("'", "''") + "'";
                    names.Append("<definedName name=\"_xlnm._FilterDatabase\" localSheetId=\"").Append(si).Append("\" hidden=\"1\">").Append(DocXml.Esc(quoted + "!$A$1:$" + Col(cols - 1) + "$" + rowsN.ToString(CultureInfo.InvariantCulture))).Append("</definedName>");
                }
                if (links.Length > 0) ws.Append("<hyperlinks>").Append(links).Append("</hyperlinks>");
                ws.Append("<pageMargins left=\"0.7\" right=\"0.7\" top=\"0.75\" bottom=\"0.75\" header=\"0.3\" footer=\"0.3\"/>");
                if (wide) ws.Append("<pageSetup orientation=\"landscape\" fitToWidth=\"1\" fitToHeight=\"0\"/>");
                ws.Append("</worksheet>");
                string partName = "xl/worksheets/sheet" + (si + 1).ToString(CultureInfo.InvariantCulture) + ".xml";
                pkg.Add(partName, ws.ToString());
                if (sheetRels.Length > 0)
                    pkg.Add("xl/worksheets/_rels/sheet" + (si + 1).ToString(CultureInfo.InvariantCulture) + ".xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" + sheetRels + "</Relationships>");
                types.Append("<Override PartName=\"/").Append(partName).Append("\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
                wbRels.Append("<Relationship Id=\"rId").Append(si + 1).Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet").Append(si + 1).Append(".xml\"/>");
                sheetList.Append("<sheet name=\"").Append(DocXml.Esc(sh.Name)).Append("\" sheetId=\"").Append(si + 1).Append("\" r:id=\"rId").Append(si + 1).Append("\"/>");
            }
            int n2 = sheets.Count;
            wbRels.Append("<Relationship Id=\"rId").Append(n2 + 1).Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
            wbRels.Append("<Relationship Id=\"rId").Append(n2 + 2).Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings\" Target=\"sharedStrings.xml\"/>");
            types.Append("</Types>");
            pkg.Add("[Content_Types].xml", types.ToString());
            pkg.Add("_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/></Relationships>");
            StringBuilder wb = new StringBuilder();
            wb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<workbook xmlns=\"").Append(Main).Append("\" xmlns:r=\"").Append(Rel).Append("\">");
            wb.Append("<workbookPr/><bookViews><workbookView/></bookViews><sheets>").Append(sheetList).Append("</sheets>");
            if (names.Length > 0) wb.Append("<definedNames>").Append(names).Append("</definedNames>");
            wb.Append("<calcPr calcId=\"191029\" fullCalcOnLoad=\"1\"/></workbook>");
            pkg.Add("xl/workbook.xml", wb.ToString());
            pkg.Add("xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" + wbRels + "</Relationships>");
            pkg.Add("xl/styles.xml", st.Xml());
            StringBuilder sst = new StringBuilder();
            sst.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<sst xmlns=\"").Append(Main).Append("\" count=\"").Append(sharedRefs).Append("\" uniqueCount=\"").Append(shared.Count).Append("\">");
            foreach (string s in shared)
            {
                string e = DocXml.Esc(s);
                bool keep = s.Length > 0 && (char.IsWhiteSpace(s[0]) || char.IsWhiteSpace(s[s.Length - 1]) || s.IndexOf('\n') >= 0);
                sst.Append("<si><t").Append(keep ? " xml:space=\"preserve\"" : "").Append(">").Append(e).Append("</t></si>");
            }
            sst.Append("</sst>");
            pkg.Add("xl/sharedStrings.xml", sst.ToString());
            pkg.Add("docProps/core.xml", DocXml.Core(title, DocXml.Author(), DateTime.UtcNow));
            return pkg.ToBytes();
        }
    }
}
