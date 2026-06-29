using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace SmartGridSuite.Client.Views.Dispatcher.Dialogs
{
    public partial class AssignTicketsWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<TechnicianAssignmentOption> TechnicianOptions { get; } = new();

        private string _selectionSummary = "Selected: (Unassigned)";

        public string SelectionSummary
        {
            get => _selectionSummary;
            private set
            {
                if (_selectionSummary == value)
                    return;

                _selectionSummary = value;
                OnPropertyChanged();
            }
        }

        public string AssignedTech
        {
            get
            {
                if (UnassignedCheckBox?.IsChecked == true)
                    return "(Unassigned)";

                var selectedNames = TechnicianOptions
                    .Where(x => x.IsSelected)
                    .Select(x => x.Name)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x)
                    .ToList();

                return FormatTechnicianList(selectedNames);
            }
        }

        public AssignTicketsWindow(
            int ticketCount,
            IEnumerable<string> techSuggestions)
        {
            InitializeComponent();

            DataContext = this;

            HeaderTextBlock.Text =
                $"Assign {ticketCount} selected ticket(s)";

            var technicianNames = techSuggestions
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Where(x =>
                    !x.Equals(
                        "(Unassigned)",
                        StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();

            foreach (var name in technicianNames)
            {
                var option =
                    new TechnicianAssignmentOption(name);

                option.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(TechnicianAssignmentOption.IsSelected))
                        UpdateSelectionSummary();
                };

                TechnicianOptions.Add(option);
            }

            UnassignedCheckBox.IsChecked = true;

            Loaded += (_, _) =>
            {
                AssignedTechDropDownButton.Focus();
                UpdateSelectionSummary();
            };
        }

        private void AssignedTechDropDownButton_Click(object sender, RoutedEventArgs e)
        {
            AssignedTechDropDownPopup.IsOpen =
                !AssignedTechDropDownPopup.IsOpen;
        }

        private void UnassignedCheckBox_Checked(
            object sender,
            RoutedEventArgs e)
        {
            foreach (var option in TechnicianOptions)
                option.IsSelected = false;

            UpdateSelectionSummary();
        }

        private void UnassignedCheckBox_Unchecked(
            object sender,
            RoutedEventArgs e)
        {
            UpdateSelectionSummary();
        }

        private void TechnicianOption_Changed(
            object sender,
            RoutedEventArgs e)
        {
            if (TechnicianOptions.Any(x => x.IsSelected))
                UnassignedCheckBox.IsChecked = false;

            UpdateSelectionSummary();
        }

        private void SelectAllTechnicians_Click(
            object sender,
            RoutedEventArgs e)
        {
            UnassignedCheckBox.IsChecked = false;

            foreach (var option in TechnicianOptions)
                option.IsSelected = true;

            UpdateSelectionSummary();
        }

        private void ClearTechnicians_Click(
            object sender,
            RoutedEventArgs e)
        {
            UnassignedCheckBox.IsChecked = false;

            foreach (var option in TechnicianOptions)
                option.IsSelected = false;

            UpdateSelectionSummary();
        }

        private void Assign_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(AssignedTech))
            {
                MessageBox.Show(
                    "Choose one or more technicians, or choose (Unassigned).",
                    "Assign Tickets",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                AssignedTechDropDownButton.Focus();
                AssignedTechDropDownPopup.IsOpen = true;
                return;
            }

            DialogResult = true;
        }

        private void UpdateSelectionSummary()
        {
            if (UnassignedCheckBox?.IsChecked == true)
            {
                AssignedTechDropDownTextBlock.Text =
                    "(Unassigned)";

                SelectionSummary =
                    "Selected: (Unassigned)";

                return;
            }

            var selectedNames = TechnicianOptions
                .Where(x => x.IsSelected)
                .Select(x => x.Name)
                .OrderBy(x => x)
                .ToList();

            if (selectedNames.Count == 0)
            {
                AssignedTechDropDownTextBlock.Text =
                    "Choose technician(s)...";

                SelectionSummary =
                    "No technician selected.";

                return;
            }

            var display =
                FormatTechnicianList(selectedNames);

            AssignedTechDropDownTextBlock.Text =
                display;

            SelectionSummary =
                $"Selected: {display}";
        }

        private static string FormatTechnicianList(
            IReadOnlyList<string> names)
        {
            if (names.Count == 0)
                return "";

            if (names.Count == 1)
                return names[0];

            if (names.Count == 2)
                return $"{names[0]} & {names[1]}";

            return string.Join(
                       ", ",
                       names.Take(names.Count - 1)) +
                   " & " +
                   names.Last();
        }

        private void OnPropertyChanged(
            [CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(name));
    }

    public sealed class TechnicianAssignmentOption : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public string Name { get; }

        private bool _isSelected;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                    return;

                _isSelected = value;

                PropertyChanged?.Invoke(
                    this,
                    new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public TechnicianAssignmentOption(string name)
        {
            Name = name;
        }
    }
}