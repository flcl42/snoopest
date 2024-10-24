using System.Net;
using System.Net.WebSockets;
using System.Text;


if (args.Length == 0)
{
    PrintHelp();
    return;
}

int index = 9;
foreach (string? arg in args)
{
    string[] parts = arg.Split(',');

    if (parts.Length < 2 || parts.Length > 4)
    {
        throw new ArgumentException("Each argument set must have 2 to 4 parameters.");
    }

    int listenOnPort = int.Parse(parts[0]);
    string redirectToHostAndPort = parts[1];
    string? jwtTokenOrPath = parts.Length > 2 ? parts[2] : null;
    string? pathToLogs = parts.Length > 3 ? parts[3] : null;

    ArgumentSet parsed = new(listenOnPort, redirectToHostAndPort, jwtTokenOrPath, pathToLogs, index++);

    _ = StartListener(parsed);
}

await Task.Delay(-1);

static async Task StartListener(ArgumentSet args)
{
    HttpListener listener = new();
    try
    {
        listener.Prefixes.Add($"http://*:{args.ListenOnPort}/");
        listener.Start();
        Console.WriteLine($"\x1b[38;5;{args.Index}mListening on port {args.ListenOnPort} and redirecting to {args.RedirectToHostAndPort}");
    }
    catch
    {
        Console.WriteLine($"Unable to listen on {args.ListenOnPort}");
        return;
    }

    while (true)
    {
        try
        {
            HttpListenerContext context = await listener.GetContextAsync();
            if (context.Request.IsWebSocketRequest)
            {
                _ = HandleWebSocketRequest(context, args);
            }
            else
            {
                _ = HandleHttpRequest(context, args);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error handling request: {ex.Message}");
        }
    }
}

static async Task HandleHttpRequest(HttpListenerContext context, ArgumentSet args)
{
    string redirectUri = $"http://{args.RedirectToHostAndPort}{context.Request.RawUrl}";
    HttpListenerRequest request = context.Request;
    HttpListenerResponse response = context.Response;

    using HttpClient client = new();
    HttpRequestMessage proxyRequest = new(new HttpMethod(request.HttpMethod), redirectUri);

    // Copy request headers
    foreach (string? headerKey in request.Headers.AllKeys.Where(key => key is not null && key != "Accept-Encoding"))
    {
        proxyRequest.Headers.TryAddWithoutValidation(headerKey!, request.Headers[headerKey]);
    }

    // Copy request body if present
    if (request.HasEntityBody)
    {
        using StreamReader requestStream = new(request.InputStream);
        string requestBody = await requestStream.ReadToEndAsync();
        proxyRequest.Content = new StringContent(requestBody, Encoding.UTF8);

        Console.WriteLine($"\u001b[38;5;{args.Index}m{Time()} >> {request.HttpMethod} {request.Url}\n{requestBody}");
    }
    else
    {
        Console.WriteLine($"\u001b[38;5;{args.Index}m{Time()} >> {request.HttpMethod} {request.Url}");
    }

    // Send request to the target host
    using HttpResponseMessage proxyResponse = await client.SendAsync(proxyRequest);

    // Copy response headers back to the original response
    foreach (KeyValuePair<string, IEnumerable<string>> header in proxyResponse.Headers)
    {
        response.Headers[header.Key] = string.Join(",", header.Value);
    }

    // Copy response content
    byte[] responseBytes = await proxyResponse.Content.ReadAsByteArrayAsync();
    await response.OutputStream.WriteAsync(responseBytes, 0, responseBytes.Length);
    response.OutputStream.Close();

    Console.WriteLine($"\u001b[38;5;{args.Index}m{Time()} << {Encoding.UTF8.GetString(responseBytes)}");
}

static string Time() => DateTime.Now.ToString("HH:mm:ss.fff");

static async Task HandleWebSocketRequest(HttpListenerContext context, ArgumentSet args)
{
    HttpListenerWebSocketContext wsContext = await context.AcceptWebSocketAsync(null);
    WebSocket webSocket = wsContext.WebSocket;

    Uri redirectUri = new($"ws://{args.RedirectToHostAndPort}{context.Request.RawUrl}");
    using ClientWebSocket clientWebSocket = new();
    await clientWebSocket.ConnectAsync(redirectUri, CancellationToken.None);

    byte[] buffer = new byte[8192];

    async Task RelayMessages(WebSocket from, WebSocket to, string direction)
    {
        while (from.State == WebSocketState.Open)
        {
            WebSocketReceiveResult result = await from.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await to.CloseAsync(WebSocketCloseStatus.NormalClosure, "Relay closed", CancellationToken.None);
                break;
            }
            await to.SendAsync(new ArraySegment<byte>(buffer, 0, result.Count), result.MessageType, result.EndOfMessage, CancellationToken.None);
            Console.WriteLine($"\u001b[38;5;{args.Index}m{Time()} {direction} {Encoding.UTF8.GetString(buffer.AsSpan(0, result.Count)).TrimEnd('\n')}");
        }
    }

    // Relay messages between client and server
    await Task.WhenAny(RelayMessages(webSocket, clientWebSocket, ">>"), RelayMessages(clientWebSocket, webSocket, "<<"));
}

static void PrintHelp()
{
    Console.WriteLine("The tool that allows to intercept and log http and web sockets requests");
    Console.WriteLine("Usage: ./snoopest args-set-1 args-set-2 ...");
    Console.WriteLine("Each arguments set must be comma-separated:");
    Console.WriteLine("  listen-on-port,redirect-to-host-and-port[,optional-jwt-token-or-path-to-it][,optional-path-to-logs]");
    Console.WriteLine("Examples:");
    Console.WriteLine("  ./snoopest 8080,example.com:8081,mytoken,/path/to/logs");
    Console.WriteLine("  ./snoopest 8080,example.com:8081");
    Console.WriteLine("  ./snoopest 8080,example.com:8081,mytoken");
}

record ArgumentSet(int ListenOnPort, string RedirectToHostAndPort, string? JwtTokenOrPath = null, string? PathToLogs = null, int Index = 0);
