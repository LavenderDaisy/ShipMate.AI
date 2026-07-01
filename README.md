# ShipMate AI — Conversational Multi-Carrier Shipping Copilot

A natural-language shipping assistant built on **.NET 8** and **Microsoft Semantic Kernel**.
Users describe what they want in plain English ("find the cheapest overnight option and
ship it"), and a large language model **autonomously orchestrates** carrier tools — rating,
booking, and tracking — to fulfil the request.

> The AI layer is deliberately decoupled from carrier integration. The LLM handles
> *intent understanding* and *tool orchestration*; the carrier engines do the *real work*
> of rating, shipping, and tracking. Swapping a mock carrier for a live UPS/FedEx/EasyPost
> API requires **no change to the AI layer**.

---

## Highlights

- **LLM Function Calling** — carrier operations are exposed to the model as typed,
  self-describing tools via `[KernelFunction]` + `[Description]` attributes.
- **Multi-step agent orchestration** — a single request can trigger a chain of tool
  calls (rate → ship → track) that the model plans on its own.
- **Cross-tool state** — a tracking number minted by `create_shipment` is resolvable by
  `track_shipment` later in the session.
- **Pluggable LLM backend** — Azure OpenAI, OpenAI, any OpenAI-compatible provider
  (DeepSeek / Qwen / Zhipu), or local **Ollama** — selectable via configuration.
- **Carrier-agnostic abstraction** — `ICarrierRateEngine` mirrors the real
  StarShip `CarrierEngine` rate-transaction dispatch pattern, so production carrier
  integrations drop in cleanly. Ships with both a live **EasyPost** engine and a mock.
- **Tested** — NUnit suite covers rate parsing/de-duplication, ZPL generation, rate
  aggregation, and ZPL printing, running fully offline against captured samples.
- **Real label printing** — sends 4x6 ZPL to a label printer via the Windows print
  spooler (raw) or TCP 9100, and can buy a real carrier label from EasyPost.
- **Secure config** — API keys via .NET user-secrets, never committed to source.

---

## Architecture

```mermaid
flowchart TB
    U["User (console)<br/>natural language"]

    subgraph AI["AI Orchestration Layer"]
        SK["Semantic Kernel<br/>(ChatCompletion + auto tool-calling)"]
        LLM["LLM backend<br/>Azure OpenAI / OpenAI-compatible / Ollama"]
    end

    subgraph Tools["Kernel Plugins (LLM-callable tools)"]
        RP["RatePlugin<br/>get_shipping_rates"]
        SP["ShipPlugin<br/>create_shipment"]
        TP["TrackPlugin<br/>track_shipment"]
        LP["LabelPrintPlugin<br/>render_label"]
        PP["PrintLabelPlugin<br/>print_label / buy_and_print_carrier_label"]
    end

    subgraph Carriers["Carrier Integration Layer"]
        RS["RatingService"]
        SS["ShippingService"]
        STORE["ShipmentStore"]
        CE["MockCarrierRateEngine x3<br/>UPS / FedEx / USPS"]
        EP["EasyPostRateEngine / LabelService<br/>(live)"]
    end

    subgraph Print["Printing Layer"]
        ZP["IZplPrinter"]
        WIN["WindowsRawZplPrinter<br/>(spooler RAW)"]
        TCP["TcpZplPrinter<br/>(port 9100)"]
    end

    U --> SK
    SK <--> LLM
    SK --> RP
    SK --> SP
    SK --> TP
    SK --> LP
    SK --> PP
    RP --> RS
    SP --> SS
    TP --> SS
    LP --> STORE
    PP --> ZP
    PP --> EP
    ZP --> WIN
    ZP --> TCP
    RS --> CE
    SS --> RS
    SS --> STORE
```

### Request flow: "find the cheapest overnight and ship it"

```mermaid
sequenceDiagram
    participant U as User
    participant SK as Semantic Kernel
    participant LLM as LLM
    participant R as get_shipping_rates
    participant S as create_shipment

    U->>SK: "cheapest overnight 30301→10001, 5 lb, then ship it"
    SK->>LLM: prompt + tool schemas
    LLM-->>SK: call get_shipping_rates(service=Overnight)
    SK->>R: execute (C#)
    R-->>SK: quotes sorted by price
    SK->>LLM: tool result
    LLM-->>SK: call create_shipment(carrier=cheapest, Overnight)
    SK->>S: execute (C#) → mints tracking number
    S-->>SK: tracking number + ETA
    SK->>LLM: tool result
    LLM-->>U: "Booked with USPS Overnight, tracking US123456789, ETA ..."
```

---

## Project layout

```
ShipMate.AI/
├─ ShipMate.AI.slnx
├─ NuGet.config                      # nuget.org only (standalone)
├─ src/ShipMate.AI.Console/           # console host + shared domain logic
│  ├─ Program.cs                      # console host: config, chat loop, --dump
│  ├─ ShipMateKernelFactory.cs        # shared kernel/service assembly (used by both hosts)
│  ├─ appsettings.json                # Provider + backend settings
│  ├─ Carriers/                       # carrier integration layer (swappable)
│  │  ├─ ICarrierRateEngine.cs        # rate-engine contract (mirrors CarrierEngine)
│  │  ├─ MockCarrierRateEngine.cs     # deterministic stand-in rate engine
│  │  ├─ EasyPostRateEngine.cs        # live multi-carrier rates via EasyPost API
│  │  ├─ EasyPostRateParser.cs        # pure JSON→RateQuote mapping (unit tested)
│  │  ├─ RateModels.cs                # RateRequest / RateQuote / ServiceLevel
│  │  ├─ RatingService.cs             # fans rate requests across carriers
│  │  ├─ ShipmentModels.cs            # ShipmentRequest / Result / TrackingInfo
│  │  ├─ IShipmentStore.cs            # shipment store contract
│  │  ├─ InMemoryShipmentStore.cs     # per-session in-memory store
│  │  ├─ MongoShipmentStore.cs        # durable MongoDB store (Atlas)
│  │  ├─ ShippingService.cs           # create shipment + synthesize tracking
│  │  ├─ EasyPostLabelService.cs      # buy a real carrier label (ZPL) via EasyPost
│  │  ├─ LabelModels.cs               # LabelFormat / LabelResult
│  │  └─ LabelService.cs              # render 4x6 ZPL label from a shipment
│  ├─ Printing/                       # ZPL printer transports
│  │  ├─ IZplPrinter.cs               # printer contract + PrintResult
│  │  ├─ WindowsRawZplPrinter.cs      # raw spooler print via winspool P/Invoke
│  │  ├─ TcpZplPrinter.cs             # network print to host:9100
│  │  └─ NullZplPrinter.cs            # no-op (Null Object) when unconfigured
│  └─ Plugins/                        # Semantic Kernel tools exposed to the LLM
│     ├─ RatePlugin.cs                # get_shipping_rates
│     ├─ ShipPlugin.cs                # create_shipment
│     ├─ TrackPlugin.cs               # track_shipment
│     ├─ LabelPrintPlugin.cs          # render_label (4x6 ZPL)
│     └─ PrintLabelPlugin.cs          # print_label / buy_and_print_carrier_label
├─ src/ShipMate.AI.Api/               # ASP.NET Core Minimal API + web chat UI
│  ├─ Program.cs                      # /api/chat endpoint + serves chat page
│  └─ ChatModels.cs                   # request/response types + embedded HTML
└─ tests/ShipMate.AI.Tests/           # NUnit test suite (32 tests)
   ├─ EasyPostRateParserTests.cs      # parse / dedupe / tier mapping
   ├─ LabelServiceTests.cs            # ZPL structure + file output
   ├─ RatingServiceTests.cs           # aggregation + cheapest-first sorting
   ├─ InMemoryShipmentStoreTests.cs   # store add / lookup / upsert
   ├─ NullZplPrinterTests.cs          # no-op printer behavior
   ├─ TcpZplPrinterTests.cs           # real loopback socket + failure path
   └─ PrintLabelPluginTests.cs        # print orchestration via fake printer
```

---

## Tech stack

| Area | Technology |
|---|---|
| Runtime | .NET 8 |
| AI orchestration | Microsoft Semantic Kernel |
| LLM backends | Azure OpenAI · OpenAI · OpenAI-compatible (DeepSeek/Qwen/Zhipu) · Ollama |
| Carrier rates | EasyPost API (live, multi-carrier) with mock fallback |
| Label printing | ZPL via Windows print spooler (raw) or TCP 9100 |
| Persistence | MongoDB Atlas (durable) with in-memory fallback |
| Web UI | ASP.NET Core Minimal API + embedded chat page |
| Observability | OpenTelemetry tracing (console exporter; OTLP-ready) |
| Capability | LLM function calling, multi-step tool orchestration |
| Config / secrets | Microsoft.Extensions.Configuration + user-secrets |
| Testing | NUnit 4 (32 tests, no API key required) |

---

## Design patterns

| Pattern | Where | Why |
|---|---|---|
| **Strategy** | `ICarrierRateEngine` → `MockCarrierRateEngine` / `EasyPostRateEngine` | Swap mock vs. live carrier rating at runtime via config; the AI layer never changes. |
| **Command** | `Plugins/*Plugin.cs` (`[KernelFunction]`) | Each carrier operation is encapsulated as a self-describing tool the LLM can invoke. |
| **Facade** | `RatingService`, `ShippingService` | A single entry point hides fan-out across carriers and result aggregation/sorting. |
| **Repository** | `IShipmentStore` → `InMemoryShipmentStore` / `MongoShipmentStore` | Abstracts shipment persistence; swap in-memory vs MongoDB via config. |
| **Adapter** | `EasyPostRateParser` (JSON → `RateQuote`, `MapServiceLevel`) | Translates EasyPost's external shape into the internal rate model. |
| **Humble Object** | `EasyPostRateParser` split from `EasyPostRateEngine` | Pure parsing logic is isolated from HTTP so it is unit-testable without network/keys. |
| **Null Object** | `NullZplPrinter` | Stands in when no printer is configured, so callers never branch on null. |
| **Factory** | `ShipMateKernelFactory` | Assembles the kernel + all services from config; shared by console and web API hosts. |
| **Dependency Injection** | constructor injection wired in `Program.cs` | Services/plugins receive collaborators, enabling stubbing in tests. |

---

## Getting started

### Prerequisites
- .NET 8 SDK (or newer)
- An LLM backend (pick one below)

### Configure an LLM backend

The `Provider` setting selects the backend. Store secrets with user-secrets so keys
never land in source control:

```powershell
cd src/ShipMate.AI.Console
```

**Option A — OpenAI-compatible (e.g. Zhipu GLM, free tier):**
```powershell
dotnet user-secrets set "Provider" "OpenAI"
dotnet user-secrets set "OpenAI:ApiKey"   "<your-key>"
dotnet user-secrets set "OpenAI:ModelId"  "glm-4-flash"
dotnet user-secrets set "OpenAI:Endpoint" "https://open.bigmodel.cn/api/paas/v4"
```

**Option B — Azure OpenAI:**
```powershell
dotnet user-secrets set "Provider" "AzureOpenAI"
dotnet user-secrets set "AzureOpenAI:Endpoint"       "https://<resource>.openai.azure.com/"
dotnet user-secrets set "AzureOpenAI:ApiKey"         "<your-key>"
dotnet user-secrets set "AzureOpenAI:DeploymentName" "gpt-4o"
```

**Option C — Local Ollama (free, offline):**
```powershell
ollama pull qwen2.5      # a model with solid function-calling support
dotnet user-secrets set "Provider" "Ollama"
# defaults: model qwen2.5/llama3.1, endpoint http://localhost:11434/v1
```

### Run — console

```powershell
dotnet run --project src/ShipMate.AI.Console
```

Then try:

```
Find the cheapest overnight from 30301 to 10001 for a 5 lb residential package, ship it and print the label.
Where is my package?   (use the tracking number returned above)
exit
```

A generated label is written to `bin/.../labels/label_<tracking>.zpl` and can be
previewed in any online ZPL viewer (e.g. Labelary) or sent to a thermal printer.

### Run — web API + chat UI

```powershell
dotnet run --project src/ShipMate.AI.Api --urls http://localhost:5099
```

Open `http://localhost:5099` in a browser. The chat page talks to `POST /api/chat`,
which uses the same Semantic Kernel, carrier, shipping, label, and printing layers as
the console host (assembled by the shared `ShipMateKernelFactory`). Each browser session
gets its own chat history. Quick-example buttons are provided.

### MongoDB persistence (optional)

Set a connection string to persist shipments across runs (e.g. MongoDB Atlas free tier):

```powershell
cd src/ShipMate.AI.Console
dotnet user-secrets set "Mongo:ConnectionString" "mongodb+srv://..."
dotnet user-secrets set "Mongo:Database" "shipmate"
```

Without a connection string the app uses an in-memory store. If MongoDB is unreachable
at startup it falls back to in-memory automatically.

### Observability (OpenTelemetry)

The web API is instrumented with OpenTelemetry tracing. Each `POST /api/chat` produces a
trace tree: the ASP.NET Core HTTP span, a `chat.request` span, and child spans for each
tool the LLM invokes (`tool.get_shipping_rates`, `tool.create_shipment`), tagged with
context like origin/destination, quote count, and tracking number.

Spans are printed to the console via the console exporter, so you can see them in the API
terminal. Example trace for a rate lookup:

```
POST /api/chat                    ← ASP.NET Core span
  └─ chat.request                 ← root span (session.id, message/reply length)
       └─ tool.get_shipping_rates ← tool span (rate.origin_zip, rate.quote_count=3, duration)
```

This makes it easy to see where time goes (LLM call vs. carrier API). For production,
swap the console exporter for an OTLP exporter pointing at Jaeger, Tempo, or a collector.

### Label preview

The `render_label` tool emits standard 4x6 inch ZPL at 203 dpi with a Code128 tracking
barcode. Paste the generated `.zpl` into the [Labelary viewer](https://labelary.com/viewer.html)
to render it, or use their API:

```powershell
# Render a saved label to PNG via the Labelary API
$zpl = Get-Content .\bin\Debug\net8.0\labels\label_<tracking>.zpl -Raw
Invoke-WebRequest -Uri "https://api.labelary.com/v1/printers/8dpmm/labels/4x6/0/" `
  -Method Post -Body $zpl -OutFile docs/label-preview.png
```

<!-- Add a rendered screenshot here once captured: -->
![ShipMate AI 4x6 ZPL label preview](docs/label-preview.png)

### Printing to a label printer

Two tools send ZPL to a real printer:

- `print_label` — prints the self-rendered demo label for a booked shipment.
- `buy_and_print_carrier_label` — buys a **real** carrier label from EasyPost (requesting
  `label_format=ZPL`) and prints its ZPL. Requires `EasyPost:ApiKey`.

Configure the printer in `appsettings.json` (or user-secrets):

```jsonc
"Printer": {
  "Type": "Windows",   // Windows | Tcp | None
  "Name": "4X6Virtual", // Windows printer name (Type=Windows)
  "Host": "",           // printer IP (Type=Tcp)
  "Port": 9100
}
```

- **Windows** — sends raw bytes through the print spooler (`winspool.drv` P/Invoke) with
  the `RAW` data type, so the printer interprets ZPL directly instead of rendering text.
- **Tcp** — opens a socket to `Host:Port` (9100 is the standard raw/JetDirect port) and
  streams the ZPL, for network label printers.
- **None** — no physical printing; labels are still written to the `labels/` folder.

> Tip: a free "Generic / Text Only" Windows printer on port 9100 works as a virtual ZPL
> sink for testing the spooler path end to end.

---

## Notes & limitations

- Carrier rates come from the **EasyPost API** when `EasyPost:ApiKey` is set (use a free
  test key, `EZTK...`); otherwise the app falls back to deterministic **mock** carriers
  so the AI pipeline still runs end-to-end with no credentials. Both implement the same
  `ICarrierRateEngine` contract, so the AI layer is unchanged either way.
- Buying a real label downloads it from EasyPost's public CDN; on networks that block
  that CDN the shipment is still purchased and the label URL is returned for manual fetch.
- Shipments persist in **MongoDB** when `Mongo:ConnectionString` is set (e.g. Atlas free
  tier); otherwise an in-memory store is used. If MongoDB is unreachable at startup the
  app falls back to in-memory so the demo still runs.
- Smaller models may occasionally execute only one tool per turn; a brief follow-up
  ("now ship it") nudges the orchestration forward.

## Testing

```powershell
dotnet test
```

The NUnit suite runs fully offline — no API key or network access required. EasyPost
response mapping is tested against captured JSON samples by isolating the pure parsing
logic (`EasyPostRateParser`) from the HTTP-bound engine. Printing is verified with a fake
printer and a real loopback socket (`TcpZplPrinter`), so no physical printer is needed.
The shipment store contract is tested via `InMemoryShipmentStoreTests`.

## Roadmap

- [x] Real carrier integration behind `ICarrierRateEngine` (EasyPost)
- [x] `LabelPrintPlugin` — generate 4x6 ZPL shipping labels
- [x] ZPL printing (Windows spooler / TCP 9100) + real EasyPost label buying
- [x] MongoDB persistence for shipments and tracking
- [x] Unit test suite (NUnit, 32 tests)
- [x] Minimal API + web chat UI
- [x] OpenTelemetry tracing of tool-call chains
- [ ] RAG knowledge base for carrier rules (prohibited items, international eligibility)
```
