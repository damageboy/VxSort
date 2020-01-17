using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VxSort;
using static Test.DataGeneration;
using DataGenerator = System.Func<(int[] data, int[] sortedData, string reproContext)>;

namespace Test
{
    [Parallelizable(ParallelScope.All)]
    public partial class ParityTests
    {
        public class SortTestCaseData : TestCaseData
        {
            public SortTestCaseData(DataGenerator generator) : base(generator) { }
        }

        static IEnumerable<TestCaseData> PreSorted =>
            from size in new[] {10, 100, 1_000, 10_000, 100_000, 1_000_000 }
            select new SortTestCaseData(() => (Enumerable.Range(0, size).ToArray(), Enumerable.Range(0, size).ToArray(), "pre-sorted") ).SetArgDisplayNames($"S{size:0000000}");

        static IEnumerable<TestCaseData> HalfMinValue =>
            from size in new[] {10, 100, 1_000, 10_000 }
            from seed in new[] {666, 333, 999, 314159}
            select new SortTestCaseData(() => GenerateData(size, seed, int.MinValue, 0.5)).SetArgDisplayNames($"{size:0000000}/{seed}/0.5min");

        static IEnumerable<TestCaseData> HalfMaxValue =>
            from size in new[] {10, 100, 1_000, 10_000 }
            from seed in new[] {666, 333, 999, 314159}
            select new SortTestCaseData(() => GenerateData(size, seed, int.MaxValue, 0.5)).SetArgDisplayNames($"{size:0000000}/{seed}/0.5max");


        static IEnumerable<TestCaseData> ConstantSeed =>
            from size in new[] {10, 100, 1_000, 10_000, 100_000, 1_000_000 }
            from seed in new[] {666, 333, 999, 314159}
            select new SortTestCaseData(() => GenerateData(size, seed)).SetArgDisplayNames($"{size:0000000}/{seed}");

        static IEnumerable<TestCaseData> TimeSeed =>
            from size in new[] {10, 100, 1_000, 10_000, 100_000, 1_000_000}
            let numIterations = int.Parse(Environment.GetEnvironmentVariable("NUM_CYCLES") ?? "100")
            from i in Enumerable.Range(0, numIterations)
            let seed = ((int) DateTime.Now.Ticks + i * 666) % int.MaxValue
            select new SortTestCaseData(() => GenerateData(size + i, seed)).SetArgDisplayNames($"{size + i:0000000}/R{i}");

        static IEnumerable<TestCaseData> AllTests =>
            PreSorted.Concat(HalfMinValue).Concat(HalfMaxValue).Concat(ConstantSeed).Concat(TimeSeed);


        [TestCaseSource(nameof(AllTests))]
        public void VxSortPrimitveUnstable(DataGenerator generator)
        {
            var (randomData, sortedData, reproContext) = generator();
            VectorizedSort.UnstableSort(randomData);

            Assert.That(randomData, Is.Ordered,             reproContext);
            Assert.That(randomData, Is.EqualTo(sortedData), reproContext);
        }
    }
}
