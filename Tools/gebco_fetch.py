"""Resumable parallel-chunk fetcher for the GEBCO_2026 global GeoTIFF archive.

The origin host throttles per connection (~13 KB/s) but scales near-linearly
with concurrent connections, so the object is pulled as fixed-size byte ranges.
"""

import concurrent.futures
import hashlib
import json
import os
import sys
import time
import urllib.request

URL = ("https://dap.ceda.ac.uk/bodc/gebco/global/gebco_2026/"
       "ice_surface_elevation/geotiff/gebco_2026_geotiff.zip?download=1")
TOTAL = 4241629269
CHUNK = 8 * 1024 * 1024
WORKERS = 24
MAX_TRIES = 40

BASE = "C:/Works/GitHubProjects/HundunWorld/Data/GEBCO_2026"
PARTS = os.path.join(BASE, "parts")
OUT = os.path.join(BASE, "raw", "gebco_2026_geotiff.zip")
LOG = os.path.join(BASE, "fetch.log")
RECEIPT = os.path.join(BASE, "fetch_receipt.json")

count = (TOTAL + CHUNK - 1) // CHUNK


def log(msg):
    line = "%s %s\n" % (time.strftime("%Y-%m-%dT%H:%M:%S"), msg)
    with open(LOG, "a", encoding="utf-8") as fh:
        fh.write(line)
        fh.flush()


def part_path(index):
    return os.path.join(PARTS, "p%05d.part" % index)


def expected_size(index):
    lo = index * CHUNK
    return min(CHUNK, TOTAL - lo)


def fetch(index):
    path = part_path(index)
    want = expected_size(index)
    if os.path.exists(path) and os.path.getsize(path) == want:
        return index, want, "cached"

    lo = index * CHUNK
    hi = lo + want - 1
    tmp = path + ".tmp"
    resume_from = 0
    if os.path.exists(tmp):
        resume_from = min(os.path.getsize(tmp), want)

    last_err = "unknown"
    for attempt in range(1, MAX_TRIES + 1):
        try:
            req = urllib.request.Request(URL)
            # A 200 instead of 206 means Range was ignored; that would write the
            # whole object into a slot reserved for one slice.
            req.add_header("Range", "bytes=%d-%d" % (lo + resume_from, hi))
            req.add_header("User-Agent", "hundunworld-terrain-fetch/1.0")
            with urllib.request.urlopen(req, timeout=180) as resp:
                if resp.status != 206:
                    raise RuntimeError("expected_status_%s" % resp.status)
                head = resp.headers.get("Content-Range", "")
                if head and head.split("/")[-1].strip() != str(TOTAL):
                    raise RuntimeError("content_range_total_mismatch:%s" % head)
                mode = "r+b" if resume_from else "wb"
                with open(tmp, mode) as fh:
                    if resume_from:
                        fh.seek(resume_from)
                    got = resume_from
                    buf = resp.read(256 * 1024)
                    while buf:
                        fh.write(buf)
                        got += len(buf)
                        buf = resp.read(256 * 1024)
                    fh.flush()
                    os.fsync(fh.fileno())
                if got != want:
                    resume_from = got
                    raise RuntimeError("short_read_%s_of_%s" % (got, want))
            os.replace(tmp, path)
            return index, want, "ok"
        except Exception as exc:  # noqa: BLE001 - transient network failures retry
            last_err = "%s:%s" % (type(exc).__name__, exc)
            time.sleep(min(60, 5 * attempt))

    log("chunk_fail idx=%d err=%s" % (index, last_err))
    return index, 0, "fail"


def main():
    os.makedirs(PARTS, exist_ok=True)
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    done = sum(1 for i in range(count)
               if os.path.exists(part_path(i))
               and os.path.getsize(part_path(i)) == expected_size(i))
    log("start chunks=%d already=%d workers=%d" % (count, done, WORKERS))
    t0 = time.time()
    failed = []

    with concurrent.futures.ThreadPoolExecutor(max_workers=WORKERS) as pool:
        futures = [pool.submit(fetch, i) for i in range(count)]
        for fut in concurrent.futures.as_completed(futures):
            index, size, status = fut.result()
            if status == "fail":
                failed.append(index)
            else:
                done += 1
            if done % 24 == 0 or status == "fail":
                elapsed = time.time() - t0
                rate = done * CHUNK / elapsed / 1024 if elapsed > 0 else 0
                remaining = (count - done) * CHUNK / max(rate * 1024, 1) / 60
                log("progress done=%d/%d rate_kb_s=%d eta_min=%d failed=%d"
                    % (done, count, rate, remaining, len(failed)))

    if failed:
        log("abort reason=failed_chunks_%d" % len(failed))
        print("FAILED chunks:", failed[:20], "...total", len(failed))
        return 1

    log("assemble begin parts=%d" % count)
    digest = hashlib.sha256()
    total_written = 0
    with open(OUT + ".tmp", "wb") as out:
        for i in range(count):
            path = part_path(i)
            want = expected_size(i)
            size = os.path.getsize(path)
            if size != want:
                raise RuntimeError("part_%d_size_%s_expected_%s" % (i, size, want))
            with open(path, "rb") as fh:
                while True:
                    block = fh.read(1024 * 1024)
                    if not block:
                        break
                    out.write(block)
                    digest.update(block)
                    total_written += len(block)
    if total_written != TOTAL:
        raise RuntimeError("assembled_%s_expected_%s" % (total_written, TOTAL))
    os.replace(OUT + ".tmp", OUT)

    receipt = {
        "url": URL,
        "bytes": total_written,
        "sha256": digest.hexdigest(),
        "chunks": count,
        "elapsed_sec": round(time.time() - t0, 1),
        "finished_utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
    }
    with open(RECEIPT, "w", encoding="utf-8") as fh:
        json.dump(receipt, fh, indent=2)
    log("assemble done sha256=%s" % receipt["sha256"])
    print(json.dumps(receipt, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
