using System;

namespace QuickSort.cs
{
    public class Scalar
    {
        public static void QuickSort<T>(T[] items) where T : IComparable<T>
            => QuickSortScalar(items, 0, items.Length);

        static void QuickSortScalar<T>(T[] items, int left, int right) where T : IComparable<T>
        {
            if (left == right) return;
            int pivot = Partition(items, left, right);
            QuickSortScalar(items, left, pivot);
            QuickSortScalar(items, pivot + 1, right);
        }

        static int Partition<T>(T[] items, int left, int right) where T : IComparable<T>
        {
            int pivotPos = (right + left) / 2; // often a random index between left and right is used
            T pivotValue = items[pivotPos];

            Swap(ref items[right - 1], ref items[pivotPos]);
            int store = left;
            for (int i = left; i < right - 1; ++i)
            {
                if (items[i].CompareTo(pivotValue) < 0)
                {
                    Swap(ref items[i], ref items[store]);
                    ++store;
                }
            }

            Swap(ref items[right - 1], ref items[store]);
            return store;
        }

        static void Swap<T>(ref T a, ref T b)
        {
            T temp = a;
            a = b;
            b = temp;
        }
    }
}