# Teste de fumaca da API: percorre o caminho principal do app, do cadastro ao dashboard.
# Uso: pwsh tools/smoke.ps1  (com a API rodando em http://localhost:5080)

$ErrorActionPreference = 'Stop'
$base = 'http://localhost:5080/api/v1'
$email = "smoke-$(Get-Random)@teste.local"
$senha = 'senhaDeTeste123'

function Show($titulo, $valor) { Write-Output ("{0,-46} {1}" -f $titulo, $valor) }

Write-Output "=========== TESTE DE FUMACA DA API ==========="

# 1. Cadastro. Dispara o evento que cria as categorias do seed.
$null = Invoke-RestMethod "$base/auth/register" -Method Post -ContentType 'application/json' `
  -Body (@{ name = 'Juan Smoke'; email = $email; password = $senha } | ConvertTo-Json)
Show '1. cadastro' 'ok'

# 2. Login. Guarda o access token; o refresh vem em cookie httpOnly.
$sessao = $null
$login = Invoke-RestMethod "$base/auth/login" -Method Post -ContentType 'application/json' `
  -Body (@{ email = $email; password = $senha } | ConvertTo-Json) -SessionVariable sessao
$headers = @{ Authorization = "Bearer $($login.accessToken)" }
Show '2. login' "token de $($login.user.name)"

# 3. Seed de categorias criado pelo modulo Transactions ao ouvir UserRegistered.
$categorias = Invoke-RestMethod "$base/categories" -Headers $headers
$ajuste = $categorias.categories | Where-Object { $_.system }
Show '3. categorias do seed' "$($categorias.categories.Count) (sistema: $($ajuste.Count))"

$mercado = $categorias.categories | Where-Object { $_.name -eq 'Mercado' } | Select-Object -First 1
$salario = $categorias.categories | Where-Object { $_.name -eq 'Salario' } | Select-Object -First 1
$lazer = $categorias.categories | Where-Object { $_.name -eq 'Lazer' } | Select-Object -First 1

# 4. Conta corrente com saldo inicial.
$corrente = Invoke-RestMethod "$base/accounts" -Method Post -Headers $headers -ContentType 'application/json' `
  -Body (@{ name = 'Corrente'; type = 'checking'; initialBalance = 1000.00 } | ConvertTo-Json)
Show '4. conta corrente criada' "saldo inicial R$ $($corrente.initialBalance)"

# 5. Cartao no formato que quebrava a regra antiga: fecha 25, vence 02 do mes seguinte.
$cartao = Invoke-RestMethod "$base/accounts" -Method Post -Headers $headers -ContentType 'application/json' `
  -Body (@{ name = 'Nubank'; type = 'credit_card'; initialBalance = 0; closingDay = 25; dueDay = 2 } | ConvertTo-Json)
Show '5. cartao criado' "fecha $($cartao.closingDay), vence $($cartao.dueDay)"

# 6. Despesa simples.
$despesa = Invoke-RestMethod "$base/transactions" -Method Post -Headers $headers -ContentType 'application/json' `
  -Body (@{ type = 'expense'; amount = 150.00; date = '2026-08-14'; description = 'Mercado Assai';
            accountId = $corrente.id; categoryId = $mercado.id } | ConvertTo-Json)
Show '6. despesa de R$ 150,00' "saldo da conta: R$ $($despesa.accountBalance)"

# 7. Receita.
$receita = Invoke-RestMethod "$base/transactions" -Method Post -Headers $headers -ContentType 'application/json' `
  -Body (@{ type = 'income'; amount = 3000.00; date = '2026-08-05'; description = 'Salario';
            accountId = $corrente.id; categoryId = $salario.id } | ConvertTo-Json)
Show '7. receita de R$ 3.000,00' "saldo da conta: R$ $($receita.accountBalance)"

# 8. Parcelamento: o rateio precisa fechar exatamente com o total.
$parcelado = Invoke-RestMethod "$base/transactions/installments" -Method Post -Headers $headers -ContentType 'application/json' `
  -Body (@{ description = 'Fone'; totalAmount = 100.00; installments = 3; date = '2026-08-14';
            cardId = $cartao.id; categoryId = $lazer.id } | ConvertTo-Json)
$valores = ($parcelado.transactions | ForEach-Object { $_.amount })
$soma = ($valores | Measure-Object -Sum).Sum
Show '8. R$ 100,00 em 3x' "$($valores -join ' + ') = $soma"
Show '   fatura de cada parcela' (($parcelado.transactions | ForEach-Object { $_.statementMonth }) -join ', ')

# 9. Transferencia. Nao pode contar como despesa.
$poupanca = Invoke-RestMethod "$base/accounts" -Method Post -Headers $headers -ContentType 'application/json' `
  -Body (@{ name = 'Poupanca'; type = 'savings'; initialBalance = 0 } | ConvertTo-Json)
$transferencia = Invoke-RestMethod "$base/transactions" -Method Post -Headers $headers -ContentType 'application/json' `
  -Body (@{ type = 'transfer'; amount = 500.00; date = '2026-08-15'; description = 'Guardando';
            accountId = $corrente.id; destinationAccountId = $poupanca.id } | ConvertTo-Json)
Show '9. transferencia de R$ 500,00' "corrente R$ $($transferencia.accountBalance), poupanca R$ $($transferencia.destinationAccountBalance)"

# 10. Orcamento.
$null = Invoke-RestMethod "$base/budgets/$($mercado.id)" -Method Put -Headers $headers -ContentType 'application/json' `
  -Body (@{ monthlyLimit = 600.00 } | ConvertTo-Json)
