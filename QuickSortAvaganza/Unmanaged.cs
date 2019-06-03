using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace QuickSort.cs
{
    public class Unmanaged
    {


        public static unsafe void QuickSort<T>(T* items, int left, int right) where T : unmanaged, IComparable<T>
        {
            if (left == right) return;
            int pivot = Partition(items, left, right);
            QuickSort(items, left, pivot);
            QuickSort(items, pivot + 1, right);
        }

        static unsafe int Partition<T>(T* items, int left, int right) where T : unmanaged, IComparable<T>
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
