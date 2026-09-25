# Prompts do FinanceMove

> Prompts prontos para colar no Claude Code, aberto na raiz do repositorio. Cada um aciona uma
> skill de `.claude/skills/` (o nome aparece entre crases), que carrega o checklist certo. Ajuste
> os trechos entre `<>`.

## Uso diario (analise dos seus dados)

Antes: no app, **Configuracoes -> Exportar meus dados**, e salve o CSV em `dados-pessoais/` (pasta
fora do git).

```text
Use a skill financemove-analise com dados-pessoais/<arquivo>.csv. Minhas contas de investimento
no app sao "<Corretora>". Quanto gastei no cartao este mes (compras e fatura), quanto ja tenho de
parcelas comprometidas nos proximos meses e quanto sobrou por mes no ultimo semestre?
```

```text
Use a skill financemove-analise. Quero comprar <produto> por R$ <valor> a vista ou <N>x de
R$ <parcela> sem juros. Meu dinheiro esta num CDB de <X>% do CDI. Vale mais pagar a vista com o
dinheiro investido ou parcelar e deixar rendendo? Considere minha reserva de emergencia pelo CSV.
```

```text
Use a skill financemove-analise. Tenho R$ <valor> para investir, com objetivo <reserva /
entrada de imovel em <ano> / aposentadoria>. Com as taxas de hoje, quais categorias fazem sentido
e quanto renderia liquido em cada uma?
```

## Antes de subir qualquer mudanca

```text
Use a skill financemove-seguranca para revisar o que mudou desde o ultimo commit e depois a skill
financemove-deploy para rodar o pre-voo. Me diga se posso subir.
```

## Proximas fases do produto

A ordem sugerida vai do que mais ajuda ja (cartao) ao que depende de mais peca (mercado ao vivo).
Cada fase e um prompt; rode uma, verifique no app, suba, e so entao a proxima.

### Fase 1 - Cartao no dashboard (rapido, ajuda ja)

```text
Use a skill financemove-feature. No dashboard, quero um card por cartao de credito com: a fatura
aberta (total ate agora, fecha em DD/MM, vence em DD/MM), a proxima fatura e o total de parcelas
ja comprometidas nos proximos 6 meses. Tudo calculado na API (o front so formata). Atualize a SPEC
com os criterios de aceite e escreva os testes de integracao, inclusive o de isolamento.
```

### Fase 2 - 2FA com aplicativo autenticador (antes de convidar mais gente)

```text
Use a skill financemove-feature e depois a financemove-seguranca. Quero 2FA por TOTP (Google
Authenticator, Authy) usando o que o ASP.NET Identity ja oferece (authenticator key + token
provider), sem criptografia escrita a mao: ativar em Configuracoes com QR code, confirmar com um
codigo, 10 codigos de recuperacao de uso unico, login em duas etapas quando ativo, desativar
exigindo senha + codigo. O passo do codigo tambem precisa de rate limit. Registre a decisao num
ADR-003 e atualize docs/api.md.
```

### Fase 3 - Patrimonio e investimentos (quanto eu tenho e quanto guardo)

```text
Use a skill financemove-modulo. Quero o modulo de patrimonio e investimentos, 100% manual por
enquanto, partindo do desenho da v2 em docs/modelo-de-dados.md (asset, asset_movement,
asset_snapshot) e em docs/fluxos-usuario.md secao 5, estendido para bens: imovel, veiculo,
investimento (renda fixa, tesouro, acoes, fundos, cripto) e outros, cada um com valor atual
informado por mim e data da avaliacao; dividas atreladas (financiamento do imovel/carro) com saldo
devedor. Aporte e resgate sao transferencias reais entre contas (nunca despesa). Tela nova
"Patrimonio" com patrimonio liquido (bens + investimentos + contas - dividas), evolucao mensal e
quanto aportei por mes. Antes de codar, me mostre o modelo de dados proposto e como a escrita que
cruza Transactions e o modulo novo fica consistente se falhar no meio.
```

### Fase 4 - Metas (quanto eu quero investir)

```text
Use a skill financemove-modulo. Quero o modulo de metas conforme docs/fluxos-usuario.md secao 4 e
a interface IMetasApi de docs/arquitetura.md secao 4: meta com valor alvo e prazo, aporte como
transferencia real, valor acumulado derivado (nunca coluna), sugestao mensal calculada na API com
os exemplos numericos do doc como teste. Mostre no dashboard o quanto falta por mes para cada meta.
```

### Fase 5 - Mercado ao vivo e simulador (vale a pena usar o dinheiro?)

```text
Use a skill financemove-modulo. Quero um modulo de dados de mercado: um BackgroundService que busca
uma vez por dia na API publica do Banco Central (SGS: 432 Selic meta, 4389 CDI, 13522 IPCA 12m) e
guarda o historico no schema proprio, com fallback para o ultimo valor salvo se a API falhar. Use
essas taxas para: (1) mostrar o rendimento estimado dos meus investimentos de renda fixa no modulo
de patrimonio; (2) um simulador "a vista x parcelado" e "resgatar x financiar" com IR regressivo,
na mesma logica do script .claude/skills/financemove-analise/scripts/avista_parcelado.py, calculado
na API em decimal; (3) um painel "referencias de hoje" (Selic, CDI, IPCA, taxa real) com a data e a
fonte. Cotacao de acoes (brapi) fica como passo seguinte e opcional. Nada de recomendacao de
produto especifico: mostrar dados e contas, com aviso de conteudo educacional.
```

### Depois disso

- **Importar extrato (OFX/CSV do banco):** skill `financemove-feature`, usando o `external_id` que ja
  existe para nao duplicar (SPEC secao 4, v2).
- **Backup automatico fora do Railway** e **Row Level Security** no Postgres: sao pre-requisitos do
  portao de abertura publica (SPEC secao 9.4), se um dia o app sair do circulo de convidados.
