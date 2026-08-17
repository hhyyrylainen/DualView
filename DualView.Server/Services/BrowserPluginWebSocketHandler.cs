using System.Buffers.Binary;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DualView.Server.Models;
using Backend.Services;

namespace DualView.Server.Services;

/// <summary>
///   Handles the authenticated, length-prefixed browser plugin protocol.
/// </summary>
public sealed class BrowserPluginWebSocketHandler
{
    private const string ExpectedGreeting = "HELODV3";
    private const string ReadyResponse = "DVREADY";
    private const int MaximumMessageSize = 32 * 1024 * 1024;
    private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromMinutes(15);

    private readonly IServiceScopeFactory serviceScopeFactory;
    private readonly ILogger<BrowserPluginWebSocketHandler> logger;

    public BrowserPluginWebSocketHandler(IServiceScopeFactory serviceScopeFactory,
        ILogger<BrowserPluginWebSocketHandler> logger)
    {
        this.serviceScopeFactory = serviceScopeFactory;
        this.logger = logger;
    }

    public async Task HandleAsync(WebSocket socket, string apiVersion, CancellationToken cancellation)
    {
        try
        {
            var greeting = await ReceiveHandshakeStringAsync(socket, ExpectedGreeting, cancellation);
            if (apiVersion != "v3" || greeting == null)
            {
                logger.LogWarning("Browser plugin disconnected due to an invalid API version or greeting");
                await CloseAsync(socket, WebSocketCloseStatus.ProtocolError, "Invalid greeting");
                return;
            }

            using var handshakeScope = serviceScopeFactory.CreateScope();
            var databaseService = handshakeScope.ServiceProvider.GetRequiredService<IDatabaseService>();
            var settings = await databaseService.GetAppSettingsAsync();
            var configuredAccessKey = settings.BrowserPluginAccessKey;
            var accessKey = await ReceiveHandshakeStringAsync(socket, null, cancellation);
            if (accessKey == null || configuredAccessKey == null ||
                !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(accessKey),
                    Encoding.UTF8.GetBytes(configuredAccessKey)))
            {
                logger.LogWarning("Browser plugin disconnected due to an invalid access key");
                await CloseAsync(socket, WebSocketCloseStatus.PolicyViolation, "Invalid access key");
                return;
            }

            await SendTextAsync(socket, ReadyResponse, cancellation);
            await ProcessMessagesAsync(socket, cancellation);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            logger.LogInformation("Browser plugin connection was cancelled");
        }
        catch (OperationCanceledException ex)
        {
            logger.LogWarning(ex, "Browser plugin disconnected after the receive timeout");
            await CloseAsync(socket, WebSocketCloseStatus.EndpointUnavailable, "Receive timeout");
        }
        catch (WebSocketException ex)
        {
            logger.LogWarning(ex, "Browser plugin websocket disconnected unexpectedly");
        }
        catch (InvalidDataException ex)
        {
            logger.LogWarning(ex, "Browser plugin disconnected due to invalid data");
            await CloseAsync(socket, WebSocketCloseStatus.InvalidPayloadData, "Invalid data");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Browser plugin websocket handler failed");
        }
        finally
        {
            socket.Dispose();
        }
    }

    private async Task ProcessMessagesAsync(WebSocket socket, CancellationToken cancellation)
    {
        while (socket.State == WebSocketState.Open)
        {
            var messageData = await ReceiveMessageDataAsync(socket, cancellation);
            if (messageData == null)
            {
                logger.LogInformation("Browser plugin disconnected");
                return;
            }

            BrowserPluginMessage? message;
            try
            {
                message = JsonSerializer.Deserialize<BrowserPluginMessage>(messageData);
                if (message == null || string.IsNullOrWhiteSpace(message.Type))
                    throw new JsonException("The message has no type");
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Browser plugin sent invalid JSON");
                await CloseAsync(socket, WebSocketCloseStatus.InvalidPayloadData, "Invalid JSON");
                return;
            }

            switch (message.Type)
            {
                case "ping":
                {
                    // Example scope, not used but shown here for the real messages which will need this approach
                    using var messageScope = serviceScopeFactory.CreateScope();
                    _ = messageScope.ServiceProvider.GetRequiredService<IDatabaseService>();
                    await SendMessageAsync(socket, new BrowserPluginMessage { Type = "pong" }, cancellation);
                    break;
                }
                default:
                {
                    logger.LogWarning("Browser plugin sent an unknown message type: {MessageType}", message.Type);
                    await CloseAsync(socket, WebSocketCloseStatus.ProtocolError, "Unknown message type");
                    return;
                }
            }
        }
    }

    private async Task<string?> ReceiveHandshakeStringAsync(WebSocket socket, string? expected,
        CancellationToken cancellation)
    {
        var data = await ReceiveWebSocketHandshakeDataRawAsync(socket, 128, cancellation);
        if (data == null)
            return null;

        var value = Encoding.UTF8.GetString(data);
        return expected == null || value == expected ? value : null;
    }

    private async Task<byte[]?> ReceiveMessageDataAsync(WebSocket socket, CancellationToken cancellation)
    {
        var header = await ReceiveExactBytesAsync(socket, sizeof(int), cancellation);
        if (header == null)
            return null;

        var messageLength = BinaryPrimitives.ReadInt32BigEndian(header);
        if (messageLength <= 0 || messageLength > MaximumMessageSize)
            throw new InvalidDataException($"Invalid browser plugin message length: {messageLength}");

        return await ReceiveExactBytesAsync(socket, messageLength, cancellation);
    }

    private async Task<byte[]?> ReceiveExactBytesAsync(WebSocket socket, int length, CancellationToken cancellation)
    {
        var result = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var receiveResult = await ReceiveAsync(socket, result.AsMemory(offset, length - offset), cancellation);
            if (receiveResult.MessageType == WebSocketMessageType.Close)
                return null;

            if (receiveResult.MessageType != WebSocketMessageType.Binary)
                throw new InvalidDataException("Unexpected websocket message type");

            offset += receiveResult.Count;
            if (receiveResult.Count == 0 && receiveResult.EndOfMessage)
                throw new InvalidDataException("Unexpected end of websocket message");
        }

        return result;
    }

    private async Task<byte[]?> ReceiveWebSocketHandshakeDataRawAsync(WebSocket socket, int maximumLength,
        CancellationToken cancellation)
    {
        using var data = new MemoryStream();
        var buffer = new byte[Math.Min(maximumLength, 4096)];
        while (true)
        {
            var receiveResult = await ReceiveAsync(socket, buffer, cancellation);
            if (receiveResult.MessageType == WebSocketMessageType.Close)
                return null;
            if (receiveResult.MessageType != WebSocketMessageType.Text)
                throw new InvalidDataException("Handshake must be a text websocket message");

            if (data.Length + receiveResult.Count > maximumLength)
                throw new InvalidDataException("Handshake is too long");
            data.Write(buffer, 0, receiveResult.Count);
            if (receiveResult.EndOfMessage)
                return data.ToArray();
        }
    }

    private async Task<WebSocketReceiveResult> ReceiveAsync(WebSocket socket, Memory<byte> buffer,
        CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(ReceiveTimeout);
        var result = await socket.ReceiveAsync(buffer, timeout.Token);
        return new WebSocketReceiveResult(result.Count, result.MessageType, result.EndOfMessage);
    }

    private static Task<WebSocketReceiveResult> ReceiveAsync(WebSocket socket, byte[] buffer,
        CancellationToken cancellation)
    {
        return ReceiveWithTimeoutAsync(socket, buffer, cancellation);
    }

    private static async Task<WebSocketReceiveResult> ReceiveWithTimeoutAsync(WebSocket socket, byte[] buffer,
        CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(ReceiveTimeout);
        return await socket.ReceiveAsync(buffer, timeout.Token);
    }

    private static Task SendTextAsync(WebSocket socket, string value, CancellationToken cancellation)
    {
        return socket.SendAsync(Encoding.UTF8.GetBytes(value), WebSocketMessageType.Text, true, cancellation);
    }

    private static Task SendMessageAsync(WebSocket socket, BrowserPluginMessage message,
        CancellationToken cancellation)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message);
        var framedMessage = new byte[sizeof(int) + payload.Length];
        BinaryPrimitives.WriteInt32BigEndian(framedMessage, payload.Length);
        payload.CopyTo(framedMessage, sizeof(int));
        return socket.SendAsync(framedMessage, WebSocketMessageType.Binary, true, cancellation);
    }

    private static async Task CloseAsync(WebSocket socket, WebSocketCloseStatus status, string description)
    {
        if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            await socket.CloseAsync(status, description, CancellationToken.None);
    }
}
