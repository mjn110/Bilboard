# CLI runner: evaluates the Bilboard dashboard generator with the VisEval benchmark.
#
#   python run_eval.py --benchmark ./visEval_dataset --limit 25 --queries-per-instance 1
#
# See README.md for the full setup.

from __future__ import annotations

import argparse
import json
import os
import time
import zipfile
from collections import defaultdict
from itertools import islice
from pathlib import Path

import dotenv

from _compat import (  # must precede any viseval import
    CAIRO_HELP,
    cairo_available,
    patches_applied,
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


def _plan(dataset: Dataset, limit: int | None, queries_per_instance: int | None):
    """How many instances and NL queries this run will actually cover."""
    ids = list(dataset.dict.keys())
    if limit:
        ids = ids[:limit]

    queries = 0
    for key in ids:
        count = len(dataset.dict[key]["nl_queries"])
        queries += min(count, queries_per_instance) if queries_per_instance else count

    return len(ids), queries


def _trim(dataset: Dataset, limit: int | None, queries_per_instance: int | None, total: int):
    """Cap instances and NL queries, and report progress as they stream past."""
    source = dataset.benchmark
    if limit:
        source = islice(source, limit)

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


def _vision_model(args):
    if args.vision_model == "none":
        return None

    if not cairo_available:
        raise SystemExit(
            f"--vision-model {args.vision_model} needs cairosvg to rasterize the charts.\n\n"
            + CAIRO_HELP
        )

    if args.vision_model == "openai":
        from langchain_openai import ChatOpenAI

        return ChatOpenAI(
            model=args.vision_model_name,
            temperature=0.0,
            max_retries=5,
            timeout=60,
            max_tokens=4096,
        )

    from langchain_openai import AzureChatOpenAI

    return AzureChatOpenAI(
        model_name=args.vision_model_name,
        temperature=0.0,
        max_retries=5,
        request_timeout=60,
        max_tokens=4096,
    )


HERE = Path(__file__).resolve().parent
DATASET_ZIP = "viseval_dataset.zip"


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


def _health_error(error, args) -> str:
    """Turn a failed health check into something actionable."""
    response = getattr(error, "response", None)
    status = getattr(response, "status_code", None)

    if status == 401:
        used = f'"{args.api_key}"' if args.api_key else "(empty)"
        return (
            f"Bilboard IS running at {args.endpoint}, but it rejected the API key {used}.\n\n"
            "That key must match Evaluation:ApiKey in Bilboard/appsettings.Development.json:\n"
            '    "Evaluation": { "Enabled": true, "ApiKey": "..." }\n\n'
            "Fix it either way:\n"
            "  * pass it explicitly:   python run_eval.py ... --api-key <value from appsettings>\n"
            "  * or put it in .env:    copy .env.example .env   (then edit BILBOARD_EVAL_KEY)\n\n"
            "If you edited appsettings.Development.json while the app was running, restart it —\n"
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


def _summarise(details) -> dict:
    """Aspect-level pass rates computed straight from the check results."""
    totals = defaultdict(int)
    passes = defaultdict(int)
    reasons = defaultdict(int)
    queries = 0
    fully_passing = 0

    for detail in details:
        for query_results in detail.results:
            queries += 1
            ok = True
            for check in query_results:
                totals[check.aspect] += 1
                if check.answer:
                    passes[check.aspect] += 1
                else:
                    ok = False
                    reasons[f"{check.aspect}: {str(check.rationale)[:120]}"] += 1
            if ok:
                fully_passing += 1

    return {
        "queries_evaluated": queries,
        "pass_rate": (fully_passing / queries) if queries else 0.0,
        "aspects": {
            aspect: {
                "checked": totals[aspect],
                "passed": passes[aspect],
                "pass_rate": passes[aspect] / totals[aspect] if totals[aspect] else 0.0,
            }
            for aspect in sorted(totals)
        },
        "top_failures": dict(
            sorted(reasons.items(), key=lambda item: item[1], reverse=True)[:15]
        ),
    }


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
        "--webdriver",
        type=str,
        default="",
        help="Path to chromedriver; empty skips the layout check.",
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
            "max_consecutive_failures": args.max_consecutive_failures,
            "save_html": not args.no_html,
            "logs": args.logs,
        }
    )

    try:
        print(f"Bilboard health: {agent.health()}")
    except Exception as error:  # noqa: BLE001
        raise SystemExit(_health_error(error, args))

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
    instances, queries = _plan(dataset, args.limit or None, args.queries_per_instance or None)

    cached = sum(1 for path in args.logs.glob("*/result.json") if path.is_file())

    print(f"Logs: {args.logs}")
    print(f"Plan: {instances} instances, {queries} NL queries (= {queries} generations).")
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

    dataset = _trim(dataset, args.limit or None, args.queries_per_instance or None, instances)

    evaluator = Evaluator(
        webdriver_path=args.webdriver or None,
        vision_model=_vision_model(args),
    )

    args.logs.mkdir(parents=True, exist_ok=True)
    config = {"library": args.library, "logs": args.logs}

    result = evaluator.evaluate(agent, dataset, config)

    summary = _summarise(result.details)
    print("\n=== Bilboard / VisEval summary ===")
    print(json.dumps(summary, indent=2))

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
