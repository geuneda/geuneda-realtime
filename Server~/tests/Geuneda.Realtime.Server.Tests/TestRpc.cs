using System;
using System.Threading;
using System.Threading.Tasks;

namespace Geuneda.Realtime.Server.Tests
{
	public sealed class TestRpc : ITestRpc
	{
		private readonly IRpcContext _context;
		private readonly TestState _state;

		public TestRpc(IRpcContext context, TestState state)
		{
			_context = context;
			_state = state;
		}

		public Task<ITestRpc.LoginRes> Login(ITestRpc.LoginReq request)
		{
			_context.SignIn(request.UserId);
			return Task.FromResult(new ITestRpc.LoginRes { SessionId = _context.Session.Id });
		}

		public Task<ITestRpc.EchoRes> Echo(ITestRpc.EchoReq request)
		{
			return Task.FromResult(new ITestRpc.EchoRes { Text = request.Text, UserId = _context.Session.UserId });
		}

		public Task Notify(ITestRpc.NotifyReq request)
		{
			_state.Notes.Enqueue(request.Note);
			return Task.CompletedTask;
		}

		public Task<ITestRpc.ClockRes> Clock()
		{
			return Task.FromResult(new ITestRpc.ClockRes { Ticks = 42 });
		}

		public Task<ITestRpc.EchoRes> Fail(ITestRpc.FailReq request)
		{
			throw new RpcStatusException(request.Status);
		}

		public Task<ITestRpc.EchoRes> Crash()
		{
			throw new InvalidOperationException("boom");
		}

		public async Task<ITestRpc.EchoRes> Slow(ITestRpc.SlowReq request, CancellationToken cancellationToken)
		{
			await Task.Delay(request.DelayMilliseconds, cancellationToken);
			return new ITestRpc.EchoRes { Text = request.Text };
		}

		public async Task Hold()
		{
			_state.GateReached.TrySetResult();
			await _state.Gate.Task;
			_state.MarkPassedGate();
		}

		public Task HoldBlocking()
		{
			_state.GateReached.TrySetResult();
			_state.BlockingGate.Wait();
			_state.MarkPassedGate();
			return Task.CompletedTask;
		}

		public async Task HoldWithCallback(ITestRpc.CallbackReq request)
		{
			if (request.Mode == "throw")
			{
				_context.CallAborted.Register(() => throw new InvalidOperationException("callback failed"));
			}
			else
			{
				_context.CallAborted.Register(() => _state.BlockingGate.Wait());
			}

			_state.GateReached.TrySetResult();
			await _state.Gate.Task;
			_state.MarkPassedGate();
		}

		public async Task<ITestRpc.LoginRes> LoginLate(ITestRpc.LoginReq request)
		{
			_state.GateReached.TrySetResult();
			await _state.Gate.Task;
			_context.SignIn(request.UserId);
			_state.MarkPassedGate();
			return new ITestRpc.LoginRes { SessionId = _context.Session.Id };
		}

		public async Task PushMe(ITestRpc.EchoReq request)
		{
			await _context.Session.PushAsync(RpcTopic.Of(ITestRpc.PushTopic), new ITestRpc.EchoRes { Text = request.Text });
		}
	}
}
