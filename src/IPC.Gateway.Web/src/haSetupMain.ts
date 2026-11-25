import { createApp } from 'vue'
import AntDesignVue from 'ant-design-vue'
import 'ant-design-vue/dist/reset.css'
import HaSetup from './components/ha/HaSetup.vue'

createApp(HaSetup).use(AntDesignVue).mount('#app')
