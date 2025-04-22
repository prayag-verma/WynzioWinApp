using System;
using System.Net.NetworkInformation;
using System.Threading;
using Serilog;

namespace Wynzio.Services.Network
{
    /// <summary>
    /// Implementation of network status monitoring service
    /// </summary>
    internal class NetworkStatusService : INetworkStatusService, IDisposable
    {
        private readonly ILogger _logger;
        private Timer? _checkTimer;
        private bool _isInternetAvailable;
        private const int CheckIntervalMs = 5000; // 5 seconds

        public bool IsInternetAvailable => _isInternetAvailable;

        public event EventHandler<NetworkStatusChangedEventArgs>? NetworkStatusChanged;

        public NetworkStatusService()
        {
            _logger = Log.ForContext<NetworkStatusService>();
            _isInternetAvailable = CheckInternetConnectivity();
        }

        /// <summary>
        /// Start monitoring network status
        /// </summary>
        public void StartMonitoring()
        {
            _checkTimer?.Dispose();
            _checkTimer = new Timer(CheckNetworkStatus, null, 0, CheckIntervalMs);
            _logger.Information("Network status monitoring started");
        }

        /// <summary>
        /// Stop monitoring network status
        /// </summary>
        public void StopMonitoring()
        {
            _checkTimer?.Dispose();
            _checkTimer = null;
            _logger.Information("Network status monitoring stopped");
        }

        /// <summary>
        /// Check network status and raise event if changed
        /// </summary>
        private void CheckNetworkStatus(object? state)
        {
            bool previousStatus = _isInternetAvailable;
            bool currentStatus = CheckInternetConnectivity();

            // Update status if changed
            if (previousStatus != currentStatus)
            {
                _isInternetAvailable = currentStatus;

                _logger.Information("Network connectivity status changed: {Status}",
                    currentStatus ? "Connected" : "Disconnected");

                // Raise event
                OnNetworkStatusChanged(new NetworkStatusChangedEventArgs(currentStatus));
            }
        }

        /// <summary>
        /// Check if internet connectivity is available
        /// </summary>
        private bool CheckInternetConnectivity()
        {
            try
            {
                // First check if network interface is available
                if (!NetworkInterface.GetIsNetworkAvailable())
                    return false;

                // Check if we can reach a known DNS server
                using var ping = new Ping();
                var reply = ping.Send("8.8.8.8", 2000); // Google DNS with 2 second timeout

                return reply.Status == IPStatus.Success;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Error checking internet connectivity");
                return false;
            }
        }

        /// <summary>
        /// Raise the NetworkStatusChanged event
        /// </summary>
        private void OnNetworkStatusChanged(NetworkStatusChangedEventArgs e)
        {
            NetworkStatusChanged?.Invoke(this, e);
        }

        /// <summary>
        /// Dispose resources
        /// </summary>
        public void Dispose()
        {
            _checkTimer?.Dispose();
        }
    }
}