using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Wynzio.Utilities
{
    /// <summary>
    /// Helper class for encryption and decryption operations
    /// </summary>
    internal static class EncryptionHelper
    {
        // Use a unique salt for this application
        private static readonly byte[] Salt = new byte[]
        {
            0x49, 0x76, 0x61, 0x6e, 0x20, 0x4d, 0x65, 0x64, 0x76, 0x65, 0x64, 0x65, 0x76, 0x57, 0x79, 0x6e
        };

        // Iteration count for key derivation
        private const int Iterations = 10000;

        // Key size for AES encryption
        private const int KeySize = 256;

        /// <summary>
        /// Encrypt a string with a password
        /// </summary>
        /// <param name="plainText">Text to encrypt</param>
        /// <param name="password">Password for encryption</param>
        /// <returns>Base64-encoded encrypted string</returns>
        public static string Encrypt(string plainText, string password)
        {
            if (string.IsNullOrEmpty(plainText))
                return string.Empty;

            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
            byte[] cipherBytes;

            using (var aes = Aes.Create())
            {
                aes.KeySize = KeySize;

                using var deriveBytes = new Rfc2898DeriveBytes(
                    password, Salt, Iterations, HashAlgorithmName.SHA256);

                aes.Key = deriveBytes.GetBytes(aes.KeySize / 8);
                aes.IV = deriveBytes.GetBytes(aes.BlockSize / 8);

                using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
                using var ms = new MemoryStream();
                using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                {
                    cs.Write(plainBytes, 0, plainBytes.Length);
                    cs.FlushFinalBlock();
                }

                cipherBytes = ms.ToArray();
            }

            return Convert.ToBase64String(cipherBytes);
        }

        /// <summary>
        /// Decrypt a string with a password
        /// </summary>
        /// <param name="cipherText">Encrypted text (Base64-encoded)</param>
        /// <param name="password">Password for decryption</param>
        /// <returns>Decrypted string</returns>
        public static string Decrypt(string cipherText, string password)
        {
            if (string.IsNullOrEmpty(cipherText))
                return string.Empty;

            try
            {
                byte[] cipherBytes = Convert.FromBase64String(cipherText);
                byte[] plainBytes;

                using (var aes = Aes.Create())
                {
                    aes.KeySize = KeySize;

                    using var deriveBytes = new Rfc2898DeriveBytes(
                        password, Salt, Iterations, HashAlgorithmName.SHA256);

                    aes.Key = deriveBytes.GetBytes(aes.KeySize / 8);
                    aes.IV = deriveBytes.GetBytes(aes.BlockSize / 8);

                    using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
                    using var ms = new MemoryStream(cipherBytes);
                    using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
                    using var resultMs = new MemoryStream();

                    cs.CopyTo(resultMs);
                    plainBytes = resultMs.ToArray();
                }

                return Encoding.UTF8.GetString(plainBytes);
            }
            catch (Exception)
            {
                // Return empty string on decryption failure
                return string.Empty;
            }
        }

        /// <summary>
        /// Generate an encryption key based on machine-specific information
        /// </summary>
        /// <returns>Encryption key</returns>
        public static string GenerateEncryptionKey()
        {
            try
            {
                // Get hardware information from system environment
                var processorId = Environment.ProcessorCount.ToString();
                var machineGuid = Environment.MachineName;
                var osVersion = Environment.OSVersion.ToString();

                // Combine hardware info with application-specific data
                var combined = $"Wynzio-{processorId}-{machineGuid}-{osVersion}";

                // Generate key using SHA256
                byte[] keyBytes = SHA256.HashData(Encoding.UTF8.GetBytes(combined));

                // Convert to Base64 string
                return Convert.ToBase64String(keyBytes);
            }
            catch (Exception)
            {
                // Fallback to static key if machine info can't be accessed
                return "Wynzio-SecureRemoteAccess-FallbackKey-2025";
            }
        }

        /// <summary>
        /// Generate a secure random string with the specified length
        /// </summary>
        /// <param name="length">Length of the string to generate</param>
        /// <returns>Secure random string</returns>
        public static string GenerateRandomString(int length)
        {
            const string allowedChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

            // Create byte array for the random bytes
            byte[] randomBytes = new byte[length];

            // Fill the array with random bytes
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(randomBytes);
            }

            // Create the output string
            var result = new StringBuilder(length);

            // Convert random bytes to characters from allowed set
            foreach (byte b in randomBytes)
            {
                result.Append(allowedChars[b % allowedChars.Length]);
            }

            return result.ToString();
        }

        /// <summary>
        /// Generate a secure random host ID
        /// </summary>
        /// <returns>Random host ID string (16 characters)</returns>
        public static string GenerateHostId()
        {
            return GenerateRandomString(16);
        }

        /// <summary>
        /// Generate a secure random session ID
        /// </summary>
        /// <returns>Random session ID string (32 characters)</returns>
        public static string GenerateSessionId()
        {
            return GenerateRandomString(32);
        }
    }
}