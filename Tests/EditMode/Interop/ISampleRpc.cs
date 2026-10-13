using System.Threading.Tasks;
using Geuneda.Realtime;

// ReSharper disable once CheckNamespace

namespace Geuneda.Realtime.Tests.Interop
{
	/// <summary>
	/// Unity 테스트와 샘플 서버(<c>Server~/samples/Geuneda.Realtime.SampleServer</c>)가 함께 컴파일하는 계약입니다.
	/// Unity 에서는 클라이언트 프록시(<c>SampleRpcClient</c>)가, 서버에서는 라우트 표가 생성됩니다.
	/// </summary>
	[RpcService]
	public interface ISampleRpc
	{
		[RpcAllowAnonymous]
		Task<LoginResponse> Login(LoginRequest request);

		Task<EchoResponse> Echo(EchoRequest request);

		/// <summary>요청한 상태 코드로 실패합니다.</summary>
		Task<EchoResponse> Fail(FailRequest request);

		Task Remember(RememberRequest request);

		Task<RememberedResponse> Recall();

		/// <summary>같은 세션에 <see cref="PushTopic"/> 푸시를 보낸 뒤 답합니다.</summary>
		Task PushMe(EchoRequest request);

		public const string PushTopic = "Sample/Echoed";

		public sealed class LoginRequest
		{
			public string UserId;
		}

		public sealed class LoginResponse
		{
			public string SessionId;
		}

		public sealed class EchoRequest
		{
			public string Text;
		}

		public sealed class EchoResponse
		{
			public string Text;
			public string UserId;
		}

		public sealed class FailRequest
		{
			public int Status;
		}

		public sealed class RememberRequest
		{
			public string Note;
		}

		public sealed class RememberedResponse
		{
			public string Note;
		}
	}
}
