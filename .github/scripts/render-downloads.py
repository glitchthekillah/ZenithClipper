import html
import json
import re
import sys
from pathlib import Path
from urllib.parse import quote

repo, index_path = sys.argv[1:]
release = json.load(sys.stdin)
tag = release["tag_name"]
if release["draft"] or release["prerelease"] or not re.fullmatch(r"v\d+\.\d+\.\d+", tag):
    sys.exit("Expected a published stable release")

assets = release["assets"]
base = f"https://github.com/{repo}/releases/download/{tag}/"
link = re.compile(r'(data-asset="([^"]+)"\s+href=")[^"]+(")')
version_label = re.compile(r'(<span id="dl-ver">Version )[^<]+(</span>)')
version_badge = re.compile(r'(<span class="vv">)[^<]+(</span>)')
index = Path(index_path)
source = index.read_text(encoding="utf-8")

def update_link(match):
    prefix, suffix, closing = match.groups()
    matches = [asset for asset in assets if asset["name"].endswith(suffix)]
    if len(matches) != 1:
        sys.exit(f"{tag} needs exactly one download matching {suffix}")
    asset = matches[0]
    url = asset["browser_download_url"]
    if asset["state"] != "uploaded" or asset["size"] <= 0 or url != base + quote(asset["name"]):
        sys.exit(f"{tag} has an invalid asset for {suffix}")
    return prefix + html.escape(url, quote=True) + closing

rendered, link_count = link.subn(update_link, source)
required_count = {"ZenithClipper": 6, "Zenith": 5}[repo.split("/")[-1]]
if link_count != required_count:
    sys.exit(f"Expected {required_count} download buttons, found {link_count}")
version = tag[1:]
rendered, label_count = version_label.subn(lambda m: m[1] + version + m[2], rendered)
if label_count != 1:
    sys.exit("Expected one download version label")
rendered = version_badge.sub(lambda m: m[1] + version + m[2], rendered)
index.write_text(rendered, encoding="utf-8")
print(f"Verified {link_count} downloads for {tag}")
