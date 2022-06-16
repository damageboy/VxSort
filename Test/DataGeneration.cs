using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Test {
    /// <summary>
    /// Tests + Setup code comparing various quicksort to arraysort in terms of correctness/parity
    /// </summary>
    public class DataGeneration
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

            Span<byte> hash = stackalloc byte[20];
            SHA1.HashData(MemoryMarshal.Cast<int, byte>(new ReadOnlySpan<int>(data)), hash);
            var dataHash = Convert.ToBase64String(hash);
            SHA1.HashData(MemoryMarshal.Cast<int, byte>(new ReadOnlySpan<int>(sorted)), hash);
            var sortedHash = Convert.ToBase64String(hash);

            var reproContext = $"[{size},{seed}] -> [{dataHash},{sortedHash}]";

            return (data, sorted, reproContext);
        }
    }
}