using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using VxSortResearch.PermutationTables;
using VxSortResearch.Statistics;
using VxSortResearch.Unstable.SmallSort;
using VxSortResearch.Utils;
using static System.Runtime.Intrinsics.X86.Avx;
using static System.Runtime.Intrinsics.X86.Avx2;
using static System.Runtime.Intrinsics.X86.Popcnt.X64;
using static System.Runtime.Intrinsics.X86.Popcnt;
using static VxSortResearch.PermutationTables.Int32.BytePermTables;

namespace VxSortResearch.Unstable.AVX2.Happy
{
    using ROS = ReadOnlySpan<int>;

    public static class DoublePumpOverlinedUnroll8BitonicSort
    {
        public static unsafe void Sort<T>(T[] array) where T : unmanaged, IComparable<T>
        {
            if (array == null)
                throw new ArgumentNullException(nameof(array));

            Stats.BumpSorts(nameof(DoublePumpOverlinedUnroll8BitonicSort), array.Length);
            fixed (T* p = &array[0]) {
                // Yes this looks horrid, but the C# JIT will happily elide
                // the irrelevant code per each type being compiled, so we're good
                if (typeof(T) == typeof(int)) {
                    var left = (int*) p;
                    var sorter = new VxSortInt32(startPtr: left, endPtr: left + array.Length - 1);
                    var depthLimit = 2 * FloorLog2PlusOne(array.Length);
                    sorter.Sort(left, left + array.Length - 1, VxSortInt32.REALIGN_BOTH, depthLimit);
                }
            }
        }

        static int FloorLog2PlusOne(int n)
        {
            int result = 0;
            while (n >= 1)
            {
                result++;
                n /= 2;
            }
            return result;
        }

        static unsafe void Swap<TX>(TX *left, TX *right) where TX : unmanaged
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static unsafe void InsertionSort<TX>(TX * left, TX * right) where TX : unmanaged, IComparable<TX>
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
        const int SMALL_SORT_THRESHOLD_ELEMENTS = 112;
        const int SLACK_PER_SIDE_IN_VECTORS = 8;
        const int UNROLL2_SLACK_PER_SIDE_IN_VECTORS = 4;
        const ulong ALIGN = 32;
        const ulong ALIGN_MASK = ALIGN - 1;

        internal unsafe struct VxSortInt32
        {
            const int SLACK_PER_SIDE_IN_ELEMENTS = SLACK_PER_SIDE_IN_VECTORS * 8;
            const int UNROLL2_SLACK_PER_SIDE_IN_ELEMENTS = UNROLL2_SLACK_PER_SIDE_IN_VECTORS  * 8;
            const int EIGTH_SLACK_PER_SIDE_IN_ELEMENTS = 8;
            // The formula goes like this:
            // 2 x the number of slack elements on each side +
            // 2 x amount of maximal bytes needed for alignment (32)
            // 8 more elements since we write with 8-way stores from both ends of the temporary area
            //   and we must make sure to accidentaly over-write from left -> right or vice-versa right on that edge...
            // In other words, while we allocated this much temp memory, the actual amount of elements inside said memory
            // is smaller by 8 elements + 1 for each alignmet (max alignment is actuall 7, I just round up to 8...)
            // so maxumal numbers of elements inside temporary memory is smaller by 10..
            const int PARTITION_TMP_SIZE_IN_ELEMENTS = (int) (2 * SLACK_PER_SIDE_IN_ELEMENTS + (2 * ALIGN / sizeof(int)) + 8);

            const long REALIGN_LEFT = 0x666;
            const long REALIGN_RIGHT = 0x66600000000;
            internal const long REALIGN_BOTH = REALIGN_LEFT | REALIGN_RIGHT;
            readonly int* _startPtr;
            readonly int* _endPtr;
            readonly int* _tempStart;
            readonly int* _tempEnd;
#pragma warning disable 649
            fixed int _temp[PARTITION_TMP_SIZE_IN_ELEMENTS];
            int _depth;
#pragma warning restore 649
            internal long Length => _endPtr - _startPtr + 1;

#if DEBUG
            int* _lastLeftKosherWrite;
            int* _lastRightKosherWrite;
            int* _lastTempLeftKosherWrite;
            int* _lastTempRightKosherWrite;
#endif

            public VxSortInt32(int* startPtr, int* endPtr) : this()
            {
                Debug.Assert(SMALL_SORT_THRESHOLD_ELEMENTS % 8 == 0);

                _depth = 0;
                _startPtr = startPtr;
                _endPtr   = endPtr;
                fixed (int* pTemp = _temp) {
                    _tempStart = pTemp;
                    _tempEnd   = pTemp + PARTITION_TMP_SIZE_IN_ELEMENTS;
                }

                Dbg($"temp-size is {PARTITION_TMP_SIZE_IN_ELEMENTS}");
                Dbg($"orig: {new ReadOnlySpan<int>(startPtr, (int) Length).Dump()}");
            }

            [MethodImpl(MethodImplOptions.AggressiveOptimization)]
            internal void Sort(int* left, int* right, long hint, int depthLimit)
            {
                Debug.Assert(left <= right);

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
                        SwapIfGreater(mid, right);
                        return;
                }

                Stats.BumpDepth(1);
                _depth++;

