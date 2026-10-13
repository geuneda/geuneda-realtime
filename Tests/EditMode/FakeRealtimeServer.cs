using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;
using Geuneda.Realtime;

// ReSharper disable once CheckNamespace

namespace Geuneda.Realtime.Tests
{
	/// <summary>
	/// 메모리 안의 서버입니다. 요청 메서드별 응답을 정하고, 푸시·종료를 보내고, 받은 프레임을 기록합니다.
	/// 서버가 보내는 메시지는 다음 PlayerLoop 에서 전달합니다(같은 호출 스택에서 재진입하지 않게).
	/// </summary>
	internal sealed class FakeRealtimeServer : IRealtimeTransport
	{
		public readonly List<FakeConnection> Connections = new List<FakeConnection>();
		public readonly List<RpcFrame> ReceivedRequests = new List<RpcFrame>();
		public readonly Dictionary<uint, Func<RpcFrame, (int Status, byte[] Body)?>> Handlers =
			new Dictionary<uint, Func<RpcFrame, (int, byte[])?>>();

		public int ConnectCalls;
		public int FailNextConnects;
		public bool AutoPong = true;
		public string LastSubProtocol;
		public UniTaskCompletionSource ConnectGate;

		public FakeConnection Current => Connections.Count == 0 ? null : Connections[Connections.Count - 1];

		public async UniTask<Result<IRealtimeConnection>> ConnectAsync(Uri uri, string subProtocol, CancellationToken cancellationToken)
		{
			ConnectCalls++;
			LastSubProtocol = subProtocol;

			if (ConnectGate != null)
			{
				try
				{
					await ConnectGate.Task.AttachExternalCancellation(cancellationToken);
				}
				catch (OperationCanceledException)
				{
					return Result<IRealtimeConnection>.Fail(ResultCodes.Cancelled);
				}
			}

			if (FailNextConnects > 0)
			{
				FailNextConnects--;
				return Result<IRealtimeConnection>.Fail(ResultCodes.NetworkError, "refused");
			}

			var connection = new FakeConnection(this);
			Connections.Add(connection);
			return Result<IRealtimeConnection>.Ok(connection);
		}

		public void Respond(RpcMethod method, Func<RpcFrame, (int Status, byte[] Body)?> handler)
		{
			Handlers[method.Id] = handler;
		}

		internal void OnClientFrame(FakeConnection connection, RpcFrame frame)
		{
			switch (frame.Kind)
			{
				case RpcFrameKind.Request:
					ReceivedRequests.Add(frame);

					if (Handlers.TryGetValue(frame.MethodId, out var handler))
					{
						var reply = handler(frame);
						if (reply.HasValue)
						{
							connection.ServerSend(RpcFrame.Response(frame.RequestId, reply.Value.Status, new ArraySegment<byte>(reply.Value.Body ?? new byte[0])));
						}
					}

					break;
				case RpcFrameKind.Ping:
					if (AutoPong)
					{
						connection.ServerSend(RpcFrame.Pong(frame.Sequence));
					}

					break;
			}
		}
	}
}
