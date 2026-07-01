# ShipMate.AI 拆分重写规划（Master Plan）

> 本文档是重写工作的唯一事实来源（single source of truth）。
> 后续把下面的「任务卡」逐张丢给 AI（agent 模式）执行即可，无需每次重开 plan 模式。
>
> 目标：把当前**单仓 + 全 C#（Semantic Kernel）**的 ShipMate.AI，拆成
> **C# shipping 域服务（MCP）+ Python AI 编排（LangGraph/FastAPI）+ React widget** 三层，
> 风格完全对齐公司 StarShip 家族的 `ship-mcp`（C#）+ `ship-agent`（Python+前端）。

---

## 1. 目标与动机

- **对齐公司技术栈**：shipping 业务用 C#，AI 用主流 Python，跟 `ship-mcp` / `ship-agent` 同款分工。
- **职责清晰**：C# 只做 shipping 域（rate / ship / track / label / print）并把它们暴露成 MCP 工具；Python 负责 LLM 编排、会话、RAG、可观测性。
- **简历亮点**：MCP 服务端（C#）+ LangGraph 多节点编排（Python）+ RAG + Langfuse + React widget，一整套现代 AI 应用架构。

### 已确认的方向性决策

| 维度 | 选择 |
|---|---|
| 仓库结构 | **多仓**：`shipmate-shipping`（C#）+ `shipmate-agent`（Python，含 frontend） |
| C#↔Python 通信 | **MCP（SSE/HTTP）**，对齐 `ship-mcp` |
| Python 编排框架 | **LangGraph + FastAPI**，对齐 `ship-agent` |
| RAG 归属 | **Python 侧**（和 AI 编排一起） |
| 前端 | **独立 React 聊天 widget**（放在 `shipmate-agent/frontend`，对齐 `ship-agent`） |

---

## 2. 现状盘点（源仓 `ShipMate.AI`）

已有实现（大多已在接口后面，迁移成本低）：

- **Carrier 域服务（C#，`src/ShipMate.AI.Console`）**
  - Rate：`ICarrierRateEngine` / `MockCarrierRateEngine` / `EasyPostRateEngine` / `EasyPostRateParser` / `RatingService`
  - Ship：`ShippingService` + `ShipmentModels`
  - Track：`ShippingService`（合成轨迹）
  - Label：`LabelService`（ZPL）/ `EasyPostLabelService`（真实购买）
  - Print：`IZplPrinter` + `WindowsRawZplPrinter` / `TcpZplPrinter` / `NullZplPrinter`
  - 持久化：`IShipmentStore` + `InMemoryShipmentStore` / `MongoShipmentStore`
- **AI 编排（C# Semantic Kernel）—— 这部分要迁到 Python**
  - Plugins：`RatePlugin(get_shipping_rates)`、`ShipPlugin(create_shipment)`、`TrackPlugin(track_shipment)`、`LabelPrintPlugin(render_label)`、`PrintLabelPlugin(print_label, buy_and_print_carrier_label)`、`KnowledgePlugin(search_carrier_rules)`
  - 组合根：`ShipMateKernelFactory`（多 provider：AzureOpenAI / OpenAI 兼容 / Ollama）
- **RAG（C#，`Knowledge/`）—— 要迁到 Python**
  - `CarrierKnowledgeBase` / `IEmbeddingService`（`HashingEmbeddingService` / `OpenAIEmbeddingService`）/ `VectorSearchService`
- **宿主**
  - `ShipMate.AI.Console`：CLI 聊天
  - `ShipMate.AI.Api`：Minimal API（`/api/chat`、`/api/chat/{sessionId}`、内嵌 HTML 页）+ Langfuse OTel（`LangfuseTracing.cs`）
- **测试**：`ShipMate.AI.Tests`（NUnit 4，约 37 个用例，覆盖 parser / rating / store / label / printer / RAG）

### 需要先偿还的技术债（迁移前修）

1. `Console/Program.cs` 与 `ShipMateKernelFactory.cs` 组合逻辑重复。
2. `Api/Program.cs` 结尾有重复的 `app.Run()`。
3. Console 与 Api 两个 csproj 的 Semantic Kernel 包版本不一致。

---

## 3. 目标架构

```
Browser (React widget)
      |
      v
shipmate-agent  (Python / FastAPI + LangGraph)      <-- AI 编排层 + RAG + Langfuse
      |  MCP (SSE/HTTP)
      v
shipmate-shipping (C# / ASP.NET + MCP SDK)          <-- shipping 域服务，暴露为 MCP 工具
      |
      v
Mock carriers / EasyPost / Mongo / ZPL printer
```

### 3.1 `shipmate-shipping`（C# MCP 服务，对齐 `ship-mcp`）

保留并**从 Console 项目里抽出**下列域服务，包一层 MCP 工具对外暴露：

建议目录：

