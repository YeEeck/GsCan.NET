using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;

namespace GsCan.View.Session
{
    /// <summary>
    /// Arrival-ordered Trace rows with a fixed ring. Once full, new rows
    /// overwrite the oldest in O(batch) without copying the whole buffer.
    /// </summary>
    public sealed class TraceRows : IList<FrameRow>, IList, INotifyCollectionChanged, INotifyPropertyChanged
    {
        public const int DefaultCapacity = 100_000;

        public const int DisplayCapacity = 128;

        private static readonly NotifyCollectionChangedEventArgs ResetArgs =
            new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset);

        private static readonly PropertyChangedEventArgs CountArgs = new PropertyChangedEventArgs(nameof(Count));
        private static readonly PropertyChangedEventArgs IndexerArgs = new PropertyChangedEventArgs("Item[]");

        private readonly FrameRow[] _items;
        private int _head;
        private int _count;

        public TraceRows()
            : this(DefaultCapacity)
        {
        }

        public TraceRows(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _items = new FrameRow[capacity];
        }

        public int Capacity => _items.Length;

        public event NotifyCollectionChangedEventHandler? CollectionChanged;

        public event PropertyChangedEventHandler? PropertyChanged;

        public int Count => _count;

        public bool IsReadOnly => true;

        bool IList.IsReadOnly => true;

        bool IList.IsFixedSize => false;

        bool ICollection.IsSynchronized => false;

        object ICollection.SyncRoot => this;

        public FrameRow this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                return _items[Physical(index)];
            }
            set => throw new NotSupportedException();
        }

        object? IList.this[int index]
        {
            get => this[index];
            set => throw new NotSupportedException();
        }

        internal void Append(FrameRow row)
        {
            bool grew = _count < _items.Length;
            int addIndex = _count;
            Write(row);
            if (grew)
            {
                NotifyAdd(row, addIndex);
            }
            else
            {
                NotifyReset();
            }
        }

        internal void AppendMany(IReadOnlyList<FrameRow> rows)
        {
            if (rows == null || rows.Count == 0)
            {
                return;
            }

            int start = 0;
            int n = rows.Count;
            int cap = _items.Length;
            if (n > cap)
            {
                start = n - cap;
                n = cap;
            }

            bool wasFull = _count == cap;
            int grewFrom = _count;
            for (int i = 0; i < n; i++)
            {
                Write(rows[start + i]);
            }

            if (!wasFull && _count < cap && n <= 64)
            {
                for (int i = 0; i < n; i++)
                {
                    NotifyAdd(rows[start + i], grewFrom + i);
                }

                return;
            }

            NotifyReset();
        }

        internal void ClearRows()
        {
            if (_count == 0)
            {
                return;
            }

            Array.Clear(_items, 0, _items.Length);
            _head = 0;
            _count = 0;
            NotifyReset();
        }

        public int IndexOf(FrameRow item)
        {
            for (int i = 0; i < _count; i++)
            {
                if (Equals(_items[Physical(i)], item))
                {
                    return i;
                }
            }

            return -1;
        }

        public bool Contains(FrameRow item) => IndexOf(item) >= 0;

        public void CopyTo(FrameRow[] array, int arrayIndex)
        {
            if (array == null)
            {
                throw new ArgumentNullException(nameof(array));
            }

            for (int i = 0; i < _count; i++)
            {
                array[arrayIndex + i] = _items[Physical(i)];
            }
        }

        public IEnumerator<FrameRow> GetEnumerator()
        {
            for (int i = 0; i < _count; i++)
            {
                yield return _items[Physical(i)];
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void Add(FrameRow item) => throw new NotSupportedException();

        public void Insert(int index, FrameRow item) => throw new NotSupportedException();

        public void RemoveAt(int index) => throw new NotSupportedException();

        public bool Remove(FrameRow item) => throw new NotSupportedException();

        void ICollection<FrameRow>.Clear() => throw new NotSupportedException();

        int IList.Add(object? value) => throw new NotSupportedException();

        void IList.Clear() => throw new NotSupportedException();

        bool IList.Contains(object? value) => value is FrameRow row && Contains(row);

        int IList.IndexOf(object? value) => value is FrameRow row ? IndexOf(row) : -1;

        void IList.Insert(int index, object? value) => throw new NotSupportedException();

        void IList.Remove(object? value) => throw new NotSupportedException();

        void IList.RemoveAt(int index) => throw new NotSupportedException();

        void ICollection.CopyTo(Array array, int index)
        {
            if (array == null)
            {
                throw new ArgumentNullException(nameof(array));
            }

            for (int i = 0; i < _count; i++)
            {
                array.SetValue(_items[Physical(i)], index + i);
            }
        }

        private int Physical(int logical)
        {
            return (_head + logical) % _items.Length;
        }

        private void Write(FrameRow row)
        {
            int cap = _items.Length;
            if (_count < cap)
            {
                _items[Physical(_count)] = row;
                _count++;
                return;
            }

            _items[_head] = row;
            _head++;
            if (_head == cap)
            {
                _head = 0;
            }
        }

        private void NotifyAdd(FrameRow row, int index)
        {
            PropertyChanged?.Invoke(this, CountArgs);
            PropertyChanged?.Invoke(this, IndexerArgs);
            CollectionChanged?.Invoke(
                this,
                new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, row, index));
        }

        private void NotifyReset()
        {
            PropertyChanged?.Invoke(this, CountArgs);
            PropertyChanged?.Invoke(this, IndexerArgs);
            CollectionChanged?.Invoke(this, ResetArgs);
        }
    }
}
