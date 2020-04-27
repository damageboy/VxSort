using System;
using System.Globalization;
using Bench.Utils;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Reports;
using Perfolizer.Horology;
using VxSortResearch.Unstable.AVX2.Bug;
using VxSortResearch.Unstable.AVX2.Happy;
using VxSortResearch.Unstable.AVX2.Sad;

namespace Bench
{
    using SelectedConfig = MediumConfig;

    [GenericTypeArguments(typeof(int))] // value type
    [InvocationCount(BenchmarkConstants.InvocationsPerIterationValue)]
    [Config(typeof(SelectedConfig))]
    public class JitBug<T> : QuickSortBenchBase<T> where T : unmanaged, IComparable<T>
    {
        [Benchmark(Baseline = true)]
        public void Bug() => DoublePumpWithBug.Sort(_arrays[_iterationIndex++]);

        [Benchmark]
        public void Workaround() => DoublePumpWorkaround.Sort(_arrays[_iterationIndex++]);
    }
}
