using Xunit;

namespace Geuneda.Realtime.Generator.Tests
{
	/// <summary>계약 규칙 위반이 오류 진단이 되고 코드를 만들지 않는지 확인합니다.</summary>
	public class DiagnosticTest
	{
		private static GeneratorRun Run(string members, string extra = "")
		{
			var contract = @"
using System.Threading;
using System.Threading.Tasks;
using Geuneda.Realtime;
namespace Game
{
	[RpcService]
	public interface IBadRpc
	{
" + members + @"
	}
" + extra + @"
}";
			return GeneratorHarness.Run("Contracts", false, GeneratorHarness.ClientRuntimeStub, contract);
		}

		[Theory]
		[InlineData("int Sync(int request);", "GRPC001")]
		[InlineData("ValueTask<int> Value(int request);", "GRPC001")]
		[InlineData("Task<int> Two(int a, int b);", "GRPC002")]
		[InlineData("Task<int> ByRef(ref int request);", "GRPC002")]
		[InlineData("Task<int> TokenFirst(CancellationToken a, CancellationToken b);", "GRPC002")]
		[InlineData("Task<T> Generic<T>(int request);", "GRPC003")]
		[InlineData("Task<int> Same(int request); Task<int> Same(string request);", "GRPC004")]
		[InlineData("int Count { get; }", "GRPC008")]
		[InlineData("static Task<int> Make(int request) => Task.FromResult(request);", "GRPC008")]
		[InlineData("Task<int> Body(int request) => Task.FromResult(request);", "GRPC008")]
		public void InvalidMember_ReportsErrorAndGeneratesNothing(string members, string expectedId)
		{
			var run = Run(members);

			Assert.Contains(expectedId, run.DiagnosticIds);
			Assert.Empty(run.Generated);
		}

