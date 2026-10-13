using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace Geuneda.Realtime.Server.Tests
{
	/// <summary>
	/// 프레임을 직접 다루는 테스트 클라이언트입니다(.NET ClientWebSocket). 응답을 기다리는 동안 온 푸시는 <see cref="Pushes"/> 에 모읍니다.
	/// </summary>
	internal sealed class RawRpcClient : IAsyncDisposable
	{
		private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

		private readonly ClientWebSocket _socket;
		private readonly IRpcSerializer _serializer;
		private uint _lastRequestId;

		private RawRpcClient(ClientWebSocket socket, IRpcSerializer serializer)
		{
			_socket = socket;
			_serializer = serializer;
		}

		public string SubProtocol => _socket.SubProtocol;
		public WebSocketCloseStatus? CloseStatus => _socket.CloseStatus;
		public Queue<RpcFrame> Pushes { get; } = new Queue<RpcFrame>();

		public static async Task<RawRpcClient> ConnectAsync(Uri uri, IRpcSerializer serializer = null, string subProtocol = null)
		{
			serializer ??= new JsonRpcSerializer();
			var socket = new ClientWebSocket();
			socket.Options.AddSubProtocol(subProtocol ?? RpcProtocol.SubProtocolOf(serializer.Name));
			await socket.ConnectAsync(uri, CancellationToken.None);
			return new RawRpcClient(socket, serializer);
		}

		public async Task<(int Status, TResponse Body)> CallAsync<TRequest, TResponse>(string route, TRequest request)
		{
			var requestId = await SendRequestAsync(route, request);
			var frame = await ReceiveResponseAsync(requestId);
			return (frame.Status, frame.Status == 0 ? _serializer.Deserialize<TResponse>(frame.Payload) : default);
		}

		public async Task<uint> SendRequestAsync<TRequest>(string route, TRequest request)
		{
			var requestId = ++_lastRequestId;
			await SendFrameAsync(RpcFrame.Request(requestId, Fnv1a32.Hash(route), new ArraySegment<byte>(_serializer.Serialize(request))));
			return requestId;
		}

		public async Task<RpcFrame> ReceiveResponseAsync(uint requestId)
		{
			while (true)
			{
				var frame = await ReceiveFrameAsync() ?? throw new InvalidOperationException($"Closed with {CloseStatus} before response {requestId}");
				if (frame.Kind == RpcFrameKind.Response && frame.RequestId == requestId)
				{
					return frame;
				}

				if (frame.Kind == RpcFrameKind.Push)
				{
					Pushes.Enqueue(frame);
				}
			}
		}

		public Task SendFrameAsync(RpcFrame frame)
		{
			return SendRawAsync(RpcFrameCodec.Encode(frame));
		}

		public Task SendRawAsync(byte[] bytes, WebSocketMessageType type = WebSocketMessageType.Binary)
		{
			return _socket.SendAsync(new ArraySegment<byte>(bytes), type, true, CancellationToken.None);
		}

		public T Read<T>(RpcFrame frame)
		{
			return _serializer.Deserialize<T>(frame.Payload);
		}

		/// <summary>프레임 하나를 받습니다. 서버가 닫으면 같은 코드로 답하고 null 입니다.</summary>
		public async Task<RpcFrame?> ReceiveFrameAsync(TimeSpan? timeout = null)
		{
			using var cancellation = new CancellationTokenSource(timeout ?? DefaultTimeout);
			using var message = new MemoryStream();
			var buffer = new byte[16 * 1024];

			while (true)
			{
				var received = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellation.Token);
				if (received.MessageType == WebSocketMessageType.Close)
				{
					if (_socket.State == WebSocketState.CloseReceived)
					{
						await _socket.CloseOutputAsync(received.CloseStatus ?? WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
					}

					return null;
				}

				message.Write(buffer, 0, received.Count);
				if (received.EndOfMessage)
				{
					var bytes = message.ToArray();
					return RpcFrameCodec.TryDecode(new ArraySegment<byte>(bytes), out var frame)
						? frame
						: throw new InvalidDataException("Server sent an invalid frame");
				}
			}
		}

		/// <summary>서버가 닫을 때까지 받고(프레임은 버림) 종료 코드를 돌려줍니다.</summary>
		public async Task<WebSocketCloseStatus?> WaitForCloseAsync(TimeSpan? timeout = null)
		{
			while (await ReceiveFrameAsync(timeout) != null)
			{
			}

			return CloseStatus;
		}

		public async Task CloseAsync(WebSocketCloseStatus status)
		{
			await _socket.CloseOutputAsync(status, null, CancellationToken.None);
		}

		public async ValueTask DisposeAsync()
		{
			if (_socket.State == WebSocketState.Open)
			{
				try
				{
					using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
					await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, timeout.Token);
				}
				catch (Exception exception) when (exception is WebSocketException || exception is OperationCanceledException)
				{
					// 테스트 정리는 최선 노력이다
				}
			}

			_socket.Dispose();
		}
	}
}
