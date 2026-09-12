# CLI runner: evaluates the Bilboard dashboard generator with the VisEval benchmark.
#
#   python run_eval.py --benchmark ./visEval_dataset --limit 25 --queries-per-instance 1
#
# See README.md for the full setup.

from __future__ import annotations

import argparse
import json
import os
import shutil
import time
import zipfile
from collections import defaultdict
from datetime import datetime, timezone
from itertools import islice
from pathlib import Path

import dotenv

from _compat import (  # must precede any viseval import
    RASTERIZER_HELP,
    patches_applied,
    rasterizer_available,
    rasterizer_name,
)

from viseval import Dataset, Evaluator

from bilboard_agent import BilboardAgent

dotenv.load_dotenv()

# The value shipped in Bilboard/appsettings.Development.json. Used when neither
# --api-key nor BILBOARD_EVAL_KEY is set, so a local run works with no .env file.
DEFAULT_DEV_API_KEY = "bilboard-local-eval-key"


def _duration(seconds: float) -> str:
    seconds = int(seconds)
    if seconds < 60:
        return f"{seconds}s"
    if seconds < 3600:
        return f"{seconds // 60}m{seconds % 60:02d}s"
    return f"{seconds // 3600}h{(seconds % 3600) // 60:02d}m"


def _plan(dataset: Dataset, limit: int | None, queries_per_instance: int | None,
          offset: int = 0):
    """How many instances and NL queries this run will actually cover."""
    ids = list(dataset.dict.keys())[offset:]
    if limit:
        ids = ids[:limit]

    queries = 0
    for key in ids:
        count = len(dataset.dict[key]["nl_queries"])
        queries += min(count, queries_per_instance) if queries_per_instance else count

    return len(ids), queries


def _trim(dataset: Dataset, limit: int | None, queries_per_instance: int | None, total: int,
          offset: int = 0):
    """Cap instances and NL queries, and report progress as they stream past."""
    source = dataset.benchmark
    if offset or limit:
        stop = (offset + limit) if limit else None
        source = islice(source, offset, stop)

    def generator():
        started = time.monotonic()
        done = 0
        for instance in source:
            if queries_per_instance:
                instance["nl_queries"] = instance["nl_queries"][:queries_per_instance]
                instance["query_meta"] = instance["query_meta"][:queries_per_instance]

            done += 1
            elapsed = time.monotonic() - started
            suffix = ""
            if done > 1:
                per_instance = elapsed / (done - 1)
                suffix = (
                    f" | elapsed {_duration(elapsed)}"
                    f" | eta {_duration(per_instance * (total - done + 1))}"
                )
            print(f"[{done}/{total}] instance {instance['id']}{suffix}", flush=True)
            yield instance

        print(f"[done] {done} instances in {_duration(time.monotonic() - started)}", flush=True)

    dataset.benchmark = generator()
    return dataset


def _chromedriver(requested: str):
    """Resolve --webdriver. "auto" asks Selenium Manager, then falls back to PATH."""
    if not requested:
        return None
    if requested != "auto":
        return requested

    try:
        from selenium.webdriver.chrome.options import Options
        from selenium.webdriver.common.selenium_manager import SeleniumManager

        manager = SeleniumManager()
        if hasattr(manager, "binary_paths"):
            paths = manager.binary_paths(["--browser", "chrome"])
            found = paths.get("driver_path")
        else:  # Selenium < 4.20
            found = manager.driver_location(Options())
        if found:
            return found
    except Exception as error:  # noqa: BLE001
        print(f"[readability] Selenium Manager could not supply chromedriver: {error}")

    found = shutil.which("chromedriver")
    if found:
        return found

    print(
        "[readability] No chromedriver found, so the layout check will be skipped.\n"
        "              Install Chrome, or pass --webdriver <path to chromedriver>.\n"
        "              The scale/ticks and readability ratings still run."
    )
    return None


