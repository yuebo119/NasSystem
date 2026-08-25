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

  const token = localStorage.getItem('token') ?? ''
  connection = new HubConnectionBuilder()
    .withUrl('/hubs/nas', {
      // WebSocket 无法携带 Authorization 头，后端 OnMessageReceived 白名单 /hubs 从 query 取令牌
      accessTokenFactory: () => token,
      skipNegotiation: true,
      transport: HttpTransportType.WebSockets,
    })
    .configureLogging(process.env.NODE_ENV === 'production' ? LogLevel.Warning : LogLevel.Information)
    .withAutomaticReconnect()
    .build()

  connection.onreconnected(() => {
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