                // SMALL_SORT_THRESHOLD_ELEMENTS is guaranteed (and asserted) to be a multiple of 8
                // So we can check if length is strictly smaller, knowing that we will round up to
                // SMALL_SORT_THRESHOLD_ELEMENTS exactly and no more
                // This is kind of critical given that we only limited # of implementation of
                // vectorized bitonic sort
                if (length < SMALL_SORT_THRESHOLD_ELEMENTS) {
                    var nextLength = (length & 7) > 0 ? (length + 8) & ~7: length;

                    Debug.Assert(nextLength <= BitonicSort<int>.MaxBitonicSortSize);
                    var extraSpaceNeeded = nextLength - length;
                    var fakeLeft = left - extraSpaceNeeded;
                    if (fakeLeft >= _startPtr) {
                        Dbg($"Going for BitonicSort from [{fakeLeft - _startPtr}-{fakeLeft + nextLength - _startPtr})({nextLength})");
                        BitonicSort<int>.Sort(fakeLeft, nextLength);
                    }
                    else {
                        Dbg($"Going for Insertion Sort on [{left - _startPtr} -> {right - _startPtr - 1}]");
                        InsertionSort(left, right);
                    }
                    Stats.BumpDepth(-1);
                    _depth--;
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

                // This is going to be a bit weird:
                // Pre/Post alignment calculations happen here: we prepare hints to the
                // partition function of how much to align and in which direction (pre/post).
                // The motivation to do these calculations here and the actual alignment inside the partitioning code is
                // that here, we can cache those calculations.
                // As we recurse to the left we can reuse the left cached calculation, And when we recurse
                // to the right we reuse the right calculation, so we can avoid re-calculating the same aligned addresses
                // throughout the recursion, at the cost of a minor code complexity
                // Since we branch on the magi values REALIGN_LEFT & REALIGN_RIGHT its safe to assume
                // the we are not torturing the branch predictor.'

                // We use a long as a "struct" to pass on alignment hints to the partitioning
                // By packing 2 32 bit elements into it, as the JIT seem to not do this.
                // In reality  we need more like 2x 4bits for each side, but I don't think
                // there is a real difference'

                var preAlignedLeft = (int*)  ((ulong) left & ~ALIGN_MASK);
                var cannotPreAlignLeft = (preAlignedLeft - _startPtr) >> 63;
                var preAlignLeftOffset = (preAlignedLeft - left) + (8 & cannotPreAlignLeft);
                if ((hint & REALIGN_LEFT) == REALIGN_LEFT) {
                    Trace("Recalculating left alignment");
                    // Alignment flow:
                    // * Calculate pre-alignment on the left
                    // * See it would cause us an out-of bounds read
                    // * Since we'd like to avoid that, we adjust for post-alignment
                    // * There are no branches since we do branch->arithmetic
                    hint &= unchecked((long) 0xFFFFFFFF00000000UL);
                    hint |= preAlignLeftOffset;
                }

                var preAlignedRight = (int*) (((ulong) right - 1 & ~ALIGN_MASK) + ALIGN);
                var cannotPreAlignRight = (_endPtr - preAlignedRight) >> 63;
                var preAlignRightOffset = (preAlignedRight - right - (8 & cannotPreAlignRight));
                if ((hint & REALIGN_RIGHT) == REALIGN_RIGHT) {
                    Trace("Recalculating right alignment");
                    // right is pointing just PAST the last element we intend to partition (where we also store the pivot)
                    // So we calculate alignment based on right - 1, and YES: I am casting to ulong before doing the -1, this
                    // is intentional since the whole thing is either aligned to 32 bytes or not, so decrementing the POINTER value
                    // by 1 is sufficient for the alignment, an the JIT sucks at this anyway
                    hint &= 0xFFFFFFFF;
                    hint |= preAlignRightOffset << 32;
                }

                Debug.Assert(((ulong) (left + (hint & 0xFFFFFFFF)) & ALIGN_MASK) == 0);
                Debug.Assert(((ulong) (right + (hint >> 32)) & ALIGN_MASK) == 0);

                // Compute median-of-three, of:
                // the first, mid and one before last elements
                mid = left + ((right - left) / 2);
                Dbg($"Selecting pivot with median of 3 between: {*left}, {*mid}, {*(right-1)}");
                SwapIfGreater(left, mid);
                SwapIfGreater(left, right - 1);
                SwapIfGreater(mid, right - 1);

                // Pivot is mid, place it in the right hand side
                Dbg($"Pivot is {*mid}, storing in [{right - left}]");
                Swap(mid, right);

#if DEBUG
                _lastLeftKosherWrite      = left - 1;
                _lastRightKosherWrite     = right;
                _lastTempLeftKosherWrite  = _tempStart - 1;
                _lastTempRightKosherWrite = _tempEnd;
#endif

                var sep = length < PARTITION_TMP_SIZE_IN_ELEMENTS ?
                    Partition1VectorInPlace(left, right, hint) :
                    Partition8VectorsInPlace(left, right, hint);

                Dbg($"Before Partitioning: {new ROS(left, (int) (right - left) + 1).Dump()}");

                Sort(left, sep - 2, hint | REALIGN_RIGHT, depthLimit);
                Sort(sep, right, hint | REALIGN_LEFT, depthLimit);
                Stats.BumpDepth(-1);
                _depth--;
            }

