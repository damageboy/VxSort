using System;
using System.Diagnostics;
using System.Runtime.Intrinsics;
using VxSortResearch.PermutationTables;
using static System.Runtime.Intrinsics.X86.Avx;
using static System.Runtime.Intrinsics.X86.Avx2;
using static System.Runtime.Intrinsics.X86.Popcnt;
using static VxSortResearch.PermutationTables.BitPermTables;

namespace VxSortResearch.Stable.AVX2
{
    public static class AVX2OnePass
    {
        public static unsafe void QuickSort<T>(T[] array) where T : unmanaged, IComparable<T>
        {
            if (array == null)
                throw new ArgumentNullException(nameof(array));

            fixed (T* p = &array[0])
            {
                if (array == null)
                    throw new ArgumentNullException(nameof(array));

                if (typeof(T) == typeof(int))
                    QuickSortInt32<int>.Sort((int*) p, array.Length);
            }
        }

        class QuickSortInt32<T> where T : unmanaged, IComparable<T>
        {
            internal static unsafe void Sort(int* array, int length)
            {
                var sep = VectorizedPartition(array, length);
                if (sep == length) {
                    // we have an ineffective pivot. Let us give up.
                    if (length > 1) ScalarQuickSortHelpers.QuickSort(array, 0, length - 1);
                }
                else {
                    if (sep > 2) {
                        Sort(array, sep - 1);
                    }

                    if (sep + 1 < length) {
                        Sort(array + sep, length - sep);
                    }
                }
            }
            static unsafe void Swap(int* left, int* right)
            {
                var tmp = *left;
                *left  = *right;
                *right = tmp;
            }

            static unsafe void SwapIfGreater(int* left, int* right)
            {
                if (*left <= *right) return;
                Swap(left, right);
            }

            static unsafe int VectorizedPartition(int* array, int length)
            {
                // We first select a pivot
                // We partition the array so that anything inside the range [0..boundary) <= pivot
                // The value at array[boundary -1] == pivot
                // The values [boundary..length] > pivot
                // the function returns the location of the boundary

                var origArray = array;
#if DEBUG
            var origLength = length;
            var arraySpan = new ReadOnlySpan<int>(array, length);
#endif

                if (length <= 1)
                    return 1;

                int* boundary;
                var right = array + length - 1;

                // Compute median-of-three.  But also partition them, since we've done the comparison.
                var mid = length / 2;
                // Sort lo, mid and hi appropriately, then pick mid as the pivot.
                SwapIfGreater(array + 0,   array + mid);
                SwapIfGreater(array + 0,   right);
                SwapIfGreater(array + mid, right);


                var pivot = array[mid]; // we always pick the pivot at the end
                // Move the pivot to the end, so we can easily
                // place it back at the boundary once we know where it is
                Swap(array + mid, right);
                right -= 8;

                var P = Vector256.Create(pivot);
                var pBase = Int32PermTables.IntPermTablePtr;

                while (array <= right) {
                    var data = CheckedLoad(array);
                    var mask = unchecked((uint) MoveMask(CompareGreaterThan(data, P).AsSingle()));
                    if (mask == 0xFF) {
                        boundary =  array;
                        array    += 8;
                        goto shortcut;
                    }

                    var popcnt = 8U - PopCount(mask);

                    data = PermuteVar8x32(data, Int32PermTables.GetIntPermutation(pBase, mask));
                    CheckedStore(array, data);
                    array += popcnt;
                }

                boundary = array;
                shortcut:
                while (array <= right) {
                    var data = CheckedLoad(array);
                    var mask = unchecked((uint) MoveMask(CompareGreaterThan(data, P).AsSingle()));
                    if (mask != 0xFF) {
                        var cnt = 8 - PopCount(mask);
                        var boundaryData = CheckedLoad(boundary);
                        data = PermuteVar8x32(data, Int32PermTables.GetIntPermutation(pBase, mask));
                        CheckedStore(boundary, data);
                        CheckedStore(array,    boundaryData);
                        boundary += cnt;
                    }

                    array += 8;
                }

                right += 8;

                int v;
                int tmp;
                while (array < right) {
                    v = *array;
                    if (v <= pivot) {
                        tmp         = *boundary;
                        *array      = tmp;
                        *boundary++ = v;
                    }

                    array++;
                }

                v           = *array;
                tmp         = *boundary;
                *array      = tmp;
                *boundary++ = v;

#if DEBUG
            for (var t = origArray; t < boundary; t++)
                Debug.Assert(*t <= pivot);
            for (var t = boundary; t <= right; t++)
                Debug.Assert(*t >= pivot);
#endif

                return (int) (boundary - origArray);

#if !DEBUG
                static
#endif
                    Vector256<int> CheckedLoad(int* address)
                {
#if DEBUG
                Debug.Assert(address >= origArray);
                Debug.Assert(address + 8U < origArray + length);
#endif
                    return LoadDquVector256(address);
                }

#if !DEBUG
                static
#endif
                    void CheckedStore(int* address, Vector256<int> boundaryData)
                {
#if DEBUG
                Debug.Assert(address >= origArray);
                Debug.Assert(address + 8U < origArray + length);
#endif
                    Store(address, boundaryData);
                }
            }

        }
    }
}
