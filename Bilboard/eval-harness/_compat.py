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

import sys
import types

# --------------------------------------------------------------------------- cairo

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


def _ensure_cairosvg() -> bool:
    """Return True if the real cairosvg is usable; otherwise install a placeholder."""
    try:
        import cairosvg  # noqa: F401

        return True
    except Exception:  # noqa: BLE001 - cairocffi raises OSError, not ImportError
        def _unavailable(*args, **kwargs):
            raise RuntimeError(CAIRO_HELP)

        _install_module(
            "cairosvg", svg2png=_unavailable, svg2pdf=_unavailable, svg2svg=_unavailable
        )
        return False


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


cairo_available = _ensure_cairosvg()
llmx_available = _ensure_llmx()

if not _ensure_langchain():
    raise ImportError(LANGCHAIN_HELP)

patches_applied = _patch_viseval()
