namespace Geuneda.Realtime.Generator
{
	/// <summary>
	/// 계약 메서드 하나입니다. 요청·응답 형식은 <c>global::</c> 로 시작하는 전체 이름이고, 없으면 null 입니다.
	/// </summary>
	internal sealed record MethodModel(
		string Name,
		string RouteName,
		uint RouteId,
		string RequestType,
		string ResponseType,
		bool HasCancellationToken,
		bool AllowAnonymous,
		LocationInfo Location)
	{
		/// <summary>생성 코드에 쓰는 이름입니다(예약어는 <c>@</c>).</summary>
		public string Identifier => Identifiers.Escape(Name);
	}
}
