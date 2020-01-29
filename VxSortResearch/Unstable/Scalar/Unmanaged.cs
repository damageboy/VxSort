using System;
using VxSortResearch.Statistics;

namespace VxSortResearch.Unstable.Scalar
{
    public static class Unmanaged
    {
        public static unsafe void Sort<T>(T[] array) where T : unmanaged, IComparable<T>
        {
            if (array == null)
                throw new ArgumentNullException(nameof(array));

            Stats.BumpSorts(nameof(Unmanaged), array.Length);
            fixed (T* p = &array[0]) {
                UnmanagedGenericQuickSort<T>.Sort(p, p + array.Length - 1);
            }
        }

        class UnmanagedGenericQuickSort<T> where T : unmanaged, IComparable<T>
        {
            public static unsafe void Sort(T* left, T* right)
            {
                var partitionSize = right - left + 1;

                switch (partitionSize) {
                    case 0:
                    case 1:
                        return;
                    case 2:
                        SwapIfGreater(left, right);
                        return;
                    case 3:
                        SwapIfGreater(left,      right - 1);
                        SwapIfGreater(left,      right);
                        SwapIfGreater(right - 1, right);
                        return;
                }

                if (partitionSize <= 16) {
                    InsertionSort(left, right);
                    return;
                }

                var pivotPos = PickPivotAndPartition(left, right);
                Stats.BumpDepth(1);
                Sort(left,         pivotPos - 1);
                Sort(pivotPos + 1, right);
                Stats.BumpDepth(-1);
            }

            static unsafe void Swap(T* left, T* right)
            {
                var tmp = *left;
                *left  = *right;
                *right = tmp;
            }

            static unsafe void SwapIfGreater(T* left, T* right)
            {
                Stats.BumpScalarCompares();
                if ((*left).CompareTo(*right) <= 0) return;
                Swap(left, right);
            }

            static unsafe void InsertionSort(T* left, T* right)
            {
                Stats.BumpSmallSorts();
                Stats.BumpSmallSortsSize((ulong) (right - left));
                Stats.BumpSmallSortScalarCompares(
                    (ulong) (((right - left) * (right - left + 1)) / 2)); // Sum of sequence

                T* i;
                T* j;
                T t;
                for (i = left; i < right; i++) {
                    j = i;
                    t = *(i + 1);
                    while (j >= left && t.CompareTo(*j) < 0) {
                        *(j + 1) = *j;
                        j--;
                    }

                    *(j + 1) = t;
                }
            }

            static unsafe T* PickPivotAndPartition(T* lo, T* hi)
            {
                Stats.BumpPartitionOperations();

                // Compute median-of-three.  But also partition them, since we've done the comparison.
                var mid = lo + ((hi - lo) / 2);

                // Sort lo, mid and hi appropriately, then pick mid as the pivot.
                SwapIfGreater(lo,  mid); // swap the low with the mid point
                SwapIfGreater(lo,  hi);  // swap the low with the high
                SwapIfGreater(mid, hi);  // swap the middle with the high

                var pivot = *mid;
                Swap(mid, hi - 1);
                // We already partitioned lo and hi and put the pivot in hi - 1.  And we pre-increment & decrement below.
                var left = lo;
                var right = hi - 1;

                while (left < right) {
                    while ((*++left).CompareTo(pivot) < 0)
                        Stats.BumpScalarCompares();
                    while (pivot.CompareTo(*--right) < 0)
                        Stats.BumpScalarCompares();

                    if (left >= right) break;

                    Swap(left, right);
                }

                Swap(left, hi - 1);

                return left;
            }
        }
    }
}
