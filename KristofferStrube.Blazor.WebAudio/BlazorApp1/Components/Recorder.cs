using KristofferStrube.Blazor.DOM;
using KristofferStrube.Blazor.FileAPI;
using KristofferStrube.Blazor.MediaCaptureStreams;
using KristofferStrube.Blazor.MediaStreamRecording;
using KristofferStrube.Blazor.WebAudio;
using KristofferStrube.Blazor.WebIDL;
using Microsoft.JSInterop;
using System.Text;

namespace BlazorApp1.Components;

public class AudioRecordContext
{
    private AudioContext _audCtx = default!;

    private readonly IJSRuntime _jsRT;
    private readonly MediaStream _mediaStream;

    private MediaRecorder _mediaRecorder = null!;

    private List<Blob> _blobRecord = [];

    private readonly float _sampleRate;
    private bool _recording = false;

    public AudioRecordContext(IJSRuntime jsRuntime, MediaStream mediaStream, float sampleRate = 16000)
    {
        _jsRT = jsRuntime;
        _mediaStream = mediaStream;
        _sampleRate = sampleRate;
    }

    public bool IsRecording
    {
        get { return _recording; }
    }

    public async Task EnsureInitializedAsync()
    {
        if (_audCtx is not null)
            return;

        _audCtx = await AudioContext.CreateAsync(_jsRT, new AudioContextOptions { SampleRate = _sampleRate });
    }

    public async Task StartAsync()
    {
        if (_recording) return;

        _blobRecord.Clear();

        _mediaRecorder = await MediaRecorder.CreateAsync(_jsRT, _mediaStream);
        var listener = await EventListener<BlobEvent>.CreateAsync(_jsRT,
            async (BlobEvent e) =>
            {
                _blobRecord.Add(await e.GetDataAsync());
            });
        await _mediaRecorder.AddOnDataAvailableEventListenerAsync(listener);
        await _mediaRecorder.StartAsync();
        _recording = true;
    }

    public async Task<byte[]> StopAsync()
    {
        if (!_recording) return [];

        await _mediaRecorder.StopAsync();
        await using var combinedBlob = await Blob.CreateAsync(_jsRT, [.. _blobRecord]);
        var audioBytes = await combinedBlob.ArrayBufferAsync();
        var audioBuffer = await _audCtx.DecodeAudioDataAsync(audioBytes);
        var wavBytes = await ConvertToWaveFrom(audioBuffer);
        _recording = false;
        return wavBytes;
    }

    public async ValueTask DisposeAsync()
    {
        if (_recording)
        {
            //録音を停止
            await _mediaRecorder.StopAsync();
        }
        _blobRecord.Clear();
        _blobRecord = null!;
        await _mediaStream.DisposeAsync();

        if (_mediaRecorder is not null)
            await _mediaRecorder.DisposeAsync();
        if (_audCtx is not null)
            await _audCtx.DisposeAsync();
    }

    private async Task<byte[]> ConvertToWaveFrom(AudioBuffer buffer)
    {
        const int BitsPerSample = 16;
        const int BytesPerSample = BitsPerSample / 8;
        const int WaveFormatPcm = 1;
        const int FmtChunkDataSize = 16;
        const int HeaderSize = 36; // RIFF + ファイルサイズを除く

        int rate = (int)await buffer.GetSampleRateAsync();
        int numOfChannels = (int)await buffer.GetNumberOfChannelsAsync();
        int length = (int)await buffer.GetLengthAsync();

        int byteRate = rate * numOfChannels * BytesPerSample;
        int blockAlign = numOfChannels * BytesPerSample;
        int dataChunkSize = length * numOfChannels * BytesPerSample;
        int fileSize = HeaderSize + dataChunkSize;

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(fileSize);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));

        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(FmtChunkDataSize);
        writer.Write((short)WaveFormatPcm);
        writer.Write((short)numOfChannels);
        writer.Write(rate);
        writer.Write(byteRate);
        writer.Write((short)blockAlign);
        writer.Write((short)BitsPerSample);

        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataChunkSize);

        for (int channel = 0; channel < numOfChannels; channel++)
        {
            Float32Array channelData = await buffer.GetChannelDataAsync((ulong)channel);

            var floats = await _jsRT.InvokeAsync<float[]>("getFloat32Array", channelData);

            for (int i = 0; i < length; i++)
            {
                short pcm = (short)(floats[i] * short.MaxValue);
                writer.Write(pcm);
            }
        }

        return stream.ToArray();
    }
}
