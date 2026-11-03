using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace MinGent
{
    static class AudioTime
    {
        public static double Parse(object v, double def)
        {
            double r = Read(v, def);
            return double.IsNaN(r) || double.IsInfinity(r) ? def : r;
        }

        static double Read(object v, double def)
        {
            if (v == null) return def;
            if (v is double) return (double)v;
            if (v is int) return (int)v;
            if (v is long) return (long)v;
            string s = (v as string ?? "").Trim().ToLowerInvariant();
            if (s.Length == 0) return def;
            double n;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out n)) return n;
            Match m = Regex.Match(s, @"^(?:(\d+):)?(\d+):(\d+(?:\.\d+)?)$");
            if (m.Success)
                return (m.Groups[1].Success ? Num(m.Groups[1].Value) * 3600 : 0) + Num(m.Groups[2].Value) * 60 + Num(m.Groups[3].Value);
            m = Regex.Match(s, @"^(\d+(?:\.\d+)?)\s*ms$");
            if (m.Success) return Num(m.Groups[1].Value) / 1000;
            m = Regex.Match(s, @"^(?:(\d+(?:\.\d+)?)\s*h(?:ours?|rs?)?)?\s*(?:(\d+(?:\.\d+)?)\s*m(?:in(?:ute)?s?)?)?\s*(?:(\d+(?:\.\d+)?)\s*s(?:ec(?:ond)?s?)?)?$");
            if (m.Success && (m.Groups[1].Success || m.Groups[2].Success || m.Groups[3].Success))
                return (m.Groups[1].Success ? Num(m.Groups[1].Value) * 3600 : 0) + (m.Groups[2].Success ? Num(m.Groups[2].Value) * 60 : 0) + (m.Groups[3].Success ? Num(m.Groups[3].Value) : 0);
            return def;
        }

        static double Num(string s) { return double.Parse(s, CultureInfo.InvariantCulture); }

        static double Sane(double s) { return double.IsNaN(s) || s < 0 ? 0 : Math.Min(s, 1e12); }

        static string Hours(long t)
        {
            return (t / 3600).ToString(CultureInfo.InvariantCulture) + ":" + (t / 60 % 60).ToString("00", CultureInfo.InvariantCulture) + ":" + (t % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        public static string Clock(double s)
        {
            s = Sane(s);
            double r = Math.Round(s, 1, MidpointRounding.AwayFromZero);
            if (r >= 3600) return Hours((long)Math.Round(s, MidpointRounding.AwayFromZero));
            int m = (int)(r / 60);
            return m.ToString(CultureInfo.InvariantCulture) + ":" + (r - m * 60).ToString("00.0", CultureInfo.InvariantCulture);
        }

        public static string Short(double s)
        {
            long t = (long)Math.Round(Sane(s), MidpointRounding.AwayFromZero);
            if (t >= 3600) return Hours(t);
            return (t / 60).ToString(CultureInfo.InvariantCulture) + ":" + (t % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        public static List<double> Ticks(double start, double seconds, out double step)
        {
            double[] steps = { 0.1, 0.2, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600, 7200, 14400, 43200, 86400 };
            step = steps[steps.Length - 1];
            foreach (double s in steps) if (seconds / s <= 12) { step = s; break; }
            List<double> l = new List<double>();
            long first = (long)Math.Ceiling(start / step - 1e-9), last = (long)Math.Floor((start + seconds) / step + 1e-9);
            for (long i = first; i <= last && l.Count < 64; i++) l.Add(i * step);
            return l;
        }
    }

    static class AudioMath
    {
        public static void Fft(double[] re, double[] im, int n)
        {
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j)
                {
                    double t = re[i]; re[i] = re[j]; re[j] = t;
                    t = im[i]; im[i] = im[j]; im[j] = t;
                }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = -2 * Math.PI / len, wr = Math.Cos(ang), wi = Math.Sin(ang);
                int half = len >> 1;
                for (int i = 0; i < n; i += len)
                {
                    double cr = 1, ci = 0;
                    for (int k = 0; k < half; k++)
                    {
                        int a = i + k, b = a + half;
                        double xr = re[b] * cr - im[b] * ci, xi = re[b] * ci + im[b] * cr;
                        re[b] = re[a] - xr; im[b] = im[a] - xi;
                        re[a] += xr; im[a] += xi;
                        double nr = cr * wr - ci * wi;
                        ci = cr * wi + ci * wr;
                        cr = nr;
                    }
                }
            }
        }

        public static double[] Hann(int n)
        {
            double[] w = new double[n];
            for (int i = 0; i < n; i++) w[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / n);
            return w;
        }

        public static double Db(double v) { return v <= 1e-10 ? -200 : 20 * Math.Log10(v); }

        public static string Note(double hz)
        {
            if (hz < 8 || hz > 13000) return "";
            string[] names = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
            int midi = (int)Math.Round(69 + 12 * Math.Log(hz / 440.0, 2));
            return names[(midi % 12 + 12) % 12] + (midi / 12 - 1).ToString(CultureInfo.InvariantCulture);
        }

        public static string Hz(double hz)
        {
            if (hz >= 999.5) return (hz / 1000).ToString(hz >= 9995 ? "0.0" : "0.00", CultureInfo.InvariantCulture) + " kHz";
            return (hz >= 99.95 ? Math.Round(hz).ToString(CultureInfo.InvariantCulture) : hz.ToString("0.0", CultureInfo.InvariantCulture)) + " Hz";
        }

        public static void Flush(double[] s, int from, int count)
        {
            for (int i = from; i < from + count; i++) if (s[i] > -1e-20 && s[i] < 1e-20) s[i] = 0;
        }

        public static double[] LowPass(double fc, double rate, double q)
        {
            double w = 2 * Math.PI * fc / rate, cw = Math.Cos(w), alpha = Math.Sin(w) / (2 * q), a0 = 1 + alpha;
            return new double[] { (1 - cw) / 2 / a0, (1 - cw) / a0, (1 - cw) / 2 / a0, -2 * cw / a0, (1 - alpha) / a0 };
        }

        public static double Weighted(List<float> values, List<float> weights, double p)
        {
            int n = values.Count;
            if (n == 0) return double.NaN;
            int[] idx = new int[n];
            for (int i = 0; i < n; i++) idx[i] = i;
            Array.Sort(idx, delegate (int a, int b) { return values[a].CompareTo(values[b]); });
            double total = 0;
            for (int i = 0; i < n; i++) total += weights[i];
            double want = p * total, run = 0;
            for (int i = 0; i < n; i++)
            {
                run += weights[idx[i]];
                if (run >= want) return values[idx[i]];
            }
            return values[idx[n - 1]];
        }
    }

    class AudioEnvelope
    {
        const int Cap = 8192;
        public readonly float[] Min = new float[Cap], Max = new float[Cap];
        public readonly double[] Sq = new double[Cap];
        public int Count;
        public long Block;
        long inBlock;
        float mn = float.MaxValue, mx = float.MinValue;
        double sq;

        public AudioEnvelope(long block) { Block = Math.Max(1, block); }

        public void Add(float v)
        {
            if (v < mn) mn = v;
            if (v > mx) mx = v;
            sq += (double)v * v;
            if (++inBlock >= Block) Close();
        }

        void Close()
        {
            if (Count == Cap)
            {
                int h = Cap / 2;
                for (int i = 0; i < h; i++)
                {
                    Min[i] = Math.Min(Min[2 * i], Min[2 * i + 1]);
                    Max[i] = Math.Max(Max[2 * i], Max[2 * i + 1]);
                    Sq[i] = Sq[2 * i] + Sq[2 * i + 1];
                }
                Count = h;
                Block *= 2;
                if (inBlock < Block) return;
            }
            Min[Count] = mn;
            Max[Count] = mx;
            Sq[Count] = sq;
            Count++;
            inBlock = 0;
            mn = float.MaxValue;
            mx = float.MinValue;
            sq = 0;
        }

        public void Finish()
        {
            if (inBlock == 0) return;
            if (Count == Cap)
            {
                long keep = inBlock;
                inBlock = 0;
                Close();
                inBlock = keep;
            }
            Min[Count] = mn;
            Max[Count] = mx;
            Sq[Count] = sq * Block / inBlock;
            Count++;
            inBlock = 0;
        }
    }

    class MixAnalyzer
    {
        public const int Width = 1114, Rows = 300;
        public readonly int Rate;
        public long Count;
        public readonly AudioEnvelope Env;
        public readonly List<float> Rms50 = new List<float>();
        public readonly int Rms50Len;
        double r50;
        int r50n;

        public readonly int N, M;
        readonly float[] ring;
        int pos;
        readonly double[] winN, winM, re, im;
        long avgHop, sinceAvg;
        public readonly double[] AvgPower;
        public double AvgWeight, AvgLen;
        public int AvgFrames;
        long colHop, sinceCol, frameHop, sinceFrame;
        public readonly List<float[]> Columns = new List<float[]>();
        readonly float[] colMax = new float[Rows];
        int colFrames;
        readonly double[] waiting, cre, cim;
        bool hasWaiting;
        public readonly double Bottom, Top;
        readonly int[] rowLo, rowHi;

        readonly int dec;
        public readonly double DecRate;
        readonly double[] lp1, lp2;
        readonly double[] lpz = new double[4];
        int decN;
        const int DMask = 2047;
        readonly float[] dring = new float[DMask + 1];
        int dpos;
        long dcount;
        long pitchHop, sincePitch;
        int pitchFrames;
        public readonly List<float> F0 = new List<float>(), F0W = new List<float>();
        public double SoundWeight;
        const int YinW = 512;
        const double YinThreshold = 0.15;
        readonly int maxLag, minLag;
        readonly double[] yinBuf, yinD;
        readonly double quietSq;
        long sinceFlux;
        readonly long fluxLimit;
        public readonly List<float> Flux = new List<float>();
        readonly double[] fre = new double[512], fim = new double[512], prevMag = new double[256], fwin = AudioMath.Hann(512);
        public List<float> Kept, KeptFull;
        long keepMax, fullMax;

        public void Keep(AudioKeep k, long expected)
        {
            if (k == null) return;
            if (k.Seconds > 0)
            {
                keepMax = (long)Math.Ceiling(k.Seconds * DecRate);
                Kept = new List<float>((int)Math.Min(keepMax, expected > 0 ? expected / dec + 16 : (long)(DecRate * 60)));
            }
            if (k.FullSeconds > 0)
            {
                fullMax = (long)Math.Ceiling(k.FullSeconds * Rate);
                KeptFull = new List<float>((int)Math.Min(fullMax, expected > 0 ? expected + 16 : (long)Rate * 10));
            }
        }

        public MixAnalyzer(int rate, long expected)
        {
            Rate = Math.Max(1, rate);
            if (expected <= 0) expected = (long)Rate * 30;
            Env = new AudioEnvelope(Math.Max(1, expected / 4096));
            Rms50Len = Math.Max(1, Rate / 20);
            N = Rate >= 128000 ? 32768 : Rate >= 64000 ? 16384 : Rate >= 32000 ? 8192 : Rate >= 16000 ? 4096 : 2048;
            M = N / 2;
            ring = new float[N];
            winN = AudioMath.Hann(N);
            winM = AudioMath.Hann(M);
            re = new double[N];
            im = new double[N];
            waiting = new double[M];
            cre = new double[M];
            cim = new double[M];
            AvgPower = new double[N / 2 + 1];
            AvgLen = N;
            avgHop = Math.Max(N / 2, expected / 600);
            colHop = Math.Max(Math.Max(64, M / 8), expected / Width);
            PlanFrames();
            Top = Math.Min(Rate / 2.0, 20000);
            Bottom = Math.Min(30, Top / 8);
            rowLo = new int[Rows];
            rowHi = new int[Rows];
            for (int r = 0; r < Rows; r++)
            {
                double f0 = Bottom * Math.Pow(Top / Bottom, r / (double)Rows), f1 = Bottom * Math.Pow(Top / Bottom, (r + 1) / (double)Rows);
                int k0 = Math.Max(1, Math.Min(M / 2, (int)Math.Floor(f0 * M / Rate)));
                int k1 = Math.Max(k0, Math.Min(M / 2, (int)Math.Ceiling(f1 * M / Rate) - 1));
                rowLo[r] = k0;
                rowHi[r] = k1;
            }
            dec = Math.Max(1, (int)Math.Round(Rate / 11025.0));
            DecRate = Rate / (double)dec;
            lp1 = AudioMath.LowPass(0.42 * DecRate, Rate, 0.5412);
            lp2 = AudioMath.LowPass(0.42 * DecRate, Rate, 1.3066);
            maxLag = Math.Min(YinW - 2, (int)(DecRate / 50));
            minLag = Math.Max(2, (int)(DecRate / 1500));
            yinBuf = new double[YinW + maxLag + 2];
            yinD = new double[maxLag + 2];
            long decExpected = expected / dec;
            pitchHop = Math.Max((long)(DecRate / 50), decExpected / 3000);
            fluxLimit = (long)(DecRate * 300);
            quietSq = Math.Pow(10, -50 / 10.0) * YinW;
        }

        public void Add(float v)
        {
            if (KeptFull != null && KeptFull.Count < fullMax) KeptFull.Add(v);
            Env.Add(v);
            r50 += (double)v * v;
            if (++r50n == Rms50Len) { Rms50.Add((float)Math.Sqrt(r50 / r50n)); r50 = 0; r50n = 0; }
            ring[pos] = v;
            pos = (pos + 1) & (N - 1);
            Count++;
            if (++sinceAvg >= avgHop && Count >= N) { sinceAvg = 0; AvgFrame(); }
            if (++sinceFrame >= frameHop) { sinceFrame = 0; ColumnFrame(); }
            if (++sinceCol >= colHop) { sinceCol = 0; sinceFrame = 0; CloseColumn(); }
            double x = v;
            if (dec > 1)
            {
                double y = lp1[0] * x + lpz[0];
                lpz[0] = lp1[1] * x - lp1[3] * y + lpz[1];
                lpz[1] = lp1[2] * x - lp1[4] * y;
                double z = lp2[0] * y + lpz[2];
                lpz[2] = lp2[1] * y - lp2[3] * z + lpz[3];
                lpz[3] = lp2[2] * y - lp2[4] * z;
                if (x == 0) AudioMath.Flush(lpz, 0, 4);
                if (++decN < dec) return;
                decN = 0;
                x = z;
            }
            dring[dpos] = (float)x;
            dpos = (dpos + 1) & DMask;
            dcount++;
            if (Kept != null && Kept.Count < keepMax) Kept.Add((float)x);
            if (++sincePitch >= pitchHop && dcount > YinW + maxLag + 1) { sincePitch = 0; Pitch(); }
            if (dcount <= fluxLimit && ++sinceFlux >= 128 && dcount >= 512) { sinceFlux = 0; FluxFrame(); }
        }

        public void Finish()
        {
            Env.Finish();
            if (r50n > 0 && r50n >= Rms50Len / 2) Rms50.Add((float)Math.Sqrt(r50 / r50n));
            r50n = 0;
            if (AvgFrames == 0 && Count > 0)
            {
                int n = (int)Math.Min(Count, N);
                for (int i = 0; i < N; i++) { re[i] = 0; im[i] = 0; }
                double[] w = AudioMath.Hann(n);
                for (int i = 0; i < n; i++) re[i] = ring[i] * w[i];
                AudioMath.Fft(re, im, N);
                for (int k = 0; k <= N / 2; k++) AvgPower[k] += re[k] * re[k] + im[k] * im[k];
                AvgWeight = 1;
                AvgLen = n;
                AvgFrames = 1;
            }
            if (Count > 0)
            {
                if (colFrames == 0 && !hasWaiting && (sinceCol > colHop / 2 || Columns.Count == 0)) ColumnFrame();
                if (colFrames > 0 || hasWaiting) CloseColumn();
            }
            sinceCol = 0;
            sinceFrame = 0;
        }

        void PlanFrames()
        {
            long per = Math.Min(16, Math.Max(1, (colHop + M - 1) / M));
            if (per > 1 && (per & 1) == 1) per++;
            frameHop = Math.Max(1, colHop / per);
        }

        void AvgFrame()
        {
            for (int i = 0; i < N; i++) { re[i] = ring[(pos + i) & (N - 1)] * winN[i]; im[i] = 0; }
            AudioMath.Fft(re, im, N);
            double w = avgHop;
            for (int k = 0; k <= N / 2; k++) AvgPower[k] += w * (re[k] * re[k] + im[k] * im[k]);
            AvgWeight += w;
            if (++AvgFrames % 1200 == 0) avgHop *= 2;
        }

        void ColumnFrame()
        {
            int start = (pos - M + N) & (N - 1);
            if (!hasWaiting)
            {
                for (int i = 0; i < M; i++) waiting[i] = ring[(start + i) & (N - 1)] * winM[i];
                hasWaiting = true;
                return;
            }
            for (int i = 0; i < M; i++) { cre[i] = waiting[i]; cim[i] = ring[(start + i) & (N - 1)] * winM[i]; }
            hasWaiting = false;
            TwoFrames();
        }

        void TwoFrames()
        {
            AudioMath.Fft(cre, cim, M);
            for (int r = 0; r < Rows; r++)
            {
                double best = 0;
                for (int k = rowLo[r]; k <= rowHi[r]; k++)
                {
                    double xr = cre[k], xi = cim[k], yr = cre[M - k], yi = cim[M - k];
                    double a = (xr + yr) * (xr + yr) + (xi - yi) * (xi - yi), b = (xr - yr) * (xr - yr) + (xi + yi) * (xi + yi);
                    if (a > best) best = a;
                    if (b > best) best = b;
                }
                float v = (float)Math.Min(best / 4, float.MaxValue);
                if (v > colMax[r]) colMax[r] = v;
            }
            colFrames++;
        }

        void CloseColumn()
        {
            if (hasWaiting)
            {
                for (int i = 0; i < M; i++) { cre[i] = waiting[i]; cim[i] = 0; }
                hasWaiting = false;
                TwoFrames();
            }
            if (colFrames == 0) return;
            float[] c = new float[Rows];
            Array.Copy(colMax, c, Rows);
            Array.Clear(colMax, 0, Rows);
            colFrames = 0;
            Columns.Add(c);
            if (Columns.Count < 2 * Width) return;
            List<float[]> merged = new List<float[]>(Width + 1);
            for (int i = 0; i + 1 < Columns.Count; i += 2)
            {
                float[] a = Columns[i], b = Columns[i + 1];
                for (int r = 0; r < Rows; r++) if (b[r] > a[r]) a[r] = b[r];
                merged.Add(a);
            }
            Columns.Clear();
            Columns.AddRange(merged);
            colHop *= 2;
            PlanFrames();
        }

        void Pitch()
        {
            int len = YinW + maxLag + 1;
            int start = (dpos - len + DMask + 1) & DMask;
            double energy = 0;
            for (int i = 0; i < len; i++) yinBuf[i] = dring[(start + i) & DMask];
            for (int i = 0; i < YinW; i++) energy += yinBuf[i] * yinBuf[i];
            double w = pitchHop;
            if (++pitchFrames % 6000 == 0) pitchHop *= 2;
            if (energy < quietSq) return;
            SoundWeight += w;
            double run = 0;
            yinD[0] = 1;
            for (int tau = 1; tau <= maxLag; tau++)
            {
                double s = 0;
                for (int i = 0; i < YinW; i++) { double e = yinBuf[i] - yinBuf[i + tau]; s += e * e; }
                run += s;
                yinD[tau] = run > 0 ? s * tau / run : 1;
            }
            int best = -1;
            for (int tau = minLag; tau < maxLag; tau++)
            {
                if (yinD[tau] >= YinThreshold) continue;
                while (tau + 1 < maxLag && yinD[tau + 1] < yinD[tau]) tau++;
                best = tau;
                break;
            }
            if (best < 1) return;
            double a = yinD[best - 1], b = yinD[best], c = yinD[best + 1], den = a - 2 * b + c;
            double period = best + (den != 0 ? Math.Max(-1, Math.Min(1, 0.5 * (a - c) / den)) : 0);
            if (period <= 0) return;
            double f = DecRate / period;
            if (f < 45 || f > 1700) return;
            F0.Add((float)f);
            F0W.Add((float)w);
        }

        void FluxFrame()
        {
            int start = (dpos - 512 + DMask + 1) & DMask;
            for (int i = 0; i < 512; i++) { fre[i] = dring[(start + i) & DMask] * fwin[i]; fim[i] = 0; }
            AudioMath.Fft(fre, fim, 512);
            double s = 0;
            for (int k = 1; k < 256; k++)
            {
                double m = Math.Log(1 + 100 * Math.Sqrt(fre[k] * fre[k] + fim[k] * fim[k]));
                if (m > prevMag[k]) s += m - prevMag[k];
                prevMag[k] = m;
            }
            Flux.Add((float)s);
        }

        public double FluxRate { get { return DecRate / 128.0; } }
    }

    class AudioCollector
    {
        public readonly int Channels, Rate;
        public readonly long Limit;
        public readonly bool Side;
        public long Frames;
        public readonly double[] Peak, SumSq, Sum;
        public readonly long[] Clipped;
        public double SumLR;
        public long ClipEvents, FirstClip = -1, Overs, OverEvents, FirstOver = -1, Invalid;
        public readonly List<long> ClipAt = new List<long>();
        readonly double top, nearTop, gap;
        readonly int[] runLength;
        readonly long[] runFrom;
        readonly double[] runValue, runGap, lastAbs;
        readonly bool[] over;
        readonly double[] gain, pre, rlb, kz, blockSq;
        int blockN;
        readonly int blockLen;
        public readonly List<double> Blocks = new List<double>();
        public readonly MixAnalyzer Mix;
        public uint IntBits;
        bool finished;

        public AudioCollector(int channels, int rate, double maxSeconds, double expectedSeconds, double fullScale, double step, bool side)
            : this(channels, rate, maxSeconds, expectedSeconds, fullScale, step, side, null) { }

        public AudioCollector(int channels, int rate, double maxSeconds, double expectedSeconds, double fullScale, double step, bool side, AudioKeep keep)
        {
            Channels = Math.Max(1, channels);
            Rate = Math.Max(1, rate);
            Side = side && Channels == 2;
            Limit = maxSeconds > 0 ? (long)Math.Ceiling(maxSeconds * Rate) : long.MaxValue;
            double exp = expectedSeconds;
            if (maxSeconds > 0 && exp > maxSeconds) exp = maxSeconds;
            Mix = new MixAnalyzer(Rate, exp > 0 && exp < 1e7 ? (long)(exp * Rate) : -1);
            Mix.Keep(keep, exp > 0 && exp < 1e7 ? (long)(exp * Rate) : -1);
            Peak = new double[Channels];
            SumSq = new double[Channels];
            Sum = new double[Channels];
            Clipped = new long[Channels];
            top = fullScale > 0 && fullScale <= 1 ? fullScale : 1;
            nearTop = top - Math.Max(0, step) / 2;
            gap = Math.Max(2.5 * Math.Max(0, step), 1e-4);
            runLength = new int[Channels];
            runFrom = new long[Channels];
            runValue = new double[Channels];
            runGap = new double[Channels];
            lastAbs = new double[Channels];
            over = new bool[Channels];
            gain = new double[Channels];
            for (int c = 0; c < Channels; c++) gain[c] = Gain(c);
            double[] same = { 1, 0, 0, 0, 0 };
            double f0 = 1681.974450955533, g = 3.999843853973347, q = 0.7071752369554196;
            if (f0 < 0.45 * Rate)
            {
                double k = Math.Tan(Math.PI * f0 / Rate), vh = Math.Pow(10, g / 20), vb = Math.Pow(vh, 0.4996667741545416), a0 = 1 + k / q + k * k;
                pre = new double[] { (vh + vb * k / q + k * k) / a0, 2 * (k * k - vh) / a0, (vh - vb * k / q + k * k) / a0, 2 * (k * k - 1) / a0, (1 - k / q + k * k) / a0 };
            }
            else pre = same;
            f0 = 38.13547087602444;
            q = 0.5003270373238773;
            if (f0 < 0.45 * Rate)
            {
                double k = Math.Tan(Math.PI * f0 / Rate), a0 = 1 + k / q + k * k, k48 = Math.Tan(Math.PI * f0 / 48000), s = (1 + k48 / q + k48 * k48) / a0;
                rlb = new double[] { s, -2 * s, s, 2 * (k * k - 1) / a0, (1 - k / q + k * k) / a0 };
            }
            else rlb = same;
            kz = new double[Channels * 4];
            blockSq = new double[Channels];
            blockLen = Math.Max(1, (int)Math.Round(Rate / 10.0));
        }

        public bool Full { get { return Frames >= Limit; } }

        public double Seconds { get { return Frames / (double)Rate; } }

        public bool Cancels
        {
            get
            {
                if (Channels != 2 || Side || Frames == 0) return false;
                double sum = SumSq[0] + SumSq[1] + 2 * SumLR, diff = SumSq[0] + SumSq[1] - 2 * SumLR;
                return diff > 4e-8 * Frames && sum < 0.01 * diff;
            }
        }

        public void Add(float[] data, int frames)
        {
            int ch = Channels;
            for (int i = 0; i < frames && Frames < Limit; i++)
            {
                double mix = 0, left = 0, right = 0;
                int o = i * ch;
                for (int c = 0; c < ch; c++)
                {
                    double x = data[o + c];
                    if (double.IsNaN(x) || x > 1e6 || x < -1e6) { Invalid++; x = 0; }
                    double ax = x < 0 ? -x : x;
                    if (ax > Peak[c]) Peak[c] = ax;
                    SumSq[c] += x * x;
                    Sum[c] += x;
                    if (ax < nearTop && runLength[c] == 0) { over[c] = false; lastAbs[c] = ax; }
                    else Flat(c, x, ax);
                    if (c == 0) left = x;
                    else if (c == 1) right = x;
                    mix += x;
                    int z = c * 4;
                    double y1 = pre[0] * x + kz[z];
                    kz[z] = pre[1] * x - pre[3] * y1 + kz[z + 1];
                    kz[z + 1] = pre[2] * x - pre[4] * y1;
                    double y2 = rlb[0] * y1 + kz[z + 2];
                    kz[z + 2] = rlb[1] * y1 - rlb[3] * y2 + kz[z + 3];
                    kz[z + 3] = rlb[2] * y1 - rlb[4] * y2;
                    if (x == 0) AudioMath.Flush(kz, z, 4);
                    blockSq[c] += y2 * y2;
                }
                if (ch == 2) SumLR += left * right;
                if (++blockN == blockLen)
                {
                    double s = 0;
                    for (int c = 0; c < ch; c++) { s += gain[c] * blockSq[c]; blockSq[c] = 0; }
                    Blocks.Add(s / blockLen);
                    blockN = 0;
                }
                Mix.Add((float)(Side ? (left - right) / 2 : mix / ch));
                Frames++;
            }
        }

        void Flat(int c, double x, double ax)
        {
            if (ax > 1.000001)
            {
                Overs++;
                if (!over[c])
                {
                    over[c] = true;
                    OverEvents++;
                    if (FirstOver < 0) FirstOver = Frames;
                    if (ClipAt.Count < 2000) ClipAt.Add(Frames);
                }
            }
            else over[c] = false;
            bool atTop = ax >= nearTop && ax <= 1.000001;
            if (runLength[c] > 0 && atTop && x == runValue[c]) runLength[c]++;
            else
            {
                if (runLength[c] > 0) EndRun(c, top - ax);
                if (atTop)
                {
                    runLength[c] = 1;
                    runValue[c] = x;
                    runFrom[c] = Frames;
                    runGap[c] = top - lastAbs[c];
                }
            }
            lastAbs[c] = ax;
        }

        void EndRun(int c, double after)
        {
            int n = runLength[c];
            runLength[c] = 0;
            if (n < 3 || Math.Max(runGap[c], after) < gap) return;
            ClipEvents++;
            Clipped[c] += n;
            if (FirstClip < 0 || runFrom[c] < FirstClip) FirstClip = runFrom[c];
            if (ClipAt.Count < 2000) ClipAt.Add(runFrom[c]);
        }

        public void Finish()
        {
            if (finished) return;
            finished = true;
            for (int c = 0; c < Channels; c++) if (runLength[c] > 0) EndRun(c, 0);
            Mix.Finish();
        }

        public double Gain(int c)
        {
            if (Channels == 6) return c == 3 ? 0 : c >= 4 ? 1.41 : 1;
            if (Channels == 5) return c >= 3 ? 1.41 : 1;
            return 1;
        }
    }

    class AudioKeep
    {
        public double Seconds, FullSeconds;
    }

    class AudioInfo
    {
        public string Codec = "", Error, Note;
        public int Rate, Channels, Bits, Kbps;
        public double FileSeconds, Start;
        public AudioCollector Data;
    }

    class PcmLayout
    {
        public int Channels, Rate, Bits, Bytes, Align, Law, Valid;
        public bool Float, BigEndian, Unsigned;
        public long DataAt, Frames;

        public double FullScale
        {
            get
            {
                if (Law == 1) return 32124 / 32768.0;
                if (Law == 2) return 32256 / 32768.0;
                return Float ? 1 : 1 - Step;
            }
        }

        public double Step
        {
            get
            {
                if (Law != 0) return 1024 / 32768.0;
                if (Float) return 0;
                int b = Valid > 0 && Valid < Bits ? Valid : Bits;
                return Math.Pow(2, -(Math.Max(1, Math.Min(32, b)) - 1));
            }
        }
    }

    static class AudioFile
    {
        static readonly string[] SoundExtensions = { ".wav", ".wave", ".bwf", ".mp3", ".m4a", ".m4b", ".aac", ".wma", ".flac", ".ogg", ".oga", ".opus", ".aif", ".aiff", ".aifc", ".amr", ".mka", ".caf", ".ac3", ".mp2", ".adts", ".alac" };

        public static bool IsAudio(string path)
        {
            string e = (Path.GetExtension(path ?? "") ?? "").ToLowerInvariant();
            return Array.IndexOf(SoundExtensions, e) >= 0;
        }

        public static AudioInfo Load(string path, double start, double length, CancelToken ct, bool side) { return Load(path, start, length, ct, side, null); }

        public static AudioInfo Load(string path, double start, double length, CancelToken ct, bool side, AudioKeep keep)
        {
            string why = null;
            PcmLayout f = null;
            try { f = Probe(path, out why); }
            catch (Exception ex) { why = ex.Message; }
            if (f != null) return Pcm(path, f, start, length, ct, side, keep);
            if (Path.DirectorySeparatorChar != '\\')
            {
                AudioInfo none = new AudioInfo();
                none.Error = why != null && why != "unsupported" ? why : "only WAV and AIFF files can be read on this system";
                return none;
            }
            AudioInfo info = MediaFoundationReader.Read(path, start, length, ct, side, keep);
            if (info.Error != null && info.Error != "stopped" && why != null && why != "unsupported") info.Error = why;
            return info;
        }

        static PcmLayout Probe(string path, out string why)
        {
            why = null;
            byte[] head = new byte[12];
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (fs.Read(head, 0, 12) < 12) return null;
                string a = Encoding.ASCII.GetString(head, 0, 4), b = Encoding.ASCII.GetString(head, 8, 4);
                if ((a == "RIFF" || a == "RF64" || a == "BW64") && b == "WAVE") return Wave(fs, out why);
                if (a == "FORM" && (b == "AIFF" || b == "AIFC")) return Aiff(fs, b == "AIFC", out why);
            }
            return null;
        }

        static PcmLayout Wave(FileStream fs, out string why)
        {
            why = null;
            BinaryReader br = new BinaryReader(fs);
            fs.Position = 12;
            int format = 0;
            PcmLayout f = new PcmLayout();
            long ds64 = -1, dataAt = -1, dataLen = 0;
            while (fs.Position + 8 <= fs.Length)
            {
                string id = Encoding.ASCII.GetString(br.ReadBytes(4));
                long size = br.ReadUInt32();
                long body = fs.Position;
                if (id == "ds64" && size >= 16)
                {
                    br.ReadUInt64();
                    ds64 = (long)br.ReadUInt64();
                }
                else if (id == "fmt " && size >= 16)
                {
                    format = br.ReadUInt16();
                    f.Channels = br.ReadUInt16();
                    f.Rate = (int)br.ReadUInt32();
                    br.ReadUInt32();
                    f.Align = br.ReadUInt16();
                    f.Bits = br.ReadUInt16();
                    if (format == 0xFFFE && size >= 40)
                    {
                        br.ReadUInt16();
                        f.Valid = br.ReadUInt16();
                        br.ReadUInt32();
                        format = br.ReadUInt16();
                    }
                }
                else if (id == "data")
                {
                    dataAt = body;
                    if (size == 0xFFFFFFFF && ds64 >= 0) size = ds64;
                    dataLen = size == 0 || size == 0xFFFFFFFF || dataAt + size > fs.Length ? fs.Length - dataAt : size;
                    break;
                }
                long next = body + size + (size & 1);
                if (next < body || next > fs.Length) break;
                fs.Position = next;
            }
            if (format == 0 || f.Channels <= 0 || f.Rate <= 0) { why = "the WAV file has no usable format header"; return null; }
            if (dataAt < 0) { why = "the WAV file has no sound data"; return null; }
            if (format == 1 && (f.Bits == 8 || f.Bits == 16 || f.Bits == 24 || f.Bits == 32)) f.Unsigned = f.Bits == 8;
            else if (format == 3 && (f.Bits == 32 || f.Bits == 64)) f.Float = true;
            else if ((format == 6 || format == 7) && f.Bits == 8) f.Law = format == 7 ? 1 : 2;
            else { why = "unsupported"; return null; }
            f.Bytes = f.Bits / 8;
            if (f.Align < f.Bytes * f.Channels) f.Align = f.Bytes * f.Channels;
            f.DataAt = dataAt;
            f.Frames = dataLen / f.Align;
            return f;
        }

        static uint U32(byte[] b, int o) { return (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]); }

        static PcmLayout Aiff(FileStream fs, bool aifc, out string why)
        {
            why = null;
            PcmLayout f = new PcmLayout();
            f.BigEndian = true;
            byte[] hdr = new byte[8];
            fs.Position = 12;
            bool comm = false;
            long dataAt = -1, dataLen = 0, frames = 0;
            while (fs.Position + 8 <= fs.Length)
            {
                if (fs.Read(hdr, 0, 8) < 8) break;
                string id = Encoding.ASCII.GetString(hdr, 0, 4);
                long size = U32(hdr, 4);
                long body = fs.Position;
                if (id == "COMM" && size >= 18)
                {
                    byte[] c = new byte[Math.Min(size, 64)];
                    fs.Read(c, 0, c.Length);
                    f.Channels = c[0] << 8 | c[1];
                    frames = U32(c, 2);
                    f.Bits = c[6] << 8 | c[7];
                    f.Rate = (int)Math.Round(Extended(c, 8));
                    string comp = aifc && c.Length >= 22 ? Encoding.ASCII.GetString(c, 18, 4) : "NONE";
                    if (comp == "sowt") f.BigEndian = false;
                    else if (comp == "fl32" || comp == "FL32") { f.Float = true; f.Bits = 32; }
                    else if (comp == "fl64" || comp == "FL64") { f.Float = true; f.Bits = 64; }
                    else if (comp == "ulaw" || comp == "ULAW") { f.Law = 1; f.Bits = 8; }
                    else if (comp == "alaw" || comp == "ALAW") { f.Law = 2; f.Bits = 8; }
                    else if (comp == "in24") f.Bits = 24;
                    else if (comp == "in32") f.Bits = 32;
                    else if (comp == "raw ") f.Unsigned = true;
                    else if (comp != "NONE" && comp != "twos") { why = "this AIFF file uses compression (" + comp.Trim() + ") that MinGent can't read"; return null; }
                    comm = true;
                }
                else if (id == "SSND" && size >= 8)
                {
                    byte[] s = new byte[8];
                    fs.Read(s, 0, 8);
                    long offset = U32(s, 0);
                    dataAt = body + 8 + offset;
                    dataLen = Math.Min(size - 8 - offset, fs.Length - dataAt);
                }
                long next = body + size + (size & 1);
                if (next < body || next > fs.Length) break;
                fs.Position = next;
            }
            if (!comm || f.Channels <= 0 || f.Rate <= 0 || f.Bits <= 0 || f.Bits > 64) { why = "the AIFF file has no usable format header"; return null; }
            if (dataAt < 0 || dataLen < 0) { why = "the AIFF file has no sound data"; return null; }
            f.Bytes = (f.Bits + 7) / 8;
            if (!f.Float && f.Law == 0 && f.Bytes > 4) { why = "unsupported"; return null; }
            if (f.Law != 0) f.Bytes = 1;
            f.Align = f.Bytes * f.Channels;
            f.DataAt = dataAt;
            f.Frames = Math.Min(frames > 0 ? frames : long.MaxValue, dataLen / f.Align);
            return f;
        }

        static double Extended(byte[] b, int o)
        {
            int exp = (b[o] & 0x7F) << 8 | b[o + 1];
            ulong mant = 0;
            for (int i = 0; i < 8; i++) mant = mant << 8 | b[o + 2 + i];
            if (exp == 0 && mant == 0) return 0;
            double v = mant * Math.Pow(2, exp - 16383 - 63);
            return (b[o] & 0x80) != 0 ? -v : v;
        }

        public static float MuLaw(int u)
        {
            u = ~u & 0xFF;
            int t = ((u & 0x0F) << 3) + 0x84;
            t <<= (u & 0x70) >> 4;
            return ((u & 0x80) != 0 ? 0x84 - t : t - 0x84) / 32768f;
        }

        public static float ALaw(int a)
        {
            a ^= 0x55;
            int t = (a & 0x0F) << 4, seg = (a & 0x70) >> 4;
            if (seg == 0) t += 8;
            else { t += 0x108; if (seg > 1) t <<= seg - 1; }
            return ((a & 0x80) != 0 ? t : -t) / 32768f;
        }

        static AudioInfo Pcm(string path, PcmLayout f, double start, double length, CancelToken ct, bool side, AudioKeep keep)
        {
            AudioInfo info = new AudioInfo();
            info.Rate = f.Rate;
            info.Channels = f.Channels;
            info.Bits = f.Law != 0 ? 0 : f.Valid > 0 && f.Valid < f.Bits && !f.Float ? f.Valid : f.Bits;
            bool aiff = f.BigEndian || Path.GetExtension(path).StartsWith(".aif", StringComparison.OrdinalIgnoreCase);
            info.Codec = (aiff ? "AIFF" : "WAV") + ", " + (f.Law == 1 ? "µ-law" : f.Law == 2 ? "A-law" : info.Bits + "-bit " + (f.Float ? "float" : "PCM"));
            info.FileSeconds = f.Frames / (double)f.Rate;
            double at = Math.Max(0, start) * f.Rate;
            long first = at >= f.Frames ? f.Frames : (long)Math.Round(at);
            info.Start = first / (double)f.Rate;
            long left = f.Frames - first;
            info.Data = new AudioCollector(f.Channels, f.Rate, length, left / (double)f.Rate, f.FullScale, f.Step, side, keep);
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    fs.Position = f.DataAt + first * f.Align;
                    int chunk = Math.Max(1, (1 << 20) / f.Align);
                    byte[] raw = new byte[chunk * f.Align];
                    float[] buf = new float[chunk * f.Channels];
                    uint bits = 0;
                    while (left > 0 && !info.Data.Full)
                    {
                        if (ct != null && ct.IsCancelled) { info.Error = "stopped"; return info; }
                        int want = (int)Math.Min(chunk, left);
                        int got = 0;
                        while (got < want * f.Align)
                        {
                            int r = fs.Read(raw, got, want * f.Align - got);
                            if (r <= 0) break;
                            got += r;
                        }
                        int frames = got / f.Align;
                        if (frames <= 0) break;
                        Convert(raw, frames, f, buf, ref bits);
                        info.Data.Add(buf, frames);
                        left -= frames;
                    }
                    info.Data.IntBits = bits;
                }
            }
            catch (Exception ex)
            {
                if (info.Data.Frames == 0) info.Error = ex.Message;
                else info.Note = "The file could be read only up to " + AudioTime.Clock(info.Start + info.Data.Seconds) + " (" + ex.Message + ").";
            }
            info.Data.Finish();
            return info;
        }

        static void Convert(byte[] raw, int frames, PcmLayout f, float[] buf, ref uint bits)
        {
            int ch = f.Channels, n = 0, bytes = f.Bytes, shift = 32 - bytes * 8;
            byte[] t = f.Float && f.BigEndian ? new byte[bytes] : null;
            for (int i = 0; i < frames; i++)
            {
                int o = i * f.Align;
                for (int c = 0; c < ch; c++, o += bytes)
                {
                    float v;
                    if (f.Law == 1) v = MuLaw(raw[o]);
                    else if (f.Law == 2) v = ALaw(raw[o]);
                    else if (f.Float)
                    {
                        if (t != null)
                        {
                            for (int k = 0; k < bytes; k++) t[k] = raw[o + bytes - 1 - k];
                            v = bytes == 4 ? BitConverter.ToSingle(t, 0) : (float)BitConverter.ToDouble(t, 0);
                        }
                        else v = bytes == 4 ? BitConverter.ToSingle(raw, o) : (float)BitConverter.ToDouble(raw, o);
                    }
                    else if (bytes == 1) v = (f.Unsigned ? raw[o] - 128 : (sbyte)raw[o]) / 128f;
                    else
                    {
                        int s = 0;
                        if (f.BigEndian) for (int k = 0; k < bytes; k++) s = s << 8 | raw[o + k];
                        else for (int k = bytes - 1; k >= 0; k--) s = s << 8 | raw[o + k];
                        s <<= shift;
                        if (f.Unsigned) s ^= int.MinValue;
                        bits |= (uint)s;
                        v = (float)(s / 2147483648.0);
                    }
                    buf[n++] = v;
                }
            }
        }
    }

    static class MediaFoundationReader
    {
        [ComImport, Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMFMediaType
        {
            [PreserveSig] int GetItem([In] ref Guid key, IntPtr value);
            [PreserveSig] int GetItemType([In] ref Guid key, out int type);
            [PreserveSig] int CompareItem([In] ref Guid key, IntPtr value, out int result);
            [PreserveSig] int Compare(IntPtr theirs, int match, out int result);
            [PreserveSig] int GetUINT32([In] ref Guid key, out int value);
            [PreserveSig] int GetUINT64([In] ref Guid key, out long value);
            [PreserveSig] int GetDouble([In] ref Guid key, out double value);
            [PreserveSig] int GetGUID([In] ref Guid key, out Guid value);
            [PreserveSig] int GetStringLength([In] ref Guid key, out int length);
            [PreserveSig] int GetString([In] ref Guid key, IntPtr value, int size, IntPtr length);
            [PreserveSig] int GetAllocatedString([In] ref Guid key, out IntPtr value, out int length);
            [PreserveSig] int GetBlobSize([In] ref Guid key, out int size);
            [PreserveSig] int GetBlob([In] ref Guid key, IntPtr buf, int size, IntPtr got);
            [PreserveSig] int GetAllocatedBlob([In] ref Guid key, out IntPtr buf, out int size);
            [PreserveSig] int GetUnknown([In] ref Guid key, [In] ref Guid iid, out IntPtr obj);
            [PreserveSig] int SetItem([In] ref Guid key, IntPtr value);
            [PreserveSig] int DeleteItem([In] ref Guid key);
            [PreserveSig] int DeleteAllItems();
            [PreserveSig] int SetUINT32([In] ref Guid key, int value);
            [PreserveSig] int SetUINT64([In] ref Guid key, long value);
            [PreserveSig] int SetDouble([In] ref Guid key, double value);
            [PreserveSig] int SetGUID([In] ref Guid key, [In] ref Guid value);
            [PreserveSig] int SetString([In] ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
            [PreserveSig] int SetBlob([In] ref Guid key, IntPtr buf, int size);
            [PreserveSig] int SetUnknown([In] ref Guid key, IntPtr obj);
            [PreserveSig] int LockStore();
            [PreserveSig] int UnlockStore();
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int GetItemByIndex(int index, out Guid key, IntPtr value);
            [PreserveSig] int CopyAllItems(IntPtr dest);
            [PreserveSig] int GetMajorType(out Guid type);
            [PreserveSig] int IsCompressedFormat(out int compressed);
            [PreserveSig] int IsEqual(IntPtr other, out int flags);
            [PreserveSig] int GetRepresentation(Guid rep, out IntPtr data);
            [PreserveSig] int FreeRepresentation(Guid rep, IntPtr data);
        }

        [ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMFSample
        {
            [PreserveSig] int GetItem([In] ref Guid key, IntPtr value);
            [PreserveSig] int GetItemType([In] ref Guid key, out int type);
            [PreserveSig] int CompareItem([In] ref Guid key, IntPtr value, out int result);
            [PreserveSig] int Compare(IntPtr theirs, int match, out int result);
            [PreserveSig] int GetUINT32([In] ref Guid key, out int value);
            [PreserveSig] int GetUINT64([In] ref Guid key, out long value);
            [PreserveSig] int GetDouble([In] ref Guid key, out double value);
            [PreserveSig] int GetGUID([In] ref Guid key, out Guid value);
            [PreserveSig] int GetStringLength([In] ref Guid key, out int length);
            [PreserveSig] int GetString([In] ref Guid key, IntPtr value, int size, IntPtr length);
            [PreserveSig] int GetAllocatedString([In] ref Guid key, out IntPtr value, out int length);
            [PreserveSig] int GetBlobSize([In] ref Guid key, out int size);
            [PreserveSig] int GetBlob([In] ref Guid key, IntPtr buf, int size, IntPtr got);
            [PreserveSig] int GetAllocatedBlob([In] ref Guid key, out IntPtr buf, out int size);
            [PreserveSig] int GetUnknown([In] ref Guid key, [In] ref Guid iid, out IntPtr obj);
            [PreserveSig] int SetItem([In] ref Guid key, IntPtr value);
            [PreserveSig] int DeleteItem([In] ref Guid key);
            [PreserveSig] int DeleteAllItems();
            [PreserveSig] int SetUINT32([In] ref Guid key, int value);
            [PreserveSig] int SetUINT64([In] ref Guid key, long value);
            [PreserveSig] int SetDouble([In] ref Guid key, double value);
            [PreserveSig] int SetGUID([In] ref Guid key, [In] ref Guid value);
            [PreserveSig] int SetString([In] ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
            [PreserveSig] int SetBlob([In] ref Guid key, IntPtr buf, int size);
            [PreserveSig] int SetUnknown([In] ref Guid key, IntPtr obj);
            [PreserveSig] int LockStore();
            [PreserveSig] int UnlockStore();
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int GetItemByIndex(int index, out Guid key, IntPtr value);
            [PreserveSig] int CopyAllItems(IntPtr dest);
            [PreserveSig] int GetSampleFlags(out int flags);
            [PreserveSig] int SetSampleFlags(int flags);
            [PreserveSig] int GetSampleTime(out long time);
            [PreserveSig] int SetSampleTime(long time);
            [PreserveSig] int GetSampleDuration(out long duration);
            [PreserveSig] int SetSampleDuration(long duration);
            [PreserveSig] int GetBufferCount(out int count);
            [PreserveSig] int GetBufferByIndex(int index, out IMFMediaBuffer buffer);
            [PreserveSig] int ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
        }

        [ComImport, Guid("045FA593-8799-42b8-BC8D-8968C6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMFMediaBuffer
        {
            [PreserveSig] int Lock(out IntPtr buffer, out int max, out int current);
            [PreserveSig] int Unlock();
            [PreserveSig] int GetCurrentLength(out int length);
        }

        [ComImport, Guid("70ae66f2-c809-4e4f-8915-bdcb406b7993"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMFSourceReader
        {
            [PreserveSig] int GetStreamSelection(int stream, out int selected);
            [PreserveSig] int SetStreamSelection(int stream, int selected);
            [PreserveSig] int GetNativeMediaType(int stream, int index, out IMFMediaType type);
            [PreserveSig] int GetCurrentMediaType(int stream, out IMFMediaType type);
            [PreserveSig] int SetCurrentMediaType(int stream, IntPtr reserved, IMFMediaType type);
            [PreserveSig] int SetCurrentPosition([In] ref Guid format, IntPtr position);
            [PreserveSig] int ReadSample(int stream, int flags, out int actual, out int streamFlags, out long timestamp, out IMFSample sample);
            [PreserveSig] int Flush(int stream);
            [PreserveSig] int GetServiceForStream(int stream, [In] ref Guid service, [In] ref Guid iid, out IntPtr obj);
            [PreserveSig] int GetPresentationAttribute(int stream, [In] ref Guid key, IntPtr value);
        }

        [DllImport("mfplat.dll", ExactSpelling = true)] static extern int MFStartup(int version, int flags);
        [DllImport("mfplat.dll", ExactSpelling = true)] static extern int MFShutdown();
        [DllImport("mfplat.dll", ExactSpelling = true)] static extern int MFCreateMediaType(out IMFMediaType type);
        [DllImport("mfreadwrite.dll", ExactSpelling = true)] static extern int MFCreateSourceReaderFromURL([MarshalAs(UnmanagedType.LPWStr)] string url, IntPtr attributes, out IMFSourceReader reader);
        [DllImport("ole32.dll", ExactSpelling = true)] static extern int PropVariantClear(IntPtr pv);
        [DllImport("ole32.dll", ExactSpelling = true)] static extern int CoInitializeEx(IntPtr reserved, int flags);
        [DllImport("ole32.dll", ExactSpelling = true)] static extern void CoUninitialize();

        const int FirstAudio = unchecked((int)0xFFFFFFFD), AllStreams = unchecked((int)0xFFFFFFFE), MediaSource = unchecked((int)0xFFFFFFFF);
        static Guid MajorType = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f"), Subtype = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
        static Guid AudioMajor = new Guid("73647561-0000-0010-8000-00AA00389B71");
        static Guid FloatFormat = new Guid("00000003-0000-0010-8000-00AA00389B71"), PcmFormat = new Guid("00000001-0000-0010-8000-00AA00389B71");
        static Guid NumChannels = new Guid("37e48bf5-645e-4c5b-89de-ada9e29b696a"), SampleRate = new Guid("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
        static Guid BitsPerSample = new Guid("f2deb57f-40fa-4764-aa33-ed4f2d1ff669"), BlockAlign = new Guid("322de230-9eeb-43bd-ab7a-ff412251541d");
        static Guid AvgBytes = new Guid("1aab75c8-cfef-451c-ab95-ac034b8e1731"), Duration = new Guid("6c990d33-bb8e-477a-8598-0d5d96fcd88a");

        static readonly Dictionary<Guid, string> Codecs = new Dictionary<Guid, string>
        {
            { new Guid("00000055-0000-0010-8000-00AA00389B71"), "MP3" },
            { new Guid("00000050-0000-0010-8000-00AA00389B71"), "MPEG audio" },
            { new Guid("00001610-0000-0010-8000-00AA00389B71"), "AAC" },
            { new Guid("00001600-0000-0010-8000-00AA00389B71"), "AAC" },
            { new Guid("0000F1AC-0000-0010-8000-00AA00389B71"), "FLAC" },
            { new Guid("00006C61-0000-0010-8000-00AA00389B71"), "ALAC" },
            { new Guid("0000704F-0000-0010-8000-00AA00389B71"), "Opus" },
            { new Guid("8D2FD10B-5841-4a6b-8905-588FEC1ADED9"), "Vorbis" },
            { new Guid("00000161-0000-0010-8000-00AA00389B71"), "WMA" },
            { new Guid("00000162-0000-0010-8000-00AA00389B71"), "WMA Pro" },
            { new Guid("00000163-0000-0010-8000-00AA00389B71"), "WMA Lossless" },
            { new Guid("0000000A-0000-0010-8000-00AA00389B71"), "WMA Voice" },
            { new Guid("00000001-0000-0010-8000-00AA00389B71"), "PCM" },
            { new Guid("00000003-0000-0010-8000-00AA00389B71"), "float PCM" },
            { new Guid("00000002-0000-0010-8000-00AA00389B71"), "ADPCM" },
            { new Guid("00000011-0000-0010-8000-00AA00389B71"), "IMA ADPCM" },
            { new Guid("00007361-0000-0010-8000-00AA00389B71"), "AMR" },
            { new Guid("00007362-0000-0010-8000-00AA00389B71"), "AMR-WB" },
            { new Guid("e06d802c-db46-11cf-b4d1-00805f6cbbea"), "Dolby AC-3" },
            { new Guid("a7fb87af-2d02-42fb-a4d4-05cd93843bdd"), "Dolby Digital Plus" }
        };

        static readonly string[] Lossless = { "FLAC", "ALAC", "WMA Lossless", "PCM", "float PCM" };

        static string Hex(int hr) { return "0x" + hr.ToString("X8"); }

        static void Release(object o) { if (o != null) { try { Marshal.ReleaseComObject(o); } catch { } } }

        static double Scale(bool isFloat, int bits, out double step)
        {
            step = isFloat ? 0 : Math.Pow(2, -(bits - 1));
            return 1 - step;
        }

        public static AudioInfo Read(string path, double start, double length, CancelToken ct, bool side, AudioKeep keep)
        {
            AudioInfo info = new AudioInfo();
            int co = -1;
            bool started = false;
            IMFSourceReader reader = null;
            try
            {
                co = CoInitializeEx(IntPtr.Zero, 0);
                if (MFStartup(0x00020070, 1) < 0) { info.Error = "Windows Media Foundation is not available on this PC (Windows N editions get it with the Media Feature Pack)"; return info; }
                started = true;
                int hr = MFCreateSourceReaderFromURL(Path.GetFullPath(path), IntPtr.Zero, out reader);
                if (hr < 0 || reader == null) { info.Error = "Windows can't open this file as sound (" + Hex(hr) + "). It may be damaged, or Windows has no decoder for its format"; return info; }
                reader.SetStreamSelection(AllStreams, 0);
                if (reader.SetStreamSelection(FirstAudio, 1) < 0) { info.Error = "the file has no sound track"; return info; }
                IMFMediaType native;
                if (reader.GetNativeMediaType(FirstAudio, 0, out native) >= 0 && native != null)
                {
                    try
                    {
                        Guid sub;
                        string name;
                        if (native.GetGUID(ref Subtype, out sub) >= 0) info.Codec = Codecs.TryGetValue(sub, out name) ? name : "";
                        bool lossless = Array.IndexOf(Lossless, info.Codec) >= 0;
                        int nb, bytesPerSec;
                        if (lossless && native.GetUINT32(ref BitsPerSample, out nb) >= 0 && nb > 0) info.Bits = nb;
                        if (!lossless && native.GetUINT32(ref AvgBytes, out bytesPerSec) >= 0 && bytesPerSec > 0) info.Kbps = (int)Math.Round(bytesPerSec * 8 / 1000.0);
                    }
                    finally { Release(native); }
                }
                if (info.Codec.Length == 0) info.Codec = (Path.GetExtension(path) ?? "").TrimStart('.').ToUpperInvariant();
                bool isFloat = true;
                hr = Choose(reader, FloatFormat);
                if (hr < 0) { isFloat = false; hr = Choose(reader, PcmFormat); }
                if (hr < 0) { info.Error = "Windows has no decoder that turns this sound into samples (" + Hex(hr) + ")"; return info; }
                int ch, rate, bits, align;
                if (!Current(reader, out ch, out rate, out bits, out align) || ch <= 0 || rate <= 0) { info.Error = "the decoded sound format is unknown"; return info; }
                if (isFloat ? bits != 32 : bits != 16 && bits != 24 && bits != 32) { info.Error = "the decoded sound format is not usable"; return info; }
                int bytes = bits / 8;
                if (align < bytes * ch) align = bytes * ch;
                info.Channels = ch;
                info.Rate = rate;
                IntPtr pv = Marshal.AllocCoTaskMem(24);
                try
                {
                    for (int i = 0; i < 24; i++) Marshal.WriteByte(pv, i, 0);
                    if (reader.GetPresentationAttribute(MediaSource, ref Duration, pv) >= 0)
                    {
                        long d = Marshal.ReadInt64(pv, 8);
                        if (d > 0) info.FileSeconds = d / 1e7;
                        PropVariantClear(pv);
                    }
                    if (start > 0)
                    {
                        for (int i = 0; i < 24; i++) Marshal.WriteByte(pv, i, 0);
                        Marshal.WriteInt16(pv, 0, 20);
                        Marshal.WriteInt64(pv, 8, (long)(start * 1e7));
                        Guid none = Guid.Empty;
                        reader.SetCurrentPosition(ref none, pv);
                    }
                }
                finally { Marshal.FreeCoTaskMem(pv); }
                info.Start = start;
                double expected = info.FileSeconds > 0 ? Math.Max(0, info.FileSeconds - start) : -1, step;
                double scale = Scale(isFloat, bits, out step);
                info.Data = new AudioCollector(ch, rate, length, expected, scale, step, side, keep);
                long skip = -1;
                float[] f = new float[0];
                byte[] raw = new byte[0];
                bool sawData = false, ended = false;
                double decodedEnd = 0;
                while (!info.Data.Full)
                {
                    if (ct != null && ct.IsCancelled) { info.Error = "stopped"; return info; }
                    int actual, flags;
                    long ts;
                    IMFSample sample;
                    hr = reader.ReadSample(FirstAudio, 0, out actual, out flags, out ts, out sample);
                    if (hr < 0)
                    {
                        Release(sample);
                        if (!sawData) info.Error = "Windows could not decode the sound (" + Hex(hr) + ")";
                        else info.Note = "Decoding stopped with an error at " + AudioTime.Clock(info.Start + info.Data.Seconds) + "; the rest of the file was not read.";
                        break;
                    }
                    if ((flags & 0x20) != 0)
                    {
                        int c2, r2, b2, a2;
                        bool known = Current(reader, out c2, out r2, out b2, out a2) && c2 > 0 && r2 > 0 && (isFloat ? b2 == 32 : b2 == 16 || b2 == 24 || b2 == 32);
                        if (!known || sawData && (c2 != ch || r2 != rate || b2 != bits))
                        {
                            Release(sample);
                            if (sawData) info.Note = "The sound format changes at " + AudioTime.Clock(info.Start + info.Data.Seconds) + "; only the part before it was analyzed.";
                            else info.Error = "the sound format changes in a way MinGent can't follow";
                            break;
                        }
                        if (!sawData && (c2 != ch || r2 != rate || b2 != bits))
                        {
                            ch = c2;
                            rate = r2;
                            bits = b2;
                            info.Channels = ch;
                            info.Rate = rate;
                            scale = Scale(isFloat, bits, out step);
                            info.Data = new AudioCollector(ch, rate, length, expected, scale, step, side, keep);
                            skip = -1;
                        }
                        bytes = bits / 8;
                        align = Math.Max(a2, bytes * ch);
                    }
                    if (sample != null)
                    {
                        try
                        {
                            double at = Math.Max(0, ts / 1e7);
                            if (skip < 0)
                            {
                                skip = start > at ? (long)Math.Min(Math.Round((start - at) * rate), long.MaxValue / 2) : 0;
                                info.Start = Math.Max(start, at);
                            }
                            IMFMediaBuffer buf;
                            if (sample.ConvertToContiguousBuffer(out buf) >= 0 && buf != null)
                            {
                                try
                                {
                                    IntPtr p;
                                    int max, len;
                                    if (buf.Lock(out p, out max, out len) >= 0)
                                    {
                                        try
                                        {
                                            int frames = len / align;
                                            if (frames > 0)
                                            {
                                                decodedEnd = Math.Max(decodedEnd, at + frames / (double)rate);
                                                if (f.Length < frames * ch) f = new float[frames * ch];
                                                if (isFloat && align == bytes * ch) Marshal.Copy(p, f, 0, frames * ch);
                                                else
                                                {
                                                    if (raw.Length < len) raw = new byte[len];
                                                    Marshal.Copy(p, raw, 0, len);
                                                    for (int i = 0; i < frames; i++)
                                                        for (int c = 0; c < ch; c++)
                                                        {
                                                            int o = i * align + c * bytes;
                                                            float v;
                                                            if (isFloat) v = BitConverter.ToSingle(raw, o);
                                                            else if (bytes == 2) v = (short)(raw[o] | raw[o + 1] << 8) / 32768f;
                                                            else if (bytes == 3) v = ((raw[o] | raw[o + 1] << 8 | raw[o + 2] << 16) << 8 >> 8) / 8388608f;
                                                            else v = (float)(BitConverter.ToInt32(raw, o) / 2147483648.0);
                                                            f[i * ch + c] = v;
                                                        }
                                                }
                                                int from = 0;
                                                if (skip > 0)
                                                {
                                                    from = (int)Math.Min(skip, frames);
                                                    skip -= from;
                                                }
                                                if (from < frames)
                                                {
                                                    if (from > 0) Array.Copy(f, from * ch, f, 0, (frames - from) * ch);
                                                    info.Data.Add(f, frames - from);
                                                    sawData = true;
                                                }
                                            }
                                        }
                                        finally { buf.Unlock(); }
                                    }
                                }
                                finally { Release(buf); }
                            }
                        }
                        finally { Release(sample); }
                    }
                    if ((flags & 0x2) != 0) { ended = true; break; }
                    if ((flags & 0x1) != 0)
                    {
                        if (!sawData) info.Error = "Windows reported an error while decoding the sound";
                        else info.Note = "Decoding stopped with an error at " + AudioTime.Clock(info.Start + info.Data.Seconds) + "; the rest of the file was not read.";
                        break;
                    }
                }
                if (info.FileSeconds <= 0 && ended && decodedEnd > 0) info.FileSeconds = decodedEnd;
            }
            catch (DllNotFoundException) { info.Error = "Windows Media Foundation is not available on this PC (Windows N editions get it with the Media Feature Pack)"; }
            catch (EntryPointNotFoundException) { info.Error = "Windows Media Foundation is not available on this PC"; }
            catch (Exception ex)
            {
                if (info.Data == null || info.Data.Frames == 0) info.Error = ex.Message;
                else info.Note = "Decoding stopped with an error at " + AudioTime.Clock(info.Start + info.Data.Seconds) + " (" + ex.Message + "); the rest of the file was not read.";
            }
            finally
            {
                Release(reader);
                if (started) { try { MFShutdown(); } catch { } }
                if (co == 0 || co == 1) { try { CoUninitialize(); } catch { } }
            }
            if (info.Data != null) info.Data.Finish();
            return info;
        }

        static int Choose(IMFSourceReader reader, Guid sub)
        {
            IMFMediaType want;
            int hr = MFCreateMediaType(out want);
            if (hr < 0 || want == null) return hr < 0 ? hr : -1;
            try
            {
                want.SetGUID(ref MajorType, ref AudioMajor);
                want.SetGUID(ref Subtype, ref sub);
                return reader.SetCurrentMediaType(FirstAudio, IntPtr.Zero, want);
            }
            finally { Release(want); }
        }

        static bool Current(IMFSourceReader reader, out int ch, out int rate, out int bits, out int align)
        {
            ch = rate = bits = align = 0;
            IMFMediaType cur;
            if (reader.GetCurrentMediaType(FirstAudio, out cur) < 0 || cur == null) return false;
            try
            {
                cur.GetUINT32(ref NumChannels, out ch);
                cur.GetUINT32(ref SampleRate, out rate);
                cur.GetUINT32(ref BitsPerSample, out bits);
                cur.GetUINT32(ref BlockAlign, out align);
                return true;
            }
            finally { Release(cur); }
        }
    }

    class AudioTempo
    {
        public double Bpm, Strength;

        public static AudioTempo Find(List<float> flux, double fps)
        {
            AudioTempo t = new AudioTempo();
            int len = flux.Count;
            if (len < fps * 6) return t;
            int half = Math.Max(2, (int)Math.Round(fps * 0.1));
            double[] pre = new double[len + 1];
            for (int i = 0; i < len; i++) pre[i + 1] = pre[i] + flux[i];
            double[] o = new double[len];
            for (int i = 0; i < len; i++)
            {
                int a = Math.Max(0, i - half), b = Math.Min(len - 1, i + half);
                o[i] = Math.Max(0, flux[i] - (pre[b + 1] - pre[a]) / (b - a + 1));
            }
            int lo = (int)Math.Floor(fps * 60 / 220), hi = (int)Math.Ceiling(fps * 60 / 45);
            if (lo < 2 || hi + 2 >= len / 2) return t;
            double zero = 0;
            for (int i = 0; i < len; i++) zero += o[i] * o[i];
            if (zero <= 0) return t;
            double[] ac = new double[hi + 3];
            for (int lag = lo - 1; lag <= hi + 2; lag++)
            {
                double s = 0;
                for (int i = 0; i + lag < len; i++) s += o[i] * o[i + lag];
                ac[lag] = s / zero * len / (len - lag);
            }
            double best = 0, sum = 0;
            int bestLag = 0, n = 0;
            for (int lag = lo; lag <= hi; lag++)
            {
                sum += ac[lag];
                n++;
                if (ac[lag] < ac[lag - 1] || ac[lag] < ac[lag + 1]) continue;
                double bpm = fps * 60 / lag;
                double prior = Math.Exp(-0.5 * Math.Pow(Math.Log(bpm / 120.0, 2), 2));
                double score = ac[lag] * prior;
                if (score > best) { best = score; bestLag = lag; }
            }
            if (bestLag == 0 || n == 0 || sum <= 0) return t;
            double x0 = ac[bestLag - 1], x1 = ac[bestLag], x2 = ac[bestLag + 1], den = x0 - 2 * x1 + x2;
            double shift = den != 0 ? Math.Max(-0.5, Math.Min(0.5, 0.5 * (x0 - x2) / den)) : 0;
            t.Bpm = fps * 60 / (bestLag + shift);
            t.Strength = ac[bestLag] / (sum / n);
            return t;
        }
    }

    static class AudioReport
    {
        static string N1(double v) { return v.ToString("0.0", CultureInfo.InvariantCulture); }

        static string Signed(double v, string format) { return (v >= 0 ? "+" : "") + v.ToString(format, CultureInfo.InvariantCulture); }

        static double Lufs(double z) { return z <= 0 ? double.NegativeInfinity : -0.691 + 10 * Math.Log10(z); }

        public class Loudness
        {
            public double Integrated = double.NaN, Range = double.NaN, MaxShort = double.NaN, Step;
            public List<double> Timeline = new List<double>();
        }

        public static Loudness Measure(AudioCollector d)
        {
            Loudness r = new Loudness();
            double[] z = d.Blocks.ToArray();
            int n = z.Length;
            double abs = Math.Pow(10, (-70 + 0.691) / 10), sum = 0;
            int cnt = 0;
            List<double> m = new List<double>();
            for (int i = 0; i + 4 <= n; i++) m.Add((z[i] + z[i + 1] + z[i + 2] + z[i + 3]) / 4);
            foreach (double v in m) if (v > abs) { sum += v; cnt++; }
            if (cnt > 0)
            {
                double rel = sum / cnt * 0.1;
                sum = 0;
                cnt = 0;
                foreach (double v in m) if (v > abs && v > rel) { sum += v; cnt++; }
                if (cnt > 0) r.Integrated = Lufs(sum / cnt);
            }
            if (n >= 30)
            {
                double win = 0;
                for (int i = 0; i < 30; i++) win += z[i];
                List<double> st = new List<double>();
                for (int i = 0; ; i++)
                {
                    st.Add(Math.Max(0, win / 30));
                    if (i + 30 >= n) break;
                    win += z[i + 30] - z[i];
                }
                double top = 0;
                foreach (double v in st) if (v > top) top = v;
                if (top > abs) r.MaxShort = Lufs(top);
                sum = 0;
                cnt = 0;
                foreach (double v in st) if (v > abs) { sum += v; cnt++; }
                if (cnt > 0)
                {
                    double rel = sum / cnt * 0.01;
                    List<double> keep = new List<double>();
                    foreach (double v in st) if (v > abs && v > rel) keep.Add(Lufs(v));
                    if (keep.Count >= 2)
                    {
                        keep.Sort();
                        r.Range = keep[(int)Math.Round((keep.Count - 1) * 0.95)] - keep[(int)Math.Round((keep.Count - 1) * 0.10)];
                    }
                }
            }
            double seconds = n / 10.0;
            if (seconds >= 10)
            {
                double[] steps = { 1, 2, 5, 10, 15, 20, 30, 60, 120, 300, 600, 900, 1800, 3600 };
                r.Step = steps[steps.Length - 1];
                foreach (double s in steps) if (seconds / s <= 24) { r.Step = s; break; }
                int per = (int)Math.Round(r.Step * 10);
                for (int i = 0; i < n; i += per)
                {
                    double s = 0;
                    int k = 0;
                    for (int j = i; j < n && j < i + per; j++, k++) s += z[j];
                    if (k < per / 2 && r.Timeline.Count > 0) break;
                    r.Timeline.Add(Lufs(s / k));
                }
            }
            return r;
        }

        public static string Build(string name, AudioInfo info, Loudness loud)
        {
            AudioCollector d = info.Data;
            MixAnalyzer mx = d.Mix;
            StringBuilder sb = new StringBuilder();
            double seconds = d.Seconds;
            double total = info.FileSeconds > 0 ? info.FileSeconds : info.Start + seconds;
            sb.Append(name).Append(": ").Append(info.Codec.Length > 0 ? info.Codec : "sound");
            if (info.Kbps > 0) sb.Append(", about ").Append(info.Kbps).Append(" kbps");
            if (info.Bits > 0 && info.Codec.IndexOf("-bit", StringComparison.Ordinal) < 0) sb.Append(", ").Append(info.Bits).Append("-bit");
            sb.Append(", ").Append(info.Rate.ToString(CultureInfo.InvariantCulture)).Append(" Hz, ")
              .Append(info.Channels == 1 ? "mono" : info.Channels == 2 ? "stereo" : info.Channels + " channels").Append(", ").Append(info.FileSeconds > 0 ? "" : "at least ").Append(AudioTime.Clock(total)).Append(" long\n");
            if (info.Start > 0.05 || seconds < total - 0.3) sb.Append("Analyzed ").Append(AudioTime.Clock(info.Start)).Append(" to ").Append(AudioTime.Clock(info.Start + seconds)).Append(".\n");
            if (info.Note != null) sb.Append(info.Note).Append('\n');
            if (d.Frames == 0) { sb.Append("There is no sound in that part.\n"); return sb.ToString(); }
            if (d.Invalid > 0) sb.Append("Invalid samples: ").Append(d.Invalid).Append(d.Invalid == 1 ? " sample is" : " samples are").Append(" not a number or far beyond full scale; they were counted as silence\n");
            double peak = 0, sq = 0;
            for (int c = 0; c < d.Channels; c++) { peak = Math.Max(peak, d.Peak[c]); sq += d.SumSq[c]; }
            double rms = Math.Sqrt(sq / ((double)d.Frames * d.Channels));
            if (peak < 3e-5) { sb.Append("The sound is silent: every sample is zero or nearly zero (peak ").Append(N1(AudioMath.Db(peak))).Append(" dBFS).\n"); return sb.ToString(); }
            sb.Append("Loudness: ");
            if (double.IsNaN(loud.Integrated)) sb.Append("too short or too quiet to measure in LUFS");
            else
            {
                sb.Append(N1(loud.Integrated)).Append(" LUFS integrated");
                if (!double.IsNaN(loud.MaxShort)) sb.Append(", loudest 3 s ").Append(N1(loud.MaxShort)).Append(" LUFS");
                if (!double.IsNaN(loud.Range)) sb.Append(", loudness range ").Append(N1(loud.Range)).Append(" LU");
            }
            sb.Append('\n');
            sb.Append("Levels: sample peak ").Append(N1(AudioMath.Db(peak))).Append(" dBFS, RMS ").Append(N1(AudioMath.Db(rms))).Append(" dBFS, crest factor ").Append(N1(AudioMath.Db(peak) - AudioMath.Db(rms))).Append(" dB\n");
            if (d.Channels == 2) Stereo(sb, d);
            Clipping(sb, d, info.Start);
            List<string> dc = new List<string>();
            int moreDc = 0;
            for (int c = 0; c < d.Channels; c++)
            {
                double off = d.Sum[c] / d.Frames;
                if (Math.Abs(off) < 0.002) continue;
                if (dc.Count == 8) { moreDc++; continue; }
                dc.Add((d.Channels == 2 ? (c == 0 ? "left " : "right ") : d.Channels > 2 ? "channel " + (c + 1) + " " : "") + Signed(off, "0.000"));
            }
            if (dc.Count > 0) sb.Append("DC offset: ").Append(string.Join(", ", dc.ToArray())).Append(moreDc > 0 ? " and " + moreDc + " more channels" : "").Append(" (the waveform is not centered on zero)\n");
            if (d.IntBits != 0 && info.Bits >= 16)
            {
                int unused = 0;
                uint b = d.IntBits;
                while ((b & 1) == 0 && unused < 32) { b >>= 1; unused++; }
                int used = 32 - unused;
                if (used < info.Bits) sb.Append("Bit depth: only the top ").Append(used).Append(" of the ").Append(info.Bits).Append(" bits are ever used, so the file holds ").Append(used).Append("-bit audio\n");
            }
            if (d.Side) sb.Append("Mono mix: the two channels cancel when mixed (one is inverted), so the silence, background, spectrum, tones, pitch and tempo lines and the pictures use left minus right\n");
            double frame = mx.Rms50Len / (double)mx.Rate;
            List<double[]> quiet = Silences(mx.Rms50, frame, -50, 0.5);
            sb.Append("Silence (below -50 dBFS for 0.5 s or more): ");
            if (quiet.Count == 0) sb.Append("none\n");
            else
            {
                List<string> parts = new List<string>();
                double all = 0;
                foreach (double[] q in quiet)
                {
                    double to = Math.Min(q[1], seconds);
                    all += to - q[0];
                    if (parts.Count < 12) parts.Add(AudioTime.Clock(info.Start + q[0]) + "-" + AudioTime.Clock(info.Start + to));
                }
                sb.Append(string.Join(", ", parts.ToArray()));
                if (quiet.Count > 12) sb.Append(" and ").Append(quiet.Count - 12).Append(" more");
                sb.Append(" (").Append(N1(all)).Append(" s in all)\n");
            }
            List<float> levels = new List<float>();
            foreach (float v in mx.Rms50) if (v > 3e-6f) levels.Add(v);
            if (levels.Count >= 20)
            {
                levels.Sort();
                sb.Append("Background: the quietest 5% of the time is about ").Append(Math.Round(AudioMath.Db(levels[levels.Count / 20])).ToString(CultureInfo.InvariantCulture)).Append(" dBFS\n");
            }
            if (loud.Timeline.Count >= 3)
            {
                List<string> pts = new List<string>();
                for (int i = 0; i < loud.Timeline.Count; i++)
                {
                    double v = loud.Timeline[i];
                    pts.Add(AudioTime.Short(info.Start + i * loud.Step) + " " + (v < -70 ? "silent" : Math.Round(v).ToString(CultureInfo.InvariantCulture)));
                }
                sb.Append("Loudness over time (LUFS every ").Append(loud.Step >= 60 ? (loud.Step / 60).ToString(CultureInfo.InvariantCulture) + " min" : loud.Step.ToString(CultureInfo.InvariantCulture) + " s").Append("): ").Append(string.Join(", ", pts.ToArray())).Append('\n');
            }
            Spectrum(sb, mx);
            Pitch(sb, mx);
            AudioTempo tempo = AudioTempo.Find(mx.Flux, mx.FluxRate);
            if (tempo.Bpm > 0 && tempo.Strength >= 1.8)
            {
                sb.Append("Tempo: about ").Append(Math.Round(tempo.Bpm).ToString(CultureInfo.InvariantCulture)).Append(" BPM");
                if (tempo.Strength < 2.6) sb.Append(" (a weak beat; it may also be half or double that)");
                if (seconds > 305) sb.Append(", from the first 5 minutes");
                sb.Append('\n');
            }
            else sb.Append("Tempo: no clear steady beat\n");
            return sb.ToString();
        }

        static void Clipping(StringBuilder sb, AudioCollector d, double start)
        {
            sb.Append("Clipping: ");
            if (d.ClipEvents == 0 && d.OverEvents == 0) { sb.Append("none\n"); return; }
            if (d.ClipEvents > 0)
            {
                long clipped = 0;
                foreach (long c in d.Clipped) clipped += c;
                sb.Append(d.ClipEvents).Append(d.ClipEvents == 1 ? " place" : " places").Append(" where the waveform is cut flat at full scale (").Append(clipped)
                  .Append(" samples), first at ").Append(AudioTime.Clock(start + d.FirstClip / (double)d.Rate));
                if (d.OverEvents > 0) sb.Append("; also ");
            }
            if (d.OverEvents > 0)
                sb.Append(d.Overs).Append(d.Overs == 1 ? " sample" : " samples").Append(" above full scale (0 dBFS) in ").Append(d.OverEvents).Append(d.OverEvents == 1 ? " place" : " places")
                  .Append(", first at ").Append(AudioTime.Clock(start + d.FirstOver / (double)d.Rate)).Append(d.Overs == 1 ? "; it clips" : "; they clip").Append(" when played or saved as 16- or 24-bit unless the level is lowered");
            sb.Append('\n');
        }

        static void Stereo(StringBuilder sb, AudioCollector d)
        {
            double l = d.SumSq[0], r = d.SumSq[1];
            sb.Append("Stereo: ");
            if (l <= 1e-12 * r) sb.Append("the left channel is silent");
            else if (r <= 1e-12 * l) sb.Append("the right channel is silent");
            else if (l + r - 2 * d.SumLR <= 1e-9 * (l + r)) sb.Append("both channels are identical, so the sound is mono");
            else
            {
                double corr = d.SumLR / Math.Sqrt(l * r), diff = 10 * Math.Log10(l / r);
                sb.Append("phase correlation ").Append(Signed(corr, "0.00"));
                if (corr <= -0.9) sb.Append(" (the channels cancel almost completely when played in mono)");
                else if (corr < 0) sb.Append(" (below 0: the channels partly cancel when played in mono)");
                else if (corr > 0.98) sb.Append(" (nearly mono)");
                if (Math.Abs(diff) >= 0.5) sb.Append(", the ").Append(diff > 0 ? "left" : "right").Append(" side is ").Append(N1(Math.Abs(diff))).Append(" dB louder");
                else sb.Append(", balanced");
            }
            sb.Append('\n');
        }

        static void Pitch(StringBuilder sb, MixAnalyzer mx)
        {
            double voiced = 0;
            foreach (float w in mx.F0W) voiced += w;
            voiced = mx.SoundWeight > 0 ? voiced / mx.SoundWeight : 0;
            if (mx.F0.Count < 10 || voiced < 0.2)
            {
                sb.Append("Pitch: no single clear pitch most of the time (several sounds at once, noise, or unpitched sound)\n");
                return;
            }
            double med = AudioMath.Weighted(mx.F0, mx.F0W, 0.5), lo = AudioMath.Weighted(mx.F0, mx.F0W, 0.1), hi = AudioMath.Weighted(mx.F0, mx.F0W, 0.9);
            sb.Append("Pitch: one clear pitch ").Append(Math.Round(Math.Min(1, voiced) * 100).ToString(CultureInfo.InvariantCulture)).Append("% of the time there is sound; median ")
              .Append(AudioMath.Hz(med)).Append(" (").Append(AudioMath.Note(med)).Append(")");
            if (hi / lo > 1.03) sb.Append(", mostly ").Append(AudioMath.Hz(lo)).Append(" to ").Append(AudioMath.Hz(hi)).Append(" (").Append(AudioMath.Note(lo)).Append(" to ").Append(AudioMath.Note(hi)).Append(")");
            sb.Append('\n');
        }

        public static List<double[]> Silences(List<float> rms, double frame, double thresholdDb, double minSeconds)
        {
            List<double[]> l = new List<double[]>();
            double limit = Math.Pow(10, thresholdDb / 20), from = -1;
            for (int i = 0; i < rms.Count; i++)
            {
                bool q = rms[i] < limit;
                if (q && from < 0) from = i * frame;
                else if (!q && from >= 0)
                {
                    if (i * frame - from >= minSeconds - 1e-9) l.Add(new double[] { from, i * frame });
                    from = -1;
                }
            }
            double end = rms.Count * frame;
            if (from >= 0 && end - from >= minSeconds - 1e-9) l.Add(new double[] { from, end });
            return l;
        }

        static double BinHz(MixAnalyzer mx, double k) { return k * mx.Rate / mx.N; }

        static void Spectrum(StringBuilder sb, MixAnalyzer mx)
        {
            double[] p = mx.AvgPower;
            if (mx.AvgWeight <= 0) return;
            int n = p.Length;
            double nyq = mx.Rate / 2.0, total = 0, centroid = 0, peakPow = 0;
            double[] edges = { 20, 60, 250, 500, 2000, 4000, 6000, 20000 };
            string[] names = { "sub-bass (20-60 Hz)", "bass (60-250 Hz)", "low mids (250-500 Hz)", "mids (500 Hz-2 kHz)", "upper mids (2-4 kHz)", "presence (4-6 kHz)", "air (6-20 kHz)" };
            double[] bands = new double[7];
            for (int k = 1; k < n; k++)
            {
                double hz = BinHz(mx, k);
                if (hz < 20 || hz > 20000) continue;
                total += p[k];
                centroid += hz * p[k];
                if (p[k] > peakPow) peakPow = p[k];
                for (int b = 0; b < 7; b++) if (hz >= edges[b] && hz < edges[b + 1]) bands[b] += p[k];
            }
            double level = total > 0 ? 10 * Math.Log10(2 * total / (mx.AvgWeight * mx.N * mx.AvgLen * 0.375)) : -200;
            if (level < -80) { sb.Append("Frequency balance: almost no sound between 20 Hz and 20 kHz (below -80 dBFS)\n"); return; }
            List<string> bl = new List<string>();
            for (int b = 0; b < 7 && edges[b] < nyq; b++)
            {
                double pct = 100 * bands[b] / total;
                bl.Add(names[b] + " " + (pct > 0 && pct < 0.5 ? "<1" : Math.Round(pct).ToString(CultureInfo.InvariantCulture)) + "%");
            }
            sb.Append("Frequency balance: ").Append(string.Join(", ", bl.ToArray())).Append('\n');
            double cum = 0, roll = 0;
            for (int k = 1; k < n && roll == 0; k++)
            {
                double hz = BinHz(mx, k);
                if (hz < 20 || hz > 20000) continue;
                cum += p[k];
                if (cum >= 0.95 * total) roll = hz;
            }
            int topBin = 0;
            for (int k = n - 1; k > 0; k--) if (p[k] > peakPow * 1e-7) { topBin = k; break; }
            double top = BinHz(mx, topBin);
            sb.Append("Brightness: spectral centroid ").Append(AudioMath.Hz(centroid / total)).Append(", 95% of the energy is below ").Append(AudioMath.Hz(roll));
            if (top < nyq * 0.97)
            {
                sb.Append(", content reaches about ").Append(AudioMath.Hz(top));
                double below = Mean(mx, p, top - 1500, top - 500), above = Mean(mx, p, top + 300, top + 1300);
                bool sharp = top + 1300 < nyq && below > 0 && below >= 1000 * above;
                if (sharp && top >= 11000 && top < 20000) sb.Append(" and stops sharply there, which is typical of lossy encoding such as MP3 or AAC");
                else if (sharp && mx.Rate >= 88000 && top >= 20000 && top <= 24500) sb.Append(" and stops sharply there, so the file was probably converted up from 44.1 or 48 kHz audio");
            }
            else sb.Append(", content reaches the top of the range (").Append(AudioMath.Hz(nyq)).Append(")");
            sb.Append('\n');
            List<double[]> peaks = new List<double[]>();
            for (int k = 2; k < n - 2; k++)
            {
                double hz = BinHz(mx, k);
                if (hz < 30 || hz > 5000) continue;
                if (p[k] <= p[k - 1] || p[k] < p[k + 1] || p[k] <= p[k - 2] || p[k] < p[k + 2] || p[k] < peakPow * 0.01) continue;
                if (p[k] < 16 * Around(p, k)) continue;
                double a = Math.Log(Math.Max(1e-30, p[k - 1])), b = Math.Log(p[k]), c = Math.Log(Math.Max(1e-30, p[k + 1])), den = a - 2 * b + c;
                double shift = den != 0 ? Math.Max(-0.5, Math.Min(0.5, 0.5 * (a - c) / den)) : 0;
                peaks.Add(new double[] { BinHz(mx, k + shift), p[k] });
            }
            peaks.Sort(delegate (double[] x, double[] y) { return y[1].CompareTo(x[1]); });
            List<double> chosen = new List<double>();
            List<string> pl = new List<string>();
            foreach (double[] pk in peaks)
            {
                bool near = false;
                foreach (double c in chosen) if (Math.Abs(pk[0] - c) < c * 0.03) near = true;
                if (near) continue;
                chosen.Add(pk[0]);
                string note = AudioMath.Note(pk[0]);
                pl.Add(AudioMath.Hz(pk[0]) + (note.Length > 0 ? " (" + note + ")" : ""));
                if (pl.Count == 6) break;
            }
            if (pl.Count > 0) sb.Append("Strongest tones: ").Append(string.Join(", ", pl.ToArray())).Append('\n');
            string key = Key(mx);
            if (key != null) sb.Append("Key: ").Append(key).Append(" (a rough guess from the overall balance of notes)\n");
        }

        static double Around(double[] p, int k)
        {
            int w = Math.Max(8, k / 4), lo = Math.Max(1, k - w), hi = Math.Min(p.Length - 1, k + w);
            List<double> v = new List<double>(hi - lo + 1);
            for (int j = lo; j <= hi; j++) if (j < k - 2 || j > k + 2) v.Add(p[j]);
            if (v.Count == 0) return 0;
            v.Sort();
            return v[v.Count / 2];
        }

        static double Mean(MixAnalyzer mx, double[] p, double f0, double f1)
        {
            int k0 = Math.Max(1, (int)(f0 * mx.N / mx.Rate)), k1 = Math.Min(p.Length - 1, (int)(f1 * mx.N / mx.Rate));
            if (k1 < k0) return 0;
            double s = 0;
            for (int k = k0; k <= k1; k++) s += p[k];
            return s / (k1 - k0 + 1);
        }

        static readonly double[] Major = { 6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88 };
        static readonly double[] Minor = { 6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17 };
        static readonly string[] KeyNames = { "C", "C#", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B" };

        public static string Key(MixAnalyzer mx)
        {
            double[] p = mx.AvgPower;
            double[] chroma = new double[12];
            double band = 0, notes = 0, peak = 0;
            for (int k = 1; k < p.Length; k++)
            {
                double hz = BinHz(mx, k);
                if (hz < 20 || hz > 20000) continue;
                band += p[k];
                if (hz < 65 || hz > 2100) continue;
                notes += p[k];
                if (p[k] > peak) peak = p[k];
            }
            if (peak <= 0 || notes < 0.05 * band) return null;
            for (int k = 1; k < p.Length; k++)
            {
                double hz = BinHz(mx, k);
                if (hz < 65 || hz > 2100) continue;
                double midi = 69 + 12 * Math.Log(hz / 440.0, 2);
                double near = Math.Round(midi);
                if (Math.Abs(midi - near) > 0.35 && hz > 250) continue;
                int pc = ((int)near % 12 + 12) % 12;
                chroma[pc] += Math.Log(1 + 1000 * Math.Sqrt(p[k] / peak));
            }
            return KeyFrom(chroma, 0.6);
        }

        public static string KeyFrom(double[] chroma, double least)
        {
            double cmax = 0, cmin = double.MaxValue;
            foreach (double v in chroma) { cmax = Math.Max(cmax, v); cmin = Math.Min(cmin, v); }
            if (cmax <= 0 || cmax - cmin < 0.15 * cmax) return null;
            double best = -2, second = -2;
            int bestRoot = 0, bestMode = 0, secondRoot = 0, secondMode = 0;
            for (int root = 0; root < 12; root++)
                for (int mode = 0; mode < 2; mode++)
                {
                    double r = Correlation(chroma, mode == 0 ? Major : Minor, root);
                    if (r > best) { second = best; secondRoot = bestRoot; secondMode = bestMode; best = r; bestRoot = root; bestMode = mode; }
                    else if (r > second) { second = r; secondRoot = root; secondMode = mode; }
                }
            if (best < least) return null;
            string name = KeyNames[bestRoot] + (bestMode == 0 ? " major" : " minor");
            bool relative = bestMode != secondMode && (bestMode == 0 ? (bestRoot + 9) % 12 == secondRoot : (bestRoot + 3) % 12 == secondRoot);
            if (relative && best - second < 0.04) name += " or " + KeyNames[secondRoot] + (secondMode == 0 ? " major" : " minor");
            return name;
        }

        static double Correlation(double[] chroma, double[] prof, int root)
        {
            double ma = 0, mb = 0;
            for (int i = 0; i < 12; i++) { ma += chroma[(i + root) % 12]; mb += prof[i]; }
            ma /= 12;
            mb /= 12;
            double sab = 0, saa = 0, sbb = 0;
            for (int i = 0; i < 12; i++)
            {
                double a = chroma[(i + root) % 12] - ma, b = prof[i] - mb;
                sab += a * b;
                saa += a * a;
                sbb += b * b;
            }
            return saa > 0 && sbb > 0 ? sab / Math.Sqrt(saa * sbb) : 0;
        }
    }

    static class AudioPictures
    {
        const int W = 1200, Left = 64, PlotW = MixAnalyzer.Width;
        static readonly Color Paper = Color.FromArgb(255, 255, 255), Field = Color.FromArgb(246, 247, 249), Grid = Color.FromArgb(222, 226, 232), Edge = Color.FromArgb(200, 205, 213);
        static readonly Color Ink = Color.FromArgb(28, 33, 44), Muted = Color.FromArgb(88, 96, 110), Wave = Color.FromArgb(44, 92, 156), WaveRms = Color.FromArgb(132, 167, 214), Clip = Color.FromArgb(196, 38, 38);

        static Font Face(float px, FontStyle style)
        {
            try { return new Font("Segoe UI", px, style, GraphicsUnit.Pixel); }
            catch { return new Font(FontFamily.GenericSansSerif, px, style, GraphicsUnit.Pixel); }
        }

        static void TimeAxis(Graphics g, Rectangle plot, double start, double seconds, Font f)
        {
            double step;
            List<double> ticks = AudioTime.Ticks(start, seconds, out step);
            using (Pen p = new Pen(Grid))
            using (SolidBrush b = new SolidBrush(Muted))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                foreach (double t in ticks)
                {
                    int x = plot.Left + (int)Math.Round((t - start) / seconds * plot.Width);
                    g.DrawLine(p, x, plot.Top, x, plot.Bottom);
                    g.DrawString(step < 1 ? AudioTime.Clock(t) : AudioTime.Short(t), f, b, x, plot.Bottom + 4, sf);
                }
            }
        }

        public static Bitmap Waveform(AudioInfo info, string name)
        {
            AudioCollector d = info.Data;
            MixAnalyzer mx = d.Mix;
            AudioEnvelope env = mx.Env;
            Rectangle plot = new Rectangle(Left, 38, PlotW, 218);
            Bitmap bm = new Bitmap(W, 286, PixelFormat.Format32bppArgb);
            double seconds = Math.Max(1e-3, mx.Count / (double)mx.Rate);
            using (Graphics g = Graphics.FromImage(bm))
            using (Font f = Face(12, FontStyle.Regular))
            using (Font tf = Face(14, FontStyle.Bold))
            using (SolidBrush ink = new SolidBrush(Ink))
            using (SolidBrush muted = new SolidBrush(Muted))
            {
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Paper);
                using (SolidBrush fb = new SolidBrush(Field)) g.FillRectangle(fb, plot);
                g.DrawString("Waveform of " + name + (d.Side ? " (left minus right)" : d.Channels > 1 ? " (channels mixed)" : ""), tf, ink, Left - 2, 9);
                int mid = plot.Top + plot.Height / 2, amp = plot.Height / 2;
                using (Pen gp = new Pen(Grid))
                {
                    foreach (double db in new double[] { 0, -6, -20 })
                    {
                        int dy = (int)Math.Round(Math.Pow(10, db / 20) * amp);
                        g.DrawLine(gp, plot.Left, mid - dy, plot.Right, mid - dy);
                        g.DrawLine(gp, plot.Left, mid + dy, plot.Right, mid + dy);
                        g.DrawString(db == 0 ? "0 dB" : db.ToString(CultureInfo.InvariantCulture) + " dB", f, muted, 8, mid - dy - 8);
                    }
                    g.DrawLine(gp, plot.Left, mid, plot.Right, mid);
                }
                TimeAxis(g, plot, info.Start, seconds, f);
                using (Pen pk = new Pen(Wave))
                using (Pen rp = new Pen(WaveRms))
                {
                    int count = env.Count;
                    for (int x = 0; x < PlotW && count > 0; x++)
                    {
                        int a = (int)((long)x * count / PlotW), b = Math.Min(count, Math.Max(a + 1, (int)((long)(x + 1) * count / PlotW)));
                        float lo = float.MaxValue, hi = float.MinValue;
                        double s = 0;
                        for (int i = a; i < b; i++) { if (env.Min[i] < lo) lo = env.Min[i]; if (env.Max[i] > hi) hi = env.Max[i]; s += env.Sq[i]; }
                        double rms = Math.Sqrt(s / ((double)(b - a) * env.Block));
                        lo = Math.Max(-1.05f, lo);
                        hi = Math.Min(1.05f, hi);
                        int px = plot.Left + x;
                        int y0 = Math.Max(plot.Top, mid - (int)Math.Round(hi * amp)), y1 = Math.Min(plot.Bottom, mid - (int)Math.Round(lo * amp));
                        if (y1 <= y0) y1 = y0 + 1;
                        g.DrawLine(pk, px, y0, px, y1);
                        int ry = (int)Math.Round(Math.Min(rms, Math.Max(hi, -lo)) * amp);
                        if (ry > 0) g.DrawLine(rp, px, mid - ry, px, mid + ry);
                    }
                }
                if (d.ClipAt.Count > 0)
                {
                    using (Pen cp = new Pen(Clip, 2))
                        foreach (long at in d.ClipAt)
                        {
                            int x = plot.Left + (int)Math.Min(PlotW - 1, at / (double)d.Rate / seconds * PlotW);
                            g.DrawLine(cp, x, plot.Top - 7, x, plot.Top - 1);
                        }
                    using (StringFormat sf = new StringFormat())
                    {
                        sf.Alignment = StringAlignment.Far;
                        g.DrawString(d.ClipEvents == 0 ? "Red marks: above full scale" : d.OverEvents == 0 ? "Red marks: clipping" : "Red marks: clipping or above full scale", f, muted, plot.Right, 12, sf);
                    }
                }
                using (Pen fp = new Pen(Edge)) g.DrawRectangle(fp, plot);
            }
            return bm;
        }

        static readonly int[][] Viridis = {
            new int[] { 68, 1, 84 }, new int[] { 72, 36, 117 }, new int[] { 65, 68, 135 }, new int[] { 53, 95, 141 }, new int[] { 42, 120, 142 },
            new int[] { 33, 145, 140 }, new int[] { 34, 168, 132 }, new int[] { 68, 191, 112 }, new int[] { 122, 209, 81 }, new int[] { 189, 223, 38 }, new int[] { 253, 231, 37 } };

        static int Shade(double t)
        {
            if (double.IsNaN(t)) t = 0;
            t = Math.Max(0, Math.Min(1, t)) * (Viridis.Length - 1);
            int i = Math.Min(Viridis.Length - 2, (int)t);
            double u = t - i;
            int[] a = Viridis[i], b = Viridis[i + 1];
            int r = (int)(a[0] + (b[0] - a[0]) * u), g = (int)(a[1] + (b[1] - a[1]) * u), bl = (int)(a[2] + (b[2] - a[2]) * u);
            return unchecked((int)0xFF000000) | r << 16 | g << 8 | bl;
        }

        public static Bitmap Spectrogram(AudioInfo info, string name)
        {
            MixAnalyzer mx = info.Data.Mix;
            int rows = MixAnalyzer.Rows;
            Rectangle plot = new Rectangle(Left, 38, PlotW, rows);
            Bitmap bm = new Bitmap(W, plot.Bottom + 30, PixelFormat.Format32bppArgb);
            double seconds = Math.Max(1e-3, mx.Count / (double)mx.Rate);
            List<float[]> cols = mx.Columns;
            float max = 1e-20f;
            foreach (float[] c in cols) foreach (float v in c) if (v > max) max = v;
            int[] px = new int[PlotW * rows];
            int nc = cols.Count;
            for (int x = 0; x < PlotW; x++)
            {
                int a = nc == 0 ? 0 : (int)((long)x * nc / PlotW), b = nc == 0 ? 0 : Math.Min(nc, Math.Max(a + 1, (int)((long)(x + 1) * nc / PlotW)));
                for (int r = 0; r < rows; r++)
                {
                    float v = 0;
                    for (int i = a; i < b; i++) if (cols[i][r] > v) v = cols[i][r];
                    px[(rows - 1 - r) * PlotW + x] = Shade((10 * Math.Log10(Math.Max(1e-20, v / max)) + 85) / 85);
                }
            }
            using (Bitmap field = new Bitmap(PlotW, rows, PixelFormat.Format32bppArgb))
            {
                BitmapData data = field.LockBits(new Rectangle(0, 0, PlotW, rows), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    for (int y = 0; y < rows; y++) Marshal.Copy(px, y * PlotW, new IntPtr(data.Scan0.ToInt64() + (long)y * data.Stride), PlotW);
                }
                finally { field.UnlockBits(data); }
                using (Graphics g = Graphics.FromImage(bm))
                {
                    g.Clear(Paper);
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                    g.DrawImage(field, plot, new Rectangle(0, 0, PlotW, rows), GraphicsUnit.Pixel);
                }
            }
            using (Graphics g = Graphics.FromImage(bm))
            using (Font f = Face(12, FontStyle.Regular))
            using (Font tf = Face(14, FontStyle.Bold))
            using (SolidBrush ink = new SolidBrush(Ink))
            using (SolidBrush muted = new SolidBrush(Muted))
            using (Pen tick = new Pen(Muted))
            using (StringFormat sf = new StringFormat())
            {
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.DrawString("Spectrogram of " + name + (info.Data.Side ? " (left minus right; " : " (") + "frequency on a log scale; brighter is louder)", tf, ink, Left - 2, 9);
                double step;
                List<double> ticks = AudioTime.Ticks(info.Start, seconds, out step);
                using (StringFormat cf = new StringFormat())
                {
                    cf.Alignment = StringAlignment.Center;
                    foreach (double t in ticks)
                    {
                        int x = plot.Left + (int)Math.Round((t - info.Start) / seconds * plot.Width);
                        g.DrawLine(tick, x, plot.Bottom, x, plot.Bottom + 4);
                        g.DrawString(step < 1 ? AudioTime.Clock(t) : AudioTime.Short(t), f, muted, x, plot.Bottom + 6, cf);
                    }
                }
                sf.Alignment = StringAlignment.Far;
                foreach (double hz in new double[] { 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000 })
                {
                    if (hz < mx.Bottom || hz > mx.Top) continue;
                    int y = plot.Bottom - 1 - (int)Math.Round(Math.Log(hz / mx.Bottom) / Math.Log(mx.Top / mx.Bottom) * (rows - 1));
                    g.DrawLine(tick, plot.Left - 5, y, plot.Left - 1, y);
                    g.DrawString(hz >= 1000 ? (hz / 1000).ToString(CultureInfo.InvariantCulture) + " kHz" : hz.ToString(CultureInfo.InvariantCulture) + " Hz", f, muted, plot.Left - 7, y - 8, sf);
                }
                using (Pen fp = new Pen(Edge)) g.DrawRectangle(fp, plot);
            }
            return bm;
        }
    }

    class AnalyzeAudioTool : AgentTool
    {
        public const double DefaultSeconds = 3600, MaxSeconds = 3 * 3600;

        public override string Name { get { return "analyze_audio"; } }
        public override string Description
        {
            get
            {
                return "Measure the sound of an audio or video file: loudness (LUFS, peak, RMS), clipping, silences, background noise, stereo, frequency balance, strongest tones, pitch, tempo and a rough key, " +
                       "plus a waveform and a spectrogram for models that see images. It can't make out words or name a song. WAV, AIFF, MP3, M4A, FLAC, WMA and more. Covers the first hour unless you set start and end. " +
                       "Set focus for beat and bar times, hits, notes, song sections, or listen (how a sound you made comes out: start and end, clicks, pitch, tone), and compare_with to find where another file lines up with this one.";
            }
        }
        public override Dictionary<string, object> Schema
        {
            get
            {
                Dictionary<string, object> focus = P("string", "overview (default), beats, hits, notes, sections or listen; long lists are saved to a .csv file");
                focus["enum"] = Json.A("overview", "beats", "hits", "notes", "sections", "listen");
                return Obj("path", P("string", "Sound or video file"), true,
                           "start", P("number", "Seconds (default 0)"), false,
                           "end", P("number", "Seconds (default: one hour after start)"), false,
                           "focus", focus, false,
                           "compare_with", P("string", "Another sound file: where it lines up with this one, and its tempo, key and loudness"), false,
                           "pictures", P("boolean", "Waveform and spectrogram; default true for the overview, false for a focus. False when checking many files"), false);
            }
        }
        public override string Label(Dictionary<string, object> a)
        {
            string n = ReadFileTool.Short(S(a, "path") ?? "the sound"), other = S(a, "compare_with");
            if (!string.IsNullOrEmpty(other)) return "Line up " + ReadFileTool.Short(other) + " with " + n;
            switch (AudioFocus.Name(S(a, "focus")))
            {
                case "beats": return "Find the beats in " + n;
                case "hits": return "Find the hits in " + n;
                case "notes": return "Find the notes in " + n;
                case "sections": return "Find the sections of " + n;
                case "listen": return "Listen to " + n;
            }
            return "Analyze " + n;
        }

        static string Target(ToolContext ctx, string p, out ToolOutcome fail)
        {
            fail = null;
            if (string.IsNullOrEmpty(p)) { fail = ToolOutcome.Fail("path is required"); return null; }
            string err;
            string full = AgentPaths.Resolve(p, ctx.Workspace, out err);
            if (full == null) { fail = ToolOutcome.Fail(err ?? "invalid path"); return null; }
            if (Directory.Exists(full)) { fail = ToolOutcome.Fail(full + " is a folder. Give one sound file; list_directory or find_files shows what is in it."); return null; }
            if (!File.Exists(full))
            {
                List<string> sim = PathHelp.Similar(full, ctx.Workspace, ctx.Ct, false);
                fail = ToolOutcome.Fail("file not found: " + full + "." + (sim.Count > 0 ? PathHelp.Describe(sim, ctx.Workspace) : " Use find_files or list_directory to find the right path."));
                return null;
            }
            string deny = ctx.CheckRead(full);
            if (deny != null) { fail = ToolOutcome.Fail(deny); fail.Denied = true; return null; }
            return full;
        }

        public override ToolOutcome Run(ToolContext ctx, Dictionary<string, object> a)
        {
            ToolOutcome fail;
            string full = Target(ctx, S(a, "path"), out fail);
            if (full == null) return fail;
            object sv, ev;
            a.TryGetValue("start", out sv);
            a.TryGetValue("end", out ev);
            double start = Math.Min(Math.Max(0, AudioTime.Parse(sv, 0)), Latest), end = Math.Min(AudioTime.Parse(ev, -1), Latest);
            string focus = AudioFocus.Name(S(a, "focus"));
            if (focus == null) return ToolOutcome.Fail("focus must be overview, beats, hits, notes, sections or listen");
            string other = S(a, "compare_with");
            if (!string.IsNullOrEmpty(other))
            {
                string second = Target(ctx, other, out fail);
                if (second == null) return fail;
                ToolOutcome c = AudioFocus.Compare(ctx, full, second, start, end);
                c.Untrusted = AgentPaths.Foreign(full) || AgentPaths.Foreign(second);
                return c;
            }
            if (focus == "overview") return Analyze(ctx, full, start, end, B(a, "pictures", true));
            ToolOutcome o = AudioFocus.Run(ctx, full, start, end, focus, B(a, "pictures", false));
            o.Untrusted = AgentPaths.Foreign(full);
            return o;
        }

        public static ToolOutcome Analyze(ToolContext ctx, string full, double start, double end, bool pictures)
        {
            ToolOutcome o = Measure(ctx, full, Math.Min(Math.Max(0, start), Latest), Math.Min(end, Latest), pictures);
            o.Untrusted = AgentPaths.Foreign(full);
            return o;
        }

        const double Latest = 1e7;

        public static AudioInfo Open(ToolContext ctx, string full, double start, double end, double cap, AudioKeep keep, out string error)
        {
            error = null;
            string name = Path.GetFileName(full);
            if (end >= 0 && end <= start) { error = "end (" + AudioTime.Clock(end) + ") must come after start (" + AudioTime.Clock(start) + ")"; return null; }
            double length = end > start ? Math.Min(end - start, MaxSeconds) : DefaultSeconds;
            if (cap > 0) length = Math.Min(length, cap);
            CancelToken ct = ctx.Ct;
            AudioInfo info;
            try
            {
                info = AudioFile.Load(full, start, length, ct, false, keep);
                if (info.Error == null && info.Data != null && info.Data.Cancels && !(ct != null && ct.IsCancelled))
                {
                    AudioInfo side = AudioFile.Load(full, start, length, ct, true, keep);
                    if (side.Error == null && side.Data != null && side.Data.Frames > 0) info = side;
                    else if (side.Error == "stopped") info.Error = "stopped";
                }
            }
            catch (Exception ex) { error = "could not read " + name + ": " + ex.Message; return null; }
            if ((ct != null && ct.IsCancelled) || info.Error == "stopped") { error = "stopped by the user"; return null; }
            if (info.Error == "unsupported") info.Error = "this WAV file uses an encoding MinGent can't read";
            if (info.Data == null || info.Data.Frames == 0)
            {
                if (info.Error != null) { error = "could not read " + name + ": " + info.Error + "."; return null; }
                if (info.FileSeconds > 0 && start >= info.FileSeconds - 0.01) { error = name + " is only " + AudioTime.Clock(info.FileSeconds) + " long, so there is nothing after " + AudioTime.Clock(start) + "."; return null; }
                if (start > 0) { error = "there is no sound after " + AudioTime.Clock(start) + " in " + name + "; the file is probably shorter than that."; return null; }
                if (info.Data == null) { error = "could not read " + name + "."; return null; }
            }
            info.Data.Finish();
            return info;
        }

        static ToolOutcome Measure(ToolContext ctx, string full, double start, double end, bool pictures)
        {
            string error;
            AudioInfo info = Open(ctx, full, start, end, 0, null, out error);
            if (info == null) return ToolOutcome.Fail(error);
            string name = Path.GetFileName(full);
            AudioReport.Loudness loud = AudioReport.Measure(info.Data);
            string report = AudioReport.Build(name, info, loud);
            double covered = info.Start + info.Data.Seconds;
            if (info.Data.Full && info.FileSeconds > covered + 1)
                report += "This covers " + AudioTime.Clock(info.Start) + " to " + AudioTime.Clock(covered) + " of " + AudioTime.Clock(info.FileSeconds) + "; set start and end to analyze another part.\n";
            else if (info.Data.Full && info.FileSeconds <= 0)
                report += "This covers " + AudioTime.Clock(info.Start) + " to " + AudioTime.Clock(covered) + "; the file may go on, so set start and end to analyze another part.\n";
            string summary = AudioTime.Clock(info.Data.Seconds) + (double.IsNaN(loud.Integrated) ? "" : ", " + loud.Integrated.ToString("0.0", CultureInfo.InvariantCulture) + " LUFS");
            ToolOutcome o = ToolOutcome.Ok(report, summary);
            if (pictures) AudioFocus.AddPictures(ctx, o, info, name);
            return o;
        }
    }
}
