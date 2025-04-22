using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SIPSorceryMedia.Abstractions;
using System.IO;
using SIPSorceryMedia.Windows;
using SIPSorceryMedia.Encoders;

namespace Wynzio.Services.Capture
{
    /// <summary>
    /// Implementation of screen capture service using Windows Graphics Capture API
    /// </summary>
    internal class CaptureService : IScreenCaptureService
    {
        // Event for notifying when a frame is captured
        public event EventHandler<FrameCapturedEventArgs>? FrameCaptured;

        // Capture state
        private bool _isCapturing;
        private CancellationTokenSource? _captureCts;
        private Task? _captureTask;

        // Frame parameters
        private int _frameRate = 20;
        private int _quality = 75;
        private Rectangle _captureRegion;

        /// <summary>
        /// Whether the service is currently capturing
        /// </summary>
        public bool IsCapturing => _isCapturing;

        public CaptureService()
        {
            // Initialize with default parameters
            _captureRegion = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        }

        /// <summary>
        /// Start the screen capture process
        /// </summary>
        public async Task StartCaptureAsync(Rectangle? captureRegion = null, int frameRate = 20, int quality = 75, CancellationToken cancellationToken = default)
        {
            // If already capturing, stop first
            if (_isCapturing)
            {
                StopCapture();
            }

            // Set capture parameters
            _captureRegion = captureRegion ?? (Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080));
            _frameRate = frameRate;
            _quality = quality;

            // Create cancellation token source
            _captureCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // Start capture in background task
            _isCapturing = true;
            _captureTask = Task.Run(() => CaptureLoop(_captureCts.Token), _captureCts.Token);

            // Wait for capture to start
            await Task.Delay(100, cancellationToken);
        }

        /// <summary>
        /// Stop the screen capture process
        /// </summary>
        public void StopCapture()
        {
            if (!_isCapturing)
                return;

            // Signal capture loop to stop
            _captureCts?.Cancel();

            // Wait for capture task to finish
            try
            {
                _captureTask?.Wait(1000);
            }
            catch (AggregateException)
            {
                // Ignore cancellation exceptions
            }

            // Update state
            _isCapturing = false;
            _captureCts?.Dispose();
            _captureCts = null;
            _captureTask = null;
        }

        /// <summary>
        /// Get information about all available displays
        /// </summary>
        public DisplayInfo[] GetAvailableDisplays()
        {
            var screens = Screen.AllScreens;
            var displays = new DisplayInfo[screens.Length];

            for (int i = 0; i < screens.Length; i++)
            {
                var screen = screens[i];
                displays[i] = new DisplayInfo(
                    id: $"display{i}",
                    name: screen.DeviceName,
                    isPrimary: screen.Primary,
                    bounds: screen.Bounds);
            }

            return displays;
        }

        /// <summary>
        /// Main capture loop running in background
        /// </summary>
        private async Task CaptureLoop(CancellationToken cancellationToken)
        {
            try
            {
                // Initialize frame capture
                var frameInterval = TimeSpan.FromMilliseconds(1000.0 / _frameRate);
                DateTime nextCaptureTime = DateTime.Now;

                // Create video encoder - using VP8 encoder from SIPSorceryMedia.Encoders
                var encoder = new VpxVideoEncoder();

                while (!cancellationToken.IsCancellationRequested)
                {
                    // Capture screen
                    using (var bitmap = CaptureScreen())
                    {
                        if (bitmap != null)
                        {
                            // Convert to appropriate format for WebRTC
                            var frameBytes = EncodeFrame(bitmap, _quality, encoder);

                            // Raise event with encoded frame
                            var frameArgs = new FrameCapturedEventArgs(
                                frameBytes,
                                bitmap.Width,
                                bitmap.Height,
                                DateTime.Now);

                            FrameCaptured?.Invoke(this, frameArgs);
                        }
                    }

                    // Wait until next frame time
                    nextCaptureTime = nextCaptureTime.Add(frameInterval);
                    var delayTime = nextCaptureTime - DateTime.Now;
                    if (delayTime > TimeSpan.Zero)
                    {
                        await Task.Delay(delayTime, cancellationToken);
                    }
                    else
                    {
                        // If we're falling behind, reset next capture time
                        nextCaptureTime = DateTime.Now;
                    }
                }

                // Clean up encoder
                encoder.Dispose();
            }
            catch (OperationCanceledException)
            {
                // Capture was cancelled, exit normally
            }
            catch (Exception ex)
            {
                // Log the error
                System.Diagnostics.Debug.WriteLine($"Screen capture error: {ex.Message}");
            }
            finally
            {
                _isCapturing = false;
            }
        }

