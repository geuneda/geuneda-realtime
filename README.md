# Geuneda Realtime

WebSocket 실시간 RPC 클라이언트와, 같은 계약을 쓰는 ASP.NET Core 서버입니다. 클라이언트는 연결 관리(single-flight 연결, 연결마다 재로그인, 지수 백오프 재연결)와 세션(요청 ID 매칭, 서버 푸시, 핑퐁)을 나누고, 결과는 core 결과 규약(`Result`)을 따릅니다. `[RpcService]` 계약 인터페이스 하나에서 소스 생성기가 클라이언트 프록시와 서버 라우트 표를 함께 만듭니다.

| 기능 | 타입 | 네임스페이스 |
|------|------|-------------|
| 클라이언트 | `IRealtimeClient`, `RealtimeClient`, `RealtimeClientOptions`, `IRpcCaller` | `Geuneda.Realtime` |
| 상태·이벤트 | `RealtimeState`, `RealtimeDisconnectInfo` | `Geuneda.Realtime` |
| 경로 | `RpcMethod`, `RpcTopic`(이름의 FNV-1a 32비트 해시가 ID) | `Geuneda.Realtime` |
| 직렬화 | `IRpcSerializer`, `JsonRpcSerializer`, `MessagePackRpcSerializer`(선택) | `Geuneda.Realtime` |
| 전송 | `IRealtimeTransport`, `IRealtimeConnection`, `ClientWebSocketTransport` | `Geuneda.Realtime` |
| 프로토콜 | `RpcFrame`, `RpcFrameKind`, `RpcFrameCodec` | `Geuneda.Realtime` |
| 코드 | `RealtimeResultCodes`(2000 블록), `RealtimeCloseCodes`, `RpcStatusCodes`(응답 상태) | `Geuneda.Realtime` |
| 계약 | `RpcServiceAttribute`, `RpcAllowAnonymousAttribute`, 생성 코드 `XxxClient`·`IXxxClient`·`XxxMethods` | `Geuneda.Realtime`, 계약의 네임스페이스 |
| 서버(.NET, `Server~/`) | `AddRealtimeRpc`, `MapRealtimeRpc`, `IRpcContext`, `IRealtimeSession(s)`, `RpcStatusException`, 생성 코드 `GeneratedRpcRoutes` | `Geuneda.Realtime.Server` |

## 설치

`Packages/manifest.json` 에 추가합니다. core 와 UniTask 는 git 의존성이라 함께 넣어야 합니다. Newtonsoft Json 은 Unity 레지스트리에서 자동으로 설치됩니다.

```json
{
  "dependencies": {
    "com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask",
    "com.geuneda.core": "https://github.com/geuneda/geuneda-core.git",
    "com.geuneda.realtime": "https://github.com/geuneda/geuneda-realtime.git"
  }
}
```

### MessagePack(선택)

`Geuneda.Realtime.MessagePack` 어셈블리는 MessagePack 이 있을 때만 컴파일됩니다. UnityNuGet 레지스트리로 넣는 경우:

```json
{
  "dependencies": {
    "org.nuget.messagepack": "3.1.12",
    "org.nuget.microsoft.net.stringtools": "1.0.0"
  },
  "scopedRegistries": [
    { "name": "Unity NuGet", "url": "https://unitynuget-registry.openupm.com", "scopes": ["org.nuget"] }
  ]
}
```

- UnityNuGet 의 `org.nuget.messagepack` 3.x 는 `Microsoft.NET.StringTools` 의존성이 빠져 있어 `MessagePack.dll` 이 로드되지 않습니다. `org.nuget.microsoft.net.stringtools` 를 함께 넣습니다(3.1.12 + 1.0.0 조합을 EditMode 테스트로 확인).
- NuGetForUnity 같은 다른 방법으로 넣었다면 Player Settings 의 Scripting Define Symbols 에 `GEUNEDA_MESSAGEPACK` 을 추가합니다.

## 사용법

### 계약 쓰기

