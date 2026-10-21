using System;

namespace NaturalOcean
{
    /// <summary>
    /// What the sea is doing at a place: the wind over it and how long a stretch of water that wind has had, the water's
    /// depth, and a swell from elsewhere. Directions are the way the waves travel, in radians from the first of the sea's two
    /// level axes towards the second (see Sea: the axes go along with the camera without turning against the ground).
    /// </summary>
    public struct SeaState
    {
        public float Wind;            // m/s at 10 m
        public float WindTo;          // which way it blows the waves
        public float Fetch;           // metres of open water upwind
        public float Depth;           // metres of water under the waves (0: deep)
        public float Swell;           // the swell's height (the mean of its highest third), m
        public float SwellPeriod;     // seconds between its crests
        public float SwellTo;         // which way it travels
        public float Gravity;         // m/s2 at the sea's surface
        public float Air;             // (since 0.3.0) the air's density over the sea against Kerbin's at its sea level: how hard the wind takes hold of the water (0: as Kerbin's)

        public bool Near(in SeaState o) =>
            Math.Abs(Wind - o.Wind) < 0.05f && Math.Abs(Angle(WindTo - o.WindTo)) < 0.01f && Math.Abs(Fetch - o.Fetch) < 0.02f * Math.Max(1f, Fetch) && Math.Abs(Depth - o.Depth) < 0.03f * Math.Max(1f, Depth)
            && Math.Abs(Swell - o.Swell) < 0.01f && Math.Abs(SwellPeriod - o.SwellPeriod) < 0.05f && Math.Abs(Angle(SwellTo - o.SwellTo)) < 0.01f && Math.Abs(Gravity - o.Gravity) < 0.001f && Math.Abs(Air - o.Air) < 0.01f;

        static float Angle(float a) { while (a > Math.PI) a -= 2f * (float)Math.PI; while (a < -Math.PI) a += 2f * (float)Math.PI; return a; }
    }

    /// <summary>
    /// How much energy the sea has at each wavelength and direction, as measured at sea and written down as formulas:
    /// JONSWAP's spectrum for a wind sea still growing over its fetch (Hasselmann and others, 1973), which becomes Pierson and
    /// Moskowitz's fully developed sea when the fetch is long enough; its depth correction (TMA: Kitaigorodskii's factor) where
    /// the water is shallow; Mitsuyasu's spread of directions about the wind (wide for the short waves, narrow at the peak);
    /// and a swell, a narrow band of long waves from one direction. After Horvath (2015), who put these to use for pictures.
    /// </summary>
    static class Spectrum
    {
        /// <summary>The mean of the highest third of the wind's waves, m: 0.21 U2/g when fully developed, less over a short fetch (0.0016 of the root of g F / U2).</summary>
        public static double WindHeight(in SeaState s)
        {
            double g = s.Gravity, u = Math.Max(s.Wind, 0.01);
            double f = Math.Max(1.0, g * Math.Max(s.Fetch, 100.0) / (u * u));
            return Math.Min(0.21, 0.0016 * Math.Sqrt(f)) * u * u / g;
        }

        /// <summary>The frequency of the wind sea's dominant waves, radians a second.</summary>
        public static double Peak(in SeaState s)
        {
            double g = s.Gravity, u = Math.Max(s.Wind, 0.01);
            double f = Math.Min(2.2e4, Math.Max(1.0, g * Math.Max(s.Fetch, 100.0) / (u * u)));
            return 22.0 * (g / u) * Math.Pow(f, -1.0 / 3.0);
        }

        /// <summary>
        /// The wind sea's energy at a frequency (m2 s), before it is set to the height above (see Scale). Its short waves fall
        /// off here as the fifth power of the frequency all the way (Phillips: a sea as steep as it can be at every length).
        /// Real seas hold more than that in the waves a few times shorter than the dominant ones (Toba's fourth power) and far
        /// more in the ripples; how much more is set by measure where the waves are shaped (Waves.cs), not here.
        /// </summary>
        static double Jonswap(double omega, in SeaState s, double peak)
        {
            if (omega <= 0.0) return 0.0;
            double g = s.Gravity, u = Math.Max(s.Wind, 0.01);
            double f = Math.Min(2.2e4, Math.Max(1.0, g * Math.Max(s.Fetch, 100.0) / (u * u)));
            // (the peak three times sharper while the sea is growing; none of that once it is fully developed)
            double gamma = f < 1.0e4 ? 3.3 : 3.3 + (1.0 - 3.3) * (f - 1.0e4) / 1.2e4;
            double sigma = omega <= peak ? 0.07 : 0.09;
            double r = Math.Exp(-(omega - peak) * (omega - peak) / (2.0 * sigma * sigma * peak * peak));
            double e = g * g / Math.Pow(omega, 5.0) * Math.Exp(-1.25 * Math.Pow(peak / omega, 4.0)) * Math.Pow(gamma, r);
            if (s.Depth > 0f)
            {
                // (shallow water cannot carry the long waves' energy: Kitaigorodskii's factor)
                double wh = omega * Math.Sqrt(s.Depth / g);
                e *= wh <= 1.0 ? 0.5 * wh * wh : wh < 2.0 ? 1.0 - 0.5 * (2.0 - wh) * (2.0 - wh) : 1.0;
            }
            return e;
        }

        /// <summary>The number the wind sea's energy is to be multiplied by for its waves to come to WindHeight.</summary>
        public static double Scale(in SeaState s)
        {
            double peak = Peak(s), sum = 0.0;
            // (by steps of a fiftieth of the frequency, from a third of the peak to forty times it)
            double w = peak * 0.33;
            while (w < peak * 40.0) { double dw = w * 0.02; sum += Jonswap(w + 0.5 * dw, s, peak) * dw; w += dw; }
            double want = WindHeight(s);
            return sum > 1e-12 ? want * want / (16.0 * sum) : 0.0;
        }

