using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Bench.Utils;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Reports;
using Perfolizer.Horology;
using VxSortResearch.Stable.SmallSort;
using VxSortResearch.Unstable.SmallSort;

namespace Bench
{
    class SmallSortConfig : ManualConfig
    {
        public SmallSortConfig()
        {
            SummaryStyle = new SummaryStyle(CultureInfo.InvariantCulture, true, SizeUnit.B, TimeUnit.Nanosecond);
            AddColumn(new TimePerNColumn());
            AddDiagnoser(
                new DisassemblyDiagnoser(
                    new DisassemblyDiagnoserConfig(
                        maxDepth: 4, // you can change it to a bigger value if you want to get more framework methods disassembled
                        exportGithubMarkdown: true)));
        }
    }

    public class SmallSortBenchBase<T> where T : unmanaged, IComparable<T>
    {
        const ulong CACHELINE_SIZE = 64;
        protected virtual int InvocationsPerIteration { get; }

        protected int _iterationIndex = 0;
        T[] _originalValues;
        T[][] _arrays;
        GCHandle[] _gcHandles;
        protected unsafe T*[] _arrayPtrs;


        protected virtual int ArraySize { get; }

        protected unsafe T* _tmp;

        [GlobalSetup]
        public unsafe void Setup()
        {
            _tmp = (T*) Marshal.AllocHGlobal(sizeof(T) * 2 * ArraySize);
            var rolledUpArraySize = ArraySize + (int) (CACHELINE_SIZE / (ulong) sizeof(T));
            _originalValues = ValuesGenerator.ArrayOfUniqueValues<T>(rolledUpArraySize);
            _arrays = Enumerable.Range(0, InvocationsPerIteration).Select(_ => new T[rolledUpArraySize])
                .ToArray();
            _gcHandles = _arrays.Select(a => GCHandle.Alloc(a, GCHandleType.Pinned)).ToArray();
            _arrayPtrs = new T*[InvocationsPerIteration];
            for (var i = 0; i < InvocationsPerIteration; i++) {
                var p = (T*) _gcHandles[i].AddrOfPinnedObject();
                if (((ulong) p) % CACHELINE_SIZE != 0)
                    p = (T*) ((((ulong) p) + CACHELINE_SIZE) & ~(CACHELINE_SIZE - 1));

                _arrayPtrs[i] = p;
            }
        }

        [IterationCleanup]
        public void CleanupIteration() => _iterationIndex = 0; // after every iteration end we set the index to 0

        [IterationSetup]
        public void SetupArrayIteration() =>
            ValuesGenerator.FillArrays(ref _arrays, InvocationsPerIteration, _originalValues);
    }


    [GenericTypeArguments(typeof(int))]   // value type
    [InvocationCount(InvocationsPerIterationValue)]
    [Config(typeof(SmallSortConfig))]
    public class Int32OnlySmallSortBench<T> : SmallSortBenchBase<T> where T : unmanaged, IComparable<T>
    {
        const int InvocationsPerIterationValue = 4096;
        protected override int InvocationsPerIteration => InvocationsPerIterationValue;
        protected override int ArraySize => N;

        [ParamsSource(nameof(BitonicSizes))]
        public int N;

        public IEnumerable<int> BitonicSizes => Enumerable.Range(1, 16).Select(x => x * Vector256<T>.Count);

        //[Benchmark(Baseline = true)]
        public unsafe void PCSort() => PositionCountingSort.Sort<T>((int *) _arrayPtrs[_iterationIndex++], N, (int *) _tmp);

        [Benchmark]
        public unsafe void BitonicSort() => BitonicSort<T>.Sort((int *) _arrayPtrs[_iterationIndex++], N);
        
        [Benchmark]
        public unsafe void BitonicSortOpt() => BitonicSortOpt<T>.Sort((int *) _arrayPtrs[_iterationIndex++], N);
        
    }

    [GenericTypeArguments(typeof(int))] // value type
    [GenericTypeArguments(typeof(uint))] // value type
    [GenericTypeArguments(typeof(float))] // value type
    [GenericTypeArguments(typeof(long))]   // value type
    [GenericTypeArguments(typeof(ulong))]  // value type
    [GenericTypeArguments(typeof(double))] // value type
    [InvocationCount(InvocationsPerIterationValue)]
    [Config(typeof(SmallSortConfig))]
    public class GenericSmallSortBench<T> : SmallSortBenchBase<T> where T : unmanaged, IComparable<T>
    {
        const int InvocationsPerIterationValue = 4096;
        protected override int InvocationsPerIteration => InvocationsPerIterationValue;
        protected override int ArraySize => N;

        [ParamsSource(nameof(BitonicSizes))]
        public int N;

        public IEnumerable<int> BitonicSizes => Enumerable.Range(1, 16).Select(x => x * Vector256<T>.Count);

        //[Benchmark(Baseline = true)]
        public unsafe void PCSort() => PositionCountingSort.Sort<T>((int *) _arrayPtrs[_iterationIndex++], N, (int *) _tmp);

        //[Benchmark]
        public unsafe void GenericBitonicSort() => GenericBitonicSort<T>.Sort(_arrayPtrs[_iterationIndex++], N);

        [Benchmark(Baseline = true)]
        public unsafe void T4GeneratedBitonicSort() => T4GeneratedBitonicSort<T>.Sort(_arrayPtrs[_iterationIndex++], N);

        [Benchmark]
        public unsafe void T4GeneratedBitonicSortOpt() => T4GeneratedBitonicSortOpt<T>.Sort(_arrayPtrs[_iterationIndex++], N);

    }
}
