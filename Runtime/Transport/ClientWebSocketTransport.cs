using System;
using System.IO;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;

// ReSharper disable once CheckNamespace

namespace Geuneda.Realtime
{
	/// <summary>
	/// <see cref="ClientWebSocket"/> 으로 연결하는 기본 전송입니다. WebGL 에서는 동작하지 않습니다(별도 전송 필요).
	/// </summary>
	/// <remarks>
	/// 연결 실패(<see cref="WebSocketException"/>, <see cref="SocketException"/>, <see cref="IOException"/>)는 <c>NetworkError</c>,
	/// 취소는 <c>Cancelled</c>, 서버가 서브프로토콜을 돌려주지 않거나 다른 것을 고르면 <see cref="RealtimeResultCodes.ProtocolMismatch"/> 입니다.
	/// 잘못된 주소 형식 같은 오용 예외는 그대로 던집니다.
	/// </remarks>
	public sealed class ClientWebSocketTransport : IRealtimeTransport
	{
		public async UniTask<Result<IRealtimeConnection>> ConnectAsync(Uri uri, string subProtocol, CancellationToken cancellationToken)
		{
			if (uri == null)
			{
				throw new ArgumentNullException(nameof(uri));
			}

			var socket = new ClientWebSocket();

			try
			{
				if (!string.IsNullOrEmpty(subProtocol))
				{
					socket.Options.AddSubProtocol(subProtocol);
				}

				await socket.ConnectAsync(uri, cancellationToken);

				if (!string.IsNullOrEmpty(subProtocol) && socket.SubProtocol != subProtocol)
				{
					// 형식이 다르면 메시지를 잘못 읽으므로 쓰지 않고 끊는다
					var accepted = socket.SubProtocol;
					socket.Dispose();
					return Result<IRealtimeConnection>.Fail(RealtimeResultCodes.ProtocolMismatch,
						$"Server did not accept subprotocol {subProtocol} (accepted '{accepted}')");
				}

				return Result<IRealtimeConnection>.Ok(new ClientWebSocketConnection(socket));
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				socket.Dispose();
				return Result<IRealtimeConnection>.Fail(ResultCodes.Cancelled);
			}
			catch (WebSocketException exception) when (exception.WebSocketErrorCode == WebSocketError.UnsupportedProtocol)
			{
				// 서버가 요청하지 않은 서브프로토콜을 골랐다. 다시 시도해도 같으므로 재연결 대상이 아니다
				socket.Dispose();
				return Result<IRealtimeConnection>.Fail(RealtimeResultCodes.ProtocolMismatch, $"Server chose another subprotocol than {subProtocol}: {exception.Message}");
			}
			catch (Exception exception) when (exception is WebSocketException || exception is SocketException || exception is IOException)
			{
				socket.Dispose();
				return Result<IRealtimeConnection>.Fail(ResultCodes.NetworkError, $"Connect to {uri.Host} failed: {exception.Message}");
			}
			catch
			{
				socket.Dispose();
				throw;
			}
		}
	}
}
