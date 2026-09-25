#!/usr/bin/env bash
# Pre-voo do deploy do FinanceMove: pega na sua maquina o que so quebraria no Railway/Vercel.
# Uso: bash .claude/skills/financemove-deploy/scripts/preflight.sh [ref-do-ultimo-deploy] [--rapido]
#   ref-do-ultimo-deploy: commit/tag que esta no ar hoje (lista as migrations novas desde ele)
#   --rapido: pula build, testes e front (so as checagens de configuracao)

set -u
cd "$(git rev-parse --show-toplevel)" || exit 1

fail=0
ok()   { printf '  [ok]   %s\n' "$1"; }
bad()  { printf '  [FALHA] %s\n' "$1"; fail=1; }
warn() { printf '  [aviso] %s\n' "$1"; }

since=""; quick=0
for arg in "$@"; do
  case "$arg" in
    --rapido) quick=1 ;;
    *) since="$arg" ;;
  esac
done

echo "== Configuracao"

if grep -q 'TROQUE-PELO-DOMINIO-DA-API' web/vercel.json; then
  bad "web/vercel.json ainda tem o dominio de exemplo: o /api da Vercel nao vai chegar ao Railway"
else
  ok "web/vercel.json aponta para $(grep -oE 'https://[a-z0-9.-]+\.up\.railway\.app' web/vercel.json | head -1)"
fi

missing=""
for proj in $(find src -name '*.csproj' | sort); do
  grep -qF "COPY $proj" Dockerfile || missing="$missing $proj"
done
[ -z "$missing" ] && ok "Dockerfile copia todos os .csproj de src/" || bad "Dockerfile nao copia:$missing (o restore do deploy quebra)"

missing=""
for ctx in $(grep -rhoE 'class [A-Za-z]+DbContext\b' src/Modules --include=*.cs | awk '{print $2}' | sort -u); do
  grep -q "$ctx" src/Api/DatabaseMigrator.cs || missing="$missing $ctx"
done
[ -z "$missing" ] && ok "DatabaseMigrator migra todos os DbContext" || bad "DatabaseMigrator nao conhece:$missing (tabelas nao seriam criadas no deploy)"

# O Railway nao le mais arquivo de config em servico novo: replica, healthcheck e watch paths
# ficam no painel (docs/deploy.md, secao 3). Aqui so da para lembrar.
warn "confira no painel do Railway: 1 replica, healthcheck /health/ready, Dockerfile como builder"

if git ls-files | grep -qE '(^|/)\.env$|appsettings\.Production\.json'; then bad "arquivo de segredo rastreado pelo git"; else ok "nenhum .env/appsettings.Production rastreado"; fi

if [ -n "$since" ]; then
  echo "== Migrations novas desde $since"
  news=$(git diff --name-only "$since"..HEAD -- 'src/Modules/*/*/Migrations/*.cs' | grep -vE 'Designer|Snapshot' || true)
  if [ -z "$news" ]; then
    ok "nenhuma migration nova"
  else
    printf '%s\n' "$news" | sed 's/^/  - /'
    destructive=$(printf '%s\n' "$news" | xargs grep -lE 'DropColumn|DropTable|AlterColumn|RenameColumn|DropIndex' 2>/dev/null || true)
    [ -n "$destructive" ] && warn "migration destrutiva (drop/alter/rename): faca backup do banco ANTES do deploy -> $destructive"
  fi
fi

if [ "$quick" -eq 0 ]; then
  echo "== Definicao de pronto"
  node tools/clean-chars.mjs --check >/dev/null 2>&1 && ok "pontuacao ASCII" || bad "clean-chars --check"
  dotnet format FinanceMove.sln --verify-no-changes >/dev/null 2>&1 && ok "dotnet format" || bad "dotnet format (rode sem --verify-no-changes para corrigir)"
  out=$(dotnet build FinanceMove.sln -c Release 2>&1)
  echo "$out" | grep -qE '^\s*0 Error\(s\)' && echo "$out" | grep -qE '^\s*0 Warning\(s\)' && ok "build Release sem erro e sem aviso" || bad "build Release (erros ou avisos)"
  dotnet test tests/UnitTests -c Release --no-build >/dev/null 2>&1 && ok "testes unitarios" || bad "testes unitarios"
  if [ -n "${FINANCEMOVE_TEST_POSTGRES:-}" ] || command -v docker >/dev/null 2>&1; then
    dotnet test tests/IntegrationTests -c Release --no-build >/dev/null 2>&1 && ok "testes de integracao" || bad "testes de integracao"
  else
    warn "integracao nao rodou (sem Docker e sem FINANCEMOVE_TEST_POSTGRES); o CI roda no push"
  fi
  (cd web && npm run build >/dev/null 2>&1) && ok "build do front" || bad "npm run build"
  (cd web && npm run lint >/dev/null 2>&1) && ok "lint do front" || bad "npm run lint"
fi

echo
[ "$fail" -eq 0 ] && echo "Pre-voo OK." || echo "Pre-voo com FALHA: corrija antes de subir."
exit "$fail"
