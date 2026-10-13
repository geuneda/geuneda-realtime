using System;

// ReSharper disable once CheckNamespace

namespace Geuneda.Realtime
{
	/// <summary>
	/// <see cref="IRpcSerializer"/> 가 받은 본문을 읽지 못했을 때 던집니다. 클라이언트는 이 예외만 응답 해석 실패(<c>InvalidResponse</c>)로 바꿉니다.
	/// </summary>
	public sealed class RpcSerializationException : Exception
	{
		public RpcSerializationException(string message, Exception innerException) : base(message, innerException)
		{
		}
	}
}
