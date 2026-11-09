<template>
  <span class="gateway-logo"><img :src="logoUrl" alt="IPC Gateway Logo" /></span>
</template>
<script setup lang="ts">
import logoUrl from '../assets/logo.png'
</script>
<style scoped>
.gateway-logo { display: inline-flex; align-items: center; justify-content: center; flex-shrink: 0; width: 42px; height: 42px; overflow: hidden; }
.gateway-logo img { display: block; width: 100%; height: 100%; object-fit: contain; transform: scale(2.1); }
</style>
