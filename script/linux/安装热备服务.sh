#!/usr/bin/env bash
set -euo pipefail
fail() { echo "ERROR: $*" >&2; exit 1; }
if [[ ${1:-} == --help ]]; then
  echo 'sudo bash 安装热备服务.sh A|B|C config-root   (from the extracted Linux package)'
  echo 'Gateway A/B: INSTALL_ROOT INSTALL_DIR DATA_DIR DOTNET_PATH START_SERVICE. Standalone C also accepts MEMBER_CONFIG and MEMBER_DATA.'
  echo 'On a new, never provisioned A, START_AS_STANDALONE=1 explicitly enables standalone business until cluster setup.'
  exit
fi
role=${1:?Specify member A, B or C}; [[ $role =~ ^[ABC]$ && $EUID == 0 ]] || fail 'Select a valid role and run as root.'
tools=$(cd -- "$(dirname -- "$0")" && pwd -P)
package=$(realpath -e -- "${PACKAGE_ROOT:-$tools/..}")
cfg=$(realpath -m -- "${2:?Specify a dedicated local configuration directory}")
gateway=${SERVICE_NAME:-ipc-gateway}
[[ $gateway =~ ^[a-zA-Z0-9_-]+$ ]] || fail 'Invalid gateway unit name.'
root=$(realpath -m -- "${INSTALL_ROOT:-/opt/ipc-gateway}")
dotnet=${DOTNET_PATH:-$(command -v dotnet || true)}
[[ -x $dotnet ]] || fail 'Missing .NET runtime.'
json() {
  python3 -c 'import json,sys; d=json.load(open(sys.argv[1],encoding="utf-8-sig"),object_hook=lambda obj:{k.lower():v for k,v in obj.items()}); print(__import__("functools").reduce(lambda x,k:x.get(k.lower(),{}) if isinstance(x,dict) else {},sys.argv[2].split("."),d) or "")' "$1" "$2"
}
registered() {
  local loaded
  loaded=$(systemctl show "$1.service" -p LoadState --value 2>/dev/null || true)
  [[ -n $loaded && $loaded != not-found ]]
}
unit_env() {
  systemctl show "$1" -p Environment --value | python3 -c 'import shlex,sys; print(dict(x.split("=",1) for x in shlex.split(sys.stdin.read()) if "=" in x).get(sys.argv[1],""))' "$2"
}
copy_program() {
  local source=$1 dest=$2
  shift 2
  [[ $source != "$dest" && $source != "$dest/"* && $dest != "$source/"* ]] || fail 'Use separate extraction and application directories.'
  install -d -m 755 "$dest"
  rsync -a --exclude='/Data' --exclude='/logs' --exclude='/appsettings.Production.json' "$@" -- "$source/" "$dest/"
}

validate_path() {
  [[ $1 =~ ^/[a-zA-Z0-9_./:@+-]+$ && $1 != / ]] || fail 'Use a dedicated absolute path without spaces.'
}
validate_path "$root"
validate_path "$dotnet"
validate_path "$cfg"
grant_parent_traversal() {
  local user=$1 path=$2 dir
  local -a parents=()
  dir=$(dirname "$path")
  while [[ $dir != / ]]; do parents=("$dir" "${parents[@]}"); dir=$(dirname "$dir"); done
  for dir in "${parents[@]}"; do
    runuser -u "$user" -- test -x "$dir" ||
      setfacl -m "u:$user:--x" "$dir" ||
      fail "Grant $user traversal of $dir (setfacl/acl)."
  done
}
standalone=${START_AS_STANDALONE:-0}
[[ $standalone == 0 || ( $standalone == 1 && $role == A ) ]] || fail 'START_AS_STANDALONE=1 is only supported on a new A.'
if [[ $standalone == 1 ]]; then
  for old in "$gateway" ipc-gateway ipc-gateway-arbiter ipc-gateway-arbiter-{a,b,c}; do
    if registered "$old"; then fail "Existing service $old prevents standalone installation."; fi
  done
