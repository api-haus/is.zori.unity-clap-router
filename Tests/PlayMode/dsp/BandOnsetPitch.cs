using System;
using System.Collections.Generic;
using System.IO;

namespace Zori.ClapRouter.Tests.Dsp
{
    public struct BandEnergies
    {
        public double BassDb;
        public double MidDb;
        public double HighDb;
    }

    public struct PitchSweep
    {
        public double MinHz;
        public double MaxHz;
        public double Cents;
        public int Hops;
    }

    public static class WavReader
    {
        public static float[] ReadMono(string path, out int sampleRate, out int channels)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length < 44 || bytes[0] != 'R' || bytes[1] != 'I' || bytes[2] != 'F' || bytes[3] != 'F')
            {
                throw new InvalidDataException($"not a RIFF file: {path}");
            }

            int fmtChannels = 1;
            int fmtRate = 48000;
            int fmtBits = 16;
            int dataOffset = -1;
            int dataBytes = 0;

            int p = 12;
            while (p + 8 <= bytes.Length)
            {
                string id = new string(new[] { (char)bytes[p], (char)bytes[p + 1], (char)bytes[p + 2], (char)bytes[p + 3] });
                int size = ReadI32(bytes, p + 4);
                int body = p + 8;
                if (id == "fmt ")
                {
                    fmtChannels = ReadU16(bytes, body + 2);
                    fmtRate = ReadI32(bytes, body + 4);
                    fmtBits = ReadU16(bytes, body + 14);
                }
                else if (id == "data")
                {
                    dataOffset = body;
                    dataBytes = Math.Min(size, bytes.Length - body);
                    break;
                }

                p = body + size + (size & 1);
            }

            if (dataOffset < 0 || fmtBits != 16)
            {
                throw new InvalidDataException($"unsupported WAV (bits={fmtBits}, data@{dataOffset}): {path}");
            }

            sampleRate = fmtRate;
            channels = fmtChannels;

            int bytesPerFrame = fmtChannels * 2;
            int frames = dataBytes / bytesPerFrame;
            float[] mono = new float[frames];
            for (int i = 0; i < frames; i++)
            {
                int acc = 0;
                for (int ch = 0; ch < fmtChannels; ch++)
                {
                    short s = (short)ReadU16(bytes, dataOffset + i * bytesPerFrame + ch * 2);
                    acc += s;
                }

                mono[i] = (float)(acc / (double)fmtChannels / 32768.0);
            }

