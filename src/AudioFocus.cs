using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;

namespace MinGent
{
    static class AudioFocus
    {
        public const double DetailSeconds = 600, ListenSeconds = 120;
        const int InlineBeats = 32, InlineHits = 24, InlineNotes = 24;

        public static string Name(string focus)
        {
            switch ((focus ?? "").Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_'))
            {
                case "": case "overview": case "summary": case "all": case "general": case "default": case "full": return "overview";
                case "beats": case "beat": case "tempo": case "bpm": case "bars": case "beat_grid": case "grid": case "rhythm": return "beats";
                case "hits": case "hit": case "onsets": case "onset": case "transients": case "drums": case "attacks": return "hits";
                case "notes": case "note": case "melody": case "pitch": case "pitches": case "midi": return "notes";
                case "sections": case "section": case "structure": case "parts": case "segments": case "form": return "sections";
                case "listen": case "listening": case "hear": case "check": case "quality": case "qa": case "sound": case "review": return "listen";
            }
            return null;
        }

        static string F(double v, string format) { return v.ToString(format, CultureInfo.InvariantCulture); }

        public static string T3(double s) { return F(s, "0.000"); }

        static string Range(AudioInfo info)
        {
            return AudioTime.Clock(info.Start) + " to " + AudioTime.Clock(info.Start + info.Data.Seconds);
        }

        static bool Silent(AudioCollector d)
        {
            double peak = 0;
            for (int c = 0; c < d.Channels; c++) peak = Math.Max(peak, d.Peak[c]);
            return peak < 3e-5;
        }

        public static ToolOutcome Run(ToolContext ctx, string full, double start, double end, string focus, bool pictures)
        {
            bool listen = focus == "listen";
            AudioKeep keep = new AudioKeep();
            keep.Seconds = listen ? ListenSeconds : DetailSeconds;
            if (listen) keep.FullSeconds = ListenSeconds;
            string error;
            AudioInfo info = AnalyzeAudioTool.Open(ctx, full, start, end, keep.Seconds, keep, out error);
            if (info == null) return ToolOutcome.Fail(error);
            string name = Path.GetFileName(full);
            AudioCollector d = info.Data;
            if (d.Frames == 0 || d.Mix.Kept == null || d.Mix.Kept.Count == 0) return ToolOutcome.Fail("there is no sound in that part of " + name + ".");
            if (Silent(d)) return ToolOutcome.Ok(name + " is silent from " + Range(info) + ": every sample is zero or nearly zero.", "silent");
            CancelToken ct = ctx.Ct;
            AudioReport.Loudness loud = AudioReport.Measure(d);
            AudioTrack t = AudioTrack.Build(d.Mix.Kept.ToArray(), d.Mix.DecRate, info.Start, focus == "notes" || listen, ct);
            if (ct != null && ct.IsCancelled) return ToolOutcome.Fail("stopped by the user");
            List<AudioHit> hits = AudioDetail.Hits(t);
            StringBuilder sb = new StringBuilder();
            string summary;
            switch (focus)
            {
                case "beats": summary = Beats(ctx, full, name, info, t, hits, sb); break;
                case "hits": summary = Hits(ctx, full, name, info, hits, sb); break;
                case "notes": summary = Notes(ctx, full, name, info, t, hits, sb); break;
                case "sections": summary = Sections(ctx, full, name, info, t, hits, sb); break;
                default: summary = AudioListen.Report(name, info, loud, d.Mix.KeptFull != null ? d.Mix.KeptFull.ToArray() : new float[0], t, hits, sb); break;
            }
            double covered = info.Start + d.Seconds;
            if (d.Full && (info.FileSeconds <= 0 || info.FileSeconds > covered + 0.5))
                sb.Append("Only ").Append(Range(info)).Append(" was ").Append(listen ? "listened to" : "analyzed").Append(info.FileSeconds > 0 ? " of " + AudioTime.Clock(info.FileSeconds) : "").Append("; set start and end for another part.\n");
            ToolOutcome o = ToolOutcome.Ok(sb.ToString(), summary);
            if (pictures) AddPictures(ctx, o, info, name);
            return o;
        }

        public static void AddPictures(ToolContext ctx, ToolOutcome o, AudioInfo info, string name)
        {
            if (ctx.Cfg == null || !ctx.Cfg.Vision || info.Data.Mix.Count < info.Data.Rate / 20) return;
            try
            {
                List<ImageData> imgs = new List<ImageData>();
                using (Bitmap wf = AudioPictures.Waveform(info, name)) imgs.Add(ImageTools.Encode(wf, 1200));
                using (Bitmap sp = AudioPictures.Spectrogram(info, name)) imgs.Add(ImageTools.Encode(sp, 1200));
                o.Images = imgs;
                o.Text += "The waveform and the spectrogram are below.\n";
            }
            catch (Exception ex) { o.Text += "(The pictures could not be drawn: " + ex.Message + ")\n"; }
        }

        static string Beats(ToolContext ctx, string full, string name, AudioInfo info, AudioTrack t, List<AudioHit> hits, StringBuilder sb)
        {
            AudioBeats b = AudioDetail.Beats(t, hits);
            if (b.Bpm <= 0 || b.Times.Count < 4)
            {
                sb.Append(name).Append(", ").Append(Range(info)).Append(": no steady beat found");
                if (hits.Count > 0) sb.Append(" (it has ").Append(hits.Count).Append(hits.Count == 1 ? " hit" : " hits").Append("; focus=hits lists them)");
                sb.Append(".\n");
                return "no steady beat";
            }
            int n = b.Times.Count, meter = b.Meter, down = b.BarsClear ? b.Downbeat : 0;
            sb.Append(name).Append(", ").Append(Range(info)).Append(": ").Append(F(b.Bpm, "0.0")).Append(" BPM");
            if (b.MaxBpm - b.MinBpm <= Math.Max(1, 0.02 * b.Bpm)) sb.Append(", steady");
            else sb.Append(", drifting between ").Append(F(b.MinBpm, "0.0")).Append(" and ").Append(F(b.MaxBpm, "0.0"));
            sb.Append("; ").Append(n).Append(" beats from ").Append(T3(b.Times[0])).Append(" to ").Append(T3(b.Times[n - 1])).Append(" s.\n");
            if (b.BarsClear) sb.Append("Bars: ").Append(meter).Append(" beats each; the first downbeat is at ").Append(T3(b.Times[down])).Append(" s").Append(down > 0 ? " (" + down + (down == 1 ? " beat" : " beats") + " before it)" : "").Append(".\n");
            else sb.Append("Bars: no clear downbeat, so bars are counted in 4s from the first beat.\n");
            List<string> starts = new List<string>();
            for (int k = down; k < n; k += meter) starts.Add(T3(b.Times[k]));
            StringBuilder csv = new StringBuilder();
            for (int k = 0; k < n; k++)
            {
                int bar = k >= down ? (k - down) / meter + 1 : 0, beat = k >= down ? (k - down) % meter + 1 : meter - (down - k) + 1;
                csv.Append(T3(b.Times[k])).Append(',').Append(bar).Append(',').Append(beat).Append(',').Append(F(Strength(hits, b.Times[k]), "0.00")).Append('\n');
            }
            if (n <= InlineBeats)
            {
                List<string> all = new List<string>();
                foreach (double v in b.Times) all.Add(T3(v));
                sb.Append("Beat times (s): ").Append(string.Join(", ", all.ToArray())).Append(".\n");
                sb.Append("Bar starts (s): ").Append(string.Join(", ", starts.ToArray())).Append(".\n");
            }
            else
            {
                sb.Append("Bar starts (s): ").Append(string.Join(", ", starts.GetRange(0, Math.Min(8, starts.Count)).ToArray())).Append(starts.Count > 8 ? ", \u2026 (" + starts.Count + " bars)" : "").Append(".\n");
                sb.Append(SaveCsv(ctx, full, "beats", "time_s,bar,beat,strength", csv.ToString(), "Every beat with its bar and beat number"));
            }
            return F(b.Bpm, "0.0") + " BPM, " + n + " beats";
        }

        static double Strength(List<AudioHit> hits, double time)
        {
            double best = 0;
            foreach (AudioHit h in hits) if (Math.Abs(h.Time - time) <= 0.03 && h.Strength > best) best = h.Strength;
            return best;
        }

        static string Hits(ToolContext ctx, string full, string name, AudioInfo info, List<AudioHit> hits, StringBuilder sb)
        {
            if (hits.Count == 0)
            {
                sb.Append(name).Append(", ").Append(Range(info)).Append(": no hits found; the sound has no clear attacks.\n");
                return "no hits";
            }
            int low = 0, mid = 0, high = 0;
            foreach (AudioHit h in hits) { if (h.Band == "low") low++; else if (h.Band == "mid") mid++; else high++; }
            double seconds = Math.Max(0.01, info.Data.Seconds);
            sb.Append(name).Append(", ").Append(Range(info)).Append(": ").Append(hits.Count).Append(hits.Count == 1 ? " hit" : " hits");
            if (seconds >= 2) sb.Append(", about ").Append(F(hits.Count / seconds, "0.0")).Append(" a second");
            sb.Append(": ").Append(low).Append(" low (kick, bass), ").Append(mid).Append(" mid (snare, notes), ").Append(high).Append(" high (hi-hats, cymbals).\n");
            StringBuilder csv = new StringBuilder();
            foreach (AudioHit h in hits) csv.Append(T3(h.Time)).Append(',').Append(h.Band).Append(',').Append(F(h.Strength, "0.00")).Append('\n');
            int shown = hits.Count <= InlineHits ? hits.Count : 12;
            List<string> parts = new List<string>();
            for (int i = 0; i < shown; i++) parts.Add(T3(hits[i].Time) + " " + hits[i].Band + " " + F(hits[i].Strength, "0.00"));
            sb.Append(shown == hits.Count ? "Hits (time in s, band, strength 0-1): " : "First hits (time in s, band, strength 0-1): ").Append(string.Join(", ", parts.ToArray())).Append(".\n");
            if (shown < hits.Count) sb.Append(SaveCsv(ctx, full, "hits", "time_s,band,strength", csv.ToString(), "Every hit with its band and strength"));
            return hits.Count + (hits.Count == 1 ? " hit" : " hits");
        }

        static string Notes(ToolContext ctx, string full, string name, AudioInfo info, AudioTrack t, List<AudioHit> hits, StringBuilder sb)
        {
            double voiced;
            List<AudioNote> notes = AudioDetail.Notes(t, hits, out voiced);
            string share = F(Math.Round(Math.Min(1, voiced) * 100), "0") + "%";
            if (notes.Count == 0 || voiced < 0.3)
            {
                sb.Append(name).Append(", ").Append(Range(info)).Append(": no single clear melody; one clear pitch only ").Append(share).Append(" of the time there is sound, so several sounds play at once or the sound is unpitched. Notes work for one voice or instrument at a time.\n");
                string chord = AudioListen.PitchClasses(t);
                if (chord != null) sb.Append("Strongest pitch classes overall: ").Append(chord).Append(".\n");
                return "no clear melody";
            }
            sb.Append(name).Append(", ").Append(Range(info)).Append(": ").Append(notes.Count).Append(notes.Count == 1 ? " note" : " notes").Append(" from ").Append(T3(notes[0].Start)).Append(" to ").Append(T3(notes[notes.Count - 1].End)).Append(" s; one clear pitch ").Append(share).Append(" of the time there is sound.\n");
            string key = AudioDetail.Key(notes);
            if (key != null) sb.Append("Key: ").Append(key).Append(" (a guess from the notes).\n");
            StringBuilder csv = new StringBuilder();
            foreach (AudioNote nt in notes)
                csv.Append(T3(nt.Start)).Append(',').Append(T3(nt.End)).Append(',').Append(nt.Name).Append(',').Append(F(nt.Hz, "0.00")).Append(',').Append(nt.Cents).Append('\n');
            if (notes.Count <= InlineNotes)
            {
                List<string> parts = new List<string>();
                foreach (AudioNote nt in notes) parts.Add(nt.Name + " " + F(nt.Start, "0.00") + "-" + F(nt.End, "0.00") + (Math.Abs(nt.Cents) >= 15 ? " (" + (nt.Cents > 0 ? "+" : "") + nt.Cents + " cents)" : ""));
                sb.Append("Notes (start-end in s): ").Append(string.Join(", ", parts.ToArray())).Append(".\n");
            }
            else
            {
                List<string> names = new List<string>();
                for (int i = 0; i < 16; i++) names.Add(notes[i].Name);
                sb.Append("First notes: ").Append(string.Join(" ", names.ToArray())).Append(" \u2026\n");
                sb.Append(SaveCsv(ctx, full, "notes", "start_s,end_s,note,hz,cents", csv.ToString(), "Every note with its start, end, frequency and tuning"));
            }
            return notes.Count + (notes.Count == 1 ? " note" : " notes");
        }

        static string Sections(ToolContext ctx, string full, string name, AudioInfo info, AudioTrack t, List<AudioHit> hits, StringBuilder sb)
        {
            if (info.Data.Seconds < 8)
            {
                sb.Append(name).Append(" is ").Append(AudioTime.Clock(info.Data.Seconds)).Append(" long, too short to split into sections.\n");
                return "too short";
            }
            List<AudioSection> secs = AudioDetail.Sections(t, hits, info.Data.Blocks, info.Start);
            if (secs.Count < 2)
            {
                sb.Append(name).Append(", ").Append(Range(info)).Append(": one section; the sound stays much the same from start to end.\n");
                return "one section";
            }
            List<double> cen = new List<double>();
            foreach (AudioSection s in secs) cen.Add(s.Centroid);
            cen.Sort();
            double mid = cen[cen.Count / 2];
            sb.Append(name).Append(", ").Append(Range(info)).Append(": ").Append(secs.Count).Append(" sections, found from changes in the sound (not from lyrics or a score); the same letter means it sounds alike:\n");
            StringBuilder csv = new StringBuilder();
            for (int i = 0; i < secs.Count; i++)
            {
                AudioSection s = secs[i];
                string tone = mid > 0 && s.Centroid > mid * 1.4 ? "brighter" : mid > 0 && s.Centroid > 0 && s.Centroid < mid / 1.4 ? "darker" : "";
                csv.Append(s.Label).Append(',').Append(AudioFocus.T3(s.Start)).Append(',').Append(AudioFocus.T3(s.End)).Append(',').Append(double.IsNaN(s.Lufs) ? "" : F(s.Lufs, "0.0")).Append(',').Append(F(s.HitsPerSecond, "0.0")).Append(',').Append(tone).Append('\n');
                if (i >= 16 && secs.Count > 20) continue;
                sb.Append(s.Label).Append(' ').Append(AudioTime.Clock(s.Start)).Append('-').Append(AudioTime.Clock(s.End)).Append(": ");
                List<string> facts = new List<string>();
                facts.Add(double.IsNaN(s.Lufs) ? "too quiet to measure" : F(s.Lufs, "0") + " LUFS");
                facts.Add(F(s.HitsPerSecond, "0.0") + " hits a second");
                if (tone.Length > 0) facts.Add(tone);
                sb.Append(string.Join(", ", facts.ToArray())).Append('\n');
            }
            if (secs.Count > 20)
            {
                sb.Append("\u2026 and ").Append(secs.Count - 16).Append(" more.\n");
                sb.Append(SaveCsv(ctx, full, "sections", "label,start_s,end_s,lufs,hits_per_s,tone", csv.ToString(), "Every section"));
            }
            return secs.Count + " sections";
        }

        static string SaveCsv(ToolContext ctx, string audio, string kind, string header, string rows, string what)
        {
            if (ctx.Session != null && ctx.Session.Perms != null && ctx.Session.Perms.Planning) return "(The full list is saved to a .csv file when you run this outside plan mode.)\n";
            string folder = AgentPaths.IsInside(audio, ctx.Workspace) ? Path.GetDirectoryName(audio) : ctx.Workspace;
            string stem = Path.GetFileNameWithoutExtension(audio) + "." + kind, path = null;
            for (int i = 1; i <= 9 && path == null; i++)
            {
                string p = Path.Combine(folder, stem + (i == 1 ? "" : "-" + i) + ".csv");
                if (!File.Exists(p) || FirstLine(p) == header) path = p;
            }
            if (path == null) return "(The full list was not saved: " + stem + ".csv and the numbered names after it are taken.)\n";
            if (ctx.Cfg.WorkspaceIsDefault && AgentPaths.IsInside(path, ctx.Workspace)) { string we = ctx.EnsureWorkspace(); if (we != null) return "(The full list was not saved: " + we + ".)\n"; }
            string deny = ctx.CheckWrite(path, "Save the audio analysis");
            if (deny != null) return "(The full list was not saved: " + deny + ")\n";
            string err = ToolKit.WriteDisk(ctx, path, header + "\n" + rows, 0, "\n");
            if (err != null) return "(The full list was not saved: " + err + ")\n";
            return what + ": " + path + "\n";
        }

        static string FirstLine(string path)
        {
            try
            {
                using (StreamReader r = new StreamReader(path)) return (r.ReadLine() ?? "").Trim();
            }
            catch { return null; }
        }

        public static ToolOutcome Compare(ToolContext ctx, string full, string other, double start, double end)
        {
            AudioKeep keep = new AudioKeep();
            keep.Seconds = DetailSeconds;
            string error;
            AudioInfo a = AnalyzeAudioTool.Open(ctx, full, start, end, DetailSeconds, keep, out error);
            if (a == null) return ToolOutcome.Fail(error);
            AudioKeep keep2 = new AudioKeep();
            keep2.Seconds = DetailSeconds;
            AudioInfo b = AnalyzeAudioTool.Open(ctx, other, 0, -1, DetailSeconds, keep2, out error);
            if (b == null) return ToolOutcome.Fail(error);
            string na = Path.GetFileName(full), nb = Path.GetFileName(other);
            if (string.Equals(na, nb, StringComparison.OrdinalIgnoreCase)) { na = full; nb = other; }
            if (a.Data.Frames == 0 || Silent(a.Data)) return ToolOutcome.Fail(na + " has no sound in that part, so there is nothing to compare.");
            if (b.Data.Frames == 0 || Silent(b.Data)) return ToolOutcome.Fail(nb + " is silent, so there is nothing to compare.");
            CancelToken ct = ctx.Ct;
            AudioTrack ta = AudioTrack.Build(a.Data.Mix.Kept.ToArray(), a.Data.Mix.DecRate, a.Start, false, ct);
            AudioTrack tb = AudioTrack.Build(b.Data.Mix.Kept.ToArray(), b.Data.Mix.DecRate, b.Start, false, ct);
            if (ct != null && ct.IsCancelled) return ToolOutcome.Fail("stopped by the user");
            AudioDetail.Match m = AudioDetail.Align(ta, tb);
            StringBuilder sb = new StringBuilder();
            string summary;
            if (m.Found)
            {
                string how = m.Quality >= 15 ? "a strong match" : m.Quality >= 9 ? "a good match" : "a weak match, so check it by ear";
                sb.Append("Lined up (").Append(how).Append(m.Fine ? ", to the sample" : ", to about 10 ms").Append("): ");
                if (m.Offset >= 0) sb.Append(nb).Append(" starts ").Append(T3(m.Offset)).Append(" s into ").Append(na);
                else sb.Append(na).Append(" starts ").Append(T3(-m.Offset)).Append(" s into ").Append(nb);
                sb.Append(". To line them up, ").Append(m.Offset >= 0 ? "delay " + nb : "delay " + na).Append(" by ").Append(T3(Math.Abs(m.Offset))).Append(" s.\n");
                Drift(sb, ta, tb, m.Offset, na, nb);
                summary = "lined up at " + T3(m.Offset) + " s";
            }
            else
            {
                sb.Append("No part of ").Append(nb).Append(" matches ").Append(na).Append(", so they don't line up; the facts below may still help match them.\n");
                summary = "no match";
            }
            AudioReport.Loudness la = AudioReport.Measure(a.Data), lb = AudioReport.Measure(b.Data);
            Facts(sb, na, a, ta, la, double.NaN);
            Facts(sb, nb, b, tb, lb, la.Integrated);
            double ca = a.Start + a.Data.Seconds, cb = b.Start + b.Data.Seconds;
            if ((a.Data.Full && a.FileSeconds > ca + 0.5) || (b.Data.Full && b.FileSeconds > cb + 0.5))
                sb.Append("Only the first ").Append(AudioTime.Clock(DetailSeconds)).Append(" of each file was compared; set start and end to move the part of ").Append(na).Append(".\n");
            return ToolOutcome.Ok(sb.ToString(), summary);
        }

        static void Drift(StringBuilder sb, AudioTrack a, AudioTrack b, double offset, string na, string nb)
        {
            List<double[]> pts = AudioDetail.Drift(a, b, offset);
            if (pts.Count < 3) return;
            double mx = 0, my = 0;
            foreach (double[] p in pts) { mx += p[0]; my += p[1]; }
            mx /= pts.Count; my /= pts.Count;
            double sxx = 0, sxy = 0;
            foreach (double[] p in pts) { sxx += (p[0] - mx) * (p[0] - mx); sxy += (p[0] - mx) * (p[1] - my); }
            if (sxx <= 0) return;
            double slope = sxy / sxx, span = pts[pts.Count - 1][0] - pts[0][0], moved = slope * span;
            double worst = 0;
            foreach (double[] p in pts) worst = Math.Max(worst, Math.Abs(p[1] - (my + slope * (p[0] - mx))));
            if (Math.Abs(moved) < 0.015 || worst > Math.Abs(moved) / 2)
            {
                sb.Append("They stay in line over the ").Append(F(span, "0")).Append(" s checked (no drift).\n");
                return;
            }
            sb.Append("They drift apart: over ").Append(F(span, "0")).Append(" s the offset changes by ").Append(F(Math.Abs(moved) * 1000, "0")).Append(" ms, so ")
              .Append(nb).Append(" plays ").Append(F(Math.Abs(slope) * 100, "0.00")).Append("% ").Append(slope < 0 ? "slower" : "faster").Append(" than ").Append(na)
              .Append(". Stretch ").Append(nb).Append(" by ").Append(F(1 / (1 + slope), "0.00000")).Append(" (or line them up again in parts).\n");
        }

        static void Facts(StringBuilder sb, string name, AudioInfo info, AudioTrack t, AudioReport.Loudness loud, double other)
        {
            List<string> f = new List<string>();
            f.Add(AudioTime.Clock(info.FileSeconds > 0 ? info.FileSeconds : info.Start + info.Data.Seconds) + " long");
            AudioBeats b = AudioDetail.Beats(t, AudioDetail.Hits(t));
            f.Add(b.Bpm > 0 && b.Times.Count >= 4 ? F(b.Bpm, "0.0") + " BPM" : "no steady beat");
            string key = AudioReport.Key(info.Data.Mix);
            if (key != null) f.Add("key " + key);
            if (double.IsNaN(loud.Integrated)) f.Add("loudness too low or too short to measure");
            else
            {
                string l = F(loud.Integrated, "0.0") + " LUFS";
                if (!double.IsNaN(other) && Math.Abs(loud.Integrated - other) >= 0.5) l += " (" + F(Math.Abs(loud.Integrated - other), "0.0") + " dB " + (loud.Integrated > other ? "louder" : "quieter") + ")";
                f.Add(l);
            }
            sb.Append(name).Append(": ").Append(string.Join(", ", f.ToArray())).Append(".\n");
        }
    }

