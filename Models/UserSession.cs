using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Wynzio.Models
{
    /// <summary>
    /// Represents an active user session with connection details
    /// </summary>
    internal class UserSession : ObservableObject
    {
        private string _sessionId = string.Empty;
        private string _clientId = string.Empty;
        private string _clientIp = string.Empty;
        private DateTime _connectedAt = DateTime.Now;
        private bool _isActive = false;
        private bool _hasControlAccess = false;

        /// <summary>
        /// Unique session identifier
        /// </summary>
        public string SessionId
        {
            get => _sessionId;
            set => SetProperty(ref _sessionId, value);
        }

        /// <summary>
        /// Client's unique identifier
        /// </summary>
        public string ClientId
        {
            get => _clientId;
            set => SetProperty(ref _clientId, value);
        }

        /// <summary>
        /// IP address of connected client
        /// </summary>
        public string ClientIp
        {
            get => _clientIp;
            set => SetProperty(ref _clientIp, value);
        }

        /// <summary>
        /// Time when the connection was established
        /// </summary>
        public DateTime ConnectedAt
        {
            get => _connectedAt;
            set => SetProperty(ref _connectedAt, value);
        }

        /// <summary>
        /// Whether this session is currently active
        /// </summary>
        public bool IsActive
        {
            get => _isActive;
            set => SetProperty(ref _isActive, value);
        }

        /// <summary>
        /// Whether this session has remote control access
        /// </summary>
        public bool HasControlAccess
        {
            get => _hasControlAccess;
            set => SetProperty(ref _hasControlAccess, value);
        }

        /// <summary>
        /// Duration of the current session
        /// </summary>
        public TimeSpan SessionDuration => DateTime.Now - ConnectedAt;

        /// <summary>
        /// Creates a new user session with the specified client ID
        /// </summary>
        /// <param name="clientId">The client's unique identifier</param>
        /// <param name="clientIp">The client's IP address</param>
        public UserSession(string clientId, string clientIp)
        {
            SessionId = Guid.NewGuid().ToString();
            ClientId = clientId;
            ClientIp = clientIp;
            ConnectedAt = DateTime.Now;
            IsActive = true;
            // Default to no control access until authenticated
            HasControlAccess = false;
        }

        /// <summary>
        /// Creates a new empty user session
        /// </summary>
        public UserSession()
        {
            SessionId = Guid.NewGuid().ToString();
            ConnectedAt = DateTime.Now;
        }

        /// <summary>
        /// Grant control access to this session
        /// </summary>
        public void GrantControlAccess()
        {
            HasControlAccess = true;
        }

        /// <summary>
        /// Revoke control access from this session
        /// </summary>
        public void RevokeControlAccess()
        {
            HasControlAccess = false;
        }

        /// <summary>
        /// End this session
        /// </summary>
        public void EndSession()
        {
            IsActive = false;
            HasControlAccess = false;
        }
    }
}