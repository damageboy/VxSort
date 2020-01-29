using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using VxSortResearch.Statistics;
using static System.Runtime.Intrinsics.X86.Avx;
using static System.Runtime.Intrinsics.X86.Avx2;

namespace VxSortResearch.Stable.SmallSort
{
    public static unsafe class PositionCountingSort
    {
        internal static ReadOnlySpan<byte> Mask0 => new byte[] {
            0x00, 0x00, 0x00, 0x00, // 0 -> 0
            0xFF, 0xFF, 0xFF, 0xFF, // 1 -> -1
            0xFF, 0xFF, 0xFF, 0xFF, // 2 -> -1
            0xFF, 0xFF, 0xFF, 0xFF, // 3 -> -1
            0xFF, 0xFF, 0xFF, 0xFF, // 4 -> -1
            0xFF, 0xFF, 0xFF, 0xFF, // 5 -> -1
            0xFF, 0xFF, 0xFF, 0xFF, // 6 -> -1
            0xFF, 0xFF, 0xFF, 0xFF, // 7 -> -1
        };

        internal static ReadOnlySpan<byte> Mask4 => new byte[] {
            0x00, 0x00, 0x00, 0x00, // 0 -> 0
            0x00, 0x00, 0x00, 0x00, // 1 -> 0
            0x00, 0x00, 0x00, 0x00, // 2 -> 0
            0x00, 0x00, 0x00, 0x00, // 3 -> 0
            0x00, 0x00, 0x00, 0x00, // 4 -> 0
            0xFF, 0xFF, 0xFF, 0xFF, // 5 -> -1
            0xFF, 0xFF, 0xFF, 0xFF, // 6 -> -1
            0xFF, 0xFF, 0xFF, 0xFF, // 7 -> -1
        };

        const ulong CACHELINE_SIZE = 64UL;
        const ulong ALIGN = 32;


