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
using VxSortResearch.Unstable.AVX2.Happy;
using VxSortResearch.Unstable.AVX2.Sad;

namespace Bench
{
    using SelectedConfig = MediumConfig;

    static class BenchmarkConstants
    {
        public const int InvocationsPerIterationValue = 3;            
    }

    class LongConfig : ManualConfig
    {
        public LongConfig()
        {
            SummaryStyle = new SummaryStyle(CultureInfo.InvariantCulture, true, SizeUnit.B, TimeUnit.Microsecond);
            AddJob(Job.LongRun);
            
            AddColumn(new TimePerNColumn());
            AddColumn(SpeedupRatioColumn.SpeedupOfMedian);
            AddExporter(new DatatableJsonExporter());
        }
    }

    class MediumConfig : ManualConfig
    {
        public MediumConfig()
        {
            SummaryStyle = new SummaryStyle(CultureInfo.InvariantCulture, true, SizeUnit.B, TimeUnit.Microsecond);
            AddJob(Job.MediumRun);
            AddColumn(new TimePerNColumn());
            AddColumn(SpeedupRatioColumn.SpeedupOfMedian);
            AddExporter(new DatatableJsonExporter());
            AddDiagnoser(
                new DisassemblyDiagnoser(
                    new DisassemblyDiagnoserConfig(
                        maxDepth: 4, // you can change it to a bigger value if you want to get more framework methods disassembled
                        exportGithubMarkdown: true)));
        }
    }

    class ShortConfig : ManualConfig
    {
        public ShortConfig()
        {
            SummaryStyle = new SummaryStyle(CultureInfo.InvariantCulture, true, SizeUnit.B, TimeUnit.Microsecond);
            AddJob(Job.ShortRun);
            AddColumn(new TimePerNColumn());
            AddColumn(SpeedupRatioColumn.SpeedupOfMedian);
            AddExporter(new DatatableJsonExporter());
            AddDiagnoser(
                new DisassemblyDiagnoser(
                    new DisassemblyDiagnoserConfig(
                        maxDepth: 4, // you can change it to a bigger value if you want to get more framework methods disassembled
                        exportGithubMarkdown: true)));

        }
    }

    public class QuickSortBenchBase<T> where T : unmanaged, IComparable<T>
    {
        protected virtual int InvocationsPerIteration => BenchmarkConstants.InvocationsPerIterationValue;
        protected int _iterationIndex = 0;
        T[] _values;
        protected T[][] _arrays;

        [Params(100, 1_000, 10_000, 100_000, 1_000_000, 10_000_000)]
        public int N;

        [GlobalSetup]
        public void Setup() => _values = ValuesGenerator.ArrayOfUniqueValues<T>(N);

        [IterationCleanup]
        public void CleanupIteration() => _iterationIndex = 0; // after every iteration end we set the index to 0

        [IterationSetup]
        public void SetupArrayIteration() => ValuesGenerator.FillArrays(ref _arrays, InvocationsPerIteration, _values);
    }

    [GenericTypeArguments(typeof(int))] // value type
    [InvocationCount(BenchmarkConstants.InvocationsPerIterationValue)]
    [Config(typeof(SelectedConfig))]
    public class BlogPt1<T> : QuickSortBenchBase<T> where T : unmanaged, IComparable<T>
    {
        [Benchmark(Baseline=true)]
        public void ArraySort() => Array.Sort(_arrays[_iterationIndex++]);

        [Benchmark]
        public void Managed() => VxSortResearch.Unstable.Scalar.Managed.Sort(_arrays[_iterationIndex++]);

        [Benchmark]
        public void Unmanaged() => VxSortResearch.Unstable.Scalar.Unmanaged.Sort(_arrays[_iterationIndex++]);
    }

    [GenericTypeArguments(typeof(int))] // value type
    [InvocationCount(BenchmarkConstants.InvocationsPerIterationValue)]
    [Config(typeof(SelectedConfig))]
    public class BlogPt3<T> : QuickSortBenchBase<T> where T : unmanaged, IComparable<T>
    {
        [Benchmark(Baseline=true)]
        public void ArraySort() => Array.Sort(_arrays[_iterationIndex++]);

        [Benchmark]
        public void Naive() => DoublePumpNaive.Sort(_arrays[_iterationIndex++]);
    }

    [GenericTypeArguments(typeof(int))] // value type
    [InvocationCount(BenchmarkConstants.InvocationsPerIterationValue)]
    [Config(typeof(SelectedConfig))]
    public class BlogPt4_1<T> : QuickSortBenchBase<T> where T : unmanaged, IComparable<T>
    {
        [Benchmark(Baseline = true)]
        public void Naive() => DoublePumpNaive.Sort(_arrays[_iterationIndex++]);

