#!/usr/bin/env bash
# Run the supplied projects or solution from src, keeping all failures in the CI summary.
set -euo pipefail

if (( $# == 0 )); then
    echo 'Usage: test-dotnet.sh <project-or-solution> [...]' >&2
    exit 2
fi

printf '### %s\n\n' "${TEST_LABEL:-Tests}" >> "$GITHUB_STEP_SUMMARY"
status=0
for project in "$@"; do
    echo "::group::$project"
    prefix=$(basename "$project")
    dotnet test "$project" -c Release --logger "trx;LogFilePrefix=${prefix%.*}" \
        --results-directory "$GITHUB_WORKSPACE/TestResults" 2>&1 | tee "$RUNNER_TEMP/test.log" || status=1
    # An assembly summary is optional; a failed test command still fails the job.
    grep -E '^(Passed|Failed)!' "$RUNNER_TEMP/test.log" | sed 's/^/    /' >> "$GITHUB_STEP_SUMMARY" || true
    echo '::endgroup::'
done
exit "$status"