서버와 클라이언트가 함께 컴파일하는 계약 어셈블리를 둡니다. Unity 에서는 asmdef 하나(예: `Assets/Contracts/Game.Contracts.asmdef`)로, references 에 `Geuneda.Realtime`, `Geuneda.Core`, `UniTask` 를 넣고 `noEngineReferences` 를 켭니다(서버가 Unity 없이 같은 파일을 컴파일).

```csharp
using System.Threading.Tasks;
using Geuneda.Realtime;

namespace Game.Contracts
{
    [RpcService]
    public interface IShopRpc
    {
        Task<BuyProductRes> BuyProduct(BuyProductReq request);   // 응답 있음
        Task MarkSeen(MarkSeenReq request);                      // 응답 본문 없음
        Task<ShopStateRes> GetState();                           // 인자 없음

        public const string StockChangedTopic = "Shop/StockChanged";

        public sealed class BuyProductReq { public int ProductId; }
        public sealed class BuyProductRes { public int Gold; }
        public sealed class MarkSeenReq { public int ProductId; }
        public sealed class ShopStateRes { public int[] Products; }
    }

    [RpcService]
    public interface ISessionRpc
    {
        [RpcAllowAnonymous]                                      // 로그인 전에 부를 수 있는 메서드
        Task<LoginRes> Login(LoginReq request);

        public sealed class LoginReq { public string AccessToken; }
        public sealed class LoginRes { public string UserId; }
    }
}
```

- 메서드 모양은 `Task<TRes> M(TReq)`, `Task M(TReq)`, `Task<TRes> M()` 이고, 서버 구현용으로 끝에 `CancellationToken` 을 더 받을 수 있습니다. 경로 이름은 `"인터페이스 이름/메서드 이름"`, ID 는 그 FNV-1a 해시입니다.
- 요청·응답 형식과 상수는 인터페이스 안에 둘 수 있습니다(서비스마다 파일 하나).
- 생성기가 계약 규칙을 검사합니다. 위반은 빌드 오류입니다. 예약어 이름(`@event`)은 생성 코드에서 그대로 이스케이프되고, `[RpcService]` 는 partial 인터페이스의 어느 선언에 붙여도 됩니다.

| 진단 | 뜻 |
|------|----|
| GRPC001 | 반환 형식이 `Task`/`Task<T>` 가 아님(`ValueTask`, 동기 메서드 불가) |
| GRPC002 | 인자가 요청 하나 + 선택적 끝 `CancellationToken` 이 아님(ref, out, params 불가) |
| GRPC003 | 제네릭 메서드 |
| GRPC004 | 같은 이름의 메서드(오버로드) |
| GRPC005, GRPC006, GRPC007 | 제네릭 인터페이스, 다른 형식 안의 인터페이스, 다른 인터페이스를 상속 |
| GRPC008 | 속성, 이벤트, static 멤버, 기본 구현 메서드 |
| GRPC009 | 같은 인터페이스 이름이 여러 네임스페이스에 있음(경로 이름이 겹침) |
| GRPC010 | 경로 ID(FNV-1a) 충돌 |
| GRPC011 | 계약 asmdef 가 UniTask 나 Geuneda.Core 를 참조하지 않음 |
| GRPC012 | 메서드 이름이 생성 코드 이름(`XxxClient`, `IXxxClient`, `XxxMethods`, `_caller`)과 같음 |
| GRPC013 | 같은 네임스페이스의 두 서비스가 같은 생성 형식 이름을 만듦(`IShopRpc` 와 `ShopRpc`) |
| GRPC014 | 생성할 형식(`XxxClient` 등, 서버는 `GeneratedRpcRoutes`)이 이미 있음 |

### 클라이언트

계약 asmdef 가 `Geuneda.Realtime` 을 참조하면 생성기가 서비스마다 `ShopRpcClient`(`IShopRpcClient`)와 메서드 경로 표 `ShopRpcMethods` 를 만듭니다.

