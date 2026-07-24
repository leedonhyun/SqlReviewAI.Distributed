# SqlReviewAI.Distributed

`SqlReviewAI` (단일 프로세스 버전)을 아래 아키텍처로 재구성한 버전입니다.

```
클라이언트 (웹/IDE) → SignalR Hub / HTTP API (ASP.NET Core) → Orleans Silo Cluster (5개 Grain)
                                                                        │
                                                          Nerdbank.Streams로 4채널 분리
                                                          (Rule/RAG/LLM/Logs)
```

## ⚠️ 먼저 읽어주세요: 무엇을 검증했고 무엇을 검증하지 못했는지

이 샌드박스는 **nuget.org에 대한 네트워크 접근이 차단**되어 있고 **.NET 9 SDK가 설치되어 있지
않습니다.** 그래서 프로젝트마다 검증 수준이 다릅니다:

| 프로젝트 | 의존성 | 이 환경에서 검증됨? |
|---|---|---|
| `SqlReviewAI.Core` | 없음 (BCL만) | ✅ 빌드 + 이전 대화에서 실제 실행까지 완료 |
| `SqlReviewAI.Orchestration` | Core만 | ✅ 빌드 완료 |
| `SqlReviewAI.Web` | Core, Orchestration + (net9용 OpenAPI 패키지) | ✅ **OpenAPI/Swagger 두 줄만 제외하면 실제로 빌드·실행하고, SignalR Hub(단발성 `Review()` + 스트리밍 `ReviewStream()`)와 HTTP API를 Node.js `@microsoft/signalr` 클라이언트로 직접 호출해서 4채널(Rules/Rag/Llm/Logs) 전부 검증함.** OpenAPI 패키지는 net9 SDK가 없어 컴파일만 못 해봄 — 로직과는 무관. |
| `SqlReviewAI.Contracts` | Microsoft.Orleans.Sdk | ❌ NuGet 복원 불가로 미검증 |
| `SqlReviewAI.Grains` | Contracts, Orleans.Sdk | ❌ 미검증 |
| `SqlReviewAI.Silo` | Grains, Orleans.Server | ❌ 미검증 |
| `SqlReviewAI.Web.OrleansIntegration` | Contracts, Orleans.Client | ❌ 미검증 |
| `SqlReviewAI.Streaming` | Contracts, Nerdbank.Streams | ❌ 미검증 |

**즉: 핵심 비즈니스 로직(파싱→규칙→RAG→점수→설명)과 Web의 SignalR/HTTP 계층은 실제로
돌려서 검증했습니다.** Orleans/Nerdbank.Streams가 들어가는 부분은 잘 알려진 공개 API를
근거로 신중하게 작성했지만, 여러분 환경에서 `dotnet restore && dotnet build`로 직접
확인해주세요. 버전별 API 미세 차이가 있다면 소폭 수정이 필요할 수 있습니다.

### 실제로 검증한 것 (재현 가능)

```
$ curl -X POST http://localhost:5000/api/corpus/default/ingest -d @corpus.json
{"corpusId":"default","totalStatements":70}

$ curl -X POST http://localhost:5000/api/review -d '{"sql":"UPDATE MEMBER\nSET NAME=@NAME\n"}'
{"score":65,"riskLevel":3,"findings":[{"ruleCode":"MISSING_WHERE","severity":5, ...
 "evidence":"MEMBER UPDATE 47건 중 47건(100.0%)이 WHERE 절을 사용했습니다." ...}]}
```

```js
// Node.js @microsoft/signalr 클라이언트로 확인
const stream = connection.stream("ReviewStream", "default", crossJoinSql);
stream.subscribe({ next: e => console.log(e.channel, e.kind, e.payloadJson) });
// → channel=3(Logs) "리뷰 시작..."
// → channel=0(Rules) {"RuleCode":"UNUSUAL_JOIN_TYPE", "Severity":4, ...}
// → channel=1(Rag)   {"SourceFile":"select_member_order_1.sql", "SimilarityScore":0.918, ...}
// → channel=2(Llm)   "분석" "결과:" "-" "이례적인" ... (토큰 단위 스트리밍)
```

## 프로젝트 구조

