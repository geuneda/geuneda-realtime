using System;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

// ReSharper disable once CheckNamespace

namespace Geuneda.Realtime.Tests
{
	/// <summary>
	/// <see cref="LoopbackWebSocketServer"/> 의 연결 하나입니다. 백그라운드 스레드에서 핸드셰이크와 프레임을 처리합니다.
	/// </summary>
	internal sealed class LoopbackWebSocketPeer : IDisposable
	{
		public const int OpText = 1;
		public const int OpBinary = 2;

		private const string AcceptGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
		private const int OpContinuation = 0;
		private const int OpClose = 8;
		private const int OpPing = 9;
		private const int OpPong = 10;

		private readonly LoopbackWebSocketServer _server;
		private readonly TcpClient _client;
		private readonly NetworkStream _stream;
		private readonly object _writeLock = new object();
		private bool _closeSent;

		public LoopbackWebSocketPeer(LoopbackWebSocketServer server, TcpClient client)
		{
			_server = server;
			_client = client;
			_stream = client.GetStream();
		}

		public void Run()
		{
			try
			{
				Handshake();

				using (var message = new MemoryStream())
				{
					while (true)
					{
						var payload = ReadFrame(out var fin, out var opcode);

						switch (opcode)
						{
							case OpClose:
								var code = payload.Length >= 2 ? (payload[0] << 8) | payload[1] : 1005;
								_server.ClientCloseCodes.Enqueue(code);
								SendClose(code);
								return;
							case OpPing:
								WriteFrame(OpPong, true, payload, 0, payload.Length);
								break;
							case OpBinary:
							case OpContinuation:
								message.Write(payload, 0, payload.Length);
								if (fin)
								{
									HandleMessage(message.ToArray());
									message.SetLength(0);
								}

								break;
						}
					}
				}
			}
			catch (Exception exception) when (exception is IOException || exception is SocketException || exception is ObjectDisposedException)
			{
				// 연결이 닫혔다
			}
			finally
			{
				Dispose();
			}
		}

		/// <summary>메시지 하나를 <paramref name="fragments"/> 개 프레임으로 나눠 보냅니다. 연결이 닫혀 있으면 무시합니다.</summary>
		public void SendMessage(int opcode, byte[] message, int fragments)
		{
			var size = Math.Max(1, (message.Length + fragments - 1) / fragments);
			var offset = 0;

			try
			{
				// 조각 사이에 다른 데이터 프레임(응답)이 끼면 안 되므로 메시지 전체를 잠근다
				lock (_writeLock)
				{
					do
					{
						var count = Math.Min(size, message.Length - offset);
						WriteFrame(offset == 0 ? opcode : OpContinuation, offset + count >= message.Length, message, offset, count);
						offset += count;
					}
					while (offset < message.Length);
				}
			}
			catch (Exception exception) when (exception is IOException || exception is ObjectDisposedException)
			{
				// 클라이언트가 먼저 닫았다(크기 초과 등)
			}
		}

		public void SendClose(int closeStatus)
		{
			lock (_writeLock)
			{
				if (_closeSent)
				{
					return;
				}

				_closeSent = true;
			}

			try
			{
				WriteFrame(OpClose, true, new[] { (byte)(closeStatus >> 8), (byte)closeStatus }, 0, 2);
			}
			catch (Exception exception) when (exception is IOException || exception is ObjectDisposedException)
			{
				// 이미 닫혔다
			}
		}

		public void Dispose()
		{
			_client.Close();
		}

		private void Handshake()
		{
			var request = ReadHeaders();
			var key = HeaderValue(request, "Sec-WebSocket-Key");

			string accept;
			using (var sha1 = SHA1.Create())
			{
				accept = Convert.ToBase64String(sha1.ComputeHash(Encoding.ASCII.GetBytes(key + AcceptGuid)));
			}

			var response = new StringBuilder()
				.Append("HTTP/1.1 101 Switching Protocols\r\n")
				.Append("Upgrade: websocket\r\n")
				.Append("Connection: Upgrade\r\n")
				.Append("Sec-WebSocket-Accept: ").Append(accept).Append("\r\n");

			if (_server.AcceptedSubProtocol != null)
			{
				response.Append("Sec-WebSocket-Protocol: ").Append(_server.AcceptedSubProtocol).Append("\r\n");
			}

			var bytes = Encoding.ASCII.GetBytes(response.Append("\r\n").ToString());
			lock (_writeLock)
			{
				_stream.Write(bytes, 0, bytes.Length);
			}
		}

