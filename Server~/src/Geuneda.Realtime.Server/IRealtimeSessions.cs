using System.Collections.Generic;

namespace Geuneda.Realtime.Server
{
	/// <summary>
	/// 열린 세션 목록입니다(DI 싱글톤). 다른 사용자에게 푸시를 보내거나 운영 조치로 닫을 때 씁니다.
	/// </summary>
	public interface IRealtimeSessions
	{
		/// <summary>사용자의 세션입니다. 없으면 null 입니다. 사용자당 세션은 하나입니다(새 로그인이 이전 세션을 닫음).</summary>
		IRealtimeSession FindByUser(string userId);

		/// <summary>지금 열린 세션의 복사본입니다.</summary>
		IReadOnlyCollection<IRealtimeSession> All { get; }
	}
}