```
SqlReviewAI.Distributed.sln
corpus/, samples/                    이전과 동일한 예제 데이터

src/
  SqlReviewAI.Core/                  의존성 없음 — 파싱/규칙/통계/RAG/LLM 파이프라인 본체
                                      (단일 프로세스 버전과 동일 + 스트리밍 LLM 인터페이스 추가)

  SqlReviewAI.Contracts/             Orleans Grain 인터페이스 + [GenerateSerializer] DTO
    GrainInterfaces.cs                ISqlReviewGrain, ICorpusStatsGrain, IRuleEngineGrain,
                                       IRagGrain, ILlmGrain
    Dtos.cs                           SqlFeaturesDto, RuleFindingDto, ReviewResultDto,
                                       ReviewProgressEvent(4채널 이벤트) 등

  SqlReviewAI.Grains/                 5개 Grain 구현체 (다이어그램과 1:1 대응)
    SqlReviewGrain.cs                 SQL 리뷰 오케스트레이션 — 나머지 4개 Grain을 호출/집계
    CorpusStatsGrain.cs                코퍼스별 단일 활성화, 통계 상태 보유
    RuleEngineGrain.cs                 [StatelessWorker] — Core.RuleEngine 래핑
    RagGrain.cs                        코퍼스별 단일 활성화, 벡터 인덱스 보유
    LlmGrain.cs                        [StatelessWorker] — Ollama 호출, 토큰 스트리밍 지원
    Mapping.cs                         Core 도메인 모델 ↔ Contracts DTO 변환

  SqlReviewAI.Silo/                   Orleans Silo 호스트 (localhost clustering)
    Program.cs                         DI 등록 + UseOrleans() 구성

  SqlReviewAI.Orchestration/          IReviewOrchestrator 추상화 (Web과 Orleans 어느 쪽도
                                       직접 의존하지 않는 중립 계층 — 순환 참조 방지)
    IReviewOrchestrator.cs
    InProcessReviewOrchestrator.cs     기본 구현: Orleans 없이 Web 프로세스 안에서 전체 파이프라인 실행
    ReviewProgressEvent.cs             Web 계층용 4채널 이벤트 타입 (Orleans 비의존)

  SqlReviewAI.Web/                    ASP.NET Core — SignalR Hub + Minimal API + Swagger/OpenAPI
    Hubs/SqlReviewHub.cs                Review / ReviewStream / Ask
    Program.cs                          기본은 InProcessReviewOrchestrator로 동작(Orleans 불필요)
    ApiModels.cs

  SqlReviewAI.Web.OrleansIntegration/  OrleansReviewOrchestrator — IReviewOrchestrator를
                                       Orleans IClusterClient로 구현 (Web이 선택적으로 참조)

  SqlReviewAI.Streaming/              Nerdbank.Streams MultiplexingStream으로 4채널을
                                       하나의 연결 위에 분리하는 재사용 가능한 헬퍼
```

## 왜 `SqlReviewAI.Orchestration`이 별도 프로젝트인가요?

`IReviewOrchestrator`에 구현체가 두 개입니다.

- **`InProcessReviewOrchestrator`** (Orchestration 프로젝트) — Orleans 없이 Web 프로세스
  안에서 전체 파이프라인을 직접 실행합니다. **기본값이며, 실제로 빌드·실행·검증했습니다.**
- **`OrleansReviewOrchestrator`** (Web.OrleansIntegration 프로젝트) — `ISqlReviewGrain`을
  호출해 Silo 클러스터에 위임합니다.

`IReviewOrchestrator` 인터페이스 자체를 Web 프로젝트 밖(Orchestration 프로젝트)에 둔 이유는,
Web → OrleansIntegration → (다시) Web 순환 참조를 피하기 위해서입니다. Web은 항상
Orchestration만 참조하고, 두 구현체 중 하나를 DI에 등록하는 방식으로 전환합니다.

## 실행 방법

### A. Orleans 없이 바로 실행 (기본값, 검증된 경로)

```bash
cd src/SqlReviewAI.Web
# TargetFramework를 net8.0으로 낮추고 OpenAPI 두 줄을 지우면 .NET 8 SDK로도 실행됩니다.
# (.NET 9 SDK가 있다면 그대로 실행)
dotnet run
```

기본적으로 `../../corpus`의 `.sql` 파일들을 `"default"` 코퍼스로 자동 로드합니다
(레포 루트 기준 상대 경로 — 실행 위치가 다르면 `POST /api/corpus/default/ingest`로 직접 채우세요).

- Swagger UI: `http://localhost:5000/swagger`
- SQL 리뷰: `POST /api/review { "sql": "..." }`
- SignalR Hub: `/hubs/sql-review` — `Review`, `ReviewStream`(스트리밍), `Ask`

### B. Orleans Silo + Web (다이어그램대로, 미검증 — 직접 확인 필요)

```bash
# 터미널 1
cd src/SqlReviewAI.Silo
dotnet run

# 터미널 2 — Web.csproj에서 두 가지를 바꾼 뒤:
#   1) OrleansIntegration ProjectReference 주석 해제
#   2) Program.cs에서 InProcessReviewOrchestrator 등록 대신:
#        builder.Host.UseSqlReviewOrleansClient();
#        builder.Services.AddOrleansReviewOrchestrator();
cd src/SqlReviewAI.Web
dotnet run
```

