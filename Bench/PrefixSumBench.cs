using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using Bench.Utils;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Horology;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using VxSortResearch.Utils;

namespace Bench
{
    class PrefixSumConfig : ManualConfig
    {
        public PrefixSumConfig()
        {
            SummaryStyle = new SummaryStyle(true, SizeUnit.GB, TimeUnit.Nanosecond);
            Add(Job.LongRun); //);.With(InProcessEmitToolchain.Instance));
        }
    }

    public class PrefixSumBenchBase<T> where T : unmanaged, IComparable<T>
    {
        const ulong CACHELINE_SIZE = 64;
        protected virtual int InvocationsPerIteration { get; }
        protected virtual int OperationsPerInvoke { get; }

        protected int _iterationIndex = 0;
        T[] _originalValues;
        T[][] _arrays;
        GCHandle[] _gcHandles;
        protected unsafe T*[] _arrayPtrs;


        protected virtual int ArraySize { get; }

        [GlobalSetup]
        public unsafe void Setup()
        {
            var numArrays = InvocationsPerIteration*OperationsPerInvoke;
            var rolledUpArraySize = ArraySize + (int) (CACHELINE_SIZE / (ulong) sizeof(T));
            _originalValues = ValuesGenerator.ArrayOfUniqueValues<T>(rolledUpArraySize);
            _arrays = Enumerable.Range(0, numArrays).Select(_ => new T[rolledUpArraySize])
                .ToArray();
            _gcHandles = _arrays.Select(a => GCHandle.Alloc(a, GCHandleType.Pinned)).ToArray();
            _arrayPtrs = new T*[numArrays];
            for (var i = 0; i < numArrays; i++) {
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

    [GenericTypeArguments(typeof(int))] // value type
    [InvocationCount(InvocationsPerIterationValue)]
    [Config(typeof(PrefixSumConfig))]
    public class PrefixSumBench<T> : PrefixSumBenchBase<T> where T : unmanaged, IComparable<T>
    {
        const int InvocationsPerIterationValue = 4096;
        const int OperationsPerInvokeValue = 10;
        protected override int InvocationsPerIteration => InvocationsPerIterationValue;
        protected override int OperationsPerInvoke => OperationsPerInvokeValue;
        protected override int ArraySize => N;

        [Params(8)]
        public int N;

        [Benchmark(Baseline = true, OperationsPerInvoke = OperationsPerInvokeValue)]
        public unsafe void ScalarPrefixSum()
        {
            PrefixSum<int>.SumScalar((int*) _arrayPtrs[_iterationIndex++]);
            PrefixSum<int>.SumScalar((int*) _arrayPtrs[_iterationIndex++]);
            PrefixSum<int>.SumScalar((int*) _arrayPtrs[_iterationIndex++]);
            PrefixSum<int>.SumScalar((int*) _arrayPtrs[_iterationIndex++]);
            PrefixSum<int>.SumScalar((int*) _arrayPtrs[_iterationIndex++]);
            PrefixSum<int>.SumScalar((int*) _arrayPtrs[_iterationIndex++]);
            PrefixSum<int>.SumScalar((int*) _arrayPtrs[_iterationIndex++]);
            PrefixSum<int>.SumScalar((int*) _arrayPtrs[_iterationIndex++]);
            PrefixSum<int>.SumScalar((int*) _arrayPtrs[_iterationIndex++]);
            PrefixSum<int>.SumScalar((int*) _arrayPtrs[_iterationIndex++]);
        }

        [Benchmark(OperationsPerInvoke = OperationsPerInvokeValue)]
        public unsafe void VectorizedPrefixSum()
        {
            PrefixSum<T>.SumVectorized(_arrayPtrs[_iterationIndex++]);
            PrefixSum<T>.SumVectorized(_arrayPtrs[_iterationIndex++]);
            PrefixSum<T>.SumVectorized(_arrayPtrs[_iterationIndex++]);
            PrefixSum<T>.SumVectorized(_arrayPtrs[_iterationIndex++]);
            PrefixSum<T>.SumVectorized(_arrayPtrs[_iterationIndex++]);
            PrefixSum<T>.SumVectorized(_arrayPtrs[_iterationIndex++]);
            PrefixSum<T>.SumVectorized(_arrayPtrs[_iterationIndex++]);
            PrefixSum<T>.SumVectorized(_arrayPtrs[_iterationIndex++]);
            PrefixSum<T>.SumVectorized(_arrayPtrs[_iterationIndex++]);
            PrefixSum<T>.SumVectorized(_arrayPtrs[_iterationIndex++]);
        }
        [Benchmark(OperationsPerInvoke = OperationsPerInvokeValue)]
        public unsafe void VectorizedPrefixSumNoLoad()
        {
            var p = _arrayPtrs[_iterationIndex++];
            var v = PrefixSum<T>.LoadVector256Generic(p);
            PrefixSum<T>.StoreVector256Generic(p, PrefixSum<T>.SumVectorized(v));
            PrefixSum<T>.StoreVector256Generic(p, PrefixSum<T>.SumVectorized(v));
            PrefixSum<T>.StoreVector256Generic(p, PrefixSum<T>.SumVectorized(v));
            PrefixSum<T>.StoreVector256Generic(p, PrefixSum<T>.SumVectorized(v));
            PrefixSum<T>.StoreVector256Generic(p, PrefixSum<T>.SumVectorized(v));
            PrefixSum<T>.StoreVector256Generic(p, PrefixSum<T>.SumVectorized(v));
            PrefixSum<T>.StoreVector256Generic(p, PrefixSum<T>.SumVectorized(v));
            PrefixSum<T>.StoreVector256Generic(p, PrefixSum<T>.SumVectorized(v));
            PrefixSum<T>.StoreVector256Generic(p, PrefixSum<T>.SumVectorized(v));
            PrefixSum<T>.StoreVector256Generic(p, PrefixSum<T>.SumVectorized(v));
        }
    }
}
