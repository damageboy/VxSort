using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using VxSortResearch.PermutationTables;
using VxSortResearch.Statistics;
using VxSortResearch.Utils;
using static System.Runtime.Intrinsics.X86.Avx;
using static System.Runtime.Intrinsics.X86.Avx2;
using static System.Runtime.Intrinsics.X86.Popcnt;
using static VxSortResearch.PermutationTables.BitPermTables;

namespace VxSortResearch.Unstable.AVX2.Happy
{
    using ROS = ReadOnlySpan<int>;

    public static class DoublePumpOverlined
    {
        public static unsafe void Sort<T>(T[] array) where T : unmanaged, IComparable<T>
        {
            if (array == null)
                throw new ArgumentNullException(nameof(array));

            Stats.BumpSorts(nameof(DoublePumpOverlined), array.Length);
            fixed (T* p = &array[0]) {
                // Yes this looks horrid, but the C# JIT will happily elide
                // the irrelevant code per each type being compiled, so we're good
                if (typeof(T) == typeof(int)) {
                    var pInt = (int*) p;
                    var sorter = new VxSortInt32(startPtr: pInt, endPtr: pInt + array.Length - 1);
                    sorter.Sort(pInt, pInt + array.Length - 1, VxSortInt32.REALIGN_BOTH);
                }
            }
        }

