<template>
  <aside class="flow-properties">
    <a-empty v-if="!node" :description="tx('选择一个节点进行编辑')" />
    <template v-else>
      <div class="flow-properties__header">
        <strong>{{ tx('节点属性') }}</strong>
        <a-button @click="$emit('delete')" type="link" danger>{{ tx('删除') }}</a-button>
      </div>
      <a-form layout="vertical">
        <a-form-item :label="tx('名称')"><gateway-input v-model="node.label" /></a-form-item>
        <a-form-item :label="tx('类型')">
          <gateway-select v-model="node.nodeType">
            <a-select-option :label="tx('质量门控')" value="QualityGate">{{ tx('质量门控') }}</a-select-option>
            <a-select-option :label="tx('窗口统计')" value="SlidingWindow">{{ tx('窗口统计') }}</a-select-option>
            <a-select-option :label="tx('窗口计算')" value="WindowCalculation">{{ tx('窗口计算') }}</a-select-option>
            <a-select-option :label="tx('聚合计算')" value="Aggregation">{{ tx('聚合计算') }}</a-select-option>
            <a-select-option :label="tx('趋势判断')" value="Trend">{{ tx('趋势判断') }}</a-select-option>
            <a-select-option :label="tx('状态机')" value="StateMachine">{{ tx('状态机') }}</a-select-option>
            <a-select-option :label="tx('标签输入')" value="TagInput">{{ tx('标签输入') }}</a-select-option>
            <a-select-option :label="tx('条件')" value="Condition">{{ tx('条件') }}</a-select-option>
            <a-select-option :label="tx('滞回规则')" value="Hysteresis">{{ tx('滞回规则') }}</a-select-option>
            <a-select-option :label="tx('多级告警')" value="MultiLevelAlarm">{{ tx('多级告警') }}</a-select-option>
            <a-select-option :label="tx('表达式规则')" value="Expression">{{ tx('表达式规则') }}</a-select-option>
            <a-select-option :label="tx('数据处理')" value="Transform">{{ tx('数据处理') }}</a-select-option>
            <a-select-option :label="tx('函数节点')" value="Function">{{ tx('函数节点') }}</a-select-option>
            <a-select-option v-if="node.nodeType === 'ValueScript'" :label="tx('值处理脚本')" value="ValueScript">
              {{ tx('值处理脚本') }}
            </a-select-option>
            <a-select-option label="AND/OR" value="Logic">{{ 'AND/OR' }}</a-select-option>
            <a-select-option :label="tx('持续确认')" value="Duration">{{ tx('持续确认') }}</a-select-option>
            <a-select-option :label="tx('顺序/时序')" value="Sequence">{{ tx('顺序/时序') }}</a-select-option>
            <a-select-option label="Cycle Time" value="CycleTime">{{ 'Cycle Time' }}</a-select-option>
            <a-select-option :label="tx('工艺节拍')" value="ProcessTakt">{{ tx('工艺节拍') }}</a-select-option>
            <a-select-option :label="tx('异常检测')" value="AnomalyDetection">{{ tx('异常检测') }}</a-select-option>
            <a-select-option :label="tx('ONNX 推理')" value="ModelInference">{{ tx('ONNX 推理') }}</a-select-option>
            <a-select-option label="Tag Relation" value="TagRelation">{{ 'Tag Relation' }}</a-select-option>
            <a-select-option label="Context Gate" value="ContextGate">{{ 'Context Gate' }}</a-select-option>
            <a-select-option label="Alarm Lifecycle" value="AlarmLifecycle">{{ 'Alarm Lifecycle' }}</a-select-option>
            <a-select-option label="Action Policy" value="ActionPolicy">{{ 'Action Policy' }}</a-select-option>
            <a-select-option label="Debug Probe" value="DebugProbe">{{ 'Debug Probe' }}</a-select-option>
            <a-select-option :label="tx('MQTT 发布')" value="MqttPublish">{{ tx('MQTT 发布') }}</a-select-option>
            <a-select-option :label="tx('邮件通知')" value="EmailNotify">{{ tx('邮件通知') }}</a-select-option>
            <a-select-option label="Webhook" value="WebhookCall">{{ 'Webhook' }}</a-select-option>
          </gateway-select>
        </a-form-item>
        <template v-if="needsTagSource">
          <a-form-item :label="tx('标签来源')">
            <TagSelector :model-value="tagKey" :project="project" @change="changeTag" />
          </a-form-item>
        </template>
        <template v-if="node.nodeType === 'QualityGate'">
          <a-form-item :label="tx('匹配方式')">
            <gateway-choice
              v-model="node.qualityOperator"
              :options="[
                { label: tx('包含'), value: 'In' },
                { label: tx('排除'), value: 'NotIn' }
              ]"
            />
          </a-form-item>
          <a-form-item :label="tx('质量列表')">
            <gateway-select v-model="qualityValuesList" searchable mode="tags">
              <a-select-option label="Good" value="Good">{{ 'Good' }}</a-select-option>
              <a-select-option label="OutOfRange" value="OutOfRange">{{ 'OutOfRange' }}</a-select-option>
              <a-select-option label="Filtered" value="Filtered">{{ 'Filtered' }}</a-select-option>
              <a-select-option label="Spike" value="Spike">{{ 'Spike' }}</a-select-option>
              <a-select-option label="Bad" value="Bad">{{ 'Bad' }}</a-select-option>
              <a-select-option label="ReadError" value="ReadError">{{ 'ReadError' }}</a-select-option>
              <a-select-option label="NotConnected" value="NotConnected">{{ 'NotConnected' }}</a-select-option>
              <a-select-option label="Unknown" value="Unknown">{{ 'Unknown' }}</a-select-option>
            </gateway-select>
          </a-form-item>
        </template>
        <template v-if="node.nodeType === 'Condition'">
          <a-form-item :label="tx('条件类型')">
            <gateway-select v-model="node.conditionType">
              <a-select-option :label="tx('单点比较')" value="Condition">{{ tx('单点比较') }}</a-select-option>
              <a-select-option :label="tx('阈值上下限')" value="Threshold">{{ tx('阈值上下限') }}</a-select-option>
              <a-select-option :label="tx('死区变化')" value="Deadband">{{ tx('死区变化') }}</a-select-option>
              <a-select-option :label="tx('变化率')" value="RateOfChange">{{ tx('变化率') }}</a-select-option>
            </gateway-select>
          </a-form-item>
          <template v-if="node.conditionType === 'Condition'">
            <a-form-item :label="tx('运算符')">
              <gateway-select v-model="node.operator">
                <a-select-option label=">" value="GreaterThan">{{ '>' }}</a-select-option>
                <a-select-option label=">=" value="GreaterThanOrEqual">{{ '>=' }}</a-select-option>
                <a-select-option label="<" value="LessThan">{{ '<' }}</a-select-option>
                <a-select-option label="<=" value="LessThanOrEqual">{{ '<=' }}</a-select-option>
                <a-select-option label="=" value="Equal">{{ '=' }}</a-select-option>
                <a-select-option label="!=" value="NotEqual">{{ '!=' }}</a-select-option>
              </gateway-select>
            </a-form-item>
            <a-form-item :label="tx('比较值')">
              <gateway-number-input v-model="node.compareValue" :controls="false" />
            </a-form-item>
          </template>
          <template v-else-if="node.conditionType === 'Threshold'">
            <a-form-item :label="tx('下限')">
              <gateway-number-input v-model="node.lowLimit" :controls="false" />
            </a-form-item>
            <a-form-item :label="tx('上限')">
              <gateway-number-input v-model="node.highLimit" :controls="false" />
            </a-form-item>
          </template>
          <a-form-item v-else-if="node.conditionType === 'Deadband'" :label="tx('死区')">
            <gateway-number-input v-model="node.deadband" :min="0" :controls="false" />
          </a-form-item>
          <a-form-item v-else :label="tx('变化率上限 / 秒')">
            <gateway-number-input v-model="node.rateLimitPerSecond" :min="0" :controls="false" />
          </a-form-item>
        </template>
        <template v-if="node.nodeType === 'Hysteresis'">
          <a-form-item :label="tx('模式')">
            <gateway-choice
              v-model="node.hysteresisMode"
              :options="[
                { label: tx('高限'), value: 'High' },
                { label: tx('低限'), value: 'Low' }
              ]"
            />
          </a-form-item>
          <a-form-item :label="tx('动作值')">
            <gateway-number-input v-model="node.hysteresisOnValue" :controls="false" />
          </a-form-item>
          <a-form-item :label="tx('恢复值')">
            <gateway-number-input v-model="node.hysteresisOffValue" :controls="false" />
          </a-form-item>
        </template>
        <template v-if="node.nodeType === 'MultiLevelAlarm'">
          <div class="flow-alarm-levels">
            <div v-for="(level, index) in node.alarmLevels" :key="level.id || index" class="flow-alarm-level">
              <a-form-item :label="tx('级别名称')"><gateway-input v-model="level.name" /></a-form-item>
              <a-form-item :label="tx('严重度')">
                <gateway-select v-model="level.severity">
                  <a-select-option label="Info" value="Info">{{ 'Info' }}</a-select-option>
                  <a-select-option label="Warning" value="Warning">{{ 'Warning' }}</a-select-option>
                  <a-select-option label="Critical" value="Critical">{{ 'Critical' }}</a-select-option>
                </gateway-select>
              </a-form-item>
              <a-form-item :label="tx('运算符')">
                <gateway-select v-model="level.operator">
                  <a-select-option label=">" value="GreaterThan">{{ '>' }}</a-select-option>
                  <a-select-option label=">=" value="GreaterThanOrEqual">{{ '>=' }}</a-select-option>
                  <a-select-option label="<" value="LessThan">{{ '<' }}</a-select-option>
                  <a-select-option label="<=" value="LessThanOrEqual">{{ '<=' }}</a-select-option>
                  <a-select-option label="=" value="Equal">{{ '=' }}</a-select-option>
                  <a-select-option label="!=" value="NotEqual">{{ '!=' }}</a-select-option>
                </gateway-select>
              </a-form-item>
              <a-form-item :label="tx('比较值')">
                <gateway-number-input v-model="level.compareValue" :controls="false" />
              </a-form-item>
              <a-form-item :label="tx('消息')"><gateway-input v-model="level.message" /></a-form-item>
              <a-button @click="removeAlarmLevel(index)" type="link" danger>{{ tx('删除级别') }}</a-button>
            </div>
            <a-button @click="addAlarmLevel">
              <template #icon>
                <span class="ui-icon"><component :is="Plus" /></span>
              </template>
              {{ tx('新增级别') }}
            </a-button>
          </div>
        </template>
        <template v-if="node.nodeType === 'Expression'">
          <a-form-item :label="tx('表达式')">
            <gateway-input v-model="node.expression" type="textarea" :rows="3" />
          </a-form-item>
        </template>
        <template v-if="node.nodeType === 'SlidingWindow' || node.nodeType === 'WindowCalculation'">
          <a-form-item :label="tx('统计方法')">
            <gateway-select v-model="node.windowStatistic">
              <a-select-option :label="tx('平均值')" value="Average">{{ tx('平均值') }}</a-select-option>
              <a-select-option :label="tx('最小值')" value="Min">{{ tx('最小值') }}</a-select-option>
              <a-select-option :label="tx('最大值')" value="Max">{{ tx('最大值') }}</a-select-option>
              <a-select-option :label="tx('求和')" value="Sum">{{ tx('求和') }}</a-select-option>
              <a-select-option :label="tx('计数')" value="Count">{{ tx('计数') }}</a-select-option>
              <a-select-option :label="tx('标准差')" value="StdDev">{{ tx('标准差') }}</a-select-option>
            </gateway-select>
          </a-form-item>
          <a-form-item :label="tx('时间窗口（秒）')">
            <gateway-number-input v-model="node.windowSeconds" :min="1" :controls="false" />
          </a-form-item>
          <a-form-item :label="tx('最大样本数（0 为不限）')">
            <gateway-number-input v-model="node.windowSampleCount" :min="0" :controls="false" />
          </a-form-item>
          <a-form-item :label="tx('运算符')">
            <gateway-select v-model="node.operator">
              <a-select-option label=">" value="GreaterThan">{{ '>' }}</a-select-option>
              <a-select-option label=">=" value="GreaterThanOrEqual">{{ '>=' }}</a-select-option>
              <a-select-option label="<" value="LessThan">{{ '<' }}</a-select-option>
              <a-select-option label="<=" value="LessThanOrEqual">{{ '<=' }}</a-select-option>
              <a-select-option label="=" value="Equal">{{ '=' }}</a-select-option>
              <a-select-option label="!=" value="NotEqual">{{ '!=' }}</a-select-option>
            </gateway-select>
          </a-form-item>
          <a-form-item :label="tx('比较值')">
            <gateway-number-input v-model="node.compareValue" :controls="false" />
          </a-form-item>
        </template>
        <template v-if="node.nodeType === 'Aggregation'">
          <a-form-item :label="tx('聚合方法')">
            <gateway-select v-model="node.aggregationStatistic">
              <a-select-option :label="tx('平均值')" value="Average">{{ tx('平均值') }}</a-select-option>
              <a-select-option :label="tx('最小值')" value="Min">{{ tx('最小值') }}</a-select-option>
              <a-select-option :label="tx('最大值')" value="Max">{{ tx('最大值') }}</a-select-option>
              <a-select-option :label="tx('求和')" value="Sum">{{ tx('求和') }}</a-select-option>
              <a-select-option :label="tx('计数')" value="Count">{{ tx('计数') }}</a-select-option>
              <a-select-option :label="tx('标准差')" value="StdDev">{{ tx('标准差') }}</a-select-option>
              <a-select-option :label="tx('首值')" value="First">{{ tx('首值') }}</a-select-option>
              <a-select-option :label="tx('末值')" value="Last">{{ tx('末值') }}</a-select-option>
              <a-select-option :label="tx('极差')" value="Range">{{ tx('极差') }}</a-select-option>
            </gateway-select>
          </a-form-item>
          <a-form-item :label="tx('关联标签（可选）')">
            <TagSelector :model-value="relatedTagKey" :project="project" @change="changeRelatedTag" />
          </a-form-item>
          <a-form-item :label="tx('上下文标签（可选）')">
            <TagSelector :model-value="contextTagKey" :project="project" @change="changeContextTag" />
          </a-form-item>
          <a-form-item :label="tx('运算符')">
            <gateway-select v-model="node.operator">
              <a-select-option label=">" value="GreaterThan">{{ '>' }}</a-select-option>
              <a-select-option label=">=" value="GreaterThanOrEqual">{{ '>=' }}</a-select-option>
              <a-select-option label="<" value="LessThan">{{ '<' }}</a-select-option>
              <a-select-option label="<=" value="LessThanOrEqual">{{ '<=' }}</a-select-option>
              <a-select-option label="=" value="Equal">{{ '=' }}</a-select-option>
              <a-select-option label="!=" value="NotEqual">{{ '!=' }}</a-select-option>
            </gateway-select>
          </a-form-item>
          <a-form-item :label="tx('比较值')">
            <gateway-number-input v-model="node.compareValue" :controls="false" />
          </a-form-item>
        </template>
        <template v-if="node.nodeType === 'Trend'">
          <a-form-item :label="tx('趋势模式')">
            <gateway-select v-model="node.trendMode">
              <a-select-option :label="tx('斜率变化')" value="Slope">{{ tx('斜率变化') }}</a-select-option>
              <a-select-option :label="tx('上升')" value="Rising">{{ tx('上升') }}</a-select-option>
              <a-select-option :label="tx('下降')" value="Falling">{{ tx('下降') }}</a-select-option>
              <a-select-option :label="tx('稳定')" value="Stable">{{ tx('稳定') }}</a-select-option>
            </gateway-select>
          </a-form-item>
          <a-form-item :label="tx('时间窗口（秒）')">
            <gateway-number-input v-model="node.trendWindowSeconds" :min="1" :controls="false" />
          </a-form-item>
          <a-form-item :label="tx('最大样本数（0 为不限）')">
            <gateway-number-input v-model="node.trendSampleCount" :min="0" :controls="false" />
          </a-form-item>
          <a-form-item :label="tx('最小斜率 / 秒')">
            <gateway-number-input v-model="node.trendMinSlopePerSecond" :min="0" :controls="false" />
          </a-form-item>
          <a-form-item :label="tx('变化阈值')">
            <gateway-number-input v-model="node.trendChangeThreshold" :min="0" :controls="false" />
          </a-form-item>
          <a-form-item :label="tx('稳定死区')">
            <gateway-number-input v-model="node.trendStableDeadband" :min="0" :controls="false" />
          </a-form-item>
        </template>
        <template v-if="node.nodeType === 'StateMachine'">
          <a-form-item :label="tx('状态名称')"><gateway-input v-model="node.stateName" /></a-form-item>
          <a-form-item :label="tx('目标状态值')"><gateway-input v-model="node.stateExpectedValue" /></a-form-item>
          <a-form-item :label="tx('恢复状态值')"><gateway-input v-model="node.stateClearValue" /></a-form-item>
          <a-form-item :label="tx('保持超时（秒）')">
            <gateway-number-input v-model="node.stateTimeoutSeconds" :min="0" :controls="false" />
          </a-form-item>
        </template>
        <template v-if="node.nodeType === 'CycleTime' || node.nodeType === 'ProcessTakt'">
          <a-form-item :label="tx('开始值')"><gateway-input v-model="node.cycleStartValue" /></a-form-item>
          <a-form-item :label="tx('结束值')"><gateway-input v-model="node.cycleEndValue" /></a-form-item>
          <a-form-item :label="tx('最短周期（秒）')">
            <gateway-number-input v-model="node.cycleMinSeconds" :min="0" :controls="false" />
          </a-form-item>
          <a-form-item :label="tx('最长周期（秒）')">
            <gateway-number-input v-model="node.cycleMaxSeconds" :min="0" :controls="false" />
          </a-form-item>
          <template v-if="node.nodeType === 'ProcessTakt'">
            <a-form-item :label="tx('目标节拍（秒）')">
              <gateway-number-input v-model="node.taktTargetSeconds" :min="1" :controls="false" />
            </a-form-item>
            <a-form-item :label="tx('容差（%）')">
              <gateway-number-input v-model="node.taktTolerancePercent" :min="0" :controls="false" />
            </a-form-item>
          </template>
        </template>
        <template v-if="node.nodeType === 'AnomalyDetection'">
          <a-form-item :label="tx('检测模式')">
            <gateway-select v-model="node.anomalyMode">
              <a-select-option label="Z-Score" value="ZScore">{{ 'Z-Score' }}</a-select-option>
              <a-select-option :label="tx('均值偏差')" value="Deviation">{{ tx('均值偏差') }}</a-select-option>
              <a-select-option :label="tx('突变')" value="Spike">{{ tx('突变') }}</a-select-option>
            </gateway-select>
          </a-form-item>
          <a-form-item :label="tx('阈值')">
            <gateway-number-input v-model="node.anomalyThreshold" :min="0.000001" :controls="false" />
          </a-form-item>
          <a-form-item :label="tx('基线窗口（秒）')">
            <gateway-number-input v-model="node.anomalyBaselineWindowSeconds" :min="1" :controls="false" />
          </a-form-item>
          <a-form-item :label="tx('基线样本数（0 为不限）')">
            <gateway-number-input v-model="node.anomalyBaselineSampleCount" :min="0" :controls="false" />
          </a-form-item>
        </template>
        <template v-if="node.nodeType === 'ModelInference'">
          <a-form-item :label="tx('用途')">
            <gateway-choice v-model="node.modelPurpose" :options="modelPurposeOptions" />
          </a-form-item>
          <a-form-item :label="tx('ONNX 模型路径')">
            <gateway-input v-model="node.modelPath" placeholder="Models/quality.onnx" />
          </a-form-item>
          <a-form-item :label="tx('输入标签')">
            <gateway-input
              v-model="node.modelInputTags"
              type="textarea"
              :rows="2"
              :placeholder="tx('留空使用当前标签；多个标签用逗号分隔，可填写点位编码或 设备.分组.标签')"
            />
          </a-form-item>
          <a-form-item :label="tx('输入张量名')">
            <gateway-input v-model="node.modelInputName" :placeholder="tx('留空使用模型第一个输入')" />
          </a-form-item>
          <a-form-item :label="tx('多输入张量名')">
            <gateway-input v-model="node.modelInputNames" :placeholder="tx('多输入模型用逗号分隔')" />
          </a-form-item>
          <a-form-item :label="tx('输出张量名')">
            <gateway-input v-model="node.modelOutputName" :placeholder="tx('留空使用模型第一个输出')" />
          </a-form-item>
          <a-form-item :label="tx('输出序号')">
            <gateway-number-input v-model="node.modelOutputIndex" :min="0" :controls="false" />
          </a-form-item>
          <a-form-item :label="tx('判断符')">
            <gateway-select v-model="node.modelOperator">
              <a-select-option
                v-for="item in comparisonOptions"
                :key="item.value"
                :label="tx(item.label)"
                :value="item.value"
              >
                {{ tx(item.label) }}
              </a-select-option>
            </gateway-select>
          </a-form-item>
          <a-form-item :label="tx('阈值')">
            <gateway-number-input v-model="node.modelThreshold" :controls="false" />
          </a-form-item>
          <a-form-item :label="tx('超时（ms）')">
            <gateway-number-input v-model="node.modelTimeoutMilliseconds" :min="1" :max="30000" :controls="false" />
          </a-form-item>
        </template>
        <template v-if="node.nodeType === 'TagRelation'">
          <a-form-item :label="tx('关联标签')">
            <TagSelector :model-value="relatedTagKey" :project="project" @change="changeRelatedTag" />
          </a-form-item>
          <a-form-item :label="tx('关系运算符')">
            <gateway-select v-model="node.relationOperator">
              <a-select-option label=">" value="GreaterThan">{{ '>' }}</a-select-option>
              <a-select-option label=">=" value="GreaterThanOrEqual">{{ '>=' }}</a-select-option>
              <a-select-option label="<" value="LessThan">{{ '<' }}</a-select-option>
              <a-select-option label="<=" value="LessThanOrEqual">{{ '<=' }}</a-select-option>
              <a-select-option label="=" value="Equal">{{ '=' }}</a-select-option>
              <a-select-option label="!=" value="NotEqual">{{ '!=' }}</a-select-option>
            </gateway-select>
          </a-form-item>
          <a-form-item :label="tx('关联值乘数')">
            <gateway-number-input v-model="node.relationMultiplier" :controls="false" />
          </a-form-item>
          <a-form-item :label="tx('关联值偏移')">
            <gateway-number-input v-model="node.relationOffset" :controls="false" />
          </a-form-item>
        </template>
        <template v-if="node.nodeType === 'ContextGate'">
          <a-form-item :label="tx('上下文标签（可选）')">
            <TagSelector :model-value="contextTagKey" :project="project" @change="changeContextTag" />
          </a-form-item>
          <a-form-item :label="tx('上下文名称')"><gateway-input v-model="node.contextName" /></a-form-item>
          <a-form-item :label="tx('上下文运算符')">
            <gateway-select v-model="node.contextOperator">
              <a-select-option label="=" value="Equal">{{ '=' }}</a-select-option>
              <a-select-option label="!=" value="NotEqual">{{ '!=' }}</a-select-option>
              <a-select-option label=">" value="GreaterThan">{{ '>' }}</a-select-option>
              <a-select-option label=">=" value="GreaterThanOrEqual">{{ '>=' }}</a-select-option>
              <a-select-option label="<" value="LessThan">{{ '<' }}</a-select-option>
              <a-select-option label="<=" value="LessThanOrEqual">{{ '<=' }}</a-select-option>
            </gateway-select>
          </a-form-item>
          <a-form-item :label="tx('期望值')"><gateway-input v-model="node.contextExpectedValue" /></a-form-item>
        </template>
        <template v-if="node.nodeType === 'AlarmLifecycle'">
          <a-form-item :label="tx('告警级别')">
            <gateway-select v-model="node.alarmSeverity">
              <a-select-option label="Info" value="Info">{{ 'Info' }}</a-select-option>
              <a-select-option label="Warning" value="Warning">{{ 'Warning' }}</a-select-option>
              <a-select-option label="Critical" value="Critical">{{ 'Critical' }}</a-select-option>
            </gateway-select>
          </a-form-item>
          <a-form-item :label="tx('抑制重复（秒）')">
            <gateway-number-input v-model="node.alarmSuppressSeconds" :min="0" :controls="false" />
          </a-form-item>
          <a-form-item :label="tx('再次触发间隔（秒）')">
            <gateway-number-input v-model="node.alarmReTriggerSeconds" :min="0" :controls="false" />
          </a-form-item>
          <a-form-item :label="tx('升级确认（秒）')">
            <gateway-number-input v-model="node.alarmEscalateAfterSeconds" :min="0" :controls="false" />
          </a-form-item>
        </template>
        <template v-if="node.nodeType === 'ActionPolicy'">
          <a-form-item :label="tx('动作延迟（秒）')">
            <gateway-number-input v-model="node.actionDelaySeconds" :min="0" :controls="false" />
          </a-form-item>
          <a-form-item :label="tx('动作冷却（秒）')">
            <gateway-number-input v-model="node.actionCooldownSeconds" :min="0" :controls="false" />
          </a-form-item>
          <a-form-item :label="tx('每分钟最多动作数')">
            <gateway-number-input v-model="node.actionMaxPerMinute" :min="0" :controls="false" />
          </a-form-item>
        </template>
        <template v-if="node.nodeType === 'DebugProbe'">
          <a-form-item :label="tx('启用调试')"><a-switch v-model:checked="node.debugEnabled" /></a-form-item>
          <a-form-item :label="tx('调试标签')"><gateway-input v-model="node.debugLabel" /></a-form-item>
          <a-form-item :label="tx('触发时记录')"><a-switch v-model:checked="node.executeOnActive" /></a-form-item>
          <a-form-item :label="tx('恢复时记录')"><a-switch v-model:checked="node.executeOnClear" /></a-form-item>
          <a-form-item :label="tx('调试消息')">
            <gateway-input v-model="node.activeMessage" type="textarea" :rows="2" />
          </a-form-item>
        </template>
        <template v-if="node.nodeType === 'Transform' || node.nodeType === 'Function'">
          <a-form-item v-if="node.nodeType === 'Transform'" :label="tx('取绝对值')">
            <a-switch v-model:checked="node.transformUseAbsolute" />
          </a-form-item>
          <a-form-item v-if="node.nodeType === 'Transform'" :label="tx('乘数')">
            <gateway-number-input v-model="node.transformMultiplier" :controls="false" />
          </a-form-item>
          <a-form-item v-if="node.nodeType === 'Transform'" :label="tx('偏移')">
            <gateway-number-input v-model="node.transformOffset" :controls="false" />
          </a-form-item>
          <a-form-item :label="tx(node.nodeType === 'Function' ? tx('函数表达式') : tx('表达式'))">
            <gateway-input
              v-model="node.transformExpression"
              type="textarea"
              :rows="3"
              :placeholder="tx('{value} * 10 + 5，支持 abs/round/floor/ceil')"
            />
          </a-form-item>
          <a-form-item :label="tx('超时（ms）')">
            <gateway-number-input v-model="node.transformTimeoutMilliseconds" :min="1" :max="5000" :controls="false" />
          </a-form-item>
        </template>
        <template v-if="node.nodeType === 'ValueScript'">
          <a-alert
            :message="tx('规则节点固定使用所选脚本的发布版本；如需升级版本，请重新选择脚本。')"
            type="info"
            :closable="false"
          />
          <a-form-item :label="tx('值处理脚本')">
            <gateway-select
              :model-value="node.valueScriptId"
              searchable
              style="width: 100%"
              @change="changeValueScript"
            >
              <a-select-option
                v-for="script in ruleValueScripts"
                :key="script.id"
                :label="tx(`${script.name}（v${script.version}）`)"
                :value="script.id"
              >
                {{ `${script.name}（v${script.version}）` }}
              </a-select-option>
            </gateway-select>
          </a-form-item>
          <a-form-item :label="tx('固定发布版本')">
            <gateway-number-input v-model="node.valueScriptVersion" :disabled="true" :controls="false" />
          </a-form-item>
          <a-form-item :label="tx('输入 / 输出')">
            <gateway-input
              :model-value="`${node.valueScriptInputDataType || '-'} → ${node.valueScriptOutputDataType || '-'}`"
              disabled
            />
          </a-form-item>
          <a-form-item :label="tx('超时（ms）')">
            <gateway-number-input v-model="node.transformTimeoutMilliseconds" :min="10" :max="5000" :controls="false" />
          </a-form-item>
        </template>
        <a-form-item v-if="node.nodeType === 'Logic'" :label="tx('组合方式')">
          <gateway-choice v-model="node.logicalOperator" :options="['And', 'Or']" />
        </a-form-item>
        <a-form-item v-if="node.nodeType === 'Duration'" :label="tx('持续时间（秒）')">
          <gateway-number-input v-model="node.durationSeconds" :min="0" :controls="false" />
        </a-form-item>
        <a-form-item v-if="node.nodeType === 'Duration'" :label="tx('恢复确认（秒）')">
          <gateway-number-input v-model="node.clearDurationSeconds" :min="0" :controls="false" />
        </a-form-item>
        <template v-if="node.nodeType === 'Sequence'">
          <a-form-item :label="tx('总窗口（秒）')">
            <gateway-number-input v-model="node.sequenceWindowSeconds" :min="1" :controls="false" />
          </a-form-item>
          <a-form-item :label="tx('单步超时（秒）')">
            <gateway-number-input v-model="node.sequenceStepTimeoutSeconds" :min="0" :controls="false" />
          </a-form-item>
          <a-form-item :label="tx('最小间隔（秒）')">
            <gateway-number-input v-model="node.sequenceMinIntervalSeconds" :min="0" :controls="false" />
          </a-form-item>
          <a-form-item :label="tx('错序时重置')">
            <a-switch v-model:checked="node.sequenceResetOnMismatch" />
          </a-form-item>
        </template>
        <template v-if="node.nodeType === 'MqttPublish'">
          <a-form-item :label="tx('发布到 MQTT')"><a-switch v-model:checked="node.publishToMqtt" /></a-form-item>
          <a-form-item :label="tx('Topic 模板')"><gateway-input v-model="node.topicTemplate" /></a-form-item>
          <a-form-item label="QoS">
            <gateway-select v-model="node.publishQos">
              <a-select-option label="0" :value="0">{{ '0' }}</a-select-option>
              <a-select-option label="1" :value="1">{{ '1' }}</a-select-option>
              <a-select-option label="2" :value="2">{{ '2' }}</a-select-option>
            </gateway-select>
          </a-form-item>
          <a-form-item :label="tx('恢复时发布')"><a-switch v-model:checked="node.publishOnClear" /></a-form-item>
          <a-form-item :label="tx('触发消息')">
            <gateway-input v-model="node.activeMessage" type="textarea" :rows="2" />
          </a-form-item>
          <a-form-item :label="tx('恢复消息')">
            <gateway-input v-model="node.clearMessage" type="textarea" :rows="2" />
          </a-form-item>
        </template>
        <template v-if="node.nodeType === 'EmailNotify'">
          <a-form-item :label="tx('触发时发送')"><a-switch v-model:checked="node.executeOnActive" /></a-form-item>
          <a-form-item :label="tx('恢复时发送')"><a-switch v-model:checked="node.executeOnClear" /></a-form-item>
          <a-form-item :label="tx('SMTP 服务器')"><gateway-input v-model="node.emailSmtpHost" /></a-form-item>
          <a-form-item :label="tx('SMTP 端口')">
            <gateway-number-input v-model="node.emailSmtpPort" :min="1" :controls="false" />
          </a-form-item>
          <a-form-item :label="tx('启用 SSL')"><a-switch v-model:checked="node.emailEnableSsl" /></a-form-item>
          <a-form-item :label="tx('用户名')"><gateway-input v-model="node.emailUsername" /></a-form-item>
          <a-form-item :label="tx('密码')">
            <gateway-input v-model="node.emailPassword" type="password" show-password />
          </a-form-item>
          <a-form-item :label="tx('发件人')"><gateway-input v-model="node.emailFrom" /></a-form-item>
          <a-form-item :label="tx('收件人')"><gateway-input v-model="node.emailTo" /></a-form-item>
          <a-form-item :label="tx('抄送')"><gateway-input v-model="node.emailCc" /></a-form-item>
          <a-form-item :label="tx('主题模板')"><gateway-input v-model="node.emailSubjectTemplate" /></a-form-item>
          <a-form-item :label="tx('正文模板')">
            <gateway-input v-model="node.emailBodyTemplate" type="textarea" :rows="4" />
          </a-form-item>
        </template>
        <template v-if="node.nodeType === 'WebhookCall'">
          <a-form-item :label="tx('触发时调用')"><a-switch v-model:checked="node.executeOnActive" /></a-form-item>
          <a-form-item :label="tx('恢复时调用')"><a-switch v-model:checked="node.executeOnClear" /></a-form-item>
          <a-form-item label="URL"><gateway-input v-model="node.webhookUrl" /></a-form-item>
          <a-form-item label="Method">
            <gateway-select v-model="node.webhookMethod">
              <a-select-option label="POST" value="POST">{{ 'POST' }}</a-select-option>
              <a-select-option label="PUT" value="PUT">{{ 'PUT' }}</a-select-option>
              <a-select-option label="PATCH" value="PATCH">{{ 'PATCH' }}</a-select-option>
              <a-select-option label="GET" value="GET">{{ 'GET' }}</a-select-option>
              <a-select-option label="DELETE" value="DELETE">{{ 'DELETE' }}</a-select-option>
            </gateway-select>
          </a-form-item>
          <a-form-item label="Content-Type"><gateway-input v-model="node.webhookContentType" /></a-form-item>
          <a-form-item label="Headers">
            <gateway-input v-model="node.webhookHeaders" type="textarea" :rows="3" />
          </a-form-item>
          <a-form-item :label="tx('Body 模板')">
            <gateway-input v-model="node.webhookBodyTemplate" type="textarea" :rows="5" />
          </a-form-item>
          <a-form-item :label="tx('超时（秒）')">
            <gateway-number-input v-model="node.webhookTimeoutSeconds" :min="1" :controls="false" />
          </a-form-item>
          <a-form-item :label="tx('失败重试')">
            <gateway-number-input v-model="node.webhookRetryCount" :min="0" :controls="false" />
          </a-form-item>
        </template>
      </a-form>
    </template>
  </aside>
