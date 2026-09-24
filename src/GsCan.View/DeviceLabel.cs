using System;
using System.Globalization;
using GsCan;

namespace GsCan.View
{
    /// <summary>
    /// Human-readable Device list label derived from WinUSB Path + ChannelCount.
    /// Does not change GsCan's DeviceInfo.
    /// </summary>
    public static class DeviceLabel
    {
        public static string Title(DeviceInfo? info) => Title(info?.Path);

        public static string Title(string? path)
        {
            return HasVidPid(path) ? "gs_usb" : "gs_usb 设备";
        }

        public static string Detail(DeviceInfo? info) => Detail(info?.Path);

        public static string Detail(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            var vid = MatchToken(path, "vid_");
            var pid = MatchToken(path, "pid_");
            var instance = ShortInstance(path);
            if (vid != null && pid != null)
            {
                return string.IsNullOrEmpty(instance)
                    ? vid + ":" + pid
                    : vid + ":" + pid + " · " + instance;
            }

            return instance.Length > 0 ? instance : path;
        }

        public static string ChannelText(DeviceInfo? info)
        {
            return ChannelText(info?.ChannelCount ?? 0);
        }

        public static string ChannelText(int channelCount)
        {
            return channelCount.ToString(CultureInfo.InvariantCulture) + " 路通道";
        }

        public static string Summary(DeviceInfo? info)
        {
            if (info == null)
            {
                return string.Empty;
            }

            return Summary(info.Path, info.ChannelCount);
        }

        public static string Summary(string? path, int channelCount)
        {
            var title = Title(path);
            var detail = Detail(path);
            var channels = ChannelText(channelCount);
            if (string.IsNullOrEmpty(detail) || string.Equals(detail, path, StringComparison.Ordinal))
            {
                return title + " · " + channels;
            }

            return title + " · " + channels + " · " + detail;
        }

        private static bool HasVidPid(string? path)
        {
            return MatchToken(path, "vid_") != null && MatchToken(path, "pid_") != null;
        }

        private static string? MatchToken(string? path, string prefix)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            var start = path.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                return null;
            }

            start += prefix.Length;
            if (start + 4 > path.Length)
            {
                return null;
            }

            var token = path.Substring(start, 4);
            for (int i = 0; i < 4; i++)
            {
                if (!IsHex(token[i]))
                {
                    return null;
                }
            }

            return token.ToUpperInvariant();
        }

        private static string ShortInstance(string? path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            var parts = path.Split('#');
            string? instance = null;
            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (part.Length == 0
                    || part.StartsWith("{", StringComparison.Ordinal)
                    || part.IndexOf("vid_", StringComparison.OrdinalIgnoreCase) >= 0
                    || part.IndexOf("usb", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }

                instance = part;
                break;
            }

            if (string.IsNullOrEmpty(instance))
            {
                return string.Empty;
            }

            var tokens = instance.Split('&');
            var best = instance;
            var bestLength = 0;
            for (int i = 0; i < tokens.Length; i++)
            {
                var token = tokens[i];
                if (token.Length >= 4 && token.Length > bestLength && IsHexString(token))
                {
                    best = token;
                    bestLength = token.Length;
                }
            }

            return best;
        }

        private static bool IsHexString(string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                if (!IsHex(value[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsHex(char c)
        {
            return (c >= '0' && c <= '9')
                || (c >= 'a' && c <= 'f')
                || (c >= 'A' && c <= 'F');
        }
    }
}
