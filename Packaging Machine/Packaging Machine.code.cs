using System;
using System.Collections.Generic;
using System.Linq;
using VanRiet.Shipment.Sorter.Weighing.Strategies;
using static VanRiet.Shipment.Sorter.Weighing.Constants;

namespace VanRiet.Shipment.Sorter.Weighing
{
    public class WeightAllowanceCalculator
    {
        public virtual WeightAllowanceCalculationResult Calculate(uint weightInGrams)
        {
            var strategy = new PreCalculatedWeightStrategy(weightInGrams);

            return CalculateWeightAllowance(strategy);
        }

        public virtual WeightAllowanceCalculationResult Calculate(
            WeightAllowanceCalculationInformation info)
        {
            var strategy = ChooseStrategy();

            return CalculateWeightAllowance(strategy);

            IShipmentWeightAllowanceStrategy ChooseStrategy()
            {
                if (!info.Products.Any())
                    return new UnknownShipmentStrategy();

                var anyProductsWithUnknownWeight =
                    info.Products.Any(p => !p.WeightGrams.HasValue);

                if (anyProductsWithUnknownWeight && info.IsMultiColli)
                    return new UnknownShipmentStrategy();

                if (info.IsMultiColli)
                    return new MultiColliShipmentStrategy(info);

                if (anyProductsWithUnknownWeight)
                    return new UnknownProductWeightStrategy(info);

                if (info.PreCalculatedWeight.HasValue)
                    return new PreCalculatedWeightStrategy(info.PreCalculatedWeight.Value);

                return new SingleShipmentStrategy(info);
            }
        }

        private WeightAllowanceCalculationResult CalculateWeightAllowance(
            IShipmentWeightAllowanceStrategy input
        )
        {
            checked
            {
                var intermediateResults = new List<WeightAllowanceCorrection>();

                var result = ApplyToleranceCoefficientsToProductsWeight();
                result = ApplyMaximumAllowedProductsWeightCorrection(result);
                result = AddPackaging(result);
                result = ApplySupportedGrossWeightCorrection(result);
                result = ApplySensorsAccuracyCorrection(result);

                return new WeightAllowanceCalculationResult(
                    new WeightRangeGrams(
                        min: (uint) result.min,
                        max: (uint) result.max
                    ),
                    input.PackagingType,
                    input.GetType(),
                    intermediateResults
                );

                (decimal min, decimal max) ApplyToleranceCoefficientsToProductsWeight()
                {
                    decimal calculatedMin = Math.Floor(
                        input.Products.Min * _settings.MinimumProductWeightToleranceCoefficient
                    );

                    decimal calculatedMax = Math.Ceiling(
                        input.Products.Max * _settings.MaximumProductWeightToleranceCoefficient
                    );

                    var correctedRange = (calculatedMin, calculatedMax);

                    AddIntermediateResult(
                        WeightAllowanceCorrectionType.AppliedProductWeightToleranceCoefficients,
                        original: (input.Products.Min, input.Products.Max),
                        corrected: correctedRange
                    );

                    return correctedRange;
                }

                void AddIntermediateResult(WeightAllowanceCorrectionType type, (decimal, decimal) original, (decimal, decimal) corrected) =>
                    intermediateResults.Add(new WeightAllowanceCorrection(type, original: original, corrected: corrected));

                (decimal min, decimal max) AddPackaging((decimal min, decimal max) range)
                {
                    var correctedRange = (range.min + input.Packaging.Min, range.max + input.Packaging.Max);

                    AddIntermediateResult(
                        WeightAllowanceCorrectionType.AddedPackagingWeight,
                        original: range,
                        corrected: correctedRange
                    );

                    return correctedRange;
                }

                (decimal min, decimal max) ApplySensorsAccuracyCorrection(
                    (decimal min, decimal max) range
                )
                {
                    decimal calculatedMin = Math.Max(range.min - SensorsAccuracyGrams, 0);
                    decimal calculatedMax = range.max + SensorsAccuracyGrams;

                    var correctedRange = (calculatedMin, calculatedMax);

                    AddIntermediateResult(
                        WeightAllowanceCorrectionType.IncludedSensorsAccuracy,
                        original: range,
                        corrected: correctedRange
                    );

                    return correctedRange;
                }

                (decimal min, decimal max) ApplyMaximumAllowedProductsWeightCorrection(
                    (decimal min, decimal max) range
                )
                {
                    decimal calculatedMin = range.min;
                    decimal calculatedMax = range.max;
                    calculatedMin = Math.Min(calculatedMin, input.MaxSupportedWeightGrams);
                    calculatedMax = Math.Min(calculatedMax, input.MaxSupportedWeightGrams);

                    var corrected = (calculatedMin, calculatedMax);

                    if (range != corrected)
                    {
                        intermediateResults.Add(
                            new WeightAllowanceCorrection(
                                WeightAllowanceCorrectionType.ExceededMaximumAllowedProductsWeight,
                                original: range,
                                corrected: (calculatedMin, calculatedMax)
                            )
                        );
                    }

                    return corrected;
                }

                (decimal min, decimal max) ApplySupportedGrossWeightCorrection(
                    (decimal min, decimal max) range
                )
                {
                    decimal calculatedMin = range.min;
                    decimal calculatedMax = range.max;
                    calculatedMin = Math.Max(calculatedMin, 0);
                    calculatedMin = Math.Min(calculatedMin, MaxSupportedWeightGrams);
                    calculatedMax = Math.Max(calculatedMax, 0);
                    calculatedMax = Math.Min(calculatedMax, MaxSupportedWeightGrams);

                    var corrected = (calculatedMin, calculatedMax);

                    if (range != corrected)
                    {
                        intermediateResults.Add(
                            new WeightAllowanceCorrection(
                                WeightAllowanceCorrectionType.ExceededSupportedGrossWeight,
                                original: range,
                                corrected: (calculatedMin, calculatedMax)
                            )
                        );
                    }

                    return corrected;
                }
            }
        }

        private readonly IWeighingSettings _settings;

        public WeightAllowanceCalculator(IWeighingSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }
    }
}