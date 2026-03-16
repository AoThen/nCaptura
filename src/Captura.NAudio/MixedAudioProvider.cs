using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Captura.Audio
{
    class MixedAudioProvider : IAudioProvider
    {
        readonly Dictionary<NAudioProvider, MonitoredBufferedWaveProvider> _audioProviders = new Dictionary<NAudioProvider, MonitoredBufferedWaveProvider>();

        readonly IWaveProvider _mixingWaveProvider;
        
        // 溢出统计
        long _totalOverflowCount;
        long _totalOverflowBytes;

        public MixedAudioProvider(params NAudioProvider[] AudioProviders)
        {
            foreach (var provider in AudioProviders)
            {
                // 使用更大的缓冲区（2秒）并监控溢出
                var bufferedProvider = new MonitoredBufferedWaveProvider(provider.NAudioWaveFormat)
                {
                    BufferDuration = TimeSpan.FromSeconds(2),
                    DiscardOnBufferOverflow = false  // 不静默丢弃
                };

                provider.WaveIn.DataAvailable += (S, E) =>
                {
                    var result = bufferedProvider.AddSamplesWithOverflowCheck(E.Buffer, 0, E.BytesRecorded);
                    
                    if (result.OverflowDetected)
                    {
                        _totalOverflowCount++;
                        _totalOverflowBytes += result.DiscardedBytes;
                        
                        // 记录溢出事件（仅在 Debug 模式）
                        Debug.WriteLine($"[Audio] Buffer overflow detected: {result.DiscardedBytes} bytes discarded. Total overflows: {_totalOverflowCount}");
                    }
                };

                _audioProviders.Add(provider, bufferedProvider);
            }

            // 构建混音管道
            var sampleProviders = _audioProviders.Values.Select(bufferedProvider =>
            {
                var sampleProvider = bufferedProvider.ToSampleProvider();

                var providerWf = bufferedProvider.WaveFormat;

                // Mono to Stereo
                if (providerWf.Channels == 1)
                    sampleProvider = sampleProvider.ToStereo();

                // Resample
                if (providerWf.SampleRate != WaveFormat.SampleRate)
                {
                    sampleProvider = new WdlResamplingSampleProvider(sampleProvider, WaveFormat.SampleRate);
                }

                return sampleProvider;
            }).ToArray();

            if (sampleProviders.Length == 1)
            {
                _mixingWaveProvider = sampleProviders[0].ToWaveProvider16();
            }
            else
            {
                var waveProviders = sampleProviders.Select(M => M.ToWaveProvider());

                // MixingSampleProvider cannot be used here due to it removing inputs that don't return as many bytes as requested.

                // Screna expects 44.1 kHz 16-bit Stereo
                _mixingWaveProvider = new MixingWaveProvider32(waveProviders)
                    .ToSampleProvider()
                    .ToWaveProvider16();
            }
        }

        /// <summary>
        /// 获取音频溢出统计信息
        /// </summary>
        public (long OverflowCount, long OverflowBytes) GetOverflowStats()
        {
            return (_totalOverflowCount, _totalOverflowBytes);
        }

        public void Dispose()
        {
            foreach (var provider in _audioProviders.Keys)
            {
                provider.Dispose();
            }
        }

        public WaveFormat WaveFormat { get; } = new WaveFormat();

        public void Start()
        {
            foreach (var provider in _audioProviders.Keys)
            {
                provider.Start();
            }
        }

        public void Stop()
        {
            foreach (var provider in _audioProviders.Keys)
            {
                provider.Stop();
            }
        }

        public int Read(byte[] Buffer, int Offset, int Length)
        {
            return _mixingWaveProvider.Read(Buffer, Offset, Length);
        }

        /// <summary>
        /// 带溢出检测的 BufferedWaveProvider 包装器
        /// </summary>
        class MonitoredBufferedWaveProvider : IWaveProvider
        {
            readonly BufferedWaveProvider _innerProvider;

            public MonitoredBufferedWaveProvider(WaveFormat waveFormat)
            {
                _innerProvider = new BufferedWaveProvider(waveFormat);
            }

            public WaveFormat WaveFormat => _innerProvider.WaveFormat;

            public TimeSpan BufferDuration
            {
                get => _innerProvider.BufferDuration;
                set => _innerProvider.BufferDuration = value;
            }

            public bool DiscardOnBufferOverflow
            {
                get => _innerProvider.DiscardOnBufferOverflow;
                set => _innerProvider.DiscardOnBufferOverflow = value;
            }

            public int BufferedBytes => _innerProvider.BufferedBytes;

            public AddSamplesResult AddSamplesWithOverflowCheck(byte[] buffer, int offset, int count)
            {
                var bufferedBefore = _innerProvider.BufferedBytes;
                
                // 检查是否会导致溢出
                var availableSpace = _innerProvider.BufferLength - bufferedBefore;
                var willOverflow = count > availableSpace;

                if (willOverflow)
                {
                    // 缓冲区即将溢出，清除最旧的样本为新数据腾出空间
                    var discardBytes = count - availableSpace;
                    _innerProvider.ClearBuffer();
                    
                    _innerProvider.AddSamples(buffer, offset, count);
                    
                    return new AddSamplesResult
                    {
                        OverflowDetected = true,
                        DiscardedBytes = bufferedBefore + discardBytes
                    };
                }

                _innerProvider.AddSamples(buffer, offset, count);
                return new AddSamplesResult { OverflowDetected = false };
            }

            public int Read(byte[] buffer, int offset, int count)
            {
                return _innerProvider.Read(buffer, offset, count);
            }
        }

        struct AddSamplesResult
        {
            public bool OverflowDetected;
            public long DiscardedBytes;
        }
    }
}