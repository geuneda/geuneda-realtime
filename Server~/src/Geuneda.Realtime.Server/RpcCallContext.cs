using System;
using System.Threading;

namespace Geuneda.Realtime.Server
{
	/// <summary>호출마다 만드는 DI 범위의 <see cref="IRpcContext"/> 구현입니다. 디스패처가 호출 직전에 채웁니다.</summary>
	internal sealed class RpcCallContext : IRpcContext
	{
		private RealtimeServerSession _session;
		private RpcRoute _route;
		private uint _requestId;
		private CancellationToken _callAborted;

		public IRealtimeSession Session => RequireSession();

		public RpcRoute Route
		{
			get
			{
				RequireSession();
				return _route;
			}
		}

		public uint RequestId
		{
			get
			{
				RequireSession();
				return _requestId;
			}
		}

		public CancellationToken CallAborted
		{
			get
			{
				RequireSession();
				return _callAborted;
			}
		}

		public void SignIn(string userId)
		{
			RequireSession().SignIn(userId);
		}

		internal void Initialize(RealtimeServerSession session, RpcRoute route, uint requestId, CancellationToken callAborted)
		{
			_session = session;
			_route = route;
			_requestId = requestId;
			_callAborted = callAborted;
		}

		private RealtimeServerSession RequireSession()
		{
			return _session ?? throw new InvalidOperationException("IRpcContext is only available inside a realtime RPC call");
		}
	}
}
