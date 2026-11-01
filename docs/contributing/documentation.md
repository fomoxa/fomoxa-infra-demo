# Documentation

This documentation lives in `docs/` and is published with GitBook through Git Sync, using the same layout as the Fomoxa for Unity documentation. `gitbook-docs.yaml` describes the site and its two spaces, English and Vietnamese. `.gitbook.yaml` names `README.md` and `SUMMARY.md` and lists the redirects for pages that moved. Both sit at the root of the documentation.

## Structure

| Section | Folder | Purpose |
|---|---|---|
| Get started | `getting-started/` | Prepare the machine, then run the mock cluster without Kubernetes |
| Deploy | `deploy/` | Build the real cluster on k3d with Agones, one short file per group of steps, plus a Kubernetes primer and a manifest walkthrough |
| Unity client | `unity-client.md` | Open the project, build the scenes and the player |
| Operations | `operations/` | Daily tasks: logs, updates, drain, TLS, monitoring, scaling, backups |
| Testing and problems | `testing.md`, `troubleshooting.md` | What each test checks, and fixes for common failures |
| Reference | `reference.md` | Ports, scripts, environment variables, file map |
| Contributing | `contributing/` | Working on these pages |

`SUMMARY.md` is the table of contents and every page must be listed in it. A folder with several parts has a `README.md` that works as its index. A part file is aimed at about 300 lines at most, so split a page before it grows past that.

## Layout and languages

```
docs/
  .gitbook.yaml         readme, summary, redirects (English space)
  gitbook-docs.yaml     the site: two spaces, EN at docs/, VI at docs/vi/
  README.md, SUMMARY.md
  getting-started/ deploy/ operations/ contributing/
  unity-client.md testing.md troubleshooting.md reference.md
  tools/check_docs.py   the checker
  vi/                   Vietnamese mirror: its own .gitbook.yaml, README.md, SUMMARY.md
```

The Vietnamese pages live in `vi/` and mirror the English pages file for file, with the same file names. English is the source: when you change an English page, change the Vietnamese page at the same path in the same commit. GitBook serves only files inside a space, so a page never links across to the other language and never uses an image from outside its own space.

## Rules for every page

- Write plain technical prose and use sentence case for headings.
- Describe what the code does today.
- Give every step a goal, the commands, what they mean, the expected output, a check, and the common errors.
- Prefer explicit commands over scripts. When a script exists, say which commands it runs.
- Link related pages with relative links and keep headings stable, because other pages link to them.
- Put commands in fenced blocks tagged `bash`, one command or one coherent sequence per block.
- Use GitBook hints (`{% hint style="warning" %}`) only for warnings the reader must not miss.
- A short flow or sequence is a Mermaid block. Keep `;` and `#` out of its labels.

## Moving or renaming a page

1. Move the file in both languages.
2. Update `SUMMARY.md` in both spaces.
3. Add the old path to `redirects` in `.gitbook.yaml` and `vi/.gitbook.yaml`, without the `.md` extension, so old links keep working.
4. Run the checker.

## Checking the pages

```bash
python3 docs/tools/check_docs.py
```

The checker reads both spaces and reports:

- links to files or headings that do not exist, and links that leave their space (the root README and the READMEs of `MyGame` and `tools/MockCluster` are checked too);
- pages that `SUMMARY.md` does not list;
- English pages without a Vietnamese page, and the reverse;
- Vietnamese pages whose number of headings differs from the English page.

It must print `0 problems` before you commit. CI runs it in the `lint-scripts-and-manifests` job.
