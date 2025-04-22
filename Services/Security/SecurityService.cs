using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Serilog;
using Wynzio.Utilities;

namespace Wynzio.Services.Security
{
    /// <summary>
    /// Service for handling security-related operations
    /// </summary>
    internal class SecurityService : ISecurityService
    {
        private readonly ILogger _logger;
        // Initialize with empty dictionary
        private readonly Dictionary<string, DateTime> _validConnections = new Dictionary<string, DateTime>();

        // Security settings
        private const string EncryptionKey = "Wynzio-SecureRemoteAccess-Key";
        private const int ConnectionValidDurationMinutes = 60;

        public SecurityService()
        {
            _logger = Log.ForContext<SecurityService>();
        }

        /// <summary>
        /// Encrypt data using AES encryption
        /// </summary>
        /// <param name="data">Plain text data to encrypt</param>
        /// <returns>Encrypted data as Base64 string</returns>
        public string EncryptData(string data)
        {
            if (string.IsNullOrEmpty(data))
                return string.Empty;

            try
            {
                return EncryptionHelper.Encrypt(data, EncryptionKey);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error encrypting data");
                return string.Empty;
            }
        }

        /// <summary>
        /// Decrypt AES-encrypted data
        /// </summary>
        /// <param name="encryptedData">Base64-encoded encrypted data</param>
        /// <returns>Decrypted plain text</returns>
        public string DecryptData(string encryptedData)
        {
            if (string.IsNullOrEmpty(encryptedData))
                return string.Empty;

            try
            {
                return EncryptionHelper.Decrypt(encryptedData, EncryptionKey);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error decrypting data");
                return string.Empty;
            }
        }

        /// <summary>
        /// Validate a connection based on connection ID
        /// </summary>
        /// <param name="connectionId">Connection ID to validate</param>
        /// <returns>True if connection is valid, false otherwise</returns>
        public bool ValidateConnection(string connectionId)
        {
            // Validate input
            if (string.IsNullOrEmpty(connectionId))
                return false;

            // Check if connection exists and is still valid
            if (_validConnections.TryGetValue(connectionId, out DateTime validUntil))
            {
                if (validUntil > DateTime.Now)
                {
                    // Connection is still valid
                    _logger.Debug("Connection {ConnectionId} is valid", connectionId);
                    return true;
                }
                else
                {
                    // Connection has expired
                    _logger.Information("Connection {ConnectionId} has expired", connectionId);
                    _validConnections.Remove(connectionId);
                    return false;
                }
            }

            // Connection does not exist
            _logger.Warning("Invalid connection attempt with ID {ConnectionId}", connectionId);
            return false;
        }

        /// <summary>
        /// Register a new connection
        /// </summary>
        /// <param name="connectionId">Connection ID to register</param>
        public void RegisterConnection(string connectionId)
        {
            if (string.IsNullOrEmpty(connectionId))
                return;

            // Set connection validity period
            _validConnections[connectionId] = DateTime.Now.AddMinutes(ConnectionValidDurationMinutes);
            _logger.Information("Registered connection {ConnectionId}, valid for {Minutes} minutes",
                connectionId, ConnectionValidDurationMinutes);
        }

        /// <summary>
        /// Generate a secure connection token
        /// </summary>
        /// <returns>New connection token</returns>
        public string GenerateConnectionToken()
        {
            // Generate random ID
            string token = EncryptionHelper.GenerateRandomString(32);

            // Register connection
            RegisterConnection(token);

            return token;
        }

        /// <summary>
        /// Compute hash of a string using SHA-256
        /// </summary>
        /// <param name="input">Input string</param>
        /// <returns>Lowercase hex string of hash</returns>
        public static string ComputeHash(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            // Use HashData instead of creating a HashAlgorithm instance
            byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));

            var builder = new StringBuilder();
            for (int i = 0; i < bytes.Length; i++)
            {
                builder.Append(bytes[i].ToString("x2"));
            }

            return builder.ToString();
        }

        /// <summary>
        /// Revoke a connection
        /// </summary>
        /// <param name="connectionId">Connection ID to revoke</param>
        public void RevokeConnection(string connectionId)
        {
            if (string.IsNullOrEmpty(connectionId))
                return;

            // Remove method already checks if key exists, no need for ContainsKey check
            _validConnections.Remove(connectionId);
            _logger.Information("Revoked connection {ConnectionId}", connectionId);
        }
    }
}