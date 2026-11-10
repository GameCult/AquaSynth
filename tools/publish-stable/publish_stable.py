#!/usr/bin/env python3
"""The one script that publishes AquaSynth listening pages: one dev, one URL, overwritten in place.

  publish_stable.py dings SRC_DIR   -> /srv/minds-web/aqua/dings/  (https://minds.gamecult.org/aqua/dings/)
  publish_stable.py ear   SRC_DIR   -> /srv/minds-web/aqua/ear/    (https://minds.gamecult.org/aqua/ear/)
  publish_stable.py banners         -> banner on every older per-hash page, pointing at the stable pages

SRC_DIR for `dings` is a built publish dir (index.html, out/, findings/, metrics.json; see tools/dings-voices/README.md);
for `ear` a dir of aqua-ear outputs (one subdir per run, each with report.txt/png files, as `aqua_ear.py --out` writes).
Stdlib only. Idempotent: running it again with the same input leaves the same tree; the stable dir is replaced by renaming
a fully written staging dir, so a reader never sees a half-copied page. Deletes nothing outside the stable dir it owns.
--root and --base are for tests. Nothing here listens to anything: it moves files and stamps a header.
"""
import argparse, html, os, re, shutil, subprocess, sys, tempfile

ROOT = "/srv/minds-web/aqua"
BASE = "https://minds.gamecult.org/aqua"
STABLE = {"dings": "dings", "ear": "ear"}
MARK = "aqua-stable-banner"
BANNER_CSS = "background:#fff3c4;color:#222;border:2px solid #b58900;padding:.6rem 1rem;margin:0;font:1rem/1.4 system-ui,sans-serif"


def rev():
    try:
        return subprocess.check_output(["git", "-C", os.path.dirname(os.path.abspath(__file__)), "rev-parse", "--short", "HEAD"], text=True).strip()
    except Exception:
        return "unknown"


def banner(inner):
    return f'<div class="{MARK}" style="{BANNER_CSS}"><!--{MARK}-->{inner}</div>\n'


def put_banner(path, inner):
    """Insert (or replace) the marked banner right after <body>, else after </head>, else at the top. Idempotent."""
    s = open(path, encoding="utf-8").read()
    s = re.sub(rf'<div class="{MARK}".*?</div>\n?', "", s, flags=re.S)
    b = banner(inner)
    m = re.search(r"<body[^>]*>", s, re.I) or re.search(r"</head>", s, re.I)
    s = s[:m.end()] + "\n" + b + s[m.end():] if m else b + s
    open(path, "w", encoding="utf-8").write(s)


def swap_in(src, dest, prepare):
    """Copy src to a staging dir beside dest, let prepare(stage) stamp it, then replace dest by rename."""
    parent = os.path.dirname(dest)
    os.makedirs(parent, exist_ok=True)
    stage = tempfile.mkdtemp(prefix=".stage-", dir=parent)
    try:
        shutil.copytree(src, stage, dirs_exist_ok=True)
        prepare(stage)
        os.chmod(stage, 0o755)
        old = dest + ".old"
        if os.path.exists(dest):
            shutil.rmtree(old, ignore_errors=True)
            os.rename(dest, old)
        os.rename(stage, dest)
        shutil.rmtree(old, ignore_errors=True)
    except BaseException:
        shutil.rmtree(stage, ignore_errors=True)
        raise


def publish_dings(src, root, base):
    if not os.path.isfile(os.path.join(src, "index.html")) or not os.path.isdir(os.path.join(src, "out", "senders")):
        sys.exit(f"{src} is not a built dings publish dir (needs index.html and out/senders)")
    label = os.path.basename(os.path.abspath(src))
    note = (f"<strong>The one Dings page.</strong> {base}/dings/ always shows the latest version; it is overwritten in place. "
            f"Current: <code>{html.escape(label)}</code>, AquaSynth <code>{rev()}</code>. Older per-version pages are kept but are not the place to listen.")

    def prepare(stage):
        put_banner(os.path.join(stage, "index.html"), note)
        open(os.path.join(stage, "VERSION"), "w").write(f"{label}\nAquaSynth {rev()}\n")
    swap_in(src, os.path.join(root, STABLE["dings"]), prepare)


