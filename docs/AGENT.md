# The local agent (operator's view)

The agent is a small program on the user's computer. The PostmanClone Web asks it to send a request when the
target is `localhost`, a LAN address, or any other private address, which the server never reaches
([ADR-0026](decisions/0026-local-agent-and-fly-estate.md), [ADR-0027](decisions/0027-agent-threat-model.md)).

## What it is

- A .NET tool, `postmanclone-agent`, and a single-file program for win-x64, linux-x64 and osx-arm64.
- Built by CI as workflow artifacts (`agent-artifacts` in `.github/workflows/pr-validation.yml`). There is no release
  and no tag; a release channel is an open item.
- It listens on `127.0.0.1:47631` only. Nothing else on the machine, and nothing on the network, can reach it except
  through the checks in ADR-0027.

## Commands

| Command | What it does |
|---|---|
| `postmanclone-agent setup --server <url> --web-origin <origin> [--allow-private true\|false]` | Records the EnvironmentsService address and the page origins the agent accepts. Run once, before pairing. Origins are exact (`https://app.example`), with no path, wildcard or trailing slash. |
| `postmanclone-agent run [--port <n>]` | Listens on `127.0.0.1` until stopped (Ctrl+C). Exits 1 if the port is in use. |
| `postmanclone-agent pair <code> [--server <url>]` | Exchanges a pairing code from the Web's Agent page in this terminal. Prints a local pairing token **once**. |
| `postmanclone-agent status` | Shows the server, origins, the private-target setting and whether it is paired. Never prints a token. |
| `postmanclone-agent forget` | Deletes the local credentials. Then revoke the agent on the Web's Agent page as well. |

## Pairing

1. Run `setup` once with the Web's origin.
2. Start `run` and keep it running.
3. On the Web's Agent page, create a pairing code (valid five minutes, single use) and pair. The page exchanges it through
   the agent's local `POST /v1/pair`.

## Where the credentials are

The file `agent.json` in the user's configuration folder (`~/.config/postmanclone-agent` on Linux,
`~/Library/Application Support/postmanclone-agent` on macOS, `%APPDATA%\postmanclone-agent` on Windows; override with
`POSTMANCLONE_AGENT_HOME`). It holds:

- the **agent token**, a credential that lasts 90 days and can be revoked on the Web;
- the **hash** of the local pairing token (the token itself is never stored).

On Unix the file is `0600` and its folder `0700`. On Windows the file takes the profile ACL; a stricter ACL and the OS
keychain are open items. Anyone who can read the file as the same user can use the agent until it is revoked.

## Private targets

By default (`AllowPrivate` true) the agent sends to `localhost`, the LAN and link-local addresses. That is the point of
the agent. Set `--allow-private false` at `setup` to refuse them (the server's address rules then apply): a loopback
target answers `ssrf_blocked_address`. A shared collection can send from a member's computer to that member's network
while the flag is true; this is the accepted residual risk in OPEN-ITEMS §0.

## What the agent sees and keeps

- During one send it fetches the environment's variables, secrets included, with its agent token. The values are held in
  memory for that send, masked in every response, and never written to disk or to a log.
- It keeps no request history. The Web records the masked result on the server (`POST /api/v1/history/records`).
- It logs method, host, status, sizes and durations only.

## Revoking and removing

- Revoke an agent on the Web's Agent page. Its token stops working at once.
- Removing a member from a workspace does **not** revoke that member's agents yet (open item).
- `forget` removes the local credentials only.

## Not verified here

- Safari's handling of an HTTPS page calling `http://127.0.0.1`, and Chrome's Local Network Access prompt.
- The single-file binaries on a real machine (CI builds them; they were smoke-tested on Linux only in the session).
