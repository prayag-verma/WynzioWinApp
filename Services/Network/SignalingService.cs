using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using System.Security.Cryptography;
using System.Net.Http;
using System.Web;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using Wynzio.Models;
using System.Diagnostics;

namespace Wynzio.Services.Network
{
    /// <summary>
    /// Implementation of WebRTC signaling service using Socket.IO over WebSockets
    /// </summary>
    internal class SignalingService : ISignalingService, IDisposable
    {
        private ClientWebSocket? _webSocket;
        private CancellationTokenSource? _cts;
        private Task? _receiveTask;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private string _hostId = string.Empty;
        private int _reconnectAttempts = 0;
        private const int MaxReconnectAttempts = 5;
        private string? _serverUrl;
        private bool _isDisconnecting = false;
        private Timer? _heartbeatTimer;
        private readonly INetworkStatusService _networkStatusService;
        private string? _socketIOSid;
        private int _pingInterval = 25000; // Default ping interval in ms
        private int _pingTimeout = 20000;  // Default ping timeout in ms

        public bool IsConnected { get; private set; }

        public string HostId => _hostId;

        public event EventHandler<SignalingMessageEventArgs>? MessageReceived;
        public event EventHandler<ConnectionStatusEventArgs>? ConnectionStatusChanged;
        public event EventHandler<UserSession>? SessionCreated;

        public SignalingService(INetworkStatusService networkStatusService)
        {
            _logger = Log.ForContext<SignalingService>();
            _networkStatusService = networkStatusService;

            // Subscribe to network status changes
            _networkStatusService.NetworkStatusChanged += OnNetworkStatusChanged;
        }

        /// <summary>
        /// Handle network status changes
        /// </summary>
        private void OnNetworkStatusChanged(object? sender, NetworkStatusChangedEventArgs e)
        {
            if (e.IsInternetAvailable && !IsConnected && !_isDisconnecting && _serverUrl != null)
            {
                _logger.Information("Internet connectivity restored. Attempting to reconnect...");
                // Reset reconnection attempts to start fresh
                _reconnectAttempts = 0;
                ConnectAsync(_serverUrl, _hostId).ConfigureAwait(false);
            }
            else if (!e.IsInternetAvailable && IsConnected)
            {
                _logger.Information("Internet connectivity lost. Connection may be disrupted.");
            }
        }

        /// <summary>
        /// Connect to the signaling server using Socket.IO protocol
        /// </summary>
        public async Task ConnectAsync(string serverUrl, string hostId)
        {
            // Log connection attempt
            _logger.Information("Attempting to connect to {ServerUrl} with hostId {HostId}", serverUrl, hostId);

            // Reset disconnecting flag
            _isDisconnecting = false;

            try
            {
                // Check internet connectivity first
                if (!_networkStatusService.IsInternetAvailable)
                {
                    _logger.Warning("No internet connectivity. Connection attempt queued for when internet is available.");
                    _serverUrl = serverUrl;
                    _hostId = hostId;
                    OnConnectionStatusChanged(false, "Waiting for internet connectivity...");
                    return;
                }

                // Store parameters for reconnection
                _serverUrl = serverUrl;
                _hostId = hostId;

                // Create new cancellation token source
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = new CancellationTokenSource();

                // Create new WebSocket instance
                _webSocket?.Dispose();
                _webSocket = new ClientWebSocket();

                // Set timeout for connection
                _webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);

                // Set connection status to attempting
                OnConnectionStatusChanged(false, "Connecting...");

                // Connect to the server with timeout
                var connectionCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    connectionCts.Token, _cts.Token);

