import os, subprocess, sys, tempfile, unittest
HERE = os.path.dirname(os.path.abspath(__file__)); S = os.path.join(HERE, "publish_stable.py")
def run(*a): return subprocess.run([sys.executable, S, *a], capture_output=True, text=True)
def tree(root): return sorted((os.path.relpath(os.path.join(d, f), root), open(os.path.join(d, f), "rb").read()) for d, _, fs in os.walk(root) for f in fs)
class T(unittest.TestCase):
    def setUp(self):
        self.t = tempfile.TemporaryDirectory(); self.root = os.path.join(self.t.name, "aqua"); os.makedirs(self.root)
        self.src = os.path.join(self.t.name, "dings-voices-abc1234"); os.makedirs(self.src + "/out/senders")
        open(self.src + "/index.html", "w").write("<!doctype html><html><head><title>x</title></head><body><h1>v</h1></body></html>")
        open(self.src + "/out/senders/a.ogg", "wb").write(b"OggS")
    def tearDown(self): self.t.cleanup()
    def test_publish_overwrites_in_place_and_is_idempotent(self):
        r = run("dings", self.src, "--root", self.root); self.assertEqual(r.returncode, 0, r.stderr)
        first = tree(self.root + "/dings"); r = run("dings", self.src, "--root", self.root); self.assertEqual(r.returncode, 0, r.stderr)
        self.assertEqual(first, tree(self.root + "/dings"))
        page = open(self.root + "/dings/index.html").read(); self.assertEqual(page.count('<div class="aqua-stable-banner"'), 1)
        self.assertTrue(os.path.exists(self.root + "/dings/out/senders/a.ogg")); self.assertIn("dings-voices-abc1234", open(self.root + "/dings/VERSION").read())
        self.assertEqual(sorted(f for f in os.listdir(self.root) if not f.endswith(".lock")), ["dings"])   # no staging leftovers
    def test_new_version_replaces_old_files(self):
        run("dings", self.src, "--root", self.root); os.remove(self.src + "/out/senders/a.ogg"); open(self.src + "/out/senders/b.ogg", "wb").write(b"OggS")
        run("dings", self.src, "--root", self.root); self.assertEqual(os.listdir(self.root + "/dings/out/senders"), ["b.ogg"])
    def test_refuses_a_dir_that_is_not_a_built_page(self):
        r = run("dings", self.t.name, "--root", self.root); self.assertNotEqual(r.returncode, 0); self.assertFalse(os.path.exists(self.root + "/dings"))
    def test_failed_publish_keeps_the_previous_page(self):
        run("dings", self.src, "--root", self.root); before = tree(self.root + "/dings")
        os.remove(self.src + "/index.html"); self.assertNotEqual(run("dings", self.src, "--root", self.root).returncode, 0); self.assertEqual(before, tree(self.root + "/dings"))
    def test_banners_added_once_to_old_dirs_and_nothing_deleted(self):
        run("dings", self.src, "--root", self.root)
        for d, body in (("dings-voices-old", "<html><body><p>old</p></body></html>"), ("trial-1", "<p>no body tag</p>")):
            os.makedirs(f"{self.root}/{d}"); open(f"{self.root}/{d}/index.html", "w").write(body)
        os.makedirs(self.root + "/aqua-ear-examples"); open(self.root + "/aqua-ear-examples/x.png", "wb").write(b"p")
        for _ in range(2): self.assertEqual(run("banners", "--root", self.root).returncode, 0)
        for d in ("dings-voices-old", "trial-1", "aqua-ear-examples"):
            p = open(f"{self.root}/{d}/index.html").read(); self.assertEqual(p.count('<div class="aqua-stable-banner"'), 1); self.assertIn("/dings/", p)
        self.assertIn("old", open(self.root + "/dings-voices-old/index.html").read()); self.assertTrue(os.path.exists(self.root + "/aqua-ear-examples/x.png"))
        self.assertEqual(open(self.root + "/dings/index.html").read().count("Old per-version"), 0)   # the stable page is not an old page
    def test_ear_index_lists_runs(self):
        e = os.path.join(self.t.name, "ear-src"); os.makedirs(e + "/void-x"); open(e + "/void-x/compare.png", "wb").write(b"p")
        self.assertEqual(run("ear", e, "--root", self.root).returncode, 0); p = open(self.root + "/ear/index.html").read(); self.assertIn("void-x/compare.png", p)
if __name__ == "__main__": unittest.main()
