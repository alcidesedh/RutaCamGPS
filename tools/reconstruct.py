"""Reconstruye un proyecto .NET MAUI compilable a partir del código descompilado por ILSpy.

Uso: python tools/reconstruct.py
Entrada : decompiled/GPSCamRoute
Salida  : src/RutaCamGPS (no sobrescribe archivos existentes salvo --force)
"""
import re
import shutil
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "decompiled" / "GPSCamRoute"
DST = ROOT / "src" / "RutaCamGPS"
FORCE = "--force" in sys.argv

SKIP = {
    "Resource.cs", "AssemblyInfo.cs", "GeneratedBindingInterceptors.cs",
    "--z__ReadOnlyArray.cs", "--z__ReadOnlySingleElementList.cs", "-PrivateImplementationDetails-.cs",
}
SKIP_DIRS = {"__XamlGeneratedCode__", "Microsoft.Maui.Controls.Generated", "Properties"}

NS_TO_DIR = {
    "GPSCamRoute": "",
    "GPSCamRoute.Models": "Models",
    "GPSCamRoute.Services": "Services",
    "GPSCamRoute.Platforms.Android": "Platforms/Android",
    "GPSCamRoute.Platforms.Android.Recording": "Platforms/Android/Recording",
}

XAML_MAP = {
    "GPSCamRoute.App.xaml": "App.xaml",
    "GPSCamRoute.HistoryPage.xaml": "HistoryPage.xaml",
    "GPSCamRoute.MainPage.xaml": "MainPage.xaml",
    "GPSCamRoute.OverlaySettingsPage.xaml": "OverlaySettingsPage.xaml",
    "GPSCamRoute.RecordingSummaryPage.xaml": "RecordingSummaryPage.xaml",
    "GPSCamRoute.RouteDetailPage.xaml": "RouteDetailPage.xaml",
    "GPSCamRoute.Resources.Styles.Colors.xaml": "Resources/Styles/Colors.xaml",
    "GPSCamRoute.Resources.Styles.Styles.xaml": "Resources/Styles/Styles.xaml",
}

PEERABLE_BLOCK = re.compile(
    r"\n\t*(int IJavaPeerable\.get_JniIdentityHashCode\(\)|JniObjectReference IJavaPeerable\.get_PeerReference\(\)|void IJavaPeerable\.UnregisterFromRuntime\(\)|void IJavaPeerable\.[A-Za-z_]+\([^)]*\)|[A-Za-z.<>]+ IJavaPeerable\.[A-Za-z_]+\([^)]*\))\s*\n\t*\{.*?\n\t*\}\n",
    re.S,
)


def remove_method(text: str, signature_regex: str) -> str:
    m = re.search(signature_regex, text)
    if not m:
        return text
    # retrocede para incluir atributos de la línea anterior
    start = m.start()
    while True:
        prev = text.rfind("\n", 0, start - 1)
        line = text[prev + 1:start].strip()
        if line.startswith("[") and line.endswith("]"):
            start = prev + 1
        else:
            break
    brace = text.index("{", m.end())
    depth, i = 0, brace
    while i < len(text):
        if text[i] == "{":
            depth += 1
        elif text[i] == "}":
            depth -= 1
            if depth == 0:
                break
        i += 1
    return text[:start] + text[i + 1:]


def clean_cs(text: str, is_xaml_page: bool) -> str:
    text = text.replace("#define DEBUG\r\n", "").replace("#define DEBUG\n", "")
    text = text.replace("\r\n", "\n")
    # base calls mal descompiladas -> recursión infinita si se dejan
    text = re.sub(r"\(\((Activity|Context|Service|Java\.Lang\.Object|ContextWrapper)\)\(object\)this\)\.", "base.", text)
    # implementaciones explícitas de IJavaPeerable generadas por el binding
    prev = None
    while prev != text:
        prev = text
        text = PEERABLE_BLOCK.sub("\n", text)
    if is_xaml_page:
        text = re.sub(r"\n\t*\[XamlFilePath\([^)]*\)\]", "", text)
        text = re.sub(r"\n\t*\[XamlCompilation\([^)]*\)\]", "", text)
        text = re.sub(r"\n\t*\[GeneratedCode\(\"Microsoft\.Maui\.Controls\.SourceGen\"[^\]]*\]\n\t*[^\n]+;\n", "\n", text)
        text = remove_method(text, r"\n\t*(private|public) void InitializeComponent\(\)")
        text = re.sub(r"public class (\w+) : ", r"public partial class \1 : ", text, count=1)
    # líneas en blanco múltiples
    text = re.sub(r"\n{3,}", "\n\n", text)
    return text


def main() -> None:
    xaml_classes = set()
    for xaml_name, rel in XAML_MAP.items():
        content = (SRC / xaml_name).read_text(encoding="utf-8")
        m = re.search(r'x:Class="([^"]+)"', content)
        if m:
            xaml_classes.add(m.group(1).split(".")[-1])
        target = DST / rel
        if FORCE or not target.exists():
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text(content, encoding="utf-8")

    for cs in SRC.rglob("*.cs"):
        rel_dir = cs.parent.relative_to(SRC)
        if cs.name in SKIP or (rel_dir.parts and rel_dir.parts[0] in SKIP_DIRS):
            continue
        ns = rel_dir.parts[0] if rel_dir.parts else ""
        if ns not in NS_TO_DIR:
            print("  [?] namespace sin mapear:", cs)
            continue
        is_page = ns == "GPSCamRoute" and cs.stem in xaml_classes
        out_name = cs.stem + (".xaml.cs" if is_page else ".cs")
        target = DST / NS_TO_DIR[ns] / out_name
        if not FORCE and target.exists():
            continue
        target.parent.mkdir(parents=True, exist_ok=True)
        text = clean_cs(cs.read_text(encoding="utf-8-sig"), is_page)
        target.write_text(text, encoding="utf-8")
    print("Proyecto reconstruido en", DST)


if __name__ == "__main__":
    main()
