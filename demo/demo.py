#!/usr/bin/env python3
"""Scripted walkthrough of LLM-Mock-Service; it is what docs/demo.gif shows.

Start the mock with the extra low-quota deployment the demo uses, then run this script:

    docker run --rm -p 8080:8080 \
      -e LlmMock__Deployments__demo-quota__Profile=gpt-4o-mini \
      -e LlmMock__Deployments__demo-quota__RequestsPerMinute=3 \
      llm-mock-service:local
    python3 demo/demo.py

Uses only the Python standard library. MOCK_URL overrides the address (default http://localhost:8080).
"""
import json
import os
import sys
import time
import urllib.error
import urllib.request

BASE = os.environ.get("MOCK_URL", "http://localhost:8080").rstrip("/")
TYPE_DELAY = float(os.environ.get("DEMO_TYPE_DELAY", "0.02"))
WIDTH = 92

DIM, BOLD, RESET = "\033[2m", "\033[1m", "\033[0m"
GREEN, CYAN, YELLOW, RED, MAGENTA = "\033[32m", "\033[36m", "\033[33m", "\033[31m", "\033[35m"


def out(text="", end="\n"):
    sys.stdout.write(text + end)
    sys.stdout.flush()


def pause(seconds):
    time.sleep(seconds)


def heading(number, title):
    out()
    out(f"{BOLD}{MAGENTA}── {number}. {title} {'─' * (WIDTH - len(title) - 8)}{RESET}")
    pause(0.6)


def typed(command):
    out(f"{GREEN}${RESET} ", end="")
    for ch in command:
        out(ch, end="")
        time.sleep(TYPE_DELAY)
    out()
    pause(0.4)


def call(method, path, body=None):
    """Returns (status, headers, raw response). HTTP errors are results here, not exceptions."""
    data = json.dumps(body).encode() if body is not None else None
    request = urllib.request.Request(BASE + path, data=data, method=method, headers={"content-type": "application/json"})
    try:
        return urllib.request.urlopen(request, timeout=60)
    except urllib.error.HTTPError as error:
        return error


def status_line(response):
    colour = GREEN if response.status < 400 else RED if response.status >= 500 else YELLOW
    return f"{colour}HTTP {response.status}{RESET}"


def error_code(response):
    error = json.loads(response.read())["error"]
    return error.get("code") or error["type"]


def stream_chat(model, prompt, max_tokens):
    started = time.perf_counter()
    response = call("POST", "/v1/chat/completions", {
        "model": model,
        "stream": True,
        "stream_options": {"include_usage": True},
        "max_tokens": max_tokens,
        "messages": [{"role": "user", "content": prompt}],
    })
    ttft, tokens, usage, column = None, 0, None, 2
    out(f"  {DIM}(SSE chunks decoded as they arrive){RESET}")
    out("  ", end="")
    for raw in response:
        line = raw.decode().strip()
        if not line.startswith("data: ") or line == "data: [DONE]":
            continue
        chunk = json.loads(line[6:])
        usage = chunk.get("usage") or usage
        for choice in chunk["choices"]:
            text = choice["delta"].get("content")
            if not text:
                continue
            if ttft is None:
                ttft = time.perf_counter() - started
            tokens += 1
            # Wrap only at word starts, leaving room for sub-word pieces (" orches" + "tration").
            if column + len(text) > WIDTH - 12 and text.startswith(" "):
                out("\n  ", end="")
                text, column = text.lstrip(), 2
            out(f"{CYAN}{text}{RESET}", end="")
            column += len(text)
    total = time.perf_counter() - started
    out()
    out(f"  {BOLD}TTFT {ttft * 1000:.0f} ms{RESET} · {tokens} tokens in {total:.2f} s "
        f"({tokens / (total - ttft):.0f} tok/s) · usage: prompt={usage['prompt_tokens']} completion={usage['completion_tokens']}")


