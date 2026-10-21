/**
 * SignalR 实时连接（ITM-N5 收尾：把"装了没接"的 signalr 依赖接活）。
 * 全局单例连接；组件只负责按目录订阅/退订与事件处理。
 */
import { HubConnectionBuilder, HttpTransportType, LogLevel } from '@microsoft/signalr'
import type { HubConnection } from '@microsoft/signalr'

let connection: HubConnection | null = null
let startPromise: Promise<void> | null = null

/** 获取（惰性创建并启动）全局 SignalR 连接 */
export function getHubConnection(): HubConnection {
  if (connection) return connection

  connection = new HubConnectionBuilder()
    .withUrl('/hubs/nas', {
      // WebSocket 无法携带 Authorization 头，后端 OnMessageReceived 白名单 /hubs 从 query 取令牌；
      // 工厂每次调用时实时读取——令牌刷新后重连自动携带新值，避免闭包捕获过期常量
      accessTokenFactory: () => localStorage.getItem('token') ?? '',
      skipNegotiation: true,
      transport: HttpTransportType.WebSockets,
    })
    .configureLogging(process.env.NODE_ENV === 'production' ? LogLevel.Warning : LogLevel.Information)
    .withAutomaticReconnect()
    .build()

  // 连接走到终态断开时清掉 startPromise：包括 withAutomaticReconnect 的
  // 重连放弃与显式 stop()。此时 state 已回 Disconnected，下一次
  // ensureHubStarted 必须重新 start()。缺这一条，初次 start 成功后一直
  // 处于 resolved 的 startPromise 会让 ensureHubStarted 直接 await 短路
  // 返回，连接永久停在断开态而无人重启。
  // 安全性依据 signalr 源码：onclose 只在终态触发——重连窗口内 state 是
  // Reconnecting、走 _reconnect 分支，onclose 不 firing（HubConnection.js
  // 的 _close/_reconnect），所以这个重置不会把在途重连打断
  connection.onclose(() => {
    startPromise = null
  })

  return connection
}

/** 确保连接已启动（幂等，可重复调用） */
export async function ensureHubStarted(): Promise<void> {
  const conn = getHubConnection()
  if (conn.state === 'Connected') return
  if (!startPromise) {
    startPromise = conn.start().catch((err) => {
      startPromise = null
      throw err
    })
  }
  await startPromise
}

/**
 * 订阅目录变更：进入目录时调用；返回清理函数供离开时退订。
 * onFileChanged 的 path 为发生变更的文件完整虚拟路径。
 */
export async function subscribeDirectory(
  path: string,
  onFileChanged: (changedPath: string, changeType: string) => void,
): Promise<() => void> {
  const conn = getHubConnection()
  await ensureHubStarted()
  await conn.invoke('SubscribeToPath', path)
  const handler = (payload: { path?: string; changeType?: string }) => {
    if (payload?.path) onFileChanged(payload.path, payload.changeType ?? 'Modified')
  }
  conn.on('FileChanged', handler)

  return async () => {
    conn.off('FileChanged', handler)
    try {
      await conn.invoke('UnsubscribeFromPath', path)
    } catch {
      /* 连接已断开时退订失败可忽略——重连后会重新订阅当前目录 */
    }
  }
}

/**
 * 订阅全局文件变更：任何目录的写操作都推给订阅者（Dashboard 等聚合
 * 视图用——它们没有"当前目录"概念，只订目录组收不到深层变更）。
 * 返回清理函数供离开时退订
 */
export async function subscribeAllChanges(
  onFileChanged: (changedPath: string, changeType: string) => void,
): Promise<() => void> {
  const conn = getHubConnection()
  await ensureHubStarted()
  await conn.invoke('SubscribeToAll')
  const handler = (payload: { path?: string; changeType?: string }) => {
    if (payload?.path) onFileChanged(payload.path, payload.changeType ?? 'Modified')
  }
  conn.on('FileChanged', handler)

  return async () => {
    conn.off('FileChanged', handler)
    try {
      await conn.invoke('UnsubscribeFromAll')
    } catch {
      /* 连接已断开时退订失败可忽略 */
    }
  }
}

/**
 * 监听底层连接状态：connected 变化时回调。让页面能把"实时同步已断开"
 * 显示出来（否则掉线是静默的，用户误以为外部变更还会自动出现）。
 * 连接是全局单例且 onclose/onreconnecting 无法 off，故钩子只在模块级
 * 挂一次，页面按需增删自己的监听器。返回取消监听函数
 */
const stateListeners = new Set<(connected: boolean) => void>()
let stateHooked = false

function ensureStateHooked(conn: HubConnection) {
  if (stateHooked) return
  stateHooked = true
  conn.onclose(() => stateListeners.forEach(l => l(false)))
  conn.onreconnecting(() => stateListeners.forEach(l => l(false)))
  conn.onreconnected(() => stateListeners.forEach(l => l(true)))
}

export function watchHubConnectionState(
  onChange: (connected: boolean) => void,
): () => void {
  const conn = getHubConnection()
  ensureStateHooked(conn)
  stateListeners.add(onChange)
  onChange(conn.state === 'Connected')
  return () => {
    stateListeners.delete(onChange)
  }
}

/**
 * 关闭全局连接并复位单例状态：登出与 401 会话失效时调用。
 *
 * 不清的后果：旧 HubConnection 实例带着已失效的令牌留在模块里，下一个
 * 登录复用它；且 onclose 等模块级回调与 stateListeners 会跨会话累积。
 * stop() 是尽力而为——连接已断开或从未启动成功时它可能 reject，这里吞掉
 * 不外抛：登出与 401 的处理链不该被"关连接"这一步拖失败
 */
export async function stopHubConnection(): Promise<void> {
  const conn = connection
  // 先摘单例再 await stop()：stop 是异步的，这期间若有新的订阅进来，
  // getHubConnection() 应新建连接而不是拿到正在关闭的旧实例
  connection = null
  startPromise = null
  // stateHooked 必须一起复位：它标记"当前连接已挂过模块级 state 钩子"。
  // 不复位的话，stop 之后新建的连接会被 ensureStateHooked 直接跳过，
  // 页面再也收不到连接状态变化（重新登录后实时状态永远显示已断开）
  stateHooked = false
  if (conn) {
    try {
      // stop() 触发 onclose，模块级 state 钩子会照常把 false 推给各页面
      // 的监听器，UI 上的"实时同步已断开"即时可见
      await conn.stop()
    } catch {
      /* 连接已断开或未启动成功时 stop() 可能 reject；关闭流程尽力而为 */
    }
  }
  // 兜底清空：stop() 抛错的路径上 onclose 未必走完，防止监听器跨会话残留
  stateListeners.clear()
}
