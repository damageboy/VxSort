using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using VxSortResearch.Statistics;
using VxSortResearch.Unstable.SmallSort;
using VxSortResearch.Utils;
using static System.Runtime.Intrinsics.Vector128;
using static System.Runtime.Intrinsics.X86.Avx;
using static System.Runtime.Intrinsics.X86.Avx2;
using static System.Runtime.Intrinsics.X86.Popcnt;
using static System.Runtime.Intrinsics.X86.Popcnt.X64;
using static System.Runtime.Intrinsics.X86.Bmi2.X64;
using static VxSortResearch.PermutationTables.BitPermTables;
using static VxSortResearch.PermutationTables.Int32PermTables;

namespace VxSortResearch.Unstable.AVX2.Sad
{
    using ROS = ReadOnlySpan<int>;

    public static class DoublePumpOverlinedUnroll8YoDawgPrefixSumBitonicSort
    {
        public static unsafe void Sort<T>(T[] array) where T : unmanaged, IComparable<T>
        {
            if (array == null)
                throw new ArgumentNullException(nameof(array));

            Stats.BumpSorts(nameof(DoublePumpOverlinedUnroll8YoDawgBitonicSort), array.Length);
            fixed (T* p = &array[0]) {
                // Yes this looks horrid, but the C# JIT will happily elide
                // the irrelevant code per each type being compiled, so we're good
                if (typeof(T) == typeof(int)) {
                    var left = (int*) p;
                    var sorter = new VxSortInt32(startPtr: left, endPtr: left + array.Length - 1);
                    sorter.Sort(left, left + array.Length - 1, VxSortInt32.REALIGN_BOTH);
                }
            }
        }

