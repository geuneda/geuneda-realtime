using System.Linq;
using Xunit;

namespace Geuneda.Realtime.Generator.Tests
{
	/// <summary>올바른 계약에서 클라이언트·서버 코드가 만들어지고 컴파일되는지 확인합니다.</summary>
	public class GenerationTest
	{
		private const string ShopContract = @"
using System.Threading;
using System.Threading.Tasks;
using Geuneda.Realtime;

namespace Game.Contracts
{
	[RpcService]
	public interface IShopRpc
	{
		[RpcAllowAnonymous]
		Task<BuyRes> BuyProduct(BuyReq request);
		Task Notify(BuyReq request);
		Task<BuyRes> Peek();
		Task Ping();
		Task<BuyRes> Cancellable(BuyReq request, CancellationToken cancellationToken);
		Task<BuyRes> CancellableOnly(CancellationToken cancellationToken);

		public const string Topic = ""Shop/Changed"";

		public sealed class BuyReq { public int ProductId; }
		public sealed class BuyRes { public int Gold; }
	}
}";

		[Fact]
		public void ClientRuntime_GeneratesCompilingProxy()
		{
			var run = GeneratorHarness.Run("Game.Contracts", false, GeneratorHarness.ClientRuntimeStub, ShopContract);

			Assert.Empty(run.Diagnostics);
			Assert.Empty(run.CompileErrors);
			var client = Assert.Single(run.Generated).Value;
			Assert.Contains("public static class ShopRpcMethods", client);
			Assert.Contains("public interface IShopRpcClient", client);
			Assert.Contains("public sealed class ShopRpcClient : IShopRpcClient", client);
			Assert.Contains("global::Geuneda.Realtime.RpcMethod.Of(\"IShopRpc/BuyProduct\")", client);
			Assert.Contains("_caller.CallAsync<global::Game.Contracts.IShopRpc.BuyReq, global::Game.Contracts.IShopRpc.BuyRes>(ShopRpcMethods.BuyProduct, request, cancellationToken)", client);
			Assert.Contains("_caller.CallAsync<global::Game.Contracts.IShopRpc.BuyReq>(ShopRpcMethods.Notify, request, cancellationToken)", client);
			Assert.Contains("_caller.CallAsync<object, global::Game.Contracts.IShopRpc.BuyRes>(ShopRpcMethods.Peek, null, cancellationToken)", client);
			Assert.Contains("_caller.CallAsync<object>(ShopRpcMethods.Ping, null, cancellationToken)", client);
		}

		[Fact]
		public void ServerRuntime_GeneratesCompilingRouteTable()
		{
			var run = GeneratorHarness.Run("Game.Contracts", true, ShopContract);

			Assert.Empty(run.Diagnostics);
			Assert.Empty(run.CompileErrors);
			var routes = Assert.Single(run.Generated);
			Assert.Equal("GeneratedRpcRoutes.g.cs", routes.Key);
			Assert.Contains("namespace Game.Contracts", routes.Value);
			Assert.Contains($"\"IShopRpc/BuyProduct\", {Fnv1a32.Hash("IShopRpc/BuyProduct")}u, true,", routes.Value);
			Assert.Contains($"\"IShopRpc/Notify\", {Fnv1a32.Hash("IShopRpc/Notify")}u, false,", routes.Value);
			Assert.Contains("RpcRoute.Create<global::Game.Contracts.IShopRpc, global::Geuneda.Realtime.Server.RpcEmpty, global::Game.Contracts.IShopRpc.BuyRes>", routes.Value);
			Assert.Contains("service.Cancellable(request, cancellationToken)", routes.Value);
			Assert.Contains("service.CancellableOnly(cancellationToken)", routes.Value);
		}

		[Fact]
		public void NoRuntime_GeneratesNothing()
		{
			var run = GeneratorHarness.Run("Game.Contracts", false, GeneratorHarness.AttributesOnlyStub, ShopContract);

			Assert.Empty(run.Diagnostics);
			Assert.Empty(run.Generated);
			Assert.Empty(run.CompileErrors);
		}

