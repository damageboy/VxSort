using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using static System.Runtime.Intrinsics.X86.Avx;
using static System.Runtime.Intrinsics.X86.Avx2;

namespace VxSortResearch.Unstable.SmallSort
{
    using V = Vector256<int>;

    internal static partial class GenericBitonicSort<T> where T : unmanaged
    {

        // Legend:
        // X - shuffle/permute mask for generating a cross (X) shuffle
        //     the numbers (1,2,4) denote the stride of the shuffle operation
        // B - Blend mask, used for blending two vectors according to a given order
        //     the numbers (1,2,4) denote the "stride" of blending, e.g. 1 means switch vectors
        //     every element, 2 means switch vectors every two elements and so on...
        // P - Permute mask, read specific comment about it below...
        const byte X_1 = 0b10_11_00_01;
        const byte X_2 = 0b01_00_11_10;
        const byte B_1 = 0b10_10_10_10;
        const byte B_2 = 0b11_00_11_00;
        const byte B_4 = 0b11_11_00_00;

        // Shuffle (X_R) + Permute (P_X) is a more efficient way
        // (copied shamelessly from LLVM through compiler explorer)
        // For implementing X_4, which requires a cross 128-bit lane operation.
        // A Shuffle (1c lat / 1c tp) + 64 bit permute (3c lat / 1c tp) take 1 more cycle to execute than the
        // the alternative: PermuteVar8x32 / VPERMD which takes (3c lat / 1c tp)
        // But, the latter requires loading the permutation entry from cache, which can take up to 5 cycles (when cached)
        // and costs one more register, which steals a register from us for high-count bitonic sorts.
        // In short, it's faster this way, from my attempts...
        const byte X_R = 0b00_01_10_11;
        const byte P_X = 0b01_00_11_10;
        
