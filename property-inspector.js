(function () {
  'use strict';

  var ACTIONS = {
    gain: 'local.streamdock.voicemeeter.gain',
    mute: 'local.streamdock.voicemeeter.mute',
    solo: 'local.streamdock.voicemeeter.solo',
    mono: 'local.streamdock.voicemeeter.mono',
    overview: 'local.streamdock.voicemeeter.overview',
    balanceDial: 'local.streamdock.voicemeeter.balance-dial',
    outputDevice: 'local.streamdock.voicemeeter.output-device',
    rotateOutputDevice: 'local.streamdock.voicemeeter.rotate-output-device',
    inputDevice: 'local.streamdock.voicemeeter.input-device',
    rotateInputDevice: 'local.streamdock.voicemeeter.rotate-input-device',
    macroButton: 'local.streamdock.voicemeeter.macro-button',
    recorder: 'local.streamdock.voicemeeter.recorder',
    eqToggle: 'local.streamdock.voicemeeter.eq-toggle',
    appControl: 'local.streamdock.voicemeeter.app-control',
    diagnostics: 'local.streamdock.voicemeeter.diagnostics'
  };

  var websocket = null;
  var actionContext = null;
  var propertyInspectorContext = null;
  var currentAction = '';
  var devices = [];
  var deviceRequestTimer = null;
  var settings = {
    channelKind: 'strip',
    channelIndex: 0,
    step: 3,
    deviceId: '',
    macroButtonIndex: 0,
    appCommand: 'show',
    overviewTargets: ['strip:0', 'strip:1', 'strip:2', 'strip:3'],
    balancePrimaryKind: 'strip',
    balancePrimaryIndex: 0,
    balanceSecondaryKind: 'strip',
    balanceSecondaryIndex: 1,
    balanceStep: 1,
    titleLabel: '',
    invertKnob: false
  };

  function byId(id) {
    return document.getElementById(id);
  }

  function connectElgatoStreamDeckSocket(inPort, inPluginUUID, inRegisterEvent, inInfo, inActionInfo) {
    var actionInfo = parseJson(inActionInfo, {});
    actionContext = actionInfo.context || '';
    propertyInspectorContext = inPluginUUID;
    currentAction = actionInfo.action || '';
    settings = Object.assign(settings, normalizeSettings(actionInfo.payload && actionInfo.payload.settings || {}));

    websocket = new WebSocket('ws://127.0.0.1:' + inPort);
    websocket.onopen = function () {
      websocket.send(JSON.stringify({ event: inRegisterEvent, uuid: inPluginUUID }));
      render();
      requestDiagnostics();
      requestDevices();
      requestMacroStatus();
    };
    websocket.onmessage = function (event) {
      var message = parseJson(event.data, {});
      if (message.event === 'didReceiveSettings') {
        settings = Object.assign(settings, normalizeSettings(message.payload && message.payload.settings || {}));
        render();
      } else if (message.event === 'sendToPropertyInspector') {
        handlePluginMessage(message.payload || {});
      }
    };
  }

  function parseJson(value, fallback) {
    try {
      return typeof value === 'string' ? JSON.parse(value) : value;
    } catch {
      return fallback;
    }
  }

  function normalizeChannelKind(kind) {
    return kind === 'bus' ? 'bus' : 'strip';
  }

  function normalizeIndex(value, fallback) {
    var index = Math.round(Number(value));
    if (isNaN(index)) index = fallback;
    return Math.max(0, Math.min(7, index));
  }

  function normalizeStep(value, fallback, min, max) {
    var step = Number(value);
    if (isNaN(step)) step = fallback;
    return Math.max(min, Math.min(max, step));
  }

  function allChannelKeys() {
    var keys = [];
    ['strip', 'bus'].forEach(function (kind) {
      for (var i = 0; i <= 7; i++) keys.push(kind + ':' + i);
    });
    return keys;
  }

  function normalizeOverviewTargets(targets) {
    var all = allChannelKeys();
    if (!Array.isArray(targets)) return settings.overviewTargets.slice();
    var selected = targets.filter(function (target) { return all.indexOf(target) !== -1; });
    selected = selected.filter(function (target, index) { return selected.indexOf(target) === index; });
    return selected.length ? selected.slice(0, 6) : ['strip:0'];
  }

  function normalizeSettings(raw) {
    var normalized = Object.assign({}, raw);
    normalized.channelKind = normalizeChannelKind(normalized.channelKind);
    normalized.channelIndex = normalizeIndex(normalized.channelIndex, 0);
    normalized.step = normalizeStep(normalized.step, 3, 0.1, 24);
    normalized.deviceId = normalized.deviceId || '';
    normalized.macroButtonIndex = Math.max(0, Math.min(79, Math.round(Number(normalized.macroButtonIndex) || 0)));
    normalized.appCommand = ['restart', 'shutdown'].indexOf(normalized.appCommand) !== -1 ? normalized.appCommand : 'show';
    normalized.overviewTargets = normalizeOverviewTargets(normalized.overviewTargets);
    normalized.balancePrimaryKind = normalizeChannelKind(normalized.balancePrimaryKind);
    normalized.balancePrimaryIndex = normalizeIndex(normalized.balancePrimaryIndex, 0);
    normalized.balanceSecondaryKind = normalizeChannelKind(normalized.balanceSecondaryKind);
    normalized.balanceSecondaryIndex = normalizeIndex(normalized.balanceSecondaryIndex, 1);
    normalized.balanceStep = normalizeStep(normalized.balanceStep, 1, 0.1, 12);
    normalized.titleLabel = normalized.titleLabel || '';
    normalized.invertKnob = normalized.invertKnob === true || normalized.invertKnob === 'true' ||
      normalized.invert === true || normalized.invert === 'true';
    delete normalized.invert;
    return normalized;
  }

  function isGain() { return currentAction === ACTIONS.gain; }
  function isMute() { return currentAction === ACTIONS.mute; }
  function isSolo() { return currentAction === ACTIONS.solo; }
  function isMono() { return currentAction === ACTIONS.mono; }
  function isOverview() { return currentAction === ACTIONS.overview; }
  function isBalanceDial() { return currentAction === ACTIONS.balanceDial; }
  function isOutputDevice() { return currentAction === ACTIONS.outputDevice; }
  function isRotateOutputDevice() { return currentAction === ACTIONS.rotateOutputDevice; }
  function isInputDevice() { return currentAction === ACTIONS.inputDevice; }
  function isRotateInputDevice() { return currentAction === ACTIONS.rotateInputDevice; }
  function isMacroButton() { return currentAction === ACTIONS.macroButton; }
  function isRecorder() { return currentAction === ACTIONS.recorder; }
  function isEqToggle() { return currentAction === ACTIONS.eqToggle; }
  function isAppControl() { return currentAction === ACTIONS.appControl; }
  function isDiagnostics() { return currentAction === ACTIONS.diagnostics; }

  function isChannelKindAware() { return isGain() || isMute() || isMono(); }

  function isChannelIndexAware() {
    return isGain() || isMute() || isSolo() || isMono() || isOutputDevice() || isRotateOutputDevice() ||
      isInputDevice() || isRotateInputDevice() || isEqToggle();
  }

  function isDeviceSelectAction() { return isOutputDevice() || isInputDevice(); }

  function isDeviceInfoAction() {
    return isOutputDevice() || isInputDevice() || isRotateOutputDevice() || isRotateInputDevice();
  }

  function isCaptureDeviceAction() { return isInputDevice() || isRotateInputDevice(); }

  function isTitleLabelAware() { return !isOverview() && !isDiagnostics(); }

  function isInvertAware() { return isGain() || isBalanceDial() || isRotateOutputDevice() || isRotateInputDevice(); }

  function channelIndexLabel() {
    if (isSolo() || isInputDevice() || isRotateInputDevice()) return 'Strip index';
    if (isOutputDevice() || isRotateOutputDevice() || isEqToggle()) return 'Bus index';
    return settings.channelKind === 'bus' ? 'Bus index' : 'Strip index';
  }

  function buildOverviewGrid() {
    var grid = byId('overviewGrid');
    grid.innerHTML = '';
    allChannelKeys().forEach(function (key) {
      var label = document.createElement('label');
      var input = document.createElement('input');
      input.type = 'checkbox';
      input.name = 'overviewTarget';
      input.value = key;
      input.addEventListener('change', update);
      label.appendChild(input);
      label.appendChild(document.createTextNode(' ' + key.replace(':', ' ')));
      grid.appendChild(label);
    });
  }

  function render() {
    byId('channelKind').value = settings.channelKind;
    byId('channelIndex').value = settings.channelIndex;
    byId('channelIndexLabel').textContent = channelIndexLabel();
    byId('step').value = settings.step;
    byId('deviceId').value = settings.deviceId;
    byId('macroButtonIndex').value = settings.macroButtonIndex;
    byId('appCommand').value = settings.appCommand;
    byId('balancePrimaryKind').value = settings.balancePrimaryKind;
    byId('balancePrimaryIndex').value = settings.balancePrimaryIndex;
    byId('balanceSecondaryKind').value = settings.balanceSecondaryKind;
    byId('balanceSecondaryIndex').value = settings.balanceSecondaryIndex;
    byId('balanceStep').value = settings.balanceStep;
    byId('titleLabel').value = settings.titleLabel;
    byId('invertKnob').checked = !!settings.invertKnob;
    Array.prototype.forEach.call(document.querySelectorAll('input[name="overviewTarget"]'), function (input) {
      input.checked = settings.overviewTargets.indexOf(input.value) !== -1;
    });
    renderDeviceOptions();

    toggle('.channel-kind-settings', !isChannelKindAware());
    toggle('.channel-index-settings', !isChannelIndexAware());
    toggle('.gain-step-settings', !isGain());
    toggle('.overview-settings', !isOverview());
    toggle('.balance-settings', !isBalanceDial());
    toggle('.device-select-settings', !isDeviceSelectAction());
    toggle('.device-info-settings', !isDeviceInfoAction());
    toggle('.macro-settings', !isMacroButton());
    toggle('.app-command-settings', !isAppControl());
    toggle('.title-label-settings', !isTitleLabelAware());
    toggle('.invert-settings', !isInvertAware());
    toggle('.diagnostics-settings', !isDiagnostics());
  }

  function toggle(selector, hidden) {
    Array.prototype.forEach.call(document.querySelectorAll(selector), function (element) {
      element.classList.toggle('is-hidden', hidden);
    });
  }

  function update() {
    if (!websocket || websocket.readyState !== WebSocket.OPEN || !actionContext) return;
    settings.channelKind = normalizeChannelKind(byId('channelKind').value);
    settings.channelIndex = normalizeIndex(byId('channelIndex').value, settings.channelIndex);
    settings.step = normalizeStep(byId('step').value, settings.step, 0.1, 24);
    settings.deviceId = byId('deviceId').value.trim();
    settings.macroButtonIndex = Math.max(0, Math.min(79, Math.round(Number(byId('macroButtonIndex').value) || 0)));
    settings.appCommand = byId('appCommand').value;
    settings.overviewTargets = selectedOverviewTargets();
    settings.balancePrimaryKind = normalizeChannelKind(byId('balancePrimaryKind').value);
    settings.balancePrimaryIndex = normalizeIndex(byId('balancePrimaryIndex').value, settings.balancePrimaryIndex);
    settings.balanceSecondaryKind = normalizeChannelKind(byId('balanceSecondaryKind').value);
    settings.balanceSecondaryIndex = normalizeIndex(byId('balanceSecondaryIndex').value, settings.balanceSecondaryIndex);
    settings.balanceStep = normalizeStep(byId('balanceStep').value, settings.balanceStep, 0.1, 12);
    settings.titleLabel = byId('titleLabel').value.trim();
    settings.invertKnob = byId('invertKnob').checked;
    byId('channelIndexLabel').textContent = channelIndexLabel();
    websocket.send(JSON.stringify({ event: 'setSettings', context: actionContext, payload: settings }));
    if (isDeviceSelectAction() || isDeviceInfoAction()) requestDevices();
  }

  function updateFromDeviceSelect() {
    var selected = byId('deviceSelect').value;
    byId('deviceId').value = selected;
    update();
  }

  function selectedOverviewTargets() {
    return normalizeOverviewTargets(Array.prototype.filter.call(
      document.querySelectorAll('input[name="overviewTarget"]'),
      function (input) { return input.checked; }
    ).map(function (input) { return input.value; }));
  }

  function requestDiagnostics() {
    if (!websocket || websocket.readyState !== WebSocket.OPEN || !propertyInspectorContext) return;
    if (!isDiagnostics()) return;
    websocket.send(JSON.stringify({
      event: 'sendToPlugin',
      action: currentAction,
      context: propertyInspectorContext,
      payload: { command: 'diagnostics', replyContext: actionContext }
    }));
    byId('status').textContent = 'checking';
  }

  function requestDevices() {
    if (!websocket || websocket.readyState !== WebSocket.OPEN || !propertyInspectorContext) return;
    if (!isDeviceInfoAction()) return;
    websocket.send(JSON.stringify({
      event: 'sendToPlugin',
      action: currentAction,
      context: propertyInspectorContext,
      payload: { command: 'devices', dataFlow: isCaptureDeviceAction() ? 'capture' : 'render', replyContext: actionContext }
    }));
    byId('deviceStatus').textContent = 'loading';
    if (deviceRequestTimer) clearTimeout(deviceRequestTimer);
    deviceRequestTimer = setTimeout(function () {
      if (byId('deviceStatus').textContent === 'loading') byId('deviceStatus').textContent = 'no response';
    }, 10000);
  }

  function requestMacroStatus() {
    if (!websocket || websocket.readyState !== WebSocket.OPEN || !propertyInspectorContext) return;
    if (!isMacroButton()) return;
    websocket.send(JSON.stringify({
      event: 'sendToPlugin',
      action: currentAction,
      context: propertyInspectorContext,
      payload: { command: 'macroStatus', macroButtonIndex: settings.macroButtonIndex, replyContext: actionContext }
    }));
    byId('macroStatus').textContent = 'checking';
  }

  function handlePluginMessage(payload) {
    if (payload.type === 'diagnostics') {
      byId('status').textContent = payload.diagnostics && payload.diagnostics.loggedIn ? 'ok' : 'not connected';
      byId('diagnosticsOutput').textContent = JSON.stringify(payload.diagnostics, null, 2);
    } else if (payload.type === 'devices') {
      if (deviceRequestTimer) clearTimeout(deviceRequestTimer);
      devices = Array.isArray(payload.devices) ? payload.devices : [];
      byId('deviceStatus').textContent = devices.length ? devices.length + ' devices' : 'none';
      renderDeviceOptions();
    } else if (payload.type === 'macroStatus') {
      byId('macroStatus').textContent = payload.on ? 'on' : 'off';
    } else if (payload.type === 'error') {
      byId('status').textContent = payload.message || 'error';
      if (isDeviceInfoAction() || payload.source === 'devices') {
        if (deviceRequestTimer) clearTimeout(deviceRequestTimer);
        byId('deviceStatus').textContent = payload.message || 'error';
      }
      if (isMacroButton() || payload.source === 'macroStatus') {
        byId('macroStatus').textContent = payload.message || 'error';
      }
    }
  }

  function renderDeviceOptions() {
    var select = byId('deviceSelect');
    if (!select) return;
    var current = byId('deviceId').value || settings.deviceId;
    select.innerHTML = '';
    var custom = document.createElement('option');
    custom.value = '';
    custom.textContent = devices.length ? 'Manual deviceId' : 'No devices loaded';
    select.appendChild(custom);
    devices.forEach(function (device) {
      var option = document.createElement('option');
      option.value = device.id || '';
      option.textContent = device.name || device.id || 'Unknown device';
      select.appendChild(option);
    });
    select.value = devices.some(function (device) { return device.id === current; }) ? current : '';
  }

  document.addEventListener('DOMContentLoaded', function () {
    buildOverviewGrid();
    ['channelKind', 'channelIndex', 'step', 'deviceId', 'macroButtonIndex', 'appCommand',
      'balancePrimaryKind', 'balancePrimaryIndex', 'balanceSecondaryKind', 'balanceSecondaryIndex',
      'balanceStep', 'titleLabel', 'invertKnob'].forEach(function (id) {
      byId(id).addEventListener('change', update);
      byId(id).addEventListener('input', update);
    });
    byId('deviceSelect').addEventListener('change', updateFromDeviceSelect);
    byId('refreshDiagnostics').addEventListener('click', requestDiagnostics);
    byId('refreshDevices').addEventListener('click', requestDevices);
    byId('refreshMacroStatus').addEventListener('click', requestMacroStatus);
    render();
  });

  window.connectElgatoStreamDeckSocket = connectElgatoStreamDeckSocket;
}());
