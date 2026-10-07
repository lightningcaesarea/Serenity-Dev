#!/usr/bin/env python3
"""
Adds a merged pull request to Serenity's in-game changelog (Resources/Changelog/ChangelogSerenity.yml).

The entries come from the PR body's :cl: block, the same format as Starlight's:

    :cl: Optional author name
    - add: Something new.
    - tweak: Something changed.
    - fix: Something fixed.
    - remove: Something removed.

A PR with no :cl: block gets one entry made from its title. A line reading just `no-changelog`
keeps a code-only or CI-only PR out of it. Running it twice for the same PR changes nothing.

Env: PR_NUMBER, GITHUB_REPOSITORY, GITHUB_TOKEN, CHANGELOG_FILE_PATH (optional).
"""

import os
import re

import yaml

DEFAULT_PATH = "Resources/Changelog/ChangelogSerenity.yml"

# GitHub logins shown under a friendlier name.
AUTHOR_NAMES = {
    "lightningcaesarea": "Inny",
}

CHANGE_RE = re.compile(r"-\s*(add|remove|tweak|fix)\s*:\s*(.+)", re.IGNORECASE)


def parse_changelog(body: str, default_author: str):
    """Returns a list of {author, changes}, or None if the body has no :cl: block."""
    body = re.sub(r"<!--.*?-->", "", body or "", flags=re.DOTALL)
    if re.search(r"^\s*no-changelog\s*$", body, re.MULTILINE):
        return []
    if ":cl:" not in body:
        return None

    blocks = []
    lines = body.splitlines()
    i = 0
    while i < len(lines):
        line = lines[i].strip()
        if not line.startswith(":cl:"):
            i += 1
            continue

        author = line[len(":cl:"):].strip() or default_author
        changes = []
        i += 1
        while i < len(lines) and not lines[i].strip().startswith(":cl:"):
            match = CHANGE_RE.match(lines[i].strip())
            if match:
                changes.append({"message": match.group(2).strip(), "type": match.group(1).capitalize()})
            i += 1

        if changes:
            blocks.append({"author": author, "changes": changes})

    return blocks


def title_entry(title: str, author: str):
    """One entry from the PR title, typed by its first word."""
    first = title.split(" ", 1)[0].rstrip(":").lower()
    kind = {"add": "Add", "fix": "Fix", "remove": "Remove"}.get(first, "Tweak")
    message = title.strip()
    if not message.endswith((".", "!", "?")):
        message += "."
    return [{"author": author, "changes": [{"message": message, "type": kind}]}]


def add_entries(path: str, pr_number: int, blocks, merged_at: str, url: str) -> int:
    if os.path.exists(path):
        with open(path, encoding="utf-8") as f:
            data = yaml.safe_load(f) or {}
    else:
        data = {}
    data.setdefault("Order", -2)
    entries = data.setdefault("Entries", [])

    base_id = pr_number * 100
    if any(base_id < entry.get("id", 0) <= base_id + 99 for entry in entries):
        print(f"PR #{pr_number} is already in the changelog.")
        return 0

    for i, block in enumerate(blocks, start=1):
        entries.append({
            "author": block["author"],
            "changes": block["changes"],
            "id": base_id + i,
            "time": merged_at,
            "url": url,
        })
    entries.sort(key=lambda e: (e["time"], e["id"]))

    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        yaml.dump(data, f, allow_unicode=True, sort_keys=False, width=1000)
    return len(blocks)


def main():
    pr_number = int(os.environ["PR_NUMBER"])
    repo_name = os.environ["GITHUB_REPOSITORY"]
    path = os.environ.get("CHANGELOG_FILE_PATH") or DEFAULT_PATH

    from github import Github

    pr = Github(os.environ["GITHUB_TOKEN"]).get_repo(repo_name).get_pull(pr_number)
    if not pr.merged_at:
        print(f"PR #{pr_number} is not merged.")
        return

    author = AUTHOR_NAMES.get(pr.user.login, pr.user.login)
    blocks = parse_changelog(pr.body, author)
    if blocks is None:
        blocks = title_entry(pr.title, author)
    if not blocks:
        print(f"PR #{pr_number} opted out of the changelog.")
        return

    merged_at = pr.merged_at.strftime("%Y-%m-%dT%H:%M:%S.000000+00:00")
    added = add_entries(path, pr_number, blocks, merged_at, pr.html_url)
    print(f"Added {added} entries for PR #{pr_number}.")


if __name__ == "__main__":
    main()