</template>

<script setup lang="ts">
import { formatMessage } from '../i18n'
import { tx } from '../i18n'
import { computed } from 'vue'
import type { FlowRuleNode, ProjectConfig } from '../api'
import type { ValueTransformCatalogItem } from '../scriptingApi'
import { PlusOutlined as Plus } from '@ant-design/icons-vue'
import TagSelector from './TagSelector.vue'
import { applyTagSelectionToNode } from '../utils/flowRules'
import { findTagSelectionKey, type TagSelection } from '../utils/tagSelection'

const props = defineProps<{
  node: FlowRuleNode | null
  project: ProjectConfig | null
  valueScripts: ValueTransformCatalogItem[]
}>()

defineEmits<{
  delete: []
}>()

const tagKey = computed(() =>
  props.node
    ? findTagSelectionKey(props.project, {
        sourceTagId: props.node.tagId
      })
    : ''
)

const relatedTagKey = computed(() =>
  props.node
    ? findTagSelectionKey(props.project, {
        sourceTagId: props.node.relatedTagId
      })
    : ''
)

const contextTagKey = computed(() =>
  props.node
    ? findTagSelectionKey(props.project, {
        sourceTagId: props.node.contextTagId
      })
    : ''
)

const qualityValuesList = computed<string[]>({
  get() {
    return (props.node?.qualityValues || 'Good')
      .split(/[,\s;|]+/)
      .map((item) => item.trim())
      .filter(Boolean)
  },
  set(values) {
    if (!props.node) return
    props.node.qualityValues = (values || []).join(',')
  }
})

