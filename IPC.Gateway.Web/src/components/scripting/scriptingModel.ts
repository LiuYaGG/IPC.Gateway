import type {
  GatewayScriptDefinition,
  GatewayScriptType,
  ScriptDatabaseConnection,
  ScriptDatabaseTarget
} from '../../scriptingApi'

export interface ScriptTagOption {
  value: string
  label: string
  dataType: string
  canRead: boolean
  canWrite: boolean
}

export const databaseScriptExample = `// 读取点位并写入已配置的数据库目标；脚本中不能直接执行 SQL。
var temperature = Tags.ReadDouble("Channel/Device/Group/Temperature");
var receipt = await Database.InsertAsync("target-id", new
{
    Temperature = temperature,
    CollectedAt = UtcNow
});
Log.Information($"写入请求已进入队列：{receipt.RequestId}");
return temperature;`

export const tagLinkageScriptExample = `// 读取源点位，并向当前脚本白名单内的目标点位写值。
var sourceValue = Tags.ReadDouble("Channel/Device/Group/SourceTag");
if (sourceValue >= 10)
{
    await Writes.SetAsync("Channel/Device/Group/TargetTag", sourceValue);
}
return sourceValue;`

export const valueTransformScriptExample = `// 输入是只读对象；值处理脚本不能访问点位、数据库、文件或网络。
var value = Input.AsDouble();
var radians = value * Math.PI / 180D;
return Math.Round(Math.Sin(radians), 6);`

export function scriptExampleFor(scriptType: GatewayScriptType) {
  if (scriptType === 'TagLinkage') return tagLinkageScriptExample
  if (scriptType === 'ValueTransform') return valueTransformScriptExample
  return databaseScriptExample
}

export function createScript(scriptType: GatewayScriptType = 'DatabaseWrite'): GatewayScriptDefinition {
  return {
    id: createId('script'),
    name: '新脚本',
    description: '',
    enabled: true,
    scriptType,
    triggerType: 'Manual',
    intervalSeconds: 60,
    triggerTagPath: '',
    tagChangeMode: 'Any',
    debounceMilliseconds: 500,
    timeoutSeconds: 5,
    allowedWriteTagPaths: [],
    maxWritesPerExecution: 20,
    valueTransformScope: 'Both',
    nodeCategory: '处理',
    inputDataType: 'Double',
    outputDataType: 'Double',
    transformTimeoutMilliseconds: 100,
    sourceCode: scriptExampleFor(scriptType)
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
