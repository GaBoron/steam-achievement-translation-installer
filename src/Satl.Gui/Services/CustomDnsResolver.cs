using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Satl_Gui.Services;

public sealed class CustomDnsResolver
{
    private const ushort AddressRecord = 1;
    private const ushort Ipv6AddressRecord = 28;
    private readonly IReadOnlyList<DnsServerEndpoint> _servers;
    private readonly TimeSpan _timeout;

    public CustomDnsResolver(IReadOnlyList<DnsServerEndpoint> servers, TimeSpan timeout)
    {
        _servers = servers;
        _timeout = timeout;
    }

    public async Task<IReadOnlyList<IPAddress>> ResolveAsync(
        string host,
        CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var literal))
        {
            return [literal];
        }

        foreach (var server in _servers)
        {
            var addresses = new List<IPAddress>();
            foreach (var recordType in new[] { AddressRecord, Ipv6AddressRecord })
            {
                try
                {
                    addresses.AddRange(await QueryAsync(host, recordType, server, cancellationToken));
                }
                catch (Exception exception) when (
                    exception is SocketException or TimeoutException or InvalidDataException)
                {
                    // Try the other address family and then the next configured resolver.
                }
            }
            if (addresses.Count > 0)
            {
                return addresses.Distinct().ToArray();
            }
        }
        throw new SocketException((int)SocketError.HostNotFound);
    }

    private async Task<IReadOnlyList<IPAddress>> QueryAsync(
        string host,
        ushort recordType,
        DnsServerEndpoint server,
        CancellationToken cancellationToken)
    {
        var requestId = (ushort)Random.Shared.Next(ushort.MaxValue + 1);
        var query = BuildQuery(host, recordType, requestId);
        using var socket = new Socket(server.Address.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        var endpoint = new IPEndPoint(server.Address, server.Port);
        try
        {
            await socket.SendToAsync(query, SocketFlags.None, endpoint, timeout.Token);
            var buffer = new byte[4096];
            EndPoint source = server.Address.AddressFamily == AddressFamily.InterNetwork
                ? new IPEndPoint(IPAddress.Any, 0)
                : new IPEndPoint(IPAddress.IPv6Any, 0);
            var received = await socket.ReceiveFromAsync(buffer, SocketFlags.None, source, timeout.Token);
            return ParseResponse(buffer.AsSpan(0, received.ReceivedBytes), requestId);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"DNS 服务器 {server.Address} 响应超时。");
        }
    }

    private static byte[] BuildQuery(string host, ushort recordType, ushort requestId)
    {
        var asciiHost = new IdnMapping().GetAscii(host.TrimEnd('.'));
        using var stream = new MemoryStream();
        WriteUInt16(stream, requestId);
        WriteUInt16(stream, 0x0100);
        WriteUInt16(stream, 1);
        WriteUInt16(stream, 0);
        WriteUInt16(stream, 0);
        WriteUInt16(stream, 0);
        foreach (var label in asciiHost.Split('.'))
        {
            var encoded = Encoding.ASCII.GetBytes(label);
            if (encoded.Length is 0 or > 63)
            {
                throw new InvalidDataException("DNS 主机名包含无效标签。");
            }
            stream.WriteByte((byte)encoded.Length);
            stream.Write(encoded);
        }
        stream.WriteByte(0);
        WriteUInt16(stream, recordType);
        WriteUInt16(stream, 1);
        return stream.ToArray();
    }

    private static IReadOnlyList<IPAddress> ParseResponse(ReadOnlySpan<byte> payload, ushort requestId)
    {
        if (payload.Length < 12 || ReadUInt16(payload, 0) != requestId)
        {
            throw new InvalidDataException("DNS 服务器返回了无效响应。");
        }
        var flags = ReadUInt16(payload, 2);
        if ((flags & 0x000F) != 0)
        {
            throw new SocketException((int)SocketError.HostNotFound);
        }

        var questionCount = ReadUInt16(payload, 4);
        var answerCount = ReadUInt16(payload, 6);
        var offset = 12;
        for (var index = 0; index < questionCount; index++)
        {
            SkipName(payload, ref offset);
            RequireAvailable(payload, offset, 4);
            offset += 4;
        }

        var addresses = new List<IPAddress>();
        for (var index = 0; index < answerCount; index++)
        {
            SkipName(payload, ref offset);
            RequireAvailable(payload, offset, 10);
            var type = ReadUInt16(payload, offset);
            var recordClass = ReadUInt16(payload, offset + 2);
            var length = ReadUInt16(payload, offset + 8);
            offset += 10;
            RequireAvailable(payload, offset, length);
            if (recordClass == 1 && type == AddressRecord && length == 4)
            {
                addresses.Add(new IPAddress(payload.Slice(offset, length)));
            }
            else if (recordClass == 1 && type == Ipv6AddressRecord && length == 16)
            {
                addresses.Add(new IPAddress(payload.Slice(offset, length)));
            }
            offset += length;
        }
        return addresses;
    }

    private static void SkipName(ReadOnlySpan<byte> payload, ref int offset)
    {
        while (true)
        {
            RequireAvailable(payload, offset, 1);
            var length = payload[offset++];
            if (length == 0)
            {
                return;
            }
            if ((length & 0xC0) == 0xC0)
            {
                RequireAvailable(payload, offset, 1);
                offset++;
                return;
            }
            if ((length & 0xC0) != 0)
            {
                throw new InvalidDataException("DNS 名称编码无效。");
            }
            RequireAvailable(payload, offset, length);
            offset += length;
        }
    }

    private static void RequireAvailable(ReadOnlySpan<byte> payload, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > payload.Length - length)
        {
            throw new InvalidDataException("DNS 响应不完整。");
        }
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> payload, int offset)
    {
        RequireAvailable(payload, offset, 2);
        return BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(offset, 2));
    }

    private static void WriteUInt16(Stream stream, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        stream.Write(bytes);
    }
}
