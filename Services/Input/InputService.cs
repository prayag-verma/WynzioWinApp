using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Serilog;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading;

namespace Wynzio.Services.Input
{
    /// <summary>
    /// Service for simulating mouse and keyboard input
    /// </summary>
    internal class InputService : IInputService
    {
        private readonly ILogger _logger;
        private bool _isInputEnabled = false;
        private readonly SemaphoreSlim _inputLock = new SemaphoreSlim(1, 1);

        // Windows API constants for input simulation
        private const int INPUT_MOUSE = 0;
        private const int INPUT_KEYBOARD = 1;

        // Mouse event constants
        private const int MOUSEEVENTF_MOVE = 0x0001;
        private const int MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const int MOUSEEVENTF_LEFTUP = 0x0004;
        private const int MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const int MOUSEEVENTF_RIGHTUP = 0x0010;
        private const int MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        private const int MOUSEEVENTF_MIDDLEUP = 0x0040;
        private const int MOUSEEVENTF_XDOWN = 0x0080;
        private const int MOUSEEVENTF_XUP = 0x0100;
        private const int MOUSEEVENTF_WHEEL = 0x0800;
        private const int MOUSEEVENTF_ABSOLUTE = 0x8000;

        // Keyboard event constants
        private const int KEYEVENTF_KEYDOWN = 0x0000;
        private const int KEYEVENTF_KEYUP = 0x0002;

