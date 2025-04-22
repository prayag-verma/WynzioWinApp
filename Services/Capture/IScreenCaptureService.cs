using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;

namespace Wynzio.Services.Capture
{
    /// <summary>
    /// Service for capturing screen content and providing it as a stream of frames
    /// </summary>
    internal interface IScreenCaptureService
    {
        /// <summary>
        /// Event triggered when a new frame is captured
        /// </summary>
        event EventHandler<FrameCapturedEventArgs>? FrameCaptured;

        /// <summary>
        /// Start capturing the screen
        /// </summary>
        /// <param name="captureRegion">Optional region to capture. If null, captures entire primary screen</param>
        /// <param name="frameRate">Target frame rate for capture</param>
        /// <param name="quality">Quality level for compression (1-100)</param>
        /// <param name="cancellationToken">Token to cancel the capture operation</param>
        /// <returns>Task representing the capture operation</returns>
        Task StartCaptureAsync(Rectangle? captureRegion = null, int frameRate = 20, int quality = 75, CancellationToken cancellationToken = default);

        /// <summary>
        /// Stop the screen capture process
        /// </summary>
        void StopCapture();

        /// <summary>
        /// Get the list of available displays/monitors
        /// </summary>
        /// <returns>Array of display information</returns>
        DisplayInfo[] GetAvailableDisplays();

        /// <summary>
        /// Whether the capture service is currently capturing
        /// </summary>
        bool IsCapturing { get; }
    }

    /// <summary>
    /// Event arguments for frame captured event
    /// </summary>
    public class FrameCapturedEventArgs : EventArgs
    {
        /// <summary>
        /// The captured frame data (encoded with H.264/WebRTC format)
        /// </summary>
        public byte[] FrameData { get; }

        /// <summary>
        /// Width of the captured frame
        /// </summary>
        public int Width { get; }

        /// <summary>
        /// Height of the captured frame
        /// </summary>
        public int Height { get; }

        /// <summary>
        /// Timestamp when the frame was captured
        /// </summary>
        public DateTime Timestamp { get; }

        public FrameCapturedEventArgs(byte[] frameData, int width, int height, DateTime timestamp)
        {
            FrameData = frameData;
            Width = width;
            Height = height;
            Timestamp = timestamp;
        }
    }

    /// <summary>
    /// Information about a display/monitor
    /// </summary>
    public class DisplayInfo
    {
        /// <summary>
        /// Unique identifier for the display
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Display name
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Whether this is the primary display
        /// </summary>
        public bool IsPrimary { get; set; }

        /// <summary>
        /// Bounds of the display
        /// </summary>
        public Rectangle Bounds { get; set; }

        /// <summary>
        /// Creates a new DisplayInfo instance
        /// </summary>
        public DisplayInfo(string id, string name, bool isPrimary, Rectangle bounds)
        {
            Id = id;
            Name = name;
            IsPrimary = isPrimary;
            Bounds = bounds;
        }

        /// <summary>
        /// Default constructor for serialization
        /// </summary>
        public DisplayInfo()
        {
            Bounds = new Rectangle(0, 0, 1920, 1080);
        }

        /// <summary>
        /// String representation of the display info
        /// </summary>
        public override string ToString()
        {
            return $"{Name} ({Bounds.Width}x{Bounds.Height}){(IsPrimary ? " - Primary" : "")}";
        }
    }
}