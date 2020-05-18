using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Intrinsics.X86;
using NUnit.Framework;
using VxSortResearch.Utils;
using static TestBlog.DataGeneration;
using DataGenerator = System.Func<(int[] data, int[] sortedData, string reproContext)>;

namespace TestBlog
{

    public class SeriesTestCaseData : TestCaseData
    {
        public SeriesTestCaseData(DataGenerator generator) : base(generator) { }
    }

    public class PrefixSumTests
    {
        static readonly int[] _prefixSumSizes = new[] { 8  };

        static IEnumerable<TestCaseData> ConstantSeed =>
            from size in _prefixSumSizes
            from seed in new[] {666, 333, 999, 314159}
            select new SeriesTestCaseData(() => GenerateData(size, seed, modulo: 256)).SetArgDisplayNames($"{size}/{seed}");

        static IEnumerable<TestCaseData> TimeSeed =>
            from size in _prefixSumSizes
            let numIterations = int.Parse(Environment.GetEnvironmentVariable("NUM_CYCLES") ?? "100", CultureInfo.InvariantCulture)
            from i in Enumerable.Range(0, numIterations)
            let seed = ((int) DateTime.Now.Ticks + i * 666) % int.MaxValue
            select new SeriesTestCaseData(() => GenerateData(size, seed)).SetArgDisplayNames($"{size}/R{i}");

        static IEnumerable<TestCaseData> AllTests =>
            ConstantSeed.Concat(TimeSeed);



        [TestCaseSource(nameof(AllTests))]
        public unsafe void ScalarPrefixSum(DataGenerator generator)
        {
            var (input, _, reproContext) = generator();

            var result = new int[input.Length];
            Array.Copy(input, result, input.Length);

            fixed (int* pResult = &result[0]) {
                PrefixSum<int>.SumScalar(pResult);
            }

            var expectedResult = new int[input.Length];
            expectedResult[0] = input[0];
            for (var i = 1; i < input.Length; i++) {
                expectedResult[i] = expectedResult[i-1] + input[i];
            }

            Assert.That(result, Is.EqualTo(expectedResult));
        }

        [TestCaseSource(nameof(AllTests))]
        public unsafe void VectorizedPrefixSum(DataGenerator generator)
        {
            var (input, _, reproContext) = generator();

            var result = new int[input.Length];

            fixed (int* pInput = &input[0])
            fixed (int* pResult = &result[0]) {
                Avx.Store(pResult, PrefixSum<int>.SumVectorized(Avx2.LoadDquVector256(pInput)));
            }

            var expectedResult = new int[input.Length];
            expectedResult[0] = input[0];
            for (var i = 1; i < input.Length; i++) {
                expectedResult[i] = expectedResult[i-1] + input[i];
            }

            Assert.That(result, Is.EqualTo(expectedResult));
        }

        [TestCaseSource(nameof(AllTests))]
        public unsafe void VectorizedPrefixSumFromPointer(DataGenerator generator)
        {
            var (input, _, reproContext) = generator();

            var result = new int[input.Length];
            Array.Copy(input, result, input.Length);

            fixed (int* pResult = &result[0]) {
                PrefixSum<int>.SumVectorized(pResult);
            }

            var expectedResult = new int[input.Length];
            expectedResult[0] = input[0];
            for (var i = 1; i < input.Length; i++) {
                expectedResult[i] = expectedResult[i-1] + input[i];
            }
            Assert.That(result, Is.EqualTo(expectedResult));
        }
    }
}
