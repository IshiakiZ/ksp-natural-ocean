using System;

namespace NaturalOcean
{
    /// <summary>
    /// A square inverse Fourier transform of N by N complex numbers (N a power of two), in place, for one thread to use at a
    /// time. It turns "how much of each wave" into "how high the water stands at each point": the sum, at every point of a grid,
    /// of N squared waves each given by a size and a place in its swing (Tessendorf, 2001; see wiki/ksp-natural-ocean/Research.md).
    /// The wave of row r and column c runs r times across the square one way and c times the other; rows and columns past
    /// the middle are the waves running the other way (N - r of them). Nothing is divided by N: the numbers given are the
    /// waves' own heights.
    /// </summary>
    sealed class Fourier
    {
        readonly int n;
        readonly int[] swap;
        readonly float[] cosT, sinT, lineRe, lineIm;

        public Fourier(int n)
        {
            if (n < 2 || (n & (n - 1)) != 0) throw new ArgumentException("the size must be a power of two");
            this.n = n;
            int bits = 0;
            while ((1 << bits) < n) bits++;
            swap = new int[n];
            for (int i = 0; i < n; i++)
            {
                int r = 0;
                for (int b = 0; b < bits; b++) if ((i & (1 << b)) != 0) r |= 1 << (bits - 1 - b);
                swap[i] = r;
            }
            cosT = new float[n / 2]; sinT = new float[n / 2];
            for (int k = 0; k < n / 2; k++) { double a = 2.0 * Math.PI * k / n; cosT[k] = (float)Math.Cos(a); sinT[k] = (float)Math.Sin(a); }
            lineRe = new float[n]; lineIm = new float[n];
        }

        void Line()
        {
            float[] re = lineRe, im = lineIm;
            for (int len = 2; len <= n; len <<= 1)
            {
                int half = len >> 1, step = n / len;
                for (int i = 0; i < n; i += len)
                    for (int j = 0, k = 0; j < half; j++, k += step)
                    {
                        float wr = cosT[k], wi = sinT[k];
                        int a = i + j, b = a + half;
                        float vr = re[b] * wr - im[b] * wi, vi = re[b] * wi + im[b] * wr;
                        re[b] = re[a] - vr; im[b] = im[a] - vi;
                        re[a] += vr; im[a] += vi;
                    }
            }
        }

        /// <summary>
        /// The transform, in place: `re` and `im` hold N rows of N numbers each. `reach`: no wave runs more than so many times
        /// across the square (0: any may), so the rows of those that would are all noughts and are left alone: most of the
        /// rows, for a patch that carries one band of wavelengths.
        /// </summary>
        public void Inverse(float[] re, float[] im, int reach = 0)
        {
            for (int row = 0; row < n; row++)
            {
                if (reach > 0 && row > reach && row < n - reach) continue;
                int at = row * n;
                for (int i = 0; i < n; i++) { lineRe[swap[i]] = re[at + i]; lineIm[swap[i]] = im[at + i]; }
                Line();
                Array.Copy(lineRe, 0, re, at, n); Array.Copy(lineIm, 0, im, at, n);
            }
            for (int col = 0; col < n; col++)
            {
                for (int i = 0; i < n; i++) { lineRe[swap[i]] = re[i * n + col]; lineIm[swap[i]] = im[i * n + col]; }
                Line();
                for (int i = 0; i < n; i++) { re[i * n + col] = lineRe[i]; im[i * n + col] = lineIm[i]; }
            }
        }
    }
}
