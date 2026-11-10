"""A publish that is killed at ANY filesystem mutation, or read concurrently, never leaves the live dir missing or half-written.
Implementation-agnostic: a sys.audit hook counts every mutating event (rename, mkdir, rmtree, remove, copyfile, ctypes call) and
os._exit()s the publisher at the Nth one; the live dir is then checked against the old and new trees."""
import os, subprocess, sys, tempfile, threading, unittest
HERE = os.path.dirname(os.path.abspath(__file__)); S = os.path.join(HERE, "publish_stable.py")
MUTATING = {"os.rename", "os.mkdir", "os.rmdir", "os.remove", "shutil.rmtree", "shutil.copyfile", "shutil.copymode", "ctypes.call_function", "os.chmod"}
RUNNER = r'''
import sys, os, runpy
kill_at = int(sys.argv[1]); counter = [0]
MUT = %r
def hook(ev, args):
    if ev in MUT:
        counter[0] += 1
        if kill_at and counter[0] == kill_at: os._exit(9)
sys.addaudithook(hook)
sys.argv = [%r, "dings", sys.argv[2], "--root", sys.argv[3]]
try: runpy.run_path(%r, run_name="__main__")
finally:
    if not kill_at: print("EVENTS", counter[0])
''' % (MUTATING, S, S)
def make_src(d, tag):
    os.makedirs(d + "/out/senders"); open(d + "/index.html", "w").write(f"<html><body>VERSION-{tag}</body></html>"); open(d + "/out/senders/a.ogg", "w").write(f"OggS-{tag}" * 5000)
def snapshot(d): return sorted((os.path.relpath(os.path.join(r, f), d), open(os.path.join(r, f)).read()) for r, _, fs in os.walk(d) for f in fs if f != "VERSION")
class T(unittest.TestCase):
    def setUp(self):
        self.t = tempfile.TemporaryDirectory(); self.root = self.t.name + "/aqua"; os.makedirs(self.root)
        self.a, self.b = self.t.name + "/dings-voices-aaa", self.t.name + "/dings-voices-bbb"; make_src(self.a, "A"); make_src(self.b, "B")
        self.run_runner = lambda kill, src: subprocess.run([sys.executable, "-c", RUNNER, str(kill), src, self.root], capture_output=True, text=True)
        self.live = self.root + "/dings"
    def tearDown(self): self.t.cleanup()
    def stamped(self, src):   # what the live dir holds after a clean publish of src
        with tempfile.TemporaryDirectory() as r: self.assertEqual(self.run_runner(0, src).returncode, 0) if False else subprocess.run([sys.executable, S, "dings", src, "--root", r], check=True, capture_output=True); return snapshot(r + "/dings")
    def test_kill_at_every_step_over_an_existing_page_keeps_a_complete_tree(self):
        old, new = self.stamped(self.a), self.stamped(self.b)
        r = self.run_runner(0, self.a); self.assertEqual(r.returncode, 0, r.stderr)
        n = int([l for l in r.stdout.splitlines() if l.startswith("EVENTS")][0].split()[1]); self.assertGreater(n, 5)
        for kill in range(1, n + 1):
            subprocess.run([sys.executable, S, "dings", self.a, "--root", self.root], check=True, capture_output=True)
            self.assertEqual(self.run_runner(kill, self.b).returncode, 9)
            self.assertTrue(os.path.isdir(self.live), f"live dir missing after a kill at mutation {kill} of {n}")
            self.assertIn(snapshot(self.live), (old, new), f"live dir half-written after a kill at mutation {kill} of {n}")
            subprocess.run([sys.executable, S, "dings", self.b, "--root", self.root], check=True, capture_output=True)       # the next run heals
            self.assertEqual(snapshot(self.live), new); self.assertEqual([f for f in os.listdir(self.root) if f.startswith(".stage-") or f.endswith(".old")], [], f"stale staging after healing run (kill {kill})")
    def test_kill_during_the_very_first_publish_never_leaves_a_half_written_page(self):
        new = self.stamped(self.b)
        r = self.run_runner(0, self.b); n = int([l for l in r.stdout.splitlines() if l.startswith("EVENTS")][0].split()[1])
        for kill in range(1, n + 1):
            subprocess.run(["rm", "-rf", self.live]); self.assertEqual(self.run_runner(kill, self.b).returncode, 9)
            if os.path.isdir(self.live): self.assertEqual(snapshot(self.live), new, f"half-written first publish after kill {kill}")
    def test_a_raising_step_keeps_the_old_page_and_cleans_its_staging(self):
        subprocess.run([sys.executable, S, "dings", self.a, "--root", self.root], check=True, capture_output=True); old = snapshot(self.live)
        os.remove(self.b + "/index.html")
        self.assertNotEqual(subprocess.run([sys.executable, S, "dings", self.b, "--root", self.root], capture_output=True).returncode, 0)
        self.assertEqual(snapshot(self.live), old); self.assertEqual([f for f in os.listdir(self.root) if f.startswith(".stage-")], [])
    def test_a_concurrent_reader_never_finds_the_page_missing_or_cut_short(self):
        subprocess.run([sys.executable, S, "dings", self.a, "--root", self.root], check=True, capture_output=True)
        bad, stop = [], threading.Event()
        def reader():
            while not stop.is_set():
                try:
                    idx = open(self.live + "/index.html").read(); ogg = open(self.live + "/out/senders/a.ogg").read()
                    if "VERSION-" not in idx or not (ogg.startswith("OggS-A") or ogg.startswith("OggS-B")) or len(ogg) not in (len("OggS-A") * 5000,): bad.append("partial")
                except OSError as e: bad.append(repr(e))
        th = [threading.Thread(target=reader) for _ in range(4)]; [x.start() for x in th]
        try:
            for i in range(30): subprocess.run([sys.executable, S, "dings", self.b if i % 2 == 0 else self.a, "--root", self.root], check=True, capture_output=True)
        finally: stop.set(); [x.join() for x in th]
        self.assertEqual(bad, [])
if __name__ == "__main__": unittest.main()
