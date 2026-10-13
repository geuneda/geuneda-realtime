using System.Threading.Tasks;
using Geuneda.Realtime.Server;
using Geuneda.Realtime.Tests.Interop;

namespace Geuneda.Realtime.SampleServer
{
	/// <summary>상호 운용 계약 <see cref="ISampleRpc"/> 의 서버 구현입니다.</summary>
	public sealed class SampleRpc : ISampleRpc
	{
		private readonly IRpcContext _context;
		private readonly SampleMemory _memory;

		public SampleRpc(IRpcContext context, SampleMemory memory)
		{
			_context = context;
			_memory = memory;
		}

		public Task<ISampleRpc.LoginResponse> Login(ISampleRpc.LoginRequest request)
		{
			if (string.IsNullOrEmpty(request?.UserId))
			{
				throw new RpcStatusException(RpcStatusCodes.GameRangeStart, "UserId is required");
			}

			_context.SignIn(request.UserId);
			return Task.FromResult(new ISampleRpc.LoginResponse { SessionId = _context.Session.Id });
		}

		public Task<ISampleRpc.EchoResponse> Echo(ISampleRpc.EchoRequest request)
		{
			return Task.FromResult(new ISampleRpc.EchoResponse { Text = request.Text, UserId = _context.Session.UserId });
		}

		public Task<ISampleRpc.EchoResponse> Fail(ISampleRpc.FailRequest request)
		{
			throw new RpcStatusException(request.Status);
		}

		public Task Remember(ISampleRpc.RememberRequest request)
		{
			_memory.Notes[_context.Session.UserId] = request.Note;
			return Task.CompletedTask;
		}

		public Task<ISampleRpc.RememberedResponse> Recall()
		{
			_memory.Notes.TryGetValue(_context.Session.UserId, out var note);
			return Task.FromResult(new ISampleRpc.RememberedResponse { Note = note });
		}

		public async Task PushMe(ISampleRpc.EchoRequest request)
		{
			await _context.Session.PushAsync(RpcTopic.Of(ISampleRpc.PushTopic), new ISampleRpc.EchoResponse { Text = request.Text });
		}
	}
}
