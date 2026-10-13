using System.Threading;
using System.Threading.Tasks;

namespace Geuneda.Realtime.Server.Tests
{
	/// <summary>서버 테스트용 계약입니다. 생성기가 이 어셈블리에 <c>GeneratedRpcRoutes</c> 를 만듭니다.</summary>
	[RpcService]
	public interface ITestRpc
	{
		[RpcAllowAnonymous]
		Task<LoginRes> Login(LoginReq request);

		Task<EchoRes> Echo(EchoReq request);

		Task Notify(NotifyReq request);

		Task<ClockRes> Clock();

		Task<EchoRes> Fail(FailReq request);

		Task<EchoRes> Crash();

		Task<EchoRes> Slow(SlowReq request, CancellationToken cancellationToken);

		Task PushMe(EchoReq request);

		/// <summary>토큰 없이 <see cref="TestState.Gate"/> 를 기다린 뒤 끝납니다.</summary>
		Task Hold();

		/// <summary>Task 를 돌려주기 전에 스레드를 막고 <see cref="TestState.BlockingGate"/> 를 기다립니다(동기 SDK·잠금 흉내).</summary>
		Task HoldBlocking();

		/// <summary>CallAborted 에 던지거나(<c>throw</c>) 막히는(<c>block</c>) 콜백을 건 뒤 문을 기다립니다.</summary>
		Task HoldWithCallback(CallbackReq request);

		/// <summary>토큰 없이 문을 기다린 뒤 로그인합니다(외부 인증이 늦게 끝나는 경우).</summary>
		[RpcAllowAnonymous]
		Task<LoginRes> LoginLate(LoginReq request);

		public const string PushTopic = "Test/Pushed";

		public sealed class LoginReq
		{
			public string UserId { get; set; }
		}

		public sealed class LoginRes
		{
			public string SessionId { get; set; }
		}

		public sealed class EchoReq
		{
			public string Text { get; set; }
		}

		public sealed class EchoRes
		{
			public string Text { get; set; }
			public string UserId { get; set; }
		}

		public sealed class NotifyReq
		{
			public string Note { get; set; }
		}

		public sealed class ClockRes
		{
			public long Ticks { get; set; }
		}

		public sealed class FailReq
		{
			public int Status { get; set; }
		}

		public sealed class CallbackReq
		{
			public string Mode { get; set; }
		}

		public sealed class SlowReq
		{
			public string Text { get; set; }
			public int DelayMilliseconds { get; set; }
		}
	}
}
