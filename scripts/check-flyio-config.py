#!/usr/bin/env python3
"""check-flyio-config.py — the Fly configuration, checked without a Fly account (WP33, ADR-0029).

What it checks, for flyio/*.fly.toml, .github/workflows/flyio.yml and flyio/SECRETS.md:

  * every file parses (TOML with tomllib; the workflow's shape is checked by scripts/check-flyio-trigger.sh);
  * the six apps exist, each with a unique, lower-case `app` name and a region;
  * every app has a build source: a Dockerfile that exists (with a declared context) or a pinned image;
  * every app that serves HTTP has a health check path and an internal port of 8080 that the Dockerfile sets;
  * PostgreSQL has no HTTP service and no public listener, and PGDATA is a subdirectory of its mount;
  * the synchronous apps keep one machine running and the execution service scales to zero;
  * no [env] value looks like a secret (a password, a key, a connection string with a password);
  * every `http://<app>.flycast` reference names an app of the estate, and the Web's service-discovery names are present;
  * every secret name the workflow checks is documented in flyio/SECRETS.md;
  * the workflow deploys every app file except PostgreSQL, and names each app as its toml does.

`--self-test` also proves the checks bite: it breaks copies of the files in a temporary directory and requires an error for each.

Usage:  python3 scripts/check-flyio-config.py [--self-test]
Exit status: 0 when everything holds, 1 with one line per problem otherwise.
"""

from __future__ import annotations

import re
import shutil
import sys
import tempfile
import tomllib
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent

# The estate. The key is the file stem; `sync` apps stay on the request path and keep one machine running (FLY_DEPLOYMENT §8).
APPS: dict[str, dict] = {
    "web": {"file": "web.fly.toml", "health": "/health", "sync": True, "public": True},
    "authservice": {"file": "authservice.fly.toml", "health": "/health/ready", "sync": True, "public": False},
    "environments-service": {"file": "environments-service.fly.toml", "health": "/health", "sync": True, "public": False},
    "collections-service": {"file": "collections-service.fly.toml", "health": "/health", "sync": True, "public": False},
    "execution-service": {"file": "execution-service.fly.toml", "health": "/health", "sync": False, "public": False},
}
POSTGRES_FILE = "postgres.fly.toml"
AUTH_IMAGE = "ghcr.io/konradcinkusz/authservice:v0.3.4"
DISCOVERY_NAMES = ["authservice", "collections-service", "environments-service", "execution-service"]
SECRET_LIKE = re.compile(r"(?i)password|secret|token|apikey|privatekey|connectionstring")
FLYCAST = re.compile(r"http://([a-z0-9-]+)\.flycast")
DISCOVERY_KEY = re.compile(r"^services__([a-z0-9-]+)__http__\d+$")


def load(path: Path) -> dict:
    with path.open("rb") as handle:
        return tomllib.load(handle)


def check_flyio(root: Path) -> list[str]:
    errors: list[str] = []
    flyio = root / "flyio"
    workflow = root / ".github" / "workflows" / "flyio.yml"
    secrets_doc = flyio / "SECRETS.md"
    deviations = root / "docs" / "architecture" / "DEVIATIONS.md"

    # The file set is exact: a new app file must be added to this check on purpose.
    expected_files = {spec["file"] for spec in APPS.values()} | {POSTGRES_FILE}
    present = {p.name for p in flyio.glob("*.fly.toml")}
    for name in sorted(expected_files - present):
        errors.append(f"flyio/{name} is missing")
    for name in sorted(present - expected_files):
        errors.append(f"flyio/{name} is not an app of the estate (add it to scripts/check-flyio-config.py on purpose)")

    configs: dict[str, dict] = {}
    for key, spec in list(APPS.items()) + [("postgres", {"file": POSTGRES_FILE})]:
        path = flyio / spec["file"]
        if not path.exists():
            continue
        try:
            configs[key] = load(path)
        except tomllib.TOMLDecodeError as error:
            errors.append(f"flyio/{spec['file']} does not parse: {error}")

    apps_seen: dict[str, str] = {}
    for key, cfg in configs.items():
        file = f"flyio/{key}.fly.toml" if key == "postgres" else f"flyio/{APPS[key]['file']}"
        app = cfg.get("app", "")
        if not re.fullmatch(r"postmanclone-[a-z0-9-]+", app):
            errors.append(f"{file}: app must be postmanclone-<name> in lower case, got {app!r}")
        elif app in apps_seen:
            errors.append(f"{file}: app {app} is already used by {apps_seen[app]}")
        else:
            apps_seen[app] = file
        if not cfg.get("primary_region"):
            errors.append(f"{file}: primary_region is missing")
        check_build(root, file, key, cfg, errors)
        check_env(file, cfg, errors)

    for key, cfg in configs.items():
        if key == "postgres":
            check_postgres(f"flyio/{POSTGRES_FILE}", cfg, errors)
            continue
        file = f"flyio/{APPS[key]['file']}"
        check_http(file, key, cfg, errors)

    check_references(configs, errors)
    check_secrets_and_workflow(workflow, secrets_doc, configs, errors)

    if deviations.exists() and "RequireHttpsMetadata" not in deviations.read_text(encoding="utf-8"):
        errors.append("docs/architecture/DEVIATIONS.md does not record the RequireHttpsMetadata deviation (ADR-0029)")
    return errors


