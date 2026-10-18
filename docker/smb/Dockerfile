# =============================================================================
# NAS System - docker/smb/Dockerfile
# SMB sidecar：把 /app/storage 共享为 SMB/CIFS 网络磁盘
# =============================================================================
# 为什么是 sidecar 而不是应用内实现：.NET 无成熟 SMB 服务端库，进程内实现
# 不现实。sidecar 共享同一命名卷是容器部署下的标准解法。
#
# 边界（ROADMAP §2 设计红线）：SMB 是管理员直通模式，旁路应用层的配额检查、
# 软删除与 SignalR 推送。面向"把 NAS 当网络磁盘直接用"的场景，网页端的
# 治理能力不覆盖这条通道，文档中已明示。
#
# 以 root 运行是 samba 的硬性要求（绑定 445 端口后按 valid users 切换身份），
# 共享卷上的实际文件属主通过 force user 固定为 UID 1000 的 nasuser，
# 与主应用容器（Dockerfile 中 useradd -u 1000）对齐。

FROM ubuntu:24.04

RUN apt-get update && apt-get install -y --no-install-recommends \
    samba \
    smbclient \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /app/storage

COPY entrypoint.sh /usr/local/bin/entrypoint.sh
RUN chmod +x /usr/local/bin/entrypoint.sh

# 共享卷挂载点与主应用容器一致
VOLUME ["/app/storage"]

EXPOSE 445 139

ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
