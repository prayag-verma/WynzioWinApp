using System;
using System.Drawing;
using System.Threading.Tasks;

namespace Wynzio.Services.Input
{
    /// <summary>
    /// Service for simulating input events from remote clients
    /// </summary>
    internal interface IInputService
    {
        /// <summary>
        /// Whether input events are currently enabled
        /// </summary>
        bool IsInputEnabled { get; }

        /// <summary>
        /// Enable processing of input events
        /// </summary>
        void EnableInput();

        /// <summary>
        /// Disable processing of input events
        /// </summary>
        void DisableInput();

        /// <summary>
        /// Process input command received from remote client
        /// </summary>
        /// <param name="command">JSON command string</param>
        Task ProcessInputCommandAsync(string command);

        /// <summary>
        /// Simulate mouse movement to the specified coordinates
        /// </summary>
        /// <param name="x">X coordinate</param>
        /// <param name="y">Y coordinate</param>
        /// <param name="isRelative">If true, coordinates are relative to current position</param>
        /// <returns>Task representing the operation</returns>
        Task SendMouseMoveAsync(int x, int y, bool isRelative = false);

        /// <summary>
        /// Simulate mouse button click at current cursor position
        /// </summary>
        /// <param name="button">Mouse button to simulate</param>
        /// <returns>Task representing the operation</returns>
        Task SendMouseClickAsync(MouseButton button);

        /// <summary>
        /// Simulate mouse button click at specified coordinates
        /// </summary>
        /// <param name="x">X coordinate</param>
        /// <param name="y">Y coordinate</param>
        /// <param name="button">Mouse button to simulate</param>
        /// <returns>Task representing the operation</returns>
        Task SendMouseClickAsync(int x, int y, MouseButton button);

        /// <summary>
        /// Simulate mouse button down at current cursor position
        /// </summary>
        /// <param name="button">Mouse button to simulate</param>
        /// <returns>Task representing the operation</returns>
        Task SendMouseDownAsync(MouseButton button);

        /// <summary>
        /// Simulate mouse button up at current cursor position
        /// </summary>
        /// <param name="button">Mouse button to simulate</param>
        /// <returns>Task representing the operation</returns>
        Task SendMouseUpAsync(MouseButton button);

        /// <summary>
        /// Simulate mouse scroll
        /// </summary>
        /// <param name="delta">Scroll amount (positive for up, negative for down)</param>
        /// <returns>Task representing the operation</returns>
        Task SendMouseScrollAsync(int delta);

        /// <summary>
        /// Simulate keyboard key press (down and up)
        /// </summary>
        /// <param name="keyCode">Virtual key code</param>
        /// <returns>Task representing the operation</returns>
        Task SendKeyPressAsync(int keyCode);

        /// <summary>
        /// Simulate keyboard key down
        /// </summary>
        /// <param name="keyCode">Virtual key code</param>
        /// <returns>Task representing the operation</returns>
        Task SendKeyDownAsync(int keyCode);

        /// <summary>
        /// Simulate keyboard key up
        /// </summary>
        /// <param name="keyCode">Virtual key code</param>
        /// <returns>Task representing the operation</returns>
        Task SendKeyUpAsync(int keyCode);

        /// <summary>
        /// Simulate typing a string of text
        /// </summary>
        /// <param name="text">Text to type</param>
        /// <returns>Task representing the operation</returns>
        Task SendTextAsync(string text);

        /// <summary>
        /// Get current mouse cursor position
        /// </summary>
        /// <returns>Mouse cursor position</returns>
        Point GetCursorPosition();
    }

    /// <summary>
    /// Mouse button enumeration
    /// </summary>
    internal enum MouseButton
    {
        /// <summary>
        /// Left mouse button
        /// </summary>
        Left,

        /// <summary>
        /// Right mouse button
        /// </summary>
        Right,

        /// <summary>
        /// Middle mouse button
        /// </summary>
        Middle,

        /// <summary>
        /// Fourth mouse button (often Browser Back)
        /// </summary>
        XButton1,

        /// <summary>
        /// Fifth mouse button (often Browser Forward)
        /// </summary>
        XButton2
    }
}