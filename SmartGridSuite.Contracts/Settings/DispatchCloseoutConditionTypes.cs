namespace SmartGridSuite.Contracts.Settings
{
    public static class DispatchCloseoutConditionTypes
    {
        public const string Always = "Always";

        public const string WriteUpFlag = "WriteUpFlag";

        public const string ReferToSelection = "ReferToSelection";

        public static readonly string[] All =
        {
            Always,
            WriteUpFlag,
            ReferToSelection
        };

        public static bool IsValid(
            string? conditionType)
        {
            return All.Any(
                x => string.Equals(
                    x,
                    conditionType,
                    StringComparison.OrdinalIgnoreCase));
        }

        public static string Normalize(
            string? conditionType)
        {
            return All.FirstOrDefault(
                       x => string.Equals(
                           x,
                           conditionType,
                           StringComparison.OrdinalIgnoreCase))
                   ?? Always;
        }
    }
}