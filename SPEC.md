# SPEC - FinanceMove v1 (MVP)

> Documento autocontido: quem ler apenas esta spec consegue construir o sistema.
> Decidido em entrevista em 15/08/2026. Idioma do produto: pt-BR. Moeda: R$ (BRL). Datas exibidas: DD/MM/YYYY.

---

## 1. Visão

Um app web onde uma pessoa controla sua vida financeira de ponta a ponta: registra receitas, despesas
(inclusive cartão de crédito com fatura e parcelamento - o jeito brasileiro de gastar), acompanha
saldo por conta, define orçamento por categoria e enxerga para onde o dinheiro vai. Multi-usuário
com login desde o dia 1, começando com o autor + amigos/família, preparado para abrir ao público depois.

**Tese do MVP:** se o app registrar o dia a dia financeiro real de um brasileiro (débito, Pix,
cartão com fatura, conta recorrente, compra parcelada) com menos atrito que uma planilha, ele é útil.
Investimentos e metas são camadas por cima disso - ficam para depois.

---

## 2. Personas

| Persona | Descrição | O que precisa |
|---|---|---|
| **Juan** (primária) | Dev júnior, organizado, usa cartão de crédito e Pix, tem contas recorrentes (aluguel, assinaturas) e parcela compras grandes. Quer saber "quanto posso gastar esse mês" e "quanto vem de fatura". | Lançamento rápido, fatura fiel à realidade, orçamento por categoria. |
| **Ana** (secundária) | Familiar não-técnica convidada a usar. Nunca usou app de finanças. | Onboarding sem fricção: categorias já prontas, telas óbvias, sem jargão. |
| **Público futuro** (v2+) | Desconhecidos que se cadastram quando o app abrir. | Confiança: LGPD, política de privacidade, exclusão de conta, export. |

---

## 3. Decisões tomadas (e por quê)

Registradas para o "eu do futuro" não reabrir discussão sem motivo novo.

| # | Decisão | Por quê |
|---|---|---|
| D1 | **Stack (revista em 15/08/2026): API C#/.NET (ASP.NET Core + EF Core) como monólito modular + SPA React (Vite) separada. Postgres + API no Railway; SPA no Cloudflare Pages (revisto em 24/09/2026: SPA na Vercel, com proxy de mesma origem - `docs/ADR-002-deploy-e-seguranca.md`). Ver `docs/ADR-001-arquitetura.md`.** | Fatos novos: o FinanceMove será o módulo financeiro do hub AppLife (exige monólito modular) e aprofundar C# virou requisito. O ADR registra o debate e as alternativas rejeitadas. |
| D2 | **Auth (revisto em 15/08/2026): ASP.NET Identity com bearer tokens (access curto + refresh com rotação). Login Google fica para a fase 2. Ver ADR-001.** | Com backend C#, o Identity dá hash/lockout/reset prontos do framework (nada de cripto à mão), mantém a identidade no nosso banco (LGPD; futuro módulo Identidade do AppLife) e ensina o fluxo real de auth. |
| D3 | **Modelo individual:** todo dado pertence a exatamente 1 usuário (`usuario_id` em tudo). Sem espaços compartilhados/casal. | Simplicidade. Compartilhamento é migração dolorosa, mas é um futuro incerto - não se paga por ele agora. |
| D4 | **Saldo é sempre derivado** (`saldo_inicial + soma transações`), nunca armazenado. Divergência com o banco real se corrige com **transação de ajuste**. | Saldo armazenado dessincroniza silenciosamente (causa nº 1 de bug nesses apps). No volume esperado, a soma custa milissegundos. |
| D5 | **Cartão de crédito com fatura essencial na v1:** ciclo fechamento/vencimento, fatura por mês, pagamento **sempre total** via transferência. Sem pagamento parcial, sem juros rotativo. | Sem fatura o app não representa o gasto brasileiro. Parcial/rotativo dobram a complexidade e o valor deles é baixo (o número exato vem no app do banco). |
| D6 | **Transferência é um tipo próprio de transação** (origem + destino num registro). Nunca conta como receita/despesa em relatórios e orçamento. | Modelar como despesa+receita faz os relatórios mentirem ("gastei R$ 2.000" quando R$ 1.500 só mudou de bolso). |
| D7 | **Recorrência materializa no vencimento**, como transação `pendente` que o usuário confirma. Futuro é sempre calculado da regra, nunca gravado. | O banco só contém fatos; mesmo princípio do saldo derivado: uma única fonte de verdade. |
| D8 | **Parcelamento na v1, versão simples:** lançar em Nx cria as N transações de uma vez, ligadas por grupo. | Com fatura na v1, fatura sem parcela não representa a fatura real de quase ninguém no Brasil. A versão simples é barata (um loop). |
| D9 | **Valores monetários (revisto em 15/08/2026): `decimal` no C# <-> `numeric(14,2)` no Postgres. Nunca float. Arredondamento sempre explícito (`AwayFromZero`). Ver ADR-001, Decisão 3.** | Float é base 2 e não representa 0,1 com exatidão (`0.1 + 0.2 !== 0.3`); `decimal`/`numeric` são base 10, exatos. Com backend C#, `decimal` é o padrão do mercado financeiro .NET e elimina a conversão /100 dos centavos-int em toda borda. |
| D10 | **Importação CSV/OFX, Metas e Investimentos: v2** (ver secao 5). | Cada um é um subsistema. A v1 já assumiu fatura + recorrência + parcelas; esses são os cortes mais seguros. |
| D11 | **LGPD na v1: exclusão de conta (hard delete) + export de dados.** Política de privacidade e backup testado são **pré-requisitos do portão de abertura pública** (secao 9.4), não da v1. | Exclusão e portabilidade são direitos do titular; implementar depois é pior. Política/backup só se tornam críticos quando entram desconhecidos. |

