#nullable enable
using SmartGridSuite.Client.Models.Administration;
using SmartGridSuite.Contracts.Technicians;
using SmartGridSuite.Contracts.Trucks;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace SmartGridSuite.Client.Views.Administration
{
    public partial class TechnicianEditWindow : Window
    {
        private static readonly string[] AllowedTitles =
        {
            "Apprentice",
            "Journeyman",
            "Head Journeyman",
            "Supervisor"
        };

        private sealed class TruckChoiceItem
        {
            public int? Id { get; set; }
            public string DisplayText { get; set; } = "";
        }

        public TechnicianEditWindow(IEnumerable<TruckDto> trucks)
        {
            InitializeComponent();
            LoadTruckChoices(trucks);
            LoadTitleChoices();
            SetDefaultRoles();
            SetDefaultTitle();
        }

        public TechnicianEditWindow(IEnumerable<TruckDto> trucks, AdminTechnicianRow row)
        {
            InitializeComponent();
            LoadTruckChoices(trucks);
            LoadTitleChoices();
            LoadFromRow(row);
        }

        public CreateTechnicianRequest BuildCreateRequest()
        {
            return new CreateTechnicianRequest
            {
                EmployeeId = EmployeeIdTextBox.Text.Trim(),
                FirstName = FirstNameTextBox.Text.Trim(),
                LastName = LastNameTextBox.Text.Trim(),
                Title = (TitleComboBox.SelectedItem as string ?? "").Trim(),
                IsActive = GetSelectedActiveEmployee(),
                HomeTruckId = HomeTruckComboBox.SelectedValue as int?,
                WorksMonday = WorksMondayCheckBox.IsChecked == true,
                WorksTuesday = WorksTuesdayCheckBox.IsChecked == true,
                WorksWednesday = WorksWednesdayCheckBox.IsChecked == true,
                WorksThursday = WorksThursdayCheckBox.IsChecked == true,
                WorksFriday = WorksFridayCheckBox.IsChecked == true,
                WorksSaturday = WorksSaturdayCheckBox.IsChecked == true,
                WorksSunday = WorksSundayCheckBox.IsChecked == true,
                RoleCodes = GetSelectedRoleCodes()
            };
        }

        public UpdateTechnicianRequest BuildUpdateRequest()
        {
            return new UpdateTechnicianRequest
            {
                EmployeeId = EmployeeIdTextBox.Text.Trim(),
                FirstName = FirstNameTextBox.Text.Trim(),
                LastName = LastNameTextBox.Text.Trim(),
                Title = (TitleComboBox.SelectedItem as string ?? "").Trim(),
                IsActive = GetSelectedActiveEmployee(),
                HomeTruckId = HomeTruckComboBox.SelectedValue as int?,
                WorksMonday = WorksMondayCheckBox.IsChecked == true,
                WorksTuesday = WorksTuesdayCheckBox.IsChecked == true,
                WorksWednesday = WorksWednesdayCheckBox.IsChecked == true,
                WorksThursday = WorksThursdayCheckBox.IsChecked == true,
                WorksFriday = WorksFridayCheckBox.IsChecked == true,
                WorksSaturday = WorksSaturdayCheckBox.IsChecked == true,
                WorksSunday = WorksSundayCheckBox.IsChecked == true,
                RoleCodes = GetSelectedRoleCodes()
            };
        }

        private void LoadTruckChoices(IEnumerable<TruckDto> trucks)
        {
            var items = new List<TruckChoiceItem>
            {
                new TruckChoiceItem
                {
                    Id = null,
                    DisplayText = "(None)"
                }
            };

            items.AddRange(
                trucks.Select(t => new TruckChoiceItem
                {
                    Id = t.Id,
                    DisplayText = !string.IsNullOrWhiteSpace(t.DisplayName)
                        ? $"{t.TruckNumber} - {t.DisplayName}"
                        : t.TruckNumber
                }));

            HomeTruckComboBox.ItemsSource = items;
            HomeTruckComboBox.SelectedIndex = 0;
        }

        private void LoadTitleChoices()
        {
            TitleComboBox.ItemsSource = AllowedTitles.ToList();
        }

        private void LoadFromRow(AdminTechnicianRow row)
        {
            FirstNameTextBox.Text = row.FirstName;
            LastNameTextBox.Text = row.LastName;
            EmployeeIdTextBox.Text = row.EmployeeId;
            ActiveEmployeeComboBox.SelectedIndex = row.IsActive ? 0 : 1;

            HomeTruckComboBox.SelectedValue = row.HomeTruckId;
            TitleComboBox.SelectedItem = NormalizeTitle(row.Title) ?? "Journeyman";

            WorksMondayCheckBox.IsChecked = row.WorksMonday;
            WorksTuesdayCheckBox.IsChecked = row.WorksTuesday;
            WorksWednesdayCheckBox.IsChecked = row.WorksWednesday;
            WorksThursdayCheckBox.IsChecked = row.WorksThursday;
            WorksFridayCheckBox.IsChecked = row.WorksFriday;
            WorksSaturdayCheckBox.IsChecked = row.WorksSaturday;
            WorksSundayCheckBox.IsChecked = row.WorksSunday;

            var roles = row.RoleCodes.Select(x => (x ?? "").Trim().ToUpperInvariant()).ToHashSet();

            RoleTechnicianCheckBox.IsChecked = roles.Contains("TECHNICIAN");
            RoleDispatchCheckBox.IsChecked = roles.Contains("DISPATCH");
            RoleAdminCheckBox.IsChecked = roles.Contains("ADMIN");
        }

        private void SetDefaultRoles()
        {
            RoleTechnicianCheckBox.IsChecked = true;
        }

        private void SetDefaultTitle()
        {
            TitleComboBox.SelectedItem = "Journeyman";
        }

        private List<string> GetSelectedRoleCodes()
        {
            var roles = new List<string>();

            if (RoleTechnicianCheckBox.IsChecked == true)
                roles.Add("TECHNICIAN");

            if (RoleDispatchCheckBox.IsChecked == true)
                roles.Add("DISPATCH");

            if (RoleAdminCheckBox.IsChecked == true)
                roles.Add("ADMIN");

            return roles;
        }

        private static string? NormalizeTitle(string? title)
            => (title ?? "").Trim().ToUpperInvariant() switch
            {
                "APPRENTICE" => "Apprentice",
                "JOURNEYMAN" => "Journeyman",
                "HEAD JOURNEYMAN" => "Head Journeyman",
                "SUPERVISOR" => "Supervisor",
                _ => null
            };

        private bool GetSelectedActiveEmployee()
        {
            return (ActiveEmployeeComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)
                ?.Content?.ToString() == "Yes";
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var firstName = FirstNameTextBox.Text.Trim();
            var lastName = LastNameTextBox.Text.Trim();
            var employeeId = EmployeeIdTextBox.Text.Trim();
            var title = TitleComboBox.SelectedItem as string;

            if (firstName.Length == 0)
            {
                MessageBox.Show("First Name is required.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (lastName.Length == 0)
            {
                MessageBox.Show("Last Name is required.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (employeeId.Length == 0)
            {
                MessageBox.Show("Employee ID is required.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                MessageBox.Show("Title is required.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (GetSelectedRoleCodes().Count == 0)
            {
                MessageBox.Show("Select at least one role.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}