using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace GsCan.View
{
    public sealed class KindBrushConverter : IValueConverter
    {
        public static readonly IBrush Rx = Brush("#0F766E");
        public static readonly IBrush Echo = Brush("#1D4ED8");
        public static readonly IBrush Error = Brush("#DC2626");
        public static readonly IBrush Neutral = Brush("#64748B");

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return (value as string) switch
            {
                "Rx" => Rx,
                "Echo" => Echo,
                "Error" => Error,
                _ => Neutral
            };
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }

        internal static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));
    }

    public sealed class ChannelBrushConverter : IValueConverter
    {
        public static readonly IBrush Channel0 = KindBrushConverter.Brush("#2563EB");
        public static readonly IBrush Channel1 = KindBrushConverter.Brush("#7C3AED");
        public static readonly IBrush Neutral = KindBrushConverter.Neutral;

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is int channel
                ? channel == 0 ? Channel0 : channel == 1 ? Channel1 : Neutral
                : Neutral;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
