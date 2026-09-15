#!/bin/bash
# Manual VM setup. Does not restart or deploy the API.
set -euo pipefail
[[ "$(id -u)" == 0 ]] || { echo "Run with sudo." >&2; exit 1; }
SCRIPT_DIR=$(cd -- "$(dirname -- "$0")" && pwd)
getent passwd sgsapi >/dev/null
getent group sgsapi >/dev/null
[[ -x /etc/init.d/smartgridsuite-api ]] || { echo "Expected SysV service is missing." >&2; exit 1; }
for executable in /usr/bin/sudo /usr/bin/flock /usr/bin/nohup /usr/bin/setsid /usr/sbin/service /usr/sbin/visudo; do
    [[ -x "$executable" ]] || { echo "Missing: $executable" >&2; exit 1; }
done
command -v python3 >/dev/null
install -d -o root -g sgsapi -m 0750 /etc/smartgridsuite
install -o root -g root -m 0755 "$SCRIPT_DIR/smartgridsuite-restart-api" /usr/local/sbin/smartgridsuite-restart-api
sudoers_tmp=$(mktemp /etc/sudoers.d/sgs-maintenance.XXXXXX)
trap 'rm -f -- "$sudoers_tmp"' EXIT
# Empty argument list restricts sudo to this one no-argument invocation.
printf '%s\n' 'sgsapi ALL=(root) NOPASSWD: /usr/local/sbin/smartgridsuite-restart-api ""' >"$sudoers_tmp"
chmod 0440 "$sudoers_tmp"
/usr/sbin/visudo -cf "$sudoers_tmp"
install -o root -g root -m 0440 "$sudoers_tmp" /etc/sudoers.d/smartgridsuite-maintenance
# Password is read from the terminal, not process arguments or shell history.
python3 - <<'PY'
import base64, getpass, grp, hashlib, json, os, secrets, tempfile
password = getpass.getpass("New shared API restart password (12-256 characters): ")
confirmation = getpass.getpass("Confirm password: ")
if password != confirmation or not 12 <= len(password) <= 256:
    raise SystemExit("Passwords must match and contain 12-256 characters. No password was saved.")
salt = secrets.token_bytes(16)
digest = hashlib.pbkdf2_hmac("sha256", password.encode("utf-8"), salt, 210000)
verifier = "PBKDF2-SHA256:210000:" + base64.b64encode(salt).decode() + ":" + base64.b64encode(digest).decode()
path = "/etc/smartgridsuite/maintenance.json"
data = {}
if os.path.exists(path):
    with open(path, encoding="utf-8") as source:
        data = json.load(source)
data["AdminMaintenance"] = {"RestartEnabled": True, "RestartPasswordHash": verifier}
fd, temporary = tempfile.mkstemp(prefix=".maintenance-", dir="/etc/smartgridsuite")
try:
    os.fchmod(fd, 0o640)
    os.fchown(fd, 0, grp.getgrnam("sgsapi").gr_gid)
    with os.fdopen(fd, "w", encoding="utf-8") as target:
        json.dump(data, target, indent=2)
        target.write("\n")
    os.replace(temporary, path)
finally:
    if os.path.exists(temporary):
        os.unlink(temporary)
print("Restart password verifier saved. No API restart was performed.")
PY
echo "Setup complete. Deploy the updated API normally to load this feature."
