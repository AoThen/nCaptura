using System;
using System.Windows;
using System.Windows.Interop;
using System.Threading;
using Captura.Windows.DirectX;
using Captura.Windows.Gdi;
using Reactive.Bindings.Extensions;
using SharpDX.Direct3D9;

namespace Captura.Video
{
    // ReSharper disable once ClassNeverInstantiated.Global
    public class PreviewWindowService : IPreviewWindow
    {
        D3D9PreviewAssister _d3D9PreviewAssister;
        IntPtr _backBufferPtr;
        Texture _texture;
        readonly VisualSettings _visualSettings;
        
        // 跟踪待处理的帧，防止 UI 线程处理前被释放
        IBitmapFrame _pendingFrame;
        int _isProcessing = 0;

        public void Show()
        {
            _visualSettings.Expanded = true;
        }

        public bool IsVisible { get; private set; }

        public PreviewWindowService(VisualSettings VisualSettings)
        {
            _visualSettings = VisualSettings;

            VisualSettings.ObserveProperty(M => M.Expanded)
                .Subscribe(M => IsVisible = M);
        }

        IBitmapFrame _lastFrame;

        public void Display(IBitmapFrame Frame)
        {
            if (Frame is RepeatFrame)
                return;

            if (!IsVisible)
            {
                Frame.Dispose();
                return;
            }

            // 如果上一帧还在处理中，跳过当前帧（避免积压）
            if (Interlocked.CompareExchange(ref _isProcessing, 1, 0) != 0)
            {
                Frame.Dispose();
                return;
            }

            // 保存帧引用，防止在 UI 线程处理前被释放
            _pendingFrame = Frame;

            var win = MainWindow.Instance;

            // 使用 BeginInvoke 异步调用，避免阻塞录制线程
            win.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    ProcessFrame(_pendingFrame);
                }
                finally
                {
                    _pendingFrame = null;
                    Interlocked.Exchange(ref _isProcessing, 0);
                }
            }));
        }

        void ProcessFrame(IBitmapFrame Frame)
        {
            if (Frame == null)
                return;

            var win = MainWindow.Instance;

            win.DisplayImage.Image = null;

            _lastFrame?.Dispose();
            _lastFrame = Frame;

            Frame = Frame.Unwrap();

            switch (Frame)
            {
                case DrawingFrame drawingFrame:
                    try
                    {
                        // TODO: Preview is not shown during Webcam only recordings
                        // This check swallows errors
                        var h = drawingFrame.Bitmap.Height;

                        if (h == 0)
                            return;
                    }
                    catch { return; }

                    win.WinFormsHost.Visibility = Visibility.Visible;
                    win.DisplayImage.Image = drawingFrame.Bitmap;
                    break;

                case Texture2DFrame texture2DFrame:
                    win.WinFormsHost.Visibility = Visibility.Collapsed;
                    if (_d3D9PreviewAssister == null)
                    {
                        _d3D9PreviewAssister = new D3D9PreviewAssister(ServiceProvider.Get<IPlatformServices>());
                        _texture = _d3D9PreviewAssister.GetSharedTexture(texture2DFrame.PreviewTexture);

                        using var surface = _texture.GetSurfaceLevel(0);
                        _backBufferPtr = surface.NativePointer;
                    }

                    Invalidate(_backBufferPtr, texture2DFrame.Width, texture2DFrame.Height);
                    break;
            }
        }

        void Invalidate(IntPtr BackBufferPtr, int Width, int Height)
        {
            var win = MainWindow.Instance;

            win.D3DImage.Lock();
            win.D3DImage.SetBackBuffer(D3DResourceType.IDirect3DSurface9, BackBufferPtr);

            if (BackBufferPtr != IntPtr.Zero)
                win.D3DImage.AddDirtyRect(new Int32Rect(0, 0, Width, Height));

            win.D3DImage.Unlock();
        }

        public void Dispose()
        {
            var win = MainWindow.Instance;

            // 等待任何待处理的帧完成
            var startTime = DateTime.UtcNow;
            while (Interlocked.CompareExchange(ref _isProcessing, 0, 0) != 0)
            {
                if ((DateTime.UtcNow - startTime).TotalSeconds > 2)
                    break;
                Thread.Sleep(10);
            }

            // 使用同步调用确保资源释放完成
            win.Dispatcher.Invoke(() =>
            {
                win.DisplayImage.Image = null;
                win.WinFormsHost.Visibility = Visibility.Collapsed;

                _lastFrame?.Dispose();
                _lastFrame = null;
                _pendingFrame?.Dispose();
                _pendingFrame = null;

                if (_d3D9PreviewAssister != null)
                {
                    Invalidate(IntPtr.Zero, 0, 0);

                    _texture.Dispose();

                    _d3D9PreviewAssister.Dispose();

                    _d3D9PreviewAssister = null;
                }
            });
        }
    }
}