        static unsafe void Swap<TX>(TX *left, TX *right) where TX : unmanaged, IComparable<TX>
        {
            var tmp = *left;
            *left  = *right;
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

        const int SLACK_PER_SIDE_IN_VECTORS = 8;
        const int SMALL_SORT_THRESHOLD_ELEMENTS = 96;
        const ulong ALIGN = 32;
        const ulong ALIGN_MASK = ALIGN - 1;

        internal unsafe struct VxSortInt32
        {
            const int SLACK_PER_SIDE_IN_ELEMENTS = SLACK_PER_SIDE_IN_VECTORS * 8;
            const int HALF_SLACK_PER_SIDE_IN_ELEMENTS = (SLACK_PER_SIDE_IN_VECTORS / 2)  * 8;
            // The formula goes like this:
            // 2 x the number of slack elements on each side +
            // 2 x amount of maximal bytes needed for alignment (32)
            // 8 more elements since we write with 8-way stores from both ends of the temporary area
            //   and we must make sure to accidentaly over-write from left -> right or vice-versa right on that edge...
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

            static ReadOnlySpan<byte> PopCountSumComplement => new byte[] {
                //32, 0, 0, 0, 64, 0, 0, 0, 96, 0, 0, 0, 128, 0, 0, 0, 160, 0, 0, 0, 192, 0, 0, 0, 224, 0, 0, 0, 0, 1, 0, 0
                8, 0, 0, 0, 16, 0, 0, 0, 24, 0, 0, 0, 32, 0, 0, 0, 40, 0, 0, 0, 48, 0, 0, 0, 56, 0, 0, 0, 64, 0, 0, 0
            };

            internal static ReadOnlySpan<byte> TransposeShuffler => new byte[]  {
                //03, 02, 01, 00| 07, 06, 05, 04| 11, 10, 09, 08| 15, 14, 13, 12| 00, 01, 02, 03| 04, 05, 06, 07| 08, 09, 10, 11, 12, 13, 14, 15
                00, 04, 08, 12, 01, 05, 09, 13, 02, 06, 10, 14, 03, 07, 11, 15, 00, 04, 08, 12, 01, 05, 09, 13, 02, 06, 10, 14, 03, 07, 11, 15
            };

            internal static readonly uint* _popCountSumComplementPtr;
            internal static readonly byte* TransposeShufflerPtr;

            static VxSortInt32()
            {
                fixed (byte* p = PopCountSumComplement)
                    _popCountSumComplementPtr = (uint *) p;

                fixed (byte* p = TransposeShuffler)
                    TransposeShufflerPtr = (byte *) p;
            }

            public VxSortInt32(int* startPtr, int* endPtr) : this()
            {
                Debug.Assert(SMALL_SORT_THRESHOLD_ELEMENTS % 16 == 0);

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
                _lastLeftKosherWrite = left - 1;
                _lastRightKosherWrite = right;
                _lastTempLeftKosherWrite = _tempStart - 1;
                _lastTempRightKosherWrite = _tempEnd;
#endif
                var sep = length < PARTITION_TMP_SIZE_IN_ELEMENTS ?
                    Partition1VectorInPlace(left, right, hint) :
                    Partition8VectorsInPlace(left, right, hint);

                Dbg($"Before Partitioning: {new ROS(left, (int) (right - left) + 1).Dump()}");

                Sort(left, sep - 2, hint | REALIGN_RIGHT);
                Sort(sep, right, hint | REALIGN_LEFT);
                Stats.BumpDepth(-1);
                _depth--;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining|MethodImplOptions.AggressiveOptimization)]
            internal static void Transpose(Vector256<byte> shuffler,
                                           ref Vector256<uint> p0_3, ref Vector256<uint> p4_7,
                                           Vector256<uint> data)
            {
                var t1 = Shuffle(data.AsByte(), shuffler).AsUInt32();
                var t2 = Shuffle(ShiftRightLogical(data, 4).AsByte(), shuffler).AsUInt32();
                p0_3 = Permute2x128(t1, t2, 0b_00_10_00_00);
                p4_7 = Permute2x128(t1, t2, 0b_00_11_00_01);
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
                Dbg($"{nameof(Partition8VectorsInPlace)}: [{left - _startPtr}-{right - _startPtr}]({right - left + 1})");

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
                var pBase = BitPermTablePtr;
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
                RT0 = PermuteVar8x32(RT0, GetBitPermutation(pBase, rtMask));
                LT0 = PermuteVar8x32(LT0, GetBitPermutation(pBase, ltMask));
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
                Partition4VectorsToStack(readLeft,                               pivot, ref tmpLeft, ref tmpRight);
                Partition4VectorsToStack(readLeft + 4 * N, pivot, ref tmpLeft, ref tmpRight);

                Dbg($"Partition 8 Vectors on the right [{readRight - SLACK_PER_SIDE_IN_ELEMENTS - left}⋯{readRight - left - 1}]");
                Partition4VectorsToStack(readRight - SLACK_PER_SIDE_IN_ELEMENTS, pivot, ref tmpLeft, ref tmpRight);
                Partition4VectorsToStack(readRight - 4 * N, pivot, ref tmpLeft, ref tmpRight);
                tmpRight += N;

                // Adjust for the reading that was made above
                readLeft  += SLACK_PER_SIDE_IN_ELEMENTS;
                readRight -= SLACK_PER_SIDE_IN_ELEMENTS * 2;

                Trace($"Post slack [{readLeft - left}-{readRight + SLACK_PER_SIDE_IN_ELEMENTS - left})({readRight + SLACK_PER_SIDE_IN_ELEMENTS - readLeft})");
                Trace($"tmpLeft = {new ROS(tmpStartLeft, (int) (tmpLeft - tmpStartLeft)).Dump()}");
                Trace($"tmpRight = {new ROS(tmpRight, (int) (tmpStartRight - tmpRight)).Dump()}");
                Trace($"Temp space left: tmpRight - tmpLeft = {tmpRight - tmpLeft}");

                var writeRight = crappyWriteRight;

                var nextSideIsRight = false;
                var popCountSumComplement  = LoadDquVector256((uint *) _popCountSumComplementPtr);
                var shuffler = LoadDquVector256(TransposeShufflerPtr);
                pBase = BitWithInterleavedPopCountPermTablePtr;

                var pc = stackalloc uint[16];
                var p0_3 = Vector256<uint>.Zero;
                var p4_7 = Vector256<uint>.Zero;

                while (readLeft < readRight) {
                    Stats.BumpScalarCompares();
                    Stats.BumpVectorizedPartitionBlocks(8);

                    Trace($"8x: RL:{readLeft - left}|RR:{readRight + SLACK_PER_SIDE_IN_ELEMENTS - left}|{readRight + SLACK_PER_SIDE_IN_ELEMENTS - readLeft}");
                    Trace($"8x: WL:{writeLeft - left}({readLeft - writeLeft})|WR:{writeRight + N - left}({writeRight + N - (readRight + SLACK_PER_SIDE_IN_ELEMENTS)})");
                    int* nextPtr;
                    if (nextSideIsRight) {
                        nextPtr   =  readRight;
                        readRight -= SLACK_PER_SIDE_IN_ELEMENTS;
                    }
                    else {
                        nextPtr  =  readLeft;
                        readLeft += SLACK_PER_SIDE_IN_ELEMENTS;
                    }

                    Debug.Assert(readLeft - writeLeft >= SLACK_PER_SIDE_IN_ELEMENTS,   $"left head overwrite {readLeft - writeLeft}");
                    Debug.Assert(writeRight - readRight >= SLACK_PER_SIDE_IN_ELEMENTS, $"right head overwrite {writeRight - readRight}");

                    var L0 = LoadAlignedVector256(nextPtr + 0*N);
                    var L1 = LoadAlignedVector256(nextPtr + 1*N);
                    var L2 = LoadAlignedVector256(nextPtr + 2*N);
                    var L3 = LoadAlignedVector256(nextPtr + 3*N);
                    var L4 = LoadAlignedVector256(nextPtr + 4*N);
                    var L5 = LoadAlignedVector256(nextPtr + 5*N);
                    var L6 = LoadAlignedVector256(nextPtr + 6*N);
                    var L7 = LoadAlignedVector256(nextPtr + 7*N);

                    ulong maskyMcMaskFace;

                    // Generate an 8-element mask in one 64 bit integer
                    // Yes, casting to uint only to cast to ulong seems weird, but this is c# for you:
                    // https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/conversions#explicit-numeric-conversions
                    // * Casting to uint in "unchecked" mode is a reinterpret-cast, nothing happens at the code level:
                    //   "If the source type is the same size as the destination type, then the source value is treated as a value of the destination type."
                    // * casting uint -> ulong means the JIT doesn't generate a sign extension opcode,
                    //   which would not happen had we not done the uint cast beforehand...:
                    //   "If the source type is smaller than the destination type... zero-extension is used if the
                    //    source type is unsigned. The result is then treated as a value of the destination type."
                    maskyMcMaskFace  = (ulong) (uint) MoveMask(CompareGreaterThan(L0, P).AsSingle()) << 00;
                    maskyMcMaskFace |= (ulong) (uint) MoveMask(CompareGreaterThan(L1, P).AsSingle()) << 08;
                    maskyMcMaskFace |= (ulong) (uint) MoveMask(CompareGreaterThan(L2, P).AsSingle()) << 16;
                    maskyMcMaskFace |= (ulong) (uint) MoveMask(CompareGreaterThan(L3, P).AsSingle()) << 24;
                    maskyMcMaskFace |= (ulong) (uint) MoveMask(CompareGreaterThan(L4, P).AsSingle()) << 32;
                    maskyMcMaskFace |= (ulong) (uint) MoveMask(CompareGreaterThan(L5, P).AsSingle()) << 40;
                    maskyMcMaskFace |= (ulong) (uint) MoveMask(CompareGreaterThan(L6, P).AsSingle()) << 48;
                    maskyMcMaskFace |= (ulong) (uint) MoveMask(CompareGreaterThan(L7, P).AsSingle()) << 56;

                    // Just count it all at once
                    var totalRight = PopCount(maskyMcMaskFace);
                    Trace($"Total Permuted L|R: {SLACK_PER_SIDE_IN_ELEMENTS - totalRight}|{totalRight}");
                    writeRight -= totalRight;
                    // Calculate the next side ASAP
                    nextSideIsRight = (byte*) writeRight - (byte*) readRight <
                                      (2 * SLACK_PER_SIDE_IN_ELEMENTS - N) * sizeof(int);
                    Dbg($"Next side is: {(nextSideIsRight ? "RIGHT" : "LEFT")}");

                    // Load 8 permutation vectors+precalculated pop-counts at once
                    var permutations = GatherVector256(
                        pBase,
                        ConvertToVector256Int32(CreateScalarUnsafe(maskyMcMaskFace).AsByte()),
                        sizeof(int));

                    // We've just loaded 8(!) permutation VECTORS (in packed form) into one Vector256
                    // But each permutation vector is "grouped" within its own 32-bit element in that Vector256.
                    // We need to perform a 256 bit permutation operation across that entire vector to "transpose"
                    // all 64 values into the placement expected by PermuteVar8x32()/VPERMD.
                    // To perform that transformation efficiently, we devise a scheme where we spread the permutation
                    // values WITHIN a single 32-bit value in a very controlled way, for a very good reason:
                    // By optimizing the placement of each element *inside* a single uint we end up using fewer
                    // VECTORIZED instructions to permute those values (5 insns to transpose 64x3-bit values!).
                    // Each element takes up 4 bits of space, but uses 3 bits of "content", total of 8x3=24bits.
                    // We're left with 8 unused bits, where we store a pre-calculated POPCNT value because why not.
                    // The elected layout is:
                    // MSB             24             16             8              LSB
                    //  31|       ⋮      |       ⋮      |       ⋮      |       ⋮      |0
                    //    | p₃ a₇ ⋮ x a₃ | p₂ a₆ ⋮ x a₂ | p₁ a₅ ⋮ x a₁ | p₀ a₄ ⋮ x a₀ |
                    // Legend:
                    // ⋮       Nibble boundaries
                    // |       Byte boundary
                    // a₀₋₇    Permutation values, each taking up 3 bits of content
                    // x       Empty bits, don't care, not used right now
                    // p₀₋₃    The popcount, in the form of 4 bits spread out throughout the 32-bit value
                    //         in positions: 7,15,23,31. To be extracted with MoveMask()/VPMOVMSKB
                    var popCounts =
                        ConvertToVector256Int32(CreateScalarUnsafe(ParallelBitDeposit(
                                    (uint) MoveMask(permutations.AsSByte()),
                                    0x0F_0F_0F_0F_0F_0F_0F_0F)).AsByte()).AsUInt32();
                    var offsets = PrefixSum<uint>.SumVectorized(popCounts);
                    Dbg($"Precomputed leftOffsets: {offsets}");
                    Avx.Store(pc, offsets);
                    offsets = Subtract(popCountSumComplement, offsets);
                    Avx.Store(pc+8, offsets);
                    Dbg($"Precomputed rightffsets: {offsets}");

                    Transpose(shuffler,  ref p0_3, ref p4_7, permutations);
                    L0 = PermuteVar8x32(L0, p0_3.AsInt32());
                    L1 = PermuteVar8x32(L1, ShiftRightLogical(p0_3.AsInt32(), 08));
                    L2 = PermuteVar8x32(L2, ShiftRightLogical(p0_3.AsInt32(), 16));
                    L3 = PermuteVar8x32(L3, ShiftRightLogical(p0_3.AsInt32(), 24));
                    L4 = PermuteVar8x32(L4, p4_7.AsInt32());
                    L5 = PermuteVar8x32(L5, ShiftRightLogical(p4_7.AsInt32(), 08));
                    L6 = PermuteVar8x32(L6, ShiftRightLogical(p4_7.AsInt32(), 16));
                    L7 = PermuteVar8x32(L7, ShiftRightLogical(p4_7.AsInt32(), 24));

                    // pc[7], maybe pc[6] should be promoted to a register
                    Store(writeLeft,                   L0);
                    Store(writeLeft  + pc[00], L1);
                    Store(writeLeft  + pc[01], L2);
                    Store(writeLeft  + pc[02], L3);
                    Store(writeLeft  + pc[03], L4);
                    Store(writeLeft  + pc[04], L5);
                    Store(writeLeft  + pc[05], L6);
                    Store(writeLeft  + pc[06], L7);
                    Store(writeRight + pc[15], L7);
                    Store(writeRight + pc[14], L6);
                    Store(writeRight + pc[13], L5);
                    Store(writeRight + pc[12], L4);
                    Store(writeRight + pc[11], L3);
                    Store(writeRight + pc[10], L2);
                    Store(writeRight + pc[09], L1);
                    Store(writeRight + pc[08], L0);
                    writeLeft += SLACK_PER_SIDE_IN_ELEMENTS - totalRight;

                }

                readRight += HALF_SLACK_PER_SIDE_IN_ELEMENTS;

                pBase = BitPermTablePtr;
                while (readLeft < readRight) {
                    Stats.BumpScalarCompares();
                    Stats.BumpVectorizedPartitionBlocks(4);

                    Trace($"4x: RL:{readLeft - left}|RR:{readRight + HALF_SLACK_PER_SIDE_IN_ELEMENTS - left}|{readRight + HALF_SLACK_PER_SIDE_IN_ELEMENTS - readLeft}");
                    Trace($"4x: WL:{writeLeft - left}({readLeft - writeLeft})|WR:{writeRight + N - left}({writeRight + N - (readRight + HALF_SLACK_PER_SIDE_IN_ELEMENTS)})");

                    int* nextPtr;
                    if ((byte *) writeRight - (byte *) readRight  < (2*HALF_SLACK_PER_SIDE_IN_ELEMENTS - N) * sizeof(int)) {
                        nextPtr   =  readRight;
                        readRight -= HALF_SLACK_PER_SIDE_IN_ELEMENTS;
                    }
                    else {
                        nextPtr  =  readLeft;
                        readLeft += HALF_SLACK_PER_SIDE_IN_ELEMENTS;
                    }

                    Debug.Assert(readLeft - writeLeft >= HALF_SLACK_PER_SIDE_IN_ELEMENTS,   $"left head overwrite {readLeft - writeLeft}");
                    Debug.Assert(writeRight - readRight >= HALF_SLACK_PER_SIDE_IN_ELEMENTS, $"right head overwrite {writeRight - readRight}");

                    var L0 = LoadAlignedVector256(nextPtr + 0 * N);
                    var L1 = LoadAlignedVector256(nextPtr + 1 * N);
                    var L2 = LoadAlignedVector256(nextPtr + 2 * N);
                    var L3 = LoadAlignedVector256(nextPtr + 3 * N);

                    var m0 = (uint) MoveMask(CompareGreaterThan(L0, P).AsSingle());
                    var m1 = (uint) MoveMask(CompareGreaterThan(L1, P).AsSingle());
                    var m2 = (uint) MoveMask(CompareGreaterThan(L2, P).AsSingle());
                    var m3 = (uint) MoveMask(CompareGreaterThan(L3, P).AsSingle());

                    L0 = PermuteVar8x32(L0, GetBitPermutation(pBase, m0));
                    L1 = PermuteVar8x32(L1, GetBitPermutation(pBase, m1));
                    L2 = PermuteVar8x32(L2, GetBitPermutation(pBase, m2));
                    L3 = PermuteVar8x32(L3, GetBitPermutation(pBase, m3));

                    var pc0 = PopCount(m0) << 2;
                    var pc1 = PopCount(m1) << 2;
                    var pc2 = PopCount(m2) << 2;
                    var pc3 = PopCount(m3) << 2;
                    Trace($"Total Permuted Left: {(SLACK_PER_SIDE_IN_ELEMENTS * 4) - (pc0+pc1+pc2+pc3)>>2}|Total Permuted Right: {(pc0+pc1+pc2+pc3)>>2}");

                    Store(writeRight, L0);
                    writeRight = (int*) ((byte*) writeRight - pc0);
                    pc0        = (8 << 2) - pc0;
                    Store(writeRight, L1);
                    writeRight = (int*) ((byte*) writeRight - pc1);
                    pc1        = (8 << 2) - pc1;
                    Store(writeRight, L2);
                    writeRight = (int*) ((byte*) writeRight - pc2);
                    pc2        = (8 << 2) - pc2;
                    Store(writeRight, L3);
                    writeRight = (int*) ((byte*) writeRight - pc3);
                    pc3        = (8 << 2) - pc3;

                    Store(writeLeft, L0);
                    writeLeft = (int *) ((byte *) writeLeft + pc0);
                    Store(writeLeft, L1);
                    writeLeft = (int *) ((byte *) writeLeft + pc1);
                    Store(writeLeft, L2);
                    writeLeft = (int *) ((byte *) writeLeft + pc2);
                    Store(writeLeft, L3);
                    writeLeft = (int *) ((byte *) writeLeft + pc3);
                }

                readRight += HALF_SLACK_PER_SIDE_IN_ELEMENTS - N;

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

                    var current = LoadAlignedVector256(nextPtr);
                    var mask = (uint) MoveMask(CompareGreaterThan(current, P).AsSingle());
                    current = PermuteVar8x32(current, GetBitPermutation(pBase, mask));

                    Debug.Assert(readLeft - writeLeft   >= N);
                    Debug.Assert(writeRight - readRight >= N);
                    Store(writeLeft,  current);
                    Store(writeRight, current);

                    var popCount = PopCount(mask) << 2;
                    writeRight = (int *) ((byte *) writeRight - popCount);
                    writeLeft  = (int *) ((byte *) writeLeft  + (8U << 2) - popCount);
                    Trace($"WL:{writeLeft - left}|WR:{writeRight + 8 - left}");
                }

                var boundary = writeLeft;

                // 3. Copy-back the 4 registers + remainder we partitioned in the beginning
                var leftTmpSize = (int) (tmpLeft - tmpStartLeft);
                Trace($"Copying back tmpLeft 🡆 [{boundary-left}-{boundary-left+leftTmpSize})|{new ROS(tmpStartLeft, leftTmpSize).Dump()}");
                new ROS(tmpStartLeft, leftTmpSize).CopyTo(new Span<int>(boundary, leftTmpSize));
                boundary += leftTmpSize;
                var rightTmpSize = (int) (tmpStartRight - tmpRight);
                Trace($"Copying back tmpRight 🡆 [{boundary-left}-{boundary-left+rightTmpSize})|{new ROS(tmpRight, rightTmpSize).Dump()}");
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

                Dbg("----------------------------------------------------------------------");

                return boundary;

#if DEBUG
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

                // Check all stores in debug mode against left/right boundaries as well as tmp left/right boundaries
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
                    var mask = (uint) MoveMask(CompareGreaterThan(data, P).AsSingle());

                    var popCount = PopCount(mask);

                    var expectedPermutation =
                        GetIntPermutationAligned(IntPermTableAlignedPtr, mask);
                    var cleanControl = And(control, Vector256.Create(0x7));
                    Debug.Assert(MoveMask(Avx2.CompareEqual(expectedPermutation, cleanControl).AsSingle()) == 0xFF);
                    that.Dbg($"Expecting permute control: {expectedPermutation}");
                    that.Dbg($"Actual permute control   : {cleanControl}");

                    var tmp = Avx2.PermuteVar8x32(data, control);
                    that.Trace($"Permuted: <{8 - popCount}|{popCount}>{tmp}");
                    return tmp;
                }
#endif
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
                //var N = Vector256<int>.Count; // Treated as constant @ JIT time
                const int N = 8;
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
                var pBase = IntPermTableAlignedPtr;
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
                RT0 = PermuteVar8x32(RT0, GetIntPermutationAligned(pBase, rtMask));
                LT0 = PermuteVar8x32(LT0, GetIntPermutationAligned(pBase, ltMask));
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

