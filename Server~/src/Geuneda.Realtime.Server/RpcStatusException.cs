using System;

namespace Geuneda.Realtime.Server
{
	/// <summary>
	/// RPC 구현이 실패 상태로 답할 때 던집니다. 응답 상태가 <see cref="Status"/> 가 되고 본문은 비며, 서버 로그에는 남기지 않습니다(정상 실패).
	/// </summary>
	/// <remarks>
	/// 상태는 100000 이상이어야 합니다(클라이언트가 그대로 실패 코드로 받는 범위). 게임 코드는 <see cref="RpcStatusCodes.GameRangeStart"/>(101000)부터 씁니다.
	/// </remarks>
	public sealed class RpcStatusException : Exception
	{
		/// <exception cref="ArgumentOutOfRangeException"><paramref name="status"/> 가 100000 미만인 경우</exception>
		public RpcStatusException(int status, string message = null)
			: base(message ?? $"RPC failed with status {status}")
		{
			if (status < RpcStatusCodes.FrameworkRangeStart)
			{
				throw new ArgumentOutOfRangeException(nameof(status), status,
					$"RPC status must be >= {RpcStatusCodes.FrameworkRangeStart} (game codes start at {RpcStatusCodes.GameRangeStart})");
			}

			Status = status;
		}

		public int Status { get; }
	}
}
