namespace VxSortResearch.Stable
{
    internal static class ScalarQuickSortHelpers
    {
#if DEBUG
       internal static int depth;
#endif
        // for fallback
        internal static unsafe void Partition(int* array, int pivot, ref int left, ref int right) {

            while (left <= right) {
                while (array[left] < pivot) {
                    left += 1;
                }
                while (array[right] > pivot) {
                    right -= 1;
                }
                if (left <= right) {
                    var t = array[left];
                    array[left]      = array[right];
                    array[right]     = t;
                    left  += 1;
                    right -= 1;
                }
            }
        }

        //fallback
        internal static unsafe void QuickSort(int* array, int left, int right)
        {
            var i = left;
            var j = right;
            var pivot = array[(i + j)/2];

            Partition(array, pivot, ref i, ref j);
            if (left < j) {
                QuickSort(array, left, j);
            }
            if (i < right) {
                QuickSort(array, i, right);
            }
        }


    }
}
