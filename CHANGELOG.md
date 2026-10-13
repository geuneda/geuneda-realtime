# Changelog
이 패키지의 모든 주요 변경사항은 이 파일에 기록됩니다.

이 형식은 [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)를 기반으로 하며,
이 프로젝트는 [Semantic Versioning](http://semver.org/spec/v2.0.0.html)을 따릅니다.

## [Unreleased]

**New**:
- RPC 계약: `[RpcService]` 인터페이스(`Task<TRes> M(TReq)`, `Task M(TReq)`, 인자 없는 메서드, 끝 `CancellationToken`), `[RpcAllowAnonymous]`, 요청·응답 형식과 상수를 인터페이스 안에 둘 수 있음
- 소스 생성기 `Geuneda.Realtime.Generator`(Roslyn 4.3, `Runtime/Generator` 의 분석기 DLL): 클라이언트 프록시 `XxxClient`·`IXxxClient`·메서드 경로 표 `XxxMethods`, 서버 라우트 표 `GeneratedRpcRoutes`, 계약 규칙·경로 이름·ID·생성 이름 충돌 빌드 오류(GRPC001~014)
- ASP.NET Core 서버 `Geuneda.Realtime.Server`(`Server~/`, .NET 10): `AddRealtimeRpc`/`MapRealtimeRpc`, 서브프로토콜 협상(JSON, MessagePack), 로그인 전 호출 차단, 세션당 순차 실행과 대기열 포화 시 `Overloaded`, 핑 응답, 무응답 종료, 중복 로그인 4001, 닫힐 때 남은 요청 미실행·진행 중 호출 취소 알림, `IRpcContext`, `IRealtimeSessions` 푸시, `RpcStatusException`, 시작 검사
- 응답 상태 코드 `RpcStatusCodes`(100000~100999 서버 프레임워크, 101000 부터 게임), 서브프로토콜 이름 규칙 `RpcProtocol`
- 샘플 서버와 Unity 상호 운용 테스트(`[Explicit]`)

**Changed**:
- `MessagePackRpcSerializer` 의 기본 옵션이 `MessagePackSecurity.UntrustedData`(해시 충돌 방어, 깊이 제한)를 씀. 이전 기본은 신뢰 데이터 설정

## [0.1.0] - 2026-10-13

**New**:
- `RealtimeClient`(`IRealtimeClient`)와 `RealtimeClientOptions`: single-flight 연결, 연결마다 재로그인(`LoginAsync`), 호출 시 자동 연결, 지수 백오프 + 지터 자동 재연결, `Disconnected`/`Reconnected` 이벤트
- 요청 ID 매칭 RPC(`CallAsync<TRequest, TResponse>`, `CallAsync<TRequest>`)와 서버 푸시 구독(`Subscribe<TPush>`)
- 결과는 `Result`: 서버 상태 코드(100000 이상 게임 범위)는 그대로 실패 코드, 그 밖의 0 이 아닌 상태는 `InvalidResponse`, 연결 실패·끊김 `NetworkError`, 시간 초과 `Timeout`, 취소 `Cancelled`(예외 아님)
- 바이너리 프레임 프로토콜(`RpcFrame`, `RpcFrameCodec`)과 이름 해시 경로(`RpcMethod`, `RpcTopic`, FNV-1a 32비트)
- 핑퐁으로 끊김 감지, 서버 정책 종료(4000~4999, 로그인 중 포함)는 재연결하지 않음(`RealtimeCloseCodes`)
- 요청 제한 시간과 취소는 보내기 차례 대기부터 적용하고, 차례 전에 끝난 요청은 보내지 않음
- 받은 메시지 조립·해석·역직렬화는 백그라운드 스레드에서, 결과·로그인 대리자·이벤트·푸시는 메인 스레드에서
- 교체 가능한 직렬화 `IRpcSerializer`: `JsonRpcSerializer`(기본), `MessagePackRpcSerializer`(선택 어셈블리 `Geuneda.Realtime.MessagePack`)
- `ClientWebSocketTransport`: 서브프로토콜 `geuneda.rpc.v1.<형식>` 협상, 받지 않으면 `ProtocolMismatch`
- 결과 코드 `RealtimeResultCodes`(2000 블록)
- 의존성: `com.geuneda.core`, `com.cysharp.unitask`, `com.unity.nuget.newtonsoft-json`
