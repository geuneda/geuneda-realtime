using System;
using System.Linq;
using System.Net.WebSockets;
using System.Threading.Tasks;
using Xunit;

namespace Geuneda.Realtime.Server.Tests
{
	/// <summary>세션이 닫힐 때 RPC 실행 수명, 늦은 로그인, 대기열 포화, 잘못된 종료 코드를 확인합니다.</summary>
	public class SessionLifetimeTest
	{
		private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

		private static async Task<RawRpcClient> LoginAsync(RealtimeTestServer server, string userId)
		{
			var client = await RawRpcClient.ConnectAsync(server.Uri);
			var (status, _) = await client.CallAsync<ITestRpc.LoginReq, ITestRpc.LoginRes>("ITestRpc/Login", new ITestRpc.LoginReq { UserId = userId });
			Assert.Equal(0, status);
			return client;
		}

		private static async Task WaitUntilAsync(Func<bool> condition)
		{
			for (var i = 0; i < 250 && !condition(); i++)
			{
				await Task.Delay(20);
			}

			Assert.True(condition(), "condition was not met in time");
		}

		[Fact]
		public async Task ClosedSession_DoesNotRunQueuedRequests_AndCleansUpWithoutWaitingForStuckCall()
		{
			await using var server = await RealtimeTestServer.StartAsync();
			var client = await LoginAsync(server, "user-1");

			await client.SendRequestAsync<object>("ITestRpc/Hold", null);
			await server.State.GateReached.Task.WaitAsync(Wait);
			await client.SendRequestAsync("ITestRpc/Notify", new ITestRpc.NotifyReq { Note = "must not run" });
			await client.CloseAsync(WebSocketCloseStatus.NormalClosure);
			await client.WaitForCloseAsync();

			// 토큰을 쓰지 않는 호출이 아직 붙잡혀 있어도 세션은 정리된다
			await WaitUntilAsync(() => server.Sessions.All.Count == 0);
			Assert.Null(server.Sessions.FindByUser("user-1"));

			server.State.Gate.TrySetResult();
			await WaitUntilAsync(() => server.State.PassedGate == 1);
			await Task.Delay(100);

			Assert.Empty(server.State.Notes);
			await client.DisposeAsync();
		}

		[Fact]
		public async Task ClosedSession_DropsQueuedRequestsWhileStuckCallKeepsRunning()
		{
			await using var server = await RealtimeTestServer.StartAsync();
			var client = await LoginAsync(server, "user-1");
			var session = (RealtimeServerSession)server.Sessions.FindByUser("user-1");

			await client.SendRequestAsync<object>("ITestRpc/Hold", null);
			await server.State.GateReached.Task.WaitAsync(Wait);
			for (var i = 0; i < 3; i++)
			{
				await client.SendRequestAsync("ITestRpc/Notify", new ITestRpc.NotifyReq { Note = new string('x', 64 * 1024) });
			}

			await WaitUntilAsync(() => session.QueuedRequests == 3);
			await client.CloseAsync(WebSocketCloseStatus.NormalClosure);
			await client.WaitForCloseAsync();
			await WaitUntilAsync(() => server.Sessions.All.Count == 0);

			Assert.Equal(0, session.QueuedRequests);
			server.State.Gate.TrySetResult();
			await WaitUntilAsync(() => server.State.PassedGate == 1);
			Assert.Empty(server.State.Notes);
			await client.DisposeAsync();
		}

		[Fact]
		public async Task CallBlockingBeforeReturningTask_DoesNotHoldSessionCleanup()
		{
			await using var server = await RealtimeTestServer.StartAsync();
			var client = await LoginAsync(server, "user-1");

			await client.SendRequestAsync<object>("ITestRpc/HoldBlocking", null);
			await server.State.GateReached.Task.WaitAsync(Wait);
			await client.CloseAsync(WebSocketCloseStatus.NormalClosure);
			await client.WaitForCloseAsync();

			try
			{
				await WaitUntilAsync(() => server.Sessions.All.Count == 0);
			}
			finally
			{
				server.State.BlockingGate.Set();
			}

			await WaitUntilAsync(() => server.State.PassedGate == 1);
			await client.DisposeAsync();
		}

