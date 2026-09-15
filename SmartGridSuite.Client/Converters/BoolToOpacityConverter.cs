using System;
using System.Globalization;
using System.Windows.Data;

namespace SmartGridSuite.Client.Converters
{
    public class BoolToOpacityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                if (value is bool b && b)
                    return 1.0;
            }
            catch { }

            return 0.45;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
