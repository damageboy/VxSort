using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Intrinsics.X86;
using NUnit.Framework;
using VxSortResearch.Unstable.SmallSort;
using static Test.DataGeneration;
using DataGenerator = System.Func<(int[] data, int[] sortedData, string reproContext)>;

namespace TestBlog
{
    public class BitonicSortTests<T> where T : unmanaged
    {
        static readonly int[] _bitonicSizes = {8, 16, 24, 32, 40, 48, 56, 64, 72, 80, 88, 96, 104, 112, 120, 128};

        static IEnumerable<TestCaseData> PreSorted =>
            from size in _bitonicSizes
            select new SortTestCaseData(() => (Enumerable.Range(0, size).ToArray(), Enumerable.Range(0, size).ToArray(), "pre-sorted") ).SetArgDisplayNames($"{size:000}/S");

        static IEnumerable<TestCaseData> HalfMinValue =>
            from size in _bitonicSizes
            from seed in new[] {666, 333, 999, 314159}
            select new SortTestCaseData(() => GenerateData(size, seed, int.MinValue, 0.5)).SetArgDisplayNames($"{size:000}/{seed}/0.5min");

        static IEnumerable<TestCaseData> HalfMaxValue =>
            from size in _bitonicSizes
            from seed in new[] {666, 333, 999, 314159}
            select new SortTestCaseData(() => GenerateData(size, seed, int.MaxValue, 0.5)).SetArgDisplayNames($"{size:000}/{seed}/0.5max");


        static IEnumerable<TestCaseData> ConstantSeed =>
            from size in _bitonicSizes
            from seed in new[] {666, 333, 999, 314159}
            select new SortTestCaseData(() => GenerateData(size, seed, modulo: 100)).SetArgDisplayNames($"{size:000}/{seed}");

        static IEnumerable<TestCaseData> TimeSeed =>
            from size in _bitonicSizes
            let numIterations = int.Parse(Environment.GetEnvironmentVariable("NUM_CYCLES") ?? "100", CultureInfo.InvariantCulture)
            from i in Enumerable.Range(0, numIterations)
            let seed = ((int) DateTime.Now.Ticks + i * 666) % int.MaxValue
            select new SortTestCaseData(() => GenerateData(size, seed)).SetArgDisplayNames($"{size:000}/R{i}");


        static IEnumerable<TestCaseData> AllTests =>
            PreSorted.Concat(HalfMinValue).Concat(HalfMaxValue).Concat(ConstantSeed).Concat(TimeSeed);

        [TestCaseSource(nameof(AllTests))]
        public unsafe void BitonicSortTest(DataGenerator generator)
        {
            var (randomData, sortedData, reproContext) = generator();

            fixed (int* p = &randomData[0]) {
                BitonicSort<int>.Sort(p, randomData.Length);
            }

            Assert.That(randomData, Is.Ordered,             reproContext);
            Assert.That(randomData, Is.EqualTo(sortedData), reproContext);
        }
    }
}