        static unsafe void Swap<TX>(TX *left, TX *right) where TX : unmanaged, IComparable<TX>
        {
            var tmp = *left;
            *left = *right;
            *right = tmp;
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

        const int SLACK_PER_SIDE_IN_VECTORS = 1;
        const int SMALL_SORT_THRESHOLD_ELEMENTS = 32;
        const ulong ALIGN = 32;
        const ulong ALIGN_MASK = ALIGN - 1;

        internal unsafe struct VxSortInt32
        {
            const int SLACK_PER_SIDE_IN_ELEMENTS = SLACK_PER_SIDE_IN_VECTORS * 8;
            // We allocate the amount of slack space + up-to 2 more alignment blocks
            const int PARTITION_TMP_SIZE_IN_ELEMENTS = (int) (2 * SLACK_PER_SIDE_IN_ELEMENTS + 8 + (2 * ALIGN) / sizeof(int));

            const long REALIGN_LEFT = 0x666;
            const long REALIGN_RIGHT = 0x66600000000;
            internal const long REALIGN_BOTH = REALIGN_LEFT | REALIGN_RIGHT;
            readonly int* _startPtr;
            readonly int* _endPtr;
            readonly int* _tempStart;
            readonly int* _tempEnd;
#pragma warning disable 649
            fixed int _temp[PARTITION_TMP_SIZE_IN_ELEMENTS];
            int depth;
#pragma warning restore 649
            internal long Length => _endPtr - _startPtr + 1;

            public VxSortInt32(int* startPtr, int* endPtr) : this()
            {
                Debug.Assert(SMALL_SORT_THRESHOLD_ELEMENTS >= PARTITION_TMP_SIZE_IN_ELEMENTS);
                depth     = 0;
                _startPtr = startPtr;
                _endPtr   = endPtr;
                fixed (int* pTemp = _temp) {
                    _tempStart = pTemp;
                    _tempEnd   = pTemp + PARTITION_TMP_SIZE_IN_ELEMENTS;
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveOptimization)]
            internal void Sort(int* left, int* right, long hint)
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

                // Go to insertion sort below this threshold
                if (length <= SMALL_SORT_THRESHOLD_ELEMENTS) {
                    Dbg($"Going for Insertion Sort on [{left - _startPtr} -> {right - _startPtr - 1}]");
                    InsertionSort(left, right);
                    return;
                }

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

                if ((hint & REALIGN_LEFT) == REALIGN_LEFT) {
                    Trace("Recalculating left alignment");
                    // Alignment flow:
                    // * Calculate pre-alignment on the left
                    // * See it would cause us an out-of bounds read
                    // * Since we'd like to avoid that, we adjust for post-alignment
                    // * There are no branches since we do branch->arithmetic
                    hint &= unchecked((long) 0xFFFFFFFF00000000UL);
                    var preAlignedLeft = (int*)  ((ulong) left & ~ALIGN_MASK);
                    var cannotPreAlignLeft = (preAlignedLeft - _startPtr) >> 63;
                    hint |= (preAlignedLeft - left) + (8 & cannotPreAlignLeft);
                }

                if ((hint & REALIGN_RIGHT) == REALIGN_RIGHT) {
                    Trace("Recalculating right alignment");
                    // right is pointing just PAST the last element we intend to partition (where we also store the pivot)
                    // So we calculate alignment based on right - 1, and YES: I am casting to ulong before doing the -1, this
                    // is intentional since the whole thing is either aligned to 32 bytes or not, so decrementing the POINTER value
                    // by 1 is sufficient for the alignment, an the JIT sucks at this anyway
                    var preAlignedRight = (int*) (((ulong) right - 1 & ~ALIGN_MASK) + ALIGN);
                    var cannotPreAlignRight = (_endPtr - preAlignedRight) >> 63;
                    hint &= 0xFFFFFFFF;
                    hint |= (preAlignedRight - right - (8 & cannotPreAlignRight)) << 32;
                }

                Debug.Assert(((ulong) (left + (hint & 0xFFFFFFFF)) & ALIGN_MASK) == 0);
                Debug.Assert(((ulong) (right + (hint >> 32)) & ALIGN_MASK) == 0);

                // Compute median-of-three, of:
                // the first, mid and one before last elements
                mid = left + ((right - left) / 2);
                SwapIfGreater(left, mid);
                SwapIfGreater(left, right - 1);
                SwapIfGreater(mid, right - 1);

                // Pivot is mid, place it in the right hand side
                Dbg($"Pivot is {*mid}, storing in [{right - left}]");
                Swap(mid, right);

                var sep = VectorizedPartitionInPlace(left, right, hint);

                Stats.BumpDepth(1);
                depth++;
                Sort(left, sep - 2, hint | REALIGN_RIGHT);
                Sort(sep, right, hint | REALIGN_LEFT);
                Stats.BumpDepth(-1);
                depth--;
            }

            /// <summary>
            /// Partition using Vectorized AVX2 intrinsics
            /// </summary>
            /// <param name="left">pointer (inclusive) to the first element to partition</param>
            /// <param name="right">pointer (exclusive) to the last element to partition, actually points to where the pivot before partitioning</param>
            /// <param name="hint">alignment instructions</param>
            /// <returns>Position of the pivot that was passed to the function inside the array</returns>
            [MethodImpl(MethodImplOptions.AggressiveOptimization)]
            internal int* VectorizedPartitionInPlace(int* left, int* right, long hint)
            {
#if DEBUG
                var that = this; // CS1673
#endif

                Stats.BumpPartitionOperations();
                Debug.Assert(right - left > SMALL_SORT_THRESHOLD_ELEMENTS);
                Dbg($"{nameof(VectorizedPartitionInPlace)}: [{left - _startPtr}-{right - _startPtr}]({right - left + 1})");

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
                // We do this here just in case we need to pre-align to the right
                // We end up
                *right = int.MaxValue;

                var readLeft = left;
                var readRight = right;
                var writeLeft = left;
                var writeRight = right - N;

                var tmpStartLeft = _tempStart;
                var tmpLeft = tmpStartLeft;
                var tmpStartRight = _tempEnd;
                var tmpRight = tmpStartRight;

                // Broadcast the selected pivot
                var P = Vector256.Create(pivot);
                var pBase = Int32PermTables.IntPermTableAlignedPtr;
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

                // Read overlapped data from right (includes re-reading the pivot)
                var RT0 = LoadAlignedVector256(preAlignedRight);
                var LT0 = LoadAlignedVector256(preAlignedLeft);
                var rtMask = (uint) MoveMask(CompareGreaterThan(RT0, P).AsSingle());
                var ltMask = (uint) MoveMask(CompareGreaterThan(LT0, P).AsSingle());
                var rtPopCount = Math.Max(PopCount(rtMask), (uint) rightAlign);
                var ltPopCount = PopCount(ltMask);
                RT0 = PermuteVar8x32(RT0, Int32PermTables.GetIntPermutationAligned(pBase, rtMask));
                LT0 = PermuteVar8x32(LT0, Int32PermTables.GetIntPermutationAligned(pBase, ltMask));
                Avx.Store(tmpRight, RT0);
                Avx.Store(tmpLeft, LT0);

                var rai = ~((rightAlign - 1) >> 31);
                var lai = leftAlign >> 31;

                tmpRight -= rtPopCount & rai;
                rtPopCount = 8 - rtPopCount;
                readRight += (rightAlign - N) & rai;

                Avx.Store(tmpRight, LT0);
                tmpRight -= ltPopCount & lai;
                ltPopCount =  8 - ltPopCount;
                tmpLeft += ltPopCount & lai;
                tmpStartLeft += -leftAlign & lai;
                readLeft += (leftAlign + N) & lai;

                Avx.Store(tmpLeft,  RT0);
                tmpLeft       += rtPopCount & rai;
                tmpStartRight -= rightAlign & rai;

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

                Trace($"Post alignment [{readLeft - left}-{readRight - left})({readRight - readLeft}) into tmp  [{tmpLeft - tmpStartLeft}-{tmpRight - 1- tmpStartLeft}]({tmpRight-tmpLeft})");
                Trace($"tmpLeft = {new ROS(tmpStartLeft, (int) (tmpLeft - tmpStartLeft)).Dump()}");
                Trace($"tmpRight = {new ROS(tmpRight + N, (int) (tmpStartRight - (tmpRight + N))).Dump()}");

                Stats.BumpVectorizedPartitionBlocks(2);

                // Read ahead from left+right
                LT0 = LoadAlignedVector256(readLeft  + 0*N);
                RT0 = LoadAlignedVector256(readRight - 1*N);

                // Adjust for the reading that was made above
                readLeft  += 1*N;
                readRight -= 2*N;

                ltMask = (uint) MoveMask(CompareGreaterThan(LT0, P).AsSingle());
                rtMask = (uint) MoveMask(CompareGreaterThan(RT0, P).AsSingle());

                ltPopCount = PopCount(ltMask);
                rtPopCount = PopCount(rtMask);

                LT0 = PermuteVar8x32(LT0, Int32PermTables.GetIntPermutationAligned(pBase, ltMask));
                RT0 = PermuteVar8x32(RT0, Int32PermTables.GetIntPermutationAligned(pBase, rtMask));

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

                    var current = LoadAlignedVector256(nextPtr);
                    var mask = (uint) MoveMask(CompareGreaterThan(current, P).AsSingle());
                    current = PermuteVar8x32(current, Int32PermTables.GetIntPermutationAligned(pBase, mask));

                    Debug.Assert(readLeft - writeLeft   >= N);
                    Debug.Assert(writeRight - readRight >= N);
                    Store(writeLeft,  current);
                    Store(writeRight, current);

                    var popCount = PopCount(mask) << 2;
                    Trace($"Permuted: {current}|{8U - (popCount >> 2)}|{popCount >> 2}");
                    writeRight = (int *) ((byte *) writeRight - popCount);
                    writeLeft  = (int *) ((byte *) writeLeft  + (8U << 2) - popCount);
                }

                var boundary = writeLeft;

                // 3. Copy-back the 4 registers + remainder we partitioned in the beginning
                var leftTmpSize = (int) (tmpLeft - tmpStartLeft);
                Trace($"Copying back tmpLeft -> [{boundary-left}-{boundary-left+leftTmpSize})|{new ROS(tmpStartLeft, leftTmpSize).Dump()}");
                new ROS(tmpStartLeft, leftTmpSize).CopyTo(new Span<int>(boundary, leftTmpSize));
                boundary += leftTmpSize;
                var rightTmpSize = (int) (tmpStartRight - tmpRight);
                Trace($"Copying back tmpRight -> [{boundary-left}-{boundary-left+rightTmpSize})|{new ROS(tmpRight, rightTmpSize).Dump()}");
                new ROS(tmpRight, rightTmpSize).CopyTo(new Span<int>(boundary, rightTmpSize));

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
            [MethodImpl(MethodImplOptions.NoInlining)]
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
                        Trace($"Read: {v} > {pivot} -> writing to tmpRight [{tmpRight - 1 - _tempStart}]");
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
            [MethodImpl(MethodImplOptions.NoInlining)]
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
                        Trace($"Read: {v} > {pivot} -> writing to tmpRight [{tmpRight - 1 - _tempStart}]");
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
                        Dbg($"depth={depth} boundary={boundary-left}, idx = {t - left} *t({*t}) <= pivot={pivot}");
                        Debug.Assert(*t <= pivot, $"depth={depth} boundary={boundary-left}, idx = {t - left} *t({*t}) <= pivot={pivot}");
                    }
                for (var t = boundary; t <= right; t++)
                    if (!(*t >= pivot)) {
                        Dbg($"depth={depth} boundary={boundary-left}, idx = {t - left} *t({*t}) >= pivot={pivot}");
                        Debug.Assert(*t >= pivot, $"depth={depth} boundary={boundary-left}, idx = {t - left} *t({*t}) >= pivot={pivot}");
                    }
            }

            [Conditional("DEBUG")]
            void Dbg(string d) => Console.WriteLine($"{depth}> {d}");

            [Conditional("DEBUG")]
            void Trace(string d) => Console.WriteLine($"{depth}> {d}");
        }
    }
}
