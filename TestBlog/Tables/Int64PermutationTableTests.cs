using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Intrinsics;
using NUnit.Framework;
using VxSortResearch.PermutationTables.Int64;
using static System.Runtime.Intrinsics.X86.Avx;
using static System.Runtime.Intrinsics.X86.Avx2;
using static System.Runtime.Intrinsics.X86.Popcnt;

namespace TestBlog.Tables
{
    public unsafe class Int64PermutationTableTests
    {
        const uint TABLE_SIZE = 16U;

        static IEnumerable<int[]> GenerateStableInt64PermTableValues_RH()
        {
            for (var mask = 0U; mask < TABLE_SIZE; mask++) {
                var data = new[] { -1, -1, -1, -1, -1, -1, -1, -1};
                var left = 0;
                var right = 0;

                var numRight = (int) PopCount(mask);
                var numLeft = 4 - numRight;
                var leftSegment = new  Span<int>(data, 0, numLeft * 2);
                var rightSegment = new  Span<int>(data, numLeft * 2, numRight * 2);

                for (var b = 0; b < 4; b++) {
                    if (((mask >> b) & 1) == 0) {
                        leftSegment[left++] = b * 2;
                        leftSegment[left++] = b * 2 + 1;
                    }
                    else {
                        rightSegment[right++] = b * 2;
                        rightSegment[right++] = b * 2 + 1;
                    }
                }

                for (var b = 0; b < 8; b++) {
                    Assert.That(data[b], Is.Not.Negative);
                }

                yield return data;
            }
        }

        [Test]
        //[Ignore("Run to generate")]
        public void GenerateIntArrayAsBytesSourceDrop()
        {
            Console.WriteLine("        internal static ReadOnlySpan<byte> IntPermTable => new byte[] {");
            var perms = GenerateStableInt64PermTableValues_RH().ToArray();
            for (var i = 0U; i < TABLE_SIZE; i++) {
                fixed (int* p = &perms[i][0]) {
                    var pi = (byte *) p;
                    Console.Write("            ");
                    for (var j = 0; j < 8 * sizeof(int); j++)
                        Console.Write($"{pi[j]}, ");
                    Console.WriteLine($"// 0b{Convert.ToString(i, 2).PadLeft(8, '0')} ({i})");
                }
            }
            Console.WriteLine("        };");
        }

        [Test]
        //[Ignore("Run to generate")]
        public void GeneratePackedIntArrayAsBytesSourceDrop()
        {
            Console.WriteLine("        internal static ReadOnlySpan<byte> PackedIntPermTable => new byte[] {");
            var perms = GenerateStableInt64PermTableValues_RH().ToArray();
            var tmp = stackalloc int[8];
            for (var i = 0U; i < TABLE_SIZE; i += 8) {
                var packedVector = Vector256<int>.Zero;

                for (var j = 0; j < 8; j++) {
                    fixed (int* p = &perms[i + j][0]) {
                        var permVec = LoadDquVector256(p);
                        packedVector = Or(packedVector, ShiftLeftLogical(permVec, (byte) (3 * j)));
                    }
                }


                Store(tmp, packedVector);
                var pi = (byte*) tmp;
                Console.Write("            ");
                for (var j = 0; j < 8 * sizeof(int); j++)
                    Console.Write($"0x{pi[j]:X2}, ");
                Console.WriteLine($"// ({i}-{i+7})");
            }
            Console.WriteLine("        };");
        }

        [Test]
        //[Ignore("Run to generate")]
        public void GenerateByteArrayAsBytesSourceDrop()
        {
            Console.WriteLine("        internal static ReadOnlySpan<byte> BytePermTable => new byte[] {");
            var perms = GenerateStableInt64PermTableValues_RH().ToArray();
            for (var i = 0U; i < TABLE_SIZE; i++) {
                fixed (int* p = &perms[i][0]) {
                    Console.Write("            ");
                    for (var j = 0; j < 8; j++)
                        Console.Write($"{p[j]}, ");
                    Console.WriteLine($"// 0b{Convert.ToString(i, 2).PadLeft(4, '0')} ({i})");
                }
            }
            Console.WriteLine("        };");
        }

