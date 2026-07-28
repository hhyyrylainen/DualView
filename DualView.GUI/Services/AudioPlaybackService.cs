using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using DualView.Shared.Services;
using Microsoft.Extensions.Logging;
using Silk.NET.SDL;

namespace DualView.GUI.Services;

public sealed class AudioPlaybackService : IAudioPlaybackService, IDisposable
{
    private readonly ILogger<AudioPlaybackService> logger;
    private readonly IClientDatabaseService databaseService;
    private readonly Sdl sdl;
    private readonly List<AudioPlayer> pool = new();
    private readonly Dictionary<int, AudioPlayer> activeStreams = new();
    private readonly SemaphoreSlim poolLock = new(1, 1);
    private readonly CancellationTokenSource cleanupCts = new();

    private bool disposed;
    private int bufferingThreshold;

    public AudioPlaybackService(ILogger<AudioPlaybackService> logger, IClientDatabaseService databaseService,
        ISignalRService signalRService)
    {
        this.logger = logger;
        this.databaseService = databaseService;
        sdl = Sdl.GetApi();

        if (sdl.Init(Sdl.InitAudio) != 0)
        {
            logger.LogError("Failed to initialize SDL audio: {Error}", GetSdlError());
            return;
        }

        // Default threshold
        UpdateBufferingThreshold(60);

        signalRService.OnAppSettingsUpdated += HandleAppSettingsUpdated;

        // Load actual settings
        _ = Task.Run(LoadSettings);

        _ = Task.Run(() => CleanupTask(cleanupCts.Token));
    }

    public void PlaySamples(int streamId, byte[] samples)
    {
        poolLock.Wait();
        AudioPlayer? player;
        try
        {
            if (!activeStreams.TryGetValue(streamId, out player))
            {
                try
                {
                    player = GetPlayerFromPool();
                    player.CurrentStreamId = streamId;
                    activeStreams[streamId] = player;
                    logger.LogDebug("Assigned audio player to stream {StreamId}", streamId);
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Failed to get audio player for stream {StreamId}", streamId);
                    return;
                }
            }
        }
        finally
        {
            poolLock.Release();
        }

        player.PlaySamples(samples, bufferingThreshold);
    }

    public void ClearQueue(int streamId)
    {
        poolLock.Wait();
        AudioPlayer? player;
        try
        {
            if (!activeStreams.TryGetValue(streamId, out player))
                return;
        }
        finally
        {
            poolLock.Release();
        }

        player.ClearQueue();
    }

    public void Dispose()
    {
        if (disposed)
            return;

        cleanupCts.Cancel();
        cleanupCts.Dispose();

        poolLock.Wait();
        try
        {
            foreach (var player in activeStreams.Values)
            {
                player.Dispose();
            }

            activeStreams.Clear();

            foreach (var player in pool)
            {
                player.Dispose();
            }

            pool.Clear();
        }
        finally
        {
            poolLock.Release();
        }

        sdl.QuitSubSystem(Sdl.InitAudio);
        sdl.Dispose();
        disposed = true;
    }

    private void HandleAppSettingsUpdated()
    {
        _ = Task.Run(LoadSettings);
    }

    private async Task LoadSettings()
    {
        try
        {
            var settings = await databaseService.GetAppSettingsAsync();
            UpdateBufferingThreshold(settings.AudioBufferingMs);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Failed to load audio buffering settings");
        }
    }

    private void UpdateBufferingThreshold(int ms)
    {
        // 44100Hz, 16-bit, 2 channels = 176400 bytes/s
        bufferingThreshold = (int)(ms / 1000.0 * 176400);
        logger.LogDebug("Audio buffering threshold set to {Threshold} bytes ({Ms}ms)", bufferingThreshold, ms);
    }

    private AudioPlayer GetPlayerFromPool()
    {
        if (pool.Count > 0)
        {
            var player = pool[^1];
            pool.RemoveAt(pool.Count - 1);
            return player;
        }

        return CreateNewPlayer();
    }

    private unsafe AudioPlayer CreateNewPlayer()
    {
        AudioSpec desired = new AudioSpec
        {
            Freq = 44100,
            Format = 0x8010, // AUDIO_S16SYS
            Channels = 2,
            Samples = 4096,
        };

        AudioSpec obtained;
        uint device = sdl.OpenAudioDevice((byte*)null, 0, &desired, &obtained, 0);

        if (device == 0)
        {
            throw new Exception($"Failed to open SDL audio device: {GetSdlError()}");
        }

        sdl.PauseAudioDevice(device, 0);
        logger.LogInformation("Opened SDL audio device: {Freq}Hz, {Channels} channels", obtained.Freq,
            obtained.Channels);

        return new AudioPlayer(sdl, device);
    }

    private async Task CleanupTask(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            poolLock.Wait();
            try
            {
                if (token.IsCancellationRequested)
                    break;

                var now = DateTime.UtcNow;
                var toRemove = activeStreams.Where(kvp => (now - kvp.Value.LastUsed).TotalSeconds > 10).ToList();
                foreach (var kvp in toRemove)
                {
                    var player = kvp.Value;
                    player.ClearQueue();
                    player.CurrentStreamId = null;
                    activeStreams.Remove(kvp.Key);
                    pool.Add(player);
                    logger.LogDebug("Audio player for stream {StreamId} returned to pool due to inactivity", kvp.Key);
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error in audio cleanup task");
            }
            finally
            {
                poolLock.Release();
            }
        }
    }

    private unsafe string GetSdlError()
    {
        return Marshal.PtrToStringAnsi((IntPtr)sdl.GetError()) ?? "Unknown SDL Error";
    }

    private sealed class AudioPlayer : IDisposable
    {
        private readonly Sdl sdl;
        private readonly uint device;
        private readonly List<byte> buffer = new();

        public AudioPlayer(Sdl sdl, uint device)
        {
            this.sdl = sdl;
            this.device = device;
            LastUsed = DateTime.UtcNow;
        }

        public int? CurrentStreamId { get; set; }
        public DateTime LastUsed { get; set; }
        public bool IsBuffering { get; set; } = true;

        public unsafe void PlaySamples(byte[] samples, int bufferingThreshold)
        {
            LastUsed = DateTime.UtcNow;

            if (IsBuffering)
            {
                buffer.AddRange(samples);
                if (buffer.Count >= bufferingThreshold)
                {
                    FlushBuffer();
                    IsBuffering = false;
                }
            }
            else
            {
                if (sdl.GetQueuedAudioSize(device) == 0)
                {
                    IsBuffering = true;
                    buffer.AddRange(samples);
                    if (buffer.Count >= bufferingThreshold)
                    {
                        FlushBuffer();
                        IsBuffering = false;
                    }

                    return;
                }

                fixed (byte* p = samples)
                {
                    sdl.QueueAudio(device, p, (uint)samples.Length);
                }
            }
        }

        private unsafe void FlushBuffer()
        {
            if (buffer.Count == 0)
                return;

            fixed (byte* p = buffer.ToArray())
            {
                sdl.QueueAudio(device, p, (uint)buffer.Count);
            }

            buffer.Clear();
        }

        public void ClearQueue()
        {
            sdl.ClearQueuedAudio(device);
            buffer.Clear();
            IsBuffering = true;
            LastUsed = DateTime.UtcNow;
        }

        public void Dispose()
        {
            sdl.CloseAudioDevice(device);
        }
    }
}
