using System.Linq;

namespace Geuneda.Realtime.Generator
{
	/// <summary>
	/// <c>[RpcService]</c> 인터페이스 하나입니다. 메서드와, 만들면서 찾은 진단을 담습니다.
	/// </summary>
	/// <param name="Namespace">생성 코드에 쓰는 네임스페이스(예약어는 <c>@</c>, 전역이면 null)입니다.</param>
	/// <param name="MetadataNamespace">메타데이터 이름의 네임스페이스(전역이면 빈 문자열)입니다.</param>
	internal sealed record ServiceModel(
		string Namespace,
		string MetadataNamespace,
		string InterfaceName,
		string FullyQualifiedName,
		bool IsPublic,
		LocationInfo Location,
		EquatableArray<MethodModel> Methods,
		EquatableArray<DiagnosticInfo> Diagnostics)
	{
		/// <summary>앞의 <c>I</c> 를 뗀 이름입니다(<c>IShopRpc</c> → <c>ShopRpc</c>).</summary>
		public string BaseName => InterfaceName.Length > 1 && InterfaceName[0] == 'I' && char.IsUpper(InterfaceName[1])
			? InterfaceName.Substring(1)
			: InterfaceName;

		public string ClientName => BaseName + "Client";
		public string ClientInterfaceName => "I" + ClientName;
		public string MethodsClassName => BaseName + "Methods";
		public string Accessibility => IsPublic ? "public" : "internal";

		/// <summary>생성할 클라이언트 형식의 메타데이터 이름입니다(이미 있는 형식과 겹치는지 볼 때 씀).</summary>
		public string MetadataNameOf(string typeName) => MetadataNamespace.Length == 0 ? typeName : MetadataNamespace + "." + typeName;
		public bool HasErrors => Diagnostics.Any(diagnostic => diagnostic.IsError);
	}
}
