# ADR-0007: Deployment is out of scope for this build

- **Status:** Accepted
- **Date:** 2026-10-26
- **Work package:** WP00

## Context

The reference architecture (P7) expects a deployment target, and the estate's other products deploy
to Fly.io through tag-driven workflows. This build has no hosting account, no production secrets,
and no decision yet about where it runs. The build sessions also have no Docker daemon, so an image
they produce cannot be run or tested in place.

Deploying a half-configured service is worse than not deploying it: it creates credentials, a public
address and a data store that somebody must then maintain.

## Options considered

1. **Ship Fly.io artifacts now** (`fly.toml`, a deploy workflow, tags). Rejected: no account, no
   secrets, no decision. A tag-triggered workflow would be a deployment waiting for an accident.
2. **Ship images only**, built in CI, pushed nowhere. Chosen as the ceiling for this build. CI builds
   each Dockerfile (`docker-build`) so that the images are known to build, and nothing is pushed.
3. **Ship a runnable local stack only** (AppHost for development, `scripts/run-local-stack.sh` for
   end-to-end tests). Chosen alongside option 2.

## Decision

- No deployment workflow, no registry push, no `v*` tag trigger, no Fly.io or cloud configuration in
  this repository. The secret-scan workflow is tag-less on purpose.
- The Dockerfiles that WP01 onward add are built by CI and never pushed.
- Running the product is a local concern: the Aspire AppHost in development (P1) and the local stack
  script for end-to-end tests.
- Choosing a deployment target, supplying production secrets and creating a hosting account are owner
  actions. They are listed in the implementation plan and in each pull request that needs one.

## Consequences

- The product is not reachable on the internet from this repository. That is the intended state
  until the owner decides otherwise.
- The security review covers the code and the configuration, not a running production environment. The
  review says so.
- A later deployment ADR will supersede this one and add the workflows, with the owner's sign-off.
- Image builds in CI catch Dockerfile breakage early, even without a deployment.

## Revisit when

The owner chooses a hosting target and provides production secrets. That is a new ADR, with a deployment
workflow, a tag policy and a rollback plan written in the same change.
