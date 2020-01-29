using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using NUnit.Framework;
using VxSort;
using static System.Runtime.Intrinsics.X86.Avx;
using static System.Runtime.Intrinsics.X86.Avx2;
using static VxSort.BytePermutationTables;

namespace Test
{
    public class PermutationTableTests
    {
        static IEnumerable<int[]> GenerateStableIntPermTableValues()
        {
            for (var mask = 0U; mask < 256U; mask++) {
                var data = new int[] { -1, -1, -1, -1, -1, -1, -1, -1};
                var left = 0;
                var right = 0;

                var numRight = (int) Popcnt.PopCount(mask);
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

        [Test]
        [Repeat(1000)]
        public unsafe void GeneratedPermutationsAreCorrect()
        {
            var perms = GenerateStableIntPermTableValues().ToArray();

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

                var numLeft = 8 - (int) Popcnt.PopCount(i);
                Assert.That(permutedData[0..numLeft], Is.All.LessThan(pivot));
                Assert.That(permutedData[numLeft..], Is.All.GreaterThan(pivot));
                Assert.That(data.Except(permutedData), Is.Empty);
            }
        }

        [Test]
        public unsafe void GeneratedPermutationsAreStable()
        {
            var perms = GenerateStableIntPermTableValues().ToArray();
            
            for (var mask = 0U; mask < 256U; mask++) {
                var pivot = 666;

                var popCount = (int) Popcnt.PopCount(mask);
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
        public unsafe void CompiledBytePermTableAlignedPtrIsGood()
        {
            var perms = GenerateStableIntPermTableValues().ToArray();
            for (var i = 0U; i < 256U; i++) {
                fixed (int* p = &perms[i][0]) {
                    var truth = LoadDquVector256(p);
                    var test =
                        And(GetBytePermutationAligned(BytePermTableAlignedPtr, i), Vector256.Create(0x7));

                    Assert.That(truth, Is.EqualTo(test));
                }
            }
        }
    }
}