        // Basic 8-element bitonic sort
        // This will get composed and inlined throughout
        // the various bitonic-sort sizes:
        // BitonicSort1V will be directly embedded in BitonicSort{2,3,5,9}V
        // BitonicSort2V will be directly embedded in BitonicSort{3,4,6,10}V
        // BitonicSort3V will be directly embedded in BitonicSort{7,11}V
        // etc.
        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static void BitonicSort01VGeneric(ref Vector256<T> d)
        {
            // ReSharper disable JoinDeclarationAndInitializer
            Vector256<T> min, max, s;
            // ReSharper restore JoinDeclarationAndInitializer
            s   = ShuffleX1(d);
            min = MinV(s, d);
            max = MaxV(s, d);
            d   = BlendB1(min, max);

            s   = ShuffleXR(d);
            min = MinV(s, d);
            max = MaxV(s, d);
            d   = BlendB2(min, max);

            s   = ShuffleX1(d);
            min = MinV(s, d);
            max = MaxV(s, d);
            d   = BlendB1(min, max);

            if (Unsafe.SizeOf<T>() == 4) {
                s   = Reverse(d);
                min = MinV(s, d);
                max = MaxV(s, d);
                d   = BlendB4(min, max);

                s   = ShuffleX2(d);
                min = MinV(s, d);
                max = MaxV(s, d);
                d   = BlendB2(min, max);

                s   = ShuffleX1(d);
                min = MinV(s, d);
                max = MaxV(s, d);
                d   = BlendB1(min, max);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        static void BitonicSort01VMergeGeneric(ref Vector256<T> d)
        {
            // ReSharper disable JoinDeclarationAndInitializer
            Vector256<T> min, max, s;
            // ReSharper restore JoinDeclarationAndInitializer

            if (Unsafe.SizeOf<T>() == 4) {
                s   = Cross(d);
                min = MinV(s, d);
                max = MaxV(s, d);
                d   = BlendB4(min, max);
            }

            s   = ShuffleX2(d);
            min = MinV(s, d);
            max = MaxV(s, d);
            d   = BlendB2(min, max);

            s   = ShuffleX1(d);
            min = MinV(s, d);
            max = MaxV(s, d);
            d   = BlendB1(min, max);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        static Vector256<T> Reverse(in Vector256<T> v)
        {
            if (Unsafe.SizeOf<T>() == 4) {
                return Permute4x64(
                    Shuffle(v.AsInt32(), X_R).AsInt64(), P_X).As<long, T>();
            } else if (Unsafe.SizeOf<T>() == 8) {
                return Permute4x64(v.AsDouble(), 0b00_01_10_11).As<double, T>();
            }
            else {
                throw new NotImplementedException("This type is not supported yet");
            }
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        static Vector256<T> Cross(in Vector256<T> v)
        {
            if (Unsafe.SizeOf<T>() == 4) {
                return Permute4x64(v.AsInt64(), P_X).As<long, T>();
            }
            throw new NotImplementedException("This type is not supported yet");
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        static Vector256<T> BlendB1(in Vector256<T> v1, in Vector256<T> v2)
        {
            if (Unsafe.SizeOf<T>() == 4) {
                return Blend(v1.AsInt32(), v2.AsInt32(), B_1).As<int, T>();
            } else if (Unsafe.SizeOf<T>() == 8) {
                return Blend(v1.AsDouble(), v2.AsDouble(), 0b10_10).As<double, T>();
            }

            throw new NotImplementedException("This type is not supported yet");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        static Vector256<T> BlendB2(in Vector256<T> v1, in Vector256<T> v2)
        {
            if (Unsafe.SizeOf<T>() == 4) {
                return Blend(v1.AsInt32(), v2.AsInt32(), B_2).As<int, T>();
            } else if (Unsafe.SizeOf<T>() == 8) {
                return Blend(v1.AsDouble(), v2.AsDouble(), 0b11_00).As<double, T>();
            }

            throw new NotImplementedException("This type is not supported yet");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        static Vector256<T> BlendB4(in Vector256<T> v1, in Vector256<T> v2)
        {
            if (Unsafe.SizeOf<T>() == 4) {
                return Blend(v1.AsInt32(), v2.AsInt32(), B_4).As<int, T>();
            }
            throw new NotImplementedException("This type is not supported yet");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        internal static Vector256<T> MinV(in Vector256<T> v1, in Vector256<T> v2)
        {
            if (typeof(T) == typeof(int)) {
                return Min(v1.AsInt32(), v2.AsInt32()).As<int, T>();
            } else if (typeof(T) == typeof(uint)) {
                return Min(v1.AsUInt32(), v2.AsUInt32()).As<uint, T>();
            } else if (typeof(T) == typeof(long)) {
                return BlendVariable(v1.AsInt64(), v2.AsInt64(), 
                    CompareGreaterThan(v1.AsInt64(), v2.AsInt64())).As<long, T>();
            } else if (typeof(T) == typeof(ulong)) {
                var topBit = Vector256.Create(0x8000000000000000UL).AsInt64();
                return BlendVariable(v1.AsInt64(), v2.AsInt64(),
                    CompareGreaterThan(Xor(topBit, v1.AsInt64()), Xor(topBit, v2.AsInt64()))).As<long, T>();
            } else if (typeof(T) == typeof(float)) {
                return Min(v1.AsSingle(), v2.AsSingle()).As<float, T>();
            } else if (typeof(T) == typeof(double)) {
                return Min(v1.AsDouble(), v2.AsDouble()).As<double, T>();
            }

            throw new NotImplementedException("This type is not supported yet");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        internal static Vector256<T> MaxV(in Vector256<T> v1, in Vector256<T> v2)
        {
            if (typeof(T) == typeof(int)) {
                return Max(v1.AsInt32(), v2.AsInt32()).As<int, T>();
            }
            if (typeof(T) == typeof(uint)) {
                return Max(v1.AsUInt32(), v2.AsUInt32()).As<uint, T>();
            }
            else if (typeof(T) == typeof(long)) {
                return BlendVariable(v1.AsInt64(), v2.AsInt64(), 
                    CompareGreaterThan(v2.AsInt64(), v1.AsInt64())).As<long, T>();
            } else if (typeof(T) == typeof(ulong)) {
                var topBit = Vector256.Create(0x8000000000000000UL).AsInt64();
                return BlendVariable(v2.AsInt64(), v1.AsInt64(),
                    CompareGreaterThan(Xor(topBit, v1.AsInt64()), Xor(topBit, v2.AsInt64()))).As<long, T>();
            } else if (typeof(T) == typeof(float)) {
                return Max(v1.AsSingle(), v2.AsSingle()).As<float, T>();
            }
            else if (typeof(T) == typeof(double)) {
                return Max(v1.AsDouble(), v2.AsDouble()).As<double, T>();
            }
            throw new NotImplementedException("This type is not supported yet");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        static Vector256<T> ShuffleX1(in Vector256<T> v)
        {
            if (Unsafe.SizeOf<T>() == 4) {
                return Shuffle(v.AsInt32(), X_1).As<int, T>();
            } else if (Unsafe.SizeOf<T>() == 8) {
                return Shuffle(v.AsDouble(), v.AsDouble(), 0b01_01).As<double, T>();
            }

            throw new NotImplementedException("This type is not supported yet");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        static Vector256<T> ShuffleX2(in Vector256<T> v)
        {
            if (Unsafe.SizeOf<T>() == 4) {
                return Shuffle(v.AsInt32(), X_2).As<int, T>();
            } else if (Unsafe.SizeOf<T>() == 8) {
                return Permute4x64(v.AsDouble(), 0b01_00_11_10).As<double, T>();
            }
            throw new NotImplementedException("This type is not supported yet");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        static Vector256<T> ShuffleXR(in Vector256<T> v)
        {
            if (Unsafe.SizeOf<T>() == 4) {
                return Shuffle(v.AsInt32(), X_R).As<int, T>();
            } else if (Unsafe.SizeOf<T>() == 8) {
                return Permute4x64(v.AsDouble(), X_R).As<double, T>();
            }

            throw new NotImplementedException("This type is not supported yet");
        }
    }
}