def check_build(root: Path, file: str, key: str, cfg: dict, errors: list[str]) -> None:
    build = cfg.get("build", {})
    flyio = root / "flyio"
    if key == "postgres":
        image = build.get("image", "")
        if not re.fullmatch(r"postgres:\d+(\.\d+)?-[a-z]+", image):
            errors.append(f"{file}: the PostgreSQL image must be a pinned major (postgres:16-alpine), got {image!r}")
        return
    if key == "authservice":
        if build.get("image") != AUTH_IMAGE:
            errors.append(f"{file}: the authservice image must be {AUTH_IMAGE} (ADR-0010), got {build.get('image')!r}")
        return
    dockerfile = build.get("dockerfile")
    if not dockerfile:
        errors.append(f"{file}: [build] dockerfile is missing")
        return
    if not build.get("context"):
        errors.append(f"{file}: [build] context is missing; the Dockerfile copies from the repository root")
    dockerfile_path = (flyio / dockerfile).resolve()
    if not dockerfile_path.is_file():
        errors.append(f"{file}: the Dockerfile {dockerfile} does not exist")
        return
    text = dockerfile_path.read_text(encoding="utf-8")
    # Two forms are in use: ASPNETCORE_URLS=http://+:8080 and ASPNETCORE_HTTP_PORTS=8080. Both bind every interface.
    if "ASPNETCORE_URLS=http://+:8080" not in text and "ASPNETCORE_HTTP_PORTS=8080" not in text:
        errors.append(f"{file}: {dockerfile} does not listen on 8080 on all interfaces")
    if not re.search(r"^USER\s+\S+", text, re.MULTILINE):
        errors.append(f"{file}: {dockerfile} does not run as a non-root user (USER)")


def check_http(file: str, key: str, cfg: dict, errors: list[str]) -> None:
    spec = APPS[key]
    http = cfg.get("http_service")
    if not http:
        errors.append(f"{file}: no [http_service]; this app serves HTTP")
        return
    if http.get("internal_port") != 8080:
        errors.append(f"{file}: internal_port must be 8080 (the port the Dockerfile sets)")
    if not isinstance(http.get("force_https"), bool):
        errors.append(f"{file}: force_https must be set explicitly")
    if spec["public"] is False and http.get("force_https") is True:
        errors.append(f"{file}: an internal app has force_https = true, which redirects its .flycast calls to HTTPS")
    checks = http.get("checks", [])
    if not checks:
        errors.append(f"{file}: [[http_service.checks]] is missing: no health check, no deploy verdict")
    for check in checks:
        if check.get("path") != spec["health"]:
            errors.append(f"{file}: health check path must be {spec['health']}, got {check.get('path')!r}")
        if not check.get("grace_period"):
            errors.append(f"{file}: health check has no grace_period")
    minimum = http.get("min_machines_running")
    if spec["sync"] and minimum != 1:
        errors.append(f"{file}: a synchronous app must keep min_machines_running = 1 (FLY_DEPLOYMENT §8)")
    if not spec["sync"] and minimum != 0:
        errors.append(f"{file}: execution-service scales to zero (min_machines_running = 0), got {minimum!r}")


def check_postgres(file: str, cfg: dict, errors: list[str]) -> None:
    if cfg.get("http_service") or cfg.get("services"):
        errors.append(f"{file}: PostgreSQL must have no [http_service] and no [[services]] (no public listener)")
    mounts = cfg.get("mounts", [])
    if len(mounts) != 1:
        errors.append(f"{file}: exactly one [[mounts]] volume is expected")
        return
    mount = mounts[0]
    destination = mount.get("destination", "")
    pgdata = cfg.get("env", {}).get("PGDATA", "")
    if not (pgdata.startswith(destination.rstrip("/") + "/") and pgdata != destination):
        errors.append(f"{file}: PGDATA must be a subdirectory of the mount {destination} (initdb refuses lost+found)")
    if not mount.get("initial_size"):
        errors.append(f"{file}: the volume needs initial_size")
    init_files = cfg.get("files", [])
    if not any(f.get("guest_path", "").endswith(".sh") and "ON_ERROR_STOP" in f.get("raw_value", "") for f in init_files):
        errors.append(f"{file}: the init script that creates the service databases is missing")


def check_env(file: str, cfg: dict, errors: list[str]) -> None:
    for name, value in cfg.get("env", {}).items():
        if SECRET_LIKE.search(name):
            errors.append(f"{file}: [env] holds {name}, which looks like a secret; put it in Fly secrets (flyio/SECRETS.md)")
        if isinstance(value, str) and (re.search(r"Password=", value) or "-----BEGIN" in value):
            errors.append(f"{file}: [env] {name} carries a password or a key")


