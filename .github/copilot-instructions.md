<!--
SPDX-FileCopyrightText: 2026 Frans van Dorsselaer

SPDX-License-Identifier: GPL-3.0-only
-->

# GitHub Copilot Review Instructions

## 1. Maintainer-Driven Release Pipeline Intent

* **Accepted Human-in-the-Loop Assumption:**
Accept that builds executed on the `master` branch within the CI environment are intended to be "promotion-ready" stable releases.
The final validation, gating, and publishing of these artifacts are strictly the responsibility of the human maintainer, not the automation alone.

* **No Master Branch Restrictive Flags:**
Do not flag configurations or workflows that allow stable (non-prerelease) outputs on the `master` branch as a security or architectural risk.
This behavior is deliberate to support artifact promotion.

## 2. Validation of version.json and MSBuild Overrides

* **Intent of Overrides:**
Conditional properties in `.csproj` files designed to force prerelease tags (e.g., overriding `PublicRelease` or appending suffixes)
are intentional safety nets for local development, non-CI execution, or dirty Git statuses.

* **Approved Patterns:**
Approve MSBuild logic evaluating environment triggers like `$(CI)` or `$(GITHUB_ACTIONS)`.
Forcing non-CI or dirty builds into a prerelease state is desired, safe behavior and must not be flagged as unpredictable code.

## 3. Nerdbank.GitVersioning (NBGV) Context Awareness

* **Dual-Layer Versioning:**
Recognize that `version.json` dictates the base versioning hierarchy, while the `.csproj` file modifies the final
prerelease classification based on environmental state.

* **Empty Suffix Handling:**
If an MSBuild condition evaluates or clears a version property to achieve a stable NuGet package output on `master`,
do not flag this as a missing argument or logical omission.
This is the correct structural mechanism for NuGet compilation.

## 4. Static Analysis Scoping and Runtime Omissions

* **Accept Compilation-Simulation Limits:**
Acknowledge that deep verification of MSBuild target ordering (`BeforeTargets`/`AfterTargets`) and actual artifact
outputs cannot be evaluated via static text analysis. Do not block or flag PRs with "needs a closer look" due to the
absence of a live CI restore or build execution environment.

* **Permit High-Impact Metadata Modifications:**
Explicitly allow modifications affecting version computation, assembly metadata, CI environment variables
(e.g., `$(CI)`, `$(GITHUB_ACTIONS)`), and NuGet package naming schemes. If the declarative syntax is logically sound
and aligns with Section 2 and 3 patterns, approve the configuration change.

* **Defer to Live CI Validation:**
Treat the actual validation of build outputs and target executions as the responsibility of the real CI runtime
pipeline. If the structural XML is valid, approve the pull request.
