"""Queues SightForge Forge jobs (local GPU, no paid service) through Studio's own code, so they appear in Studio.

  python Tools/forge_queue.py model <name> <picture.png> "<a few words for the painted back>" [back.png]
  python Tools/forge_queue.py static <name> <picture.png> "<description>" [height_m]
  python Tools/forge_queue.py edit <name> <picture.png> "<change 1>" ["<change 2>" ...]
  python Tools/forge_queue.py items <name> <reference.png> "<item 1>" ["<item 2>" ...]

Prints the job id; the job runs in the background (one at a time on the graphics card) and writes its results to
%LOCALAPPDATA%\\ClaudeEyesStudio\\Forge\\<job id>."""
import base64
import os
import sys

app = os.path.join(os.environ["LOCALAPPDATA"], "ClaudeEyesStudio", "app")
sys.path.insert(0, app)
from eyeslib import forge  # noqa: E402


def upload(path):
    with open(path, "rb") as f:
        return {"upload": "data:image/png;base64," + base64.b64encode(f.read()).decode()}


def main(argv):
    tool, name, picture = argv[0], argv[1], os.path.abspath(argv[2])
    if tool == "model":
        b = {"tool": "model", "name": name, "front": upload(picture), "paintBack": True, "cloth": True,
             "describe": argv[3] if len(argv) > 3 else ""}
        if len(argv) > 4:
            b["back"] = upload(os.path.abspath(argv[4]))     # a back view of the same character (e.g. from the edit tool)
    elif tool == "static":
        b = {"tool": "static", "name": name, "picture": upload(picture), "describe": argv[3] if len(argv) > 3 else "",
             "faces": 20000, "tex": 2048}
        if len(argv) > 4:
            b["height"] = float(argv[4])
    elif tool == "edit":
        b = {"tool": "edit", "name": name, "picture": upload(picture), "edits": argv[3:], "colours": "free"}
    elif tool == "items":
        b = {"tool": "items", "name": name, "picture": upload(picture), "items": argv[3:], "colours": "auto"}
    else:
        raise SystemExit("tool: model | static | edit | items")
    print("FORGE_QUEUED", forge.start(None, b)["id"], flush=True)


if __name__ == "__main__":
    main(sys.argv[1:])
