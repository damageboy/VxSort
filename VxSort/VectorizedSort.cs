using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using static System.Runtime.Intrinsics.X86.Avx;
using static System.Runtime.Intrinsics.X86.Avx2;
using static System.Runtime.Intrinsics.X86.Popcnt.X64;
using static System.Runtime.Intrinsics.X86.Popcnt;
using static VxSort.BytePermutationTables;

namespace VxSort
{
    public static class VectorizedSort
    {
        public static unsafe void UnstableSort<T>(T[] array) where T : unmanaged, IComparable<T>
        {
            if (array == null) {
                throw new ArgumentNullException(nameof(array));
            }

            if (!Avx2.IsSupported) {
                throw new NotSupportedException($"{nameof(VxSort)} requires x86/AVX2 support in the processor");
            }

            fixed (T* p = &array[0]) {
                // Yes this looks horrid, but the C# JIT will happily elide
                // the irrelevant code per each type being compiled, so we're good
                if (typeof(T) == typeof(int)) {
                    var left = (int*) p;
                    var sorter = new VxUnstableSortInt32(startPtr: left, endPtr: left + array.Length - 1);
                    sorter.Sort();
                }
                else {
                    throw new NotImplementedException($"{nameof(VxSort)} does not yet support {typeof(T).Name}");
                }
            }
        }

