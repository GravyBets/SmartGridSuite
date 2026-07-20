#nullable enable
using System;
using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SmartGridSuite.Contracts.Snmp
{
    /// <summary>
    /// Evaluates admin-configured SNMP formulas.
    ///
    /// Important:
    /// - The formula itself is NOT hard-coded.
    /// - Admin config supplies formulas per OID.
    /// - "x" is the input value.
    ///
    /// Read Formula:
    ///     x = raw value returned by radio.
    ///
    /// Write Formula:
    ///     x = displayed/user-entered value.
    /// </summary>
    public static class SnmpFormulaEvaluator
    {
        // Only allow numbers, x, whitespace, decimal points, math operators, and parentheses.
        // This keeps DataTable.Compute from being exposed to arbitrary expressions.
        private static readonly Regex AllowedCharactersRegex = new(
            @"^[0-9xX\.\+\-\*/\(\)\s]+$",
            RegexOptions.Compiled);

        private static readonly Regex VariableRegex = new(
            @"\bx\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static bool IsValidFormula(string? formula)
        {
            // Validate by trying a sample calculation.
            return TryEvaluate(formula, 12m, out _);
        }

        public static bool TryEvaluate(string? formula, decimal x, out decimal result)
        {
            result = 0m;

            var cleanFormula = (formula ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(cleanFormula))
                return false;

            if (!AllowedCharactersRegex.IsMatch(cleanFormula))
                return false;

            // Require the admin to use x so the formula actually depends on the SNMP value.
            if (!VariableRegex.IsMatch(cleanFormula))
                return false;

            var xText = x.ToString(CultureInfo.InvariantCulture);

            var expression = VariableRegex.Replace(cleanFormula, xText);

            try
            {
                var rawResult = new DataTable().Compute(expression, null);

                result = Convert.ToDecimal(
                    rawResult,
                    CultureInfo.InvariantCulture);

                return true;
            }
            catch
            {
                result = 0m;
                return false;
            }
        }

        public static string FormatReadValue(
            string? rawValue,
            string? readFormula,
            int? decimalPlaces,
            string? unitLabel)
        {
            var cleanRaw = (rawValue ?? string.Empty).Trim();

            if (!decimal.TryParse(
                    cleanRaw,
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out var rawNumber))
            {
                return cleanRaw;
            }

            if (!TryEvaluate(readFormula, rawNumber, out var decoded))
                return cleanRaw;

            var text = decimalPlaces.HasValue
                ? Math.Round(decoded, decimalPlaces.Value, MidpointRounding.AwayFromZero)
                    .ToString($"F{decimalPlaces.Value}", CultureInfo.InvariantCulture)
                : decoded.ToString("0.##########", CultureInfo.InvariantCulture);

            var cleanUnit = (unitLabel ?? string.Empty).Trim();

            return string.IsNullOrWhiteSpace(cleanUnit)
                ? text
                : $"{text} {cleanUnit}";
        }

        public static bool TryBuildWriteValue(
            string? displayValue,
            string? writeFormula,
            out string rawWriteValue)
        {
            rawWriteValue = string.Empty;

            var cleanDisplay = (displayValue ?? string.Empty).Trim();

            if (!decimal.TryParse(
                    cleanDisplay,
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out var displayNumber))
            {
                return false;
            }

            if (!TryEvaluate(writeFormula, displayNumber, out var rawNumber))
                return false;

            // Radios expect whole-number SET values.
            rawWriteValue = Math.Round(rawNumber, 0, MidpointRounding.AwayFromZero)
                .ToString("0", CultureInfo.InvariantCulture);

            return true;
        }
    }
}