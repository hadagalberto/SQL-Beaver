# SQL Beaver v6 — AI completion com provider e chave do usuário (Design)

**Data:** 2026-06-11
**Base:** v1–v5 entregues (1.2.0). 663 testes.
**Pedido:** "AI completion com o usuário escolhendo o provider da API e fornecendo a chave."
Decisões confirmadas via AskUserQuestion: provedores **Anthropic + OpenAI + Gemini**; gatilho
**comando explícito**; funções **gerar SQL de comentário** + **explicar/otimizar SQL**; contexto de
schema **tabelas no escopo**. Chave guardada **criptografada (DPAPI)** — decisão do agente (padrão correto).
**Modo:** execução autônoma, 2 commits (core + UI), revisão final.

## Princípios de segurança/privacidade (inegociáveis)

- A chave de API nunca em texto puro no disco: gravada com **DPAPI** (`ProtectedData.Protect`,
  `DataProtectionScope.CurrentUser`, base64) em `%LOCALAPPDATA%\SqlBeaver\ai.json`. Só o usuário Windows
  atual descriptografa.
- O SQL do usuário sempre vai ao provedor (é o ponto). O **schema** enviado é limitado às tabelas do
  escopo do statement atual (configurável: escopo / nenhum / banco todo; default escopo).
- O diálogo de configuração diz claramente o que é enviado e a quem.
- Nada de IA no caminho da tecla: só comandos explícitos. HTTP sempre em background, com timeout; erro
  nunca escapa para o editor (status bar + Output).

## Categorias (2 commits)

1. **C1 Núcleo** — abstração de provider + 3 providers (HTTP), prompt builder, contexto de schema,
   config store com DPAPI. Puro testável por TDD; HTTP fino.
2. **C2 Comandos + UI** — diálogo de configuração, comandos Gerar/Explicar/Otimizar, wiring VSCT/package.

## C1 — Núcleo

### Sem SDK — HTTP cru (net48)
O SDK C# da Anthropic exige .NET moderno e colidiria com a carga MEF do net48. Usar `HttpClient` cru
(estático, TLS 1.2 forçado via `ServicePointManager.SecurityProtocol`), requests/respostas em JSON via
`DataContractJsonSerializer` (escapa o SQL corretamente; ignora membros desconhecidos na resposta) —
mesmo padrão de JSON já usado no projeto, sem dependência nova. As classes de IA NÃO são exportadas via
MEF (rodam no contexto de comando/package), então referenciar esses tipos é seguro.

### `IAiProvider` (abstração)
```csharp
public sealed class AiRequest { public string System; public string User; public int MaxTokens; }
public sealed class AiResult { public bool Ok; public string Text; public string Error; }
public interface IAiProvider {
    string Id { get; }                 // "anthropic" | "openai" | "gemini"
    string DefaultModel { get; }
    Task<AiResult> CompleteAsync(AiRequest req, string model, string apiKey, CancellationToken ct);
}
```

### Providers (TDD nas partes puras: montar corpo + extrair texto)
Cada provider expõe métodos PUROS testáveis: `BuildRequestBody(req, model)` → json string, e
`ExtractText(responseJson)` → string (ou lança com mensagem amigável). `CompleteAsync` é o invólucro HTTP fino.

- **AnthropicProvider** (`DefaultModel = "claude-opus-4-8"`):
  - POST `https://api.anthropic.com/v1/messages`; headers `x-api-key`, `anthropic-version: 2023-06-01`,
    `content-type: application/json`.
  - body `{ "model", "max_tokens", "system", "messages":[{"role":"user","content":USER}] }`.
    **NÃO enviar `temperature`** (removido nos modelos atuais → 400).
  - resposta: primeiro bloco `content[]` com `type=="text"` → `.text`.
- **OpenAiProvider** (`DefaultModel = "gpt-4o"`):
  - POST `https://api.openai.com/v1/chat/completions`; header `Authorization: Bearer KEY`.
  - body `{ "model", "messages":[{"role":"system",...},{"role":"user",...}], "max_tokens" }`.
  - resposta: `choices[0].message.content`.
- **GeminiProvider** (`DefaultModel = "gemini-1.5-pro"`):
  - POST `https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key=KEY`.
  - body `{ "systemInstruction":{"parts":[{"text":SYS}]}, "contents":[{"role":"user","parts":[{"text":USER}]}],
    "generationConfig":{"maxOutputTokens":N} }`.
  - resposta: `candidates[0].content.parts[0].text`.

Erros HTTP → mensagens PT-BR: 401/403 "chave de API inválida ou sem permissão"; 429 "limite de uso
atingido — tente mais tarde"; timeout/rede "falha de rede ao consultar a IA"; outro "erro da IA (status N)".
TDD ~3 por provider (body contém model/mensagens; extrai texto de uma resposta de exemplo; resposta de erro
vira mensagem). Os modelos de DataContract das respostas capturam só os campos necessários.

### `AiPromptBuilder` (puro, TDD ~8)
```csharp
public static (string System, string User) BuildGenerateFromComment(string comentario, string schemaContext);
public static (string System, string User) BuildExplain(string sql, string schemaContext);
public static (string System, string User) BuildOptimize(string sql, string schemaContext);
```
- System (gerar): "Você é um especialista em T-SQL (Microsoft SQL Server). Gere SOMENTE SQL válido para
  SQL Server. Responda apenas com o SQL — sem explicações, sem cercas ```sql```." User: o comentário +
  o `schemaContext` quando houver.
