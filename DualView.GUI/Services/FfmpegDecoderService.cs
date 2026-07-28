using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FFmpeg.AutoGen;
using ImageMagick;
using Microsoft.Extensions.Logging;

namespace DualView.GUI.Services;

public class FfmpegDecoderService : IDisposable
{
    private readonly ILogger<FfmpegDecoderService> logger;

    private readonly Regex versionRegex = new(@"so\.(\d+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly string ffmpegPath = "";

    public FfmpegDecoderService(ILogger<FfmpegDecoderService> logger, IGuiConfigurationService guiConfigurationService)
    {
        this.logger = logger;

        if (!string.IsNullOrWhiteSpace(guiConfigurationService.FfmpegLibraryPath))
            ffmpegPath = guiConfigurationService.FfmpegLibraryPath;

        if (!string.IsNullOrWhiteSpace(ffmpegPath))
        {
            logger.LogInformation("Setting ffmpeg path to {FfmpegPath}", ffmpegPath);
            ffmpeg.RootPath = ffmpegPath;

            AdjustFfmpegVersions();
        }
    }

    public AvFormatWrapper OpenFfmpeg(Stream data)
    {
        return new AvFormatWrapper(data, logger);
    }

    // public (LibVLC VLC, SemaphoreSlim Lock) GetLibVLC() => (libVLC, locker);

    public void Dispose()
    {
    }

    private void AdjustFfmpegVersions()
    {
        var original = ffmpeg.LibraryVersionMap.ToDictionary(x => x.Key, x => x.Value);

        // TODO: other platforms than Linux
        if (!OperatingSystem.IsLinux())
            logger.LogWarning("FFmpeg library version adjustment only supported on Linux!");

        var libFiles = Directory.EnumerateFiles(ffmpegPath, "lib*.so*").ToList();

        foreach (var entry in original)
        {
            var expectedBase = Path.Join(ffmpegPath, "lib" + entry.Key + ".so.");

            var expected = expectedBase + entry.Value.ToString(CultureInfo.InvariantCulture);

            if (File.Exists(expected))
            {
                // All good
                logger.LogInformation("Found ffmpeg version of {Component} at {Path} (exactly as wanted)", entry.Key,
                    expected);
                continue;
            }

            // Try to find a version that is installed
            // Get the newest version
            // TODO: could allow GUI settings file to specify these exactly
            int bestFound = -1;
            string? path = null;
            foreach (var found in libFiles.Where(x => x.StartsWith(expectedBase)))
            {
                var match = versionRegex.Match(found);

                // If a version with a name subversion suffix, avoid
                if (!match.Success)
                    continue;

                // Catch the actual version
                var version = int.Parse(match.Groups[1].Value);

                if (version > bestFound)
                {
                    bestFound = version;
                    path = found;
                }
            }

            if (bestFound < 1)
            {
                logger.LogWarning(
                    "Could not find ffmpeg version of {Component}, might error out on trying to use it!", entry.Key);
            }
            else
            {
                logger.LogInformation("Found ffmpeg version of {Component} at {Path} (adjusted from {Version})",
                    entry.Key, path, entry.Value);
                ffmpeg.LibraryVersionMap[entry.Key] = bestFound;
            }
        }
    }

    /// <summary>
    ///   Wrapper for Ffmpeg AVFormatContext that allows reading from a stream. Note that this uses blocking operations
    ///   as this is unsafe, so async can't be used.
    /// </summary>
    public sealed class AvFormatWrapper : IDisposable
    {
        private const int AVSEEK_SIZE = 0x10000;

        private const int AvIoBufferSize = 4096;

        private const int RequiredSwScaleAlignment = 64;

        private readonly Stream data;
        private readonly ILogger logger;
        private readonly SemaphoreSlim semaphore = new(1, 1);

        // These must be kept to keep the function pointers valid
        // ReSharper disable PrivateFieldCanBeConvertedToLocalVariable
        private readonly Delegate read;
        private readonly Delegate seek;
        // ReSharper restore PrivateFieldCanBeConvertedToLocalVariable

        private bool disposedValue;

        private unsafe AVFormatContext* formatContext;
        private unsafe AVIOContext* avIoCtx;

        private unsafe AVCodecContext* videoCodecContext;
        private int videoStreamIndex = -1;

        public int VideoThreadCount { get; set; } = 1;

        private unsafe AVCodecContext* audioCodecContext;
        private int audioStreamIndex = -1;

        private unsafe AVPacket* packet;
        private unsafe AVFrame* frame;
        private unsafe AVFrame* audioFrame;

        private bool hasReadyFrame;

        private unsafe SwsContext* swsCtx;
        private unsafe AVFrame* rgbaFrame = null;
        private unsafe byte* rgbaBuffer = null;
        private int rgbaBufferSize;

        private unsafe SwrContext* swrCtx;

        public event Action<byte[]>? OnAudioSamplesDecoded;

        private bool readFormat;

        private avio_alloc_context_read_packet_func readPacketFunc;
        private avio_alloc_context_seek_func seekFunc;

        private double frameRate = -1;
        private int streamWidth = -1;
        private int streamHeight = -1;
        private bool reachedEnd;

        public unsafe AvFormatWrapper(Stream data, ILogger logger)
        {
            if (!data.CanSeek || !data.CanRead)
                throw new ArgumentException("Stream must be seekable and readable");

            this.data = data;
            this.logger = logger;
            data.Seek(0, SeekOrigin.Begin);

            read = ReadPacket;
            seek = SeekStream;

            readPacketFunc.Pointer = Marshal.GetFunctionPointerForDelegate(read);
            seekFunc.Pointer = Marshal.GetFunctionPointerForDelegate(seek);

            formatContext = ffmpeg.avformat_alloc_context();

            fixed (AVFormatContext** formatContextPtr = &formatContext)
            {
                var avIoBuffer = (byte*)ffmpeg.av_malloc(AvIoBufferSize);

                if (avIoBuffer == null)
                    throw new OutOfMemoryException();

                avIoCtx = ffmpeg.avio_alloc_context(avIoBuffer, AvIoBufferSize, 0, null,
                    readPacketFunc, null, seekFunc);

                if (avIoCtx == null)
                {
                    ffmpeg.av_free(avIoBuffer);
                    throw new Exception("Failed to create AV IOContext");
                }

                formatContext->pb = avIoCtx;

                if (ffmpeg.avformat_open_input(formatContextPtr, null, null, null) < 0)
                    throw new Exception("Failed to open stream");

                packet = ffmpeg.av_packet_alloc();
                frame = ffmpeg.av_frame_alloc();
            }
        }

        ~AvFormatWrapper()
        {
            Dispose(false);
        }

        public unsafe bool FindStreamInfo()
        {
            ThrowIfDisposed();

            semaphore.Wait();
            try
            {
                if (readFormat)
                    return true;

                ThrowIfDisposed();

                int result = ffmpeg.avformat_find_stream_info(formatContext, null);

                if (result >= 0)
                {
                    readFormat = true;

                    // Code for debugging if needed
                    // ffmpeg.av_dump_format(formatContext, 0, "", 0);

                    return true;
                }

                // Error!
                logger.LogError("Error finding stream info: {Result}", result);
                return false;
            }
            finally
            {
                semaphore.Release();
            }
        }

        public unsafe bool OpenVideoStream()
        {
            semaphore.Wait();
            try
            {
                if (!readFormat)
                    throw new InvalidOperationException("Must call FindStreamInfo first");

                ThrowIfDisposed();

                if (videoCodecContext != null)
                {
                    logger.LogDebug("Video stream already open");
                    return true;
                }

                var streamIndex =
                    ffmpeg.av_find_best_stream(formatContext, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, null, 0);

                if (streamIndex < 0)
                {
                    logger.LogError("Could not find video stream");
                    return false;
                }

                var videoStream = formatContext->streams[streamIndex];
                videoStreamIndex = streamIndex;

                frameRate = videoStream->avg_frame_rate.num / (double)videoStream->avg_frame_rate.den;

                streamWidth = videoStream->codecpar->width;
                streamHeight = videoStream->codecpar->height;

                // Find a decoder
                var decoder = ffmpeg.avcodec_find_decoder(videoStream->codecpar->codec_id);

                if (decoder == null)
                {
                    logger.LogError("Could not find video decoder for codec {Id}", videoStream->codecpar->codec_id);
                    return false;
                }

                videoCodecContext = ffmpeg.avcodec_alloc_context3(decoder);

                if (videoCodecContext == null)
                {
                    logger.LogError("Could not allocate video codec context");
                    return false;
                }

                videoCodecContext->thread_count = VideoThreadCount;

                int result = ffmpeg.avcodec_parameters_to_context(videoCodecContext, videoStream->codecpar);

                if (result < 0)
                {
                    logger.LogError("Failed to copy codec parameters to video codec context: {Result}", result);
                    fixed (AVCodecContext** codecContextPtr = &videoCodecContext)
                        ffmpeg.avcodec_free_context(codecContextPtr);
                    videoCodecContext = null;
                    return false;
                }

                result = ffmpeg.avcodec_open2(videoCodecContext, decoder, null);

                if (result < 0)
                {
                    logger.LogError("Failed to open video codec context: {Result}", result);
                    fixed (AVCodecContext** codecContextPtr = &videoCodecContext)
                        ffmpeg.avcodec_free_context(codecContextPtr);
                    videoCodecContext = null;
                    return false;
                }

                return true;
            }
            finally
            {
                semaphore.Release();
            }
        }

        public unsafe bool OpenAudioStream()
        {
            semaphore.Wait();
            try
            {
                if (!readFormat)
                    throw new InvalidOperationException("Must call FindStreamInfo first");

                ThrowIfDisposed();

                if (audioCodecContext != null)
                {
                    logger.LogDebug("Audio stream already open");
                    return true;
                }

                var streamIndex =
                    ffmpeg.av_find_best_stream(formatContext, AVMediaType.AVMEDIA_TYPE_AUDIO, -1, -1, null, 0);

                if (streamIndex < 0)
                {
                    logger.LogDebug("No audio stream found");
                    return false;
                }

                var audioStream = formatContext->streams[streamIndex];
                audioStreamIndex = streamIndex;

                // Find a decoder
                var decoder = ffmpeg.avcodec_find_decoder(audioStream->codecpar->codec_id);

                if (decoder == null)
                {
                    logger.LogError("Could not find audio decoder for codec {Id}", audioStream->codecpar->codec_id);
                    return false;
                }

                audioCodecContext = ffmpeg.avcodec_alloc_context3(decoder);

                if (audioCodecContext == null)
                {
                    logger.LogError("Could not allocate audio codec context");
                    return false;
                }

                int result = ffmpeg.avcodec_parameters_to_context(audioCodecContext, audioStream->codecpar);

                if (result < 0)
                {
                    logger.LogError("Failed to copy codec parameters to audio codec context: {Result}", result);
                    fixed (AVCodecContext** codecContextPtr = &audioCodecContext)
                        ffmpeg.avcodec_free_context(codecContextPtr);
                    audioCodecContext = null;
                    return false;
                }

                result = ffmpeg.avcodec_open2(audioCodecContext, decoder, null);

                if (result < 0)
                {
                    logger.LogError("Failed to open audio codec context: {Result}", result);
                    fixed (AVCodecContext** codecContextPtr = &audioCodecContext)
                        ffmpeg.avcodec_free_context(codecContextPtr);
                    audioCodecContext = null;
                    return false;
                }

                audioFrame = ffmpeg.av_frame_alloc();

                // Setup resampling to S16, Stereo, 44100Hz
                AVChannelLayout outChannelLayout;
                ffmpeg.av_channel_layout_default(&outChannelLayout, 2);

                swrCtx = ffmpeg.swr_alloc();
                fixed (SwrContext** swrCtxPtr = &swrCtx)
                {
                    ffmpeg.swr_alloc_set_opts2(swrCtxPtr, &outChannelLayout, AVSampleFormat.AV_SAMPLE_FMT_S16, 44100,
                        &audioCodecContext->ch_layout, audioCodecContext->sample_fmt, audioCodecContext->sample_rate, 0,
                        null);
                }

                ffmpeg.swr_init(swrCtx);

                return true;
            }
            finally
            {
                semaphore.Release();
            }
        }

        // NOTE: this takes the width from the codec info. If the actual size is different, it may cause some weird
        // stretching
        public unsafe bool GetVideoSize(out int width, out int height)
        {
            ThrowIfDisposed();

            semaphore.Wait();
            try
            {
                if (videoCodecContext == null)
                {
                    width = -1;
                    height = -1;
                    return false;
                }

                width = streamWidth;
                height = streamHeight;
                return true;
            }
            finally
            {
                semaphore.Release();
            }
        }

        public bool GetFrameRate(out double fps)
        {
            ThrowIfDisposed();

            semaphore.Wait();
            try
            {
                if (frameRate <= 0 || double.IsNaN(frameRate))
                {
                    fps = -1;
                    return false;
                }

                // We can't read from the decoder context as it doesn't always have the framerate
                fps = frameRate;
                return true;
            }
            finally
            {
                semaphore.Release();
            }
        }

        public bool ReadUntilNextFrame()
        {
            semaphore.Wait();
            try
            {
                return ReadUntilNextFrameInternal();
            }
            finally
            {
                semaphore.Release();
            }
        }

        public async Task<bool> ReadUntilNextFrameAsync(CancellationToken token)
        {
            await semaphore.WaitAsync(token).ConfigureAwait(false);
            try
            {
                return ReadUntilNextFrameInternal();
            }
            finally
            {
                semaphore.Release();
            }
        }

        private unsafe bool ReadUntilNextFrameInternal()
        {
            ThrowIfDisposed();

            if (videoCodecContext == null)
            {
                logger.LogError("Video stream not open");
                return false;
            }

            if (hasReadyFrame)
            {
                // Frame was not used
                hasReadyFrame = false;

                ffmpeg.av_frame_unref(frame);
            }

            // TODO: once we process audio, we might get "ahead" on the video stream, so we might actually want to
            // check the decoder first if it has a frame before always sending at least one packet
            while (ffmpeg.av_read_frame(formatContext, packet) >= 0)
            {
                if (packet->stream_index == videoStreamIndex)
                {
                    if (ffmpeg.avcodec_send_packet(videoCodecContext, packet) < 0)
                        logger.LogError("Failed to send packet to decoder");

                    int result = ffmpeg.avcodec_receive_frame(videoCodecContext, frame);
                    if (result < 0)
                    {
                        if (result != -ffmpeg.EAGAIN && result != ffmpeg.AVERROR_EOF)
                        {
                            logger.LogError("Failed to receive frame from decoder: {Result}", result);
                            return false;
                        }
                        else
                        {
                            // Assume need more data
                            continue;
                        }
                    }

                    // We received a frame!
                    // It will be unreferenced when processed or if this is called again
                    hasReadyFrame = true;
                    return true;
                }

                if (packet->stream_index == audioStreamIndex && audioCodecContext != null)
                {
                    if (ffmpeg.avcodec_send_packet(audioCodecContext, packet) >= 0)
                    {
                        while (ffmpeg.avcodec_receive_frame(audioCodecContext, audioFrame) >= 0)
                        {
                            var samples = ResampleAudio(audioFrame);
                            if (samples != null)
                                OnAudioSamplesDecoded?.Invoke(samples);
                            ffmpeg.av_frame_unref(audioFrame);
                        }
                    }
                }

                ffmpeg.av_packet_unref(packet);
            }

            // End of stream so flush the decoders
            if (videoCodecContext != null)
                ffmpeg.avcodec_send_packet(videoCodecContext, null);

            if (audioCodecContext != null)
                ffmpeg.avcodec_send_packet(audioCodecContext, null);

            reachedEnd = true;

            // Probably ended
            return false;
        }

        private unsafe byte[]? ResampleAudio(AVFrame* currentFrame)
        {
            if (swrCtx == null)
                return null;

            int outSamples = (int)ffmpeg.av_rescale_rnd(
                ffmpeg.swr_get_delay(swrCtx, 44100) + currentFrame->nb_samples, 44100, currentFrame->sample_rate,
                AVRounding.AV_ROUND_UP);

            int outChannels = 2;
            int bytesPerSample = ffmpeg.av_get_bytes_per_sample(AVSampleFormat.AV_SAMPLE_FMT_S16);
            int bufferSize = outSamples * outChannels * bytesPerSample;

            // TODO: reuse the buffer
            byte[] buffer = new byte[bufferSize];

            fixed (byte* pBuffer = buffer)
            {
                byte* pOut = pBuffer;
                byte** ppOut = &pOut;
                byte** ppIn = (byte**)&currentFrame->data;
                int converted = ffmpeg.swr_convert(swrCtx, ppOut, outSamples, ppIn,
                    currentFrame->nb_samples);
                if (converted < 0)
                    return null;

                int actualSize = converted * outChannels * bytesPerSample;
                if (actualSize < buffer.Length)
                {
                    Array.Resize(ref buffer, actualSize);
                }
            }

            return buffer;
        }

        public unsafe bool DecodeCurrentFrame(MagickImage target)
        {
            semaphore.Wait();
            try
            {
                if (!hasReadyFrame)
                {
                    logger.LogError("No frame ready to decode");
                    return false;
                }

                ThrowIfDisposed();

                var bytesPerPixel = target.HasAlpha ? 4 : 3;

                var dstFmt = target.HasAlpha ? AVPixelFormat.AV_PIX_FMT_RGBA : AVPixelFormat.AV_PIX_FMT_RGB24;

                int srcW = frame->width;
                int srcH = frame->height;
                var srcFmt = (AVPixelFormat)frame->format;

                // We allow destination size to be what the display wants
                int targetW = (int)target.Width;
                int targetH = (int)target.Height;

                swsCtx = ffmpeg.sws_getCachedContext(swsCtx, srcW, srcH, srcFmt, targetW, targetH,
                    dstFmt, (int)SwsFlags.SWS_BILINEAR, null, null, null);

                if (swsCtx == null)
                    throw new Exception("Failed to create SWS context");

                if (rgbaFrame == null)
                    rgbaFrame = ffmpeg.av_frame_alloc();

                rgbaFrame->width = targetW;
                rgbaFrame->height = targetH;
                rgbaFrame->format = (int)dstFmt;

                int needed = ffmpeg.av_image_get_buffer_size(dstFmt, targetW, targetH, RequiredSwScaleAlignment);
                if (needed < 0)
                    throw new InvalidOperationException("av_image_get_buffer_size failed");

                if (rgbaBuffer == null || rgbaBufferSize < needed)
                {
                    if (rgbaBuffer != null)
                    {
                        ffmpeg.av_free(rgbaBuffer);
                        rgbaBuffer = null;
                    }

                    rgbaBuffer = (byte*)ffmpeg.av_malloc((ulong)needed);
                    if (rgbaBuffer == null)
                        throw new OutOfMemoryException("av_malloc failed for RGBA buffer");

                    rgbaBufferSize = needed;
                }

                byte_ptrArray4 dstData4 = default;
                int_array4 dstLineSize4 = default;

                int fillRes = ffmpeg.av_image_fill_arrays(ref dstData4, ref dstLineSize4,
                    rgbaBuffer, dstFmt,
                    targetW, targetH, RequiredSwScaleAlignment);

                if (fillRes < 0)
                    throw new InvalidOperationException("av_image_fill_arrays failed");

                // Copy plane pointers/strides into the AVFrame (first 4 planes)
                rgbaFrame->data[0] = dstData4[0];
                rgbaFrame->data[1] = dstData4[1];
                rgbaFrame->data[2] = dstData4[2];
                rgbaFrame->data[3] = dstData4[3];

                rgbaFrame->linesize[0] = dstLineSize4[0];
                rgbaFrame->linesize[1] = dstLineSize4[1];
                rgbaFrame->linesize[2] = dstLineSize4[2];
                rgbaFrame->linesize[3] = dstLineSize4[3];

                // (Optional) clear remaining planes to be tidy
                for (uint i = 4; i < 8; i++)
                {
                    rgbaFrame->data[i] = null;
                    rgbaFrame->linesize[i] = 0;
                }

                AVFrame* srcFrame = frame;

                if (IsHardwarePixelFormat((AVPixelFormat)frame->format))
                {
                    throw new Exception("Hardware decoded pixel formats is not supported yet");

                    // Free temporary (after the sws_scale, though probably should persist this)
                    /*if (srcFrame != frame)
                        ffmpeg.av_frame_free(&srcFrame);*/
                }

                int scaledH = ffmpeg.sws_scale(
                    swsCtx,
                    srcFrame->data, srcFrame->linesize,
                    0,
                    srcFrame->height, rgbaFrame->data, rgbaFrame->linesize);

                if (scaledH != targetH)
                    logger.LogDebug("sws_scale returned {ScaledH} (expected {TargetH})", scaledH, targetH);

                using var receiver = target.GetPixelsUnsafe();
                var targetPtr = (byte*)receiver.GetAreaPointer(0, 0, target.Width, target.Height).ToPointer();
                var targetStride = targetW * bytesPerPixel;

                int copyWidth = targetStride;

                var sourceStride = rgbaFrame->linesize[0];

                byte* source = rgbaFrame->data[0];

                for (int y = 0; y < targetH; ++y)
                {
                    Buffer.MemoryCopy(source + y * sourceStride, targetPtr + y * targetStride, copyWidth, copyWidth);
                }

                ffmpeg.av_frame_unref(frame);
                hasReadyFrame = false;
            }
            finally
            {
                semaphore.Release();
            }

            return true;
        }

        public void RestartVideo()
        {
            semaphore.Wait();
            try
            {
                ThrowIfDisposed();

                RestartVideoInternal();
            }
            finally
            {
                semaphore.Release();
            }
        }

        public async Task RestartVideoAsync(CancellationToken cancellationToken)
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();

                RestartVideoInternal();
            }
            finally
            {
                semaphore.Release();
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private static unsafe bool IsHardwarePixelFormat(AVPixelFormat fmt)
        {
            AVPixFmtDescriptor* desc = ffmpeg.av_pix_fmt_desc_get(fmt);
            if (desc == null)
                return false;
            return (desc->flags & ffmpeg.AV_PIX_FMT_FLAG_HWACCEL) != 0;
        }

        private unsafe int ReadPacket(void* opaque, byte* buf, int bufSize)
        {
            if (disposedValue)
                return ffmpeg.AVERROR_INVALIDDATA;

            // TODO: lock here as well?

            var bytesRead = data.Read(new Span<byte>(buf, bufSize));

            // Need to properly end the file if at the end
            if (bytesRead < 1)
                return ffmpeg.AVERROR_EOF;

            return bytesRead;
        }

        private unsafe long SeekStream(void* opaque, long offset, int whence)
        {
            if (disposedValue)
                return -1;

            // Handle different operation modes.
            // The first is just asking for the size
            if ((whence & AVSEEK_SIZE) != 0)
            {
                try
                {
                    return data.Length;
                }
                catch
                {
                    // For Stream: Length may throw if not supported.
                    logger.LogError("Stream length cannot be read");
                    return -1;
                }
            }

            // Actual seek has the data in the low 2 bits
            int originBits = whence & 0x3;

            var origin = originBits switch
            {
                0 => SeekOrigin.Begin, // SEEK_SET
                1 => SeekOrigin.Current, // SEEK_CUR
                2 => SeekOrigin.End, // SEEK_END
                _ => SeekOrigin.Begin,
            };

            try
            {
                return data.Seek(offset, origin);
            }
            catch
            {
                logger.LogError("Failed to seek stream");
                return -1;
            }
        }

        private void Dispose(bool disposing)
        {
            if (disposedValue)
            {
#if DEBUG
                logger.LogError("Double dispose on {Type}", nameof(AvFormatWrapper));
#endif
                return;
            }

            readFormat = false;

            if (disposing)
            {
                semaphore.Wait();
                try
                {
                    if (disposedValue)
                        throw new Exception("Concurrent double dispose");

                    disposedValue = true;

                    FreeNativeResources();

                    data.Dispose();
                }
                finally
                {
                    semaphore.Release();
                }
            }
            else
            {
                disposedValue = true;
                FreeNativeResources();
            }
        }

        /// <summary>
        ///   Must be called after semaphore is already locked
        /// </summary>
        /// <exception cref="Exception"></exception>
        private unsafe void RestartVideoInternal()
        {
            // Flush things
            if (hasReadyFrame)
            {
                ffmpeg.av_frame_unref(frame);
                hasReadyFrame = false;
            }

            if (!reachedEnd)
            {
                if (videoCodecContext != null)
                    ffmpeg.avcodec_send_packet(videoCodecContext, null);

                if (audioCodecContext != null)
                    ffmpeg.avcodec_send_packet(audioCodecContext, null);

                reachedEnd = true;
            }

            // Packet shouldn't be able to still have reference to anything at this point

            // And then we are ready to read again
            ffmpeg.avcodec_flush_buffers(videoCodecContext);

            if (audioCodecContext != null)
                ffmpeg.avcodec_flush_buffers(audioCodecContext);

            // Seek demuxer back to the beginning.
            // Use AV_TIME_BASE time space with stream_index = -1 (seek by global timestamp).
            // If your custom IO supports seeking, this should work.
            long targetTs = 0;

            // If the container has a non-zero start_time, seeking to it can be more reliable.
            if (formatContext->start_time != ffmpeg.AV_NOPTS_VALUE && formatContext->start_time > 0)
                targetTs = formatContext->start_time;

            int seekRes = ffmpeg.avformat_seek_file(formatContext, -1, long.MinValue, targetTs, long.MaxValue,
                ffmpeg.AVSEEK_FLAG_BACKWARD);

            if (seekRes < 0)
            {
                // Fallback: try av_seek_frame
                seekRes = ffmpeg.av_seek_frame(formatContext, -1, targetTs, ffmpeg.AVSEEK_FLAG_BACKWARD);
                if (seekRes < 0)
                    throw new Exception($"Failed to seek to start: {seekRes}");
            }

            // Clear demuxer internal buffers after seeking
            ffmpeg.avformat_flush(formatContext);

            reachedEnd = false;
        }

        private unsafe void FreeNativeResources()
        {
            if (hasReadyFrame)
            {
                hasReadyFrame = false;
                ffmpeg.av_frame_unref(frame);
            }

            // Flush decoders if not reached the stream end
            if (!reachedEnd)
            {
                if (videoCodecContext != null)
                    ffmpeg.avcodec_send_packet(videoCodecContext, null);

                if (audioCodecContext != null)
                    ffmpeg.avcodec_send_packet(audioCodecContext, null);

                reachedEnd = true;
            }

            fixed (AVCodecContext** codecContextPtr = &videoCodecContext)
            {
                if (videoCodecContext != null)
                    ffmpeg.avcodec_free_context(codecContextPtr);
                videoCodecContext = null;
            }

            fixed (AVCodecContext** codecContextPtr = &audioCodecContext)
            {
                if (audioCodecContext != null)
                    ffmpeg.avcodec_free_context(codecContextPtr);
                audioCodecContext = null;
            }

            fixed (AVFormatContext** formatContextPtr = &formatContext)
            {
                if (formatContext != null)
                    ffmpeg.avformat_close_input(formatContextPtr);

                formatContext = null;
            }

            fixed (AVIOContext** avIoCtxPtr = &avIoCtx)
            {
                if (avIoCtx != null)
                {
                    // Free the buffer
                    ffmpeg.av_freep(&avIoCtx->buffer);

                    if (avIoCtx->buffer != null)
                        throw new Exception("IO buffer didn't get cleared");

                    ffmpeg.avio_context_free(avIoCtxPtr);
                }

                avIoCtx = null;
            }

            readPacketFunc.Pointer = IntPtr.Zero;
            seekFunc.Pointer = IntPtr.Zero;

            fixed (AVPacket** avPacketPtr = &packet)
            {
                if (packet != null)
                    ffmpeg.av_packet_free(avPacketPtr);
                packet = null;
            }

            fixed (AVFrame** framePtr = &frame)
            {
                if (frame != null)
                    ffmpeg.av_frame_free(framePtr);
                frame = null;
            }

            fixed (AVFrame** framePtr = &audioFrame)
            {
                if (audioFrame != null)
                    ffmpeg.av_frame_free(framePtr);
                audioFrame = null;
            }

            if (swsCtx != null)
            {
                if (swsCtx != null)
                    ffmpeg.sws_freeContext(swsCtx);
                swsCtx = null;
            }

            if (rgbaBuffer != null)
            {
                if (rgbaBuffer != null)
                    ffmpeg.av_free(rgbaBuffer);
                rgbaBuffer = null;
                rgbaBufferSize = 0;
            }

            fixed (AVFrame** framePtr = &rgbaFrame)
            {
                if (rgbaFrame != null)
                    ffmpeg.av_frame_free(framePtr);
                rgbaFrame = null;
            }

            fixed (SwrContext** swrCtxPtr = &swrCtx)
            {
                if (swrCtx != null)
                    ffmpeg.swr_free(swrCtxPtr);
                swrCtx = null;
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposedValue)
                throw new ObjectDisposedException(nameof(AvFormatWrapper));
        }
    }
}
