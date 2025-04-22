using System;

namespace Wynzio.Services.Network
{
    /// <summary>
    /// Service for monitoring network connectivity status
    /// </summary>
    internal interface INetworkStatusService
    {
        /// <summary>
        /// Event triggered when network connectivity status changes
        /// </summary>
        event EventHandler<NetworkStatusChangedEventArgs> NetworkStatusChanged;

        /// <summary>
        /// Whether internet connectivity is currently available
        /// </summary>
        bool IsInternetAvailable { get; }

        /// <summary>
        /// Start monitoring network status
        /// </summary>
        void StartMonitoring();

        /// <summary>
        /// Stop monitoring network status
        /// </summary>
        void StopMonitoring();
    }

    /// <summary>
    /// Event arguments for network status change
    /// </summary>
    internal class NetworkStatusChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Whether internet connectivity is available
        /// </summary>
        public bool IsInternetAvailable { get; }

        public NetworkStatusChangedEventArgs(bool isInternetAvailable)
        {
            IsInternetAvailable = isInternetAvailable;
        }
    }
}