# Geuneda.Realtime - AI 에이전트 가이드

> **관련 파일**: `CLAUDE.md`는 이 파일을 감쌉니다 — `CLAUDE.md`가 아니라 `AGENTS.md`를 편집하세요. `README.md`는 사용자 대상 문서입니다.

## 1. 패키지 개요
- **패키지**: `com.geuneda.realtime`
- **Unity**: 2021.3+
- **의존성**: `com.geuneda.core`(Result, ResultCodes, ModuleLogger), `com.cysharp.unitask`, `com.unity.nuget.newtonsoft-json`. MessagePack 은 선택(`Geuneda.Realtime.MessagePack` 어셈블리)
- **서버**: `Server~/`(Unity 가 가져오지 않는 폴더)의 .NET 10 솔루션. ASP.NET Core 서버 라이브러리, 소스 생성기, 샘플 서버, 테스트
- **출처**: Lucky Defense 의 Perbase(PerbaseConnector / PerbaseSession)를 참고했습니다(geuneda 로드맵 3-2). 연결 관리와 세션 분리, single-flight 연결, 연결마다 재로그인, 요청 ID 매칭, 푸시 채널은 따랐습니다. 바꾼 것: 토큰은 URL 쿼리가 아니라 첫 메시지(로그인 RPC), XXTEA 대신 TLS(wss), 문자열 경로 대신 이름 해시 정수 ID, 고정 간격 대신 지수 백오프 + 지터, 연결·요청·핑 제한 시간 분리, 이유 없는 종료도 재연결, 4000~4999 정책 종료는 재연결 안 함.
- **계약·서버 출처**: Perbase rpc-extensions(로드맵 3-3). `Task<TRes> M(TReq)` 한 모양의 공유 계약 인터페이스, 같은 입력에서 클라이언트 프록시와 서버 라우트 표를 생성하는 구조는 따랐습니다. 바꾼 것: 경로 이름·ID 충돌은 생성기 빌드 오류(Perbase 는 짧은 이름 충돌 위험), 정수 ID, 프록시 팩토리를 Expression.Compile 대신 생성 코드의 `new`, 라우트도 타입이 정해진 대리자(리플렉션 없음), 서버 코드(라우터·서버 코덱)는 클라이언트 빌드에 들어가지 않음(`Server~`), 응답 상태 범위 분리(100000~100999 프레임워크, 101000 부터 게임).

## 2. 구조

| 파일 | 역할 |
|------|------|
| `Runtime/RealtimeClient.cs` | 연결 관리: single-flight 연결(`_connecting`), 로그인, 세션 공개, 자동 재연결, 푸시 구독, 결과를 메인 스레드로 |
| `Runtime/Internal/RealtimeSession.cs` | 연결 하나의 수명: 수신 루프, 핑 루프, 요청 ID → 대기 중 호출, 보내기 직렬화(SemaphoreSlim), 한 번만 닫기 |
| `Runtime/Protocol/` | 프레임(`RpcFrame`), 코덱(빅엔디언), 경로(`RpcMethod`/`RpcTopic`, FNV-1a) |
| `Runtime/Serialization/` | `IRpcSerializer`, JSON 기본 |
| `Runtime/MessagePack/` | 선택 어셈블리. `versionDefines`(`org.nuget.messagepack` ≥ 3.0.0) 또는 직접 넣은 `GEUNEDA_MESSAGEPACK` 이 있을 때만 컴파일 |
| `Runtime/Transport/` | `IRealtimeTransport`/`IRealtimeConnection` 과 `ClientWebSocket` 구현 |
| `Runtime/Contracts/`, `Runtime/RpcStatusCodes.cs`, `Runtime/Protocol/RpcProtocol.cs` | 계약 표시(`[RpcService]`, `[RpcAllowAnonymous]`), 응답 상태 코드, 서브프로토콜 이름 규칙. 서버도 링크 |
| `Runtime/Generator/Geuneda.Realtime.Generator.dll` | 소스 생성기 빌드 산출물(RoslynAnalyzer 레이블, 모든 플랫폼 꺼짐). 이 asmdef 와 이것을 참조하는 어셈블리에 적용 |
| `Server~/src/Geuneda.Realtime.Generator/` | 생성기 소스. `ServiceModelBuilder`(계약 검사) → `Emitter`(충돌 검사) → `ClientEmitter`/`RouteEmitter` |
| `Server~/src/Geuneda.Realtime.Server/` | 서버: `RealtimeRpcHandler`(협상), `RealtimeServerSession`(받기·순차 실행·무응답 감시·보내기), `RpcRoute`, `IRpcContext`, `IRealtimeSessions` |
| `Server~/samples/`, `Server~/tests/` | 샘플 서버(Unity 상호 운용 계약 링크), 서버·생성기 xUnit 테스트 |

