using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Intrinsics;
using NUnit.Framework;
using VxSortResearch;
using VxSortResearch.PermutationTables;
using static System.Runtime.Intrinsics.Vector128;
using static System.Runtime.Intrinsics.X86.Avx;
using static System.Runtime.Intrinsics.X86.Avx2;
using static System.Runtime.Intrinsics.X86.Bmi2.X64;
using static System.Runtime.Intrinsics.X86.Popcnt;
using static VxSortResearch.PermutationTables.BitPermTables;
using static VxSortResearch.Unstable.AVX2.Sad.DoublePumpOverlinedUnroll8YoDawgBitonicSort.VxSortInt32;

namespace TestBlog
{
    public unsafe class PermutationTableTests
    {
        static IEnumerable<int[]> GenerateStableIntPermTableValues_RH()
        {
            for (var mask = 0U; mask < 256U; mask++) {
                var data = new[] { -1, -1, -1, -1, -1, -1, -1, -1};
                var left = 0;
                var right = 0;

                var numRight = (int) PopCount(mask);
                var numLeft = 8 - numRight;
                var leftSegment = new  Span<int>(data, 0, numLeft);
                var rightSegment = new  Span<int>(data, numLeft, numRight);


                for (var b = 0; b < 8; b++) {
                    if (((mask >> b) & 1) == 0)
                        leftSegment[left++] = b;
                    else {
                        rightSegment[right++] = b;
                    }
                }

                for (var b = 0; b < 8; b++) {
                    Assert.That(data[b], Is.Not.Negative);
                }

                yield return data;
            }
        }

        static IEnumerable<int[]> GenerateStableIntPermTableValues_LH()
        {
            var perms = GenerateStableIntPermTableValues_RH().ToArray();

            var newPerms = new int[256][];

            for (var i = 0; i < 256; i++) {
                newPerms[(~i & 0xFF)] = perms[i];
            }

            for (var i = 0; i < 256; i++) {
                Assert.That(newPerms[i], Is.Not.Null);
            }

            return newPerms;
        }

