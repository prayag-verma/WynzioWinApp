using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using Wynzio.Models;
using Wynzio.Services.Capture;
using SIPSorcery.Net;
using SIPSorcery.Media;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.Windows;
using SIPSorceryMedia.Encoders;

namespace Wynzio.Services.Network
{
    /// <summary>
    /// Event arguments for data channel messages
    /// </summary>
    public class DataChannelMessageEventArgs : EventArgs
    {
        /// <summary>
        /// ID of the sender peer
        /// </summary>
        public string PeerId { get; }

        /// <summary>
        /// Message content
        /// </summary>
        public string Message { get; }

        public DataChannelMessageEventArgs(string peerId, string message)
        {
            PeerId = peerId;
            Message = message;
        }
    }

    /// <summary>
    /// Service to handle WebRTC connections and media streaming
    /// </summary>
    internal class WebRTCService : IWebRTCService, IDisposable
    {
        private readonly ILogger _logger;
        private readonly ISignalingService _signalingService;
        private readonly IScreenCaptureService _captureService;
        private readonly INetworkStatusService _networkStatusService;
        private bool _isInitialized;
        private readonly ConnectionSettings _settings;

        // For storing active WebRTC connections
        private readonly Dictionary<string, RTCPeerConnection> _peerConnections = new();
        // For storing data channels
        private readonly Dictionary<string, RTCDataChannel> _dataChannels = new();
        // For storing active peer status
        private readonly Dictionary<string, DateTime> _activePeers = new();
        // For video sources
        private readonly Dictionary<string, VideoEncoderEndPoint> _videoSources = new();

        // Frame rate limiter
        private DateTime _lastFrameSentTime = DateTime.MinValue;
        private readonly int _frameInterval = 50; // Milliseconds between frames (default 20fps)

        /// <summary>
        /// Event triggered when a connection is established
        /// </summary>
        public event EventHandler<string>? ConnectionEstablished;

        /// <summary>
        /// Event triggered when a connection is closed
        /// </summary>
        public event EventHandler<string>? ConnectionClosed;

        /// <summary>
        /// Event triggered when a data channel message is received
        /// </summary>
        public event EventHandler<DataChannelMessageEventArgs>? DataChannelMessageReceived;

        public WebRTCService(
            ISignalingService signalingService,
            IScreenCaptureService captureService,
            INetworkStatusService networkStatusService)
        {
            _logger = Log.ForContext<WebRTCService>();
            _signalingService = signalingService;
            _captureService = captureService;
            _networkStatusService = networkStatusService;
            _settings = ConnectionSettings.Load();
            _frameInterval = 1000 / _settings.CaptureFrameRate;

            // Subscribe to signaling events
            _signalingService.MessageReceived += OnSignalingMessageReceived;

            // Subscribe to network status events
            _networkStatusService.NetworkStatusChanged += OnNetworkStatusChanged;
        }

        /// <summary>
        /// Handle network status changes
        /// </summary>
        private void OnNetworkStatusChanged(object? sender, NetworkStatusChangedEventArgs e)
        {
            if (!e.IsInternetAvailable && _activePeers.Count > 0)
            {
                _logger.Warning("Internet connection lost. Active connections may be disrupted.");
            }
        }

        /// <summary>
        /// Initialize the WebRTC service
        /// </summary>
        public void Initialize()
        {
            if (_isInitialized)
                return;

            try
            {
                _logger.Information("Initializing WebRTC service");

                // Subscribe to screen capture events
                _captureService.FrameCaptured += OnFrameCaptured;

                _isInitialized = true;
                _logger.Information("WebRTC service initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to initialize WebRTC service");
            }
        }

