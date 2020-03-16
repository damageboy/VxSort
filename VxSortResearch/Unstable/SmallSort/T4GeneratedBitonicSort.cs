using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using static System.Runtime.Intrinsics.X86.Avx;
using static System.Runtime.Intrinsics.X86.Avx2;

namespace VxSortResearch.Unstable.SmallSort
{
    using V = Vector256<int>;

    internal static partial class T4GeneratedBitonicSort<T> where T : unmanaged
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
    }
}