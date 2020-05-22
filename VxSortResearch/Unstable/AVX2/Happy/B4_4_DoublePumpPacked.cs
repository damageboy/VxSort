using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using LocalsInit;
using VxSortResearch.PermutationTables;
using VxSortResearch.PermutationTables.Int32;
using VxSortResearch.Statistics;
using VxSortResearch.Utils;
using static System.Runtime.Intrinsics.X86.Avx;
using static System.Runtime.Intrinsics.X86.Avx2;
using static System.Runtime.Intrinsics.X86.Popcnt.X64;

namespace VxSortResearch.Unstable.AVX2.Happy
{
    [LocalsInit(true)]
    public static class DoublePumpPacked
    {
        public static unsafe void Sort<T>(T[] array) where T : unmanaged, IComparable<T>
        {
            if (array == null)
                throw new ArgumentNullException(nameof(array));

            Stats.BumpSorts(nameof(DoublePumpPacked), array.Length);
            fixed (T* p = &array[0]) {
                if (typeof(T) == typeof(int)) {
                    var pInt = (int*) p;
                    var sorter = new VxSortInt32(startPtr: pInt, endPtr: pInt + array.Length - 1);
                    var depthLimit = 2 * FloorLog2PlusOne(array.Length);
                    sorter.Sort(pInt, pInt + array.Length - 1, depthLimit);
                }
            }
        }

        static int FloorLog2PlusOne(int n)
        {
            var result = 0;
            while (n >= 1)
            {
                result++;
                n /= 2;
            }
            return result;
        }

        static unsafe void Swap<TX>(TX *left, TX *right) where TX : unmanaged, IComparable<TX>
        {
            var tmp = *left;
            *left  = *right;
            *right = tmp;
        }

        static void Swap<TX>(Span<TX> span, int left, int right)
        {
            var tmp = span[left];
            span[left]  = span[right];
            span[right] = tmp;
        }

        static unsafe void SwapIfGreater<TX>(TX *left, TX *right) where TX : unmanaged, IComparable<TX>
        {
            Stats.BumpScalarCompares();
            if ((*left).CompareTo(*right) <= 0) return;
            Swap(left, right);
        }

        static unsafe void InsertionSort<TX>(TX *left, TX *right) where TX : unmanaged, IComparable<TX>
        {
            Stats.BumpSmallSorts();
            Stats.BumpSmallSortsSize((ulong) (right - left));
            Stats.BumpSmallSortScalarCompares((ulong) (((right - left) * (right - left + 1))/2)); // Sum of sequence

            for (var i = left; i < right; i++) {
                var j = i;
                var t = *(i + 1);
                while (j >= left && t.CompareTo(*j) < 0) {
                    *(j + 1) = *j;
                    j--;
                }
                *(j + 1) = t;
            }
        }

        static void HeapSort<TX>(Span<TX> keys) where TX : unmanaged, IComparable<TX>
        {
            Debug.Assert(!keys.IsEmpty);

            var lo = 0;
            var hi = keys.Length - 1;

            var n = hi - lo + 1;
            for (var i = n / 2; i >= 1; i = i - 1)
            {
                DownHeap(keys, i, n, lo);
            }

            for (var i = n; i > 1; i--)
            {
                Swap(keys, lo, lo + i - 1);
                DownHeap(keys, 1, i - 1, lo);
            }
        }

        static void DownHeap<TX>(Span<TX> keys, int i, int n, int lo) where TX : unmanaged, IComparable<TX>
        {
            Debug.Assert(lo >= 0);
            Debug.Assert(lo < keys.Length);

            var d = keys[lo + i - 1];
            while (i <= n / 2)
            {
                var child = 2 * i;
                if (child < n && keys[lo + child - 1].CompareTo(keys[lo + child]) < 0)
                {
                    child++;
                }

                if (keys[lo + child - 1].CompareTo(d) < 0)
                    break;

                keys[lo + i - 1] = keys[lo + child - 1];
                i                = child;
            }

            keys[lo + i - 1] = d;
        }

        const int SLACK_PER_SIDE_IN_VECTORS = 1;

