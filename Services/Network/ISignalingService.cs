using System;
using System.Threading.Tasks;
using Wynzio.Models;

namespace Wynzio.Services.Network
{
    /// <summary>
    /// Defines the contract for a WebRTC signaling service
    /// </summary>
    internal interface ISignalingService
    {
        /// <summary>
        /// Event triggered when a message is received from the signaling server
        /// </summary>
        event EventHandler<SignalingMessageEventArgs> MessageReceived;

        /// <summary>
        /// Event triggered when connection status changes
        /// </summary>
        event EventHandler<ConnectionStatusEventArgs> ConnectionStatusChanged;

        /// <summary>
        /// Event triggered when a new remote connection is established
        /// </summary>
        event EventHandler<UserSession> SessionCreated;

        /// <summary>
        /// Connect to the signaling server
        /// </summary>
        /// <param name="serverUrl">WebSocket URL of the signaling server</param>
        /// <param name="remotePcId">Unique identifier for this remote PC</param>
        /// <returns>Task representing the connection operation</returns>
        Task ConnectAsync(string serverUrl, string remotePcId);

        /// <summary>
        /// Disconnect from the signaling server
        /// </summary>
        /// <returns>Task representing the disconnection operation</returns>
        Task DisconnectAsync();

        /// <summary>
        /// Send a signaling message to a specific peer
        /// </summary>
        /// <param name="peerId">ID of the recipient peer</param>
        /// <param name="messageType">Type of signaling message</param>
        /// <param name="payload">Message payload</param>
        /// <returns>Task representing the send operation</returns>
        Task SendMessageAsync(string peerId, SignalingMessageType messageType, object payload);

        /// <summary>
        /// Attempt to reuse an existing session
        /// </summary>
        /// <param name="sid">Session ID to reuse</param>
        /// <param name="remotePcId">Remote PC ID</param>
        /// <returns>Task representing the session reuse operation</returns>
        Task ReuseSessionAsync(string sid, string remotePcId);

        /// <summary>
        /// Whether the service is currently connected to the signaling server
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Unique identifier for this remote PC
        /// </summary>
        string RemotePcId { get; }
    }

    /// <summary>
    /// Types of WebRTC signaling messages
    /// </summary>
    internal enum SignalingMessageType
    {
        /// <summary>
        /// Initial offer to establish a connection
        /// </summary>
        Offer,

        /// <summary>
        /// Response to an offer
        /// </summary>
        Answer,

        /// <summary>
        /// ICE candidate for connection negotiation
        /// </summary>
        IceCandidate,

        /// <summary>
        /// Request to establish a connection
        /// </summary>
        Connect,

        /// <summary>
        /// Request to terminate a connection
        /// </summary>
        Disconnect,

        /// <summary>
        /// Custom command or message
        /// </summary>
        Custom
    }

    /// <summary>
    /// Event arguments for signaling messages
    /// </summary>
    internal class SignalingMessageEventArgs : EventArgs
    {
        /// <summary>
        /// ID of the sender peer
        /// </summary>
        public string PeerId { get; }

        /// <summary>
        /// Type of the signaling message
        /// </summary>
        public SignalingMessageType MessageType { get; }

        /// <summary>
        /// Message payload
        /// </summary>
        public object Payload { get; }

        public SignalingMessageEventArgs(string peerId, SignalingMessageType messageType, object payload)
        {
            PeerId = peerId;
            MessageType = messageType;
            Payload = payload;
        }
    }

    /// <summary>
    /// Event arguments for connection status changes
    /// </summary>
    internal class ConnectionStatusEventArgs : EventArgs
    {
        /// <summary>
        /// Whether the connection is established
        /// </summary>
        public bool IsConnected { get; }

        /// <summary>
        /// Error message, if any
        /// </summary>
        public string? ErrorMessage { get; }

        public ConnectionStatusEventArgs(bool isConnected, string? errorMessage = null)
        {
            IsConnected = isConnected;
            ErrorMessage = errorMessage;
        }
    }
}