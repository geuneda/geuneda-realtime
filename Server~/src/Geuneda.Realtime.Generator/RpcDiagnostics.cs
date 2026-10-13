using Microsoft.CodeAnalysis;

namespace Geuneda.Realtime.Generator
{
	/// <summary>
	/// 계약 규칙 위반 진단입니다. 모두 오류라 빌드가 멈추고, 오류가 있으면 코드를 만들지 않습니다.
	/// </summary>
	internal static class RpcDiagnostics
	{
		private const string Category = "Geuneda.Realtime";

		public static readonly DiagnosticDescriptor InvalidReturnType = Error("GRPC001",
			"RPC 메서드는 Task 또는 Task<T> 를 돌려줘야 합니다",
			"RPC 메서드 '{0}' 는 Task 또는 Task<T> 를 돌려줘야 합니다");

		public static readonly DiagnosticDescriptor InvalidParameters = Error("GRPC002",
			"RPC 메서드 인자가 잘못됐습니다",
			"RPC 메서드 '{0}' 의 인자는 요청 하나와 선택적인 끝 CancellationToken 하나여야 합니다(ref, out, params 불가)");

		public static readonly DiagnosticDescriptor GenericMethod = Error("GRPC003",
			"RPC 메서드는 제네릭일 수 없습니다",
			"RPC 메서드 '{0}' 는 제네릭일 수 없습니다");

		public static readonly DiagnosticDescriptor DuplicateMethodName = Error("GRPC004",
			"RPC 메서드 이름이 겹칩니다",
			"RPC 서비스 '{0}' 에 이름이 '{1}' 인 메서드가 여럿 있습니다(오버로드 불가)");

		public static readonly DiagnosticDescriptor GenericService = Error("GRPC005",
			"RPC 서비스는 제네릭일 수 없습니다",
			"RPC 서비스 '{0}' 는 제네릭일 수 없습니다");

		public static readonly DiagnosticDescriptor NestedService = Error("GRPC006",
			"RPC 서비스는 다른 형식 안에 둘 수 없습니다",
			"RPC 서비스 '{0}' 는 다른 형식 안에 둘 수 없습니다");

		public static readonly DiagnosticDescriptor InheritedService = Error("GRPC007",
			"RPC 서비스는 다른 인터페이스를 상속할 수 없습니다",
			"RPC 서비스 '{0}' 는 다른 인터페이스를 상속할 수 없습니다");

		public static readonly DiagnosticDescriptor InvalidMember = Error("GRPC008",
			"RPC 서비스에는 구현 없는 인스턴스 메서드만 둘 수 있습니다",
			"RPC 서비스 '{0}' 의 '{1}' 는 허용되지 않습니다(속성, 이벤트, static 멤버, 기본 구현 불가. 중첩 형식과 상수는 허용)");

		public static readonly DiagnosticDescriptor DuplicateServiceName = Error("GRPC009",
			"RPC 서비스 이름이 겹칩니다",
			"RPC 서비스 이름 '{0}' 가 여러 네임스페이스에 있습니다({1}). 경로 이름이 '인터페이스 이름/메서드 이름' 이라 겹치므로 이름을 바꾸세요");

		public static readonly DiagnosticDescriptor RouteIdCollision = Error("GRPC010",
			"RPC 경로 ID 가 겹칩니다",
			"RPC 경로 '{0}' 와 '{1}' 의 ID(FNV-1a {2})가 겹칩니다. 메서드 이름을 바꾸세요");

		public static readonly DiagnosticDescriptor MissingClientDependencies = Error("GRPC011",
			"클라이언트 프록시에 필요한 참조가 없습니다",
			"RPC 계약 '{0}' 의 클라이언트 프록시를 만들려면 이 어셈블리(asmdef)가 UniTask 와 Geuneda.Core 를 참조해야 합니다");

		public static readonly DiagnosticDescriptor ReservedMemberName = Error("GRPC012",
			"RPC 메서드 이름이 생성 코드의 이름과 겹칩니다",
			"RPC 메서드 '{0}' 의 이름이 생성 코드의 이름 '{1}' 과 겹칩니다. 메서드 이름을 바꾸세요");

		public static readonly DiagnosticDescriptor GeneratedTypeCollision = Error("GRPC013",
			"생성할 형식 이름이 겹칩니다",
			"RPC 서비스 '{0}' 와 '{1}' 가 같은 네임스페이스에 같은 생성 형식 이름('{2}')을 만듭니다. 인터페이스 이름을 바꾸세요");

		public static readonly DiagnosticDescriptor ExistingGeneratedType = Error("GRPC014",
			"생성할 형식이 이미 있습니다",
			"생성할 형식 '{0}' 이 이 어셈블리에 이미 있습니다. 그 형식이나 RPC 서비스의 이름을 바꾸세요");

		private static DiagnosticDescriptor Error(string id, string title, string message)
		{
			return new DiagnosticDescriptor(id, title, message, Category, DiagnosticSeverity.Error, true);
		}
	}
}
