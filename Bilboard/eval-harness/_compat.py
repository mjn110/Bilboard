# Import-time compatibility shims for the VisEval toolkit.
#
# IMPORT THIS BEFORE ANYTHING THAT IMPORTS `viseval`.
#
# vis-evaluator 0.0.3 was published against LangChain 0.1 and CairoSVG, but its wheel
# declares `langchain` with no upper bound and CairoSVG needs a native library that
# Windows does not ship. Both problems break `import viseval` before any evaluation
# code runs, for runs that never use either. This module patches over exactly that,
# and only when the real thing is missing:
#
#   1. cairosvg               -> placeholder that raises only if it is actually called
#   2. langchain.schema       -> langchain_core.messages          (LangChain >= 0.2)
#   3. langchain.chat_models  -> langchain_core.language_models   (LangChain >= 0.2)
#   4. llmx.TextGenerator     -> placeholder (used only in a type annotation)
#
# Nothing here changes what VisEval measures.

import importlib
import sys
import tempfile
import types
from pathlib import Path

# ---------------------------------------------------------------------- rasterizer

CAIRO_HELP = """The readability checks need the native Cairo library, which Python's
cairosvg package does not bundle on Windows. Either:

  * run without readability scoring (--vision-model none, the default), or
  * install the native library and re-run:
      - Windows: install the "GTK3 Runtime for Windows" installer (it puts
        libcairo-2.dll on PATH), then restart your shell. Conda users can instead run
        `conda install -c conda-forge cairo`.
      - macOS:   brew install cairo
      - Linux:   apt install libcairo2
"""

RASTERIZER_HELP = """Could not rasterize a chart for the readability checks.

Normally this never happens: every chart is drawn by chart_render.py, which keeps a PNG
of each figure so no SVG rasterizer is needed. Seeing this means VisEval asked for the
PNG of an SVG that chart_render did not produce. Install a real rasterizer if you need
to score foreign SVGs:

    pip install svglib reportlab      (pure Python, no native libraries)

or install native Cairo as described above.
"""


def _install_module(dotted_name: str, **attributes) -> types.ModuleType:
    """Register a module (creating any missing parent packages) and set attributes."""
    parts = dotted_name.split(".")

    for index in range(1, len(parts) + 1):
        name = ".".join(parts[:index])
        if name not in sys.modules:
            module = types.ModuleType(name)
            module.__bilboard_stub__ = True
            if index < len(parts):
                module.__path__ = []  # mark as a package so submodules can be imported
            sys.modules[name] = module
            if index > 1:
                setattr(sys.modules[".".join(parts[: index - 1])], parts[index - 1], module)

    module = sys.modules[dotted_name]
    for key, value in attributes.items():
        setattr(module, key, value)

    # Make `from parent import child` work when the parent was already imported.
    if len(parts) > 1:
        setattr(sys.modules[".".join(parts[:-1])], parts[-1], module)

    return module


def _rasterize_with_svglib(svg):
    """Last-resort pure-Python SVG -> PNG, used only for SVGs we did not draw."""
    try:
        from io import BytesIO

        from reportlab.graphics import renderPM
        from svglib.svglib import svg2rlg
    except Exception:  # noqa: BLE001
        return None

    try:
        if isinstance(svg, str):
            svg = svg.encode("utf-8")
        drawing = svg2rlg(BytesIO(svg))
        if drawing is None:
            return None
        out = BytesIO()
        renderPM.drawToFile(drawing, out, fmt="PNG")
        return out.getvalue()
    except Exception:  # noqa: BLE001
        return None


def _ensure_rasterizer():
    """Make `cairosvg.svg2png` work, one way or another.

    Preference order:
      1. the real cairosvg, if the native library is present;
      2. the PNG that chart_render.py already saved for this exact SVG - pixel-identical
         to the figure, and the reason readability can be scored on Windows at all;
      3. svglib + reportlab, if installed, for SVGs we did not draw.

    Returns (available, name).
    """
    try:
        import cairosvg  # noqa: F401

        return True, "cairosvg (native Cairo)"
    except Exception:  # noqa: BLE001 - cairocffi raises OSError, not ImportError
        pass

    def _svg2png(bytestring=None, url=None, write_to=None, **kwargs):
        if bytestring is None:
            raise RuntimeError(RASTERIZER_HELP)

        from chart_render import lookup_png  # local import: avoids an import cycle

        png = lookup_png(bytestring)
        if png is None:
            png = _rasterize_with_svglib(bytestring)
        if png is None:
            raise RuntimeError(RASTERIZER_HELP)

        if write_to is not None:
            if hasattr(write_to, "write"):
                write_to.write(png)
            else:
                Path(write_to).write_bytes(png)
        return png

    def _unavailable(*args, **kwargs):
        raise RuntimeError(CAIRO_HELP)

    _install_module(
        "cairosvg", svg2png=_svg2png, svg2pdf=_unavailable, svg2svg=_unavailable
    )
    return True, "matplotlib (chart_render PNG cache)"


# ----------------------------------------------------------------------- langchain

LANGCHAIN_HELP = """viseval imports `langchain.schema` and `langchain.chat_models.base`,
which LangChain removed in 0.2. Those names could not be mapped onto the installed
LangChain either, so install a compatible one:

    pip install "langchain==0.1.20"
"""


