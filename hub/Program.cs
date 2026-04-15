using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using DotNetEnv;

Env.Load(Path.Combine(Directory.GetCurrentDirectory(), ".env"));

var builder = WebApplication.CreateBuilder(args);

var allowedOrigin = GetRequiredEnv("HUB_ALLOWED_ORIGIN");
var host = Environment.GetEnvironmentVariable("HUB_HOST") ?? "0.0.0.0";
var port = int.TryParse(Environment.GetEnvironmentVariable("HUB_PORT"), out var parsedPort)
  ? parsedPort
  : 5005;

var certPath = GetRequiredEnv("HUB_CERT_PATH");
var certPassword = GetRequiredEnv("HUB_CERT_PASSWORD");

if (!Path.IsPathRooted(certPath))
{
  certPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), certPath));
}

if (!File.Exists(certPath))
{
  throw new FileNotFoundException($"TLS certificate not found: {certPath}");
}

var certificate = new X509Certificate2(certPath, certPassword);

builder.Services.AddCors(options =>
{
  options.AddPolicy("ViewerCors", policy =>
  {
    policy
      .WithOrigins(allowedOrigin)
      .AllowAnyHeader()
      .AllowAnyMethod();
  });
});

builder.WebHost.ConfigureKestrel(options =>
{
  options.Listen(IPAddress.Any, port, listen =>
  {
    listen.UseHttps(certificate);
  });
});

var app = builder.Build();

app.UseCors("ViewerCors");
app.UseWebSockets();

app.MapGet("/", () =>
{
  return Results.Text("Dorado hub is running over HTTPS.");
});

app.MapGet("/api/ping", () =>
{
  return Results.Json(new
  {
    ok = true,
    message = "HTTPS ping success",
    serverTimeUtc = DateTime.UtcNow
  });
});

app.Map("/ws", async context =>
{
  if (!context.WebSockets.IsWebSocketRequest)
  {
    context.Response.StatusCode = StatusCodes.Status400BadRequest;
    await context.Response.WriteAsync("Expected a WebSocket request.");
    return;
  }

  using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
  var buffer = new byte[4096];

  while (true)
  {
    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

    if (result.MessageType == WebSocketMessageType.Close)
    {
      await webSocket.CloseAsync(
        WebSocketCloseStatus.NormalClosure,
        "Closing",
        CancellationToken.None
      );
      break;
    }

    var receivedText = Encoding.UTF8.GetString(buffer, 0, result.Count);
    var responseText = $"WSS pong from server: {receivedText}";
    var responseBytes = Encoding.UTF8.GetBytes(responseText);

    await webSocket.SendAsync(
      new ArraySegment<byte>(responseBytes),
      WebSocketMessageType.Text,
      true,
      CancellationToken.None
    );
  }
});

Console.WriteLine($"HTTPS endpoint: https://{host}:{port}/api/ping");
Console.WriteLine($"WSS endpoint: wss://{host}:{port}/ws");
Console.WriteLine($"Allowed origin: {allowedOrigin}");
Console.WriteLine($"Certificate path: {certPath}");

app.Run();

static string GetRequiredEnv(string key)
{
  var value = Environment.GetEnvironmentVariable(key);

  if (string.IsNullOrWhiteSpace(value))
  {
    throw new InvalidOperationException($"Environment variable '{key}' is required.");
  }

  return value;
}