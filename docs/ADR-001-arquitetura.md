# ADR-001 — Arquitetura do FinanceMove

- **Status:** Aceita — 15/08/2026
- **Decisor:** Juan, após debate de alternativas com trade-offs
- **Supersede:** decisões D1 (stack), D2 (auth) e D9 (representação de dinheiro) da `SPEC.md`, que foi atualizada nesta data para apontar para este ADR

## Contexto

O FinanceMove (SPEC.md, v1) é um app web de finanças pessoais multi-usuário, pt-BR/R$. Dois fatos
novos desde a spec original mudaram o peso dos critérios:

1. **Estratégico:** o sistema vai virar, no futuro, o módulo financeiro de um hub maior ("AppLife":
   hábitos, treinos, alimentação, agenda, metas de vida). A arquitetura deve ser um **monólito
   modular** — módulos com fronteiras explícitas, sem microserviços.
2. **De aprendizado:** o autor é dev júnior (C# e Node/TS) e quer se aprofundar em C#; o mercado-alvo
   dele valoriza a stack .NET. Aprendizado passou a ser requisito, não efeito colateral.

Restrições permanentes: custo de infra ~US$ 5/mês (Railway já pago), dados financeiros sensíveis
(LGPD), dev solo, escala inicial < 50 usuários com intenção de crescer a centenas.

---

## Decisão 1 — Backend: C#/.NET (ASP.NET Core + EF Core + Npgsql)

Monólito modular em .NET LTS: uma solution, **um par de projetos por módulo**
(`Modulo` + `Modulo.Contratos`), DI nativa, EF Core com migrations, `BackgroundService` para jobs.

**Alternativas rejeitadas:**

- **NestJS (Node/TS)** — módulos de 1ª classe e entrega mais rápida (linguagem já dominada), mas
  exatamente por isso não avança o objetivo de C#; e JavaScript não tem tipo decimal nativo, o que
  torna dinheiro permanentemente mais frágil (obrigaria centavos-int ou biblioteca).
- **Next.js fullstack (decisão D1 original)** — o mais rápido até produção, porém a pior base para
  monólito modular: API routes não impõem fronteira nenhuma (qualquer arquivo importa qualquer um),
  e o backend do futuro AppLife nasceria sem chassi. Rejeição motivada pelos fatos novos do contexto,
  não por defeito da decisão original.

**Por quê:** melhor mecanismo de fronteiras do ecossistema mainstream (projetos + `internal`),
`decimal` nativo, e é o requisito de carreira. Custo aceito conscientemente: MVP ~30–50% mais lento
que o plano Next.js, e duas peças em produção (API + front).

## Decisão 2 — Banco: PostgreSQL (gerenciado no Railway)

**Alternativas rejeitadas:**

- **SQL Server** — o "natural" de C#, morre no custo: sem tier gratuito viável fora do Azure
  (~US$ 5/mês só o Azure SQL Basic, limitado). Postgres é cidadão de 1ª classe no EF Core via Npgsql.
- **SQLite** — excelente para testes locais, inviável para web multi-usuário em host com disco
  efêmero (Railway) e sem acesso concorrente robusto.

**Multi-tenancy por `usuario_id` com defesa em profundidade** (rejeitados: schema-per-tenant —
overkill absurdo para o volume; e "confiar no `Where` manual" — um esquecimento vira vazamento):

1. Toda tabela de negócio tem `usuario_id`; **Global Query Filter do EF Core** injeta
   `WHERE usuario_id = @sessão` automaticamente em toda query de todo módulo;
2. Índices e constraints únicos são compostos começando por `usuario_id`;
3. Recurso de outro usuário responde **404** (não 403 — não vazar existência);
4. RLS do Postgres entra como segunda tranca no portão de abertura pública (SPEC §9.4) — adiada
   conscientemente, não esquecida.

## Decisão 3 — Dinheiro: `decimal` (C#) ↔ `numeric(14,2)` (Postgres)

**Nunca float/double, em nenhuma camada.** Motivo matemático: float é base 2, e 0,1 em binário é
dízima periódica — o valor guardado é uma aproximação (`0.1 + 0.2 = 0.30000000000000004`). Somas de
milhares de transações acumulam erro; comparações de igualdade falham. `decimal` do C# é base 10
(128 bits), exato para dinheiro; `numeric(14,2)` no Postgres idem, com escala fixa.

**Regras vinculantes:**

- Escala 2 fixa em todas as colunas monetárias; `SUM(valor)` no SQL devolve reais legíveis.
- Arredondamento **sempre explícito**: `Math.Round(x, 2, MidpointRounding.AwayFromZero)`.
  Pegadinha registrada: o padrão do C# é arredondamento bancário — `Math.Round(2.5)` dá **2**.
- Divisão de parcelas nunca arredonda parcela a parcela: calcula N−1 parcelas e a última recebe o
  resto (ou distribui os centavos excedentes nas primeiras), garantindo Σ parcelas = total ao centavo.
- **O front não faz aritmética de dinheiro** — JSON number vira double no JavaScript; a SPA apenas
  exibe o que a API mandar. Toda agregação (somas, percentuais, projeções) é do servidor.

**Alternativa rejeitada:** centavos inteiros (`bigint`) — a decisão D9 original. Também 100% correta
e imune a erro de escala; rejeitada porque, com backend C#, `decimal` é o caminho idiomático do
ecossistema financeiro .NET, elimina a conversão `/100` em toda borda e mantém o SQL legível.
(Se o backend fosse Node, centavos-int continuaria sendo a escolha certa.)

## Decisão 4 — Frontend: SPA React (Vite + React Router + shadcn/ui), hospedada separada

Porta direta do protótipo Lovable (que exporta exatamente Vite + React Router + shadcn/ui — zero
conversão de framework). A SPA vive no **Cloudflare Pages** (grátis, CDN), separada da API.

**Alternativas rejeitadas:**

- **Next.js como front** — retrabalho de portar React Router → App Router para ganhar SSR que um app
  100% atrás de login não usa (SEO irrelevante).
- **SPA servida pelo próprio ASP.NET (`wwwroot`)** — era a recomendação técnica inicial (um deploy,
  mesma origem, zero CORS, cookie httpOnly). **Rejeitada em debate pelo decisor**, com argumento
  aceito: SPA separada + API com token é o desenho mais comum do mercado C# brasileiro, e treinar
  exatamente esse desenho é parte do objetivo do projeto.

**Condições técnicas anexadas à decisão** (mitigam o custo da separação):

- CORS restrito à **única origem** do front (nada de `*`), com preflight correto;
- Auth por **bearer token** (ver Decisão 5), não cookie cross-site;
- Contrato de API versionado (`/api/v1/...`) desde o dia 1, já que front e back deployam separados.

## Decisão 5 — Auth: ASP.NET Identity com bearer tokens

Identity API endpoints (nativos do .NET 8+): e-mail/senha com hash PBKDF2, lockout, confirmação e
reset de e-mail **prontos do framework** — nenhuma criptografia escrita à mão. Access token curto
(~15 min) + refresh token com rotação e revogação server-side. E-mail transacional via Resend
(free tier). Login Google (OAuth) fica como fase 2 do auth. A identidade mora no nosso banco e vira
o módulo **Identidade** do futuro AppLife.

**Alternativas rejeitadas:**

- **Gerenciado (Clerk/Auth0)** — a decisão D2 original, tomada quando o plano era Next.js. Com SPA +
  API C#, sobraria a integração menos educativa (validar JWT de terceiro, espelhar usuários via
  webhook) e o hub nasceria com a identidade em vendor externo (lock-in + LGPD com dados fora).
