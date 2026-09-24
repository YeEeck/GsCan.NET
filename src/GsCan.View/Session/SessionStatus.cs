using System.ComponentModel;
using GsCan;

namespace GsCan.View.Session
{
    public sealed class SessionStatus : INotifyPropertyChanged
    {
        private readonly object _gate = new object();
        private int _rxCount;
        private int _txCount;
        private int _errorCount;
        private int _overflowCount;
        private bool _dirty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public int RxCount
        {
            get { lock (_gate) { return _rxCount; } }
        }

        public int TxCount
        {
            get { lock (_gate) { return _txCount; } }
        }

        public int ErrorCount
        {
            get { lock (_gate) { return _errorCount; } }
        }

        public int OverflowCount
        {
            get { lock (_gate) { return _overflowCount; } }
        }

        internal void Observe(CanFrame frame)
        {
            lock (_gate)
            {
                switch (frame.Kind)
                {
                    case CanFrameKind.Echo:
                        _txCount++;
                        break;
                    case CanFrameKind.Error:
                        _errorCount++;
                        break;
                    default:
                        _rxCount++;
                        break;
                }

                if (frame.Overflow)
                {
                    _overflowCount++;
                }

                _dirty = true;
            }
        }

        internal void Publish()
        {
            lock (_gate)
            {
                if (!_dirty)
                {
                    return;
                }

                _dirty = false;
            }

            Raise(nameof(RxCount));
            Raise(nameof(TxCount));
            Raise(nameof(ErrorCount));
            Raise(nameof(OverflowCount));
        }

        private void Raise(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