    static class AudioListen
    {
        const double Floor = 0.001;

        static string F(double v, string format) { return v.ToString(format, CultureInfo.InvariantCulture); }

        static string Secs(double s)
        {
            if (s < 10) return F(s, "0.00") + " s";
            if (s < 60) return F(s, "0.0") + " s";
            return AudioTime.Clock(s);
        }

        static string Ms(double s) { return s < 1 ? F(Math.Max(0.1, s * 1000), s < 0.01 ? "0.0" : "0") + " ms" : F(s, "0.00") + " s"; }

        static double MaxAbs(float[] x, int a, int b)
        {
            double m = 0;
            for (int i = Math.Max(0, a); i <= Math.Min(x.Length - 1, b); i++) { double v = Math.Abs(x[i]); if (v > m) m = v; }
            return m;
        }

        public static string Report(string name, AudioInfo info, AudioReport.Loudness loud, float[] x, AudioTrack t, List<AudioHit> hits, StringBuilder sb)
        {
            AudioCollector d = info.Data;
            double rate = d.Rate, peak = 0, t0 = info.Start;
            for (int c = 0; c < d.Channels; c++) peak = Math.Max(peak, d.Peak[c]);
            int n = x.Length;
            sb.Append(name).Append(", as heard: ").Append(Secs(n / rate)).Append(", ")
              .Append(info.Channels == 1 ? "mono" : info.Channels == 2 ? "stereo" : info.Channels + " channels").Append(", ").Append(F(rate, "0")).Append(" Hz");
            if (!double.IsNaN(loud.Integrated)) sb.Append(", ").Append(F(loud.Integrated, "0.0")).Append(" LUFS");
            sb.Append(", peak ").Append(F(AudioMath.Db(peak), "0.0")).Append(" dBFS.\n");
            int first = -1, last = -1;
            for (int i = 0; i < n; i++) if (Math.Abs(x[i]) >= Floor) { first = i; break; }
            for (int i = n - 1; i >= 0; i--) if (Math.Abs(x[i]) >= Floor) { last = i; break; }
            if (first < 0)
            {
                sb.Append("Nothing reaches -60 dBFS, so it is silent or nearly so.\n");
                return "nearly silent";
            }
            double f0 = MedianPitch(t);
            int hop = Math.Max(1, (int)(rate / 2000)), win = Math.Max(2 * hop, (int)(rate * Math.Max(0.002, f0 > 0 ? 1.2 / f0 : 0)));
            double[] env = Envelope(x, hop, win);
            double lead = first / rate, trail = (n - 1 - last) / rate;
            int w05 = Math.Max(1, (int)(rate * 0.0005)), w100 = (int)(rate * 0.1);
            bool loop = n >= rate * 0.05 && lead < 0.001 && trail < 0.001;
            bool startMid = first == 0 && Math.Abs(x[0]) >= 0.02, endMid = last == n - 1 && Math.Abs(x[n - 1]) >= 0.02;
            double p05 = MaxAbs(x, first, first + w05), p100 = MaxAbs(x, first, first + w100);
            bool hardStart = startMid || (p100 >= 0.01 && p05 >= 0.5 * p100);
            double e05 = MaxAbs(x, last - w05, last), e100 = MaxAbs(x, last - w100, last);
            bool hardEnd = endMid || (e100 >= 0.01 && e05 >= 0.5 * e100);
            Start(sb, x, env, hop, rate, first, lead, startMid, hardStart, loop);
            List<AudioHit> strong = new List<AudioHit>();
            foreach (AudioHit h in hits) if (h.Strength >= 0.2) strong.Add(h);
            if (strong.Count >= 2) Rhythm(sb, strong);
            else Shape(sb, env, hop, rate, first, last, t0);
            double voiced;
            List<AudioNote> notes = AudioDetail.Notes(t, hits, out voiced);
            double segFrom, segTo, toneHz;
            string pitchKind = Pitch(sb, x, rate, t, notes, out segFrom, out segTo, out toneHz);
            Tone(sb, x, rate, first, last, segFrom - t0, segTo - t0, toneHz, pitchKind);
            End(sb, env, hop, rate, last, trail, endMid, hardEnd, loop, x, t0);
            if (loop && (startMid || endMid)) Loop(sb, x, rate);
            Problems(sb, info, loud, peak, x, rate, first, last, f0, notes, hits, toneHz, t0);
            return "listened, " + Secs(n / rate);
        }