---

## 4. Escopo do MVP (v1)

### Dentro

- Cadastro e login (e-mail/senha via ASP.NET Identity; login Google na fase 2); cadastro aberto a convidados (amigos/família).
- **Contas**: corrente, poupança, dinheiro e cartão de crédito. Saldo inicial, arquivamento, ajuste de saldo.
- **Transações**: receita, despesa e transferência; lançamento manual; edição e exclusão; filtros por mês, conta, categoria e tipo, e busca por descrição.
- **Cartão de crédito**: fatura essencial (D5) + compras parceladas (D8).
- **Recorrência**: regras mensais/semanais/anuais que geram transações pendentes no vencimento (D7).
- **Categorias**: seed padrão no cadastro, criar/renomear/arquivar. Sem subcategorias.
- **Orçamento**: limite mensal por categoria de despesa, barra de progresso do mês corrente.
- **Dashboard reduzido**: saldo total e por conta, receitas x despesas do mês, donut de despesas por categoria do mês, últimas transações.
- **LGPD**: excluir minha conta (hard delete de tudo) e exportar meus dados (CSV).

### Fora (v2/v3 - registrado para o modelo de dados não fechar portas)

| Item | Versão | Direção já decidida |
|---|---|---|
| Metas & Sonhos | v2 | Cards com progresso; projeção automática só depois. |
| Investimentos | v2 | **100% manual**: aportes/resgates como transações + usuário atualiza o valor atual dos ativos; evolução de 12 meses via snapshots dessas atualizações. Cotação por API (brapi) é v2.1 opcional - só cobriria bolsa; CDB/Tesouro/fundos seriam manuais de qualquer jeito. |
| Importação CSV/OFX | v2 | O difícil é dedup, não parse. `transacao.id_externo` já nasce na v1 para isso (secao 7). |
| Gráfico fluxo de caixa 6 meses | v2 | Dados já existem; é só uma query + gráfico. |
| Pagamento parcial de fatura / rotativo | v2 | Juros lançados manualmente enquanto isso. |
| Subcategorias | v2 | - |
| Compartilhamento (casal/família) | v3 | Exige migrar posse de dados de usuário para "espaço". Aceito o custo da migração se/quando o caso de uso for real. |
| Notificações (e-mail/push) | v3 | - |
| App mobile | v3 | Web responsivo cobre o MVP. |

---

## 5. Regras de negócio