		[Theory]
		[InlineData("throw")]
		[InlineData("block")]
		public async Task CallAbortedCallback_ThatThrowsOrBlocks_DoesNotStopClose(string mode)
		{
			await using var server = await RealtimeTestServer.StartAsync();
			var client = await LoginAsync(server, "user-1");
			var session = server.Sessions.FindByUser("user-1");

			await client.SendRequestAsync("ITestRpc/HoldWithCallback", new ITestRpc.CallbackReq { Mode = mode });
			await server.State.GateReached.Task.WaitAsync(Wait);

			try
			{
				// 동기로 막히는 회귀에서도 제한 시간이 걸리도록 호출 자체를 따로 시작한다
				await Task.Run(() => session.CloseAsync(RealtimeCloseCodes.Maintenance)).WaitAsync(Wait);
				Assert.Equal(RealtimeCloseCodes.Maintenance, (int)await client.WaitForCloseAsync());
				await WaitUntilAsync(() => server.Sessions.All.Count == 0);
				Assert.True(session.Closed.IsCancellationRequested);
			}
			finally
			{
				server.State.BlockingGate.Set();
				server.State.Gate.TrySetResult();
			}

			await client.DisposeAsync();
		}

		[Fact]
		public async Task LateSignInOnClosedSession_DoesNotKickNewSession()
		{
			await using var server = await RealtimeTestServer.StartAsync();
			var late = await RawRpcClient.ConnectAsync(server.Uri);

			await late.SendRequestAsync("ITestRpc/LoginLate", new ITestRpc.LoginReq { UserId = "user-1" });
			await server.State.GateReached.Task.WaitAsync(Wait);
			await late.CloseAsync(WebSocketCloseStatus.NormalClosure);
			await late.WaitForCloseAsync();
			await WaitUntilAsync(() => server.Sessions.All.Count == 0);

			await using var fresh = await RawRpcClient.ConnectAsync(server.Uri);
			var (status, login) = await fresh.CallAsync<ITestRpc.LoginReq, ITestRpc.LoginRes>("ITestRpc/Login", new ITestRpc.LoginReq { UserId = "user-1" });
			server.State.Gate.TrySetResult();
			await Task.Delay(200);
			var (echoStatus, _) = await fresh.CallAsync<ITestRpc.EchoReq, ITestRpc.EchoRes>("ITestRpc/Echo", new ITestRpc.EchoReq { Text = "still mine" });

			Assert.Equal(0, status);
			Assert.Equal(0, echoStatus);
			Assert.Equal(login.SessionId, server.Sessions.FindByUser("user-1")?.Id);
			Assert.Equal(0, server.State.PassedGate);
			await late.DisposeAsync();
		}

		[Fact]
		public async Task FullQueue_RejectsWithOverloadedAndStillAnswersPing()
		{
			await using var server = await RealtimeTestServer.StartAsync(options => options.MaxQueuedRequests = 1);
			await using var client = await LoginAsync(server, "user-1");

			var holdId = await client.SendRequestAsync<object>("ITestRpc/Hold", null);
			await server.State.GateReached.Task.WaitAsync(Wait);
			var queuedId = await client.SendRequestAsync("ITestRpc/Notify", new ITestRpc.NotifyReq { Note = "queued" });
			var rejectedId = await client.SendRequestAsync("ITestRpc/Notify", new ITestRpc.NotifyReq { Note = "rejected" });
			await client.SendFrameAsync(RpcFrame.Ping(9));

			var rejected = await client.ReceiveFrameAsync();
			var pong = await client.ReceiveFrameAsync();
			server.State.Gate.TrySetResult();
			var hold = await client.ReceiveFrameAsync();
			var queued = await client.ReceiveFrameAsync();

			Assert.Equal(rejectedId, rejected.Value.RequestId);
			Assert.Equal(RpcStatusCodes.Overloaded, rejected.Value.Status);
			Assert.Equal(RpcFrameKind.Pong, pong.Value.Kind);
			Assert.Equal((holdId, 0), (hold.Value.RequestId, hold.Value.Status));
			Assert.Equal((queuedId, 0), (queued.Value.RequestId, queued.Value.Status));
			Assert.Equal(new[] { "queued" }, server.State.Notes.ToArray());
		}

		[Fact]
		public async Task CloseWithUnsendableCode_ThrowsAndKeepsSessionUsable()
		{
			await using var server = await RealtimeTestServer.StartAsync();
			await using var client = await LoginAsync(server, "user-1");
			var session = server.Sessions.FindByUser("user-1");

			await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => session.CloseAsync(RealtimeCloseCodes.Abnormal));
			var (status, _) = await client.CallAsync<ITestRpc.EchoReq, ITestRpc.EchoRes>("ITestRpc/Echo", new ITestRpc.EchoReq { Text = "ok" });
			await session.CloseAsync(RealtimeCloseCodes.Maintenance);

			Assert.Equal(0, status);
			Assert.Equal(RealtimeCloseCodes.Maintenance, (int)await client.WaitForCloseAsync());
		}
	}
}
