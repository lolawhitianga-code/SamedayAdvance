#!/usr/bin/env python3
"""Static check: every {Binding Path} in the XAML must exist as a real property
on the compiled ViewModel/row types, and every {StaticResource} must be defined."""
import json
import re
import subprocess
import sys
from pathlib import Path

TOOLS = Path(__file__).parent
APP = TOOLS.parent / "src/DiagFileMonitor.App"

# Properties that come from WPF itself rather than our own types.
WPF_PROVIDED = {
    "Name", "ItemCount",          # CollectionViewGroup, in GroupItem templates
    "IsChecked", "Text", "SelectedItem", "Tag", "DataContext",
}


TARGETS = [
    TOOLS / "vmcheck/bin/Debug/net8.0-windows/vmcheck.dll",                       # App ViewModels + converters
    TOOLS / "vmcheck/bin/Debug/net8.0-windows/DiagFileMonitor.Core.dll",          # Core models bound by the grid
]


def dump_properties():
    merged = {}
    for target in TARGETS:
        out = subprocess.run(
            ["dotnet", "run", "--project", str(TOOLS / "dumpprops/dumpprops.csproj"), "--no-build", "--",
             str(target),
             str(TOOLS / "vmcheck/bin/Debug/net8.0-windows"),
             "/usr/lib/dotnet/packs/Microsoft.NETCore.App.Ref/8.0.31/ref/net8.0",
             "/root/.nuget/packages/microsoft.windowsdesktop.app.ref/8.0.31/ref/net8.0"],
            capture_output=True, text=True, cwd=TOOLS)
        if out.returncode != 0:
            print(f"FAILED to dump properties from {target}:\n{out.stderr}")
            sys.exit(2)
        merged.update(json.loads(out.stdout))
    return merged


def binding_paths(xaml: str):
    """Yield (path, context) for every binding expression in the file."""
    # {Binding Foo}, {Binding Path=Foo}, {Binding Foo, Converter=...}
    for m in re.finditer(r"\{Binding\s+([^}]*)\}", xaml):
        body = m.group(1).strip()
        if not body:
            continue
        first = body.split(",")[0].strip()
        if first.startswith("Path="):
            first = first[len("Path="):].strip()
        elif "=" in first:
            continue  # e.g. {Binding ElementName=x, Path=y} handled below
        if first:
            yield first, m.group(0)
    # <Binding Path="Foo" /> inside MultiBinding
    for m in re.finditer(r"<Binding\s+Path=\"([^\"]+)\"", xaml):
        yield m.group(1), m.group(0)


def main():
    props = dump_properties()
    known = set(WPF_PROVIDED)
    for type_name, names in props.items():
        known.update(names)

    problems_at_start = []
    problems = []
    checked = 0

    all_keys = set()
    for xaml_file in APP.rglob("*.xaml"):
        # A repeated x:Key inside one dictionary is not a build error - WPF throws when it
        # parses the file, so the app dies on startup with no warning beforehand.
        keys_here = re.findall(r"x:Key=\"([^\"]+)\"", xaml_file.read_text())
        seen = set()
        for key in keys_here:
            if key in seen:
                problems_at_start.append(f"{xaml_file.name}: x:Key '{key}' is defined twice")
            seen.add(key)
        all_keys.update(keys_here)

    problems.extend(problems_at_start)

    for xaml_file in sorted(APP.rglob("*.xaml")):
        text = xaml_file.read_text()

        # Malformed hex colours are a XAML parse error at runtime, not build time.
        for m in re.finditer(r"\"(#[0-9A-Za-z]+)\"", text):
            colour = m.group(1)[1:]
            if len(colour) not in (3, 4, 6, 8) or not re.fullmatch(r"[0-9A-Fa-f]+", colour):
                problems.append(f"{xaml_file.name}: malformed colour '#{colour}'")

        defined_keys = all_keys
        for m in re.finditer(r"\{StaticResource\s+([^}]+)\}", text):
            key = m.group(1).strip()
            # {StaticResource {x:Type Button}} is a style inheriting the default - not a named key.
            if key.startswith("{"):
                continue
            if key not in defined_keys:
                problems.append(f"{xaml_file.name}: undefined StaticResource '{key}'")

        for path, ctx in binding_paths(text):
            checked += 1
            segments = [s.strip() for s in re.split(r"[.\[]", path) if s.strip()]
            if not segments or segments[0].startswith("("):
                continue
            # A RelativeSource/ElementName binding starts from another object (e.g.
            # PlacementTarget.Tag.FooCommand), so only its final segment is ours to verify.
            redirected = "RelativeSource" in ctx or "ElementName" in ctx
            target = segments[-1] if redirected else segments[0]
            if target not in known:
                problems.append(f"{xaml_file.name}: binding '{path}' has no matching property '{target}' ({ctx})")

    print(f"checked {checked} bindings across {len(list(APP.rglob('*.xaml')))} xaml file(s)")
    if problems:
        print("\nPROBLEMS:")
        for p in problems:
            print("  - " + p)
        sys.exit(1)
    print("XAML binding check: PASS")


if __name__ == "__main__":
    main()
