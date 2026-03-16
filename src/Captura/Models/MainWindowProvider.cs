using System;
using System.Diagnostics;
using System.Windows;
using Captura.Views;

namespace Captura.Models
{
    class MainWindowProvider : IMainWindow
    {
        readonly Func<Window> _window;

        public MainWindowProvider(Func<Window> Window)
        {
            _window = Window;
        }

        public bool IsVisible
        {
            get => _window.Invoke().IsVisible && _window.Invoke().WindowState != WindowState.Minimized;
            set
            {
                var win = _window.Invoke();
                if (value)
                {
                    if (win.WindowState == WindowState.Minimized)
                        win.WindowState = WindowState.Normal;
                    win.Show();
                    win.Activate();
                }
                else win.Hide();
            }
        }

        public bool IsMinimized
        {
            get => _window.Invoke().WindowState == WindowState.Minimized;
            set => _window.Invoke().WindowState = value ? WindowState.Minimized : WindowState.Normal;
        }

        public void EditImage(string FileName)
        {
            var settings = ServiceProvider.Get<Settings>().ScreenShots;
            var editor = settings.ExternalEditor;

            // 验证编辑器路径
            if (string.IsNullOrWhiteSpace(editor))
            {
                ServiceProvider.Get<IMessageProvider>()?.ShowError("External editor is not configured");
                return;
            }

            // 验证文件路径存在
            if (!System.IO.File.Exists(FileName))
            {
                ServiceProvider.Get<IMessageProvider>()?.ShowError("File does not exist");
                return;
            }

            // 使用 ProcessStartInfo 并设置安全选项
            var startInfo = new ProcessStartInfo
            {
                FileName = editor,
                Arguments = $"\"{FileName}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                ServiceProvider.Get<IMessageProvider>()?.ShowError($"Failed to launch editor: {ex.Message}");
            }
        }

        public void TrimMedia(string FileName)
        {
            var win = new TrimmerWindow();

            win.Open(FileName);

            win.ShowAndFocus();
        }

        public void UploadToYouTube(string FileName)
        {
            var win = new YouTubeUploaderWindow();

            win.Open(FileName);

            win.ShowAndFocus();
        }
    }
}
