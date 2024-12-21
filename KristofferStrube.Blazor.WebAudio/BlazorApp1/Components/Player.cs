using KristofferStrube.Blazor.DOM;
using KristofferStrube.Blazor.WebAudio;
using Microsoft.JSInterop;
using System.Collections.Concurrent;

namespace BlazorApp1.Components;

public class BasicListPlayer
{
    private string? _playingIndex = null;
    private readonly ConcurrentDictionary<string, byte[]> _dict = new();
    private readonly BasicPlayer _player;

    public event Action<string>? Ended;

    public BasicListPlayer(IJSRuntime jsRuntime)
    //: base(jsRuntime)
    {
        _player = new BasicPlayer(jsRuntime);
        _player.Ended += OnInternalAudioEnded;
    }

    private void OnInternalAudioEnded()
    {
        string endedInex = _playingIndex!;
        _playingIndex = null;
        Ended?.Invoke(endedInex);
    }

    public async Task EnsureInitializedAsync()
    {
        await _player.EnsureInitializedAsync();
    }

    public IEnumerable<string> Keys => _dict.Keys;

    public void UpdateOrAdd(string key, byte[] value)
    {
        _dict[key] = value; // キーが存在すれば更新、存在しなければ追加
    }

    public async Task StartAsync(string index)
    {
        await _player.StopAsync();
        //_player.StopAsync().GetAwaiter().GetResult();

        //_playingIndex = null;
        await _player.SetSoundAsync(_dict[index]);
        await _player.StartAsync();
        _playingIndex = index;
    }

    public async Task StopAsync()
    {
        if (_playingIndex == null)
            return;

        await _player.StopAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _player.DisposeAsync();
    }
}

public class BasicPlayer
{
    private AudioContext _audCtx = default!;
    private AudioDestinationNode _audDstNode = default!;
    private AudioBuffer _audBuff = default!;
    private AudioBufferSourceNode _audBuffSrcNode = default!;

    private readonly IJSRuntime _jsRT;
    private bool _playing = false;

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
        if (_audCtx is not null)
            return;

        _audCtx = await AudioContext.CreateAsync(_jsRT);
        _audDstNode = await _audCtx.GetDestinationAsync();
    }

    public async Task SetSoundAsync(byte[] audioBytes)
    {
        // 前のバッファはDispose
        if (_audBuff is not null)
            await _audBuff.DisposeAsync();

        _audBuff = await _audCtx.DecodeAudioDataAsync(audioBytes);
    }

    public async Task StartAsync()
    {
        await EnsureInitializedAsync();

        if (_playing) return;

        if (_audBuff is null) return;

        //再生時には以下の３コールが必要
        _audBuffSrcNode = await _audCtx.CreateBufferSourceAsync();
        await _audBuffSrcNode.SetBufferAsync(_audBuff);
        await _audBuffSrcNode.ConnectAsync(_audDstNode);

        await using EventListener<Event> endedListener = await EventListener<Event>.CreateAsync(_jsRT, e =>
        {
            _playing = false;
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
        if (_audCtx is not null)
            await _audCtx.DisposeAsync();
    }

}
