using System;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using QuickSort.cs;

namespace QuickSort.Avaganza
{
    class Program
    {
        static void Main(string[] args)
        {
            BenchmarkRunner.Run<QuickSortRunner>();
        }
    }

    public class QuickSortRunner
    {
        Random _rand;
        int[] _dataSample;


        [Params(100, 1_000, 10_000, 100_000, 1_000_000)]
        public int N;

        [GlobalSetup]
        public void GlobalSetup()
        {
            _rand = new Random((int) DateTime.Now.Ticks);

            _dataSample = new int[N];
        }
        [IterationSetup]
        public void IterationSetup()
        {
            for (int i = 0; i < N; ++i)
            {
                _dataSample[i] = _rand.Next();
            }
        }

        [Benchmark(Baseline=true)]
        public void ArraySort()
        {
            Array.Sort(_dataSample);
        }

        [Benchmark]
        public void QuickSortScalar() => Scalar.QuickSort(_dataSample);



        [Benchmark]
        public unsafe void QuickSortUnsafe()
        {
            fixed (int* p = &_dataSample[0])
            {
                Unmanaged.QuickSort(p, 0, _dataSample.Length);
            }
        }
    }
}
