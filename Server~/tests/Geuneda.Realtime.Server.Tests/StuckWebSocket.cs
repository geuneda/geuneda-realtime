using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace Geuneda.Realtime.Server.Tests
{
	/// <summary>
	/// 받기가 취소를 무시하고 <see cref="Release"/> 할 때까지 끝나지 않는 WebSocket 대역입니다.
	/// 연결이 취소된 뒤 받기 루프 정리가 늦어지는 틈을 재현합니다.
	/// </summary>
	internal sealed class StuckWebSocket : WebSocket
	{
		private readonly TaskCompletionSource<WebSocketReceiveResult> _receive =
			new TaskCompletionSource<WebSocketReceiveResult>(TaskCreationOptions.RunContinuationsAsynchronously);

		public override WebSocketCloseStatus? CloseStatus => null;
		public override string CloseStatusDescription => null;
		public override WebSocketState State => WebSocketState.Open;
		public override string SubProtocol => "geuneda.rpc.v1.json";

		public void Release()
		{
			_receive.TrySetResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true, WebSocketCloseStatus.NormalClosure, null));
		}

		public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
		{
			return _receive.Task;
		}

		public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
		{
			return Task.CompletedTask;
		}

		public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken)
		{
			return Task.CompletedTask;
		}

		public override Task CloseAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken)
		{
			return Task.CompletedTask;
		}

		public override void Abort()
		{
		}

		public override void Dispose()
		{
		}
	}
}
