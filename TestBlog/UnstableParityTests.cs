using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using Test;
using VxSortResearch.Unstable.AVX2.Happy;
using VxSortResearch.Unstable.AVX2.Sad;
using VxSortResearch.Unstable.Scalar;
using static Test.DataGeneration;
using DataGenerator = System.Func<(int[] data, int[] sortedData, string reproContext)>;

namespace TestBlog
{
    public class SortTestCaseData : TestCaseData
    {
        public SortTestCaseData(DataGenerator generator) : base(generator) { }
    }

    public class SortTestGenerator<T> where T : unmanaged
    {
        public SortTestGenerator(System.Func<(T[] data, T[] sortedData, string reproContext)> generator)
        {
            Generator = generator;
        }

        public Func<(T[] data, T[] sortedData, string reproContext)> Generator { get; set; }
    }

    public class GSortTestCaseData<T> : TestCaseData where T : unmanaged
    {
        public GSortTestCaseData(SortTestGenerator<T> generator) : base(generator) { }
    }

    [Parallelizable(ParallelScope.All)]
    public class UnstableParityTests
    {
        static int NumCycles => int.Parse(Environment.GetEnvironmentVariable("NUM_CYCLES") ?? "10");

        static readonly int[] ArraySizes = { 10, 129, 152, 100, 1_000, 10_000, 100_000, 1_000_000 };

        static readonly int[] ConstantSeeds = { 666, 333, 999, 314159 };

        static IEnumerable<TestCaseData> PreSorted =>
            from size in ArraySizes
            from i in Enumerable.Range(0, NumCycles)
            let realSize = size + i
            select new SortTestCaseData(() => (Enumerable.Range(0, realSize).ToArray(), Enumerable.Range(0, realSize).ToArray(), "pre-sorted") ).SetArgDisplayNames($"S{realSize:0000000}");

        static IEnumerable<TestCaseData> ReverseSorted =>
            from size in ArraySizes
            from i in Enumerable.Range(0, NumCycles)
            let realSize = size + i
            select new SortTestCaseData(() => (Enumerable.Range(0, realSize).Reverse().ToArray(), Enumerable.Range(0, realSize).ToArray(), "reverse-sorted") ).SetArgDisplayNames($"Ƨ{realSize:0000000}");

        static IEnumerable<TestCaseData> HalfMinValue =>
            from size in ArraySizes
            from seed in ConstantSeeds
            from i in Enumerable.Range(0, NumCycles)
            let realSize = size + i
            select new SortTestCaseData(() => GenerateData(realSize, seed, int.MinValue, 0.5)).SetArgDisplayNames($"{realSize:0000000}/{seed}/0.5min");

        static IEnumerable<TestCaseData> HalfMaxValue =>
            from size in ArraySizes
            from seed in ConstantSeeds
            from i in Enumerable.Range(0, NumCycles)
            let realSize = size + i
            select new SortTestCaseData(() => GenerateData(realSize, seed, int.MaxValue, 0.5)).SetArgDisplayNames($"{realSize:0000000}/{seed}/0.5max");

        static IEnumerable<TestCaseData> AllOnes =>
            from size in ArraySizes
            from i in Enumerable.Range(0, NumCycles)
            let realSize = size + i
            select new SortTestCaseData(() => (Enumerable.Repeat(1, realSize).ToArray(), Enumerable.Repeat(1, realSize).ToArray(), "all-ones") ).SetArgDisplayNames($"1:{realSize:0000000}");

        static IEnumerable<TestCaseData> ConstantSeed =>
            from size in ArraySizes
            from seed in ConstantSeeds
            from i in Enumerable.Range(0, NumCycles)
            let realSize = size + i
            select new SortTestCaseData(() => GenerateData(realSize, seed)).SetArgDisplayNames($"{realSize:0000000}/{seed}");

        static IEnumerable<TestCaseData> TimeSeed =>
            from size in ArraySizes
            from i in Enumerable.Range(0, NumCycles)
            let realSize = size + i
            let seed = ((int) DateTime.Now.Ticks + i * 666) % int.MaxValue
            select new SortTestCaseData(() => GenerateData(realSize, seed)).SetArgDisplayNames($"{realSize:0000000}/R{i}");

        static IEnumerable<TestCaseData> AllTests =>
            PreSorted.Concat(ReverseSorted).Concat(HalfMinValue).Concat(HalfMaxValue).Concat(AllOnes).Concat(ConstantSeed).Concat(TimeSeed);

        [TestCaseSource(nameof(AllTests))]
        public void ManagedTest(DataGenerator generator)
        {
            var (randomData, sortedData, reproContext) = generator();
            Managed.Sort(randomData);
            Assert.That(randomData, Is.Ordered, reproContext);
            Assert.That(randomData, Is.EqualTo(sortedData), reproContext);
        }

        [TestCaseSource(nameof(AllTests))]
        public void UnmanagedTest(DataGenerator generator)
        {
            var (randomData, sortedData, reproContext) = generator();

            Unmanaged.Sort(randomData);

            Assert.That(randomData, Is.Ordered, reproContext);
            Assert.That(randomData, Is.EqualTo(sortedData), reproContext);
        }

        [TestCaseSource(nameof(AllTests))]
        public void DoublePumpNaiveTest(DataGenerator generator)
        {
            var (randomData, sortedData, reproContext) = generator();
            DoublePumpNaive.Sort(randomData);

            Assert.That(randomData, Is.Ordered, reproContext);
            Assert.That(randomData, Is.EqualTo(sortedData), reproContext);
        }

