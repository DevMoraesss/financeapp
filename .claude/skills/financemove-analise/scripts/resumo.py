#!/usr/bin/env python3
"""
Resumo do CSV exportado pelo FinanceMove (Configuracoes -> Exportar meus dados).

Toda conta de dinheiro usa Decimal, nunca float: e a mesma regra do app (ADR-001, Decisao 3).

Uso:
  python3 resumo.py dados-pessoais/financemove-transacoes.csv [--hoje AAAA-MM-DD] [--meses 6]
                    [--investimentos "Corretora,Tesouro"]

--investimentos: nomes das contas que sao investimento. Transferencia PARA elas conta como aporte;
                 transferencia SAINDO delas, como resgate.
"""
import argparse
import csv
import sys
from collections import defaultdict
from datetime import date, datetime
from decimal import Decimal, ROUND_HALF_UP

CENT = Decimal("0.01")


def money(value: Decimal) -> str:
    """Decimal -> 'R$ 1.234,56' (sem passar por float)."""
    q = value.quantize(CENT, rounding=ROUND_HALF_UP)
    sign = "-" if q < 0 else ""
    inteiro, frac = f"{abs(q):.2f}".split(".")
    grupos = []
    while len(inteiro) > 3:
        grupos.insert(0, inteiro[-3:])
        inteiro = inteiro[:-3]
    grupos.insert(0, inteiro)
    return f"{sign}R$ {'.'.join(grupos)},{frac}"


def pct(part: Decimal, whole: Decimal) -> str:
    if whole == 0:
        return "-"
    return f"{(part / whole * 100).quantize(Decimal('0.1'), rounding=ROUND_HALF_UP)}%".replace(".", ",")


def parse_value(text: str) -> Decimal:
    return Decimal(text.strip().replace(".", "").replace(",", "."))


def month_key(d: date) -> str:
    return f"{d.year:04d}-{d.month:02d}"


def label(key: str) -> str:
    return f"{key[5:]}/{key[:4]}"


def load(path: str):
    rows = []
    with open(path, encoding="utf-8-sig", newline="") as handle:
        for row in csv.DictReader(handle, delimiter=";"):
            fatura = (row.get("Fatura") or "").strip()
            rows.append({
                "data": datetime.strptime(row["Data"].strip(), "%d/%m/%Y").date(),
                "descricao": row["Descricao"],
                "tipo": row["Tipo"].strip(),
                "categoria": row["Categoria"].strip(),
                "conta": row["Conta"].strip(),
                "destino": row["Conta destino"].strip(),
                "confirmada": row["Status"].strip() == "Confirmada",
                "parcela": row["Parcela"].strip(),
                "fatura": f"{fatura[3:]}-{fatura[:2]}" if len(fatura) == 7 else "",
                "valor": parse_value(row["Valor"]),
            })
    return rows


