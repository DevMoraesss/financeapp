#!/usr/bin/env python3
"""
A vista ou parcelado? Quanto custa HOJE cada opcao, considerando que o dinheiro parado rende.

Pergunta que responde: "compro a vista (com desconto) ou parcelo e deixo o dinheiro rendendo?" e
tambem "uso o dinheiro investido para pagar a vista?". Para cada parcela, calcula quanto seria
preciso aplicar hoje para ter o valor dela no vencimento, ja descontado o IR sobre o rendimento.
A soma e o custo do parcelado em dinheiro de hoje; compare com o preco a vista.

Uso:
  python3 avista_parcelado.py --avista 900 --parcela 100 --parcelas 10 --taxa-anual 13.65
          [--percentual-cdi 100] [--primeira-em-meses 1] [--isento]

--taxa-anual: taxa do investimento (ex.: CDI atual, de taxas.py)
--percentual-cdi: quanto do CDI o investimento paga (CDB 100% CDI = 100; poupanca nao se encaixa aqui)
--isento: LCI/LCA e similares (sem IR)
IR regressivo da renda fixa por prazo de cada resgate: ate 180 dias 22,5%; ate 360, 20%; ate 720,
17,5%; acima, 15%. Confirme se a tabela ainda e essa antes de apresentar (regra pode mudar).
Ignora IOF (so incide em resgate com menos de 30 dias).
"""
import argparse
import sys
from decimal import Decimal, ROUND_HALF_UP, getcontext

getcontext().prec = 28
CENT = Decimal("0.01")


def money(value: Decimal) -> str:
    q = value.quantize(CENT, rounding=ROUND_HALF_UP)
    inteiro, frac = f"{abs(q):.2f}".split(".")
    grupos = []
    while len(inteiro) > 3:
        grupos.insert(0, inteiro[-3:])
        inteiro = inteiro[:-3]
    grupos.insert(0, inteiro)
    return f"{'-' if q < 0 else ''}R$ {'.'.join(grupos)},{frac}"


def ir_rate(days: int) -> Decimal:
    if days <= 180:
        return Decimal("0.225")
    if days <= 360:
        return Decimal("0.20")
    if days <= 720:
        return Decimal("0.175")
    return Decimal("0.15")


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--avista", type=Decimal, required=True)
    p.add_argument("--parcela", type=Decimal, required=True)
    p.add_argument("--parcelas", type=int, required=True)
    p.add_argument("--taxa-anual", type=Decimal, required=True, help="% a.a., ex.: 13.65")
    p.add_argument("--percentual-cdi", type=Decimal, default=Decimal(100))
    p.add_argument("--primeira-em-meses", type=int, default=1)
    p.add_argument("--isento", action="store_true")
    a = p.parse_args()

    anual = a.taxa_anual / 100 * a.percentual_cdi / 100
    mensal = (1 + anual) ** (Decimal(1) / 12) - 1

    total_hoje = Decimal(0)
    print("| Parcela | Vence em | IR | Aplicar hoje |")
    print("|---|---|---|---|")
    for k in range(a.parcelas):
        meses = a.primeira_em_meses + k
        bruto = (1 + mensal) ** meses - 1
        ir = Decimal(0) if a.isento else ir_rate(meses * 30)
        liquido = bruto * (1 - ir)
        hoje = a.parcela / (1 + liquido)
        total_hoje += hoje
        print(f"| {k + 1}/{a.parcelas} | {meses} mes(es) | {'isento' if a.isento else f'{ir * 100:.1f}%'.replace('.', ',')} | {money(hoje)} |")

    parcelado_nominal = a.parcela * a.parcelas
    print()
    taxa_aa = str((anual * 100).quantize(Decimal("0.01"))).replace(".", ",")
    taxa_am = str((mensal * 100).quantize(Decimal("0.0001"))).replace(".", ",")
    print(f"Taxa usada: {taxa_aa}% a.a. ({taxa_am}% a.m. bruto, antes do IR)")
    print(f"Parcelado, soma nominal: {money(parcelado_nominal)}")
    print(f"Parcelado, custo em dinheiro de hoje: {money(total_hoje)}")
    print(f"A vista: {money(a.avista)}")
    diferenca = a.avista - total_hoje
    if diferenca > 0:
        print(f"-> Parcelar e deixar rendendo sai {money(diferenca)} mais barato (em valor de hoje).")
    elif diferenca < 0:
        print(f"-> Pagar a vista sai {money(-diferenca)} mais barato (em valor de hoje).")
    else:
        print("-> Empate.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
