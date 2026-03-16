// Adapted from https://github.com/jasonpang/desktop-duplication-net

using SharpDX.DXGI;
using System;
using System.Diagnostics;
using Captura.Video;
using Captura.Windows.DirectX;

namespace Captura.Windows.DesktopDuplication
{
    public class DesktopDuplicator : IDisposable
    {
        Direct2DEditorSession _editorSession;
        DxMousePointer _mousePointer;
        DuplCapture _duplCapture;

        public DesktopDuplicator(bool IncludeCursor, Output1 Output, IPreviewWindow PreviewWindow)
        {
            _duplCapture = new DuplCapture(Output);

            var bounds = Output.Description.DesktopBounds;
            var width = bounds.Right - bounds.Left;
            var height = bounds.Bottom - bounds.Top;

            _editorSession = new Direct2DEditorSession(width, height, PreviewWindow);

            if (IncludeCursor)
                _mousePointer = new DxMousePointer(_editorSession);
        }

        public IEditableFrame Capture()
        {
            try
            {
                if (!_duplCapture.Get(_editorSession.DesktopTexture, _mousePointer))
                    return RepeatFrame.Instance;
            }
            catch (SharpDXException ex)
            {
                Debug.WriteLine($"Desktop capture error: {ex.Message}");

                try { _duplCapture.Init(); }
                catch (Exception initEx)
                {
                    Debug.WriteLine($"Re-init failed: {initEx.Message}");
                }

                return RepeatFrame.Instance;
            }

            var editor = new Direct2DEditor(_editorSession);

            _mousePointer?.Draw(editor);

            return editor;
        }

        public void Dispose()
        {
            void SafeDispose(IDisposable disposable, string name)
            {
                try
                {
                    disposable?.Dispose();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error disposing {name}: {ex.Message}");
                }
            }

            SafeDispose(_duplCapture, nameof(_duplCapture));
            _duplCapture = null;

            // Mouse Pointer disposed later to prevent errors.
            SafeDispose(_mousePointer, nameof(_mousePointer));
            _mousePointer = null;

            SafeDispose(_editorSession, nameof(_editorSession));
            _editorSession = null;
        }
    }
}
