using System;
using System.Buffers.Binary;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace OrderTracker.Desktop.Services;

public sealed class NetworkTimeService
{
    private const int NtpPort = 123;
    private const int NtpPacketLength = 48;
    private const byte ClientRequestHeader = 0x1B;
    private static readonly string[] NtpHosts = { "time.nist.gov", "pool.ntp.org", "time.windows.com" };
    private static readonly TimeSpan HostTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan MaximumClockDifference = TimeSpan.FromDays(365);
    private static readonly DateTimeOffset NtpEpoch = new(1900, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private TimeSpan? _lastOffset;

    public TimeSpan? LastOffset => _lastOffset;

    public async Task<DateTimeOffset?> TryGetUtcTimeAsync()
    {
        foreach (var host in NtpHosts)
        {
            var networkTime = await TryGetHostUtcTimeAsync(host);
            if (networkTime.HasValue)
            {
                var localUtcNow = DateTimeOffset.UtcNow;
                _lastOffset = networkTime.Value - localUtcNow;
                return networkTime;
            }
        }

        return _lastOffset.HasValue
            ? DateTimeOffset.UtcNow + _lastOffset.Value
            : null;
    }

    private static async Task<IPEndPoint> ResolveEndpointAsync(string host, CancellationToken cancellationToken)
    {
        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
        var address = addresses.FirstOrDefault(candidate => candidate.AddressFamily == AddressFamily.InterNetwork) ??
                      addresses.FirstOrDefault();

        if (address is null)
        {
            throw new SocketException((int)SocketError.HostNotFound);
        }

        return new IPEndPoint(address, NtpPort);
    }

    private static async Task<DateTimeOffset?> TryGetHostUtcTimeAsync(string host)
    {
        try
        {
            using var cancellation = new CancellationTokenSource(HostTimeout);
            var endpoint = await ResolveEndpointAsync(host, cancellation.Token);
            using var socket = new Socket(endpoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            var request = new byte[NtpPacketLength];
            var response = new byte[NtpPacketLength];
            request[0] = ClientRequestHeader;

            await socket.ConnectAsync(endpoint, cancellation.Token);
            await socket.SendAsync(request, SocketFlags.None, cancellation.Token);
            var received = await socket.ReceiveAsync(response, SocketFlags.None, cancellation.Token);
            if (received < NtpPacketLength ||
                (response[0] & 0x07) != 4 ||
                response[1] == 0)
            {
                return null;
            }

            var seconds = BinaryPrimitives.ReadUInt32BigEndian(response.AsSpan(40, 4));
            if (seconds == 0)
            {
                return null;
            }

            var timestamp = ReadTimestamp(response, 40);
            return (timestamp - DateTimeOffset.UtcNow).Duration() <= MaximumClockDifference
                ? timestamp
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static DateTimeOffset ReadTimestamp(byte[] buffer, int start)
    {
        var seconds = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(start, 4));
        var fraction = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(start + 4, 4));
        var milliseconds = seconds * 1000d + fraction * 1000d / 4294967296d;
        return NtpEpoch.AddMilliseconds(milliseconds);
    }
}
