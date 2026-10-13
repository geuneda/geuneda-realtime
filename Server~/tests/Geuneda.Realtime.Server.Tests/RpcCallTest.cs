using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MessagePack;
using MessagePack.Resolvers;
using Xunit;

namespace Geuneda.Realtime.Server.Tests
{
	/// <summary>실제 Kestrel 서버와 프레임 클라이언트로 RPC 처리 규칙을 확인합니다.</summary>
	public class RpcCallTest
	{
		private const string Login = "ITestRpc/Login";
		private const string Echo = "ITestRpc/Echo";

		private static async Task<RawRpcClient> LoginAsync(RealtimeTestServer server, string userId, IRpcSerializer serializer = null)
		{
			var client = await RawRpcClient.ConnectAsync(server.Uri, serializer);
			var (status, _) = await client.CallAsync<ITestRpc.LoginReq, ITestRpc.LoginRes>(Login, new ITestRpc.LoginReq { UserId = userId });
			Assert.Equal(RpcStatusCodes.Success, status);
			return client;
		}

		[Fact]
		public async Task Login_ThenEcho_SeesUserOfSession()
		{
			await using var server = await RealtimeTestServer.StartAsync();
			await using var client = await LoginAsync(server, "user-1");

			var (status, body) = await client.CallAsync<ITestRpc.EchoReq, ITestRpc.EchoRes>(Echo, new ITestRpc.EchoReq { Text = "hi" });

			Assert.Equal(0, status);
			Assert.Equal("hi", body.Text);
			Assert.Equal("user-1", body.UserId);
			Assert.Equal("geuneda.rpc.v1.json", client.SubProtocol);
			Assert.NotNull(server.Sessions.FindByUser("user-1"));
		}

		[Fact]
		public async Task CallBeforeLogin_IsUnauthenticated()
		{
			await using var server = await RealtimeTestServer.StartAsync();
			await using var client = await RawRpcClient.ConnectAsync(server.Uri);

			var (status, _) = await client.CallAsync<ITestRpc.EchoReq, ITestRpc.EchoRes>(Echo, new ITestRpc.EchoReq { Text = "hi" });

			Assert.Equal(RpcStatusCodes.Unauthenticated, status);
		}

		[Fact]
		public async Task UnknownMethod_IsMethodNotFound()
		{
			await using var server = await RealtimeTestServer.StartAsync();
			await using var client = await LoginAsync(server, "user-1");

			var (status, _) = await client.CallAsync<ITestRpc.EchoReq, ITestRpc.EchoRes>("IUnknownRpc/Nope", new ITestRpc.EchoReq());

			Assert.Equal(RpcStatusCodes.MethodNotFound, status);
		}

		[Fact]
		public async Task UnreadableRequest_IsBadRequest()
		{
			await using var server = await RealtimeTestServer.StartAsync();
			await using var client = await LoginAsync(server, "user-1");

			await client.SendFrameAsync(RpcFrame.Request(99, Fnv1a32.Hash(Echo), new ArraySegment<byte>(Encoding.UTF8.GetBytes("{broken"))));
			var response = await client.ReceiveResponseAsync(99);

			Assert.Equal(RpcStatusCodes.BadRequest, response.Status);
		}

		[Fact]
		public async Task StatusException_BecomesResponseStatus()
		{
			await using var server = await RealtimeTestServer.StartAsync();
			await using var client = await LoginAsync(server, "user-1");

			var (status, _) = await client.CallAsync<ITestRpc.FailReq, ITestRpc.EchoRes>("ITestRpc/Fail", new ITestRpc.FailReq { Status = 101005 });

			Assert.Equal(101005, status);
		}

		[Fact]
		public async Task UnexpectedException_IsInternalErrorAndSessionStaysOpen()
		{
			await using var server = await RealtimeTestServer.StartAsync();
			await using var client = await LoginAsync(server, "user-1");

			var (crashed, _) = await client.CallAsync<object, ITestRpc.EchoRes>("ITestRpc/Crash", null);
			var (echoed, body) = await client.CallAsync<ITestRpc.EchoReq, ITestRpc.EchoRes>(Echo, new ITestRpc.EchoReq { Text = "still here" });

			Assert.Equal(RpcStatusCodes.InternalError, crashed);
			Assert.Equal(0, echoed);
			Assert.Equal("still here", body.Text);
		}

