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

    public static class DoublePumpUnroll4
    {
        public static unsafe void Sort<T>(T[] array) where T : unmanaged, IComparable<T>
        {
            if (array == null)
                throw new ArgumentNullException(nameof(array));

            fixed (T* p = &array[0]) {
                // Yes this looks horrid, but the C# JIT will happily elide
                // the irrelevant code per each type being compiled, so we're good
                if (typeof(T) == typeof(int)) {
                    var left = (int*) p;
                    var sorter = new VxSortInt32(startPtr: left, endPtr: left + array.Length - 1);
                    sorter.Sort(left, left + array.Length - 1);
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

        // We need UNROLL_SIZE + 1 elements for each SIDE of the array we're sorting...
        // For 32-bit integers this is 2 * (4+1) * 8 == 80 x int == 320 bytes
        // We allocated one more Vector256 worth of tmp-space for the VectorizedPartitionOnStack
        const int UNROLL_SIZE_IN_VECTORS = 4;
        const int SLACK_PER_SIDE_IN_VECTORS = UNROLL_SIZE_IN_VECTORS + 1;
        const int UNROLL_SIZE_IN_ELEMENTS = UNROLL_SIZE_IN_VECTORS * 8;
        const int SLACK_PER_SIDE_IN_ELEMENTS = SLACK_PER_SIDE_IN_VECTORS * 8;
        // we need temporary storage for the slack + up to 0-7  elements that are
        // left as remainder, so we allocate 8 elements more
        const int TMP_SIZE_IN_ELEMENTS = 2 * SLACK_PER_SIDE_IN_ELEMENTS + 8;
        const int UNROLL_SIZE_IN_BYTES = UNROLL_SIZE_IN_ELEMENTS * sizeof(int);

        internal unsafe struct VxSortInt32
        {
            readonly int* _startPtr;
            readonly int* _endPtr;
            readonly int* _tempStart;
            readonly int* _tempEnd;
#pragma warning disable 649
            fixed int _temp[TMP_SIZE_IN_ELEMENTS];
            int depth;
#pragma warning restore 649

            internal long Length => _endPtr - _startPtr + 1;

            public VxSortInt32(int* startPtr, int* endPtr) : this()
            {
                depth = 0;
                _startPtr = startPtr;
                _endPtr   = endPtr;
                fixed (int* pTemp = _temp) {
                    _tempStart = pTemp;
                    _tempEnd   = pTemp + TMP_SIZE_IN_ELEMENTS;
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveOptimization)]
            internal void Sort(int* left, int* right)
            {
                Debug.Assert(left <= right);

                var length = (int) (right - left + 1);

                switch (length) {
                    case 0:
                    case 1:
                        return;
                    case 2:
                        SwapIfGreater(left, right);
                        return;
                    case 3:
                        SwapIfGreater(left, right - 1);
                        SwapIfGreater(left, right);
                        SwapIfGreater(right - 1, right);
                        return;
                }

                // We need at least 3 elements for the median-of-3 pivot selection,
                // So we don't bother with AVX2 partitioning before we have 8 + 3 elements
                if (length <= 16 + 3) {
                    InsertionSort(left, right);
                    return;
                }

                // Compute median-of-three. But also partition them, since we've done the comparison.
                var mid = left + ((right - left) / 2);
                // Sort lo, mid and hi appropriately, then pick mid as the pivot.
                // Move the pivot to one before the end, so we can easily
                // place it back at the boundary once we figure out where it's at
                SwapIfGreater(left, mid);
                SwapIfGreater(left, right);
                SwapIfGreater(mid, right);
                Swap(mid, right - 1);
                Dbg($"Pivot is in [{right - 1 - left}]] == {*(right -1)}");

                // We need at least 3 elements for the median-of-3 pivot selection,
                // and 4 * AVX2 element size to get going
                var sep = length < 2 * SLACK_PER_SIDE_IN_ELEMENTS + 3 ?
                    VectorizedPartitionOnStack(left, right) :
                    VectorizedPartitionInPlace(left, right);

                depth++;
                Sort(left, sep - 1);
                Sort(sep, right);
                depth--;
            }

            [MethodImpl(MethodImplOptions.AggressiveOptimization)]
            int* VectorizedPartitionInPlace(int* left, int* right)
            {
#if DEBUG
                var that = this; // CS1673
#endif
                Dbg($"{nameof(VectorizedPartitionInPlace)}: [{left - _startPtr}-{right - _startPtr}]({right-left+1})");

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
                var readLeft = left + 1;
                var readRight = right - 1;
                var writeLeft = readLeft;
                var writeRight = readRight - N;

                var pivot = *(right - 1);
                // Boundaries for the tmp array
                var tmpLeft = _tempStart;
                var tmpRight = _tempEnd - N;

                Partition5Vectors(readLeft, pivot, ref tmpLeft, ref tmpRight);
                Partition5Vectors(readRight - UNROLL_SIZE_IN_ELEMENTS - N, pivot, ref tmpLeft, ref tmpRight);
                tmpRight += N;

                Trace($"tmpRight = {new ROS(tmpRight, (int) (_tempEnd - tmpRight)).Dump()}");
                Trace($"tmpLeft = {new ROS(_tempStart, (int) (tmpLeft - _tempStart)).Dump()}");
                Trace($"tmpRight - tmpLeft = {tmpRight - tmpLeft}");
                Debug.Assert((tmpLeft - _tempStart) + (_tempEnd - tmpRight) == 2 * (UNROLL_SIZE_IN_VECTORS + 1) * N);

                // Broadcast the selected pivot
                var P = Vector256.Create(pivot);
                Trace($"pivot Vector256 is: {P}");
                var pBase = Int32PermTables.IntPermTablePtr;

                // Adjust for the reading that was made above
                readLeft += SLACK_PER_SIDE_IN_ELEMENTS;
                readRight -= SLACK_PER_SIDE_IN_ELEMENTS + UNROLL_SIZE_IN_ELEMENTS;

                while (readLeft < readRight) {
                    Trace($"4x: RL:{readLeft - left}|RR:{readRight + UNROLL_SIZE_IN_ELEMENTS - left}|{readRight + UNROLL_SIZE_IN_ELEMENTS - readLeft}");
                    Trace($"4x: WL:{writeLeft - left}({readLeft - writeLeft})|WR:{writeRight + 8 - left}({writeRight + 8 - (readRight + UNROLL_SIZE_IN_ELEMENTS)})");
                    int *nextPtr;
                    if (((byte *) readLeft   -  (byte *) writeLeft) <=
                        ((byte *) writeRight - ((byte *) readRight + UNROLL_SIZE_IN_BYTES))) {
                        nextPtr = readLeft;
                        readLeft += UNROLL_SIZE_IN_ELEMENTS;
                        Dbg($"Reading from LEFT, new WL:{writeLeft - left}({readLeft - writeLeft})");
                    } else {
                        nextPtr = readRight;
                        readRight -= UNROLL_SIZE_IN_ELEMENTS;
                        Dbg($"Reading from RIGHT, new WR:{writeRight + 8 - left}({writeRight + 8 - (readRight + UNROLL_SIZE_IN_ELEMENTS)})");
                    }

                    Debug.Assert(readLeft - writeLeft >= UNROLL_SIZE_IN_ELEMENTS, $"left head overwrite {readLeft - writeLeft}");
                    Debug.Assert(writeRight - readRight >= UNROLL_SIZE_IN_ELEMENTS, $"right head overwrite {writeRight - readRight}");

                    var L0 = LoadDquVector256(nextPtr + 0*N);
                    var L1 = LoadDquVector256(nextPtr + 1*N);
                    var L2 = LoadDquVector256(nextPtr + 2*N);
                    var L3 = LoadDquVector256(nextPtr + 3*N);

                    var m0 = (uint) MoveMask(CompareGreaterThan(L0, P).AsSingle());
                    var m1 = (uint) MoveMask(CompareGreaterThan(L1, P).AsSingle());
                    var m2 = (uint) MoveMask(CompareGreaterThan(L2, P).AsSingle());
                    var m3 = (uint) MoveMask(CompareGreaterThan(L3, P).AsSingle());

                    L0 = PermuteVar8x32(L0, Int32PermTables.GetIntPermutation(pBase, m0));
                    L1 = PermuteVar8x32(L1, Int32PermTables.GetIntPermutation(pBase, m1));
                    L2 = PermuteVar8x32(L2, Int32PermTables.GetIntPermutation(pBase, m2));
                    L3 = PermuteVar8x32(L3, Int32PermTables.GetIntPermutation(pBase, m3));

                    var pc0 = PopCount(m0) << 2;
                    var pc1 = PopCount(m1) << 2;
                    var pc2 = PopCount(m2) << 2;
                    var pc3 = PopCount(m3) << 2;
                    Trace($"Total Permuted Left: {(8 * 4) - (pc0+pc1+pc2+pc3)>>2}|Total Permuted Right: {(pc0+pc1+pc2+pc3)>>2}");

                    Store(writeRight, L0);
                    writeRight = (int *) ((byte *) writeRight  - pc0);
                    pc0 = (8 << 2) - pc0;
                    Store(writeRight, L1);
                    writeRight = (int *) ((byte *) writeRight  - pc1);
                    pc1 = (8 << 2)- pc1;
                    Store(writeRight, L2);
                    writeRight = (int *) ((byte *) writeRight  - pc2);
                    pc2 = (8 << 2) - pc2;
                    Store(writeRight, L3);
                    writeRight = (int *) ((byte *) writeRight  - pc3);
                    pc3 = (8 << 2) - pc3;

                    Store(writeLeft, L0);
                    writeLeft = (int *) ((byte *) writeLeft + pc0);
                    Store(writeLeft, L1);
                    writeLeft = (int *) ((byte *) writeLeft + pc1);
                    Store(writeLeft, L2);
                    writeLeft = (int *) ((byte *) writeLeft + pc2);
                    Store(writeLeft, L3);
                    writeLeft = (int *) ((byte *) writeLeft + pc3);
                }

                readRight += UNROLL_SIZE_IN_ELEMENTS - N;

                Trace($"Before 1x WL:{writeLeft - left}({readLeft - writeLeft})|WR:{writeRight + 8 - left}({writeRight - readRight})");
                Trace($"Before 1x RL:{readLeft - left}|RR:{readRight + 8- left}|{readRight + 8 - readLeft}");
                while (readLeft < readRight) {
                    Vector256<int> current;
                    if (((byte *) readLeft - (byte *) writeLeft) <=
                        ((byte *) writeRight - (byte *) readRight)) {
                        current  =  LoadDquVector256(readLeft);
                        readLeft += N;
                    } else {
                        current   =  LoadDquVector256(readRight);
                        readRight -= N;
                    }

                    var mask = (uint) MoveMask(CompareGreaterThan(current, P).AsSingle());
                    current = PermuteVar8x32(current, Int32PermTables.GetIntPermutation(pBase, mask));

                    var popCount = PopCount(mask) << 2;
                    Trace($"Permuted: {current}|{8U - (popCount >> 2)}|{popCount >> 2}");

                    Store(writeLeft, current);
                    Store(writeRight, current);

                    writeRight = (int *) ((byte *) writeRight - popCount);
                    writeLeft  = (int *) ((byte *) writeLeft  + (8U << 2) - popCount);

                    Trace($"WL:{writeLeft - left}|WR:{writeRight + 8 - left}");
                    Debug.Assert(readLeft - writeLeft >= 8);
                    Debug.Assert(writeRight - readRight >= 8);
                }

                // We're scalar from now, so
                // correct the right read pointer back
                readRight += N;

                var boundary = writeLeft;

                // 2. partition remaining part into the tmp stack space
                Trace($"Doing remainder as scalar partition on [{readLeft - left}-{readRight - left})({readRight - readLeft}) into tmp  [{tmpLeft - _tempStart}-{tmpRight - 1- _tempStart}]");
                while (readLeft < readRight) {
                    var v = *readLeft++;

                    if (v <= pivot) {
                        Trace($"Read: [{readLeft - 1 - left}]={v} <= {pivot} -> writing to tmpLeft [{tmpLeft - _tempStart}]");
                        *tmpLeft++ = v;
                    }
                    else {
                        Trace($"Read: [{readLeft - 1 - left}]={v} > {pivot} -> writing to tmpRight [{tmpRight - 1 - _tempStart}]");
                        *--tmpRight = v;
                    }
                    Trace($"RL:{readLeft-left} 🡄 🡆 RR:{readRight - left}");
                }

                // 3. Copy-back the 4 registers + remainder we partitioned in the beginning
                var leftTmpSize = (int)((byte *) tmpLeft - (byte *)_tempStart);
                Trace($"Copying back tmpLeft -> [{boundary-left}-{boundary-left+leftTmpSize})|{new ROS(_tempStart, leftTmpSize).Dump()}");
                new ReadOnlySpan<byte>((byte *) _tempStart, leftTmpSize).CopyTo(new Span<byte>(boundary, leftTmpSize));
                boundary  = (int *)((byte *) boundary + leftTmpSize);
                var rightTmpSize = (int) ((byte *) (_tempEnd) - (byte *) tmpRight);
                Trace($"Copying back tmpRight -> [{boundary-left}-{boundary-left+rightTmpSize})|{new ROS(tmpRight, rightTmpSize).Dump()}");
                new ReadOnlySpan<byte>(tmpRight, rightTmpSize).CopyTo(new Span<byte>(boundary, rightTmpSize));

                // Shove to pivot right at the boundary
                Dbg($"Swapping pivot {*(right - 1)} from [{right - 1 - left}] into [{boundary - left}]");
                Swap(boundary++, right -1);

                Dbg($"Final Boundary: {boundary - left}/{right - left + 1}");
                Dbg(new ROS(_startPtr, (int) Length).Dump());

                VerifyBoundaryIsCorrect(left, right, pivot, boundary);

                Debug.Assert(boundary > left);
                Debug.Assert(boundary <= right);

                return boundary;

    #if DEBUG
                Vector256<int> LoadDquVector256(int* address) {
                    Debug.Assert(address >= left);
                    Debug.Assert(address + 8U <= right);
                    var tmp = Avx.LoadDquVector256(address);
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

            [MethodImpl(MethodImplOptions.AggressiveOptimization)]
            int* VectorizedPartitionOnStack(int* left, int* right)
            {
#if DEBUG
                var that = this; // CS1673
#endif
                Dbg($"{nameof(VectorizedPartitionOnStack)}: [{left - _startPtr}-{right - _startPtr }]({right-left+1})");

                var N = Vector256<int>.Count; // Treated as constant @ JIT time
                var pivot = *(right - 1);

                var tmpLeft = _tempStart;
                var tmpRight = _tempEnd - N;

                // The first element and last two are about to be
                // partitioned anyway as part of the median-of-3
                // pivot selection, so we won't be partitioning them
                // as part of the vectorized pass
                var readLeft = left + 1;
                var readRight = right - N - 1;

                if ((readRight - readLeft ) > 4 * N) {
                    Partition5Vectors(readLeft, pivot, ref tmpLeft, ref tmpRight);
                    readLeft += 5 * 8;
                }

                if ((readRight - readLeft) > 2 * 8) {
                    Partition3Vectors(readLeft, pivot, ref tmpLeft, ref tmpRight);
                    readLeft += 3 * 8;
                }

                // Broadcast the selected pivot
                var P = Vector256.Create(pivot);
                Trace($"pivot Vector256 is: {P}");
                var pBase = BytePermTables.BytePermTablePtr;

                // We only get here if we have at LEAST one AVX2
                // vector worth of data to partition after
                // median-of-3 process

                // Do the first one
                while (readLeft < readRight) {
                    var L0 = LoadDquVector256(readLeft);
                    readLeft += 8;
                    var m0 = (uint) MoveMask(CompareGreaterThan(L0, P).AsSingle());
                    var popCount0 = PopCount(m0) << 2;
                    L0 = PermuteVar8x32(L0, BytePermTables.GetBytePermutation(pBase, m0));
                    Trace($"Permuted: {L0}|{8U - popCount0 >> 2}|{popCount0 >> 2}");
                    Store(tmpRight, L0);
                    tmpRight  = (int *) ((byte *) tmpRight - popCount0);
                    popCount0 =  32 - popCount0;
                    Store(tmpLeft, L0);
                    tmpLeft = (int*) ((byte*) tmpLeft + popCount0);
                }

                tmpRight += 8;
                readRight += 8;

                Trace($"tmpRight = {new ROS(tmpRight, (int) (_tempEnd - tmpRight)).Dump()}");
                Trace($"tmpLeft = {new ROS(_tempStart, (int) (tmpLeft - _tempStart)).Dump()}");
                Trace($"tmpRight - tmpLeft = {tmpRight - tmpLeft}");

                Trace($"Doing remainder as scalar partition on [{readLeft - left}-{readRight - left})({readRight - readLeft}) into tmp  [{tmpLeft - _tempStart}-{tmpRight - 1- _tempStart}]");
                while (readLeft < readRight) {
                    var v = *readLeft++;

                    if (v <= pivot) {
                        Trace($"Read: [{readLeft - 1 - left}]={v} <= {pivot} -> writing to tmpLeft [{tmpLeft - _tempStart}]");
                        *tmpLeft++ = v;
                    }
                    else {
                        Trace($"Read: [{readLeft - 1 - left}]={v} > {pivot} -> writing to tmpRight [{tmpRight - 1 - _tempStart}]");
                        *--tmpRight = v;
                    }
                    Trace($"RL:{readLeft-left} 🡄 🡆 RR:{readRight - left}");
                }

                var leftTmpSize = (int) (tmpLeft - _tempStart);
                Trace($"Copying back tmpLeft -> [{1}-{leftTmpSize+1}]|{new ROS(_tempStart, leftTmpSize).Dump()}");
                new ROS(_tempStart, leftTmpSize).CopyTo(new Span<int>(left+1, leftTmpSize));

                var boundary = left + leftTmpSize + 1;

                var rightTmpSize = (int) (_tempEnd - tmpRight);
                Trace($"Copying back tmpRight -> [{boundary-left}-{right - 2 - left}]|{new ROS(tmpRight, rightTmpSize).Dump()}");
                new ROS(tmpRight, rightTmpSize).CopyTo(new Span<int>(boundary, rightTmpSize));

                // Shove to pivot right at the boundary
                Dbg($"Swapping pivot {*(right - 1)} from [{right - 1 - left}] into [{boundary - left}]");
                Swap(boundary++, right -1);

                Dbg($"Final Boundary: {boundary - left}/{right - left + 1}");
                Dbg(new ROS(_startPtr, (int) Length).Dump());

                VerifyBoundaryIsCorrect(left, right, pivot, boundary);

                return boundary;

    #if DEBUG
                Vector256<int> LoadDquVector256(int* address)
                {
                    Debug.Assert(address >= left);
                    Debug.Assert(address + 8U <= right);
                    var tmp = Avx.LoadDquVector256(address);
                    that.Trace($"Reading from offset: [{address - left}-{address - left + 7U}]: {tmp}");
                    return tmp;
                }

                void Store(int *address, Vector256<int> data) {
                    Debug.Assert(address >= that._tempStart);
                    Debug.Assert(address + 8U <= that._tempEnd);
                    that.Trace($"Storing to [{address-that._tempStart}-{address-that._tempStart+7U}]");
                    Avx.Store(address, data);
                }
    #endif
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

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            void Partition5Vectors(int* p, int pivot, ref int* tmpLeft, ref int* tmpRight)
            {
                var pBase = Int32PermTables.IntPermTablePtr;

                var P = Vector256.Create(pivot);

                // Read-ahead 5 Vector256<int> from p
                var L0 = LoadDquVector256(p + 0*8);
                var L1 = LoadDquVector256(p + 1*8);
                var L2 = LoadDquVector256(p + 2*8);
                var L3 = LoadDquVector256(p + 3*8);
                var L4 = LoadDquVector256(p + 4*8);

                var m0 = (uint) MoveMask(CompareGreaterThan(L0, P).AsSingle());
                var m1 = (uint) MoveMask(CompareGreaterThan(L1, P).AsSingle());
                var m2 = (uint) MoveMask(CompareGreaterThan(L2, P).AsSingle());
                var m3 = (uint) MoveMask(CompareGreaterThan(L3, P).AsSingle());
                var m4 = (uint) MoveMask(CompareGreaterThan(L4, P).AsSingle());

                var pc0 = PopCount(m0) << 2;
                var pc1 = PopCount(m1) << 2;
                var pc2 = PopCount(m2) << 2;
                var pc3 = PopCount(m3) << 2;
                var pc4 = PopCount(m4) << 2;

                L0 = PermuteVar8x32(L0, Int32PermTables.GetIntPermutation(pBase, m0));
                L1 = PermuteVar8x32(L1, Int32PermTables.GetIntPermutation(pBase, m1));
                L2 = PermuteVar8x32(L2, Int32PermTables.GetIntPermutation(pBase, m2));
                L3 = PermuteVar8x32(L3, Int32PermTables.GetIntPermutation(pBase, m3));
                L4 = PermuteVar8x32(L4, Int32PermTables.GetIntPermutation(pBase, m4));
                Trace($"Total Permuted Left: {8 * 5 - (pc0+pc1+pc2+pc3+pc4) >> 2}|Total Permuted Right: {(pc0+pc1+pc2+pc3+pc4) >> 2}");

                Store(tmpRight, L0);
                tmpRight = (int*) ((byte *) tmpRight - pc0);
                pc0 = 32 - pc0;
                Store(tmpRight, L1);
                tmpRight = (int*) ((byte *) tmpRight - pc1);
                pc1 = 32 - pc1;
                Store(tmpRight, L2);
                tmpRight = (int*) ((byte *) tmpRight - pc2);
                pc2 = 32 - pc2;
                Store(tmpRight, L3);
                tmpRight = (int*) ((byte *) tmpRight - pc3);
                pc3 = 32 - pc3;
                Store(tmpRight, L4);
                tmpRight = (int*) ((byte *) tmpRight - pc4);
                pc4 =  32 - pc4;

                Store(tmpLeft, L0);
                tmpLeft = (int *) ((byte *) tmpLeft + pc0);
                Store(tmpLeft, L1);
                tmpLeft = (int *) ((byte *) tmpLeft + pc1);
                Store(tmpLeft, L2);
                tmpLeft = (int *) ((byte *) tmpLeft + pc2);
                Store(tmpLeft, L3);
                tmpLeft = (int *) ((byte *) tmpLeft + pc3);
                Store(tmpLeft, L4);
                tmpLeft = (int *) ((byte *) tmpLeft + pc4);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static void Partition3Vectors(int* p, int pivot, ref int* tmpLeft, ref int* tmpRight)
            {
                var pBase = Int32PermTables.IntPermTablePtr;

                var P = Vector256.Create(pivot);

                // Read-ahead 5 Vector256<int> from p
                var L0 = LoadDquVector256(p + 0*8);
                var L1 = LoadDquVector256(p + 1*8);
                var L2 = LoadDquVector256(p + 2*8);

                var m0 = (uint) MoveMask(CompareGreaterThan(L0, P).AsSingle());
                var m1 = (uint) MoveMask(CompareGreaterThan(L1, P).AsSingle());
                var m2 = (uint) MoveMask(CompareGreaterThan(L2, P).AsSingle());

                var pc0 = PopCount(m0) << 2;
                var pc1 = PopCount(m1) << 2;
                var pc2 = PopCount(m2) << 2;

                L0 = PermuteVar8x32(L0, Int32PermTables.GetIntPermutation(pBase, m0));
                L1 = PermuteVar8x32(L1, Int32PermTables.GetIntPermutation(pBase, m1));
                L2 = PermuteVar8x32(L2, Int32PermTables.GetIntPermutation(pBase, m2));

                Store(tmpRight, L0);
                tmpRight = (int*) ((byte *) tmpRight - pc0);
                pc0 = 32 - pc0;
                Store(tmpRight, L1);
                tmpRight = (int*) ((byte *) tmpRight - pc1);
                pc1 = 32 - pc1;
                Store(tmpRight, L2);
                tmpRight = (int*) ((byte *) tmpRight - pc2);
                pc2 = 32 - pc2;

                Store(tmpLeft, L0);
                tmpLeft = (int *) ((byte *) tmpLeft + pc0);
                Store(tmpLeft, L1);
                tmpLeft = (int *) ((byte *) tmpLeft + pc1);
                Store(tmpLeft, L2);
                tmpLeft = (int *) ((byte *) tmpLeft + pc2);
            }

            [Conditional("DEBUG")]
            void Dbg(string d) => Console.WriteLine($"{depth}> {d}");

            [Conditional("DEBUG")]
            void Trace(string d) => Console.WriteLine($"{depth}> {d}");
        }
    }
}
