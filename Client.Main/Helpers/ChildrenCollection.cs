using System;
using System.Collections.Generic;
using System.Threading;

namespace Client.Main.Helpers
{
    public interface IChildItem<T> where T : class
    {
        T Parent { get; set; }
    }

    public class ChildrenEventArgs<T> where T : class, IChildItem<T>
    {
        public T Control { get; }

        public ChildrenEventArgs(T control)
        {
            Control = control;
        }
    }

    public class ChildrenCollection<T> : ICollection<T> where T : class, IChildItem<T>
    {
        private List<T> _controls = new List<T>();
        private volatile bool _snapshotDirty = true;
        private T[] _snapshot = Array.Empty<T>();
        private int _count;
        private readonly object _lock = new object();

        public T Parent { get; private set; }
        public int Count => Volatile.Read(ref _count);
        public bool IsReadOnly => false;

        public event EventHandler<ChildrenEventArgs<T>> ControlAdded;
        public event EventHandler<ChildrenEventArgs<T>> ControlRemoved;

        internal ChildrenCollection(T parent)
        {
            Parent = parent;
        }

        /// <summary>
        /// Returns a snapshot of the collection without taking a lock during iteration.
        /// Snapshot is refreshed only when the collection changes.
        /// </summary>
        public IReadOnlyList<T> GetSnapshot() => GetSnapshotArray();

        public T this[int index]
        {
            get
            {
                lock (_lock)
                {
                    return _controls[index];
                }
            }
            set => throw new NotImplementedException("Not implemented set index in ChildrenCollection");
        }

        public int IndexOf(T control)
        {
            return Array.IndexOf(GetSnapshotArray(), control);
        }

        public bool MoveToEnd(T control)
        {
            lock (_lock)
            {
                int index = _controls.IndexOf(control);
                if (index < 0 || index == _controls.Count - 1)
                    return false;

                _controls.RemoveAt(index);
                _controls.Add(control);
                InvalidateSnapshot();
                return true;
            }
        }

        public bool MoveToStart(T control)
        {
            lock (_lock)
            {
                int index = _controls.IndexOf(control);
                if (index <= 0)
                    return false;

                _controls.RemoveAt(index);
                _controls.Insert(0, control);
                InvalidateSnapshot();
                return true;
            }
        }

        // Add a strongly-typed Add for IChildItem<T> controls
        public void Add(T control)
        {
            lock (_lock)
            {
                control.Parent = Parent;
                _controls.Add(control);
                InvalidateSnapshot();
                Volatile.Write(ref _count, _controls.Count);
            }
            ControlAdded?.Invoke(this, new ChildrenEventArgs<T>(control));
        }

        internal void Add(object value)
        {
            if (value is T control)
            {
                Add(control);
            }
            else
            {
                throw new ArgumentException($"Value must be of type {typeof(T).Name}");
            }
        }

        public bool Detach(T control)
        {
            bool removed;
            lock (_lock)
            {
                removed = _controls.Remove(control);
                if (removed)
                {
                    InvalidateSnapshot();
                    Volatile.Write(ref _count, _controls.Count);
                }
            }

            if (removed)
            {
                control.Parent = null;
                ControlRemoved?.Invoke(this, new ChildrenEventArgs<T>(control));
            }
            return removed;
        }

        public void Insert(int index, T control)
        {
            lock (_lock)
            {
                control.Parent = Parent;
                _controls.Insert(index, control);
                InvalidateSnapshot();
                Volatile.Write(ref _count, _controls.Count);
            }

            ControlAdded?.Invoke(this, new ChildrenEventArgs<T>(control));
        }

        public T[] ToArray()
        {
            lock (_lock)
            {
                return _controls.ToArray();
            }
        }

        public Enumerator GetEnumerator() => new(GetSnapshotArray());

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => new Enumerator(GetSnapshotArray());
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            new Enumerator(GetSnapshotArray());

        public struct Enumerator : IEnumerator<T>
        {
            private readonly T[] _items;
            private int _index;

            internal Enumerator(T[] items)
            {
                _items = items ?? Array.Empty<T>();
                _index = -1;
            }

            public T Current => _items[_index];
            object System.Collections.IEnumerator.Current => Current;

            public bool MoveNext()
            {
                int next = _index + 1;
                if ((uint)next >= (uint)_items.Length)
                    return false;

                _index = next;
                return true;
            }

            public void Reset() => _index = -1;
            public void Dispose() { }
        }

        public bool Contains(T item)
        {
            return Array.IndexOf(GetSnapshotArray(), item) >= 0;
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            lock (_lock)
            {
                _controls.CopyTo(array, arrayIndex);
            }
        }

        public bool Remove(T control)
        {
            bool removed;
            lock (_lock)
            {
                removed = _controls.Remove(control);
                if (removed)
                {
                    InvalidateSnapshot();
                    Volatile.Write(ref _count, _controls.Count);
                }
            }

            if (removed)
            {
                control.Parent = null;
                ControlRemoved?.Invoke(this, new ChildrenEventArgs<T>(control));
            }

            return removed;
        }

        public void RemoveAt(int index)
        {
            T control;
            lock (_lock)
            {
                control = _controls[index];
                _controls.RemoveAt(index);
                InvalidateSnapshot();
                Volatile.Write(ref _count, _controls.Count);
            }

            control.Parent = null;
            ControlRemoved?.Invoke(this, new ChildrenEventArgs<T>(control));
        }

        public void Clear()
        {
            T[] controls;
            lock (_lock)
            {
                controls = _controls.ToArray();
                _controls.Clear();
                InvalidateSnapshot();
                Volatile.Write(ref _count, 0);
            }

            foreach (var control in controls)
            {
                control.Parent = null;
                ControlRemoved?.Invoke(this, new ChildrenEventArgs<T>(control));
            }
        }

        bool ICollection<T>.Remove(T control)
        {
            return this.Remove(control);
        }

        private void InvalidateSnapshot() => _snapshotDirty = true;

        internal T[] GetSnapshotArray()
        {
            // Collection mutations publish _snapshotDirty through a volatile write. The
            // immutable cached array can therefore be returned without entering the lock
            // during the steady state, which is the common Update/Draw path.
            if (!_snapshotDirty)
                return _snapshot;

            lock (_lock)
            {
                if (_snapshotDirty)
                {
                    _snapshot = _controls.Count == 0
                        ? Array.Empty<T>()
                        : _controls.ToArray(); // never mutate a snapshot already used by a traversal
                    _snapshotDirty = false;
                }

                return _snapshot;
            }
        }
    }
}