## 3. 규칙

- **결과 규약**: 정상 실패(연결 실패, 끊김, 시간 초과, 취소, 서버 상태 코드, 정책 종료, 형식 불일치)는 `Result` 코드, 오용(Dispose 뒤 호출, 잘못된 옵션, null 핸들러)은 예외. `Dispose` 뒤 `CallAsync` 의 예외는 반환된 UniTask 를 기다릴 때 나옵니다(async 메서드). 로그인 대리자의 예외(버그)는 결과로 바꾸지 않고 `TrySetException` 으로 연결을 기다리던 호출 모두에 던집니다. 받을 호출자가 없는 자동 재연결에서만 오류 로그를 남기고 `Unknown` 으로 멈춥니다.
- **서버 상태 코드**: 0 성공, 100000 이상은 그대로 실패 코드(100000~100999 서버 프레임워크 `RpcStatusCodes`, 101000 부터 게임), 그 밖의 값은 `InvalidResponse`. 공용 코드(예: 4 `NetworkError`)와 겹치면 재연결·재시도 판단이 틀어지므로 범위를 넓히지 마세요. `RpcStatusException` 은 100000 미만을 받지 않습니다.
- **상태 변경은 lock 안, await 는 lock 밖**. 클라이언트는 현재 세션의 종료만 처리합니다(로그인 중이던 세션이나 이전 세션의 종료는 무시).
- **세션 닫기 순서**(`RealtimeSession.Close`, 한 번만): 소유자 알림(클라이언트가 `_session` 을 비움) → 수명 취소(루프·보내기 중단) → 종료 보내기 시작 → 대기 호출 실패. 대기 호출의 이어지는 코드가 그 자리에서 실행되므로 소유자 상태가 먼저 바뀌어야 합니다(실패 직후 `ConnectAsync` 가 닫힌 세션을 성공으로 보면 안 됨). 받기는 수명이 아니라 `_receiveStop` 으로 멈추고, 종료를 보낸 뒤 취소합니다(ClientWebSocket 은 대기 중인 받기를 취소하면 소켓을 끊어 종료를 못 보냄). 닫힌 뒤 받은 메시지는 버립니다.
- **보내기**: `SemaphoreSlim` 으로 하나씩. 요청의 제한 시간·취소는 차례 대기부터 적용하고, 차례가 오기 전에 끝난 요청은 보내지 않습니다. 핑은 기다리지 않고 보내(막힌 보내기가 핑 타이머를 멈추지 않게) 끊김 감지가 계속됩니다.
- **세션 공개 시점**: 로그인이 끝나고 닫히지 않은 세션만 `_session` 으로 공개합니다. 로그인은 `SessionCaller` 로 공개 전 세션에 보냅니다. 공개하지 못한 모든 경로(로그인 실패, 대리자 예외, 로그인 중 종료, 취소, Dispose)는 `ConnectOnceAsync` 의 `finally` 가 세션을 닫습니다. 로그인 대리자의 기다림은 연결 토큰에 묶어(`AttachExternalCancellation`) 대리자가 토큰을 무시해도 `ConnectTimeout`·Dispose 가 듣고, 공개 직전에도 토큰을 확인해 늦게 끝난 로그인은 공개하지 않습니다. 로그인 중 정책 종료는 `FailClosedDuringLogin` 이 `_policyCloseReason` 을 남깁니다.
- **재연결**: `NetworkError`/`Timeout` 만 다시 시도합니다. 그 밖의 실패(로그인 거부, `ProtocolMismatch`, `NotReconnectable`, `Closed`)는 바로 멈춥니다. 정책 종료(4000~4999)는 `_policyCloseReason` 을 남겨 호출을 `NotReconnectable` 로 막고, `ConnectAsync` 가 지웁니다. 루프는 한 번에 하나(`_reconnecting`)이고, 도는 동안 생긴 종료도 그 루프가 맡습니다: 성공을 확정하는 lock 안에서 `_session` 이 비어 있으면 처음부터 다시 시도합니다(종료 쪽은 같은 lock 에서 `_reconnecting` 을 보고 새 루프를 만들지 않음).
- **스레드**: `ClientWebSocketConnection` 은 소켓 작업을 `ConfigureAwait(false)` 로 이어 가므로 세션의 수신 처리(조립, 해석, 응답·푸시 역직렬화)는 백그라운드 스레드에서 돌 수 있습니다. 공개 호출·연결 결과와 `SessionCaller` 결과는 `SwitchToMainThread` 뒤 돌려주고, 로그인 대리자는 메인 스레드에서 부릅니다. 이벤트와 푸시는 공통 `Dispatch` 로 `CallbackDispatcher`(기본 `UniTask.Post`)에 넘깁니다. 실행 시점에 클라이언트 수명(푸시는 구독도)을 확인하고, 핸들러 예외와 디스패처 자체의 예외는 로그만 남깁니다(세션 정리·재연결을 끊지 않게). 공개 호출·연결과 `SessionCaller` 는 `finally` 에서 메인 스레드로 돌아가 예외도 메인 스레드에서 받습니다. 핑 루프는 `UniTask.Delay(ignoreTimeScale: true)` 입니다(백그라운드 스레드에서 만들어도 됨).
- **프로토콜 호환**: 프레임 레이아웃(flags 0, Ping·Pong 6바이트), 서브프로토콜 이름(`geuneda.rpc.v1.<형식>`), FNV-1a 해시, 종료 코드 의미(받은 코드를 그대로 돌려보냄, 1003·1009 로컬 거부)는 서버(로드맵 3-3, C# ASP.NET Core)와의 계약입니다. 바꾸려면 서브프로토콜 버전(`v2`)을 올립니다.
- **전송 계약**: `ReceiveAsync` 는 전송 오류를 종료 결과(1006)로 돌려주고 취소 때만 예외입니다. 돌려준 배열은 다시 쓰지 않습니다(푸시 본문이 그 배열을 가리킴). 서브프로토콜을 서버가 돌려주지 않거나(연결 뒤 비교) 다른 것을 고르면(`WebSocketError.UnsupportedProtocol`) `ProtocolMismatch` 입니다.
- **공유 파일**: `Runtime/Protocol/`, `Runtime/Serialization/`, `Runtime/MessagePack/MessagePackRpcSerializer.cs`, `Runtime/Contracts/`, `RealtimeCloseCodes.cs`, `RpcStatusCodes.cs` 는 서버 라이브러리가 링크해 컴파일합니다(`Fnv1a32.cs` 는 생성기도). Unity 형식·UniTask·Geuneda.Core 를 쓰지 말고, 고치면 `dotnet test "Server~/Geuneda.Realtime.Server.slnx"` 도 돌립니다.
- **생성기**: netstandard2.0, 다른 패키지 의존 없음, Microsoft.CodeAnalysis 4.3 API 만(Unity 6000.3 의 Roslyn 4.3 — `ForAttributeWithMetadataName` 대신 `CreateSyntaxProvider`). 클라이언트 프록시는 컴파일에 `IRpcCaller` 가, 라우트 표는 `Geuneda.Realtime.Server.RpcRoute` 가 보일 때만 만듭니다. 오류 진단이 하나라도 있으면 아무 코드도 만들지 않습니다. partial 인터페이스는 특성 목록이 있는 선언 중 첫 번째에서 한 번 처리합니다. 생성 코드의 식별자는 `Identifiers.Escape`(예약어 `@`)를 거치고 경로 문자열·해시는 원래 이름입니다. 생성 이름 충돌은 GRPC012(멤버)·013(서비스끼리, 생성 형식 종류와 상관없이 이름 전체 비교)·014(이미 있는 형식, 출력 단계에서 컴파일을 봄)로 막습니다. 소스를 고치면 `dotnet build "Server~/src/Geuneda.Realtime.Generator" -c Release` 로 `Runtime/Generator` DLL 을 갱신해 함께 커밋합니다. 진단 ID 를 더하면 `AnalyzerReleases.Unshipped.md` 에도 적습니다.
- **서버 세션 토큰**: 내부 전용 `_connection`(요청 중단에 연결, 받기·보내기·무응답 감시, 종료 시간 초과)과 `_dispatch`(처리 루프), 공개 `_callsAborted`(`IRpcContext.CallAborted`, 로그인 검사)와 `_closed`(`IRealtimeSession.Closed`)로 나눕니다. 내부 대기는 공개 토큰에 의존하지 않습니다(사용자 콜백이 내부 대기를 붙잡지 않게). 공개 토큰은 `SignalExternally`(`CancelAsync`)로 취소 상태를 바로 세우고 콜백은 스레드 풀에서 돌며 실패는 경고 로그만 남깁니다(기다리지 않음). `StopCalls` 는 공개 호출 토큰을 먼저 취소 상태로 만든 뒤 `_dispatch` 를 멈추고, `CloseAsync` 진입·`RunAsync` 끝·`_connection` 취소(내부 콜백) 모두에서 불립니다. 처리 루프는 호출을 `Task.Run` 으로 시작해 `WaitAsync(_dispatch)` 로 기다리고, 멈추면 그 호출을 놓아 줍니다(자기 DI 범위를 들고 끝까지 돌고 결과는 버림). 세션 끝에서 대기열을 비웁니다. 그래서 연결·레지스트리 정리가 토큰을 무시하거나 스레드를 막는 구현, 던지거나 막히는 취소 콜백에 붙잡히지 않습니다.
- **서버 순서·과부하**: 요청은 받은 순서대로 하나씩 실행합니다(구매·사용 순서 보장). 대기열이 가득 차면 읽기를 멈추지 않고 그 요청을 `Overloaded` 로 바로 거절합니다(멈추면 핑을 못 받아 정상 연결이 끊김). 핑은 받기 루프가 바로 답합니다.
- **서버 종료**: 보낼 수 있는 코드인지(`CanSend`) 먼저 검사한 뒤 상태를 바꿉니다. 종료는 한 번만 보내고, 보낸 뒤에는 클라이언트 종료 답을 기다리며 메시지를 버리다 `CloseTimeout` 뒤 끝냅니다(바로 끊으면 클라이언트가 종료 프레임 전에 리셋을 받을 수 있음).
- **로그인·레지스트리**: 사용자당 세션은 하나이고 새 `SignIn` 이 이전 세션을 4001 로 닫습니다. 사용자 표는 레지스트리 잠금 하나로 지키고, `SignIn` 은 그 안에서 세션의 공개 호출 토큰(`CallsAborted`)이 취소되지 않았는지 확인합니다(그 토큰은 종료 시작·연결 취소 즉시 취소 상태가 되고 제거는 그 뒤라, 닫힌 세션의 늦은 로그인이 새 세션을 밀어내지 않음). 세션은 `_userId` 를 먼저 남기고 레지스트리를 부르며, 거절되면 되돌립니다. 설정·라우트·구현 등록 오류는 `MapRealtimeRpc` 에서 예외입니다. static 가변 상태 없이 DI 싱글톤(`IRealtimeSessions`)으로 둡니다.
- 결과 코드는 2000 블록(`RealtimeResultCodes`, core README 의 범위 표에 등록). 값은 바꾸거나 재사용하지 않습니다.
- static 가변 상태 금지(D2). 클라이언트는 인스턴스로 만들어 `MainInstaller.Bind<IRealtimeClient>` 로 바인딩합니다.

## 4. 테스트

- `Tests/EditMode/` (`Geuneda.Realtime.Tests.EditMode`)
  - `ProtocolTest`: 바이트 레이아웃, 왕복, 잘못된 프레임(flags, Ping·Pong 본문 포함), FNV 벡터, JSON, 종료 코드 범위
  - `RealtimeClientTest`: `FakeRealtimeServer`/`FakeConnection`(메모리 전송, 서버 메시지는 다음 PlayerLoop 에 전달, `SendGate` 로 막힌 보내기 재현)으로 연결, 로그인, single-flight, 상태 코드, 시간 초과와 늦은 응답, 취소, 막힌 보내기 중 취소·시간 초과·Dispose, 로그인 대리자 예외(백그라운드에서 던져도 메인 스레드에서 받음), 토큰을 무시하는 로그인의 시간 초과와 Dispose, 큐에 들어간 연결 이벤트의 Dispose 뒤 무시, 던지는 디스패처, 로그인 중 정책 종료(처음·재연결), 실패 직후 재접속, 재연결 중 재종료, 푸시와 큐에 들어간 푸시의 해제 뒤 무시, 재연결, 정책 종료, 핑 시간 초과, Dispose, 옵션 검증, 백오프 범위
  - `ClientWebSocketTransportTest`: `LoopbackWebSocketServer`(TcpListener 위의 최소 RFC 6455 서버, 127.0.0.1 빈 포트)로 실제 `ClientWebSocket` 의 서브프로토콜, 조각난 100KB 메시지, 로그인 대리자와 호출 결과의 메인 스레드 복귀, 핑퐁, 서버 정책 종료와 같은 코드 응답, 1012 종료가 ClientWebSocket 에서 1006 이 되는 플랫폼 동작, 텍스트 1003, 정확히 16MB 허용과 16MB+1 의 1009, Dispose 종료, 서브프로토콜 생략·다른 선택(재연결 중단 포함), 연결 실패
  - 배치 모드 EditMode 는 프레임이 매우 빨라 대기는 프레임 수가 아니라 시간(최대 5초)으로 합니다.
  - `GeneratedClientTest`: Unity 컴파일에서 생성된 `SampleRpcClient`/`SampleRpcMethods`(계약 `Interop/ISampleRpc.cs`)의 메서드 모양별 호출과 ID
  - `SampleServerInteropTest`(`[Explicit]`, `Interop` 범주): 실제 샘플 서버와 로그인·호출·상태 코드·푸시·MethodNotFound. 서버를 띄우고(`dotnet run --project "Server~/samples/Geuneda.Realtime.SampleServer" -- --urls http://127.0.0.1:5199`) `GEUNEDA_REALTIME_INTEROP_URL=ws://127.0.0.1:5199/rpc` 로 이 테스트를 이름으로 골라 실행합니다. `Interop/` 폴더는 샘플 서버가 링크하므로 Unity 전용 코드를 두지 않습니다.
- `Server~/tests/Geuneda.Realtime.Server.Tests`: 127.0.0.1 Kestrel 과 프레임 클라이언트(`RawRpcClient`)로 로그인 차단, 상태 코드, 순차 실행과 핑, 푸시, 중복 로그인 4001, MessagePack 협상, 서브프로토콜 거부, 종료 코드(1002·1003·1009·1001·되돌림), 시작 검사, Unity 와 같은 바이트 벡터. `SessionTokenTest`: 받기가 붙잡힌 가짜 WebSocket(`StuckWebSocket`)으로 연결 취소가 정리 전에 호출 토큰을 취소하고 늦은 로그인을 거절하는지. `SessionLifetimeTest`: 토큰 없는 호출(`TestState.Gate`)·스레드를 막는 호출(`BlockingGate`)로 닫힌 세션의 대기 요청 미실행·대기열 비우기·정리, 늦은 로그인, 대기열 포화 `Overloaded` 와 핑, 보낼 수 없는 종료 코드, 던지거나 막히는 CallAborted 콜백.
- `Server~/tests/Geuneda.Realtime.Generator.Tests`: 생성기를 직접 돌려 클라이언트·서버 생성 코드가 컴파일되는지(partial·예약어 포함), 진단 GRPC001~014 를 확인합니다(Roslyn 4.3.1 로 컴파일).
- `Tests/MessagePack/` (`Geuneda.Realtime.MessagePack.Tests.EditMode`): MessagePack 이 있을 때만 컴파일. 왕복, 오프셋, 잘못된 본문, 서브프로토콜 이름. 호스트 레포 manifest 에 `org.nuget.messagepack` 3.1.12 와 `org.nuget.microsoft.net.stringtools` 1.0.0 이 있습니다.