        /// <summary>The water's skin, as it pulls ripples flat: its tension over its density, m3/s2 (water at 20 degrees).</summary>
        public const double Skin = 7.3e-5;

        /// <summary>
        /// How fast a wave swings (radians a second) whose length is 2 pi / k, in water so deep (0: deep), and how that changes
        /// with k. Gravity pulls long waves back, the water's skin the ripples (below about seven centimetres it matters; below
        /// 1.7 cm it is the stronger of the two).
        /// </summary>
        public static double Swing(double k, double depth, double g, out double slope)
        {
            double pull = g * k + Skin * k * k * k, pullSlope = g + 3.0 * Skin * k * k;
            if (depth > 0.0 && k * depth < 20.0)
            {
                double tanh = Math.Tanh(k * depth);
                double omega = Math.Sqrt(pull * tanh);
                slope = (pullSlope * tanh + pull * depth * (1.0 - tanh * tanh)) / (2.0 * Math.Max(omega, 1e-9));
                return omega;
            }
            double deep = Math.Sqrt(pull);
            slope = pullSlope / (2.0 * Math.Max(deep, 1e-9));
            return deep;
        }

        static double LogGamma(double x)
        {
            // (Lanczos)
            double[] c = { 76.18009172947146, -86.50532032941677, 24.01409824083091, -1.231739572450155, 0.1208650973866179e-2, -0.5395239384953e-5 };
            double y = x, t = x + 5.5;
            t -= (x + 0.5) * Math.Log(t);
            double sum = 1.000000000190015;
            for (int j = 0; j < 6; j++) sum += c[j] / ++y;
            return -t + Math.Log(2.5066282746310005 * sum / x);
        }

        /// <summary>How the energy at one frequency is shared out by direction: cos to the power 2s of half the angle from the wind, so that all directions together come to one.</summary>
        static double Spread(double off, double sharp)
        {
            double c = Math.Cos(0.5 * off);
            if (c <= 0.0) return 0.0;
            double norm = Math.Exp(LogGamma(sharp + 1.0) - LogGamma(sharp + 0.5)) / (2.0 * Math.Sqrt(Math.PI));
            return norm * Math.Pow(c * c, sharp);
        }

        /// <summary>
        /// The energy of the waves that run along one wave vector (kx, kz: radians a metre along the sea's two axes), per unit
        /// of wave-vector area, m4: the wind's sea and the swell, apart (the wind sea's short waves are raised afterwards, the
        /// swell is not). `scale` is Scale's number for this sea. `omega`: how fast such a wave swings.
        /// </summary>
        public static void At(double kx, double kz, in SeaState s, double scale, double peak, out double wind, out double swell, out double omega)
        {
            wind = 0.0; swell = 0.0; omega = 0.0;
            double k = Math.Sqrt(kx * kx + kz * kz);
            if (k < 1e-9) return;
            omega = Swing(k, s.Depth, s.Gravity, out double slope);
            double way = Math.Atan2(kz, kx);
            if (s.Wind > 0.05f && scale > 0.0)
            {
                // (Mitsuyasu: the dominant waves keep to the wind, the more so the longer the sea has grown; shorter and longer
                // ones spread. Not below 2.2 though, where his formula goes on down to waves running every way alike: Cox and
                // Munk found the sea's slopes half as large again along the wind as across it, which is a spread of about 2.)
                double top = 11.5 * Math.Pow(Math.Max(peak * s.Wind / s.Gravity, 0.5), -2.5);
                double sharp = omega <= peak ? top * Math.Pow(omega / peak, 5.0) : top * Math.Pow(omega / peak, -2.5);
                sharp = Math.Min(Math.Max(sharp, 2.2), 60.0);
                wind = scale * Jonswap(omega, s, peak) * Spread(Wrap(way - s.WindTo), sharp) * slope / k;
            }
            if (s.Swell > 0.01f && s.SwellPeriod > 1f)
            {
                // (Swell: long waves from storms far off. One that has run a thousand kilometres keeps to its way within ten
                // or fifteen degrees and to its period within a tenth, and comes in groups (sets) with short, broken crests;
                // and the open sea nearly always carries two, from two storms: here a second, three-fifths as high, a fifth
                // shorter in period, coming seventy degrees round. Held to a few degrees and one band, as it first was, it
                // came out as a handful of the largest patch's waves: one long, perfect sine across the whole sea.)
                swell = SwellBand(omega, way, s.Swell, s.SwellPeriod, s.SwellTo, k, slope)
                      + SwellBand(omega, way, 0.6f * s.Swell, 0.8f * s.SwellPeriod, s.SwellTo + 1.25f, k, slope);
            }
        }

        /// <summary>One train of swell: the mean of its highest third so high, so many seconds apart, coming so.</summary>
        static double SwellBand(double omega, double way, double height, double period, double to, double k, double slope)
        {
            double at = 2.0 * Math.PI / period, wide = 0.09 * at;
            double band = Math.Exp(-(omega - at) * (omega - at) / (2.0 * wide * wide)) / (wide * Math.Sqrt(2.0 * Math.PI));
            return height * height / 16.0 * band * Spread(Wrap(way - to), 18.0) * slope / k;
        }

        static double Wrap(double a) { while (a > Math.PI) a -= 2.0 * Math.PI; while (a < -Math.PI) a += 2.0 * Math.PI; return a; }
    }
}