def publish_ear(src, root, base):
    runs = sorted(d for d in os.listdir(src) if os.path.isdir(os.path.join(src, d)))
    if not runs:
        sys.exit(f"{src} has no run subdirectories")

    def prepare(stage):
        items = []
        for r in runs:
            files = sorted(f for f in os.listdir(os.path.join(stage, r)) if os.path.isfile(os.path.join(stage, r, f)))
            links = " · ".join(f'<a href="{html.escape(r)}/{html.escape(f)}">{html.escape(f)}</a>' for f in files)
            img = next((f for f in ("compare.png", "spectrogram.png") if f in files), None)
            pic = f'<p><a href="{html.escape(r)}/{img}"><img src="{html.escape(r)}/{img}" alt="{html.escape(r)}" style="max-width:100%"></a></p>' if img else ""
            items.append(f"<section><h2>{html.escape(r)}</h2>{pic}<p>{links}</p></section>")
        page = ('<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">'
                "<title>aqua-ear examples</title></head><body style=\"font:1rem/1.5 system-ui,sans-serif;max-width:60rem;margin:0 auto;padding:1rem\">"
                "<h1>aqua-ear examples</h1><p>Pictures and reports from <code>tools/aqua-ear</code> (spectrogram, onsets, level envelope, detector verdicts). "
                "They are signal facts: the tool cannot judge timbre, annoyance or feel, and nobody has judged these by ear. "
                f"Current as of AquaSynth <code>{rev()}</code>.</p>" + "".join(items) + "</body></html>\n")
        open(os.path.join(stage, "index.html"), "w").write(page)
        put_banner(os.path.join(stage, "index.html"), f"<strong>The one aqua-ear examples page.</strong> {base}/ear/ is overwritten in place.")
    swap_in(src, os.path.join(root, STABLE["ear"]), prepare)


def banners(root, base):
    skip = set(STABLE.values())
    for d in sorted(os.listdir(root)):
        p = os.path.join(root, d)
        if d in skip or d.startswith(".") or not os.path.isdir(p):
            continue
        dings = d.startswith("dings-voices-")
        inner = (f"<strong>Old per-version page.</strong> The one Dings page is <a href=\"{base}/dings/\">{base}/dings/</a> (always the latest); listen there."
                 if dings else
                 f"<strong>Old per-version page, kept for the record.</strong> One-place rule: current Dings are at <a href=\"{base}/dings/\">{base}/dings/</a>, "
                 f"aqua-ear examples at <a href=\"{base}/ear/\">{base}/ear/</a>.")
        idx = os.path.join(p, "index.html")
        if not os.path.exists(idx):      # a dir with no page of its own gets a minimal one, so the banner has somewhere to stand
            files = sorted(os.listdir(p))
            open(idx, "w").write('<!doctype html><html lang="en"><head><meta charset="utf-8"><title>' + html.escape(d) + "</title></head><body><h1>" + html.escape(d)
                                 + "</h1><ul>" + "".join(f'<li><a href="{html.escape(f)}">{html.escape(f)}</a></li>' for f in files) + "</ul></body></html>\n")
        put_banner(idx, inner)
        print("banner:", idx)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("what", choices=["dings", "ear", "banners"])
    ap.add_argument("src", nargs="?")
    ap.add_argument("--root", default=ROOT)
    ap.add_argument("--base", default=BASE)
    a = ap.parse_args()
    if a.what == "banners":
        banners(a.root, a.base)
        return
    if not a.src or not os.path.isdir(a.src):
        sys.exit("SRC_DIR is required and must be a directory")
    {"dings": publish_dings, "ear": publish_ear}[a.what](a.src, a.root, a.base)
    print(f"published {a.what}: {a.root}/{STABLE[a.what]}/  ->  {a.base}/{STABLE[a.what]}/")


if __name__ == "__main__":
    main()