Todas as regras abaixo valem **por usuário** - nenhuma query cruza dados de usuários diferentes.

### 5.1 Contas e saldo

- Tipos: `corrente`, `poupanca`, `dinheiro`, `cartao_credito`.
- Toda conta tem `saldo_inicial` (pode ser negativo) definido na criação.
- **Saldo atual (derivado)** = `saldo_inicial` + soma transações da conta com `status = confirmada` **e** `data <= hoje`.
  - Transações `pendentes` (recorrência não confirmada) e parcelas com data futura **não** entram no saldo atual.
  - Em transferências: valor sai da conta de origem e entra na de destino.
- **Ajuste de saldo**: usuário informa o saldo real do banco; o sistema cria uma transação (receita ou despesa, conforme o sinal da diferença) na categoria de sistema "Ajuste", com a data de hoje. O histórico nunca é reescrito.
- Conta com transações não pode ser excluída - só **arquivada** (sai dos formulários; histórico e relatórios intactos).

### 5.2 Transações

- Tipos: `receita`, `despesa`, `transferencia`.
- Campos obrigatórios: tipo, valor (> 0, decimal com 2 casas), data, conta; categoria obrigatória para receita/despesa e proibida para transferência.
- Transferência tem `conta_origem` e `conta_destino` (diferentes) num único registro (D6).
- Relatórios, dashboard e orçamento **ignoram transferências** por definição.
- Status: `confirmada` (padrão em lançamento manual) ou `pendente` (só gerada por recorrência, secao 5.5).
- Data pode ser passada ou futura (parcelas). Exibição sempre DD/MM/YYYY; armazenamento como `date` (sem hora); o "hoje" do sistema usa o fuso **America/Sao_Paulo**.

### 5.3 Cartão de crédito e fatura

- Conta do tipo `cartao_credito` tem `dia_fechamento` e `dia_vencimento` (1-28, para evitar aritmética de fim de mês na v1; o formulário valida isso).
- **A fatura não é uma tabela - é derivada** das transações do cartão pelo ciclo:
  - A fatura é identificada por `AAAA-MM` = **o mês em que ela VENCE** (é como as pessoas falam: "a fatura de setembro" é a que se paga em setembro).
  - O fechamento correspondente é o `dia_fechamento` **imediatamente anterior** ao vencimento:
    - se `dia_vencimento > dia_fechamento`, fechamento e vencimento caem **no mesmo mês**;
    - se `dia_vencimento < dia_fechamento`, o fechamento cai **no mês anterior** ao vencimento (caso comum no Brasil: Nubank fecha 25 e vence 02).
  - Ela contém as despesas do cartão com `data` no intervalo: **do dia seguinte ao fechamento anterior até o dia do fechamento desta fatura** (inclusive).
  - Exemplo A (fecha 03, vence 10): fatura **09/2026** fecha em 03/09, vence em 10/09 e cobre compras de 04/08 a 03/09.
  - Exemplo B (fecha 25, vence 02): fatura **09/2026** fecha em 25/08, vence em 02/09 e cobre compras de 26/07 a 25/08.
  - `dia_vencimento = dia_fechamento` é rejeitado no cadastro (ciclo ambíguo).
- Status derivado da fatura: `aberta` (hoje <= fechamento), `fechada` (hoje > fechamento e não paga), `paga` (existe transferência de pagamento apontando para ela).
- **Pagar fatura**: cria uma transferência `conta escolhida -> cartão` no valor **total** da fatura, com o campo `fatura_mes = AAAA-MM`. Pagamento parcial não existe na v1 (D5); juros que o banco cobrar são lançados pelo usuário como despesa comum.
- Regime de **competência**: a despesa conta no relatório/orçamento pelo mês da **data da compra**, não do pagamento da fatura (o pagamento é transferência e não conta como despesa).

### 5.4 Parcelamento

- Ao lançar despesa parcelada ("R$ 3.600,00 em 12x"), o sistema cria as N transações de uma vez:
  - Cada parcela com `valor = total / N`, arredondado a 2 casas com o resto distribuído nas primeiras parcelas - R$ 33,34 + R$ 33,33 + R$ 33,33 para R$ 100,00 em 3x - garantindo soma parcelas = total ao centavo.
  - Parcela `k` tem data = data da compra + (k-1) meses; a descrição exibe `k/N` ("Notebook 3/12").
  - Todas ligadas por `grupo_parcelamento_id`; status `confirmada` (contam no saldo/fatura quando a data chega).