$orcamento = Invoke-RestMethod "$base/budgets?month=2026-08" -Headers $headers
$item = $orcamento.items | Select-Object -First 1
Show '10. orcamento Mercado' "R$ $($item.spent) de R$ $($item.limit) = $($item.percent)% ($($item.status))"

# 11. Dashboard: prova que transferencia nao entrou como despesa.
$dash = Invoke-RestMethod "$base/dashboard?month=2026-08" -Headers $headers
Show '11. dashboard saldo total' "R$ $($dash.totalBalance)"
Show '    receitas / despesas do mes' "R$ $($dash.month.income) / R$ $($dash.month.expenses)"
Show '    categorias no donut' $dash.expensesByCategory.Count

# 12. Ajuste de saldo (SPEC US-11).
$ajusteSaldo = Invoke-RestMethod "$base/accounts/$($corrente.id)/adjust-balance" -Method Post -Headers $headers `
  -ContentType 'application/json' -Body (@{ realBalance = 2300.00 } | ConvertTo-Json)
Show '12. ajuste de saldo' "$($ajusteSaldo.createdTransaction.type) de R$ $($ajusteSaldo.createdTransaction.amount) -> R$ $($ajusteSaldo.currentBalance)"

# 13. Fatura do cartao.
$faturas = Invoke-RestMethod "$base/cards/$($cartao.id)/statements" -Headers $headers
$comMovimento = $faturas.statements | Where-Object { $_.total -gt 0 } | Select-Object -First 1
Show '13. fatura com movimento' "$($comMovimento.month): R$ $($comMovimento.total) ($($comMovimento.status))"
Show '    fecha / vence' "$($comMovimento.closingDate) / $($comMovimento.dueDate)"

# 14. Recorrencia: criar a regra NAO cria transacao.
$antes = (Invoke-RestMethod "$base/transactions?month=2026-08" -Headers $headers).pagination.total
$recorrencia = Invoke-RestMethod "$base/recurrences" -Method Post -Headers $headers -ContentType 'application/json' `
  -Body (@{ description = 'Internet'; amount = 120.00; type = 'expense'; frequency = 'monthly';
            referenceDay = 20; startsOn = '2026-08-15'; accountId = $corrente.id; categoryId = $mercado.id } | ConvertTo-Json)
$depois = (Invoke-RestMethod "$base/transactions?month=2026-08" -Headers $headers).pagination.total
Show '14. recorrencia criada' "proxima geracao em $($recorrencia.nextRunOn); transacoes: $antes -> $depois"

# 15. Isolamento entre usuarios: recurso alheio responde 404, nunca 403 (SPEC US-14).
$outroEmail = "outro-$(Get-Random)@teste.local"
$null = Invoke-RestMethod "$base/auth/register" -Method Post -ContentType 'application/json' `
  -Body (@{ name = 'Outro'; email = $outroEmail; password = $senha } | ConvertTo-Json)
$outroLogin = Invoke-RestMethod "$base/auth/login" -Method Post -ContentType 'application/json' `
  -Body (@{ email = $outroEmail; password = $senha } | ConvertTo-Json)
$outroHeaders = @{ Authorization = "Bearer $($outroLogin.accessToken)" }
try {
  $null = Invoke-RestMethod "$base/accounts/$($corrente.id)" -Headers $outroHeaders
  Show '15. isolamento entre usuarios' 'FALHOU: outro usuario leu a conta'
} catch {
  Show '15. isolamento entre usuarios' "HTTP $($_.Exception.Response.StatusCode.value__) (esperado 404)"
}

# 16. Sem token nenhum.
try {
  $null = Invoke-RestMethod "$base/accounts"
  Show '16. rota protegida sem token' 'FALHOU: respondeu sem autenticacao'
} catch {
  Show '16. rota protegida sem token' "HTTP $($_.Exception.Response.StatusCode.value__) (esperado 401)"
}

# 17. Refresh do token pelo cookie httpOnly.
$refresh = Invoke-RestMethod "$base/auth/refresh" -Method Post -WebSession $sessao
Show '17. refresh do access token' ($(if ($refresh.accessToken -ne $login.accessToken) { 'token novo emitido' } else { 'FALHOU: token repetido' }))

# 18. Exclusao de conta (LGPD): um DELETE apaga tudo em cascata.
$null = Invoke-RestMethod "$base/me" -Method Delete -Headers $headers -ContentType 'application/json' `
  -Body (@{ confirmation = 'EXCLUIR'; password = $senha } | ConvertTo-Json)
try {
  $null = Invoke-RestMethod "$base/auth/login" -Method Post -ContentType 'application/json' `
    -Body (@{ email = $email; password = $senha } | ConvertTo-Json)
  Show '18. exclusao de conta (LGPD)' 'FALHOU: login ainda funciona'
} catch {
  Show '18. exclusao de conta (LGPD)' "login morreu com HTTP $($_.Exception.Response.StatusCode.value__)"
}

Write-Output "=========== FIM ==========="
