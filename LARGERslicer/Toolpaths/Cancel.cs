using System;
using System.Threading;

namespace LARGERslicer.Toolpaths
{
    /// <summary>
    /// Stops long slicer work when nobody needs its result any more: Grasshopper 2 cancels a solution
    /// when an input changes, Grasshopper 1's background calculation cancels the old task. The heavy
    /// entry points (Slice, Walls, Ribs, Bottom, Build, Round) take an optional CancellationToken; it
    /// is kept here for the calling thread and the parallel loops it starts, and the loops call
    /// Check() now and then. A cancelled call ends with an OperationCanceledException (Walls returns
    /// null instead, as before). Without a token nothing changes: Check() costs a field read.
    /// </summary>
    public static class Cancel
    {
        private static readonly AsyncLocal<CancellationToken> Current = new AsyncLocal<CancellationToken>();

        /// <summary>The token of the running calculation (None outside one).</summary>
        public static CancellationToken Token => Current.Value;

        /// <summary>Makes token the current one until the returned scope is disposed. A token that can not
        /// be cancelled leaves an outer one in place (a nested call without token still stops).</summary>
        public static IDisposable Use(CancellationToken token)
        {
            if (!token.CanBeCanceled) return Scope.None;
            var old = Current.Value;
            Current.Value = token;
            return new Scope(old);
        }

        /// <summary>Throws OperationCanceledException when the current calculation was cancelled.</summary>
        public static void Check()
        {
            var t = Current.Value;
            if (t.IsCancellationRequested) throw new OperationCanceledException(t);
        }

        /// <summary>True when the current calculation was cancelled.</summary>
        public static bool Requested => Current.Value.IsCancellationRequested;

        private sealed class Scope : IDisposable
        {
            public static readonly IDisposable None = new Scope(default) { _done = true };
            private readonly CancellationToken _old;
            private bool _done;
            public Scope(CancellationToken old) { _old = old; }
            public void Dispose()
            {
                if (_done) return;
                _done = true;
                Current.Value = _old;
            }
        }
    }
}
