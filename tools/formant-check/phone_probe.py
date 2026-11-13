#!/usr/bin/env python3
"""A machine ear for the tract renders: what phone string a speech recogniser reads out of a clip. A PROXY for 'what was heard', never a
verdict: it is trained on natural speech and reads the vowels of this tract poorly (every isolated vowel came back as /i/), so use it
for the events it separates well (a burst read as /v/ against /b/, a nasal present or missing, a consonant that is only a click).
It reproduced three of Meta's four first listening reports on tract-weak-spots-1 before any fix: ba with burst -> 'v ɑ' (she heard
'fah then bah'), ana -> 'v ɑ m ɑ' (a basketball buzzer, no articulation), nibu -> 'i b u' (both consonants clicks, the /n/ missing).
  phone_probe.py CLIP.wav...
Needs torch, transformers, scipy and the HuggingFace model facebook/wav2vec2-lv-60-espeak-cv-ft (about 1.2 GB, downloaded once into
HF_HOME). The CTC decode reads vocab.json directly, so the phonemizer library is not needed.
"""
import sys, wave, json
import numpy as np
import torch
from scipy.signal import resample_poly
from transformers import Wav2Vec2ForCTC
from huggingface_hub import hf_hub_download

MODEL = "facebook/wav2vec2-lv-60-espeak-cv-ft"


def phones(model, vocab, path):
    with wave.open(path) as w:
        sr = w.getframerate()
        x = np.frombuffer(w.readframes(w.getnframes()), dtype="<i2").astype(np.float32) / 32768
    x = resample_poly(x, 16000, sr).astype(np.float32)
    x = np.concatenate([np.zeros(4000, np.float32), x, np.zeros(4000, np.float32)])  # silence either side, as speech has
    x = (x - x.mean()) / (x.std() + 1e-7)
    with torch.no_grad():
        logits = model(torch.tensor(x)[None]).logits[0]
    ids = logits.argmax(-1).tolist()
    out = [vocab[i] for k, i in enumerate(ids) if (k == 0 or i != ids[k - 1]) and vocab[i] != "<pad>"]
    return " ".join(out)


if __name__ == "__main__":
    model = Wav2Vec2ForCTC.from_pretrained(MODEL).eval()
    vocab = {v: k for k, v in json.load(open(hf_hub_download(MODEL, "vocab.json"))).items()}
    for p in sys.argv[1:]:
        print(f"{p.split('/')[-1]:24s} | {phones(model, vocab, p)}")
