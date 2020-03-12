using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Test {
    /// <summary>
    /// Tests + Setup code comparing various quicksort to arraysort in terms of correctness/parity
    /// </summary>
    public static class DataGeneration
    {
        internal static (int[] randomData, int[] sortedData, string reproContext) GenerateData(
            int size, int seed, int forcedValue = -1, double forcedValueRate = double.NaN, int modulo = int.MaxValue, bool dontSort = false)
        {
            var r = new Random(seed);
            var data = new int[size];
            for (var i = 0; i < size; ++i)
                data[i] = double.IsNaN(forcedValueRate) ? r.Next() % modulo :
                    r.NextDouble() > forcedValueRate ? forcedValue : (r.Next() % modulo);

            int[] sorted = null;
            if (!dontSort) {
                sorted = new int[size];
                data.CopyTo(sorted, 0);
                Array.Sort(sorted);
            }

            var reproContext = "";

            using (var sha1 = new SHA512CryptoServiceProvider()) {
                Span<byte> hash = stackalloc byte[20];
                sha1.TryComputeHash(MemoryMarshal.Cast<int, byte>(new ReadOnlySpan<int>(data)), hash, out _);
                var dataHash = Convert.ToBase64String(hash);
                sha1.TryComputeHash(MemoryMarshal.Cast<int, byte>(new ReadOnlySpan<int>(sorted)), hash, out _);
                var sortedHash = Convert.ToBase64String(hash);

                reproContext = $"[{size},{seed}] -> [{dataHash},{sortedHash}]";
            }

            return (data, sorted, reproContext);
        }
        internal static (T[] randomData, T[] sortedData, string reproContext) GenerateData<T>(
            int size, int seed, T forcedValue = default, double forcedValueRate = double.NaN, T maxValue = default, bool dontSort = false)
        where T : unmanaged
        {
            var r = new Random(seed);
            var data = new T[size];
            for (var i = 0; i < size; ++i)
                data[i] = double.IsNaN(forcedValueRate) ? NextValue() :
                    r.NextDouble() > forcedValueRate    ? forcedValue : NextValue();

            T[] sorted = null;
            if (!dontSort) {
                sorted = new T[size];
                data.CopyTo(sorted, 0);
                Array.Sort(sorted);
            }

            var reproContext = "";

            using (var sha1 = new SHA512CryptoServiceProvider()) {
                Span<byte> hash = stackalloc byte[20];
                sha1.TryComputeHash(MemoryMarshal.Cast<T, byte>(new ReadOnlySpan<T>(data)), hash, out _);
                var dataHash = Convert.ToBase64String(hash);
                sha1.TryComputeHash(MemoryMarshal.Cast<T, byte>(new ReadOnlySpan<T>(sorted)), hash, out _);
                var sortedHash = Convert.ToBase64String(hash);

                reproContext = $"[{size},{seed}] -> [{dataHash},{sortedHash}]";
            }

            return (data, sorted, reproContext);

            T NextValue()
            {
                if (typeof(T) == typeof(int)) {
                    var value = r.Next();
                    return Unsafe.As<int, T>(ref value);
                } if (typeof(T) == typeof(uint)) {
                    var value = (uint) r.Next();
                    return Unsafe.As<uint, T>(ref value);
                } else if (typeof(T) == typeof(long)) {
                    var value = (long) r.Next() << 32 | (uint) r.Next();
                    return Unsafe.As<long, T>(ref value);
                } else if (typeof(T) == typeof(ulong)) {
                    var value = (ulong) (uint) r.Next() << 32 | (uint) r.Next();
                    return Unsafe.As<ulong, T>(ref value);
                } else if (typeof(T) == typeof(float)) {
                    var value = (float) r.NextDouble();
                    return Unsafe.As<float, T>(ref value);
                } else if (typeof(T) == typeof(double)) {
                    //var value = r.NextDouble();
                    var value = (double) r.Next(100);
                    return Unsafe.As<double, T>(ref value);
                }
                throw new NotSupportedException();
            }

        }
    }
}