- **Do zero (bcrypt+JWT à mão)** — aprendizado real de auth se obtém igualmente com o Identity, que
  expõe todos os conceitos sem o risco de errar sal, timing attack ou expiração de token de reset.
  Em dados financeiros, o pior custo-benefício possível.

## Decisão 6 — Hospedagem: Railway (API + Postgres) + Cloudflare Pages (SPA)

Custo total: **~US$ 5/mês já pagos** (Railway Hobby inclui US$ 5 de uso; excedente só se crescer —
pior caso realista ~US$ 8–12). Cloudflare Pages hospeda a SPA de graça com CDN. Sem cold start, sem
operar banco.

**Alternativas rejeitadas (preços de ago/2026):**

| Opção | Custo real | Motivo da rejeição |
|---|---|---|
| Render | free tier dorme após 15 min (cold start 30–60 s) e o Postgres grátis de 256 MB **expira em 90 dias**; para valer, ~US$ 14/mês (web US$ 7 + PG US$ 7) | ~3× o custo do Railway já pago, sem vantagem |
| Fly.io | sem free tier desde 2024; app pequeno + Postgres ≈ US$ 8–12/mês | Postgres "de máquinas": failover e backup viram operação nossa |
| VPS Hetzner | ~€ 4,50/mês | máximo aprendizado de ops, mas TLS, backup e patch de segurança viram responsabilidade permanente — pesado demais para dados financeiros de terceiros agora |
| Azure | App Service F1 grátis dorme e limita CPU (60 min/dia); Postgres flexível ~US$ 13+/mês | pagar mais para ter menos que o Railway já pago |

Fontes de preço: [Railway — Pricing Plans](https://docs.railway.com/pricing/plans),
[Render Postgres — pricing & limits](https://kuberns.com/blogs/render-postgres-pricing-setup-limits/),
[Fly.io — Resource Pricing](https://fly.io/docs/about/pricing/),
[Fly.io free tier em 2026](https://www.saaspricepulse.com/tools/flyio).

---

## Consequências

**Positivas:** chassi de monólito modular pronto para o AppLife absorver; aprofundamento real em C#;
identidade e dados 100% em casa (LGPD); custo mensal inalterado; front do protótipo aproveitado
quase inteiro.

**Negativas (aceitas):** MVP mais lento que o plano Next.js original; duas pipelines de deploy;
CORS + bearer para configurar corretamente na fundação (errar aqui custa tardes de debug — por isso
as condições da Decisão 4 são vinculantes).

**Dívidas conscientes:** login Google (fase 2 do auth); RLS no Postgres (portão de abertura pública);
sem ambiente de staging por ora.

**Documentos afetados:** `SPEC.md` atualizada (D1, D2, D9 apontam para cá; modelo de dados §7 migrou
de centavos para `numeric(14,2)`); `docs/arquitetura.md` detalha componentes e fronteiras de módulos.
