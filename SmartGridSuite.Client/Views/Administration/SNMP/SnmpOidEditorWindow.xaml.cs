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

            MaxHeight = SystemParameters.WorkArea.Height - 40;
            MaxWidth = SystemParameters.WorkArea.Width - 40;

            if (Height > MaxHeight)
                Height = MaxHeight;

            if (Width > MaxWidth)
                Width = MaxWidth;

            HookEvents();
            SetDefaults();
        }

        public void LoadOid(SnmpOidConfigDto? oid, int suggestedSortOrder = 10)
        {
            if (oid is null)
            {
                WindowTitleTextBlock.Text = "New SNMP OID";
                Result = null;

                SetComboText(CategoryComboBox, "Config");
                LabelTextBox.Text = string.Empty;
                OidTextBox.Text = string.Empty;
                SetComboText(ValueTypeComboBox, "String");
                SortOrderTextBox.Text = suggestedSortOrder.ToString();
                WritableCheckBox.IsChecked = false;
                ShowInWorkspaceCheckBox.IsChecked = true;

                SetComboText(DecodeModeComboBox, "Raw");
                ShowRawAlongsideDecodedCheckBox.IsChecked = false;

                // Formula fields start blank for new OIDs.
                // They only apply when Decode Mode is Formula.
                ReadFormulaTextBox.Text = string.Empty;
                WriteFormulaTextBox.Text = string.Empty;
                DecimalPlacesTextBox.Text = string.Empty;
                UnitLabelTextBox.Text = string.Empty;

                // Preview fields are only for testing formulas in the editor.
                // They are not saved to the OID.
                RawFormulaPreviewTextBox.Text = string.Empty;
                DisplayFormulaPreviewTextBox.Text = string.Empty;
                ReadFormulaPreviewTextBlock.Text = "—";
                WriteFormulaPreviewTextBlock.Text = "—";

                _decodeValues.Clear();
                RefreshDecodeGrid();
                ClearDecodeEditor();

                // Keep Raw / ValueMap / Formula UI states synced with the selected decode mode.
                UpdateDecodeModeUi();

                return;
            }

            WindowTitleTextBlock.Text = "Edit SNMP OID";

            SetComboText(CategoryComboBox, string.IsNullOrWhiteSpace(oid.Category) ? "Config" : oid.Category);
            LabelTextBox.Text = oid.Label;
            OidTextBox.Text = oid.Oid;
            SetComboText(ValueTypeComboBox, oid.ValueType);
            SortOrderTextBox.Text = oid.SortOrder.ToString();
            WritableCheckBox.IsChecked = oid.IsWritable;
            ShowInWorkspaceCheckBox.IsChecked = oid.ShowInWorkspace;

            SetComboText(DecodeModeComboBox, string.IsNullOrWhiteSpace(oid.DecodeMode) ? "Raw" : oid.DecodeMode);
            ShowRawAlongsideDecodedCheckBox.IsChecked = oid.ShowRawValueAlongsideDecoded;

            // Load formula settings saved on this OID.
            // Different radio profiles can use different formulas.
            ReadFormulaTextBox.Text = oid.ReadFormula ?? string.Empty;
            WriteFormulaTextBox.Text = oid.WriteFormula ?? string.Empty;
            DecimalPlacesTextBox.Text = oid.DecimalPlaces?.ToString() ?? string.Empty;
            UnitLabelTextBox.Text = oid.UnitLabel ?? string.Empty;

            // Keep preview inputs blank when opening an existing OID.
            // The admin can type sample values without changing the saved OID.
            RawFormulaPreviewTextBox.Text = string.Empty;
            DisplayFormulaPreviewTextBox.Text = string.Empty;
            ReadFormulaPreviewTextBlock.Text = "—";
            WriteFormulaPreviewTextBlock.Text = "—";

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
            ClearDecodeEditor();
            UpdateDecodeModeUi();
        }

        private void HookEvents()
        {
            AddDecodeRowButton.Click += AddDecodeRowButton_Click;
            RemoveDecodeRowButton.Click += RemoveDecodeRowButton_Click;
            ApplyDecodeRowButton.Click += ApplyDecodeRowButton_Click;
            DecodeValuesDataGrid.SelectionChanged += DecodeValuesDataGrid_SelectionChanged;
            DecodeModeComboBox.SelectionChanged += DecodeModeComboBox_SelectionChanged;

            // Formula preview updates live as the admin types.
            // This lets us confirm radio raw value -> display value,
            // and display/user value -> raw SET value before saving the OID.
            ReadFormulaTextBox.TextChanged += FormulaPreviewChanged;
            WriteFormulaTextBox.TextChanged += FormulaPreviewChanged;
            DecimalPlacesTextBox.TextChanged += FormulaPreviewChanged;
            UnitLabelTextBox.TextChanged += FormulaPreviewChanged;
            RawFormulaPreviewTextBox.TextChanged += FormulaPreviewChanged;
            DisplayFormulaPreviewTextBox.TextChanged += FormulaPreviewChanged;

            SaveButtonEx.Click += SaveButtonEx_Click;
            CancelButtonEx.Click += CancelButtonEx_Click;
        }

        private void SetDefaults()
        {
            SetComboText(CategoryComboBox, "Config");
            SetComboText(ValueTypeComboBox, "String");
            SetComboText(DecodeModeComboBox, "Raw");
            SortOrderTextBox.Text = "10";
            ShowInWorkspaceCheckBox.IsChecked = true;
            DecodeSortOrderTextBox.Text = "0";

            // Formula decoder defaults.
            // These are disabled unless Decode Mode is Formula.
            ReadFormulaTextBox.Text = string.Empty;
            WriteFormulaTextBox.Text = string.Empty;
            DecimalPlacesTextBox.Text = string.Empty;
            UnitLabelTextBox.Text = string.Empty;

            // Preview fields are only for testing formulas in the editor.
            // They are not saved to the OID.
            RawFormulaPreviewTextBox.Text = string.Empty;
            DisplayFormulaPreviewTextBox.Text = string.Empty;
            ReadFormulaPreviewTextBlock.Text = "—";
            WriteFormulaPreviewTextBlock.Text = "—";

            UpdateDecodeModeUi();
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

            // Decode mode controls whether Raw, ValueMap, or Formula behavior is used.
            var decodeMode = GetComboText(DecodeModeComboBox, "Raw");

            // Value type is important because Formula mode expects a numeric integer raw value.
            var valueType = GetComboText(ValueTypeComboBox, "String");

            // Formula values are configured per OID.
            // ReadFormula: raw radio value -> display value.
            // WriteFormula: displayed/user-entered value -> raw radio SET value.
            var readFormula = (ReadFormulaTextBox.Text ?? string.Empty).Trim();
            var writeFormula = (WriteFormulaTextBox.Text ?? string.Empty).Trim();
            var unitLabel = (UnitLabelTextBox.Text ?? string.Empty).Trim();

            int? decimalPlaces = null;

            if (!string.IsNullOrWhiteSpace(DecimalPlacesTextBox.Text))
            {
                if (!int.TryParse(DecimalPlacesTextBox.Text.Trim(), out var parsedDecimalPlaces) ||
                    parsedDecimalPlaces < 0 ||
                    parsedDecimalPlaces > 10)
                {
                    MessageBox.Show(
                        "Decimals must be a whole number between 0 and 10.",
                        "SNMP OID Formula Decoder",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    DecimalPlacesTextBox.Focus();
                    return;
                }

                decimalPlaces = parsedDecimalPlaces;
            }

            if (string.Equals(decodeMode, "Formula", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(valueType, "Integer", StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(
                        "Formula Decode can only be used with Integer OIDs.",
                        "SNMP OID Formula Decoder",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    ValueTypeComboBox.Focus();
                    return;
                }

                if (!SnmpFormulaEvaluator.IsValidFormula(readFormula))
                {
                    MessageBox.Show(
                        "Read Formula is required and must use x.\n\nExamples:\nx / 100000\nx / 10\n(x / 10) + 2",
                        "SNMP OID Formula Decoder",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    ReadFormulaTextBox.Focus();
                    return;
                }

                if (WritableCheckBox.IsChecked == true &&
                    !SnmpFormulaEvaluator.IsValidFormula(writeFormula))
                {
                    MessageBox.Show(
                        "Writable Formula OIDs need a valid Write Formula so the displayed value can be converted back to a whole-number radio SET value.\n\nExamples:\nx * 100000\nx * 10",
                        "SNMP OID Formula Decoder",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    WriteFormulaTextBox.Focus();
                    return;
                }

                if (!string.IsNullOrWhiteSpace(writeFormula) &&
                    !SnmpFormulaEvaluator.IsValidFormula(writeFormula))
                {
                    MessageBox.Show(
                        "Write Formula must use x and contain only safe math.\n\nExamples:\nx * 100000\nx * 10",
                        "SNMP OID Formula Decoder",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    WriteFormulaTextBox.Focus();
                    return;
                }
            }
            else
            {
                // Keep non-formula OIDs clean so old Raw/ValueMap behavior stays simple.
                readFormula = string.Empty;
                writeFormula = string.Empty;
                decimalPlaces = null;
                unitLabel = string.Empty;
            }

            Result = new SnmpOidConfigDto
            {
                Category = GetComboText(CategoryComboBox, "Config"),
                Label = label,
                Oid = oid,
                ValueType = valueType,
                IsWritable = WritableCheckBox.IsChecked == true,
                ShowInWorkspace = ShowInWorkspaceCheckBox.IsChecked == true,
                SortOrder = sortOrder,
                DecodeMode = decodeMode,

                // Formula decoder config.
                // Stored per OID so every radio/profile can use its own scaling.
                ReadFormula = string.IsNullOrWhiteSpace(readFormula) ? null : readFormula,
                WriteFormula = string.IsNullOrWhiteSpace(writeFormula) ? null : writeFormula,
                DecimalPlaces = decimalPlaces,
                UnitLabel = string.IsNullOrWhiteSpace(unitLabel) ? null : unitLabel,
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

        private void SetDecodeUiEnabled(bool enabled)
        {
            if (DecodeValuesBorder is null)
                return;

            DecodeValuesBorder.IsEnabled = enabled;
            DecodeValuesBorder.Opacity = enabled ? 1.0 : 0.55;
        }

        private void DecodeModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateDecodeModeUi();
        }

        private void UpdateDecodeModeUi()
        {
            var mode = GetComboText(DecodeModeComboBox, "Raw");

            var isValueMap = string.Equals(
                mode,
                "ValueMap",
                StringComparison.OrdinalIgnoreCase);

            var isFormula = string.Equals(
                mode,
                "Formula",
                StringComparison.OrdinalIgnoreCase);

            // ValueMap rows only matter for ValueMap mode.
            SetDecodeUiEnabled(isValueMap);

            // Formula fields and preview only matter for Formula mode.
            SetFormulaDecodeUiEnabled(isFormula);

            UpdateFormulaPreview();
        }

        private void SetFormulaDecodeUiEnabled(bool enabled)
        {
            if (FormulaDecodeBorder is null)
                return;

            FormulaDecodeBorder.IsEnabled = enabled;
            FormulaDecodeBorder.Opacity = enabled ? 1.0 : 0.55;
        }

        private void FormulaPreviewChanged(object sender, TextChangedEventArgs e)
        {
            UpdateFormulaPreview();
        }

        private void UpdateFormulaPreview()
        {
            if (ReadFormulaPreviewTextBlock is null ||
                WriteFormulaPreviewTextBlock is null)
            {
                return;
            }

            var decodeMode = GetComboText(DecodeModeComboBox, "Raw");

            if (!string.Equals(decodeMode, "Formula", StringComparison.OrdinalIgnoreCase))
            {
                ReadFormulaPreviewTextBlock.Text = "Formula mode not selected.";
                WriteFormulaPreviewTextBlock.Text = "Formula mode not selected.";
                return;
            }

            UpdateReadFormulaPreview();
            UpdateWriteFormulaPreview();
        }

        private void UpdateReadFormulaPreview()
        {
            var rawSample = (RawFormulaPreviewTextBox.Text ?? string.Empty).Trim();
            var readFormula = (ReadFormulaTextBox.Text ?? string.Empty).Trim();
            var unitLabel = (UnitLabelTextBox.Text ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(rawSample))
            {
                ReadFormulaPreviewTextBlock.Text = "Enter a raw sample.";
                return;
            }

            if (string.IsNullOrWhiteSpace(readFormula))
            {
                ReadFormulaPreviewTextBlock.Text = "Enter a Read Formula.";
                return;
            }

            if (!decimal.TryParse(
                    rawSample,
                    System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var rawNumber))
            {
                ReadFormulaPreviewTextBlock.Text = "Raw sample must be numeric.";
                return;
            }

            if (!SnmpFormulaEvaluator.TryEvaluate(readFormula, rawNumber, out var decodedNumber))
            {
                ReadFormulaPreviewTextBlock.Text = "Invalid Read Formula.";
                return;
            }

            var displayText = FormatFormulaNumber(decodedNumber);

            if (!string.IsNullOrWhiteSpace(unitLabel))
                displayText += " " + unitLabel;

            ReadFormulaPreviewTextBlock.Text = displayText;
        }

        private void UpdateWriteFormulaPreview()
        {
            var displaySample = (DisplayFormulaPreviewTextBox.Text ?? string.Empty).Trim();
            var writeFormula = (WriteFormulaTextBox.Text ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(displaySample))
            {
                WriteFormulaPreviewTextBlock.Text = "Enter a display sample.";
                return;
            }

            if (string.IsNullOrWhiteSpace(writeFormula))
            {
                WriteFormulaPreviewTextBlock.Text = "Enter a Write Formula.";
                return;
            }

            if (!SnmpFormulaEvaluator.TryBuildWriteValue(
                    displaySample,
                    writeFormula,
                    out var rawWriteValue))
            {
                WriteFormulaPreviewTextBlock.Text = "Invalid Write Formula.";
                return;
            }

            WriteFormulaPreviewTextBlock.Text = rawWriteValue;
        }

        private string FormatFormulaNumber(decimal value)
        {
            if (!string.IsNullOrWhiteSpace(DecimalPlacesTextBox.Text))
            {
                if (int.TryParse(DecimalPlacesTextBox.Text.Trim(), out var decimalPlaces) &&
                    decimalPlaces >= 0 &&
                    decimalPlaces <= 10)
                {
                    return Math.Round(value, decimalPlaces, MidpointRounding.AwayFromZero)
                        .ToString(
                            $"F{decimalPlaces}",
                            System.Globalization.CultureInfo.InvariantCulture);
                }
            }

            return value.ToString(
                "0.##########",
                System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}