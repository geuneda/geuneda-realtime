using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;

// ReSharper disable once CheckNamespace

namespace Geuneda.Realtime
{
	/// <summary>
	/// 연결 하나의 수명입니다: 수신 루프, 핑퐁, 요청 ID 와 응답 매칭, 보내기 직렬화. 재연결하면 새 세션을 만듭니다.
	/// </summary>
	/// <remarks>
	/// 닫히면(서버 종료, 전송 오류, 핑퐁 무응답, 잘못된 프레임, 클라이언트 종료) 소유자에게 먼저 알리고, 그다음 대기 중인 호출을 실패로 끝냅니다.
	/// 호출 결과를 받은 코드가 이어서 바로 연결을 요청해도 소유자는 이미 이 세션을 내려놓은 상태입니다.
	/// 상태 변경은 <c>_gate</c> 안에서 하고, await 는 그 밖에서 합니다.
	/// </remarks>
	internal sealed class RealtimeSession
	{
		private static readonly ModuleLogger Logger = new ModuleLogger("Geuneda.Realtime");

		private readonly IRealtimeConnection _connection;
		private readonly TimeSpan _pingInterval;
		private readonly TimeSpan _pingTimeout;
		private readonly Action<RealtimeSession, RpcFrame> _onPush;
		private readonly Action<RealtimeSession, RealtimeDisconnectInfo> _onClosed;
		private readonly object _gate = new object();
		private readonly Dictionary<uint, UniTaskCompletionSource<Result<RpcFrame>>> _pending =
			new Dictionary<uint, UniTaskCompletionSource<Result<RpcFrame>>>();
		private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
		private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
		/// <summary>
		/// 받기만 따로 멈춥니다. ClientWebSocket 은 대기 중인 받기를 취소하면 소켓을 끊으므로, 종료를 보낸 뒤에 취소합니다.
		/// </summary>
		private readonly CancellationTokenSource _receiveStop = new CancellationTokenSource();
		private readonly Stopwatch _clock = Stopwatch.StartNew();

		private long _lastPongMilliseconds;
		private uint _lastRequestId;
		private int _lastPingSequence;
		private bool _closed;
		private RealtimeDisconnectInfo _closeInfo;

		public RealtimeSession(IRealtimeConnection connection, TimeSpan pingInterval, TimeSpan pingTimeout,
			Action<RealtimeSession, RpcFrame> onPush, Action<RealtimeSession, RealtimeDisconnectInfo> onClosed)
		{
			_connection = connection;
			_pingInterval = pingInterval;
			_pingTimeout = pingTimeout;
			_onPush = onPush;
			_onClosed = onClosed;
		}

		public bool IsClosed
		{
			get
			{
				lock (_gate)
				{
					return _closed;
				}
			}
		}

		/// <summary>닫힌 이유입니다. 열려 있으면 null 입니다.</summary>
		public RealtimeDisconnectInfo? CloseInfo
		{
			get
			{
				lock (_gate)
				{
					return _closed ? _closeInfo : (RealtimeDisconnectInfo?)null;
				}
			}
		}

		public void Start()
		{
			Interlocked.Exchange(ref _lastPongMilliseconds, _clock.ElapsedMilliseconds);
			ReceiveLoopAsync().Forget();

			if (_pingInterval > TimeSpan.Zero)
			{
				PingLoopAsync().Forget();
			}
		}

		/// <summary>
		/// 요청을 보내고 응답 프레임을 기다립니다. 상태 코드는 해석하지 않습니다.
		/// 실패: 닫힘·보내기 실패·끊김 <c>NetworkError</c>(클라이언트 종료면 그때 정한 코드), 시간 초과 <c>Timeout</c>, 취소 <c>Cancelled</c>.
		/// 제한 시간과 취소는 보내기 차례를 기다릴 때부터 적용하고, 차례가 오기 전에 끝난 요청은 보내지 않습니다.
		/// </summary>
		public async UniTask<Result<RpcFrame>> CallAsync(uint methodId, byte[] payload, TimeSpan timeout, CancellationToken cancellationToken)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				return Result<RpcFrame>.Fail(ResultCodes.Cancelled);
			}

			var completion = new UniTaskCompletionSource<Result<RpcFrame>>();
			uint requestId;

			lock (_gate)
			{
				if (_closed)
				{
					return Result<RpcFrame>.Fail(ResultCodes.NetworkError, "Connection is closed");
				}

				requestId = NextRequestId();
				_pending.Add(requestId, completion);
			}