		[Fact]
		public void GenericService_IsGRPC005()
		{
			var run = GeneratorHarness.Run("Contracts", false, GeneratorHarness.ClientRuntimeStub, @"
using System.Threading.Tasks;
using Geuneda.Realtime;
[RpcService] public interface IGenericRpc<T> { Task<int> Get(int request); }");

			Assert.Contains("GRPC005", run.DiagnosticIds);
		}

		[Fact]
		public void NestedService_IsGRPC006()
		{
			var run = GeneratorHarness.Run("Contracts", false, GeneratorHarness.ClientRuntimeStub, @"
using System.Threading.Tasks;
using Geuneda.Realtime;
public static class Outer { [RpcService] public interface IInnerRpc { Task<int> Get(int request); } }");

			Assert.Contains("GRPC006", run.DiagnosticIds);
		}

		[Fact]
		public void InheritedService_IsGRPC007()
		{
			var run = GeneratorHarness.Run("Contracts", false, GeneratorHarness.ClientRuntimeStub, @"
using System.Threading.Tasks;
using Geuneda.Realtime;
public interface IBase { }
[RpcService] public interface IChildRpc : IBase { Task<int> Get(int request); }");

			Assert.Contains("GRPC007", run.DiagnosticIds);
		}

		[Fact]
		public void SameServiceNameInTwoNamespaces_IsGRPC009()
		{
			var run = GeneratorHarness.Run("Contracts", false, GeneratorHarness.ClientRuntimeStub, @"
using System.Threading.Tasks;
using Geuneda.Realtime;
namespace A { [RpcService] public interface IShopRpc { Task<int> Get(int request); } }
namespace B { [RpcService] public interface IShopRpc { Task<int> Put(int request); } }");

			Assert.Contains("GRPC009", run.DiagnosticIds);
			Assert.Empty(run.Generated);
		}

		[Fact]
		public void RouteIdCollision_IsGRPC010()
		{
			var (first, second) = FindCollision();
			var run = GeneratorHarness.Run("Contracts", false, GeneratorHarness.ClientRuntimeStub, $@"
using System.Threading.Tasks;
using Geuneda.Realtime;
[RpcService] public interface I {{ Task<int> {first}(int request); Task<int> {second}(int request); }}");

			Assert.Contains("GRPC010", run.DiagnosticIds);
			Assert.Empty(run.Generated);
		}

		[Fact]
		public void ClientRuntimeWithoutUniTaskOrCore_IsGRPC011()
		{
			var run = GeneratorHarness.Run("Contracts", false, GeneratorHarness.ClientRuntimeWithoutDependenciesStub, @"
using System.Threading.Tasks;
using Geuneda.Realtime;
[RpcService] public interface IShopRpc { Task<int> Get(int request); }");

			Assert.Contains("GRPC011", run.DiagnosticIds);
			Assert.Empty(run.Generated);
		}

		[Theory]
		[InlineData("_caller")]
		[InlineData("ShopRpcClient")]
		[InlineData("IShopRpcClient")]
		[InlineData("ShopRpcMethods")]
		public void MethodNamedLikeGeneratedMember_IsGRPC012(string methodName)
		{
			var run = GeneratorHarness.Run("Contracts", false, GeneratorHarness.ClientRuntimeStub, $@"
using System.Threading.Tasks;
using Geuneda.Realtime;
[RpcService] public interface IShopRpc {{ Task<int> {methodName}(int request); }}");

			Assert.Contains("GRPC012", run.DiagnosticIds);
			Assert.Empty(run.Generated);
		}

		[Fact]
		public void TwoServicesWithSameGeneratedNames_IsGRPC013()
		{
			var run = GeneratorHarness.Run("Contracts", false, GeneratorHarness.ClientRuntimeStub, @"
using System.Threading.Tasks;
using Geuneda.Realtime;
namespace Game
{
	[RpcService] public interface IShopRpc { Task<int> Get(int request); }
	[RpcService] public interface ShopRpc { Task<int> Put(int request); }
}");

			Assert.Contains("GRPC013", run.DiagnosticIds);
			Assert.Empty(run.Generated);
		}

		[Fact]
		public void InterfaceAndClassOfDifferentServicesWithSameName_IsGRPC013()
		{
			// IShopRpc 의 인터페이스 IShopRpcClient 와 IIShopRpc 의 클래스 IShopRpcClient 가 겹친다
			var run = GeneratorHarness.Run("Contracts", false, GeneratorHarness.ClientRuntimeStub, @"
using System.Threading.Tasks;
using Geuneda.Realtime;
namespace Game
{
	[RpcService] public interface IShopRpc { Task<int> Get(int request); }
	[RpcService] public interface IIShopRpc { Task<int> Put(int request); }
}");

			Assert.Contains("GRPC013", run.DiagnosticIds);
			Assert.Empty(run.Generated);
		}

		[Fact]
		public void ExistingClientTypeName_IsGRPC014()
		{
			var run = GeneratorHarness.Run("Contracts", false, GeneratorHarness.ClientRuntimeStub, @"
using System.Threading.Tasks;
using Geuneda.Realtime;
namespace Game
{
	[RpcService] public interface IShopRpc { Task<int> Get(int request); }
	public sealed class ShopRpcClient { }
}");

			Assert.Contains("GRPC014", run.DiagnosticIds);
			Assert.Empty(run.Generated);
		}

		[Fact]
		public void ExistingRouteTableName_IsGRPC014()
		{
			var run = GeneratorHarness.Run("Game.Contracts", true, @"
using System.Threading.Tasks;
using Geuneda.Realtime;
namespace Game.Contracts
{
	[RpcService] public interface IShopRpc { Task<int> Get(int request); }
	public static class GeneratedRpcRoutes { }
}");

			Assert.Contains("GRPC014", run.DiagnosticIds);
			Assert.Empty(run.Generated);
		}

		/// <summary>경로 "I/메서드" 의 FNV-1a 32비트 충돌 쌍을 찾습니다(생일 문제로 수십만 개 안에서 나옴).</summary>
		private static (string First, string Second) FindCollision()
		{
			var seen = new System.Collections.Generic.Dictionary<uint, string>();
			for (var i = 0; ; i++)
			{
				var name = "M" + i;
				var id = Fnv1a32.Hash("I/" + name);
				if (seen.TryGetValue(id, out var other))
				{
					return (other, name);
				}

				seen.Add(id, name);
			}
		}
	}
}
