using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VxSort;
using static Test.DataGeneration;
using DataGenerator = System.Func<(int[] data, int[] sortedData, string reproContext)>;

namespace Test
{
    public class PositionCountingSortTests
    {
        static readonly int[] _pcSortSizes = new[] { 8, 16, 24, 32, 40, 48, 56, 64 };

        static readonly int[] _sixTeen = new[] { 16 };

        public class SortTestCaseData : TestCaseData
        {
            public SortTestCaseData(DataGenerator generator) : base(generator) { }
        }

        static IEnumerable<TestCaseData> PreSorted =>
            from size in _pcSortSizes
            select new SortTestCaseData(() => (Enumerable.Range(0, size).ToArray(), Enumerable.Range(0, size).ToArray(), "pre-sorted") ).SetArgDisplayNames($"S{size}");

        static IEnumerable<TestCaseData> HalfMinValue =>
            from size in _pcSortSizes
            from seed in new[] {666, 333, 999, 314159}
            select new SortTestCaseData(() => GenerateData(size, seed, int.MinValue, 0.5)).SetArgDisplayNames($"{size}/{seed}/0.5min");

        static IEnumerable<TestCaseData> HalfMaxValue =>
            from size in _pcSortSizes
            from seed in new[] {666, 333, 999, 314159}
            select new SortTestCaseData(() => GenerateData(size, seed, int.MaxValue, 0.5)).SetArgDisplayNames($"{size}/{seed}/0.5max");


        static IEnumerable<TestCaseData> ConstantSeed =>
            from size in _pcSortSizes
            from seed in new[] {666, 333, 999, 314159}
            select new SortTestCaseData(() => GenerateData(size, seed)).SetArgDisplayNames($"{size}/{seed}");

        static IEnumerable<TestCaseData> TimeSeed =>
            from size in _pcSortSizes
            let numIterations = int.Parse(Environment.GetEnvironmentVariable("NUM_CYCLES") ?? "100")
            from i in Enumerable.Range(0, numIterations)
            let seed = ((int) DateTime.Now.Ticks + i * 666) % int.MaxValue
            select new SortTestCaseData(() => GenerateData(size, seed)).SetArgDisplayNames($"{size}/R{i}");

        static IEnumerable<TestCaseData> PreSorted16 =>
            from size in _sixTeen
            select new SortTestCaseData(() => (Enumerable.Range(0, size).ToArray(), Enumerable.Range(0, size).ToArray(), "pre-sorted") ).SetArgDisplayNames($"S{size}");

        static IEnumerable<TestCaseData> HalfMinValue16 =>
            from size in _sixTeen
            from seed in new[] {666, 333, 999, 314159}
            select new SortTestCaseData(() => GenerateData(size, seed, int.MinValue, 0.5)).SetArgDisplayNames($"{size}/{seed}/0.5min");

        static IEnumerable<TestCaseData> HalfMaxValue16 =>
            from size in _sixTeen
            from seed in new[] {666, 333, 999, 314159}
            select new SortTestCaseData(() => GenerateData(size, seed, int.MaxValue, 0.5)).SetArgDisplayNames($"{size}/{seed}/0.5max");


        static IEnumerable<TestCaseData> ConstantSeed16 =>
            from size in _sixTeen
            from seed in new[] {666, 333, 999, 314159}
            select new SortTestCaseData(() => GenerateData(size, seed)).SetArgDisplayNames($"{size}/{seed}");

        static IEnumerable<TestCaseData> TimeSeed16 =>
            from size in _sixTeen
            let numIterations = int.Parse(Environment.GetEnvironmentVariable("NUM_CYCLES") ?? "100")
            from i in Enumerable.Range(0, numIterations)
            let seed = ((int) DateTime.Now.Ticks + i * 666) % int.MaxValue
            select new SortTestCaseData(() => GenerateData(size, seed)).SetArgDisplayNames($"{size}/R{i}");


        static IEnumerable<TestCaseData> AllTests =>
            PreSorted.Concat(HalfMinValue).Concat(HalfMaxValue).Concat(ConstantSeed).Concat(TimeSeed);

        static IEnumerable<TestCaseData> AllTests16 =>
            PreSorted16.Concat(HalfMinValue16).Concat(HalfMaxValue16).Concat(ConstantSeed16).Concat(TimeSeed16);
    }
}