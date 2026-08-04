#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Despliegue y control remoto AppInWhats en servidor Debian vía SSH/SFTP."""

import argparse
import json
import os
import sys
import tarfile
import tempfile
import time

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
CONFIG_PATH = os.path.join(HERE, "server.config.json")
KEY_PATH = os.path.join(HERE, ".aiw_server_key")
PUB_PATH = KEY_PATH + ".pub"

EXCLUDE_DIRS = {
    "node_modules", ".git", "dist", ".angular", "coverage",
    ".data", "tmp", "temp", "__pycache__", ".idea", ".vscode"
}
EXCLUDE_FILES = {".aiw-launcher-session", ".DS_Store"}


def log(msg):
    print(msg, flush=True)


def ensure_paramiko():
    try:
        import paramiko  # noqa: F401
        return
    except ImportError:
        log("Instalando paramiko…")
        import subprocess
        subprocess.check_call([sys.executable, "-m", "pip", "install", "--user", "paramiko"])
        import paramiko  # noqa: F401


def load_config():
    if not os.path.isfile(CONFIG_PATH):
        example = os.path.join(HERE, "server.config.example.json")
        raise SystemExit(
            "Falta launcher/server.config.json.\n"
            "Copia server.config.example.json y rellena host/usuario/password."
        )
    with open(CONFIG_PATH, "r", encoding="utf-8") as f:
        cfg = json.load(f)
    for key in ("host", "user", "password", "remoteRoot"):
        if not cfg.get(key):
            raise SystemExit("Config incompleta: falta '%s'" % key)
    cfg.setdefault("port", 22)
    cfg.setdefault("frontendPort", 1231)
    cfg.setdefault("backendPort", 3001)
    return cfg


def ensure_local_key():
    if os.path.isfile(KEY_PATH) and os.path.isfile(PUB_PATH):
        return
    log("Generando clave SSH local…")
    ensure_paramiko()
    from paramiko import RSAKey
    key = RSAKey.generate(2048)
    key.write_private_key_file(KEY_PATH)
    with open(PUB_PATH, "w", encoding="utf-8") as f:
        f.write("%s %s AppInWhats-DevBuild\n" % (key.get_name(), key.get_base64()))
    try:
        os.chmod(KEY_PATH, 0o600)
    except Exception:
        pass


def connect(cfg, prefer_key=True):
    ensure_paramiko()
    import paramiko
    client = paramiko.SSHClient()
    client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    kwargs = {
        "hostname": cfg["host"],
        "port": int(cfg.get("port", 22)),
        "username": cfg["user"],
        "timeout": 30,
        "allow_agent": False,
        "look_for_keys": False,
    }
    if prefer_key and os.path.isfile(KEY_PATH):
        try:
            client.connect(key_filename=KEY_PATH, **kwargs)
            return client
        except Exception as ex:
            log("Clave SSH no aceptada aún (%s). Usando password…" % ex)
            client.close()
            client = paramiko.SSHClient()
            client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    client.connect(password=cfg["password"], **kwargs)
    return client


def run(client, cmd, check=True, use_pty=True):
    log("$ " + cmd)
    stdin, stdout, stderr = client.exec_command(cmd, get_pty=use_pty)
    out = stdout.read().decode("utf-8", "replace")
    err = stderr.read().decode("utf-8", "replace")
    code = stdout.channel.recv_exit_status()
    if out.strip():
        print(out, end="" if out.endswith("\n") else "\n", flush=True)
    if err.strip():
        print(err, end="" if err.endswith("\n") else "\n", flush=True)
    if check and code != 0:
        raise SystemExit("Comando remoto falló (%s): %s" % (code, cmd))
    return code, out, err


def remote_which(client, name):
    _, out, _ = run(
        client,
        "command -v %s 2>/dev/null || true" % name,
        check=False,
        use_pty=False,
    )
    lines = [ln.strip() for ln in (out or "").splitlines() if ln.strip()]
    if not lines:
        return ""
    path = lines[-1]
    if path.startswith("$") or " " in path:
        return ""
    return path