        [Test]
        [Repeat(1000)]
        public void GeneratedPermutationsAreCorrect()
        {
            var perms = GenerateStableInt64PermTableValues_RH().ToArray();

            for (var i = 0U; i < TABLE_SIZE; i++) {
                var pivot = 666L;

                var r = new Random((int) DateTime.UtcNow.Ticks);

                var data = new long[4] {-1, -1, -1, -1};
                for (var j = 0; j < 4; j++) {
                    data[j] = (((i >> j) & 0x1) == 0) ? r.Next(0, 666) : r.Next(777, 1000);
                }

                // Check if I messed up and there's a -1 somewhere
                Assert.That(data, Is.All.Not.Negative);

                var permutedData = new long[4];

                fixed (int* perm = &perms[i][0])
                fixed (long* pSrc = &data[0])
                fixed (long* pDest = &permutedData[0]) {
                    var dataVector = LoadDquVector256(pSrc);
                    dataVector = PermuteVar8x32(dataVector.AsInt32(), LoadDquVector256(perm)).AsInt64();
                    Store(pDest, dataVector);
                }

                var numLeft = 4 - (int) PopCount(i);
                Assert.That(permutedData[0..numLeft], Is.All.LessThan(pivot));
                Assert.That(permutedData[numLeft..], Is.All.GreaterThan(pivot));
                Assert.That(data.Except(permutedData), Is.Empty);
            }
        }

        [Test]
        public void GeneratedPermutationsAreStable()
        {
            var perms = GenerateStableInt64PermTableValues_RH().ToArray();

            for (var mask = 0U; mask < TABLE_SIZE; mask++) {
                var pivot = 666L;

                var popCount = (int) PopCount(mask);
                var numRight = popCount;
                var numLeft = 4 - popCount;

                for (var numPivot = 0; numPivot < 4; numPivot++) {
                    var data = new long[] { -1, -1, -1, -1 };
                    var smallerThanData = Enumerable.Range(100, numLeft).ToArray();
                    var largerThanData = Enumerable.Range(777, numRight).ToArray();


                    for (int b = 0, si = 0, li = 0; b < 4; b++) {
                        data[b] = (((mask >> b) & 1) == 0) ? smallerThanData[si++] : largerThanData[li++];
                    }
                    var permutedData = new long[] {-1, -1, -1, -1};

                    fixed (int* perm = &perms[mask][0])
                    fixed (long* pSrc = &data[0])
                    fixed (long* pDest = &permutedData[0]) {
                        var dataVector = LoadDquVector256(pSrc);
                        dataVector =
                            PermuteVar8x32(dataVector.AsInt32(), LoadDquVector256(perm)).AsInt64();
                        Store(pDest, dataVector);
                    }

                    var msg = $"mask is {mask}/{Convert.ToString(mask, 2).PadLeft(8, '0')}|numPivot={numPivot}";
                    Assert.That(permutedData[0..numLeft], Is.All.LessThan(pivot), msg);
                    Assert.That(permutedData[0..numLeft], Is.Ordered, msg);
                    Assert.That(permutedData[^numRight..], Is.All.GreaterThan(pivot), msg);
                    Assert.That(permutedData[^numRight..], Is.Ordered, msg);
                    Assert.That(data.Except(permutedData), Is.Empty, msg);
                }
            }
        }
        //
        // [Test]
        // public void CompiledIntPermTablePtrIsGood()
        // {
        //     var perms = GenerateStableInt64PermTableValues_RH().ToArray();
        //     for (var i = 0U; i < 256U; i++) {
        //         fixed (int* p = &perms[i][0]) {
        //             var truth = LoadDquVector256(p);
        //             var test = Int32PermTables.GetIntPermutation(Int32PermTables.IntPermTablePtr, i);
        //             Assert.That(test, Is.EqualTo(truth));
        //         }
        //     }
        // }
        //
        //
        // [Test]
        // public void CompiledIntPermTableAlignedPtrIsGood()
        // {
        //     var perms = GenerateStableInt64PermTableValues_RH().ToArray();
        //     for (var i = 0U; i < 256U; i++) {
        //         fixed (int* p = &perms[i][0]) {
        //             var truth = LoadDquVector256(p);
        //             var test = Int32PermTables.GetIntPermutationAligned(Int32PermTables.IntPermTableAlignedPtr, i);
        //             Assert.That(test, Is.EqualTo(truth));
        //         }
        //     }
        // }

        [Test]
        public void CompiledBytePermTablePtrIsGood()
        {
            var perms = GenerateStableInt64PermTableValues_RH().ToArray();
            for (var i = 0U; i < TABLE_SIZE; i++) {
                fixed (int* p = &perms[i][0]) {
                    var truth = LoadDquVector256(p);
                    var test = And(BytePermTables.GetBytePermutation(BytePermTables.BytePermTablePtr, i), Vector256.Create(0x7));
                    Assert.That(test, Is.EqualTo(truth));
                }
            }
        }

        [Test]
        public void CompiledBytePermTableAlignedPtrIsGood()
        {
            var perms = GenerateStableInt64PermTableValues_RH().ToArray();
            for (var i = 0U; i < TABLE_SIZE; i++) {
                fixed (int* p = &perms[i][0]) {
                    var truth = LoadDquVector256(p);
                    var test = And(BytePermTables.GetBytePermutationAligned(BytePermTables.BytePermTableAlignedPtr, i), Vector256.Create(0x7));
                    Assert.That(test, Is.EqualTo(truth));
                }
            }
        }
    }
}