const modelPurposeOptions = [
  { label: '设备异常预警', value: 'DeviceAnomaly' },
  { label: '质量预测', value: 'QualityPrediction' }
]

const comparisonOptions = [
  { label: '>', value: 'GreaterThan' },
  { label: '>=', value: 'GreaterThanOrEqual' },
  { label: '<', value: 'LessThan' },
  { label: '<=', value: 'LessThanOrEqual' },
  { label: '=', value: 'Equal' },
  { label: '!=', value: 'NotEqual' }
]

const needsTagSource = computed(
  () =>
    !!props.node &&
    [
      'TagInput',
      'QualityGate',
      'Condition',
      'Hysteresis',
      'MultiLevelAlarm',
      'Expression',
      'SlidingWindow',
      'WindowCalculation',
      'Aggregation',
      'Trend',
      'StateMachine',
      'CycleTime',
      'ProcessTakt',
      'AnomalyDetection',
      'ModelInference',
      'TagRelation',
      'ContextGate'
    ].includes(props.node.nodeType)
)
const ruleValueScripts = computed(() => props.valueScripts.filter((script) => script.scope !== 'TagCleaning'))

/**
 * 替换规则节点使用的脚本并固定当前发布版本及类型元数据。
 */
function changeValueScript(scriptId: string) {
  if (!props.node) return
  const script = ruleValueScripts.value.find((item) => item.id === scriptId)
  if (!script) return
  props.node.label = script.name
  props.node.valueScriptId = script.id
  props.node.valueScriptVersion = script.version
  props.node.valueScriptCategory = script.nodeCategory
  props.node.valueScriptInputDataType = script.inputDataType
  props.node.valueScriptOutputDataType = script.outputDataType
}