        [Test]
        [Ignore("Run to generate")]
        public void GenerateIntArrayAsBytesSourceDrop()
        {
            Console.WriteLine("        internal static ReadOnlySpan<byte> IntPermTable => new byte[] {");
            var perms = GenerateStableIntPermTableValues_RH().ToArray();
            for (var i = 0U; i < 256U; i++) {
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
        public void GenerateIntArrayAsBytesSourceDrop_LH()
        {
            Console.WriteLine("        internal static ReadOnlySpan<byte> IntPermTableLeftHand => new byte[] {");
            var perms = GenerateStableIntPermTableValues_LH().ToArray();
            for (var i = 0U; i < 256U; i++) {
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
            var perms = GenerateStableIntPermTableValues_RH().ToArray();
            var tmp = stackalloc int[8];
            for (var i = 0U; i < 256U; i += 8) {
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
        [Ignore("Run to generate")]
        public void GenerateIntArrayWithPopCountsAsBytesSourceDrop()
        {
            Console.WriteLine("        internal static ReadOnlySpan<byte> IntPermTableWithPopCountSpread => new byte[] {");
            var perms = GenerateStableIntPermTableValues_RH().ToArray();
            for (var i = 0U; i < 256U; i++) {
                fixed (int* ip = &perms[i][0]) {
                    var p = (uint*) ip;

                    // Get the left pop count for i
                    var leftPopCount = 8 - PopCount(i);
                    // pre-left shift it by 2 (*4 / sizeof(int))
                    leftPopCount *= sizeof(int);

                    for (var j = 0; j < 8; j++) {
                        var popCountBit = (leftPopCount >> j) & 0x1;
                        p[j] |= popCountBit << 31;
                    }

                    var bp = (byte *) p;
                    Console.Write("            ");
                    for (var j = 0; j < 8 * sizeof(int); j++)
                        Console.Write($"0x{bp[j]:X2}, ");
                    Console.WriteLine($"// 0b{Convert.ToString(i, 2).PadLeft(8, '0')} ({i})|Left-PC:{leftPopCount/sizeof(int)}");
                }
            }
            Console.WriteLine("        };");
        }


        [Test]
        //[Ignore("Run to generate")]
        public void GenerateByteArrayAsBytesSourceDrop()
        {
            Console.WriteLine("        internal static ReadOnlySpan<byte> BytePermTable => new byte[] {");
            var perms = GenerateStableIntPermTableValues_RH().ToArray();
            for (var i = 0U; i < 256U; i++) {
                fixed (int* p = &perms[i][0]) {
                    Console.Write("            ");
                    for (var j = 0; j < 8; j++)
                        Console.Write($"{p[j]}, ");
                    Console.WriteLine($"// 0b{Convert.ToString(i, 2).PadLeft(8, '0')} ({i})");
                }
            }
            Console.WriteLine("        };");
        }
        
        [Test]
        [Ignore("Run to generate")]
        public void GenerateByteArrayWithLeftPopCountAsBytesSourceDrop()
        {
            Console.WriteLine("        internal static ReadOnlySpan<byte> BytePermTableWithLeftPopCount => new byte[] {");
            var perms = GenerateStableIntPermTableValues_RH().ToArray();
            for (var i = 0U; i < 256U; i++) {
                fixed (int* p = &perms[i][0]) {
                    var up = (uint*) p;
                    var leftPopCount = 8 - PopCount(i);
                    Console.Write("            ");
                    // We shove it into the second element on purpose so we
                    // can extract if more efficiently
                    up[1] = up[1] | (leftPopCount << 4);
                    for (var j = 0; j < 8; j++)
                        Console.Write($"0x{up[j]:X2}, ");
                    Console.WriteLine($"// 0b{Convert.ToString(i, 2).PadLeft(8, '0')} ({i})|Left-PC: {leftPopCount}");
                }
            }
            Console.WriteLine("        };");
        }
        
        [Test]
        //[Ignore("Run to generate")]
        public void GenerateByteArrayWithMinusRightPopCountAsBytesSourceDrop()
        {
            Console.WriteLine("        internal static ReadOnlySpan<byte> BytePermTableWithMinusRightPopCount => new byte[] {");
            var perms = GenerateStableIntPermTableValues_RH().ToArray();
            for (var i = 0U; i < 256U; i++) {
                fixed (int* p = &perms[i][0]) {
                    var up = (uint*) p;
                    var minusRightPopCount = -PopCount(i);
                    Console.Write("            ");
                    // We shove it into the second element on purpose so we
                    // can extract if more efficiently
                    up[1] = up[1] | (uint) ((minusRightPopCount << 4) & 0xFF);
                    for (var j = 0; j < 8; j++)
                        Console.Write($"0x{up[j]:X2}, ");
                    Console.WriteLine($"// 0b{Convert.ToString(i, 2).PadLeft(8, '0')} ({i})|Right-PC: {-minusRightPopCount}");
                }
            }
            Console.WriteLine("        };");
        }
        

        [Test]
        [Ignore("Run to generate")]
        public void Generate3BitCompressedPermutationTableSourceDrop()
        {
            Console.WriteLine("        internal static ReadOnlySpan<byte> BitPermTable => new byte[] {");

            var perms = GenerateStableIntPermTableValues_RH().ToArray();
            for (var i = 0U; i < 256U; i++) {
                var bytePerm = perms[i].Select(e => (byte) e).ToArray();
                ulong pul;
                fixed (byte* p = &bytePerm[0])
                    pul = *((ulong *) p);

                var compressed = ParallelBitExtract(pul,
                    0b00000111_00000111_00000111_00000111_00000111_00000111_00000111_00000111);
                Console.Write("            ");
                foreach (var b in BitConverter.GetBytes((uint) compressed))
                    Console.Write($"0b{Convert.ToString((int) b, 2).PadLeft(8, '0')}, ");
                Console.WriteLine($"// 0b{Convert.ToString(i, 2).PadLeft(8, '0')} ({i})");
                var orig = ParallelBitDeposit(compressed,
                    0b00000111_00000111_00000111_00000111_00000111_00000111_00000111_00000111);
            }
            Console.WriteLine("        };");
        }

        [Test]
        [Ignore("Run to generate")]
        public void Generate3BitCompressedPermutationTableWithPopCountsSourceDrop()
        {
            Console.WriteLine("        internal static ReadOnlySpan<byte> BitWithPopCountPermTable => new byte[] {");

            var perms = GenerateStableIntPermTableValues_RH().ToArray();
            for (var i = 0U; i < 256U; i++) {
                var bytePerm = perms[i].Select(e => (byte) e).ToArray();
                ulong pul;
                fixed (byte* p = &bytePerm[0]) pul = *((ulong *) p);

                var compressed = ParallelBitExtract(pul,
                    0b00000111_00000111_00000111_00000111_00000111_00000111_00000111_00000111);

                var popCount = PopCount(i);

                // Add the popCount value:
                // Left shift by 24, since the first 24 bits are occupied by the mess above
                // Then left shift by to more bit, since, we use the popCounts to advance
                // int pointers anyway, so we leave 2 more 0 bits in there, so we only need to left shift by 24...
                compressed |= popCount << 26;

                Console.Write("            ");
                foreach (var b in BitConverter.GetBytes((uint) compressed))
                    Console.Write($"0b{Convert.ToString((int) b, 2).PadLeft(8, '0')}, ");
                Console.WriteLine($"// 0b{Convert.ToString(i, 2).PadLeft(8, '0')} ({i})");
                var orig = ParallelBitDeposit(compressed,
                    0b00000111_00000111_00000111_00000111_00000111_00000111_00000111_00000111);

                Assert.That(pul,Is.EqualTo(orig));
            }
            Console.WriteLine("        };");
        }

        [Test]
        [Ignore("Run to generate")]
        public void Generate4BitInterleavedCompressedPermutationTableWithInterleavedPopCountsSourceDrop()
        {
            Console.WriteLine("        internal static ReadOnlySpan<byte> BitWithInterleavedPopCountPermTable => new byte[] {");

            var perms = GenerateStableIntPermTableValues_RH().ToArray();
            for (var i = 0U; i < 256U; i++) {
                var b = perms[i].Select(e => (byte) e).ToArray();

                // We spread the permutation values in a very convoluted way,
                // because we can "optimize" the placement of each element
                // inside a single uint so that we will end up using less instructions
                // to transpose those values, each element takes up 4 bits of space, but only uses
                // 3 bits of "content". The elected layout is:
                // MSB                               LSB
                //         24    16     8
                // 31|       |       |       |       |0
                //   | a₇ a₃ | a₆ a₂ | a₅ a₁ | a₄ a₀ |
                var permValue =
                    (uint) b[0] << 00 | (uint) b[4] << 04 | (uint) b[1] << 08 | (uint) b[5] << 12 |
                    (uint) b[2] << 16 | (uint) b[6] << 20 | (uint) b[3] << 24 | (uint) b[7] << 28;

                var expectedPermMask =
                    1U << 03 | 1U << 07 | 1U << 11 | 1U << 15 |
                    1U << 19 | 1U << 23 | 1U << 27 | 1U << 31;
                Assert.That(permValue & expectedPermMask, Is.Zero);
                Assert.That(permValue & ~expectedPermMask, Is.EqualTo(permValue));

                // Popcount is really a 4 bit value [0-8] (inclusive)
                var popCount = 8U - PopCount(i);
                var p0 = (popCount >> 0) & 1;
                var p1 = (popCount >> 1) & 1;
                var p2 = (popCount >> 2) & 1;
                var p3 = (popCount >> 3) & 1;

                // Spread out the popCount bits in very specific positions:
                // 7, 15, 23, 31
                // These are position we can later extract with one opcode (vpmovmskb)
                var popCountSpread = p0 << 7 | p1 << 15 | p2 << 23 | p3 << 31;
                Assert.That(popCountSpread & expectedPermMask, Is.EqualTo(popCountSpread));

                // Merge:
                var compressed = permValue | popCountSpread;

                Console.Write("            ");
                foreach (var x in BitConverter.GetBytes((uint) compressed))
                    Console.Write($"0x{Convert.ToString((int) x, 16).PadLeft(2, '0')}, ");
                Console.WriteLine($"// 0b{Convert.ToString(i, 2).PadLeft(8, '0')} ({i})|Left-PC:{popCount}");
            }
            Console.WriteLine("        };");
        }

        [Test]
        [Ignore("Run to generate")]
        public void GenerateLeftOffsetBaseArrayAsBytesSourceDrop()
        {
            var data = new uint[] {8 << 2, 16 << 2, 24 << 2, 32 << 2, 40 << 2, 48 << 2, 56 << 2, 64 << 2};
            fixed (uint* p = &data[0]) {
                var pi = (byte *) p;
                for (var j = 0; j < 8 * sizeof(uint); j++)
                    Console.Write($"{pi[j]}, ");
                Console.WriteLine();
            }
        }

        [Test]
        [Repeat(1000)]
        public void GeneratedPermutationsAreCorrect()
        {
            var perms = GenerateStableIntPermTableValues_RH().ToArray();

            for (var i = 0U; i < 256U; i++) {
                var pivot = 666;

                var r = new Random((int) DateTime.UtcNow.Ticks);

                var data = new int[8] {-1, -1, -1, -1, -1, -1, -1, -1};
                for (var j = 0; j < 8; j++) {
                    data[j] = (((i >> j) & 0x1) == 0) ? r.Next(0, 666) : r.Next(777, 1000);
                }

                // Check if I messed up and there's a -1 somewhere
                Assert.That(data, Is.All.Not.Negative);

                var permutedData = new int[8];

                fixed (int* perm = &perms[i][0])
                fixed (int* pSrc = &data[0])
                fixed (int* pDest = &permutedData[0]) {
                    var dataVector = LoadDquVector256(pSrc);
                    dataVector = PermuteVar8x32(dataVector, LoadDquVector256(perm));
                    Store(pDest, dataVector);
                }

                var numLeft = 8 - (int) PopCount(i);
                Assert.That(permutedData[0..numLeft], Is.All.LessThan(pivot));
                Assert.That(permutedData[numLeft..], Is.All.GreaterThan(pivot));
                Assert.That(data.Except(permutedData), Is.Empty);
            }
        }

        [Test]
        public void GeneratedPermutationsAreStable()
        {
            var perms = GenerateStableIntPermTableValues_RH().ToArray();

            for (var mask = 0U; mask < 256U; mask++) {
                var pivot = 666;

                var popCount = (int) PopCount(mask);
                var numRight = popCount;
                var numLeft = 8 - popCount;

                for (var numPivot = 0; numPivot < 4; numPivot++) {
                    var data = new int[] {-1, -1, -1, -1, -1, -1, -1, -1};
                    var smallerThanData = Enumerable.Range(100, numLeft).ToArray();
                    var largerThanData = Enumerable.Range(777, numRight).ToArray();


                    for (int b = 0, si = 0, li = 0; b < 8; b++) {
                        data[b] = (((mask >> b) & 1) == 0) ? smallerThanData[si++] : largerThanData[li++];
                    }
                    var permutedData = new int[] {-1, -1, -1, -1, -1, -1, -1, -1};

                    fixed (int* perm = &perms[mask][0])
                    fixed (int* pSrc = &data[0])
                    fixed (int* pDest = &permutedData[0]) {
                        var dataVector = LoadDquVector256(pSrc);
                        dataVector =
                            PermuteVar8x32(dataVector, LoadDquVector256(perm));
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

        [Test]
        public void CompiledIntPermTablePtrIsGood()
        {
            var perms = GenerateStableIntPermTableValues_RH().ToArray();
            for (var i = 0U; i < 256U; i++) {
                fixed (int* p = &perms[i][0]) {
                    var truth = LoadDquVector256(p);
                    var test = Int32PermTables.GetIntPermutation(Int32PermTables.IntPermTablePtr, i);
                    Assert.That(test, Is.EqualTo(truth));
                }
            }
        }


        [Test]
        public void CompiledIntPermTableLeftHandPtrIsGood()
        {
            var perms = GenerateStableIntPermTableValues_LH().ToArray();
            for (var i = 0U; i < 256U; i++) {
                fixed (int* p = &perms[i][0]) {
                    var truth = LoadDquVector256(p);
                    var test = Int32PermTables.GetIntPermutation(Int32PermTables.IntPermTableLeftHandPtr, i);
                    Assert.That(test, Is.EqualTo(truth));
                }
            }
        }

        [Test]
        public void IntPermTablesComplementEachOther()
        {

            var r = new Random((int) DateTime.UtcNow.Ticks);

            var data = stackalloc int[8];

            for (var i = 0; i < 1000000; i++) {
                r.NextBytes(new Span<byte>(data, 8 * sizeof(int)));

                var randomIndex = data[0] % 8;

                var pivot = data[randomIndex];
                var P = Vector256.Create(pivot);

                var dataVec = LoadDquVector256(data);

                var maskRightHand = (uint) MoveMask(CompareGreaterThan(dataVec, P).AsSingle());
                var maskLeftHand = (uint)  MoveMask(CompareGreaterThan(P, dataVec).AsSingle());

                var permRight = Int32PermTables.GetIntPermutation(Int32PermTables.IntPermTablePtr, maskRightHand);
                var permLeft = Int32PermTables.GetIntPermutation(Int32PermTables.IntPermTableLeftHandPtr, maskLeftHand);

                Assert.That(permLeft, Is.EqualTo(permRight));
            }
        }
        [Test]
        public void CompiledIntPermTableAlignedPtrIsGood()
        {
            var perms = GenerateStableIntPermTableValues_RH().ToArray();
            for (var i = 0U; i < 256U; i++) {
                fixed (int* p = &perms[i][0]) {
                    var truth = LoadDquVector256(p);
                    var test = Int32PermTables.GetIntPermutationAligned(Int32PermTables.IntPermTableAlignedPtr, i);
                    Assert.That(test, Is.EqualTo(truth));
                }
            }
        }

        [Test]
        public void CompiledBytePermTablePtrIsGood()
        {
            var perms = GenerateStableIntPermTableValues_RH().ToArray();
            for (var i = 0U; i < 256U; i++) {
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
            var perms = GenerateStableIntPermTableValues_RH().ToArray();
            for (var i = 0U; i < 256U; i++) {
                fixed (int* p = &perms[i][0]) {
                    var truth = LoadDquVector256(p);
                    var test = And(BytePermTables.GetBytePermutation(BytePermTables.BytePermTableAlignedPtr, i), Vector256.Create(0x7));
                    Assert.That(test, Is.EqualTo(truth));
                }
            }
        }
        
        [Test]
        public void CompiledBytePermTableWithLeftPopCountAlignedPtrIsGood()
        {
            var perms = GenerateStableIntPermTableValues_RH().ToArray();
            for (var i = 0U; i < 256U; i++) {
                fixed (int* p = &perms[i][0]) {
                    var truth = LoadDquVector256(p);
                    var perm = BytePermTables.GetBytePermutationAligned(BytePermTables.BytePermTableWithLeftPopCountAlignedPtr, i);
                    var test = And(perm, Vector256.Create(0x7));
                    Assert.That(test, Is.EqualTo(truth));
                    var leftPopCount = perm.AsUInt64().GetElement(0) >> 36;
                    var expectedLeftPopCount = 8 - PopCount(i);
                    Assert.That(leftPopCount, Is.EqualTo(expectedLeftPopCount));
                }
            }
        }
        
        [Test]
        public void CompiledBytePermTableWithMinusRightPopCountAlignedPtrIsGood()
        {
            var perms = GenerateStableIntPermTableValues_RH().ToArray();
            for (var i = 0U; i < 256U; i++) {
                fixed (int* p = &perms[i][0]) {
                    var truth = LoadDquVector256(p);
                    var perm = BytePermTables.GetSignedBytePermutationAligned(BytePermTables.BytePermTableWithMinusRightPopCountAlignedPtr, i);
                    var test = And(perm, Vector256.Create(0x7));
                    Assert.That(test, Is.EqualTo(truth));
                    var leftPopCount = perm.AsInt64().GetElement(0) >> 36;
                    var expectedLeftPopCount = -PopCount(i);
                    Assert.That(leftPopCount, Is.EqualTo(expectedLeftPopCount));
                }
            }
        }        
        

        [Test]
        public void CompiledIntPermTableWithSpreadPopCountPtrIsGood()
        {
            var perms = GenerateStableIntPermTableValues_RH().ToArray();
            for (var i = 0U; i < 256U; i++) {
                fixed (int* p = &perms[i][0]) {
                    var truth = LoadDquVector256(p);
                    var perm = Int32PermTables.GetIntPermutation(Int32PermTables.IntPermTableWithPopCountSpreadAlignedPtr, i);
                    var test = And(perm, Vector256.Create(0x7));
                    Assert.That(truth, Is.EqualTo(test));

                    var leftPopCount = MoveMask(perm.AsSingle());
                    leftPopCount >>= 2;
                    var expectedPopCount = 8 - PopCount(i);
                    Assert.That(leftPopCount, Is.EqualTo(expectedPopCount));

                }
            }
        }

        [Test]
        public void CompiledBitPermTablePtrIsGood()
        {
            var perms = GenerateStableIntPermTableValues_RH().ToArray();
            for (var i = 0U; i < 256U; i++) {
                fixed (int* p = &perms[i][0]) {
                    var truth = LoadDquVector256(p);
                    var test = GetBitPermutation(BitPermTablePtr, i);
                    Assert.That(truth, Is.EqualTo(test));
                }
            }
        }

        [Test]
        public void Transpose1Works()
        {
            var dataPtr = stackalloc uint[] {
                0x73625140,
                0x00000000,
                0x00000000,
                0x00000000,
                0x00000000,
                0x00000000,
                0x00000000,
                0x00000000,
            };

            var data = LoadDquVector256(dataPtr);
            var shuffler = LoadDquVector256(TransposeShufflerPtr);
            var mask = Vector256.Create(0x7);
            var p0_3 = Vector256<uint>.Zero;
            var p4_7= Vector256<uint>.Zero;
            Transpose(shuffler, ref p0_3, ref p4_7, data);
            Assert.That(p0_3.GetNibblesAsArray(0), Is.EqualTo(new[] {0, 1, 2, 3, 4, 5, 6, 7}));
        }

        [Test]
        public void Transpose8Works()
        {
            var dataPtr = stackalloc uint[] {
                0x73625140,
                0x04736251,
                0x15047362,
                0x26150473,
                0x37261504,
                0x40372615,
                0x51403726,
                0x62514037,
            };

            var data = LoadDquVector256(dataPtr);
            var shuffler = LoadDquVector256(TransposeShufflerPtr);
            var p0_3 = Vector256<uint>.Zero;
            var p4_7= Vector256<uint>.Zero;
            Transpose(shuffler, ref p0_3, ref p4_7, data);
            Assert.That(p0_3.GetNibblesAsArray(0), Is.EqualTo(new[] {0, 1, 2, 3, 4, 5, 6, 7}));
            Assert.That(p0_3.GetNibblesAsArray(2), Is.EqualTo(new[] {1, 2, 3, 4, 5, 6, 7, 0}));
            Assert.That(p0_3.GetNibblesAsArray(4), Is.EqualTo(new[] {2, 3, 4, 5, 6, 7, 0, 1}));
            Assert.That(p0_3.GetNibblesAsArray(6), Is.EqualTo(new[] {3, 4, 5, 6, 7, 0, 1, 2}));
            Assert.That(p4_7.GetNibblesAsArray(0), Is.EqualTo(new[] {4, 5, 6, 7, 0, 1, 2, 3}));
            Assert.That(p4_7.GetNibblesAsArray(2), Is.EqualTo(new[] {5, 6, 7, 0, 1, 2, 3, 4}));
            Assert.That(p4_7.GetNibblesAsArray(4), Is.EqualTo(new[] {6, 7, 0, 1, 2, 3, 4, 5}));
            Assert.That(p4_7.GetNibblesAsArray(6), Is.EqualTo(new[] {7, 0, 1, 2, 3, 4, 5, 6}));
        }

        [Test]
        public void CompiledBitWithInterleavedPopCountPermTablePtrIsGood()
        {
            var perms = GenerateStableIntPermTableValues_RH().ToArray();
            var shuffler = LoadDquVector256(TransposeShufflerPtr);

            // We read everything in groups of 8 in this test
            for (var i = 0U; i < 256U - 8; i++) {
                var permutations = LoadDquVector256(BitWithInterleavedPopCountPermTablePtr + i);

                var p0_3 = Vector256<uint>.Zero;
                var p4_7= Vector256<uint>.Zero;
                Transpose(shuffler, ref p0_3, ref p4_7, permutations);

                Assert.That(p0_3.GetNibblesAsArray(0, 0x7), Is.EqualTo(perms[i + 0]));
                Assert.That(p0_3.GetNibblesAsArray(2, 0x7), Is.EqualTo(perms[i + 1]));
                Assert.That(p0_3.GetNibblesAsArray(4, 0x7), Is.EqualTo(perms[i + 2]));
                Assert.That(p0_3.GetNibblesAsArray(6, 0x7), Is.EqualTo(perms[i + 3]));
                Assert.That(p4_7.GetNibblesAsArray(0, 0x7), Is.EqualTo(perms[i + 4]));
                Assert.That(p4_7.GetNibblesAsArray(2, 0x7), Is.EqualTo(perms[i + 5]));
                Assert.That(p4_7.GetNibblesAsArray(4, 0x7), Is.EqualTo(perms[i + 6]));
                Assert.That(p4_7.GetNibblesAsArray(6, 0x7), Is.EqualTo(perms[i + 7]));

                var extractedPackedPopCounts = (uint) MoveMask(permutations.AsSByte());
                var popCounts =
                    ConvertToVector256Int32(
                        CreateScalarUnsafe(
                            ParallelBitDeposit(extractedPackedPopCounts, 0x0F_0F_0F_0F_0F_0F_0F_0F)).AsByte()).AsUInt32();
                var expectedPopCounts = new[] {
                    8 - (int) PopCount(i + 0),
                    8 - (int) PopCount(i + 1),
                    8 - (int) PopCount(i + 2),
                    8 - (int) PopCount(i + 3),
                    8 - (int) PopCount(i + 4),
                    8 - (int) PopCount(i + 5),
                    8 - (int) PopCount(i + 6),
                    8 - (int) PopCount(i + 7),
                };

                Assert.That(popCounts.GetNibblesAsArray(0), Is.EqualTo(expectedPopCounts));
            }
        }

        [Test]
        public void CompiledBitWithInterleavedPopCountPermTablePtrIsGoodRandomized()
        {
            var perms = GenerateStableIntPermTableValues_RH().ToArray();
            var shuffler = LoadDquVector256(TransposeShufflerPtr);
            var pBase = BitWithInterleavedPopCountPermTablePtr;

            var r = new Random(666);

            for (var round = 0; round < 100000; round++) {
                var indicesAsLong = 0UL;
                var indices = new Span<byte>(&indicesAsLong, 8);
                r.NextBytes(indices);
                var indicesAsLongCopy = indicesAsLong;

                // Load 8 permutation vectors+precalculated pop-counts at once
                var permutations = GatherVector256(
                    pBase,
                    ConvertToVector256Int32(CreateScalarUnsafe(indicesAsLong).AsByte()),
                    sizeof(int));

                var p0_3 = Vector256<uint>.Zero;
                var p4_7= Vector256<uint>.Zero;
                Transpose(shuffler, ref p0_3, ref p4_7, permutations);

                Assert.That(p0_3.GetNibblesAsArray(0, 0x7), Is.EqualTo(perms[indices[0]]));
                Assert.That(p0_3.GetNibblesAsArray(2, 0x7), Is.EqualTo(perms[indices[1]]));
                Assert.That(p0_3.GetNibblesAsArray(4, 0x7), Is.EqualTo(perms[indices[2]]));
                Assert.That(p0_3.GetNibblesAsArray(6, 0x7), Is.EqualTo(perms[indices[3]]));
                Assert.That(p4_7.GetNibblesAsArray(0, 0x7), Is.EqualTo(perms[indices[4]]));
                Assert.That(p4_7.GetNibblesAsArray(2, 0x7), Is.EqualTo(perms[indices[5]]));
                Assert.That(p4_7.GetNibblesAsArray(4, 0x7), Is.EqualTo(perms[indices[6]]));
                Assert.That(p4_7.GetNibblesAsArray(6, 0x7), Is.EqualTo(perms[indices[7]]));

                var extractedPackedPopCounts = (uint) MoveMask(permutations.AsSByte());
                var popCounts =
                    ConvertToVector256Int32(
                        CreateScalarUnsafe(
                            ParallelBitDeposit(extractedPackedPopCounts, 0x0F_0F_0F_0F_0F_0F_0F_0F)).AsByte()).AsUInt32();

                Assert.That(popCounts.GetNibblesAsArray(0), Is.EqualTo(
                    new[] {
                        8 - (int) PopCount(indices[0]),
                        8 - (int) PopCount(indices[1]),
                        8 - (int) PopCount(indices[2]),
                        8 - (int) PopCount(indices[3]),
                        8 - (int) PopCount(indices[4]),
                        8 - (int) PopCount(indices[5]),
                        8 - (int) PopCount(indices[6]),
                        8 - (int) PopCount(indices[7]),
                    }
                    ), () => $"Failed for input indices: [0x{indicesAsLongCopy:X16}]");
            }
        }



        [Test]
        public void TestMultipleSameBirthdayProblem()
        {
            var randomData = stackalloc byte[8];
            var randomDataSpan = new Span<byte>(randomData, 8);
            var cachelineHits = stackalloc int[16];
            var cacheLineSpan = new Span<int>(cachelineHits, 16);
            var r = new Random((int) DateTime.UtcNow.Ticks);

            long totalCachelines = 0;

            const int ITERATIONS = 10_000_000;
            for (var i = 0; i < ITERATIONS; i++) {
                r.NextBytes(randomDataSpan);
                cacheLineSpan.Clear();
                for (var b = 0; b < 8; b++) {
                    Assert.That(randomDataSpan[b] / 16, Is.LessThan(16));
                    cacheLineSpan[randomDataSpan[b] / 16] = 1;
                }
                for (var c = 0; c < 16; c++)
                    totalCachelines += cachelineHits[c];
            }

            Console.WriteLine($"Avg Cachelines read: {(double) totalCachelines / ITERATIONS}");
        }
    }

    internal static unsafe class Vector256TestExtensions
    {
        internal static int[] GetNibblesAsArray(this Vector256<uint> data, int whichNibble, int mask = 0xF)
        {
            var next8 = new int[8];
            fixed (int* p = next8) {
                Store(p, And(ShiftRightLogical(data, (byte) (whichNibble * 4)).AsInt32(), Vector256.Create(mask)));
            }
            return next8;
        }
    }
}
