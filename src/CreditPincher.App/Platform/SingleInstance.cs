using System;
using System.Threading;

namespace CreditPincher.App.Platform
{
    /// <summary>
    /// Keeps exactly one tray icon alive. A second launch (from the Start menu, or from
    /// the "start with Windows" entry after a manual start) signals the running instance
    /// to show its dashboard and then exits.
    /// </summary>
    public sealed class SingleInstance : IDisposable
    {
        private const string MutexName = @"Local\CreditPincher.SingleInstance";
        private const string SignalName = @"Local\CreditPincher.ShowDashboard";

        private readonly Mutex _mutex;
        private readonly EventWaitHandle _signal;
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private Thread _listener;

        public SingleInstance()
        {
            bool createdNew;
            _mutex = new Mutex(true, MutexName, out createdNew);
            IsFirstInstance = createdNew;

            bool signalCreated;
            _signal = new EventWaitHandle(false, EventResetMode.AutoReset, SignalName, out signalCreated);
        }

        /// <summary>Raised on a background thread when another launch asks us to surface.</summary>
        public event Action ShowRequested;

        public bool IsFirstInstance { get; private set; }

        /// <summary>Asks the already-running instance to open its dashboard.</summary>
        public void SignalExistingInstance()
        {
            _signal.Set();
        }

        public void StartListening()
        {
            if (!IsFirstInstance || _listener != null)
            {
                return;
            }

            _listener = new Thread(() =>
            {
                var handles = new WaitHandle[] { _signal, _cancellation.Token.WaitHandle };
                while (!_cancellation.IsCancellationRequested)
                {
                    if (WaitHandle.WaitAny(handles) == 0)
                    {
                        var handler = ShowRequested;
                        if (handler != null)
                        {
                            handler();
                        }
                    }
                }
            })
            {
                IsBackground = true,
                Name = "CreditPincher single-instance listener",
            };

            _listener.Start();
        }

        public void Dispose()
        {
            _cancellation.Cancel();

            if (IsFirstInstance)
            {
                try
                {
                    _mutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    // Already released, or never owned; nothing to clean up.
                }
            }

            _mutex.Dispose();
            _signal.Dispose();
            _cancellation.Dispose();
        }
    }
}