- Explain: System pede explicação em PT-BR, passo a passo, do que o SQL faz. Optimize: System pede
  análise de performance (índices, sargabilidade, SELECT *, etc.) e uma versão melhorada, em PT-BR.
- Tests: cada um inclui o input; generate proíbe cercas; schemaContext vazio não quebra; texto do
  comentário sem o `--` (limpo).

### `AiSchemaContext` (puro, TDD ~5)
```csharp
/// <summary>Renderiza as tabelas do escopo (colunas + tipos + PK) como texto compacto para o prompt.
/// Vazio quando scope vazio ou nível "none". Nível "all" usa todas as Tables do metadata (cap ~60).</summary>
public static string Render(IReadOnlyList<TableRef> scope, DbMetadata metadata, AiSchemaScope level);
```
Formato: `Tabela: Cadastro.Pessoas (IdPessoa int PK, Nome varchar(250), CPFCNPJ varchar(14), ...)`.
`AiSchemaScope { Scope, None, All }`. Tests: scope renderiza colunas+PK; none → vazio; all cap; tabela
sem colunas no cache → só o nome; múltiplas tabelas.

### `AiSecretProtector` (DPAPI, TDD ~3 — roundtrip real)
```csharp
public static string Protect(string plaintext);    // base64 do blob DPAPI; null/empty → null
public static string Unprotect(string protectedB64); // texto; inválido → null (não lança)
```
`System.Security.Cryptography.ProtectedData`, escopo CurrentUser. Tests: roundtrip; null→null;
base64 inválido → null sem lançar.

### `AiConfig` + `AiConfigStore`
`[DataContract(Namespace="")] AiConfig { provider, model, schemaScope, keyProtected }` (keyProtected = DPAPI b64).
`AiConfigStore` (lazy, padrão dos outros stores): `Load()`, `Save(AiConfig, string plaintextKeyOrNull)`
(criptografa e grava `ai.json` atômico), `GetApiKey()` (descriptografa), `IsConfigured()`. Plaintext da
chave nunca persiste nem fica em campo público. TDD na resolução pura (provider inválido → default;
nível ausente → Scope) ~3.

## C2 — Comandos + UI

### Config dialog `AiSettingsDialog` (WinForms)
- Combo Provider (Anthropic/OpenAI/Gemini) → ao trocar, preenche o Model com o `DefaultModel` do provider.
- TextBox Model (editável). TextBox Chave de API (`UseSystemPasswordChar=true`); placeholder "•••• (mantida)"
  quando já há chave salva — só re-grava se o usuário digitar nova.
- Combo Contexto de schema (Tabelas no escopo / Nenhum / Banco todo).
- Label de privacidade: "Seu SQL e o schema selecionado são enviados ao provedor escolhido. A chave é
  guardada criptografada (DPAPI) nesta máquina."
- Botão **Testar conexão**: monta um request trivial ("responda OK") em background → mostra sucesso/erro.
- Salvar → `AiConfigStore.Save`.

### Comandos (Tools > SQL Beaver + menu de contexto; ids novos após 0x010D)
- `AiSettings` (`0x010E`): abre o diálogo.
- `AiGenerateFromComment` (`0x010F`, atalho **Ctrl+K, Ctrl+I**): pega o comentário da linha do caret
  (ou linhas de comentário contíguas acima/na seleção), monta schemaContext do escopo, chama o provider
  ativo em background, insere o SQL retornado abaixo do comentário (uma edição/um undo). Sem config →
  status "configure a IA em Tools > SQL Beaver > IA (configuração)". Limpa cercas ```sql do retorno.
- `AiExplain` (`0x0110`): seleção (ou statement atual via `GetStatementBoundsAt`) → BuildExplain →
  abre o resultado em NOVA janela (comentado). 
- `AiOptimize` (`0x0111`): idem com BuildOptimize.
- Todos: `IsConfigured()` falso → status amigável; HTTP em `Task.Run`, aplica ed/abre janela na UI thread;
  status "consultando IA…" enquanto roda; catch-all → Log + status. Cancelável por timeout (~60s).
- `ResponseSqlCleaner` puro (TDD ~4): remove cercas ```sql / ``` e espaços de borda do texto retornado.

### Wiring
ids em `SqlBeaverCommandIds`; Buttons+IDSymbol no VSCT; `AddCommand` no package para CADA um; handlers
em `EditorCommands`; KeyBinding Ctrl+K Ctrl+I. README seção "IA (opcional)".

## Erros, performance, testes
- Exceção nunca escapa; cache-only no schema; HTTP só em background com timeout/cancelamento; chave nunca
  logada. Lógica pura com TDD (providers body/extract, prompt builder, schema context, secret protector,
  cleaner, config resolver). Meta: 663 → ~700.

## Pendências previstas
- Ghost-text automático (estilo Copilot) — fora do v6 (custo/latência; o usuário escolheu comando explícito).
- Ollama/local — fora (usuário escolheu os 3 cloud); a abstração `IAiProvider` aceita adicionar depois.
- Streaming das respostas — não necessário para SQL curto; pode entrar se respostas longas incomodarem.
- Model ids dos provedores mudam com o tempo — são editáveis no diálogo; defaults documentados.
- VSIX não assinado (herdado).
