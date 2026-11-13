using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace LARGERslicer.Components
{
    /// <summary>
    /// What a calculation in the background hands back: the outputs, the messages and the text under
    /// the component. It is filled on the worker thread and played back on the UI thread, so the
    /// calculation itself never touches Grasshopper.
    /// </summary>
    public sealed class AsyncJob
    {
        internal readonly List<KeyValuePair<int, object>> Items = new List<KeyValuePair<int, object>>();
        internal readonly List<KeyValuePair<int, List<object>>> Lists = new List<KeyValuePair<int, List<object>>>();
        internal readonly List<KeyValuePair<GH_RuntimeMessageLevel, string>> Messages = new List<KeyValuePair<GH_RuntimeMessageLevel, string>>();
        internal readonly List<Action> Apply = new List<Action>();
        internal Exception Error;

        public AsyncJob(CancellationToken token) { Token = token; }

        /// <summary>Set when the inputs changed meanwhile: the result is not needed any more.</summary>
        public CancellationToken Token { get; }

        /// <summary>The text under the component (null = leave it).</summary>
        public string Message;

        public void SetData(int index, object value) { lock (Items) Items.Add(new KeyValuePair<int, object>(index, value)); }

        /// <summary>The list is read right away (on the worker thread), not later on the UI thread.</summary>
        public void SetDataList(int index, IEnumerable values)
        {
            var list = values == null ? new List<object>() : values.Cast<object>().ToList();
            lock (Lists) Lists.Add(new KeyValuePair<int, List<object>>(index, list));
        }

        public void AddRuntimeMessage(GH_RuntimeMessageLevel level, string text) { lock (Messages) Messages.Add(new KeyValuePair<GH_RuntimeMessageLevel, string>(level, text)); }

        /// <summary>Something to do on the UI thread when the result is handed out (e.g. fill a preview list).</summary>
        public void OnApply(Action action) { if (action != null) lock (Apply) Apply.Add(action); }
    }

    /// <summary>
    /// Long calculations in the background, so the canvas stays usable (as in the IGA Toolkit).
    /// <para>How a component uses it, in SolveInstance:</para>
    /// <code>
    /// if (Async.Deliver(da)) return;            // the finished result, handed out
    /// ... read all inputs here, on the UI thread, and copy geometry ...
    /// Async.Run(da, job => { ... calculate, job.SetData(...), job.AddRuntimeMessage(...) ... });
    /// </code>
    /// <para>Run starts a task with the copied inputs; the component shows "computing 3.2 s" and its
    /// outputs stay empty meanwhile. When the inputs change, a new task starts and the old result is
    /// thrown away (its token is cancelled). When all tasks of a solution are done, the component
    /// expires itself on the UI thread and the next solution hands out the stored results. Exceptions
    /// become error messages. With Background off, Run calculates right away as before.</para>
    /// </summary>
    public sealed class AsyncSolve
    {
        private readonly GH_Component _owner;
        private readonly object _lock = new object();
        private int _generation;
        private bool _deliver, _selfExpire, _dirty;
        private Dictionary<int, AsyncJob> _done = new Dictionary<int, AsyncJob>();
        private int _started, _pending;
        private readonly Stopwatch _clock = new Stopwatch();
        private CancellationTokenSource _cts;
        private Timer _timer;

        /// <summary>Calculate in the background (default on; kept in the file).</summary>
        public bool Enabled { get; set; } = true;

        public AsyncSolve(GH_Component owner) { _owner = owner; }

        /// <summary>A row for the component: Background: on / off.</summary>
        public OptionRow Row() => new OptionRow
        {
            Kind = RowKind.Toggle, Label = "Background", Input = -1,
            GetSetting = () => Enabled,
            SetSetting = v => Enabled = v,
        };

        public void AppendMenu(System.Windows.Forms.ToolStripDropDown menu)
        {
            GH_DocumentObject.Menu_AppendItem(menu, "Calculate in the background", (s, e) =>
            {
                _owner.RecordUndoEvent("Background");
                Enabled = !Enabled;
                _owner.Attributes?.ExpireLayout();
                _owner.ExpireSolution(true);
            }, true, Enabled).ToolTipText = "On: long calculations run beside the canvas, which stays usable; the component shows how long it has been working. Off: Grasshopper waits for the result.";
        }

        public void Write(GH_IO.Serialization.GH_IWriter writer) => writer.SetBoolean("LARGER.Background", Enabled);

        public void Read(GH_IO.Serialization.GH_IReader reader)
        {
            if (reader.ItemExists("LARGER.Background")) Enabled = reader.GetBoolean("LARGER.Background");
        }

        // ------------------------------------------------------------ the solution

        /// <summary>BeforeSolveInstance: a new solution either hands out finished results or starts over.</summary>
        public void Before()
        {
            if (_deliver) return;
            lock (_lock)
            {
                CancelTasks();
                _generation++;
                _done = new Dictionary<int, AsyncJob>();
                _started = 0; _pending = 0; _dirty = false;
                _cts = new CancellationTokenSource();
                _clock.Restart();
            }
        }

        /// <summary>True when this solution hands out finished results: then SolveInstance returns right away.</summary>
        public bool Deliver(IGH_DataAccess da)
        {
            if (!_deliver) return false;
            AsyncJob job;
            lock (_lock) _done.TryGetValue(da.Iteration, out job);
            if (job != null) Hand(job, da);
            return true;
        }

        /// <summary>Calculates in the background (or right away with Background off). The work must not touch
        /// Grasshopper or the component: everything goes through the job.</summary>
        public void Run(IGH_DataAccess da, Action<AsyncJob> work)
        {
            if (!Enabled || _cts == null)
            {
                var now = new AsyncJob(CancellationToken.None);
                try { work(now); } catch (Exception e) { now.Error = e; }
                Hand(now, da);
                return;
            }
            int iteration = da.Iteration, generation;
            CancellationToken token;
            lock (_lock) { generation = _generation; token = _cts.Token; _started++; _pending++; }
            var job = new AsyncJob(token);
            Task.Run(() =>
            {
                try { if (!token.IsCancellationRequested) work(job); }
                catch (Exception e) { job.Error = e; }
                Finished(generation, iteration, job);
            });
        }

        /// <summary>AfterSolveInstance.</summary>
        public void After()
        {
            if (_deliver)
            {
                _deliver = false;
                StopTimer();
                return;
            }
            bool running;
            lock (_lock) running = _pending > 0;
            if (running) { ShowClock(); StartTimer(); }
        }

        /// <summary>ExpireSolution: an input changed (unless the component expired itself to hand out its results).</summary>
        public void Expired()
        {
            if (_selfExpire || OptionAttributes.QuietReset) return;
            _deliver = false;
            _dirty = true;
        }

        /// <summary>The component left the document or the document closes: forget the work.</summary>
        public void Cancel()
        {
            lock (_lock) { CancelTasks(); _generation++; _pending = 0; }
            StopTimer();
            _deliver = false;
        }

        private void CancelTasks()
        {
            try { _cts?.Cancel(); } catch { }
            _cts = null;
        }

        private void Finished(int generation, int iteration, AsyncJob job)
        {
            lock (_lock)
            {
                if (generation != _generation) return; // the inputs changed meanwhile
                _done[iteration] = job;
                _pending--;
                if (_pending > 0) return;
            }
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() => Ready(generation)));
        }

        /// <summary>UI thread: all tasks of this solution are done; one more (short) solution hands them out.</summary>
        private void Ready(int generation)
        {
            if (generation != _generation) return;
            StopTimer();
            var doc = _owner.OnPingDocument();
            if (doc == null) return;
            doc.ScheduleSolution(1, d =>
            {
                // an input changed after the work started: that solution starts the work again instead
                if (generation != _generation || _dirty) return;
                _deliver = true;
                _selfExpire = true;
                try { _owner.ExpireSolution(false); }
                finally { _selfExpire = false; }
            });
        }

        private void Hand(AsyncJob job, IGH_DataAccess da)
        {
            if (job.Error != null)
            {
                var e = job.Error is AggregateException ae && ae.InnerException != null ? ae.InnerException : job.Error;
                _owner.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The calculation failed: " + e.Message);
            }
            lock (job.Messages) foreach (var m in job.Messages) _owner.AddRuntimeMessage(m.Key, m.Value);
            lock (job.Items) foreach (var kv in job.Items) da.SetData(kv.Key, kv.Value);
            lock (job.Lists) foreach (var kv in job.Lists) da.SetDataList(kv.Key, kv.Value);
            lock (job.Apply) foreach (var a in job.Apply) { try { a(); } catch (Exception e) { _owner.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, e.Message); } }
            if (job.Message != null) _owner.Message = job.Message;
            else if (_owner.Message != null && _owner.Message.StartsWith("computing", StringComparison.Ordinal)) _owner.Message = null;
        }

        // ------------------------------------------------------------ the clock on the component

        private void ShowClock()
        {
            int started, pending;
            lock (_lock) { started = _started; pending = _pending; }
            string count = started > 1 ? (started - pending) + "/" + started + " · " : "";
            _owner.Message = "computing " + count + _clock.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";
            _owner.Attributes?.ExpireLayout();
            Grasshopper.Instances.RedrawCanvas();
        }

        private void StartTimer()
        {
            StopTimer();
            int generation = _generation;
            _timer = new Timer(_ => Rhino.RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                bool running;
                lock (_lock) running = generation == _generation && _pending > 0;
                if (running && !_deliver) ShowClock();
            })), null, 200, 200);
        }

        private void StopTimer()
        {
            var t = _timer; _timer = null;
            try { t?.Dispose(); } catch { }
        }

        // ------------------------------------------------------------ copies of inputs

        /// <summary>A copy of a geometry input for the worker thread (geometry is duplicated, values pass through).</summary>
        public static object Copy(object o)
        {
            switch (o)
            {
                case Grasshopper.Kernel.Types.IGH_Goo goo: return Copy(goo.ScriptVariable());
                case GeometryBase g: return g.Duplicate();
                default: return o;
            }
        }

        public static List<object> Copy(IEnumerable<object> items) => items.Where(o => o != null).Select(Copy).ToList();
    }
}
