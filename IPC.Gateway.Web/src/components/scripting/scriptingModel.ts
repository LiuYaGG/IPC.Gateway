import type {
  GatewayScriptDefinition,
  ScriptDatabaseConnection,
  ScriptDatabaseTarget
} from '../../scriptingApi'

export interface ScriptTagOption {
  value: string
  label: string
}

export const scriptExample = `// 读取点位并写入已配置的数据库目标；脚本中不能直接执行 SQL。
var temperature = Tags.ReadDouble("Channel/Device/Group/Temperature");
var receipt = await Database.InsertAsync("target-id", new
{
    Temperature = temperature,
    CollectedAt = UtcNow
});
Log.Information($"写入请求已进入队列：{receipt.RequestId}");
return temperature;`

export function createScript(): GatewayScriptDefinition {
  return {
    id: createId('script'),
    name: '新脚本',
    description: '',
    enabled: true,
    triggerType: 'Manual',
    intervalSeconds: 60,
    triggerTagPath: '',
    tagChangeMode: 'Any',
    debounceMilliseconds: 500,
    timeoutSeconds: 5,
    sourceCode: scriptExample
  }
}

export function createConnection(): ScriptDatabaseConnection {
  return {
    id: createId('db'),
    name: '新数据库连接',
    provider: 'SqlServer',
    connectionString: '',
    enabled: true,
    connectionTimeoutSeconds: 10
  }
}

export function createTarget(connectionId = ''): ScriptDatabaseTarget {
  return {
    id: createId('target'),
    name: '新写入目标',
    connectionId,
    schema: '',
    table: '',
    enabled: true,
    allowInsert: true,
    allowUpdate: false,
    allowedColumns: [],
    keyColumns: [],
    maxAffectedRows: 1
  }
}

export function cloneValue<T>(value: T): T {
  return JSON.parse(JSON.stringify(value)) as T
}

export function splitColumns(value: string): string[] {
  return value
    .split(/[,，\n]/)
    .map(item => item.trim())
    .filter((item, index, all) => Boolean(item) && all.indexOf(item) === index)
}

function createId(prefix: string): string {
  const random = typeof crypto !== 'undefined' && 'randomUUID' in crypto
    ? crypto.randomUUID().replaceAll('-', '')
    : `${Date.now()}${Math.random().toString(16).slice(2)}`
  return `${prefix}-${random.slice(0, 16)}`
}
