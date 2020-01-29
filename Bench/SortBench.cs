using System;
using System.Net.Http.Headers;
using Bench.Utils;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Extensions;
using BenchmarkDotNet.Horology;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Reports;
using VxSort;

namespace Bench
{
    class LongConfig : ManualConfig
    {
        public LongConfig()
        {
            SummaryStyle = new SummaryStyle(true, SizeUnit.GB, TimeUnit.Microsecond);
            Add(Job.LongRun);
            Add(new TimePerNColumn());
        }
    }

    class MediumConfig : ManualConfig
    {
        public MediumConfig()
        {
            SummaryStyle = new SummaryStyle(true, SizeUnit.GB, TimeUnit.Microsecond);
            Add(Job.MediumRun);
            Add(new TimePerNColumn());
        }
    }

    class ShortConfig : ManualConfig
    {
        public ShortConfig()
        {
            SummaryStyle = new SummaryStyle(true, SizeUnit.GB, TimeUnit.Microsecond);
            Add(Job.ShortRun);
            Add(new TimePerNColumn());
        }
    }


    public class SortBenchBase<T> where T : unmanaged, IComparable<T>
    {
        protected virtual int InvocationsPerIteration { get; }
        protected int _iterationIndex = 0;
        T[] _values;
        protected T[][] _arrays;

        [Params(10, 100, 1_000, 10_000, 100_000, 1_000_000)]//, 10_000_000)]
        public int N;

        [GlobalSetup]
        public void Setup() => _values = ValuesGenerator.ArrayOfUniqueValues<T>(N);

        [IterationCleanup]
        public void CleanupIteration() => _iterationIndex = 0; // after every iteration end we set the index to 0

        [IterationSetup]
        public void SetupArrayIteration() => ValuesGenerator.FillArrays(ref _arrays, InvocationsPerIteration, _values);
    }

    [GenericTypeArguments(typeof(int))] // value type
    [InvocationCount(InvocationsPerIterationValue)]
    [Config(typeof(MediumConfig))]
    [DatatableJsonExporter]
    public class UnstableSort<T> : SortBenchBase<T> where T : unmanaged, IComparable<T>
    {
        const int InvocationsPerIterationValue = 3;

        protected override int InvocationsPerIteration => InvocationsPerIterationValue;

        [Benchmark(Baseline=true)]
        public void ArraySort() => Array.Sort(_arrays[_iterationIndex++]);

        [Benchmark]
        public void VxSort() => VectorizedSort.UnstableSort(_arrays[_iterationIndex++]);
    }
}
