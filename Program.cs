using System.Net;
using System.Net.WebSockets;
using System.Text;


if (args.Length == 0)
{
    PrintHelp();
    return;
}

try
{
    List<ArgumentSet> parsedArguments = [];

    foreach (string? arg in args)
    {
        string[] parts = arg.Split(',');

        if (parts.Length < 2 || parts.Length > 4)
        {
            throw new ArgumentException("Each argument set must have 2 to 4 parameters.");
        }

        string listenOnPort = parts[0];
        string redirectToHostAndPort = parts[1];
        string? jwtTokenOrPath = parts.Length > 2 ? parts[2] : null;
        string? pathToLogs = parts.Length > 3 ? parts[3] : null;

        ArgumentSet parsed = new()
        {
            ListenOnPort = listenOnPort,
            RedirectToHostAndPort = redirectToHostAndPort,
            JwtTokenOrPath = jwtTokenOrPath,
            PathToLogs = pathToLogs
        };

        parsedArguments.Add(parsed);

        _ = StartListener(parsed);
    }
    
    foreach (ArgumentSet argumentSet in parsedArguments)
    {
        Console.WriteLine("Parsed Argument Set:");
        Console.WriteLine($"  Listen On Port: {argumentSet.ListenOnPort}");
        Console.WriteLine($"  Redirect To Host And Port: {argumentSet.RedirectToHostAndPort}");
        if (!string.IsNullOrEmpty(argumentSet.JwtTokenOrPath))
        {
            Console.WriteLine($"  JWT Token or Path: {argumentSet.JwtTokenOrPath}");
        }
        if (!string.IsNullOrEmpty(argumentSet.PathToLogs))
        {
            Console.WriteLine($"  Path To Logs: {argumentSet.PathToLogs}");
        }
        Console.WriteLine();
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    PrintHelp();
}

await Task.Delay(-1);

static async Task StartListener(ArgumentSet args)
{
    int port = int.Parse(args.ListenOnPort);
    HttpListener listener = new();
    listener.Prefixes.Add($"http://*:{port}/");
    listener.Start();
    Console.WriteLine($"Listening on port {port} and redirecting to {args.RedirectToHostAndPort}");

   
    while (true)
    {
        try
        {
            HttpListenerContext context = await listener.GetContextAsync();
            if (context.Request.IsWebSocketRequest)
            {
                await HandleWebSocketRequest(context, args.RedirectToHostAndPort);
            }
            else
            {
                await HandleHttpRequest(context, args.RedirectToHostAndPort);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error handling request: {ex.Message}");
        }
    }
}

static async Task HandleHttpRequest(HttpListenerContext context, string redirectHostAndPort)
{
    string redirectUri = $"http://{redirectHostAndPort}{context.Request.RawUrl}";
    HttpListenerRequest request = context.Request;
    HttpListenerResponse response = context.Response;

    using HttpClient client = new();
    HttpRequestMessage proxyRequest = new(new HttpMethod(request.HttpMethod), redirectUri);

    // Copy request headers
    foreach (string? headerKey in request.Headers.AllKeys)
    {
        proxyRequest.Headers.TryAddWithoutValidation(headerKey, request.Headers[headerKey]);
    }

    // Copy request body if present
    if (request.HasEntityBody)
    {
        using StreamReader requestStream = new(request.InputStream);
        string requestBody = await requestStream.ReadToEndAsync();
        proxyRequest.Content = new StringContent(requestBody, Encoding.UTF8);

        if (response.ContentType == "application/json")
        {
            Console.WriteLine(">> {0}", requestBody);
        }
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

    if(response.ContentType == "application/json")
    {
        Console.WriteLine("<< {0}", Encoding.UTF8.GetString(responseBytes));
    }
    
}

static async Task HandleWebSocketRequest(HttpListenerContext context, string redirectHostAndPort)
{
    HttpListenerWebSocketContext wsContext = await context.AcceptWebSocketAsync(null);
    WebSocket webSocket = wsContext.WebSocket;

    Uri redirectUri = new($"ws://{redirectHostAndPort}{context.Request.RawUrl}");
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
            Console.WriteLine("{0} {1}", direction, Encoding.UTF8.GetString(buffer.AsSpan(0, result.Count)).TrimEnd('\n'));
        }
    }

    // Relay messages between client and server
    await Task.WhenAny(RelayMessages(webSocket, clientWebSocket, ">>"), RelayMessages(clientWebSocket, webSocket, "<<"));
}



static void PrintHelp()
{
    Console.WriteLine("Usage: MyApp <argument set 1> <argument set 2> ...");
    Console.WriteLine("Each argument set must be comma-separated:");
    Console.WriteLine("  listen-on-port,redirect-to-host-and-port[,optional-jwt-token-or-path-to-it][,optional-path-to-logs]");
    Console.WriteLine("Example:");
    Console.WriteLine("  MyApp 8080,example.com:8081,mytoken,/path/to/logs");
    Console.WriteLine("  MyApp 8080,example.com:8081");
    Console.WriteLine("  MyApp 8080,example.com:8081,mytoken");
}


class ArgumentSet
{
    public string ListenOnPort { get; set; }
    public string RedirectToHostAndPort { get; set; }
    public string? JwtTokenOrPath { get; set; }
    public string? PathToLogs { get; set; }
}