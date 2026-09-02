using System;
using System.Collections.Generic;

namespace CreditPincher.Core.Compat
{
    /// <summary>
    /// Numeric helpers that live on <c>Math</c> / <c>double</c> in .NET 8 but not in the
    /// .NET Framework 4.8 that ships with Windows.
    /// </summary>
    public static class MathEx
    {
        /// <summary>Stand-in for <c>double.IsFinite</c>.</summary>
        public static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        /// <summary>Stand-in for <c>Math.Clamp</c>.</summary>
        public static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        /// <summary>Stand-in for <c>Math.Clamp</c>.</summary>
        public static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }

    /// <summary>
    /// The handful of LINQ operators CreditPincher uses that arrived after .NET Framework
    /// 4.8: <c>MaxBy</c> and <c>DistinctBy</c> (.NET 6) and <c>GetValueOrDefault</c>
    /// (.NET Core). Same semantics as the originals for the way they are called here.
    /// </summary>
    public static class EnumerableEx
    {
        /// <summary>The element with the largest key. Returns the default when empty.</summary>
        public static TSource MaxBy<TSource, TKey>(
            this IEnumerable<TSource> source,
            Func<TSource, TKey> keySelector)
        {
            var comparer = Comparer<TKey>.Default;
            var best = default(TSource);
            var bestKey = default(TKey);
            var found = false;

            foreach (var item in source)
            {
                var key = keySelector(item);
                if (!found || comparer.Compare(key, bestKey) > 0)
                {
                    best = item;
                    bestKey = key;
                    found = true;
                }
            }

            return best;
        }

        /// <summary>First occurrence of each distinct key, in source order.</summary>
        public static IEnumerable<TSource> DistinctBy<TSource, TKey>(
            this IEnumerable<TSource> source,
            Func<TSource, TKey> keySelector)
        {
            var seen = new HashSet<TKey>();
            foreach (var item in source)
            {
                if (seen.Add(keySelector(item)))
                {
                    yield return item;
                }
            }
        }

        /// <summary>The value for <paramref name="key"/>, or the default when absent.</summary>
        public static TValue GetValueOrDefault<TKey, TValue>(
            this IReadOnlyDictionary<TKey, TValue> dictionary,
            TKey key)
        {
            TValue value;
            return dictionary.TryGetValue(key, out value) ? value : default(TValue);
        }
    }

    /// <summary>
    /// Minimal stand-in for .NET 8's <c>TimeProvider</c>, kept so
    /// <see cref="CreditPincher.Core.Services.CreditUsageStorage"/> can still have its
    /// clock replaced in a test.
    /// </summary>
    public abstract class TimeProvider
    {
        private static readonly TimeProvider SystemProvider = new SystemTimeProvider();

        public static TimeProvider System
        {
            get { return SystemProvider; }
        }

        public abstract DateTimeOffset GetUtcNow();

        private sealed class SystemTimeProvider : TimeProvider
        {
            public override DateTimeOffset GetUtcNow()
            {
                return DateTimeOffset.UtcNow;
            }
        }
    }
}