        static double MedianPitch(AudioTrack t)
        {
            if (t.F0 == null) return 0;
            List<double> v = new List<double>();
            for (int f = 0; f < t.Count; f++) if (t.F0[f] > 0 && t.Clarity[f] >= 0.75) v.Add(t.F0[f]);
            if (v.Count == 0) return 0;
            v.Sort();
            return v[v.Count / 2];
        }

        static double[] Envelope(float[] x, int hop, int win)
        {
            int m = x.Length / hop + 1;
            double[] block = new double[m];
            for (int i = 0; i < x.Length; i++)
            {
                double a = Math.Abs(x[i]);
                int b = i / hop;
                if (a > block[b]) block[b] = a;
            }
            int half = Math.Max(0, win / hop / 2);
            double[] env = new double[m];
            for (int k = 0; k < m; k++)
            {
                double v = 0;
                for (int j = Math.Max(0, k - half); j <= Math.Min(m - 1, k + half); j++) if (block[j] > v) v = block[j];
                env[k] = v;
            }
            return env;
        }

        static void Start(StringBuilder sb, float[] x, double[] env, int hop, double rate, int first, double lead, bool mid, bool hard, bool loop)
        {
            sb.Append("Start: ");
            if (lead >= 0.02) sb.Append(Secs(lead)).Append(" of silence, then ");
            if (mid)
            {
                sb.Append("the file begins mid-wave (first sample ").Append(F(x[0], "0.00")).Append(")");
                sb.Append(loop ? ".\n" : ", so playback starts with a click.\n");
                return;
            }
            if (hard)
            {
                sb.Append("the sound starts at full level at once, with no fade-in; on a tone that is heard as a click, on a drum it is the attack.\n");
                return;
            }
            int k0 = first / hop, end = Math.Min(env.Length - 1, k0 + (int)(0.3 * rate / hop));
            double top = 0;
            for (int k = k0; k <= end; k++) top = Math.Max(top, env[k]);
            int from = -1, to = -1;
            for (int k = Math.Max(0, k0 - 4); k <= end && to < 0; k++)
            {
                if (from < 0 && env[k] >= 0.01 * top) from = k;
                if (env[k] >= 0.95 * top) to = k;
            }
            double rise = from >= 0 && to >= from ? (to - from) * hop / rate : 0;
            sb.Append(lead >= 0.02 ? "the sound rises" : "rises").Append(rise < 0.001 ? " to full level in under a millisecond, a sharp attack.\n" : " to full level over " + Ms(rise) + ".\n");
        }

