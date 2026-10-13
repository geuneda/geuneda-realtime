using System;
using System.IO;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Threading;
using Cysharp.Threading.Tasks;

// ReSharper disable once CheckNamespace

namespace Geuneda.Realtime
{
	/// <summary>
	/// <see cref="ClientWebSocket"/> 하나를 감싼 연결입니다. 바이너리 메시지만 주고받습니다.
	/// </summary>
	/// <remarks>
	/// 소켓 작업은 메인 스레드로 돌아오지 않고(<c>ConfigureAwait(false)</c>) 이어 갑니다. 조각마다 메인 스레드를 거치면 큰 메시지 하나에 수십 프레임이 걸리기 때문입니다.
	/// 그래서 결과는 백그라운드 스레드에서 끝날 수 있습니다.
	/// </remarks>
	internal sealed class ClientWebSocketConnection : IRealtimeConnection
	{
		/// <summary>받는 메시지 하나의 최대 크기입니다. 넘으면 연결을 닫습니다.</summary>
		internal const int MaxMessageSize = 16 * 1024 * 1024;

		private readonly ClientWebSocket _socket;
		private readonly byte[] _buffer = new byte[16 * 1024];

		public ClientWebSocketConnection(ClientWebSocket socket)
		{
			_socket = socket;
		}

		public async UniTask<bool> SendAsync(ArraySegment<byte> message, CancellationToken cancellationToken)
		{
			try
			{
				await _socket.SendAsync(message, WebSocketMessageType.Binary, true, cancellationToken).ConfigureAwait(false);
				return true;
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception exception) when (IsTransportException(exception))
			{
				return false;
			}
		}

		public async UniTask<RealtimeReceiveResult> ReceiveAsync(CancellationToken cancellationToken)
		{
			try
			{
				using (var message = new MemoryStream())
				{
					while (true)
					{
						var received = await _socket.ReceiveAsync(new ArraySegment<byte>(_buffer), cancellationToken).ConfigureAwait(false);

						if (received.MessageType == WebSocketMessageType.Close)
						{
							return RealtimeReceiveResult.Closed(
								(int)(received.CloseStatus ?? (WebSocketCloseStatus)RealtimeCloseCodes.Abnormal), received.CloseStatusDescription);
						}

						if (received.MessageType != WebSocketMessageType.Binary)
						{
							return RealtimeReceiveResult.Closed(RealtimeCloseCodes.UnsupportedData, "Text messages are not supported");
						}

						if (message.Length + received.Count > MaxMessageSize)
						{
							return RealtimeReceiveResult.Closed(RealtimeCloseCodes.MessageTooBig, $"Message larger than {MaxMessageSize} bytes");
						}

						message.Write(_buffer, 0, received.Count);

						if (received.EndOfMessage)
						{
							return RealtimeReceiveResult.FromMessage(new ArraySegment<byte>(message.ToArray()));
						}
					}
				}
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception exception) when (IsTransportException(exception))
			{
				return RealtimeReceiveResult.Closed(RealtimeCloseCodes.Abnormal, exception.Message);
			}
		}

		public async UniTask CloseAsync(int closeStatus, string reason, CancellationToken cancellationToken)
		{
			try
			{
				if (_socket.State == WebSocketState.Open || _socket.State == WebSocketState.CloseReceived)
				{
					await _socket.CloseOutputAsync((WebSocketCloseStatus)closeStatus, reason, cancellationToken).ConfigureAwait(false);
				}
			}
			catch (Exception exception) when (exception is OperationCanceledException || IsTransportException(exception))
			{
				// 닫기는 최선 노력이다. 실패해도 Dispose 로 소켓을 정리한다
			}
		}

		public void Dispose()
		{
			_socket.Dispose();
		}

		private static bool IsTransportException(Exception exception)
		{
			return exception is WebSocketException || exception is SocketException || exception is IOException || exception is ObjectDisposedException;
		}
	}
}
