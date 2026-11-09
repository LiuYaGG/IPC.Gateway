import { createApp } from 'vue'
import AntDesignVue from 'ant-design-vue'
import 'ant-design-vue/dist/reset.css'
import App from './App.vue'
import GatewayTable from './components/controls/GatewayTable.vue'
import GatewayInput from './components/controls/GatewayInput.vue'
import GatewaySelect from './components/controls/GatewaySelect.vue'
import GatewayNumberInput from './components/controls/GatewayNumberInput.vue'
import GatewayChoice from './components/controls/GatewayChoice.vue'
import GatewayFileUpload from './components/controls/GatewayFileUpload.vue'
import { busyDirective } from './utils/ui'
import './styles.css'
import './monet-theme.css'

createApp(App)
  .use(AntDesignVue)
  .component('GatewayTable', GatewayTable)
  .component('GatewayInput', GatewayInput)
  .component('GatewaySelect', GatewaySelect)
  .component('GatewayNumberInput', GatewayNumberInput)
  .component('GatewayChoice', GatewayChoice)
  .component('GatewayFileUpload', GatewayFileUpload)
  .directive('busy', busyDirective)
  .mount('#app')