def ensure_node_npm(client):
    node = remote_which(client, "node")
    npm = remote_which(client, "npm")

    if node:
        run(client, "node -v", check=False, use_pty=False)
    if npm:
        run(client, "npm -v", check=False, use_pty=False)

    if node and npm:
        log("Node/npm OK (%s, %s)" % (node, npm))
        return

    if not node:
        log("Node no encontrado. Intentando instalar Node.js 20 LTS…")
        run(
            client,
            "export DEBIAN_FRONTEND=noninteractive; "
            "apt-get update -y && "
            "(curl -fsSL https://deb.nodesource.com/setup_20.x | bash -) && "
            "apt-get install -y nodejs",
            check=False,
        )
        node = remote_which(client, "node")
        npm = remote_which(client, "npm")

    if node and not npm:
        log("Hay Node pero falta npm. Instalando npm…")
        # Evita repos rotos (p.ej. MariaDB 404) que bloquean apt-get update
        run(
            client,
            "export DEBIAN_FRONTEND=noninteractive; "
            "mkdir -p /root/aiw-apt-bak; "
            "for f in /etc/apt/sources.list.d/*mariadb* /etc/apt/sources.list.d/*MariaDB*; do "
            "  [ -f \"$f\" ] && mv \"$f\" /root/aiw-apt-bak/ 2>/dev/null || true; "
            "done; "
            "apt-get update -y && apt-get install -y npm",
            check=False,
        )
        npm = remote_which(client, "npm")
        if not npm:
            log("apt no pudo instalar npm. Probando instalador oficial…")
            run(
                client,
                "curl -fsSL https://www.npmjs.com/install.sh -o /tmp/npm-install.sh && "
                "chmod +x /tmp/npm-install.sh && "
                "bash /tmp/npm-install.sh",
                check=False,
            )
            npm = remote_which(client, "npm")
        if not npm:
            # Último recurso: npm embebido vía corepack (Node 16.10+)
            run(client, "corepack enable && corepack prepare npm@10.8.2 --activate", check=False)
            npm = remote_which(client, "npm")
        if not npm:
            # Descarga binario node+npm oficial y lo pone en PATH
            log("Instalando Node 20 oficial (incluye npm)…")
            run(
                client,
                "cd /tmp && "
                "curl -fsSLO https://nodejs.org/dist/v20.18.1/node-v20.18.1-linux-x64.tar.xz && "
                "tar -xJf node-v20.18.1-linux-x64.tar.xz -C /usr/local --strip-components=1 && "
                "hash -r; command -v node; command -v npm; node -v; npm -v",
                check=False,
            )
            node = remote_which(client, "node")
            npm = remote_which(client, "npm")

    if not node or not npm:
        raise SystemExit(
            "En el servidor falta Node.js y/o npm tras intentar instalarlos.\n"
            "Instala manualmente Node 20 LTS + npm y vuelve a ejecutar.\n"
            "node=%s npm=%s" % (node or "(no)", npm or "(no)")
        )

    run(client, "node -v && npm -v", check=False)
    log("Node/npm listos.")


def install_authorized_key(client):
    ensure_local_key()
    with open(PUB_PATH, "r", encoding="utf-8") as f:
        pub = f.read().strip()
    run(client, "mkdir -p ~/.ssh && chmod 700 ~/.ssh")
    # Idempotente: solo añade si no existe
    escaped = pub.replace("'", "'\"'\"'")
    run(
        client,
        "grep -qxF '%s' ~/.ssh/authorized_keys 2>/dev/null || "
        "echo '%s' >> ~/.ssh/authorized_keys; chmod 600 ~/.ssh/authorized_keys"
        % (escaped, escaped),
        check=False,
    )
    log("Clave SSH instalada en el servidor.")


def should_exclude(name):
    base = os.path.basename(name.rstrip("/\\"))
    if base in EXCLUDE_DIRS or base in EXCLUDE_FILES:
        return True
    if base.startswith(".git"):
        return True
    return False


def make_tarball(local_dir, label):
    fd, path = tempfile.mkstemp(prefix="aiw-%s-" % label, suffix=".tar.gz")
    os.close(fd)
    log("Empaquetando %s…" % label)
    count = [0]

    def filter_tar(tarinfo):
        parts = tarinfo.name.replace("\\", "/").split("/")
        for p in parts:
            if p in EXCLUDE_DIRS:
                return None
        if os.path.basename(tarinfo.name) in EXCLUDE_FILES:
            return None
        count[0] += 1
        return tarinfo

    with tarfile.open(path, "w:gz") as tar:
        tar.add(local_dir, arcname=label, filter=filter_tar)
    size_mb = os.path.getsize(path) / (1024.0 * 1024.0)
    log("Paquete %s: %.1f MB (%s entradas)" % (label, size_mb, count[0]))
    return path