fi
service="ipc-gateway-arbiter-${role,,}"
for old in ipc-gateway-arbiter "ipc-gateway-arbiter-${role,,}"; do
  registered "$old" || continue
  [[ $(systemctl show "$old.service" -p Description --value) == 'IPC Gateway Arbiter' ]] || fail "Unrelated service: $old"
  if [[ $role != C && $old != ipc-gateway-arbiter ]]; then fail "Uninstall standalone $old, preserving its state, before installing the integrated gateway."; fi
  old_config=$(unit_env "$old" IPC_ARBITER_CONFIG)
  [[ -r $old_config ]] || fail "Cannot identify $old. Restore the existing member.json."
  if [[ $(json "$old_config" Arbiter.NodeId) != "$role" ]]; then
    [[ $old == ipc-gateway-arbiter ]] && continue
    fail "Conflicting member identity on $old."
  fi
  [[ $role == C ]] || fail "Uninstall standalone $old, preserving its state, before installing the integrated gateway."
  [[ $service == "ipc-gateway-arbiter-${role,,}" ]] || fail "Two services already exist for $role."
  service=$old
  prior_state=$(unit_env "$old" Arbiter__StateDirectory)
  [[ -n $prior_state ]] || prior_state=$(json "$old_config" Arbiter.StateDirectory)
  if [[ -n $prior_state ]]; then
    [[ -z ${MEMBER_DATA:-} || $(realpath -m -- "$MEMBER_DATA") == "$(realpath -m -- "$prior_state")" ]] || fail 'Existing arbiter state path differs.'
    MEMBER_DATA=$prior_state
  fi
done

