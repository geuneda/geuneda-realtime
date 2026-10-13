using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Geuneda.Realtime.Server.Tests
{
	/// <summary>시작할 때 잡아야 하는 설정 오류와 공개 API 의 오용 검사를 확인합니다.</summary>
	public class StartupTest
	{
		[Fact]
		public async Task MissingServiceImplementation_FailsAtMap()
		{
			await using var app = RealtimeTestServer.Build(services => services.AddRealtimeRpc(options => options.AddRoutes(GeneratedRpcRoutes.All)));

			var exception = Assert.Throws<InvalidOperationException>(() => app.MapRealtimeRpc("/rpc"));

			Assert.Contains(typeof(ITestRpc).FullName, exception.Message);
		}

		[Fact]
		public async Task DuplicateRoutes_FailAtMap()
		{
			await using var app = RealtimeTestServer.Build(services =>
			{
				services.AddScoped<ITestRpc, TestRpc>();
				services.AddRealtimeRpc(options => options.AddRoutes(GeneratedRpcRoutes.All).AddRoutes(GeneratedRpcRoutes.All));
			});

			Assert.Throws<InvalidOperationException>(() => app.MapRealtimeRpc("/rpc"));
		}

		[Fact]
		public async Task DuplicateSerializerNames_FailAtMap()
		{
			await using var app = RealtimeTestServer.Build(services =>
			{
				services.AddScoped<ITestRpc, TestRpc>();
				services.AddRealtimeRpc(options => options.Serializers.Add(new JsonRpcSerializer()));
			});

			Assert.Throws<InvalidOperationException>(() => app.MapRealtimeRpc("/rpc"));
		}

		[Fact]
		public void RouteCreate_RejectsIdThatIsNotTheNameHash()
		{
			Assert.Throws<ArgumentException>(() => RpcRoute.Create<ITestRpc, ITestRpc.EchoReq, ITestRpc.EchoRes>(
				"ITestRpc/Echo", 1u, false, (service, request, cancellationToken) => service.Echo(request)));
		}

		[Fact]
		public void StatusException_RejectsCodesBelowServerRange()
		{
			Assert.Throws<ArgumentOutOfRangeException>(() => new RpcStatusException(4));
			Assert.Equal(101000, new RpcStatusException(RpcStatusCodes.GameRangeStart).Status);
		}

		[Fact]
		public async Task RpcContext_OutsideCall_Throws()
		{
			await using var app = RealtimeTestServer.Build(services => services.AddRealtimeRpc(options => { }));
			using var scope = app.Services.CreateScope();

			var context = scope.ServiceProvider.GetRequiredService<IRpcContext>();

			Assert.Throws<InvalidOperationException>(() => context.Session);
		}

		[Fact]
		public void GeneratedRoutes_MatchContractNamesAndHashes()
		{
			Assert.Equal(12, GeneratedRpcRoutes.All.Count);

			foreach (var route in GeneratedRpcRoutes.All)
			{
				Assert.StartsWith("ITestRpc/", route.Name);
				Assert.Equal(Fnv1a32.Hash(route.Name), route.Id);
				Assert.Equal(typeof(ITestRpc), route.ServiceType);
				Assert.Equal(route.Name == "ITestRpc/Login" || route.Name == "ITestRpc/LoginLate", route.AllowAnonymous);
			}
		}

		[Fact]
		public void SharedCodec_MatchesUnityByteVector()
		{
			// Unity ProtocolTest.Request_EncodesBigEndianHeaderThenPayload 와 같은 벡터
			var bytes = RpcFrameCodec.Encode(RpcFrame.Request(0x01020304, 0xA0B0C0D0, new ArraySegment<byte>(new byte[] { 9, 8 })));

			Assert.Equal(new byte[] { 1, 0, 1, 2, 3, 4, 0xA0, 0xB0, 0xC0, 0xD0, 9, 8 }, bytes);
			Assert.Equal(0xBF9CF968u, Fnv1a32.Hash("foobar"));
		}
	}
}
