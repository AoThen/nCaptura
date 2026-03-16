using System;
using System.Diagnostics;
using System.Windows.Input;
using Captura.Loc;
using Captura.Models;
using Reactive.Bindings;

namespace Captura.ViewModels
{
    // ReSharper disable once ClassNeverInstantiated.Global
    public class AboutViewModel : ViewModelBase
    {
        public ICommand HyperlinkCommand { get; }

        public static Version Version { get; }

        public string AppVersion { get; }

        static AboutViewModel()
        {
            Version = ServiceProvider.AppVersion;
        }

        public AboutViewModel(Settings Settings, ILocalizationProvider Loc) : base(Settings, Loc)
        {
            AppVersion = "v" + Version.ToString(3);

            HyperlinkCommand = new ReactiveCommand<string>()
                .WithSubscribe(OpenUrl);
        }

        void OpenUrl(string Url)
        {
            // 验证 URL 格式
            if (string.IsNullOrWhiteSpace(Url))
                return;

            // 只允许 http/https 协议
            if (Uri.TryCreate(Url, UriKind.Absolute, out var uri))
            {
                if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                {
                    ServiceProvider.Get<IMessageProvider>()?.ShowError("Only http/https links are allowed");
                    return;
                }

                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = uri.ToString(),
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    ServiceProvider.Get<IMessageProvider>()?.ShowError($"Failed to open link: {ex.Message}");
                }
            }
        }
    }
}