        static void Shape(StringBuilder sb, double[] env, int hop, double rate, int first, int last, double t0)
        {
            int k0 = first / hop, k1 = Math.Min(env.Length - 1, last / hop), kp = k0;
            for (int k = k0; k <= k1; k++) if (env[k] > env[kp]) kp = k;
            double top = env[kp];
            if (top <= 0) return;
            int half = -1, d20 = -1;
            for (int k = kp; k <= k1; k++)
            {
                if (half < 0 && env[k] < 0.5 * top) half = k;
                if (env[k] < 0.1 * top) { d20 = k; break; }
            }
            double at = kp * hop / rate, length = (k1 - kp) * hop / rate;
            sb.Append("Shape: peaks at ").Append(Secs(t0 + at));
            if (half < 0) { sb.Append(" and stays above half level to the end.\n"); return; }
            double halfAfter = (half - kp) * hop / rate;
            bool decays = length > 0 && halfAfter < 0.3 * length;
            if (decays)
            {
                sb.Append(", then decays: half level ").Append(Ms(halfAfter)).Append(" later");
                if (d20 > 0) sb.Append(", 20 dB down ").Append(Ms((d20 - kp) * hop / rate)).Append(" later");
            }
            else
            {
                sb.Append(", stays above half level until ").Append(Secs(t0 + half * hop / rate));
                if (d20 > 0) sb.Append(", then is 20 dB down by ").Append(Secs(t0 + d20 * hop / rate));
            }
            sb.Append(".\n");
        }

