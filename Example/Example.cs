using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using VxSort;
using static Test.DataGeneration;


namespace Example
{
    static class Program
    {
        enum Example
        {
            Stable,
            Unstable,
            PCSort256,
            BitonicSort,
        }

        /// <summary>
        /// Run various quicksort/sort variants
        /// </summary>
        /// <param name="typeList">The type of sort routine to invoke</param>
        /// <param name="sizeList">The size(s) of the sort problem to invoke</param>
        /// <param name="maxLoops"># of loops to execute for the largest size</param>
        /// <param name="modulo">Squash all the values into a certain range</param>
        /// <param name="forceValue">force specific value</param>
        /// <param name="forceRate">how many elements should be the forced value</param>
        /// <param name="noCheck">how many elements should be the forced value</param>
        /// <param name="statsFile">optional path pointing where to store stats in json format</param>
        /// <param name="seed">Specify a seed for the random number generator</param>
        /// <param name="wait">Hang at the end of execution (for attaching JitDasm or debugger after the fact)</param>
        public static unsafe void Main(string typeList,                  string sizeList,
            int                                maxLoops   = 100,          int    modulo    = int.MaxValue,
            int                                forceValue = int.MinValue, double forceRate = double.NaN,
            bool                               noCheck    = false,        string statsFile = null,
            int                                seed       = 666,
            bool                               wait       = false)
        {
            var types = typeList.Split(',').Select(Enum.Parse<Example>).ToArray();
            var sizes = sizeList.Split(',').Select(int.Parse).ToArray();

            if (!CheckArgs()) return;

            var totalElements = sizes.Max() * maxLoops;

            var tmp = stackalloc int[1000];

            foreach (var type in types) {
                foreach (var s in sizes) {
                    var loops = totalElements / s;
                    var dups = 1;

                    if (loops > 1000) {
                        dups  = totalElements / 1000;
                        dups = Math.Min(dups, 1000);
                        loops = 1000;
                    }
                    Console.Write($"Sorting {type}/{s}({dups})...");

                    var arrays =
                        Enumerable.Range(seed, loops)
                            .Select(seed => GenerateData(s, seed, forceValue, forceRate, modulo, noCheck)).ToArray();

                    var copy = new int[s];

                    foreach (var (orig, sortedData, reproContext) in arrays) {
                        for (var d = 0; d < dups; d++) {
                            // Copy the array to avoid the data generation showing up / dominating performance traces...
                            Array.Copy(orig, copy, orig.Length);
                            fixed (int* pCopy = &copy[0]) {
                                switch (type) {
                                    //case Example.PCSort256:   PositionCountingSort.Sort<int>(pCopy, s, tmp); break;
                                    case Example.BitonicSort: BitonicSort<int>.Sort(pCopy, s); break;
                                    case Example.Unstable:    VectorizedSort.UnstableSort(orig); break;
                                    default:
                                        Console.WriteLine($"Nope, can't sort with {type}, ba-bye");
                                        return;
                                }
                            }
                        }

                        if (!noCheck) {
                            // Copy back once for checking...
                            Array.Copy(copy, orig, orig.Length);
                            DoubleCheckSorting(orig, sortedData, reproContext);
                        }

                    }
                    arrays = null;
                    GC.Collect(); // Be safe?
                    Console.WriteLine($"...Done");
                }
            }

            if (wait) {
                Console.ReadLine();
            }

            static void DoubleCheckSorting(int[] orig, int[] sortedData, string reproContext)
            {
                for (var i = 1; i < orig.Length; i++)
                    if (orig[i] < orig[i - 1])
                        throw new Exception(
                            $"Nope, you can't sort copy[{i}]({orig[i]}) < copy[{i - 1}]({orig[i - 1]}) [{reproContext}]");

                for (var i = 0; i < orig.Length; i++)
                    if (orig[i] != sortedData[i])
                        throw new Exception(
                            $"Nope, you can't sort copy[{i}]({orig[i]}) != sorted[{i}]({sortedData[i]}) [{reproContext}]");
            }

            bool CheckArgs()
            {
                foreach (var type in types) {
                    foreach (var size in sizes) {
                        switch (type) {
                            case Example.BitonicSort:
                            case Example.PCSort256:
                                if (size % 8 != 0) {
                                    Console.Error.WriteLine("You can only invoke PCSort with size divisible by 8");
                                    return false;
                                }

                                if (size > 64) {
                                    Console.Error.WriteLine("You can only invoke PCSort with size <= 64");
                                    return false;
                                }

                                if (size < 8) {
                                    Console.Error.WriteLine("You can only invoke PCSort with size >= 8 64");
                                    return false;
                                }

                                break;
                        }
                    }

                }
                return true;
            }
        }

        static IEnumerable<List<T>> ChunkIt<T>(this IEnumerable<T> xs, int size)
        {
            var curr = new List<T>(size);

            foreach (var x in xs)
            {
                curr.Add(x);

                if (curr.Count == size)
                {
                    yield return curr;
                    curr = new List<T>(size);
                }
            }
        }
    }
}