        static int* Mask0AlignedPtr;
        static int* Mask4AlignedPtr;
        static PositionCountingSort()
        {
            Mask0AlignedPtr = (int*) Marshal.AllocHGlobal((int) CACHELINE_SIZE);
            if (((ulong) Mask0AlignedPtr) % ALIGN != 0)
                Mask0AlignedPtr = (int*) ((((ulong) Mask0AlignedPtr) + ALIGN) & ~(ALIGN - 1));
            Mask0.CopyTo(new Span<byte>(Mask0AlignedPtr, Mask0.Length));

            Mask4AlignedPtr = (int*) Marshal.AllocHGlobal((int) CACHELINE_SIZE);
            if (((ulong) Mask4AlignedPtr) % ALIGN != 0)
                Mask4AlignedPtr = (int*) ((((ulong) Mask4AlignedPtr) + ALIGN) & ~(ALIGN - 1));
            Mask4.CopyTo(new Span<byte>(Mask4AlignedPtr, Mask4.Length));
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.AggressiveInlining)]
        public static void Sort<T>(int* left, int length, int* tmp) where T : unmanaged, IComparable<T>
        {
            Stats.BumpSmallSorts();
            Stats.BumpSmallSortsSize((ulong) (length));

            Debug.Assert(length % 8 == 0, "length must be multiple of 8");
            Debug.Assert(length >= 8, "length must be >= 8");

            var lengthInVectors = length/8;
            var cnt = (Vector256<int>*) tmp;

            var seed = Vector256<int>.Zero;
            var eight = Vector256.Create(8);
            for (var i = 0; i < lengthInVectors; i++) {
                Store((int*) (cnt + i), seed);
                seed = Add(seed, eight);
            }

            Stats.BumpVectorizedLoads(2);
            var mask0 = LoadAlignedVector256(Mask0AlignedPtr);
            var mask4 = LoadAlignedVector256(Mask4AlignedPtr);

            // It would make to pre-compute the masks before
            // the whole thing starts, and be done with it,
            // but we have "only" 16 regs, and we'd be out of registers
            // if all the masks are pre-computed.
            // So:
            // * mask0+mask4 are loaded from memory,
            // * mask1+mask2 are precomputed here
            // * mask3+mask5+mask6 are recomputed from mask0,mask4
            //   in every iteration, and it should be slightly cheaper
            //   that loading them from stack every time, since the shuffle
            //   operation used to generate them takes only 1 cycle
            var mask1 = Shuffle(mask0, 0b_01_01_00_00); //(0,  0, -1, -1, -1, -1, -1, -1);
            var mask2 = Shuffle(mask0, 0b_01_00_00_00); //(0,  0,  0, -1, -1, -1, -1, -1);

            for (var i = 0; i < lengthInVectors; i++) {
                Stats.BumpVectorizedLoads(1);
                var current = LoadDquVector256(left + i*8);
                var reg0 = Shuffle(current, 0b00_00_00_00);
                var reg1 = Shuffle(current, 0b01_01_01_01);
                var reg2 = Shuffle(current, 0b10_10_10_10);
                var reg3 = Shuffle(current, 0b11_11_11_11);
                var reg4 = Permute4x64(reg0.AsInt64(), 0b10_10_10_10).AsInt32();
                var reg5 = Permute4x64(reg1.AsInt64(), 0b10_10_10_10).AsInt32();
                var reg6 = Permute4x64(reg2.AsInt64(), 0b10_10_10_10).AsInt32();
                var reg7 = Permute4x64(reg3.AsInt64(), 0b10_10_10_10).AsInt32();
                reg0 = Permute4x64(reg0.AsInt64(), 0b00_00_00_00).AsInt32();
                reg1 = Permute4x64(reg1.AsInt64(), 0b00_00_00_00).AsInt32();
                reg2 = Permute4x64(reg2.AsInt64(), 0b00_00_00_00).AsInt32();
                reg3 = Permute4x64(reg3.AsInt64(), 0b00_00_00_00).AsInt32();

                Vector256<int> data;
                Vector256<int> sum;
                for (var j = 0; j < i; j++) {
                    Stats.BumpVectorizedLoads(2);
                    data = LoadDquVector256(left + j*8);
                    sum = LoadDquVector256((int*) (cnt + j));
                    sum = Subtract(sum, CompareGreaterThan(data, reg0));
                    sum = Subtract(sum, CompareGreaterThan(data, reg1));
                    sum = Subtract(sum, CompareGreaterThan(data, reg2));
                    sum = Subtract(sum, CompareGreaterThan(data, reg3));
                    sum = Subtract(sum, CompareGreaterThan(data, reg4));
                    sum = Subtract(sum, CompareGreaterThan(data, reg5));
                    sum = Subtract(sum, CompareGreaterThan(data, reg6));
                    sum = Subtract(sum, CompareGreaterThan(data, reg7));

                    Stats.BumpVectorizedStores();
                    Store((int *) (cnt + j), sum);
                }
                Stats.BumpVectorizedLoads();
                var currentSum = LoadDquVector256((int*) (cnt + i));
                currentSum = Subtract(currentSum, CompareGreaterThan(current, reg0));
                currentSum = Subtract(currentSum, CompareGreaterThan(current, reg1));
                currentSum = Subtract(currentSum, CompareGreaterThan(current, reg2));
                currentSum = Subtract(currentSum, CompareGreaterThan(current, reg3));
                currentSum = Subtract(currentSum, CompareGreaterThan(current, reg4));
                currentSum = Subtract(currentSum, CompareGreaterThan(current, reg5));
                currentSum = Subtract(currentSum, CompareGreaterThan(current, reg6));
                currentSum = Subtract(currentSum, CompareGreaterThan(current, reg7));

                currentSum = Subtract(currentSum, And(CompareEqual(reg0, current), mask0));
                currentSum = Subtract(currentSum, And(CompareEqual(reg1, current), mask1));
                currentSum = Subtract(currentSum, And(CompareEqual(reg2, current), mask2));
                currentSum = Subtract(currentSum, And(CompareEqual(reg3, current), Shuffle(mask0, 0b_00_00_00_00)));
                currentSum = Subtract(currentSum, And(CompareEqual(reg4, current), mask4));
                currentSum = Subtract(currentSum, And(CompareEqual(reg5, current), Shuffle(mask4, 0b_01_01_00_00)));
                currentSum = Subtract(currentSum, And(CompareEqual(reg6, current), Shuffle(mask4, 0b_01_00_00_00)));
                Stats.BumpVectorizedStores();
                Store((int *) (cnt + i), currentSum);

                for (var j = i+1; j < lengthInVectors; j++) {
                    Stats.BumpVectorizedLoads(2);
                    data = LoadDquVector256(left + j*8);
                    sum = LoadDquVector256((int*) (cnt + j));
                    sum = Add(sum, CompareGreaterThan(reg0, data));
                    sum = Add(sum, CompareGreaterThan(reg1, data));
                    sum = Add(sum, CompareGreaterThan(reg2, data));
                    sum = Add(sum, CompareGreaterThan(reg3, data));
                    sum = Add(sum, CompareGreaterThan(reg4, data));
                    sum = Add(sum, CompareGreaterThan(reg5, data));
                    sum = Add(sum, CompareGreaterThan(reg6, data));
                    sum = Add(sum, CompareGreaterThan(reg7, data));
                    Stats.BumpVectorizedStores();
                    Store((int*) (cnt + j), sum);
                }
            }

            var writeTmp = tmp + length;

            for (int* kp = (int*) cnt, sp = left; kp < writeTmp; kp += 8, sp += 8) {
                writeTmp[kp[0]] = sp[0];
                writeTmp[kp[1]] = sp[1];
                writeTmp[kp[2]] = sp[2];
                writeTmp[kp[3]] = sp[3];
                writeTmp[kp[4]] = sp[4];
                writeTmp[kp[5]] = sp[5];
                writeTmp[kp[6]] = sp[6];
                writeTmp[kp[7]] = sp[7];
            }

            new ReadOnlySpan<int>(writeTmp, length).CopyTo(new Span<int>(left, length));
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public static void PCSort256Aligned(int* left, int length, int* tmp)
        {
            Stats.BumpSmallSorts();
            Stats.BumpSmallSortsSize((ulong) (length));

            Debug.Assert(length % 8 == 0, "length must be multiple of 8");
            Debug.Assert(length >= 8, "length must be >= 8");
            Debug.Assert((ulong) left % ALIGN == 0, $"left pointer must be aligned to {ALIGN}");

            var lengthInVectors = length/8;
            var cnt = (Vector256<int>*) tmp;

            var seed = Vector256<int>.Zero;
            var eight = Vector256.Create(8);
            for (var i = 0; i < lengthInVectors; i++) {
                Store((int*) (cnt + i), seed);
                seed = Add(seed, eight);
            }


            Stats.BumpVectorizedLoads(2);
            var mask0 = LoadAlignedVector256(Mask0AlignedPtr);
            var mask4 = LoadAlignedVector256(Mask4AlignedPtr);

            // It would make to pre-compute the masks before
            // the whole thing starts, and be done with it,
            // but we have "only" 16 regs, and we'd be out of registers
            // if all the masks are pre-computed.
            // So:
            // * mask0+mask4 are loaded from memory,
            // * mask1+mask2 are precomputed here
            // * mask3+mask5+mask6 are recomputed from mask0,mask4
            //   in every iteration, and it should be slightly cheaper
            //   that loading them from stack every time, since the shuffle
            //   operation used to generate them takes only 1 cycle
            var mask1 = Shuffle(mask0, 0b_01_01_00_00); //(0,  0, -1, -1, -1, -1, -1, -1);
            var mask2 = Shuffle(mask0, 0b_01_00_00_00); //(0,  0,  0, -1, -1, -1, -1, -1);

            for (var i = 0; i < lengthInVectors; i++) {
                Stats.BumpVectorizedLoads();
                var current = LoadAlignedVector256(left + i*8);
                var reg0 = Shuffle(current, 0b00_00_00_00);
                var reg1 = Shuffle(current, 0b01_01_01_01);
                var reg2 = Shuffle(current, 0b10_10_10_10);
                var reg3 = Shuffle(current, 0b11_11_11_11);
                var reg4 = Permute4x64(reg0.AsInt64(), 0b10_10_10_10).AsInt32();
                var reg5 = Permute4x64(reg1.AsInt64(), 0b10_10_10_10).AsInt32();
                var reg6 = Permute4x64(reg2.AsInt64(), 0b10_10_10_10).AsInt32();
                var reg7 = Permute4x64(reg3.AsInt64(), 0b10_10_10_10).AsInt32();
                reg0 = Permute4x64(reg0.AsInt64(), 0b00_00_00_00).AsInt32();
                reg1 = Permute4x64(reg1.AsInt64(), 0b00_00_00_00).AsInt32();
                reg2 = Permute4x64(reg2.AsInt64(), 0b00_00_00_00).AsInt32();
                reg3 = Permute4x64(reg3.AsInt64(), 0b00_00_00_00).AsInt32();

                Vector256<int> data;
                Vector256<int> sum;
                for (var j = 0; j < i; j++) {
                    Stats.BumpVectorizedLoads(2);
                    data = LoadAlignedVector256(left + j*8);
                    sum = LoadDquVector256((int*) (cnt + j));
                    sum = Subtract(sum, CompareGreaterThan(data, reg0));
                    sum = Subtract(sum, CompareGreaterThan(data, reg1));
                    sum = Subtract(sum, CompareGreaterThan(data, reg2));
                    sum = Subtract(sum, CompareGreaterThan(data, reg3));
                    sum = Subtract(sum, CompareGreaterThan(data, reg4));
                    sum = Subtract(sum, CompareGreaterThan(data, reg5));
                    sum = Subtract(sum, CompareGreaterThan(data, reg6));
                    sum = Subtract(sum, CompareGreaterThan(data, reg7));
                    Stats.BumpVectorizedStores();
                    Store((int *) (cnt + j), sum);
                }

                Stats.BumpVectorizedLoads(1);
                var currentSum = LoadDquVector256((int*) (cnt + i));
                currentSum = Subtract(currentSum, CompareGreaterThan(current, reg0));
                currentSum = Subtract(currentSum, CompareGreaterThan(current, reg1));
                currentSum = Subtract(currentSum, CompareGreaterThan(current, reg2));
                currentSum = Subtract(currentSum, CompareGreaterThan(current, reg3));
                currentSum = Subtract(currentSum, CompareGreaterThan(current, reg4));
                currentSum = Subtract(currentSum, CompareGreaterThan(current, reg5));
                currentSum = Subtract(currentSum, CompareGreaterThan(current, reg6));
                currentSum = Subtract(currentSum, CompareGreaterThan(current, reg7));

                currentSum = Subtract(currentSum, And(CompareEqual(reg0, current), mask0));
                currentSum = Subtract(currentSum, And(CompareEqual(reg1, current), mask1));
                currentSum = Subtract(currentSum, And(CompareEqual(reg2, current), mask2));
                currentSum = Subtract(currentSum, And(CompareEqual(reg3, current), Shuffle(mask0, 0b_00_00_00_00)));
                currentSum = Subtract(currentSum, And(CompareEqual(reg4, current), mask4));
                currentSum = Subtract(currentSum, And(CompareEqual(reg5, current), Shuffle(mask4, 0b_01_01_00_00)));
                currentSum = Subtract(currentSum, And(CompareEqual(reg6, current), Shuffle(mask4, 0b_01_00_00_00)));
                Stats.BumpVectorizedStores();
                Store((int *) (cnt + i), currentSum);

                for (var j = i+1; j < lengthInVectors; j++) {
                    Stats.BumpVectorizedLoads(2);
                    data = LoadAlignedVector256(left + j*8);
                    sum = LoadDquVector256((int*) (cnt + j));
                    sum = Add(sum, CompareGreaterThan(reg0, data));
                    sum = Add(sum, CompareGreaterThan(reg1, data));
                    sum = Add(sum, CompareGreaterThan(reg2, data));
                    sum = Add(sum, CompareGreaterThan(reg3, data));
                    sum = Add(sum, CompareGreaterThan(reg4, data));
                    sum = Add(sum, CompareGreaterThan(reg5, data));
                    sum = Add(sum, CompareGreaterThan(reg6, data));
                    sum = Add(sum, CompareGreaterThan(reg7, data));
                    Stats.BumpVectorizedStores();
                    Store((int*) (cnt + j), sum);
                }
            }

            var writeTmp = tmp + length;

            for (int* kp = (int*) cnt, sp = left; kp < writeTmp; kp += 8, sp += 8) {
                writeTmp[kp[0]] = sp[0];
                writeTmp[kp[1]] = sp[1];
                writeTmp[kp[2]] = sp[2];
                writeTmp[kp[3]] = sp[3];
                writeTmp[kp[4]] = sp[4];
                writeTmp[kp[5]] = sp[5];
                writeTmp[kp[6]] = sp[6];
                writeTmp[kp[7]] = sp[7];
            }

            new ReadOnlySpan<int>(writeTmp, length).CopyTo(new Span<int>(left, length));
        }
    }
}