        /// <summary>
        /// Create a WebRTC offer for a specific peer
        /// </summary>
        public async Task CreateOfferAsync(string peerId)
        {
            try
            {
                _logger.Information("Creating offer for peer {PeerId}", peerId);

                // Create a new RTCPeerConnection
                var peerConnection = CreatePeerConnection(peerId);

                // Create a data channel for control messages
                var dataChannelInit = new RTCDataChannelInit { ordered = true };

                // In SIPSorcery v8.0.11, createDataChannel returns a Task<RTCDataChannel>
                var dataChannelTask = peerConnection.createDataChannel("control", dataChannelInit);
                RTCDataChannel? dataChannel = await dataChannelTask;

                if (dataChannel != null)
                {
                    // Set up data channel event handlers
                    SetupDataChannel(dataChannel, peerId);

                    // Store the data channel
                    _dataChannels[peerId] = dataChannel;
                }

                // Add to active peer tracking
                _activePeers[peerId] = DateTime.Now;
                _peerConnections[peerId] = peerConnection;

                // Add video track to peer connection for screen sharing
                await AddScreenTrackAsync(peerConnection, peerId);

                // Create an offer
                var offer = peerConnection.createOffer(null);

                // Create RTCSessionDescriptionInit for local description
                var sessionDesc = new RTCSessionDescriptionInit
                {
                    sdp = offer.sdp,
                    type = RTCSdpType.offer
                };

                // Set local description
                await peerConnection.setLocalDescription(sessionDesc);

                // Send offer through signaling service
                await _signalingService.SendMessageAsync(peerId, SignalingMessageType.Offer, new
                {
                    sdp = offer.sdp,
                    type = "offer",
                    hostId = _settings.HostId,
                    stunServer = _settings.StunServer,
                    timestamp = DateTime.Now.Ticks
                });

                _logger.Information("Offer sent to peer {PeerId}", peerId);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error creating offer for peer {PeerId}", peerId);
                throw;
            }
        }

        /// <summary>
        /// Process a WebRTC answer from a remote peer
        /// </summary>
        public async Task ProcessAnswerAsync(string peerId, string sdp, string type)
        {
            await Task.CompletedTask;
            try
            {
                _logger.Information("Processing answer from peer {PeerId}", peerId);

                if (!_peerConnections.TryGetValue(peerId, out var peerConnection))
                {
                    _logger.Warning("No peer connection found for {PeerId}", peerId);
                    return;
                }

                // Create RTCSessionDescriptionInit for remote description
                var sessionDesc = new RTCSessionDescriptionInit
                {
                    sdp = sdp,
                    type = RTCSdpType.answer
                };

                // Set remote description
                var result = peerConnection.setRemoteDescription(sessionDesc);

                if (result != SetDescriptionResultEnum.OK)
                {
                    _logger.Error("Failed to set remote description: {Result}", result);
                    return;
                }

                // Notify that connection is established
                ConnectionEstablished?.Invoke(this, peerId);

                _logger.Information("Successfully processed answer from {PeerId}", peerId);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error processing answer from peer {PeerId}", peerId);
                throw;
            }
        }

        /// <summary>
        /// Process an ICE candidate from a remote peer
        /// </summary>
        public async Task AddIceCandidateAsync(string peerId, string candidate, int? sdpMLineIndex, string? sdpMid)
        {
            await Task.CompletedTask;
            try
            {
                if (!_peerConnections.TryGetValue(peerId, out var peerConnection))
                {
                    _logger.Warning("No peer connection found for {PeerId} when adding ICE candidate", peerId);
                    return;
                }

                // Create RTCIceCandidateInit for the candidate
                var iceCandidate = new RTCIceCandidateInit
                {
                    candidate = candidate,
                    sdpMid = sdpMid,
                    sdpMLineIndex = (ushort)(sdpMLineIndex ?? 0)  // Cast to ushort to fix the error
                };

                // Add ICE candidate - no need to await
                peerConnection.addIceCandidate(iceCandidate);

                _logger.Debug("Added ICE candidate for {PeerId}", peerId);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error adding ICE candidate for peer {PeerId}", peerId);
            }
        }

