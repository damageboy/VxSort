using System;
using System.Collections.Generic;
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
    public class GenericBitonicSortTests
    {
        static readonly int[] _bitonic32bSizes = {8, 16, 24, 32, 40, 48, 56, 64, 72, 80, 88, 96, 104, 112, 120, 128};
        static readonly int[] _bitonic64bSizes = {4,  8, 12, 16, 20, 24, 28, 32, 36, 40, 44, 48,  52,  56,  60,  64};
        static readonly Type[] _bitonicTypes = { typeof(int), typeof(uint), typeof(float) };
        
        static IEnumerable<TestCaseData> IntTests =>
            from size in _bitonic32bSizes
            from seed in new[] {666, 333, 999, 314159}
            select new GSortTestCaseData<int>(new SortTestGenerator<int>(() => DataGeneration.GenerateData<int>(size, seed))).SetArgDisplayNames($"int/{size:000}/{seed}");

        static IEnumerable<TestCaseData> UIntTests =>
            from size in _bitonic32bSizes
            from seed in new[] {666, 333, 999, 314159}
            select new GSortTestCaseData<uint>(new SortTestGenerator<uint>(() => DataGeneration.GenerateData<uint>(size, seed))).SetArgDisplayNames($"uint/{size:000}/{seed}");

        static IEnumerable<TestCaseData> FloatTests =>
            from size in _bitonic32bSizes
            from seed in new[] {666, 333, 999, 314159}
            select new GSortTestCaseData<float>(new SortTestGenerator<float>(() => DataGeneration.GenerateData<float>(size, seed))).SetArgDisplayNames($"float/{size:000}/{seed}");

        static IEnumerable<TestCaseData> DoubleTests =>
            from size in _bitonic64bSizes
            from seed in new[] {666, 333, 999, 314159}
            select new GSortTestCaseData<double>(new SortTestGenerator<double>(() => DataGeneration.GenerateData<double>(size, seed))).SetArgDisplayNames($"double/{size:000}/{seed}");

        static IEnumerable<TestCaseData> LongTests =>
            from size in _bitonic64bSizes
            from seed in new[] {666, 333, 999, 314159}
            select new GSortTestCaseData<long>(new SortTestGenerator<long>(() => DataGeneration.GenerateData<long>(size, seed))).SetArgDisplayNames($"long/{size:000}/{seed}");

        static IEnumerable<TestCaseData> ULongTests =>
            from size in _bitonic64bSizes
            from seed in new[] {666, 333, 999, 314159}
            select new GSortTestCaseData<ulong>(new SortTestGenerator<ulong>(() => DataGeneration.GenerateData<ulong>(size, seed))).SetArgDisplayNames($"ulong/{size:000}/{seed}");



        [TestCaseSourceGeneric(nameof(IntTests),    TypeArguments = new[] { typeof(int)}   )]
        [TestCaseSourceGeneric(nameof(UIntTests),   TypeArguments = new[] { typeof(uint)}  )]
        [TestCaseSourceGeneric(nameof(FloatTests),  TypeArguments = new[] { typeof(float)} )]
        [TestCaseSourceGeneric(nameof(DoubleTests), TypeArguments = new[] { typeof(double)})]
        [TestCaseSourceGeneric(nameof(LongTests),   TypeArguments = new[] { typeof(long)}  )]
        [TestCaseSourceGeneric(nameof(ULongTests),  TypeArguments = new[] { typeof(ulong)} )]
        public unsafe void GenericBitonicSortTest<T>(SortTestGenerator<T> dg) where T : unmanaged
        {
            var (randomData, sortedData, reproContext) = dg.Generator();

            fixed (T* p = &randomData[0]) {
                GenericBitonicSort<T>.Sort(p, randomData.Length);
            }

            Assert.That(randomData, Is.Ordered,             reproContext);
            Assert.That(randomData, Is.EqualTo(sortedData), reproContext);
        }

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