def sftp_put(client, local_path, remote_path):
    sftp = client.open_sftp()
    try:
        log("Subiendo %s → %s" % (os.path.basename(local_path), remote_path))
        sftp.put(local_path, remote_path)
    finally:
        sftp.close()


def deploy_component(client, cfg, name):
    local = os.path.join(ROOT, name)
    if not os.path.isdir(local):
        raise SystemExit("No existe carpeta local: " + local)
    remote_root = cfg["remoteRoot"].rstrip("/")
    run(client, "mkdir -p '%s' '%s/logs' '%s/run'" % (remote_root, remote_root, remote_root))
    tarball = make_tarball(local, name)
    remote_tar = "/tmp/aiw-%s.tar.gz" % name
    try:
        sftp_put(client, tarball, remote_tar)
        # Extrae preservando node_modules remoto si no viene en el tar (ya excluido)
        run(
            client,
            "mkdir -p '%s' && tar -xzf '%s' -C '%s' && rm -f '%s'"
            % (remote_root, remote_tar, remote_root, remote_tar),
        )
        log("Desplegado: %s" % name)
    finally:
        try:
            os.remove(tarball)
        except Exception:
            pass


def write_remote_scripts(client, cfg):
    remote_root = cfg["remoteRoot"].rstrip("/")
    fe_port = int(cfg["frontendPort"])
    be_port = int(cfg["backendPort"])
    start_sh = r'''#!/bin/bash
set -e
ROOT="{root}"
mkdir -p "$ROOT/logs" "$ROOT/run"
export NG_CLI_ANALYTICS=false
export FORCE_COLOR=0

stop_one() {{
  local name="$1"
  local pidfile="$ROOT/run/${{name}}.pid"
  if [ -f "$pidfile" ]; then
    pid=$(cat "$pidfile" || true)
    if [ -n "$pid" ] && kill -0 "$pid" 2>/dev/null; then
      kill "$pid" 2>/dev/null || true
      sleep 1
      kill -9 "$pid" 2>/dev/null || true
    fi
    rm -f "$pidfile"
  fi
  # Mata por puerto por si quedó huérfano
  if [ "$name" = "backend" ]; then
    fuser -k {be}/tcp 2>/dev/null || true
  else
    fuser -k {fe}/tcp 2>/dev/null || true
  fi
}}

start_backend() {{
  stop_one backend
  cd "$ROOT/backend"
  if [ ! -d node_modules ]; then
    echo "npm install backend…"
    npm install --legacy-peer-deps
  fi
  export PORT={be}
  export AIW_CONFIG_CFG="$ROOT/backend/config.cfg"
  export NODE_OPTIONS="--max-old-space-size=768"
  nohup npx --yes nodemon --watch scr --ext ts --exec "npx --yes ts-node scr/index.ts" \
    > "$ROOT/logs/backend.log" 2>&1 &
  echo $! > "$ROOT/run/backend.pid"
  echo "Backend PID $(cat $ROOT/run/backend.pid) :{be}"
}}

start_frontend() {{
  stop_one frontend
  cd "$ROOT/frontend"
  if [ ! -d node_modules ]; then
    echo "npm install frontend…"
    npm install --legacy-peer-deps
  fi
  export NODE_OPTIONS="--max-old-space-size=4096"
  PROXY="$ROOT/frontend/proxy.conf.native.json"
  nohup npx --yes ng serve --host 0.0.0.0 --port {fe} --proxy-config "$PROXY" \
    > "$ROOT/logs/frontend.log" 2>&1 &
  echo $! > "$ROOT/run/frontend.pid"
  echo "Frontend PID $(cat $ROOT/run/frontend.pid) :{fe}"
}}

case "${{1:-all}}" in
  backend) start_backend ;;
  frontend) start_frontend ;;
  all) start_backend; start_frontend ;;
  *) echo "uso: start.sh [backend|frontend|all]"; exit 1 ;;
esac
'''.format(root=remote_root, be=be_port, fe=fe_port)

    stop_sh = r'''#!/bin/bash
ROOT="{root}"
for name in backend frontend; do
  pidfile="$ROOT/run/${{name}}.pid"
  if [ -f "$pidfile" ]; then
    pid=$(cat "$pidfile" || true)
    if [ -n "$pid" ]; then
      kill "$pid" 2>/dev/null || true
      sleep 1
      kill -9 "$pid" 2>/dev/null || true
    fi
    rm -f "$pidfile"
  fi
done
fuser -k {be}/tcp 2>/dev/null || true
fuser -k {fe}/tcp 2>/dev/null || true
echo "Servicios detenidos"
'''.format(root=remote_root, be=be_port, fe=fe_port)

    status_sh = r'''#!/bin/bash
ROOT="{root}"
for name in backend frontend; do
  pidfile="$ROOT/run/${{name}}.pid"
  if [ -f "$pidfile" ] && kill -0 "$(cat "$pidfile")" 2>/dev/null; then
    echo "$name: OK pid=$(cat "$pidfile")"
  else
    echo "$name: parado"
  fi
done
ss -lntp 2>/dev/null | grep -E ':{be}\s|:{fe}\s' || netstat -lntp 2>/dev/null | grep -E ':{be} |:{fe} ' || true
'''.format(root=remote_root, be=be_port, fe=fe_port)

    sftp = client.open_sftp()
    try:
        run(client, "mkdir -p '%s/run'" % remote_root)
        for name, content in (("start.sh", start_sh), ("stop.sh", stop_sh), ("status.sh", status_sh)):
            remote = "%s/run/%s" % (remote_root, name)
            with sftp.file(remote, "w") as f:
                f.write(content)
            run(client, "chmod +x '%s'" % remote)
        log("Scripts remotos actualizados.")
    finally:
        sftp.close()


