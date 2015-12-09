using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace Recorder
{
    /// <summary>
    /// Based on +http://code.msdn.microsoft.com/CSWPFClipboardViewer-f601b815
    /// </summary>
    class ClipboardHelper
    {

        #region Private fields

        /// <summary>
        /// Next clipboard viewer window 
        /// </summary>
        private IntPtr hWndNextViewer;

        /// <summary>
        /// The <see cref="HwndSource"/> for this window.
        /// </summary>
        private HwndSource hWndSource;

        private bool isViewing;

        #endregion

        /// <summary>
        /// Occurs when the contents of the clipboard is updated.
        /// </summary>
        public EventHandler onClipboardUpdateHandler;

        public ClipboardHelper(Window window)
        {
            var windowHelper = new WindowInteropHelper(window);
            hWndSource = HwndSource.FromHwnd(windowHelper.Handle);
        }

        #region Clipboard viewer related methods

        public void InitCBViewer()
        {
            hWndSource.AddHook(this.WinProc);   // start processing window messages
            hWndNextViewer = Win32.SetClipboardViewer(hWndSource.Handle);   // set this window as a viewer
            isViewing = true;
        }

        public void RemoveCBViewer()
        {
            if (isViewing)
            {
                // remove this window from the clipboard viewer chain
                Win32.ChangeClipboardChain(hWndSource.Handle, hWndNextViewer);

                hWndNextViewer = IntPtr.Zero;
                hWndSource.RemoveHook(this.WinProc);
                isViewing = false;
            }
        }

        private void DrawClipboard()
        {
            if (Clipboard.ContainsImage())
            {
                if (this.onClipboardUpdateHandler != null)
                {
                    this.onClipboardUpdateHandler(this, EventArgs.Empty);
                }
            }
        }

        private IntPtr WinProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            switch (msg)
            {
                case Win32.WM_CHANGECBCHAIN:
                    if (wParam == hWndNextViewer)
                    {
                        // clipboard viewer chain changed, need to fix it.
                        hWndNextViewer = lParam;
                    }
                    else if (hWndNextViewer != IntPtr.Zero)
                    {
                        // pass the message to the next viewer.
                        Win32.SendMessage(hWndNextViewer, msg, wParam, lParam);
                    }
                    break;

                case Win32.WM_DRAWCLIPBOARD:
                    // clipboard content changed
                    DrawClipboard();
                    // pass the message to the next viewer.
                    Win32.SendMessage(hWndNextViewer, msg, wParam, lParam);
                    break;
            }

            return IntPtr.Zero;
        }

        #endregion
    } // end of class ClipboardHelper

    /// <summary>
    /// This static class holds the Win32 function declarations and constants needed by
    /// this sample application.
    /// </summary>
    internal static class Win32
    {
        /// <summary>
        /// The WM_DRAWCLIPBOARD message notifies a clipboard viewer window that the content of the clipboard has changed. 
        /// </summary>
        internal const int WM_DRAWCLIPBOARD = 0x0308;

        /// <summary>
        /// A clipboard viewer window receives the WM_CHANGECBCHAIN message when another window is removing itself from the clipboard viewer chain.
        /// </summary>
        internal const int WM_CHANGECBCHAIN = 0x030D;

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        internal static extern IntPtr SetClipboardViewer(IntPtr hWndNewViewer);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        internal static extern bool ChangeClipboardChain(IntPtr hWndRemove, IntPtr hWndNewNext);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        internal static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);
    }

}