        static void Rhythm(StringBuilder sb, List<AudioHit> hits)
        {
            List<double> gaps = new List<double>();
            for (int i = 1; i < hits.Count; i++) gaps.Add(hits[i].Time - hits[i - 1].Time);
            List<double> sorted = new List<double>(gaps);
            sorted.Sort();
            sb.Append("Rhythm: ").Append(hits.Count).Append(" onsets from ").Append(AudioFocus.T3(hits[0].Time)).Append(" to ").Append(AudioFocus.T3(hits[hits.Count - 1].Time)).Append(" s, ");
            if (gaps.Count == 1) { sb.Append(AudioFocus.T3(gaps[0])).Append(" s apart.\n"); return; }
            double low = sorted[0], high = sorted[sorted.Count - 1];
            if (high - low <= 0.06 * low)
            {
                double mean = 0;
                foreach (double g in gaps) mean += g;
                mean /= gaps.Count;
                sb.Append("evenly spaced every ").Append(AudioFocus.T3(mean)).Append(" s (").Append(F(60 / mean, "0.#")).Append(" a minute).\n");
                return;
            }
            double step = sorted[Math.Min(sorted.Count - 1, sorted.Count / 10)], total = 0, units = 0;
            int fit = 0, empty = 0;
            foreach (double g in gaps)
            {
                double m = g / step, r = Math.Round(m);
                if (r >= 1 && Math.Abs(m - r) <= 0.06) { fit++; total += g; units += r; if (r > 1) empty++; }
            }
            if (units > 0) step = total / units;
            if (step > 0.02 && fit >= 0.9 * gaps.Count)
            {
                sb.Append("on a grid of ").Append(AudioFocus.T3(step)).Append(" s (").Append(F(60 / step, "0.#")).Append(" steps a minute)");
                sb.Append(empty > 0 ? ", with some steps left empty.\n" : ".\n");
                return;
            }
            sb.Append("unevenly spaced, ").Append(AudioFocus.T3(low)).Append(" to ").Append(AudioFocus.T3(high)).Append(" s apart.\n");
        }

        class Run
        {
            public int From, To;
            public List<double> Hz = new List<double>();
            public List<int> Frames = new List<int>();
        }

        static List<Run> Runs(AudioTrack t)
        {
            List<Run> runs = new List<Run>();
            if (t.F0 == null) return runs;
            Run cur = null;
            int gap = 0;
            for (int f = 0; f < t.Count; f++)
            {
                bool on = t.F0[f] > 0 && t.Clarity[f] >= 0.75;
                if (on && cur != null && Math.Abs(12 * Math.Log(t.F0[f] / cur.Hz[cur.Hz.Count - 1], 2)) > 1.5) { runs.Add(cur); cur = null; }
                if (on)
                {
                    if (cur == null) { cur = new Run(); cur.From = f; }
                    cur.To = f;
                    cur.Hz.Add(t.F0[f]);
                    cur.Frames.Add(f);
                    gap = 0;
                }
                else if (cur != null && ++gap > 2) { runs.Add(cur); cur = null; }
            }
            if (cur != null) runs.Add(cur);
            return runs;
        }

        static string Pitch(StringBuilder sb, float[] x, double rate, AudioTrack t, List<AudioNote> notes, out double segFrom, out double segTo, out double toneHz)
        {
            segFrom = segTo = toneHz = 0;
            List<Run> runs = Runs(t);
            int voicedFrames = 0, sounding = 0;
            foreach (Run r in runs) voicedFrames += r.Hz.Count;
            for (int f = 0; f < t.Count; f++) if (t.Level[f] >= 0.003) sounding++;
            Run main = null;
            foreach (Run r in runs) if (main == null || r.Hz.Count > main.Hz.Count) main = r;
            if (main != null && main.Hz.Count >= 5 && sounding > 0 && voicedFrames >= 0.3 * sounding)
            {
                double slope, corr;
                Fit(t, main, out slope, out corr);
                double dur = (main.To - main.From) / t.Fps, change = slope * dur;
                if (Math.Abs(change) >= 100 && Math.Abs(corr) >= 0.9 && main.Hz.Count >= 0.6 * voicedFrames)
                {
                    double a = main.Hz[0], b = main.Hz[main.Hz.Count - 1];
                    sb.Append("Pitch: glides ").Append(change > 0 ? "up" : "down").Append(" from ").Append(AudioMath.Hz(a)).Append(" (").Append(AudioMath.Note(a)).Append(") to ").Append(AudioMath.Hz(b)).Append(" (").Append(AudioMath.Note(b)).Append(") over ").Append(Secs(dur)).Append(".\n");
                    return "glide";
                }
                if (notes.Count >= 2)
                {
                    List<string> names = new List<string>();
                    List<double> lens = new List<double>(), steps = new List<double>();
                    for (int i = 0; i < notes.Count; i++)
                    {
                        if (i < 16) names.Add(notes[i].Name);
                        lens.Add(notes[i].End - notes[i].Start);
                        if (i > 0) steps.Add(notes[i].Start - notes[i - 1].Start);
                    }
                    lens.Sort();
                    steps.Sort();
                    sb.Append("Notes: ").Append(string.Join(" ", names.ToArray())).Append(notes.Count > 16 ? " \u2026" : "").Append(" (").Append(notes.Count).Append(" notes");
                    double ml = lens[lens.Count / 2], ms = steps[steps.Count / 2];
                    if (lens[lens.Count - 1] - lens[0] <= 0.25 * ml) sb.Append(", each about ").Append(Secs(ml));
                    else sb.Append(", ").Append(Secs(lens[0])).Append(" to ").Append(Secs(lens[lens.Count - 1])).Append(" long");
                    if (steps[steps.Count - 1] - steps[0] <= 0.1 * ms) sb.Append(", one every ").Append(Secs(ms));
                    sb.Append(").\n");
                    AudioNote longest = notes[0];
                    foreach (AudioNote nt in notes) if (nt.End - nt.Start > longest.End - longest.Start) longest = nt;
                    double l = longest.End - longest.Start;
                    segFrom = longest.Start + 0.1 * l; segTo = longest.End - 0.1 * l; toneHz = longest.Hz;
                    return "notes";
                }
                double from = t.Time(main.From), to = t.Time(main.To), len = to - from;
                segFrom = from + 0.1 * len; segTo = to - 0.1 * len;
                double center;
                string vib = Vibrato(t, main, out center);
                toneHz = vib != null ? center : Refine(x, rate, segFrom - t.Start, segTo - t.Start, center);
                sb.Append("Pitch: a steady ").Append(toneHz >= 999.5 ? AudioMath.Hz(toneHz) : F(toneHz, "0.0") + " Hz").Append(" (").Append(Tuning(toneHz)).Append(")");
                if (vib != null) sb.Append(" with vibrato ").Append(vib);
                sb.Append(".\n");
                return "steady";
            }
            double[] p = Spectrum(x, 0, x.Length, rate);
            if (p != null)
            {
                double binHz = rate / ((p.Length - 1) * 2.0), total = 0;
                int top = 1;
                for (int k = 1; k < p.Length; k++) { total += p[k]; if (p[k] > p[top]) top = k; }
                double near = 0;
                for (int k = Math.Max(1, top - 3); k <= Math.Min(p.Length - 1, top + 3); k++) near += p[k];
                double hzTop = Peak(p, top) * binHz;
                if (total > 0 && near >= 0.5 * total && hzTop >= 1500)
                {
                    toneHz = hzTop;
                    segFrom = t.Start; segTo = t.Start + x.Length / rate;
                    sb.Append("Pitch: a steady tone at ").Append(AudioMath.Hz(hzTop)).Append(" (").Append(Tuning(hzTop)).Append(").\n");
                    return "steady";
                }
            }
            if (MedianFlatness(t) >= 0.3) { sb.Append("Pitch: none; it is noise-like.\n"); return "noise"; }
            string classes = PitchClasses(t);
            if (classes != null) { sb.Append("Pitch: several tones at once; the strongest pitch classes are ").Append(classes).Append(".\n"); return "chord"; }
            sb.Append("Pitch: no clear pitch.\n");
            return "none";
        }

        static void Fit(AudioTrack t, Run r, out double slope, out double corr)
        {
            double n = r.Hz.Count, sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0, refHz = r.Hz[0];
            for (int i = 0; i < r.Hz.Count; i++)
            {
                double xx = r.Frames[i] / t.Fps, yy = 1200 * Math.Log(r.Hz[i] / refHz, 2);
                sx += xx; sy += yy; sxx += xx * xx; syy += yy * yy; sxy += xx * yy;
            }
            double vx = sxx - sx * sx / n, vy = syy - sy * sy / n, cxy = sxy - sx * sy / n;
            slope = vx > 0 ? cxy / vx : 0;
            corr = vx > 0 && vy > 0 ? cxy / Math.Sqrt(vx * vy) : 0;
        }

