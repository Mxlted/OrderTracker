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
    private static readonly DateTimeOffset NtpEpoch = new(1900, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public async Task<DateTimeOffset?> TryGetUtcTimeAsync(string host = "time.nist.gov", int timeoutMilliseconds = 2500)
    {
        try
        {
            using var cancellation = new CancellationTokenSource(timeoutMilliseconds);
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            var request = new byte[NtpPacketLength];
            var response = new byte[NtpPacketLength];
            request[0] = ClientRequestHeader;

            var endpoint = await ResolveEndpointAsync(host, cancellation.Token);
            await socket.ConnectAsync(endpoint, cancellation.Token);
            await socket.SendAsync(request, SocketFlags.None, cancellation.Token);
            var received = await socket.ReceiveAsync(response, SocketFlags.None, cancellation.Token);

            return received >= NtpPacketLength
                ? ReadTimestamp(response, 40)
                : null;
        }
        catch
        {
            return null;
        }
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

    private static DateTimeOffset ReadTimestamp(byte[] buffer, int start)
    {
        var seconds = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(start, 4));
        var fraction = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(start + 4, 4));
        var milliseconds = seconds * 1000d + fraction * 1000d / 4294967296d;
        return NtpEpoch.AddMilliseconds(milliseconds);
    }
}