                LT0 = PermuteVar8x32(LT0, GetIntPermutationAligned(pBase, ltMask));
                RT0 = PermuteVar8x32(RT0, GetIntPermutationAligned(pBase, rtMask));

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
                    current = PermuteVar8x32(current, GetIntPermutationAligned(pBase, mask));

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

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static void Partition4VectorsToStack(int* p, int pivot, ref int* tmpLeft, ref int* tmpRight)
            {
                var N = Vector256<int>.Count; // Treated as constant @ JIT time
                var pBase = IntPermTableAlignedPtr;

                var P = Vector256.Create(pivot);

                // Read-ahead 5 Vector256<int> from p
                var L0 = LoadAlignedVector256(p + 0 * N);
                var L1 = LoadAlignedVector256(p + 1 * N);
                var L2 = LoadAlignedVector256(p + 2 * N);
                var L3 = LoadAlignedVector256(p + 3 * N);

                var m0 = (uint) MoveMask(CompareGreaterThan(L0, P).AsSingle());
                var m1 = (uint) MoveMask(CompareGreaterThan(L1, P).AsSingle());
                var m2 = (uint) MoveMask(CompareGreaterThan(L2, P).AsSingle());
                var m3 = (uint) MoveMask(CompareGreaterThan(L3, P).AsSingle());

                var pc0 = PopCount(m0) << 2;
                var pc1 = PopCount(m1) << 2;
                var pc2 = PopCount(m2) << 2;
                var pc3 = PopCount(m3) << 2;

                L0 = PermuteVar8x32(L0, GetIntPermutationAligned(pBase, m0));
                L1 = PermuteVar8x32(L1, GetIntPermutationAligned(pBase, m1));
                L2 = PermuteVar8x32(L2, GetIntPermutationAligned(pBase, m2));
                L3 = PermuteVar8x32(L3, GetIntPermutationAligned(pBase, m3));

                Store(tmpRight, L0);
                tmpRight = (int*) ((byte*) tmpRight - pc0);
                pc0      = 32 - pc0;
                Store(tmpRight, L1);
                tmpRight = (int*) ((byte*) tmpRight - pc1);
                pc1      = 32 - pc1;
                Store(tmpRight, L2);
                tmpRight = (int*) ((byte*) tmpRight - pc2);
                pc2      = 32 - pc2;
                Store(tmpRight, L3);
                tmpRight = (int*) ((byte*) tmpRight - pc3);
                pc3      = 32 - pc3;

                Store(tmpLeft, L0);
                tmpLeft = (int*) ((byte*) tmpLeft + pc0);
                Store(tmpLeft, L1);
                tmpLeft = (int*) ((byte*) tmpLeft + pc1);
                Store(tmpLeft, L2);
                tmpLeft = (int*) ((byte*) tmpLeft + pc2);
                Store(tmpLeft, L3);
                tmpLeft = (int*) ((byte*) tmpLeft + pc3);
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

            [Conditional("DEBUG")]
            void Dbg(string d) => Console.WriteLine($"{_depth}> {d}");

            [Conditional("DEBUG")]
            void Trace(string d) => Console.WriteLine($"{_depth}> {d}");
        }
    }
}