        internal unsafe ref struct VxSortInt32
        {
            const int SLACK_PER_SIDE_IN_ELEMENTS = SLACK_PER_SIDE_IN_VECTORS * 8;
            const int SMALL_SORT_THRESHOLD_ELEMENTS = 40;
            const int TMP_SIZE_IN_ELEMENTS = 2 * SLACK_PER_SIDE_IN_ELEMENTS + 8;

            readonly int* _startPtr,  _endPtr,
                          _tempStart, _tempEnd;
#pragma warning disable 649
            fixed int _temp[TMP_SIZE_IN_ELEMENTS];
            int _depth;
#pragma warning restore 649
            internal long Length => _endPtr - _startPtr + 1;

            public VxSortInt32(int* startPtr, int* endPtr) : this()
            {
                Debug.Assert(SMALL_SORT_THRESHOLD_ELEMENTS >= TMP_SIZE_IN_ELEMENTS);
                _depth = 0;
                _startPtr = startPtr;
                _endPtr   = endPtr;
                fixed (int* pTemp = _temp) {
                    _tempStart = pTemp;
                    _tempEnd   = pTemp + TMP_SIZE_IN_ELEMENTS;
                }
            }

            internal void Sort(int* left, int* right, int depthLimit)
            {
                var length = (int) (right - left + 1);

                int* mid;
                switch (length) {
                    case 0:
                    case 1:
                        return;
                    case 2:
                        SwapIfGreater(left, right);
                        return;
                    case 3:
                        mid = right - 1;
                        SwapIfGreater(left, mid);
                        SwapIfGreater(left, right);
                        SwapIfGreater(mid,  right);
                        return;
                }

                // Go to insertion sort below this threshold
                if (length <= SMALL_SORT_THRESHOLD_ELEMENTS) {
                    Dbg($"Going for Insertion Sort on [{left - _startPtr} -> {right - _startPtr - 1}]");
                    InsertionSort(left, right);
                    return;
                }

                // Detect a whole bunch of bad cases where partitioning
                // will not do well:
                // 1. Reverse sorted array
                // 2. High degree of repeated values (dutch flag problem, one value)
                if (depthLimit == 0)
                {
                    HeapSort(new Span<int>(left, (int) (right - left + 1)));
                    _depth--;
                    return;
                }
                depthLimit--;

                // Compute median-of-three, of:
                // the first, mid and one before last elements
                mid = left + ((right - left) / 2);
                SwapIfGreater(left, mid);
                SwapIfGreater(left, right - 1);
                SwapIfGreater(mid,  right - 1);

                // Pivot is mid, place it in the right hand side
                Dbg($"Pivot is {*mid}, storing in [{right - left}]");
                Swap(mid, right);

                var sep = VectorizedPartitionInPlace(left, right);

                Stats.BumpDepth(1);
                _depth++;
                Sort( left, sep - 1, depthLimit);
                Sort(sep,  right, depthLimit);
                Stats.BumpDepth(-1);
                _depth--;
            }

            /// <summary>
            /// Partition using Vectorized AVX2 intrinsics
            /// </summary>
            /// <param name="left">pointer (inclusive) to the first element to partition</param>
            /// <param name="right">pointer (exclusive) to the last element to partition, actually points to where the pivot before partitioning</param>
            /// <returns>Position of the pivot that was passed to the function inside the array</returns>
            [MethodImpl(MethodImplOptions.AggressiveOptimization)]
            int* VectorizedPartitionInPlace(int* left, int* right)
            {
                Stats.BumpPartitionOperations();
                Debug.Assert(right - left >= SLACK_PER_SIDE_IN_ELEMENTS * 2);
                Dbg($"{nameof(VectorizedPartitionInPlace)}: [{left - _startPtr}-{right - _startPtr}]({right - left + 1})");
                var N = Vector256<int>.Count; // treated as constant by the JIT
                var pivot = *right;

                var tmpLeft = _tempStart;
                var tmpRight = _tempEnd - N;

                var pBase = BytePermTables.BytePermTableAlignedPtr;
                var P = Vector256.Create(pivot);
                Trace($"pivot Vector256 is: {P}");

                Stats.BumpVectorizedPartitionBlocks(2);
                PartitionBlock(left         , P, pBase, ref tmpLeft, ref tmpRight);
                PartitionBlock(right - N - 1, P, pBase, ref tmpLeft, ref tmpRight);

                var writeLeft = left;
                var writeRight = right - N - 1;
                var readLeft = left + N;
                var readRight = right - 2*N - 1;

                while (readRight >= readLeft) {
                    Stats.BumpScalarCompares();
                    Stats.BumpVectorizedPartitionBlocks();

                    int* nextPtr;
                    if ((byte *) writeRight - (byte *) readRight < N * sizeof(int)) {
                        nextPtr   =  readRight;
                        readRight -= N;
                    } else {
                        nextPtr  =  readLeft;
                        readLeft += N;
                    }

                    PartitionBlock(nextPtr, P, pBase, ref writeLeft, ref writeRight);
                }

                // We're scalar from now, so adjust required right-side pointers
                readRight += N;
                tmpRight += N;

                Trace($"Doing remainder as scalar partition on [{readLeft - left}-{readRight - left})({readRight - readLeft}) into tmp  [{tmpLeft - _tempStart}-{tmpRight - 1 - _tempStart}]");
                while (readLeft < readRight) {
                    Stats.BumpScalarCompares();
                    var v = *readLeft++;

                    if (v <= pivot) {
                        Trace($"Read: [{readLeft - 1 - left}]={v} <= {pivot} -> writing to tmpLeft [{tmpLeft - _tempStart}]");
                        *tmpLeft++ = v;
                    }
                    else {
                        Trace($"Read: [{readLeft - 1 - left}]={v} > {pivot} -> writing to tmpRight [{tmpRight - 1 - _tempStart}]");
                        *--tmpRight = v;
                    }

                    Trace($"RL:{readLeft - left} 🡄🡆 RR:{readRight - left}");
                }

                var leftTmpSize = (uint) (int)(tmpLeft - _tempStart);
                Trace($"Copying back tmpLeft -> [{writeLeft - left}-{writeLeft - left + leftTmpSize})");
                Unsafe.CopyBlockUnaligned(writeLeft, _tempStart, leftTmpSize*sizeof(int));
                writeLeft += leftTmpSize;
                var rightTmpSize = (uint) (int) (_tempEnd - tmpRight);
                Trace($"Copying back tmpRight -> [{writeLeft - left}-{writeLeft - left + rightTmpSize})");
                Unsafe.CopyBlockUnaligned(writeLeft, tmpRight, rightTmpSize*sizeof(int));

                // Shove to pivot back to the boundary
                Dbg($"Swapping pivot {*right} from [{right - left}] into [{writeLeft - left}]");
                Swap(writeLeft++, right);
                Dbg($"Final Boundary: {writeLeft - left}/{right - left + 1}");
                Dbg(new ReadOnlySpan<int>(_startPtr, (int) Length).Dump());

                VerifyBoundaryIsCorrect(left, right, pivot, writeLeft);

                return writeLeft;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining|MethodImplOptions.AggressiveOptimization)]
#if !DEBUG
            static
#endif
                void PartitionBlock(int *dataPtr, Vector256<int> P, byte* pBase, ref int* writeLeft, ref int* writeRight)
            {
#if DEBUG
                var that = this;
#endif

                var dataVec = LoadDquVector256(dataPtr);
                var mask = (ulong) (uint) MoveMask(CompareGreaterThan(dataVec, P).AsSingle());
                dataVec = PermuteVar8x32(dataVec, BytePermTables.GetBytePermutationAligned(pBase, mask));
                Store(writeLeft,  dataVec);
                Store(writeRight, dataVec);

                var popCount = PopCount(mask) << 2;
                writeRight = (int*) ((byte*) writeRight - popCount);
                writeLeft  = (int*) ((byte*) writeLeft + (8U << 2) - popCount);

#if DEBUG
                Vector256<int> LoadDquVector256(int* address)
                {
                    //Debug.Assert(address >= left);
                    //Debug.Assert(address + 8U <= right);
                    var tmp = Avx.LoadDquVector256(address);
                    //Trace($"Reading from offset: [{address - left}-{address - left + 7U}]: {tmp}");
                    return tmp;
                }

                void Store(int* address, Vector256<int> boundaryData)
                {
                    //Debug.Assert(address >= left);
                    //Debug.Assert(address + 8U <= right);
                    //Debug.Assert((address >= writeLeft && address + 8U <= readLeft) ||
                    //             (address >= readRight && address <= writeRight));
                    //Trace($"Storing to [{address - left}-{address - left + 7U}]");
                    Avx.Store(address, boundaryData);
                }
#endif
            }

            [Conditional("DEBUG")]
            void VerifyBoundaryIsCorrect(int* left, int* right, int pivot, int* boundary)
            {
                for (var t = left; t < boundary; t++)
                    if (!(*t <= pivot)) {
                        Dbg($"depth={_depth} boundary={boundary - left}, idx = {t - left} *t({*t}) <= pivot={pivot}");
                        Debug.Assert(*t <= pivot, $"depth={_depth} boundary={boundary - left}, idx = {t - left} *t({*t}) <= pivot={pivot}");
                    }

                for (var t = boundary; t <= right; t++)
                    if (!(*t >= pivot)) {
                        Dbg($"depth={_depth} boundary={boundary - left}, idx = {t - left} *t({*t}) >= pivot={pivot}");
                        Debug.Assert(*t >= pivot, $"depth={_depth} boundary={boundary - left}, idx = {t - left} *t({*t}) >= pivot={pivot}");
                    }
            }

            [Conditional("DEBUG")]
            void Dbg(string d) => Console.WriteLine($"{_depth}> {d}");

            [Conditional("DEBUG")]
            void Trace(string d) => Console.WriteLine($"{_depth}> {d}");

        }
    }
}