		[Fact]
		public async Task NoResponseAndNoArgumentMethods_Work()
		{
			await using var server = await RealtimeTestServer.StartAsync();
			await using var client = await LoginAsync(server, "user-1");

			var notifyId = await client.SendRequestAsync("ITestRpc/Notify", new ITestRpc.NotifyReq { Note = "remember" });
			var notify = await client.ReceiveResponseAsync(notifyId);
			var (clockStatus, clock) = await client.CallAsync<object, ITestRpc.ClockRes>("ITestRpc/Clock", null);

			Assert.Equal(0, notify.Status);
			Assert.Equal(0, notify.Payload.Count);
			Assert.Equal(new[] { "remember" }, server.State.Notes.ToArray());
			Assert.Equal(0, clockStatus);
			Assert.Equal(42, clock.Ticks);
		}

		[Fact]
		public async Task Requests_RunInOrderAndPingIsAnsweredMeanwhile()
		{
			await using var server = await RealtimeTestServer.StartAsync();
			await using var client = await LoginAsync(server, "user-1");

			var slowId = await client.SendRequestAsync("ITestRpc/Slow", new ITestRpc.SlowReq { Text = "first", DelayMilliseconds = 300 });
			var echoId = await client.SendRequestAsync(Echo, new ITestRpc.EchoReq { Text = "second" });
			await client.SendFrameAsync(RpcFrame.Ping(7));

			var pong = await client.ReceiveFrameAsync();
			var first = await client.ReceiveFrameAsync();
			var second = await client.ReceiveFrameAsync();

			Assert.Equal(RpcFrameKind.Pong, pong.Value.Kind);
			Assert.Equal(7u, pong.Value.Sequence);
			Assert.Equal(slowId, first.Value.RequestId);
			Assert.Equal(echoId, second.Value.RequestId);
			Assert.Equal("first", client.Read<ITestRpc.EchoRes>(first.Value).Text);
		}

		[Fact]
		public async Task Push_FromHandler_ReachesSessionBeforeResponse()
		{
			await using var server = await RealtimeTestServer.StartAsync();
			await using var client = await LoginAsync(server, "user-1");

			var (status, _) = await client.CallAsync<ITestRpc.EchoReq, object>("ITestRpc/PushMe", new ITestRpc.EchoReq { Text = "pushed" });
			var push = Assert.Single(client.Pushes);

			Assert.Equal(0, status);
			Assert.Equal(RpcTopic.Of(ITestRpc.PushTopic).Id, push.TopicId);
			Assert.Equal("pushed", client.Read<ITestRpc.EchoRes>(push).Text);
		}

		[Fact]
		public async Task PushFromOutside_UsesSessionRegistry()
		{
			await using var server = await RealtimeTestServer.StartAsync();
			await using var client = await LoginAsync(server, "user-1");

			var sent = await server.Sessions.FindByUser("user-1").PushAsync(RpcTopic.Of("Mail/Arrived"), new ITestRpc.EchoReq { Text = "mail" });
			var push = await client.ReceiveFrameAsync();

			Assert.True(sent);
			Assert.Equal(RpcFrameKind.Push, push.Value.Kind);
			Assert.Equal("mail", client.Read<ITestRpc.EchoReq>(push.Value).Text);
		}

		[Fact]
		public async Task DuplicateLogin_ClosesPreviousSessionWith4001()
		{
			await using var server = await RealtimeTestServer.StartAsync();
			await using var first = await LoginAsync(server, "user-1");
			await using var second = await LoginAsync(server, "user-1");

			var closeStatus = await first.WaitForCloseAsync();
			var (status, _) = await second.CallAsync<ITestRpc.EchoReq, ITestRpc.EchoRes>(Echo, new ITestRpc.EchoReq { Text = "ok" });

			Assert.Equal(RealtimeCloseCodes.DuplicateLogin, (int)closeStatus);
			Assert.Equal(0, status);
		}

		[Fact]
		public async Task MessagePackSubProtocol_IsNegotiated()
		{
			var serializer = new MessagePackRpcSerializer(MessagePackSerializerOptions.Standard
				.WithResolver(ContractlessStandardResolver.Instance)
				.WithSecurity(MessagePackSecurity.UntrustedData));
			await using var server = await RealtimeTestServer.StartAsync(options => options.Serializers.Add(serializer));
			await using var client = await LoginAsync(server, "user-1", serializer);

			var (status, body) = await client.CallAsync<ITestRpc.EchoReq, ITestRpc.EchoRes>(Echo, new ITestRpc.EchoReq { Text = "packed" });

			Assert.Equal("geuneda.rpc.v1.msgpack", client.SubProtocol);
			Assert.Equal(0, status);
			Assert.Equal("packed", body.Text);
		}
	}
}
