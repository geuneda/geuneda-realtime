// ReSharper disable once CheckNamespace

namespace Geuneda.Realtime
{
	/// <summary>
	/// 응답 상태 코드입니다. 서버 프레임워크(<c>Geuneda.Realtime.Server</c>)가 게임 범위 앞쪽 100000~100999 를 쓰고, 게임 오류는 101000 부터 씁니다.
	/// </summary>
	/// <remarks>
	/// 클라이언트는 100000 이상을 그대로 실패 코드로 돌려주므로, 이 코드들도 <c>Result.Code</c> 로 받습니다.
	/// </remarks>
	public static class RpcStatusCodes
	{
		public const int Success = 0;

		/// <summary>서버 프레임워크 코드 블록의 시작입니다.</summary>
		public const int FrameworkRangeStart = 100000;
		/// <summary>서버 처리 중 예상 못 한 오류입니다(서버 로그 확인).</summary>
		public const int InternalError = FrameworkRangeStart;
		/// <summary>로그인 전에 <see cref="RpcAllowAnonymousAttribute"/> 가 없는 메서드를 불렀습니다.</summary>
		public const int Unauthenticated = FrameworkRangeStart + 1;
		/// <summary>서버에 없는 메서드입니다(클라이언트와 서버의 계약 버전 차이).</summary>
		public const int MethodNotFound = FrameworkRangeStart + 2;
		/// <summary>요청 본문을 읽지 못했습니다.</summary>
		public const int BadRequest = FrameworkRangeStart + 3;
		/// <summary>세션의 처리 대기 요청이 가득 차 실행하지 않고 거절했습니다(잠시 뒤 다시 시도).</summary>
		public const int Overloaded = FrameworkRangeStart + 4;
		/// <summary>서버 프레임워크 코드 블록의 끝입니다.</summary>
		public const int FrameworkRangeEnd = 100999;

		/// <summary>게임 오류 코드의 시작입니다.</summary>
		public const int GameRangeStart = 101000;
	}
}