### C. Ollama 연동 (실제 RAG 임베딩 + Qwen3 설명)

Silo와 Web 둘 다 `OLLAMA_URL` 환경변수(또는 Web의 `Ollama:Url` 설정)를 읽습니다.

```bash
ollama pull qwen3:14b
ollama pull nomic-embed-text
ollama serve

OLLAMA_URL=http://localhost:11434 dotnet run   # Silo 또는 Web에서
```

미설정 시 오프라인 모드(해싱 임베딩 + 템플릿 설명)로 동작하며, 이전 단일 프로세스 버전과
동일하게 완전한 리포트를 생성합니다.

## Grain 설계 메모

- **`SqlReviewGrain`**: 코퍼스 id로 키. 상태 없음 — `CorpusStatsGrain`(같은 키),
  `RagGrain`(같은 키), `RuleEngineGrain`/`LlmGrain`(공유, 정수 키 0)을 호출해 집계만 합니다.
- **`CorpusStatsGrain` / `RagGrain`**: 코퍼스별 단일 활성화. Orleans가 키당 하나의 활성화만
  보장하므로 동시 ingest가 자연스럽게 직렬화됩니다. 이 레퍼런스 구현은 상태를 메모리에만
  유지합니다 — 운영에서는 `[PersistentState]`로 스토리지 프로바이더를 붙이세요.
- **`RuleEngineGrain` / `LlmGrain`**: `[StatelessWorker]` — 상태가 없는 순수 계산/호출이라
  Orleans가 실로당 여러 활성화를 동시에 띄워 처리량을 낼 수 있습니다.
- **`ISqlReviewGrain.ReviewStreamAsync`**가 `IAsyncEnumerable<T>`를 직접 반환합니다 —
  Orleans 7.2+의 grain-call streaming 지원을 사용합니다 (별도 Streams 프로바이더 설정 불필요,
  Orleans Streams pub/sub 서브시스템과는 다른 기능입니다). 사용 중인 Orleans 버전이 이를
  지원하는지 확인하세요.

## Nerdbank.Streams 4채널 (`SqlReviewAI.Streaming`)

`ReviewChannelMultiplexer`는 duplex `Stream` 하나를 `rules`/`rag`/`llm`/`logs` 4개 채널로
분리합니다 (`MultiplexingStream.OfferChannelAsync`/`AcceptChannelAsync`, 채널당 4바이트
길이 프리픽스 + UTF-8 JSON 프레임).

```csharp
// 발신측 (예: Silo 워커, 또는 Web 내부 파이프라인 프로세스)
var mux = await ReviewChannelMultiplexer.OpenAsOfferingSideAsync(someDuplexStream);
await mux.WriteAsync(new ReviewProgressEvent(ReviewChannel.Rules, "finding", json, DateTimeOffset.UtcNow));

// 수신측 (예: Web이 SignalR로 중계)
var mux = await ReviewChannelMultiplexer.OpenAsAcceptingSideAsync(sameDuplexStream);
await foreach (var evt in mux.ReadChannelAsync(ReviewChannels.Rules)) { /* relay to SignalR */ }
```

완전히 프로세스 내부에서 테스트하려면 `Nerdbank.Streams.FullDuplexStream.CreatePair()`로
만든 인메모리 루프백 스트림 쌍을 양쪽에 각각 넘기면 됩니다 (네트워크 불필요).

**현재 `SqlReviewAI.Web`은 이 멀티플렉서를 사용하지 않고, `IAsyncEnumerable`을 SignalR
스트리밍 Hub 메서드로 직접 중계합니다** (더 간단하고, 실제로 검증한 경로입니다). 여러
프로세스/네트워크 경계에서 4채널을 물리적으로 분리해야 하는 배포 형태라면
`SqlReviewAI.Streaming`을 Web/Silo 사이에 끼워 넣으세요.

## 이전 버전(SqlReviewAI, 단일 프로세스)과 무엇이 다른가요?

`SqlReviewAI.Core`는 동일합니다(+ 토큰 스트리밍 LLM 인터페이스 추가). 이번 버전은 그 위에
분산 오케스트레이션 계층(Orleans Grain), 실시간 클라이언트 계층(SignalR + Swagger), 그리고
선택적 저수준 멀티플렉싱 전송(Nerdbank.Streams)을 얹은 것입니다. 규칙 엔진, 통계 분석,
RAG, 점수 계산 로직 자체는 바뀌지 않았습니다.
