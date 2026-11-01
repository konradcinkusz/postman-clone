// The local agent's browser client (WP31, ADR-0026 Decision 9, ADR-0028). This is the only script the Web loads besides Blazor's own.
// It has no library and no inline code, and it never touches the DOM: every function is a single fetch whose status and text go back
// to the circuit, which decides what they mean. The page's session token never reaches this file. The pairing token is held by the
// circuit and passed in per call; this file keeps no state except the abort controllers of sends in flight.
//
// Functions (called by AgentBridge through IJSRuntime as agentClient.<name>):
//   probe(origins)                  -> { state: "answered" | "refused" | "unreachable", base, paired }
//   pair(origin, code)              -> { status, text }      status 0 = no answer
//   send(origin, token, body, id)   -> { status, text }
//   cancel(id)                      -> void
//   postRun(recordJson, antiforgery) -> { status, text }     same-origin: the Web's own /agent/runs
(function () {
    "use strict";

    var PROBE_TIMEOUT_MS = 2000;
    var inFlight = new Map();

    function signalFor(sendId) {
        var controller = new AbortController();
        inFlight.set(sendId, controller);
        return controller;
    }

    async function readAnswer(response) {
        return { status: response.status, text: await response.text() };
    }

    async function call(url, init) {
        try {
            var response = await fetch(url, init);
            return await readAnswer(response);
        } catch (error) {
            // A refused connection, a timeout or an abort: no answer from the agent.
            return { status: 0, text: null };
        }
    }

    async function probe(origins) {
        for (var i = 0; i < origins.length; i++) {
            var origin = origins[i];
            try {
                var response = await fetch(origin + "/v1/status", {
                    method: "GET",
                    mode: "cors",
                    credentials: "omit",
                    cache: "no-store",
                    signal: AbortSignal.timeout(PROBE_TIMEOUT_MS)
                });
                if (response.status === 403 || response.status === 421) {
                    // The agent answered and refused this page's origin or host: trying another origin will not help.
                    return { state: "refused", base: origin, paired: false };
                }

                if (response.status !== 200) {
                    continue;
                }

                var body = await response.json();
                if (body && body.agent === "postmanclone-agent") {
                    return { state: "answered", base: origin, paired: body.paired === true };
                }
            } catch (error) {
                // No answer on this origin: try the next one.
            }
        }

        return { state: "unreachable", base: null, paired: false };
    }

    function pair(origin, code) {
        return call(origin + "/v1/pair", {
            method: "POST",
            mode: "cors",
            credentials: "omit",
            cache: "no-store",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ code: code })
        });
    }

    async function send(origin, token, bodyJson, sendId) {
        var controller = signalFor(sendId);
        try {
            return await call(origin + "/v1/send", {
                method: "POST",
                mode: "cors",
                credentials: "omit",
                cache: "no-store",
                headers: { "Content-Type": "application/json", "X-Agent-Pairing-Token": token },
                body: bodyJson,
                signal: controller.signal
            });
        } finally {
            inFlight.delete(sendId);
        }
    }

    function cancel(sendId) {
        var controller = inFlight.get(sendId);
        if (controller) {
            controller.abort();
            inFlight.delete(sendId);
        }
    }

    function postRun(recordJson, antiforgery) {
        return call("/agent/runs", {
            method: "POST",
            credentials: "same-origin",
            cache: "no-store",
            headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": antiforgery },
            body: recordJson
        });
    }

    window.agentClient = {
        probe: probe,
        pair: pair,
        send: send,
        cancel: cancel,
        postRun: postRun
    };
})();
