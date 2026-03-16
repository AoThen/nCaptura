// Adapted from https://github.com/jasonpang/desktop-duplication-net

using SharpDX;
using SharpDX.DXGI;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Collections.Generic;
using Captura.Video;
using Captura.Windows.DirectX;

namespace Captura.Windows.DesktopDuplication
{
    public class FullScreenDesktopDuplicator : IDisposable
    {
        Direct2DEditorSession _editorSession;
        readonly List<DeskDuplOutputEntry> _outputs = new List<DeskDuplOutputEntry>();

        public FullScreenDesktopDuplicator(bool IncludeCursor,
            IPreviewWindow PreviewWindow,
            IPlatformServices PlatformServices)
        {
            using var factory = new Factory1();
            var outputs = factory
                .Adapters1
                .SelectMany(M => M.Outputs)
                .ToArray();

            var bounds = PlatformServices.DesktopRectangle;

            Width = bounds.Width;
            Height = bounds.Height;

            PointTransform = P => new Point(P.X - bounds.Left, P.Y - bounds.Top);

            _editorSession = new Direct2DEditorSession(Width, Height, PreviewWindow);

            _outputs.AddRange(outputs.Select(M =>
            {
                var output1 = M.QueryInterface<Output1>();

                var rect = M.Description.DesktopBounds;

                return new DeskDuplOutputEntry
                {
                    DuplCapture = new DuplCapture(output1),
                    Location = new SharpDX.Point(rect.Left - bounds.Left, rect.Top - bounds.Top),
                    MousePointer = IncludeCursor ? new DxMousePointer(_editorSession) : null
                };
            }));
        }

        public int Width { get; }
        public int Height { get; }

        public Func<Point, Point> PointTransform { get; }

        public IEditableFrame Capture()
        {
            foreach (var entry in _outputs)
            {
                try
                {
                    if (!entry.DuplCapture.Get(_editorSession.DesktopTexture, entry.MousePointer, entry.Location))
                        return RepeatFrame.Instance;
                }
                catch (SharpDXException ex)
                {
                    Debug.WriteLine($"Desktop capture error: {ex.Message}");

                    try { entry.DuplCapture.Init(); }
                    catch (Exception initEx)
                    {
                        Debug.WriteLine($"Re-init failed: {initEx.Message}");
                    }

                    return RepeatFrame.Instance;
                }
            }

            var editor = new Direct2DEditor(_editorSession);

            foreach (var entry in _outputs)
            {
                entry.MousePointer?.Draw(editor, entry.Location);
            }

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

            foreach (var entry in _outputs)
            {
                SafeDispose(entry.DuplCapture, nameof(entry.DuplCapture));
                entry.DuplCapture = null;

                // Mouse Pointer disposed later to prevent errors.
                SafeDispose(entry.MousePointer, nameof(entry.MousePointer));
                entry.MousePointer = null;
            }

            SafeDispose(_editorSession, nameof(_editorSession));
            _editorSession = null;
        }
    }
}