            /// <summary>
            /// Partition using Vectorized AVX2 intrinsics
            /// </summary>
            /// <param name="left">pointer (inclusive) to the first element to partition</param>
            /// <param name="right">pointer (exclusive) to the last element to partition, actually points to where the pivot before partitioning</param>
            /// <param name="hint">alignment instructions</param>
            /// <returns>Position of the pivot that was passed to the function inside the array</returns>
            [MethodImpl(MethodImplOptions.AggressiveOptimization)]
            internal int* Partition8VectorsInPlace(int* left, int* right, long hint)
            {
#if DEBUG
                var that = this; // CS1673
#endif

                Stats.BumpPartitionOperations();
                Debug.Assert(right - left >= SMALL_SORT_THRESHOLD_ELEMENTS, $"Not enough elements: {right-left} >= {SMALL_SORT_THRESHOLD_ELEMENTS}");
                Dbg2($"{nameof(Partition8VectorsInPlace)}: [{left - _startPtr}-{right - _startPtr}]({right - left + 1})");

                Debug.Assert((((ulong) left) & 0x3) == 0);
                Debug.Assert((((ulong) right) & 0x3) == 0);
                // Vectorized double-pumped (dual-sided) partitioning:
                // We start with picking a pivot using the media-of-3 "method"
                // Once we have sensible pivot stored as the last element of the array
                // We process the array from both ends.
                //
                // To get this rolling, we first read 2 Vector256 elements from the left and
                // another 2 from the right, and store them in some temporary space
                // in order to leave enough "space" inside the vector for storing partitioned values.
                // Why 2 from each side? Because we need n+1 from each side
                // where n is the number of Vector256 elements we process in each iteration...
                // The reasoning behind the +1 is because of the way we decide from *which*
                // side to read, we may end up reading up to one more vector from any given side
                // and writing it in its entirety to the opposite side (this becomes slightly clearer
                // when reading the code below...)
                // Conceptually, the bulk of the processing looks like this after clearing out some initial
                // space as described above:

                // [.............................................................................]
                //  ^wl          ^rl                                               rr^        wr^
                // Where:
                // wl = writeLeft
                // rl = readLeft
                // rr = readRight
                // wr = writeRight

                // In every iteration, we select what side to read from based on how much
                // space is left between head read/write pointer on each side...
                // We read from where there is a smaller gap, e.g. that side
                // that is closer to the unfortunate possibility of its write head overwriting
                // its read head... By reading from THAT side, we're ensuring this does not happen

                // An additional unfortunate complexity we need to deal with is that the right pointer
                // must be decremented by another Vector256<T>.Count elements
                // Since the Load/Store primitives obviously accept start addresses
                var N = Vector256<int>.Count; // Treated as constant @ JIT time
                var pivot = *right;
                Dbg($"Read pivot {pivot} from [{right-left}]");
                // We do this here just in case we need to pre-align to the right
                // We end up
                *right = int.MaxValue;

                var readLeft = left;
                var readRight = right;
                var writeLeft = left;
                var crappyWriteRight = right - N;

                var tmpStartLeft = _tempStart;
                var tmpLeft = tmpStartLeft;
                var tmpStartRight = _tempEnd;
                var tmpRight = tmpStartRight;

                // Broadcast the selected pivot
                var P = Vector256.Create(pivot);
                var pBase = BytePermTableWithLeftPopCountAlignedPtr;
                tmpRight -= N;

                #region Vector256 Alignment
                // the read heads always advance by 8 elements, or 32 bytes,
                // We can spend some extra time here to align the pointers
                // so they start at a cache-line boundary
                // Once that happens, we can read with Avx.LoadAlignedVector256
                // And also know for sure that our reads will never cross cache-lines
                // Otherwise, 50% of our AVX2 Loads will need to read from two cache-lines
                var leftAlign = unchecked((int) (hint & 0xFFFFFFFF));
                var rightAlign = unchecked((int) (hint >> 32));

                var preAlignedLeft = left + leftAlign;
                var preAlignedRight = right + rightAlign - N;

                // We preemptively go through the motions of
                // vectorized alignment, and at worst we re-neg
                // by not advancing the various read/tmp pointers
                // as if nothing ever happenned if the conditions
                // are wrong from vectorized alginment
                Stats.BumpVectorizedPartitionBlocks(2);
                var RT0 = LoadAlignedVector256(preAlignedRight);
                var LT0 = LoadAlignedVector256(preAlignedLeft);
                var rtMask = (uint) MoveMask(CompareGreaterThan(RT0, P).AsSingle());
                var ltMask = (uint) MoveMask(CompareGreaterThan(LT0, P).AsSingle());
                var rtPopCount = Math.Max(PopCount(rtMask), (uint) rightAlign);
                var ltPopCount = PopCount(ltMask);
                RT0 = PermuteVar8x32(RT0, GetBytePermutationAligned(pBase, rtMask));
                LT0 = PermuteVar8x32(LT0, GetBytePermutationAligned(pBase, ltMask));
                Avx.Store(tmpRight, RT0);
                Avx.Store(tmpLeft, LT0);

                var rightAlignMask = ~((rightAlign - 1) >> 31);
                var leftAlignMask = leftAlign >> 31;

                tmpRight -= rtPopCount & rightAlignMask;
                rtPopCount = 8 - rtPopCount;
                readRight += (rightAlign - N) & rightAlignMask;

                Avx.Store(tmpRight, LT0);
                tmpRight -= ltPopCount & leftAlignMask;
                ltPopCount =  8 - ltPopCount;
                tmpLeft += ltPopCount & leftAlignMask;
                tmpStartLeft += -leftAlign & leftAlignMask;
                readLeft += (leftAlign + N) & leftAlignMask;

                Avx.Store(tmpLeft,  RT0);
                tmpLeft       += rtPopCount & rightAlignMask;
                tmpStartRight -= rightAlign & rightAlignMask;

                if (leftAlign > 0) {
                    tmpRight += N;
                    readLeft = AlignLeftScalarUncommon(readLeft, pivot, ref tmpLeft, ref tmpRight);
                    tmpRight -= N;
                }

                if (rightAlign < 0) {
                    tmpRight += N;
                    readRight = AlignRightScalarUncommon(readRight, pivot, ref tmpLeft, ref tmpRight);
                    tmpRight -= N;
                }
                Debug.Assert(((ulong) readLeft & ALIGN_MASK) == 0);
                Debug.Assert(((ulong) readRight & ALIGN_MASK) == 0);

                Debug.Assert((((byte *) readRight - (byte *) readLeft) % (long) ALIGN) == 0);
                Debug.Assert((readRight -  readLeft) >= SLACK_PER_SIDE_IN_ELEMENTS * 2);

                Trace($"Post alignment [{readLeft - left}-{readRight - left})({readRight - readLeft})");
                Trace($"tmpLeft = {new ROS(tmpStartLeft, (int) (tmpLeft - tmpStartLeft)).Dump()}");
                Trace($"tmpRight = {new ROS(tmpRight + N, (int) (tmpStartRight - (tmpRight + N))).Dump()}");
                #endregion

                Stats.BumpVectorizedPartitionBlocks(16);
                // Make 4 vectors worth of space on each side by partitioning them straight into the temporary memory
                Dbg($"Partition 8 Vectors on the left  [{readLeft - left}⋯{readLeft+SLACK_PER_SIDE_IN_ELEMENTS - left - 1}]");
                LoadAndPartition8Vectors(readLeft, P, pBase, ref tmpLeft, ref tmpRight);
                Dbg($"Partition 8 Vectors on the right [{readRight - SLACK_PER_SIDE_IN_ELEMENTS - left}⋯{readRight - left - 1}]");
                LoadAndPartition8Vectors(readRight - SLACK_PER_SIDE_IN_ELEMENTS, P, pBase, ref tmpLeft, ref tmpRight);
                tmpRight += N;

                // Adjust for the reading that was made above
                readLeft  += SLACK_PER_SIDE_IN_ELEMENTS;
                readRight -= SLACK_PER_SIDE_IN_ELEMENTS * 2;

                Trace($"Post slack [{readLeft - left}-{readRight + SLACK_PER_SIDE_IN_ELEMENTS - left})({readRight + SLACK_PER_SIDE_IN_ELEMENTS - readLeft})");
                Trace($"tmpLeft = {new ROS(tmpStartLeft, (int) (tmpLeft - tmpStartLeft)).Dump()}");
                Trace($"tmpRight = {new ROS(tmpRight, (int) (tmpStartRight - tmpRight)).Dump()}");
                Trace($"Temp space left: tmpRight - tmpLeft = {tmpRight - tmpLeft}");

                var writeRight = crappyWriteRight;

                while (readLeft < readRight) {
                    Stats.BumpScalarCompares();
                    Stats.BumpVectorizedPartitionBlocks(8);

                    Trace($"8x: RL:{readLeft - left}|RR:{readRight + SLACK_PER_SIDE_IN_ELEMENTS - left}|{readRight + SLACK_PER_SIDE_IN_ELEMENTS - readLeft}");
                    Trace($"8x: WL:{writeLeft - left}({readLeft - writeLeft})|WR:{writeRight + N - left}({writeRight + N - (readRight + SLACK_PER_SIDE_IN_ELEMENTS)})");
                    int* nextPtr;
                    if ((byte *) writeRight - (byte *) readRight  < (2*SLACK_PER_SIDE_IN_ELEMENTS - N)*sizeof(int)) {
                        nextPtr   =  readRight;
                        readRight -= SLACK_PER_SIDE_IN_ELEMENTS;
                    } else {
                        nextPtr  =  readLeft;
                        readLeft += SLACK_PER_SIDE_IN_ELEMENTS;
                    }

                    Debug.Assert(readLeft - writeLeft >= SLACK_PER_SIDE_IN_ELEMENTS,   $"left head overwrite {readLeft - writeLeft}");
                    Debug.Assert(writeRight - readRight >= SLACK_PER_SIDE_IN_ELEMENTS, $"right head overwrite {writeRight - readRight}");

                    LoadAndPartition8Vectors(nextPtr, P, pBase, ref writeLeft, ref writeRight);
                }

                readRight += UNROLL2_SLACK_PER_SIDE_IN_ELEMENTS;

                while (readLeft < readRight) {
                    Stats.BumpScalarCompares();
                    Stats.BumpVectorizedPartitionBlocks(4);

                    Trace($"4x: RL:{readLeft - left}|RR:{readRight + UNROLL2_SLACK_PER_SIDE_IN_ELEMENTS - left}|{readRight + UNROLL2_SLACK_PER_SIDE_IN_ELEMENTS - readLeft}");
                    Trace($"4x: WL:{writeLeft - left}({readLeft - writeLeft})|WR:{writeRight + N - left}({writeRight + N - (readRight + UNROLL2_SLACK_PER_SIDE_IN_ELEMENTS)})");

                    int* nextPtr;
                    if ((byte *) writeRight - (byte *) readRight  < (2*UNROLL2_SLACK_PER_SIDE_IN_ELEMENTS - N) * sizeof(int)) {
                        nextPtr   =  readRight;
                        readRight -= UNROLL2_SLACK_PER_SIDE_IN_ELEMENTS;
                    } else {
                        nextPtr  =  readLeft;
                        readLeft += UNROLL2_SLACK_PER_SIDE_IN_ELEMENTS;
                    }

                    Debug.Assert(readLeft - writeLeft >= UNROLL2_SLACK_PER_SIDE_IN_ELEMENTS,   $"left head overwrite {readLeft - writeLeft}");
                    Debug.Assert(writeRight - readRight >= UNROLL2_SLACK_PER_SIDE_IN_ELEMENTS, $"right head overwrite {writeRight - readRight}");

                    LoadAndPartition4Vectors(nextPtr, P, pBase, ref writeLeft, ref writeRight);
                }

                readRight += UNROLL2_SLACK_PER_SIDE_IN_ELEMENTS - N;

                Trace($"Before 1x WL:{writeLeft - left}({readLeft - writeLeft})|WR:{writeRight + 8 - left}({writeRight - readRight})");
                Trace($"Before 1x RL:{readLeft - left}|RR:{readRight + 8 - left}|{readRight + 8 - readLeft}");
                while (readLeft <= readRight) {
                    Stats.BumpScalarCompares();
                    Stats.BumpVectorizedPartitionBlocks();

                    int* nextPtr;
                    if (((byte *) writeRight - (byte *) readRight) < N * sizeof(int)) {
                        nextPtr   =  readRight;
                        readRight -= N;
                    } else {
                        nextPtr  =  readLeft;
                        readLeft += N;
                    }

                    PartitionBlock1V(LoadAlignedVector256(nextPtr), P, pBase, ref writeLeft, ref writeRight);
                }

                var boundary = writeLeft;

                // 3. Copy-back the 4 registers + remainder we partitioned in the beginning
                var leftTmpSize = (uint) (ulong) (tmpLeft - tmpStartLeft);
                Trace($"Copying back tmpLeft 🡆 [{boundary-left}-{boundary-left+leftTmpSize})|{new ROS(tmpStartLeft, (int) leftTmpSize).Dump()}");
                Unsafe.CopyBlockUnaligned(boundary, tmpStartLeft, leftTmpSize*sizeof(int));
                boundary += leftTmpSize;
                var rightTmpSize = (uint) (ulong) (tmpStartRight - tmpRight);
                Trace($"Copying back tmpRight 🡆 [{boundary-left}-{boundary-left+rightTmpSize})|{new ROS(tmpRight, (int) rightTmpSize).Dump()}");
                Unsafe.CopyBlockUnaligned(boundary, tmpRight, rightTmpSize*sizeof(int));

                Dbg(new ROS(left, (int) (right - left + 1)).Dump());

                // Shove to pivot back to the boundary
                var value = *boundary;
                Dbg($"Writing boundary [{boundary - left}]({value}) into pivot pos [{right - left}]");
                *right = value;
                *boundary++ = pivot;

                Dbg($"Final Boundary: {boundary - left}/{right - left + 1}");
                Dbg(new ROS(left, (int) (right - left + 1)).Dump());
                Dbg(new ROS(_startPtr, (int) Length).Dump());

                VerifyBoundaryIsCorrect(left, right, pivot, boundary);

                Debug.Assert(boundary > left);
                Debug.Assert(boundary <= right);

                Dbg("----------------------------------------------------------------------");

                return boundary;

#if XXX
                Vector256<int> LoadAlignedVector256(int* address) {
                    const int CACHE_LINE_SIZE = 64;
                    Debug.Assert(address >= left - 8,       $"a: 0x{(ulong) address:X16}, left: 0x{(ulong) left:X16} d: {left - address}");
                    Debug.Assert(address + 8U <= right + 8, $"a: 0x{(ulong) address:X16}, right: 0x{(ulong) right:X16} d: {right - address}");
                    Debug.Assert(
                        (((ulong) address) / CACHE_LINE_SIZE) ==
                                 (((ulong) address + ALIGN - 1) / CACHE_LINE_SIZE),
                                                                     $"0x{(ulong)address:X16} crosses two cachelines...");
                    var tmp = Avx.LoadAlignedVector256(address);

                    var (symbol, offset) = (address - left < right - (address + 7))
                            ? ("🡆", address - left)
                            : ("🡄", right - (address + 7));

                    that.Trace($"Reading from offset: [{address - left}⋯{address - left + 7U}] {symbol} {offset}: {tmp}");
                    return tmp;
                }

                // Check all stores in debug mode againt left/right boundaries as well as tmp left/right boundaries
                void Store(int *address, Vector256<int> data) {
                    string symbol = "WTF";
                    long offset = -666;
                    var mask = (uint) MoveMask(CompareGreaterThan(data, P).AsSingle());
                    var popCount = PopCount(mask);

                    // Hairy debugging code:
                    // We first figure if this is a write to the tmp area or to the main partition
                    if (address >= left && address <= right) {
                        Debug.Assert(address >= left, $"OOB: address(0x{(ulong) address:X16}) >= left (0x{(ulong) left:X16})");
                        Debug.Assert(address + N <= right, $"OOB: address + 8U (0x{(ulong) address:X16} + 8) <= right (0x{(ulong) right:X16})");
                        // Once we're writing somewhere inside our partition, we are
                        // only ever allowed to write EITHER right of _lastLeftKosherWrite (and then increment it!)
                        // Or write just left of  _lastRightKosherWrite (and then decrement it!)
                        if (address == that._lastLeftKosherWrite + 1) {
                            that._lastLeftKosherWrite += N - popCount;
                            symbol = "🡆";
                            offset = address - left;
                        } else if (address + N == that._lastRightKosherWrite) {
                            that._lastRightKosherWrite -= popCount;
                            symbol = "🡄";
                            offset = right - (address + N - 1);
                        }
                        else {
                            Debug.Fail($"OOB: Not writing right next last kosher position: address(0x{(ulong) address:X16}), _lastLeftKosher = 0x{(ulong) that._lastLeftKosherWrite:X16}, _lastRightKosher = 0x{(ulong) that._lastRightKosherWrite:X16}");
                        }
                        that.Trace($"Storing in-place [{address - left}⋯{address - left + 7U}] {symbol} {offset}");
                    } else if (address >= that._tempStart && address < that._tempEnd) {
                        Debug.Assert(address >= that._tempStart, $"address(0x{(ulong) address:X16}) >= that._tempStart (0x{(ulong) that._tempStart:X16})");
                        Debug.Assert(address + N < that._tempEnd, $"address + 8U (0x{(ulong) address:X16} + 8) < that._tempEnd 0x{(ulong) that._tempEnd:X16})");

                        // Once we're writing somewhere inside our tmp area, we are
                        // only ever allowed to write EITHER right of _lastTempLeftKosherWrite (and then increment it!)
                        // Or write just left of  _lastTempRightKosherWrite (and then decrement it!)
                        if (address == that._lastLeftKosherWrite + 1) {
                            that._lastTempLeftKosherWrite += N - popCount;
                            symbol =  "🡆";
                            offset =  address - that._tempStart;
                        } else if (address + N == that._lastRightKosherWrite) {
                            that._lastTempRightKosherWrite -= popCount;
                            symbol =  "🡄";
                            offset =  that._tempEnd - (address + N - 1);
                        }
                        else {
                            Debug.Fail($"OOB: Not writing right next last temp kosher position: address(0x{(ulong) address:X16}), _lastTempLeftKosher = 0x{(ulong) that._lastTempLeftKosherWrite:X16}, _lastTempRightKosher = 0x{(ulong) that._lastTempRightKosherWrite:X16}");
                        }
                        that.Trace($"Storing to tmp [{address - that._tempStart}⋯{address - that._tempStart + N - 1}] {symbol} {offset}");

                    }
                    else {
                        Debug.Fail($"Address 0x{(ulong) address:X16} is completely out of bounds, WTF?");
                    }

                    Avx.Store(address, data);
                }

                Vector256<int> PermuteVar8x32(
                    Vector256<int> data,
                    Vector256<int> control)
                {
                    var popCount = PopCount((uint) MoveMask(CompareGreaterThan(data, P).AsSingle()));
                    var tmp = Avx2.PermuteVar8x32(data, control);


                    that.Trace($"Permuted: <{8 - popCount}|{popCount}>{tmp}");
                    return tmp;
                }
#endif
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining|MethodImplOptions.AggressiveOptimization)]
            static void LoadAndPartition8Vectors(int* dataPtr, Vector256<int> P, byte* pBase, ref int* writeLeftPtr, ref int* writeRightPtr)
            {
                var N = Vector256<int>.Count; // Treated as constant @ JIT time

                var L0 = LoadAlignedVector256(dataPtr + 0 * N);
                var L1 = LoadAlignedVector256(dataPtr + 1 * N);
                var L2 = LoadAlignedVector256(dataPtr + 2 * N);
                var L3 = LoadAlignedVector256(dataPtr + 3 * N);
                var L4 = LoadAlignedVector256(dataPtr + 4 * N);
                var L5 = LoadAlignedVector256(dataPtr + 5 * N);
                var L6 = LoadAlignedVector256(dataPtr + 6 * N);
                var L7 = LoadAlignedVector256(dataPtr + 7 * N);
                PartitionBlock4V(P, L0, L1, L2, L3, pBase, ref writeLeftPtr, ref writeRightPtr);
                PartitionBlock4V(P, L4, L5, L6, L7, pBase, ref writeLeftPtr, ref writeRightPtr);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining|MethodImplOptions.AggressiveOptimization)]
            static void LoadAndPartition4Vectors(int* dataPtr, Vector256<int> P, byte* pBase, ref int* writeLeft, ref int* writeRight)
            {
                var N = Vector256<int>.Count; // Treated as constant @ JIT time

                var L0 = LoadAlignedVector256(dataPtr + 0 * N);
                var L1 = LoadAlignedVector256(dataPtr + 1 * N);
                var L2 = LoadAlignedVector256(dataPtr + 2 * N);
                var L3 = LoadAlignedVector256(dataPtr + 3 * N);
                PartitionBlock4V(P, L0, L1, L2, L3, pBase, ref writeLeft, ref writeRight);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining|MethodImplOptions.AggressiveOptimization)]
            static void PartitionBlock4V(Vector256<int> P,  Vector256<int> L0, Vector256<int> L1, Vector256<int> L2,
                Vector256<int>                          L3, byte*          pBase,
                ref int*                                writeLeft,
                ref int*                                writeRight)
            {
                PartitionBlock1V(L0, P, pBase, ref writeLeft, ref writeRight);
                PartitionBlock1V(L1, P, pBase, ref writeLeft, ref writeRight);
                PartitionBlock1V(L2, P, pBase, ref writeLeft, ref writeRight);
                PartitionBlock1V(L3, P, pBase, ref writeLeft, ref writeRight);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining|MethodImplOptions.AggressiveOptimization)]
            static void PartitionBlock1V(Vector256<int> data, Vector256<int> pivot, byte* pBase, ref int* writeLeft, ref int* writeRight)
            {
                // Looks kinda silly, the (ulong) (uint) thingy right?
                // Well, it's making a yucky lemonade out of lemons is what it is.
                // This is a crappy way of making the jit generate slightly less worse code
                // due to: https://github.com/dotnet/runtime/issues/431#issuecomment-568280829
                // To summarize: VMOVMASK is mis-understood as a 32-bit write by the CoreCLR 3.x JIT.
                // It's really a 64 bit write in 64 bit mode, in other words, it clears the entire register.
                // Again, the JIT *should* be aware that the destination register just had it's top 32 bits cleared.
                // It doesn't.
                // This causes a variety of issues, here it's that GetBytePermutation* method is generated
                // with suboptimal x86 code (see above issue/comment).
                // By forcefully clearing the 32-top bits by casting to ulong, we "help" the JIT further down the road
                // and the rest of the code is generated more cleanly.
                // In other words, until the issue is resolved we "pay" with a 2-byte instruction for this useless cast
                // But this helps the JIT generate slightly better code below (saving 3 bytes).
                var mask = (ulong) (uint) MoveMask(CompareGreaterThan(data, pivot).AsSingle());
                data = PermuteVar8x32(data, GetBytePermutationAligned(pBase, mask));
                // We make sure the last use of `mask` is for this PopCount operation. Why?
                // Again, this is to get the best code generated on an Intel CPU. This round it's intel's fault, yay.
                // There's a relatively well know CPU errata where POPCNT has a false dependency on the destination operand.
                // The JIT is already aware of this, so it will clear the destination operand before emitting a POPCNT:
                // https://github.com/dotnet/coreclr/issues/19555
                // By "delaying" the PopCount to this stage, it is highly likely (I don't know why, I just know it is...)
                // that the JIT will emit a POPCNT X,X instruction, where X is now both the source and the destination
                // for PopCount. This means that there is no need for clearing the destination register (it would even be
                // an error to do so). This saves about two bytes in the instruction stream.
                var pc = -(long) (int) PopCount(mask);
                Store(writeLeft,  data);
                Store(writeRight, data);
                // I comfortably ignored having negated the PopCount result after casting to (long)
                // The reasoning behind this is that be storing the PopCount as a negative
                // while also expressing the pointer bumping (next two lines) in this very specific form that
                // it is expressed: a summation of two variables with an optional constant (that CAN be negative)
                // We are allowing the JIT to encode this as two LEA opcodes in x64: https://www.felixcloutier.com/x86/lea
                // This saves a considerable amount of space in the instruction stream, which are then exploded
                // when this block is unrolled. All in all this is has a very clear benefit in perf while decreasing code
                // size.
                // TODO: Currently the entire sorting operation generates a right-hand popcount that needs to be negated
                //       If/When I re-write it to do left-hand comparison/pop-counting we can save another two bytes
                //       for the negation operation, which will also do its share to speed things up while lowering
                //       the native code size, yay for future me!
                writeRight = writeRight + pc;
                writeLeft  = writeLeft + pc + 8;
            }

            /// <summary>
            /// Partition using Vectorized AVX2 intrinsics
            /// </summary>
            /// <param name="left">pointer (inclusive) to the first element to partition</param>
            /// <param name="right">pointer (exclusive) to the last element to partition, actually points to where the pivot before partitioning</param>
            /// <param name="hint">alignment instructions</param>
            /// <returns>Position of the pivot that was passed to the function inside the array</returns>
            [MethodImpl(MethodImplOptions.AggressiveOptimization)]
            internal int* Partition1VectorInPlace(int* left, int* right, long hint)
            {
#if DEBUG
                var that = this; // CS1673
#endif

                Stats.BumpPartitionOperations();
                Debug.Assert(right - left > SMALL_SORT_THRESHOLD_ELEMENTS);
                Dbg($"{nameof(Partition1VectorInPlace)}: [{left - _startPtr}-{right - _startPtr}]({right - left + 1})");

                Debug.Assert((((ulong) left) & 0x3) == 0);
                Debug.Assert((((ulong) right) & 0x3) == 0);
                // Vectorized double-pumped (dual-sided) partitioning:
                // We start with picking a pivot using the media-of-3 "method"
                // Once we have sensible pivot stored as the last element of the array
                // We process the array from both ends.
                //
                // To get this rolling, we first read 2 Vector256 elements from the left and
                // another 2 from the right, and store them in some temporary space
                // in order to leave enough "space" inside the vector for storing partitioned values.
                // Why 2 from each side? Because we need n+1 from each side
                // where n is the number of Vector256 elements we process in each iteration...
                // The reasoning behind the +1 is because of the way we decide from *which*
                // side to read, we may end up reading up to one more vector from any given side
                // and writing it in its entirety to the opposite side (this becomes slightly clearer
                // when reading the code below...)
                // Conceptually, the bulk of the processing looks like this after clearing out some initial
                // space as described above:

                // [.............................................................................]
                //  ^wl          ^rl                                               rr^        wr^
                // Where:
                // wl = writeLeft
                // rl = readLeft
                // rr = readRight
                // wr = writeRight

                // In every iteration, we select what side to read from based on how much
                // space is left between head read/write pointer on each side...
                // We read from where there is a smaller gap, e.g. that side
                // that is closer to the unfortunate possibility of its write head overwriting
                // its read head... By reading from THAT side, we're ensuring this does not happen

                // An additional unfortunate complexity we need to deal with is that the right pointer
                // must be decremented by another Vector256<T>.Count elements
                // Since the Load/Store primitives obviously accept start addresses
                var N = Vector256<int>.Count; // Treated as constant @ JIT time
                var pivot = *right;
                Dbg($"Read pivot {pivot} from [{right-left}]");
                // We do this here just in case we need to pre-align to the right
                // We end up
                *right = int.MaxValue;

                var readLeft = left;
                var readRight = right;
                var writeLeft = readLeft;
                var writeRight = readRight - N;

                var tmpStartLeft = _tempStart;
                var tmpLeft = tmpStartLeft;
                var tmpStartRight = _tempEnd;
                var tmpRight = tmpStartRight;

                // Broadcast the selected pivot
                var P = Vector256.Create(pivot);
                var pBase = BytePermTableAlignedPtr;
                tmpRight -= N;

                // the read heads always advance by 8 elements, or 32 bytes,
                // We can spend some extra time here to align the pointers
                // so they start at a cache-line boundary
                // Once that happens, we can read with Avx.LoadAlignedVector256
                // And also know for sure that our reads will never cross cache-lines
                // Otherwise, 50% of our AVX2 Loads will need to read from two cache-lines

                var leftAlign = unchecked((int) (hint & 0xFFFFFFFF));
                var rightAlign = unchecked((int) (hint >> 32));

                var preAlignedLeft = left + leftAlign;
                var preAlignedRight = right + rightAlign - N;

                // We preemptively go through the motions of
                // vectorized alignment, and at worst we re-neg
                // by not advancing the various read/tmp pointers
                // as if nothing ever happenned if the conditions
                // are wrong from vectorized alginment
                Stats.BumpVectorizedPartitionBlocks(2);
                var RT0 = LoadAlignedVector256(preAlignedRight);
                var LT0 = LoadAlignedVector256(preAlignedLeft);
                var rtMask = (uint) MoveMask(CompareGreaterThan(RT0, P).AsSingle());
                var ltMask = (uint) MoveMask(CompareGreaterThan(LT0, P).AsSingle());
                var rtPopCount = Math.Max(PopCount(rtMask), (uint) rightAlign);
                var ltPopCount = PopCount(ltMask);
                RT0 = PermuteVar8x32(RT0, GetBytePermutationAligned(pBase, rtMask));
                LT0 = PermuteVar8x32(LT0, GetBytePermutationAligned(pBase, ltMask));
                Avx.Store(tmpRight, RT0);
                Avx.Store(tmpLeft, LT0);

                var rightAlignMask = ~((rightAlign - 1) >> 31);
                var leftAlignMask = leftAlign >> 31;

                tmpRight -= rtPopCount & rightAlignMask;
                rtPopCount = 8 - rtPopCount;
                readRight += (rightAlign - N) & rightAlignMask;

                Avx.Store(tmpRight, LT0);
                tmpRight -= ltPopCount & leftAlignMask;
                ltPopCount =  8 - ltPopCount;
                tmpLeft += ltPopCount & leftAlignMask;
                tmpStartLeft += -leftAlign & leftAlignMask;
                readLeft += (leftAlign + N) & leftAlignMask;

                Avx.Store(tmpLeft,  RT0);
                tmpLeft       += rtPopCount & rightAlignMask;
                tmpStartRight -= rightAlign & rightAlignMask;

                if (leftAlign > 0) {
                    tmpRight += N;
                    readLeft = AlignLeftScalarUncommon(readLeft, pivot, ref tmpLeft, ref tmpRight);
                    tmpRight -= N;
                }

                if (rightAlign < 0) {
                    tmpRight += N;
                    readRight = AlignRightScalarUncommon(readRight, pivot, ref tmpLeft, ref tmpRight);
                    tmpRight -= N;
                }
                Debug.Assert(((ulong) readLeft & ALIGN_MASK) == 0);
                Debug.Assert(((ulong) readRight & ALIGN_MASK) == 0);

                Debug.Assert((((byte *) readRight - (byte *) readLeft) % (long) ALIGN) == 0);
                Debug.Assert((readRight -  readLeft) >= EIGTH_SLACK_PER_SIDE_IN_ELEMENTS * 2);

                Trace($"Post alignment [{readLeft - left}-{readRight - left})({readRight - readLeft})");
                Trace($"tmpLeft = {new ROS(tmpStartLeft, (int) (tmpLeft - tmpStartLeft)).Dump()}");
                Trace($"tmpRight = {new ROS(tmpRight + N, (int) (tmpStartRight - (tmpRight + N))).Dump()}");

                Stats.BumpVectorizedPartitionBlocks(2);
                // Read ahead from left+right
                LT0 = LoadAlignedVector256(readLeft  + 0*N);
                RT0 = LoadAlignedVector256(readRight - 1*N);

                //PartitionBlock1V(LT0, P, pBase, ref tmpLeft, ref tmpRight);
                //PartitionBlock1V(RT0, P, pBase, ref tmpLeft, ref tmpRight);

                // Adjust for the reading that was made above
                readLeft  += 1*N;
                readRight -= 2*N;

                ltMask = (uint) MoveMask(CompareGreaterThan(LT0, P).AsSingle());
                rtMask = (uint) MoveMask(CompareGreaterThan(RT0, P).AsSingle());

                ltPopCount = PopCount(ltMask);
                rtPopCount = PopCount(rtMask);

                LT0 = PermuteVar8x32(LT0, GetBytePermutationAligned(pBase, ltMask));
                RT0 = PermuteVar8x32(RT0, GetBytePermutationAligned(pBase, rtMask));

                Avx.Store(tmpRight, LT0);
                tmpRight -= ltPopCount;
                ltPopCount = 8 - ltPopCount;
                Avx.Store(tmpRight, RT0);
                tmpRight -= rtPopCount;
                rtPopCount = 8 - rtPopCount;
                tmpRight += N;
                Trace($"tmpRight = {new ROS(tmpRight, (int) (tmpStartRight - tmpRight)).Dump()}");

                Avx.Store(tmpLeft, LT0);
                tmpLeft += ltPopCount;
                Avx.Store(tmpLeft, RT0);
                tmpLeft += rtPopCount;
                Trace($"tmpLeft = {new ROS(tmpStartLeft, (int) (tmpLeft - tmpStartLeft)).Dump()}");
                Trace($"tmpRight - tmpLeft = {tmpRight - tmpLeft}");

                Trace($"WL:{writeLeft - left}|WR:{writeRight + 8 - left}");
                while (readRight >= readLeft) {
                    Stats.BumpScalarCompares();
                    Stats.BumpVectorizedPartitionBlocks();

                    int* nextPtr;
                    if (((byte *) readLeft   - (byte *) writeLeft) <=
                        ((byte *) writeRight - (byte *) readRight)) {
                        nextPtr   = readLeft;
                        readLeft += N;
                    } else {
                        nextPtr    = readRight;
                        readRight -= N;
                    }

                    PartitionBlock1V(LoadAlignedVector256(nextPtr), P, pBase, ref writeLeft, ref writeRight);
                }

                var boundary = writeLeft;

                // 3. Copy-back the 4 registers + remainder we partitioned in the beginning
                var leftTmpSize = (uint) (ulong) (tmpLeft - tmpStartLeft);
                Trace($"Copying back tmpLeft -> [{boundary-left}-{boundary-left+leftTmpSize})|{new ROS(tmpStartLeft, (int) leftTmpSize).Dump()}");
                Unsafe.CopyBlockUnaligned(boundary, tmpStartLeft, leftTmpSize*sizeof(int));
                boundary += leftTmpSize;
                var rightTmpSize = (uint) (ulong) (tmpStartRight - tmpRight);
                Trace($"Copying back tmpRight -> [{boundary-left}-{boundary-left+rightTmpSize})|{new ROS(tmpRight, (int) rightTmpSize).Dump()}");
                Unsafe.CopyBlockUnaligned(boundary, tmpRight, rightTmpSize*sizeof(int));

                Dbg(new ROS(left, (int) (right - left + 1)).Dump());

                // Shove to pivot back to the boundary
                var value = *boundary;
                Dbg($"Writing boundary [{boundary - left}]({value}) into pivot pos [{right - left}]");
                *right = value;
                *boundary++ = pivot;

                Dbg($"Final Boundary: {boundary - left}/{right - left + 1}");
                Dbg(new ROS(left, (int) (right - left + 1)).Dump());
                Dbg(new ROS(_startPtr, (int) Length).Dump());

                VerifyBoundaryIsCorrect(left, right, pivot, boundary);

                Debug.Assert(boundary > left);
                Debug.Assert(boundary <= right);

                return boundary;

#if DEBUG
                Vector256<int> LoadAlignedVector256(int* address) {
                    const int CACHE_LINE_SIZE = 64;
                    Debug.Assert(address >= left - 8, $"a: 0x{(ulong) address:X16}, left: 0x{(ulong) left:X16} d: {left - address}");
                    Debug.Assert(address + 8U <= right + 8, $"a: 0x{(ulong) address:X16}, right: 0x{(ulong) right:X16} d: {right - address}");
                    Debug.Assert((((ulong) address) / CACHE_LINE_SIZE) == (((ulong) address + (uint) sizeof(Vector256<int>) - 1) / CACHE_LINE_SIZE), $"0x{(ulong)address:X16}");
                    var tmp = Avx.LoadAlignedVector256(address);
                    that.Trace($"Reading from offset: [{address - left}-{address - left + 7U}]: {tmp}");
                    return tmp;
                }

                void Store(int *address, Vector256<int> boundaryData) {
                    Debug.Assert(address >= left);
                    Debug.Assert(address + 8U <= right);
                    Debug.Assert((address >= writeLeft && address + 8U <= readLeft) || (address >= readRight && address <= writeRight));
                    that.Trace($"Storing to [{address-left}-{address-left+7U}]");
                    Avx.Store(address, boundaryData);
                }
#endif
            }

            /// <summary>
            /// Called when the left hand side of the entire array does not have enough elements
            /// for us to align the memory with vectorized operations, so we do this uncommon slower alternative.
            /// Generally speaking this is probably called for all the partitioning calls on the left edge of the array
            /// </summary>
            /// <param name="readLeft"></param>
            /// <param name="pivot"></param>
            /// <param name="tmpLeft"></param>
            /// <param name="tmpRight"></param>
            /// <returns></returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            int* AlignLeftScalarUncommon(int* readLeft, int pivot, ref int* tmpLeft, ref int* tmpRight)
            {
                if (((ulong) readLeft & ALIGN_MASK) == 0)
                    return readLeft;

                var nextAlign = (int*) (((ulong) readLeft + ALIGN) & ~ALIGN_MASK);
                Trace($"post-aligning from left {nextAlign - readLeft}");
                while (readLeft < nextAlign) {
                    var v = *readLeft++;
                    Stats.BumpScalarCompares();
                    if (v <= pivot) {
                        Trace($"{v} <= {pivot} -> writing to tmpLeft [{tmpLeft - _tempStart}]");
                        *tmpLeft++ = v;
                    } else {
                        Trace($"{v} > {pivot} -> writing to tmpRight [{tmpRight - 1 - _tempStart}]");
                        *--tmpRight = v;
                    }

                }

                return readLeft;
            }

            /// <summary>
            /// Called when the right hand side of the entire array does not have enough elements
            /// for us to align the memory with vectorized operations, so we do this uncommon slower alternative.
            /// Generally speaking this is probably called for all the partitioning calls on the right edge of the array
            /// </summary>
            /// <param name="readRight"></param>
            /// <param name="pivot"></param>
            /// <param name="tmpLeft"></param>
            /// <param name="tmpRight"></param>
            /// <returns></returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            int* AlignRightScalarUncommon(int* readRight, int pivot, ref int* tmpLeft, ref int* tmpRight)
            {
                if (((ulong) readRight & ALIGN_MASK) == 0)
                    return readRight;

                var nextAlign = (int*) ((ulong) readRight & ~ALIGN_MASK);
                Trace($"post-aligning from right {readRight - nextAlign}");
                while (readRight > nextAlign) {
                    var v = *--readRight;
                    Stats.BumpScalarCompares();
                    if (v <= pivot) {
                        Trace($"{v} <= {pivot} -> writing to tmpLeft [{tmpLeft - _tempStart}]");
                        *tmpLeft++ = v;
                    } else {
                        Trace($"{v} > {pivot} -> writing to tmpRight [{tmpRight - 1 - _tempStart}]");
                        *--tmpRight = v;
                    }
                }

                return readRight;
            }

            [Conditional("DEBUG")]
            void VerifyBoundaryIsCorrect(int* left, int* right, int pivot, int* boundary)
            {
                for (var t = left; t < boundary; t++)
                    if (!(*t <= pivot)) {
                        Dbg($"depth={_depth} boundary={boundary-left}, idx = {t - left} *t({*t}) <= pivot={pivot}");
                        Debug.Assert(*t <= pivot, $"depth={_depth} boundary={boundary-left}, idx = {t - left} *t({*t}) <= pivot={pivot}");
                    }
                for (var t = boundary; t <= right; t++)
                    if (!(*t >= pivot)) {
                        Dbg($"depth={_depth} boundary={boundary-left}, idx = {t - left} *t({*t}) >= pivot={pivot}");
                        Debug.Assert(*t >= pivot, $"depth={_depth} boundary={boundary-left}, idx = {t - left} *t({*t}) >= pivot={pivot}");
                    }
            }


            void Dbg2(string d) => Console.WriteLine($"{_depth}> {d}");

            [Conditional("DEBUG")]
            void Dbg(string d) => Console.WriteLine($"{_depth}> {d}");

            [Conditional("DEBUG")]
            void Trace(string d) => Console.WriteLine($"{_depth}> {d}");
        }
    }
}
