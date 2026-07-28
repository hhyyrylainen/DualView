namespace DualView.GUI.Services;

public interface IAudioPlaybackService
{
    void PlaySamples(int streamId, byte[] samples);
    void ClearQueue(int streamId);
}