		[Fact]
		public void PartialInterface_GeneratesOneClientWithAllMethods()
		{
			const string contract = @"
using System.Threading.Tasks;
using Geuneda.Realtime;
[RpcService] public partial interface IPartRpc { Task<int> First(int request); }
public partial interface IPartRpc { Task<int> Second(int request); }";

			var run = GeneratorHarness.Run("Contracts", false, GeneratorHarness.ClientRuntimeStub, contract);

			Assert.Empty(run.CompileErrors);
			var client = Assert.Single(run.Generated).Value;
			Assert.Contains("First(int request", client);
			Assert.Contains("Second(int request", client);
			Assert.DoesNotContain("namespace", client);
		}

		[Fact]
		public void AttributeOnLaterPartialDeclaration_IsStillFound()
		{
			const string first = @"
using System.Threading.Tasks;
public partial interface IPartRpc { Task<int> First(int request); }";
			const string second = @"
using System.Threading.Tasks;
using Geuneda.Realtime;
[RpcService] public partial interface IPartRpc { Task<int> Second(int request); }";

			const string sameFileSource = @"
using System.Threading.Tasks;
using Geuneda.Realtime;
public partial interface IPartRpc { Task<int> First(int request); }
[RpcService] public partial interface IPartRpc { Task<int> Second(int request); }";

			var sameFile = GeneratorHarness.Run("Contracts", false, GeneratorHarness.ClientRuntimeStub, sameFileSource);
			var splitFiles = GeneratorHarness.Run("Contracts", true, first, second);

			Assert.Empty(sameFile.CompileErrors);
			var client = Assert.Single(sameFile.Generated).Value;
			Assert.Contains("First(int request", client);
			Assert.Contains("Second(int request", client);
			Assert.Empty(splitFiles.CompileErrors);
			Assert.Contains("\"IPartRpc/First\"", Assert.Single(splitFiles.Generated).Value);
		}

		[Fact]
		public void KeywordNames_AreEscapedInClientAndServerCode()
		{
			const string contract = @"
using System.Threading.Tasks;
using Geuneda.Realtime;
namespace @class.Contracts { [RpcService] public interface IKeywordRpc { Task<int> @event(int request); Task @class(); } }";

			var client = GeneratorHarness.Run("Contracts", false, GeneratorHarness.ClientRuntimeStub, contract);
			var server = GeneratorHarness.Run("Contracts", true, contract);

			Assert.Empty(client.CompileErrors);
			Assert.Empty(server.CompileErrors);
			Assert.Contains("namespace @class.Contracts", client.Generated.Values.Single());
			Assert.Contains("KeywordRpcMethods.@event", client.Generated.Values.Single());
			Assert.Contains("\"IKeywordRpc/event\"", server.Generated.Values.Single());
			Assert.Contains("service.@event(request)", server.Generated.Values.Single());
		}

		[Fact]
		public void InternalInterface_GeneratesInternalClient()
		{
			const string contract = @"
using System.Threading.Tasks;
using Geuneda.Realtime;
namespace Game { [RpcService] internal interface IHiddenRpc { Task<int> Get(int request); } }";

			var run = GeneratorHarness.Run("Contracts", false, GeneratorHarness.ClientRuntimeStub, contract);

			Assert.Empty(run.CompileErrors);
			var client = run.Generated.Values.Single();
			Assert.Contains("internal sealed class HiddenRpcClient", client);
			Assert.Contains("internal interface IHiddenRpcClient", client);
		}

		[Theory]
		[InlineData("Game.Contracts", "Game.Contracts")]
		[InlineData("1Game-Contracts", "_1Game_Contracts")]
		[InlineData("Game.class", "Game.@class")]
		public void RouteNamespace_IsSanitizedAssemblyName(string assemblyName, string expected)
		{
			Assert.Equal(expected, RouteEmitter.NamespaceOf(assemblyName));
		}
	}
}