        [Benchmark]
        public void MicroOpt() => DoublePumpMicroOpt.Sort(_arrays[_iterationIndex++]);
    }
    
    [GenericTypeArguments(typeof(int))] // value type
    [InvocationCount(BenchmarkConstants.InvocationsPerIterationValue)]    [Config(typeof(SelectedConfig))]
    public class BlogPt4_2<T> : QuickSortBenchBase<T> where T : unmanaged, IComparable<T>
    {
        [Benchmark(Baseline = true)]
        public void MicroOpt() => DoublePumpMicroOpt.Sort(_arrays[_iterationIndex++]);

        [Benchmark]
        public void MicroOpt_24() => DoublePumpMicroOptCutoff_24.Sort(_arrays[_iterationIndex++]);
        
        [Benchmark]
        public void MicroOpt_32() => DoublePumpMicroOptCutoff_32.Sort(_arrays[_iterationIndex++]);

        [Benchmark]
        public void MicroOpt_40() => DoublePumpMicroOptCutoff_40.Sort(_arrays[_iterationIndex++]);

        [Benchmark]
        public void MicroOpt_48() => DoublePumpMicroOptCutoff_48.Sort(_arrays[_iterationIndex++]);
    }
    
    [GenericTypeArguments(typeof(int))] // value type
    [InvocationCount(BenchmarkConstants.InvocationsPerIterationValue)]    [Config(typeof(SelectedConfig))]
    public class BlogPt4_3<T> : QuickSortBenchBase<T> where T : unmanaged, IComparable<T>
    {
        [Benchmark(Baseline = true)]
        public void MicroOpt_40() => DoublePumpMicroOptCutoff_40.Sort(_arrays[_iterationIndex++]);

        [Benchmark]
        public void SimpleBranch() => DoublePumpSimpleBranch.Sort(_arrays[_iterationIndex++]);
    }
    
    [GenericTypeArguments(typeof(int))] // value type
    [InvocationCount(BenchmarkConstants.InvocationsPerIterationValue)]
    [Config(typeof(SelectedConfig))]
    public class BlogPt4_4<T> : QuickSortBenchBase<T> where T : unmanaged, IComparable<T>
    {
        [Benchmark(Baseline = true)]
        public void SimpleBranch() => DoublePumpSimpleBranch.Sort(_arrays[_iterationIndex++]);

        [Benchmark]
        public void Packed() => DoublePumpPacked.Sort(_arrays[_iterationIndex++]);
    }
    
    [GenericTypeArguments(typeof(int))] // value type
    [InvocationCount(BenchmarkConstants.InvocationsPerIterationValue)]
    [Config(typeof(SelectedConfig))]
    public class BlogPt4_5<T> : QuickSortBenchBase<T> where T : unmanaged, IComparable<T>
    {
        [Benchmark(Baseline = true)]
        public void Packed() => DoublePumpPacked.Sort(_arrays[_iterationIndex++]);

        [Benchmark]
        public void Jedi() => DoublePumpJedi.Sort(_arrays[_iterationIndex++]);
    }

    [GenericTypeArguments(typeof(int))] // value type
    [InvocationCount(BenchmarkConstants.InvocationsPerIterationValue)]
    [Config(typeof(SelectedConfig))]
    public class BlogPt4_6<T> : QuickSortBenchBase<T> where T : unmanaged, IComparable<T>
    {
        [Benchmark(Baseline = true)]
        public void Naive() => DoublePumpNaive.Sort(_arrays[_iterationIndex++]);

        [Benchmark]
        public void Jedi() => DoublePumpJedi.Sort(_arrays[_iterationIndex++]);
    }

    
    [GenericTypeArguments(typeof(int))] // value type
    [InvocationCount(BenchmarkConstants.InvocationsPerIterationValue)]
    [Config(typeof(SelectedConfig))]
    public class BlogPt5_1<T> : QuickSortBenchBase<T> where T : unmanaged, IComparable<T>
    {
        [Benchmark(Baseline=true)]
        public void MicroOpt() => DoublePumpMicroOpt.Sort(_arrays[_iterationIndex++]);

        [Benchmark]
        public void Aligned() => DoublePumpAligned.Sort(_arrays[_iterationIndex++]);
    }

    [GenericTypeArguments(typeof(int))] // value type
    [InvocationCount(BenchmarkConstants.InvocationsPerIterationValue)]
    [Config(typeof(SelectedConfig))]
    public class BlogPt5_2<T> : QuickSortBenchBase<T> where T : unmanaged, IComparable<T>
    {
        [Benchmark(Baseline =true)]
        public void MicroOpt() => DoublePumpMicroOpt.Sort(_arrays[_iterationIndex++]);

