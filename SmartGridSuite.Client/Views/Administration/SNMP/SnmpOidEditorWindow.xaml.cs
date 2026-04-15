using SmartGridSuite.Contracts.Snmp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace SmartGridSuite.Client.Views.Administration.SNMP
{
    public partial class SnmpOidEditorWindow : Window
    {
        private readonly List<SnmpOidDecodeValueDto> _decodeValues = new();
        private SnmpOidDecodeValueDto? _editingDecodeRow;

        public SnmpOidConfigDto? Result { get; private set; }

        public SnmpOidEditorWindow()
        {
            InitializeComponent();

            HookEvents();
            SetDefaults();
        }

        public void LoadOid(SnmpOidConfigDto? oid)
        {
            if (oid is null)
            {
                WindowTitleTextBlock.Text = "New SNMP OID";
                Result = null;
                _decodeValues.Clear();
                RefreshDecodeGrid();
                return;
            }

            WindowTitleTextBlock.Text = "Edit SNMP OID";

            CategoryTextBox.Text = oid.Category;
            LabelTextBox.Text = oid.Label;
            OidTextBox.Text = oid.Oid;
            SetComboText(ValueTypeComboBox, oid.ValueType);
            SortOrderTextBox.Text = oid.SortOrder.ToString();
            WritableCheckBox.IsChecked = oid.IsWritable;
            ShowInWorkspaceCheckBox.IsChecked = oid.ShowInWorkspace;

            SetComboText(DecodeModeComboBox, string.IsNullOrWhiteSpace(oid.DecodeMode) ? "Raw" : oid.DecodeMode);
            ShowRawAlongsideDecodedCheckBox.IsChecked = oid.ShowRawValueAlongsideDecoded;

            _decodeValues.Clear();
            _decodeValues.AddRange(
                oid.DecodeValues
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.RawValue)
                    .Select(x => new SnmpOidDecodeValueDto
                    {
                        Id = x.Id,
                        RawValue = x.RawValue,
                        DisplayText = x.DisplayText,
                        SortOrder = x.SortOrder
                    }));

            RefreshDecodeGrid();
        }

        private void HookEvents()
        {
            AddDecodeRowButton.Click += AddDecodeRowButton_Click;
            RemoveDecodeRowButton.Click += RemoveDecodeRowButton_Click;
            ApplyDecodeRowButton.Click += ApplyDecodeRowButton_Click;
            DecodeValuesDataGrid.SelectionChanged += DecodeValuesDataGrid_SelectionChanged;

            SaveButtonEx.Click += SaveButtonEx_Click;
            CancelButtonEx.Click += CancelButtonEx_Click;
        }

        private void SetDefaults()
        {
            SetComboText(ValueTypeComboBox, "String");
            SetComboText(DecodeModeComboBox, "Raw");
            SortOrderTextBox.Text = "0";
            ShowInWorkspaceCheckBox.IsChecked = true;
            DecodeSortOrderTextBox.Text = "0";
            RefreshDecodeGrid();
        }

        private void AddDecodeRowButton_Click(object sender, RoutedEventArgs e)
        {
            ClearDecodeEditor();
            DecodeValuesDataGrid.SelectedItem = null;
            StatusTextBlock.Text = "New decode row.";
        }

        private void RemoveDecodeRowButton_Click(object sender, RoutedEventArgs e)
        {
            if (DecodeValuesDataGrid.SelectedItem is not SnmpOidDecodeValueDto selected)
            {
                MessageBox.Show(
                    "Select a decode row first.",
                    "SNMP OID Decoder",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            _decodeValues.Remove(selected);
            RefreshDecodeGrid();
            ClearDecodeEditor();

            StatusTextBlock.Text = "Decode row removed.";
        }

        private void ApplyDecodeRowButton_Click(object sender, RoutedEventArgs e)
        {
            var rawValue = (DecodeRawValueTextBox.Text ?? string.Empty).Trim();
            var displayText = (DecodeDisplayTextBox.Text ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(rawValue))
            {
                MessageBox.Show(
                    "Raw value is required.",
                    "SNMP OID Decoder",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (string.IsNullOrWhiteSpace(displayText))
            {
                MessageBox.Show(
                    "Display text is required.",
                    "SNMP OID Decoder",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (!int.TryParse((DecodeSortOrderTextBox.Text ?? "0").Trim(), out var sortOrder))
                sortOrder = 0;

            if (_editingDecodeRow is null)
            {
                _decodeValues.Add(new SnmpOidDecodeValueDto
                {
                    Id = 0,
                    RawValue = rawValue,
                    DisplayText = displayText,
                    SortOrder = sortOrder
                });
            }
            else
            {
                _editingDecodeRow.RawValue = rawValue;
                _editingDecodeRow.DisplayText = displayText;
                _editingDecodeRow.SortOrder = sortOrder;
            }

            RefreshDecodeGrid();
            ClearDecodeEditor();
            StatusTextBlock.Text = "Decode row applied.";
        }

        private void DecodeValuesDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DecodeValuesDataGrid.SelectedItem is not SnmpOidDecodeValueDto selected)
            {
                _editingDecodeRow = null;
                return;
            }

            _editingDecodeRow = selected;
            DecodeRawValueTextBox.Text = selected.RawValue;
            DecodeDisplayTextBox.Text = selected.DisplayText;
            DecodeSortOrderTextBox.Text = selected.SortOrder.ToString();
        }

        private void SaveButtonEx_Click(object sender, RoutedEventArgs e)
        {
            var label = (LabelTextBox.Text ?? string.Empty).Trim();
            var oid = (OidTextBox.Text ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(label))
            {
                MessageBox.Show(
                    "Label is required.",
                    "SNMP OID",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (string.IsNullOrWhiteSpace(oid))
            {
                MessageBox.Show(
                    "OID is required.",
                    "SNMP OID",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (!int.TryParse((SortOrderTextBox.Text ?? "0").Trim(), out var sortOrder))
                sortOrder = 0;

            Result = new SnmpOidConfigDto
            {
                Category = string.IsNullOrWhiteSpace(CategoryTextBox.Text) ? "General" : CategoryTextBox.Text.Trim(),
                Label = label,
                Oid = oid,
                ValueType = GetComboText(ValueTypeComboBox, "String"),
                IsWritable = WritableCheckBox.IsChecked == true,
                ShowInWorkspace = ShowInWorkspaceCheckBox.IsChecked == true,
                SortOrder = sortOrder,
                DecodeMode = GetComboText(DecodeModeComboBox, "Raw"),
                ShowRawValueAlongsideDecoded = ShowRawAlongsideDecodedCheckBox.IsChecked == true,
                DecodeValues = _decodeValues
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.RawValue)
                    .Select(x => new SnmpOidDecodeValueDto
                    {
                        Id = x.Id,
                        RawValue = x.RawValue,
                        DisplayText = x.DisplayText,
                        SortOrder = x.SortOrder
                    })
                    .ToList()
            };

            DialogResult = true;
            Close();
        }

        private void CancelButtonEx_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void RefreshDecodeGrid()
        {
            DecodeValuesDataGrid.ItemsSource = null;
            DecodeValuesDataGrid.ItemsSource = _decodeValues
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.RawValue)
                .ToList();
        }

        private void ClearDecodeEditor()
        {
            _editingDecodeRow = null;
            DecodeRawValueTextBox.Text = string.Empty;
            DecodeDisplayTextBox.Text = string.Empty;
            DecodeSortOrderTextBox.Text = "0";
        }

        private static string GetComboText(ComboBox comboBox, string fallback)
        {
            if (comboBox.SelectedItem is ComboBoxItem item && item.Content is string itemText)
                return itemText;

            if (comboBox.Text is { Length: > 0 } text)
                return text.Trim();

            return fallback;
        }

        private static void SetComboText(ComboBox comboBox, string value)
        {
            var match = comboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(x =>
                    string.Equals(x.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                comboBox.SelectedItem = match;
                return;
            }

            comboBox.Text = value;
        }
    }
}