- Excluir o grupo remove as parcelas com data futura; as passadas ficam (foram fato).
- Parcelamento só em cartão de crédito na v1. Editar série (mudar valor de todas) é v2 - na v1 edita-se parcela a parcela.

### 5.5 Recorrência

- Regra: descrição, valor, tipo (receita/despesa), categoria, conta, frequência (`semanal`/`mensal`/`anual`), dia de referência, data de início, data de fim opcional, `proxima_geracao` (data), `ativa`.
- **Geração por catch-up idempotente**: a cada carregamento autenticado do app (e opcionalmente um cron diário), o sistema processa toda regra ativa com `proxima_geracao <= hoje`: cria a transação com `status = pendente` e a data devida, e avança `proxima_geracao` para a próxima ocorrência. Rodar duas vezes não duplica (a geração avança o ponteiro na mesma operação, em transação de banco).
- Transação pendente aparece destacada ("prevista - confirmar?"); **não** entra no saldo nem no orçamento até o usuário confirmar (1 clique, podendo ajustar o valor - conta de luz varia). Pode ser descartada.
- Editar a regra afeta **só gerações futuras**; transações já criadas não mudam. Desativar a regra para de gerar; nada é apagado.
- Ocorrências futuras aparecem nas telas como "previsto", calculadas da regra em tempo de leitura - nunca gravadas (D7).

### 5.6 Categorias

- No cadastro do usuário, o sistema cria o seed (Apêndice A): categorias de despesa e de receita, mais a categoria de sistema **"Ajuste"** (não arquivável, não editável).
- Usuário cria, renomeia e **arquiva** (nunca exclui): categoria arquivada some dos formulários; histórico e relatórios antigos intactos.
- Categoria tem tipo (`receita` | `despesa`), cor e ícone (para o donut e as listas).

### 5.7 Orçamento

- Um limite mensal por categoria de **despesa** (no máx. 1 orçamento por categoria); vale para todos os meses.
- Progresso do mês = soma despesas `confirmadas` da categoria com data no mês calendário corrente / limite.
- Barra muda de cor: normal (< 80%), atenção (80-100%), estourado (> 100%). Sem bloqueio, sem notificação (v1 informa, não policia).

---

## 6. Histórias de usuário e critérios de aceite

Formato: **Dado / Quando / Então**. Todas assumem usuário autenticado, exceto US-01.

**US-01 - Cadastro e login**
Como visitante, quero criar conta com e-mail/senha para ter meu espaço financeiro privado.
- Dado que me cadastro com e-mail válido, quando confirmo, então entro logado e vejo o dashboard vazio com as categorias padrão já criadas (Apêndice A).
- Dado que erro a senha 5+ vezes, então o lockout do ASP.NET Identity bloqueia novas tentativas temporariamente (proteção contra força bruta).
- Dado que estou deslogado, quando acesso qualquer URL interna, então sou redirecionado ao login.

**US-02 - Criar conta financeira**
Como usuário, quero cadastrar minhas contas (corrente, poupança, dinheiro, cartão) com saldo inicial.
- Dado o formulário, quando escolho tipo `cartao_credito`, então os campos `dia_fechamento` e `dia_vencimento` (1-28) tornam-se obrigatórios; para os demais tipos, não existem.
- Dado que crio "Corrente" com saldo inicial R$ 1.000,00, então o dashboard mostra saldo total R$ 1.000,00.
- Dado uma conta com transações, quando tento excluí-la, então só me é oferecido arquivar; arquivada, ela sai dos formulários de lançamento mas segue nos relatórios.

**US-03 - Lançar despesa/receita**
Como usuário, quero registrar um gasto em menos de 10 segundos.
- Dado o modal de nova transação, quando salvo despesa "Mercado, R$ 150,00, 14/08/2026, conta Corrente", então o saldo da conta cai para R$ 850,00 imediatamente e a transação aparece na lista.
- Valor aceita vírgula decimal ("150,00") e rejeita zero/negativo; data padrão = hoje.
- Dado que edito o valor para R$ 100,00, então saldo e relatórios refletem na hora (nada de saldo em cache - D4).