def table(headers, lines):
    out = ["| " + " | ".join(headers) + " |", "|" + "---|" * len(headers)]
    out += ["| " + " | ".join(str(c) for c in line) + " |" for line in lines]
    return "\n".join(out)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("csv")
    parser.add_argument("--hoje", default=date.today().isoformat())
    parser.add_argument("--meses", type=int, default=6)
    parser.add_argument("--investimentos", default="")
    args = parser.parse_args()

    hoje = date.fromisoformat(args.hoje)
    atual = month_key(hoje)
    investimentos = {name.strip().lower() for name in args.investimentos.split(",") if name.strip()}
    rows = load(args.csv)

    # Meses de referencia: os N ultimos ate o atual.
    meses = []
    y, m = hoje.year, hoje.month
    for _ in range(args.meses):
        meses.insert(0, f"{y:04d}-{m:02d}")
        y, m = (y - 1, 12) if m == 1 else (y, m - 1)

    print(f"# Resumo financeiro (hoje = {hoje:%d/%m/%Y})\n")
    print("Valores confirmados, regime de competencia (data da compra). Transferencias nao sao receita nem despesa.\n")

    # 1. Receitas x despesas por mes
    receitas, despesas, aportes, resgates = defaultdict(Decimal), defaultdict(Decimal), defaultdict(Decimal), defaultdict(Decimal)
    for r in rows:
        if not r["confirmada"]:
            continue
        k = month_key(r["data"])
        if r["tipo"] == "Receita":
            receitas[k] += r["valor"]
        elif r["tipo"] == "Despesa" and r["data"] <= hoje:
            despesas[k] += r["valor"]
        elif r["tipo"] == "Transferencia" and investimentos:
            if r["destino"].lower() in investimentos and r["conta"].lower() not in investimentos:
                aportes[k] += r["valor"]
            elif r["conta"].lower() in investimentos and r["destino"].lower() not in investimentos:
                resgates[k] += r["valor"]

    linhas = []
    for k in meses:
        sobra = receitas[k] - despesas[k]
        linha = [label(k), money(receitas[k]), money(despesas[k]), money(sobra), pct(sobra, receitas[k])]
        if investimentos:
            linha += [money(aportes[k]), money(resgates[k])]
        linhas.append(linha)
    headers = ["Mes", "Receitas", "Despesas", "Sobra", "Taxa de poupanca"]
    if investimentos:
        headers += ["Aportes", "Resgates"]
    print("## Receitas x despesas por mes\n")
    print(table(headers, linhas))
    # Mes fechado = anterior ao atual E a partir do primeiro lancamento. Mes vazio antes de a pessoa
    # comecar a usar o app entraria como zero e puxaria a media para baixo.
    primeiro = min((month_key(r["data"]) for r in rows), default=atual)
    fechados = [k for k in meses if primeiro <= k < atual]
    if fechados:
        media = sum((despesas[k] for k in fechados), Decimal(0)) / len(fechados)
        print(f"\nDespesa media dos meses fechados: **{money(media)}** (base para reserva de emergencia: 6x = {money(media * 6)}).")

    # 2. Faturas por cartao
    faturas = defaultdict(Decimal)
    pagas = set()
    for r in rows:
        if r["fatura"] and r["tipo"] == "Despesa":
            faturas[(r["conta"], r["fatura"])] += r["valor"]
        if r["fatura"] and r["tipo"] == "Transferencia":
            pagas.add((r["destino"], r["fatura"]))
    if faturas:
        print("\n## Faturas por cartao (mes = vencimento)\n")
        linhas = []
        for (cartao, mes) in sorted(faturas, key=lambda item: (item[0], item[1])):
            if mes < meses[0]:
                continue
            if (cartao, mes) in pagas:
                situacao = "paga"
            elif mes > atual:
                situacao = "a vencer"
            elif mes == atual:
                situacao = "vence este mes"
            else:
                situacao = "vencida, sem pagamento registrado"
            linhas.append([cartao, label(mes), money(faturas[(cartao, mes)]), situacao])
        print(table(["Cartao", "Fatura", "Total", "Situacao"], linhas))

    # 3. Parcelas ja comprometidas
    futuro = defaultdict(Decimal)
    for r in rows:
        if r["parcela"] and r["tipo"] == "Despesa" and r["data"] > hoje:
            futuro[r["fatura"] or month_key(r["data"])] += r["valor"]
    if futuro:
        print("\n## Parcelas ja comprometidas (compras feitas, ainda por vir)\n")
        print(table(["Fatura/mes", "Valor"], [[label(k), money(v)] for k, v in sorted(futuro.items())]))
        print(f"\nTotal comprometido: **{money(sum(futuro.values(), Decimal(0)))}**")

    # 4. Categorias do mes atual e media
    por_categoria = defaultdict(lambda: defaultdict(Decimal))
    for r in rows:
        if r["confirmada"] and r["tipo"] == "Despesa" and r["data"] <= hoje:
            por_categoria[r["categoria"]][month_key(r["data"])] += r["valor"]
    if por_categoria:
        print(f"\n## Despesas por categoria ({label(atual)} x media dos meses fechados)\n")
        linhas = []
        for categoria, valores in sorted(por_categoria.items(), key=lambda item: -item[1][atual]):
            media = sum((valores[k] for k in fechados), Decimal(0)) / len(fechados) if fechados else Decimal(0)
            linhas.append([categoria, money(valores[atual]), money(media), pct(valores[atual], despesas[atual])])
        print(table(["Categoria", label(atual), "Media", "% do mes"], linhas))

    # 5. Pendencias de recorrencia
    pendentes = [r for r in rows if not r["confirmada"]]
    if pendentes:
        print("\n## Pendentes de confirmar (recorrencias)\n")
        print(table(["Data", "Descricao", "Valor"], [[f"{r['data']:%d/%m/%Y}", r["descricao"], money(r["valor"])] for r in pendentes]))

    return 0


if __name__ == "__main__":
    sys.exit(main())
