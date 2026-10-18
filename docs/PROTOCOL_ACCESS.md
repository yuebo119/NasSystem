# 协议层访问指南（WebDAV / SMB）

> ROADMAP Phase 0 交付物。协议层让 NAS 从"网页应用"变成可挂载的网络存储：
> WebDAV 走应用层（配额、软删除、实时推送全部生效），SMB 走 sidecar 直通
> （管理员场景，旁路应用层治理，边界见下文）。

## 一、WebDAV（应用层通道，推荐）

地址：`http://<服务器地址>:6050/dav`

认证：HTTP Basic，用户名密码与应用账号完全相同（登录网页端的那一套）。

### Windows（资源管理器）

1. 此电脑 → 映射网络驱动器 → 输入 `http://<地址>:6050/dav`
2. 勾选"使用其他凭据连接"，输入应用账号
3. 大文件（超过 500MB）可直接拖入：WebDAV 通道单独放开网页端的 500MB 上传上限，配额检查仍然生效

### macOS（Finder）

访达 → 前往 → 连接服务器（Cmd+K）→ 输入 `http://<地址>:6050/dav`，选择注册用户。

### Linux（davfs2）

```bash
sudo apt install davfs2
sudo mkdir -p /mnt/nas
sudo mount -t davfs http://<地址>:6050/dav /mnt/nas   # 提示时输应用账号
# 持久化：把凭据写入 /etc/davfs2/secrets，参考 davfs2 手册
```

### iOS / Android（文件 App / 第三方客户端）

iOS 文件 App → 连接服务器 → 选 WebDAV。安卓可用 Solid Explorer、CX 文件管理器等任意 WebDAV 客户端。

### 能力与边界

| 能力 | 状态 |
|---|---|
| 浏览、上传、下载、新建文件夹、重命名、移动、复制 | 支持 |
| 删除 | 软删除进回收站，可在网页端恢复 |
| 配额检查、隐藏文件过滤、SignalR 实时推送 | 生效（与网页端同口径） |
| 文件锁（LOCK/UNLOCK，class 2） | 不支持：Office 类客户端只能只读打开，资源管理器与 Finder 不受影响 |
| 大文件 | 无 500MB 限制，配额内可传任意大小 |

安全说明：Basic 认证的凭据是 Base64 编码不是加密，公网部署必须走 HTTPS（参考 nginx + 证书配置）。同一客户端 5 分钟内 10 次认证失败会被锁定 5 分钟，防暴力破解。

## 二、SMB（sidecar 直通，管理员场景）
SMB 通道面向"把 NAS 当传统网络磁盘直接用"的场景：监控录像写入、剪辑工作站直连大文件等。**该通道旁路应用层治理**：配额不检查、删除不进回收站（物理删除）、网页端无实时刷新提示。日常使用走 WebDAV。

### 启动

```bash
# 默认 compose up 不带 SMB，需显式启用 profile
NAS_SMB_PASSWORD='设置一个强密码' docker compose --profile smb up -d
```

参数：

| 项 | 值 |
|---|---|
| 共享名 | `\\<服务器地址>\nas` |
| 用户名 | `nasadmin`（固定，见 docker/smb/smb.conf） |
| 密码 | 环境变量 `NAS_SMB_PASSWORD`（未设置时 entrypoint 用 nasadmin 兜底并告警，仅限本地体验） |
| 挂载点 | `/app/storage`，与 WebDAV、网页端同一物理目录 |

### 边界声明

1. SMB 账号体系独立：`nasadmin` 在 samba 自有密码库里，与应用账号（SQLite）不互通，改名改密只影响 SMB
2. 文件属主固定为 UID 1000 的 `nasuser`（主应用容器同 UID），两侧对齐由 entrypoint 启动时校验，不一致直接拒绝启动
3. Windows 挂载 `\\<地址>\nas` 后此电脑里出现网络位置，凭据选"记住我"可免重复输入

## 三、两条通道的选择

日常从网页端、手机访问，用 WebDAV：治理能力齐全，删除可恢复。需要传统网络磁盘语义（大块连续写、第三方软件直写）时用 SMB，心里清楚它不过应用层。协议层与网页端共享同一存储目录，任一侧写入的文件另一侧立即可见（SMB 侧因无事件推送，网页端需手动刷新或点扫描同步）。

## 四、外网访问（免公网 IP）

没有公网 IP 也能从外网访问。设置页（管理员）的「外网访问」区块按方案生成配置与操作步骤，最后一公里在你自己的基础设施上完成，本项目不运营任何中继服务。

| 方案 | 适合场景 | 要点 |
|---|---|---|
| Cloudflare Tunnel | 个人/家庭首选 | 免公网 IP、免开端口，HTTPS 由 Cloudflare 边缘终止，免费额度覆盖个人场景 |
| frp | 有公网服务器的人 | 自建或租用一台有公网 IP 的 frps，NAS 常驻内网连出去 |
| Tailscale | 多设备组网 | 装客户端入网即虚拟内网，按需再开 Funnel 暴露公网 |

共同前提：无论哪个方案，外网访问都必须开 HTTPS（Cloudflare 边缘自动终止；frp 建议前置 nginx 或套 Cloudflare）。Basic 认证的凭据是 Base64 编码不是加密，裸 HTTP 外网暴露等同于把密码写在流量里。
