using System;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace CreditPincher.App.Platform
{
    /// <summary>
    /// Registers a system-wide hotkey (default Ctrl+Alt+U) that opens the quick-log box,
    /// so usage can be recorded without leaving whatever app you are in.
    ///
    /// The hotkey is hosted on a message-only window; the tray app has no main window
    /// to hang it off.
    /// </summary>
    public sealed class HotKeyManager : IDisposable
    {
        private const int WmHotKey = 0x0312;
        private const int HotKeyId = 0xC0DE;

        private static readonly IntPtr HwndMessage = new IntPtr(-3);

        private HwndSource _source;
        private bool _registered;

        /// <summary>Raised on the UI thread when the hotkey is pressed.</summary>
        public event Action Pressed;

        /// <summary>Last failure reason, shown in Settings when registration did not take.</summary>
        public string LastError { get; private set; }

        public bool IsRegistered
        {
            get { return _registered; }
        }

        /// <summary>
        /// (Re)registers the hotkey. Returns false when the combination is invalid or
        /// already owned by another application, which is a normal, recoverable situation.
        /// </summary>
        public bool Register(string modifiersText, string keyText)
        {
            Unregister();
            LastError = null;

            uint modifiers;
            uint virtualKey;
            if (!TryParse(modifiersText, keyText, out modifiers, out virtualKey))
            {
                LastError = "Unrecognised hotkey combination.";
                return false;
            }

            EnsureSource();

            var handle = _source == null ? IntPtr.Zero : _source.Handle;
            if (handle == IntPtr.Zero)
            {
                LastError = "Could not create the hotkey window.";
                return false;
            }

            _registered = RegisterHotKey(handle, HotKeyId, modifiers, virtualKey);
            if (!_registered)
            {
                LastError = "Another application already owns this shortcut.";
            }

            return _registered;
        }

        public void Unregister()
        {
            if (_registered && _source != null && _source.Handle != IntPtr.Zero)
            {
                UnregisterHotKey(_source.Handle, HotKeyId);
            }

            _registered = false;
        }

        public void Dispose()
        {
            Unregister();

            if (_source != null)
            {
                _source.RemoveHook(WndProc);
                _source.Dispose();
                _source = null;
            }
        }

        /// <summary>Human readable form for the settings tab, e.g. "Ctrl+Alt+U".</summary>
        public static string Describe(string modifiersText, string keyText)
        {
            return string.IsNullOrWhiteSpace(modifiersText) ? keyText : modifiersText + "+" + keyText;
        }

        private void EnsureSource()
        {
            if (_source != null)
            {
                return;
            }

            var parameters = new HwndSourceParameters("CreditPincherHotKeyWindow")
            {
                Width = 0,
                Height = 0,
                PositionX = 0,
                PositionY = 0,
                WindowStyle = 0,
                ParentWindow = HwndMessage,
            };

            _source = new HwndSource(parameters);
            _source.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WmHotKey && wParam.ToInt32() == HotKeyId)
            {
                handled = true;

                var pressed = Pressed;
                if (pressed != null)
                {
                    pressed();
                }
            }

            return IntPtr.Zero;
        }

        private static bool TryParse(string modifiersText, string keyText, out uint modifiers, out uint virtualKey)
        {
            modifiers = 0;
            virtualKey = 0;

            var parts = (modifiersText ?? string.Empty)
                .Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var part in parts)
            {
                switch (part.Trim().ToLowerInvariant())
                {
                    case "ctrl":
                    case "control":
                        modifiers |= ModControl;
                        break;
                    case "alt":
                        modifiers |= ModAlt;
                        break;
                    case "shift":
                        modifiers |= ModShift;
                        break;
                    case "win":
                    case "windows":
                        modifiers |= ModWin;
                        break;
                    default:
                        return false;
                }
            }

            Key key;
            if (!Enum.TryParse(keyText, true, out key) || key == Key.None)
            {
                return false;
            }

            virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);

            // A bare key with no modifier would swallow that key globally.
            return modifiers != 0 && virtualKey != 0;
        }

        private const uint ModAlt = 0x0001;
        private const uint ModControl = 0x0002;
        private const uint ModShift = 0x0004;
        private const uint ModWin = 0x0008;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
