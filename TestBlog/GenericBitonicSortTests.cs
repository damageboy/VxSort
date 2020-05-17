using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using NUnit.Framework;
using Test;
using VxSortResearch.Unstable.SmallSort;

using static System.Runtime.Intrinsics.X86.Avx;
using static System.Runtime.Intrinsics.X86.Avx2;

namespace TestBlog
{

    [TestFixture(typeof(int))]
    [TestFixture(typeof(uint))]
    [TestFixture(typeof(float))]

    [TestFixture(typeof(long))]
    [TestFixture(typeof(ulong))]
    [TestFixture(typeof(double))]
    public class GenericBitonicSortTests<T> where T : unmanaged
    {
        static IEnumerable<int> BitonicSizes => Enumerable.Range(1, 16).Select(x => x * Vector256<T>.Count);

        static IEnumerable<TestCaseData> ConstantSeed =>
            from size in BitonicSizes
            from seed in new[] {666, 333, 999, 314159}
            select new GSortTestCaseData<T>(new SortTestGenerator<T>(() => DataGeneration.GenerateData<T>(size, seed))).SetArgDisplayNames($"{typeof(T)}/{size:000}/{seed}");

        static IEnumerable<TestCaseData> TimeSeed =>
            from size in BitonicSizes
            let numIterations = int.Parse(Environment.GetEnvironmentVariable("NUM_CYCLES") ?? "100", CultureInfo.InvariantCulture)
            from i in Enumerable.Range(0, numIterations)
            let seed = ((int) DateTime.Now.Ticks + i * 666) % int.MaxValue
            select new GSortTestCaseData<T>(new SortTestGenerator<T>(() => DataGeneration.GenerateData<T>(size, seed))).SetArgDisplayNames($"{typeof(T)}/{size:000}/R{i}");
        
        static IEnumerable<TestCaseData> AllTests =>
            ConstantSeed.Concat(TimeSeed);
            //PreSorted.Concat(HalfMinValue).Concat(HalfMaxValue).Concat(ConstantSeed).Concat(TimeSeed);

        
        [TestCaseSource(nameof(AllTests))]
        public unsafe void GenericBitonicSortTest(SortTestGenerator<T> dg)
        {
            var (randomData, sortedData, reproContext) = dg.Generator();

            fixed (T* p = &randomData[0]) {
                GenericBitonicSort<T>.Sort(p, randomData.Length);
            }

            Assert.That(randomData, Is.Ordered,             reproContext);
            Assert.That(randomData, Is.EqualTo(sortedData), reproContext);
        }

        [TestCaseSource(nameof(AllTests))]
        public unsafe void T4GeneratedBitonicSortTest(SortTestGenerator<T> dg)
        {
            var (randomData, sortedData, reproContext) = dg.Generator();

            fixed (T* p = &randomData[0]) {
                T4GeneratedBitonicSort<T>.Sort(p, randomData.Length);
            }

            Assert.That(randomData, Is.Ordered,             reproContext);
            Assert.That(randomData, Is.EqualTo(sortedData), reproContext);
        }
        

        [TestCaseSource(nameof(AllTests))]
        public unsafe void T4GeneratedBitonicSortOptTest(SortTestGenerator<T> dg)
        {
            var (randomData, sortedData, reproContext) = dg.Generator();

            fixed (T* p = &randomData[0]) {
                T4GeneratedBitonicSortOpt<T>.Sort(p, randomData.Length);
            }

            Assert.That(randomData, Is.Ordered,             reproContext);
            Assert.That(randomData, Is.EqualTo(sortedData), reproContext);
        }
    }

    public class BasicBitonicOpsTests
    {
                [Test]
        [TestCase(new[] { 100L, 101L, 100L, 101L }, new[] { 100L, 101L, 100L, 101L })]
        [TestCase(new[] { 100L, 101L, 100L, 101L }, new[] { 1L, 1L, 1L, 1L })]
        [TestCase(new[] { 1L, 1L, 1L, 1L }, new[] { 100L, 101L, 100L, 101L })]
        public void AVX2MinMaxInt64BitWorks(long[] set1, long[] set2)
        {
            var v1 = Vector256.Create(set1[0], set1[1], set1[2], set1[3]);
            var v2 = Vector256.Create(set2[0], set2[1], set2[2], set2[3]);

            var min = GenericBitonicSort<long>.MinV(v1, v2);
            var max = GenericBitonicSort<long>.MaxV(v1, v2);

            Assert.That(min.GetElement(0), Is.EqualTo(Math.Min(set1[0], set2[0])));
            Assert.That(min.GetElement(1), Is.EqualTo(Math.Min(set1[1], set2[1])));
            Assert.That(min.GetElement(2), Is.EqualTo(Math.Min(set1[2], set2[2])));
            Assert.That(min.GetElement(3), Is.EqualTo(Math.Min(set1[3], set2[3])));

            Assert.That(max.GetElement(0), Is.EqualTo(Math.Max(set1[0], set2[0])));
            Assert.That(max.GetElement(1), Is.EqualTo(Math.Max(set1[1], set2[1])));
            Assert.That(max.GetElement(2), Is.EqualTo(Math.Max(set1[2], set2[2])));
            Assert.That(max.GetElement(3), Is.EqualTo(Math.Max(set1[3], set2[3])));
        }


        [Test]
        [TestCase(new[] { 100UL, 101UL, 100UL, 101UL }, new[] { 100UL, 101UL, 100UL, 101UL })]
        [TestCase(new[] { 100UL, 101UL, 100UL, 101UL }, new[] { 1UL, 1UL, 1UL, 1UL })]
        [TestCase(new[] { 1UL, 1UL, 1UL, 1UL }, new[] { 100UL, 101UL, 100UL, 101UL })]
        [TestCase(new[] { 0x8000000000000100UL, 101UL, 100UL, 101UL }, new[] { 1UL, 1UL, 1UL, 1UL })]
        [TestCase(new[] { 1UL, 1UL, 1UL, 1UL }, new[] { 0x8000000000000100UL, 101UL, 100UL, 101UL })]
        public void AVX2MinMaxUInt64BitWorks(ulong[] set1, ulong[] set2)
        {
            var v1 = Vector256.Create(set1[0], set1[1], set1[2], set1[3]);
            var v2 = Vector256.Create(set2[0], set2[1], set2[2], set2[3]);

            var min = GenericBitonicSort<ulong>.MinV(v1, v2);
            var max = GenericBitonicSort<ulong>.MaxV(v1, v2);

            Assert.That(min.GetElement(0), Is.EqualTo(Math.Min(set1[0], set2[0])));
            Assert.That(min.GetElement(1), Is.EqualTo(Math.Min(set1[1], set2[1])));
            Assert.That(min.GetElement(2), Is.EqualTo(Math.Min(set1[2], set2[2])));
            Assert.That(min.GetElement(3), Is.EqualTo(Math.Min(set1[3], set2[3])));

            Assert.That(max.GetElement(0), Is.EqualTo(Math.Max(set1[0], set2[0])));
            Assert.That(max.GetElement(1), Is.EqualTo(Math.Max(set1[1], set2[1])));
            Assert.That(max.GetElement(2), Is.EqualTo(Math.Max(set1[2], set2[2])));
            Assert.That(max.GetElement(3), Is.EqualTo(Math.Max(set1[3], set2[3])));
        }
    }
}