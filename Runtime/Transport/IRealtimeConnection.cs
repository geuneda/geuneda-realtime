using System;
using System.Threading;
using Cysharp.Threading.Tasks;

// ReSharper disable once CheckNamespace

namespace Geuneda.Realtime
{
	/// <summary>
	/// 열린 메시지 연결 하나입니다. 보내기는 한 번에 하나씩 부르고(호출자가 직렬화), 받기는 한 곳에서만 부릅니다.
	/// 결과는 어느 스레드에서 끝나도 됩니다(세션은 스레드 안전).
	/// </summary>
	public interface IRealtimeConnection : IDisposable
	{
		/// <summary>바이너리 메시지 하나를 보냅니다. 연결이 끊겨 보내지 못하면 false 입니다. 취소 때만 예외입니다.</summary>
		UniTask<bool> SendAsync(ArraySegment<byte> message, CancellationToken cancellationToken);

		/// <summary>
		/// 메시지 하나 또는 연결 종료를 받습니다. 전송 오류도 종료(<see cref="RealtimeCloseCodes.Abnormal"/>)로 돌려주고, 취소 때만 예외입니다.
		/// 돌려준 메시지 배열은 다시 쓰지 않습니다(받는 쪽이 보관할 수 있음).
		/// </summary>
		UniTask<RealtimeReceiveResult> ReceiveAsync(CancellationToken cancellationToken);

		/// <summary>종료 메시지를 보내고 닫습니다. 실패해도 예외 없이 끝납니다.</summary>
		UniTask CloseAsync(int closeStatus, string reason, CancellationToken cancellationToken);
	}
}
