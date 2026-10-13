using System;

// ReSharper disable once CheckNamespace

namespace Geuneda.Realtime
{
	/// <summary>
	/// RPC 계약 인터페이스 표시입니다. 소스 생성기가 이 인터페이스로 클라이언트 프록시(<c>IShopRpc</c> → <c>ShopRpcClient</c>)와
	/// 서버 라우트 표(<c>GeneratedRpcRoutes</c>)를 만듭니다.
	/// </summary>
	/// <remarks>
	/// <para>
	/// 메서드 모양은 <c>Task&lt;TResponse&gt; M(TRequest request)</c>, 응답 본문이 없는 <c>Task M(TRequest request)</c>, 인자 없는 <c>Task&lt;TResponse&gt; M()</c> 입니다.
	/// 서버 구현용으로 끝에 <see cref="System.Threading.CancellationToken"/> 하나를 더 받을 수 있습니다. 다른 모양, 오버로드, 제네릭, 속성은 생성기 오류입니다.
	/// </para>
	/// <para>
	/// 경로 이름은 <c>"인터페이스 이름/메서드 이름"</c>(예: <c>"IShopRpc/BuyProduct"</c>)이고 ID 는 그 FNV-1a 해시입니다. 이름이나 ID 가 겹치면 생성기 오류입니다.
	/// 서버가 같은 파일을 컴파일하므로 계약 어셈블리는 Unity 형식을 쓰지 않습니다(asmdef <c>noEngineReferences</c>).
	/// </para>
	/// </remarks>
	[AttributeUsage(AttributeTargets.Interface, Inherited = false)]
	public sealed class RpcServiceAttribute : Attribute
	{
	}
}