            return mono;
        }

        private static int ReadU16(byte[] b, int o)
        {
            return b[o] | (b[o + 1] << 8);
        }

        private static int ReadI32(byte[] b, int o)
        {
            return b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24);
        }
    }

    public sealed class Biquad
    {
        private readonly double _b0;
        private readonly double _b1;
        private readonly double _b2;
        private readonly double _a1;
        private readonly double _a2;

        private Biquad(double b0, double b1, double b2, double a0, double a1, double a2)
        {
            _b0 = b0 / a0;
            _b1 = b1 / a0;
            _b2 = b2 / a0;
            _a1 = a1 / a0;
            _a2 = a2 / a0;
        }

        public static Biquad LowPass(double freq, double sampleRate, double q = 0.70710678)
        {
            double w0 = 2.0 * Math.PI * freq / sampleRate;
            double cos = Math.Cos(w0);
            double alpha = Math.Sin(w0) / (2.0 * q);
            double b1 = 1.0 - cos;
            double b0 = b1 / 2.0;
            return new Biquad(b0, b1, b0, 1.0 + alpha, -2.0 * cos, 1.0 - alpha);
        }

        public static Biquad HighPass(double freq, double sampleRate, double q = 0.70710678)
        {
            double w0 = 2.0 * Math.PI * freq / sampleRate;
            double cos = Math.Cos(w0);
            double alpha = Math.Sin(w0) / (2.0 * q);
            double b1 = -(1.0 + cos);
            double b0 = (1.0 + cos) / 2.0;
            return new Biquad(b0, b1, b0, 1.0 + alpha, -2.0 * cos, 1.0 - alpha);
        }

        public float[] Process(float[] x, int start, int count)
        {
            float[] y = new float[count];
            double z1 = 0.0;
            double z2 = 0.0;
            for (int i = 0; i < count; i++)
            {
                double xn = x[start + i];
                double yn = _b0 * xn + z1;
                z1 = _b1 * xn - _a1 * yn + z2;
                z2 = _b2 * xn - _a2 * yn;
                y[i] = (float)yn;
            }

            return y;
        }
    }

    public static class Analysis
    {
        public static double RmsDb(float[] x, int start, int count)
        {
            if (count <= 0)
            {
                return -200.0;
            }

            double sum = 0.0;
            for (int i = 0; i < count; i++)
            {
                double v = x[start + i];
                sum += v * v;
            }

            double rms = Math.Sqrt(sum / count);
            return 20.0 * Math.Log10(rms + 1e-12);
        }

        public static BandEnergies BandRmsDb(float[] x, int sampleRate, int start, int count)
        {
            float[] bass = Biquad.LowPass(250.0, sampleRate).Process(x, start, count);
            float[] hp250 = Biquad.HighPass(250.0, sampleRate).Process(x, start, count);
            float[] mid = Biquad.LowPass(2000.0, sampleRate).Process(hp250, 0, count);
            float[] high = Biquad.HighPass(2000.0, sampleRate).Process(x, start, count);
            return new BandEnergies
            {
                BassDb = RmsDb(bass, 0, count),
                MidDb = RmsDb(mid, 0, count),
                HighDb = RmsDb(high, 0, count)
            };
        }

        public static long FirstOnsetSample(float[] x, int start, int end, double eps)
        {
            int run = 0;
            for (int i = start; i < end && i < x.Length; i++)
            {
                if (Math.Abs(x[i]) > eps)
                {
                    run++;
                    if (run >= 4)
                    {
                        return i - (run - 1);
                    }
                }
                else
                {
                    run = 0;
                }
            }

            return -1;
        }

        public static double AutocorrPitchHz(float[] x, int sampleRate, int center, int window, double minHz, double maxHz)
        {
            int half = window / 2;
            int start = Math.Max(0, center - half);
            int end = Math.Min(x.Length, start + window);
            int n = end - start;
            if (n < 256)
            {
                return 0.0;
            }

            double[] w = new double[n];
            double mean = 0.0;
            for (int i = 0; i < n; i++)
            {
                mean += x[start + i];
            }

            mean /= n;
            for (int i = 0; i < n; i++)
            {
                double hann = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / (n - 1));
                w[i] = (x[start + i] - mean) * hann;
            }

            int minLag = (int)(sampleRate / maxHz);
            int maxLag = Math.Min(n - 1, (int)(sampleRate / minHz));

            double bestScore = 0.0;
            int bestLag = 0;
            double norm0 = 1e-12;
            for (int i = 0; i < n; i++)
            {
                norm0 += w[i] * w[i];
            }

            for (int lag = minLag; lag <= maxLag; lag++)
            {
                double acc = 0.0;
                for (int i = 0; i + lag < n; i++)
                {
                    acc += w[i] * w[i + lag];
                }

                double score = acc / norm0;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestLag = lag;
                }
            }

            if (bestLag == 0 || bestScore < 0.05)
            {
                return 0.0;
            }

            double refined = ParabolicLag(w, n, bestLag);
            return sampleRate / refined;
        }

        private static double ParabolicLag(double[] w, int n, int lag)
        {
            double Corr(int l)
            {
                if (l < 1 || l >= n)
                {
                    return 0.0;
                }

                double acc = 0.0;
                for (int i = 0; i + l < n; i++)
                {
                    acc += w[i] * w[i + l];
                }

                return acc;
            }

            double ym1 = Corr(lag - 1);
            double y0 = Corr(lag);
            double yp1 = Corr(lag + 1);
            double denom = ym1 - 2.0 * y0 + yp1;
            if (Math.Abs(denom) < 1e-12)
            {
                return lag;
            }

            double delta = 0.5 * (ym1 - yp1) / denom;
            return lag + delta;
        }

        public static PitchSweep PitchSweepCents(float[] x, int sampleRate, int start, int end, int hop, int window,
            double minHz, double maxHz)
        {
            double lo = double.MaxValue;
            double hi = 0.0;
            int hops = 0;
            List<double> track = new List<double>();
            for (int c = start + window / 2; c + window / 2 < end; c += hop)
            {
                double f = AutocorrPitchHz(x, sampleRate, c, window, minHz, maxHz);
                if (f > 0.0)
                {
                    track.Add(f);
                }
            }

            if (track.Count >= 3)
            {
                double[] sorted = track.ToArray();
                Array.Sort(sorted);
                lo = Percentile(sorted, 0.1);
                hi = Percentile(sorted, 0.9);
                hops = track.Count;
            }

            double cents = lo < hi && lo > 0.0 ? 1200.0 * Math.Log(hi / lo, 2.0) : 0.0;
            return new PitchSweep { MinHz = lo == double.MaxValue ? 0.0 : lo, MaxHz = hi, Cents = cents, Hops = hops };
        }

        private static double Percentile(double[] sorted, double q)
        {
            if (sorted.Length == 0)
            {
                return 0.0;
            }

            double pos = q * (sorted.Length - 1);
            int i = (int)Math.Floor(pos);
            double frac = pos - i;
            if (i + 1 < sorted.Length)
            {
                return sorted[i] * (1.0 - frac) + sorted[i + 1] * frac;
            }

            return sorted[i];
        }

        public static double Centroid(float[] x, int sampleRate, int center, int window)
        {
            double[] re;
            double[] im;
            WindowedFft(x, center, window, out re, out im);
            int half = re.Length / 2;
            double num = 0.0;
            double den = 0.0;
            for (int k = 1; k < half; k++)
            {
                double mag = Math.Sqrt(re[k] * re[k] + im[k] * im[k]);
                double freq = (double)k * sampleRate / re.Length;
                num += freq * mag;
                den += mag;
            }

            return den > 1e-12 ? num / den : 0.0;
        }

        public static List<double> SpectralPeaks(float[] x, int sampleRate, int center, int window, double fLo,
            double fHi, int maxPeaks)
        {
            double[] re;
            double[] im;
            WindowedFft(x, center, window, out re, out im);
            int n = re.Length;
            int half = n / 2;
            double[] mag = new double[half];
            double maxMag = 1e-12;
            for (int k = 0; k < half; k++)
            {
                mag[k] = Math.Sqrt(re[k] * re[k] + im[k] * im[k]);
                if (mag[k] > maxMag)
                {
                    maxMag = mag[k];
                }
            }

            int kLo = Math.Max(1, (int)(fLo * n / sampleRate));
            int kHi = Math.Min(half - 1, (int)(fHi * n / sampleRate));
            List<KeyValuePair<double, double>> peaks = new List<KeyValuePair<double, double>>();
            for (int k = kLo; k <= kHi; k++)
            {
                if (mag[k] > mag[k - 1] && mag[k] >= mag[k + 1] && mag[k] > 0.15 * maxMag)
                {
                    double freq = (double)k * sampleRate / n;
                    peaks.Add(new KeyValuePair<double, double>(freq, mag[k]));
                }
            }

            peaks.Sort((a, b) => b.Value.CompareTo(a.Value));
            List<double> result = new List<double>();
            foreach (KeyValuePair<double, double> pk in peaks)
            {
                bool near = false;
                foreach (double f in result)
                {
                    if (Math.Abs(f - pk.Key) < 25.0)
                    {
                        near = true;
                        break;
                    }
                }

                if (!near)
                {
                    result.Add(pk.Key);
                }

                if (result.Count >= maxPeaks)
                {
                    break;
                }
            }

            return result;
        }

        public static int OnsetCount(float[] x, int sampleRate, int start, int end, int window, int hop)
        {
            List<double> flux = new List<double>();
            double[] prev = null;
            for (int c = start; c + window < end; c += hop)
            {
                double[] re;
                double[] im;
                WindowedFft(x, c + window / 2, window, out re, out im);
                int half = re.Length / 2;
                double[] mag = new double[half];
                for (int k = 0; k < half; k++)
                {
                    mag[k] = Math.Sqrt(re[k] * re[k] + im[k] * im[k]);
                }

                double f = 0.0;
                if (prev != null)
                {
                    for (int k = 0; k < half; k++)
                    {
                        double d = mag[k] - prev[k];
                        if (d > 0.0)
                        {
                            f += d;
                        }
                    }
                }

                flux.Add(f);
                prev = mag;
            }

            if (flux.Count < 3)
            {
                return 0;
            }

            double mean = 0.0;
            foreach (double v in flux)
            {
                mean += v;
            }

            mean /= flux.Count;
            double var = 0.0;
            foreach (double v in flux)
            {
                var += (v - mean) * (v - mean);
            }

            double std = Math.Sqrt(var / flux.Count);
            double thresh = mean + 0.6 * std;

            int count = 0;
            int refractory = 0;
            for (int i = 1; i < flux.Count - 1; i++)
            {
                if (refractory > 0)
                {
                    refractory--;
                    continue;
                }

                if (flux[i] > thresh && flux[i] >= flux[i - 1] && flux[i] > flux[i + 1])
                {
                    count++;
                    refractory = Math.Max(1, sampleRate / 20 / hop);
                }
            }

            return count;
        }

        private static void WindowedFft(float[] x, int center, int window, out double[] re, out double[] im)
        {
            int n = NextPow2(window);
            re = new double[n];
            im = new double[n];
            int half = window / 2;
            int start = center - half;
            for (int i = 0; i < window; i++)
            {
                int idx = start + i;
                double sample = idx >= 0 && idx < x.Length ? x[idx] : 0.0;
                double hann = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / (window - 1));
                re[i] = sample * hann;
            }

            Fft(re, im);
        }

        private static int NextPow2(int v)
        {
            int p = 1;
            while (p < v)
            {
                p <<= 1;
            }

            return p;
        }

        private static void Fft(double[] re, double[] im)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1)
                {
                    j ^= bit;
                }

                j ^= bit;
                if (i < j)
                {
                    (re[i], re[j]) = (re[j], re[i]);
                    (im[i], im[j]) = (im[j], im[i]);
                }
            }

            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = -2.0 * Math.PI / len;
                double wRe = Math.Cos(ang);
                double wIm = Math.Sin(ang);
                for (int i = 0; i < n; i += len)
                {
                    double curRe = 1.0;
                    double curIm = 0.0;
                    for (int k = 0; k < len / 2; k++)
                    {
                        int a = i + k;
                        int b = i + k + len / 2;
                        double tRe = re[b] * curRe - im[b] * curIm;
                        double tIm = re[b] * curIm + im[b] * curRe;
                        re[b] = re[a] - tRe;
                        im[b] = im[a] - tIm;
                        re[a] += tRe;
                        im[a] += tIm;
                        double nextRe = curRe * wRe - curIm * wIm;
                        curIm = curRe * wIm + curIm * wRe;
                        curRe = nextRe;
                    }
                }
            }
        }
    }
}
