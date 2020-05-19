using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VxSortResearch.Unstable.AVX2.Happy;
using VxSortResearch.Unstable.Scalar;
using DataGenerator = System.Func<(int[] data, int[] sortedData, string reproContext)>;

namespace TestBlog.Generic
{
    [Parallelizable(ParallelScope.All)]
    [TestFixture(typeof(int))]
    //[TestFixture(typeof(uint))]
    //[TestFixture(typeof(float))]

    [TestFixture(typeof(long))]
    //[TestFixture(typeof(ulong))]
    //[TestFixture(typeof(double))]

    public class GenericUnstableParityTests<T> where T : unmanaged, IComparable<T>
    {
        static int NumCycles => int.Parse(Environment.GetEnvironmentVariable("NUM_CYCLES") ?? "10");

        static readonly int[] ArraySizes = { 10, 129, 152, 100, 1_000, 10_000, 100_000, 1_000_000 };

        static readonly int[] ConstantSeeds = { 666, 333, 999, 314159 };

        static IEnumerable<TestCaseData> ConstantSeed =>
            from size in ArraySizes
            from seed in ConstantSeeds
            from i in Enumerable.Range(0, NumCycles)
            let realSize = size + i
            select new GSortTestCaseData<T>(new SortTestGenerator<T>(() => DataGeneration.GenerateData<T>(size, seed))).SetArgDisplayNames($"{typeof(T)}/{size:000}/{seed}");

        static IEnumerable<TestCaseData> TimeSeed =>
            from size in ArraySizes
            from i in Enumerable.Range(0, NumCycles)
            let realSize = size + i
            let seed = ((int) DateTime.Now.Ticks + i * 666) % int.MaxValue
            select new GSortTestCaseData<T>(new SortTestGenerator<T>(() => DataGeneration.GenerateData<T>(size, seed))).SetArgDisplayNames($"{typeof(T)}/{size:000}/R{i}");

        static IEnumerable<TestCaseData> AllTests =>
            ConstantSeed.Concat(TimeSeed);    
            //PreSorted.Concat(ReverseSorted).Concat(HalfMinValue).Concat(HalfMaxValue).Concat(AllOnes).Concat(ConstantSeed).Concat(TimeSeed);

        [TestCaseSource(nameof(AllTests))]
        public void ManagedTest(SortTestGenerator<T> dg)
        {
            var (randomData, sortedData, reproContext) = dg.Generator();
            Managed.Sort(randomData);
            Assert.That(randomData, Is.Ordered, reproContext);
            Assert.That(randomData, Is.EqualTo(sortedData), reproContext);
        }

        [TestCaseSource(nameof(AllTests))]
        public void UnmanagedTest(SortTestGenerator<T> dg)
        {
            var (randomData, sortedData, reproContext) = dg.Generator();

            Unmanaged.Sort(randomData);

            Assert.That(randomData, Is.Ordered, reproContext);
            Assert.That(randomData, Is.EqualTo(sortedData), reproContext);
        }

        [TestCaseSource(nameof(AllTests))]
        public void GenericDoublePumpJediTest(SortTestGenerator<T> dg)
        {
            var (randomData, sortedData, reproContext) = dg.Generator();
            DoublePumpJedi.Sort(randomData);

            Assert.That(randomData, Is.Ordered,             reproContext);
            Assert.That(randomData, Is.EqualTo(sortedData), reproContext);
        }
        

        [TestCaseSource(nameof(AllTests))]
        public void DoublePumpOverlinedTest(SortTestGenerator<T> dg)
        {
            var (randomData, sortedData, reproContext) = dg.Generator();
            DoublePumpOverlined.Sort(randomData);

            Assert.That(randomData, Is.Ordered,             reproContext);
            Assert.That(randomData, Is.EqualTo(sortedData), reproContext);
        }        
    }
}
