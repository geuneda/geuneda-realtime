using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

// ReSharper disable once CheckNamespace

namespace Geuneda.Realtime.Tests
{
	/// <summary>
	/// 실제 <c>ClientWebSocket</c> 을 검증하는 127.0.0.1 WebSocket 서버(RFC 6455 최소 구현)입니다.
	/// 요청은 받은 본문을 상태 0 으로 돌려주고, 핑에는 퐁을, 종료에는 같은 코드로 답합니다.
	/// </summary>
	internal sealed class LoopbackWebSocketServer : IDisposable
	{
		private readonly TcpListener _listener;
		private readonly object _gate = new object();
		private readonly List<LoopbackWebSocketPeer> _peers = new List<LoopbackWebSocketPeer>();
		private readonly Task _acceptTask;
		private int _acceptedCount;
		private bool _disposed;
		private volatile string _acceptedSubProtocol;

		/// <param name="acceptedSubProtocol">핸드셰이크에서 돌려줄 서브프로토콜입니다. null 이면 돌려주지 않습니다.</param>
		/// <param name="fragmentResponses">응답을 두 프레임으로 나눠 보낼지 여부입니다.</param>
		public LoopbackWebSocketServer(string acceptedSubProtocol, bool fragmentResponses)
		{
			_acceptedSubProtocol = acceptedSubProtocol;
			FragmentResponses = fragmentResponses;

			_listener = new TcpListener(IPAddress.Loopback, 0);
			_listener.Start();
			Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
			_acceptTask = Task.Run(AcceptLoop);
		}

		public int Port { get; }
		/// <summary>다음 핸드셰이크부터 돌려줄 서브프로토콜입니다. 테스트 중에 바꿀 수 있습니다.</summary>
		public string AcceptedSubProtocol
		{
			get => _acceptedSubProtocol;
			set => _acceptedSubProtocol = value;
		}
		public bool FragmentResponses { get; }
		public Uri Uri => new Uri($"ws://127.0.0.1:{Port}/rpc");
		public int AcceptedCount => Volatile.Read(ref _acceptedCount);
		public ConcurrentQueue<int> ClientCloseCodes { get; } = new ConcurrentQueue<int>();
		public ConcurrentQueue<uint> PingSequences { get; } = new ConcurrentQueue<uint>();

		/// <summary>열린 연결 모두에 서버 종료를 보냅니다.</summary>
		public void CloseAll(int closeStatus)
		{
			foreach (var peer in SnapshotPeers())
			{
				peer.SendClose(closeStatus);
			}
		}

		/// <summary>
		/// 열린 연결 모두에 메시지 하나를 <paramref name="fragments"/> 개 프레임으로 나눠 보내고, 다 쓰면 끝나는 작업을 돌려줍니다.
		/// 큰 메시지는 클라이언트가 메인 스레드에서 읽으므로 백그라운드 스레드에서 보냅니다(메인 스레드에서 쓰면 서로 기다림).
		/// </summary>
		public Task SendToAll(int opcode, byte[] message, int fragments)
		{
			return Task.WhenAll(SnapshotPeers().Select(peer => Task.Run(() => peer.SendMessage(opcode, message, fragments))));
		}

		public void Dispose()
		{
			LoopbackWebSocketPeer[] peers;
			lock (_gate)
			{
				_disposed = true;
				peers = _peers.ToArray();
			}

			_listener.Stop();

			foreach (var peer in peers)
			{
				peer.Dispose();
			}

			_acceptTask.Wait(TimeSpan.FromSeconds(2));
		}

		private LoopbackWebSocketPeer[] SnapshotPeers()
		{
			lock (_gate)
			{
				return _peers.ToArray();
			}
		}

		private void AcceptLoop()
		{
			while (true)
			{
				TcpClient client;
				try
				{
					client = _listener.AcceptTcpClient();
				}
				catch (Exception exception) when (exception is SocketException || exception is ObjectDisposedException || exception is InvalidOperationException)
				{
					return;
				}

				LoopbackWebSocketPeer peer;
				lock (_gate)
				{
					// 닫는 중에 받은 연결은 등록하지 않고 바로 닫는다
					if (_disposed)
					{
						client.Close();
						return;
					}

					peer = new LoopbackWebSocketPeer(this, client);
					_peers.Add(peer);
				}

				Interlocked.Increment(ref _acceptedCount);
				Task.Run(peer.Run);
			}
		}
	}
}
