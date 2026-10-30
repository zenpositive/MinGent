using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;

namespace MinGent
{
    static class DocxWriter
    {
        const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        const string Ink = "1F2328", Muted = "59636E", Line = "D0D7DE", Soft = "F6F8FA", LinkBlue = "0969DA";
        const int BulletAbstract = 0, OrderedAbstract = 1, TaskAbstract = 2;
        const double MathPt = 12;

        class Ctx
        {
            public readonly StringBuilder Rels = new StringBuilder();
            public readonly Dictionary<string, string> Links = new Dictionary<string, string>(StringComparer.Ordinal);
            public readonly List<KeyValuePair<string, byte[]>> Media = new List<KeyValuePair<string, byte[]>>();
            public readonly Dictionary<string, int> Nums = new Dictionary<string, int>(StringComparer.Ordinal);
            public readonly StringBuilder NumXml = new StringBuilder();
            public int NextRel = 6, NextPic = 1;
            public int TextW, TextH;
            public DocFormulas Formulas;

            public string Link(string url)
            {
                string id;
                if (Links.TryGetValue(url, out id)) return id;
                id = "rId" + (NextRel++).ToString(CultureInfo.InvariantCulture);
                Links[url] = id;
                Rels.Append("<Relationship Id=\"").Append(id).Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink\" Target=\"").Append(DocXml.Esc(url)).Append("\" TargetMode=\"External\"/>");
                return id;
            }

            readonly Dictionary<DocPicture, string> images = new Dictionary<DocPicture, string>();

            public string Image(DocPicture p)
            {
                string id;
                if (images.TryGetValue(p, out id)) return id;
                id = "rId" + (NextRel++).ToString(CultureInfo.InvariantCulture);
                images[p] = id;
                string name = "image" + (Media.Count + 1).ToString(CultureInfo.InvariantCulture) + (p.Jpeg ? ".jpeg" : ".png");
                Media.Add(new KeyValuePair<string, byte[]>("word/media/" + name, p.Data));
                Rels.Append("<Relationship Id=\"").Append(id).Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"media/").Append(name).Append("\"/>");
                return id;
            }

