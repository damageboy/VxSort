using System;
using VxSortResearch.Statistics;

namespace VxSortResearch.Unstable.Scalar
{
    public static class Managed
    {
        public static void Sort<T>(T[] array) where T : IComparable<T>
        {
            if (array == null)
                throw new ArgumentNullException(nameof(array));

            Stats.BumpSorts(nameof(Managed), array.Length);
            ScalarQuickSort<T>.Sort(array, 0, array.Length - 1);
        }

        class ScalarQuickSort<T> where T : IComparable<T>
        {
            internal static void Sort(T[] items, int left, int right)
            {
                int partitionSize = right - left + 1;

                switch (partitionSize) {
                    case 0:
                    case 1:
                        return;
                    case 2:
                        SwapIfGreater(ref items[left], ref items[right]);
                        return;
                    case 3:
                        SwapIfGreater(ref items[left],      ref items[right - 1]);
                        SwapIfGreater(ref items[left],      ref items[right]);
                        SwapIfGreater(ref items[right - 1], ref items[right]);
                        return;
                }

                var pivotPos = PickPivotAndPartition(items, left, right);
                Stats.BumpDepth(1);
                Sort(items, left,         pivotPos - 1);
                Sort(items, pivotPos + 1, right);
                Stats.BumpDepth(-1);
            }

            static void Swap(ref T left, ref T right)
            {
                var tmp = left;
                left  = right;
                right = tmp;
            }

            static void SwapIfGreater(ref T left, ref T right)
            {
                Stats.BumpScalarCompares();
                if (left.CompareTo(right) <= 0) return;
                Swap(ref left, ref right);
            }

            static int PickPivotAndPartition(T[] items, int lo, int hi)
            {
                Stats.BumpPartitionOperations();
                // Compute median-of-three.  But also partition them, since we've done the comparison.
                var mid = lo + ((hi - lo) / 2);

                // Sort lo, mid and hi appropriately, then pick mid as the pivot.
                SwapIfGreater(ref items[lo],  ref items[mid]); // swap the low with the mid point
                SwapIfGreater(ref items[lo],  ref items[hi]);  // swap the low with the high
                SwapIfGreater(ref items[mid], ref items[hi]);  // swap the middle with the high

                var pivot = items[mid];
                Swap(ref items[mid], ref items[hi - 1]);
                // We already partitioned lo and hi and put the pivot in hi - 1.  And we pre-increment & decrement below.
                var left = lo;
                var right = hi - 1;

                while (left < right) {
                    while ((items[++left]).CompareTo(pivot) < 0)
                        Stats.BumpScalarCompares();
                    ;
                    while (pivot.CompareTo(items[--right]) < 0)
                        Stats.BumpScalarCompares();
                    ;

                    if (left >= right) break;

                    Swap(ref items[left], ref items[right]);
                }

                Swap(ref items[left], ref items[hi - 1]);

                return left;
            }
        }
    }
}