```
shipmate-shipping/
  ShipMate.Shipping.sln
  src/
    ShipMate.Shipping.Domain/        # 纯域逻辑，无框架依赖
      Carriers/  (ICarrierRateEngine, RatingService, ShippingService, *Models)
      Label/     (LabelService, EasyPostLabelService)
      Printing/  (IZplPrinter + 实现)
      Storage/   (IShipmentStore + InMemory/Mongo)
    ShipMate.Shipping.McpServer/     # ASP.NET + MCP SDK 宿主（对齐 ship-mcp 的 ApteanShipMcpServer）
      Program.cs
      Tools/     (ShippingTools: 用 [McpServerTool] 暴露)
      appsettings.json
  tests/
    ShipMate.Shipping.Tests/         # 迁移现有 NUnit 用例
```

**暴露的 MCP 工具（工具名沿用现有 SK function 名，降低迁移摩擦）：**

| MCP 工具 | 来源 plugin | 说明 |
|---|---|---|
| `get_shipping_rates` | RatePlugin | 多 carrier 比价 |
| `create_shipment` | ShipPlugin | 下单、生成 tracking |
| `track_shipment` | TrackPlugin | 查询轨迹 |
| `render_label` | LabelPrintPlugin | 生成 ZPL 标签 |
| `print_label` | PrintLabelPlugin | 推送 ZPL 到打印机 |
| `buy_and_print_carrier_label` | PrintLabelPlugin | EasyPost 真实购买 + 打印 |

> `search_carrier_rules`（RAG）**不**放这里，迁到 Python。

### 3.2 `shipmate-agent`（Python，对齐 `ship-agent`）

```
shipmate-agent/
  backend/
    app/
      main.py          # FastAPI 入口，/api/chat 等
      config.py        # provider/密钥/MCP 地址（env 驱动）
      llm.py           # OpenAI 兼容 / Azure OpenAI 选择
      mcp_client.py    # 连 shipmate-shipping 的 MCP
      graph.py         # LangGraph：trim -> planner -> route -> tool_executor/rag -> summarizer
      session.py       # 会话历史（对齐 Api 的 ChatSessionStore）
      rag/             # 迁移过来的 RAG：知识库 + embedding + 向量检索
      tracing.py       # Langfuse / OTel（对齐 LangfuseTracing.cs）
      agents/          # 各节点/子 agent
    prompts/           # 系统提示词、SQL/工具提示（对齐 ship-agent/prompts）
    requirements.txt
    .env.example
    tests/
  frontend/            # React 聊天 widget（对齐 ship-agent/frontend）
  README.md
```

### 3.3 C#↔Python 契约

- 传输：MCP over HTTP/SSE。Python 用 MCP client（对齐 `ship-agent/app/mcp_client.py`）连 C# 的 `/mcp` 端点。
- 工具 schema：以 3.1 表格中的工具名 + 参数为契约。**先在文档里把每个工具的 JSON schema 定死**，两边照此实现（见任务卡 T1/T4）。
- 认证：本地开发先不做鉴权；生产可对齐 ship-mcp 的 service token + tenant header（列为后续项，不阻塞）。

---

## 4. 迁移阶段与任务卡

> 每张卡片自带上下文与验收标准，可独立丢给 AI 执行。建议按阶段顺序做，阶段内可并行的已标注。

### Phase 0 — 准备与技术债

**T0.1 修技术债（在源仓 `ShipMate.AI` 先做）**
- 合并 `Console/Program.cs` 与 `ShipMateKernelFactory.cs` 的重复组合逻辑。
- 删掉 `Api/Program.cs` 里重复的 `app.Run()`。
- 统一 Console/Api 的 Semantic Kernel 包版本。
- 验收：两个宿主都能正常跑；测试全绿。

**T0.2 建两个新仓骨架**
- 创建 `shipmate-shipping`（sln + 三个项目）与 `shipmate-agent`（backend + frontend）空骨架 + README + .gitignore。
- 验收：`dotnet build` 通过；`uvicorn app.main:app` 能起一个空的 FastAPI；前端能 `npm run dev`。

### Phase 1 — C# shipping 域抽取（`shipmate-shipping`）

**T1.1 抽取 Domain 项目**
- 把源仓 `Carriers/`、`Printing/`、Label、Store 相关类原样迁入 `ShipMate.Shipping.Domain`（去掉任何 SK 依赖）。
- 迁移 `ShipMate.AI.Tests` 中与这些类相关的 NUnit 用例到 `ShipMate.Shipping.Tests`。
- 验收：域项目零框架依赖、可独立编译；迁移的测试全绿。

**T1.2 建 MCP 宿主**
- 参照 `ship-mcp/ApteanShipMcpServer` 的结构，建 `ShipMate.Shipping.McpServer`：ASP.NET + 官方 C# MCP SDK，映射 `/mcp` 端点。
- 用 `[McpServerTool]` 暴露 3.1 表格的 6 个工具，内部调用 Domain 服务。
- provider/EasyPost/Mongo/printer 配置走 `appsettings.json` + user-secrets（**密钥禁止进 appsettings**）。
- 验收：`dotnet run` 起服务；用 MCP inspector / curl 能列出并调用 `get_shipping_rates` 等工具并拿到结果。