**US-04 - Transferir entre contas**
Como usuário, quero mover dinheiro entre minhas contas sem que isso conte como gasto.
- Dado transferência de R$ 500,00 Corrente -> Poupança, então Corrente -R$ 500,00, Poupança +R$ 500,00, e as despesas do mês **não** mudam.
- Origem = destino é rejeitado. Transferência não tem categoria.

**US-05 - Fatura do cartão**
Como usuário de cartão, quero ver minhas compras agrupadas na fatura certa e pagá-la.
- Dado cartão que fecha dia 03 e vence dia 10, quando lanço compra em 04/08, então ela aparece na fatura 09/2026; compra em 02/08 aparece na fatura 08/2026 (secao 5.3).
- Dado a fatura fechada com total R$ 1.840,00, quando clico "Pagar fatura" e escolho a conta Corrente, então é criada uma transferência Corrente -> Cartão de R$ 1.840,00, a fatura fica `paga`, e as despesas do mês do pagamento não incluem esse valor.
- A tela de faturas mostra: mês, total, status (aberta/fechada/paga), fechamento e vencimento, e a lista de compras.

**US-06 - Compra parcelada**
Como usuário, quero lançar "R$ 300,00 em 3x" e ver cada parcela na fatura certa.
- Dado o lançamento em 14/08 num cartão que fecha dia 03, então existem 3 transações de R$ 100,00 ("Fone 1/3" 14/08 -> fatura 09/2026; "2/3" 14/09 -> fatura 10/2026; "3/3" 14/10 -> fatura 11/2026).
- O saldo atual do cartão reflete só as parcelas com data <= hoje.
- Quando excluo o grupo em 20/08, então as parcelas 2/3 e 3/3 (futuras) somem e a 1/3 permanece.
- Total que não divide exato (R$ 100,00 em 3x) distribui o resto nas primeiras parcelas e a soma bate o total ao centavo.

**US-07 - Conta recorrente**
Como usuário, quero cadastrar "Internet R$ 120,00 todo dia 20" e só confirmar quando cair.
- Dado a regra criada em 15/08, então nenhuma transação existe ainda; a tela de transações mostra a ocorrência de 20/08 como "prevista".
- Quando o dia 20/08 chega (ou passa) e eu abro o app, então existe exatamente 1 transação pendente de 20/08 - abrir o app de novo não cria outra (idempotência).
- Quando confirmo (podendo ajustar o valor), então ela vira `confirmada` e o saldo cai; quando descarto, ela some e o saldo não muda.
- Quando edito a regra para R$ 130,00, então só as ocorrências ainda não geradas usam o novo valor.

**US-08 - Categorias**
Como usuário, quero adaptar as categorias à minha vida.
- Dado o primeiro login, então vejo o seed do Apêndice A.
- Quando arquivo "Lazer" tendo 200 transações nela, então ela sai dos formulários e os relatórios antigos continuam mostrando "Lazer".
- A categoria "Ajuste" não pode ser renomeada nem arquivada.

**US-09 - Orçamento**
Como usuário, quero limitar meu gasto mensal por categoria e ver o quanto já usei.
- Dado limite "Mercado: R$ 600,00", quando tenho R$ 150,00 gastos em Mercado no mês, então a barra mostra 25% (R$ 150,00 de R$ 600,00).
- Transferências e transações pendentes não contam; despesas de cartão contam pelo mês da compra.
- Quando o gasto passa de 100%, a barra indica estouro (cor), sem bloquear nada.

**US-10 - Dashboard**
Como usuário, quero abrir o app e entender minha situação em 5 segundos.
- Mostra: saldo total e por conta; receitas e despesas do mês corrente (cards); donut de despesas por categoria do mês; últimas 10 transações; pendências de recorrência a confirmar.
- Todos os números respeitam D4/D6 (derivados; transferências fora).