        static string Vibrato(AudioTrack t, Run r, out double center)
        {
            List<double> sorted = new List<double>(r.Hz);
            sorted.Sort();
            center = sorted[sorted.Count / 2];
            double dur = r.Hz.Count / t.Fps;
            if (dur < 0.4) return null;
            List<double> c = new List<double>();
            foreach (double h in r.Hz) c.Add(1200 * Math.Log(h / center, 2));
            int w = Math.Max(1, (int)(t.Fps * 0.3) / 2);
            List<double> dev = new List<double>();
            double mean = 0;
            for (int i = 0; i < c.Count; i++)
            {
                double s = 0;
                int k = 0;
                for (int j = Math.Max(0, i - w); j <= Math.Min(c.Count - 1, i + w); j++) { s += c[j]; k++; }
                dev.Add(c[i] - s / k);
                mean += c[i];
            }
            int crossings = 0;
            double sq = 0;
            for (int i = 0; i < dev.Count; i++)
            {
                sq += dev[i] * dev[i];
                if (i > 0 && (dev[i - 1] < 0) != (dev[i] < 0)) crossings++;
            }
            double depth = Math.Sqrt(sq / dev.Count) * Math.Sqrt(2), speed = crossings / (2 * dur);
            if (speed < 4 || speed > 9 || depth < 15) return null;
            center = center * Math.Pow(2, mean / c.Count / 1200);
            return "(" + F(speed, "0.0") + " Hz, " + F(depth, "0") + " cents each way)";
        }

        static double MedianFlatness(AudioTrack t)
        {
            List<double> v = new List<double>();
            for (int f = 0; f < t.Count; f++) if (t.Level[f] >= 0.003) v.Add(t.Flatness[f]);
            if (v.Count == 0) return 0;
            v.Sort();
            return v[v.Count / 2];
        }

        public static string PitchClasses(AudioTrack t)
        {
            double[] sum = new double[12];
            for (int f = 0; f < t.Count; f++)
            {
                if (t.Level[f] < 0.003) continue;
                for (int i = 0; i < 12; i++) sum[i] += t.Chroma[f][i];
            }
            double top = 0;
            foreach (double v in sum) top = Math.Max(top, v);
            if (top <= 0) return null;
            string[] names = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
            List<int> idx = new List<int>();
            for (int i = 0; i < 12; i++) if (sum[i] >= 0.5 * top) idx.Add(i);
            if (idx.Count < 2 || idx.Count > 6) return null;
            idx.Sort(delegate (int a, int b) { return sum[b].CompareTo(sum[a]); });
            List<string> parts = new List<string>();
            foreach (int i in idx) parts.Add(names[i]);
            if (parts.Count == 2) return parts[0] + " and " + parts[1];
            return string.Join(", ", parts.GetRange(0, parts.Count - 1).ToArray()) + " and " + parts[parts.Count - 1];
        }

        static int Pow2(int n)
        {
            int p = 256;
            while (p < n && p < 65536) p <<= 1;
            return p;
        }

        static double[] Spectrum(float[] x, int from, int to, double rate)
        {
            from = Math.Max(0, from);
            to = Math.Min(x.Length, to);
            int len = to - from;
            if (len < 64) return null;
            int size = Math.Min(rate > 50000 ? 16384 : 8192, Pow2(len));
            double[] win = AudioMath.Hann(size), re = new double[size], im = new double[size], acc = new double[size / 2 + 1];
            int frames = 0, step = Math.Max(size / 2, (len - size) / 200);
            for (int s = from; ; s += step)
            {
                for (int i = 0; i < size; i++)
                {
                    int j = s + i;
                    re[i] = j < to ? x[j] * win[i] : 0;
                    im[i] = 0;
                }
                AudioMath.Fft(re, im, size);
                for (int k = 0; k <= size / 2; k++) acc[k] += re[k] * re[k] + im[k] * im[k];
                frames++;
                if (s + size >= to) break;
            }
            for (int k = 0; k < acc.Length; k++) acc[k] /= frames;
            return acc;
        }

        static double Peak(double[] p, int k)
        {
            if (k <= 0 || k >= p.Length - 1) return k;
            double a = Math.Log(p[k - 1] + 1e-30), b = Math.Log(p[k] + 1e-30), c = Math.Log(p[k + 1] + 1e-30), den = a - 2 * b + c;
            return den < 0 ? k + Math.Max(-0.5, Math.Min(0.5, 0.5 * (a - c) / den)) : k;
        }

        static string Tuning(double hz)
        {
            double cents = 1200 * Math.Log(hz / 440.0, 2);
            int off = (int)Math.Round(cents - Math.Round(cents / 100) * 100);
            return AudioMath.Note(hz) + (Math.Abs(off) <= 1 ? ", in tune" : ", " + (off > 0 ? "+" : "") + off + " cents");
        }

        static double Refine(float[] x, double rate, double from, double to, double hz)
        {
            int a = Math.Max(0, (int)(from * rate)), b = Math.Min(x.Length, (int)(to * rate)), len = b - a;
            if (len < 256) return hz;
            int size = 1024;
            while (size < len * 4 && size < 262144) size <<= 1;
            int used = Math.Min(len, size);
            double[] win = AudioMath.Hann(used), re = new double[size], im = new double[size];
            for (int i = 0; i < used; i++) re[i] = x[a + i] * win[i];
            AudioMath.Fft(re, im, size);
            double[] p = new double[size / 2 + 1];
            for (int k = 0; k <= size / 2; k++) p[k] = re[k] * re[k] + im[k] * im[k];
            double binHz = rate / size;
            int lo = Math.Max(1, (int)(hz * 0.97 / binHz)), hi = Math.Min(p.Length - 2, (int)Math.Ceiling(hz * 1.03 / binHz));
            int best = -1;
            double strongest = 0;
            for (int k = 1; k < p.Length; k++) strongest = Math.Max(strongest, p[k]);
            for (int k = lo; k <= hi; k++) if (best < 0 || p[k] > p[best]) best = k;
            if (best < 0 || p[best] < strongest * 0.01) return hz;
            return Peak(p, best) * binHz;
        }

        static void Tone(StringBuilder sb, float[] x, double rate, int first, int last, double segFrom, double segTo, double hz, string kind)
        {
            if (kind == "noise" || kind == "none" || kind == "chord" || kind == "glide")
            {
                double[] all = Spectrum(x, first, last + 1, rate);
                if (all == null) return;
                double binHz = rate / ((all.Length - 1) * 2.0), total = 0;
                for (int k = 1; k < all.Length; k++) total += all[k];
                if (total <= 0) return;
                double run = 0, p10 = 0, p50 = 0, p90 = 0;
                for (int k = 1; k < all.Length; k++)
                {
                    run += all[k];
                    if (p10 == 0 && run >= 0.1 * total) p10 = k * binHz;
                    if (p50 == 0 && run >= 0.5 * total) p50 = k * binHz;
                    if (p90 == 0 && run >= 0.9 * total) { p90 = k * binHz; break; }
                }
                sb.Append("Tone: ").Append(kind == "noise" ? "noise-like, with " : "").Append("most of its energy between ").Append(AudioMath.Hz(p10)).Append(" and ").Append(AudioMath.Hz(p90)).Append(", centered near ").Append(AudioMath.Hz(p50)).Append(".\n");
                return;
            }
            if (hz <= 0 || segTo <= segFrom) return;
            double[] p = Spectrum(x, (int)(segFrom * rate), (int)(segTo * rate), rate);
            if (p == null) return;
            double bin = rate / ((p.Length - 1) * 2.0), nyq = rate * 0.45;
            List<double> lv = new List<double>();
            List<int> at = new List<int>();
            for (int h = 1; h <= 12 && h * hz < nyq; h++)
            {
                double c = h * hz / bin;
                int w = Math.Max(2, (int)Math.Ceiling(0.015 * c)), best = -1;
                for (int k = Math.Max(1, (int)c - w); k <= Math.Min(p.Length - 1, (int)c + w + 1); k++) if (best < 0 || p[k] > p[best]) best = k;
                double e = 0;
                for (int k = Math.Max(1, best - 2); k <= Math.Min(p.Length - 1, best + 2); k++) e += p[k];
                lv.Add(e);
                at.Add(best);
            }
            if (lv.Count == 0 || lv[0] <= 0) return;
            double[] db = new double[lv.Count];
            for (int i = 0; i < lv.Count; i++) db[i] = 10 * Math.Log10(lv[i] / lv[0] + 1e-30);
            sb.Append("Tone").Append(kind == "notes" ? " (longest note)" : "").Append(": ").Append(Timbre(db)).Append(".\n");
            Inharmonic(sb, p, bin, hz, at, lv[0]);
        }