def _fix_image_blocks(message):
    """Rewrite {"image_url": "<data uri>"} to {"image_url": {"url": "<data uri>"}}."""
    content = getattr(message, "content", None)
    if not isinstance(content, list):
        return message

    changed = False
    blocks = []
    for block in content:
        if (
            isinstance(block, dict)
            and block.get("type") == "image_url"
            and isinstance(block.get("image_url"), str)
        ):
            block = dict(block, image_url={"url": block["image_url"]})
            changed = True
        blocks.append(block)

    if not changed:
        return message
    try:
        return message.model_copy(update={"content": blocks})
    except Exception:  # noqa: BLE001 - older message classes
        return type(message)(content=blocks)


class VisionModel:
    """Wraps a chat model so VisEval's image blocks reach the API in the shape it wants.

    viseval's readability_check and scale_and_ticks_check build the image block as
    {"type": "image_url", "image_url": "<data uri>"}. LangChain passes content blocks
    through verbatim, and the OpenAI API rejects a bare string there:

        Invalid type for 'messages[1].content[1].image_url':
        expected an object, but got a string instead.

    Both functions swallow that into `warnings.warn(...)` and return (None, ...), so the
    aspect is silently dropped and readability comes back empty with no visible error.
    Normalising here fixes both call sites without patching viseval.
    """

    def __init__(self, inner):
        self._inner = inner

    def __getattr__(self, name):
        return getattr(self._inner, name)

    def invoke(self, messages, *args, **kwargs):
        return self._inner.invoke(
            [_fix_image_blocks(message) for message in messages], *args, **kwargs
        )


def _vision_model(args):
    if args.vision_model == "none":
        return None

    if not rasterizer_available:
        raise SystemExit(
            f"--vision-model {args.vision_model} needs a chart rasterizer.\n\n"
            + RASTERIZER_HELP
        )

    if args.vision_model == "openai":
        from langchain_openai import ChatOpenAI

        return VisionModel(
            ChatOpenAI(
                model=args.vision_model_name,
                temperature=0.0,
                max_retries=5,
                timeout=60,
                max_tokens=4096,
            )
        )

    from langchain_openai import AzureChatOpenAI

    return VisionModel(
        AzureChatOpenAI(
            model_name=args.vision_model_name,
            temperature=0.0,
            max_retries=5,
            request_timeout=60,
            max_tokens=4096,
        )
    )


HERE = Path(__file__).resolve().parent
DATASET_ZIP = "viseval_dataset.zip"

# Harness modules whose content changes what a result looks like.
HARNESS_MODULES = ("chart_render.py", "bilboard_agent.py", "_compat.py", "run_eval.py")


def _harness_version():
    """(mtime, filename) of the most recently changed harness module."""
    newest = 0.0
    newest_name = ""
    for name in HARNESS_MODULES:
        path = HERE / name
        if path.is_file():
            mtime = path.stat().st_mtime
            if mtime > newest:
                newest, newest_name = mtime, name
    return newest, newest_name


def _dataset_root(path: Path):
    """Return `path` if it holds visEval.json, else a child folder that does."""
    if path.is_file():
        return None
    if (path / "visEval.json").is_file():
        return path
    if path.is_dir():
        for child in sorted(path.iterdir()):
            if child.is_dir() and (child / "visEval.json").is_file():
                return child
    return None


