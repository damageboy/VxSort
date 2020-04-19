using System;
using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Extensions;
using BenchmarkDotNet.Mathematics;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using JetBrains.Annotations;

namespace Bench.Utils
{
    public class SpeedupRatioColumn : BaselineCustomColumn
    {
        public enum RatioMetric
        {
            Min,
            Mean,
            Median,
        }

        public static readonly IColumn SpeedupOfMin = new SpeedupRatioColumn(RatioMetric.Min);
        public static readonly IColumn SpeedupOfMean = new SpeedupRatioColumn(RatioMetric.Mean);
        public static readonly IColumn SpeedupOfMedian = new SpeedupRatioColumn(RatioMetric.Median);

        public RatioMetric Metric { get; }

        private SpeedupRatioColumn(RatioMetric metric)
        {
            Metric = metric;
        }

        public override string Id => nameof(SpeedupRatioColumn) + "." + Metric;

        public override string ColumnName
        {
            get {
                return Metric switch {
                    RatioMetric.Mean   => "SpeedupMean",
                    RatioMetric.Min    => "SpeedupMin",
                    RatioMetric.Median => "SpeedupMedian",
                    _                  => throw new NotSupportedException()
                };
            }
        }

        internal override string GetValue(Summary summary,        BenchmarkCase benchmarkCase, Statistics baseline,
            IReadOnlyDictionary<string, Metric>   baselineMetric, Statistics    current,
            IReadOnlyDictionary<string, Metric>   currentMetric,  bool          isBaseline)
        {
            var ratio = GetRatioStatistics(current, baseline);
            if (ratio == null)
                return "NA";

            var cultureInfo = summary.GetCultureInfo();
            return Metric switch {
                RatioMetric.Mean => IsNonBaselinesPrecise(summary, baseline, benchmarkCase)
                    ? ratio.Mean.ToString("N3", cultureInfo)
                    : ratio.Mean.ToString("N2", cultureInfo),
                RatioMetric.Min => IsNonBaselinesPrecise(summary, baseline, benchmarkCase)
                    ? ratio.Min.ToString("N3", cultureInfo)
                    : ratio.Min.ToString("N2", cultureInfo),
                RatioMetric.Median => IsNonBaselinesPrecise(summary, baseline, benchmarkCase)
                    ? ratio.Median.ToString("N3", cultureInfo)
                    : ratio.Median.ToString("N2", cultureInfo),
                _ => throw new NotSupportedException()
            };
        }

        private static bool IsNonBaselinesPrecise(Summary summary, Statistics baselineStat, BenchmarkCase benchmarkCase)
        {
            string logicalGroupKey = summary.GetLogicalGroupKey(benchmarkCase);
            var nonBaselines = summary.GetNonBaselines(logicalGroupKey);
            return nonBaselines.Any(x => GetRatioStatistics(summary[x].ResultStatistics, baselineStat)?.Mean < 0.01);
        }

        [CanBeNull]
        private static Statistics GetRatioStatistics([CanBeNull] Statistics current, [CanBeNull] Statistics baseline)
        {
            if (current == null || current.N < 1)
                return null;
            if (baseline == null || baseline.N < 1)
                return null;
            try
            {
                return Statistics.Divide(baseline, current);
            }
            catch (DivideByZeroException)
            {
                return null;
            }
        }

        public override int PriorityInCategory => (int) Metric;
        public override bool IsNumeric => true;
        public override UnitType UnitType => UnitType.Dimensionless;

        public override string Legend
        {
            get {
                return Metric switch {
                    RatioMetric.Min    => "Speedup of the minimum execution times ([Current]/[Baseline])",
                    RatioMetric.Mean   => "Speedup of the mean execution times ([Current]/[Baseline])",
                    RatioMetric.Median => "Speedup of the median execution times ([Current]/[Baseline])",
                    _                  => throw new ArgumentOutOfRangeException(nameof(Metric))
                };
            }
        }
    }
}