using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Realtime;

// ReSharper disable once CheckNamespace

namespace Geuneda.Realtime.Tests
{
	/// <summary>
	/// <see cref="FakeRealtimeServer"/> 가 만든 연결 하나입니다. 서버가 보내는 메시지는 다음 PlayerLoop 에서 전달합니다.
	/// </summary>
	internal sealed class FakeConnection : IRealtimeConnection
	{
		private readonly FakeRealtimeServer _server;
		private readonly Queue<RealtimeReceiveResult> _inbox = new Queue<RealtimeReceiveResult>();
		private UniTaskCompletionSource<RealtimeReceiveResult> _waiting;

		public int? ClientCloseStatus;
		public bool Disposed;
		public bool ServerClosed;
		/// <summary>설정하면 완료될 때까지 보내기가 끝나지 않습니다(막힌 전송 재현).</summary>
		public UniTaskCompletionSource SendGate;

		public FakeConnection(FakeRealtimeServer server)
		{
			_server = server;
		}

		public async UniTask<bool> SendAsync(ArraySegment<byte> message, CancellationToken cancellationToken)
		{
			if (ServerClosed || Disposed)
			{
				return false;
			}

			var copy = new byte[message.Count];
			Buffer.BlockCopy(message.Array, message.Offset, copy, 0, message.Count);

			if (SendGate != null)
			{
				await SendGate.Task.AttachExternalCancellation(cancellationToken);
			}

			if (RpcFrameCodec.TryDecode(new ArraySegment<byte>(copy), out var frame))
			{
				_server.OnClientFrame(this, frame);
			}

			return true;
		}

		public UniTask<RealtimeReceiveResult> ReceiveAsync(CancellationToken cancellationToken)
		{
			if (_inbox.Count > 0)
			{
				return UniTask.FromResult(_inbox.Dequeue());
			}

			_waiting = new UniTaskCompletionSource<RealtimeReceiveResult>();
			return _waiting.Task.AttachExternalCancellation(cancellationToken);
		}

		public UniTask CloseAsync(int closeStatus, string reason, CancellationToken cancellationToken)
		{
			ClientCloseStatus = closeStatus;
			return UniTask.CompletedTask;
		}

		public void Dispose()
		{
			Disposed = true;
		}

		public void ServerSend(RpcFrame frame)
		{
			Deliver(RealtimeReceiveResult.FromMessage(new ArraySegment<byte>(RpcFrameCodec.Encode(frame))));
		}

		public void ServerSendRaw(byte[] bytes)
		{
			Deliver(RealtimeReceiveResult.FromMessage(new ArraySegment<byte>(bytes)));
		}

		public void ServerClose(int status, string reason)
		{
			ServerClosed = true;
			Deliver(RealtimeReceiveResult.Closed(status, reason));
		}

		private void Deliver(RealtimeReceiveResult result)
		{
			// 다음 PlayerLoop 에서 전달해 보내는 쪽 호출 스택에서 수신 처리가 이어지지 않게 한다
			UniTask.Post(() =>
			{
				var waiting = _waiting;
				_waiting = null;

				if (waiting == null || !waiting.TrySetResult(result))
				{
					_inbox.Enqueue(result);
				}
			});
		}
	}
}