**US-11 - Ajuste de saldo**
Como usuário, quero acertar o saldo quando o app divergir do banco real.
- Dado saldo no app R$ 1.230,00 e no banco R$ 1.198,00, quando informo R$ 1.198,00 em "Ajustar saldo", então é criada despesa de R$ 32,00 na categoria "Ajuste" (despesa) com data de hoje e o saldo passa a R$ 1.198,00. Nenhuma transação antiga é alterada.
- Dado saldo no app R$ 1.198,00 e no banco R$ 1.250,00, então é criada **receita** de R$ 52,00 na categoria "Ajuste" (receita).

**US-12 - Exportar meus dados (LGPD)**
Como usuário, quero baixar tudo que é meu.
- Quando clico "Exportar dados", então baixo um CSV (UTF-8, separador `;`, datas DD/MM/YYYY, valores com vírgula) com todas as minhas transações: data, descrição, categoria, conta, conta destino (se transferência), tipo, status, valor, parcela, e um segundo arquivo/aba com contas e categorias.

**US-13 - Excluir minha conta (LGPD)**
Como usuário, quero apagar minha existência do app.
- Quando confirmo a exclusão (digitar "EXCLUIR" ou re-autenticar), então usuário, contas, transações, regras, orçamentos e categorias são apagados permanentemente (hard delete), incluindo o registro no módulo Identidade; meu login deixa de funcionar.
- Dados de outros usuários não são afetados.

**US-14 - Isolamento entre usuários (segurança)**
Como usuário, meus dados são invisíveis a qualquer outra conta.
- Dado o usuário B logado, quando B requisita por URL/API um recurso do usuário A (id conhecido), então recebe 404 (não 403 - não vazar existência).
- Toda query no servidor filtra por `usuario_id` da **sessão**, nunca por id vindo do cliente.

---

## 7. Modelo de dados conceitual

Postgres. Todos os `id` são UUID; todos os valores monetários são **`numeric(14,2)`** (D9);
todas as tabelas (exceto `usuario`) têm `usuario_id` obrigatório + índice (multi-tenancy: ADR-001,
Decisão 2). `criado_em`/`atualizado_em` em tudo. Um schema Postgres por módulo (`docs/arquitetura.md` secao 2).

```
usuario -- módulo Identidade (tabelas do ASP.NET Identity)
  id, email (único), nome, criado_em (+ colunas padrão do Identity: hash de senha, lockout, etc.)

conta
  id, usuario_id, nome, tipo ∈ {corrente, poupanca, dinheiro, cartao_credito},
  saldo_inicial numeric(14,2), dia_fechamento?, dia_vencimento?, -- só cartão, 1..28
  arquivada (bool)

categoria
  id, usuario_id, nome, tipo ∈ {receita, despesa}, cor, icone,
  sistema (bool: "Ajuste"), arquivada (bool)
  -- única por (usuario_id, nome, tipo) entre ativas

transacao
  id, usuario_id,
  tipo ∈ {receita, despesa, transferencia},
  valor numeric(14,2) (> 0), data (date), descricao,
  status ∈ {pendente, confirmada},
  conta_id (origem),
  conta_destino_id? -- obrigatória sse tipo = transferencia
  categoria_id? -- obrigatória sse tipo != transferencia
  regra_recorrencia_id? -- de onde veio, se gerada
  grupo_parcelamento_id?, parcela_num?, parcela_total?,
  fatura_mes? (char 'AAAA-MM') -- só em transferência de pagamento de fatura
  id_externo? -- reservado p/ dedup de importação (v2); único por usuário quando presente

regra_recorrencia
  id, usuario_id, descricao, valor numeric(14,2), tipo ∈ {receita, despesa},
  categoria_id, conta_id,
  frequencia ∈ {semanal, mensal, anual}, dia_referencia,
  inicio (date), fim? (date), proxima_geracao (date), ativa (bool)

grupo_parcelamento
  id, usuario_id, descricao, valor_total numeric(14,2), parcelas (int), cartao_id

orcamento
  id, usuario_id, categoria_id (única por usuário), limite_mensal numeric(14,2)
```

**O que NÃO existe de propósito:** tabela de saldo (D4), tabela de fatura (derivada - secao 5.3),
tabela de "transação futura de recorrência" (derivada da regra - D7).

