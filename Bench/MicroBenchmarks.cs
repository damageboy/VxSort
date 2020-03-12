using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Reports;
using Perfolizer.Horology;
using VxSortResearch;
using VxSortResearch.PermutationTables;
using static System.Runtime.Intrinsics.X86.Avx;
using static System.Runtime.Intrinsics.X86.Avx2;
using static System.Runtime.Intrinsics.Vector128;
using static VxSortResearch.Unstable.AVX2.Sad.DoublePumpOverlinedUnroll8YoDawgBitonicSort.VxSortInt32;


namespace Bench
{
    class MicroBenchmarksConfig : ManualConfig
    {
        public MicroBenchmarksConfig()
        {
            SummaryStyle = new SummaryStyle(CultureInfo.InvariantCulture, true, SizeUnit.GB, TimeUnit.Nanosecond);
            AddJob(Job.LongRun); //);.With(InProcessEmitToolchain.Instance));
        }
    }

    [InvocationCount(InvocationsPerIterationValue)]
    [Config(typeof(MicroBenchmarksConfig))]
    [DisassemblyDiagnoser]
    public class MicroBenchmarks
    {
        ulong[] _indices;
        Random _random;
        GCHandle _gch;
        unsafe ulong* _indicesPtr;
        int _iterationIndex;
        const int InvocationsPerIterationValue = 4096;
        const int OperationsPerInvoke = 8;

        [GlobalSetup]
        public unsafe void Setup()
        {
            _random = new Random((int) DateTime.UtcNow.Ticks);
            _indices = new ulong[InvocationsPerIterationValue*OperationsPerInvoke];
            _gch = GCHandle.Alloc(_indices, GCHandleType.Pinned);
            _indicesPtr = (ulong*) _gch.AddrOfPinnedObject().ToPointer();
            _iterationIndex = 0;
        }

        [IterationCleanup]
        public void CleanupIteration() => _iterationIndex = 0; // after every iteration end we set the index to 0

        [IterationSetup]
        public unsafe void SetupArrayIteration() =>
            _random.NextBytes(new Span<byte>(_indicesPtr,
                sizeof(ulong) * InvocationsPerIterationValue * OperationsPerInvoke));

