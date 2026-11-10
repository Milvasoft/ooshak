using System.Net;
using System.Text;
using System.Text.Json;

namespace Ooshak.Core;

/// <summary>
/// Receives Claude Code hook payloads on localhost. The custom header forces a CORS preflight,
/// so a web page in a browser cannot post here.
/// </summary>
public sealed class HookServer(
    int port,
    Action<string, JsonElement> onEvent,
    Func<JsonElement, Task<string?>>? onPermission = null) : IDisposable
{
    public const string RequiredHeader = "X-Ooshak";
    public const string PermissionRoute = "permissionrequest";

    private readonly HttpListener _listener = new();

    public void Start()
    {
        _listener.Prefixes.Add($"http://localhost:{port}/");
        _listener.Start();
        _ = Task.Run(LoopAsync);
    }

    private async Task LoopAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception e) when (e is HttpListenerException or ObjectDisposedException)
            {
                return;
            }

            Handle(context);
        }
    }

    private void Handle(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        if (request.HttpMethod != "POST" || request.Headers[RequiredHeader] is null)
        {
            Finish(response, 403);
            return;
        }

        JsonElement payload;
        try
        {
            using var json = JsonDocument.Parse(request.InputStream);
            payload = json.RootElement.Clone();
        }
        catch (JsonException)
        {
            Finish(response, 400);
            return;
        }

        var route = request.Url!.AbsolutePath.Trim('/');

        // A permission question stays open until the user answers, so it must not hold up other hooks.
        if (route == PermissionRoute && onPermission is not null)
        {
            _ = Task.Run(() => AnswerAsync(response, payload, onPermission));
            return;
        }

        onEvent(route, payload);
        Finish(response, 204);
    }

    private static async Task AnswerAsync(HttpListenerResponse response, JsonElement payload, Func<JsonElement, Task<string?>> answer)
    {
        try
        {
            var body = await answer(payload);
            if (body is null)
            {
                response.StatusCode = 204;
                return;
            }

            var bytes = Encoding.UTF8.GetBytes(body);
            response.ContentType = "application/json";
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes);
        }
        catch (Exception e) when (e is HttpListenerException or ObjectDisposedException or InvalidOperationException)
        {
            // The hook gave up waiting, so Claude's own dialog has taken over.
        }
        finally
        {
            try
            {
                response.Close();
            }
            catch (Exception e) when (e is HttpListenerException or ObjectDisposedException)
            {
                // Same as above: nobody is listening any more.
            }
        }
    }

    private static void Finish(HttpListenerResponse response, int status)
    {
        response.StatusCode = status;
        response.Close();
    }

    public void Dispose() => ((IDisposable)_listener).Dispose();
}