		private void HandleMessage(byte[] bytes)
		{
			if (!RpcFrameCodec.TryDecode(new ArraySegment<byte>(bytes), out var frame))
			{
				return;
			}

			switch (frame.Kind)
			{
				case RpcFrameKind.Request:
					var reply = RpcFrameCodec.Encode(RpcFrame.Response(frame.RequestId, 0, frame.Payload));
					if (_server.FragmentResponses && reply.Length > 1)
					{
						var half = reply.Length / 2;
						WriteFrame(OpBinary, false, reply, 0, half);
						WriteFrame(OpContinuation, true, reply, half, reply.Length - half);
					}
					else
					{
						WriteFrame(OpBinary, true, reply, 0, reply.Length);
					}

					break;
				case RpcFrameKind.Ping:
					_server.PingSequences.Enqueue(frame.Sequence);
					var pong = RpcFrameCodec.Encode(RpcFrame.Pong(frame.Sequence));
					WriteFrame(OpBinary, true, pong, 0, pong.Length);
					break;
			}
		}

		private string ReadHeaders()
		{
			var headers = new StringBuilder();

			// 헤더 끝(빈 줄)까지 한 바이트씩 읽어 뒤따르는 프레임 바이트를 먹지 않게 한다
			while (!headers.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
			{
				var value = _stream.ReadByte();
				if (value < 0)
				{
					throw new IOException("Connection closed during handshake");
				}

				headers.Append((char)value);
			}

			return headers.ToString();
		}

		private static string HeaderValue(string request, string name)
		{
			foreach (var line in request.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries))
			{
				var colon = line.IndexOf(':');
				if (colon > 0 && string.Equals(line.Substring(0, colon).Trim(), name, StringComparison.OrdinalIgnoreCase))
				{
					return line.Substring(colon + 1).Trim();
				}
			}

			throw new IOException($"Missing header {name}");
		}

		/// <summary>프레임 하나를 읽어 가린 값(mask)을 풉니다. 연결이 닫히면 <see cref="IOException"/> 입니다.</summary>
		private byte[] ReadFrame(out bool fin, out int opcode)
		{
			var header = ReadExactly(2);
			fin = (header[0] & 0x80) != 0;
			opcode = header[0] & 0x0F;
			var masked = (header[1] & 0x80) != 0;
			long length = header[1] & 0x7F;

			if (length == 126)
			{
				var extended = ReadExactly(2);
				length = (extended[0] << 8) | extended[1];
			}
			else if (length == 127)
			{
				var extended = ReadExactly(8);
				length = 0;
				for (var i = 0; i < 8; i++)
				{
					length = (length << 8) | extended[i];
				}
			}

			var mask = masked ? ReadExactly(4) : null;
			var payload = ReadExactly((int)length);

			if (mask != null)
			{
				for (var i = 0; i < payload.Length; i++)
				{
					payload[i] ^= mask[i % 4];
				}
			}

			return payload;
		}

		private byte[] ReadExactly(int count)
		{
			var buffer = new byte[count];
			var read = 0;

			while (read < count)
			{
				var n = _stream.Read(buffer, read, count - read);
				if (n == 0)
				{
					throw new IOException("Connection closed");
				}

				read += n;
			}

			return buffer;
		}

		private void WriteFrame(int opcode, bool fin, byte[] payload, int offset, int count)
		{
			var header = new MemoryStream();
			header.WriteByte((byte)((fin ? 0x80 : 0) | opcode));

			if (count < 126)
			{
				header.WriteByte((byte)count);
			}
			else if (count <= ushort.MaxValue)
			{
				header.WriteByte(126);
				header.WriteByte((byte)(count >> 8));
				header.WriteByte((byte)count);
			}
			else
			{
				header.WriteByte(127);
				for (var shift = 56; shift >= 0; shift -= 8)
				{
					header.WriteByte((byte)((long)count >> shift));
				}
			}

			lock (_writeLock)
			{
				_stream.Write(header.ToArray(), 0, (int)header.Length);
				_stream.Write(payload, offset, count);
			}
		}
	}
}