if [[ $role == C ]]; then
  config=$(realpath -e -- "${MEMBER_CONFIG:-$cfg/C/member.json}")
  [[ $(json "$config" Arbiter.NodeId) == C ]] || fail 'Member identity differs from C.'
  arbiter=$root/arbiter-C
  [[ -f $package/components/IPC.Gateway.Arbiter/IPC.Gateway.Arbiter.dll ]] || fail 'Missing C member payload.'
  if systemctl is-active --quiet "$service.service"; then fail "Stop $service before updating."; fi
  state=${MEMBER_DATA:-$(json "$config" Arbiter.StateDirectory)}
  [[ $state == /* ]] || fail 'Specify the existing absolute MEMBER_DATA path.'
  if [[ -f $state/identity.json ]]; then
    [[ $(json "$state/identity.json" NodeId) == "$role" && $(json "$state/identity.json" ClusterId) == "$(json "$config" Arbiter.ClusterId)" ]] || fail 'Member data belongs to another node or cluster.'
  fi
  validate_path "$config"
  validate_path "$state"
  [[ $state != "$arbiter" && $state != "$arbiter/"* && $arbiter != "$state/"* ]] || fail 'Keep program and member state separate.'
  prepare_identity() {
    local user=$1 section=$2 file=$3 data=$4 key path dir
    getent group "$user" >/dev/null || groupadd --system "$user"
    id "$user" >/dev/null 2>&1 || useradd --system --gid "$user" --home-dir "$data" --no-create-home --shell /usr/sbin/nologin "$user"
    for key in config CertificatePath CertificatePasswordFile state; do
      case $key in
        config) path=$file ;;
        state) path=$data; [[ -d $path ]] || install -d -m 750 -o "$user" -g "$user" "$path" ;;
        *) path=$(json "$file" "$section.$key"); [[ -n $path ]] || continue
           [[ $path == /* ]] || path="$(dirname "$file")/$path" ;;
      esac
      grant_parent_traversal "$user" "$path"
      if [[ $key == state ]]; then
        runuser -u "$user" -- test -w "$path" || fail "Grant $user access to existing state $path."
      else
        [[ -f $path ]] || fail "Missing $key: $path"
        runuser -u "$user" -- test -r "$path" || setfacl -m "u:$user:r--" "$path" || fail "Grant $user read access to $path (setfacl/acl)."
      fi
    done
  }
  memberuser=$(systemctl show "$service.service" -p User --value 2>/dev/null || true)
  prepare_identity "${memberuser:-ipc-arbiter}" Arbiter "$config" "$state"
  config_dir=$(dirname -- "$config")
  case "$config_dir" in /|/etc|/opt|/usr|/usr/local|/var|/var/lib|/home) fail 'Use a directory dedicated to this member configuration.' ;; esac
  grant_parent_traversal "${memberuser:-ipc-arbiter}" "$config_dir/member.json"
  if ! runuser -u "${memberuser:-ipc-arbiter}" -- test -w "$config_dir"; then
    command -v setfacl >/dev/null || fail "Grant ${memberuser:-ipc-arbiter} write access to $config_dir (install acl/setfacl)."
    setfacl -m "u:${memberuser:-ipc-arbiter}:rwx" "$config_dir"
  fi
  runuser -u "${memberuser:-ipc-arbiter}" -- test -w "$config_dir" || fail "Grant ${memberuser:-ipc-arbiter} write access to the dedicated member directory."
  copy_program "$package/components/IPC.Gateway.Arbiter" "$arbiter"
  if ! registered "$service"; then
    PATH="$(dirname "$dotnet"):$PATH" DATA_DIR="$state" ARBITER_SERVICE="$service" START_SERVICE=0 bash "$tools/发布仲裁客户端.sh" install "$arbiter" "$config"
  else
    [[ $(systemctl show "$service.service" -p Description --value) == 'IPC Gateway Arbiter' ]] || fail "Unrelated service: $service"
    drop="/etc/systemd/system/$service.service.d"; install -d "$drop"
    printf '# IPC Gateway HA installer\n[Service]\nWorkingDirectory=%s\nExecStart=\nExecStart=%s %s/IPC.Gateway.Arbiter.dll\nEnvironment=IPC_ARBITER_CONFIG=%s\nReadWritePaths=%s %s\n' "$arbiter" "$dotnet" "$arbiter" "$config" "$state" "$config_dir" > "$drop/90-ipc-ha.conf"
  fi
  systemctl daemon-reload
  member_diagnostics() {
    systemctl status "$service.service" --no-pager -l >&2 || true
    journalctl -u "$service.service" -n 80 --no-pager -a -o cat >&2 || true
  }
  member_unit="/etc/systemd/system/$service.service"
  member_load_state=$(systemctl show "$service.service" -p LoadState --value 2>/dev/null || true)
  if [[ ! -f $member_unit || $member_load_state != loaded ]]; then
    member_diagnostics
    fail "Member registration failed: expected $member_unit and LoadState=loaded. Member data was retained at $state."
  fi
  if [[ ${START_SERVICE:-0} == 1 ]]; then
    if ! systemctl start "$service.service" || ! systemctl is-active --quiet "$service.service"; then
      member_diagnostics
      fail "Member $service is not active after startup. Inspect the service logs above. Member data was retained at $state."
    fi
    echo "Started C member: $service. State: $state."
  else
    echo "Registered C member: $service. State: $state. Start with: systemctl start $service.service"
  fi
  exit
fi

if [[ -n ${MEMBER_CONFIG:-}${GATEWAY_CONFIG:-}${MEMBER_DATA:-} ]]; then
  fail 'A/B use the dedicated configuration directory. Migrate legacy state before replacing the standalone member.'
fi
if systemctl is-active --quiet "$gateway.service"; then fail "Stop $gateway before updating."; fi
if registered "$gateway"; then [[ $(systemctl show "$gateway.service" -p Description --value) == 'IPC Gateway Edge Service' ]] || fail "Unrelated gateway service."; fi
oldapp=$(systemctl show "$gateway.service" -p WorkingDirectory --value 2>/dev/null || true)
app=$(realpath -m -- "${INSTALL_DIR:-${oldapp:-$root/app}}")
if [[ -n $oldapp && $app != "$(realpath -m -- "$oldapp")" ]]; then fail 'Existing gateway uses another application directory.'; fi
validate_path "$app"
[[ $cfg != "$app" && $cfg != "$app/"* && $app != "$cfg/"* && $app != / ]] || fail 'Use a dedicated identity directory outside the gateway program.'
previous_role=$(unit_env "$gateway" Gateway__EmbeddedArbiter__Role || true)
if [[ -n $previous_role ]]; then
  [[ $previous_role == "$role" && $(unit_env "$gateway" Gateway__EmbeddedArbiter__ConfigRoot) == "$cfg" ]] || fail 'Existing integrated gateway belongs to another role or identity directory.'
elif [[ -d $cfg && -n $(find "$cfg" -mindepth 1 -maxdepth 1 ! -name "$role" ! -name .setup.lock ! -name setup-complete ! -name standalone-before-setup -print -quit) ]]; then
  fail 'The configuration directory contains unrelated entries. Select a dedicated directory.'
fi
if [[ $standalone == 1 ]]; then
  if [[ -d $cfg && -n $(find "$cfg" -mindepth 1 -maxdepth 1 ! -name standalone-before-setup -print -quit) ]]; then fail 'An empty unprovisioned identity root is required.'; fi
  for dir in "$root/ha" "$root/ha-data" "$app/Data/HighAvailability" "${DATA_DIR:-$app/Data}/HighAvailability" "${DATA_DIR:-$app/Data}/ha-gateway-A" /var/lib/ipc-gateway-arbiter /etc/ipc-arbiter /etc/ipc-gateway/ha; do
    if [[ $(realpath -m -- "$dir") != "$cfg" && -d $dir && -n $(find "$dir" -mindepth 1 -maxdepth 1 -print -quit) ]]; then fail "Existing HA data at $dir prevents standalone installation."; fi
  done
  prod="$app/appsettings.Production.json"
  if [[ -f $prod ]]; then
    for field in ClusterId NodeId Endpoints; do legacy=$(json "$prod" "Gateway.HighAvailability.$field"); [[ -z $legacy ]] || fail 'Existing registered gateway identity.'; done
    mode=$(json "$prod" Gateway.HighAvailability.Mode); [[ ${mode,,} != cluster ]] || fail 'Existing clustered gateway configuration.'
  fi
fi
[[ -f $package/payload/IPC.Gateway.WebHost.dll && -f $package/payload/IPC.Gateway.Arbiter.dll && -f $package/payload/wwwroot/index.html ]] || fail 'Incomplete integrated gateway payload.'
if [[ -n ${PACKAGE_ZIP:-} && -f $app/IPC.Gateway.WebHost.dll ]]; then
  updates=${UPDATE_DIR:-$(unit_env "$gateway" Gateway__Maintenance__Updates__UpdateDirectory || true)}
  "$dotnet" "$package/payload/IPC.Gateway.WebHost.dll" --prepare-driver-trust "$(realpath -e -- "$PACKAGE_ZIP")" "$app" "${updates:-${DATA_DIR:-$app/Data}/Updates}" || fail 'Preparing driver trust failed; no program files were replaced.'
fi
# Load each actual native library before changing any files. UOS 2.28 may
# already have a site-compatible library from a previous installation.
excludes=()
for lib in libe_sqlite3.so Drivers/libe_sqlite3.so; do
  if [[ -f $package/payload/$lib ]] && ! python3 -c 'import ctypes,sys; ctypes.CDLL(sys.argv[1]).sqlite3_libversion_number()' "$package/payload/$lib" 2>/dev/null; then
    [[ -f $app/$lib ]] && python3 -c 'import ctypes,sys; ctypes.CDLL(sys.argv[1]).sqlite3_libversion_number()' "$app/$lib" || fail "No compatible $lib. Supply the UOS build before updating."
    excludes+=(--exclude="/$lib")
    echo "Retaining UOS-compatible $lib"
  fi
done
gwuser=$(systemctl show "$gateway.service" -p User --value 2>/dev/null || true)
gwuser=${gwuser:-${SERVICE_USER:-ipc-gateway}}
if ! id "$gwuser" >/dev/null 2>&1; then
  gwgroup=${SERVICE_GROUP:-$gwuser}
  getent group "$gwgroup" >/dev/null || groupadd --system "$gwgroup"
  useradd --system --gid "$gwgroup" --home-dir "${DATA_DIR:-$app/Data}" --no-create-home --shell /usr/sbin/nologin "$gwuser"
fi
install -d -m 700 -o "$gwuser" -g "$(id -gn "$gwuser")" "$cfg"
grant_parent_traversal "$gwuser" "$cfg"
for marker in "$cfg/.setup.lock" "$cfg/setup-complete"; do
  if [[ -e $marker ]]; then
    [[ -f $marker && ! -L $marker ]] || fail 'Invalid local provisioning marker.'
    chown "$gwuser" "$marker"
  fi
done
if [[ -d $cfg/$role ]]; then
  [[ ! -L $cfg/$role ]] || fail 'Refusing a linked role directory.'
  # The optional offline wizard may have created these private files as root.
  find "$cfg/$role" -xdev -exec chown --no-dereference "$gwuser" {} +
fi
runuser -u "$gwuser" -- test -w "$cfg" || fail "Grant $gwuser access through the parent directories of $cfg."
drop="/etc/systemd/system/$gateway.service.d"
install -d "$drop"
printf '# IPC Gateway HA installer\n[Service]\nEnvironment=IPC_GATEWAY_HA_CONFIG=\nEnvironment=Gateway__EmbeddedArbiter__Role=%s\nEnvironment=Gateway__EmbeddedArbiter__ConfigRoot=%s\nReadWritePaths=%s\n' "$role" "$cfg" "$cfg" > "$drop/90-ipc-ha.conf"
systemctl daemon-reload
copy_program "$package/payload" "$app" "${excludes[@]}" --chown="$gwuser:$(id -gn "$gwuser")"
if [[ $standalone == 1 ]]; then
  marker="$cfg/standalone-before-setup"
  if [[ -e $marker || -L $marker ]]; then
    [[ -f $marker && ! -L $marker && $(wc -c < "$marker") -eq 1 && $(cat -- "$marker") == A ]] || fail 'Invalid standalone provisioning marker.'
  else
    runuser -u "$gwuser" -- bash -c 'umask 077; set -C; printf A > "$1"' _ "$marker"
  fi
fi
existing_updates=$(unit_env "$gateway" Gateway__Maintenance__Updates__UpdateDirectory || true)
existing_url=$(unit_env "$gateway" ASPNETCORE_URLS || true)
gateway_data=${DATA_DIR:-}
if [[ -z $gateway_data && -n $existing_updates ]]; then gateway_data=$(dirname -- "$existing_updates"); fi
REGISTER_EXISTING=1 HA_STATE_DIR="$cfg/$role/gateway-state" SERVICE_NAME="$gateway" SERVICE_USER="$gwuser" SERVICE_GROUP="$(id -gn "$gwuser")" \
  DATA_DIR="${gateway_data:-$app/Data}" SERVICE_URL="${SERVICE_URL:-${existing_url:-http://127.0.0.1:5184}}" \
  INSTALL_DIR="$app" DOTNET_PATH="$dotnet" START_SERVICE=0 bash "$tools/发布Linux服务.sh" "$package/payload"
if [[ ${START_SERVICE:-0} == 1 ]]; then systemctl start "$gateway"; fi
echo "Installed integrated gateway $role: $gateway. Identity directory: $cfg."