def _ensure_langchain() -> bool:
    """Map viseval's LangChain 0.1 import paths onto whatever LangChain is installed."""
    patched = False

    # from langchain.schema import HumanMessage, SystemMessage
    try:
        from langchain.schema import HumanMessage, SystemMessage  # noqa: F401
    except Exception:  # noqa: BLE001
        try:
            from langchain_core.messages import HumanMessage, SystemMessage
        except Exception:  # noqa: BLE001
            return False
        _install_module(
            "langchain.schema", HumanMessage=HumanMessage, SystemMessage=SystemMessage
        )
        patched = True

    # from langchain.chat_models.base import BaseChatModel
    try:
        from langchain.chat_models.base import BaseChatModel  # noqa: F401
    except Exception:  # noqa: BLE001
        try:
            from langchain_core.language_models.chat_models import BaseChatModel
        except Exception:  # noqa: BLE001
            return False
        _install_module("langchain.chat_models.base", BaseChatModel=BaseChatModel)
        _install_module("langchain.chat_models", BaseChatModel=BaseChatModel)
        patched = True

    return True if not patched else True


def _ensure_llmx() -> bool:
    """viseval only uses llmx.TextGenerator inside a type annotation."""
    try:
        from llmx import TextGenerator  # noqa: F401

        return True
    except Exception:  # noqa: BLE001
        class TextGenerator:  # minimal stand-in for the annotation
            """Placeholder for llmx.TextGenerator (not installed)."""

        _install_module("llmx", TextGenerator=TextGenerator)
        return False


# ------------------------------------------------------------------ viseval itself


def _patch_viseval() -> list:
    """Fix bugs in the released vis-evaluator wheel.

    vis-evaluator 0.0.3 (the only version on PyPI) calls `surface_form_check(...)` in
    evaluate.py without importing it, so every evaluation dies on the first query with
    `NameError: name 'surface_form_check' is not defined`. It is imported correctly on
    the GitHub main branch. Patching it here means either install works.
    """
    notes = []

    try:
        import viseval.evaluate as evaluate_module
        from viseval.check import surface_form_check
    except Exception:  # noqa: BLE001 - let the caller's own import raise properly
        return notes

    if not hasattr(evaluate_module, "surface_form_check"):
        evaluate_module.surface_form_check = surface_form_check
        notes.append(
            "patched viseval.evaluate.surface_form_check "
            "(missing import in vis-evaluator 0.0.3)"
        )

    return notes


def _patch_layout_check() -> list:
    """Make VisEval's layout check survive Windows and a long run.

    viseval.check.layout_check has three problems that only show up at scale:

      1. it builds the page URL as f"file://{os.getcwd()}/temp_x.svg", which on Windows
         produces "file://C:\\Users\\..." - Chrome cannot open that, so every layout
         check silently returns None and the aspect is skipped;
      2. it writes the temp SVG into the current working directory, and leaves it behind
         whenever removal fails;
      3. it calls driver.close() only on the success path and never driver.quit(), so a
         Chrome and a chromedriver process leak per query. Over a full benchmark run that
         is thousands of orphaned processes.

    The JavaScript is reused verbatim from the module, so this changes how the check is
    driven, never what it measures.
    """
    notes = []

    module = sys.modules.get("viseval.check.layout_check")
    if module is None:
        try:
            module = importlib.import_module("viseval.check.layout_check")
        except Exception:  # noqa: BLE001
            return notes

    def layout_check(context: dict, webdriver_path):
        if webdriver_path is None:
            return None, "No webdriver path provided."

        from selenium import webdriver as selenium_webdriver
        from selenium.webdriver.chrome.service import Service

        options = selenium_webdriver.ChromeOptions()
        options.add_argument("--headless=new")
        options.add_argument("--disable-gpu")
        options.add_argument("--allow-file-access-from-files")

        driver = None
        temp_path = None
        try:
            with tempfile.NamedTemporaryFile(
                "w", suffix=".svg", delete=False, encoding="utf-8"
            ) as handle:
                handle.write(context["svg_string"])
                temp_path = Path(handle.name)

            driver = selenium_webdriver.Chrome(
                service=Service(webdriver_path), options=options
            )
            driver.get(temp_path.as_uri())
            no_overflow = not driver.execute_script(module.overflowScript)
            no_overlap = not driver.execute_script(module.overlapScript)

            if no_overflow and no_overlap:
                message = "No overflow or overlap detected."
            elif not no_overflow and not no_overlap:
                message = "Overflow and overlap detected."
            elif not no_overflow:
                message = "Overflow detected."
            else:
                message = "Overlap detected."

            return no_overflow and no_overlap, message
        except Exception as error:  # noqa: BLE001 - matches upstream: skip, never crash
            print(f"Layout check skipped: {error}")
            return None, "Layout check could not run."
        finally:
            if driver is not None:
                try:
                    driver.quit()
                except Exception:  # noqa: BLE001
                    pass
            if temp_path is not None:
                try:
                    temp_path.unlink(missing_ok=True)
                except Exception:  # noqa: BLE001
                    pass

    module.layout_check = layout_check

    # evaluate.py did `from .check import layout_check`, so it holds its own reference.
    for name in ("viseval.check", "viseval.evaluate"):
        target = sys.modules.get(name)
        if target is not None and hasattr(target, "layout_check"):
            target.layout_check = layout_check

    notes.append("patched viseval layout_check (Windows file URL, temp file, browser leak)")
    return notes


rasterizer_available, rasterizer_name = _ensure_rasterizer()
cairo_available = rasterizer_name.startswith("cairosvg")
llmx_available = _ensure_llmx()

if not _ensure_langchain():
    raise ImportError(LANGCHAIN_HELP)

patches_applied = _patch_viseval() + _patch_layout_check()
