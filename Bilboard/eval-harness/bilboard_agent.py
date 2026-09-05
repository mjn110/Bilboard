# A VisEval Agent that drives the Bilboard dashboard generator over HTTP.
#
#   generate()  ->  POST {base_url}/api/eval/generate   (the Boards.razor agent workflow)
#   execute()   ->  POST {base_url}/api/eval/execute    (BIL deserialization + render)
#
# Everything AI-related happens inside the Blazor Server app; this file only moves data
# and turns Bilboard's chart decision into a VisEval-readable SVG.

from __future__ import annotations

import json
import os
from pathlib import Path
from typing import Optional, Tuple

import pandas as pd
import requests
import urllib3

import _compat  # noqa: F401  - must precede any viseval import; see _compat.py

from viseval.agent import Agent, ChartExecutionResult

from chart_render import ChartRenderError, render_svg

urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)

DEFAULT_BASE_URL = "https://localhost:7162"
API_KEY_HEADER = "X-Eval-Key"


class BilboardAgent(Agent):
    """Calls the Bilboard evaluation endpoints instead of generating code locally."""

    def __init__(self, llm=None, config: dict = None) -> None:
        config = config or {}

        self.base_url = str(
            config.get("base_url") or os.getenv("BILBOARD_EVAL_URL", DEFAULT_BASE_URL)
        ).rstrip("/")
        self.api_key = config.get("api_key") or os.getenv("BILBOARD_EVAL_KEY", "")
        self.model = config.get("model") or os.getenv("BILBOARD_EVAL_MODEL") or None
        self.max_rows = int(config.get("max_rows", 60))
        self.timeout = int(config.get("timeout", 300))
        self.verify = bool(config.get("verify", False))
        self.single_component = bool(config.get("single_component", True))
        self.data_in_prompt = bool(config.get("data_in_prompt", True))
        self.include_trace = bool(config.get("include_trace", True))
        self.save_html = bool(config.get("save_html", True))

        logs = config.get("logs")
        self.logs = Path(str(logs)) if logs else None
        self.failures = 0
        self.consecutive_failures = 0
        self.max_consecutive_failures = int(config.get("max_consecutive_failures", 10))

        self.session = requests.Session()
        self.session.headers.update({"Content-Type": "application/json"})
        if self.api_key:
            self.session.headers.update({API_KEY_HEADER: self.api_key})

    # ------------------------------------------------------------------ helpers
    def health(self) -> dict:
        response = self.session.get(
            f"{self.base_url}/api/eval/health", timeout=30, verify=self.verify
        )
        response.raise_for_status()
        return response.json()

    def selftest(self) -> dict:
        """One real round trip to the chat model, through Bilboard."""
        params = {"model": self.model} if self.model else None
        response = self.session.get(
            f"{self.base_url}/api/eval/selftest",
            params=params,
            timeout=120,
            verify=self.verify,
        )
        response.raise_for_status()
        return response.json()

    def _table_payload(self, csv_path: str) -> dict:
        frame = pd.read_csv(csv_path)
        total = len(frame)
        sample = frame.head(self.max_rows)

        return {
            "name": Path(csv_path).stem,
            "columns": [str(column) for column in sample.columns],
            "rows": [
                ["" if pd.isna(cell) else str(cell) for cell in row]
                for row in sample.itertuples(index=False, name=None)
            ],
            "totalRowCount": int(total),
        }

    # ----------------------------------------------------------------- generate
    def generate(
        self, nl_query: str, tables: list[str], config: dict
    ) -> Tuple[Optional[str], Optional[dict]]:
        try:
            payload = {
                "nlQuery": nl_query,
                "tables": [self._table_payload(table) for table in tables],
                "model": self.model,
                "singleComponent": self.single_component,
                "includeTrace": self.include_trace,
                "dataInPrompt": self.data_in_prompt,
            }

            response = self.session.post(
                f"{self.base_url}/api/eval/generate",
                data=json.dumps(payload),
                timeout=self.timeout,
                verify=self.verify,
            )

            if response.status_code != 200:
                self._record_failure(
                    nl_query,
                    f"HTTP {response.status_code}: {response.text[:400]}",
                    None,
                )
                return None, None

            body = response.json()
            if not body.get("success") or not body.get("dashboardJson"):
                self._record_failure(nl_query, body.get("errorMsg"), body.get("trace"))
                return None, None

            self.consecutive_failures = 0
            context = {
                "tables": tables,
                "nl_query": nl_query,
                "usage": body.get("usage"),
                "elapsed_ms": body.get("elapsedMs"),
                "trace": body.get("trace"),
            }
            return body["dashboardJson"], context

        except Exception as error:  # noqa: BLE001 - VisEval treats any failure as "no code"
            self._record_failure(nl_query, f"{type(error).__name__}: {error}", None)
            return None, None

    def _record_failure(self, nl_query: str, reason, trace) -> None:
        """Log a generation failure with the agent transcript, so it can be diagnosed.

        VisEval discards everything about a failed generation except "code is None", so
        without this the only evidence is a one-line warning.
        """
        self.failures += 1
        self.consecutive_failures += 1
        print(f"    ! generation failed: {reason}", flush=True)

        if (
            self.max_consecutive_failures
            and self.consecutive_failures >= self.max_consecutive_failures
        ):
            raise SystemExit(
                f"\nAborting: {self.consecutive_failures} generations failed in a row.\n"
                f"Last error: {reason}\n\n"
                "Nothing is being measured in this state. Check the model is reachable:\n"
                f"    curl -k -H \"X-Eval-Key: {self.api_key or '<key>'}\" "
                f"{self.base_url}/api/eval/selftest\n\n"
                "Raise --max-consecutive-failures if a long unbroken failure run is expected."
            )

        if not self.logs:
            return

        try:
            self.logs.mkdir(parents=True, exist_ok=True)
            record = {
                "nl_query": nl_query,
                "error": reason,
                "agent_messages": trace or [],
            }
            with (self.logs / "generation-failures.jsonl").open(
                "a", encoding="utf-8"
            ) as handle:
                handle.write(json.dumps(record, ensure_ascii=False) + "\n")
        except OSError:
            pass

    # ------------------------------------------------------------------ execute
    def execute(
        self, code: str, context: dict, log_name: str = None
    ) -> ChartExecutionResult:
        try:
            response = self.session.post(
                f"{self.base_url}/api/eval/execute",
                data=json.dumps({"dashboardJson": code, "includeHtml": self.save_html}),
                timeout=self.timeout,
                verify=self.verify,
            )
        except Exception as error:  # noqa: BLE001
            return ChartExecutionResult(
                status=False, error_msg=f"Could not reach /api/eval/execute: {error}"
            )

        if response.status_code != 200:
            return ChartExecutionResult(
                status=False,
                error_msg=f"/api/eval/execute returned {response.status_code}: {response.text[:400]}",
            )

        body = response.json()
        if not body.get("status"):
            # BIL could not deserialize or render the generated configuration:
            # exactly the failure the chat UI would show.
            return ChartExecutionResult(
                status=False, error_msg=body.get("errorMsg") or "BIL execution failed."
            )

        if log_name and self.save_html and body.get("html"):
            try:
                html_path = Path(str(log_name)).with_suffix(".html")
                html_path.parent.mkdir(parents=True, exist_ok=True)
                html_path.write_text(_wrap_html(body["html"]), encoding="utf-8")
            except OSError:
                pass

        chart_spec = body.get("chartSpec")
        if log_name:
            try:
                spec_path = Path(str(log_name)).with_suffix(".spec.json")
                spec_path.parent.mkdir(parents=True, exist_ok=True)
                spec_path.write_text(
                    json.dumps(
                        {"dashboard": _safe_json(code), "chartSpec": chart_spec},
                        indent=2,
                    ),
                    encoding="utf-8",
                )
            except OSError:
                pass

        try:
            svg_string = render_svg(chart_spec, str(log_name) if log_name else None)
        except ChartRenderError as error:
            return ChartExecutionResult(status=False, error_msg=str(error))
        except Exception as error:  # noqa: BLE001
            return ChartExecutionResult(
                status=False, error_msg=f"SVG rendering failed: {error}"
            )

        return ChartExecutionResult(status=True, svg_string=svg_string)


