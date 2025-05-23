using Captura.Video;

namespace Captura.FFmpeg
{
    class AmdH264VideoCodec : FFmpegVideoCodec
    {
        const string Descr = "Encode to Mp4: AMD AMF H.264 Encoder (codec h264)";

        public AmdH264VideoCodec() : base("AMD: Mp4 (H.264, AAC)", ".mp4", Descr) { }

        public override FFmpegAudioArgsProvider AudioArgsProvider => FFmpegAudioItem.Aac;

        public override void Apply(FFmpegSettings Settings, VideoWriterArgs WriterArgs, FFmpegOutputArgs OutputArgs)
        {
            OutputArgs.AddArg("c:v", "h264_amf")
                .AddArg("pixel_format", "yuv444p")
                .AddArg("preset", "fast");
        }
    }
}