        [TestCaseSource(nameof(AllTests))]
        public void DoublePumpMicroOptTest(DataGenerator generator)
        {
            var (randomData, sortedData, reproContext) = generator();
            DoublePumpMicroOpt.Sort(randomData);

            Assert.That(randomData, Is.Ordered, reproContext);
            Assert.That(randomData, Is.EqualTo(sortedData), reproContext);
        }

        [TestCaseSource(nameof(AllTests))]
        public void DoublePumpAlignedTest(DataGenerator generator)
        {
            var (randomData, sortedData, reproContext) = generator();
            DoublePumpAligned.Sort(randomData);

            Assert.That(randomData, Is.Ordered, reproContext);
            Assert.That(randomData, Is.EqualTo(sortedData), reproContext);
        }

        [TestCaseSource(nameof(AllTests))]
        public void DoublePumpOverlinedTest(DataGenerator generator)
        {
            var (randomData, sortedData, reproContext) = generator();
            DoublePumpOverlined.Sort(randomData);

            Assert.That(randomData, Is.Ordered,             reproContext);
            Assert.That(randomData, Is.EqualTo(sortedData), reproContext);
        }

        [TestCaseSource(nameof(AllTests))]
        public void DoublePumpOverlinedSimplerBranchTest(DataGenerator generator)
        {
            var (randomData, sortedData, reproContext) = generator();
            DoublePumpOverlinedSimplerBranch.Sort(randomData);

            Assert.That(randomData, Is.Ordered,             reproContext);
            Assert.That(randomData, Is.EqualTo(sortedData), reproContext);
        }

        [TestCaseSource(nameof(AllTests))]
        public void DoublePumpOverlinedBranchlessTest(DataGenerator generator)
        {
            var (randomData, sortedData, reproContext) = generator();
            DoublePumpOverlinedBranchless.Sort(randomData);

            Assert.That(randomData, Is.Ordered,             reproContext);
            Assert.That(randomData, Is.EqualTo(sortedData), reproContext);
        }


        [TestCaseSource(nameof(AllTests))]
        public void DoublePumpOverlinedPCSortTest(DataGenerator generator)
        {
            var (randomData, sortedData, reproContext) = generator();
            DoublePumpOverlinedPCSort.Sort(randomData);

            Assert.That(randomData, Is.Ordered, reproContext);
            Assert.That(randomData, Is.EqualTo(sortedData), reproContext);
        }

        [TestCaseSource(nameof(AllTests))]
        public void DoublePumpOverlinedUnroll4PCSortTest(DataGenerator generator)
        {
            var (randomData, sortedData, reproContext) = generator();
            DoublePumpOverlinedUnroll4PCSort.Sort(randomData);

            Assert.That(randomData, Is.Ordered, reproContext);
            Assert.That(randomData, Is.EqualTo(sortedData), reproContext);
        }

        [TestCaseSource(nameof(AllTests))]
        public void DoublePumpOverlinedUnroll4BitonicSortTest(DataGenerator generator)
        {
            var (randomData, sortedData, reproContext) = generator();
            DoublePumpOverlinedUnroll4BitonicSort.Sort(randomData);

            Assert.That(randomData, Is.Ordered,             reproContext);
            Assert.That(randomData, Is.EqualTo(sortedData), reproContext);
        }

        [TestCaseSource(nameof(AllTests))]
        public void DoublePumpOverlinedUnroll8BitonicSortTest(DataGenerator generator)
        {
            var (randomData, sortedData, reproContext) = generator();
            DoublePumpOverlinedUnroll8BitonicSort.Sort(randomData);

            Assert.That(randomData, Is.Ordered,             reproContext);
            Assert.That(randomData, Is.EqualTo(sortedData), reproContext);
        }

        [TestCaseSource(nameof(AllTests))]
        public void DoublePumpOverlinedPackedUnroll8BitonicSortTest(DataGenerator generator)
        {
            var (randomData, sortedData, reproContext) = generator();
            DoublePumpOverlinedPackedUnroll8BitonicSort.Sort(randomData);

            Assert.That(randomData, Is.Ordered,             reproContext);
            Assert.That(randomData, Is.EqualTo(sortedData), reproContext);
        }

        [TestCaseSource(nameof(AllTests))]
        public void DoublePumpOverlinedUnroll8YoDawgBitonicSortTest(DataGenerator generator)
        {
            var (randomData, sortedData, reproContext) = generator();
            DoublePumpOverlinedUnroll8YoDawgBitonicSort.Sort(randomData);

            Assert.That(randomData, Is.Ordered,             reproContext);
            Assert.That(randomData, Is.EqualTo(sortedData), reproContext);
        }


        [TestCaseSource(nameof(AllTests))]
        public void DoublePumpOverlinedUnroll8YoDawgPrefixSumBitonicSortTest(DataGenerator generator)
        {
            var (randomData, sortedData, reproContext) = generator();
            DoublePumpOverlinedUnroll8YoDawgPrefixSumBitonicSort.Sort(randomData);

            Assert.That(randomData, Is.Ordered,             reproContext);
            Assert.That(randomData, Is.EqualTo(sortedData), reproContext);
        }


        [TestCaseSource(nameof(AllTests))]
        public void DoublePumpOverlinedUnroll8HalfDawgBitonicSortTest(DataGenerator generator)
        {
            var (randomData, sortedData, reproContext) = generator();
            DoublePumpOverlinedUnroll8HalfDawgBitonicSort.Sort(randomData);

            Assert.That(randomData, Is.Ordered,             reproContext);
            Assert.That(randomData, Is.EqualTo(sortedData), reproContext);
        }
    }
}
