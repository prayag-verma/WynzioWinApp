using System;
using Newtonsoft.Json;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using Wynzio.Utilities;
using System.Runtime.InteropServices;

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

        private string _remotePcId = string.Empty;
        private string _signalServer = "wss://wynzio.com/signal";
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
        private string _osName = string.Empty;
        private string _osVersion = string.Empty;

        /// <summary>
        /// Unique identifier for this remote PC
        /// </summary>
        public string RemotePcId
        {
            get => _remotePcId;
            set => SetProperty(ref _remotePcId, value);
        }

        /// <summary>
        /// System name for this remote PC (computer name by default)
        /// </summary>
        public string SystemName
        {
            get => string.IsNullOrEmpty(_systemName) ? Environment.MachineName : _systemName;
            set => SetProperty(ref _systemName, value);
        }

        /// <summary>
        /// OS Name (Windows edition)
        /// </summary>
        public string OSName
        {
            get
            {
                if (string.IsNullOrEmpty(_osName))
                {
                    try
                    {
                        if (OperatingSystem.IsWindows())
                        {
                            _osName = GetWindowsEdition();
                        }
                        else
                        {
                            _osName = Environment.OSVersion.Platform.ToString();
                        }
                    }
                    catch
                    {
                        _osName = "Windows";
                    }
                }
                return _osName;
            }
            set => SetProperty(ref _osName, value);
        }

        /// <summary>
        /// OS Version
        /// </summary>
        public string OSVersion
        {
            get
            {
                if (string.IsNullOrEmpty(_osVersion))
                {
                    _osVersion = Environment.OSVersion.Version.ToString();
                }
                return _osVersion;
            }
            set => SetProperty(ref _osVersion, value);
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

            // Generate and save a new remote PC ID if none exists
            if (string.IsNullOrEmpty(defaultSettings.RemotePcId))
            {
                defaultSettings.RemotePcId = GenerateNewRemotePcId();
                defaultSettings.Save();
            }

            return defaultSettings;
        }

        /// <summary>
        /// Generate a new secure random remote PC ID
        /// </summary>
        /// <returns>A new remote PC ID</returns>
        private static string GenerateNewRemotePcId()
        {
            return EncryptionHelper.GenerateHostId();
        }

        /// <summary>
        /// Get Windows edition name
        /// </summary>
        /// <returns>Windows edition (e.g., "Windows 11 Home")</returns>
        private static string GetWindowsEdition()
        {
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    // This is Windows-specific code that should be protected by the platform check
                    string? productName = Microsoft.Win32.Registry.GetValue(
                        @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion",
                        "ProductName", "Windows")?.ToString();

                    return !string.IsNullOrEmpty(productName) ? productName : "Windows";
                }
                catch
                {
                    return "Windows";
                }
            }
            else
            {
                // For non-Windows systems
                return Environment.OSVersion.Platform.ToString();
            }
        }
    }
}