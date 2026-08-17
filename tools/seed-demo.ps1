# Cria uma conta de demonstracao com dados realistas, para conferir o app populado.
# Uso: pwsh tools/seed-demo.ps1   (com a API rodando em http://localhost:5080)
#
# Nao use em producao: e so um atalho de desenvolvimento.

$ErrorActionPreference = 'Stop'
$base = 'http://localhost:5080/api/v1'
$email = 'juan@financemove.local'
$senha = 'financemove123'

function Post($rota, $corpo, $headers) {
  Invoke-RestMethod "$base/$rota" -Method Post -Headers $headers -ContentType 'application/json' `
    -Body ($corpo | ConvertTo-Json)
}

# Cadastro. Se o e-mail ja existir, a API responde sucesso do mesmo jeito (docs/api.md secao 2.1).
$null = Invoke-RestMethod "$base/auth/register" -Method Post -ContentType 'application/json' `
  -Body (@{ name = 'Juan Moraes'; email = $email; password = $senha } | ConvertTo-Json)

$login = Invoke-RestMethod "$base/auth/login" -Method Post -ContentType 'application/json' `
  -Body (@{ email = $email; password = $senha } | ConvertTo-Json)
$h = @{ Authorization = "Bearer $($login.accessToken)" }

# Se ja houver conta cadastrada, o seed ja rodou antes: nao duplica nada.
$contasExistentes = Invoke-RestMethod "$base/accounts" -Headers $h
if ($contasExistentes.accounts.Count -gt 0) {
  Write-Output "Demo ja existe. Entre com $email / $senha"
  exit 0
}

$cat = (Invoke-RestMethod "$base/categories" -Headers $h).categories
function CatId($nome, $tipo) { ($cat | Where-Object { $_.name -eq $nome -and $_.type -eq $tipo })[0].id }

# Contas
$corrente = Post 'accounts' @{ name = 'Nubank Conta'; type = 'checking'; initialBalance = 4200.00 } $h
$poupanca = Post 'accounts' @{ name = 'Reserva'; type = 'savings'; initialBalance = 12000.00 } $h
$carteira = Post 'accounts' @{ name = 'Carteira'; type = 'cash'; initialBalance = 180.00 } $h
$cartao = Post 'accounts' @{ name = 'Nubank Cartao'; type = 'credit_card'; initialBalance = 0
                             closingDay = 25; dueDay = 2 } $h

$mes = (Get-Date).ToString('yyyy-MM')
function Dia($d) { "$mes-$($d.ToString('00'))" }

# Receitas
$null = Post 'transactions' @{ type = 'income'; amount = 9800.00; date = Dia 5; description = 'Salario Tech Ltda'
                               accountId = $corrente.id; categoryId = (CatId 'Salario' 'income') } $h
$null = Post 'transactions' @{ type = 'income'; amount = 1500.00; date = Dia 12; description = 'Projeto freelance'
                               accountId = $corrente.id; categoryId = (CatId 'Freelance' 'income') } $h

# Despesas no debito
$despesas = @(
  @{ v = 2450.00; d = 10; t = 'Aluguel'; c = 'Moradia'; a = $corrente.id },
  @{ v = 620.00; d = 12; t = 'Condominio'; c = 'Moradia'; a = $corrente.id },
  @{ v = 252.20; d = 15; t = 'Energia CPFL'; c = 'Moradia'; a = $corrente.id },
  @{ v = 480.77; d = 8; t = 'Mercado Assai'; c = 'Mercado'; a = $corrente.id },
  @{ v = 318.40; d = 18; t = 'Mercado Pao de Acucar'; c = 'Mercado'; a = $corrente.id },
  @{ v = 129.90; d = 5; t = 'Academia Smart Fit'; c = 'Academia'; a = $corrente.id },
  @{ v = 105.50; d = 20; t = 'Padaria do Ze'; c = 'Alimentacao'; a = $carteira.id },
  @{ v = 480.00; d = 19; t = 'Plano de saude Unimed'; c = 'Saude'; a = $corrente.id }
)
foreach ($x in $despesas) {
  $null = Post 'transactions' @{ type = 'expense'; amount = $x.v; date = Dia $x.d; description = $x.t
                                 accountId = $x.a; categoryId = (CatId $x.c 'expense') } $h
}

# Despesas no cartao, para a fatura ter movimento
$noCartao = @(
  @{ v = 191.62; d = 14; t = 'iFood'; c = 'Alimentacao' },
  @{ v = 89.80; d = 6; t = 'Netflix e Spotify'; c = 'Assinaturas' },
  @{ v = 119.90; d = 11; t = 'Internet Vivo Fibra'; c = 'Assinaturas' },
  @{ v = 179.39; d = 21; t = 'Uber'; c = 'Transporte' },
  @{ v = 376.59; d = 7; t = 'Gasolina Shell'; c = 'Transporte' },
  @{ v = 308.42; d = 24; t = 'Cinema e jantar'; c = 'Lazer' }
)
foreach ($x in $noCartao) {
  $null = Post 'transactions' @{ type = 'expense'; amount = $x.v; date = Dia $x.d; description = $x.t
                                 accountId = $cartao.id; categoryId = (CatId $x.c 'expense') } $h
}

# Compra parcelada: prova o rateio e a fatura de cada parcela
$null = Post 'transactions/installments' @{ description = 'Notebook'; totalAmount = 3600.00; installments = 12
                                            date = Dia 14; cardId = $cartao.id
                                            categoryId = (CatId 'Outros' 'expense') } $h

# Transferencia: nao pode aparecer como despesa
$null = Post 'transactions' @{ type = 'transfer'; amount = 1000.00; date = Dia 6; description = 'Guardando na reserva'
                               accountId = $corrente.id; destinationAccountId = $poupanca.id } $h

# Orcamentos
$limites = @{ Mercado = 1100.00; Alimentacao = 400.00; Transporte = 500.00; Moradia = 3400.00
              Lazer = 300.00; Assinaturas = 250.00; Academia = 150.00; Saude = 600.00 }
foreach ($nome in $limites.Keys) {
  $null = Invoke-RestMethod "$base/budgets/$(CatId $nome 'expense')" -Method Put -Headers $h `
    -ContentType 'application/json' -Body (@{ monthlyLimit = $limites[$nome] } | ConvertTo-Json)
}

# Recorrencias
$null = Post 'recurrences' @{ description = 'Aluguel'; amount = 2450.00; type = 'expense'; frequency = 'monthly'
                              referenceDay = 10; startsOn = (Dia 1); accountId = $corrente.id
                              categoryId = (CatId 'Moradia' 'expense') } $h
$null = Post 'recurrences' @{ description = 'Academia Smart Fit'; amount = 129.90; type = 'expense'
                              frequency = 'monthly'; referenceDay = 5; startsOn = (Dia 1)
                              accountId = $corrente.id; categoryId = (CatId 'Academia' 'expense') } $h

$dash = Invoke-RestMethod "$base/dashboard?month=$mes" -Headers $h
Write-Output ""
Write-Output "Demo criada. Entre em http://localhost:5173 com:"
Write-Output "  e-mail: $email"
Write-Output "  senha:  $senha"
Write-Output ""
Write-Output "Saldo total:        R$ $($dash.totalBalance)"
Write-Output "Receitas do mes:    R$ $($dash.month.income)"
Write-Output "Despesas do mes:    R$ $($dash.month.expenses)"
Write-Output "Categorias no donut: $($dash.expensesByCategory.Count)"
