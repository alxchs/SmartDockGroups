#!/usr/bin/env python3
"""Regenera os apêndices de docs/PROMPT_GERACAO_UNICA.md a partir dos arquivos reais.

Os apêndices (paleta, ícones, textos nos idiomas, constantes) são copiados do código, nunca
digitados: o prompt de geração em um passo só vale se bater com o app. Rode depois de mudar
`Theming/*.xaml`, `Localization/*.json` ou as constantes citadas, e confira com `git diff`.

    python tools/atualizar_apendices_prompt.py
"""
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
APP = ROOT / "src" / "SmartDockGroups.App"
CORE = ROOT / "src" / "SmartDockGroups.Core"
DOC = ROOT / "docs" / "PROMPT_GERACAO_UNICA.md"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8-sig")


def palette() -> str:
    def brushes(name):
        text = read(APP / "Theming" / name)
        return dict(re.findall(r'<(?:SolidColorBrush|Color) x:Key="(\w+)"(?: Color="(#[0-9A-Fa-f]+)")?[^>]*>(?:(#[0-9A-Fa-f]+)</Color>)?', text) and
                    [(m[0], m[1] or m[2]) for m in re.findall(r'x:Key="(\w+)"(?: Color="(#[0-9A-Fa-f]+)")?[^>]*>(?:(#[0-9A-Fa-f]+)</Color>)?', text)])
    dark, light = brushes("Dark.xaml"), brushes("Light.xaml")
    lines = ["| Chave | Escuro | Claro |", "|---|---|---|"]
    for key in dark:
        lines.append(f"| `{key}` | `{dark[key]}` | `{light.get(key, '')}` |")
    lines.append("")
    lines.append("Cada chave é um `SolidColorBrush` (a última, `AppShadowColor`, é um `Color`) num `ResourceDictionary`; "
                 "o app troca o dicionário inteiro ao mudar o tema e todos os controles usam `DynamicResource`.")
    return "\n".join(lines)


def icons() -> str:
    text = read(APP / "Theming" / "Icons.xaml")
    rows = re.findall(r'<StreamGeometry x:Key="(Icon\w+)">(.*?)</StreamGeometry>', text, re.S)
    out = ["Cada ícone é um `StreamGeometry` (a sintaxe de dados do `Path`):", ""]
    out += [f"- `{name}`: `{' '.join(data.split())}`" for name, data in rows]
    out.append("")
    out.append("`IconStrokeThickness=1.7` e `IconNominalSize=24`. `AppIcons.Create(chave, pincel, tamanho)` põe o "
               "desenho num canvas de exatamente 24×24, traça com `StrokeThickness=1.7`, pontas e junções "
               "arredondadas, sem preenchimento, e o escala num `Viewbox` (sem o canvas fixo, um ícone baixo "
               "seria esticado até os mesmos limites de um alto e o conjunto perderia o peso comum).")
    return "\n".join(out)


def used_string_keys() -> set:
    """Chaves de texto que o código (C# e XAML) realmente pede."""
    shape = r"(?:group|item|settings|tray|desktop|overlay|common)\.[A-Za-z]+"
    pattern = re.compile(rf'"({shape})"|Loc ({shape})')
    keys = set()
    for path in list(APP.rglob("*.cs")) + list(APP.rglob("*.xaml")):
        if "obj" in path.parts or "bin" in path.parts:
            continue
        for match in pattern.finditer(read(path)):
            keys.add(match.group(1) or match.group(2))
    return keys


def strings(code: str) -> str:
    data = json.loads(read(APP / "Localization" / f"Strings.{code}.json"))
    used = used_string_keys()
    kept = {key: value for key, value in data.items() if key in used}
    legacy = len(data) - len(kept)
    out = [f"{len(kept)} chaves, todas usadas pelo código. `{{0}}`, `{{1}}`, `{{2}}` são argumentos de "
           "`string.Format`; a sequência `\\n` é quebra de linha. "
           f"(O arquivo do repositório ainda carrega {legacy} chaves legadas sem uso — do antigo editor de temas "
           "e de menus removidos —; uma regeneração pode omiti-las.)", "", "```json"]
    out.append(json.dumps(kept, ensure_ascii=False, indent=2))
    out.append("```")
    return "\n".join(out)


def constants() -> str:
    sources = [
        ("Desktop/DesktopGroupWindow.cs", "Janela do grupo"),
        ("Desktop/AppFolderTile.cs", "Ladrilho do App Folder"),
        ("Desktop/GroupOverlayWindow.cs", "Folha do App Folder"),
        ("Desktop/MonitorPlacement.cs", "Monitores"),
        ("Services/GlobalHotkeyService.cs", "Atalhos globais"),
    ]
    out = []
    for relative, title in sources:
        text = read(APP / relative)
        rows = re.findall(r"const (?:double|int|uint|string) (\w+) = ([^;]+);", text)
        if not rows:
            continue
        out.append(f"**{title}** (`{relative}`)")
        out.append("")
        out += [f"- `{name} = {value.strip()}`" for name, value in rows]
        out.append("")
    dock = read(CORE / "Models" / "DockState.cs")
    for name, value in re.findall(r"const (?:double|int) (\w+) = ([^;]+);", dock):
        out.append(f"- `DockLayout.{name} = {value.strip()}`")
    limits = read(CORE / "Models" / "GroupLimits.cs")
    for name, value in re.findall(r"const (?:double|int) (\w+) = ([^;]+);", limits):
        out.append(f"- `GroupLimits.{name} = {value.strip()}`")
    return "\n".join(out)


BLOCKS = {
    "palette": palette,
    "icons": icons,
    "strings-en": lambda: strings("en"),
    "strings-pt": lambda: strings("pt"),
    "constants": constants,
}


def main() -> int:
    text = DOC.read_text(encoding="utf-8")
    for name, build in BLOCKS.items():
        pattern = re.compile(rf"(<!-- BEGIN:{re.escape(name)} -->\n).*?(<!-- END:{re.escape(name)} -->)", re.S)
        if not pattern.search(text):
            print(f"marcador ausente: {name}", file=sys.stderr)
            return 1
        text = pattern.sub(lambda m: f"{m.group(1)}{build()}\n{m.group(2)}", text, count=1)
    DOC.write_text(text, encoding="utf-8")
    print(f"apêndices atualizados em {DOC.relative_to(ROOT)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
