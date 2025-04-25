using System;
using System.Windows;
using System.Windows.Input;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using Wynzio.Models;
using Wynzio.Services.Capture;
using Wynzio.Services.Input;
using Wynzio.Services.Network;
using Wynzio.Services.Security;
using Wynzio.Utilities;

namespace Wynzio.ViewModels
{
    /// <summary>
    /// Main view model for the application
    /// </summary>
    internal class MainViewModel : ObservableObject, IDisposable
    {
        private readonly ILogger _logger;
        private readonly IScreenCaptureService _captureService;
        private readonly IInputService _inputService;
        private readonly ISignalingService _signalingService;
        private readonly ISecurityService _securityService;
        private readonly IWebRTCService _webRTCService;
        private readonly AutoStartManager _autoStartManager;

        private readonly ConnectionSettings _settings;
        private bool _isConnected;
        private bool _isCapturing;
        private bool _isAutoStartEnabled;
        private string _statusMessage = "Disconnected";
        private string _remotePcId = "";
        private string _activeClientId = "";

        /// <summary>
        /// Whether the application is connected to the signaling server
        /// </summary>
        public bool IsConnected
        {
            get => _isConnected;
            private set => SetProperty(ref _isConnected, value);
        }

        /// <summary>
        /// Whether the screen is being captured
        /// </summary>
        public bool IsCapturing
        {
            get => _isCapturing;
            private set => SetProperty(ref _isCapturing, value);
        }

        /// <summary>
        /// Whether auto-start is enabled
        /// </summary>
        public bool IsAutoStartEnabled
        {
            get => _isAutoStartEnabled;
            set
            {
                if (SetProperty(ref _isAutoStartEnabled, value))
                {
                    if (value)
                        _autoStartManager.EnableAutoStart();
                    else
                        _autoStartManager.DisableAutoStart();
                }
            }
        }

        /// <summary>
        /// Status message to display
        /// </summary>
        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        /// <summary>
        /// Host ID for this computer
        /// </summary>
        public string RemotePcId
        {
            get => _remotePcId;
            private set => SetProperty(ref _remotePcId, value);
        }

        /// <summary>
        /// Currently active client ID
        /// </summary>
        public string ActiveClientId
        {
            get => _activeClientId;
            private set => SetProperty(ref _activeClientId, value);
        }

        /// <summary>
        /// Command to start the service
        /// </summary>
        public ICommand StartCommand { get; }

        /// <summary>
        /// Command to stop the service
        /// </summary>
        public ICommand StopCommand { get; }

        /// <summary>
        /// Command to exit the application
        /// </summary>
        public ICommand ExitCommand { get; }

        /// <summary>
        /// Command to toggle auto-start
        /// </summary>
        public ICommand ToggleAutoStartCommand { get; }

        public MainViewModel(
            IScreenCaptureService captureService,
            IInputService inputService,
            ISignalingService signalingService,
            ISecurityService securityService,
            IWebRTCService webRTCService)
        {
            _logger = Log.ForContext<MainViewModel>();
            _captureService = captureService;
            _inputService = inputService;
            _signalingService = signalingService;
            _securityService = securityService;
            _webRTCService = webRTCService;
            _autoStartManager = new AutoStartManager();

            // Load settings
            _settings = ConnectionSettings.Load();
            _remotePcId = _settings.RemotePcId;

            // Initialize auto-start state
            _isAutoStartEnabled = _autoStartManager.IsAutoStartEnabled();

            // Create commands
            StartCommand = new RelayCommand(StartService, () => !IsConnected);
            StopCommand = new RelayCommand(StopService, () => IsConnected);
            ExitCommand = new RelayCommand(ExitApplication);
            ToggleAutoStartCommand = new RelayCommand(() => IsAutoStartEnabled = !IsAutoStartEnabled);

            // Wire up events
            _signalingService.ConnectionStatusChanged += OnConnectionStatusChanged;
            _signalingService.SessionCreated += OnSessionCreated;

            // Subscribe to WebRTC events
            _webRTCService.ConnectionEstablished += OnWebRTCConnectionEstablished;
            _webRTCService.ConnectionClosed += OnWebRTCConnectionClosed;
            _webRTCService.DataChannelMessageReceived += OnDataChannelMessageReceived;

            // Auto-start service after a short delay
            if (_settings.AutoConnect)
            {
                // Use a short delay to allow UI to initialize
                Task.Run(async () => {
                    await Task.Delay(1000);
                    Application.Current.Dispatcher.Invoke(StartService);
                });
            }
        }

