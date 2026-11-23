namespace SmartGridSuite.Client.Models.Dispatcher
{
    // One line from the currently displayed technician write-up's equipment section.
    // Preserve repeated lines and serial values exactly as submitted.
    public sealed class DispatchEquipmentChangeLine
    {
        public string DisplayText { get; set; } = "";
        public string SerialNumber { get; set; } = "";
        public bool HasSerialNumber => !string.IsNullOrWhiteSpace(SerialNumber);
    }
}
