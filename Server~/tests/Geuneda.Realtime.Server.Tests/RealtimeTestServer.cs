using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geuneda.Realtime.Server.Tests
{
	/// <summary>127.0.0.1 빈 포트에 띄운 Kestrel 실시간 RPC 서버입니다.</summary>
	internal sealed class RealtimeTestServer : IAsyncDisposable
	{
		private readonly WebApplication _app;

		private RealtimeTestServer(WebApplication app, Uri uri)
		{
			_app = app;
			Uri = uri;
		}

		public Uri Uri { get; }
		public TestState State => _app.Services.GetRequiredService<TestState>();
		public IRealtimeSessions Sessions => _app.Services.GetRequiredService<IRealtimeSessions>();
		public IServiceProvider Services => _app.Services;

		public static async Task<RealtimeTestServer> StartAsync(Action<RealtimeServerOptions> configure = null)
		{
			var app = Build(services =>
			{
				services.AddScoped<ITestRpc, TestRpc>();
				services.AddRealtimeRpc(options =>
				{
					options.AddRoutes(GeneratedRpcRoutes.All);
					configure?.Invoke(options);
				});
			});

			app.UseWebSockets();
			app.MapRealtimeRpc("/rpc");
			await app.StartAsync();

			var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.First();
			return new RealtimeTestServer(app, new Uri(address.Replace("http://", "ws://") + "/rpc"));
		}

		public static WebApplication Build(Action<IServiceCollection> configureServices)
		{
			var builder = WebApplication.CreateSlimBuilder();
			builder.WebHost.UseUrls("http://127.0.0.1:0");
			builder.Logging.ClearProviders();
			builder.Services.AddSingleton<TestState>();
			configureServices(builder.Services);
			return builder.Build();
		}

		public async ValueTask DisposeAsync()
		{
			await _app.StopAsync();
			await _app.DisposeAsync();
		}
	}
}
