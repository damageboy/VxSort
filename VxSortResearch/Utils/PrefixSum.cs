using System;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using static System.Runtime.Intrinsics.X86.Avx;
using static System.Runtime.Intrinsics.X86.Avx2;


namespace VxSortResearch.Utils
{
    internal class PrefixSum<T> where T : unmanaged
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static unsafe void SumVectorized(T* p)
        {
            if (typeof(T) == typeof(float)) {
                Store((float *) p, SumVectorized(LoadVector256((float *) p)));
                return;
            }
            else if (typeof(T) == typeof(int)) {
                Store((int *) p, SumVectorized(LoadDquVector256((int *) p)));
                return;
            }

            throw new NotSupportedException($"type {typeof(T).Name} is not supported");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static Vector256<T> SumVectorized(Vector256<T> x)
        {
            if (typeof(T) == typeof(float)) {
                return SumVectorized(x.AsSingle()).As<float, T>();
            }
            else if (typeof(T) == typeof(int)) {
                return SumVectorized(x.AsInt32()).As<int, T>();
            }
            else if (typeof(T) == typeof(uint)) {
                return SumVectorized(x.AsUInt32()).As<uint, T>();

            }
            throw new NotSupportedException($"type {typeof(T).Name} is not supported");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static Vector256<float> SumVectorized(Vector256<float> x) {
            Vector256<float> tmp0, tmp1;

            tmp0 = Permute(x, 0b10_01_00_11);
            tmp1 = Permute2x128(tmp0, tmp0, 0b00_10_10_01);
            x  = Add(x, Blend(tmp0, tmp1, 0b0_0_0_1_0_0_0_1));

            tmp0 = Permute(x, 0b01_00_11_10);
            tmp1 = Permute2x128(tmp0, tmp0, 41);
            x  = Add(x, Blend(tmp0, tmp1, 0b0_0_1_1_0_0_1_1));

            x = Add(x, Permute2x128(x, x, 0b00_10_10_01));
            return x;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static Vector256<int> SumVectorized(Vector256<int> x)
        {
            Vector256<int> tmp0, tmp1;

            tmp0 = Permute(x.AsSingle(), 0b10_01_00_11).AsInt32();
            tmp1 = Permute2x128(tmp0, tmp0, 0b00_10_10_01);
            x  = Add(x, Blend(tmp0, tmp1, 0b0_0_0_1_0_0_0_1));

            tmp0 = Permute(x.AsSingle(), 0b01_00_11_10).AsInt32();
            tmp1 = Permute2x128(tmp0, tmp0, 41);
            x  = Add(x, Blend(tmp0, tmp1, 0b0_0_1_1_0_0_1_1));

            x = Add(x, Permute2x128(x, x, 0b00_10_10_01));
            return x;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static Vector256<uint> SumVectorized(Vector256<uint> x)
        {
            Vector256<uint> tmp0, tmp1;

            tmp0 = Permute(x.AsSingle(), 0b10_01_00_11).AsUInt32();
            tmp1 = Permute2x128(tmp0, tmp0, 0b00_10_10_01);
            x    = Add(x, Blend(tmp0, tmp1, 0b0_0_0_1_0_0_0_1));

            tmp0 = Permute(x.AsSingle(), 0b01_00_11_10).AsUInt32();
            tmp1 = Permute2x128(tmp0, tmp0, 41);
            x    = Add(x, Blend(tmp0, tmp1, 0b0_0_1_1_0_0_1_1));

            x = Add(x, Permute2x128(x, x, 0b00_10_10_01));
            return x;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static unsafe void SumScalar(T* p)
        {
            if (typeof(T) == typeof(float)) {
                SumScalar((float*) p);
                return;
            }
            else if (typeof(T) == typeof(int)) {
                SumScalar((int*) p);
                return;
            }

            throw new NotSupportedException($"type {typeof(T).Name} is not supported");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static unsafe void SumScalar(float* p)
        {
            p[1] += p[0];
            p[2] += p[1];
            p[3] += p[2];
            p[4] += p[3];
            p[5] += p[4];
            p[6] += p[5];
            p[7] += p[6];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static unsafe void SumScalar(int* p)
        {
            p[1] += p[0];
            p[2] += p[1];
            p[3] += p[2];
            p[4] += p[3];
            p[5] += p[4];
            p[6] += p[5];
            p[7] += p[6];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static unsafe Vector256<T> LoadVector256Generic(T* p)
        {
            if (typeof(T) == typeof(float)) {
                return Avx.LoadVector256((float *) p).As<float, T>();
            }
            else if (typeof(T) == typeof(int)) {
                return LoadDquVector256((int *) p).As<int, T>();
            }

            throw new NotSupportedException($"type {typeof(T).Name} is not supported");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static unsafe void StoreVector256Generic(T *p, Vector256<T> v)
        {
            if (typeof(T) == typeof(float)) {
                Store((float *) p, v.As<T, float>());
                return;
            }
            else if (typeof(T) == typeof(int)) {
                Store((int *) p, v.As<T, int>());
                return;
            }

            throw new NotSupportedException($"type {typeof(T).Name} is not supported");
        }
    }
}