**Integridade:** exclusão de usuário faz cascade em tudo (US-13). `conta` e `categoria`
referenciadas por transações usam arquivamento, não delete. Constraints de banco (checks) para as
regras "sse" da `transacao` - não confiar só na aplicação.

---

## 8. Telas do MVP

Aproveitar o layout do protótipo Lovable (tema escuro), reduzido ao escopo v1. O navegador de mês
do topo (‹ Julho de 2026 ›) do protótipo vira o **filtro global de período** de Dashboard, Transações
e Orçamento; a sidebar ganha o menu do usuário (avatar + sair). O item "Investimentos"/"Metas" some
da navegação até a v2.

1. **Login/Cadastro** - telas próprias sobre os endpoints do ASP.NET Identity (login, cadastro, confirmação e reset de senha).
2. **Dashboard** - cards (saldo total, receitas do mês, despesas do mês), donut por categoria, últimas 10 transações, pendências a confirmar. *(Corta do protótipo: card "investido", gráfico 6 meses, metas.)*
3. **Transações** - cards de entradas/saídas/saldo do mês; lista com filtros (mês, conta, categoria, tipo, status) e busca por descrição; modal de nova transação (com abas despesa/receita/transferência, opção de recorrência e de parcelamento quando conta = cartão).
4. **Faturas** - por cartão: mês a mês, status, total, compras, botão pagar. *(Tela nova - não existia no protótipo, nasce da decisão D5.)*
5. **Orçamento** - limite por categoria com barra de progresso do mês.
6. **Configurações** - contas (criar/editar/arquivar/ajustar saldo), categorias, exportar dados, excluir conta.

---

## 9. Requisitos não-funcionais

### 9.1 Escala e desempenho
- Dimensionado para **< 50 usuários ativos** (amigos/família); arquitetura não deve impedir chegar a **centenas** sem redesign (queries por usuário + índices já bastam).
- Páginas principais respondem em < 2 s; nenhuma otimização prematura (sem cache de saldo, sem réplicas - D4 explica).

### 9.2 Custo
- Teto: **~US$ 5/mês (Railway, já pago)** - API .NET + Postgres no Railway; SPA na Vercel (Hobby, grátis; antes previsto no Cloudflare Pages); e-mail transacional no Resend (free). Total extra: R$ 0.

### 9.3 Segurança
- Auth via ASP.NET Identity com bearer tokens (D2/ADR-001). Token verificado **no servidor** em toda rota/API; CORS restrito à origem única da SPA.
- **Isolamento por usuário é a invariante nº 1** (US-14): `usuario_id` sempre da sessão; recurso alheio -> 404.
- HTTPS em tudo (Railway provê); segredos só em variáveis de ambiente; nada de dado sensível em log.
- Validação de entrada no servidor (FluentValidation ou DataAnnotations) mesmo com validação no cliente.

### 9.4 LGPD e dados
- V1: exclusão de conta com hard delete (US-13) e export (US-12).
- **Portão de abertura pública** - antes de aceitar cadastros de desconhecidos, é obrigatório: (a) página de política de privacidade + aceite no cadastro; (b) rotina de backup própria (dump agendado fora do Railway) com **um restore testado e documentado**; (c) revisar retenção de logs. Até lá, cadastro é por convite/link não divulgado.
- Fuso oficial do sistema: America/Sao_Paulo. Locale fixo pt-BR; moeda única BRL.

### 9.5 Qualidade e testabilidade
- Migrações de banco versionadas (EF Core Migrations) - banco nunca é alterado à mão.
- **"Hoje" injetável**: toda regra que depende da data atual (saldo, fatura, recorrência) lê de um provedor de relógio que, em desenvolvimento/teste, aceita override (ex.: env `TEST_TODAY=2026-09-04`). Sem isso a verificação secao 10 é impossível de rodar.
- Testes automatizados mínimos: unidade para as 4 zonas de risco - atribuição de compra->fatura, divisão de parcelas em centavos, catch-up de recorrência (idempotência) e cálculo de saldo.

---

