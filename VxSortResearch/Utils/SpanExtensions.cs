using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace VxSortResearch.Utils
{
    public static class SpanExtensions
    {
        public static string Dump<T>(this ReadOnlySpan<T> span)
        {
            var sb = new StringBuilder();
            sb.Append("[");
            sb.Append(span.Length);
            sb.Append("] <");

            var i = 0;
            foreach (var x in span)
            {
                if (i % 10 == 0)
                    sb.AppendFormat(CultureInfo.InvariantCulture, "[{0}]:", i);
                i++;

                sb.Append(x);
                sb.Append(", ");
            }

            sb.Length -= 2;

            sb.Append(">");
            return sb.ToString();
        }

        public static string Dump<T>(this Span<T> span) => ((ReadOnlySpan<T>) span).Dump();

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