**T1.3 定死工具 JSON schema**
- 把 6 个工具的入参/出参 schema 写进 `shipmate-shipping/docs/tool-contracts.md`，作为 Python 侧契约。
- 验收：文档含每个工具的 name / 参数 / 返回示例。

### Phase 2 — Python 编排层（`shipmate-agent/backend`）

**T2.1 FastAPI + 配置骨架**（依赖 T0.2）
- 参照 `ship-agent/app`：`main.py`(`/api/chat`)、`config.py`、`llm.py`（OpenAI 兼容 / Azure）、`session.py`。
- provider 走 env（对齐用户现有 Zhipu glm-4-flash / OpenAI 兼容用法）。
- 验收：`/api/chat` 能收消息、调一次 LLM、回文本（还没接工具）。

**T2.2 MCP client 接入**（依赖 T1.2、T1.3）
- 写 `mcp_client.py` 连 `shipmate-shipping` 的 `/mcp`，把 6 个工具动态绑定给 LLM。
- 验收：Python 侧能列出并成功调用 C# 的 `get_shipping_rates` 并把结果喂回 LLM。

**T2.3 LangGraph 图**（依赖 T2.2）
- 参照 `ship-agent/app/graph.py`：`trim -> planner -> route -> tool_executor -> summarizer`。
- planner 决定调哪个工具；tool_executor 走 MCP；summarizer 组织自然语言回复。
- 验收：一句「帮我比一下 UPS/FedEx 到某地的运费」能走完整个图并给出比价回复。

### Phase 3 — RAG 迁到 Python（`shipmate-agent/backend/app/rag`）

**T3.1 迁移知识库与检索**
- 把 `CarrierKnowledgeBase` 的语料迁成数据文件；用 Python 实现 embedding（OpenAI 兼容 / 本地）+ 余弦 top-K 检索（对齐现有 `VectorSearchService` 语义）。
- 暴露成图里的一个 `search_carrier_rules` 节点/工具。
- 验收：「锂电池能走空运吗」能命中知识库并由 summarizer 引用规则回答。

**T3.2 接进 LangGraph**（依赖 T2.3、T3.1）
- planner 能在「知识问答」与「shipping 操作」之间路由（对齐 ship-agent 的 route 分支）。
- 验收：知识类问题走 RAG 分支、操作类问题走工具分支。

### Phase 4 — React widget（`shipmate-agent/frontend`）

**T4.1 建聊天 widget**（依赖 T2.1）
- 参照 `ship-agent/frontend`：React + assistant-ui，打包成 Web Component `<shipmate-chat-widget>`。
- 连 `shipmate-agent` 的 `/api/chat`。
- 验收：`npm run dev` 能聊天、能看到工具调用/回复。

### Phase 5 — 可观测性

**T5.1 Langfuse/OTel 对齐**（依赖 T2.3）
- 参照源仓 `LangfuseTracing.cs`，在 Python 侧接 Langfuse（LangGraph 有现成回调）。
- 验收：Langfuse 里能看到每轮 trace（planner / 工具调用 / summarizer）。

### Phase 6 — 收尾

**T6.1 Docker + README + CI**
- 两个仓各自 Dockerfile（对齐 ship-mcp / ship-agent）。
- README 写清本地一键起：先起 `shipmate-shipping`（`:5001/mcp`），再起 `shipmate-agent`（`:8001`），再起前端。
- 验收：照 README 从零能跑通全链路。

**T6.2 源仓归档**
- 旧 `ShipMate.AI` 仓 README 顶部加迁移说明，指向两个新仓；保留历史。

---

## 5. 建议执行顺序

```
T0.1 -> T0.2 -> T1.1 -> T1.2 -> T1.3
                          |        \
                          v         v
                        T2.1 ---> T2.2 -> T2.3 -> T3.1 -> T3.2
                          |                 |
                          v                 v
                        T4.1              T5.1
                                            |
                                            v
                                          T6.1 -> T6.2
```

- 关键路径：T1.2/T1.3（C# 工具就绪）→ T2.2（Python 接上）→ T2.3（图跑通）。
- T4.1（前端）在 T2.1 后即可并行。

## 6. 每张卡片交给 AI 时的固定话术模板

> 参照本仓 `docs/rewrite-plan.md` 的任务卡 **T?.?**。仓库 `<路径>`。
> 目标：<粘贴该卡目标>。
> 约束：密钥只用 user-secrets/env，绝不写进 appsettings 或代码；不自动 commit/push。
> 完成后：<粘贴该卡验收标准>，并跑一遍相关测试。
