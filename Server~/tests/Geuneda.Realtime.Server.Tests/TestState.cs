using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Geuneda.Realtime.Server.Tests
{
	/// <summary>테스트 서버의 싱글톤 기록입니다.</summary>
	public sealed class TestState
	{
		public ConcurrentQueue<string> Notes { get; } = new ConcurrentQueue<string>();

		/// <summary>토큰을 쓰지 않는 느린 호출(<c>Hold</c>, <c>LoginLate</c>)을 붙잡아 두는 문입니다.</summary>
		public TaskCompletionSource Gate { get; } = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		/// <summary><c>Hold</c>·<c>LoginLate</c> 가 문 앞에 도착하면 완료됩니다.</summary>
		public TaskCompletionSource GateReached { get; } = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		/// <summary>스레드를 막는(동기) 느린 호출 <c>HoldBlocking</c> 의 문입니다.</summary>
		public ManualResetEventSlim BlockingGate { get; } = new ManualResetEventSlim();

		/// <summary>문을 지나 끝까지 실행된 호출 수입니다.</summary>
		public int PassedGate => Volatile.Read(ref _passedGate);

		private int _passedGate;

		public void MarkPassedGate()
		{
			Interlocked.Increment(ref _passedGate);
		}
	}
}
