<template>
  <aside class="flow-properties">
    <el-empty v-if="!node" description="选择一个节点进行编辑" />

    <template v-else>
      <div class="flow-properties__header">
        <strong>节点属性</strong>
        <el-button type="danger" text @click="$emit('delete')">删除</el-button>
      </div>

      <el-form label-position="top">
        <el-form-item label="名称">
          <el-input v-model="node.label" />
        </el-form-item>
        <el-form-item label="类型">
          <el-select v-model="node.nodeType">
            <el-option label="质量门控" value="QualityGate" />
            <el-option label="窗口统计" value="SlidingWindow" />
            <el-option label="窗口计算" value="WindowCalculation" />
            <el-option label="聚合计算" value="Aggregation" />
            <el-option label="趋势判断" value="Trend" />
            <el-option label="状态机" value="StateMachine" />
            <el-option label="标签输入" value="TagInput" />
            <el-option label="条件" value="Condition" />
            <el-option label="滞回规则" value="Hysteresis" />
            <el-option label="多级告警" value="MultiLevelAlarm" />
            <el-option label="表达式规则" value="Expression" />
            <el-option label="数据处理" value="Transform" />
            <el-option label="函数节点" value="Function" />
            <el-option v-if="node.nodeType === 'ValueScript'" label="值处理脚本" value="ValueScript" />
            <el-option label="AND/OR" value="Logic" />
            <el-option label="持续确认" value="Duration" />
            <el-option label="顺序/时序" value="Sequence" />
            <el-option label="Cycle Time" value="CycleTime" />
            <el-option label="工艺节拍" value="ProcessTakt" />
            <el-option label="异常检测" value="AnomalyDetection" />
            <el-option label="ONNX 推理" value="ModelInference" />
            <el-option label="Tag Relation" value="TagRelation" />
            <el-option label="Context Gate" value="ContextGate" />
            <el-option label="Alarm Lifecycle" value="AlarmLifecycle" />
            <el-option label="Action Policy" value="ActionPolicy" />
            <el-option label="Debug Probe" value="DebugProbe" />
            <el-option label="MQTT 发布" value="MqttPublish" />
            <el-option label="邮件通知" value="EmailNotify" />
            <el-option label="Webhook" value="WebhookCall" />
          </el-select>
        </el-form-item>

        <template v-if="needsTagSource">
          <el-form-item label="标签来源">
            <TagSelector :model-value="tagKey" :project="project" @change="changeTag" />
          </el-form-item>
        </template>

        <template v-if="node.nodeType === 'QualityGate'">
          <el-form-item label="匹配方式">
            <el-segmented
              v-model="node.qualityOperator"
              :options="[
                { label: '包含', value: 'In' },
                { label: '排除', value: 'NotIn' }
              ]"
            />
          </el-form-item>
          <el-form-item label="质量列表">
            <el-select v-model="qualityValuesList" multiple filterable allow-create default-first-option>
              <el-option label="Good" value="Good" />
              <el-option label="OutOfRange" value="OutOfRange" />
              <el-option label="Filtered" value="Filtered" />
              <el-option label="Spike" value="Spike" />
              <el-option label="Bad" value="Bad" />
              <el-option label="ReadError" value="ReadError" />
              <el-option label="NotConnected" value="NotConnected" />
              <el-option label="Unknown" value="Unknown" />
            </el-select>
          </el-form-item>
        </template>

        <template v-if="node.nodeType === 'Condition'">
          <el-form-item label="条件类型">
            <el-select v-model="node.conditionType">
              <el-option label="单点比较" value="Condition" />
              <el-option label="阈值上下限" value="Threshold" />
              <el-option label="死区变化" value="Deadband" />
              <el-option label="变化率" value="RateOfChange" />
            </el-select>
          </el-form-item>

          <template v-if="node.conditionType === 'Condition'">
            <el-form-item label="运算符">
              <el-select v-model="node.operator">
                <el-option label=">" value="GreaterThan" />
                <el-option label=">=" value="GreaterThanOrEqual" />
                <el-option label="<" value="LessThan" />
                <el-option label="<=" value="LessThanOrEqual" />
                <el-option label="=" value="Equal" />
                <el-option label="!=" value="NotEqual" />
              </el-select>
            </el-form-item>
            <el-form-item label="比较值">
              <el-input-number v-model="node.compareValue" :controls="false" />
            </el-form-item>
          </template>

          <template v-else-if="node.conditionType === 'Threshold'">
            <el-form-item label="下限">
              <el-input-number v-model="node.lowLimit" :controls="false" />
            </el-form-item>
            <el-form-item label="上限">
              <el-input-number v-model="node.highLimit" :controls="false" />
            </el-form-item>
          </template>

          <el-form-item v-else-if="node.conditionType === 'Deadband'" label="死区">
            <el-input-number v-model="node.deadband" :min="0" :controls="false" />
          </el-form-item>

          <el-form-item v-else label="变化率上限 / 秒">
            <el-input-number v-model="node.rateLimitPerSecond" :min="0" :controls="false" />
          </el-form-item>
        </template>

        <template v-if="node.nodeType === 'Hysteresis'">
          <el-form-item label="模式">
            <el-segmented
              v-model="node.hysteresisMode"
              :options="[
                { label: '高限', value: 'High' },
                { label: '低限', value: 'Low' }
              ]"
            />
          </el-form-item>
          <el-form-item label="动作值">
            <el-input-number v-model="node.hysteresisOnValue" :controls="false" />
          </el-form-item>
          <el-form-item label="恢复值">
            <el-input-number v-model="node.hysteresisOffValue" :controls="false" />
          </el-form-item>
        </template>

        <template v-if="node.nodeType === 'MultiLevelAlarm'">
          <div class="flow-alarm-levels">
            <div
              v-for="(level, index) in node.alarmLevels"
              :key="level.id || index"
              class="flow-alarm-level"
            >
              <el-form-item label="级别名称">
                <el-input v-model="level.name" />
              </el-form-item>
              <el-form-item label="严重度">
                <el-select v-model="level.severity">
                  <el-option label="Info" value="Info" />
                  <el-option label="Warning" value="Warning" />
                  <el-option label="Critical" value="Critical" />
                </el-select>
              </el-form-item>
              <el-form-item label="运算符">
                <el-select v-model="level.operator">
                  <el-option label=">" value="GreaterThan" />
                  <el-option label=">=" value="GreaterThanOrEqual" />
                  <el-option label="<" value="LessThan" />
                  <el-option label="<=" value="LessThanOrEqual" />
                  <el-option label="=" value="Equal" />
                  <el-option label="!=" value="NotEqual" />
                </el-select>
              </el-form-item>
              <el-form-item label="比较值">
                <el-input-number v-model="level.compareValue" :controls="false" />
              </el-form-item>
              <el-form-item label="消息">
                <el-input v-model="level.message" />
              </el-form-item>
              <el-button type="danger" text @click="removeAlarmLevel(index)">删除级别</el-button>
            </div>
            <el-button plain :icon="Plus" @click="addAlarmLevel">新增级别</el-button>
          </div>
        </template>

        <template v-if="node.nodeType === 'Expression'">
          <el-form-item label="表达式">
            <el-input v-model="node.expression" type="textarea" :rows="3" />
          </el-form-item>
        </template>

        <template v-if="node.nodeType === 'SlidingWindow' || node.nodeType === 'WindowCalculation'">
          <el-form-item label="统计方法">
            <el-select v-model="node.windowStatistic">
              <el-option label="平均值" value="Average" />
              <el-option label="最小值" value="Min" />
              <el-option label="最大值" value="Max" />
              <el-option label="求和" value="Sum" />
              <el-option label="计数" value="Count" />
              <el-option label="标准差" value="StdDev" />
            </el-select>
          </el-form-item>
          <el-form-item label="时间窗口（秒）">
            <el-input-number v-model="node.windowSeconds" :min="1" :controls="false" />
          </el-form-item>
          <el-form-item label="最大样本数（0 为不限）">
            <el-input-number v-model="node.windowSampleCount" :min="0" :controls="false" />
          </el-form-item>
          <el-form-item label="运算符">
            <el-select v-model="node.operator">
              <el-option label=">" value="GreaterThan" />
              <el-option label=">=" value="GreaterThanOrEqual" />
              <el-option label="<" value="LessThan" />
              <el-option label="<=" value="LessThanOrEqual" />
              <el-option label="=" value="Equal" />
              <el-option label="!=" value="NotEqual" />
            </el-select>
          </el-form-item>
          <el-form-item label="比较值">
            <el-input-number v-model="node.compareValue" :controls="false" />
          </el-form-item>
        </template>

        <template v-if="node.nodeType === 'Aggregation'">
          <el-form-item label="聚合方法">
            <el-select v-model="node.aggregationStatistic">
              <el-option label="平均值" value="Average" />
              <el-option label="最小值" value="Min" />
              <el-option label="最大值" value="Max" />
              <el-option label="求和" value="Sum" />
              <el-option label="计数" value="Count" />
              <el-option label="标准差" value="StdDev" />
              <el-option label="首值" value="First" />
              <el-option label="末值" value="Last" />
              <el-option label="极差" value="Range" />
            </el-select>
          </el-form-item>
          <el-form-item label="关联标签（可选）">
            <TagSelector :model-value="relatedTagKey" :project="project" @change="changeRelatedTag" />
          </el-form-item>
          <el-form-item label="上下文标签（可选）">
            <TagSelector :model-value="contextTagKey" :project="project" @change="changeContextTag" />
          </el-form-item>
          <el-form-item label="运算符">
            <el-select v-model="node.operator">
              <el-option label=">" value="GreaterThan" />
              <el-option label=">=" value="GreaterThanOrEqual" />
              <el-option label="<" value="LessThan" />
              <el-option label="<=" value="LessThanOrEqual" />
              <el-option label="=" value="Equal" />
              <el-option label="!=" value="NotEqual" />
            </el-select>
          </el-form-item>
          <el-form-item label="比较值">
            <el-input-number v-model="node.compareValue" :controls="false" />
          </el-form-item>
        </template>

        <template v-if="node.nodeType === 'Trend'">
          <el-form-item label="趋势模式">
            <el-select v-model="node.trendMode">
              <el-option label="斜率变化" value="Slope" />
              <el-option label="上升" value="Rising" />
              <el-option label="下降" value="Falling" />
              <el-option label="稳定" value="Stable" />
            </el-select>
          </el-form-item>
          <el-form-item label="时间窗口（秒）">
            <el-input-number v-model="node.trendWindowSeconds" :min="1" :controls="false" />
          </el-form-item>
          <el-form-item label="最大样本数（0 为不限）">
            <el-input-number v-model="node.trendSampleCount" :min="0" :controls="false" />
          </el-form-item>
          <el-form-item label="最小斜率 / 秒">
            <el-input-number v-model="node.trendMinSlopePerSecond" :min="0" :controls="false" />
          </el-form-item>
          <el-form-item label="变化阈值">
            <el-input-number v-model="node.trendChangeThreshold" :min="0" :controls="false" />
          </el-form-item>
          <el-form-item label="稳定死区">
            <el-input-number v-model="node.trendStableDeadband" :min="0" :controls="false" />
          </el-form-item>
        </template>

        <template v-if="node.nodeType === 'StateMachine'">
          <el-form-item label="状态名称">
            <el-input v-model="node.stateName" />
          </el-form-item>
          <el-form-item label="目标状态值">
            <el-input v-model="node.stateExpectedValue" />
          </el-form-item>
          <el-form-item label="恢复状态值">
            <el-input v-model="node.stateClearValue" />
          </el-form-item>
          <el-form-item label="保持超时（秒）">
            <el-input-number v-model="node.stateTimeoutSeconds" :min="0" :controls="false" />
          </el-form-item>
        </template>

        <template v-if="node.nodeType === 'CycleTime' || node.nodeType === 'ProcessTakt'">
          <el-form-item label="开始值">
            <el-input v-model="node.cycleStartValue" />
          </el-form-item>
          <el-form-item label="结束值">
            <el-input v-model="node.cycleEndValue" />
          </el-form-item>
          <el-form-item label="最短周期（秒）">
            <el-input-number v-model="node.cycleMinSeconds" :min="0" :controls="false" />
          </el-form-item>
          <el-form-item label="最长周期（秒）">
            <el-input-number v-model="node.cycleMaxSeconds" :min="0" :controls="false" />
          </el-form-item>
          <template v-if="node.nodeType === 'ProcessTakt'">
            <el-form-item label="目标节拍（秒）">
              <el-input-number v-model="node.taktTargetSeconds" :min="1" :controls="false" />
            </el-form-item>
            <el-form-item label="容差（%）">
              <el-input-number v-model="node.taktTolerancePercent" :min="0" :controls="false" />
            </el-form-item>
          </template>
        </template>

        <template v-if="node.nodeType === 'AnomalyDetection'">
          <el-form-item label="检测模式">
            <el-select v-model="node.anomalyMode">
              <el-option label="Z-Score" value="ZScore" />
              <el-option label="均值偏差" value="Deviation" />
              <el-option label="突变" value="Spike" />
            </el-select>
          </el-form-item>
          <el-form-item label="阈值">
            <el-input-number v-model="node.anomalyThreshold" :min="0.000001" :controls="false" />
          </el-form-item>
          <el-form-item label="基线窗口（秒）">
            <el-input-number v-model="node.anomalyBaselineWindowSeconds" :min="1" :controls="false" />
          </el-form-item>
          <el-form-item label="基线样本数（0 为不限）">
            <el-input-number v-model="node.anomalyBaselineSampleCount" :min="0" :controls="false" />
          </el-form-item>
        </template>

        <template v-if="node.nodeType === 'ModelInference'">
          <el-form-item label="用途">
            <el-segmented v-model="node.modelPurpose" :options="modelPurposeOptions" />
          </el-form-item>
          <el-form-item label="ONNX 模型路径">
            <el-input v-model="node.modelPath" placeholder="Models/quality.onnx" />
          </el-form-item>
          <el-form-item label="输入标签">
            <el-input
              v-model="node.modelInputTags"
              type="textarea"
              :rows="2"
              placeholder="留空使用当前标签；多个标签用逗号分隔，可填写点位编码或 设备.分组.标签"
            />
          </el-form-item>
          <el-form-item label="输入张量名">
            <el-input v-model="node.modelInputName" placeholder="留空使用模型第一个输入" />
          </el-form-item>
          <el-form-item label="多输入张量名">
            <el-input v-model="node.modelInputNames" placeholder="多输入模型用逗号分隔" />
          </el-form-item>
          <el-form-item label="输出张量名">
            <el-input v-model="node.modelOutputName" placeholder="留空使用模型第一个输出" />
          </el-form-item>
          <el-form-item label="输出序号">
            <el-input-number v-model="node.modelOutputIndex" :min="0" :controls="false" />
          </el-form-item>
          <el-form-item label="判断符">
            <el-select v-model="node.modelOperator">
              <el-option
                v-for="item in comparisonOptions"
                :key="item.value"
                :label="item.label"
                :value="item.value"
              />
            </el-select>
          </el-form-item>
          <el-form-item label="阈值">
            <el-input-number v-model="node.modelThreshold" :controls="false" />
          </el-form-item>
          <el-form-item label="超时（ms）">
            <el-input-number v-model="node.modelTimeoutMilliseconds" :min="1" :max="30000" :controls="false" />
          </el-form-item>
        </template>

        <template v-if="node.nodeType === 'TagRelation'">
          <el-form-item label="关联标签">
            <TagSelector :model-value="relatedTagKey" :project="project" @change="changeRelatedTag" />
          </el-form-item>
          <el-form-item label="关系运算符">
            <el-select v-model="node.relationOperator">
              <el-option label=">" value="GreaterThan" />
              <el-option label=">=" value="GreaterThanOrEqual" />
              <el-option label="<" value="LessThan" />
              <el-option label="<=" value="LessThanOrEqual" />
              <el-option label="=" value="Equal" />
              <el-option label="!=" value="NotEqual" />
            </el-select>
          </el-form-item>
          <el-form-item label="关联值乘数">
            <el-input-number v-model="node.relationMultiplier" :controls="false" />
          </el-form-item>
          <el-form-item label="关联值偏移">
            <el-input-number v-model="node.relationOffset" :controls="false" />
          </el-form-item>
        </template>

        <template v-if="node.nodeType === 'ContextGate'">
          <el-form-item label="上下文标签（可选）">
            <TagSelector :model-value="contextTagKey" :project="project" @change="changeContextTag" />
          </el-form-item>
          <el-form-item label="上下文名称">
            <el-input v-model="node.contextName" />
          </el-form-item>
          <el-form-item label="上下文运算符">
            <el-select v-model="node.contextOperator">
              <el-option label="=" value="Equal" />
              <el-option label="!=" value="NotEqual" />
              <el-option label=">" value="GreaterThan" />
              <el-option label=">=" value="GreaterThanOrEqual" />
              <el-option label="<" value="LessThan" />
              <el-option label="<=" value="LessThanOrEqual" />
            </el-select>
          </el-form-item>
          <el-form-item label="期望值">
            <el-input v-model="node.contextExpectedValue" />
          </el-form-item>
        </template>

        <template v-if="node.nodeType === 'AlarmLifecycle'">
          <el-form-item label="告警级别">
            <el-select v-model="node.alarmSeverity">
              <el-option label="Info" value="Info" />
              <el-option label="Warning" value="Warning" />
              <el-option label="Critical" value="Critical" />
            </el-select>
          </el-form-item>
          <el-form-item label="抑制重复（秒）">
            <el-input-number v-model="node.alarmSuppressSeconds" :min="0" :controls="false" />
          </el-form-item>
          <el-form-item label="再次触发间隔（秒）">
            <el-input-number v-model="node.alarmReTriggerSeconds" :min="0" :controls="false" />
          </el-form-item>
          <el-form-item label="升级确认（秒）">
            <el-input-number v-model="node.alarmEscalateAfterSeconds" :min="0" :controls="false" />
          </el-form-item>
        </template>

        <template v-if="node.nodeType === 'ActionPolicy'">
          <el-form-item label="动作延迟（秒）">
            <el-input-number v-model="node.actionDelaySeconds" :min="0" :controls="false" />
          </el-form-item>
          <el-form-item label="动作冷却（秒）">
            <el-input-number v-model="node.actionCooldownSeconds" :min="0" :controls="false" />
          </el-form-item>
          <el-form-item label="每分钟最多动作数">
            <el-input-number v-model="node.actionMaxPerMinute" :min="0" :controls="false" />
          </el-form-item>
        </template>

        <template v-if="node.nodeType === 'DebugProbe'">
          <el-form-item label="启用调试">
            <el-switch v-model="node.debugEnabled" />
          </el-form-item>
          <el-form-item label="调试标签">
            <el-input v-model="node.debugLabel" />
          </el-form-item>
          <el-form-item label="触发时记录">
            <el-switch v-model="node.executeOnActive" />
          </el-form-item>
          <el-form-item label="恢复时记录">
            <el-switch v-model="node.executeOnClear" />
          </el-form-item>
          <el-form-item label="调试消息">
            <el-input v-model="node.activeMessage" type="textarea" :rows="2" />
          </el-form-item>
        </template>

        <template v-if="node.nodeType === 'Transform' || node.nodeType === 'Function'">
          <el-form-item v-if="node.nodeType === 'Transform'" label="取绝对值">
            <el-switch v-model="node.transformUseAbsolute" />
          </el-form-item>
          <el-form-item v-if="node.nodeType === 'Transform'" label="乘数">
            <el-input-number v-model="node.transformMultiplier" :controls="false" />
          </el-form-item>
          <el-form-item v-if="node.nodeType === 'Transform'" label="偏移">
            <el-input-number v-model="node.transformOffset" :controls="false" />
          </el-form-item>
          <el-form-item :label="node.nodeType === 'Function' ? '函数表达式' : '表达式'">
            <el-input
              v-model="node.transformExpression"
              type="textarea"
              :rows="3"
              placeholder="{value} * 10 + 5，支持 abs/round/floor/ceil"
            />
          </el-form-item>
          <el-form-item label="超时（ms）">
            <el-input-number v-model="node.transformTimeoutMilliseconds" :min="1" :max="5000" :controls="false" />
          </el-form-item>
        </template>

        <template v-if="node.nodeType === 'ValueScript'">
          <el-alert title="规则节点固定使用所选脚本的发布版本；如需升级版本，请重新选择脚本。" type="info" :closable="false" />
          <el-form-item label="值处理脚本">
            <el-select :model-value="node.valueScriptId" filterable style="width: 100%" @change="changeValueScript">
              <el-option
                v-for="script in ruleValueScripts"
                :key="script.id"
                :label="`${script.name}（v${script.version}）`"
                :value="script.id"
              />
            </el-select>
          </el-form-item>
          <el-form-item label="固定发布版本">
            <el-input-number v-model="node.valueScriptVersion" :disabled="true" :controls="false" />
          </el-form-item>
          <el-form-item label="输入 / 输出">
            <el-input :model-value="`${node.valueScriptInputDataType || '-'} → ${node.valueScriptOutputDataType || '-'}`" disabled />
          </el-form-item>
          <el-form-item label="超时（ms）">
            <el-input-number v-model="node.transformTimeoutMilliseconds" :min="10" :max="5000" :controls="false" />
          </el-form-item>
        </template>

        <el-form-item v-if="node.nodeType === 'Logic'" label="组合方式">
          <el-segmented v-model="node.logicalOperator" :options="['And', 'Or']" />
        </el-form-item>

        <el-form-item v-if="node.nodeType === 'Duration'" label="持续时间（秒）">
          <el-input-number v-model="node.durationSeconds" :min="0" :controls="false" />
        </el-form-item>
        <el-form-item v-if="node.nodeType === 'Duration'" label="恢复确认（秒）">
          <el-input-number v-model="node.clearDurationSeconds" :min="0" :controls="false" />
        </el-form-item>

        <template v-if="node.nodeType === 'Sequence'">
          <el-form-item label="总窗口（秒）">
            <el-input-number v-model="node.sequenceWindowSeconds" :min="1" :controls="false" />
          </el-form-item>
          <el-form-item label="单步超时（秒）">
            <el-input-number v-model="node.sequenceStepTimeoutSeconds" :min="0" :controls="false" />
          </el-form-item>
          <el-form-item label="最小间隔（秒）">
            <el-input-number v-model="node.sequenceMinIntervalSeconds" :min="0" :controls="false" />
          </el-form-item>
          <el-form-item label="错序时重置">
            <el-switch v-model="node.sequenceResetOnMismatch" />
          </el-form-item>
        </template>

        <template v-if="node.nodeType === 'MqttPublish'">
          <el-form-item label="发布到 MQTT">
            <el-switch v-model="node.publishToMqtt" />
          </el-form-item>
          <el-form-item label="Topic 模板">
            <el-input v-model="node.topicTemplate" />
          </el-form-item>
          <el-form-item label="QoS">
            <el-select v-model="node.publishQos">
              <el-option label="0" :value="0" />
              <el-option label="1" :value="1" />
              <el-option label="2" :value="2" />
            </el-select>
          </el-form-item>
          <el-form-item label="恢复时发布">
            <el-switch v-model="node.publishOnClear" />
          </el-form-item>
          <el-form-item label="触发消息">
            <el-input v-model="node.activeMessage" type="textarea" :rows="2" />
          </el-form-item>
          <el-form-item label="恢复消息">
            <el-input v-model="node.clearMessage" type="textarea" :rows="2" />
          </el-form-item>
        </template>

        <template v-if="node.nodeType === 'EmailNotify'">
          <el-form-item label="触发时发送">
            <el-switch v-model="node.executeOnActive" />
          </el-form-item>
          <el-form-item label="恢复时发送">
            <el-switch v-model="node.executeOnClear" />
          </el-form-item>
          <el-form-item label="SMTP 服务器">
            <el-input v-model="node.emailSmtpHost" />
          </el-form-item>
          <el-form-item label="SMTP 端口">
            <el-input-number v-model="node.emailSmtpPort" :min="1" :controls="false" />
          </el-form-item>
          <el-form-item label="启用 SSL">
            <el-switch v-model="node.emailEnableSsl" />
          </el-form-item>
          <el-form-item label="用户名">
            <el-input v-model="node.emailUsername" />
          </el-form-item>
          <el-form-item label="密码">
            <el-input v-model="node.emailPassword" type="password" show-password />
          </el-form-item>
          <el-form-item label="发件人">
            <el-input v-model="node.emailFrom" />
          </el-form-item>
          <el-form-item label="收件人">
            <el-input v-model="node.emailTo" />
          </el-form-item>
          <el-form-item label="抄送">
            <el-input v-model="node.emailCc" />
          </el-form-item>
          <el-form-item label="主题模板">
            <el-input v-model="node.emailSubjectTemplate" />
          </el-form-item>
          <el-form-item label="正文模板">
            <el-input v-model="node.emailBodyTemplate" type="textarea" :rows="4" />
          </el-form-item>
        </template>

        <template v-if="node.nodeType === 'WebhookCall'">
          <el-form-item label="触发时调用">
            <el-switch v-model="node.executeOnActive" />
          </el-form-item>
          <el-form-item label="恢复时调用">
            <el-switch v-model="node.executeOnClear" />
          </el-form-item>
          <el-form-item label="URL">
            <el-input v-model="node.webhookUrl" />
          </el-form-item>
          <el-form-item label="Method">
            <el-select v-model="node.webhookMethod">
              <el-option label="POST" value="POST" />
              <el-option label="PUT" value="PUT" />
              <el-option label="PATCH" value="PATCH" />
              <el-option label="GET" value="GET" />
              <el-option label="DELETE" value="DELETE" />
            </el-select>
          </el-form-item>
          <el-form-item label="Content-Type">
            <el-input v-model="node.webhookContentType" />
          </el-form-item>
          <el-form-item label="Headers">
            <el-input v-model="node.webhookHeaders" type="textarea" :rows="3" />
          </el-form-item>
          <el-form-item label="Body 模板">
            <el-input v-model="node.webhookBodyTemplate" type="textarea" :rows="5" />
          </el-form-item>
          <el-form-item label="超时（秒）">
            <el-input-number v-model="node.webhookTimeoutSeconds" :min="1" :controls="false" />
          </el-form-item>
          <el-form-item label="失败重试">
            <el-input-number v-model="node.webhookRetryCount" :min="0" :controls="false" />
          </el-form-item>
        </template>
      </el-form>
    </template>
  </aside>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import type { FlowRuleNode, ProjectConfig } from '../api'