```csharp
using System;
using Geuneda.Core;
using Geuneda.Realtime;
using Game.Contracts;

// 부트스트랩: 한 번 만들어 인터페이스로 바인딩한다
var client = new RealtimeClient(new RealtimeClientOptions
{
    ServerUri = new Uri(gameConfig.RealtimeUrl),               // 예: wss://game.example.com/rpc
    LoginAsync = async (caller, token) =>
        await new SessionRpcClient(caller).Login(new ISessionRpc.LoginReq { AccessToken = auth.AccessToken }, token)
});
client.Disconnected += info => { if (!info.WillReconnect) ShowDisconnectedPopup(info.CloseStatus); };
client.Reconnected += result => { if (!result.IsSuccess) ShowReconnectFailedPopup(); };
MainInstaller.Bind<IRealtimeClient>(client);
MainInstaller.Bind<IShopRpcClient>(new ShopRpcClient(client));

// 사용하는 쪽: 연결이 없으면 첫 호출이 연결한다(ConnectOnCall)
var result = await shop.BuyProduct(new IShopRpc.BuyProductReq { ProductId = id }, token);
if (result.TryGetValue(out var response))
{
    Apply(response);
}
else if (result.Code == GameErrors.NotEnoughGold)
{
    ShowNotEnoughGold();
}

// 서버 푸시: Dispose 로 구독 해제
_stockSubscription = realtime.Subscribe<StockChanged>(RpcTopic.Of(IShopRpc.StockChangedTopic), OnStockChanged);
```

생성 코드 없이 `RpcMethod.Of("IShopRpc/BuyProduct")` 와 `IRpcCaller.CallAsync` 로 직접 불러도 됩니다(프록시가 하는 일과 같음).

### 결과

| 상황 | 결과 |
|------|------|
| 응답 상태 0 | 성공. 본문을 직렬화로 읽은 값 |
| 응답 상태 100000 이상 | 그 값이 실패 코드. 100000~100999 는 서버 프레임워크(`RpcStatusCodes`: `InternalError` 100000, `Unauthenticated` 100001, `MethodNotFound` 100002, `BadRequest` 100003, `Overloaded` 100004), 101000 부터 게임 |
| 응답 상태가 그 밖의 값(음수, 1~99999), 본문을 읽지 못함 | `RealtimeResultCodes.InvalidResponse` |
| 연결 실패, 보내기 실패, 응답 전에 끊김 | `ResultCodes.NetworkError` |
| 요청 제한 시간(`RequestTimeout`) 초과 | `ResultCodes.Timeout`(연결은 유지, 늦은 응답은 버림) |
| 연결+로그인 제한 시간(`ConnectTimeout`) 초과 | `ResultCodes.Timeout` |
| 호출자 취소 | `ResultCodes.Cancelled`(예외 아님) |
| 로그인 실패 | 로그인 대리자가 돌려준 코드. 연결은 닫음 |
| 로그인 대리자가 예외를 던짐(취소 제외) | 그 예외를 연결을 기다리던 호출 모두에 그대로 던짐. 연결은 닫음 |
| 서버 정책 종료(4000~4999, 로그인 중 포함) 뒤의 호출·연결 | `RealtimeResultCodes.NotReconnectable`. `ConnectAsync` 로 다시 연결 |
| `ConnectOnCall = false` 이고 연결 없음 | `RealtimeResultCodes.NotConnected` |
| 서버가 서브프로토콜(형식)을 돌려주지 않거나 다른 것을 고름 | `RealtimeResultCodes.ProtocolMismatch`(재연결하지 않음) |
| `Dispose` 로 끝난 호출·연결 | `RealtimeResultCodes.Closed` |
| `Dispose` 뒤의 호출, 잘못된 옵션, null 핸들러 | 예외(오용) |