                try
                {
                    // Load settings for API key
                    var settings = ConnectionSettings.Load();

                    // Perform Socket.IO handshake
                    await PerformSocketIOHandshake(serverUrl, settings.ApiKey, combinedCts.Token);

                    // Now connect WebSocket with proper sid
                    var wsUri = new UriBuilder(serverUrl);
                    var query = HttpUtility.ParseQueryString(wsUri.Query);
                    query["EIO"] = "4";
                    query["transport"] = "websocket";
                    query["sid"] = _socketIOSid;
                    wsUri.Query = query.ToString();

                    // Log the connection URI
                    _logger.Information("Connecting to WebSocket at {Uri}", wsUri.Uri);

                    // Add authorization header with API key
                    _webSocket.Options.SetRequestHeader("Authorization", $"ApiKey {settings.ApiKey}");

                    // Connect to the server
                    await _webSocket.ConnectAsync(wsUri.Uri, combinedCts.Token);

                    // Send Socket.IO upgrade packet
                    await SendSocketIOMessage("2probe");

                    // Start receiving messages
                    _receiveTask = ReceiveMessagesAsync(_cts.Token);

                    // Update connection status
                    IsConnected = true;
                    _reconnectAttempts = 0;

                    // Perform Socket.IO connection upgrade
                    await SendSocketIOMessage("5"); // Engine.IO upgrade packet

                    // Connect to the default namespace
                    await SendSocketIOMessage("40"); // Socket.IO connect packet

                    // Set up heartbeat timer
                    StartHeartbeatTimer();

                    // Send authentication request
                    await SendAuthenticationRequestAsync();

                    OnConnectionStatusChanged(true);
                    _logger.Information("Connected to signaling server: {ServerUrl}", serverUrl);
                }
                catch (OperationCanceledException)
                {
                    throw new TimeoutException("Connection attempt timed out");
                }
                finally
                {
                    connectionCts.Dispose();
                    combinedCts.Dispose();
                }
            }
            catch (Exception ex)
            {
                // Don't log as error for common connection issues
                if (ex is WebSocketException || ex is TimeoutException)
                {
                    _logger.Warning("Failed to connect to signaling server: {ServerUrl}, {Message}",
                        serverUrl, ex.Message);
                }
                else
                {
                    _logger.Error(ex, "Failed to connect to signaling server: {ServerUrl}", serverUrl);
                }

                OnConnectionStatusChanged(false, "Connection failed");

                // Try to reconnect if appropriate and not explicitly disconnecting
                if (_reconnectAttempts < MaxReconnectAttempts && _serverUrl != null && !_isDisconnecting)
                {
                    _reconnectAttempts++;
                    _logger.Information("Attempting to reconnect ({Attempt}/{MaxAttempts})...",
                        _reconnectAttempts, MaxReconnectAttempts);

                    // Wait before reconnecting with exponential backoff
                    int delaySeconds = (int)Math.Pow(2, _reconnectAttempts);
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds));