            public int Num(string key, int abstractId, int level, int start)
            {
                int id;
                if (Nums.TryGetValue(key, out id)) return id;
                id = Nums.Count + 1;
                Nums[key] = id;
                NumXml.Append("<w:num w:numId=\"").Append(id).Append("\"><w:abstractNumId w:val=\"").Append(abstractId).Append("\"/>");
                if (abstractId == OrderedAbstract)
                    NumXml.Append("<w:lvlOverride w:ilvl=\"").Append(level).Append("\"><w:startOverride w:val=\"").Append(Math.Max(0, start)).Append("\"/></w:lvlOverride>");
                NumXml.Append("</w:num>");
                return id;
            }
        }

        public static byte[] Build(DocModel m)
        {
            bool letter = DocPage.Letter;
            int pageW = letter ? 12240 : 11906, pageH = letter ? 15840 : 16838, margin = 1440;
            Ctx c = new Ctx();
            c.TextW = pageW - 2 * margin;
            c.TextH = pageH - 2 * margin;
            StringBuilder body = new StringBuilder();
            using (c.Formulas = new DocFormulas(MathPt))
            {
                for (int i = 0; i < m.Blocks.Count; i++)
                {
                    DocBlock b = m.Blocks[i];
                    DocBlock next = i + 1 < m.Blocks.Count ? m.Blocks[i + 1] : null;
                    if (i > 0 && b.Kind == DocKind.Code && m.Blocks[i - 1].Kind == DocKind.Code) body.Append("<w:p><w:pPr><w:pStyle w:val=\"Spacer\"/></w:pPr></w:p>");
                    bool lastItem = b.Kind == DocKind.Item && (next == null || next.Kind != DocKind.Item || next.ListId != b.ListId);
                    Block(body, b, c, lastItem);
                }
            }
            if (m.Blocks.Count == 0) body.Append("<w:p/>");
            body.Append("<w:sectPr><w:footerReference w:type=\"default\" r:id=\"rId4\"/><w:footerReference w:type=\"first\" r:id=\"rId5\"/>");
            body.Append("<w:pgSz w:w=\"").Append(pageW).Append("\" w:h=\"").Append(pageH).Append("\"/>");
            body.Append("<w:pgMar w:top=\"").Append(margin).Append("\" w:right=\"").Append(margin).Append("\" w:bottom=\"").Append(margin).Append("\" w:left=\"").Append(margin).Append("\" w:header=\"708\" w:footer=\"708\" w:gutter=\"0\"/>");
            body.Append("<w:cols w:space=\"708\"/><w:titlePg/></w:sectPr>");

            StringBuilder doc = new StringBuilder();
            doc.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n");
            doc.Append("<w:document xmlns:w=\"").Append(W).Append("\" xmlns:r=\"").Append(R).Append("\" xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\">");
            doc.Append("<w:body>").Append(body).Append("</w:body></w:document>");

            DocPackage pkg = new DocPackage();
            StringBuilder types = new StringBuilder();
            types.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
            types.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
            types.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");
            types.Append("<Default Extension=\"png\" ContentType=\"image/png\"/>");
            types.Append("<Default Extension=\"jpeg\" ContentType=\"image/jpeg\"/>");
            types.Append("<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>");
            types.Append("<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>");
            types.Append("<Override PartName=\"/word/numbering.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml\"/>");
            types.Append("<Override PartName=\"/word/settings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml\"/>");
            types.Append("<Override PartName=\"/word/footer1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml\"/>");
            types.Append("<Override PartName=\"/word/footer2.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml\"/>");
            types.Append("<Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>");
            types.Append("</Types>");
            pkg.Add("[Content_Types].xml", types.ToString());
            pkg.Add("_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
                "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/>" +
                "</Relationships>");
            pkg.Add("word/document.xml", doc.ToString());
            pkg.Add("word/_rels/document.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
                "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering\" Target=\"numbering.xml\"/>" +
                "<Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings\" Target=\"settings.xml\"/>" +
                "<Relationship Id=\"rId4\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer\" Target=\"footer1.xml\"/>" +
                "<Relationship Id=\"rId5\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer\" Target=\"footer2.xml\"/>" +
                c.Rels + "</Relationships>");
            pkg.Add("word/styles.xml", Styles());
            pkg.Add("word/numbering.xml", Numbering(c));
            pkg.Add("word/settings.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<w:settings xmlns:w=\"" + W + "\"><w:zoom w:percent=\"100\"/><w:defaultTabStop w:val=\"720\"/><w:characterSpacingControl w:val=\"doNotCompress\"/><w:compat><w:compatSetting w:name=\"compatibilityMode\" w:uri=\"http://schemas.microsoft.com/office/word\" w:val=\"15\"/></w:compat></w:settings>");
            pkg.Add("word/footer1.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<w:ftr xmlns:w=\"" + W + "\"><w:p><w:pPr><w:pStyle w:val=\"Footer\"/><w:jc w:val=\"center\"/></w:pPr><w:fldSimple w:instr=\" PAGE \"><w:r><w:t>2</w:t></w:r></w:fldSimple></w:p></w:ftr>");
            pkg.Add("word/footer2.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<w:ftr xmlns:w=\"" + W + "\"><w:p><w:pPr><w:pStyle w:val=\"Footer\"/></w:pPr></w:p></w:ftr>");
            pkg.Add("docProps/core.xml", DocXml.Core(m.Title, DocXml.Author(), DateTime.UtcNow));
            foreach (KeyValuePair<string, byte[]> md in c.Media) pkg.Add(md.Key, md.Value);
            return pkg.ToBytes();
        }

        static string PPr(string style, bool keepNext, string numPr, bool bidi, string spacing, bool contextualOff, string jc, string tabs)
        {
            return PPr(style, keepNext, numPr, bidi, spacing, contextualOff, jc, tabs, false);
        }

        static string PPr(string style, bool keepNext, string numPr, bool bidi, string spacing, bool contextualOff, string jc, string tabs, bool middle)
        {
            StringBuilder p = new StringBuilder();
            if (style != null) p.Append("<w:pStyle w:val=\"").Append(style).Append("\"/>");
            if (keepNext) p.Append("<w:keepNext/>");
            if (numPr != null) p.Append(numPr);
            if (tabs != null) p.Append(tabs);
            if (bidi) p.Append("<w:bidi/>");
            if (spacing != null) p.Append(spacing);
            if (contextualOff) p.Append("<w:contextualSpacing w:val=\"0\"/>");
            if (jc != null) p.Append("<w:jc w:val=\"").Append(jc).Append("\"/>");
            if (middle) p.Append("<w:textAlignment w:val=\"center\"/>");
            return p.Length == 0 ? "" : "<w:pPr>" + p + "</w:pPr>";
        }

        static void Block(StringBuilder sb, DocBlock b, Ctx c, bool lastItem)
        {
            bool rtl = DocBidi.RtlSpans(b.Spans);
            switch (b.Kind)
            {
                case DocKind.Heading:
                    sb.Append("<w:p>").Append(PPr("Heading" + b.Level.ToString(CultureInfo.InvariantCulture), false, null, rtl, null, false, null, null));
                    Runs(sb, b.Spans, c, false, rtl);
                    sb.Append("</w:p>");
                    break;
                case DocKind.Para:
                    sb.Append("<w:p>").Append(PPr(null, false, null, rtl, null, false, null, null));
                    Runs(sb, b.Spans, c, false, rtl);
                    sb.Append("</w:p>");
                    break;
                case DocKind.Quote:
                    sb.Append("<w:p>").Append(PPr("Quote", false, null, rtl, null, false, null, null));
                    Runs(sb, b.Spans, c, false, rtl);
                    sb.Append("</w:p>");
                    break;
                case DocKind.Item:
                    {
                        int lvl = Math.Max(0, Math.Min(8, b.Level));
                        int num;
                        if (b.Task >= 0) num = c.Num("t", TaskAbstract, lvl, 1);
                        else if (b.Ordered) num = c.Num("o" + b.Run.ToString(CultureInfo.InvariantCulture), OrderedAbstract, lvl, b.Number);
                        else num = c.Num("b", BulletAbstract, lvl, 1);
                        string numPr = "<w:numPr><w:ilvl w:val=\"" + lvl.ToString(CultureInfo.InvariantCulture) + "\"/><w:numId w:val=\"" + num.ToString(CultureInfo.InvariantCulture) + "\"/></w:numPr>";
                        sb.Append("<w:p>").Append(PPr("ListParagraph", false, numPr, rtl, lastItem ? "<w:spacing w:after=\"160\"/>" : null, lastItem, null, null));
                        if (b.Task >= 0)
                            sb.Append("<w:r><w:rPr><w:rFonts w:ascii=\"Segoe UI Symbol\" w:hAnsi=\"Segoe UI Symbol\" w:cs=\"Segoe UI Symbol\"/></w:rPr><w:t xml:space=\"preserve\">").Append(b.Task == 1 ? "\u2611" : "\u2610").Append(" </w:t></w:r>");
                        Runs(sb, b.Spans, c, false, rtl);
                        sb.Append("</w:p>");
                        break;
                    }
                case DocKind.Code:
                    {
                        string[] lines = (b.Code ?? "").Split('\n');
                        bool keep = lines.Length <= 30;
                        for (int i = 0; i < lines.Length; i++)
                        {
                            sb.Append("<w:p>").Append(PPr("Code", keep && i < lines.Length - 1, null, false, null, false, null, null));
                            if (lines[i].Length > 0) sb.Append("<w:r>").Append(TextContent(lines[i])).Append("</w:r>");
                            sb.Append("</w:p>");
                        }
                        break;
                    }
                case DocKind.Math:
                    MathBlock(sb, b, c);
                    break;
                case DocKind.Rule:
                    sb.Append("<w:p><w:pPr><w:pStyle w:val=\"Rule\"/></w:pPr></w:p>");
                    break;
                case DocKind.PageBreak:
                    sb.Append("<w:p><w:r><w:br w:type=\"page\"/></w:r></w:p>");
                    break;
                case DocKind.Picture:
                    {
                        DocPicture p = b.Picture;
                        bool caption = p.Figure && (p.Alt ?? "").Trim().Length > 0;
                        sb.Append("<w:p>").Append(PPr(caption ? "Figure" : null, caption, null, false, null, false, "center", null)).Append("<w:r>");
                        Picture(sb, p, c, 0.85);
                        sb.Append("</w:r></w:p>");
                        if (caption)
                        {
                            List<DocSpan> cap = new List<DocSpan>();
                            cap.Add(new DocSpan { Text = DocModel.Clean(p.Alt.Trim()) });
                            bool capRtl = DocBidi.RtlSpans(cap);
                            sb.Append("<w:p>").Append(PPr("Caption", false, null, capRtl, null, false, "center", null));
                            Runs(sb, cap, c, false, capRtl);
                            sb.Append("</w:p>");
                        }
                        break;
                    }
                case DocKind.Table:
                    Table(sb, b, c);
                    break;
            }
        }

        static void MathBlock(StringBuilder sb, DocBlock b, Ctx c)
        {
            double textPt = c.TextW / 20.0;
            DocFormula f = c.Formulas.Render(b.Tex, textPt * 0.8, Color.FromArgb(0x1F, 0x23, 0x28));
            if (f == null)
            {
                sb.Append("<w:p>").Append(PPr("Equation", false, null, false, null, false, "center", null));
                sb.Append("<w:r><w:rPr><w:rFonts w:ascii=\"Cambria Math\" w:hAnsi=\"Cambria Math\" w:cs=\"Cambria Math\"/></w:rPr>").Append(TextContent(b.Code)).Append("</w:r></w:p>");
                return;
            }
            string tag = f.Tag.Length > 0 ? f.Tag : null;
            for (int i = 0; i < f.Lines.Count; i++)
            {
                bool tagged = tag != null && i == f.Lines.Count - 1;
                string tabs = tagged ? "<w:tabs><w:tab w:val=\"center\" w:pos=\"" + (c.TextW / 2).ToString(CultureInfo.InvariantCulture) + "\"/><w:tab w:val=\"right\" w:pos=\"" + c.TextW.ToString(CultureInfo.InvariantCulture) + "\"/></w:tabs>" : null;
                sb.Append("<w:p>").Append(PPr("Equation", i < f.Lines.Count - 1, null, false, null, false, tagged ? null : "center", tabs, tagged));
                if (tagged) sb.Append("<w:r><w:tab/></w:r>");
                DocPicture p = f.Lines[i];
                sb.Append("<w:r>");
                Picture(sb, p, c, 0.85);
                sb.Append("</w:r>");
                if (tagged) sb.Append("<w:r><w:tab/>").Append(TextContent(tag)).Append("</w:r>");
                sb.Append("</w:p>");
            }
        }

        static void Runs(StringBuilder sb, List<DocSpan> spans, Ctx c, bool bold, bool paraRtl)
        {
            foreach (DocSpan s in spans)
            {
                if (s.Break) { sb.Append("<w:r><w:br/></w:r>"); continue; }
                if (s.Text.Length == 0) continue;
                StringBuilder runs = new StringBuilder();
                foreach (KeyValuePair<string, bool> part in DocBidi.Split(s.Text, paraRtl))
                {
                    StringBuilder rp = new StringBuilder();
                    if (s.Link != null) rp.Append("<w:rStyle w:val=\"Hyperlink\"/>");
                    else if (s.Code) rp.Append("<w:rStyle w:val=\"InlineCode\"/>");
                    if (s.Link != null && s.Code) rp.Append("<w:rFonts w:ascii=\"Consolas\" w:hAnsi=\"Consolas\" w:cs=\"Consolas\"/>");
                    if (s.Bold || bold) rp.Append("<w:b/><w:bCs/>");
                    if (s.Italic) rp.Append("<w:i/><w:iCs/>");
                    if (s.Strike) rp.Append("<w:strike/>");
                    if (part.Value) rp.Append("<w:rtl/>");
                    runs.Append("<w:r>");
                    if (rp.Length > 0) runs.Append("<w:rPr>").Append(rp).Append("</w:rPr>");
                    runs.Append(TextContent(part.Key)).Append("</w:r>");
                }
                if (s.Link != null) sb.Append("<w:hyperlink r:id=\"").Append(c.Link(s.Link)).Append("\" w:history=\"1\">").Append(runs).Append("</w:hyperlink>");
                else sb.Append(runs);
            }
        }

        static string TextContent(string text)
        {
            StringBuilder sb = new StringBuilder();
            string[] parts = (text ?? "").Split('\t');
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0) sb.Append("<w:tab/>");
                if (parts[i].Length > 0) sb.Append("<w:t xml:space=\"preserve\">").Append(DocXml.Esc(parts[i])).Append("</w:t>");
            }
            return sb.ToString();
        }

        static void Picture(StringBuilder sb, DocPicture p, Ctx c, double maxHeight)
        {
            double wIn = p.Width / p.DpiX, hIn = p.Height / p.DpiY;
            double maxW = c.TextW / 1440.0, maxH = c.TextH / 1440.0 * maxHeight;
            double scale = Math.Min(1.0, Math.Min(maxW / wIn, maxH / hIn));
            long cx = Math.Max(1L, (long)Math.Round(wIn * scale * 914400)), cy = Math.Max(1L, (long)Math.Round(hIn * scale * 914400));
            string rid = c.Image(p);
            int id = c.NextPic++;
            string alt = DocXml.Esc(p.Alt ?? "");
            string name = DocXml.Esc((p.Name ?? "").Length > 0 ? p.Name : "Picture " + id.ToString(CultureInfo.InvariantCulture));
            sb.Append("<w:drawing><wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\"><wp:extent cx=\"").Append(cx).Append("\" cy=\"").Append(cy).Append("\"/>");
            sb.Append("<wp:effectExtent l=\"0\" t=\"0\" r=\"0\" b=\"0\"/>");
            sb.Append("<wp:docPr id=\"").Append(id).Append("\" name=\"Picture ").Append(id).Append("\" descr=\"").Append(alt).Append("\"/>");
            sb.Append("<wp:cNvGraphicFramePr><a:graphicFrameLocks noChangeAspect=\"1\"/></wp:cNvGraphicFramePr>");
            sb.Append("<a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><pic:pic>");
            sb.Append("<pic:nvPicPr><pic:cNvPr id=\"").Append(id).Append("\" name=\"").Append(name).Append("\" descr=\"").Append(alt).Append("\"/><pic:cNvPicPr><a:picLocks noChangeAspect=\"1\"/></pic:cNvPicPr></pic:nvPicPr>");
            sb.Append("<pic:blipFill><a:blip r:embed=\"").Append(rid).Append("\"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>");
            sb.Append("<pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"").Append(cx).Append("\" cy=\"").Append(cy).Append("\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr>");
            sb.Append("</pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing>");
        }

        static void Table(StringBuilder sb, DocBlock b, Ctx c)
        {
            int cols = b.Rows[0].Count;
            double[] weight = new double[cols];
            bool rtl = false, decided = false;
            foreach (List<List<DocSpan>> row in b.Rows)
                for (int i = 0; i < cols && i < row.Count; i++)
                {
                    string plain = Plain(row[i]);
                    if (!decided)
                        foreach (char ch in plain)
                        {
                            if (DocBidi.Rtl(ch)) { rtl = true; decided = true; break; }
                            if (char.IsLetter(ch)) { decided = true; break; }
                        }
                    int longest = 0;
                    foreach (string piece in plain.Split('\n')) longest = Math.Max(longest, piece.Length);
                    weight[i] = Math.Max(weight[i], Math.Min(60, longest + 2));
                }
            double total = 0;
            for (int i = 0; i < cols; i++) { weight[i] = Math.Max(4, weight[i]); total += weight[i]; }
            int[] widths = new int[cols];
            int used = 0;
            for (int i = 0; i < cols; i++) { widths[i] = (int)(c.TextW * weight[i] / total); used += widths[i]; }
            widths[cols - 1] += c.TextW - used;
            sb.Append("<w:tbl><w:tblPr>");
            if (rtl) sb.Append("<w:bidiVisual/>");
            sb.Append("<w:tblW w:w=\"5000\" w:type=\"pct\"/><w:tblBorders>");
            foreach (string side in new string[] { "top", "left", "bottom", "right", "insideH", "insideV" })
                sb.Append("<w:").Append(side).Append(" w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"").Append(Line).Append("\"/>");
            sb.Append("</w:tblBorders><w:tblLayout w:type=\"autofit\"/>");
            sb.Append("<w:tblCellMar><w:top w:w=\"60\" w:type=\"dxa\"/><w:left w:w=\"100\" w:type=\"dxa\"/><w:bottom w:w=\"60\" w:type=\"dxa\"/><w:right w:w=\"100\" w:type=\"dxa\"/></w:tblCellMar>");
            sb.Append("<w:tblLook w:val=\"04A0\" w:firstRow=\"1\" w:lastRow=\"0\" w:firstColumn=\"1\" w:lastColumn=\"0\" w:noHBand=\"0\" w:noVBand=\"1\"/></w:tblPr><w:tblGrid>");
            for (int i = 0; i < cols; i++) sb.Append("<w:gridCol w:w=\"").Append(widths[i]).Append("\"/>");
            sb.Append("</w:tblGrid>");
            for (int r = 0; r < b.Rows.Count; r++)
            {
                bool head = r == 0;
                sb.Append("<w:tr>");
                if (head) sb.Append("<w:trPr><w:cantSplit/><w:tblHeader/></w:trPr>");
                else sb.Append("<w:trPr><w:cantSplit/></w:trPr>");
                for (int i = 0; i < cols; i++)
                {
                    List<DocSpan> cell = i < b.Rows[r].Count ? b.Rows[r][i] : new List<DocSpan>();
                    bool cellRtl = DocBidi.RtlSpans(cell);
                    sb.Append("<w:tc><w:tcPr><w:tcW w:w=\"").Append(widths[i]).Append("\" w:type=\"dxa\"/>");
                    if (head) sb.Append("<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"").Append(Soft).Append("\"/>");
                    sb.Append("</w:tcPr><w:p>");
                    char al = i < b.Align.Count ? b.Align[i] : 'l';
                    string jc = al == 'c' ? "center" : al == 'r' && !cellRtl && !rtl ? "right" : null;
                    sb.Append(PPr("TableText", false, null, cellRtl, null, false, jc, null));
                    Runs(sb, cell, c, head, cellRtl);
                    sb.Append("</w:p></w:tc>");
                }
                sb.Append("</w:tr>");
            }
            sb.Append("</w:tbl><w:p><w:pPr><w:pStyle w:val=\"Spacer\"/></w:pPr></w:p>");
        }

        static string Plain(List<DocSpan> spans)
        {
            StringBuilder sb = new StringBuilder();
            foreach (DocSpan s in spans) sb.Append(s.Break ? "\n" : s.Text);
            return sb.ToString();
        }

        static string Styles()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<w:styles xmlns:w=\"").Append(W).Append("\">");
            sb.Append("<w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii=\"Calibri\" w:eastAsia=\"Calibri\" w:hAnsi=\"Calibri\" w:cs=\"Arial\"/><w:color w:val=\"").Append(Ink).Append("\"/><w:sz w:val=\"22\"/><w:szCs w:val=\"22\"/></w:rPr></w:rPrDefault>");
            sb.Append("<w:pPrDefault><w:pPr><w:spacing w:after=\"160\" w:line=\"276\" w:lineRule=\"auto\"/></w:pPr></w:pPrDefault></w:docDefaults>");
            sb.Append("<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/><w:qFormat/></w:style>");
            int[] size = { 40, 30, 25, 22, 22, 22 };
            int[] before = { 360, 300, 240, 200, 200, 200 };
            int[] after = { 120, 100, 80, 60, 60, 60 };
            for (int h = 1; h <= 6; h++)
            {
                sb.Append("<w:style w:type=\"paragraph\" w:styleId=\"Heading").Append(h).Append("\"><w:name w:val=\"heading ").Append(h).Append("\"/><w:basedOn w:val=\"Normal\"/><w:next w:val=\"Normal\"/><w:uiPriority w:val=\"9\"/><w:qFormat/>");
                sb.Append("<w:pPr><w:keepNext/><w:keepLines/><w:spacing w:before=\"").Append(before[h - 1]).Append("\" w:after=\"").Append(after[h - 1]).Append("\" w:line=\"259\" w:lineRule=\"auto\"/><w:outlineLvl w:val=\"").Append(h - 1).Append("\"/></w:pPr>");
                sb.Append("<w:rPr><w:b/><w:bCs/>");
                if (h == 6) sb.Append("<w:i/><w:iCs/>");
                sb.Append("<w:color w:val=\"").Append(h >= 5 ? Muted : Ink).Append("\"/><w:sz w:val=\"").Append(size[h - 1]).Append("\"/><w:szCs w:val=\"").Append(size[h - 1]).Append("\"/></w:rPr></w:style>");
            }
            sb.Append("<w:style w:type=\"paragraph\" w:styleId=\"ListParagraph\"><w:name w:val=\"List Paragraph\"/><w:basedOn w:val=\"Normal\"/><w:uiPriority w:val=\"34\"/><w:qFormat/><w:pPr><w:spacing w:after=\"60\"/><w:contextualSpacing/></w:pPr></w:style>");
            sb.Append("<w:style w:type=\"paragraph\" w:styleId=\"Quote\"><w:name w:val=\"Quote\"/><w:basedOn w:val=\"Normal\"/><w:next w:val=\"Normal\"/><w:uiPriority w:val=\"29\"/><w:qFormat/>");
            sb.Append("<w:pPr><w:pBdr><w:left w:val=\"single\" w:sz=\"18\" w:space=\"8\" w:color=\"").Append(Line).Append("\"/></w:pBdr><w:ind w:left=\"240\" w:right=\"240\"/></w:pPr>");
            sb.Append("<w:rPr><w:i/><w:iCs/><w:color w:val=\"").Append(Muted).Append("\"/></w:rPr></w:style>");
            sb.Append("<w:style w:type=\"paragraph\" w:customStyle=\"1\" w:styleId=\"Code\"><w:name w:val=\"Code\"/><w:basedOn w:val=\"Normal\"/><w:qFormat/>");
            sb.Append("<w:pPr><w:keepLines/><w:pBdr>");
            foreach (string side in new string[] { "top", "left", "bottom", "right" })
                sb.Append("<w:").Append(side).Append(" w:val=\"single\" w:sz=\"4\" w:space=\"").Append(side == "left" || side == "right" ? "6" : "3").Append("\" w:color=\"").Append(Line).Append("\"/>");
            sb.Append("</w:pBdr><w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"").Append(Soft).Append("\"/><w:spacing w:after=\"160\" w:line=\"240\" w:lineRule=\"auto\"/><w:ind w:left=\"144\" w:right=\"144\"/><w:contextualSpacing/></w:pPr>");
            sb.Append("<w:rPr><w:rFonts w:ascii=\"Consolas\" w:hAnsi=\"Consolas\" w:cs=\"Consolas\"/><w:sz w:val=\"19\"/><w:szCs w:val=\"19\"/></w:rPr></w:style>");
            sb.Append("<w:style w:type=\"paragraph\" w:customStyle=\"1\" w:styleId=\"Equation\"><w:name w:val=\"Equation\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:spacing w:before=\"120\" w:after=\"200\" w:line=\"240\" w:lineRule=\"auto\"/></w:pPr></w:style>");
            sb.Append("<w:style w:type=\"paragraph\" w:customStyle=\"1\" w:styleId=\"Figure\"><w:name w:val=\"Figure\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:spacing w:after=\"60\"/></w:pPr></w:style>");
            sb.Append("<w:style w:type=\"paragraph\" w:styleId=\"Caption\"><w:name w:val=\"caption\"/><w:basedOn w:val=\"Normal\"/><w:next w:val=\"Normal\"/><w:uiPriority w:val=\"35\"/><w:unhideWhenUsed/><w:qFormat/><w:pPr><w:spacing w:after=\"240\"/></w:pPr><w:rPr><w:i/><w:iCs/><w:color w:val=\"").Append(Muted).Append("\"/><w:sz w:val=\"19\"/><w:szCs w:val=\"19\"/></w:rPr></w:style>");
            sb.Append("<w:style w:type=\"paragraph\" w:customStyle=\"1\" w:styleId=\"Rule\"><w:name w:val=\"Rule\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:pBdr><w:bottom w:val=\"single\" w:sz=\"6\" w:space=\"1\" w:color=\"").Append(Line).Append("\"/></w:pBdr><w:spacing w:before=\"120\" w:after=\"240\" w:line=\"120\" w:lineRule=\"exact\"/></w:pPr><w:rPr><w:sz w:val=\"8\"/><w:szCs w:val=\"8\"/></w:rPr></w:style>");
            sb.Append("<w:style w:type=\"paragraph\" w:customStyle=\"1\" w:styleId=\"TableText\"><w:name w:val=\"Table Text\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:spacing w:after=\"0\" w:line=\"252\" w:lineRule=\"auto\"/></w:pPr></w:style>");
            sb.Append("<w:style w:type=\"paragraph\" w:customStyle=\"1\" w:styleId=\"Spacer\"><w:name w:val=\"Spacer\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:spacing w:after=\"120\" w:line=\"120\" w:lineRule=\"exact\"/></w:pPr><w:rPr><w:sz w:val=\"8\"/><w:szCs w:val=\"8\"/></w:rPr></w:style>");
            sb.Append("<w:style w:type=\"paragraph\" w:styleId=\"Footer\"><w:name w:val=\"footer\"/><w:basedOn w:val=\"Normal\"/><w:uiPriority w:val=\"99\"/><w:unhideWhenUsed/><w:pPr><w:spacing w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/></w:pPr><w:rPr><w:color w:val=\"").Append(Muted).Append("\"/><w:sz w:val=\"18\"/><w:szCs w:val=\"18\"/></w:rPr></w:style>");
            sb.Append("<w:style w:type=\"character\" w:default=\"1\" w:styleId=\"DefaultParagraphFont\"><w:name w:val=\"Default Paragraph Font\"/><w:uiPriority w:val=\"1\"/><w:semiHidden/><w:unhideWhenUsed/></w:style>");
            sb.Append("<w:style w:type=\"character\" w:styleId=\"Hyperlink\"><w:name w:val=\"Hyperlink\"/><w:basedOn w:val=\"DefaultParagraphFont\"/><w:uiPriority w:val=\"99\"/><w:unhideWhenUsed/><w:rPr><w:color w:val=\"").Append(LinkBlue).Append("\"/><w:u w:val=\"single\"/></w:rPr></w:style>");
            sb.Append("<w:style w:type=\"character\" w:customStyle=\"1\" w:styleId=\"InlineCode\"><w:name w:val=\"Inline Code\"/><w:basedOn w:val=\"DefaultParagraphFont\"/><w:uiPriority w:val=\"1\"/><w:qFormat/><w:rPr><w:rFonts w:ascii=\"Consolas\" w:hAnsi=\"Consolas\" w:cs=\"Consolas\"/><w:sz w:val=\"20\"/><w:szCs w:val=\"20\"/><w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"EFF1F3\"/></w:rPr></w:style>");
            sb.Append("<w:style w:type=\"table\" w:default=\"1\" w:styleId=\"TableNormal\"><w:name w:val=\"Normal Table\"/><w:uiPriority w:val=\"99\"/><w:semiHidden/><w:unhideWhenUsed/><w:tblPr><w:tblInd w:w=\"0\" w:type=\"dxa\"/><w:tblCellMar><w:top w:w=\"0\" w:type=\"dxa\"/><w:left w:w=\"108\" w:type=\"dxa\"/><w:bottom w:w=\"0\" w:type=\"dxa\"/><w:right w:w=\"108\" w:type=\"dxa\"/></w:tblCellMar></w:tblPr></w:style>");
            sb.Append("<w:style w:type=\"numbering\" w:default=\"1\" w:styleId=\"NoList\"><w:name w:val=\"No List\"/><w:uiPriority w:val=\"99\"/><w:semiHidden/><w:unhideWhenUsed/></w:style>");
            sb.Append("</w:styles>");
            return sb.ToString();
        }

        static string Numbering(Ctx c)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<w:numbering xmlns:w=\"").Append(W).Append("\">");
            Abstract(sb, BulletAbstract, "bullet");
            Abstract(sb, OrderedAbstract, "ordered");
            Abstract(sb, TaskAbstract, "task");
            sb.Append(c.NumXml);
            sb.Append("</w:numbering>");
            return sb.ToString();
        }

        static void Abstract(StringBuilder sb, int id, string kind)
        {
            sb.Append("<w:abstractNum w:abstractNumId=\"").Append(id).Append("\"><w:multiLevelType w:val=\"hybridMultilevel\"/>");
            for (int l = 0; l < 9; l++)
            {
                int left = 360 + 360 * l;
                sb.Append("<w:lvl w:ilvl=\"").Append(l).Append("\"><w:start w:val=\"1\"/>");
                if (kind == "bullet")
                {
                    sb.Append("<w:numFmt w:val=\"bullet\"/><w:lvlText w:val=\"").Append(DocModel.Bullet(l)).Append("\"/><w:lvlJc w:val=\"left\"/>");
                    sb.Append("<w:pPr><w:ind w:left=\"").Append(left).Append("\" w:hanging=\"360\"/></w:pPr>");
                    sb.Append("<w:rPr><w:rFonts w:ascii=\"Calibri\" w:hAnsi=\"Calibri\" w:cs=\"Calibri\"/></w:rPr>");
                }
                else if (kind == "ordered")
                {
                    string fmt = l % 3 == 1 ? "lowerLetter" : l % 3 == 2 ? "lowerRoman" : "decimal";
                    sb.Append("<w:numFmt w:val=\"").Append(fmt).Append("\"/><w:lvlText w:val=\"%").Append(l + 1).Append(".\"/><w:lvlJc w:val=\"left\"/>");
                    sb.Append("<w:pPr><w:ind w:left=\"").Append(left).Append("\" w:hanging=\"360\"/></w:pPr>");
                }
                else
                {
                    sb.Append("<w:numFmt w:val=\"none\"/><w:lvlText w:val=\"\"/><w:lvlJc w:val=\"left\"/>");
                    sb.Append("<w:pPr><w:ind w:left=\"").Append(left).Append("\" w:hanging=\"360\"/></w:pPr>");
                }
                sb.Append("</w:lvl>");
            }
            sb.Append("</w:abstractNum>");
        }
    }
}