def _safe_json(text: str):
    try:
        return json.loads(text)
    except (TypeError, ValueError):
        return text


def _wrap_html(fragment: str) -> str:
    """Wrap the BIL fragment so the saved file can be opened / screenshotted directly."""
    return (
        "<!doctype html><html><head><meta charset='utf-8'>"
        "<link rel='stylesheet' "
        "href='https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css'>"
        "<link rel='stylesheet' "
        "href='https://cdn.jsdelivr.net/npm/bootstrap-icons@1.11.3/font/bootstrap-icons.css'>"
        "<style>.pie{--p:20;--b:20px;--c:darkred;--w:110px;width:var(--w);aspect-ratio:1;"
        "position:absolute;cursor:default;display:inline-grid;margin:5px;place-content:center;"
        "font-size:25px;font-weight:bold;font-family:sans-serif}.pie:before,.pie:after{content:'';"
        "position:absolute;border-radius:50%}.pie:before{inset:0;background:radial-gradient("
        "farthest-side,var(--c) 98%,#0000) top/var(--b) var(--b) no-repeat,conic-gradient(var(--c) "
        "calc(var(--p)*1%),#0000 0);-webkit-mask:radial-gradient(farthest-side,#0000 calc(99.5% - "
        "var(--b)),#000 calc(100% - var(--b)));mask:radial-gradient(farthest-side,#0000 calc(99.5% "
        "- var(--b)),#000 calc(100% - var(--b)))}.pie:after{inset:calc(50% - var(--b)/2);"
        "background:var(--c);transform:rotate(calc(var(--p)*3.6deg)) translateY(calc(50% - "
        "var(--w)/2))}</style></head><body class='p-4'><div class='container-fluid'>"
        f"{fragment}</div></body></html>"
    )
