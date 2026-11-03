using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MinGent
{
    class AudioTrack
    {
        public const int BandCount = 16;
        public double Rate, Fps, Start;
        public int Hop, Size, Count;
        public float[] X;
        public float[] Flux, Onset, Pulse, Low, Mid, High, Level, Centroid, Flatness, F0, Clarity;
        public float[][] Bands, Chroma;

        public double Time(double frame) { return Start + frame * Hop / Rate; }

        public double Seconds { get { return X.Length / Rate; } }

        public static AudioTrack Build(float[] x, double rate, double start, bool pitch, CancelToken ct)
        {
            AudioTrack t = new AudioTrack();
            t.X = x ?? new float[0];
            t.Rate = rate;
            t.Start = start;
            t.Size = rate > 13000 ? 2048 : 1024;
            t.Hop = Math.Max(16, (int)Math.Round(rate / 86.0));
            t.Fps = rate / t.Hop;
            int n = t.X.Length == 0 ? 0 : t.X.Length / t.Hop + 1;
            t.Count = n;
            t.Flux = new float[n]; t.Onset = new float[n]; t.Pulse = new float[n]; t.Low = new float[n]; t.Mid = new float[n]; t.High = new float[n];
            t.Level = new float[n]; t.Centroid = new float[n]; t.Flatness = new float[n];
            t.Bands = new float[n][]; t.Chroma = new float[n][];
            int size = t.Size, half = size / 2;
            double[] win = AudioMath.Hann(size), re = new double[size], im = new double[size];
            double[] power = new double[half + 1];
            double binHz = rate / size, top = Math.Min(rate * 0.45, 8000);
            int topBin = Math.Min(half, (int)(top / binHz));
            const int OnN = 512, OnHalf = OnN / 2;
            double[] onWin = AudioMath.Hann(OnN), ore = new double[OnN], oim = new double[OnN];
            double[][] past = new double[2][];
            for (int i = 0; i < 2; i++) past[i] = new double[OnHalf + 1];
            double[] pnow = new double[OnHalf + 1];
            double onHz = rate / OnN, norm = OnN * (double)OnN / 16;
            int onTop = Math.Min(OnHalf - 1, (int)(top / onHz));
            List<int> edges = new List<int>();
            edges.Add(Math.Max(1, (int)(40 / onHz)));
            for (double hz = 40 * Math.Pow(2, 1 / 3.0); hz < top; hz *= Math.Pow(2, 1 / 3.0))
            {
                int k = (int)Math.Round(hz / onHz);
                if (k > edges[edges.Count - 1] && k <= onTop) edges.Add(k);
            }
            if (edges[edges.Count - 1] < onTop + 1) edges.Add(onTop + 1);
            int nbands = edges.Count - 1;
            int[] region = new int[nbands];
            int[] regionCount = new int[3];
            for (int b = 0; b < nbands; b++)
            {
                double hz = Math.Sqrt(edges[b] * (double)edges[b + 1]) * onHz;
                region[b] = hz < 200 ? 0 : hz < 2000 ? 1 : 2;
                regionCount[region[b]]++;
            }
            double[][] lev = new double[3][];
            for (int i = 0; i < 3; i++) { lev[i] = new double[nbands]; for (int b = 0; b < nbands; b++) lev[i][b] = -6; }
            int[] bandAt = new int[half + 1];
            int[] pitchClass = new int[half + 1];
            double edgeLo = 50, edgeHi = Math.Max(edgeLo * 2, top);
            for (int k = 0; k <= half; k++)
            {
                double hz = k * binHz;
                bandAt[k] = hz < edgeLo || hz >= edgeHi ? -1 : Math.Min(BandCount - 1, (int)(BandCount * Math.Log(hz / edgeLo) / Math.Log(edgeHi / edgeLo)));
                pitchClass[k] = hz < 55 || hz > 2100 ? -1 : (((int)Math.Round(69 + 12 * Math.Log(hz / 440.0, 2))) % 12 + 12) % 12;
            }
            for (int f = 0; f < n; f++)
            {
                if ((f & 255) == 0 && ct != null && ct.IsCancelled) break;
                int c = f * t.Hop, from = c - half;
                for (int i = 0; i < OnN; i++)
                {
                    int j = c - OnHalf + i;
                    ore[i] = j >= 0 && j < t.X.Length ? t.X[j] * onWin[i] : 0;
                    oim[i] = 0;
                }
                AudioMath.Fft(ore, oim, OnN);
                double[] now = past[1], lnow = lev[2];
                past[1] = past[0]; past[0] = now;
                lev[2] = lev[1]; lev[1] = lev[0]; lev[0] = lnow;
                double plain = 0;
                double[] rise = new double[3];
                for (int k = 1; k <= OnHalf; k++)
                {
                    double pk = ore[k] * ore[k] + oim[k] * oim[k];
                    pnow[k] = pk / norm;
                    now[k] = Math.Log(1 + 100 * Math.Sqrt(pk));
                }
                double[] one = past[1], lone = lev[1], ltwo = lev[2];
                for (int k = 1; k <= onTop; k++)
                {
                    double d = now[k] - one[k];
                    if (d > 0) plain += d;
                }
                double sup = 0, pulse = 0;
                for (int b = 0; b < nbands; b++)
                {
                    double e = 0;
                    for (int k = edges[b]; k < edges[b + 1]; k++) e += pnow[k];
                    lnow[b] = Math.Max(-6, Math.Log10(e + 1e-12));
                }
                for (int b = 0; b < nbands; b++)
                {
                    double d = lnow[b] - lone[b];
                    if (d > 0) pulse += d;
                    double ref2 = ltwo[b];
                    if (b > 0) ref2 = Math.Max(ref2, ltwo[b - 1]);
                    if (b + 1 < nbands) ref2 = Math.Max(ref2, ltwo[b + 1]);
                    double e = lnow[b] - ref2;
                    if (e > 0) { sup += e; rise[region[b]] += e; }
                }
                t.Flux[f] = (float)plain;
                t.Onset[f] = (float)sup;
                t.Pulse[f] = (float)pulse;
                t.Low[f] = regionCount[0] > 0 ? (float)(rise[0] / regionCount[0]) : 0;
                t.Mid[f] = regionCount[1] > 0 ? (float)(rise[1] / regionCount[1]) : 0;
                t.High[f] = regionCount[2] > 0 ? (float)(rise[2] / regionCount[2]) : 0;
                for (int i = 0; i < size; i++)
                {
                    int j = from + i;
                    re[i] = j >= 0 && j < t.X.Length ? t.X[j] * win[i] : 0;
                    im[i] = 0;
                }
                AudioMath.Fft(re, im, size);
                double sum = 0, wsum = 0, logSum = 0;
                int logN = 0;
                float[] bands = new float[BandCount];
                double[] bandPow = new double[BandCount];
                double[] chroma = new double[12];
                for (int k = 1; k <= half; k++)
                {
                    double p = re[k] * re[k] + im[k] * im[k];
                    power[k] = p;
                    if (k <= topBin)
                    {
                        double hz = k * binHz;
                        if (hz >= 30) { sum += p; wsum += p * hz; }
                        if (hz >= 100) { logSum += Math.Log(p + 1e-12); logN++; }
                    }
                    if (bandAt[k] >= 0) bandPow[bandAt[k]] += p;
                    if (pitchClass[k] >= 0) chroma[pitchClass[k]] += Math.Sqrt(p);
                }
                t.Centroid[f] = sum > 0 ? (float)(wsum / sum) : 0;
                double mean = 0;
                int cnt = 0;
                for (int k = 1; k <= topBin; k++) if (k * binHz >= 100) { mean += power[k]; cnt++; }
                mean = cnt > 0 ? mean / cnt : 0;
                t.Flatness[f] = mean > 1e-12 && logN > 0 ? (float)Math.Min(1, Math.Exp(logSum / logN) / mean) : 0;
                for (int b = 0; b < BandCount; b++) bands[b] = (float)(10 * Math.Log10(bandPow[b] + 1e-10));
                t.Bands[f] = bands;
                float[] ch = new float[12];
                for (int i = 0; i < 12; i++) ch[i] = (float)chroma[i];
                t.Chroma[f] = ch;
                double sq = 0;
                int a = Math.Max(0, c - t.Hop / 2), b2 = Math.Min(t.X.Length, c + t.Hop / 2);
                for (int i = a; i < b2; i++) sq += (double)t.X[i] * t.X[i];
                t.Level[f] = b2 > a ? (float)Math.Sqrt(sq / (b2 - a)) : 0;
            }
            if (pitch) t.Pitch(ct);
            return t;
        }

        void Pitch(CancelToken ct)
        {
            F0 = new float[Count];
            Clarity = new float[Count];
            int w = Math.Max(256, (int)(Rate * 0.046));
            int maxLag = Math.Min(w - 2, (int)(Rate / 50)), minLag = Math.Max(2, (int)(Rate / 1700));
            double[] d = new double[maxLag + 2];
            double quiet = Math.Pow(10, -50 / 10.0) * w;
            for (int f = 0; f < Count; f++)
            {
                if ((f & 127) == 0 && ct != null && ct.IsCancelled) break;
                int s = f * Hop - (w + maxLag) / 2;
                if (s < 0 || s + w + maxLag + 1 > X.Length) continue;
                double energy = 0;
                for (int i = 0; i < w; i++) energy += (double)X[s + i] * X[s + i];
                if (energy < quiet) continue;
                double run = 0;
                d[0] = 1;
                int best = -1, filled = 0;
                for (int tau = 1; tau <= maxLag; tau++)
                {
                    double sum = 0;
                    for (int i = 0; i < w; i++) { double e = X[s + i] - X[s + i + tau]; sum += e * e; }
                    run += sum;
                    d[tau] = run > 0 ? sum * tau / run : 1;
                    filled = tau;
                    if (tau >= minLag + 1 && tau < maxLag && d[tau - 1] < 0.15 && d[tau] >= d[tau - 1]) { best = tau - 1; break; }
                }
                if (best < 0)
                {
                    for (int tau = minLag; tau < filled; tau++)
                    {
                        if (d[tau] >= 0.15) continue;
                        while (tau + 1 < filled && d[tau + 1] < d[tau]) tau++;
                        best = tau;
                        break;
                    }
                }
                if (best >= 0 && best + 1 > filled) best = -1;
                if (best < 1) continue;
                double x0 = d[best - 1], x1 = d[best], x2 = d[best + 1], den = x0 - 2 * x1 + x2;
                double period = best + (den != 0 ? Math.Max(-1, Math.Min(1, 0.5 * (x0 - x2) / den)) : 0);
                if (period <= 0) continue;
                double hz = Rate / period;
                if (hz < 45 || hz > 1700) continue;
                F0[f] = (float)hz;
                Clarity[f] = (float)Math.Max(0, 1 - x1);
            }
        }
    }

    class AudioHit
    {
        public double Time, Strength;
        public string Band = "";
        public int Frame;
    }

    class AudioNote
    {
        public double Start, End, Midi, Hz;
        public string Name { get { return AudioMath.Note(Hz); } }
        public int Cents { get { return (int)Math.Round((Midi - Math.Round(Midi)) * 100); } }
    }

    class AudioSection
    {
        public double Start, End;
        public string Label = "";
        public int Repeats = -1;
        public double Lufs = double.NaN, HitsPerSecond, Centroid;
    }

    class AudioBeats
    {
        public double Bpm, Strength;
        public List<double> Times = new List<double>();
        public int Meter = 4, Downbeat;
        public bool BarsClear;
        public double MinBpm, MaxBpm, Spread;
    }

    static class AudioDetail
    {
        public const double Lead = 0.0;

        static float[] Novelty(float[] flux, double fps)
        {
            int n = flux.Length;
            float[] o = new float[n];
            if (n == 0) return o;
            int half = Math.Max(2, (int)Math.Round(fps * 0.1));
            double[] pre = new double[n + 1];
            for (int i = 0; i < n; i++) pre[i + 1] = pre[i] + flux[i];
            for (int i = 0; i < n; i++)
            {
                int a = Math.Max(0, i - half), b = Math.Min(n - 1, i + half);
                o[i] = (float)Math.Max(0, flux[i] - (pre[b + 1] - pre[a]) / (b - a + 1));
            }
            return o;
        }

        static double Mean(float[] v)
        {
            if (v.Length == 0) return 0;
            double s = 0;
            foreach (float x in v) s += x;
            return s / v.Length;
        }

        static double Std(float[] v, double mean)
        {
            if (v.Length < 2) return 0;
            double s = 0;
            foreach (float x in v) s += (x - mean) * (x - mean);
            return Math.Sqrt(s / v.Length);
        }

        static double Max(float[] v)
        {
            double m = 0;
            foreach (float x in v) if (x > m) m = x;
            return m;
        }

        public static List<AudioHit> Hits(AudioTrack t)
        {
            List<AudioHit> hits = new List<AudioHit>();
            if (t.Count < 3) return hits;
            float[] o = Novelty(t.Onset, t.Fps);
            double top = Max(o);
            if (top <= 0) return hits;
            double mean = Mean(o), sd = Std(o, mean);
            int rise = Math.Max(2, (int)Math.Round(t.Fps * 0.03));
            int w = Math.Max(1, (int)Math.Round(t.Fps * 0.03)), back = Math.Max(2, (int)Math.Round(t.Fps * 0.2)), ahead = Math.Max(1, (int)Math.Round(t.Fps * 0.05));
            int gap = Math.Max(1, (int)Math.Round(t.Fps * 0.04));
            double[] pre = new double[o.Length + 1];
            for (int i = 0; i < o.Length; i++) pre[i + 1] = pre[i] + o[i];
            double quiet = Math.Pow(10, -55 / 20.0);
            int last = -gap - 1;
            for (int i = 1; i < o.Length - 1; i++)
            {
                double v = o[i];
                if (v < 0.04 * top || v <= 0) continue;
                bool peak = true;
                for (int j = Math.Max(0, i - w); j <= Math.Min(o.Length - 1, i + w) && peak; j++) if (o[j] > v || (o[j] == v && j < i)) peak = false;
                if (!peak) continue;
                int a = Math.Max(0, i - back), b = Math.Min(o.Length - 1, i + ahead);
                double local = (pre[b + 1] - pre[a]) / (b - a + 1);
                if (v < local + 0.5 * sd) continue;
                double lvl = 0, before = 0;
                for (int j = i; j <= Math.Min(t.Count - 1, i + rise); j++) lvl = Math.Max(lvl, t.Level[j]);
                for (int j = Math.Max(0, i - rise); j < i; j++) before += t.Level[j];
                before /= Math.Max(1, i - Math.Max(0, i - rise));
                if (lvl < quiet || lvl < before * 1.12) continue;
                if (i - last < gap)
                {
                    if (hits.Count > 0 && v > hits[hits.Count - 1].Strength * top) { hits.RemoveAt(hits.Count - 1); }
                    else continue;
                }
                AudioHit h = new AudioHit();
                h.Frame = i;
                h.Strength = v / top;
                h.Time = Refine(t, i);
                double l = 0, m = 0, hh = 0;
                for (int j = i; j <= Math.Min(t.Count - 1, i + 1); j++) { l += t.Low[j]; m += t.Mid[j]; hh += t.High[j]; }
                h.Band = l >= m && l >= hh ? "low" : hh >= m ? "high" : "mid";
                hits.Add(h);
                last = i;
            }
            return hits;
        }

        static double Typical(float[] v)
        {
            List<float> l = new List<float>();
            foreach (float x in v) if (x > 0) l.Add(x);
            if (l.Count == 0) return 0;
            l.Sort();
            return l[(int)(l.Count * 0.9)];
        }

        static double Refine(AudioTrack t, int frame)
        {
            int c = frame * t.Hop;
            int step = Math.Max(1, (int)(t.Rate / 1000)), width = 3 * step;
            int from = Math.Max(1, c - (int)(t.Rate * 0.035)), to = Math.Min(t.X.Length, c + (int)(t.Rate * 0.025));
            if (to - from < width * 4) return t.Time(frame) - Lead;
            List<double> env = new List<double>();
            for (int i = from; i + width <= to; i += step)
            {
                double s = 0;
                for (int j = i; j < i + width; j++) { double d = t.X[j] - t.X[j - 1]; s += d * d; }
                env.Add(Math.Sqrt(s / width));
            }
            int p = 0;
            for (int i = 1; i < env.Count; i++) if (env[i] > env[p]) p = i;
            if (env[p] <= 0 || p == 0) return t.Time(frame) - Lead;
            int at = -1;
            double steep = 0;
            for (int i = Math.Max(0, p - (int)(0.04 * 1000)); i < p; i++)
            {
                double r = env[i + 1] - env[i];
                if (r > steep) { steep = r; at = i; }
            }
            if (at < 0) return t.Time(frame) - Lead;
            while (at > 0 && env[at] - env[at - 1] > 0.25 * steep) at--;
            return t.Start + (from + at * step + width + step / 2.0) / t.Rate;
        }

        public static AudioBeats Beats(AudioTrack t, List<AudioHit> hits)
        {
            AudioBeats r = new AudioBeats();
            if (t.Count < t.Fps * 4) return r;
            AudioTempo tempo = AudioTempo.Find(new List<float>(t.Flux), t.Fps);
            r.Bpm = tempo.Bpm;
            r.Strength = tempo.Strength;
            if (tempo.Bpm <= 0 || tempo.Strength < 1.5) { r.Bpm = 0; return r; }
            float[] o = Novelty(t.Pulse, t.Fps);
            double mean = Mean(o), sd = Std(o, mean);
            if (sd <= 0) { r.Bpm = 0; return r; }
            int n = o.Length;
            double[] on = new double[n];
            for (int i = 0; i < n; i++) on[i] = o[i] / sd;
            double period = t.Fps * 60 / tempo.Bpm;
            double[] score = new double[n];
            int[] link = new int[n];
            const double tight = 100;
            for (int i = 0; i < n; i++)
            {
                int a = (int)Math.Round(i - 2 * period), b = (int)Math.Round(i - period / 2);
                double best = double.NegativeInfinity;
                int at = -1;
                for (int j = Math.Max(0, a); j <= b && j < i; j++)
                {
                    double lg = Math.Log((i - j) / period);
                    double s = score[j] - tight * lg * lg;
                    if (s > best) { best = s; at = j; }
                }
                score[i] = on[i] + (at >= 0 && best > 0 ? best : 0);
                link[i] = at >= 0 && best > 0 ? at : -1;
            }
            int end = n - 1;
            double top = double.NegativeInfinity;
            for (int i = Math.Max(0, n - (int)Math.Ceiling(period)); i < n; i++) if (score[i] > top) { top = score[i]; end = i; }
            List<int> frames = new List<int>();
            for (int i = end; i >= 0; i = link[i]) { frames.Add(i); if (link[i] < 0) break; }
            frames.Reverse();
            double quiet = Math.Pow(10, -50 / 20.0);
            int first = 0, lastF = frames.Count - 1;
            double firstHit = hits.Count > 0 ? hits[0].Time : t.Start, lastHit = hits.Count > 0 ? hits[hits.Count - 1].Time : t.Start + t.Seconds;
            double margin = 0.75 * period / t.Fps;
            while (first < frames.Count && (LevelNear(t, frames[first]) < quiet || t.Time(frames[first]) < firstHit - margin)) first++;
            while (lastF > first && (LevelNear(t, frames[lastF]) < quiet || t.Time(frames[lastF]) > lastHit + margin)) lastF--;
            List<double> hitTimes = new List<double>();
            foreach (AudioHit h in hits) hitTimes.Add(h.Time);
            double snap = 0.07;
            int hi = 0;
            for (int k = first; k <= lastF; k++)
            {
                double tm = t.Time(frames[k]) - Lead;
                while (hi < hitTimes.Count && hitTimes[hi] < tm - snap) hi++;
                double near = tm;
                double bestD = snap;
                for (int q = hi; q < hitTimes.Count && hitTimes[q] <= tm + snap; q++)
                    if (Math.Abs(hitTimes[q] - tm) < bestD) { bestD = Math.Abs(hitTimes[q] - tm); near = hitTimes[q]; }
                r.Times.Add(near);
            }
            if (r.Times.Count >= 4)
            {
                List<double> ibi = new List<double>();
                for (int k = 1; k < r.Times.Count; k++) ibi.Add(r.Times[k] - r.Times[k - 1]);
                List<double> sorted = new List<double>(ibi);
                sorted.Sort();
                double med = sorted[sorted.Count / 2];
                r.Bpm = 60 / med;
                List<double> local = new List<double>();
                for (int k = 0; k + 8 <= ibi.Count; k += 4)
                {
                    double s = 0;
                    for (int q = k; q < k + 8; q++) s += ibi[q];
                    local.Add(60 / (s / 8));
                }
                r.MinBpm = r.MaxBpm = r.Bpm;
                foreach (double v in local) { r.MinBpm = Math.Min(r.MinBpm, v); r.MaxBpm = Math.Max(r.MaxBpm, v); }
                double dev = 0;
                foreach (double v in ibi) dev += Math.Abs(v - med);
                r.Spread = dev / ibi.Count;
                Meter(t, r);
            }
            return r;
        }

        static double LevelNear(AudioTrack t, int frame)
        {
            double m = 0;
            for (int i = Math.Max(0, frame - 4); i <= Math.Min(t.Count - 1, frame + 8); i++) m = Math.Max(m, t.Level[i]);
            return m;
        }

        static void Meter(AudioTrack t, AudioBeats r)
        {
            int nb = r.Times.Count;
            double[] s = new double[nb];
            double lowRef = Typical(t.Low), allRef = Typical(t.Flux);
            for (int k = 0; k < nb; k++)
            {
                int f = (int)Math.Round((r.Times[k] - t.Start) * t.Fps);
                double lo = 0, all = 0;
                for (int i = Math.Max(0, f - 2); i <= Math.Min(t.Count - 1, f + 3); i++) { lo = Math.Max(lo, t.Low[i]); all = Math.Max(all, t.Flux[i]); }
                double change = 0;
                if (k > 0 && k + 1 < nb) change = ChromaChange(t, r.Times[k - 1], r.Times[k], r.Times[k + 1]);
                s[k] = (lowRef > 0 ? lo / lowRef : 0) + 0.5 * (allRef > 0 ? all / allRef : 0) + 1.5 * change;
            }
            double sm = 0;
            foreach (double v in s) sm += v;
            sm /= nb;
            double sd = 0;
            foreach (double v in s) sd += (v - sm) * (v - sm);
            sd = Math.Sqrt(sd / nb);
            double best = double.NegativeInfinity;
            int bestM = 4, bestP = 0;
            foreach (int m in new int[] { 4, 3 })
            {
                if (nb < m * 3) continue;
                for (int p = 0; p < m; p++)
                {
                    double on = 0, off = 0;
                    int non = 0, noff = 0;
                    for (int k = 0; k < nb; k++)
                    {
                        if (((k - p) % m + m) % m == 0) { on += s[k]; non++; }
                        else { off += s[k]; noff++; }
                    }
                    if (non == 0 || noff == 0) continue;
                    double c = on / non - off / noff;
                    if (m == 3) c *= 0.85;
                    if (c > best) { best = c; bestM = m; bestP = p; }
                }
            }
            r.Meter = bestM;
            r.Downbeat = bestP;
            r.BarsClear = sd > 0 && best > 0.35 * sd;
            if (!r.BarsClear) { r.Meter = 4; r.Downbeat = 0; }
        }

        static double ChromaChange(AudioTrack t, double a, double b, double c)
        {
            double[] x = ChromaMean(t, a, b), y = ChromaMean(t, b, c);
            double dot = 0, nx = 0, ny = 0;
            for (int i = 0; i < 12; i++) { dot += x[i] * y[i]; nx += x[i] * x[i]; ny += y[i] * y[i]; }
            if (nx <= 0 || ny <= 0) return 0;
            return 1 - dot / Math.Sqrt(nx * ny);
        }

        static double[] ChromaMean(AudioTrack t, double from, double to)
        {
            double[] m = new double[12];
            int a = Math.Max(0, (int)((from - t.Start) * t.Fps)), b = Math.Min(t.Count - 1, (int)((to - t.Start) * t.Fps));
            for (int f = a; f <= b; f++) for (int i = 0; i < 12; i++) m[i] += t.Chroma[f][i];
            return m;
        }

        public static List<AudioNote> Notes(AudioTrack t, List<AudioHit> hits, out double voiced)
        {
            List<AudioNote> notes = new List<AudioNote>();
            voiced = 0;
            if (t.F0 == null) return notes;
            bool[] onset = new bool[t.Count];
            foreach (AudioHit h in hits) if (h.Strength >= 0.15) for (int i = Math.Max(0, h.Frame - 1); i <= Math.Min(t.Count - 1, h.Frame + 1); i++) onset[i] = true;
            int sounding = 0, pitched = 0;
            double quiet = Math.Pow(10, -50 / 20.0);
            List<double> run = new List<double>();
            int startF = -1, lastF = -1, gap = 0;
            for (int f = 0; f <= t.Count; f++)
            {
                bool loud = f < t.Count && t.Level[f] >= quiet;
                if (loud) sounding++;
                bool on = f < t.Count && t.F0[f] > 0 && t.Clarity[f] >= 0.75;
                if (on) pitched++;
                double midi = on ? 69 + 12 * Math.Log(t.F0[f] / 440.0, 2) : 0;
                bool cont = false;
                if (on && run.Count > 0)
                {
                    double med = Median(run);
                    cont = Math.Abs(midi - med) < 0.7 && !(onset[f] && f - startF > 2);
                }
                if (on && cont) { run.Add(midi); lastF = f; gap = 0; continue; }
                if (!on && run.Count > 0 && gap < 2 && f < t.Count) { gap++; continue; }
                if (run.Count > 0) Close(t, notes, run, startF, lastF);
                run.Clear();
                gap = 0;
                if (on) { run.Add(midi); startF = f; lastF = f; }
            }
            voiced = sounding > 0 ? pitched / (double)sounding : 0;
            List<AudioNote> merged = new List<AudioNote>();
            foreach (AudioNote nt in notes)
            {
                AudioNote prev = merged.Count > 0 ? merged[merged.Count - 1] : null;
                if (prev != null && Math.Round(prev.Midi) == Math.Round(nt.Midi) && nt.Start - prev.End < 0.03 && !OnsetBetween(hits, prev.End - 0.02, nt.Start + 0.02))
                {
                    prev.End = nt.End;
                    continue;
                }
                merged.Add(nt);
            }
            for (int i = 1; i < merged.Count; i++)
            {
                AudioNote a = merged[i - 1], b = merged[i];
                if (b.Start < a.End || b.Start - a.End > 0.08) continue;
                int fa = Math.Max(0, (int)Math.Round((a.End - t.Start) * t.Fps) - 3), fb = Math.Min(t.Count - 1, (int)Math.Round((b.Start - t.Start) * t.Fps) + 1);
                bool silent = false;
                for (int f = fa; f <= fb; f++) if (t.Level[f] < quiet) silent = true;
                if (silent || fb <= fa) continue;
                int best = fa;
                for (int f = fa; f <= fb; f++) if (t.Flux[f] > t.Flux[best]) best = f;
                double at = t.Time(best + 1);
                if (at > a.Start + 0.03 && at < b.End - 0.03) { a.End = at; b.Start = at; }
            }
            return merged;
        }

        static bool OnsetBetween(List<AudioHit> hits, double a, double b)
        {
            foreach (AudioHit h in hits) if (h.Strength >= 0.15 && h.Time >= a && h.Time <= b) return true;
            return false;
        }

        static void Close(AudioTrack t, List<AudioNote> notes, List<double> run, int startF, int lastF)
        {
            List<float> lv = new List<float>();
            for (int f = startF; f <= lastF; f++) lv.Add(t.Level[f]);
            lv.Sort();
            double med = lv[lv.Count / 2];
            while (lastF > startF + 2 && t.Level[lastF] < 0.4 * med) lastF--;
            double start = t.Time(startF) - 0.5 / t.Fps, end = t.Time(lastF) + 0.5 / t.Fps;
            if (end - start < 0.05 || run.Count < 3) return;
            AudioNote n = new AudioNote();
            n.Midi = Median(run);
            n.Hz = 440 * Math.Pow(2, (n.Midi - 69) / 12);
            n.Start = Math.Max(t.Start, start);
            n.End = end;
            notes.Add(n);
        }

        static double Median(List<double> v)
        {
            List<double> s = new List<double>(v);
            s.Sort();
            return s.Count == 0 ? 0 : s[s.Count / 2];
        }

        public static List<AudioSection> Sections(AudioTrack t, List<AudioHit> hits, List<double> lufsBlocks, double lufsStart)
        {
            List<AudioSection> list = new List<AudioSection>();
            double seconds = t.Count / t.Fps;
            if (seconds < 4) return list;
            double blockSec = seconds < 40 ? 0.25 : 0.5;
            int per = Math.Max(1, (int)Math.Round(blockSec * t.Fps));
            int nb = t.Count / per;
            if (nb < 8) return list;
            int dims = AudioTrack.BandCount + 12 + 1;
            double[][] feat = new double[nb][];
            for (int b = 0; b < nb; b++)
            {
                double[] v = new double[dims];
                double lvl = 0;
                for (int f = b * per; f < (b + 1) * per; f++)
                {
                    for (int i = 0; i < AudioTrack.BandCount; i++) v[i] += t.Bands[f][i];
                    double cs = 0;
                    for (int i = 0; i < 12; i++) cs += t.Chroma[f][i];
                    if (cs > 0) for (int i = 0; i < 12; i++) v[AudioTrack.BandCount + i] += t.Chroma[f][i] / cs;
                    lvl += t.Level[f] * (double)t.Level[f];
                }
                for (int i = 0; i < dims - 1; i++) v[i] /= per;
                v[dims - 1] = 10 * Math.Log10(lvl / per + 1e-10);
                feat[b] = v;
            }
            double[] mu = new double[dims], sd = new double[dims];
            for (int b = 0; b < nb; b++) for (int i = 0; i < dims; i++) mu[i] += feat[b][i];
            for (int i = 0; i < dims; i++) mu[i] /= nb;
            for (int b = 0; b < nb; b++) for (int i = 0; i < dims; i++) sd[i] += (feat[b][i] - mu[i]) * (feat[b][i] - mu[i]);
            for (int i = 0; i < dims; i++) sd[i] = Math.Sqrt(sd[i] / nb);
            double[][] z = new double[nb][];
            for (int b = 0; b < nb; b++)
            {
                z[b] = new double[dims];
                for (int i = 0; i < dims; i++)
                {
                    double w = i == dims - 1 ? 2.0 : i >= AudioTrack.BandCount ? 0.8 : 1.0;
                    z[b][i] = sd[i] > 1e-9 ? w * (feat[b][i] - mu[i]) / sd[i] : 0;
                }
            }
            int kernel = Math.Max(4, (int)Math.Round((seconds < 40 ? 2.0 : 6.0) / blockSec));
            kernel = Math.Min(kernel, Math.Max(2, nb / 4));
            double[] nov = new double[nb];
            double wsum = 0;
            for (int i = -kernel; i < kernel; i++)
                for (int j = -kernel; j < kernel; j++) wsum += Math.Exp(-0.5 * ((i + 0.5) * (i + 0.5) + (j + 0.5) * (j + 0.5)) / (kernel * kernel * 0.25));
            List<double> valid = new List<double>();
            for (int c = kernel; c + kernel <= nb; c++)
            {
                double s = 0;
                for (int i = -kernel; i < kernel; i++)
                    for (int j = -kernel; j < kernel; j++)
                    {
                        double g = Math.Exp(-0.5 * ((i + 0.5) * (i + 0.5) + (j + 0.5) * (j + 0.5)) / (kernel * kernel * 0.25));
                        double sign = (i < 0) == (j < 0) ? 1 : -1;
                        s += sign * g * Cos(z[c + i], z[c + j]);
                    }
                nov[c] = Math.Max(0, s / wsum);
                valid.Add(nov[c]);
            }
            double nmax = 0;
            foreach (double v in nov) nmax = Math.Max(nmax, v);
            valid.Sort();
            double need = Math.Max(0.2, 2 * (valid.Count > 0 ? valid[valid.Count / 2] : 0));
            List<int> bounds = new List<int>();
            bounds.Add(0);
            if (nmax > 0)
            {
                for (int c = 1; c < nb - 1; c++)
                {
                    double v = nov[c];
                    if (v < 0.3 * nmax || v < need) continue;
                    bool peak = true;
                    for (int j = Math.Max(0, c - kernel); j <= Math.Min(nb - 1, c + kernel) && peak; j++) if (nov[j] > v || (nov[j] == v && j < c)) peak = false;
                    if (peak && c - bounds[bounds.Count - 1] >= kernel) bounds.Add(c);
                }
            }
            bounds.Add(nb);
            if (bounds.Count >= 3 && bounds[bounds.Count - 1] - bounds[bounds.Count - 2] < kernel) bounds.RemoveAt(bounds.Count - 2);
            List<double[]> means = new List<double[]>();
            for (int k = 0; k + 1 < bounds.Count; k++)
            {
                double[] m = new double[dims];
                for (int b = bounds[k]; b < bounds[k + 1]; b++) for (int i = 0; i < dims; i++) m[i] += z[b][i];
                means.Add(m);
                AudioSection s = new AudioSection();
                s.Start = t.Start + bounds[k] * per / t.Fps;
                s.End = t.Start + (k + 2 == bounds.Count ? t.Count : bounds[k + 1] * per) / t.Fps;
                list.Add(s);
            }
            List<int> reps = new List<int>();
            for (int k = 0; k < list.Count; k++)
            {
                int same = -1;
                double best = 0.8;
                for (int r = 0; r < reps.Count; r++)
                {
                    double c = Cos(means[k], means[reps[r]]);
                    if (c > best) { best = c; same = r; }
                }
                if (same < 0) { reps.Add(k); list[k].Label = ((char)('A' + Math.Min(25, reps.Count - 1))).ToString(); }
                else { list[k].Label = list[reps[same]].Label; list[k].Repeats = reps[same]; }
            }
            foreach (AudioSection s in list)
            {
                int cnt = 0;
                foreach (AudioHit h in hits) if (h.Time >= s.Start && h.Time < s.End && h.Strength >= 0.1) cnt++;
                s.HitsPerSecond = cnt / Math.Max(0.01, s.End - s.Start);
                List<float> cen = new List<float>();
                int a = (int)((s.Start - t.Start) * t.Fps), b2 = Math.Min(t.Count, (int)((s.End - t.Start) * t.Fps));
                for (int f = a; f < b2; f++) if (t.Level[f] > 1e-3) cen.Add(t.Centroid[f]);
                cen.Sort();
                s.Centroid = cen.Count > 0 ? cen[cen.Count / 2] : 0;
                if (lufsBlocks != null) s.Lufs = Lufs(lufsBlocks, s.Start - lufsStart, s.End - lufsStart);
            }
            return list;
        }

        static double Cos(double[] a, double[] b)
        {
            double dot = 0, na = 0, nb = 0;
            for (int i = 0; i < a.Length; i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
            return na > 0 && nb > 0 ? dot / Math.Sqrt(na * nb) : 0;
        }

        public static double Lufs(List<double> blocks, double from, double to)
        {
            int a = Math.Max(0, (int)Math.Floor(from * 10)), b = Math.Min(blocks.Count, (int)Math.Ceiling(to * 10));
            double abs = Math.Pow(10, (-70 + 0.691) / 10), sum = 0;
            int n = 0;
            for (int i = a; i + 4 <= b; i++)
            {
                double m = (blocks[i] + blocks[i + 1] + blocks[i + 2] + blocks[i + 3]) / 4;
                if (m > abs) { sum += m; n++; }
            }
            if (n == 0) return double.NaN;
            double rel = sum / n * 0.1;
            double s2 = 0;
            int n2 = 0;
            for (int i = a; i + 4 <= b; i++)
            {
                double m = (blocks[i] + blocks[i + 1] + blocks[i + 2] + blocks[i + 3]) / 4;
                if (m > abs && m > rel) { s2 += m; n2++; }
            }
            return n2 > 0 ? -0.691 + 10 * Math.Log10(s2 / n2) : double.NaN;
        }

        public static string Key(List<AudioNote> notes)
        {
            double[] chroma = new double[12];
            double total = 0;
            foreach (AudioNote n in notes)
            {
                int pc = (((int)Math.Round(n.Midi)) % 12 + 12) % 12;
                chroma[pc] += n.End - n.Start;
                total += n.End - n.Start;
            }
            if (total <= 0 || notes.Count < 6) return null;
            return AudioReport.KeyFrom(chroma, 0.55);
        }

        public class Match
        {
            public double Offset, Quality;
            public bool Found, Fine;
        }

        public static Match Align(AudioTrack a, AudioTrack b)
        {
            Match m = new Match();
            const double fps = 100;
            double[] ea = Envelope(a, fps), eb = Envelope(b, fps);
            if (ea.Length < fps || eb.Length < fps) return m;
            int n = 1;
            while (n < ea.Length + eb.Length) n <<= 1;
            double[] ar = new double[n], ai = new double[n], br = new double[n], bi = new double[n];
            Array.Copy(ea, ar, ea.Length);
            Array.Copy(eb, br, eb.Length);
            AudioMath.Fft(ar, ai, n);
            AudioMath.Fft(br, bi, n);
            double[] cr = new double[n], ci = new double[n];
            for (int k = 0; k < n; k++)
            {
                cr[k] = ar[k] * br[k] + ai[k] * bi[k];
                ci[k] = -(ai[k] * br[k] - ar[k] * bi[k]);
            }
            AudioMath.Fft(cr, ci, n);
            double na = 0, nb2 = 0;
            foreach (double v in ea) na += v * v;
            foreach (double v in eb) nb2 += v * v;
            if (na <= 0 || nb2 <= 0) return m;
            int minOverlap = (int)(Math.Min(ea.Length, eb.Length) * 0.5);
            double best = double.NegativeInfinity;
            int bestLag = 0;
            double[] score = new double[n];
            for (int k = 0; k < n; k++)
            {
                int lag = k <= n / 2 ? k : k - n;
                int overlap = Math.Min(ea.Length, lag + eb.Length) - Math.Max(0, lag);
                if (overlap < Math.Max(minOverlap, (int)fps)) { score[k] = double.NaN; continue; }
                double v = cr[k] / n / Math.Sqrt(na * nb2) * Math.Min(ea.Length, eb.Length) / overlap;
                score[k] = v;
                if (v > best) { best = v; bestLag = lag; }
            }
            double sum = 0, sq = 0;
            int cnt = 0;
            for (int k = 0; k < n; k++)
            {
                if (double.IsNaN(score[k])) continue;
                int lag = k <= n / 2 ? k : k - n;
                if (Math.Abs(lag - bestLag) < fps * 0.5) continue;
                sum += score[k];
                sq += score[k] * score[k];
                cnt++;
            }
            if (cnt < 10) return m;
            double mean = sum / cnt, sdv = Math.Sqrt(Math.Max(0, sq / cnt - mean * mean));
            m.Quality = sdv > 0 ? (best - mean) / sdv : 0;
            m.Found = m.Quality >= 6;
            m.Offset = bestLag / fps + a.Start - b.Start;
            m.Offset = FineOffset(a, b, m.Offset, out m.Fine);
            return m;
        }

        static double[] Envelope(AudioTrack t, double fps)
        {
            float[] o = Novelty(t.Flux, t.Fps);
            int n = (int)(t.Count / t.Fps * fps);
            double[] e = new double[n];
            double mean = Mean(o), sd = Std(o, mean);
            for (int i = 0; i < n; i++)
            {
                double f = i / fps * t.Fps;
                int k = (int)f;
                if (k + 1 >= o.Length) break;
                double v = o[k] + (o[k + 1] - o[k]) * (f - k);
                e[i] = sd > 0 ? (v - mean) / sd : 0;
            }
            return e;
        }

        static double FineOffset(AudioTrack a, AudioTrack b, double offset, out bool fine)
        {
            fine = false;
            if (Math.Abs(a.Rate - b.Rate) > 0.5) return offset;
            double rate = a.Rate;
            int center = (int)Math.Round((offset - a.Start + b.Start) * rate), span = (int)(rate * 0.015);
            int from = Math.Max(0, -center + span), len = (int)Math.Min(rate * 10, Math.Min(b.X.Length - from, a.X.Length - (center + span + from)));
            if (len < rate * 0.5) return offset;
            double best = double.NegativeInfinity;
            int bestLag = center;
            for (int lag = center - span; lag <= center + span; lag++)
            {
                double s = 0, na = 0, nb2 = 0;
                for (int i = from; i < from + len; i++)
                {
                    int j = i + lag;
                    if (j < 0 || j >= a.X.Length) continue;
                    double x = a.X[j], y = b.X[i];
                    s += x * y; na += x * x; nb2 += y * y;
                }
                double c = na > 0 && nb2 > 0 ? s / Math.Sqrt(na * nb2) : 0;
                if (c > best) { best = c; bestLag = lag; }
            }
            if (best < 0.3) return offset;
            fine = true;
            return bestLag / rate + a.Start - b.Start;
        }

        public static List<double[]> Drift(AudioTrack a, AudioTrack b, double offset)
        {
            List<double[]> r = new List<double[]>();
            const double fps = 100;
            double[] ea = Envelope(a, fps), eb = Envelope(b, fps);
            double from = Math.Max(b.Start, a.Start - offset), to = Math.Min(b.Start + eb.Length / fps, a.Start + ea.Length / fps - offset);
            double span = to - from;
            if (span < 30) return r;
            int win = (int)(fps * 8), search = (int)(fps * 0.25);
            int count = Math.Min(8, (int)(span / 8));
            double[] c = new double[2 * search + 1];
            for (int w = 0; w < count; w++)
            {
                double center = from + (w + 0.5) * span / count;
                int bi = (int)Math.Round((center - b.Start) * fps) - win / 2, ai = (int)Math.Round((center + offset - a.Start) * fps) - win / 2;
                int best = -1;
                for (int lag = -search; lag <= search; lag++)
                {
                    double s = 0, na = 0, nb = 0;
                    for (int k = 0; k < win; k++)
                    {
                        int ia = ai + lag + k, ib = bi + k;
                        if (ia < 0 || ia >= ea.Length || ib < 0 || ib >= eb.Length) continue;
                        s += ea[ia] * eb[ib]; na += ea[ia] * ea[ia]; nb += eb[ib] * eb[ib];
                    }
                    c[lag + search] = na > 0 && nb > 0 ? s / Math.Sqrt(na * nb) : 0;
                    if (best < 0 || c[lag + search] > c[best]) best = lag + search;
                }
                if (best < 0 || c[best] < 0.5) continue;
                double shift = 0;
                if (best > 0 && best < c.Length - 1)
                {
                    double den = c[best - 1] - 2 * c[best] + c[best + 1];
                    if (den < 0) shift = Math.Max(-0.5, Math.Min(0.5, 0.5 * (c[best - 1] - c[best + 1]) / den));
                }
                r.Add(new double[] { center, offset + (best - search + shift) / fps, c[best] });
            }
            return r;
        }
    }
}
