using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;
using Geuneda.Realtime;
using NUnit.Framework;
using UnityEngine.TestTools;

// ReSharper disable once CheckNamespace

namespace Geuneda.Realtime.Tests
{
	/// <summary>
	/// 실제 <see cref="ClientWebSocketTransport"/> 을 127.0.0.1 서버(<see cref="LoopbackWebSocketServer"/>)에 붙여 검증합니다.
	/// </summary>
	[TestFixture]
	public class ClientWebSocketTransportTest
	{
		private sealed class EchoBody
		{
			public string Text;
		}

		private static readonly RpcMethod LoginMethod = RpcMethod.Of("ISessionRpc/Login");
		private static readonly RpcMethod EchoMethod = RpcMethod.Of("IEchoRpc/Echo");
		private const string SubProtocol = "geuneda.rpc.v1.json";

		private LoopbackWebSocketServer _server;
		private RealtimeClient _client;
		private List<RealtimeDisconnectInfo> _disconnects;
		private int _mainThreadId;
		private int _loginResumedThreadId;

		[SetUp]
		public void SetUp()
		{
			_disconnects = new List<RealtimeDisconnectInfo>();
			_mainThreadId = Thread.CurrentThread.ManagedThreadId;
			_loginResumedThreadId = 0;
		}

		[TearDown]
		public void TearDown()
		{
			_client?.Dispose();
			_server?.Dispose();
		}

		private RealtimeClient CreateClient(Action<RealtimeClientOptions> configure = null)
		{
			var options = new RealtimeClientOptions
			{
				ServerUri = _server.Uri,
				ConnectTimeout = TimeSpan.FromSeconds(5),
				RequestTimeout = TimeSpan.FromSeconds(5),
				PingInterval = TimeSpan.Zero,
				LoginAsync = async (caller, cancellationToken) =>
				{
					var login = await caller.CallAsync(LoginMethod, new EchoBody { Text = "token" }, cancellationToken);
					_loginResumedThreadId = Thread.CurrentThread.ManagedThreadId;
					return login;
				}
			};

			configure?.Invoke(options);

			_client = new RealtimeClient(options);
			_client.Disconnected += info => _disconnects.Add(info);
			return _client;
		}

		private static byte[] PushOfSize(int size)
		{
			return RpcFrameCodec.Encode(RpcFrame.Push(1, new ArraySegment<byte>(new byte[size - RpcFrameCodec.ShortHeaderSize])));
		}

		private static async UniTask WaitUntil(Func<bool> condition)
		{
			var clock = Stopwatch.StartNew();
			while (!condition() && clock.Elapsed < TimeSpan.FromSeconds(5))
			{
				await UniTask.Yield();
			}

			Assert.IsTrue(condition(), "condition was not met in time");
		}

		[UnityTest]
		public IEnumerator RealSocket_RoundTripsLargeFragmentedMessagesAndPings() => UniTask.ToCoroutine(async () =>
		{
			_server = new LoopbackWebSocketServer(SubProtocol, true);
			var client = CreateClient(options =>
			{
				options.PingInterval = TimeSpan.FromMilliseconds(50);
				options.PingTimeout = TimeSpan.FromSeconds(2);
			});
			var text = new string('x', 100 * 1024);

			var connected = await client.ConnectAsync();
			var echoed = await client.CallAsync<EchoBody, EchoBody>(EchoMethod, new EchoBody { Text = text });
			var callResumedThreadId = Thread.CurrentThread.ManagedThreadId;
			await WaitUntil(() => _server.PingSequences.Count >= 3);

			Assert.IsTrue(connected.IsSuccess, connected.ToString());
			Assert.AreEqual(text, echoed.Value.Text);
			Assert.AreEqual(_mainThreadId, _loginResumedThreadId, "login delegate resumes on the main thread");
			Assert.AreEqual(_mainThreadId, callResumedThreadId, "call result on the main thread");
			Assert.AreEqual(RealtimeState.Connected, client.State);
			CollectionAssert.IsEmpty(_disconnects);
		});

		[UnityTest]
		public IEnumerator RealSocket_ServerPolicyClose_ReportsStatusAndAnswersClose() => UniTask.ToCoroutine(async () =>
		{
			_server = new LoopbackWebSocketServer(SubProtocol, false);
			var client = CreateClient();
			await client.ConnectAsync();

			_server.CloseAll(RealtimeCloseCodes.DuplicateLogin);
			await WaitUntil(() => _disconnects.Count == 1 && _server.ClientCloseCodes.Count == 1);
			var blocked = await client.CallAsync<EchoBody, EchoBody>(EchoMethod, new EchoBody { Text = "x" });

			Assert.AreEqual(RealtimeCloseCodes.DuplicateLogin, _disconnects[0].CloseStatus);
			Assert.IsFalse(_disconnects[0].WillReconnect);
			Assert.AreEqual(RealtimeCloseCodes.DuplicateLogin, _server.ClientCloseCodes.Single(), "client echoes the close code");
			Assert.AreEqual(RealtimeResultCodes.NotReconnectable, blocked.Code);
			Assert.AreEqual(1, _server.AcceptedCount);
		});

