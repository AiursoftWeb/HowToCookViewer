using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;
using Microsoft.Extensions.Options;

namespace Aiursoft.HowToCookViewer.Tests.IntegrationTests;

internal sealed class TestPortReservation : IDisposable
{
    private Socket? _socket;
    public int Port { get; }

    public TestPortReservation()
    {
        const int maxAttempts = 4;
        for (var attempt = 1; ; attempt++)
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                socket.ExclusiveAddressUse = true;
                socket.Bind(new IPEndPoint(IPAddress.Any, 0));
                socket.Listen(512);
                _socket = socket;
                Port = ((IPEndPoint)socket.LocalEndPoint!).Port;
                return;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse && attempt < maxAttempts)
            {
                socket.Dispose();
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    }

    public void ConfigureServer(IHost server)
    {
        // Configure before StartAsync. Kestrel takes ownership of the same socket;
        // releasing it and rebinding the port would reopen the allocation race.
        server.Services.GetRequiredService<IOptions<SocketTransportOptions>>().Value.CreateBoundListenSocket = endpoint =>
        {
            if (endpoint is not IPEndPoint ip || ip.Port != Port || !ip.Address.Equals(IPAddress.Any))
            {
                throw new InvalidOperationException("The reserved socket must match Kestrel's IPv4 wildcard endpoint.");
            }
            return Interlocked.Exchange(ref _socket, null)
                ?? throw new InvalidOperationException("The reserved socket was already transferred or disposed.");
        };
    }

    public void Dispose() => Interlocked.Exchange(ref _socket, null)?.Dispose();
}