        // Windows API structures for input simulation
        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public int mouseData;
            public int dwFlags;
            public int time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public short wVk;
            public short wScan;
            public int dwFlags;
            public int time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT
        {
            public int uMsg;
            public short wParamL;
            public short wParamH;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct INPUT_UNION
        {
            [FieldOffset(0)]
            public MOUSEINPUT mi;
            [FieldOffset(0)]
            public KEYBDINPUT ki;
            [FieldOffset(0)]
            public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public int type;
            public INPUT_UNION u;
        }

        // Windows API functions for input simulation
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        /// <summary>
        /// Whether input simulation is enabled
        /// </summary>
        public bool IsInputEnabled => _isInputEnabled;

        public InputService()
        {
            _logger = Log.ForContext<InputService>();
        }

        /// <summary>
        /// Enable input simulation
        /// </summary>
        public void EnableInput()
        {
            _isInputEnabled = true;
            _logger.Information("Input simulation enabled");
        }

        /// <summary>
        /// Disable input simulation
        /// </summary>
        public void DisableInput()
        {
            _isInputEnabled = false;
            _logger.Information("Input simulation disabled");
        }

        /// <summary>
        /// Process input command received from remote client
        /// </summary>
        /// <param name="command">JSON command string</param>
        public async Task ProcessInputCommandAsync(string command)
        {
            if (!_isInputEnabled)
            {
                _logger.Warning("Attempted to process input command while input is disabled");
                return;
            }

            try
            {
                // Lock to prevent multiple simultaneous input commands
                await _inputLock.WaitAsync();

                try
                {
                    // Parse the command
                    JObject? commandObj = null;

                    try
                    {
                        commandObj = JObject.Parse(command);
                    }
                    catch (JsonException)
                    {
                        // Try parsing as object inside another object (from WebRTC data channel)
                        var outerObj = JsonConvert.DeserializeObject<dynamic>(command);
                        if (outerObj?.type?.ToString() == "control-command" && outerObj?.command != null)
                        {
                            string innerCommand = outerObj.command!.ToString();
                            commandObj = JObject.Parse(innerCommand);
                        }
                    }

                    if (commandObj == null)
                    {
                        _logger.Warning("Failed to parse input command: {Command}", command);
                        return;
                    }

                    // Get command type
                    string typeStr = commandObj["type"]?.ToString() ?? "";
                    if (!Enum.TryParse<InputCommandType>(typeStr, true, out var commandType))
                    {
                        _logger.Warning("Invalid command type: {Type}", typeStr);
                        return;
                    }

                    // Get command properties
                    int x = commandObj["x"]?.Value<int>() ?? 0;
                    int y = commandObj["y"]?.Value<int>() ?? 0;
                    bool isRelative = commandObj["isRelative"]?.Value<bool>() ?? false;

                    // Get button, if any
                    MouseButton button = MouseButton.Left;
                    if (commandObj["button"] != null)
                    {
                        string buttonStr = commandObj["button"]?.ToString() ?? string.Empty;
                        if (Enum.TryParse<MouseButton>(buttonStr, true, out var parsedButton))
                        {
                            button = parsedButton;
                        }
                    }

                    // Get scroll delta, if any
                    int scrollDelta = commandObj["scrollDelta"]?.Value<int>() ?? 0;

                    // Get key code, if any
                    int keyCode = commandObj["keyCode"]?.Value<int>() ?? 0;

                    // Get text, if any
                    string text = commandObj["text"]?.ToString() ?? "";

                    // Process the command based on its type
                    switch (commandType)
                    {
                        case InputCommandType.MouseMove:
                            await SendMouseMoveAsync(x, y, isRelative);
                            break;

                        case InputCommandType.MouseClick:
                            if (x > 0 && y > 0)
                                await SendMouseClickAsync(x, y, button);
                            else
                                await SendMouseClickAsync(button);
                            break;

                        case InputCommandType.MouseDown:
                            await SendMouseDownAsync(button);
                            break;

                        case InputCommandType.MouseUp:
                            await SendMouseUpAsync(button);
                            break;

                        case InputCommandType.MouseScroll:
                            await SendMouseScrollAsync(scrollDelta);
                            break;

                        case InputCommandType.KeyPress:
                            await SendKeyPressAsync(keyCode);
                            break;

                        case InputCommandType.KeyDown:
                            await SendKeyDownAsync(keyCode);
                            break;

                        case InputCommandType.KeyUp:
                            await SendKeyUpAsync(keyCode);
                            break;

                        case InputCommandType.Text:
                            await SendTextAsync(text);
                            break;

                        default:
                            _logger.Warning("Unknown input command type: {Type}", commandType);
                            break;
                    }
                }
                finally
                {
                    _inputLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error processing input command: {Command}", command);

                // Make sure we release the lock even if an exception occurs
                if (_inputLock.CurrentCount == 0)
                {
                    _inputLock.Release();
                }
            }
        }

        /// <summary>
        /// Simulate mouse movement
        /// </summary>
        public async Task SendMouseMoveAsync(int x, int y, bool isRelative = false)
        {
            if (!_isInputEnabled)
            {
                _logger.Warning("Attempted to simulate mouse move while input is disabled");
                return;
            }

            await Task.Run(() =>
            {
                try
                {
                    Point targetPosition;

                    if (isRelative)
                    {
                        // For relative movement, get current position and add the delta
                        var currentPos = GetCursorPosition();
                        targetPosition = new Point(currentPos.X + x, currentPos.Y + y);
                    }
                    else
                    {
                        targetPosition = new Point(x, y);
                    }

                    // Ensure coordinates are within screen bounds (multi-monitor support)
                    targetPosition = EnsureWithinScreenBounds(targetPosition);

                    // Use SetCursorPos for absolute positioning
                    bool result = SetCursorPos(targetPosition.X, targetPosition.Y);
                    if (!result)
                    {
                        _logger.Warning("SetCursorPos failed for position {X}, {Y}", targetPosition.X, targetPosition.Y);
                    }
                    else
                    {
                        _logger.Debug("Mouse moved to {X}, {Y}", targetPosition.X, targetPosition.Y);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error simulating mouse move");
                }
            });
        }

        /// <summary>
        /// Simulate mouse click at current position
        /// </summary>
        public async Task SendMouseClickAsync(MouseButton button)
        {
            var currentPos = GetCursorPosition();
            await SendMouseClickAsync(currentPos.X, currentPos.Y, button);
        }

        /// <summary>
        /// Simulate mouse click at specified position
        /// </summary>
        public async Task SendMouseClickAsync(int x, int y, MouseButton button)
        {
            // Move mouse to position
            await SendMouseMoveAsync(x, y);

            // Send mouse down followed by mouse up
            await SendMouseDownAsync(button);

            // Add small delay for realism
            await Task.Delay(10);

            await SendMouseUpAsync(button);
        }

        /// <summary>
        /// Simulate mouse button down
        /// </summary>
        public async Task SendMouseDownAsync(MouseButton button)
        {
            if (!_isInputEnabled)
            {
                _logger.Warning("Attempted to simulate mouse down while input is disabled");
                return;
            }

            await Task.Run(() =>
            {
                try
                {
                    // Prepare inputs
                    var inputs = new INPUT[1];
                    inputs[0].type = INPUT_MOUSE;
                    inputs[0].u.mi.dx = 0;
                    inputs[0].u.mi.dy = 0;
                    inputs[0].u.mi.mouseData = 0;
                    inputs[0].u.mi.time = 0;
                    inputs[0].u.mi.dwExtraInfo = IntPtr.Zero;

                    // Set flags based on button
                    switch (button)
                    {
                        case MouseButton.Left:
                            inputs[0].u.mi.dwFlags = MOUSEEVENTF_LEFTDOWN;
                            break;
                        case MouseButton.Right:
                            inputs[0].u.mi.dwFlags = MOUSEEVENTF_RIGHTDOWN;
                            break;
                        case MouseButton.Middle:
                            inputs[0].u.mi.dwFlags = MOUSEEVENTF_MIDDLEDOWN;
                            break;
                        case MouseButton.XButton1:
                        case MouseButton.XButton2:
                            inputs[0].u.mi.dwFlags = MOUSEEVENTF_XDOWN;
                            inputs[0].u.mi.mouseData = button == MouseButton.XButton1 ? 1 : 2;
                            break;
                    }

                    // Send input and check result
                    uint result = SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
                    if (result == 0)
                    {
                        _logger.Warning("SendInput failed for mouse button {Button} down", button);
                    }
                    else
                    {
                        _logger.Debug("Mouse button {Button} down", button);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error simulating mouse down for {Button}", button);
                }
            });
        }

        /// <summary>
        /// Simulate mouse button up
        /// </summary>
        public async Task SendMouseUpAsync(MouseButton button)
        {
            if (!_isInputEnabled)
            {
                _logger.Warning("Attempted to simulate mouse up while input is disabled");
                return;
            }

            await Task.Run(() =>
            {
                try
                {
                    // Prepare inputs
                    var inputs = new INPUT[1];
                    inputs[0].type = INPUT_MOUSE;
                    inputs[0].u.mi.dx = 0;
                    inputs[0].u.mi.dy = 0;
                    inputs[0].u.mi.mouseData = 0;
                    inputs[0].u.mi.time = 0;
                    inputs[0].u.mi.dwExtraInfo = IntPtr.Zero;

                    // Set flags based on button
                    switch (button)
                    {
                        case MouseButton.Left:
                            inputs[0].u.mi.dwFlags = MOUSEEVENTF_LEFTUP;
                            break;
                        case MouseButton.Right:
                            inputs[0].u.mi.dwFlags = MOUSEEVENTF_RIGHTUP;
                            break;
                        case MouseButton.Middle:
                            inputs[0].u.mi.dwFlags = MOUSEEVENTF_MIDDLEUP;
                            break;
                        case MouseButton.XButton1:
                        case MouseButton.XButton2:
                            inputs[0].u.mi.dwFlags = MOUSEEVENTF_XUP;
                            inputs[0].u.mi.mouseData = button == MouseButton.XButton1 ? 1 : 2;
                            break;
                    }

                    // Send input and check result
                    uint result = SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
                    if (result == 0)
                    {
                        _logger.Warning("SendInput failed for mouse button {Button} up", button);
                    }
                    else
                    {
                        _logger.Debug("Mouse button {Button} up", button);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error simulating mouse up for {Button}", button);
                }
            });
        }

        /// <summary>
        /// Simulate mouse wheel scroll
        /// </summary>
        public async Task SendMouseScrollAsync(int delta)
        {
            if (!_isInputEnabled)
            {
                _logger.Warning("Attempted to simulate mouse scroll while input is disabled");
                return;
            }

            await Task.Run(() =>
            {
                try
                {
                    // Prepare input
                    var inputs = new INPUT[1];
                    inputs[0].type = INPUT_MOUSE;
                    inputs[0].u.mi.dx = 0;
                    inputs[0].u.mi.dy = 0;
                    inputs[0].u.mi.mouseData = delta * 120; // 120 is the standard scroll amount per notch
                    inputs[0].u.mi.dwFlags = MOUSEEVENTF_WHEEL;
                    inputs[0].u.mi.time = 0;
                    inputs[0].u.mi.dwExtraInfo = IntPtr.Zero;

                    // Send input and check result
                    uint result = SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
                    if (result == 0)
                    {
                        _logger.Warning("SendInput failed for mouse scroll with delta {Delta}", delta);
                    }
                    else
                    {
                        _logger.Debug("Mouse scrolled by {Delta}", delta);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error simulating mouse scroll");
                }
            });
        }

        /// <summary>
        /// Simulate key press (down and up)
        /// </summary>
        public async Task SendKeyPressAsync(int keyCode)
        {
            await SendKeyDownAsync(keyCode);

            // Add small delay for realism
            await Task.Delay(5);

            await SendKeyUpAsync(keyCode);
        }

        /// <summary>
        /// Simulate key down
        /// </summary>
        public async Task SendKeyDownAsync(int keyCode)
        {
            if (!_isInputEnabled)
            {
                _logger.Warning("Attempted to simulate key down while input is disabled");
                return;
            }

            await Task.Run(() =>
            {
                try
                {
                    // Prepare input
                    var inputs = new INPUT[1];
                    inputs[0].type = INPUT_KEYBOARD;
                    inputs[0].u.ki.wVk = (short)keyCode;
                    inputs[0].u.ki.wScan = 0;
                    inputs[0].u.ki.dwFlags = KEYEVENTF_KEYDOWN;
                    inputs[0].u.ki.time = 0;
                    inputs[0].u.ki.dwExtraInfo = IntPtr.Zero;

                    // Send input and check result
                    uint result = SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
                    if (result == 0)
                    {
                        _logger.Warning("SendInput failed for key down {KeyCode}", keyCode);
                    }
                    else
                    {
                        _logger.Debug("Key down: {KeyCode}", keyCode);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error simulating key down for {KeyCode}", keyCode);
                }
            });
        }

        /// <summary>
        /// Simulate key up
        /// </summary>
        public async Task SendKeyUpAsync(int keyCode)
        {
            if (!_isInputEnabled)
            {
                _logger.Warning("Attempted to simulate key up while input is disabled");
                return;
            }

            await Task.Run(() =>
            {
                try
                {
                    // Prepare input
                    var inputs = new INPUT[1];
                    inputs[0].type = INPUT_KEYBOARD;
                    inputs[0].u.ki.wVk = (short)keyCode;
                    inputs[0].u.ki.wScan = 0;
                    inputs[0].u.ki.dwFlags = KEYEVENTF_KEYUP;
                    inputs[0].u.ki.time = 0;
                    inputs[0].u.ki.dwExtraInfo = IntPtr.Zero;

                    // Send input and check result
                    uint result = SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
                    if (result == 0)
                    {
                        _logger.Warning("SendInput failed for key up {KeyCode}", keyCode);
                    }
                    else
                    {
                        _logger.Debug("Key up: {KeyCode}", keyCode);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error simulating key up for {KeyCode}", keyCode);
                }
            });
        }

        /// <summary>
        /// Simulate typing a string
        /// </summary>
        public async Task SendTextAsync(string text)
        {
            if (!_isInputEnabled)
            {
                _logger.Warning("Attempted to simulate text input while input is disabled");
                return;
            }

            if (string.IsNullOrEmpty(text))
                return;

            // Lock to prevent multiple text entry operations running concurrently
            await _inputLock.WaitAsync();

            try
            {
                foreach (char c in text)
                {
                    // Convert character to virtual key code
                    short vk = VirtualKeyCodeFromChar(c);

                    if (vk != 0)
                    {
                        // For special characters that require shift key
                        bool needsShift = char.IsUpper(c) || IsShiftCharacter(c);

                        if (needsShift)
                        {
                            // Press shift down
                            await SendKeyDownAsync(0x10); // VK_SHIFT
                            await Task.Delay(5);
                        }

                        // Press and release character key
                        await SendKeyPressAsync(vk);

                        if (needsShift)
                        {
                            // Release shift
                            await Task.Delay(5);
                            await SendKeyUpAsync(0x10); // VK_SHIFT
                        }
                    }
                    else
                    {
                        // For characters not directly mappable to virtual keys,
                        // use Windows-specific SendInput with Unicode
                        await SendUnicodeCharacter(c);
                    }

                    // Small delay between keypresses for realism
                    await Task.Delay(10);
                }

                _logger.Debug("Text input: {TextLength} characters", text.Length);
            }
            finally
            {
                _inputLock.Release();
            }
        }

        /// <summary>
        /// Send a Unicode character using Windows input system
        /// </summary>
        private async Task SendUnicodeCharacter(char c)
        {
            await Task.Run(() =>
            {
                try
                {
                    // Prepare input for Unicode character
                    var inputs = new INPUT[2]; // Need 2 inputs: one down, one up

                    // Down event
                    inputs[0].type = INPUT_KEYBOARD;
                    inputs[0].u.ki.wVk = 0; // We're using scan code for Unicode
                    inputs[0].u.ki.wScan = (short)c; // Unicode character
                    inputs[0].u.ki.dwFlags = 0x4; // KEYEVENTF_UNICODE
                    inputs[0].u.ki.time = 0;
                    inputs[0].u.ki.dwExtraInfo = IntPtr.Zero;

                    // Up event
                    inputs[1].type = INPUT_KEYBOARD;
                    inputs[1].u.ki.wVk = 0;
                    inputs[1].u.ki.wScan = (short)c;
                    inputs[1].u.ki.dwFlags = 0x4 | 0x2; // KEYEVENTF_UNICODE | KEYEVENTF_KEYUP
                    inputs[1].u.ki.time = 0;
                    inputs[1].u.ki.dwExtraInfo = IntPtr.Zero;

                    // Send both inputs
                    uint result = SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
                    if (result == 0)
                    {
                        _logger.Warning("SendInput failed for Unicode character {Char}", (int)c);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error sending Unicode character {Char}", (int)c);
                }
            });
        }

        /// <summary>
        /// Check if character requires shift key
        /// </summary>
        private bool IsShiftCharacter(char c)
        {
            // Common shift characters
            string shiftChars = "~!@#$%^&*()_+{}|:\"<>?";
            return shiftChars.Contains(c);
        }

        /// <summary>
        /// Convert character to virtual key code
        /// </summary>
        private short VirtualKeyCodeFromChar(char c)
        {
            // Handle common keys directly
            if (c >= 'a' && c <= 'z')
            {
                return (short)(c - 'a' + 0x41); // 'A' virtual key is 0x41
            }

            if (c >= 'A' && c <= 'Z')
            {
                return (short)(c - 'A' + 0x41);
            }

            if (c >= '0' && c <= '9')
            {
                return (short)(c - '0' + 0x30); // '0' virtual key is 0x30
            }

            // Map special characters to virtual key codes
            switch (c)
            {
                case ' ': return 0x20; // VK_SPACE
                case '\t': return 0x09; // VK_TAB
                case '\r': return 0x0D; // VK_RETURN
                case '\n': return 0x0D; // VK_RETURN (newline mapped to return)

                // Punctuation keys
                case '.': return 0xBE; // VK_OEM_PERIOD
                case ',': return 0xBC; // VK_OEM_COMMA
                case '/': return 0xBF; // VK_OEM_2
                case ';': return 0xBA; // VK_OEM_1
                case '\'': return 0xDE; // VK_OEM_7
                case '[': return 0xDB; // VK_OEM_4
                case ']': return 0xDD; // VK_OEM_6
                case '\\': return 0xDC; // VK_OEM_5
                case '-': return 0xBD; // VK_OEM_MINUS
                case '=': return 0xBB; // VK_OEM_PLUS
                case '`': return 0xC0; // VK_OEM_3

                // Shifted keys
                case '!': return 0x31; // 1 key
                case '@': return 0x32; // 2 key
                case '#': return 0x33; // 3 key
                case '$': return 0x34; // 4 key
                case '%': return 0x35; // 5 key
                case '^': return 0x36; // 6 key
                case '&': return 0x37; // 7 key
                case '*': return 0x38; // 8 key
                case '(': return 0x39; // 9 key
                case ')': return 0x30; // 0 key
                case '_': return 0xBD; // - key
                case '+': return 0xBB; // = key
                case '{': return 0xDB; // [ key
                case '}': return 0xDD; // ] key
                case '|': return 0xDC; // \ key
                case ':': return 0xBA; // ; key
                case '"': return 0xDE; // ' key
                case '<': return 0xBC; // , key
                case '>': return 0xBE; // . key
                case '?': return 0xBF; // / key
                case '~': return 0xC0; // ` key

                default: return 0; // For any other character, use 0 to signal Unicode handling
            }
        }

        /// <summary>
        /// Get current cursor position
        /// </summary>
        public Point GetCursorPosition()
        {
            GetCursorPos(out POINT point);
            return new Point(point.X, point.Y);
        }

        /// <summary>
        /// Ensure a point is within screen bounds (for multi-monitor setups)
        /// </summary>
        private Point EnsureWithinScreenBounds(Point p)
        {
            // Get the full desktop area (all monitors combined)
            Rectangle virtualScreen = SystemInformation.VirtualScreen;

            // Clamp coordinates to virtual screen area
            int x = Math.Clamp(p.X, virtualScreen.Left, virtualScreen.Right - 1);
            int y = Math.Clamp(p.Y, virtualScreen.Top, virtualScreen.Bottom - 1);

            return new Point(x, y);
        }
    }

    /// <summary>
    /// Types of input commands
    /// </summary>
    internal enum InputCommandType
    {
        MouseMove,
        MouseClick,
        MouseDown,
        MouseUp,
        MouseScroll,
        KeyPress,
        KeyDown,
        KeyUp,
        Text
    }
}