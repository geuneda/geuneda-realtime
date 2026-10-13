using System;

// ReSharper disable once CheckNamespace

namespace Geuneda.Realtime
{
	/// <summary>
	/// 로그인 전에도 부를 수 있는 RPC 메서드 표시입니다(로그인 RPC 등). 표시가 없는 메서드를 로그인 전에 부르면 서버가
	/// <see cref="RpcStatusCodes.Unauthenticated"/> 로 답합니다.
	/// </summary>
	[AttributeUsage(AttributeTargets.Method, Inherited = false)]
	public sealed class RpcAllowAnonymousAttribute : Attribute
	{
	}
}
