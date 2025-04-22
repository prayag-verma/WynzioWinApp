using System;
using Newtonsoft.Json;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using Wynzio.Utilities;

namespace Wynzio.Models
{
    /// <summary>
    /// Stores connection settings for the application
    /// </summary>
    internal class ConnectionSettings : ObservableObject
    {
        private const string SettingsFileName = "connection.config";
        private static readonly string SettingsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Wynzio",
            SettingsFileName);

        private string _hostId = string.Empty;
        private string _signalServer = "wss://wynzio.com/socket.io";
        private bool _autoConnect = true;
        private string _stunServer = "stun:stun.l.google.com:19302";
        private string _turnServer = string.Empty;
        private string _turnUsername = string.Empty;
        private string _turnPassword = string.Empty;
        private int _captureFrameRate = 20;
        private int _captureQuality = 75;
        private string _apiKey = "3f7a9b25e8d146c0b2f15a6d90e74c8d";
        private string _apiUrl = "https://wynzio.com";
        private bool _useSecureConnection = true;
        private int _reconnectInterval = 30;
        private int _heartbeatInterval = 60;
        private int _maxReconnectAttempts = 10;
        private int _connectionTimeout = 10000;
        private string _systemName = string.Empty;

        /// <summary>
        /// Unique identifier for this host
        /// </summary>
        public string HostId
        {
            get => _hostId;
            set => SetProperty(ref _hostId, value);
        }

        /// <summary>
        /// System name for this host (computer name by default)
        /// </summary>
        public string SystemName
        {
            get => string.IsNullOrEmpty(_systemName) ? Environment.MachineName : _systemName;
            set => SetProperty(ref _systemName, value);
        }

        /// <summary>
        /// Signaling server WebSocket URL
        /// </summary>
        public string SignalServer
        {
            get => _signalServer;
            set => SetProperty(ref _signalServer, value);
        }

        /// <summary>
        /// Automatically connect on startup
        /// </summary>
        public bool AutoConnect
        {
            get => _autoConnect;
            set => SetProperty(ref _autoConnect, value);
        }

        /// <summary>
        /// STUN server address
        /// </summary>
        public string StunServer
        {
            get => _stunServer;
            set => SetProperty(ref _stunServer, value);
        }

        /// <summary>
        /// TURN server address
        /// </summary>
        public string TurnServer
        {
            get => _turnServer;
            set => SetProperty(ref _turnServer, value);
        }

        /// <summary>
        /// TURN server username
        /// </summary>
        public string TurnUsername
        {
            get => _turnUsername;
            set => SetProperty(ref _turnUsername, value);
        }

        /// <summary>
        /// TURN server password
        /// </summary>
        public string TurnPassword
        {
            get => _turnPassword;
            set => SetProperty(ref _turnPassword, value);
        }

        /// <summary>
        /// Screen capture frame rate
        /// </summary>
        public int CaptureFrameRate
        {
            get => _captureFrameRate;
            set => SetProperty(ref _captureFrameRate, value);
        }

        /// <summary>
        /// Screen capture quality (1-100)
        /// </summary>
        public int CaptureQuality
        {
            get => _captureQuality;
            set => SetProperty(ref _captureQuality, value);
        }

        /// <summary>
        /// API key for authenticating with the server
        /// </summary>
        public string ApiKey
        {
            get => _apiKey;
            set => SetProperty(ref _apiKey, value);
        }

        /// <summary>
        /// API endpoint base URL
        /// </summary>
        public string ApiUrl
        {
            get => _apiUrl;
            set => SetProperty(ref _apiUrl, value);
        }

        /// <summary>
        /// Whether to use secure connections (HTTPS/WSS)
        /// </summary>
        public bool UseSecureConnection
        {
            get => _useSecureConnection;
            set => SetProperty(ref _useSecureConnection, value);
        }

        /// <summary>
        /// Reconnect interval in seconds
        /// </summary>
        public int ReconnectInterval
        {
            get => _reconnectInterval;
            set => SetProperty(ref _reconnectInterval, value);
        }

        /// <summary>
        /// Heartbeat interval in seconds
        /// </summary>
        public int HeartbeatInterval
        {
            get => _heartbeatInterval;
            set => SetProperty(ref _heartbeatInterval, value);
        }

        /// <summary>
        /// Maximum number of reconnection attempts
        /// </summary>
        public int MaxReconnectAttempts
        {
            get => _maxReconnectAttempts;
            set => SetProperty(ref _maxReconnectAttempts, value);
        }

        /// <summary>
        /// Connection timeout in milliseconds
        /// </summary>
        public int ConnectionTimeout
        {
            get => _connectionTimeout;
            set => SetProperty(ref _connectionTimeout, value);
        }

        /// <summary>
        /// Save settings to file
        /// </summary>
        public void Save()
        {
            try
            {
                string? directoryPath = Path.GetDirectoryName(SettingsFilePath);
                if (!string.IsNullOrEmpty(directoryPath) && !Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }

                string json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(SettingsFilePath, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                // Log the exception
                System.Diagnostics.Debug.WriteLine($"Error saving settings: {ex.Message}");
            }
        }

        /// <summary>
        /// Load settings from file
        /// </summary>
        /// <returns>Loaded settings or default settings if file not found</returns>
        public static ConnectionSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath, Encoding.UTF8);
                    var settings = JsonConvert.DeserializeObject<ConnectionSettings>(json);
                    if (settings != null)
                    {
                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                // Log the exception
                System.Diagnostics.Debug.WriteLine($"Error loading settings: {ex.Message}");
            }

            // Return default settings if file doesn't exist or error occurred
            var defaultSettings = new ConnectionSettings();

            // Generate and save a new host ID if none exists
            if (string.IsNullOrEmpty(defaultSettings.HostId))
            {
                defaultSettings.HostId = GenerateNewHostId();
                defaultSettings.Save();
            }

            return defaultSettings;
        }

        /// <summary>
        /// Generate a new secure random host ID
        /// </summary>
        /// <returns>A new host ID</returns>
        private static string GenerateNewHostId()
        {
            return EncryptionHelper.GenerateHostId();
        }
    }
}