        [Benchmark]
        public void Overlined() => DoublePumpOverlined.Sort(_arrays[_iterationIndex++]);

        [Benchmark]
        public void SimplerBranch() => DoublePumpOverlinedSimplerBranch.Sort(_arrays[_iterationIndex++]);

        [Benchmark]
        public void SimplerBranchless() => DoublePumpOverlinedBranchless.Sort(_arrays[_iterationIndex++]);
    }

    [GenericTypeArguments(typeof(int))] // value type
    [InvocationCount(BenchmarkConstants.InvocationsPerIterationValue)]
    [Config(typeof(SelectedConfig))]
    public class BlogPt6_1<T> : QuickSortBenchBase<T> where T : unmanaged, IComparable<T>
    {
        [Benchmark(Baseline = true)]
        public void AlignedOverlap() => DoublePumpOverlined.Sort(_arrays[_iterationIndex++]);

        [Benchmark]
        public void Unrolled4() => DoublePumpOverlinedUnroll4PCSort.Sort(_arrays[_iterationIndex++]);
    }


    [GenericTypeArguments(typeof(int))] // value type
    [InvocationCount(BenchmarkConstants.InvocationsPerIterationValue)]
    [Config(typeof(SelectedConfig))]
    public class BlogPt6_2<T> : QuickSortBenchBase<T> where T : unmanaged, IComparable<T>
    {
        [Benchmark(Baseline = true)]
        public void ArraySort() => Array.Sort(_arrays[_iterationIndex++]);

        [Benchmark]
        public void Unrolled4() => DoublePumpOverlinedUnroll4PCSort.Sort(_arrays[_iterationIndex++]);
    }

    [GenericTypeArguments(typeof(int))] // value type
    [InvocationCount(BenchmarkConstants.InvocationsPerIterationValue)]
    [Config(typeof(SelectedConfig))]
    public class BlogPt5<T> : QuickSortBenchBase<T> where T : unmanaged, IComparable<T>
    {
        [Benchmark(Baseline=true)]
        public void ArraySort() => Array.Sort(_arrays[_iterationIndex++]);
        
        //[Benchmark(Baseline=true)]
        public void PCSort() => DoublePumpOverlinedPCSort.Sort(_arrays[_iterationIndex++]);

        //[Benchmark]//(Baseline=true)]
        public void Unrolled4PCSort() => DoublePumpOverlinedUnroll4PCSort.Sort(_arrays[_iterationIndex++]);

        [Benchmark]//(Baseline=true)]
        public void Unrolled4BitonicSort() => DoublePumpOverlinedUnroll4BitonicSort.Sort(_arrays[_iterationIndex++]);

        [Benchmark]
        public void Unrolled8BitonicSort() => DoublePumpOverlinedUnroll8BitonicSort.Sort(_arrays[_iterationIndex++]);
        
        //[Benchmark]
        public void PackedUnrolled8BitonicSort() => DoublePumpOverlinedPackedUnroll8BitonicSort.Sort(_arrays[_iterationIndex++]);
        
    }

    [GenericTypeArguments(typeof(int))] // value type
    [InvocationCount(BenchmarkConstants.InvocationsPerIterationValue)]
    [Config(typeof(SelectedConfig))]
    public class BlogPt7<T> : QuickSortBenchBase<T> where T : unmanaged, IComparable<T>
    {
        //[Benchmark(Baseline =true)]
        public void ArraySort() => Array.Sort(_arrays[_iterationIndex++]);


        [Benchmark(Baseline=true)]
        public void Unrolled8BitonicSort() => DoublePumpOverlinedUnroll8BitonicSort.Sort(_arrays[_iterationIndex++]);


        [Benchmark]
        public void Unrolled8YoDawgBitonicSort() => DoublePumpOverlinedUnroll8YoDawgBitonicSort.Sort(_arrays[_iterationIndex++]);

        [Benchmark]
        public void Unrolled8HalfDawgBitonicSort() => DoublePumpOverlinedUnroll8HalfDawgBitonicSort.Sort(_arrays[_iterationIndex++]);

    }

    [GenericTypeArguments(typeof(int))] // value type
    [InvocationCount(BenchmarkConstants.InvocationsPerIterationValue)]
    [Config(typeof(SelectedConfig))]
    public class CrapCrap<T> : QuickSortBenchBase<T> where T : unmanaged, IComparable<T>
    {
        [Benchmark(Baseline = true)]
        public void AlignedOverlap() => DoublePumpOverlined.Sort(_arrays[_iterationIndex++]);

        [Benchmark]
        public void AlignedOverlapBranchless() => DoublePumpOverlinedBranchless.Sort(_arrays[_iterationIndex++]);
    }
}
