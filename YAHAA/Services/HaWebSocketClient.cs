using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace YAHAA.Services
{
    /// <summary>
    /// A minimal client for Home Assistant's WebSocket API: connects, authenticates with a
    /// long-lived token, sends commands and awaits their results, and dispatches subscribed events.
    /// </summary>
    public sealed partial class HaWebSocketClient : IDisposable
    {
        private readonly ClientWebSocket _ws = new();
        private readonly ConcurrentDictionary<int, TaskCompletionSource<CommandResult>> _pending = new();
        private readonly SemaphoreSlim _sendLock = new(1, 1);

        private int _id;
        private Action<JsonElement>? _onEvent;
        private CancellationToken _ct;

        public bool IsOpen => _ws.State == WebSocketState.Open;

        public void OnEvent(Action<JsonElement> handler) => _onEvent = handler;

        public async Task ConnectAndAuthAsync(string baseUrl, string token, CancellationToken ct)
        {
            _ct = ct;
            await _ws.ConnectAsync(ToWebSocketUri(baseUrl), ct).ConfigureAwait(false);

            // Handshake: auth_required -> auth -> auth_ok.
            _ = await ReceiveMessageAsync(ct).ConfigureAwait(false);
            await SendRawAsync(JsonSerializer.Serialize(new { type = "auth", access_token = token }), ct).ConfigureAwait(false);

            var authReply = await ReceiveMessageAsync(ct).ConfigureAwait(false);
            using (var doc = JsonDocument.Parse(authReply))
            {
                var type = doc.RootElement.TryGetProperty("type", out var t) ? t.GetString() : null;
                if (type != "auth_ok")
                    throw new InvalidOperationException("Home Assistant rejected the access token.");
            }

            _ = Task.Run(() => ReceiveLoopAsync(ct), ct);
        }

        public async Task<JsonElement> SendCommandAsync(Dictionary<string, object?> command, CancellationToken ct)
        {
            var id = Interlocked.Increment(ref _id);
            command["id"] = id;

            var tcs = new TaskCompletionSource<CommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = tcs;

            await SendRawAsync(JsonSerializer.Serialize(command), ct).ConfigureAwait(false);

            using (ct.Register(() => tcs.TrySetCanceled()))
            {
                var result = await tcs.Task.ConfigureAwait(false);
                if (!result.Success)
                    throw new InvalidOperationException(result.Error ?? "Home Assistant command failed.");
                return result.Result;
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            try
            {
                while (_ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var message = await ReceiveMessageAsync(ct).ConfigureAwait(false);
                    Dispatch(message);
                }
            }
            catch
            {
                // Connection ended; pending commands are failed below.
            }
            finally
            {
                foreach (var tcs in _pending.Values)
                    tcs.TrySetException(new InvalidOperationException("WebSocket connection closed."));
                _pending.Clear();
            }
        }

        private void Dispatch(string message)
        {
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeProp)) return;

            switch (typeProp.GetString())
            {
                case "event":
                    if (root.TryGetProperty("event", out var ev))
                        _onEvent?.Invoke(ev.Clone());
                    break;

                case "result":
                    var id = root.GetProperty("id").GetInt32();
                    if (!_pending.TryRemove(id, out var tcs)) break;

                    var success = root.TryGetProperty("success", out var s) && s.GetBoolean();
                    if (success)
                    {
                        var result = root.TryGetProperty("result", out var r) ? r.Clone() : default;
                        tcs.TrySetResult(new CommandResult(true, result, null));
                    }
                    else
                    {
                        string? error = null;
                        if (root.TryGetProperty("error", out var e) && e.TryGetProperty("message", out var m))
                            error = m.GetString();
                        tcs.TrySetResult(new CommandResult(false, default, error));
                    }
                    break;
            }
        }

        private async Task<string> ReceiveMessageAsync(CancellationToken ct)
        {
            var buffer = new byte[8192];
            using var ms = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                    throw new InvalidOperationException("WebSocket closed by Home Assistant.");
                ms.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
        }

        private async Task SendRawAsync(string json, CancellationToken ct)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            await _sendLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private static Uri ToWebSocketUri(string baseUrl)
        {
            var uri = new Uri(HomeAssistantClient.NormalizeUrl(baseUrl));
            var scheme = uri.Scheme == Uri.UriSchemeHttps ? "wss" : "ws";
            return new Uri($"{scheme}://{uri.Authority}/api/websocket");
        }

        public void Dispose()
        {
            try
            {
                if (_ws.State == WebSocketState.Open)
                    _ws.Abort();
            }
            catch
            {
                // best effort
            }

            _ws.Dispose();
            _sendLock.Dispose();
        }

        private readonly record struct CommandResult(bool Success, JsonElement Result, string? Error);
    }
}