def main():
    out(f"{BOLD}LLM-Mock-Service{RESET}  {DIM}OpenAI / Azure OpenAI-compatible mock for LLM performance testing{RESET}")
    out(f"{DIM}Realistic streaming latency · real quotas and 429s · fault injection · no inference cost{RESET}")
    pause(1.2)
    out()
    typed(f"export MOCK={BASE}")

    heading(1, "Stream a chat completion (OpenAI API, realistic token timing)")
    typed("curl -N $MOCK/v1/chat/completions -d '{\"model\":\"gpt-4o\",\"stream\":true,...}'")
    stream_chat("gpt-4o", "Why load test an AI application?", max_tokens=70)
    pause(2.5)

    heading(2, "Same mock, Azure OpenAI route: per-deployment quota headers")
    typed("curl -i $MOCK/openai/deployments/gpt-4o-eastus/chat/completions -d ...")
    started = time.perf_counter()
    response = call("POST", "/openai/deployments/gpt-4o-eastus/chat/completions", {
        "max_tokens": 20, "messages": [{"role": "user", "content": "Hello"}]})
    body = json.loads(response.read())
    out(f"  {status_line(response)} in {time.perf_counter() - started:.2f} s")
    for header in ("x-ratelimit-limit-requests", "x-ratelimit-remaining-requests", "x-ratelimit-limit-tokens", "x-ratelimit-remaining-tokens"):
        out(f"  {DIM}{header}:{RESET} {response.headers[header]}")
    out(f"  {DIM}usage:{RESET} {body['usage']}  {DIM}finish_reason:{RESET} {body['choices'][0]['finish_reason']}")
    pause(2.5)

    heading(3, "Exhaust a 3 RPM quota: real 429 with Retry-After")
    typed("for i in 1 2 3 4; do curl $MOCK/openai/deployments/demo-quota/chat/completions -d ...; done")
    for i in range(1, 5):
        response = call("POST", "/openai/deployments/demo-quota/chat/completions?api-version=2024-10-21", {
            "max_tokens": 1, "messages": [{"role": "user", "content": "hi"}]})
        detail = ""
        if response.status == 429:
            detail = f"  {DIM}Retry-After:{RESET} {response.headers['Retry-After']} s  {DIM}({error_code(response)}){RESET}"
        else:
            response.read()
        out(f"  request {i}: {status_line(response)}{detail}")
        pause(0.5)
    pause(2.5)

    heading(4, "Inject a regional outage at runtime, then recover")
    typed("curl -X PUT $MOCK/_admin/deployments/gpt-4o-westeurope/faults -d '{\"serviceUnavailableRate\":1}'")
    out(f"  {status_line(call('PUT', '/_admin/deployments/gpt-4o-westeurope/faults', {'serviceUnavailableRate': 1}))}")
    request_westeurope = ("POST", "/openai/deployments/gpt-4o-westeurope/chat/completions?api-version=2024-10-21",
                          {"max_tokens": 5, "messages": [{"role": "user", "content": "hi"}]})
    typed("curl $MOCK/openai/deployments/gpt-4o-westeurope/chat/completions -d ...")
    response = call(*request_westeurope)
    out(f"  {status_line(response)}  {DIM}({error_code(response)}, simulated){RESET}")
    pause(1.2)
    typed("curl -X DELETE $MOCK/_admin/deployments/gpt-4o-westeurope/faults")
    out(f"  {status_line(call('DELETE', '/_admin/deployments/gpt-4o-westeurope/faults'))}")
    typed("curl $MOCK/openai/deployments/gpt-4o-westeurope/chat/completions -d ...")
    response = call(*request_westeurope)
    response.read()
    out(f"  {status_line(response)}  {DIM}traffic flows again{RESET}")
    pause(1.5)

    out()
    out(f"{BOLD}Point your SDK's base URL at the mock. Your app's code stays unchanged.{RESET}")
    pause(3)


if __name__ == "__main__":
    main()
