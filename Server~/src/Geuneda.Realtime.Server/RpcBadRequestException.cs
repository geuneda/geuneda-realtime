using System;

namespace Geuneda.Realtime.Server
{
	/// <summary>요청 본문을 읽지 못했을 때 라우트가 던집니다. 디스패처가 <see cref="RpcStatusCodes.BadRequest"/> 로 바꿉니다.</summary>
	internal sealed class RpcBadRequestException : Exception
	{
		public RpcBadRequestException(string message, Exception innerException) : base(message, innerException)
		{
		}
	}
}