		static int FloorLog2PlusOne(uint n)
        {
            var result = 0;
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
            if ((*left).CompareTo(*right) <= 0) return;
            Swap(left, right);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static unsafe void InsertionSort<TX>(TX * left, TX * right) where TX : unmanaged, IComparable<TX>
        {
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
            while (i <= n / 2) {
                var child = 2 * i;
                if (child < n && keys[lo + child - 1].CompareTo(keys[lo + child]) < 0) {
                    child++;
                }

                if (keys[lo + child - 1].CompareTo(d) < 0)
                    break;

                keys[lo + i - 1] = keys[lo + child - 1];
                i                = child;
            }

            keys[lo + i - 1] = d;
        }

        // How much initial room needs to be made
        // during setup in full Vector25 units
        const int SLACK_PER_SIDE_IN_VECTORS = 8;

        // Once we come out of the first unrolled loop
        // this will be the size of the second unrolled loop.
        const int UNROLL2_SIZE_IN_VECTORS = 4;

        // Alignment in bytes
        const ulong ALIGN = 32;
        const ulong ALIGN_MASK = ALIGN - 1;

        internal unsafe ref struct VxUnstableSortInt32
        {
            // We need this as a compile time constant
            const int V256_N = 256 / 8 / sizeof(int);

            const int SMALL_SORT_THRESHOLD_ELEMENTS = 112;
            const int SLACK_PER_SIDE_IN_ELEMENTS = SLACK_PER_SIDE_IN_VECTORS * V256_N;
            const int UNROLL2_SLACK_PER_SIDE_IN_ELEMENTS = UNROLL2_SIZE_IN_VECTORS  * V256_N;
            const int EIGHTH_SLACK_PER_SIDE_IN_ELEMENTS = V256_N;

            // The formula goes like this:
            // 2 x the number of slack elements on each side +
            // 2 x amount of maximal bytes needed for alignment (32)
            // 8 more elements since we write with 8-way stores from both ends of the temporary area
            //   and we must make sure to accidentaly over-write from left -> right or vice-versa right on that edge...
            const int PARTITION_TMP_SIZE_IN_ELEMENTS = (int) (2 * SLACK_PER_SIDE_IN_ELEMENTS + 2 * ALIGN / sizeof(int) + V256_N);

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

            public VxUnstableSortInt32(int* startPtr, int* endPtr) : this()
            {
                Debug.Assert(SMALL_SORT_THRESHOLD_ELEMENTS % V256_N == 0);

                _depth = 0;
                _startPtr = startPtr;
                _endPtr   = endPtr;
                fixed (int* pTemp = _temp) {
                    _tempStart = pTemp;
                    _tempEnd   = pTemp + PARTITION_TMP_SIZE_IN_ELEMENTS;
                }
            }


            [MethodImpl(MethodImplOptions.AggressiveOptimization)]
            internal void Sort()
            {
                // It makes no sense to sort arrays smaller than the max supported
                // bitonic sort with hybrid partitioning, so we special case those sized
                // and just copy the entire source to the tmp memory, pad it with
                // int.MaxValue and call BitonicSort
                var cachedLength = (uint) (ulong) Length;
                if (cachedLength <= BitonicSort<int>.MaxBitonicSortSize) {
                    CopyAndSortWithBitonic(cachedLength);
                    return;
                }

                var depthLimit = 2 * FloorLog2PlusOne(cachedLength);
                HybridSort(_startPtr, _endPtr, REALIGN_BOTH, depthLimit);
            }

            void CopyAndSortWithBitonic(uint cachedLength)
            {
                var start = _startPtr;
                var tmp = _tempStart;
                var byteCount = cachedLength * sizeof(int);

                var adjustedLength = cachedLength & ~0b111;
                Store(tmp + adjustedLength, Vector256.Create(int.MaxValue));
                Unsafe.CopyBlockUnaligned(tmp, start, byteCount);
                BitonicSort<int>.Sort(tmp, (int) Math.Min(adjustedLength + 8, BitonicSort<int>.MaxBitonicSortSize));
                Unsafe.CopyBlockUnaligned(start, tmp, byteCount);
            }

            [MethodImpl(MethodImplOptions.AggressiveOptimization)]
            internal void HybridSort(int* left, int* right, long realignHint, int depthLimit)
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

                _depth++;

                // SMALL_SORT_THRESHOLD_ELEMENTS is guaranteed (and asserted) to be a multiple of 8
                // So we can check if length is strictly smaller, knowing that we will round up to
                // SMALL_SORT_THRESHOLD_ELEMENTS exactly and no more
                // This is kind of critical given that we only limited # of implementation of
                // vectorized bitonic sort
                if (length < SMALL_SORT_THRESHOLD_ELEMENTS) {
                    var nextLength = (length & 7) > 0 ? (length + V256_N) & ~7: length;

                    Debug.Assert(nextLength <= BitonicSort<int>.MaxBitonicSortSize);
                    var extraSpaceNeeded = nextLength - length;
                    var fakeLeft = left - extraSpaceNeeded;
                    if (fakeLeft >= _startPtr) {
                        BitonicSort<int>.Sort(fakeLeft, nextLength);
                    }
                    else {
                        InsertionSort(left, right);
                    }
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
                var preAlignLeftOffset = (preAlignedLeft - left) + (V256_N & cannotPreAlignLeft);
                if ((realignHint & REALIGN_LEFT) != 0) {
                    // Alignment flow:
                    // * Calculate pre-alignment on the left
                    // * See it would cause us an out-of bounds read
                    // * Since we'd like to avoid that, we adjust for post-alignment
                    // * There are no branches since we do branch->arithmetic
                    realignHint &= unchecked((long) 0xFFFFFFFF00000000UL);
                    realignHint |= preAlignLeftOffset;
                }

                var preAlignedRight = (int*) (((ulong) right - 1 & ~ALIGN_MASK) + ALIGN);
                var cannotPreAlignRight = (_endPtr - preAlignedRight) >> 63;
                var preAlignRightOffset = (preAlignedRight - right - (V256_N & cannotPreAlignRight));
                if ((realignHint & REALIGN_RIGHT) != 0) {
                    // right is pointing just PAST the last element we intend to partition (where we also store the pivot)
                    // So we calculate alignment based on right - 1, and YES: I am casting to ulong before doing the -1, this
                    // is intentional since the whole thing is either aligned to 32 bytes or not, so decrementing the POINTER value
                    // by 1 is sufficient for the alignment, an the JIT sucks at this anyway
                    realignHint &= 0xFFFFFFFF;
                    realignHint |= preAlignRightOffset << 32;
                }

                Debug.Assert(((ulong) (left + (realignHint & 0xFFFFFFFF)) & ALIGN_MASK) == 0);
                Debug.Assert(((ulong) (right + (realignHint >> 32)) & ALIGN_MASK) == 0);

                // Compute median-of-three, of:
                // the first, mid and one before last elements
                mid = left + (right - left) / 2;
                SwapIfGreater(left, mid);
                SwapIfGreater(left, right - 1);
                SwapIfGreater(mid, right - 1);

                // Pivot is mid, place it in the right hand side
                Swap(mid, right);

                var sep = length < PARTITION_TMP_SIZE_IN_ELEMENTS ?
                    Partition1VectorInPlace(left, right, realignHint) :
                    Partition8VectorsInPlace(left, right, realignHint);

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
                HybridSort(left, sep - 2, realignHint | REALIGN_RIGHT, depthLimit);
                HybridSort(sep, right, realignHint | REALIGN_LEFT, depthLimit);
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
                Debug.Assert(right - left >= SMALL_SORT_THRESHOLD_ELEMENTS, $"Not enough elements: {right-left} >= {SMALL_SORT_THRESHOLD_ELEMENTS}");

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
                var crappyWriteRight = right - N;

                var tmpStartLeft = _tempStart;
                var tmpLeft = tmpStartLeft;
                var tmpStartRight = _tempEnd;
                var tmpRight = tmpStartRight;

                // Broadcast the selected pivot
                var P = Vector256.Create(pivot);
                var pBase = BytePermTableAlignedPtr;
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
                var RT0 = LoadAlignedVector256(preAlignedRight);
                var LT0 = LoadAlignedVector256(preAlignedLeft);
                var rtMask = (uint) MoveMask(CompareGreaterThan(RT0, P).AsSingle());
                var ltMask = (uint) MoveMask(CompareGreaterThan(LT0, P).AsSingle());
                var rtPopCount = Math.Max(PopCount(rtMask), (uint) rightAlign);
                var ltPopCount = PopCount(ltMask);
                RT0 = PermuteVar8x32(RT0, GetBytePermutationAligned(pBase, rtMask));
                LT0 = PermuteVar8x32(LT0, GetBytePermutationAligned(pBase, ltMask));
                Store(tmpRight, RT0);
                Store(tmpLeft, LT0);

                var rightAlignMask = ~((rightAlign - 1) >> 31);
                var leftAlignMask = leftAlign >> 31;

                tmpRight -= rtPopCount & rightAlignMask;
                rtPopCount = V256_N - rtPopCount;
                readRight += (rightAlign - N) & rightAlignMask;

                Store(tmpRight, LT0);
                tmpRight -= ltPopCount & leftAlignMask;
                ltPopCount =  V256_N - ltPopCount;
                tmpLeft += ltPopCount & leftAlignMask;
                tmpStartLeft += -leftAlign & leftAlignMask;
                readLeft += (leftAlign + N) & leftAlignMask;

                Store(tmpLeft,  RT0);
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

                #endregion

                // Make 8 vectors worth of space on each side by partitioning them straight into the temporary memory
                LoadAndPartition8Vectors(readLeft, P, pBase, ref tmpLeft, ref tmpRight);
                LoadAndPartition8Vectors(readRight - SLACK_PER_SIDE_IN_ELEMENTS, P, pBase, ref tmpLeft, ref tmpRight);
                tmpRight += N;

                // Adjust for the reading that was made above
                readLeft  += SLACK_PER_SIDE_IN_ELEMENTS;
                readRight -= SLACK_PER_SIDE_IN_ELEMENTS * 2;

                var writeRight = crappyWriteRight;

                while (readLeft < readRight) {
                    int* nextPtr;
                    if ((byte *) writeRight - (byte *) readRight  < (2*SLACK_PER_SIDE_IN_ELEMENTS - N)*sizeof(int)) {
                        nextPtr   =  readRight;
                        readRight -= SLACK_PER_SIDE_IN_ELEMENTS;
                    } else {
                        nextPtr  =  readLeft;
                        readLeft += SLACK_PER_SIDE_IN_ELEMENTS;
                    }

                    LoadAndPartition8Vectors(nextPtr, P, pBase, ref writeLeft, ref writeRight);
                }

                readRight += UNROLL2_SLACK_PER_SIDE_IN_ELEMENTS;

                while (readLeft < readRight) {
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

                while (readLeft <= readRight) {
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
                Unsafe.CopyBlockUnaligned(boundary, tmpStartLeft, leftTmpSize*sizeof(int));
                boundary += leftTmpSize;
                var rightTmpSize = (uint) (ulong) (tmpStartRight - tmpRight);
                Unsafe.CopyBlockUnaligned(boundary, tmpRight, rightTmpSize*sizeof(int));

                // Shove to pivot back to the boundary
                var value = *boundary;
                *right = value;
                *boundary++ = pivot;

                Debug.Assert(boundary > left);
                Debug.Assert(boundary <= right);

                return boundary;
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
            static void PartitionBlock1V(Vector256<int> L0, Vector256<int> P, byte* pBase, ref int* writeLeft, ref int* writeRight)
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
                var m0 = (ulong) (uint) MoveMask(CompareGreaterThan(L0, P).AsSingle());
                L0 = PermuteVar8x32(L0, GetBytePermutationAligned(pBase, m0));
                // We make sure the last use of m0 is for this PopCount operation. Why?
                // Again, this is to get the best code generated on an Intel CPU. This round it's intel's fault, yay.
                // There's a relatively well know CPU errata where POPCNT has a false dependency on the destination operand.
                // The JIT is already aware of this, so it will clear the destination operand before emitting a POPCNT:
                // https://github.com/dotnet/coreclr/issues/19555
                // By "delaying" the PopCount to this stage, it is highly likely (I don't know why, I just know it is...)
                // that the JIT will emit a POPCNT X,X instruction, where X is now both the source and the destination
                // for PopCount. This means that there is no need for clearing the destination register (it would even be
                // an error to do so). This saves about two bytes in the instruction stream.
                var pc = -((long) (int) PopCount(m0));
                Store(writeLeft,  L0);
                Store(writeRight, L0);
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
                writeLeft = writeLeft + pc + V256_N;
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
                Debug.Assert(right - left > SMALL_SORT_THRESHOLD_ELEMENTS);

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
                // as if nothing ever happened if the conditions
                // are wrong from vectorized alignment
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
                rtPopCount = V256_N - rtPopCount;
                readRight += (rightAlign - N) & rightAlignMask;

                Avx.Store(tmpRight, LT0);
                tmpRight -= ltPopCount & leftAlignMask;
                ltPopCount =  V256_N - ltPopCount;
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
                Debug.Assert((readRight -  readLeft) >= EIGHTH_SLACK_PER_SIDE_IN_ELEMENTS * 2);

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

                LT0 = PermuteVar8x32(LT0, GetBytePermutationAligned(pBase, ltMask));
                RT0 = PermuteVar8x32(RT0, GetBytePermutationAligned(pBase, rtMask));

                Store(tmpRight, LT0);
                tmpRight -= ltPopCount;
                ltPopCount = V256_N - ltPopCount;
                Store(tmpRight, RT0);
                tmpRight -= rtPopCount;
                rtPopCount = V256_N - rtPopCount;
                tmpRight += N;

                Store(tmpLeft, LT0);
                tmpLeft += ltPopCount;
                Store(tmpLeft, RT0);
                tmpLeft += rtPopCount;

                while (readRight >= readLeft) {

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
                    current = PermuteVar8x32(current, GetBytePermutationAligned(pBase, mask));

                    Debug.Assert(readLeft - writeLeft   >= N);
                    Debug.Assert(writeRight - readRight >= N);
                    Store(writeLeft,  current);
                    Store(writeRight, current);

                    var popCount = PopCount(mask) << 2;
                    writeRight = (int *) ((byte *) writeRight - popCount);
                    writeLeft  = (int *) ((byte *) writeLeft  + (8U << 2) - popCount);
                }

                var boundary = writeLeft;

                // 3. Copy-back the 4 registers + remainder we partitioned in the beginning
                var leftTmpSize = (uint) (ulong) (tmpLeft - tmpStartLeft);
                Unsafe.CopyBlockUnaligned(boundary, tmpStartLeft, leftTmpSize*sizeof(int));
                boundary += leftTmpSize;
                var rightTmpSize = (uint) (ulong)  (tmpStartRight - tmpRight);
                Unsafe.CopyBlockUnaligned(boundary, tmpRight, rightTmpSize*sizeof(int));

                // Shove to pivot back to the boundary
                var value = *boundary;
                *right = value;
                *boundary++ = pivot;

                Debug.Assert(boundary > left);
                Debug.Assert(boundary <= right);

                return boundary;
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
            static int* AlignLeftScalarUncommon(int* readLeft, int pivot, ref int* tmpLeft, ref int* tmpRight)
            {
                if (((ulong) readLeft & ALIGN_MASK) == 0)
                    return readLeft;

                var nextAlign = (int*) (((ulong) readLeft + ALIGN) & ~ALIGN_MASK);
                while (readLeft < nextAlign) {
                    var v = *readLeft++;
                    if (v <= pivot) {
                        *tmpLeft++ = v;
                    } else {
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
            static int* AlignRightScalarUncommon(int* readRight, int pivot, ref int* tmpLeft, ref int* tmpRight)
            {
                if (((ulong) readRight & ALIGN_MASK) == 0)
                    return readRight;

                var nextAlign = (int*) ((ulong) readRight & ~ALIGN_MASK);
                while (readRight > nextAlign) {
                    var v = *--readRight;
                    if (v <= pivot) {
                        *tmpLeft++ = v;
                    } else {
                        *--tmpRight = v;
                    }
                }

                return readRight;
            }
        }
    }
}
