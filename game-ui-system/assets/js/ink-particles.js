/**
 * 混沌世界 MMORPG — 水墨粒子动效系统（运行时）
 * Ink Particles Runtime — 春意青金灰沉浸式 UI 动效
 *
 * 暴露：window.InkParticles
 * 方法：burst(x, y, type)、ripple(el)、firefly(el)、startAmbient()、stopAmbient()
 *
 * 事件协议：
 * - click 委托：匹配 [data-particle] 或 .ds-btn → burst
 * - panel:show 自定义事件 → ripple
 * - toast:show 自定义事件 → firefly
 * - DOMContentLoaded → startAmbient()
 *
 * 动效曲线：cubic-bezier(0.16, 1, 0.3, 1) — ease-out，参考苹果 HIG
 */
(function () {
  'use strict';

  // ═══════════════════════════════════════════════════════════════
  // 配置
  // ═══════════════════════════════════════════════════════════════
  var CONFIG = {
    burstCount: 14,        // 金粉粒子数量
    burstCountMax: 16,     // 金粉粒子上限
    fireflyCount: 7,       // 青玉萤光数量
    ambientCount: 20,      // 环境微粒数量
    burstDuration: 800,   // 金粉持续时长 ms
    rippleDuration: 1200, // 涟漪持续时长 ms
    fireflyDuration: 1000, // 萤光持续时长 ms
    ambientDuration: 12000, // 环境微粒生命周期 ms
    easing: 'cubic-bezier(0.16, 1, 0.3, 1)',
  };

  // ═══════════════════════════════════════════════════════════════
  // 状态
  // ═══════════════════════════════════════════════════════════════
  var layer = null;
  var ambientTimer = null;
  var ambientNodes = [];
  var reducedMotion = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  // ═══════════════════════════════════════════════════════════════
  // 工具函数
  // ═══════════════════════════════════════════════════════════════
  function rand(min, max) {
    return Math.random() * (max - min) + min;
  }

  function randInt(min, max) {
    return Math.floor(rand(min, max + 1));
  }

  function ensureLayer() {
    if (layer && document.body.contains(layer)) return layer;
    layer = document.createElement('div');
    layer.className = 'ink-particle-layer';
    layer.setAttribute('aria-hidden', 'true');
    document.body.appendChild(layer);
    return layer;
  }

  function removeNode(node, delay) {
    setTimeout(function () {
      if (node && node.parentNode) {
        node.parentNode.removeChild(node);
      }
    }, delay);
  }

  // ═══════════════════════════════════════════════════════════════
  // 1. 金粉爆发（gold-burst）
  // ═══════════════════════════════════════════════════════════════
  function burst(x, y, type) {
    if (reducedMotion) return;
    var root = ensureLayer();
    var count = randInt(CONFIG.burstCount, CONFIG.burstCountMax);
    var particleType = type === 'jade' ? 'ink-particle--jade' : 'ink-particle--gold';

    for (var i = 0; i < count; i++) {
      var p = document.createElement('div');
      p.className = 'ink-particle ' + particleType;
      if (Math.random() > 0.7) p.classList.add('lg');

      // 随机角度与距离
      var angle = (Math.PI * 2 * i) / count + rand(-0.3, 0.3);
      var distance = rand(40, 90);
      var dx = Math.cos(angle) * distance;
      var dy = Math.sin(angle) * distance + rand(20, 50); // 重力下坠

      p.style.left = x + 'px';
      p.style.top = y + 'px';
      p.style.setProperty('--dx', dx + 'px');
      p.style.setProperty('--dy', dy + 'px');
      p.style.animation = 'gold-burst ' + CONFIG.burstDuration + 'ms ' + CONFIG.easing + ' forwards';
      p.style.animationDelay = rand(0, 80) + 'ms';

      root.appendChild(p);
      removeNode(p, CONFIG.burstDuration + 120);
    }
  }

  // ═══════════════════════════════════════════════════════════════
  // 2. 墨韵涟漪（ink-ripple）
  // ═══════════════════════════════════════════════════════════════
  function ripple(el) {
    if (reducedMotion) return;
    if (!el || !el.getBoundingClientRect) return;
    var root = ensureLayer();
    var rect = el.getBoundingClientRect();
    var cx = rect.left + rect.width / 2;
    var cy = rect.top + rect.height / 2;
    var size = Math.max(rect.width, rect.height);

    // 第一圈涟漪
    var ring1 = document.createElement('div');
    ring1.className = 'ink-ripple-ring';
    ring1.style.left = cx + 'px';
    ring1.style.top = cy + 'px';
    ring1.style.width = size + 'px';
    ring1.style.height = size + 'px';
    ring1.style.transform = 'translate(-50%, -50%) scale(0)';
    ring1.style.animation = 'ink-ripple ' + CONFIG.rippleDuration + 'ms ' + CONFIG.easing + ' forwards';
    root.appendChild(ring1);
    removeNode(ring1, CONFIG.rippleDuration + 100);

    // 第二圈涟漪（延迟 30%）
    var ring2 = document.createElement('div');
    ring2.className = 'ink-ripple-ring ink-ripple-ring--gold';
    ring2.style.left = cx + 'px';
    ring2.style.top = cy + 'px';
    ring2.style.width = (size * 1.2) + 'px';
    ring2.style.height = (size * 1.2) + 'px';
    ring2.style.transform = 'translate(-50%, -50%) scale(0)';
    ring2.style.animation = 'ink-ripple-second ' + CONFIG.rippleDuration + 'ms ' + CONFIG.easing + ' forwards';
    root.appendChild(ring2);
    removeNode(ring2, CONFIG.rippleDuration + 100);
  }

  // ═══════════════════════════════════════════════════════════════
  // 3. 青玉萤光（jade-firefly）
  // ═══════════════════════════════════════════════════════════════
  function firefly(el) {
    if (reducedMotion) return;
    if (!el) el = document.body;
    if (!el.getBoundingClientRect) return;
    var root = ensureLayer();
    var rect = el.getBoundingClientRect();

    for (var i = 0; i < CONFIG.fireflyCount; i++) {
      var p = document.createElement('div');
      p.className = 'ink-particle ink-particle--jade';

      // 从元素边缘随机位置出发
      var side = randInt(0, 3); // 0=上 1=右 2=下 3=左
      var startX, startY;
      if (side === 0) { startX = rand(rect.left, rect.right); startY = rect.top; }
      else if (side === 1) { startX = rect.right; startY = rand(rect.top, rect.bottom); }
      else if (side === 2) { startX = rand(rect.left, rect.right); startY = rect.bottom; }
      else { startX = rect.left; startY = rand(rect.top, rect.bottom); }

      // 飘向元素外围
      var angle = rand(0, Math.PI * 2);
      var distance = rand(30, 70);
      var dx = Math.cos(angle) * distance;
      var dy = Math.sin(angle) * distance;

      p.style.left = startX + 'px';
      p.style.top = startY + 'px';
      p.style.setProperty('--dx', dx + 'px');
      p.style.setProperty('--dy', dy + 'px');
      p.style.animation = 'jade-firefly ' + CONFIG.fireflyDuration + 'ms ' + CONFIG.easing + ' forwards';
      p.style.animationDelay = rand(0, 200) + 'ms';

      root.appendChild(p);
      removeNode(p, CONFIG.fireflyDuration + 250);
    }
  }

  // ═══════════════════════════════════════════════════════════════
  // 4. 环境水墨微粒（ambient）
  // ═══════════════════════════════════════════════════════════════
  function startAmbient() {
    if (reducedMotion) return;
    if (ambientTimer) return; // 已启动
    var root = ensureLayer();
    var w = window.innerWidth;
    var h = window.innerHeight;

    function spawn() {
      var p = document.createElement('div');
      var isGold = Math.random() > 0.6;
      p.className = 'ink-ambient' + (isGold ? ' ink-ambient--gold' : '');

      var startX = rand(0, w);
      var startY = rand(0, h);
      var dx = rand(-60, 60);
      var dy = rand(-100, -20); // 整体向上飘
      var dxMid = dx * 0.5 + rand(-20, 20);
      var dyMid = dy * 0.5;
      var maxOpacity = rand(0.3, 0.7);
      var duration = rand(8000, CONFIG.ambientDuration);

      p.style.left = startX + 'px';
      p.style.top = startY + 'px';
      p.style.setProperty('--dx', dx + 'px');
      p.style.setProperty('--dy', dy + 'px');
      p.style.setProperty('--dx-mid', dxMid + 'px');
      p.style.setProperty('--dy-mid', dyMid + 'px');
      p.style.setProperty('--max-opacity', maxOpacity);
      p.style.animation = 'ambient-drift ' + duration + 'ms ' + CONFIG.easing + ' forwards';

      root.appendChild(p);
      ambientNodes.push(p);
      removeNode(p, duration + 100);

      // 清理已移除的节点引用
      ambientNodes = ambientNodes.filter(function (n) { return n.parentNode; });
    }

    // 初始批量生成
    for (var i = 0; i < CONFIG.ambientCount; i++) {
      setTimeout(spawn, i * 200);
    }

    // 持续补充
    ambientTimer = setInterval(function () {
      if (ambientNodes.length < CONFIG.ambientCount) {
        spawn();
      }
    }, 800);
  }

  function stopAmbient() {
    if (ambientTimer) {
      clearInterval(ambientTimer);
      ambientTimer = null;
    }
    ambientNodes.forEach(function (n) {
      if (n && n.parentNode) n.parentNode.removeChild(n);
    });
    ambientNodes = [];
  }

  // ═══════════════════════════════════════════════════════════════
  // 事件绑定
  // ═══════════════════════════════════════════════════════════════
  function shouldTriggerBurst(target) {
    // 匹配 [data-particle] 或 .ds-btn，但排除禁用元素
    if (!target || target.disabled) return false;
    if (target.closest('[disabled]')) return false;
    if (target.matches('[data-particle]') || target.closest('[data-particle]')) return true;
    if (target.matches('.ds-btn') || target.closest('.ds-btn')) return true;
    return false;
  }

  function getParticleType(target) {
    var el = target.matches('[data-particle]') ? target : target.closest('[data-particle]');
    if (el) {
      var val = el.getAttribute('data-particle');
      if (val === 'jade-burst') return 'jade';
      if (val === 'gold-burst') return 'gold';
    }
    return 'gold'; // 默认金粉
  }

  function bindEvents() {
    // 点击委托 — 金粉爆发
    document.addEventListener('click', function (e) {
      var target = e.target;
      if (shouldTriggerBurst(target)) {
        var type = getParticleType(target);
        burst(e.clientX, e.clientY, type);
      }
    }, { passive: true });

    // 面板切换 — 墨韵涟漪
    document.addEventListener('panel:show', function (e) {
      var target = e.target || e.detail;
      ripple(target);
    });

    // 信息提示 — 青玉萤光
    document.addEventListener('toast:show', function (e) {
      var target = e.target || e.detail;
      firefly(target);
    });

    // 窗口尺寸变化时重置环境微粒
    var resizeTimer = null;
    window.addEventListener('resize', function () {
      if (resizeTimer) clearTimeout(resizeTimer);
      resizeTimer = setTimeout(function () {
        stopAmbient();
        startAmbient();
      }, 300);
    });
  }

  // ═══════════════════════════════════════════════════════════════
  // 初始化
  // ═══════════════════════════════════════════════════════════════
  function init() {
    bindEvents();
    startAmbient();
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }

  // ═══════════════════════════════════════════════════════════════
  // 暴露 API
  // ═══════════════════════════════════════════════════════════════
  window.InkParticles = {
    burst: burst,
    ripple: ripple,
    firefly: firefly,
    startAmbient: startAmbient,
    stopAmbient: stopAmbient,
    config: CONFIG
  };
})();
