using System.Net;
using Microsoft.Extensions.FileProviders;
using PartyGameKit.Protocol;
using PartyGameKit.Sample.SharedCounter;

var advertisedHost = ReadOption(args, "--host") ?? "127.0.0.1";
var websocketPort = ReadPort(args, "--ws-port", 5042);
var httpPort = ReadPort(args, "--http-port", 8080);
var repositoryRoot = Directory.GetCurrentDirectory();
var webRoot = Path.Combine(repositoryRoot, "samples", "SharedCounter", "web");
var sdkRoot = Path.Combine(repositoryRoot, "clients", "typescript", "dist");

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

await using var counterHost = await SharedCounterHost.StartAsync(
    advertisedHost,
    IPAddress.Any,
    websocketPort);

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://0.0.0.0:{httpPort}");
var app = builder.Build();
using var webFiles = new PhysicalFileProvider(webRoot);
using var sdkFiles = new PhysicalFileProvider(sdkRoot);

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
}));

Console.WriteLine("PartyGameKit Shared Counter sample");
Console.WriteLine($"Shared screen: http://{advertisedHost}:{httpPort}/?role=shared-screen");
Console.WriteLine($"Player:        http://{advertisedHost}:{httpPort}/?role=player");
Console.WriteLine($"Join payload:  {JoinDescriptorCodec.SerializeText(counterHost.JoinDescriptor)}");
Console.WriteLine("Use two different phones/browser profiles for two players because player identity is persisted per browser profile.");

await app.RunAsync();

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
