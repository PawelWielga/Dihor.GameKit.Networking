using System.Net;
using Microsoft.Extensions.FileProviders;
using PartyGameKit.Core;
using PartyGameKit.Protocol;
using PartyGameKit.Sample.SharedCounter;
using PartyGameKit.Transport.SignalR;

var advertisedHost = ReadOption(args, "--host") ?? "127.0.0.1";
var transportMode = (ReadOption(args, "--transport") ?? "lan").ToLowerInvariant();
var websocketPort = ReadPort(args, "--ws-port", 5042);
var httpPort = ReadPort(args, "--http-port", 8080);
var repositoryRoot = Directory.GetCurrentDirectory();
var webRoot = Path.Combine(repositoryRoot, "samples", "SharedCounter", "web");
var sdkRoot = Path.Combine(repositoryRoot, "clients", "typescript", "dist");
var signalRBrowserRoot = Path.Combine(
    repositoryRoot,
    "clients",
    "typescript",
    "node_modules",
    "@microsoft",
    "signalr",
    "dist",
    "browser");

if (!File.Exists(Path.Combine(webRoot, "index.html")))
{
    throw new InvalidOperationException(
        "Run the sample from the PartyGameKit repository root so samples/SharedCounter/web can be found.");
}

if (!File.Exists(Path.Combine(sdkRoot, "index.js")))
{
    throw new InvalidOperationException(
        "TypeScript SDK output is missing. Run `npm install` and `npm run build` in clients/typescript first.");
}

if (transportMode is not ("lan" or "signalr"))
{
    throw new ArgumentException("--transport must be either 'lan' or 'signalr'.");
}

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://0.0.0.0:{httpPort}");
if (transportMode == "signalr")
{
    if (!File.Exists(Path.Combine(signalRBrowserRoot, "signalr.min.js")))
    {
        throw new InvalidOperationException(
            "SignalR browser runtime is missing. Run `npm install` in clients/typescript first.");
    }
    builder.Services.AddPartyGameKitSignalR();
}

var app = builder.Build();
using var webFiles = new PhysicalFileProvider(webRoot);
using var sdkFiles = new PhysicalFileProvider(sdkRoot);
PhysicalFileProvider? signalRBrowserFiles = null;
SharedCounterHost counterHost;

if (transportMode == "signalr")
{
    var registry = app.Services.GetRequiredService<SignalRRoomRegistry>();
    var transport = registry.RegisterRoom(SharedCounterHost.RoomId, SharedCounterHost.JoinCode);
    var endpoint = $"http://{advertisedHost}:{httpPort}/partygamekit";
    var descriptor = new JoinDescriptor(
        SharedCounterHost.RoomId,
        SharedCounterHost.JoinCode,
        "signalr",
        endpoint,
        ProtocolVersions.Current);
    counterHost = SharedCounterHost.StartWithTransport(transport, descriptor);
    app.MapPartyGameKitSignalR("/partygamekit");
    signalRBrowserFiles = new PhysicalFileProvider(signalRBrowserRoot);
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = signalRBrowserFiles,
        RequestPath = "/signalr-runtime",
    });
}
else
{
    counterHost = await SharedCounterHost.StartAsync(
        advertisedHost,
        IPAddress.Any,
        websocketPort);
}

await using (counterHost)
{
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = webFiles });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = webFiles });
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = sdkFiles,
        RequestPath = "/sdk",
    });
    app.MapGet("/config.json", () => Results.Json(new
    {
        joinDescriptor = JoinDescriptorCodec.SerializeText(counterHost.JoinDescriptor),
        roomId = counterHost.JoinDescriptor.RoomId.Value,
        joinCode = counterHost.JoinDescriptor.JoinCode.Value,
        transport = counterHost.JoinDescriptor.Transport,
    }));

    Console.WriteLine("PartyGameKit Shared Counter sample");
    Console.WriteLine($"Transport:     {transportMode}");
    Console.WriteLine($"Shared screen: http://{advertisedHost}:{httpPort}/?role=shared-screen");
    Console.WriteLine($"Player:        http://{advertisedHost}:{httpPort}/?role=player");
    Console.WriteLine($"Join payload:  {JoinDescriptorCodec.SerializeText(counterHost.JoinDescriptor)}");
    Console.WriteLine("Use two different phones/browser profiles for two players because player identity is persisted per browser profile.");

    try
    {
        await app.RunAsync();
    }
    finally
    {
        signalRBrowserFiles?.Dispose();
    }
}

static string? ReadOption(string[] arguments, string name)
{
    var index = Array.IndexOf(arguments, name);
    if (index < 0)
    {
        return null;
    }

    if (index + 1 >= arguments.Length || string.IsNullOrWhiteSpace(arguments[index + 1]))
    {
        throw new ArgumentException($"{name} requires a value.");
    }

    return arguments[index + 1].Trim();
}

static int ReadPort(string[] arguments, string name, int defaultValue)
{
    var value = ReadOption(arguments, name);
    if (value is null)
    {
        return defaultValue;
    }

    if (!int.TryParse(value, out var port) || port is < 1 or > 65535)
    {
        throw new ArgumentOutOfRangeException(name, value, "Port must be between 1 and 65535.");
    }

    return port;
}
