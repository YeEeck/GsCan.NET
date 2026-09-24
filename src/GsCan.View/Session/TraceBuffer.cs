using System.Collections.Generic;

namespace GsCan.View.Session
{
    internal sealed class TraceBuffer
    {
        public const int Capacity = 100_000;
        public const int DisplayCapacity = 128;

        private readonly TraceRows _rows;

        public TraceBuffer(TraceRows rows)
        {
            _rows = rows;
        }

        public void Append(FrameRow row)
        {
            _rows.Append(row);
        }

        public void AppendMany(IReadOnlyList<FrameRow> rows)
        {
            _rows.AppendMany(rows);
        }

        public void Clear()
        {
            _rows.ClearRows();
        }
    }
}
