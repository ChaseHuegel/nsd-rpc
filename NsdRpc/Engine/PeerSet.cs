using System.Collections;

namespace NsdRpc;

/// <summary>
/// An immutable snapshot of peers. Iterating it does not allocate. The set
/// does not change when the live peers change. A default instance is empty.
/// </summary>
public readonly struct PeerSet : IReadOnlyList<RpcPeer>
{
    private static readonly RpcPeer[] Empty = [];

    private readonly RpcPeer[]? _peers;

    internal PeerSet(RpcPeer[] peers)
    {
        _peers = peers;
    }

    /// <summary>The number of peers in the snapshot.</summary>
    public int Count => _peers?.Length ?? 0;

    /// <summary>The peer at one index.</summary>
    public RpcPeer this[int index] => _peers![index];

    /// <summary>An allocation-free enumerator over the snapshot.</summary>
    public Enumerator GetEnumerator()
    {
        return new Enumerator(_peers ?? Empty);
    }

    IEnumerator<RpcPeer> IEnumerable<RpcPeer>.GetEnumerator()
    {
        return ((IEnumerable<RpcPeer>)(_peers ?? Empty)).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return (_peers ?? Empty).GetEnumerator();
    }

    /// <summary>An allocation-free enumerator over a <see cref="PeerSet"/>.</summary>
    public struct Enumerator
    {
        private readonly RpcPeer[] _peers;
        private int _index;

        internal Enumerator(RpcPeer[] peers)
        {
            _peers = peers;
            _index = -1;
        }

        /// <summary>The current peer.</summary>
        public RpcPeer Current => _peers[_index];

        /// <summary>Advances to the next peer.</summary>
        public bool MoveNext()
        {
            int next = _index + 1;
            if (next < _peers.Length)
            {
                _index = next;
                return true;
            }

            return false;
        }
    }
}
