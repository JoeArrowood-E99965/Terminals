using System;
using System.Net.Sockets;
using System.Threading;

using Terminals.Data;

namespace Terminals.Connections
{
    // ----------------------------------------------------
    /// <summary>
    ///     Checks, if the connection port is available. 
    ///     Simulates reconnect feature of the RDP client.
    ///     Does use the port scanner, so it needs 
    ///     administrative priviledges.
    ///     Is disposable because of used internal Timer.
    /// </summary>

    internal sealed class ConnectionStateDetector : IDisposable
    {
        /// -----------------------------------------------
        /// <summary>
        ///     Try reconnect max. 1 hour. Consider provide 
        ///     application configuration option for this value.
        /// </summary>

        private const int RECONNECT_MAX_DURATION = 1000 * 3600;

        /// -----------------------------------------------
        /// <summary>
        ///     Once per 20 seconds
        /// </summary>

        private const int TIMER_INTERVAL = 1000 * 20;

        private int _retriesCount;
        private readonly Timer _retriesTimer;
        private string _serverName;
        private int _port;

        private readonly object _activityLock = new object();
        private bool _disabled;
        private bool _isRunning;

        private readonly Action<string, int> _testAction;

        private readonly int _reconnectMaxDuration;

        private readonly int _timerInterval;

        // ------------------------------------------------

        internal bool IsRunning
        {
            get
            {
                lock(_activityLock)
                {
                    return _isRunning;
                }
            }
        }

        // ------------------------------------------------

        private bool CanTest
        {
            get
            {
                lock(_activityLock)
                {
                    return _isRunning && !_disabled;
                }
            }
        }

        /// -----------------------------------------------
        /// <summary>
        ///     Connection to the favorite target service 
        ///     should be available again
        /// </summary>

        internal event EventHandler Reconnected;

        /// -----------------------------------------------
        /// <summary>
        ///     Detector stoped to try reconnect, because 
        ///     maximum amount of retries exceeded.
        /// </summary>

        internal event EventHandler ReconnectExpired;

        // ------------------------------------------------

        internal ConnectionStateDetector() : this(TestAction, RECONNECT_MAX_DURATION, TIMER_INTERVAL)
        {
        }

        // ------------------------------------------------

        internal ConnectionStateDetector(Action<string, int> testAction, int reconnectMaxDuration, int timerInterval)
        {
            if(timerInterval <= 0)
            {
                throw new ArgumentOutOfRangeException("timerInterval", "Interval has to be non zero positive number.");
            }

            _testAction = testAction;
            _reconnectMaxDuration = reconnectMaxDuration;
            _timerInterval = timerInterval;
            _retriesTimer = new Timer(TryReconnection);
        }

        // ------------------------------------------------

        private void TryReconnection(object state)
        {
            if(!CanTest) { return; }

            _retriesCount++;
            bool success = TryReconnection();

            if(success)
            {
                ReportReconnected();
                return;
            }

            if(_retriesCount > (_reconnectMaxDuration / _timerInterval))
            {
                ReconnectionFail();
            }
        }

        // ------------------------------------------------

        private bool TryReconnection()
        {
            try
            {
                // simulate reconnect, cant use port scanned, because it requires admin priviledges

                _testAction(_serverName, _port);
                return true;
            }
            catch // exception is not necessary, simply is has to work
            {
                return false;
            }
        }

        // ------------------------------------------------

        private static void TestAction(string serverName, int port)
        {
            var portClient = new TcpClient(serverName, port);
        }

        // ------------------------------------------------

        private void ReconnectionFail()
        {
            if(ReconnectExpired != null)
            {
                ReconnectExpired(this, EventArgs.Empty);
            }
        }

        // ------------------------------------------------

        private void ReportReconnected()
        {
            if(Reconnected != null)
            {
                Reconnected(this, EventArgs.Empty);
            }
        }

        // ------------------------------------------------

        internal void AssignFavorite(IFavorite favorite)
        {
            _serverName = favorite.ServerName;
            _port = favorite.Port;
        }

        // ------------------------------------------------

        internal void Start()
        {
            lock(_activityLock)
            {
                if(_disabled) { return; }

                _isRunning = true;
                _retriesCount = 0;
                _retriesTimer.Change(0, _timerInterval);
            }
        }

        // ------------------------------------------------

        internal void Stop()
        {
            lock(_activityLock)
            {
                _isRunning = false;
                _retriesTimer.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }

        // ------------------------------------------------

        public void Dispose()
        {
            Disable();
            _retriesTimer.Dispose();
        }

        // ------------------------------------------------
        /// <summary>
        ///     Fill space between disconnected request from 
        ///     GUI and real disconnect of the client.
        /// </summary>

        private void Disable()
        {
            lock(_activityLock)
            {
                _disabled = true;
            }
        }

        // ------------------------------------------------

        public override string ToString()
        {
            return string.Format("ConnectionStateDetector:IsRunning={0},Disabled={1}", _isRunning, _disabled);
        }
    }
}