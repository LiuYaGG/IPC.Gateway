<template>
  <section class="topology-legend">
    <span><i class="tone-good" /> 正常</span>
    <span><i class="tone-warn" /> 预警</span>
    <span><i class="tone-bad" /> 异常</span>
    <span><i class="tone-disabled" /> 停用</span>
    <span><i class="tone-normal" /> 未知/无数据</span>
  </section>
</template>

<style scoped>
.topology-legend {
  display: flex;
  flex-wrap: wrap;
  gap: 10px 16px;
  align-items: center;
  color: #64748b;
  font-size: 12px;
}

.topology-legend span {
  display: inline-flex;
  align-items: center;
  gap: 6px;
}

.topology-legend i {
  width: 10px;
  height: 10px;
  border-radius: 999px;
}

.tone-good {
  background: #16a34a;
}

.tone-warn {
  background: #d97706;
}

.tone-bad {
  background: #dc2626;
}

.tone-disabled {
  background: #94a3b8;
}

.tone-normal {
  background: #2563eb;
}
</style>
