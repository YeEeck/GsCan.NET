using System;
using System.Globalization;
using Avalonia.Data.Converters;
using GsCan.View.Session;

namespace GsCan.View
{
    public sealed class BitrateLabelConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is int bitsPerSecond ? ChannelState.FormatBitrate(bitsPerSecond) : value;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