def _resolve_benchmark(explicit) -> Path:
    """Find the dataset. --benchmark is optional: the zip ships next to this script."""
    if explicit:
        found = _dataset_root(Path(explicit))
        if found:
            return found
        raise SystemExit(
            f"--benchmark {explicit} does not contain visEval.json.\n"
            "Point it at the unzipped visEval_dataset folder (the one holding\n"
            "visEval.json and databases/)."
        )

    for candidate in (HERE / "visEval_dataset", Path.cwd() / "visEval_dataset", HERE, Path.cwd()):
        found = _dataset_root(candidate)
        if found:
            return found

    # Not unzipped yet - do it, rather than making the user run Expand-Archive.
    archive = HERE / DATASET_ZIP
    if archive.is_file():
        print(f"Extracting {archive.name} (first run only)...")
        with zipfile.ZipFile(archive) as bundle:
            bundle.extractall(HERE)
        found = _dataset_root(HERE / "visEval_dataset") or _dataset_root(HERE)
        if found:
            print(f"Dataset ready at {found}")
            return found

    raise SystemExit(
        "Could not find the VisEval dataset.\n\n"
        f"Expected {HERE / 'visEval_dataset'} (or {DATASET_ZIP} beside this script).\n"
        "Download viseval_dataset.zip from\n"
        "  https://github.com/microsoft/VisEval/blob/main/viseval_dataset.zip\n"
        "unzip it here, or pass --benchmark <path to visEval_dataset>."
    )


def _check_build_is_current(health: dict) -> None:
    """Refuse to run against a Bilboard process older than the C# it is meant to serve.

    A stale process keeps answering on the port, so `dotnet run` silently fails to bind
    and the benchmark measures a build from days ago.
    """
    build_time = health.get("buildTimeUtc")
    source_dir = HERE.parent / "Evaluation"
    if not build_time or not source_dir.is_dir():
        return

    try:
        built = datetime.fromisoformat(str(build_time).replace("Z", "+00:00"))
        if built.tzinfo is None:
            built = built.replace(tzinfo=timezone.utc)
    except ValueError:
        return

    newest_path, newest_mtime = None, 0.0
    for path in list(source_dir.rglob("*.cs")) + list(source_dir.rglob("*.razor")):
        mtime = path.stat().st_mtime
        if mtime > newest_mtime:
            newest_path, newest_mtime = path, mtime

    if newest_path is None:
        return

    newest = datetime.fromtimestamp(newest_mtime, timezone.utc)
    if newest <= built:
        return

    raise SystemExit(
        "The running Bilboard build is OLDER than your evaluation source.\n\n"
        f"    running build : {built:%Y-%m-%d %H:%M:%S} UTC\n"
        f"    newest source : {newest:%Y-%m-%d %H:%M:%S} UTC  ({newest_path.name})\n\n"
        "Your edits are not in the process answering on this port, so the run would\n"
        "measure old code. This usually means an earlier instance is still holding the\n"
        "port and `dotnet run` could not bind. In another window:\n\n"
        "    netstat -ano | findstr :5074\n"
        "    taskkill /PID <pid> /F\n"
        "    cd C:\\Users\\Mohammad\\Documents\\GitHub\\Bilboard\\Bilboard\n"
        "    dotnet build   (check it says Build succeeded)\n"
        "    dotnet run\n"
    )


def _mask(secret) -> str:
    """Show enough of a key to recognise it, never enough to use it.

    This used to print the key verbatim, which puts whatever is in BILBOARD_EVAL_KEY into
    the terminal - including an OpenAI key, if the two got swapped in .env.
    """
    if not secret:
        return "(empty)"
    text = str(secret)
    if len(text) <= 8:
        return f'"{text[0]}...{text[-1]}" ({len(text)} chars)'
    return f'"{text[:4]}...{text[-2:]}" ({len(text)} chars)'


