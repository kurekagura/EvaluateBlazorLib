using KristofferStrube.Blazor.DOM;
using KristofferStrube.Blazor.WebAudio;
using Microsoft.JSInterop;

namespace BlazorApp1;

public class BasicPlayer
{
    private AudioContext _audCtxt = default!;
    private AudioDestinationNode _audDstNode = default!;
    private AudioBuffer _audBuff = default!;
    private AudioBufferSourceNode _audBuffSrcNode = default!;

    private readonly IJSRuntime _jsRT;
    private bool _playing = false;
    //private bool _trackLoaded = false;

    public event Action? Ended;

    public BasicPlayer(IJSRuntime jsRuntime)
    {
        _jsRT = jsRuntime;
    }

    public bool IsPlaying
    {
        get { return _playing; }
        //set { _playing = value; }
    }

    public async Task EnsureInitializedAsync()
    {
        if (_audCtxt is not null)
            return;

        _audCtxt = await AudioContext.CreateAsync(_jsRT);
        _audDstNode = await _audCtxt.GetDestinationAsync();
    }

    public async Task SetSoundAsync(byte[] audioBytes)
    {
        // 前のバッファはDispose
        if (_audBuff is not null)
            await _audBuff.DisposeAsync();

        _audBuff = await _audCtxt.DecodeAudioDataAsync(audioBytes);
    }

    public async Task StartAsync()
    {
        await EnsureInitializedAsync();

        if (_playing) return;

        if (_audBuff is null) return;

        //再生時には以下の３コールが必要
        _audBuffSrcNode = await _audCtxt.CreateBufferSourceAsync();
        await _audBuffSrcNode.SetBufferAsync(_audBuff);
        await _audBuffSrcNode.ConnectAsync(_audDstNode);

        await using EventListener<Event> endedListener = await EventListener<Event>.CreateAsync(_jsRT, e =>
        {
            Console.WriteLine(e);

            _playing = false;
            //IsPlaying = false;
            Ended?.Invoke();
        });
        await _audBuffSrcNode.AddOnEndedEventListenerAsync(endedListener);
        await _audBuffSrcNode.StartAsync(); //Start playing
        _playing = true;
    }

    public async Task StopAsync()
    {
        if (!_playing)
            return;

        //停止時には以下の2コールが必要
        await _audBuffSrcNode.StopAsync();
        await _audBuffSrcNode.DisconnectAsync();
        //await _audBuffSrcNode.DisposeAsync();

        _playing = false;
    }

    public async ValueTask DisposeAsync()
    {
        if (_playing)
        {
            //再生を停止しないと鳴り続ける
            await _audBuffSrcNode.StopAsync();
            await _audBuffSrcNode.DisconnectAsync();
        }

        if (_audBuffSrcNode is not null)
            await _audBuffSrcNode.DisposeAsync();
        if (_audDstNode is not null)
            await _audDstNode.DisposeAsync();
        //_audBuffは_audCtxtがHAS
        if (_audBuff is not null)
            await _audBuff.DisposeAsync();
        if (_audCtxt is not null)
            await _audCtxt.DisposeAsync();
    }

}