        /// <summary>
        /// Start the service
        /// </summary>
        private async void StartService()
        {
            try
            {
                StatusMessage = "Connecting...";

                // Connect to signaling server
                await _signalingService.ConnectAsync(_settings.SignalServer, _settings.RemotePcId);

                // Initialize WebRTC service
                _webRTCService.Initialize();

                // Start screen capture
                await _captureService.StartCaptureAsync(
                    frameRate: _settings.CaptureFrameRate,
                    quality: _settings.CaptureQuality);

                // Enable input service
                _inputService.EnableInput();

                IsCapturing = true;
                StatusMessage = "Connected";
                _logger.Information("Service started successfully");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                _logger.Error(ex, "Error starting service");
            }
        }

        /// <summary>
        /// Stop the service
        /// </summary>
        private async void StopService()
        {
            try
            {
                StatusMessage = "Disconnecting...";

                // Close all WebRTC connections
                await _webRTCService.CloseAllConnectionsAsync();

                // Stop screen capture
                _captureService.StopCapture();

                // Disable input service
                _inputService.DisableInput();

                // Disconnect from signaling server
                await _signalingService.DisconnectAsync();

                IsCapturing = false;
                StatusMessage = "Disconnected";
                _logger.Information("Service stopped successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error stopping service");
            }
        }

        /// <summary>
        /// Exit the application
        /// </summary>
        private void ExitApplication()
        {
            // First stop the service if it's running
            if (IsConnected)
            {
                StopService();
            }

            // Exit the application
            Application.Current.Shutdown();
        }

        /// <summary>
        /// Handle connection status changes
        /// </summary>
        private void OnConnectionStatusChanged(object? sender, ConnectionStatusEventArgs e)
        {
            IsConnected = e.IsConnected;
            StatusMessage = e.IsConnected ? "Connected" : e.ErrorMessage ?? "Disconnected";

            // Command can execute changed
            CommandManager.InvalidateRequerySuggested();
        }

        /// <summary>
        /// Handle session creation
        /// </summary>
        private void OnSessionCreated(object? sender, UserSession session)
        {
            _logger.Information("New session created: {SessionId} for client {ClientId}",
                session.SessionId, session.ClientId);

            // Store the active client ID
            ActiveClientId = session.ClientId;
        }

        /// <summary>
        /// Handle WebRTC connection established
        /// </summary>
        private void OnWebRTCConnectionEstablished(object? sender, string peerId)
        {
            _logger.Information("WebRTC connection established with peer {PeerId}", peerId);
            StatusMessage = $"Connected to client {peerId}";

            // Store active client ID
            ActiveClientId = peerId;
        }

        /// <summary>
        /// Handle WebRTC connection closed
        /// </summary>
        private void OnWebRTCConnectionClosed(object? sender, string peerId)
        {
            _logger.Information("WebRTC connection closed with peer {PeerId}", peerId);

            // Clear active client ID if it matches the closed connection
            if (ActiveClientId == peerId)
            {
                ActiveClientId = "";
            }

            StatusMessage = IsConnected ? "Connected" : "Disconnected";
        }

        /// <summary>
        /// Handle WebRTC data channel message received
        /// </summary>
        private async void OnDataChannelMessageReceived(object? sender, DataChannelMessageEventArgs e)
        {
            try
            {
                // Process input commands
                await _inputService.ProcessInputCommandAsync(e.Message);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error processing data channel message");
            }
        }

        /// <summary>
        /// Clean up resources
        /// </summary>
        public void Dispose()
        {
            // Unsubscribe from events
            _signalingService.ConnectionStatusChanged -= OnConnectionStatusChanged;
            _signalingService.SessionCreated -= OnSessionCreated;

            // Unsubscribe from WebRTC events
            _webRTCService.ConnectionEstablished -= OnWebRTCConnectionEstablished;
            _webRTCService.ConnectionClosed -= OnWebRTCConnectionClosed;
            _webRTCService.DataChannelMessageReceived -= OnDataChannelMessageReceived;

            // Dispose WebRTC service
            if (_webRTCService is IDisposable disposableWebRTC)
            {
                disposableWebRTC.Dispose();
            }

            // Stop the service if running
            if (IsConnected)
            {
                StopService();
            }
        }
    }
}