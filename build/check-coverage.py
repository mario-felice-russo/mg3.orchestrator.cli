"""Fallisce se la copertura delle righe di un assembly è sotto la soglia.

Uso: python3 build/check-coverage.py <Summary.json> <soglia> <assembly> [<assembly> ...]
"""
import json
import sys


def main() -> int:
    if len(sys.argv) < 4:
        print(__doc__)
        return 2

    path, threshold, names = sys.argv[1], float(sys.argv[2]), sys.argv[3:]
    with open(path, encoding="utf-8") as handle:
        assemblies = {a["name"]: a["coverage"] for a in json.load(handle)["coverage"]["assemblies"]}

    failed = False
    for name in names:
        value = assemblies.get(name)
        if value is None:
            print(f"ERRORE: assembly '{name}' assente dal report di copertura.")
            failed = True
        elif value < threshold:
            print(f"ERRORE: {name} {value:.1f}% < {threshold:.0f}%")
            failed = True
        else:
            print(f"OK: {name} {value:.1f}% >= {threshold:.0f}%")

    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
