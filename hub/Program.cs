using System.Net.WebSockets;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.Use(async (context, next) =>
{
  context.Response.Headers["Access-Control-Allow-Origin"] = "*";
  context.Response.Headers["Access-Control-Allow-Headers"] = "*";
  context.Response.Headers["Access-Control-Allow-Methods"] = "GET,POST,OPTIONS";
  context.Response.Headers["Access-Control-Allow-Private-Network"] = "true";

  if (context.Request.Method == "OPTIONS")
  {
    context.Response.StatusCode = StatusCodes.Status204NoContent;
    return;
  }

  await next();
});

app.UseWebSockets();

app.MapGet("/", () =>
{
  return Results.Text("Bridge server is running.");
});

app.MapGet("/api/ping", () =>
{
  return Results.Json(new
  {
    ok = true,
    message = "HTTP ping success",
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
      await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
      break;
    }

    var receivedText = Encoding.UTF8.GetString(buffer, 0, result.Count);
    var responseText = $"WebSocket pong from server: {receivedText}";
    var responseBytes = Encoding.UTF8.GetBytes(responseText);

    await webSocket.SendAsync(
      new ArraySegment<byte>(responseBytes),
      WebSocketMessageType.Text,
      true,
      CancellationToken.None
    );
  }
});

var port = GetPort(args, 5005);
app.Urls.Add($"http://0.0.0.0:{port}");

Console.WriteLine($"Bridge server listening on http://0.0.0.0:{port}");
Console.WriteLine("HTTP endpoint: /api/ping");
Console.WriteLine("WebSocket endpoint: /ws");

app.Run();

static int GetPort(string[] args, int defaultPort)
{
  foreach (var arg in args)
  {
    if (arg.StartsWith("--port=") && int.TryParse(arg["--port=".Length..], out var port))
    {
      return port;
    }
  }

  return defaultPort;
}