- 제한 시간과 취소는 보내기 차례를 기다릴 때부터 적용합니다. 차례가 오기 전에 끝난 요청은 서버에 보내지 않습니다. 이미 보내는 중이거나 보낸 요청은 서버가 처리할 수 있으므로(결과만 버림), 구매 같은 요청은 서버에서 멱등하게 처리합니다.
- 호출과 연결 결과, 로그인 대리자는 메인 스레드에서 이어집니다. `Disconnected`, `Reconnected`, 푸시 핸들러는 `CallbackDispatcher`(기본 메인 스레드 PlayerLoop)에서 불리고, 핸들러 예외는 로그만 남깁니다.
- 받은 메시지 조립과 프레임 해석, 응답·푸시 역직렬화는 백그라운드 스레드에서 할 수 있습니다(큰 메시지가 조각마다 메인 스레드를 거치지 않게). 직접 만든 `IRpcSerializer` 와 `CallbackDispatcher` 는 스레드 안전해야 합니다.
- 읽지 못한 푸시는 경고 로그를 남기고 버립니다. 같은 토픽에 여러 핸들러를 붙일 수 있습니다. 구독을 해제했거나 클라이언트를 `Dispose` 했으면 이미 디스패처에 넘긴 푸시도 실행하지 않습니다.

### 연결과 재연결

- 동시에 여러 곳에서 연결을 요청해도 한 번만 연결하고 결과를 함께 받습니다. 연결할 때마다(재연결 포함) `LoginAsync` 를 다시 부릅니다. 토큰은 URL 이 아니라 첫 메시지(로그인 RPC)로 보냅니다.
- 끊기면 대기 중인 호출은 `NetworkError` 로 끝나고, `Disconnected(WillReconnect: true)` 뒤 `ReconnectBaseDelay × 2^n`(최대 `ReconnectMaxDelay`)의 50~100% 를 기다리며 `MaxReconnectAttempts` 번까지 다시 연결합니다. 결과는 `Reconnected` 로 알립니다. 로그인 거부처럼 일시적이지 않은 실패는 바로 멈춥니다.
- 서버가 4000~4999 로 닫으면(중복 로그인 4001, 점검 4002, 강제 종료 4003 등) 재연결하지 않습니다(`WillReconnect: false`). 로그인 중(재연결의 로그인 포함)에 받아도 같습니다.
- 재연결 중에 다시 끊기면 진행 중인 재연결이 이어서 맡습니다(`WillReconnect: true`). `Reconnected` 는 재연결이 끝날 때 한 번 옵니다.
- 핑은 `PingInterval` 마다 보내고, 퐁이 `PingInterval + PingTimeout` 동안 없으면 끊긴 것으로 봅니다(`Disconnected` 의 상태 1006, 이유 `Ping timeout`).

## 서버 (ASP.NET Core)

`Server~/` 폴더(Unity 가 가져오지 않음)에 .NET 10 솔루션 `Geuneda.Realtime.Server.slnx` 가 있습니다.

| 프로젝트 | 내용 |
|----------|------|
| `src/Geuneda.Realtime.Server` | 서버 라이브러리. 프레임 코덱·직렬화·계약 표시·코드 파일은 이 패키지의 `Runtime/` 을 링크해 클라이언트와 같은 파일을 씀 |
| `src/Geuneda.Realtime.Generator` | 소스 생성기(netstandard2.0, Roslyn 4.3). Release 빌드가 `Runtime/Generator/Geuneda.Realtime.Generator.dll` 을 갱신 |
| `samples/Geuneda.Realtime.SampleServer` | Unity 테스트의 계약(`Tests/EditMode/Interop`)을 링크한 샘플 서버 |
| `tests/*` | 서버(실제 Kestrel), 생성기 테스트 |

게임 서버는 Unity 프로젝트의 계약 폴더를 링크하고 생성기를 analyzer 로 참조합니다(아직 NuGet 패키지가 아니라 이 패키지 체크아웃을 경로로 참조).

```xml
<!-- Game.Server.csproj -->
<ItemGroup>
  <Compile Include="../GameUnity/Assets/Contracts/**/*.cs" LinkBase="Contracts" />
  <ProjectReference Include="../geuneda-realtime/Server~/src/Geuneda.Realtime.Server/Geuneda.Realtime.Server.csproj" />
  <ProjectReference Include="../geuneda-realtime/Server~/src/Geuneda.Realtime.Generator/Geuneda.Realtime.Generator.csproj"
                    OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
</ItemGroup>
```

