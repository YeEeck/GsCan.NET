using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace GsCan.View.Session
{
    internal sealed class TraceLogWriter
    {
        public void Write(string path, IEnumerable<FrameRow> rows)
        {
            using var writer = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.WriteLine("TimestampMicroseconds,Channel,Kind,Id,Extended,Remote,IsFd,BitRateSwitch,ErrorStateIndicator,Overflow,Length,Data");
            foreach (var row in rows)
            {
                writer.Write(row.TimestampMicroseconds.ToString(CultureInfo.InvariantCulture));
                writer.Write(',');
                writer.Write(row.Channel.ToString(CultureInfo.InvariantCulture));
                writer.Write(',');
                writer.Write(row.Kind);
                writer.Write(',');
                writer.Write(row.Id.ToString(CultureInfo.InvariantCulture));
                writer.Write(',');
                writer.Write(row.Extended);
                writer.Write(',');
                writer.Write(row.Remote);
                writer.Write(',');
                writer.Write(row.IsFd);
                writer.Write(',');
                writer.Write(row.BitRateSwitch);
                writer.Write(',');
                writer.Write(row.ErrorStateIndicator);
                writer.Write(',');
                writer.Write(row.Overflow);
                writer.Write(',');
                writer.Write(row.Length.ToString(CultureInfo.InvariantCulture));
                writer.Write(',');
                writer.WriteLine(row.DataHex);
            }
        }
    }
}