        static string Timbre(double[] db)
        {
            int n = db.Length;
            Func<int, double> H = delegate (int h) { return h <= n ? db[h - 1] : -120; };
            bool pure = true;
            for (int h = 2; h <= n; h++) if (H(h) > -30) pure = false;
            if (pure) return "pure, like a sine wave (every overtone is more than 30 dB below the fundamental)";
            double even = -120, odd = -120;
            for (int h = 2; h <= Math.Min(n, 8); h++) { if (h % 2 == 0) even = Math.Max(even, H(h)); else odd = Math.Max(odd, H(h)); }
            string list = Levels(db);
            if (n >= 3 && odd > -35 && even <= odd - 20)
            {
                if (n >= 5 && H(3) > -13 && H(3) < -6.5 && H(5) > -17.5 && H(5) < -10.5) return "square-like: odd harmonics only (" + list + ")";
                if (n >= 5 && H(3) > -23 && H(3) < -15 && H(5) > -33 && H(5) < -23) return "triangle-like: odd harmonics only (" + list + ")";
                return "odd harmonics only (" + list + ")";
            }
            if (n >= 4 && H(2) > -9 && H(2) < -3 && H(3) > -13 && H(3) < -6.5 && H(4) > -15.5 && H(4) < -8.5)
                return "sawtooth-like: every harmonic, falling off evenly (" + list + ")";
            return "overtones " + list;
        }

        static string Levels(double[] db)
        {
            List<string> parts = new List<string>();
            for (int h = 2; h <= db.Length && parts.Count < 6; h++)
            {
                if (db[h - 1] < -60) continue;
                parts.Add(Ordinal(h) + " " + F(db[h - 1], "0") + " dB");
            }
            return parts.Count == 0 ? "all below -60 dB" : string.Join(", ", parts.ToArray());
        }

        static string Ordinal(int h) { return h + (h == 2 ? "nd" : h == 3 ? "rd" : "th"); }

        static void Inharmonic(StringBuilder sb, double[] p, double bin, double hz, List<int> at, double fundamental)
        {
            bool[] near = new bool[p.Length];
            for (int h = 1; h * hz < p.Length * bin; h++)
            {
                int c = (int)Math.Round(h * hz / bin), w = Math.Max(3, (int)Math.Ceiling(0.02 * c));
                for (int k = Math.Max(0, c - w); k <= Math.Min(p.Length - 1, c + w); k++) near[k] = true;
            }
            foreach (int k0 in at) for (int k = Math.Max(0, k0 - 3); k <= Math.Min(p.Length - 1, k0 + 3); k++) near[k] = true;
            List<double> all = new List<double>();
            for (int k = 1; k < p.Length; k++) all.Add(p[k]);
            all.Sort();
            double floor = all[all.Count / 2];
            int best = -1;
            for (int k = Math.Max(2, (int)(Math.Max(2 * hz, 1000) / bin)); k < p.Length - 1; k++)
            {
                if (near[k] || p[k] < p[k - 1] || p[k] < p[k + 1]) continue;
                if (best < 0 || p[k] > p[best]) best = k;
            }
            if (best < 0) return;
            double e = p[best - 1] + p[best] + p[best + 1], rel = 10 * Math.Log10(e / fundamental + 1e-30);
            if (p[best] < floor * 300 || rel < -40) return;
            sb.Append("Also partials that are not harmonics of the pitch, strongest at ").Append(AudioMath.Hz(Peak(p, best) * bin)).Append(" (").Append(F(rel, "0")).Append(" dB): aliasing from a wave made without band-limiting, another note at the same time, or a bell-like sound.\n");
        }

        static void End(StringBuilder sb, double[] env, int hop, double rate, int last, double trail, bool mid, bool hard, bool loop, float[] x, double t0)
        {
            sb.Append("End: ");
            if (mid) sb.Append("the file stops mid-wave (last sample ").Append(F(x[x.Length - 1], "0.00")).Append(")").Append(loop ? "" : ", so playback ends with a click");
            else if (hard) sb.Append("the sound stops at once at ").Append(Secs(t0 + last / rate)).Append(", with no fade-out; that is heard as a click");
            else
            {
                int k1 = Math.Min(env.Length - 1, last / hop), k0 = Math.Max(0, k1 - (int)(0.3 * rate / hop));
                double top = 0;
                for (int k = k0; k <= k1; k++) top = Math.Max(top, env[k]);
                int from = k0, to = k1;
                for (int k = k1; k >= k0; k--) if (env[k] >= 0.95 * top) { from = k; break; }
                for (int k = from; k <= k1; k++) if (env[k] < 0.01 * top) { to = k; break; }
                double fall = Math.Max(0, to - from) * hop / rate;
                sb.Append(fall < 0.001 ? "stops within a millisecond, ending at " : "fades out over " + Ms(fall) + ", ending at ").Append(Secs(t0 + last / rate));
            }
            if (trail >= 0.02) sb.Append(", then ").Append(Secs(trail)).Append(" of silence");
            sb.Append(".\n");
        }

        static void Loop(StringBuilder sb, float[] x, double rate)
        {
            int n = x.Length, w = Math.Max(4, (int)(rate * 0.01));
            List<double> steps = new List<double>();
            for (int i = 1; i < Math.Min(n, w); i++) steps.Add(Math.Abs(x[i] - x[i - 1]));
            for (int i = Math.Max(1, n - w); i < n; i++) steps.Add(Math.Abs(x[i] - x[i - 1]));
            steps.Sort();
            double typical = steps.Count > 0 ? steps[(int)(steps.Count * 0.95)] : 0, jump = Math.Abs(x[0] - x[n - 1]);
            if (jump <= Math.Max(3 * typical, 0.01)) sb.Append("Loop: both ends are mid-wave but join smoothly, so it loops without a click (played once, it clicks at both ends).\n");
            else sb.Append("Loop: played as a loop, the end jumps to the start by ").Append(F(jump, "0.00")).Append(", which clicks each time it repeats.\n");
        }