                    // Try to reconnect
                    await ConnectAsync(_serverUrl, _hostId);
                }
            }
        }

        /// <summary>
        /// Perform Socket.IO handshake via HTTP polling
        /// </summary>
        private async Task PerformSocketIOHandshake(string serverUrl, string apiKey, CancellationToken token)
        {
            try
            {
                // Convert WebSocket URL to HTTP
                string httpUrl = serverUrl.Replace("wss://", "https://").Replace("ws://", "http://");
                string handshakeUrl = $"{httpUrl}/socket.io/?EIO=4&transport=polling";

                _logger.Information("Initiating Socket.IO handshake with {HandshakeUrl}", handshakeUrl);

                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("Authorization", $"ApiKey {apiKey}");

                // Add a reasonable timeout
                var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);

                try
                {
                    var response = await httpClient.GetAsync(handshakeUrl, combinedCts.Token);
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new Exception($"Socket.IO handshake failed: {response.StatusCode}");
                    }

                    var content = await response.Content.ReadAsStringAsync(combinedCts.Token);
                    _logger.Debug("Received handshake response: {Content}", content);

                    // Extract the JSON payload from the Socket.IO response
                    // This is more robust than using regex as it handles multiple formats
                    string jsonPayload = ExtractJsonFromSocketIOResponse(content);

                    if (string.IsNullOrEmpty(jsonPayload))
                    {
                        _logger.Error("Failed to extract JSON payload from Socket.IO response: {Content}", content);
                        throw new Exception($"Invalid Socket.IO handshake response: {content}");
                    }

                    // Parse the JSON payload
                    try
                    {
                        var handshakeData = JObject.Parse(jsonPayload);

                        // Extract session ID and timing parameters
                        _socketIOSid = handshakeData["sid"]?.ToString();
                        _pingInterval = handshakeData["pingInterval"]?.Value<int>() ?? 25000;
                        _pingTimeout = handshakeData["pingTimeout"]?.Value<int>() ?? 20000;

                        if (string.IsNullOrEmpty(_socketIOSid))
                        {
                            throw new Exception("Socket.IO handshake did not return a session ID");
                        }

                        // Log success with session info
                        _logger.Information("Socket.IO handshake successful. Session ID: {SessionId}, Ping interval: {PingInterval}ms",
                            _socketIOSid, _pingInterval);

                        // Verify upgrades includes websocket
                        var upgrades = handshakeData["upgrades"] as JArray;
                        bool supportsWebsocket = false;

                        if (upgrades != null)
                        {
                            foreach (var upgrade in upgrades)
                            {
                                if (upgrade.ToString().Equals("websocket", StringComparison.OrdinalIgnoreCase))
                                {
                                    supportsWebsocket = true;
                                    break;
                                }
                            }
                        }

                        if (!supportsWebsocket)
                        {
                            _logger.Warning("Socket.IO server does not list WebSocket as an upgrade option");
                        }
                    }
                    catch (JsonException jsonEx)
                    {
                        _logger.Error(jsonEx, "Error parsing Socket.IO handshake JSON: {JsonPayload}", jsonPayload);
                        throw new Exception($"Error parsing Socket.IO handshake JSON: {jsonEx.Message}");
                    }
                }
                finally
                {
                    timeoutCts.Dispose();
                    combinedCts.Dispose();
                }
            }
            catch (OperationCanceledException)
            {
                _logger.Error("Socket.IO handshake timed out");
                throw new TimeoutException("Socket.IO handshake timed out");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during Socket.IO handshake");
                throw;
            }
        }

        /// <summary>
        /// Extract JSON payload from Socket.IO response, handling different formats
        /// </summary>
        /// <param name="response">Raw Socket.IO response</param>
        /// <returns>JSON payload as string</returns>
        private string ExtractJsonFromSocketIOResponse(string response)
        {
            if (string.IsNullOrEmpty(response))
                return string.Empty;

            try
            {
                // Trim any whitespace
                response = response.Trim();

                // First try standard Socket.IO v4+ packet format: length:type{json}
                var standardMatch = Regex.Match(response, @"^(\d+):(\d)(.*)$");
                if (standardMatch.Success)
                {
                    string packetType = standardMatch.Groups[2].Value;

                    // Type 0 is the OPEN packet in Socket.IO
                    if (packetType == "0")
                    {
                        return standardMatch.Groups[3].Value;
                    }
                }

                // Try to find a JSON object directly - for non-standard responses
                var jsonMatch = Regex.Match(response, @"(\{.*\})");
                if (jsonMatch.Success)
                {
                    string potentialJson = jsonMatch.Groups[1].Value;

                    // Validate that it's actually JSON by attempting to parse it
                    JObject.Parse(potentialJson);

                    return potentialJson;
                }

                // Alternative format: some implementations might use a different format
                // Try to extract JSON by finding the first '{' and last '}'
                int firstBrace = response.IndexOf('{');
                int lastBrace = response.LastIndexOf('}');

                if (firstBrace >= 0 && lastBrace > firstBrace)
                {
                    string potentialJson = response.Substring(firstBrace, lastBrace - firstBrace + 1);

                    // Validate that it's actually JSON by attempting to parse it
                    JObject.Parse(potentialJson);

                    return potentialJson;
                }

                // If we're here, we couldn't extract valid JSON using any method
                _logger.Warning("Could not extract valid JSON from Socket.IO response using any method");
                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error extracting JSON from Socket.IO response");
                return string.Empty;
            }
        }

        /// <summary>
        /// Start the heartbeat timer
        /// </summary>
        private void StartHeartbeatTimer()
        {
            // Use pingInterval from Socket.IO handshake
            int intervalMs = _pingInterval;

            // Create or reset timer
            _heartbeatTimer?.Dispose();
            _heartbeatTimer = new Timer(SendHeartbeat, null, intervalMs, intervalMs);
        }

        /// <summary>
        /// Send a heartbeat to the server
        /// </summary>
        private async void SendHeartbeat(object? state)
        {
            if (!IsConnected || _webSocket == null || _webSocket.State != WebSocketState.Open)
                return;

            try
            {
                // Send Socket.IO ping packet (packet type 2)
                await SendSocketIOMessage("2");
                _logger.Debug("Socket.IO ping sent");
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error sending heartbeat");
            }
        }

        /// <summary>
        /// Disconnect from the signaling server
        /// </summary>
        public async Task DisconnectAsync()
        {
            if (!IsConnected || _webSocket == null || _cts == null)
                return;

            // Set flag to prevent reconnection attempts
            _isDisconnecting = true;

            try
            {
                // Stop heartbeat timer
                _heartbeatTimer?.Dispose();
                _heartbeatTimer = null;

                // Send disconnect packet
                if (_webSocket.State == WebSocketState.Open)
                {
                    await SendSocketIOMessage("41"); // Socket.IO disconnect packet

                    // Close the WebSocket connection gracefully
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure,
                        "Client disconnecting", CancellationToken.None);
                }

                // Cancel any ongoing operations
                _cts.Cancel();

                // Update state
                IsConnected = false;
                OnConnectionStatusChanged(false, "Disconnected");

                _logger.Information("Disconnected from signaling server");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error disconnecting from signaling server");
            }
            finally
            {
                // Cleanup resources
                _webSocket?.Dispose();
                _webSocket = null;
                _cts?.Dispose();
                _cts = null;
            }
        }

        /// <summary>
        /// Send a message to a specific peer
        /// </summary>
        public async Task SendMessageAsync(string peerId, SignalingMessageType messageType, object payload)
        {
            if (!IsConnected || _webSocket == null || _cts == null)
            {
                _logger.Warning("Cannot send message when not connected");
                return;
            }

            // Create message object
            var message = new
            {
                type = messageType.ToString().ToLower(),
                to = peerId,
                from = _hostId,
                payload
            };

            try
            {
                // Create Socket.IO event message
                var eventData = new object[]
                {
                    "message", // Event name
                    message    // Event data
                };

                string eventJson = JsonConvert.SerializeObject(eventData);
                await SendSocketIOMessage("42" + eventJson); // 42 = Socket.IO event packet
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error sending message to peer {PeerId}", peerId);

                // If there's a WebSocket error, set connection status to disconnected
                if (ex is WebSocketException)
                {
                    IsConnected = false;
                    OnConnectionStatusChanged(false, "Connection lost");

                    // Try to reconnect
                    if (_serverUrl != null && !_isDisconnecting && _networkStatusService.IsInternetAvailable)
                    {
                        _reconnectAttempts = 0;
                        await ConnectAsync(_serverUrl, _hostId);
                    }
                }
            }
        }

        /// <summary>
        /// Send a Socket.IO formatted message
        /// </summary>
        private async Task SendSocketIOMessage(string message)
        {
            if (_webSocket == null || _cts == null)
                throw new InvalidOperationException("WebSocket is not initialized");

            if (_webSocket.State != WebSocketState.Open)
                throw new WebSocketException("WebSocket is not in an open state");

            try
            {
                byte[] buffer = Encoding.UTF8.GetBytes(message);

                // Use a lock to ensure only one send operation at a time
                await _sendLock.WaitAsync();
                try
                {
                    await _webSocket.SendAsync(
                        new ArraySegment<byte>(buffer),
                        WebSocketMessageType.Text,
                        true,
                        _cts.Token);

                    _logger.Debug("Sent Socket.IO message: {Message}", message);
                }
                finally
                {
                    _sendLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error sending Socket.IO message");
                throw;
            }
        }

        /// <summary>
        /// Send authentication request to the server
        /// </summary>
        private async Task SendAuthenticationRequestAsync()
        {
            try
            {
                // Load connection settings
                var settings = ConnectionSettings.Load();

                // Generate a hardware-based fingerprint if needed
                if (string.IsNullOrEmpty(_hostId))
                {
                    _hostId = GetHardwareFingerprint();
                    settings.HostId = _hostId;
                    settings.Save();
                }

                // Create authentication message with updated format
                var authData = new
                {
                    type = "auto-register",
                    hostId = _hostId,
                    systemName = settings.SystemName,
                    apiKey = settings.ApiKey,
                    platform = "windows",
                    version = GetApplicationVersion(),
                    timestamp = DateTime.Now.Ticks,
                    // Add deviceId to match server expectations
                    deviceId = _hostId,
                    metadata = new
                    {
                        osVersion = Environment.OSVersion.ToString(),
                        platform = "windows",
                        version = GetApplicationVersion()
                    }
                };

                // Create Socket.IO event message
                var eventData = new object[]
                {
                    "auto-register", // Event name
                    authData    // Event data
                };

                string eventJson = JsonConvert.SerializeObject(eventData);
                await SendSocketIOMessage("42" + eventJson); // 42 = Socket.IO event packet

                _logger.Information("Sent authentication request for device {DeviceId}", _hostId);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error sending authentication request");
            }
        }

        /// <summary>
        /// Generate a unique hardware fingerprint
        /// </summary>
        private string GetHardwareFingerprint()
        {
            try
            {
                // Get hardware information from system environment
                var processorId = Environment.ProcessorCount.ToString();
                var machineGuid = Environment.MachineName;
                var osVersion = Environment.OSVersion.ToString();

                // Combine and hash to create a fingerprint
                var combined = $"{processorId}|{machineGuid}|{osVersion}";
                byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(combined));

                // Convert to a shorter hex string (16 chars)
                return Convert.ToHexString(hashBytes)[..16].ToLowerInvariant();
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to generate hardware fingerprint, using random ID");
                return Guid.NewGuid().ToString("N")[..16];
            }
        }

        /// <summary>
        /// Get application version
        /// </summary>
        private static string GetApplicationVersion()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var version = assembly.GetName().Version;
                return version?.ToString() ?? "1.0.0.0";
            }
            catch
            {
                return "1.0.0.0";
            }
        }

        /// <summary>
        /// Receive and process messages from the server
        /// </summary>
        private async Task ReceiveMessagesAsync(CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];

            try
            {
                while (_webSocket != null && _webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    // Create memory to accumulate message
                    using var ms = new System.IO.MemoryStream();
                    WebSocketReceiveResult? result = null;

                    try
                    {
                        // Read complete message (may span multiple frames)
                        do
                        {
                            // Create a linked cancellation token with a timeout
                            using var receiveTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                                cancellationToken, receiveTimeoutCts.Token);

                            try
                            {
                                result = await _webSocket.ReceiveAsync(
                                    new ArraySegment<byte>(buffer), linkedCts.Token);
                            }
                            catch (OperationCanceledException)
                            {
                                if (receiveTimeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                                {
                                    // This was a timeout, not a cancellation
                                    _logger.Warning("WebSocket receive operation timed out");

                                    // Send a ping/pong to check connection
                                    if (_webSocket.State == WebSocketState.Open)
                                    {
                                        // Break the loop and try again
                                        break;
                                    }
                                    else
                                    {
                                        // Socket is no longer open
                                        throw new WebSocketException("WebSocket is no longer open");
                                    }
                                }
                                else
                                {
                                    // This was a regular cancellation
                                    throw;
                                }
                            }

                            if (result.MessageType == WebSocketMessageType.Close)
                            {
                                // Handle WebSocket close
                                await _webSocket.CloseAsync(
                                    WebSocketCloseStatus.NormalClosure,
                                    "Server closed connection",
                                    CancellationToken.None);

                                IsConnected = false;
                                OnConnectionStatusChanged(false, "Server closed connection");
                                return;
                            }

                            // Add received data to memory buffer
                            ms.Write(buffer, 0, result.Count);
                        }
                        while (result != null && !result.EndOfMessage);

                        // Skip processing if no result or loop was broken due to timeout
                        if (result == null)
                            continue;

                        // Reset memory position for reading
                        ms.Seek(0, System.IO.SeekOrigin.Begin);

                        // Process the complete message
                        if (result.MessageType == WebSocketMessageType.Text)
                        {
                            string message = Encoding.UTF8.GetString(ms.ToArray());
                            _logger.Debug("Received WebSocket message: {Message}", message);

                            // Process the Socket.IO message
                            await ProcessSocketIOMessageAsync(message);
                        }
                    }
                    catch (WebSocketException)
                    {
                        // Just rethrow to be handled by the outer catch
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error processing WebSocket message");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation, don't log as error
                _logger.Information("WebSocket receive operation was cancelled");
            }
            catch (WebSocketException ex)
            {
                _logger.Warning(ex, "WebSocket error occurred");
                IsConnected = false;
                OnConnectionStatusChanged(false, "Connection lost");

                // Try to reconnect if not explicitly disconnecting
                if (!cancellationToken.IsCancellationRequested && _serverUrl != null && !_isDisconnecting && _networkStatusService.IsInternetAvailable)
                {
                    try
                    {
                        await Task.Delay(2000, CancellationToken.None); // Wait before reconnecting
                        _reconnectAttempts = 0; // Reset attempt counter for a new series of retries
                        await ConnectAsync(_serverUrl, _hostId);
                    }
                    catch (Exception reconnectEx)
                    {
                        _logger.Error(reconnectEx, "Error during reconnection attempt");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in WebSocket receive loop");
                IsConnected = false;
                OnConnectionStatusChanged(false, "Connection error");
            }
        }

        /// <summary>
        /// Process a Socket.IO message
        /// </summary>
        private async Task ProcessSocketIOMessageAsync(string message)
        {
            try
            {
                _logger.Debug("Processing Socket.IO message: {Message}", message);

                // Socket.IO packet format:
                // https://socket.io/docs/v4/custom-parser/

                // Engine.IO packet types:
                // 0: open, 1: close, 2: ping, 3: pong, 4: message, 5: upgrade, 6: noop

                // Socket.IO packet types (after Engine.IO type 4):
                // 0: connect, 1: disconnect, 2: event, 3: ack, 4: error, 5: binary event, 6: binary ack

                if (message.StartsWith("0")) // Engine.IO open
                {
                    // Parse the handshake response
                    var jsonStart = message.IndexOf('{');
                    if (jsonStart > 0)
                    {
                        var handshakeData = JObject.Parse(message.Substring(jsonStart));
                        _socketIOSid = handshakeData["sid"]?.ToString();
                        _pingInterval = handshakeData["pingInterval"]?.Value<int>() ?? 25000;
                        _pingTimeout = handshakeData["pingTimeout"]?.Value<int>() ?? 20000;
                    }
                    _logger.Information("Socket.IO connection opened");
                }
                else if (message.StartsWith("3")) // Engine.IO pong
                {
                    // Heartbeat response, nothing to do
                    _logger.Debug("Received Socket.IO pong");
                }
                else if (message.StartsWith("40")) // Socket.IO connect event
                {
                    _logger.Information("Socket.IO namespace connected");
                    // We're now fully connected, update status
                    OnConnectionStatusChanged(true);
                }
                else if (message.StartsWith("42")) // Socket.IO event
                {
                    // Extract event data (format is 42["eventName", {...data...}])
                    var eventJson = message.Substring(2);
                    var eventData = JArray.Parse(eventJson);

                    if (eventData.Count >= 2)
                    {
                        string eventName = eventData[0]?.ToString() ?? "";
                        JToken? eventPayload = eventData[1];

                        await ProcessEventMessageAsync(eventName, eventPayload);
                    }
                }
                else if (message.StartsWith("41")) // Socket.IO disconnect event
                {
                    _logger.Information("Socket.IO namespace disconnected");
                    OnConnectionStatusChanged(false, "Disconnected from namespace");
                }
                else if (message.StartsWith("44")) // Socket.IO error event
                {
                    var errorJson = message.Substring(2);
                    _logger.Warning("Socket.IO error: {Error}", errorJson);
                    OnConnectionStatusChanged(false, $"Error: {errorJson}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error processing Socket.IO message: {Message}", message);
            }
        }

        /// <summary>
        /// Process a Socket.IO event message
        /// </summary>
        private async Task ProcessEventMessageAsync(string eventName, JToken? payload)
        {
            try
            {
                _logger.Debug("Processing Socket.IO event: {EventName}", eventName);

                switch (eventName.ToLower())
                {
                    case "message":
                        // This is a signaling message, parse it
                        if (payload is JObject msgPayload)
                        {
                            string type = msgPayload["type"]?.ToString() ?? "";
                            string from = msgPayload["from"]?.ToString() ?? "";
                            JToken? messagePayload = msgPayload["payload"];

                            // Handle different message types
                            await ProcessSignalingMessageAsync(type, from, messagePayload);
                        }
                        break;

                    case "connect-request":
                        // Someone wants to connect to us
                        if (payload is JObject connectPayload)
                        {
                            string clientId = connectPayload["clientId"]?.ToString() ?? "";
                            if (!string.IsNullOrEmpty(clientId))
                            {
                                await HandleConnectionRequestAsync(clientId, connectPayload);
                            }
                        }
                        break;

                    case "registration-success":
                        _logger.Information("Device registration confirmed by server");
                        break;

                    case "error":
                        string errorMessage = payload?.ToString() ?? "Unknown error";
                        _logger.Warning("Received error from server: {Error}", errorMessage);
                        break;

                    default:
                        _logger.Debug("Unhandled Socket.IO event: {EventName}", eventName);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error processing Socket.IO event: {EventName}", eventName);
            }
        }

        /// <summary>
        /// Process a signaling message
        /// </summary>
        private async Task ProcessSignalingMessageAsync(string type, string from, JToken? payload)
        {
            try
            {
                // Handle different message types
                switch (type.ToLower())
                {
                    case "connect":
                        // New client connection request
                        _logger.Information("Connection request from client {ClientId}", from);
                        await HandleConnectionRequestAsync(from, payload);
                        break;

                    case "offer":
                        // WebRTC offer from client
                        _logger.Information("Received WebRTC offer from {ClientId}", from);
                        OnMessageReceived(new SignalingMessageEventArgs(
                            from, SignalingMessageType.Offer, payload ?? new JObject()));
                        break;

                    case "answer":
                        // WebRTC answer from client
                        _logger.Information("Received WebRTC answer from {ClientId}", from);
                        OnMessageReceived(new SignalingMessageEventArgs(
                            from, SignalingMessageType.Answer, payload ?? new JObject()));
                        break;

                    case "ice-candidate":
                        // ICE candidate from client
                        _logger.Debug("Received ICE candidate from {ClientId}", from);
                        OnMessageReceived(new SignalingMessageEventArgs(
                            from, SignalingMessageType.IceCandidate, payload ?? new JObject()));
                        break;

                    case "disconnect":
                        // Client disconnection
                        _logger.Information("Client {ClientId} disconnected", from);
                        OnMessageReceived(new SignalingMessageEventArgs(
                            from, SignalingMessageType.Disconnect, new JObject()));
                        break;

                    case "remote-control-request":
                        // Remote control request from a client
                        _logger.Information("Remote control request from client {ClientId}", from);
                        if (payload != null)
                        {
                            string requestId = payload["requestId"]?.ToString() ?? "";
                            string peerId = payload["peerId"]?.ToString() ?? from;

                            // Auto-accept remote control request
                            await SendControlResponseAsync(requestId, peerId, true);
                        }
                        break;

                    default:
                        // Unknown message type
                        _logger.Warning("Received unknown message type: {Type} from {ClientId}", type, from);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error processing signaling message of type {Type} from {From}", type, from);
            }
        }

        /// <summary>
        /// Send control response to client
        /// </summary>
        private async Task SendControlResponseAsync(string requestId, string peerId, bool accepted)
        {
            try
            {
                var response = new
                {
                    type = "control-response",
                    requestId = requestId,
                    accepted = accepted,
                    peerId = peerId
                };

                // Create Socket.IO event message
                var eventData = new object[]
                {
                    "control-response", // Event name
                    response           // Event data
                };

                string eventJson = JsonConvert.SerializeObject(eventData);
                await SendSocketIOMessage("42" + eventJson); // 42 = Socket.IO event packet

                _logger.Information("Sent control response to {PeerId}: {Accepted}", peerId, accepted);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error sending control response");
            }
        }

        /// <summary>
        /// Handle a connection request from a client
        /// </summary>
        private async Task HandleConnectionRequestAsync(string clientId, JToken? payload)
        {
            // Create a new session for this client
            var clientIp = GetClientIpFromPayload(payload);
            var session = new UserSession(clientId, clientIp);

            // Notify about the new session
            OnSessionCreated(session);

            // Automatically accept the connection request for now
            var response = new
            {
                type = "connect-response",
                to = clientId,
                from = _hostId,
                payload = new
                {
                    accepted = true,
                    hostId = _hostId,
                    systemName = Environment.MachineName
                }
            };

            // Create Socket.IO event message
            var eventData = new object[]
            {
                "message", // Event name
                response  // Event data
            };

            string eventJson = JsonConvert.SerializeObject(eventData);
            await SendSocketIOMessage("42" + eventJson); // 42 = Socket.IO event packet
        }

        /// <summary>
        /// Extract client IP from connection payload if available
        /// </summary>
        private static string GetClientIpFromPayload(JToken? payload)
        {
            if (payload != null && payload["ip"] != null)
            {
                return payload["ip"]!.ToString();
            }

            return "unknown";
        }

        /// <summary>
        /// Trigger the MessageReceived event
        /// </summary>
        private void OnMessageReceived(SignalingMessageEventArgs args)
        {
            MessageReceived?.Invoke(this, args);
        }

        /// <summary>
        /// Trigger the ConnectionStatusChanged event
        /// </summary>
        private void OnConnectionStatusChanged(bool isConnected, string? errorMessage = null)
        {
            ConnectionStatusChanged?.Invoke(this, new ConnectionStatusEventArgs(isConnected, errorMessage));
        }

        /// <summary>
        /// Trigger the SessionCreated event
        /// </summary>
        private void OnSessionCreated(UserSession session)
        {
            SessionCreated?.Invoke(this, session);
        }

        /// <summary>
        /// Clean up resources
        /// </summary>
        public void Dispose()
        {
            // Unsubscribe from network status events
            _networkStatusService.NetworkStatusChanged -= OnNetworkStatusChanged;

            // Stop timers
            _heartbeatTimer?.Dispose();

            // Disconnect if connected
            if (IsConnected)
            {
                _isDisconnecting = true;
                DisconnectAsync().Wait(1000);
            }

            // Clean up resources
            _webSocket?.Dispose();
            _cts?.Dispose();
            _sendLock.Dispose();
        }
    }
}