using System;
using System.Globalization;
using System.Windows.Data;

namespace SmartGridSuite.Client.Views
{
    public class TaskCountCapConverter : IValueConverter
    {
        public int Cap { get; set; } = 99;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int i)
            {
                if (i <= Cap) return i.ToString();
                return $"{Cap}+";
            }

            return "0";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