def _health_error(error, args) -> str:
    """Turn a failed health check into something actionable."""
    response = getattr(error, "response", None)
    status = getattr(response, "status_code", None)

    if status == 401:
        used = _mask(args.api_key)
        looks_like_openai = (args.api_key or "").startswith(("sk-", "sk_"))
        hint = ""
        if looks_like_openai:
            hint = (
                "\nThat value looks like an OpenAI key. BILBOARD_EVAL_KEY is the local\n"
                "handshake with your own app, not an OpenAI key - the OpenAI one belongs in\n"
                "OPENAI_API_KEY. Check .env for a swapped pair.\n"
            )
        return (
            f"Bilboard IS running at {args.endpoint}, but it rejected the API key {used}.\n"
            + hint
            + "\nThat key must match Evaluation:ApiKey in Bilboard/appsettings.Development.json:\n"
            '    "Evaluation": { "Enabled": true, "ApiKey": "..." }\n\n'
            "Fix it either way:\n"
            "  * pass it explicitly:   python run_eval.py ... --api-key <value from appsettings>\n"
            "  * or set BILBOARD_EVAL_KEY in eval-harness/.env\n\n"
            "If you edited appsettings.Development.json while the app was running, restart it -\n"
            "the key is read once at startup."
        )

    if status == 404:
        return (
            f"Bilboard is running at {args.endpoint} but has no /api/eval endpoints (404).\n\n"
            "Either Evaluation:Enabled is false, or the app was built before the Evaluation\n"
            "folder was added. Check appsettings.Development.json, then rebuild and restart:\n"
            "    dotnet run"
        )

    return (
        f"Cannot reach Bilboard at {args.endpoint}: {error}\n\n"
        "Start the Blazor app in another shell:\n"
        "    cd Bilboard && dotnet run\n"
        "and confirm the port matches --endpoint (see Properties/launchSettings.json)."
    )


# Readability is a separate dimension: VisEval runs it only on queries that already
# passed validity and legality, and excludes it from the pass rate. It is kept out of the
# pass-rate tally here for the same reason - otherwise a run with --readability would
# report a lower pass rate than the same run without it, and the two would not be
# comparable. "readability check" also carries a 1-5 score rather than a boolean, so it is
# averaged instead of counted.
READABILITY_ASPECTS = ("layout check", "scale and ticks check", "readability check")


def _summarise(details) -> dict:
    """Aspect-level pass rates computed straight from the check results."""
    totals = defaultdict(int)
    passes = defaultdict(int)
    reasons = defaultdict(int)
    scores = []
    queries = 0
    fully_passing = 0

    for detail in details:
        for query_results in detail.results:
            queries += 1
            ok = True
            for check in query_results:
                if check.aspect == "readability check":
                    try:
                        scores.append(float(check.answer))
                    except (TypeError, ValueError):
                        pass
                    continue

                totals[check.aspect] += 1
                if check.answer:
                    passes[check.aspect] += 1
                else:
                    if check.aspect not in READABILITY_ASPECTS:
                        ok = False
                    reasons[f"{check.aspect}: {str(check.rationale)[:120]}"] += 1
            if ok:
                fully_passing += 1

    summary = {
        "queries_evaluated": queries,
        "pass_rate": (fully_passing / queries) if queries else 0.0,
        "aspects": {
            aspect: {
                "checked": totals[aspect],
                "passed": passes[aspect],
                "pass_rate": passes[aspect] / totals[aspect] if totals[aspect] else 0.0,
            }
            for aspect in sorted(totals)
            if aspect not in READABILITY_ASPECTS
        },
        "top_failures": dict(
            sorted(reasons.items(), key=lambda item: item[1], reverse=True)[:15]
        ),
    }

    measured = [aspect for aspect in READABILITY_ASPECTS if totals[aspect] or scores]
    if measured:
        readability = {
            "note": "scored only on queries that passed validity and legality; "
            "excluded from pass_rate",
        }
        for aspect in ("layout check", "scale and ticks check"):
            if totals[aspect]:
                readability[aspect] = {
                    "checked": totals[aspect],
                    "passed": passes[aspect],
                    "pass_rate": passes[aspect] / totals[aspect],
                }
        if scores:
            readability["readability_score"] = {
                "rated": len(scores),
                "mean": sum(scores) / len(scores),
                "scale": "1-5, higher is better",
            }
        summary["readability"] = readability

    return summary


