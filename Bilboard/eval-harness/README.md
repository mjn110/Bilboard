# Evaluating Bilboard with VisEval

[VisEval](https://github.com/microsoft/VisEval) is a NL2VIS benchmark: 1,150 instances,
each with ~4 natural-language queries, the relevant database tables as CSV, and a
ground-truth chart. It scores a generator on three dimensions — did it produce a
visualization at all (**validity**), does that visualization answer the query
(**legality**: chart type, data, order), and is it readable (**readability**).

VisEval expects an `Agent` with two methods. Bilboard supplies both over HTTP:

| VisEval | Bilboard |
| --- | --- |
| `Agent.generate(nl_query, tables, config)` | `POST /api/eval/generate` — the same four-agent workflow the Boards chat page runs |
| `Agent.execute(code, context, log_name)` | `POST /api/eval/execute` — deserialize the config into BIL components and render them |

"Code" in VisEval's vocabulary is Bilboard's **BIL dashboard configuration JSON**.

## How the pieces fit

```
run_eval.py
  └─ viseval.Evaluator
       └─ BilboardAgent  (bilboard_agent.py)
            ├─ generate ──HTTP──▶  Blazor  /api/eval/generate  ──▶ OpenAI agent workflow
            │                                                      (DashboardGenerator.cs)
            ├─ execute  ──HTTP──▶  Blazor  /api/eval/execute    ──▶ BIL deserialize + render
            │                                                      (BilExecutor.cs)
            └─ chart_render.py  ──▶ matplotlib SVG ──▶ VisEval checks
```

### Why matplotlib is in the loop

VisEval's legality checker parses **matplotlib-flavoured SVG** — it looks for
`<g id="figure_1">`, `<g id="axes_1">`, tick groups and text comments. Bilboard renders
Bootstrap HTML (CSS `conic-gradient` pies, cards, progress bars), which that parser
cannot read at all, so every instance would fail deconstruction and every score would
be zero regardless of how good the generator is.

So the responsibilities are split:

* **Bilboard decides** the chart type and the data — that is what is being evaluated.
* **`chart_render.py` redraws that decision** with matplotlib so VisEval's checks have
  something they can measure.

The real BIL markup is still produced by `/api/eval/execute` on every query and saved
next to each SVG as `<index>.html`, so you can open or screenshot the actual dashboard.
A configuration that BIL cannot deserialize or render fails `code execution`, exactly as
it would in the chat UI.

**Ceiling check.** Feeding VisEval's own ground truth through this harness scores
**98.7 %** over 300 instances (chart type 100 %, deconstruction 100 %, surface-form
100 %, order 100 %, data 98.7 %). The residual ~1 % is VisEval recovering long or
duplicated tick labels from SVG text, which affects every agent equally. Any score
below that ceiling is attributable to Bilboard, not to the plumbing.

## Setup

### 1. Python

```bash
cd Bilboard/eval-harness
python -m venv .venv
.venv\Scripts\activate          # Windows
pip install -r requirements.txt
```

`.env` is optional: the runner defaults to the endpoint and API key that
`appsettings.Development.json` ships with, so a stock local run needs no configuration.
Copy `.env.example` to `.env` only if you changed either (or want readability scoring).

> `requirements.txt` installs VisEval **from GitHub, not PyPI** — the released
> `vis-evaluator` 0.0.3 crashes on the first query (`NameError: surface_form_check`).
> That needs `git` on PATH; without it, download the repo ZIP and run `pip install .`
> inside. Either way `_compat.py` papers over the released build's bugs at runtime, so
> an existing `pip install vis-evaluator` also works. See Troubleshooting.

### 2. Dataset

Nothing to do — `viseval_dataset.zip` is already in this folder (git-ignored), and the
first run extracts it to `eval-harness\visEval_dataset\` automatically.

`--benchmark` is therefore optional. Pass it only to point at a dataset kept somewhere
else. The original download lives in the
[VisEval repo](https://github.com/microsoft/VisEval/blob/main/viseval_dataset.zip) if you
ever need it again.

### 3. Bilboard

```bash
setx OPENAI_API_KEY "sk-..."     # new shell afterwards
cd Bilboard
dotnet run
```

`appsettings.Development.json` already enables the endpoints:

```json
"Evaluation": { "Enabled": true, "ApiKey": "bilboard-local-eval-key" }
```

Smoke test:

```bash
curl -k -H "X-Eval-Key: bilboard-local-eval-key" https://localhost:7162/api/eval/health
```

## Running

Start small — 25 instances × 1 query is ~25 generations:

```bash
python run_eval.py --limit 25 --queries-per-instance 1
```

Full benchmark — **1,150 instances / 2,524 queries, roughly 14–42 hours** of wall clock
and 2,524 runs of the four-agent workflow against your OpenAI key:

```bash
python run_eval.py --logs .\logs-full
```

The runner prints the plan and this estimate before it starts. Ctrl+C is safe: every
finished instance is cached as `logs/<id>/result.json` and skipped when you re-run, so a
long benchmark can be done in sittings. On a resumed run you get:

```
Logs: C:\...\eval-harness\logs
Resuming: 412 instance(s) already evaluated in that folder will be skipped (no API calls).
```

The log folder defaults to `logs` **next to `run_eval.py`**, not to your current
directory, so resuming works whether you launch from a terminal, an IDE, or an absolute
path. Pass `--logs` to keep separate runs apart — a different folder means a fresh start.

With readability scoring (needs Chrome/chromedriver, a vision model, **and** native Cairo
— see Troubleshooting):

```bash
python run_eval.py --limit 50 ^
  --webdriver C:\tools\chromedriver.exe --vision-model openai --vision-model-name gpt-4o
```

### Useful flags

| Flag | Meaning |
| --- | --- |
| `--benchmark PATH` | dataset location; optional, defaults to the bundled one |
| `--limit N` | first N instances only (0 = all) |
| `--queries-per-instance N` | first N of each instance's ~4 NL queries |
| `--type single\|multiple\|all` | single-table, multi-table, or both |
| `--irrelevant-tables` | also pass distractor tables (harder) |
| `--model` | override the chat model Bilboard uses |
| `--max-rows` | rows per table sent to the generator (default 60) |
| `--dashboard-mode` | ask for a full multi-component dashboard instead of one chart |
| `--no-data-in-prompt` | send rows only to the data-analysis agent, as the chat UI does |
| `--endpoint`, `--api-key` | point at a different Bilboard instance |
| `--verify-tls` | verify the certificate (off by default for the dev cert) |
| `--logs PATH` | results folder; defaults to `logs` beside this script |

Runs are **resumable**: VisEval writes `logs/<id>/result.json` per instance and skips
instances that already have one, with no API call. Delete the folder to re-run.

## Output

```
logs/
  summary.json               aspect-level pass rates + the most common failures
  score.json                 VisEval's own score() output
  details.csv                per-instance records
  generation-failures.jsonl  every failed generation, with the full agent transcript
  evaluation.log
  <instance-id>/
    0.svg               what VisEval scored
    0.html              the real BIL dashboard render
    0.spec.json         generated BIL config + the extracted chart spec
    result.json         per-check verdicts
```

`summary.json` is computed directly from the check results, because VisEval's own
`score()` raises `KeyError: 'readability_score'` when no query passes legality — which
happens on a broken run, exactly when you most want the numbers.

## Reading the results

| Aspect | What a failure means |
| --- | --- |
| `code execution` | the generated config would not render in the chat UI either — bad JSON, wrong enum, missing fields |
| `surface-form check` | the chart is empty or degenerate |
| `deconstruction` | the chart could not be parsed (rare; usually an unsupported shape) |
| `chart type check` | the generator picked the wrong chart type for the question |
| `data check` | wrong values — the generator invented data instead of aggregating the supplied tables, or aggregated incorrectly |
| `order check` | right data, wrong sort |
| `layout check` / `scale and ticks check` / `readability check` | readability, only run when a webdriver / vision model is configured |

`data check` is the one to watch first: Bilboard's production prompt tells the model to
"generate realistic sample data", which is right for a demo dashboard and wrong for a
benchmark. Evaluation mode (default) overrides that instruction — see
`EvaluationAddendum` in `Evaluation/DashboardPrompts.cs`.

### Where the data actually reaches the generator

In the chat pipeline the uploaded file goes into the **data-analysis agent's system
instructions** only. The config generator that writes the JSON runs next in the sequence
and never sees a row — it works from the analyst's prose summary. Measured over 25
instances that produced chart type 100% / data check 14%: the right chart, invented
numbers.

Evaluation mode therefore also appends the rows to the user prompt (`dataInPrompt`,
default true). Use `--no-data-in-prompt` to measure the shipping behaviour instead. The
gap between the two runs is the cost of that pipeline shape, and it is worth quoting.

## Troubleshooting

**`OSError: no library called "cairo-2" was found` / `cannot load library 'libcairo-2.dll'`**

`viseval/evaluate.py` imports `cairosvg` at module level, so `import viseval` fails on
Windows even for runs that never use it. `cairosvg` binds to the native Cairo library,
which is not bundled on Windows.

`_compat.py` handles this: it registers a placeholder `cairosvg` before VisEval is
imported, so runs without readability scoring work with no native library at all. It is
imported first by both `run_eval.py` and `bilboard_agent.py` — keep those imports at the
top if you refactor.

Cairo is only genuinely needed for `--vision-model openai|azure`, which rasterizes each
chart to PNG for the vision model. If you ask for that on a machine without it, the run
stops immediately with instructions rather than failing mid-benchmark. To install it:

* **Windows** — the *GTK3 Runtime for Windows* installer puts `libcairo-2.dll` on PATH;
  restart your shell afterwards. With conda: `conda install -c conda-forge cairo`.
* **macOS** — `brew install cairo`
* **Linux** — `apt install libcairo2`

**`ModuleNotFoundError: No module named 'langchain.schema'`**

`vis-evaluator` 0.0.3 was written against LangChain 0.1 but its PyPI wheel declares
`langchain` with no upper bound, so pip installs LangChain 1.x, where `langchain.schema`
and `langchain.chat_models.base` no longer exist. VisEval needs exactly three symbols
from those paths, so `_compat.py` maps them onto `langchain_core` and the harness runs
on either LangChain generation. Nothing to install.

**`NameError: name 'surface_form_check' is not defined`**

A real bug in vis-evaluator 0.0.3 — the only version on PyPI. `evaluate.py` calls
`surface_form_check()` without importing it, so every run dies on the first query. It is
fixed on the GitHub main branch, which is why `requirements.txt` installs from git.
`_compat.py` also patches it at runtime and prints `compat: patched ...` when it does,
so an existing PyPI install keeps working.

**`! generation failed: The generator did not return a JSON array.`**

`/api/eval/generate` ran the workflow but the config-generator agent replied with prose
instead of a JSON array — it refused, asked a question, or wrapped the array in something
`JsonPayload.ExtractJsonArray` could not find. Every occurrence is appended to
`logs/generation-failures.jsonl` with the full agent transcript; read the
`Dashboard config generator agent` message there to see what it actually said. These
count as `generation` failures and drag `pass_rate` down, which is correct — the chat UI
would render nothing in the same situation.

Common causes: the table sample is too large or too confusing for `gpt-4o-mini` (lower
`--max-rows`, or `--model gpt-4o`), or the query needs a join the prompt does not explain.

**The run seems to go on forever**

Without `--limit` this is 2,524 generations (14–42 hours). The runner now prints a plan
and `[n/total]` progress with an ETA before each instance. Ctrl+C, then re-run with
`--limit 25 --queries-per-instance 1`; completed instances are cached and skipped.

**`Bilboard IS running ... but it rejected the API key`** — a 401 means the app answered,
so only the key is wrong. It must match `Evaluation:ApiKey` in
`Bilboard/appsettings.Development.json`. Pass `--api-key <value>`, or set
`BILBOARD_EVAL_KEY` in `.env`. The key is read at startup, so restart the app if you
edited appsettings while it was running.

**`... has no /api/eval endpoints (404)`** — `Evaluation:Enabled` is false, or the app is
running a build from before the `Evaluation` folder existed. Rebuild and restart.

**`Cannot reach Bilboard at ...`** — the app is not running, or `--endpoint` does not
match the port in `Properties/launchSettings.json` (`https://localhost:7162`).

**`KeyError: 'readability_score'` from `score()`** — VisEval's own bug when no query
passes legality. Read `logs/summary.json` instead; it is always written.

## Files

| File | Purpose |
| --- | --- |
| `_compat.py` | makes VisEval's module-level `cairosvg` import optional; import it first |
| `bilboard_agent.py` | the `viseval.agent.Agent` implementation; talks to the two endpoints |
| `chart_render.py` | ChartSpec → matplotlib SVG that VisEval can deconstruct |
| `run_eval.py` | CLI: dataset config, evaluator config, scoring, artifacts |
