using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VxSort;
using static Test.DataGeneration;
using DataGenerator = System.Func<(int[] data, int[] sortedData, string reproContext)>;

namespace Test
{
    public class BitonicSortTests
    {
        static readonly int[] BitonicSizes = { 8, 16, 24, 32, 40, 48, 56, 64, 72, 80, 88, 96, 104, 112, 120, 128 };

        static IEnumerable<TestCaseData> PreSorted =>
            from size in BitonicSizes
            select new ParityTests.SortTestCaseData(() => (Enumerable.Range(0, size).ToArray(), Enumerable.Range(0, size).ToArray(), "pre-sorted") ).SetArgDisplayNames($"{size:000}/S");

        static IEnumerable<TestCaseData> ReverseSorted =>
            from size in BitonicSizes
            select new ParityTests.SortTestCaseData(() => (Enumerable.Range(0, size).Reverse().ToArray(), Enumerable.Range(0, size).ToArray(), "reverse-sorted") ).SetArgDisplayNames($"Ƨ{size:0000000}");

        static IEnumerable<TestCaseData> HalfMinValue =>
            from size in BitonicSizes
            from seed in new[] {666, 333, 999, 314159}
            select new ParityTests.SortTestCaseData(() => GenerateData(size, seed, int.MinValue, 0.5)).SetArgDisplayNames($"{size:000}/{seed}/0.5min");

        static IEnumerable<TestCaseData> HalfMaxValue =>
            from size in BitonicSizes
            from seed in new[] {666, 333, 999, 314159}
            select new ParityTests.SortTestCaseData(() => GenerateData(size, seed, int.MaxValue, 0.5)).SetArgDisplayNames($"{size:000}/{seed}/0.5max");

        static IEnumerable<TestCaseData> AllOnes =>
            from size in BitonicSizes
            select new ParityTests.SortTestCaseData(() => (Enumerable.Repeat(1, size).ToArray(), Enumerable.Repeat(1, size).ToArray(), "all-ones") ).SetArgDisplayNames($"1:{size:0000000}");


        static IEnumerable<TestCaseData> ConstantSeed =>
            from size in BitonicSizes
            from seed in new[] {666, 333, 999, 314159}
            select new ParityTests.SortTestCaseData(() => GenerateData(size, seed, modulo: 100)).SetArgDisplayNames($"{size:000}/{seed}");

        static IEnumerable<TestCaseData> TimeSeed =>
            from size in BitonicSizes
            let numIterations = int.Parse(Environment.GetEnvironmentVariable("NUM_CYCLES") ?? "100")
            from i in Enumerable.Range(0, numIterations)
            let seed = ((int) DateTime.Now.Ticks + i * 666) % int.MaxValue
            select new ParityTests.SortTestCaseData(() => GenerateData(size, seed)).SetArgDisplayNames($"{size:000}/R{i}");

        [TestCaseSource(nameof(PreSorted))]
        [TestCaseSource(nameof(HalfMinValue))]
        [TestCaseSource(nameof(HalfMaxValue))]
        [TestCaseSource(nameof(ConstantSeed))]
        [TestCaseSource(nameof(TimeSeed))]
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