			using (var timeoutSource = new CancellationTokenSource(timeout))
			using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token))
			{
				try
				{
					var request = RpcFrame.Request(requestId, methodId, new ArraySegment<byte>(payload));
					var sent = await SendAsync(request, linked.Token).AttachExternalCancellation(linked.Token);
					linked.Token.ThrowIfCancellationRequested();

					if (!sent)
					{
						// 닫히면서 이미 정해진 결과(클라이언트 종료 코드 등)가 있으면 그 결과를 쓴다
						completion.TrySetResult(Result<RpcFrame>.Fail(ResultCodes.NetworkError, "Send failed"));
					}

					return await completion.Task.AttachExternalCancellation(linked.Token);
				}
				catch (OperationCanceledException) when (linked.IsCancellationRequested)
				{
					// 늦게 온 응답은 수신 루프가 버린다
					RemovePending(requestId);

					return cancellationToken.IsCancellationRequested
						? Result<RpcFrame>.Fail(ResultCodes.Cancelled)
						: Result<RpcFrame>.Fail(ResultCodes.Timeout, $"No response in {timeout.TotalSeconds:0.###}s");
				}
			}
		}

		/// <summary>
		/// 세션을 닫습니다. 처음 한 번만 동작합니다. <paramref name="notify"/> 면 먼저 소유자에게 알리고, 수명을 끝내 루프와 보내기를 멈추고,
		/// 연결에 <paramref name="sendStatus"/> 로 종료를 보내기 시작한 뒤, 대기 중인 호출을 <paramref name="pendingCode"/> 로 끝냅니다.
		/// </summary>
		public void Close(RealtimeDisconnectInfo info, int sendStatus, int pendingCode, bool notify)
		{
			UniTaskCompletionSource<Result<RpcFrame>>[] pending;

			lock (_gate)
			{
				if (_closed)
				{
					return;
				}

				_closed = true;
				_closeInfo = info;
				pending = _pending.Values.ToArray();
				_pending.Clear();
			}

			try
			{
				// 대기 호출의 이어지는 코드는 그 자리에서 실행될 수 있으므로 소유자 상태부터 바꾼다
				if (notify)
				{
					_onClosed(this, info);
				}
			}
			finally
			{
				// 소유자 알림이 실패해도 정리는 끝까지 한다
				_lifetime.Cancel();
				CloseConnectionAsync(sendStatus).Forget();

				var message = $"Connection closed: {info.Reason ?? info.CloseStatus.ToString()}";
				foreach (var completion in pending)
				{
					completion.TrySetResult(Result<RpcFrame>.Fail(pendingCode, message));
				}
			}
		}

		private uint NextRequestId()
		{
			// 0 은 쓰지 않는다(푸시와 구분하기 쉽도록)
			_lastRequestId = _lastRequestId == uint.MaxValue ? 1 : _lastRequestId + 1;
			return _lastRequestId;
		}

		private void RemovePending(uint requestId)
		{
			lock (_gate)
			{
				_pending.Remove(requestId);
			}
		}

		/// <summary>
		/// 프레임 하나를 보냅니다. 보내기는 한 번에 하나씩입니다. <paramref name="waitToken"/> 이 차례 전에 취소되거나 세션이 닫히면 보내지 않고 false 입니다.
		/// 보내기 시작한 프레임은 세션 수명 동안 끝까지 보냅니다(중간에 끊으면 연결이 깨짐).
		/// </summary>
		private async UniTask<bool> SendAsync(RpcFrame frame, CancellationToken waitToken)
		{
			var bytes = RpcFrameCodec.Encode(frame);

			try
			{
				await _sendLock.WaitAsync(waitToken);
			}
			catch (OperationCanceledException) when (waitToken.IsCancellationRequested)
			{
				return false;
			}

			try
			{
				if (IsClosed || waitToken.IsCancellationRequested)
				{
					return false;
				}

				bool sent;
				try
				{
					sent = await _connection.SendAsync(new ArraySegment<byte>(bytes), _lifetime.Token);
				}
				catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
				{
					return false;
				}

				if (!sent)
				{
					Close(new RealtimeDisconnectInfo(RealtimeCloseCodes.Abnormal, "Send failed", false),
						RealtimeCloseCodes.GoingAway, ResultCodes.NetworkError, true);
				}

				return sent;
			}
			finally
			{
				_sendLock.Release();
			}
		}

		private async UniTaskVoid ReceiveLoopAsync()
		{
			var token = _receiveStop.Token;

			try
			{
				while (true)
				{
					var received = await _connection.ReceiveAsync(token);

					// 닫는 중(종료를 보내는 동안)에 온 메시지는 버린다
					if (IsClosed)
					{
						return;
					}

					if (received.IsClosed)
					{
						// 받은 종료 코드를 그대로 돌려준다(RFC 6455 5.5.1). 연결이 직접 거부한 경우(1003, 1009)도 그 코드로 알린다
						var sendStatus = RealtimeCloseCodes.CanSend(received.CloseStatus) ? received.CloseStatus : RealtimeCloseCodes.Normal;
						Close(new RealtimeDisconnectInfo(received.CloseStatus, received.CloseReason, false),
							sendStatus, ResultCodes.NetworkError, true);
						return;
					}

					if (!RpcFrameCodec.TryDecode(received.Message, out var frame))
					{
						Close(new RealtimeDisconnectInfo(RealtimeCloseCodes.ProtocolError, "Invalid frame", false),
							RealtimeCloseCodes.ProtocolError, ResultCodes.NetworkError, true);
						return;
					}

					HandleFrame(frame);
				}
			}
			catch (OperationCanceledException) when (token.IsCancellationRequested)
			{
				// 세션을 닫았다
			}
			catch (Exception exception)
			{
				// 수신 루프의 예상 못 한 오류(버그 포함)로 세션이 멈춘 채 남지 않게 닫고 재연결에 맡긴다
				Logger.Error(exception, "Realtime receive loop failed");
				Close(new RealtimeDisconnectInfo(RealtimeCloseCodes.Abnormal, exception.Message, false),
					RealtimeCloseCodes.GoingAway, ResultCodes.NetworkError, true);
			}
		}

		private void HandleFrame(in RpcFrame frame)
		{
			switch (frame.Kind)
			{
				case RpcFrameKind.Response:
					UniTaskCompletionSource<Result<RpcFrame>> completion;

					lock (_gate)
					{
						if (_pending.TryGetValue(frame.RequestId, out completion))
						{
							_pending.Remove(frame.RequestId);
						}
					}

					if (completion == null)
					{
						Logger.Debug($"Dropped response for finished request {frame.RequestId}");
					}
					else
					{
						completion.TrySetResult(Result<RpcFrame>.Ok(frame));
					}

					break;
				case RpcFrameKind.Push:
					_onPush(this, frame);
					break;
				case RpcFrameKind.Ping:
					SendAsync(RpcFrame.Pong(frame.Sequence), _lifetime.Token).Forget();
					break;
				case RpcFrameKind.Pong:
					Interlocked.Exchange(ref _lastPongMilliseconds, _clock.ElapsedMilliseconds);
					break;
				default:
					Close(new RealtimeDisconnectInfo(RealtimeCloseCodes.ProtocolError, $"Unexpected {frame.Kind} frame", false),
						RealtimeCloseCodes.ProtocolError, ResultCodes.NetworkError, true);
					break;
			}
		}

		private async UniTaskVoid PingLoopAsync()
		{
			var token = _lifetime.Token;
			var limitMilliseconds = (long)(_pingInterval + _pingTimeout).TotalMilliseconds;

			try
			{
				while (true)
				{
					await UniTask.Delay(_pingInterval, true, PlayerLoopTiming.Update, token);

					var sincePong = _clock.ElapsedMilliseconds - Interlocked.Read(ref _lastPongMilliseconds);
					if (sincePong > limitMilliseconds)
					{
						Close(new RealtimeDisconnectInfo(RealtimeCloseCodes.Abnormal, "Ping timeout", false),
							RealtimeCloseCodes.GoingAway, ResultCodes.NetworkError, true);
						return;
					}

					// 보내기가 막혀도 타이머는 계속 돌아야 끊김을 감지한다
					SendAsync(RpcFrame.Ping((uint)Interlocked.Increment(ref _lastPingSequence)), _lifetime.Token).Forget();
				}
			}
			catch (OperationCanceledException) when (token.IsCancellationRequested)
			{
				// 세션을 닫았다
			}
		}

		private async UniTaskVoid CloseConnectionAsync(int sendStatus)
		{
			try
			{
				// 종료 설명은 123바이트 제한이 있어 싣지 않는다(이유는 로그와 Disconnected 로 전달)
				using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
				{
					await _connection.CloseAsync(sendStatus, null, timeout.Token);
				}
			}
			finally
			{
				_receiveStop.Cancel();
				_connection.Dispose();
			}
		}
	}
}