import type { ValueTransformCatalogItem } from '../scriptingApi'
import { Plus } from '@element-plus/icons-vue'
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

const tagKey = computed(() => props.node
  ? findTagSelectionKey(props.project, {
    sourceTagId: props.node.tagId
  })
  : '')

const relatedTagKey = computed(() => props.node
  ? findTagSelectionKey(props.project, {
    sourceTagId: props.node.relatedTagId
  })
  : '')

const contextTagKey = computed(() => props.node
  ? findTagSelectionKey(props.project, {
    sourceTagId: props.node.contextTagId
  })
  : '')

const qualityValuesList = computed<string[]>({
  get() {
    return (props.node?.qualityValues || 'Good')
      .split(/[,\s;|]+/)
      .map(item => item.trim())
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

const needsTagSource = computed(() => !!props.node && [
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
].includes(props.node.nodeType))
const ruleValueScripts = computed(() => props.valueScripts.filter(script => script.scope !== 'TagCleaning'))

/**
 * 替换规则节点使用的脚本并固定当前发布版本及类型元数据。
 */
function changeValueScript(scriptId: string) {
  if (!props.node) return
  const script = ruleValueScripts.value.find(item => item.id === scriptId)
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
    name: `级别 ${props.node.alarmLevels.length + 1}`,
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
