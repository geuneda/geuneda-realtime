using Geuneda.Realtime;
using Geuneda.Realtime.SampleServer;
using Geuneda.Realtime.Server;
using Geuneda.Realtime.Tests.Interop;
using MessagePack;
using MessagePack.Resolvers;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

// 실행: dotnet run --project Server~/samples/Geuneda.Realtime.SampleServer -- --urls http://127.0.0.1:5199
// Unity 상호 운용 테스트: GEUNEDA_REALTIME_INTEROP_URL=ws://127.0.0.1:5199/rpc
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<SampleMemory>();
builder.Services.AddScoped<ISampleRpc, SampleRpc>();
builder.Services.AddRealtimeRpc(options =>
{
	options.AddRoutes(GeneratedRpcRoutes.All);
	// 네트워크 입력이므로 UntrustedData 보안을 켠다
	options.Serializers.Add(new MessagePackRpcSerializer(MessagePackSerializerOptions.Standard
		.WithResolver(ContractlessStandardResolver.Instance)
		.WithSecurity(MessagePackSecurity.UntrustedData)));
});

var app = builder.Build();
app.UseWebSockets();
app.MapRealtimeRpc("/rpc");
app.Run();
