using Dihor.GameKit.Networking.Transport.Lan;

namespace Dihor.GameKit.Networking.Packaging.AndroidClient;

public static class AndroidLanClientPackageProbe
{
    public static Task<LanWebSocketClient> ConnectAsync(
        Uri endpoint,
        string handshakeJson,
        CancellationToken cancellationToken = default) =>
        LanWebSocketClient.ConnectAsync(
            endpoint,
            handshakeJson,
            cancellationToken: cancellationToken);
}
