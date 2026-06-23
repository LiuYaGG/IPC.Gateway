<template>
  <div class="login-backdrop" aria-hidden="true">
    <div class="login-backdrop__plant">
      <span class="plant-line plant-line--rail"></span>
      <span class="plant-line plant-line--belt"></span>
      <span class="plant-line plant-line--arm plant-line--arm-a"></span>
      <span class="plant-line plant-line--arm plant-line--arm-b"></span>
      <span class="plant-line plant-line--column plant-line--column-a"></span>
      <span class="plant-line plant-line--column plant-line--column-b"></span>
      <span class="plant-node plant-node--a"></span>
      <span class="plant-node plant-node--b"></span>
      <span class="plant-node plant-node--c"></span>
    </div>

    <div class="login-backdrop__data login-backdrop__data--fast">
      <span></span><span></span><span></span><span></span>
    </div>
    <div class="login-backdrop__data login-backdrop__data--slow">
      <span></span><span></span><span></span>
    </div>

    <div class="login-backdrop__telemetry">
      <span class="telemetry-dot telemetry-dot--a"></span>
      <span class="telemetry-dot telemetry-dot--b"></span>
      <span class="telemetry-dot telemetry-dot--c"></span>
      <span class="telemetry-dot telemetry-dot--d"></span>
    </div>
  </div>
</template>

<style scoped>
.login-backdrop {
  position: absolute;
  inset: 0;
  overflow: hidden;
  pointer-events: none;
  background:
    radial-gradient(circle at 18% 22%, rgba(34, 211, 238, 0.2), transparent 28%),
    radial-gradient(circle at 78% 28%, rgba(20, 184, 166, 0.18), transparent 30%),
    linear-gradient(145deg, #06111b 0%, #0b1722 46%, #101827 100%);
}

.login-backdrop::before,
.login-backdrop::after {
  content: "";
  position: absolute;
  inset: 0;
}

.login-backdrop::before {
  opacity: 0.3;
  background:
    linear-gradient(rgba(56, 189, 248, 0.16) 1px, transparent 1px),
    linear-gradient(90deg, rgba(20, 184, 166, 0.14) 1px, transparent 1px);
  background-size: 54px 54px;
  mask-image: linear-gradient(90deg, transparent, #000 18%, #000 82%, transparent);
}

.login-backdrop::after {
  background:
    linear-gradient(90deg, rgba(6, 17, 27, 0.84), rgba(6, 17, 27, 0.2) 44%, rgba(6, 17, 27, 0.86)),
    linear-gradient(0deg, rgba(6, 17, 27, 0.34), transparent 34%, rgba(6, 17, 27, 0.78));
}

.login-backdrop__plant {
  position: absolute;
  inset: 12% 6% 10%;
  opacity: 0.88;
  filter: drop-shadow(0 0 22px rgba(45, 212, 191, 0.16));
}

.plant-line,
.plant-node {
  position: absolute;
  display: block;
}

.plant-line {
  background: linear-gradient(90deg, rgba(148, 163, 184, 0.12), rgba(45, 212, 191, 0.54), rgba(148, 163, 184, 0.16));
  border-radius: 999px;
}

.plant-line--rail {
  left: 5%;
  right: 4%;
  bottom: 18%;
  height: 2px;
}

.plant-line--belt {
  left: 10%;
  right: 14%;
  bottom: 28%;
  height: 8px;
  background: linear-gradient(90deg, rgba(71, 85, 105, 0.28), rgba(45, 212, 191, 0.34), rgba(59, 130, 246, 0.26));
  box-shadow: 0 0 28px rgba(45, 212, 191, 0.12);
}

.plant-line--arm {
  width: 26%;
  height: 3px;
  transform-origin: left center;
}

.plant-line--arm-a {
  left: 23%;
  top: 44%;
  transform: rotate(-28deg);
}

.plant-line--arm-b {
  right: 20%;
  top: 39%;
  transform: rotate(24deg);
}

.plant-line--column {
  width: 4px;
  bottom: 18%;
  background: linear-gradient(180deg, rgba(148, 163, 184, 0.16), rgba(45, 212, 191, 0.46));
}

.plant-line--column-a {
  left: 22%;
  height: 44%;
}

.plant-line--column-b {
  right: 24%;
  height: 50%;
}

.plant-node {
  width: 13px;
  height: 13px;
  border: 2px solid rgba(125, 249, 233, 0.86);
  border-radius: 50%;
  background: rgba(6, 17, 27, 0.78);
  box-shadow: 0 0 20px rgba(45, 212, 191, 0.5);
  animation: loginPulse 2.8s ease-in-out infinite;
}

.plant-node--a {
  left: 21.4%;
  top: 42%;
}

.plant-node--b {
  right: 23.4%;
  top: 37%;
  animation-delay: 0.6s;
}

.plant-node--c {
  left: 54%;
  bottom: 25%;
  animation-delay: 1.2s;
}

.login-backdrop__data {
  position: absolute;
  inset: 0;
}

.login-backdrop__data span {
  position: absolute;
  left: -18%;
  width: 18%;
  height: 2px;
  border-radius: 999px;
  background: linear-gradient(90deg, transparent, rgba(45, 212, 191, 0.92), rgba(56, 189, 248, 0.12));
  box-shadow: 0 0 16px rgba(45, 212, 191, 0.5);
  animation: loginDataFlow 7.6s linear infinite;
}

.login-backdrop__data--fast span:nth-child(1) { top: 22%; animation-delay: 0s; }
.login-backdrop__data--fast span:nth-child(2) { top: 36%; animation-delay: 1.7s; }
.login-backdrop__data--fast span:nth-child(3) { top: 58%; animation-delay: 3.1s; }
.login-backdrop__data--fast span:nth-child(4) { top: 72%; animation-delay: 5s; }
.login-backdrop__data--slow span { animation-duration: 11s; opacity: 0.58; }
.login-backdrop__data--slow span:nth-child(1) { top: 30%; animation-delay: 1s; }
.login-backdrop__data--slow span:nth-child(2) { top: 49%; animation-delay: 4.2s; }
.login-backdrop__data--slow span:nth-child(3) { top: 66%; animation-delay: 7s; }

.telemetry-dot {
  position: absolute;
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: #2dd4bf;
  box-shadow: 0 0 0 6px rgba(45, 212, 191, 0.1), 0 0 22px rgba(45, 212, 191, 0.64);
  animation: loginTelemetry 3.6s ease-in-out infinite;
}

.telemetry-dot--a { left: 12%; top: 22%; }
.telemetry-dot--b { right: 16%; top: 24%; animation-delay: 0.7s; }
.telemetry-dot--c { left: 18%; bottom: 24%; animation-delay: 1.4s; }
.telemetry-dot--d { right: 24%; bottom: 22%; animation-delay: 2s; }

@keyframes loginDataFlow {
  0% { transform: translateX(0); opacity: 0; }
  12% { opacity: 1; }
  82% { opacity: 1; }
  100% { transform: translateX(760%); opacity: 0; }
}

@keyframes loginPulse {
  0%, 100% { transform: scale(1); opacity: 0.78; }
  50% { transform: scale(1.28); opacity: 1; }
}

@keyframes loginTelemetry {
  0%, 100% { opacity: 0.42; transform: scale(0.9); }
  50% { opacity: 1; transform: scale(1.22); }
}

@media (prefers-reduced-motion: reduce) {
  .login-backdrop__data span,
  .plant-node,
  .telemetry-dot {
    animation: none;
  }
}
</style>