        /// <summary>
        /// Capture the screen as a bitmap
        /// </summary>
        private Bitmap? CaptureScreen()
        {
            try
            {
                // Create bitmap with screen dimensions
                var bitmap = new Bitmap(_captureRegion.Width, _captureRegion.Height);

                // Create graphics context and capture screen content
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(
                        _captureRegion.X,
                        _captureRegion.Y,
                        0, 0,
                        _captureRegion.Size,
                        CopyPixelOperation.SourceCopy);
                }

                return bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error capturing screen: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Encode a bitmap frame to compressed format
        /// </summary>
        private static byte[] EncodeFrame(Bitmap bitmap, int quality, VpxVideoEncoder encoder)
        {
            try
            {
                // Convert bitmap to byte array in BGR format
                var bitmapData = bitmap.LockBits(
                    new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                    System.Drawing.Imaging.ImageLockMode.ReadOnly,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                int stride = bitmapData.Stride;
                int length = stride * bitmap.Height;
                byte[] bgrData = new byte[length];

                // Copy the bitmap data
                Marshal.Copy(bitmapData.Scan0, bgrData, 0, length);
                bitmap.UnlockBits(bitmapData);

                // Try to use VP8 encoding
                try
                {
                    // Call ForceKeyFrame method - it's a method, not a property
                    encoder.ForceKeyFrame();

                    // Encode frame using VP8 - correct parameter order for VpxVideoEncoder
                    var encodedFrame = encoder.EncodeVideo(
                        bitmap.Width,
                        bitmap.Height,
                        bgrData,
                        VideoPixelFormatsEnum.Bgra,
                        VideoCodecsEnum.VP8);

                    if (encodedFrame != null && encodedFrame.Length > 0)
                    {
                        return encodedFrame;
                    }
                }
                catch (Exception encodeEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Error in VP8 encoding: {encodeEx.Message}, falling back to JPEG");
                }

                // Fallback to JPEG if VP8 encoding fails
                using var memoryStream = new MemoryStream();

                // Configure JPEG quality
                var encoderParameters = new System.Drawing.Imaging.EncoderParameters(1);
                encoderParameters.Param[0] = new System.Drawing.Imaging.EncoderParameter(
                    System.Drawing.Imaging.Encoder.Quality, quality);

                // Get JPEG encoder
                var jpegEncoder = GetEncoder(System.Drawing.Imaging.ImageFormat.Jpeg);

                // Save bitmap to memory stream with JPEG encoding
                if (jpegEncoder != null)
                {
                    bitmap.Save(memoryStream, jpegEncoder, encoderParameters);
                }
                else
                {
                    bitmap.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Jpeg);
                }

                return memoryStream.ToArray();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error encoding frame: {ex.Message}");

                // If all else fails, return an empty array
                return Array.Empty<byte>();
            }
        }

        /// <summary>
        /// Get encoder for specific image format
        /// </summary>
        private static System.Drawing.Imaging.ImageCodecInfo? GetEncoder(System.Drawing.Imaging.ImageFormat format)
        {
            var codecs = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders();

            foreach (var codec in codecs)
            {
                if (codec.FormatID == format.Guid)
                {
                    return codec;
                }
            }

            // If not found, return null which will use default encoding
            return null;
        }
    }
}