```csharp
// Program.cs. 생성기가 이 어셈블리 이름의 네임스페이스에 GeneratedRpcRoutes 를 만든다
builder.Services.AddScoped<ISessionRpc, SessionRpc>();
builder.Services.AddScoped<IShopRpc, ShopRpc>();
builder.Services.AddRealtimeRpc(options => options.AddRoutes(GeneratedRpcRoutes.All));
var app = builder.Build();
app.UseWebSockets();
app.MapRealtimeRpc("/rpc");      // 라우트 ID 중복, 구현 미등록, 설정 오류는 여기서 예외

// 구현: 계약 인터페이스를 그대로 구현한다(호출마다 DI 범위)
public sealed class ShopRpc : IShopRpc
{
    private readonly IRpcContext _context;
    public ShopRpc(IRpcContext context) => _context = context;

    public async Task<IShopRpc.BuyProductRes> BuyProduct(IShopRpc.BuyProductReq request)
    {
        var userId = _context.Session.UserId;
        if (!await _wallet.TrySpend(userId, price)) throw new RpcStatusException(GameErrors.NotEnoughGold); // 101000 이상
        return new IShopRpc.BuyProductRes { Gold = ... };
    }
    ...
}
// 로그인: _context.SignIn(userId). 같은 사용자의 이전 세션은 4001 로 닫힘
// 다른 사용자에게 푸시: sessions.FindByUser(id)?.PushAsync(RpcTopic.Of(IShopRpc.StockChangedTopic), message)
```

| 서버 동작 | 내용 |
|-----------|------|
| 서브프로토콜 | 클라이언트가 요청한 것 중 `options.Serializers` 에 있는 첫 형식(기본 JSON, MessagePack 은 추가). 없으면 서브프로토콜 없이 받고 1002 로 닫음(클라이언트는 `ProtocolMismatch`) |
| 로그인 | `[RpcAllowAnonymous]` 가 없는 메서드는 `IRpcContext.SignIn` 전에 `Unauthenticated`(100001). 닫히기 시작한 세션의 `SignIn` 은 `OperationCanceledException`(늦게 끝난 로그인이 새 세션을 밀어내지 않음) |
| 실행 순서 | 세션마다 받은 순서대로 하나씩 실행. 대기가 `MaxQueuedRequests`(256)를 넘으면 그 요청은 `Overloaded`(100004)로 바로 거절(받기·핑은 계속). 핑은 받기 루프가 바로 답함 |
| 실패 | `RpcStatusException(status)` → 그 상태(로그 없음), 요청 본문 해석 실패 → `BadRequest`(100003), 없는 메서드 → `MethodNotFound`(100002), 그 밖의 예외 → `InternalError`(100000, 오류 로그) |
| 종료 | 클라이언트 종료에는 같은 코드로 답함. 텍스트 1003, `MaxMessageSize`(16MB) 초과 1009, 잘못된 프레임 1002, `IdleTimeout`(60초) 동안 아무 메시지 없음 1001. 서버가 닫으면 클라이언트 답을 `CloseTimeout`(5초)까지 기다림. 보낼 수 없는 코드(1006 등)의 `CloseAsync` 는 예외 |
| 닫힐 때 호출 | 종료가 시작되면 대기 중인 요청은 실행하지 않고, 진행 중인 호출의 `IRpcContext.CallAborted`(계약의 `CancellationToken`)를 취소. 연결 정리는 진행 중인 호출을 기다리지 않음(그 호출은 끝까지 돌고 결과는 버림, 스레드를 막는 구현이어도 같음) |
| MessagePack | `MessagePackRpcSerializer` 의 기본 옵션은 `UntrustedData` 보안. 옵션을 직접 넘길 때도 서버는 `.WithSecurity(MessagePackSecurity.UntrustedData)` 를 붙임 |