function changeTag(selection: TagSelection | null) {
  if (!props.node) return
  applyTagSelectionToNode(props.node, selection)
}

function changeRelatedTag(selection: TagSelection | null) {
  if (!props.node) return
  props.node.relatedChannelId = selection?.channelId ?? ''
  props.node.relatedChannelName = selection?.channelName ?? ''
  props.node.relatedDeviceId = selection?.deviceId ?? ''
  props.node.relatedGroupId = selection?.groupId ?? ''
  props.node.relatedTagId = selection?.tagId ?? ''
  props.node.relatedDeviceName = selection?.deviceName ?? ''
  props.node.relatedGroupName = selection?.groupName ?? ''
  props.node.relatedTagName = selection?.tagName ?? ''
  props.node.relatedPointCode = selection?.pointCode ?? ''
  props.node.relatedDataType = selection?.dataType ?? ''
}

function changeContextTag(selection: TagSelection | null) {
  if (!props.node) return
  props.node.contextChannelId = selection?.channelId ?? ''
  props.node.contextChannelName = selection?.channelName ?? ''
  props.node.contextDeviceId = selection?.deviceId ?? ''
  props.node.contextGroupId = selection?.groupId ?? ''
  props.node.contextTagId = selection?.tagId ?? ''
  props.node.contextDeviceName = selection?.deviceName ?? ''
  props.node.contextGroupName = selection?.groupName ?? ''
  props.node.contextTagName = selection?.tagName ?? ''
  props.node.contextPointCode = selection?.pointCode ?? ''
  props.node.contextDataType = selection?.dataType ?? ''
}

function addAlarmLevel() {
  if (!props.node) return
  if (!props.node.alarmLevels) props.node.alarmLevels = []
  props.node.alarmLevels.push({
    id: createId(),
    name: formatMessage('级别 {0}', [props.node.alarmLevels.length + 1]),
    severity: 'Warning',
    operator: 'GreaterThanOrEqual',
    compareValue: 0,
    message: ''
  })
}

function removeAlarmLevel(index: number) {
  if (!props.node?.alarmLevels) return
  props.node.alarmLevels.splice(index, 1)
}

function createId() {
  return typeof crypto !== 'undefined' && 'randomUUID' in crypto
    ? crypto.randomUUID().replace(/-/g, '')
    : Math.random().toString(16).slice(2) + Date.now().toString(16)
}
</script>
