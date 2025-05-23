using Captura.Video;

namespace Captura.FFmpeg
{
    class AmdHevcVideoCodec : FFmpegVideoCodec
    {
        const string Descr = "Encode to Mp4: AMD AMF HEVC encoder (codec hevc)";

        public AmdHevcVideoCodec() : base("AMD Mp4 (HEVC, AAC)", ".mp4", Descr) { }

        public override FFmpegAudioArgsProvider AudioArgsProvider => FFmpegAudioItem.Aac;

        public override void Apply(FFmpegSettings Settings, VideoWriterArgs WriterArgs, FFmpegOutputArgs OutputArgs)
        {
            OutputArgs.AddArg("c:v", "hevc_amf")
                .AddArg("preset", "fast");
        }
    }
}