```bash
dotnet test "Server~/Geuneda.Realtime.Server.slnx"                              # 서버·생성기 테스트
dotnet build "Server~/src/Geuneda.Realtime.Generator" -c Release                # 생성기를 고쳤으면 Unity 용 DLL 갱신
dotnet run --project "Server~/samples/Geuneda.Realtime.SampleServer" -- --urls http://127.0.0.1:5199
```

## 프로토콜(서버 구현용)

WebSocket 바이너리 메시지 하나가 프레임 하나입니다. 서브프로토콜은 `geuneda.rpc.v1.<직렬화 이름>`(`geuneda.rpc.v1.json`, `geuneda.rpc.v1.msgpack`)이고, 서버는 같은 값을 돌려줘야 합니다.

| 종류(1바이트) | 헤더(빅엔디언) | 본문 |
|---------------|----------------|------|
| `1` Request(클라이언트 → 서버) | 종류, flags(0), 요청 ID(u32), 메서드 ID(u32) — 10바이트 | 요청 |
| `2` Response(서버 → 클라이언트) | 종류, flags(0), 요청 ID(u32), 상태(i32, 0 성공 또는 100000 이상, `RpcStatusCodes`) — 10바이트 | 응답(상태 0 일 때) |
| `3` Push(서버 → 클라이언트) | 종류, flags(0), 토픽 ID(u32) — 6바이트 | 푸시 |
| `4` Ping, `5` Pong(양방향) | 종류, flags(0), 순번(u32) — 6바이트 | 없음 |

- 메서드·토픽 ID 는 이름(UTF-8)의 FNV-1a 32비트 해시입니다(`Fnv1a32.Hash`). 생성기와 서버(`MapRealtimeRpc`)가 ID 충돌을 검사합니다.
- 클라이언트 요청 ID 는 1부터 늘어나고 0 은 쓰지 않습니다. 서버는 응답에 같은 ID 를 돌려줍니다.
- 서버는 Ping 에 같은 순번의 Pong 으로 답합니다. 클라이언트도 서버 Ping 에 Pong 으로 답합니다.
- flags 는 0 이고 Ping·Pong 은 정확히 6바이트입니다. 잘못된 프레임을 받으면 클라이언트는 1002 로 닫고 재연결합니다. 텍스트 메시지는 1003, 16MB 를 넘는 메시지는 1009 로 닫습니다.
- 서버가 닫으면 클라이언트는 받은 종료 코드를 그대로 돌려보냅니다(보낼 수 없는 1005·1006·1015 는 1000).
- 서버 재시작 종료에는 1001 을 씁니다. Unity(Mono)의 `ClientWebSocket` 은 1012~1014 를 받으면 무효 코드로 보고 끊으므로 클라이언트에는 1006 으로 보입니다(재연결은 함).
- 종료 설명은 123바이트 제한이 있어 클라이언트는 싣지 않습니다(이유는 로그와 `Disconnected` 로 전달).

## 요구 사항

- Unity 2021.3 이상. WebGL 은 `ClientWebSocket` 을 쓸 수 없어 별도 `IRealtimeTransport` 가 필요합니다.
- 소스 생성기는 Microsoft.CodeAnalysis 4.3 API 만 써서 Roslyn 4.3 이 들어 있는 Unity 에서 돕니다(6000.3 에서 확인). 생성기가 돌지 않는 Unity 에서는 프록시 없이 `RpcMethod` 와 `CallAsync` 로 부릅니다.
- 서버: .NET 10(ASP.NET Core), Newtonsoft.Json 13, MessagePack 3.1.
- `com.geuneda.core` 0.1.0, `com.cysharp.unitask` 2.5.10, `com.unity.nuget.newtonsoft-json` 3.2.1
- MessagePack(선택): MessagePack-CSharp 3.x. IL2CPP 에서는 소스 생성기가 만든 포매터가 들어 있는 리졸버를 `MessagePackRpcSerializer(options)` 로 넘깁니다.

변경 이력은 [CHANGELOG.md](CHANGELOG.md) 를 참고하세요.

## 라이선스

MIT. [LICENSE.md](LICENSE.md) 를 참고하세요.