        /// <summary>
        /// Process an incoming WebRTC offer
        /// </summary>
        public async Task ProcessOfferAsync(string peerId, string sdp, string type)
        {
            try
            {
                _logger.Information("Processing offer from peer {PeerId}", peerId);

                // Create a new peer connection or get existing one
                var peerConnection = _peerConnections.TryGetValue(peerId, out var existingConnection)
                    ? existingConnection
                    : CreatePeerConnection(peerId);

                // If this is a new connection, set it up
                if (!_peerConnections.ContainsKey(peerId))
                {
                    _peerConnections[peerId] = peerConnection;
                    _activePeers[peerId] = DateTime.Now;
                }

                // Create RTCSessionDescriptionInit for the remote offer
                var sessionDesc = new RTCSessionDescriptionInit
                {
                    sdp = sdp,
                    type = RTCSdpType.offer
                };

                // Set remote description
                var result = peerConnection.setRemoteDescription(sessionDesc);

                if (result != SetDescriptionResultEnum.OK)
                {
                    _logger.Error("Failed to set remote description: {Result}", result);
                    return;
                }

                // Add video track to peer connection for screen sharing
                await AddScreenTrackAsync(peerConnection, peerId);

                // Create answer
                var answer = peerConnection.createAnswer(null);

                // Create RTCSessionDescriptionInit for local description
                var localDesc = new RTCSessionDescriptionInit
                {
                    sdp = answer.sdp,
                    type = RTCSdpType.answer
                };

                // Set local description
                await peerConnection.setLocalDescription(localDesc);

                // Send answer through signaling service
                await _signalingService.SendMessageAsync(peerId, SignalingMessageType.Answer, new
                {
                    sdp = answer.sdp,
                    type = "answer",
                    hostId = _settings.HostId,
                    accepted = true,
                    timestamp = DateTime.Now.Ticks
                });

                _logger.Information("Answer sent to peer {PeerId}", peerId);

                // Notify that connection is established
                ConnectionEstablished?.Invoke(this, peerId);

                // Start screen capture if it's not already running
                if (!_captureService.IsCapturing)
                {
                    await _captureService.StartCaptureAsync(
                        frameRate: _settings.CaptureFrameRate,
                        quality: _settings.CaptureQuality);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error processing offer from peer {PeerId}", peerId);
                throw;
            }
        }

        /// <summary>
        /// Create a WebRTC peer connection with default configuration
        /// </summary>
        private RTCPeerConnection CreatePeerConnection(string peerId)
        {
            // Create RTCConfiguration
            var configuration = new RTCConfiguration
            {
                iceServers = GetIceServers()
            };

            // Create peer connection
            var peerConnection = new RTCPeerConnection(configuration);

            // Set up event handlers
            SetupPeerConnectionEvents(peerConnection, peerId);

            return peerConnection;
        }

        /// <summary>
        /// Get ICE servers configuration
        /// </summary>
        private List<RTCIceServer> GetIceServers()
        {
            var iceServers = new List<RTCIceServer>();

            // Add STUN server if configured
            if (!string.IsNullOrEmpty(_settings.StunServer))
            {
                iceServers.Add(new RTCIceServer
                {
                    urls = _settings.StunServer
                });
            }

            // Add TURN server if configured
            if (!string.IsNullOrEmpty(_settings.TurnServer))
            {
                iceServers.Add(new RTCIceServer
                {
                    urls = _settings.TurnServer,
                    username = _settings.TurnUsername,
                    credential = _settings.TurnPassword
                });
            }

            // Use Google's STUN server as fallback
            if (iceServers.Count == 0)
            {
                iceServers.Add(new RTCIceServer
                {
                    urls = "stun:stun.l.google.com:19302"
                });
            }

            return iceServers;
        }

        /// <summary>
        /// Set up event handlers for peer connection
        /// </summary>
        private void SetupPeerConnectionEvents(RTCPeerConnection peerConnection, string peerId)
        {
            // ICE candidate generation
            peerConnection.onicecandidate += (candidate) =>
            {
                try
                {
                    if (candidate != null)
                    {
                        // Send ICE candidate to remote peer through signaling service
                        _signalingService.SendMessageAsync(peerId, SignalingMessageType.IceCandidate, new
                        {
                            candidate = candidate.candidate,
                            sdpMLineIndex = candidate.sdpMLineIndex,
                            sdpMid = candidate.sdpMid
                        }).ConfigureAwait(false);

                        _logger.Debug("Sent ICE candidate to {PeerId}", peerId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error sending ICE candidate to {PeerId}", peerId);
                }
            };

            // ICE connection state change
            peerConnection.oniceconnectionstatechange += (state) =>
            {
                _logger.Debug("ICE connection state changed for {PeerId}: {State}", peerId, state);

                switch (state)
                {
                    case RTCIceConnectionState.connected:
                        // Notify connection established (if not already done)
                        ConnectionEstablished?.Invoke(this, peerId);
                        break;

                    case RTCIceConnectionState.failed:
                    case RTCIceConnectionState.disconnected:
                    case RTCIceConnectionState.closed:
                        // Close connection if it failed/closed
                        ClosePeerConnectionAsync(peerId).ConfigureAwait(false);
                        break;
                }
            };

            // Connection state change
            peerConnection.onconnectionstatechange += (state) =>
            {
                _logger.Debug("Connection state changed for {PeerId}: {State}", peerId, state);

                if (state == RTCPeerConnectionState.failed ||
                    state == RTCPeerConnectionState.closed)
                {
                    ClosePeerConnectionAsync(peerId).ConfigureAwait(false);
                }
            };

            // Data channel event
            peerConnection.ondatachannel += (dc) =>
            {
                _logger.Information("Data channel received from {PeerId}: {Label}", peerId, dc.label);
                SetupDataChannel(dc, peerId);
            };
        }

        /// <summary>
        /// Set up event handlers for data channel
        /// </summary>
        private void SetupDataChannel(RTCDataChannel dataChannel, string peerId)
        {
            // Store the data channel
            _dataChannels[peerId] = dataChannel;

            // Open event
            dataChannel.onopen += () =>
            {
                _logger.Information("Data channel opened with {PeerId}: {Label}", peerId, dataChannel.label);
            };

            // Close event
            dataChannel.onclose += () =>
            {
                _logger.Information("Data channel closed with {PeerId}: {Label}", peerId, dataChannel.label);
                _dataChannels.Remove(peerId);
            };

            // Message event - using the onmessage delegate with correct parameters
            // Convert raw data payload to string and raise DataChannelMessageReceived
            dataChannel.onmessage += (RTCDataChannel dc, DataChannelPayloadProtocols protocol, byte[] data) =>
            {
                try
                {
                    _logger.Debug("Received data channel message from {PeerId}", peerId);
                    if (data != null && data.Length > 0)
                    {
                        string msg = Encoding.UTF8.GetString(data);
                        DataChannelMessageReceived?.Invoke(this,
                            new DataChannelMessageEventArgs(peerId, msg));
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error processing data channel message from {PeerId}", peerId);
                }
            };
        }

        /// <summary>
        /// Add screen capture track to peer connection for screen sharing
        /// </summary>
        private async Task AddScreenTrackAsync(RTCPeerConnection peerConnection, string peerId)
        {
            try
            {
                // Create a video endpoint
                var videoEndpoint = new VideoEncoderEndPoint();
                _videoSources[peerId] = videoEndpoint;

                // Restrict to VP8 codec
                videoEndpoint.RestrictFormats(format => format.Codec == VideoCodecsEnum.VP8);

                // Add tracks to peer connection
                var videoTrack = new MediaStreamTrack(
                    videoEndpoint.GetVideoSourceFormats(),
                    MediaStreamStatusEnum.SendRecv);

                peerConnection.addTrack(videoTrack);

                // Start screen capture if not already running
                if (!_captureService.IsCapturing)
                {
                    await _captureService.StartCaptureAsync(
                        frameRate: _settings.CaptureFrameRate,
                        quality: _settings.CaptureQuality);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error adding screen track to peer connection");
                throw;
            }
        }

        /// <summary>
        /// Send a data channel message to a specific peer
        /// </summary>
        public async Task SendDataChannelMessageAsync(string peerId, string message)
        {
            await Task.CompletedTask;
            try
            {
                _logger.Debug("Sending data channel message to peer {PeerId}", peerId);

                // Check if we have a data channel for this peer
                if (_dataChannels.TryGetValue(peerId, out var dataChannel) &&
                    dataChannel.readyState == RTCDataChannelState.open)
                {
                    // Send message through data channel
                    dataChannel.send(message);
                    return;
                }

                _logger.Warning("No open data channel for peer {PeerId}", peerId);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error sending data channel message to peer {PeerId}", peerId);
            }
        }

        /// <summary>
        /// Close a specific peer connection
        /// </summary>
        public async Task ClosePeerConnectionAsync(string peerId)
        {
            await Task.CompletedTask;
            try
            {
                _logger.Information("Closing connection to peer {PeerId}", peerId);

                // Remove from active peers
                _activePeers.Remove(peerId);

                // Remove video source
                if (_videoSources.TryGetValue(peerId, out var videoSource))
                {
                    videoSource.Dispose();
                    _videoSources.Remove(peerId);
                }

                // Close data channel if any
                if (_dataChannels.TryGetValue(peerId, out var dataChannel))
                {
                    dataChannel.close();
                    _dataChannels.Remove(peerId);
                }

                // Close peer connection
                if (_peerConnections.TryGetValue(peerId, out var peerConnection))
                {
                    peerConnection.close();
                    _peerConnections.Remove(peerId);
                }

                // Notify that connection is closed
                ConnectionClosed?.Invoke(this, peerId);

                // Stop screen capture if no active peers
                if (_activePeers.Count == 0 && _captureService.IsCapturing)
                {
                    _captureService.StopCapture();
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error closing connection to peer {PeerId}", peerId);
            }
        }

        /// <summary>
        /// Close all peer connections
        /// </summary>
        public async Task CloseAllConnectionsAsync()
        {
            try
            {
                _logger.Information("Closing all connections");

                // Get list of peer IDs
                var peerIds = _activePeers.Keys.ToList();

                // Close each connection
                foreach (var peerId in peerIds)
                {
                    await ClosePeerConnectionAsync(peerId);
                }

                // Stop screen capture
                if (_captureService.IsCapturing)
                {
                    _captureService.StopCapture();
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error closing all connections");
            }
        }

        /// <summary>
        /// Handle a signaling message
        /// </summary>
        private async void OnSignalingMessageReceived(object? sender, SignalingMessageEventArgs e)
        {
            try
            {
                _logger.Information("Received signaling message from {PeerId}: {Type}",
                    e.PeerId, e.MessageType);

                switch (e.MessageType)
                {
                    case SignalingMessageType.Offer:
                        if (e.Payload is JObject offerData)
                        {
                            string sdp = offerData["sdp"]?.ToString() ?? string.Empty;
                            string type = offerData["type"]?.ToString() ?? string.Empty;
                            await ProcessOfferAsync(e.PeerId, sdp, type);
                        }
                        break;

                    case SignalingMessageType.Answer:
                        if (e.Payload is JObject answerData)
                        {
                            string sdp = answerData["sdp"]?.ToString() ?? string.Empty;
                            string type = answerData["type"]?.ToString() ?? string.Empty;
                            await ProcessAnswerAsync(e.PeerId, sdp, type);
                        }
                        break;

                    case SignalingMessageType.IceCandidate:
                        if (e.Payload is JObject iceData)
                        {
                            string candidate = iceData["candidate"]?.ToString() ?? string.Empty;
                            int? sdpMLineIndex = iceData["sdpMLineIndex"]?.Value<int>();
                            string? sdpMid = iceData["sdpMid"]?.ToString();
                            await AddIceCandidateAsync(e.PeerId, candidate, sdpMLineIndex, sdpMid);
                        }
                        break;

                    case SignalingMessageType.Disconnect:
                        await ClosePeerConnectionAsync(e.PeerId);
                        break;

                    case SignalingMessageType.Connect:
                        await CreateOfferAsync(e.PeerId);
                        break;

                    case SignalingMessageType.Custom:
                        // Process custom commands (like control commands)
                        if (e.Payload is JObject customData)
                        {
                            string command = customData["command"]?.ToString() ?? string.Empty;

                            if (!string.IsNullOrEmpty(command) &&
                                customData["data"] != null)
                            {
                                // Raise data channel message event for input processing
                                DataChannelMessageReceived?.Invoke(this,
                                    new DataChannelMessageEventArgs(e.PeerId,
                                    customData["data"]?.ToString() ?? "{}"));
                            }
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error handling signaling message from {PeerId}", e.PeerId);
            }
        }

        /// <summary>
        /// Handle captured screen frames and send to peers
        /// </summary>
        private void OnFrameCaptured(object? sender, FrameCapturedEventArgs e)
        {
            try
            {
                // Only process if we have active peers
                if (_activePeers.Count == 0)
                    return;

                // Only send frames if we have internet connectivity
                if (!_networkStatusService.IsInternetAvailable)
                    return;

                // Rate limit frames
                if ((DateTime.Now - _lastFrameSentTime).TotalMilliseconds < _frameInterval)
                    return;

                _lastFrameSentTime = DateTime.Now;

                // Send to all video endpoints
                foreach (var videoSource in _videoSources.Values)
                {
                    try
                    {
                        // For VideoEncoderEndPoint, use ExternalVideoSourceRawSample
                        videoSource.ExternalVideoSourceRawSample(
                            20, // duration in milliseconds
                            e.Width,
                            e.Height,
                            e.FrameData,
                            VideoPixelFormatsEnum.Bgra);
                    }
                    catch (Exception sourceEx)
                    {
                        _logger.Warning(sourceEx, "Error sending frame to video source");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error processing captured frame");
            }
        }

        /// <summary>
        /// Clean up resources
        /// </summary>
        public void Dispose()
        {
            try
            {
                // Unsubscribe from events
                _signalingService.MessageReceived -= OnSignalingMessageReceived;
                _captureService.FrameCaptured -= OnFrameCaptured;
                _networkStatusService.NetworkStatusChanged -= OnNetworkStatusChanged;

                // Close all connections
                CloseAllConnectionsAsync().Wait(TimeSpan.FromSeconds(2));

                // Clean up collections
                foreach (var source in _videoSources.Values)
                {
                    source.Dispose();
                }
                _videoSources.Clear();
                _peerConnections.Clear();
                _dataChannels.Clear();
                _activePeers.Clear();

                _logger.Information("WebRTC service resources cleaned up");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error disposing WebRTCService");
            }
        }
    }
}