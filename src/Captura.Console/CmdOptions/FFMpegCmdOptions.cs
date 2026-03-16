using System.Threading.Tasks;
using CommandLine;

namespace Captura
{
    [Verb("ffmpeg", HelpText = "Manage FFmpeg")]
    // ReSharper disable once ClassNeverInstantiated.Global
    class FFmpegCmdOptions : ICmdlineVerb
    {
        [Option("install", HelpText = "Install FFmpeg to specified folder.")]
        public string Install { get; set; }

        public async Task Run()
        {
            var ffmpegManager = ServiceProvider.Get<FFmpegConsoleManager>();

            await ffmpegManager.Run(this);
        }
    }
}
