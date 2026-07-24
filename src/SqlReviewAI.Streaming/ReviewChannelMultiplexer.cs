using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Nerdbank.Streams;
using SqlReviewAI.Contracts;

namespace SqlReviewAI.Streaming;

/// <summary>
/// Well-known channel names for the four logical streams in the
/// architecture diagram: rule-engine findings, RAG hits, LLM tokens, and
/// logs/metrics.
/// </summary>
public static class ReviewChannels
{
    public const string Rules = "rules";
    public const string Rag = "rag";
    public const string Llm = "llm";
    public const string Logs = "logs";

    public static readonly string[] All = { Rules, Rag, Llm, Logs };
}

/// <summary>
/// Splits ONE duplex connection into the four review-pipeline channels
/// using <see cref="MultiplexingStream"/>, instead of opening four separate
/// sockets/pipes. Typical uses:
///   - Silo-side: a background worker offers the four channels over a
///     connection to the Web process and writes ReviewProgressEvents as
///     the pipeline executes.
///   - Web-side: accepts the four channels and relays each one to the
///     matching part of the UI (e.g. forwarding over SignalR).
///   - Fully in-process demo: connect the two sides with
///     <c>Nerdbank.Streams.FullDuplexStream.CreatePair()</c> (an in-memory
///     loopback pair) instead of a real socket — useful for testing the
///     channel wiring without any network at all.
///
/// Wire format per channel: a 4-byte little-endian length prefix followed
/// by that many bytes of UTF-8 JSON (one <see cref="ReviewProgressEvent"/>
/// per frame). Deliberately simple/text-based rather than a binary
/// protocol, so channel traffic is easy to inspect while developing.
/// </summary>
public sealed class ReviewChannelMultiplexer : IAsyncDisposable
{
    private readonly MultiplexingStream _mux;
    private readonly Dictionary<string, MultiplexingStream.Channel> _channels;

    private ReviewChannelMultiplexer(MultiplexingStream mux, Dictionary<string, MultiplexingStream.Channel> channels)
    {
        _mux = mux;
        _channels = channels;
    }

    /// <summary>Producing side: offers all four channels. Must be paired
    /// with a call to <see cref="OpenAsAcceptingSideAsync"/> on the other
    /// end of the same duplex stream.</summary>
    public static async Task<ReviewChannelMultiplexer> OpenAsOfferingSideAsync(Stream duplexStream, CancellationToken ct = default)
    {
        var mux = await MultiplexingStream.CreateAsync(duplexStream, cancellationToken: ct);
        var channels = new Dictionary<string, MultiplexingStream.Channel>();
        foreach (var name in ReviewChannels.All)
        {
            channels[name] = await mux.OfferChannelAsync(name, ct);
        }
        return new ReviewChannelMultiplexer(mux, channels);
    }

    /// <summary>Consuming side: accepts the four channels offered by the other side.</summary>
    public static async Task<ReviewChannelMultiplexer> OpenAsAcceptingSideAsync(Stream duplexStream, CancellationToken ct = default)
    {
        var mux = await MultiplexingStream.CreateAsync(duplexStream, cancellationToken: ct);
        var channels = new Dictionary<string, MultiplexingStream.Channel>();
        foreach (var name in ReviewChannels.All)
        {
            channels[name] = await mux.AcceptChannelAsync(name, ct);
        }
        return new ReviewChannelMultiplexer(mux, channels);
    }

    /// <summary>Writes one event as a length-prefixed JSON frame onto the channel matching its <c>Channel</c> field.</summary>
    public async Task WriteAsync(ReviewProgressEvent evt, CancellationToken ct = default)
    {
        var channel = _channels[ChannelNameFor(evt.Channel)];
        var json = JsonSerializer.SerializeToUtf8Bytes(evt);

        var lengthPrefix = new byte[4];
        BitConverter.TryWriteBytes(lengthPrefix, json.Length);

        await channel.Output.WriteAsync(lengthPrefix, ct);
        await channel.Output.WriteAsync(json, ct);
        await channel.Output.FlushAsync(ct);
    }

    /// <summary>Reads all events from one named channel until it completes.
    /// Run one of these per channel (e.g. via Task.WhenAll / four parallel
    /// foreach loops) to reassemble all four streams concurrently on the
    /// consuming side.</summary>
    public async IAsyncEnumerable<ReviewProgressEvent> ReadChannelAsync(
        string channelName, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var reader = _channels[channelName].Input;

        while (true)
        {
            var lengthBuffer = new byte[4];
            if (!await ReadExactAsync(reader, lengthBuffer, ct)) yield break;

            var length = BitConverter.ToInt32(lengthBuffer);
            var payload = new byte[length];
            if (!await ReadExactAsync(reader, payload, ct)) yield break;

            var evt = JsonSerializer.Deserialize<ReviewProgressEvent>(payload);
            if (evt is not null) yield return evt;
        }
    }

    /// <summary>Fills <paramref name="buffer"/> completely from the pipe, or
    /// returns false if the channel completed early (fewer bytes than requested).</summary>
    private static async Task<bool> ReadExactAsync(PipeReader reader, byte[] buffer, CancellationToken ct)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            ReadResult result = await reader.ReadAsync(ct);
            ReadOnlySequence<byte> seq = result.Buffer;

            if (seq.Length == 0 && result.IsCompleted)
            {
                reader.AdvanceTo(seq.End);
                return false;
            }

            var available = (int)Math.Min(seq.Length, buffer.Length - totalRead);
            seq.Slice(0, available).CopyTo(buffer.AsSpan(totalRead, available));
            totalRead += available;

            var consumed = seq.GetPosition(available);
            reader.AdvanceTo(consumed, seq.End);

            if (totalRead >= buffer.Length) break;
            if (result.IsCompleted) return false;
        }
        return true;
    }

    private static string ChannelNameFor(ReviewChannel channel) => channel switch
    {
        ReviewChannel.Rules => ReviewChannels.Rules,
        ReviewChannel.Rag => ReviewChannels.Rag,
        ReviewChannel.Llm => ReviewChannels.Llm,
        ReviewChannel.Logs => ReviewChannels.Logs,
        _ => throw new ArgumentOutOfRangeException(nameof(channel)),
    };

    public async ValueTask DisposeAsync()
    {
        foreach (var channel in _channels.Values)
        {
            channel.Dispose();
        }
        await _mux.DisposeAsync();
    }
}
