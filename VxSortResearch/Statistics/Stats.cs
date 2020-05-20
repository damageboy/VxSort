using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace VxSortResearch.Statistics
{
    internal class RawStats
    {
        public string MethodName                { get; internal set; }
        public int   ProblemSize                { get; internal set; }
        public int   MaxDepth                   { get; internal set; }
        public ulong NumPartitionOperations     { get; internal set; }
        public ulong NumSmallSorts              { get; internal set; }
        public ulong TotalSmallSortsSize        { get; internal set; }
        public double AverageSmallSortSize      { get; internal set; }
        public ulong NumVectorizedLoads         { get; internal set; }
        public ulong NumVectorizedStores        { get; internal set; }
        public ulong NumVectorCompares          { get; internal set; }
        public ulong NumPermutations            { get; internal set; }
        public ulong NumScalarCompares          { get; internal set; }
        public ulong NumSmallSortScalarCompares { get; internal set; }
        public double PercentSmallSortCompares  { get; internal set; }
        public ulong NumScalarSwaps             { get; internal set; }
        internal int Depth                      { get; set; }
    }

    internal class DataTableScaledStats : RawStats
    {
        public DataTableScaledStats(RawStats stats, RawStats baseLine)
        {
            foreach (var prop in typeof(RawStats).GetProperties())
                prop.SetValue(this, prop.GetValue(stats));

            foreach (var prop in typeof(DataTableScaledStats).GetProperties()
                .Where(f => f.Name.EndsWith("Scaled", StringComparison.InvariantCulture))) {
                var x = typeof(RawStats).GetProperty(prop.Name.Replace("Scaled", "", StringComparison.InvariantCulture));

                var scaled = Convert.ToDouble(x.GetValue(stats), CultureInfo.InvariantCulture) / Convert.ToDouble(x.GetValue(baseLine), CultureInfo.InvariantCulture);
                if (double.IsInfinity(scaled) || double.IsNaN(scaled))
                    prop.SetValue(this, null);
                else
                    prop.SetValue(this, scaled);
            }
        }

        // ReSharper disable UnusedMember.Global
        public double? MaxDepthScaled                            { get; internal set; }
        public double? NumPartitionOperationsScaled              { get; internal set; }
        public double? NumSmallSortsScaled                       { get; internal set; }
        public double? TotalSmallSortsSizeScaled                 { get; internal set; }
        public double? AverageSmallSortSizeScaled                { get; internal set; }
        public double? NumVectorizedLoadsScaled                  { get; internal set; }
        public double? NumVectorizedStoresScaled                 { get; internal set; }
        public double? NumVectorComparesScaled                   { get; internal set; }
        public double? NumPermutationsScaled                     { get; internal set; }
        public double? NumScalarComparesScaled                   { get; internal set; }
        public double? NumSmallSortScalarComparesScaled          { get; internal set; }
        public double? NumScalarSwapsScaled                      { get; internal set; }

        public string MaxDepthScaledDataTable                    { get; internal set; }
        public string NumPartitionOperationsScaledDataTable      { get; internal set; }
        public string NumSmallSortsScaledDataTable               { get; internal set; }
        public string TotalSmallSortsSizeScaledDataTable         { get; internal set; }
        public string AverageSmallSortSizeScaledDataTable        { get; internal set; }
        public string NumVectorizedLoadsScaledDataTable          { get; internal set; }
        public string NumVectorizedStoresScaledDataTable         { get; internal set; }
        public string NumVectorComparesScaledDataTable           { get; internal set; }
        public string NumPermutationsScaledDataTable             { get; internal set; }
        public string NumScalarComparesScaledDataTable           { get; internal set; }
        public string NumSmallSortScalarComparesScaledDataTable  { get; internal set; }
        public string NumScalarSwapsScaledDataTable              { get; internal set; }
        // ReSharper restore UnusedMember.Global
    }

    public static class Stats
    {
        [Conditional("STATS")]
        public static void BumpSorts(string method, int sortSize)
        {
            if (!_logs.TryGetValue(method, out var sizeDictionary))
                _logs[method] = sizeDictionary = new Dictionary<int, List<RawStats>>();

            if (!sizeDictionary.TryGetValue(sortSize, out var list))
                sizeDictionary[sortSize] = list = new List<RawStats>();

            list.Add(_s = new RawStats());
        }

        [Conditional("STATS")]
        public static void BumpDepth(int n)
        {
            _s.Depth += n;
            if (_s.Depth > _s.MaxDepth)
                _s.MaxDepth = _s.Depth;
        }

        [Conditional("STATS")] public static void BumpPartitionOperations(ulong n = 1)     => _s.NumPartitionOperations += n;
        [Conditional("STATS")] public static void BumpSmallSorts(ulong n = 1)              => _s.NumSmallSorts += n;
        [Conditional("STATS")] public static void BumpSmallSortsSize(ulong n)              => _s.TotalSmallSortsSize += n;
        [Conditional("STATS")] public static void BumpVectorizedLoads(ulong n = 1)         => _s.NumVectorizedLoads += n;
        [Conditional("STATS")] public static void BumpVectorizedStores(ulong n = 1)        => _s.NumVectorizedStores += n;
        [Conditional("STATS")] public static void BumpVectorCompares(ulong n = 1)          => _s.NumVectorCompares += n;
        [Conditional("STATS")] public static void BumpPermutations(ulong n = 1)            => _s.NumPermutations += n;
        [Conditional("STATS")] public static void BumpScalarCompares(ulong n = 1)          => _s.NumScalarCompares += n;
        [Conditional("STATS")] public static void BumpSmallSortScalarCompares(ulong n = 1)
        {
            _s.NumScalarCompares += n;
            _s.NumSmallSortScalarCompares += n;
        }

        [Conditional("STATS")] public static void BumpVectorizedPartitionBlocks(ulong n = 1)
        {
            BumpVectorizedLoads(2 * n); // One for data + One for permutation
            BumpVectorCompares(n);
            BumpVectorizedStores(2 * n); // One for left, One for right
            BumpPermutations(n);
        }
        
        [Conditional("STATS")] public static void BumpPackedVectorizedPartitionBlocks(ulong n = 1)
        {
            BumpVectorizedLoads(n); // One for data + One for permutation
            BumpVectorCompares(n);
            BumpVectorizedStores(2 * n); // One for left, One for right
            BumpPermutations(n);
        }


        static readonly Dictionary<string, Dictionary<int, List<RawStats>>> _logs =
            new Dictionary<string, Dictionary<int, List<RawStats>>>();

        static RawStats _s;
        static Dictionary<string, Dictionary<int, RawStats>> _aggStats;



        internal static Dictionary<string, Dictionary<int, RawStats>> AggregateAllStats()
        {
            if (_aggStats != null)
                return _aggStats;

            var allStats =
                _logs.ToDictionary(x => x.Key, x => x.Value
                .Select(kvp => (kvp.Key, kvp.Value.Count, AggStats(kvp.Value)))
                .Select(x => (x.Key, AvgStats(x.Item3, (ulong) x.Count)))
                .OrderBy(x => x.Key)
                .ToDictionary(x => x.Key, x => x.Item2));

            foreach (var (methodName, sizeDict) in allStats) {
                foreach (var (size, oneStat) in sizeDict) {
                    oneStat.MethodName  = methodName;
                    oneStat.ProblemSize = size;
                }
            }

            return _aggStats = allStats;

            RawStats AggStats(List<RawStats> list)
            {
                var aggStats = list.Aggregate((acc, s) => {
                    acc.MaxDepth                   += s.MaxDepth;
                    acc.NumPartitionOperations     += s.NumPartitionOperations;
                    acc.NumSmallSorts              += s.NumSmallSorts;
                    acc.TotalSmallSortsSize        += s.TotalSmallSortsSize;
                    acc.NumVectorizedLoads         += s.NumVectorizedLoads;
                    acc.NumVectorizedStores        += s.NumVectorizedStores;
                    acc.NumVectorCompares          += s.NumVectorCompares;
                    acc.NumPermutations            += s.NumPermutations;
                    acc.NumScalarCompares          += s.NumScalarCompares;
                    acc.NumSmallSortScalarCompares += s.NumSmallSortScalarCompares;
                    acc.NumScalarSwaps             += s.NumScalarSwaps;
                    return acc;
                });
                return aggStats;
            }

            RawStats AvgStats(RawStats aggStats, ulong n)
            {
                return new RawStats {
                    MaxDepth                   = aggStats.MaxDepth / (int) n,
                    NumPartitionOperations     = aggStats.NumPartitionOperations / n,
                    NumSmallSorts              = aggStats.NumSmallSorts / n,
                    TotalSmallSortsSize        = aggStats.TotalSmallSortsSize / n,
                    AverageSmallSortSize       = aggStats.NumSmallSorts == 0 ? 0 : aggStats.TotalSmallSortsSize / (double) aggStats.NumSmallSorts,
                    NumVectorizedLoads         = aggStats.NumVectorizedLoads / n,
                    NumVectorizedStores        = aggStats.NumVectorizedStores / n,
                    NumVectorCompares          = aggStats.NumVectorCompares / n,
                    NumPermutations            = aggStats.NumPermutations / n,
                    NumScalarCompares          = aggStats.NumScalarCompares / n,
                    NumSmallSortScalarCompares = aggStats.NumSmallSortScalarCompares / n,
                    PercentSmallSortCompares   = aggStats.NumSmallSortScalarCompares / (double) aggStats.NumScalarCompares
                };

            }

        }

        public static string GenerateTableDump()
        {
            var stats = AggregateAllStats();

            var columnNames = new[] {
                "Method",
                "Size",
                "MaxDepth",
                "\\# Partitions",
                "\\# Small Sorts",
                "Avg. Small Sort Size",
                "\\# Vector Loads",
                "\\# Vector Stores",
                "\\# Vector Compares",
                "\\# Permutations",
                "\\# Scalar Compares",
                "\\# Small Sort Compares",
                "% Small Sort Compares"
            };

            var table = new ConsoleTable(
                columnNames, new[] {"", "" }.Concat(Enumerable.Repeat("#,0.##", 11))
                );

            foreach (var (methodName, sizeDict) in stats) {
                table.AddRow(columnNames.Select(_ => String.Empty).ToArray());
                foreach (var (size, oneStat) in sizeDict) {
                    table.AddRow(GetRowValues(oneStat));
                }
            }

            return table.ToMarkDownString();

            // ReSharper disable HeapView.BoxingAllocation
            object[] GetRowValues(RawStats s) =>
                new object[] {
                    s.MethodName,
                    s.ProblemSize,
                    s.MaxDepth,
                    s.NumPartitionOperations,
                    s.NumSmallSorts,
                    s.AverageSmallSortSize,
                    s.NumVectorizedLoads,
                    s.NumVectorizedStores,
                    s.NumVectorCompares,
                    s.NumPermutations,
                    s.NumScalarCompares,
                    s.NumSmallSortScalarCompares,
                    s.PercentSmallSortCompares,
                };
            // ReSharper restore HeapView.BoxingAllocation
        }

        public static void GenerateJsonStats(string statsFilename, string baseLine)
        {
            var stats = AggregateAllStats();

            foreach (var (methodName, sizeDict) in stats) {
                foreach (var (size, oneStat) in sizeDict) {
                    oneStat.MethodName = methodName;
                    oneStat.ProblemSize       = size;
                }
            }

            var bySize =
                from methodName in stats.Keys
                let sizeDict = stats[methodName]
                from size in sizeDict.Keys
                let oneStat = sizeDict[size]
                group oneStat by size
                into g
                select new { Stats = g.Select(s => new DataTableScaledStats(s, stats[baseLine][s.ProblemSize])).ToArray() };

            var sizedGroups = bySize.ToArray();

            foreach (var g in sizedGroups) {
                RescaleGroup(g.Stats, baseLine);
            }

            using var writer = new Utf8JsonWriter(File.Create(statsFilename), new JsonWriterOptions {Indented = true});
            JsonSerializer.Serialize(writer, sizedGroups.SelectMany(s => s.Stats).ToArray());
        }

        static void RescaleGroup(IEnumerable<DataTableScaledStats> stats, string baseLine)
        {
            foreach (var field in typeof(DataTableScaledStats).GetProperties().Where(f => f.Name.EndsWith("DataTable", StringComparison.InvariantCulture))) {
                var origField = typeof(DataTableScaledStats).GetProperty(field.Name.Replace("ScaledDataTable", "", StringComparison.InvariantCulture));
                var scaledField = typeof(DataTableScaledStats).GetProperty(field.Name.Replace("DataTable", "", StringComparison.InvariantCulture));

                var nonNaNScaled = stats.Select(s => (double?) scaledField.GetValue(s)).Where(d => d.HasValue).Select(d => d.Value)
                    .ToArray();
                var origMax = stats.Select(s => Convert.ToDouble(origField.GetValue(s), CultureInfo.InvariantCulture)).Max();

                var max = (nonNaNScaled.Length == 0) ? origMax : nonNaNScaled.Max();

                foreach (var s in stats) {
                    var origValue = origField.GetValue(s);
                    var dataTableScale = (double?) scaledField.GetValue(s) * 100 ?? 0;
                    var value = $"({origValue:0.##}:{Math.Round(dataTableScale / max)});{dataTableScale:#,0.##}%";
                    if (s.MethodName == baseLine)
                        value += ";#aaaaaa";
                    field.SetValue(s, value);
                }
            }
        }
    }
}
