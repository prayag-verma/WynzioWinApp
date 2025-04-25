using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Wynzio.Models;

namespace Wynzio.Utilities
{
    /// <summary>
    /// Manages session persistence with encrypted storage
    /// </summary>
    internal class SessionManager
    {
        private const string SessionFileName = "session.dat";
        private static readonly string SessionFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Wynzio",
            SessionFileName);

        /// <summary>
        /// Session data structure
        /// </summary>
        public class SessionData
        {
            public string Sid { get; set; } = string.Empty;
            public long Timestamp { get; set; }
        }

        /// <summary>
        /// Save session data with encryption
        /// </summary>
        /// <param name="sid">Socket.IO session ID</param>
        public static void SaveSession(string sid)
        {
            try
            {
                if (string.IsNullOrEmpty(sid))
                {
                    System.Diagnostics.Debug.WriteLine("Cannot save empty session ID");
                    return;
                }

                string? directoryPath = Path.GetDirectoryName(SessionFilePath);
                if (!string.IsNullOrEmpty(directoryPath) && !Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }

                var sessionData = new SessionData
                {
                    Sid = sid,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                // Convert to JSON
                string json = JsonConvert.SerializeObject(sessionData);

                // Encrypt the JSON string
                string encryptedData = EncryptionHelper.Encrypt(json, EncryptionHelper.GenerateEncryptionKey());

                // Write encrypted data to file
                File.WriteAllText(SessionFilePath, encryptedData, Encoding.UTF8);

                System.Diagnostics.Debug.WriteLine($"Session saved successfully: {sid}");
            }
            catch (Exception ex)
            {
                // Log the exception
                System.Diagnostics.Debug.WriteLine($"Error saving session: {ex.Message}");
            }
        }

        /// <summary>
        /// Load session data with decryption
        /// </summary>
        /// <returns>Session data or null if not found or invalid</returns>
        public static SessionData? LoadSession()
        {
            try
            {
                if (File.Exists(SessionFilePath))
                {
                    // Read encrypted data
                    string encryptedData = File.ReadAllText(SessionFilePath, Encoding.UTF8);

                    // Decrypt the data
                    string json = EncryptionHelper.Decrypt(encryptedData, EncryptionHelper.GenerateEncryptionKey());

                    if (!string.IsNullOrEmpty(json))
                    {
                        // Deserialize JSON to session data
                        var sessionData = JsonConvert.DeserializeObject<SessionData>(json);
                        System.Diagnostics.Debug.WriteLine($"Session loaded successfully: {sessionData?.Sid}");
                        return sessionData;
                    }
                }
            }
            catch (Exception ex)
            {
                // Log the exception
                System.Diagnostics.Debug.WriteLine($"Error loading session: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Check if session is valid (not expired)
        /// </summary>
        /// <param name="session">Session data to validate</param>
        /// <returns>True if session is valid</returns>
        public static bool IsSessionValid(SessionData? session)
        {
            if (session == null) return false;

            // Check if session is older than 24 hours
            long currentTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long sessionAge = currentTime - session.Timestamp;

            bool isValid = !string.IsNullOrEmpty(session.Sid) && sessionAge < 86400000;
            System.Diagnostics.Debug.WriteLine($"Session validity check: {isValid}, Age: {sessionAge / 1000} seconds");

            return isValid;
        }

        /// <summary>
        /// Clear the session file
        /// </summary>
        public static void ClearSession()
        {
            try
            {
                if (File.Exists(SessionFilePath))
                {
                    File.Delete(SessionFilePath);
                    System.Diagnostics.Debug.WriteLine("Session cleared successfully");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error clearing session: {ex.Message}");
            }
        }
    }
}