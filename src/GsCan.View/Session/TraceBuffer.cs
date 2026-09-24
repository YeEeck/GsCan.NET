using System.Collections.ObjectModel;

namespace GsCan.View.Session
{
    internal sealed class TraceBuffer
    {
        public const int Capacity = 100_000;

        private readonly ObservableCollection<FrameRow> _rows;

        public TraceBuffer(ObservableCollection<FrameRow> rows)
        {
            _rows = rows;
        }

        public void Append(FrameRow row)
        {
            if (_rows.Count >= Capacity)
            {
                _rows.RemoveAt(0);
            }

            _rows.Add(row);
        }

        public void Clear()
        {
            _rows.Clear();
        }
    }
}