def check_references(configs: dict[str, dict], errors: list[str]) -> None:
    estate = {cfg.get("app", "") for cfg in configs.values()}
    for key, cfg in configs.items():
        for name, value in cfg.get("env", {}).items():
            if not isinstance(value, str):
                continue
            for target in FLYCAST.findall(value):
                if target not in estate:
                    errors.append(f"flyio/{key}.fly.toml: {name} points at {target}.flycast, which is not an app of the estate")
            match = DISCOVERY_KEY.match(name)
            if match and match.group(1) not in DISCOVERY_NAMES:
                errors.append(f"flyio/{key}.fly.toml: {name} names a service the estate does not have")

    web_env = configs.get("web", {}).get("env", {})
    names = {DISCOVERY_KEY.match(k).group(1) for k in web_env if DISCOVERY_KEY.match(k)}
    for needed in DISCOVERY_NAMES:
        if needed not in names:
            errors.append(f"flyio/web.fly.toml: no services__{needed}__http__0 (the Web's BackendServices needs it)")
    exec_env = configs.get("execution-service", {}).get("env", {})
    if "services__environments-service__http__0" not in exec_env:
        errors.append("flyio/execution-service.fly.toml: no services__environments-service__http__0")


def check_secrets_and_workflow(workflow: Path, secrets_doc: Path, configs: dict[str, dict], errors: list[str]) -> None:
    if not workflow.exists():
        errors.append(".github/workflows/flyio.yml is missing")
        return
    text = workflow.read_text(encoding="utf-8")
    documented = secrets_doc.read_text(encoding="utf-8") if secrets_doc.exists() else ""
    if not documented:
        errors.append("flyio/SECRETS.md is missing")

    for key, cfg in configs.items():
        app = cfg.get("app", "")
        if app and app not in text:
            errors.append(f"the workflow never names {app} (flyio/{key}.fly.toml)")
        if key != "postgres":
            config_file = f"flyio/{APPS[key]['file']}"
            if f"--config {config_file}" not in text:
                errors.append(f"the workflow does not deploy {config_file}")

    # Names on the same line only: [ \t], never a newline, so the next YAML line is not read as a secret name.
    for match in re.finditer(r"scripts/flyio-preflight\.sh[ \t]+(\S+)((?:[ \t]+[A-Za-z_][A-Za-z0-9_]*)*)", text):
        for name in match.group(2).split():
            if name not in documented:
                errors.append(f"flyio/SECRETS.md does not document the secret {name} that the workflow checks")


def self_test() -> list[str]:
    """Breaks a copy of the real files in several ways; each break must produce at least one error."""
    failures: list[str] = []
    scenarios = {
        "web without a health check path": lambda root: _edit(
            root / "flyio" / "web.fly.toml", 'path = "/health"', 'path = "/index"'),
        "postgres with a public listener": lambda root: _append(
            root / "flyio" / "postgres.fly.toml", '\n[http_service]\n  internal_port = 8080\n  force_https = true\n'),
        "a password in a public [env] table": lambda root: _edit(
            root / "flyio" / "web.fly.toml", 'ASPNETCORE_URLS = "http://+:8080"',
            'ASPNETCORE_URLS = "http://+:8080"\n  ConnectionStrings__x = "Host=h;Password=abc"'),
        "a secret name missing from SECRETS.md": lambda root: _edit(
            root / "flyio" / "SECRETS.md", "Jwt__PrivateKeyPem", "a signing key", everywhere=True),
        "a flycast name of an app that does not exist": lambda root: _edit(
            root / "flyio" / "web.fly.toml", "postmanclone-collections.flycast", "postmanclone-nowhere.flycast"),
    }
    for name, break_it in scenarios.items():
        with tempfile.TemporaryDirectory() as scratch:
            copy = Path(scratch)
            shutil.copytree(REPO / "flyio", copy / "flyio")
            (copy / ".github" / "workflows").mkdir(parents=True)
            shutil.copy(REPO / ".github" / "workflows" / "flyio.yml", copy / ".github" / "workflows" / "flyio.yml")
            (copy / "docs" / "architecture").mkdir(parents=True)
            shutil.copy(REPO / "docs" / "architecture" / "DEVIATIONS.md", copy / "docs" / "architecture" / "DEVIATIONS.md")
            break_it(copy)
            if not check_flyio(copy):
                failures.append(f"self-test: the check accepted a broken copy ({name})")
    return failures


def _edit(path: Path, old: str, new: str, everywhere: bool = False) -> None:
    text = path.read_text(encoding="utf-8")
    if old not in text:
        raise SystemExit(f"self-test setup: {old!r} is not in {path}")
    path.write_text(text.replace(old, new) if everywhere else text.replace(old, new, 1), encoding="utf-8")


def _append(path: Path, extra: str) -> None:
    path.write_text(path.read_text(encoding="utf-8") + extra, encoding="utf-8")


def main(argv: list[str]) -> int:
    if "--self-test" in argv:
        failures = self_test()
        for line in failures:
            print(line, file=sys.stderr)
        if failures:
            return 1
        print("check-flyio-config: the self-test breaks five copies, and each one fails the check.")
    errors = check_flyio(REPO)
    for line in errors:
        print(f"check-flyio-config: {line}", file=sys.stderr)
    if errors:
        return 1
    print("check-flyio-config: six Fly configs, the workflow and SECRETS.md agree.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
