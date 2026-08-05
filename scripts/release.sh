#!/usr/bin/env bash
#
# Ponango.Inertia release driver.
#
#   ./scripts/release.sh cut     2.1.0    -> create + push release/2.1.0  (CI builds a candidate)
#   ./scripts/release.sh publish 2.1.0    -> create + push tag v2.1.0     (CI publishes to nuget.org)
#
# Two phases on purpose: `publish` refuses to tag unless the candidate build for this exact commit
# is green. nuget.org versions are permanent -- a version pushed by mistake cannot be replaced, only
# unlisted -- so the gate is worth the extra command.
#
# IMPORTANT: run this locally. A push made by a workflow using the default GITHUB_TOKEN does not
# trigger other workflows, so a tag created by CI would silently never fire release.yml.

set -euo pipefail

die() { echo "error: $*" >&2; exit 1; }

command -v git >/dev/null || die "git not found"
cd "$(git rev-parse --show-toplevel)"

usage() {
    cat >&2 <<'EOF'
usage:
  release.sh cut     <MAJOR.MINOR.PATCH>    e.g. release.sh cut 2.1.0
  release.sh publish <MAJOR.MINOR.PATCH>    e.g. release.sh publish 2.1.0

  cut     -> branch release/X.Y.Z from main; CI builds+packs a candidate (publishes nothing)
  publish -> tag vX.Y.Z on that branch;      CI pushes to nuget.org (permanent)
EOF
    exit 2
}

[ $# -eq 2 ] || usage
COMMAND="$1"
VERSION="$2"

require_clean_tree() {
    git diff --quiet && git diff --cached --quiet \
        || die "working tree is dirty -- commit or stash first"
}

case "$COMMAND" in

cut)
    [[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] \
        || die "cut takes MAJOR.MINOR.PATCH (e.g. 2.1.0), got '$VERSION'"

    BRANCH="release/$VERSION"
    require_clean_tree

    [ "$(git rev-parse --abbrev-ref HEAD)" = "main" ] \
        || die "cut a release from main (currently on $(git rev-parse --abbrev-ref HEAD))"

    git fetch origin main --quiet
    [ "$(git rev-parse HEAD)" = "$(git rev-parse origin/main)" ] \
        || die "local main is not in sync with origin/main"

    git ls-remote --exit-code --heads origin "$BRANCH" >/dev/null 2>&1 \
        && die "$BRANCH already exists on origin"

    git branch "$BRANCH"
    git push -u origin "$BRANCH"

    echo
    echo "Pushed $BRANCH. CI is building the candidate."
    echo "Watch it:   gh run watch"
    echo "Then:       ./scripts/release.sh publish $VERSION"
    ;;

publish)
    [[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] \
        || die "publish takes MAJOR.MINOR.PATCH (e.g. 2.1.0), got '$VERSION'"

    TAG="v$VERSION"
    BRANCH="release/$VERSION"
    CURRENT="$(git rev-parse --abbrev-ref HEAD)"

    require_clean_tree

    # release/2.1.0 is the convention; release/2.1 is accepted, matching release.yml.
    case "$CURRENT" in
        "$BRANCH"|"release/${VERSION%.*}") ;;
        *) die "publish from $BRANCH; currently on $CURRENT" ;;
    esac

    git ls-remote --exit-code --tags origin "refs/tags/$TAG" >/dev/null 2>&1 \
        && die "$TAG already exists on origin -- nuget.org versions are permanent, pick a new patch"

    git fetch origin "$CURRENT" --quiet
    [ "$(git rev-parse HEAD)" = "$(git rev-parse "origin/$CURRENT")" ] \
        || die "local $CURRENT is not in sync with origin/$CURRENT -- push first"

    # Gate on the candidate build for THIS commit.
    if command -v gh >/dev/null; then
        SHA="$(git rev-parse HEAD)"
        STATUS="$(gh run list --commit "$SHA" --workflow release --limit 1 \
                    --json conclusion --jq '.[0].conclusion // "none"' 2>/dev/null || echo "unknown")"
        case "$STATUS" in
            success) ;;
            none|unknown)
                die "no candidate build found for $SHA -- push to $BRANCH first, or re-run with SKIP_BUILD_CHECK=1" ;;
            *)
                [ "${SKIP_BUILD_CHECK:-}" = "1" ] \
                    || die "candidate build for $SHA is '$STATUS', not 'success' -- fix it, or set SKIP_BUILD_CHECK=1" ;;
        esac
    else
        echo "warning: gh not found -- skipping the green-build check" >&2
    fi

    git tag -a "$TAG" -m "Ponango.Inertia $VERSION"
    git push origin "$TAG"

    echo
    echo "Pushed $TAG. CI is publishing to nuget.org and creating the GitHub Release."
    echo "Watch it:   gh run watch"
    ;;

*)
    usage
    ;;
esac