		[UnityTest]
		public IEnumerator RealSocket_Close1012_IsRejectedByClientWebSocketAsAbnormal() => UniTask.ToCoroutine(async () =>
		{
			// Unity(Mono) 의 ClientWebSocket 은 받은 종료 코드 1012~1014 를 무효로 보고 끊는다. 서버는 재시작 종료에 1001 을 쓴다(README)
			_server = new LoopbackWebSocketServer(SubProtocol, false);
			var client = CreateClient(options => options.MaxReconnectAttempts = 0);
			await client.ConnectAsync();

			_server.CloseAll(1012);
			await WaitUntil(() => _disconnects.Count == 1);

			Assert.AreEqual(RealtimeCloseCodes.Abnormal, _disconnects[0].CloseStatus);
			Assert.IsTrue(RealtimeCloseCodes.IsReconnectable(_disconnects[0].CloseStatus));
		});

		[UnityTest]
		public IEnumerator RealSocket_TextMessage_ClosesWithUnsupportedData() => UniTask.ToCoroutine(async () =>
		{
			_server = new LoopbackWebSocketServer(SubProtocol, false);
			var client = CreateClient(options => options.MaxReconnectAttempts = 0);
			await client.ConnectAsync();

			await _server.SendToAll(LoopbackWebSocketPeer.OpText, Encoding.UTF8.GetBytes("hello"), 1);
			await WaitUntil(() => _disconnects.Count == 1 && _server.ClientCloseCodes.Count == 1);

			Assert.AreEqual(RealtimeCloseCodes.UnsupportedData, _disconnects[0].CloseStatus);
			Assert.AreEqual(RealtimeCloseCodes.UnsupportedData, _server.ClientCloseCodes.Single());
		});

		[UnityTest]
		public IEnumerator RealSocket_MessageAtLimitIsAcceptedAndOverLimitClosesWithMessageTooBig() => UniTask.ToCoroutine(async () =>
		{
			_server = new LoopbackWebSocketServer(SubProtocol, false);
			var client = CreateClient(options => options.MaxReconnectAttempts = 0);
			await client.ConnectAsync();
			var limit = ClientWebSocketConnection.MaxMessageSize;

			await _server.SendToAll(LoopbackWebSocketPeer.OpBinary, PushOfSize(limit), 4);
			var alive = await client.CallAsync<EchoBody, EchoBody>(EchoMethod, new EchoBody { Text = "alive" });
			var overLimit = _server.SendToAll(LoopbackWebSocketPeer.OpBinary, PushOfSize(limit + 1), 4);
			await WaitUntil(() => _disconnects.Count == 1 && _server.ClientCloseCodes.Count == 1);
			await overLimit;

			Assert.AreEqual("alive", alive.Value.Text);
			Assert.AreEqual(RealtimeCloseCodes.MessageTooBig, _disconnects[0].CloseStatus);
			Assert.AreEqual(RealtimeCloseCodes.MessageTooBig, _server.ClientCloseCodes.Single());
		});

		[UnityTest]
		public IEnumerator RealSocket_Dispose_SendsNormalClose() => UniTask.ToCoroutine(async () =>
		{
			_server = new LoopbackWebSocketServer(SubProtocol, false);
			var client = CreateClient();
			await client.ConnectAsync();

			client.Dispose();
			await WaitUntil(() => _server.ClientCloseCodes.Count == 1);

			Assert.AreEqual(RealtimeCloseCodes.Normal, _server.ClientCloseCodes.Single());
			CollectionAssert.IsEmpty(_disconnects);
		});

		[UnityTest]
		public IEnumerator RealSocket_SubProtocolNotAccepted_IsProtocolMismatch() => UniTask.ToCoroutine(async () =>
		{
			_server = new LoopbackWebSocketServer(null, false);

			var result = await new ClientWebSocketTransport().ConnectAsync(_server.Uri, SubProtocol, CancellationToken.None);

			Assert.AreEqual(RealtimeResultCodes.ProtocolMismatch, result.Code);
		});

		[UnityTest]
		public IEnumerator RealSocket_ServerChoosesOtherSubProtocol_IsProtocolMismatchAndStopsReconnect() => UniTask.ToCoroutine(async () =>
		{
			_server = new LoopbackWebSocketServer(SubProtocol, false);
			var reconnects = new List<Result>();
			var client = CreateClient(options =>
			{
				options.ReconnectBaseDelay = TimeSpan.FromMilliseconds(10);
				options.ReconnectMaxDelay = TimeSpan.FromMilliseconds(20);
			});
			client.Reconnected += result => reconnects.Add(result);
			await client.ConnectAsync();

			_server.AcceptedSubProtocol = "geuneda.rpc.v1.msgpack";
			var direct = await new ClientWebSocketTransport().ConnectAsync(_server.Uri, SubProtocol, CancellationToken.None);
			_server.CloseAll(RealtimeCloseCodes.GoingAway);
			await WaitUntil(() => reconnects.Count == 1);

			Assert.AreEqual(RealtimeResultCodes.ProtocolMismatch, direct.Code, direct.ToString());
			Assert.AreEqual(RealtimeResultCodes.ProtocolMismatch, reconnects[0].Code, reconnects[0].ToString());
			Assert.AreEqual(3, _server.AcceptedCount, "first connect, direct connect, one reconnect attempt");
		});

		[UnityTest]
		public IEnumerator RealSocket_NoServer_IsNetworkError() => UniTask.ToCoroutine(async () =>
		{
			_server = new LoopbackWebSocketServer(SubProtocol, false);
			var uri = _server.Uri;
			_server.Dispose();

			var result = await new ClientWebSocketTransport().ConnectAsync(uri, SubProtocol, CancellationToken.None);

			Assert.AreEqual(ResultCodes.NetworkError, result.Code, result.ToString());
		});
	}
}
