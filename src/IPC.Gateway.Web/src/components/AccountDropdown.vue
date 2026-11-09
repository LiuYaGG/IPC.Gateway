<template>
  <a-dropdown v-model:open="open" :trigger="['click']" placement="bottomRight">
    <button class="account-menu" type="button" :aria-label="tx('用户菜单')" :aria-expanded="open">
      <a-avatar :size="26"><template #icon><UserOutlined /></template></a-avatar>
      <span class="ui-icon"><DownOutlined /></span>
    </button>
    <template #overlay>
      <section class="account-card" :aria-label="tx('当前登录账户')">
        <div class="account-card__profile">
          <span class="account-card__caption">{{ tx('当前登录账户') }}</span>
          <div class="account-card__identity">
            <a-avatar class="account-card__avatar" :size="32"><template #icon><UserOutlined /></template></a-avatar>
            <div class="account-card__person">
              <strong>{{ displayName || username || tx('用户') }}</strong>
              <span class="account-card__login"><span>{{ tx('登录名') }}</span><span>{{ username || '—' }}</span></span>
            </div>
          </div>
        </div>
        <div class="account-card__actions">
          <a-button type="text" class="account-card__action" @click="choose('password')"><template #icon><KeyOutlined /></template>{{ tx('修改密码') }}</a-button>
          <span class="account-card__divider" aria-hidden="true"></span>
          <a-button type="text" class="account-card__action account-card__logout" @click="choose('logout')"><template #icon><LogoutOutlined /></template>{{ tx('退出登录') }}</a-button>
        </div>
      </section>
    </template>
  </a-dropdown>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { DownOutlined, UserOutlined, KeyOutlined, LogoutOutlined } from '@ant-design/icons-vue'
import { tx } from '../i18n'

defineProps<{ displayName?: string; username?: string }>()
const emit = defineEmits<{ password: []; logout: [] }>()
const open = ref(false)
function choose(action: 'password' | 'logout') {
  open.value = false
  if (action === 'password') emit('password')
  else emit('logout')
}
</script>

<style scoped>
.account-card {
  width: 246px;
  max-width: calc(100vw - 24px);
  overflow: hidden;
  color: #000;
  background: #fff;
  border: 1px solid #e0e9f2;
  border-radius: 6px;
  box-shadow: 0 5px 18px rgb(52 67 89 / 7%);
  font-family: 'Cascadia Code', 'Microsoft YaHei', sans-serif;
  line-height: 1.5;
}
.account-card__profile { padding: 13px 14px 15px; background: #edf4fa; }
.account-card__caption { display: block; margin-bottom: 11px; color: #71869b; font-size: calc(10px + 1pt); letter-spacing: .4px; }
.account-card__identity { display: flex; align-items: center; gap: 10px; }
.account-card__avatar.ant-avatar { flex-shrink: 0; color: #567b9f; background: #fff; border: 1px solid #e0e9f2; }
.account-card__person { display: flex; flex-direction: column; min-width: 0; gap: 5px; }
.account-card__person strong { font-size: calc(11px + 1pt); line-height: 1.4; font-weight: 600; overflow-wrap: anywhere; }
.account-card__login { display: flex; flex-wrap: wrap; gap: 3px 9px; color: #7a8a9e; font-size: calc(10px + 1pt); }
.account-card__login > span:last-child { min-width: 0; color: #456989; font-size: calc(11px + 1pt); overflow-wrap: anywhere; }
.account-card__actions { display: grid; grid-template-columns: minmax(0, 1fr) 1px minmax(0, 1fr); gap: 5px; align-items: stretch; min-height: 46px; padding: 5px 8px; }
.account-card__action.ant-btn { width: 100%; height: auto; min-height: 34px; padding: 5px 2px; column-gap: 7px; border: 0; border-radius: 4px; background: #fff; color: #52677c; font-size: calc(11px + 1pt); white-space: normal; line-height: 1.4; }
.account-card__action :deep(.anticon) { color: #7c91a4; font-size: 14px; }
.account-card__action.ant-btn:hover { background: #f0f6fc; color: #456989; }
.account-card__logout.ant-btn:hover { background: #fcf4f4; color: #b75959; }
.account-card__logout.ant-btn:hover :deep(.anticon) { color: inherit; }
.account-card__divider { align-self: center; height: 14px; background: #e7eef5; }
@media (pointer: coarse) { .account-card__action.ant-btn { min-height: 44px; } }
</style>
