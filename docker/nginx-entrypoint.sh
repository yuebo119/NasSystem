#!/bin/sh
# =============================================================================
# NAS System - Nginx 入口脚本
# =============================================================================
# 解决两个部署断点：
#
# 1. 证书缺失时 nginx 起不来。nginx.conf 里 ssl_certificate 指向固定路径，
#    文件不存在时 nginx 直接以 "cannot load certificate" 退出。首次部署或
#    忘挂 certs 目录就会陷入 "起不来 → 没法进容器补证书" 的死循环。
#    这里在启动前检查，缺证书就生成自签证书兜底，让服务先能跑起来。
#
# 2. ACME 续期路径不成立。nginx.conf 把 /.well-known/acme-challenge/ 的
#    root 指向 /var/www/certbot，但 docker-compose 没有为该路径挂卷，
#    certbot 写进去的文件 nginx 看不到，HTTP-01 验证永远失败。
#    该卷映射已在 docker-compose.yml 补上；本脚本只负责证书兜底。
#
# 自签证书仅用于"服务可启动"，浏览器会告警。生产请把真实证书放进
# ./certs 目录（挂载到 /etc/nginx/certs），本脚本检测到已存在就直接跳过。
set -eu

CERT_DIR="${NGINX_CERT_DIR:-/etc/nginx/certs}"
CERT_FILE="$CERT_DIR/fullchain.pem"
KEY_FILE="$CERT_DIR/privkey.pem"

if [ -f "$CERT_FILE" ] && [ -f "$KEY_FILE" ]; then
    echo "[nginx-entrypoint] 检测到已有证书，跳过生成"
else
    echo "[nginx-entrypoint] 未找到证书，生成自签证书（仅保证服务可启动，浏览器会告警）"
    mkdir -p "$CERT_DIR"
    openssl req -x509 -nodes -newkey rsa:2048 \
        -days 365 \
        -keyout "$KEY_FILE" \
        -out "$CERT_FILE" \
        -subj "/C=CN/ST=Local/L=Local/O=NASSystem/CN=localhost" \
        >/dev/null 2>&1
    chmod 600 "$KEY_FILE"
    echo "[nginx-entrypoint] 自签证书已生成于 $CERT_DIR"
    echo "[nginx-entrypoint] 生产环境请将真实证书放入该目录后重启"
fi

exec nginx -g 'daemon off;'
