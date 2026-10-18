#!/bin/sh
# =============================================================================
# NAS System - docker/smb/entrypoint.sh
# SMB sidecar 启动：建对齐用户、种 smb 密码、以前台跑 smbd
# =============================================================================
set -e

NAS_UID="${NAS_UID:-1000}"
NAS_GID="${NAS_GID:-1000}"
SMB_USER="${SMB_USER:-nasadmin}"

# UID/GID 对齐校验：主应用容器的 nasuser 固定 1000（见根 Dockerfile）。
# 两侧不一致时 sidecar 落盘的文件应用层无权读写，症状是"网页端看不到
# SMB 拷进来的文件"，属于难排查的权限谜团，这里 fail-fast
if [ "$NAS_UID" != "1000" ] || [ "$NAS_GID" != "1000" ]; then
    echo "[smb] FATAL: NAS_UID/NAS_GID 必须为 1000（与主应用容器 nasuser 对齐），当前 ${NAS_UID}/${NAS_GID}" >&2
    exit 1
fi

# 建与主应用同 UID 的属主用户（不存在才建，容器重启时已存在）
if ! getent group nasuser >/dev/null 2>&1; then
    groupadd -r -g "$NAS_GID" nasuser
fi
if ! getent passwd nasuser >/dev/null 2>&1; then
    useradd -r -u "$NAS_UID" -g nasuser -M -s /usr/sbin/nologin nasuser
fi

# 共享卷属主校正：命名卷首次创建时属主可能是 root，
# 不校正则 nasuser 连根目录都进不去
chown -R nasuser:nasuser /app/storage 2>/dev/null || true

# SMB 账号独立于应用账号体系（应用账号在 SQLite 里，samba 有自己的
# passdb 后端，两边不通）。密码从环境变量注入，缺省值仅供本地体验
if [ -z "${NAS_SMB_PASSWORD}" ]; then
    echo "[smb] WARN: NAS_SMB_PASSWORD 未设置，使用默认密码 nasadmin（生产必须显式设置）" >&2
    NAS_SMB_PASSWORD="nasadmin"
fi

printf '%s\n%s\n' "${NAS_SMB_PASSWORD}" "${NAS_SMB_PASSWORD}" \
    | smbpasswd -a -s "${SMB_USER}" >/dev/null 2>&1
smbpasswd -e "${SMB_USER}" >/dev/null 2>&1

echo "[smb] 启动 smbd：共享 \\\\$(hostname)\\nas -> /app/storage（用户 ${SMB_USER}）"
exec smbd --foreground --no-process-group --configfile=/etc/samba/smb.conf
