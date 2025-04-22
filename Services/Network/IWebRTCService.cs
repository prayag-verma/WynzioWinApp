using System;
using System.Threading.Tasks;

namespace Wynzio.Services.Network
{
    /// <summary>
    /// Interface for WebRTC service
    /// </summary>
    internal interface IWebRTCService
    {
        /// <summary>
        /// Event triggered when a connection is established
        /// </summary>
        event EventHandler<string>? ConnectionEstablished;

        /// <summary>
        /// Event triggered when a connection is closed
        /// </summary>
        event EventHandler<string>? ConnectionClosed;

        /// <summary>
        /// Event triggered when a data channel message is received
        /// </summary>
        event EventHandler<DataChannelMessageEventArgs>? DataChannelMessageReceived;

        /// <summary>
        /// Initialize the WebRTC service
        /// </summary>
        void Initialize();

        /// <summary>
        /// Create a WebRTC offer for a specific peer
        /// </summary>
        Task CreateOfferAsync(string peerId);

        /// <summary>
        /// Process a WebRTC answer from a remote peer
        /// </summary>
        Task ProcessAnswerAsync(string peerId, string sdp, string type);

        /// <summary>
        /// Process an ICE candidate from a remote peer
        /// </summary>
        Task AddIceCandidateAsync(string peerId, string candidate, int? sdpMLineIndex, string? sdpMid);

        /// <summary>
        /// Process an incoming WebRTC offer
        /// </summary>
        Task ProcessOfferAsync(string peerId, string sdp, string type);

        /// <summary>
        /// Send a data channel message to a specific peer
        /// </summary>
        Task SendDataChannelMessageAsync(string peerId, string message);

        /// <summary>
        /// Close a specific peer connection
        /// </summary>
        Task ClosePeerConnectionAsync(string peerId);

        /// <summary>
        /// Close all peer connections
        /// </summary>
        Task CloseAllConnectionsAsync();
    }
}