def main():
    parser = argparse.ArgumentParser(
        description="Evaluate the Bilboard dashboard generator with VisEval."
    )

    # dataset
    parser.add_argument(
        "--benchmark",
        type=Path,
        default=None,
        help="Path to the unzipped visEval_dataset folder. Optional: defaults to "
        "visEval_dataset next to this script, extracting the bundled zip if needed.",
    )
    parser.add_argument(
        "--type", type=str, choices=["all", "single", "multiple"], default="all"
    )
    parser.add_argument("--irrelevant-tables", action="store_true")
    parser.add_argument(
        "--limit", type=int, default=0, help="Evaluate only the first N instances (0 = all)."
    )
    parser.add_argument(
        "--offset",
        type=int,
        default=0,
        help="Skip the first N instances. Useful because the dataset is not shuffled — "
        "instances carrying a sort requirement sit at positions 600-1099.",
    )
    parser.add_argument(
        "--queries-per-instance",
        type=int,
        default=0,
        help="Use only the first N of each instance's NL queries (0 = all, usually 4).",
    )

    # Bilboard endpoints
    parser.add_argument(
        "--endpoint",
        type=str,
        default=os.getenv("BILBOARD_EVAL_URL", "https://localhost:7162"),
    )
    parser.add_argument(
        "--api-key",
        type=str,
        default=os.getenv("BILBOARD_EVAL_KEY", DEFAULT_DEV_API_KEY),
        help="Must match Evaluation:ApiKey in Bilboard/appsettings.Development.json.",
    )
    parser.add_argument(
        "--model", type=str, default=None, help="Override the chat model Bilboard uses."
    )
    parser.add_argument("--max-rows", type=int, default=60)
    parser.add_argument("--timeout", type=int, default=300)
    parser.add_argument(
        "--verify-tls",
        action="store_true",
        help="Verify the server certificate (off by default for the ASP.NET dev cert).",
    )
    parser.add_argument(
        "--dashboard-mode",
        action="store_true",
        help="Ask for a full multi-component dashboard instead of one chart per query.",
    )
    parser.add_argument(
        "--retries",
        type=int,
        default=3,
        help="Retries per generation on a transient failure, with exponential backoff "
        "(default 3). Rate limits are the usual cause on a long run.",
    )
    parser.add_argument(
        "--retry-backoff",
        type=float,
        default=20,
        help="Seconds before the first retry; doubles each attempt (default 20).",
    )
    parser.add_argument(
        "--max-consecutive-failures",
        type=int,
        default=10,
        help="Abort after this many generation failures in a row (0 = never). Stops a "
        "run that is measuring nothing.",
    )
    parser.add_argument(
        "--skip-selftest",
        action="store_true",
        help="Do not make a test call to the chat model before starting.",
    )
    parser.add_argument(
        "--no-query-engine",
        action="store_true",
        help="Trust the numbers the model writes instead of computing them from its Query "
        "block. Use this to measure the model's own arithmetic.",
    )
    parser.add_argument(
        "--max-execute-rows",
        type=int,
        default=20000,
        help="Largest table the query engine will aggregate (default 20000).",
    )
    parser.add_argument(
        "--no-data-in-prompt",
        action="store_true",
        help="Send the tables only to the data-analysis agent, as the chat UI does. The "
        "config generator then never sees the rows and invents values - use this to "
        "measure the pipeline exactly as it ships.",
    )
    parser.add_argument(
        "--no-html",
        action="store_true",
        help="Skip the BIL HTML render (faster; no <index>.html artifacts).",
    )

    # evaluator
    parser.add_argument("--library", type=str, default="matplotlib")
    parser.add_argument(
        "--logs",
        type=Path,
        default=None,
        help="Where results are cached and written. Defaults to a 'logs' folder next to "
        "this script, so a run resumes no matter which directory you launch it from.",
    )
    parser.add_argument(
        "--readability",
        action="store_true",
        help="Score the readability dimension too: layout check, scale-and-ticks check "
        "and the vision readability rating. Shorthand for --webdriver auto "
        "--vision-model openai. Adds two vision-model calls per passing query and does "
        "not change the pass rate, so a slice is usually enough.",
    )
    parser.add_argument(
        "--webdriver",
        type=str,
        default="",
        help="Path to chromedriver, or 'auto' to let Selenium Manager fetch one. "
        "Empty skips the layout check.",
    )
    parser.add_argument(
        "--vision-model",
        type=str,
        choices=["none", "openai", "azure"],
        default="none",
        help="Vision model backend for the readability checks.",
    )
    parser.add_argument("--vision-model-name", type=str, default="gpt-4o")

    args = parser.parse_args()

    if args.readability:
        if not args.webdriver:
            args.webdriver = "auto"
        if args.vision_model == "none":
            args.vision_model = "openai"

    for note in patches_applied:
        print(f"compat: {note}")

    # Anchored to the script, not the working directory: VisEval resumes by looking for
    # logs/<id>/result.json, so a cwd-relative default would silently start over
    # whenever you launched from somewhere else.
    args.logs = (args.logs if args.logs else HERE / "logs").resolve()

    benchmark = _resolve_benchmark(args.benchmark)

    agent = BilboardAgent(
        config={
            "base_url": args.endpoint,
            "api_key": args.api_key,
            "model": args.model,
            "max_rows": args.max_rows,
            "timeout": args.timeout,
            "verify": args.verify_tls,
            "single_component": not args.dashboard_mode,
            "data_in_prompt": not args.no_data_in_prompt,
            "use_query_engine": not args.no_query_engine,
            "max_execute_rows": args.max_execute_rows,
            "max_consecutive_failures": args.max_consecutive_failures,
            "retries": args.retries,
            "retry_backoff": args.retry_backoff,
            "save_html": not args.no_html,
            "logs": args.logs,
        }
    )

    try:
        health = agent.health()
        print(f"Bilboard health: {health}")
    except Exception as error:  # noqa: BLE001
        raise SystemExit(_health_error(error, args))

    _check_build_is_current(health)

    # A benchmark against an unreachable model produces 2,500 identical failures and
    # measures nothing. One call settles it up front.
    if not args.skip_selftest:
        try:
            probe = agent.selftest()
        except Exception as error:  # noqa: BLE001
            raise SystemExit(
                f"Model self-test could not be run: {error}\n"
                "If this Bilboard build predates /api/eval/selftest, rebuild it or pass "
                "--skip-selftest."
            )

        if probe.get("success"):
            print(
                f"Model self-test: ok ({probe.get('model')}, "
                f"{probe.get('totalTokenCount')} tokens, {probe.get('elapsedMs')} ms)"
            )
        else:
            raise SystemExit(
                f"Model self-test FAILED for {probe.get('model')}:\n"
                f"    {probe.get('errorMsg')}\n\n"
                "Bilboard is running, but it cannot reach the chat model, so every\n"
                "generation would fail and the benchmark would measure nothing. Usual\n"
                "causes: OPENAI_API_KEY missing from the shell that started the app,\n"
                "an expired key, or exhausted quota / rate limits.\n"
                "Fix that, restart the app, and re-run. Use --skip-selftest to override."
            )

    dataset = Dataset(benchmark, args.type, args.irrelevant_tables)
    instances, queries = _plan(
        dataset, args.limit or None, args.queries_per_instance or None, args.offset
    )

    cached = sum(1 for path in args.logs.glob("*/result.json") if path.is_file())

    print(f"Logs: {args.logs}")
    print(f"Plan: {instances} instances, {queries} NL queries (= {queries} generations).")

    # Stamp the harness code into the run. Python reads these modules once at startup, so
    # editing one mid-run changes nothing about the run in progress - and a cached result
    # from a previous run is never re-scored. Both have silently invalidated measurements
    # before; this makes the code behind any log identifiable.
    changed_at, changed_name = _harness_version()
    if changed_name:
        stamp = datetime.fromtimestamp(changed_at, timezone.utc).strftime("%Y-%m-%d %H:%M UTC")
        print(f"Harness: newest change {changed_name} at {stamp}")
        stale = [
            path
            for path in args.logs.glob("*/result.json")
            if path.is_file() and path.stat().st_mtime < changed_at
        ]
        if stale:
            print(
                f"         WARNING: {len(stale)} of {cached} cached result(s) predate "
                f"{changed_name}.\n"
                "         They were produced by older harness code, will NOT be re-scored,\n"
                "         and will be mixed into this run's numbers. Use a fresh --logs\n"
                "         directory if you are measuring the effect of a code change."
            )

    if cached:
        print(
            f"Resuming: {cached} instance(s) already evaluated in that folder will be "
            "skipped (no API calls).\n"
            "          Delete the folder to re-evaluate them from scratch."
        )
    if not args.limit:
        print(
            f"      At 20-60 s per query that is roughly "
            f"{_duration(queries * 20)} to {_duration(queries * 60)} of wall clock,\n"
            f"      plus {queries} runs of a 4-agent workflow against your OpenAI key.\n"
            "      Ctrl+C is safe - finished instances are cached under the logs folder and\n"
            "      skipped on the next run. For a quick sample use:\n"
            "          python run_eval.py --limit 25 --queries-per-instance 1"
        )

    dataset = _trim(
        dataset, args.limit or None, args.queries_per_instance or None, instances, args.offset
    )

    vision_model = _vision_model(args)
    webdriver_path = _chromedriver(args.webdriver)

    if vision_model or webdriver_path:
        active = []
        if webdriver_path:
            active.append("layout check")
        if vision_model:
            active.append("scale and ticks check")
            active.append("readability rating")
        print(
            "[readability] on: " + ", ".join(active) + "\n"
            f"[readability] rasterizer: {rasterizer_name}"
            + (f"\n[readability] chromedriver: {webdriver_path}" if webdriver_path else "")
            + (
                f"\n[readability] {args.vision_model_name} is called twice per query that "
                "passes validity and legality. Readability does not affect the pass rate."
                if vision_model
                else ""
            )
        )

    evaluator = Evaluator(
        webdriver_path=webdriver_path,
        vision_model=vision_model,
    )

    args.logs.mkdir(parents=True, exist_ok=True)
    config = {"library": args.library, "logs": args.logs}

    result = evaluator.evaluate(agent, dataset, config)

    summary = _summarise(result.details)
    summary["values_from"] = dict(agent.values_from)
    if agent.query_errors:
        from collections import Counter as _C
        summary["top_query_errors"] = dict(_C(agent.query_errors).most_common(8))
    print("\n=== Bilboard / VisEval summary ===")
    print(json.dumps(summary, indent=2))

    # viseval swallows a failed vision call into warnings.warn and returns no score, so
    # an empty readability block looks identical to "everything was fine". Say so plainly.
    if vision_model is not None:
        readability = summary.get("readability") or {}
        if "readability_score" not in readability:
            print(
                "\n[readability] WARNING: a vision model was configured but no readability\n"
                "              scores came back. viseval turns a failed vision call into a\n"
                "              Python warning and returns nothing, so the aspect vanishes\n"
                "              silently. Scroll up for a UserWarning naming the real cause -\n"
                "              usually a missing OPENAI_API_KEY, no access to "
                f"{args.vision_model_name}, or a rejected image payload."
            )

    (args.logs / "summary.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")

    try:
        score = result.score()
        print("\n=== VisEval score() ===")
        print(json.dumps({k: float(v) for k, v in score.items()}, indent=2))
        (args.logs / "score.json").write_text(
            json.dumps({k: float(v) for k, v in score.items()}, indent=2), encoding="utf-8"
        )
        result.detail_records().to_csv(args.logs / "details.csv", index=False)
    except Exception as error:  # noqa: BLE001
        print(f"\nVisEval score() could not be computed: {error}")

    print(f"\nArtifacts written to {args.logs.resolve()}")


if __name__ == "__main__":
    main()
