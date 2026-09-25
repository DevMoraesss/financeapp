#!/usr/bin/env python3
"""
Taxas de referencia do Banco Central (API publica SGS), com a data de cada valor.

Uso: python3 taxas.py
Series: 432 Selic meta (% a.a.), 4389 CDI anualizado base 252 (% a.a.), 13522 IPCA 12 meses (%),
        433 IPCA do mes (%). Fonte: https://api.bcb.gov.br/dados/serie/bcdata.sgs.<codigo>/dados/ultimos/1
"""
import json
import sys
import urllib.request

SERIES = [
    (432, "Selic meta (Copom)", "% a.a."),
    (4389, "CDI anualizado", "% a.a."),
    (13522, "IPCA acumulado 12 meses", "%"),
    (433, "IPCA do mes", "%"),
]


def fetch(code: int):
    url = f"https://api.bcb.gov.br/dados/serie/bcdata.sgs.{code}/dados/ultimos/1?formato=json"
    with urllib.request.urlopen(url, timeout=15) as response:
        return json.load(response)[0]


def main() -> int:
    ok = True
    for code, name, unit in SERIES:
        try:
            item = fetch(code)
            print(f"{name}: {item['valor'].replace('.', ',')} {unit} (data {item['data']}, serie SGS {code})")
        except Exception as error:  # noqa: BLE001 - relata e segue para a proxima serie
            ok = False
            print(f"{name}: indisponivel ({error}). Busque em bcb.gov.br antes de usar este numero.")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
