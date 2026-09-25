#!/usr/bin/env bash
# Varredura heuristica de seguranca do FinanceMove.
# Cada linha listada e um SUSPEITO para conferir no codigo, nao um veredito.
# Uso: bash .claude/skills/financemove-seguranca/scripts/scan.sh [--deps]

set -u
cd "$(git rev-parse --show-toplevel)" || exit 1

found=0

# Descarta linhas que sao so comentario (//, ///, * de bloco): falar de float num comentario nao e bug.
code_only() { grep -vE '^[^:]+:[0-9]+:\s*(//|\*|/\*)' || true; }

section() { printf '\n== %s\n' "$1"; }
report() {
  if [ -n "$1" ]; then
    printf '%s\n' "$1" | sed 's/^/  /'
    found=$((found + 1))
  else
    echo "  ok"
  fi
}

section "Dinheiro em ponto flutuante no C# (regra numero um)"
report "$(grep -rnE '\b(double|float)\b' src --include=*.cs | grep -v '/Migrations/' | code_only)"

section "Relogio do sistema fora do IClock"
report "$(grep -rnE 'DateTime(Offset)?\.(Now|UtcNow|Today)\b' src --include=*.cs | grep -v 'SystemClock.cs' | code_only)"

section "userId vindo do cliente em vez do token (rota, query ou corpo)"
report "$(grep -rnE '(Guid|string)\??\s+userId\b|\bUserId\b' src/Api/Endpoints --include=*.cs || true)"

section "SQL montado com texto (FromSqlRaw/ExecuteSqlRaw); FromSql(\$\"...\") e parametrizado e seguro"
report "$(grep -rnE 'FromSqlRaw|ExecuteSqlRaw|SqlQueryRaw' src --include=*.cs || true)"

section "403 para recurso (deveria ser 404: 403 confirma que o id existe)"
report "$(grep -rnE 'Results\.Forbid|Status403Forbidden|Results\.StatusCode\(403\)' src/Api --include=*.cs || true)"

section "Arquivo de endpoints sem RequireAuthorization"
report "$(grep -L 'RequireAuthorization' src/Api/Endpoints/*.cs | grep -v 'AuthEndpoints.cs' || true)"

section "Log que pode carregar dado sensivel"
report "$(grep -rnE 'Log(Information|Warning|Error|Debug|Critical|Trace)\(.*([Pp]assword|[Ss]enha|[Tt]oken|[Ee]mail|Amount|Balance)' src --include=*.cs || true)"

section "CORS aberto ou cookie cross-site"
report "$(grep -rnE 'AllowAnyOrigin|WithOrigins\(\"\*\"\)|SameSiteMode\.None' src --include=*.cs || true)"

section "Front: token em storage, HTML cru ou fetch fora do api.ts"
report "$({ grep -rnE 'localStorage|sessionStorage|dangerouslySetInnerHTML|innerHTML\s*=' web/src; grep -rn 'fetch(' web/src | grep -v 'web/src/lib/api.ts'; } | code_only)"

section "Front: URL absoluta de API (quebra a mesma origem e o cookie Strict)"
report "$(grep -rnE "['\"\`]https?://" web/src --include=*.ts --include=*.tsx | code_only)"

section "Front: aritmetica com dinheiro (o front so formata)"
report "$(grep -rnE '\.(amount|currentBalance|totalBalance|total|spent|limit|difference|income|expenses|balance)\s*[-+*/]|[-+*/]\s*[a-zA-Z_.]*\.(amount|currentBalance|total|spent|limit)\b|\.reduce\(' web/src || true)"

section "CSP da Vercel afrouxada"
report "$(grep -nE "unsafe-eval|script-src[^;]*unsafe-inline|default-src \*" web/vercel.json || true)"

section "Segredo a caminho do git"
secrets="$(git diff --cached --name-only 2>/dev/null | grep -E '(^|/)\.env$|\.env\.|appsettings\.Production|\.pfx$|\.key$' | grep -v '\.env\.example' || true)"
secrets="$secrets$(git grep -nIE '(SigningKey|Password|InviteCode|DATABASE_URL)\s*["]?\s*[:=]\s*["]?[A-Za-z0-9+/=_-]{24,}' -- . ':!*.md' ':!**/Migrations/**' 2>/dev/null | grep -vE 'nao-use-em-producao|financemove_dev|COLE-AQUI' || true)"
report "$secrets"

section "Dado pessoal fora de dados-pessoais/ (CSV/OFX rastreado pelo git)"
report "$(git ls-files | grep -iE '\.(csv|ofx|xlsx)$' || true)"

if [ "${1:-}" = "--deps" ]; then
  section "Pacotes NuGet com vulnerabilidade conhecida"
  dotnet package list --project FinanceMove.sln --vulnerable --include-transitive 2>&1 | grep -E 'has the following vulnerable|>' || echo "  ok"
  section "Pacotes npm com vulnerabilidade alta (producao)"
  (cd web && npm audit --omit=dev --audit-level=high 2>&1 | tail -3)
fi

printf '\n%s secao(oes) com suspeitos para conferir.\n' "$found"