        [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public unsafe void Gather18PackedPermutationSameCacheline()
        {
            var p = BitPermTables.BitWithInterleavedPopCountPermTablePtr;
            // Load 8 permutation vectors+precalculated pop-counts at once
            var indices = 0;
            GatherVector256(p, ConvertToVector256Int32(CreateScalarUnsafe(indices).AsByte()), sizeof(int));
            GatherVector256(p, ConvertToVector256Int32(CreateScalarUnsafe(indices).AsByte()), sizeof(int));
            GatherVector256(p, ConvertToVector256Int32(CreateScalarUnsafe(indices).AsByte()), sizeof(int));
            GatherVector256(p, ConvertToVector256Int32(CreateScalarUnsafe(indices).AsByte()), sizeof(int));
            GatherVector256(p, ConvertToVector256Int32(CreateScalarUnsafe(indices).AsByte()), sizeof(int));
            GatherVector256(p, ConvertToVector256Int32(CreateScalarUnsafe(indices).AsByte()), sizeof(int));
            GatherVector256(p, ConvertToVector256Int32(CreateScalarUnsafe(indices).AsByte()), sizeof(int));
            GatherVector256(p, ConvertToVector256Int32(CreateScalarUnsafe(indices).AsByte()), sizeof(int));
        }

        [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public unsafe void Gather18PackedPermutationNTimes()
        {
            var p = BitPermTables.BitWithInterleavedPopCountPermTablePtr;
            var indices = _indicesPtr[_iterationIndex++];
            GatherVector256(p, ConvertToVector256Int32(CreateScalarUnsafe(indices).AsByte()), sizeof(int));
            GatherVector256(p, ConvertToVector256Int32(CreateScalarUnsafe(indices).AsByte()), sizeof(int));
            GatherVector256(p, ConvertToVector256Int32(CreateScalarUnsafe(indices).AsByte()), sizeof(int));
            GatherVector256(p, ConvertToVector256Int32(CreateScalarUnsafe(indices).AsByte()), sizeof(int));
            GatherVector256(p, ConvertToVector256Int32(CreateScalarUnsafe(indices).AsByte()), sizeof(int));
            GatherVector256(p, ConvertToVector256Int32(CreateScalarUnsafe(indices).AsByte()), sizeof(int));
            GatherVector256(p, ConvertToVector256Int32(CreateScalarUnsafe(indices).AsByte()), sizeof(int));
            GatherVector256(p, ConvertToVector256Int32(CreateScalarUnsafe(indices).AsByte()), sizeof(int));
        }

        [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public unsafe void GatherN8PackedPermutations1Time()
        {
            var p = BitPermTables.BitWithInterleavedPopCountPermTablePtr;
            var indicesPtr = _indicesPtr + _iterationIndex;
            _iterationIndex += OperationsPerInvoke;
            GatherVector256(p, ConvertToVector256Int32(CreateScalarUnsafe(indicesPtr[0]).AsByte()), sizeof(int));
            GatherVector256(p, ConvertToVector256Int32(CreateScalarUnsafe(indicesPtr[1]).AsByte()), sizeof(int));
            GatherVector256(p, ConvertToVector256Int32(CreateScalarUnsafe(indicesPtr[2]).AsByte()), sizeof(int));
            GatherVector256(p, ConvertToVector256Int32(CreateScalarUnsafe(indicesPtr[3]).AsByte()), sizeof(int));
            GatherVector256(p, ConvertToVector256Int32(CreateScalarUnsafe(indicesPtr[4]).AsByte()), sizeof(int));
            GatherVector256(p, ConvertToVector256Int32(CreateScalarUnsafe(indicesPtr[5]).AsByte()), sizeof(int));
            GatherVector256(p, ConvertToVector256Int32(CreateScalarUnsafe(indicesPtr[6]).AsByte()), sizeof(int));
            GatherVector256(p, ConvertToVector256Int32(CreateScalarUnsafe(indicesPtr[7]).AsByte()), sizeof(int));
        }

        [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public unsafe uint GatherN8PackedPermutations1TimeAndUnpack()
        {
            var shuffler = LoadDquVector256(TransposeShufflerPtr);
            var p = BitPermTables.BitWithInterleavedPopCountPermTablePtr;
            var indicesPtr = _indicesPtr + _iterationIndex;
            _iterationIndex += OperationsPerInvoke;
            var tmp = Vector256<uint>.Zero;
            var p0_3 = Vector256<uint>.Zero;
            var p4_7 = Vector256<uint>.Zero;
            Transpose(shuffler, ref p0_3, ref p4_7,GatherVector256(p, ConvertToVector256Int32(CreateScalarUnsafe(indicesPtr[0]).AsByte()), sizeof(int)));
            tmp = Add(tmp, Add(p0_3, p4_7));
            Transpose(shuffler, ref p0_3, ref p4_7,GatherVector256(p, ConvertToVector256Int32(CreateScalarUnsafe(indicesPtr[1]).AsByte()), sizeof(int)));
            tmp = Add(tmp, Add(p0_3, p4_7));
            Transpose(shuffler, ref p0_3, ref p4_7,GatherVector256(p, ConvertToVector256Int32(CreateScalarUnsafe(indicesPtr[2]).AsByte()), sizeof(int)));
            tmp = Add(tmp, Add(p0_3, p4_7));
            Transpose(shuffler, ref p0_3, ref p4_7,GatherVector256(p, ConvertToVector256Int32(CreateScalarUnsafe(indicesPtr[3]).AsByte()), sizeof(int)));
            tmp = Add(tmp, Add(p0_3, p4_7));
            Transpose(shuffler, ref p0_3, ref p4_7,GatherVector256(p, ConvertToVector256Int32(CreateScalarUnsafe(indicesPtr[4]).AsByte()), sizeof(int)));
            tmp = Add(tmp, Add(p0_3, p4_7));
            Transpose(shuffler, ref p0_3, ref p4_7,GatherVector256(p, ConvertToVector256Int32(CreateScalarUnsafe(indicesPtr[5]).AsByte()), sizeof(int)));
            tmp = Add(tmp, Add(p0_3, p4_7));
            Transpose(shuffler, ref p0_3, ref p4_7,GatherVector256(p, ConvertToVector256Int32(CreateScalarUnsafe(indicesPtr[6]).AsByte()), sizeof(int)));
            tmp = Add(tmp, Add(p0_3, p4_7));
            Transpose(shuffler, ref p0_3, ref p4_7,GatherVector256(p, ConvertToVector256Int32(CreateScalarUnsafe(indicesPtr[7]).AsByte()), sizeof(int)));
            tmp = Add(tmp, Add(p0_3, p4_7));
            return tmp.GetElement(0);
        }

        [Benchmark(OperationsPerInvoke = 1)]
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public unsafe int Gather1Time8UnpackedSamePermutation()
        {
            var p = Int32PermTables.IntPermTableAlignedPtr;
            var tmp =
                Add(Int32PermTables.GetIntPermutationAligned(p, 0),
                    Add(Int32PermTables.GetIntPermutationAligned(p, 0),
                        Add(Int32PermTables.GetIntPermutationAligned(p, 0),
                            Add(Int32PermTables.GetIntPermutationAligned(p, 0),
                                Add(Int32PermTables.GetIntPermutationAligned(p, 0),
                                    Add(Int32PermTables.GetIntPermutationAligned(p, 0),
                                        Add(Int32PermTables.GetIntPermutationAligned(p, 0),
                                            Int32PermTables.GetIntPermutationAligned(p, 0))))))));
            return tmp.GetElement(0);
        }

        [Benchmark(OperationsPerInvoke = 1)]
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public unsafe int Gather1Time8UnpackedPermutation()
        {
            var p = Int32PermTables.IntPermTableAlignedPtr;
            var indicesPtr = (byte *) (_indicesPtr + _iterationIndex++);
            var tmp =
                Add(Int32PermTables.GetIntPermutationAligned(p, indicesPtr[0]),
                    Add(Int32PermTables.GetIntPermutationAligned(p, indicesPtr[1]),
                        Add(Int32PermTables.GetIntPermutationAligned(p, indicesPtr[2]),
                            Add(Int32PermTables.GetIntPermutationAligned(p, indicesPtr[3]),
                                Add(Int32PermTables.GetIntPermutationAligned(p, indicesPtr[4]),
                                    Add(Int32PermTables.GetIntPermutationAligned(p, indicesPtr[5]),
                                        Add(Int32PermTables.GetIntPermutationAligned(p, indicesPtr[6]),
                                            Int32PermTables.GetIntPermutationAligned(p, indicesPtr[7]))))))));
            return tmp.GetElement(0);
        }
    }
}