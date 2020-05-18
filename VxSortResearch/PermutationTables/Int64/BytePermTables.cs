using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using VxSortResearch.Utils;

namespace VxSortResearch.PermutationTables.Int64
{
    public unsafe class BytePermTables
    {
        internal static ReadOnlySpan<byte> BytePermTable => new byte[] {
            0, 1, 2, 3, 4, 5, 6, 7, // 0b0000 (0)
            2, 3, 4, 5, 6, 7, 0, 1, // 0b0001 (1)
            0, 1, 4, 5, 6, 7, 2, 3, // 0b0010 (2)
            4, 5, 6, 7, 0, 1, 2, 3, // 0b0011 (3)
            0, 1, 2, 3, 6, 7, 4, 5, // 0b0100 (4)
            2, 3, 6, 7, 0, 1, 4, 5, // 0b0101 (5)
            0, 1, 6, 7, 2, 3, 4, 5, // 0b0110 (6)
            6, 7, 0, 1, 2, 3, 4, 5, // 0b0111 (7)
            0, 1, 2, 3, 4, 5, 6, 7, // 0b1000 (8)
            2, 3, 4, 5, 0, 1, 6, 7, // 0b1001 (9)
            0, 1, 4, 5, 2, 3, 6, 7, // 0b1010 (10)
            4, 5, 0, 1, 2, 3, 6, 7, // 0b1011 (11)
            0, 1, 2, 3, 4, 5, 6, 7, // 0b1100 (12)
            2, 3, 0, 1, 4, 5, 6, 7, // 0b1101 (13)
            0, 1, 2, 3, 4, 5, 6, 7, // 0b1110 (14)
            0, 1, 2, 3, 4, 5, 6, 7, // 0b1111 (15)
        };


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector256<int> GetBytePermutation(byte * pBase, uint index)
        {
            Debug.Assert(index <= 255);
            Debug.Assert(pBase != null);
            return Avx2.ConvertToVector256Int32(pBase + index * 8);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector256<int> GetBytePermutation(byte * pBase, ulong index)
        {
            Debug.Assert(index <= 255);
            Debug.Assert(pBase != null);
            return Avx2.ConvertToVector256Int32(pBase + index * 8);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector256<int> GetBytePermutationAligned(byte * pBase, uint index)
        {
            Debug.Assert(index <= 255);
            Debug.Assert(pBase != null);
            Debug.Assert(((ulong) (pBase + index * 8)) % 8 == 0);
            return Avx2.ConvertToVector256Int32(pBase + index * 8);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe Vector256<int> GetBytePermutationAligned(byte * pBase, ulong index)
        {
            Debug.Assert(index < 16);
            Debug.Assert(pBase != null);
            Debug.Assert(((ulong) (pBase + index * 8)) % 8 == 0);
            return Avx2.ConvertToVector256Int32(pBase + index * 8);
        }

        internal static readonly byte* BytePermTablePtr;
        internal static readonly byte* BytePermTableAlignedPtr;

        const uint PAGE_SIZE = 4096U;

        static BytePermTables()
        {
            fixed (byte* p = BytePermTables.BytePermTable)
                BytePermTablePtr = p;

            BytePermTableAlignedPtr = (byte*) BytePermTable.AlignSpan(PAGE_SIZE);
        }

    }
}

