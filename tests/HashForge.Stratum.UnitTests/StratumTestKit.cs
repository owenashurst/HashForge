using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using HashForge.Domain;
using HashForge.Stratum.Protocol;
using HashForge.Stratum.Sessions;
using HashForge.Stratum.Transport;
using Microsoft.Extensions.Options;

namespace HashForge.Stratum.UnitTests;

/// <summary>An in-memory miner connection: tests queue the lines the "miner" sends and inspect what it receives.</summary>
internal sealed class FakeConnection : IStratumConnection
{
    private readonly Channel<string?> _incoming = Channel.CreateUnbounded<string?>();

    public List<string> Sent { get; } = [];

    /// <summary>The lines that arrived already encoded, as the exact memory they were sent from.</summary>
    public List<ReadOnlyMemory<byte>> EncodedLines { get; } = [];

    public bool Disposed { get; private set; }

    public IPAddress RemoteAddress { get; init; } = IPAddress.Parse("192.168.1.50");

    public void MinerSends(params string[] lines)
    {
        foreach (var line in lines)
        {
            _incoming.Writer.TryWrite(line);
        }
    }

    public void MinerDisconnects() => _incoming.Writer.TryWrite(null);

    public async Task<string?> ReadLineAsync(CancellationToken cancellationToken) =>
        await _incoming.Reader.ReadAsync(cancellationToken);

    public Task WriteLineAsync(string line, CancellationToken cancellationToken)
    {
        lock (Sent)
        {
            Sent.Add(line);
        }

        return Task.CompletedTask;
    }

    public Task WriteAsync(ReadOnlyMemory<byte> encodedLine, CancellationToken cancellationToken)
    {
        EncodedLines.Add(encodedLine);
        return WriteLineAsync(Encoding.UTF8.GetString(encodedLine.Span).TrimEnd('\n'), cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

internal static class StratumTestKit
{
    public const string MinerAddress = "bc1qmineraddress000";

    public static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    public static readonly byte[] Extranonce1 = [0x01, 0x02, 0x03, 0x04];

    public static IOptions<StratumOptions> Options(Action<StratumOptions>? configure = null)
    {
        var options = new StratumOptions();
        configure?.Invoke(options);
        return Microsoft.Extensions.Options.Options.Create(options);
    }

    public static PoolDefinition Pool(string name = "solo", PayoutScheme scheme = PayoutScheme.Solo, int port = 3333) =>
        new() { Name = name, Scheme = scheme, Port = port, FeeAddress = "fee" };

    public static StratumSession Session(FakeConnection? connection = null, PoolDefinition? pool = null, byte[]? extranonce1 = null) =>
        new(
            pool ?? Pool(),
            new MinerChannel(connection ?? new FakeConnection(), TimeSpan.FromSeconds(30)),
            extranonce1 ?? Extranonce1,
            new VarDiffController(Options().Value, Now),
            Now,
            TimeProvider.System);

    /// <summary>A session that has subscribed and authorised "bc1qmineraddress000.rig1".</summary>
    public static StratumSession AuthorizedSession(FakeConnection? connection = null, PoolDefinition? pool = null)
    {
        var session = Session(connection, pool);
        session.IsSubscribed = true;
        session.Workers.Authorize($"{MinerAddress}.rig1", new WorkerIdentity(MinerAddress, "rig1"));
        return session;
    }

    public static StratumRequest Request(string method, params object?[] parameters)
    {
        var line = JsonSerializer.Serialize(new { id = 1, method, @params = parameters });
        return StratumRequest.TryParse(line, out var request) ? request : throw new InvalidOperationException("Bad test request.");
    }

    public static JsonElement Json(string text) => JsonElement.Parse(text);
}