## 10. Verificação fim-a-fim (prova de que o MVP funciona)

Roteiro manual (ou Playwright, depois) que atravessa todas as regras. Executar em ambiente limpo.
Datas assumem "hoje" = **15/08/2026**, avançado via `TEST_TODAY` (secao 9.5). Conferir cada valor exato.

| # | Ação | Resultado esperado |
|---|---|---|
| 1 | Cadastrar usuário A (a@teste.com) e logar | Dashboard vazio; categorias do Apêndice A existem |
| 2 | Criar conta "Corrente", saldo inicial R$ 1.000,00 | Saldo total: **R$ 1.000,00** |
| 3 | Criar cartão "Roxo": fecha dia 03, vence dia 10 | Saldo do cartão: R$ 0,00 |
| 4 | Lançar despesa Mercado R$ 150,00, 14/08, Corrente | Corrente: **R$ 850,00**; donut: Mercado 100% |
| 5 | Lançar receita Salário R$ 3.000,00, 05/08 | Saldo total: **R$ 3.850,00**; cards do mês: receitas R$ 3.000,00 / despesas R$ 150,00 |
| 6 | Lançar no cartão "Fone" R$ 300,00 em 3x, 14/08 | 3 parcelas de R$ 100,00: 1/3 na fatura 09/2026, 2/3 na 10/2026, 3/3 na 11/2026; cartão hoje: **-R$ 100,00**; despesas de agosto: **R$ 250,00** |
| 7 | Criar regra "Internet" R$ 120,00, mensal, dia 20, Corrente, Assinaturas | Nenhuma transação criada; 20/08 aparece como "prevista" |
| 8 | Avançar para 21/08 e recarregar o app **duas vezes** | Exatamente **1** transação pendente de 20/08 (idempotência); saldo inalterado |
| 9 | Confirmar a pendente | Corrente: **R$ 3.730,00**; despesas de agosto: R$ 370,00 |
| 10 | Avançar para 04/09 | Fatura 09/2026 do Roxo: **fechada**, total **R$ 100,00** |
| 11 | Pagar fatura 09/2026 pela Corrente | Transferência de R$ 100,00 criada; fatura **paga**; Corrente: **R$ 3.630,00**; cartão: R$ 0,00; despesas de **setembro** não incluem esses R$ 100,00 |
| 12 | Definir orçamento Mercado R$ 600,00 e voltar a olhar agosto | Barra: R$ 150,00 de R$ 600,00 = **25%** |
| 13 | Ajustar saldo da Corrente para R$ 3.600,00 | Despesa "Ajuste de saldo" R$ 30,00 criada; Corrente: **R$ 3.600,00** |
| 14 | Exportar dados | CSV contém as 9 transações (com a transferência e parcelas 2/3 e 3/3 futuras), datas DD/MM/YYYY, valores com vírgula |
| 15 | Cadastrar usuário B; tentar acessar por URL um id de transação de A | Dashboard de B vazio; recurso de A -> **404** |
| 16 | Usuário A: excluir minha conta | Login de A morre; banco não tem nenhum registro com o usuario_id de A; B continua funcionando |

**Critério de aceite do MVP:** os 16 passos passam, com os valores exatos, num deploy no Railway acessado pelo navegador.

---

## Apêndice A - Seed de categorias (criado no cadastro)

Alinhado às categorias do protótipo Lovable aprovado:

- **Despesas:** Mercado, Alimentação, Moradia, Transporte, Saúde, Academia, Assinaturas, Lazer, Presentes, Educação, Outros
- **Receitas:** Salário, Freelance, Outros
- **Sistema:** duas linhas com o nome "Ajuste" - uma de tipo `receita` e outra de tipo `despesa` (a unicidade é por `usuario_id + nome + tipo`, então convivem). O ajuste positivo usa a de receita, o negativo a de despesa. Nenhuma das duas é editável ou arquivável.

Cada uma com cor e ícone padrão distintos (para o donut e as listas). O protótipo tinha uma categoria
de despesa "Investimentos" - ela **não** entra no seed: aporte não é gasto; na v1, aporte se registra
como transferência para uma conta do usuário (ex.: "Corretora"), coerente com D6.
