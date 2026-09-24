using System;
using System.Globalization;
using Avalonia.Data.Converters;
using GsCan;

namespace GsCan.View
{
    public sealed class DeviceLabelConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var part = parameter as string;
            if (value is DeviceInfo info)
            {
                switch (part)
                {
                    case "title":
                        return DeviceLabel.Title(info);
                    case "detail":
                        return DeviceLabel.Detail(info);
                    case "channels":
                        return DeviceLabel.ChannelText(info);
                    default:
                        return DeviceLabel.Summary(info);
                }
            }

            if (value is string path)
            {
                switch (part)
                {
                    case "title":
                        return DeviceLabel.Title(path);
                    case "detail":
                        return DeviceLabel.Detail(path);
                    default:
                        return DeviceLabel.Summary(path, 0);
                }
            }

            return string.Empty;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
