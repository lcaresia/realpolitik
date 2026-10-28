using System;

namespace CurrencyMod.Multiplayer
{
    /// <summary>
    /// Deterministic versions of the transcendental functions the economy uses. Math.Log/Exp/Pow/Tanh call the C
    /// runtime, whose last bits may differ between machines (Windows versions, macOS); in multiplayer every peer runs the
    /// economy and a single different bit desynchronizes the game. These use only +, −, × and ÷ (IEEE 754, identical on
    /// every x64 machine), so all peers get the same result.
    /// </summary>
    internal static class DetMath
    {
        private const double Ln2Hi = 6.93147180369123816490e-01;
        private const double Ln2Lo = 1.90821492927058770002e-10;
        private const double InvLn2 = 1.44269504088896338700e+00;
        private const double Sqrt2 = 1.41421356237309504880;

        internal static double Exp(double x)
        {
            if (double.IsNaN(x))
            {
                return double.NaN;
            }
            if (x > 709.78)
            {
                return double.PositiveInfinity;
            }
            if (x < -745.13)
            {
                return 0.0;
            }
            // x = k·ln2 + r with |r| ≤ ln2/2; e^x = 2^k · e^r.
            double k = Math.Floor(x * InvLn2 + 0.5);
            double r = (x - k * Ln2Hi) - k * Ln2Lo;
            double term = 1.0;
            double sum = 1.0;
            for (int n = 1; n < 24; n++)
            {
                term = term * r / n;
                sum += term;
            }
            return ScaleByPowerOfTwo(sum, (int)k);
        }

        internal static double Log(double x)
        {
            if (double.IsNaN(x) || x < 0)
            {
                return double.NaN;
            }
            if (x == 0)
            {
                return double.NegativeInfinity;
            }
            if (double.IsPositiveInfinity(x))
            {
                return x;
            }
            int exponent = 0;
            long bits = BitConverter.DoubleToInt64Bits(x);
            if ((bits >> 52) == 0)
            {
                // Subnormal: bring it into the normal range first.
                x *= 18014398509481984.0; // 2^54
                exponent -= 54;
                bits = BitConverter.DoubleToInt64Bits(x);
            }
            exponent += (int)((bits >> 52) & 0x7FF) - 1023;
            double mantissa = BitConverter.Int64BitsToDouble((bits & 0x000FFFFFFFFFFFFFL) | 0x3FF0000000000000L);
            if (mantissa > Sqrt2)
            {
                mantissa *= 0.5;
                exponent++;
            }
            // log(m) = 2·atanh(f), f = (m − 1)/(m + 1), |f| < 0.172.
            double f = (mantissa - 1.0) / (mantissa + 1.0);
            double f2 = f * f;
            double power = f;
            double series = 0.0;
            for (int n = 1; n < 40; n += 2)
            {
                series += power / n;
                power *= f2;
            }
            return exponent * Ln2Hi + (2.0 * series + exponent * Ln2Lo);
        }

        /// <summary>a^b for a ≥ 0 (the only case the economy needs); NaN for a negative base.</summary>
        internal static double Pow(double a, double b)
        {
            if (b == 0)
            {
                return 1.0;
            }
            if (a == 0)
            {
                return b > 0 ? 0.0 : double.PositiveInfinity;
            }
            if (a < 0 || double.IsNaN(a) || double.IsNaN(b))
            {
                return double.NaN;
            }
            return Exp(b * Log(a));
        }

        internal static double Tanh(double x)
        {
            if (double.IsNaN(x))
            {
                return double.NaN;
            }
            if (x > 20)
            {
                return 1.0;
            }
            if (x < -20)
            {
                return -1.0;
            }
            double e = Exp(2.0 * x);
            return (e - 1.0) / (e + 1.0);
        }

        private static double ScaleByPowerOfTwo(double value, int k)
        {
            while (k > 1000)
            {
                value *= BitConverter.Int64BitsToDouble((long)(1000 + 1023) << 52);
                k -= 1000;
            }
            while (k < -1000)
            {
                value *= BitConverter.Int64BitsToDouble((long)(-1000 + 1023) << 52);
                k += 1000;
            }
            return value * BitConverter.Int64BitsToDouble((long)(k + 1023) << 52);
        }
    }
}