        static void Problems(StringBuilder sb, AudioInfo info, AudioReport.Loudness loud, double peak, float[] x, double rate, int first, int last, double f0, List<AudioNote> notes, List<AudioHit> hits, double toneHz, double t0)
        {
            AudioCollector d = info.Data;
            List<string> p = new List<string>();
            string clicks = Clicks(x, rate, first, last, f0 > 0 ? f0 : toneHz, notes, hits, t0);
            if (clicks != null) p.Add(clicks);
            if (d.ClipEvents > 0)
                p.Add("clipped flat at full scale in " + d.ClipEvents + (d.ClipEvents == 1 ? " place" : " places") + ", first at " + AudioFocus.T3(info.Start + d.FirstClip / (double)d.Rate) + " s");
            if (d.OverEvents > 0)
                p.Add(d.Overs + (d.Overs == 1 ? " sample goes" : " samples go") + " past full scale, first at " + AudioFocus.T3(info.Start + d.FirstOver / (double)d.Rate) + " s; they clip when saved as 16- or 24-bit");
            double dc = 0;
            for (int c = 0; c < d.Channels; c++) { double v = d.Sum[c] / d.Frames; if (Math.Abs(v) > Math.Abs(dc)) dc = v; }
            if (Math.Abs(dc) >= 0.01) p.Add("DC offset " + (dc > 0 ? "+" : "") + F(dc, "0.000") + ": the wave is not centered on zero (subtract the mean or high-pass it)");
            if (!double.IsNaN(loud.Integrated) && loud.Integrated > -6) p.Add("very loud at " + F(loud.Integrated, "0.0") + " LUFS; finished music is usually -14 to -8 LUFS");
            else if (!double.IsNaN(loud.Integrated) && loud.Integrated < -35) p.Add("very quiet at " + F(loud.Integrated, "0.0") + " LUFS");
            else if (double.IsNaN(loud.Integrated) && peak < 0.03) p.Add("very quiet: the peak is only " + F(AudioMath.Db(peak), "0.0") + " dBFS");
            if (peak >= 0.989 && d.ClipEvents == 0 && d.OverEvents == 0) p.Add("the peak reaches 0 dBFS, leaving no headroom; MP3 or AAC encoding will clip it");
            if (d.Side) p.Add("the two channels cancel when mixed to mono (one is inverted)");
            else if (d.Channels == 2 && d.SumSq[0] <= 1e-12 * d.SumSq[1]) p.Add("the left channel is silent");
            else if (d.Channels == 2 && d.SumSq[1] <= 1e-12 * d.SumSq[0]) p.Add("the right channel is silent");
            if (rate < 22050) p.Add("the " + F(rate, "0") + " Hz sample rate keeps nothing above " + AudioMath.Hz(rate / 2) + ", so it sounds muffled");
            string rumble = Rumble(x, rate, first, last, f0 > 0 ? f0 : toneHz);
            if (rumble != null) p.Add(rumble);
            if (p.Count == 0) sb.Append("Problems: none found (no clicks, clipping, DC offset or extreme loudness).\n");
            else sb.Append("Problems: ").Append(string.Join("; ", p.ToArray())).Append(".\n");
        }

        static string Rumble(float[] x, double rate, int first, int last, double f0)
        {
            if (f0 > 0 && f0 < 60) return null;
            double[] p = Spectrum(x, first, last + 1, rate);
            if (p == null) return null;
            double bin = rate / ((p.Length - 1) * 2.0), low = 0, total = 0;
            for (int k = 1; k < p.Length; k++) { total += p[k]; if (k * bin < 40) low += p[k]; }
            if (total <= 0 || low < 0.15 * total) return null;
            return F(100 * low / total, "0") + "% of the energy is below 40 Hz, which most speakers can't play and which uses up headroom";
        }

        class Click
        {
            public int From, To, At;
            public double Size;
        }

        static string Regular(List<Click> found, double rate)
        {
            if (found.Count < 4) return null;
            List<double> gaps = new List<double>();
            for (int i = 1; i < found.Count; i++) gaps.Add(found[i].At - found[i - 1].At);
            double mean = 0, sd = 0;
            foreach (double g in gaps) mean += g;
            mean /= gaps.Count;
            foreach (double g in gaps) sd += (g - mean) * (g - mean);
            sd = Math.Sqrt(sd / gaps.Count);
            if (mean <= 0 || sd > 0.02 * mean) return null;
            return "every " + F(mean / rate, "0.0000") + " s (" + F(mean, "0") + " samples)";
        }

        static double Local(float[] x, int from, int to, List<double> buf)
        {
            buf.Clear();
            for (int i = Math.Max(1, from); i < Math.Min(x.Length - 1, to); i++) buf.Add(Math.Abs(x[i + 1] - 2.0 * x[i] + x[i - 1]));
            if (buf.Count == 0) return 0;
            buf.Sort();
            return buf[(int)(buf.Count * 0.9)];
        }

        static bool Locked(List<Click> found, double rate, double f0)
        {
            if (f0 <= 0 || found.Count < 4) return false;
            double period = rate / f0;
            int with = 0;
            for (int i = 0; i < found.Count; i++)
            {
                bool next = false;
                for (int j = i + 1; j < found.Count && found[j].At - found[i].At <= period * 1.05; j++)
                    if (Math.Abs(found[j].At - found[i].At - period) <= Math.Max(2, period * 0.05)) { next = true; break; }
                if (next || i >= found.Count - 2) with++;
            }
            return with >= 0.7 * found.Count;
        }

        static string Clicks(float[] x, double rate, int first, int last, double f0, List<AudioNote> notes, List<AudioHit> hits, double t0)
        {
            int n = x.Length, block = Math.Max(16, (int)(rate * 0.01));
            if (n < 3 * block) return null;
            int blocks = n / block + 1;
            double[] q = new double[blocks];
            double[] buf = new double[block];
            for (int b = 0; b < blocks; b++)
            {
                int a = Math.Max(1, b * block), e = Math.Min(n - 1, (b + 1) * block), m = 0;
                for (int i = a; i < e; i++) buf[m++] = Math.Abs(x[i + 1] - 2.0 * x[i] + x[i - 1]);
                if (m == 0) continue;
                Array.Sort(buf, 0, m);
                q[b] = buf[(int)(m * 0.9)];
            }
            int lo = first + (int)(rate * 0.001), hi = last - (int)(rate * 0.001), side = Math.Max(8, (int)(rate * 0.002)), skip = 3;
            List<Click> found = new List<Click>();
            List<double> scratch = new List<double>();
            Click cur = null;
            int gap = Math.Max(1, (int)(rate * 0.001));
            for (int i = Math.Max(1, lo); i < Math.Min(n - 1, hi); i++)
            {
                int b = i / block;
                double s1 = q[Math.Max(0, b - 1)], s2 = q[b], s3 = q[Math.Min(blocks - 1, b + 1)];
                double scale = Math.Max(Math.Min(s1, s2), Math.Min(Math.Max(s1, s2), s3));
                double v = Math.Abs(x[i + 1] - 2.0 * x[i] + x[i - 1]);
                if (v <= Math.Max(0.02, 12 * scale)) continue;
                if (cur != null && i - cur.To <= gap) { cur.To = i; if (v > cur.Size) { cur.Size = v; cur.At = i; } continue; }
                double around = Math.Max(Local(x, i + skip, i + skip + side, scratch), Local(x, i - skip - side, i - skip, scratch));
                if (v <= 12 * around) continue;
                cur = new Click();
                cur.From = cur.To = cur.At = i;
                cur.Size = v;
                found.Add(cur);
            }
            if (found.Count == 0) return null;
            double sounding = Math.Max(0.05, (hi - lo) / rate);
            bool merged = false;
            foreach (Click c in found) if (c.To - c.From > rate * 0.01) merged = true;
            if (merged || found.Count / sounding > 20)
            {
                if (f0 > 0 && (merged || Locked(found, rate, f0))) return null;
                string every = Regular(found, rate);
                if (every != null) return "a sudden jump " + every + " throughout, which points to a buffer or block boundary in the code";
                return "crackle: about " + F(found.Count / sounding, "0") + " sudden jumps a second throughout";
            }
            double size = 0;
            List<string> times = new List<string>();
            foreach (Click c in found)
            {
                size = Math.Max(size, c.Size);
                if (times.Count < 8) times.Add(AudioFocus.T3(t0 + c.At / rate));
            }
            StringBuilder sbc = new StringBuilder();
            sbc.Append(found.Count == 1 ? "a click inside the sound at " : found.Count + " clicks inside the sound, at ").Append(string.Join(", ", times.ToArray())).Append(found.Count > 8 ? " \u2026" : "").Append(" s (jumps up to ").Append(F(size, "0.00")).Append(")");
            string regular = Regular(found, rate);
            if (regular != null) sbc.Append(", one ").Append(regular).Append(", which points to a buffer or block boundary in the code");
            int atChanges = 0;
            foreach (Click c in found)
            {
                double tc = t0 + c.At / rate;
                bool hit = false;
                foreach (AudioNote nt in notes) if (Math.Abs(nt.Start - tc) <= 0.025 || Math.Abs(nt.End - tc) <= 0.025) { hit = true; break; }
                if (!hit) foreach (AudioHit h in hits) if (Math.Abs(h.Time - tc) <= 0.025) { hit = true; break; }
                if (hit) atChanges++;
            }
            if (atChanges * 2 >= found.Count && regular == null) sbc.Append(", at note changes: notes start or stop mid-wave, so give each note a short fade in and out or keep the phase running");
            return sbc.ToString();
        }
    }
}
