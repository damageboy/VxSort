using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace VxSortResearch.Utils
{
    public static class SpanExtensions
    {
        public static unsafe void * AlignSpan(this ReadOnlySpan<byte> unalignedSpan, ulong alignment)
        {
            var alignedPtr = (byte*) Marshal.AllocHGlobal(unalignedSpan.Length + (int) alignment);
            var x = alignedPtr;
            if (((ulong) alignedPtr) % alignment != 0)
                alignedPtr = (byte *) (((ulong) alignedPtr + alignment) & ~(alignment - 1));

            Debug.Assert((ulong) alignedPtr % alignment == 0);
            unalignedSpan.CopyTo(new Span<byte>(alignedPtr, unalignedSpan.Length));
            return alignedPtr;
        }
    }
}