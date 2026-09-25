#!/usr/bin/env bash
# Verificacao pos-deploy do FinanceMove, de fora, como um visitante qualquer.
# Uso: bash .claude/skills/financemove-deploy/scripts/verify.sh https://<projeto>.vercel.app [https://<api>.up.railway.app]

set -u
APP="${1:?informe a URL da Vercel}"; API="${2:-}"
fail=0
check() { # descricao, obtido, esperado
  if [ "$2" = "$3" ]; then printf '  [ok]    %s\n' "$1"; else printf '  [FALHA] %s (veio %s, esperado %s)\n' "$1" "$2" "$3"; fail=1; fi
}
has() { # descricao, texto, padrao
  if printf '%s' "$2" | grep -qiE "$3"; then printf '  [ok]    %s\n' "$1"; else printf '  [FALHA] %s\n' "$1"; fail=1; fi
}

echo "== SPA ($APP)"
spa=$(curl -sS -D - -o /dev/null "$APP/")
check "pagina inicial responde" "$(printf '%s' "$spa" | head -1 | awk '{print $2}')" "200"
has "Content-Security-Policy presente" "$spa" '^content-security-policy:'
has "HSTS presente" "$spa" '^strict-transport-security:'
has "X-Frame-Options DENY" "$spa" '^x-frame-options: *deny'
check "rota interna da SPA (F5 em /faturas) cai no index" "$(curl -s -o /dev/null -w '%{http_code}' "$APP/faturas")" "200"

echo "== API pelo proxy da Vercel"
check "rota protegida sem token = 401" "$(curl -s -o /dev/null -w '%{http_code}' "$APP/api/v1/accounts")" "401"
check "cadastro sem convite = 403" "$(curl -s -o /dev/null -w '%{http_code}' -H 'Content-Type: application/json' \
  -d '{"name":"verificacao","email":"verificacao@exemplo.invalid","password":"umaFraseBemLonga"}' "$APP/api/v1/auth/register")" "403"
apih=$(curl -sS -D - -o /dev/null "$APP/api/v1/accounts")
has "API com Cache-Control no-store" "$apih" '^cache-control: *no-store'
has "API com X-Content-Type-Options nosniff" "$apih" '^x-content-type-options: *nosniff'

if [ -n "$API" ]; then
  echo "== API direta ($API)"
  ready=$(curl -s "$API/health/ready")
  has "/health/ready Healthy (banco conectado)" "$ready" '"status":"Healthy"'
fi

echo
[ "$fail" -eq 0 ] && echo "Deploy verificado." || echo "Deploy com FALHA em pelo menos uma checagem."
exit "$fail"
