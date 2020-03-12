using System.Collections.Generic;
using System.Linq;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using NUnit.Framework;
using Test;
using VxSortResearch.Unstable.SmallSort;

namespace TestBlog
{
    public class GenericBitonicSortTests
    {
        static IEnumerable<TestCaseData> IntTests =>
            from size in new int[] { 8 }
            from seed in new[] {666, 333, 999, 314159}
            select new GSortTestCaseData<int>(new SortTestGenerator<int>(() => DataGeneration.GGenerateData<int>(size, seed))).SetArgDisplayNames($"int/{size:000}/{seed}");

        static IEnumerable<TestCaseData> UIntTests =>
            from size in new int[] { 8 }
            from seed in new[] {666, 333, 999, 314159}
            select new GSortTestCaseData<uint>(new SortTestGenerator<uint>(() => DataGeneration.GGenerateData<uint>(size, seed))).SetArgDisplayNames($"uint/{size:000}/{seed}");

        static IEnumerable<TestCaseData> FloatTests =>
            from size in new int[] { 8 }
            from seed in new[] {666, 333, 999, 314159}
            select new GSortTestCaseData<float>(new SortTestGenerator<float>(() => DataGeneration.GGenerateData<float>(size, seed))).SetArgDisplayNames($"float/{size:000}/{seed}");
        [TestCaseSourceGeneric(nameof(IntTests),   TypeArguments = new[] { typeof(int)})]
        [TestCaseSourceGeneric(nameof(UIntTests),  TypeArguments = new[] { typeof(uint)})]
        [TestCaseSourceGeneric(nameof(FloatTests), TypeArguments = new[] { typeof(float)})]

        public unsafe void GenericBitonicSortTest<T>(SortTestGenerator<T> dg) where T : unmanaged
        {
            var (randomData, sortedData, reproContext) = dg.Generator();

            fixed (T* p = &randomData[0]) {
                var wat = (void*) p;
                var v = Avx2.LoadDquVector256((byte *)wat).As<byte, T>();
                BitonicSort<T>.BitonicSort01VGeneric(ref v);
                Avx.Store((byte *)wat, v.AsByte());
            }

            Assert.That(randomData, Is.Ordered,             reproContext);
            Assert.That(randomData, Is.EqualTo(sortedData), reproContext);
        }
    }
}