<template>
  <div class="login-monet-backdrop" aria-hidden="true">
    <div class="login-monet-glow login-monet-glow--one"></div>
    <div class="login-monet-glow login-monet-glow--two"></div>
    <div class="login-monet-ribbon login-monet-ribbon--one"></div>
    <div class="login-monet-ribbon login-monet-ribbon--two"></div>
  </div>
</template>

<style scoped>
.login-monet-backdrop {
  position: absolute;
  inset: 0;
  overflow: hidden;
  pointer-events: none;
  background: #f1f6fb;
}
.login-monet-glow {
  position: absolute;
  border-radius: 50%;
  filter: blur(40px);
}
.login-monet-glow--one {
  width: 65%;
  height: 85%;
  left: -20%;
  top: -24%;
  background: radial-gradient(ellipse, #a9c8e272, #bad6e93e 55%, transparent 75%);
  animation: monet-login-float 20s ease-in-out infinite alternate;
}
.login-monet-glow--two {
  width: 72%;
  height: 97%;
  right: -25%;
  bottom: -40%;
  background: radial-gradient(ellipse, #b5d0e76b, #d6e7f32d 60%, transparent 75%);
  animation: monet-login-float 24s ease-in-out infinite alternate-reverse;
}
.login-monet-ribbon {
  position: absolute;
  width: max(1050px, 100%);
  height: max(420px, 65%);
  border: 45px solid #a9c8e222;
  border-radius: 50%;
  transform: rotate(-30deg);
  box-shadow: 0 0 0 18px #d5e4f022, inset 0 0 40px #a3bfda12;
}
.login-monet-ribbon--one {
  right: 65%;
  top: 21%;
}
.login-monet-ribbon--two {
  left: 67%;
  top: 6%;
  border-color: #b6cfe527;
}
@keyframes monet-login-float {
  to { transform: translate(55px, 35px) scale(1.06); }
}
@media (prefers-reduced-motion: reduce) {
  .login-monet-glow { animation: none; }
}
</style>