def action_bootstrap(cfg):
    client = connect(cfg, prefer_key=False)
    try:
        install_authorized_key(client)
        ensure_node_npm(client)
        write_remote_scripts(client, cfg)
        deploy_component(client, cfg, "backend")
        deploy_component(client, cfg, "frontend")
        run(client, "bash '%s/run/start.sh' all" % cfg["remoteRoot"].rstrip("/"))
        log("")
        log("Listo. Abre: http://%s:%s" % (cfg["host"], cfg["frontendPort"]))
    finally:
        client.close()


def action_deploy(cfg, which):
    client = connect(cfg)
    try:
        ensure_node_npm(client)
        write_remote_scripts(client, cfg)
        if which in ("backend", "both"):
            deploy_component(client, cfg, "backend")
        if which in ("frontend", "both"):
            deploy_component(client, cfg, "frontend")
        remote = cfg["remoteRoot"].rstrip("/")
        if which == "backend":
            run(client, "bash '%s/run/start.sh' backend" % remote)
        elif which == "frontend":
            run(client, "bash '%s/run/start.sh' frontend" % remote)
        else:
            run(client, "bash '%s/run/start.sh' all" % remote)
        log("Cambios aplicados. http://%s:%s" % (cfg["host"], cfg["frontendPort"]))
    finally:
        client.close()


def action_start(cfg):
    client = connect(cfg)
    try:
        ensure_node_npm(client)
        write_remote_scripts(client, cfg)
        run(client, "bash '%s/run/start.sh' all" % cfg["remoteRoot"].rstrip("/"))
        log("Iniciado. http://%s:%s" % (cfg["host"], cfg["frontendPort"]))
    finally:
        client.close()


def action_stop(cfg):
    client = connect(cfg)
    try:
        run(client, "bash '%s/run/stop.sh'" % cfg["remoteRoot"].rstrip("/"), check=False)
    finally:
        client.close()


def action_status(cfg):
    client = connect(cfg)
    try:
        run(client, "bash '%s/run/status.sh'" % cfg["remoteRoot"].rstrip("/"), check=False)
    finally:
        client.close()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "action",
        choices=["bootstrap", "deploy-backend", "deploy-frontend", "deploy-both", "start", "stop", "status"],
    )
    args = parser.parse_args()
    cfg = load_config()
    log("Servidor %s@%s:%s" % (cfg["user"], cfg["host"], cfg.get("port", 22)))

    if args.action == "bootstrap":
        action_bootstrap(cfg)
    elif args.action == "deploy-backend":
        action_deploy(cfg, "backend")
    elif args.action == "deploy-frontend":
        action_deploy(cfg, "frontend")
    elif args.action == "deploy-both":
        action_deploy(cfg, "both")
    elif args.action == "start":
        action_start(cfg)
    elif args.action == "stop":
        action_stop(cfg)
    elif args.action == "status":
        action_status(cfg)


if __name__ == "__main__":
    try:
        main()
    except SystemExit:
        raise
    except Exception as ex:
        log("ERROR: %s" % ex)
        sys.exit(1)
